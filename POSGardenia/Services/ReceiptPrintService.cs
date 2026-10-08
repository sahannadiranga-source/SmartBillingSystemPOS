using POSGardenia.Controls;
using POSGardenia.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace POSGardenia.Services
{
    public class ReceiptPrintService
    {
        public void PrintReceipt(ReceiptData receipt, string printerName)
        {
            try
            {
                if (receipt == null)
                    throw new Exception("Receipt data is null.");

                if (string.IsNullOrWhiteSpace(printerName))
                {
                    PrintWithDialog(receipt);
                    return;
                }

                var printServer = new LocalPrintServer();
                var printQueue = printServer.GetPrintQueue(printerName);

                if (printQueue == null)
                    throw new Exception("Selected printer was not found.");

                var document = BuildReceiptDocument(receipt);
                document.PageWidth = 280;
                document.ColumnWidth = 280;

                var writer = PrintQueue.CreateXpsDocumentWriter(printQueue);
                writer.Write(((IDocumentPaginatorSource)document).DocumentPaginator);
            }
            catch (Exception ex)
            {
                AppMessage.Show("Failed to print receipt.\n" + ex.Message);
            }
        }

        public void PrintWithDialog(ReceiptData receipt)
        {
            try
            {
                if (receipt == null)
                    throw new Exception("Receipt data is null.");

                var printDialog = new PrintDialog();

                if (printDialog.ShowDialog() != true)
                    return;

                var document = BuildReceiptDocument(receipt);
                document.PageWidth = printDialog.PrintableAreaWidth;
                document.PageHeight = printDialog.PrintableAreaHeight;
                document.ColumnWidth = printDialog.PrintableAreaWidth;

                IDocumentPaginatorSource paginatorSource = document;
                printDialog.PrintDocument(paginatorSource.DocumentPaginator, $"Receipt {receipt.BillNo}");
            }
            catch (Exception ex)
            {
                AppMessage.Show("Failed to print receipt.\n" + ex.Message);
            }
        }

        public void PrintTest(string printerName)
        {
            try
            {
                var testReceipt = new ReceiptData
                {
                    BusinessName = "Gardenia Restaurant",
                    BillNo = "TEST-001",
                    TableName = "Test",
                    BillType = "TEST PRINT",
                    PrintedAt = DateTime.Now,
                    Items =
                    {
                        new ReceiptLine
                        {
                            ProductName = "Test Item",
                            Quantity = 1,
                            UnitPrice = 100
                        }
                    },
                    Total = 100
                };

                PrintReceipt(testReceipt, printerName);
            }
            catch (Exception ex)
            {
                AppMessage.Show("Failed to print test receipt.\n" + ex.Message);
            }
        }

        private FlowDocument BuildReceiptDocument(ReceiptData receipt)
        {
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                PagePadding = new Thickness(10),
                TextAlignment = TextAlignment.Left
            };

            document.Blocks.Add(CreateParagraph(receipt.BusinessName, 18, FontWeights.Bold, TextAlignment.Center));
            document.Blocks.Add(CreateParagraph("CUSTOMER RECEIPT", 14, FontWeights.Bold, TextAlignment.Center));
            document.Blocks.Add(CreateParagraph("--------------------------------", 12, FontWeights.Normal, TextAlignment.Center));

            document.Blocks.Add(CreateParagraph($"Bill No : {receipt.BillNo}"));
            document.Blocks.Add(CreateParagraph($"Type    : {receipt.BillType}"));
            document.Blocks.Add(CreateParagraph($"Table   : {receipt.TableName}"));
            document.Blocks.Add(CreateParagraph($"Printed : {receipt.PrintedAt:yyyy-MM-dd HH:mm:ss}"));
            document.Blocks.Add(CreateParagraph("--------------------------------"));

            document.Blocks.Add(CreateParagraph("Item".PadRight(LineWidth - 6) + "Amount", 12, FontWeights.Bold));
            document.Blocks.Add(CreateParagraph("--------------------------------"));

            foreach (var item in receipt.Items ?? Enumerable.Empty<ReceiptLine>())
            {
                foreach (var line in FormatItemLines(item))
                    document.Blocks.Add(CreateParagraph(line));
            }

            document.Blocks.Add(CreateParagraph("--------------------------------"));
            document.Blocks.Add(CreateParagraph($"TOTAL: {receipt.Total:0.00}", 16, FontWeights.Bold, TextAlignment.Right));
            document.Blocks.Add(CreateParagraph($"CASH: {receipt.CashAmount:0.00}", 13, FontWeights.Normal, TextAlignment.Right));
            document.Blocks.Add(CreateParagraph($"CARD: {receipt.CardAmount:0.00}", 13, FontWeights.Normal, TextAlignment.Right));

            if (receipt.ChangeAmount > 0)
            {
                document.Blocks.Add(CreateParagraph($"RECEIVED: {receipt.ReceivedAmount:0.00}", 13, FontWeights.Normal, TextAlignment.Right));
                document.Blocks.Add(CreateParagraph($"CHANGE: {receipt.ChangeAmount:0.00}", 16, FontWeights.Bold, TextAlignment.Right));
            }

            if (receipt.DueAmount > 0)
            {
                document.Blocks.Add(CreateParagraph($"DUE: {receipt.DueAmount:0.00}", 16, FontWeights.Bold, TextAlignment.Right));
                document.Blocks.Add(CreateParagraph("*** PARTIALLY PAID ***", 13, FontWeights.Bold, TextAlignment.Center));
            }

            document.Blocks.Add(CreateParagraph("--------------------------------", 12, FontWeights.Normal, TextAlignment.Center));
            document.Blocks.Add(CreateParagraph("Thank you!", 14, FontWeights.Bold, TextAlignment.Center));

            return document;
        }

        // Characters across one receipt line (the dashes are this long too).
        public const int LineWidth = 32;

        // One item as receipt lines: the full name (wrapped, never cut off) and the amount at the right.
        // More than one of it adds "2 x 140.00" so the price is shown once, only where it says something new.
        //   Cheese one peace
        //     2 x 140.00                 280.00
        //   Kadala                       100.00
        public static List<string> FormatItemLines(ReceiptLine item, int width = LineWidth)
        {
            string amount = item.LineTotal.ToString("0.00");
            var lines = WrapText((item.ProductName ?? "").Trim(), width);
            if (lines.Count == 0)
                lines.Add("");

            string tail = amount;
            string detail = "";

            if (item.Quantity != 1)
            {
                // the breakdown sits on its own line, under the name
                detail = $"  {item.Quantity:0.##} x {item.UnitPrice:0.00}";
                lines.Add(detail);
            }

            // the amount goes at the right of the last line when it fits, otherwise on a line of its own
            string last = lines[^1];
            if (last.Length + 1 + tail.Length <= width)
                lines[^1] = last.PadRight(width - tail.Length) + tail;
            else
                lines.Add(tail.PadLeft(width));

            return lines;
        }

        // Breaks text into lines of at most `width` characters at spaces; a single word longer than a line is split.
        private static List<string> WrapText(string text, int width)
        {
            var lines = new List<string>();
            var current = "";

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string piece = word;

                while (piece.Length > width)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current);
                        current = "";
                    }

                    lines.Add(piece.Substring(0, width));
                    piece = piece.Substring(width);
                }

                if (current.Length == 0)
                    current = piece;
                else if (current.Length + 1 + piece.Length <= width)
                    current += " " + piece;
                else
                {
                    lines.Add(current);
                    current = piece;
                }
            }

            if (current.Length > 0)
                lines.Add(current);

            return lines;
        }

        private Paragraph CreateParagraph(
            string text,
            double fontSize = 12,
            FontWeight? fontWeight = null,
            TextAlignment alignment = TextAlignment.Left)
        {
            return new Paragraph(new Run(text ?? ""))
            {
                FontSize = fontSize,
                FontWeight = fontWeight ?? FontWeights.Normal,
                TextAlignment = alignment,
                Margin = new Thickness(0, 2, 0, 2)
            };
        }
    }
}