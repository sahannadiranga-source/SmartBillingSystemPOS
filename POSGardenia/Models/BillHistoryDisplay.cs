namespace POSGardenia.Models
{
    public class BillHistoryDisplay
    {
        public int Id { get; set; }
        public string VisibleBillNumber { get; set; } = "";
        public string BillType { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Status { get; set; } = "";
        public string PaymentStatus { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string PaidAt { get; set; } = "";
        public decimal CashAmount { get; set; }
        public decimal CardAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DueAmount { get; set; }

        public string DisplayText => $"Bill No: {VisibleBillNumber} | {TableName} | {Status}";
    }
}
