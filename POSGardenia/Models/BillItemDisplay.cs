namespace POSGardenia.Models
{
    public class BillItemDisplay
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal LineTotal { get; set; }
        public string Status { get; set; } = "";
        public bool IsKitchenPrinted { get; set; }
    }
}