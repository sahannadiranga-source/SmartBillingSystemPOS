namespace POSGardenia.Models
{
    public class PosCartLine
    {
        public int? BillItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal LineTotal => UnitPrice * Quantity;
        public bool IsKitchenItem { get; set; }

        public bool IsExistingItem { get; set; }
        public bool IsNewItem => !IsExistingItem;

        public string LineState => IsExistingItem ? "Existing" : "New";
    }
}