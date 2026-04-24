using System;

namespace POSGardenia.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public string PaymentMethod { get; set; } = ""; // CASH, CARD
        public decimal Amount { get; set; }
        public DateTime PaidAt { get; set; }
    }
}