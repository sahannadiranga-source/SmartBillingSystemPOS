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
        public DateTime PrintedAt { get; set; }
        public decimal Total { get; set; }
        public decimal CashAmount { get; set; }
        public decimal CardAmount { get; set; }
        public decimal DueAmount { get; set; }

        // Cash handed over and the change given back for the payment just made (0 = exact amount).
        public decimal ReceivedAmount { get; set; }
        public decimal ChangeAmount { get; set; }
        public List<ReceiptLine> Items { get; set; } = new();
    }
}