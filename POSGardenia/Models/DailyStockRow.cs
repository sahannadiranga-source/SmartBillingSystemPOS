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

        // Selling price of what was sold (bill price x quantity of every sale that day, less cancelled lines).
        public decimal SalesValue { get; set; }

        // Full bottle / pack of the main item (blank when not set).
        public string? PackName { get; set; }
        public decimal? PackSize { get; set; }
        public decimal? ExtraPerPack { get; set; }

        // The end-of-day physical count and what it means (empty when this item was not counted that day).
        public decimal? CountedQuantity { get; set; }
        public decimal? CountDifference { get; set; }
        public string CountCheckText { get; set; } = "";
        public string CountTone { get; set; } = "";

        public decimal ClosingQuantity =>
            OpeningQuantity + ReceivedQuantity - SoldQuantity + AdjustedQuantity;

        public bool IsNegative => ClosingQuantity < 0;

    }
}
