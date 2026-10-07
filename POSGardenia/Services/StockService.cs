using Microsoft.Data.Sqlite;
using POSGardenia.Data;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Services
{
    // The only place that changes stock. Every change is a ledger row (StockMovements);
    // the bill hooks (RecordSale / ReverseSale) run inside the caller's bill transaction so
    // a bill and its stock can never disagree.
    public class StockService
    {
        private const string DateFormat = "yyyy-MM-dd";

        private readonly StockItemRepository _stockItemRepository = new();
        private readonly StockMovementRepository _stockMovementRepository = new();
        private readonly StockCountRepository _stockCountRepository = new();

        // -----------------------------
        // Bill hooks (called inside the bill's own transaction)
        // -----------------------------

        // Deducts stock for one bill line. Products with no stock link are ignored, and a
        // missing UnitsPerSale never fails the sale (the setup screens are the real guard).
        public void RecordSale(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billItemId,
            int productId,
            decimal quantity,
            DateTime saleDate)
        {
            if (quantity <= 0)
                return;

            int? stockItemId = null;
            decimal? unitsPerSale = null;

            using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = transaction;
                lookup.CommandText = "SELECT StockItemId, UnitsPerSale FROM Products WHERE Id = @productId;";
                lookup.Parameters.AddWithValue("@productId", productId);

                using var reader = lookup.ExecuteReader();
                if (reader.Read())
                {
                    if (!reader.IsDBNull(0)) stockItemId = reader.GetInt32(0);
                    if (!reader.IsDBNull(1)) unitsPerSale = reader.GetDecimal(1);
                }
            }

            if (!stockItemId.HasValue || !unitsPerSale.HasValue || unitsPerSale.Value <= 0)
                return;

            _stockMovementRepository.Insert(connection, transaction, new StockMovement
            {
                StockItemId = stockItemId.Value,
                MovementDate = saleDate.ToString(DateFormat),
                MovementType = StockMovementTypes.Sale,
                QuantityChange = -(quantity * unitsPerSale.Value),
                BillItemId = billItemId
            });
        }

        // Puts back whatever is still deducted for a bill line (cancelled item / voided bill).
        // Safe to call twice: once the net is zero it does nothing.
        public void ReverseSale(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billItemId,
            DateTime reversalDate)
        {
            foreach (var (stockItemId, net) in _stockMovementRepository.GetNetSaleForBillItem(connection, transaction, billItemId))
            {
                if (net >= 0)
                    continue;

                _stockMovementRepository.Insert(connection, transaction, new StockMovement
                {
                    StockItemId = stockItemId,
                    MovementDate = reversalDate.ToString(DateFormat),
                    MovementType = StockMovementTypes.SaleReversal,
                    QuantityChange = -net,
                    BillItemId = billItemId
                });
            }
        }

        // -----------------------------
        // Manual stock entries
        // -----------------------------

        public int CreateStockItem(StockItem item, decimal openingQuantity, DateTime date)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            int id = CreateStockItem(connection, transaction, item, openingQuantity, date);

            transaction.Commit();
            return id;
        }

        // Same, inside the caller's transaction (product setup creates the stock item and its products together).
        public int CreateStockItem(
            SqliteConnection connection,
            SqliteTransaction transaction,
            StockItem item,
            decimal openingQuantity,
            DateTime date)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new Exception("Stock item name is required.");
            if (string.IsNullOrWhiteSpace(item.TrackingUnit))
                throw new Exception("Tracking unit is required.");
            if (openingQuantity < 0)
                throw new Exception("Opening quantity cannot be negative.");

            int id = _stockItemRepository.Add(connection, transaction, item);

            if (openingQuantity > 0)
            {
                _stockMovementRepository.Insert(connection, transaction, new StockMovement
                {
                    StockItemId = id,
                    MovementDate = date.ToString(DateFormat),
                    MovementType = StockMovementTypes.InitialStock,
                    QuantityChange = openingQuantity
                });
            }

            return id;
        }

        public void ReceiveStock(int stockItemId, decimal quantity, string? note, DateTime date)
        {
            if (quantity <= 0)
                throw new Exception("Quantity received must be greater than zero.");

            WriteManualMovement(stockItemId, StockMovementTypes.Restock, quantity, note, date);
        }

        // Signed: positive adds stock, negative removes it (count correction).
        public void AdjustStock(int stockItemId, decimal signedQuantity, string? note, DateTime date)
        {
            if (signedQuantity == 0)
                throw new Exception("Adjustment cannot be zero.");

            WriteManualMovement(stockItemId, StockMovementTypes.Adjustment, signedQuantity, note, date);
        }

        // Breakage / spillage / free serving. Enter a positive amount; it is stored as an outflow.
        public void RecordWastage(int stockItemId, decimal quantity, string? note, DateTime date)
        {
            if (quantity <= 0)
                throw new Exception("Wastage quantity must be greater than zero.");

            WriteManualMovement(stockItemId, StockMovementTypes.Wastage, -quantity, note, date);
        }

        private void WriteManualMovement(int stockItemId, string type, decimal change, string? note, DateTime date)
        {
            string dateText = date.ToString(DateFormat);

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            _stockMovementRepository.Insert(connection, transaction, new StockMovement
            {
                StockItemId = stockItemId,
                MovementDate = dateText,
                MovementType = type,
                QuantityChange = change,
                Note = note
            });

            transaction.Commit();
        }

        // -----------------------------
        // Daily sheet and Close Day
        // -----------------------------

        public List<DailyStockRow> GetDailySheet(DateTime date)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            string day = date.ToString(DateFormat);
            var rows = _stockMovementRepository.GetDailyRows(connection, day);

            var counts = _stockCountRepository.GetForDate(connection, day);
            if (counts.Count == 0)
                return rows;

            var servings = _stockCountRepository.GetSmallestServings(connection);

            foreach (var row in rows)
            {
                if (!counts.TryGetValue(row.StockItemId, out var count))
                    continue;

                // Compare with the previous count plus everything since (so each count is judged on its own
                // stretch of days). The first count of an item has nothing earlier: it is compared with the books.
                var previous = _stockCountRepository.GetLatestBefore(connection, row.StockItemId, day);
                decimal expected = row.ClosingQuantity;
                decimal sold = 0;

                if (previous != null)
                {
                    var (net, soldSince) = _stockCountRepository.GetMovementsBetween(connection, row.StockItemId, previous.CountDate, day);
                    expected = previous.TotalQuantity + net;
                    sold = soldSince;
                }

                decimal? pricePerUnit = null, shotSize = null;
                if (servings.TryGetValue(row.StockItemId, out var serving) && serving.Units > 0)
                {
                    pricePerUnit = serving.Price / serving.Units;
                    shotSize = serving.Units;
                }

                var check = CountCheck.Evaluate(count.TotalQuantity, expected, previous != null, sold,
                    row.PackSize, row.ExtraPerPack, row.TrackingUnit, pricePerUnit, shotSize);

                row.CountedQuantity = count.TotalQuantity;
                row.CountDifference = check.Difference;
                row.CountCheckText = check.Text;
                row.CountTone = check.Tone;
            }

            return rows;
        }

        // -----------------------------
        // End-of-day stock count
        // -----------------------------

        public StockCount? GetCount(int stockItemId, DateTime date)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            return _stockCountRepository.Get(connection, stockItemId, date.ToString(DateFormat));
        }

        // Saves what was physically there at the end of the day: whole bottles / packs plus the open one.
        // It never changes the stock itself; it is only compared with it. Counting again replaces the count.
        public StockCount SaveCount(int stockItemId, DateTime date, decimal fullBottles, decimal openQuantity)
        {
            if (date.Date > DateTime.Today)
                throw new Exception("A count cannot be for a future date.");

            if (fullBottles < 0 || openQuantity < 0)
                throw new Exception("Counts cannot be negative.");

            if (fullBottles != Math.Floor(fullBottles))
                throw new Exception("Full bottles / packs must be a whole number.");

            var item = _stockItemRepository.GetById(stockItemId)
                ?? throw new Exception("Stock item not found.");

            if (fullBottles > 0 && !(item.PackSize is > 0))
                throw new Exception("Set the bottle / pack size first, or enter the whole amount in the open field.");

            var count = new StockCount
            {
                StockItemId = stockItemId,
                CountDate = date.ToString(DateFormat),
                FullBottles = fullBottles,
                OpenQuantity = openQuantity,
                TotalQuantity = fullBottles * (item.PackSize ?? 0) + openQuantity
            };

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            _stockCountRepository.Save(connection, count);

            return count;
        }

        public void DeleteCount(int stockItemId, DateTime date)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            _stockCountRepository.Delete(connection, stockItemId, date.ToString(DateFormat));
        }

        // -----------------------------
        // Inventory tab support
        // -----------------------------

        public List<StockItemDisplay> GetStockItemsForDisplay() => _stockItemRepository.GetAllForDisplay();

        public List<StockItem> GetActiveStockItems() => _stockItemRepository.GetActive();

        public StockItem? GetStockItem(int id) => _stockItemRepository.GetById(id);

        // Sets what one full bottle / pack of a main item holds (e.g. 750 ml per bottle, 20 units per pack).
        // The name is not chosen: ml -> "bottle", unit -> "pack". A null size removes it.
        // extraPerPack (liquor only): ml each bottle gives beyond its size, e.g. 25. Blank = none.
        public void SetPack(int stockItemId, decimal? packSize, decimal? extraPerPack = null)
        {
            if (packSize.HasValue && packSize.Value <= 0)
                throw new Exception("Bottle / pack size must be greater than zero (or leave it blank to remove it).");

            if (extraPerPack.HasValue && extraPerPack.Value < 0)
                throw new Exception("Extra per bottle cannot be negative.");

            if (extraPerPack == 0)
                extraPerPack = null;

            var item = _stockItemRepository.GetById(stockItemId)
                ?? throw new Exception("Stock item not found.");

            if (packSize.HasValue && PackFormatter.IsCountedInBottles(item.TrackingUnit))
                throw new Exception("Items counted in bottles do not need a bottle / pack size.");

            if (extraPerPack.HasValue && !string.Equals(item.TrackingUnit, "ml", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Extra per bottle only applies to liquor counted in ml.");

            if (extraPerPack.HasValue && !packSize.HasValue)
                throw new Exception("Set the bottle size before the extra per bottle.");

            string? name = packSize.HasValue ? PackFormatter.PackNameFor(item.TrackingUnit) : null;

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            _stockItemRepository.UpdatePack(connection, transaction, stockItemId, name, packSize, extraPerPack);

            transaction.Commit();
        }

        // Consistency check: cached quantities must equal the ledger totals.
        public List<string> FindQuantityMismatches()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            return _stockMovementRepository.FindQuantityMismatches(connection);
        }
    }
}
