using System;
using System.ComponentModel;
using System.Windows;

namespace POSGardenia.Models
{
    // Row shapes for the Inventory tab grids (columns are auto-generated from these properties).
    public class StockItemDisplay
    {
        // Column order = property order of the visible ones:
        // Unit, Linked Products, Name, In Stock, In Stock, System Count, Counted, Counted, Received Today.
        [Browsable(false)]
        public int Id { get; set; }
        public string Unit { get; set; } = "";
        public int LinkedProducts { get; set; }
        public string Name { get; set; } = "";
        public decimal InStock { get; set; }

        [Browsable(false)]
        public string? PackName { get; set; }
        [Browsable(false)]
        public decimal? PackSize { get; set; }

        // In stock, counted in full bottles / packs (blank when the main item has no full-pack size).
        public string InPacks => PackFormatter.Describe(InStock, Unit, PackName, PackSize);

        // What the books closed with yesterday (everything before today), in the item's own unit.
        [Browsable(false)]
        public decimal PreviousClose { get; set; }

        // The same, as people count it: "10 bottles + 720 ml". Items with no bottle / pack size show the plain
        // amount ("12 bottles", "3300 ml") so the column is never empty.
        public string SystemCount => PackSize is > 0 && !PackFormatter.IsCountedInBottles(Unit)
            ? PackFormatter.Describe(PreviousClose, Unit, PackName, PackSize)
            : $"{PreviousClose.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} {PackFormatter.Plural(Unit, PreviousClose)}";

        // Counted by hand for today (type it in the table; blank = not counted). It becomes today's Opening.
        public decimal? Counted { get; set; }

        // The same count in full bottles / packs.
        public string CountedInPacks => Counted.HasValue ? PackFormatter.Describe(Counted.Value, Unit, PackName, PackSize) : "";

        // Stock received today (what "Add stock" and deliveries put in).
        public decimal ReceivedToday { get; set; }

        // ---- typing the count as bottles + what is left (the second Counted column) ----

        // The two boxes of the cell: whole bottles / packs, then the rest in ml / units.
        [Browsable(false)]
        public decimal? CountBottles { get; set; }
        [Browsable(false)]
        public decimal? CountLoose { get; set; }

        [Browsable(false)]
        public bool CountedInBottleUnit => PackFormatter.IsCountedInBottles(Unit);

        // A bottle / pack size is set (so bottles + the rest can be added up).
        [Browsable(false)]
        public bool HasPack => PackSize is > 0 && !CountedInBottleUnit;

        // Beer is counted in bottles only (no "rest"); an item with no bottle size has no bottles box.
        [Browsable(false)]
        public Visibility BottlesBoxVisibility => HasPack || CountedInBottleUnit ? Visibility.Visible : Visibility.Collapsed;
        [Browsable(false)]
        public Visibility LooseBoxVisibility => CountedInBottleUnit ? Visibility.Collapsed : Visibility.Visible;

        [Browsable(false)]
        public string BottlesLabel => CountedInBottleUnit
            ? "bottles"
            : PackFormatter.Plural(PackName ?? PackFormatter.PackNameFor(Unit), 2);

        // Fills the two boxes from today's count when the cell opens for editing.
        public void BeginCountEdit()
        {
            CountBottles = null;
            CountLoose = null;

            if (!Counted.HasValue)
                return;

            decimal counted = Counted.Value;

            if (CountedInBottleUnit)
            {
                CountBottles = Tidy(counted);
            }
            else if (HasPack)
            {
                decimal whole = Math.Floor(counted / PackSize!.Value);
                CountBottles = Tidy(whole);
                CountLoose = Tidy(counted - whole * PackSize.Value);
            }
            else
            {
                CountLoose = Tidy(counted);
            }
        }

        // 60.0 -> 60 (amounts read from the database carry a trailing .0)
        private static decimal Tidy(decimal value) => value / 1.0000000000000000000000000000m;

        // What the two boxes add up to, in the item's own unit. total = null with no error means "both blank".
        public bool TryGetCountedTotal(out decimal? total, out string error)
        {
            total = null;
            error = "";

            if (!CountBottles.HasValue && !CountLoose.HasValue)
                return true;

            decimal bottles = CountBottles ?? 0;
            decimal loose = CountLoose ?? 0;

            if (bottles < 0 || loose < 0)
            {
                error = "Morning Manual Count: enter 0 or more.";
                return false;
            }

            if (CountedInBottleUnit)
            {
                total = bottles;
                return true;
            }

            if (!HasPack)
            {
                total = loose;
                return true;
            }

            if (bottles != Math.Floor(bottles))
            {
                error = $"Morning Manual Count: the bottles / packs must be a whole number (put the rest in the {Unit} box).";
                return false;
            }

            total = bottles * PackSize!.Value + loose;
            return true;
        }
    }

    public class DailyStockDisplay
    {
        // Column order = property order of the visible ones:
        // Unit, Item, Difference, Opening, Sold, Sold, Sales Value, Received, Left, Closing.
        [Browsable(false)]
        public int StockItemId { get; set; }
        public string Unit { get; set; } = "";
        public string Item { get; set; } = "";

        // Counted opening minus the previous day's close (blank when the item was not counted that day).
        public decimal? Difference { get; set; }

        public decimal Opening { get; set; }
        public decimal Sold { get; set; }

        // Sold counted in full bottles / packs (blank when there is no full-pack size).
        public string SoldInPacks { get; set; } = "";

        // Selling price of the quantity sold.
        public decimal SalesValue { get; set; }

        public decimal Received { get; set; }

        // What is left, counted in full bottles / packs.
        public string InPacks { get; set; } = "";

        public decimal Closing { get; set; }

        // Adjustments are already inside Closing; not shown as a column.
        [Browsable(false)]
        public decimal Adjusted { get; set; }

        [Browsable(false)]
        public string Status { get; set; } = "";

        [Browsable(false)]
        public bool IsNegative { get; set; }
    }
}
