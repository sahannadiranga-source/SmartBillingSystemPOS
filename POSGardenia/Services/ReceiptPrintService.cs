using POSGardenia.Models;
using System;
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
                MessageBox.Show("Failed to print receipt.\n" + ex.Message);
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
                MessageBox.Show("Failed to print receipt.\n" + ex.Message);
            }
        }

        public void PrintTest(string printerName)
        {
            try
            {
                var testReceipt = new ReceiptData
                {
                    BusinessName = "Smart Billing System POS",
                    BillNo = "TEST-001",
                    TableName = "Test",
                    BillType = "TEST PRINT",
                    PaymentMethod = "N/A",
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
                MessageBox.Show("Failed to print test receipt.\n" + ex.Message);
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
            document.Blocks.Add(CreateParagraph($"Payment : {receipt.PaymentMethod}"));
            document.Blocks.Add(CreateParagraph($"Printed : {receipt.PrintedAt:yyyy-MM-dd HH:mm:ss}"));
            document.Blocks.Add(CreateParagraph("--------------------------------"));

            document.Blocks.Add(CreateParagraph("Item                 Qty   Price   Total", 12, FontWeights.Bold));
            document.Blocks.Add(CreateParagraph("--------------------------------"));

            foreach (var item in receipt.Items ?? Enumerable.Empty<ReceiptLine>())
            {
                var name = item.ProductName ?? "";
                if (name.Length > 18)
                    name = name.Substring(0, 18);

                string line =
                    $"{name.PadRight(18)} " +
                    $"{item.Quantity,4:0.##} " +
                    $"{item.UnitPrice,7:0.00} " +
                    $"{item.LineTotal,7:0.00}";

                document.Blocks.Add(CreateParagraph(line));
            }

            document.Blocks.Add(CreateParagraph("--------------------------------"));
            document.Blocks.Add(CreateParagraph($"TOTAL: {receipt.Total:0.00}", 16, FontWeights.Bold, TextAlignment.Right));
            document.Blocks.Add(CreateParagraph("--------------------------------", 12, FontWeights.Normal, TextAlignment.Center));
            document.Blocks.Add(CreateParagraph("Thank you!", 14, FontWeights.Bold, TextAlignment.Center));

            return document;
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