using System;

namespace POSGardenia.Models
{
    public class Bill
    {
        public int Id { get; set; }
        public int? DiningTableId { get; set; }
        public string BillType { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public string BillDate { get; set; } = "";
        public int DailyBillNumber { get; set; }
    }
}