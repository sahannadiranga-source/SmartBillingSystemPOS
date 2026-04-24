namespace POSGardenia.Models
{
    public class AppSettings
    {
        public string ReceiptPrinterName { get; set; } = "";
        public string BackupFolderPath { get; set; } = "";
        public int BackupIntervalMinutes { get; set; } = 15;
    }
}