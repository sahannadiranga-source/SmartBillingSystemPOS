using System.ComponentModel;

namespace POSGardenia.Models
{
    // Row shapes for the Inventory tab grids (columns are auto-generated from these properties).
    public class StockItemDisplay
    {
        [Browsable(false)]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal InStock { get; set; }

        [Browsable(false)]
        public string? PackName { get; set; }
        [Browsable(false)]
        public decimal? PackSize { get; set; }

        // In stock, counted in full bottles / packs (blank when the main item has no full-pack size).
        public string InPacks => PackFormatter.Describe(InStock, Unit, PackName, PackSize);

        public int LinkedProducts { get; set; }
    }

    public class DailyStockDisplay
    {
        // Column order = property order: Received and Adjusted side by side, then Sold.
        [Browsable(false)]
        public int StockItemId { get; set; }
        public string Item { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Opening { get; set; }
        public decimal Received { get; set; }
        public decimal Adjusted { get; set; }
        public decimal Sold { get; set; }
        public decimal Closing { get; set; }

        // Sold and what is left, counted in full bottles / packs (blank when there is no full-pack size).
        public string SoldInPacks { get; set; } = "";
        public string InPacks { get; set; } = "";

        // Selling price of the quantity sold.
        public decimal SalesValue { get; set; }

        public string Status { get; set; } = "";

        [Browsable(false)]
        public bool IsNegative { get; set; }
    }
}
