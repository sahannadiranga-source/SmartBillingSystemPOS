namespace POSGardenia.Models
{
    public class BillHistoryDisplay
    {
        public int Id { get; set; }
        public string VisibleBillNumber { get; set; } = "";
        public string BillType { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Status { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string PaidAt { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public decimal TotalAmount { get; set; }

        public string DisplayText => $"Bill No: {VisibleBillNumber} | {TableName} | {Status}";
    }
}