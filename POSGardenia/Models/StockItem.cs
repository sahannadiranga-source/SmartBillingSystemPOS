namespace POSGardenia.Models
{
    // The physical thing on the shelf (e.g. a whiskey brand tracked in ml).
    // Several sellable Products can draw from one StockItem.
    public class StockItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? CategoryId { get; set; }
        public string TrackingUnit { get; set; } = "";   // "ml" (liquor), "bottle" (beer), "unit" (everything else)
        public decimal CurrentQuantity { get; set; }
        public bool IsActive { get; set; }

        // A full bottle / pack, in the tracking unit (e.g. "bottle" = 750 ml, "pack" = 20 units).
        // Optional: used to show stock as bottles / packs and as a way to count when entering stock.
        public string? PackName { get; set; }
        public decimal? PackSize { get; set; }

        // Liquor only: ml each bottle gives beyond its size (e.g. 25). Blank = none. Used by the stock count.
        public decimal? ExtraPerPack { get; set; }

        public override string ToString() => Name;
    }
}
