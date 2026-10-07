namespace POSGardenia.Models
{
    // The stock counted by hand at the start of a day. It becomes that day's Opening on Daily Stock.
    // Difference = the counted opening minus what the books closed the day before with
    // (positive = more than the books said, negative = stock is missing). It is saved with the day.
    public class StockOpeningCount
    {
        public int Id { get; set; }
        public int StockItemId { get; set; }
        public string CountDate { get; set; } = "";     // yyyy-MM-dd
        public decimal CountedQuantity { get; set; }    // in the item's stock unit (ml / bottle / unit)
        public decimal PreviousClose { get; set; }
        public decimal Difference { get; set; }
    }
}
