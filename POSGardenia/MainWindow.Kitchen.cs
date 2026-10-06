using POSGardenia.Models;
using POSGardenia.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace POSGardenia
{
    // Kitchen order tickets (KOT). A ticket is built from what is actually unsent in the database
    // (active kitchen items with IsKitchenPrinted = 0) and the items are marked sent only after the
    // ticket printed. If printing fails nothing is marked, so the items go out on the next ticket
    // for that bill or via Tables > Send to Kitchen.
    public partial class MainWindow
    {
        private readonly KitchenTicketService _kitchenTicketService = new();

        private const string SameAsReceiptPrinter = "(Same as receipt printer)";

        // Sends the unsent kitchen items of a bill. True = nothing to send, or it printed.
        private bool SendKitchenTicket(int billId, string tableName)
        {
            try
            {
                var pending = _billItemRepository.GetPendingKitchenItemsByBillId(billId);
                if (pending.Count == 0)
                    return true;

                bool addOn = _billItemRepository.HasSentKitchenItems(billId);
                var ticket = BuildKitchenTicket(billId, tableName, pending, addOn ? KitchenTicketKind.AddOn : KitchenTicketKind.New);
                ticket.KotNo = _billItemRepository.GetNextKotNumber();

                var (sent, error) = _kitchenTicketService.Print(ticket, _appSettings.KitchenPrinterName, _appSettings.ReceiptPrinterName);
                if (!sent)
                {
                    WarnKitchenNotSent(ticket, error);
                    return false;
                }

                try
                {
                    _billItemRepository.MarkKitchenItemsSent(pending.Select(x => x.Id).ToList(), ticket.KotNo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("The kitchen ticket printed, but it could not be recorded as sent, so it may print again with the next order on this bill.\n" + ex.Message,
                        "Kitchen ticket", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("KITCHEN TICKET NOT SENT.\nTell the kitchen about the new items yourself, then use Tables > Send to Kitchen.\n\n" + ex.Message,
                    "Kitchen ticket", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private KitchenTicket BuildKitchenTicket(int billId, string tableName, List<BillItemDisplay> items, KitchenTicketKind kind)
        {
            return new KitchenTicket
            {
                BillNo = GetVisibleBillNumber(billId),
                TableName = string.IsNullOrWhiteSpace(tableName) ? "Quick Sale" : tableName,
                PrintedAt = DateTime.Now,
                Kind = kind,
                // the same dish added twice shows once, with the total quantity
                Lines = items
                    .GroupBy(x => x.ProductName)
                    .Select(g => new KitchenTicketLine { Name = g.Key, Quantity = g.Sum(x => x.Quantity) })
                    .ToList()
            };
        }

        private static void WarnKitchenNotSent(KitchenTicket ticket, string error)
        {
            string items = string.Join("\n", ticket.Lines.Select(l => $"   {l.Quantity:0.##} x {l.Name}"));

            MessageBox.Show(
                $"KITCHEN TICKET NOT SENT ({error})\n\nTell the kitchen:\n{items}\n\nThe items stay pending. Fix the printer, then use Tables > Send to Kitchen.",
                "Kitchen ticket",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // Tables tab: send what is still unsent for the selected bill; if everything was already sent,
        // offer to print the whole kitchen list again (lost ticket, paper jam).
        private void SendToKitchenFromTable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTablesBill == null)
                {
                    MessageBox.Show("Select an open bill first.");
                    return;
                }

                int billId = _selectedTablesBill.Id;
                string tableName = string.IsNullOrWhiteSpace(_selectedTablesBill.TableName) ? "Quick Sale" : _selectedTablesBill.TableName;

                if (_billItemRepository.GetPendingKitchenItemsByBillId(billId).Count > 0)
                {
                    if (SendKitchenTicket(billId, tableName))
                        MessageBox.Show("Sent to the kitchen.");
                    return;
                }

                var all = _billItemRepository.GetPendingKitchenItemsByBillId(billId, pendingOnly: false);
                if (all.Count == 0)
                {
                    MessageBox.Show("This bill has no kitchen items.");
                    return;
                }

                var again = MessageBox.Show(
                    "All kitchen items were already sent.\nPrint the full kitchen list again?",
                    "Send to Kitchen",
                    MessageBoxButton.YesNo);

                if (again != MessageBoxResult.Yes)
                    return;

                var ticket = BuildKitchenTicket(billId, tableName, all, KitchenTicketKind.Reprint);
                ticket.OriginalKots = string.Join(", ", _billItemRepository.GetKotNumbersForBill(billId).Select(n => n.ToString("D4")));
                var (sent, error) = _kitchenTicketService.Print(ticket, _appSettings.KitchenPrinterName, _appSettings.ReceiptPrinterName);
                if (!sent)
                    WarnKitchenNotSent(ticket, error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to send to the kitchen.\n" + ex.Message);
            }
        }

        private void SaveKitchenPrinter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string selected = KitchenPrinterComboBox.SelectedItem as string ?? SameAsReceiptPrinter;

                _appSettings.KitchenPrinterName = selected == SameAsReceiptPrinter ? "" : selected;
                _settingsService.Save(_appSettings);

                MessageBox.Show("Kitchen printer saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save the kitchen printer.\n" + ex.Message);
            }
        }

        private void TestKitchenPrinter_Click(object sender, RoutedEventArgs e)
        {
            string selected = KitchenPrinterComboBox.SelectedItem as string ?? SameAsReceiptPrinter;
            string kitchenPrinter = selected == SameAsReceiptPrinter ? "" : selected;

            var ticket = new KitchenTicket
            {
                BillNo = "TEST",
                TableName = "Test",
                PrintedAt = DateTime.Now,
                Kind = KitchenTicketKind.New,
                Lines = { new KitchenTicketLine { Name = "Test item", Quantity = 1 } }
            };

            var (sent, error) = _kitchenTicketService.Print(ticket, kitchenPrinter, _appSettings.ReceiptPrinterName);
            MessageBox.Show(sent ? "Test ticket sent." : "Test ticket failed.\n" + error);
        }
    }
}
