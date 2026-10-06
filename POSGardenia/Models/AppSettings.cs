namespace POSGardenia.Models
{
    public class AppSettings
    {
        public string ReceiptPrinterName { get; set; } = "";

        // Printer for kitchen tickets; blank = use the receipt printer.
        public string KitchenPrinterName { get; set; } = "";
        public string BackupFolderPath { get; set; } = "";
        public int BackupIntervalMinutes { get; set; } = 15;

        public string DailyReportFolderPath { get; set; } = "";
        public string DailyReportTime { get; set; } = "23:00";

        // Touch screens: show the on-screen keyboard / keypad when a text field is tapped.
        public bool UseOnScreenKeyboard { get; set; } = true;
    }
}