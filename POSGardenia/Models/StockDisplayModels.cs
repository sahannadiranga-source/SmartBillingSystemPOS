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
        public int LinkedProducts { get; set; }
    }

    public class DailyStockDisplay
    {
        [Browsable(false)]
        public int StockItemId { get; set; }
        public string Item { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Opening { get; set; }
        public decimal Received { get; set; }
        public decimal Sold { get; set; }
        public decimal Adjusted { get; set; }
        public decimal Closing { get; set; }
        public string Status { get; set; } = "";

        [Browsable(false)]
        public bool IsNegative { get; set; }
    }

    // "Enter as": the stock item's own unit, or a bigger pack such as "Whiskey (Bottle)" = 750.
    public class PackOption
    {
        public string Label { get; set; } = "";
        public decimal Units { get; set; } = 1;

        public override string ToString() => Label;
    }
}
