using POSGardenia.Data;
using POSGardenia.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace POSGardenia.Services
{
    public class DailyReportService
    {
        private const string Ink = "111827";
        private const string Muted = "6B7280";
        private const string Accent = "C2410C";
        private const string HeaderFill = "F3F4F6";
        private const string CardFill = "FFF7ED";

        private const double Left = 40;
        private const double Right = SimplePdf.PageWidth - 40;
        private const double Bottom = SimplePdf.PageHeight - 50;   // room for the footer
        private const double RowHeight = 20;

        private readonly PaymentRepository _paymentRepository = new();
        private readonly ExpenseRepository _expenseRepository = new();
        private readonly BillItemRepository _billItemRepository = new();

        private static string FileNameFor(DateTime reportDate)
        {
            return $"Daily_Report_{reportDate:yyyy-MM-dd}.pdf";
        }

        // Builds the report for one day and saves it as a PDF in reportFolder. Returns the file path.
        public string GenerateDailyReport(string reportFolder, DateTime reportDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportFolder))
                    throw new Exception("Daily report folder is not selected.");

                if (!Directory.Exists(reportFolder))
                    Directory.CreateDirectory(reportFolder);

                string filePath = Path.Combine(reportFolder, FileNameFor(reportDate));
                SaveReport(filePath, reportDate);
                return filePath;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to generate daily report. " + ex.Message, ex);
            }
        }

        // Same report, saved to an exact file path (used by "Save as PDF" on the Reports page).
        public void SaveReport(string filePath, DateTime reportDate)
        {
            BuildPdf(reportDate).Save(filePath);
        }

        public static string DefaultFileName(DateTime reportDate) => FileNameFor(reportDate);

        public bool ReportExists(string reportFolder, DateTime reportDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportFolder))
                    return false;

                return File.Exists(Path.Combine(reportFolder, FileNameFor(reportDate)));
            }
            catch
            {
                return false;
            }
        }

        private SimplePdf BuildPdf(DateTime reportDate)
        {
            string dateText = reportDate.ToString("yyyy-MM-dd");

            decimal sales = _paymentRepository.GetSalesTotalBySingleDate(dateText);
            int paidBills = _paymentRepository.GetPaidBillCountBySingleDate(dateText);
            decimal expenses = _expenseRepository.GetDeductibleTotalByDate(dateText);             // normal expenses
            decimal stockPurchases = _expenseRepository.GetStockPurchaseTotalByDate(dateText);   // money spent buying stock
            decimal net = sales - expenses;                                                       // stock purchases are not taken off

            var paymentBreakdown = _paymentRepository.GetPaymentBreakdownBySingleDate(dateText) ?? new();
            var soldItems = _billItemRepository.GetItemSalesReportBySingleDate(dateText) ?? new();
            var allExpenses = _expenseRepository.GetByDate(dateText) ?? new();
            var expenseList = allExpenses.Where(e => !e.IsStockPurchase).ToList();
            var purchaseList = allExpenses.Where(e => e.IsStockPurchase).ToList();

            var pdf = new SimplePdf();
            double y = 40;

            // ---- title
            pdf.Text(Left, y, "Gardenia Restaurant", 22, bold: true, color: Accent);
            pdf.Text(Left, y + 28, "Daily Sales Report", 13, color: Muted);
            pdf.Text(Right, y + 2, reportDate.ToString("dddd", CultureInfo.InvariantCulture), 11, align: PdfAlign.Right, color: Muted);
            pdf.Text(Right, y + 16, reportDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), 16, bold: true, align: PdfAlign.Right, color: Ink);
            y += 56;
            pdf.Line(Left, y, Right, 1.2, Accent);
            y += 16;

            // ---- the totals: expenses and stock purchases side by side, apart
            string[] labels = { "Total Sales", "Paid Bills", "Expenses", "Stock Purchases", "Net Sale" };
            string[] values = { Money(sales), paidBills.ToString(CultureInfo.InvariantCulture), Money(expenses), Money(stockPurchases), Money(net) };
            double gap = 8;
            double cardWidth = (Right - Left - gap * 4) / 5;
            for (int i = 0; i < 5; i++)
            {
                double x = Left + i * (cardWidth + gap);
                pdf.Box(x, y, cardWidth, 50, CardFill);
                pdf.Text(x + 8, y + 9, labels[i], 9, color: Muted);

                // big numbers shrink a little so they always fit inside the card
                double size = 15;
                while (size > 8 && SimplePdf.Measure(values[i], size, bold: true) > cardWidth - 16)
                    size -= 0.5;

                pdf.Text(x + 8, y + 26 + (15 - size) / 2, values[i], size, bold: true, color: i == 4 && net < 0 ? "B91C1C" : Ink);
            }
            y += 50 + 22;

            // ---- payment breakdown
            var payColumns = new[]
            {
                new Col("Payment method", 300, PdfAlign.Left),
                new Col("Amount", 215, PdfAlign.Right)
            };
            var payRows = paymentBreakdown
                .Select(p => new[] { string.IsNullOrWhiteSpace(p.PaymentMethod) ? "UNKNOWN" : p.PaymentMethod, Money(p.TotalAmount) })
                .ToList();
            y = Table(pdf, y, "Payment Breakdown", payColumns, payRows, "No payments.",
                paymentBreakdown.Count > 0 ? new[] { "Total", Money(paymentBreakdown.Sum(p => p.TotalAmount)) } : null);

            // ---- sold items, by stock item
            var itemColumns = new[]
            {
                new Col("Item", 215, PdfAlign.Left),
                new Col("Sold", 85, PdfAlign.Right),
                new Col("In bottles / packs", 110, PdfAlign.Left),
                new Col("Total", 105, PdfAlign.Right)
            };
            var itemRows = soldItems
                .Select(i =>
                {
                    string sold = $"{i.QuantitySold.ToString("#,0.##", CultureInfo.InvariantCulture)} {i.Unit}".TrimEnd();

                    // the bottle column only helps when it says something new ("2 bottles + 150 ml")
                    string packs = i.InPacks ?? "";
                    if (SameWords(packs, sold))
                        packs = "";

                    return new[] { i.Item ?? "", sold, packs, Money(i.TotalSales) };
                })
                .ToList();
            y = Table(pdf, y, "Items Sold  (by stock item - liquor bottles and shots are added together in ml)", itemColumns, itemRows, "No items sold.",
                soldItems.Count > 0 ? new[] { "Total", "", "", Money(soldItems.Sum(i => i.TotalSales)) } : null);

            // ---- expenses
            var expenseColumns = new[]
            {
                new Col("Description", 400, PdfAlign.Left),
                new Col("Amount", 115, PdfAlign.Right)
            };
            var expenseRows = expenseList
                .Select(e => new[] { e.Description ?? "", Money(e.Amount) })
                .ToList();
            y = Table(pdf, y, "Expenses", expenseColumns, expenseRows, "No expenses.",
                expenseList.Count > 0 ? new[] { "Total", Money(expenseList.Sum(e => e.Amount)) } : null);

            // ---- stock purchases: their own table (only on days when stock was bought)
            if (purchaseList.Count > 0)
            {
                var purchaseRows = purchaseList
                    .Select(e => new[] { StripPurchasePrefix(e.Description ?? ""), Money(e.Amount) })
                    .ToList();
                y = Table(pdf, y, "Stock Purchases", expenseColumns, purchaseRows, "No stock purchases.",
                    new[] { "Total", Money(purchaseList.Sum(e => e.Amount)) });

                if (y + 8 > Bottom)
                    y = NextPage(pdf);

                pdf.Text(Left, y - 12, "Stock purchases are not deducted from Net Sale.", 9, color: Muted);
                y += 8;
            }

            // ---- footer on every page
            string generated = $"Generated {DateTime.Now:yyyy-MM-dd HH:mm}";
            for (int p = 0; p < pdf.PageCount; p++)
            {
                pdf.GoToPage(p);
                pdf.Line(Left, SimplePdf.PageHeight - 38, Right, 0.5, "D1D5DB");
                pdf.Text(Left, SimplePdf.PageHeight - 32, $"Gardenia Restaurant  -  Daily Sales Report  -  {dateText}", 8, color: Muted);
                pdf.Text(Right, SimplePdf.PageHeight - 32, $"{generated}    Page {p + 1} of {pdf.PageCount}", 8, align: PdfAlign.Right, color: Muted);
            }

            return pdf;
        }

        private record Col(string Header, double Width, PdfAlign Align);

        // Draws a titled table, moving to a new page when it runs out of room (the header row repeats).
        // Returns the y position below it.
        private static double Table(SimplePdf pdf, double y, string title, Col[] columns, List<string[]> rows, string emptyText, string[]? totalRow)
        {
            // keep the title with at least its header and first two rows
            if (y + 30 + RowHeight * 3 > Bottom)
                y = NextPage(pdf);

            pdf.Text(Left, y, title, 12, bold: true, color: Ink);
            y += 22;
            y = HeaderRow(pdf, y, columns);

            if (rows.Count == 0)
            {
                pdf.Text(Left + 8, y + 4, emptyText, 10, color: Muted);
                return y + RowHeight + 18;
            }

            foreach (var row in rows)
            {
                if (y + RowHeight > Bottom)
                {
                    y = NextPage(pdf);
                    pdf.Text(Left, y, title + " (continued)", 10, bold: true, color: Muted);
                    y += 18;
                    y = HeaderRow(pdf, y, columns);
                }

                DrawRow(pdf, y, columns, row, bold: false);
                pdf.Line(Left, y + RowHeight, Right, 0.4, "E5E7EB");
                y += RowHeight;
            }

            if (totalRow != null)
            {
                if (y + RowHeight > Bottom)
                {
                    y = NextPage(pdf);
                    y = HeaderRow(pdf, y, columns);
                }

                pdf.Line(Left, y, Right, 1, Ink);
                DrawRow(pdf, y, columns, totalRow, bold: true);
                y += RowHeight;
            }

            return y + 18;
        }

        private static double HeaderRow(SimplePdf pdf, double y, Col[] columns)
        {
            pdf.Box(Left, y, Right - Left, RowHeight, HeaderFill);
            double x = Left;
            foreach (var col in columns)
            {
                double textX = col.Align == PdfAlign.Right ? x + col.Width - 8 : x + 8;
                pdf.Text(textX, y + 5, col.Header, 9, bold: true, align: col.Align, color: Muted);
                x += col.Width;
            }

            return y + RowHeight;
        }

        private static void DrawRow(SimplePdf pdf, double y, Col[] columns, string[] cells, bool bold)
        {
            double x = Left;
            for (int c = 0; c < columns.Length; c++)
            {
                var col = columns[c];
                string text = SimplePdf.Fit(cells[c], 10, bold, col.Width - 16);
                double textX = col.Align == PdfAlign.Right ? x + col.Width - 8 : x + 8;
                pdf.Text(textX, y + 5, text, 10, bold, col.Align, Ink);
                x += col.Width;
            }
        }

        // The table is already titled "Stock Purchases", so the saved "Stock purchase: " wording is dropped.
        private static string StripPurchasePrefix(string description)
        {
            const string prefix = "Stock purchase:";
            return description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? description.Substring(prefix.Length).Trim()
                : description;
        }

        // "2 bottle" and "2 bottles" say the same thing
        private static bool SameWords(string a, string b)
        {
            static string Normal(string s) => s.Replace(",", "").Replace("s", "").Trim();
            return Normal(a) == Normal(b);
        }

        private static double NextPage(SimplePdf pdf)
        {
            pdf.NewPage();
            return 40;
        }

        private static string Money(decimal value)
        {
            return value.ToString("#,0.00", CultureInfo.InvariantCulture);
        }
    }
}
