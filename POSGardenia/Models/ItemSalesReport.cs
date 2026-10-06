using System.ComponentModel;

namespace POSGardenia.Models
{
    // One row of "Item Sales". Sales are grouped by STOCK item, not by product: the bottle and the
    // 100 / 50 / 25 ml shots of one liquor are a single row counted in ml. Products that are not
    // tracked in stock (food) keep their own row, counted as sold.
    public class ItemSalesReport
    {
        public string Item { get; set; } = "";

        // ml / bottle / unit for stock items; blank for products that are not tracked in stock.
        public string Unit { get; set; } = "";

        // In the stock unit (e.g. 1250 ml). For untracked products: how many were sold.
        public decimal QuantitySold { get; set; }

        [Browsable(false)]
        public string? PackName { get; set; }
        [Browsable(false)]
        public decimal? PackSize { get; set; }

        // The same quantity counted in full bottles / packs (blank when the item has no bottle / pack size).
        public string InPacks => PackFormatter.Describe(QuantitySold, Unit, PackName, PackSize);

        public decimal TotalSales { get; set; }
    }
}
