using System.ComponentModel;

namespace POSGardenia.Models
{
    public class ProductDisplay
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public decimal SellingPrice { get; set; }
        [Browsable(false)]
        public bool IsKitchenItem { get; set; }
        public bool IsActive { get; set; }

        [Browsable(false)]
        public int? StockItemId { get; set; }

        // Blank = not tracked. Products with the same MainItem share one stock.
        // UnitsPerSale is how much of it one sale uses (e.g. 50 for a 50 ml shot).
        public string MainItem { get; set; } = "";
        public decimal? UnitsPerSale { get; set; }
    }
}
