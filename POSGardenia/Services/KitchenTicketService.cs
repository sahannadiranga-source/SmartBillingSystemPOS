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
    // Builds and prints kitchen order tickets. Unlike the receipt printer it never shows a message box:
    // it tells the caller whether the ticket really went out, so the bill's items are only marked as
    // sent when printing worked.
    public class KitchenTicketService
    {
        // Printer order: kitchen printer, else the receipt printer, else ask (print dialog).
        public (bool Sent, string Error) Print(KitchenTicket ticket, string kitchenPrinterName, string receiptPrinterName)
        {
            try
            {
                if (ticket == null || ticket.Lines.Count == 0)
                    return (false, "Nothing to print.");

                string printerName = !string.IsNullOrWhiteSpace(kitchenPrinterName)
                    ? kitchenPrinterName
                    : receiptPrinterName;

                var document = BuildDocument(ticket);

                if (string.IsNullOrWhiteSpace(printerName))
                {
                    var dialog = new PrintDialog();
                    if (dialog.ShowDialog() != true)
                        return (false, "Printing was cancelled.");

                    document.PageWidth = dialog.PrintableAreaWidth;
                    document.PageHeight = dialog.PrintableAreaHeight;
                    document.ColumnWidth = dialog.PrintableAreaWidth;
                    dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"KOT {ticket.BillNo}");
                    return (true, "");
                }

                var queue = new LocalPrintServer().GetPrintQueue(printerName);
                if (queue == null)
                    return (false, $"Printer '{printerName}' was not found.");

                document.PageWidth = 280;
                document.ColumnWidth = 280;

                var writer = PrintQueue.CreateXpsDocumentWriter(queue);
                writer.Write(((IDocumentPaginatorSource)document).DocumentPaginator);
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public FlowDocument BuildDocument(KitchenTicket ticket)
        {
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                PagePadding = new Thickness(10),
                TextAlignment = TextAlignment.Left
            };

            document.Blocks.Add(Paragraph("KITCHEN ORDER", 20, FontWeights.Bold, TextAlignment.Center));

            if (ticket.KotNo > 0)
                document.Blocks.Add(Paragraph($"KOT No: {ticket.KotNo:D4}", 26, FontWeights.Bold, TextAlignment.Center));

            string tag = ticket.Kind switch
            {
                KitchenTicketKind.AddOn => "*** ADD-ON ***",
                KitchenTicketKind.Reprint => "*** REPRINT ***",
                _ => ""
            };
            if (tag.Length > 0)
                document.Blocks.Add(Paragraph(tag, 16, FontWeights.Bold, TextAlignment.Center));

            if (ticket.Kind == KitchenTicketKind.Reprint && ticket.OriginalKots.Length > 0)
                document.Blocks.Add(Paragraph($"Original KOT: {ticket.OriginalKots}", 14, FontWeights.Normal, TextAlignment.Center));

            document.Blocks.Add(Paragraph("--------------------------------", 12, FontWeights.Normal, TextAlignment.Center));
            document.Blocks.Add(Paragraph($"Table : {ticket.TableName}", 18, FontWeights.Bold));
            document.Blocks.Add(Paragraph($"Bill  : {ticket.BillNo}", 14, FontWeights.Normal));
            document.Blocks.Add(Paragraph($"Time  : {ticket.PrintedAt:HH:mm}   {ticket.PrintedAt:yyyy-MM-dd}", 14, FontWeights.Normal));
            document.Blocks.Add(Paragraph("--------------------------------", 12, FontWeights.Normal, TextAlignment.Center));

            foreach (var line in ticket.Lines)
                document.Blocks.Add(Paragraph($"{line.Quantity:0.##} x {line.Name}", 18, FontWeights.Bold));

            document.Blocks.Add(Paragraph("--------------------------------", 12, FontWeights.Normal, TextAlignment.Center));

            return document;
        }

        private static Paragraph Paragraph(string text, double size, FontWeight weight, TextAlignment alignment = TextAlignment.Left)
        {
            return new Paragraph(new Run(text ?? ""))
            {
                FontSize = size,
                FontWeight = weight,
                TextAlignment = alignment,
                Margin = new Thickness(0, 3, 0, 3)
            };
        }
    }
}
