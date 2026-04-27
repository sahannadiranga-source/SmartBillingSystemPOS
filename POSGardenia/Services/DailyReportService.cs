using POSGardenia.Data;
using POSGardenia.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace POSGardenia.Services
{
    public class DailyReportService
    {
        private readonly PaymentRepository _paymentRepository = new();
        private readonly ExpenseRepository _expenseRepository = new();
        private readonly BillItemRepository _billItemRepository = new();

        public string GenerateDailyReport(string reportFolder, DateTime reportDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportFolder))
                    throw new Exception("Daily report folder is not selected.");

                if (!Directory.Exists(reportFolder))
                    Directory.CreateDirectory(reportFolder);

                string dateText = reportDate.ToString("yyyy-MM-dd");
                string fileName = $"Daily_Report_{dateText}.txt";
                string filePath = Path.Combine(reportFolder, fileName);

                decimal sales = _paymentRepository.GetSalesTotalBySingleDate(dateText);
                int paidBills = _paymentRepository.GetPaidBillCountBySingleDate(dateText);
                decimal expenses = _expenseRepository.GetTotalByDate(dateText);
                decimal net = sales - expenses;

                var soldItems = _billItemRepository.GetItemSalesReportBySingleDate(dateText);
                var expenseList = _expenseRepository.GetByDate(dateText);

                var sb = new StringBuilder();

                sb.AppendLine("SMART BILLING SYSTEM POS");
                sb.AppendLine("DAILY SALES REPORT");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine($"Date           : {dateText}");
                sb.AppendLine($"Generated At   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine($"Total Sales    : {sales:0.00}");
                sb.AppendLine($"Paid Bills     : {paidBills}");
                sb.AppendLine($"Total Expenses : {expenses:0.00}");
                sb.AppendLine($"Net Sale       : {net:0.00}");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine();
                sb.AppendLine("SOLD ITEMS");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine("Item                          Qty     Total");
                sb.AppendLine("----------------------------------------");

                if (soldItems == null || soldItems.Count == 0)
                {
                    sb.AppendLine("No items sold.");
                }
                else
                {
                    foreach (var item in soldItems)
                    {
                        string name = item.ProductName ?? "";
                        if (name.Length > 28)
                            name = name.Substring(0, 28);

                        sb.AppendLine($"{name.PadRight(28)} {item.QuantitySold,6:0.##} {item.TotalSales,10:0.00}");
                    }
                }

                sb.AppendLine("----------------------------------------");
                sb.AppendLine();
                sb.AppendLine("EXPENSES");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine("Description                   Amount");
                sb.AppendLine("----------------------------------------");

                if (expenseList == null || expenseList.Count == 0)
                {
                    sb.AppendLine("No expenses.");
                }
                else
                {
                    foreach (var expense in expenseList)
                    {
                        string description = expense.Description ?? "";
                        if (description.Length > 28)
                            description = description.Substring(0, 28);

                        sb.AppendLine($"{description.PadRight(28)} {expense.Amount,10:0.00}");
                    }
                }

                sb.AppendLine("----------------------------------------");
                sb.AppendLine();
                sb.AppendLine("END OF REPORT");

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                return filePath;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to generate daily report. " + ex.Message, ex);
            }
        }

        public bool ReportExists(string reportFolder, DateTime reportDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportFolder))
                    return false;

                string dateText = reportDate.ToString("yyyy-MM-dd");
                string fileName = $"Daily_Report_{dateText}.txt";
                string filePath = Path.Combine(reportFolder, fileName);

                return File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }
    }
}