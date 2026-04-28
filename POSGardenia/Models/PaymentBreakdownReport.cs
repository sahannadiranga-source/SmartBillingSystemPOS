namespace POSGardenia.Models
{
    public class PaymentBreakdownReport
    {
        public string PaymentMethod { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public int BillCount { get; set; }
    }
}