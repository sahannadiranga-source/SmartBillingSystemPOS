using System;
using System.Collections.Generic;

namespace POSGardenia.Models
{
    public class ReceiptData
    {
        public string BusinessName { get; set; } = "Smart Billing POS";
        public string BillNo { get; set; } = "";
        public string TableName { get; set; } = "";
        public string BillType { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public DateTime PrintedAt { get; set; }
        public decimal Total { get; set; }
        public List<ReceiptLine> Items { get; set; } = new();
    }
}