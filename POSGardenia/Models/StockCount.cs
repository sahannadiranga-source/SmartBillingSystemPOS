namespace POSGardenia.Models
{
    // What was physically there at the END of a day: whole bottles / packs plus the open one.
    // TotalQuantity (in the stock unit) is saved when the count is entered, so a later change of
    // bottle size never changes an old count.
    public class StockCount
    {
        public int Id { get; set; }
        public int StockItemId { get; set; }
        public string CountDate { get; set; } = "";     // yyyy-MM-dd
        public decimal FullBottles { get; set; }
        public decimal OpenQuantity { get; set; }
        public decimal TotalQuantity { get; set; }
    }
}
