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

        // Stock link: both null = untracked product (sales never touch stock).
        public int? StockItemId { get; set; }
        public decimal? UnitsPerSale { get; set; }

        // Name of the main item (stock item) this product draws from; used to group products in the POS.
        public string? MainItemName { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
