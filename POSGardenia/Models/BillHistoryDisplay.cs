using System.ComponentModel;

namespace POSGardenia.Models
{
    public class BillHistoryDisplay
    {
        public int Id { get; set; }
        public string VisibleBillNumber { get; set; } = "";
        public string BillType { get; set; } = "";
        public string TableName { get; set; } = "";
        // The plain bill status (OPEN / PAID / VOID), used by the code; the table shows PaymentStatus as "Status".
        [Browsable(false)]
        public string Status { get; set; } = "";
        public string PaymentStatus { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string PaidAt { get; set; } = "";
        public decimal CashAmount { get; set; }
        public decimal CardAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DueAmount { get; set; }

        [Browsable(false)]   // repeats the columns already shown
        public string DisplayText => $"Bill No: {VisibleBillNumber} | {TableName} | {Status}";
    }
}
