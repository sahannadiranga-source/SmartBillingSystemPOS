using System;

namespace POSGardenia.Models
{
    // One line of the Daily Stock sheet. Everything here is calculated from the ledger.
    public class DailyStockRow
    {
        public int StockItemId { get; set; }
        public string StockItemName { get; set; } = "";
        public string TrackingUnit { get; set; } = "";
        public decimal OpeningQuantity { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal SoldQuantity { get; set; }        // positive number = amount sold
        public decimal AdjustedQuantity { get; set; }    // signed: adjustments and wastage

        public decimal ClosingQuantity =>
            OpeningQuantity + ReceivedQuantity - SoldQuantity + AdjustedQuantity;

        public bool IsNegative => ClosingQuantity < 0;

    }
}
