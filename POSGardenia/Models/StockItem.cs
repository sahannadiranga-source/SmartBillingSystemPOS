namespace POSGardenia.Models
{
    // The physical thing on the shelf (e.g. a whiskey brand tracked in ml).
    // Several sellable Products can draw from one StockItem.
    public class StockItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? CategoryId { get; set; }
        public string TrackingUnit { get; set; } = "";   // "ml", "bottle", "cigarette"
        public decimal CurrentQuantity { get; set; }
        public bool IsActive { get; set; }

        public override string ToString() => Name;
    }
}
