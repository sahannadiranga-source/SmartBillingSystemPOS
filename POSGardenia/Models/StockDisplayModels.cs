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

        // Stock received today (what "Add stock" and deliveries put in).
        public decimal ReceivedToday { get; set; }
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
        // Column order = property order of the visible ones:
        // Item, Unit, Opening, Received, Sold, Sold, Sales Value, Left, Closing.
        [Browsable(false)]
        public int StockItemId { get; set; }
        public string Item { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Opening { get; set; }
        public decimal Received { get; set; }
        public decimal Sold { get; set; }

        // Sold counted in full bottles / packs (blank when there is no full-pack size).
        public string SoldInPacks { get; set; } = "";

        // Selling price of the quantity sold.
        public decimal SalesValue { get; set; }

        // What is left, counted in full bottles / packs.
        public string InPacks { get; set; } = "";

        public decimal Closing { get; set; }

        // End-of-day physical count: not shown as columns (the verdict is under Save Count; the row
        // turns red / amber when a count is missing / to check).
        [Browsable(false)]
        public decimal? Counted { get; set; }

        [Browsable(false)]
        public string CountedInPacks { get; set; } = "";

        [Browsable(false)]
        public decimal? Difference { get; set; }

        // Not shown as columns: adjustments are already inside Closing, and the count verdict is
        // shown under Save Count. IsNegative / CountTone only colour the row.
        [Browsable(false)]
        public decimal Adjusted { get; set; }

        [Browsable(false)]
        public string CountCheck { get; set; } = "";

        [Browsable(false)]
        public string Status { get; set; } = "";

        [Browsable(false)]
        public bool IsNegative { get; set; }

        // "red" / "amber" row colour from the count check.
        [Browsable(false)]
        public string CountTone { get; set; } = "";
    }
}
