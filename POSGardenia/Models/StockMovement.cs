namespace POSGardenia.Models
{
    public static class StockMovementTypes
    {
        public const string InitialStock = "InitialStock";
        public const string Restock = "Restock";
        public const string Sale = "Sale";
        public const string SaleReversal = "SaleReversal";
        public const string Adjustment = "Adjustment";
        public const string Wastage = "Wastage";

        // The stock counted by hand at the start of a day, as a change to that day's opening.
        public const string OpeningCount = "OpeningCount";
    }

    // One row of the stock ledger. QuantityChange is negative when stock goes out.
    public class StockMovement
    {
        public int Id { get; set; }
        public int StockItemId { get; set; }
        public string MovementDate { get; set; } = "";   // yyyy-MM-dd
        public string MovementType { get; set; } = "";
        public decimal QuantityChange { get; set; }
        public int? BillItemId { get; set; }
        public string? Note { get; set; }
    }
}
