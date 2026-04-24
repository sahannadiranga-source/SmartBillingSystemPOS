namespace POSGardenia.Models
{
    public class ProductDisplay
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public decimal SellingPrice { get; set; }
        public bool IsKitchenItem { get; set; }
        public bool IsActive { get; set; }
    }
}