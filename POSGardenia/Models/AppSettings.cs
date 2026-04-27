namespace POSGardenia.Models
{
    public class AppSettings
    {
        public string ReceiptPrinterName { get; set; } = "";
        public string BackupFolderPath { get; set; } = "";
        public int BackupIntervalMinutes { get; set; } = 15;

        public string DailyReportFolderPath { get; set; } = "";
        public string DailyReportTime { get; set; } = "23:00";
    }
}