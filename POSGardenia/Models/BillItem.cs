namespace POSGardenia.Models
{
    public class BillItem
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public int ProductId { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Quantity { get; set; }
        public string Status { get; set; } = "";   // ACTIVE, CANCELLED, VOID
        public bool IsKitchenPrinted { get; set; }
    }
}