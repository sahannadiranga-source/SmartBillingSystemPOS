namespace POSGardenia.Models
{
    public class ReceiptLine
    {
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal => Quantity * UnitPrice;
    }
}