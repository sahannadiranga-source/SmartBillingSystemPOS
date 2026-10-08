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
        private readonly ExpenseRepository _expenseRepository = new();
        private readonly StockOpeningCountRepository _openingCountRepository = new();

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

        // purchaseAmount (optional): what was paid for it. It is saved with the stock, in one step, as that day's
        // expense marked "stock purchase": listed with the expenses and in the daily report, but not taken off Net Sales.
        public void ReceiveStock(int stockItemId, decimal quantity, string? note, DateTime date, decimal? purchaseAmount = null)
        {
            if (quantity <= 0)
                throw new Exception("Quantity received must be greater than zero.");

            if (purchaseAmount.HasValue && purchaseAmount.Value <= 0)
                throw new Exception("Purchase amount must be greater than zero (or leave it blank).");

            if (!purchaseAmount.HasValue)
            {
                WriteManualMovement(stockItemId, StockMovementTypes.Restock, quantity, note, date);
                return;
            }

            var item = _stockItemRepository.GetById(stockItemId)
                ?? throw new Exception("Stock item not found.");

            string dateText = date.ToString(DateFormat);

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            _stockMovementRepository.Insert(connection, transaction, new StockMovement
            {
                StockItemId = stockItemId,
                MovementDate = dateText,
                MovementType = StockMovementTypes.Restock,
                QuantityChange = quantity,
                Note = note
            });

            string description = $"Stock purchase: {item.Name} {quantity:0.##} {item.TrackingUnit}";
            if (!string.IsNullOrWhiteSpace(note))
                description += $" - {note.Trim()}";

            _expenseRepository.Add(connection, transaction, new Expense
            {
                ExpenseDate = dateText,
                Description = description,
                Amount = purchaseAmount.Value,
                CreatedAt = DateTime.Now,
                IsStockPurchase = true
            });

            transaction.Commit();
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

            return _stockMovementRepository.GetDailyRows(connection, date.ToString(DateFormat));
        }

        // -----------------------------
        // Opening count (Stock Items tab -> that day's Opening on Daily Stock)
        // -----------------------------

        // Counts an item by hand for today: counted is the amount in the item's own unit (ml / bottle / unit).
        // It becomes today's Opening. The ledger gets one "OpeningCount" entry for the gap between the books
        // (everything before today) and the count; counting again only adds the further change, so history is kept.
        // Difference (saved with the day) = the counted opening - what the books closed with yesterday.
        public void SaveOpeningCount(int stockItemId, DateTime date, decimal counted)
        {
            if (date.Date != DateTime.Today)
                throw new Exception("The opening count is for today only.");

            if (counted < 0)
                throw new Exception("The counted amount cannot be negative.");

            string day = date.ToString(DateFormat);

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (_stockItemRepository.GetById(stockItemId) == null)
                throw new Exception("Stock item not found.");

            decimal previousClose = _openingCountRepository.SumMovementsBefore(connection, transaction, stockItemId, day);
            decimal alreadyMoved = _openingCountRepository.SumOpeningCountMovements(connection, transaction, stockItemId, day);
            decimal change = (counted - previousClose) - alreadyMoved;

            if (change != 0)
                WriteOpeningCountMovement(connection, transaction, stockItemId, day, change, $"Opening count {counted:0.##}");

            _openingCountRepository.Save(connection, transaction, new StockOpeningCount
            {
                StockItemId = stockItemId,
                CountDate = day,
                CountedQuantity = counted,
                PreviousClose = previousClose,
                Difference = counted - previousClose
            });

            transaction.Commit();
        }

        // Takes today's count away: the opening goes back to what the books say.
        public void ClearOpeningCount(int stockItemId, DateTime date)
        {
            if (date.Date != DateTime.Today)
                throw new Exception("The opening count is for today only.");

            string day = date.ToString(DateFormat);

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            decimal alreadyMoved = _openingCountRepository.SumOpeningCountMovements(connection, transaction, stockItemId, day);
            if (alreadyMoved != 0)
                WriteOpeningCountMovement(connection, transaction, stockItemId, day, -alreadyMoved, "Opening count removed");

            _openingCountRepository.Delete(connection, transaction, stockItemId, day);

            transaction.Commit();
        }

        public StockOpeningCount? GetOpeningCount(int stockItemId, DateTime date)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            return _openingCountRepository.Get(connection, stockItemId, date.ToString(DateFormat));
        }

        private void WriteOpeningCountMovement(
            SqliteConnection connection, SqliteTransaction transaction,
            int stockItemId, string day, decimal change, string note)
        {
            _stockMovementRepository.Insert(connection, transaction, new StockMovement
            {
                StockItemId = stockItemId,
                MovementDate = day,
                MovementType = StockMovementTypes.OpeningCount,
                QuantityChange = change,
                Note = note
            });
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
