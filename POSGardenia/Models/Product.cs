namespace POSGardenia.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int CategoryId { get; set; }
        public decimal SellingPrice { get; set; }
        public bool IsKitchenItem { get; set; }
        public bool IsActive { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}