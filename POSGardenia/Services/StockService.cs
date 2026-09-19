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

            return _stockMovementRepository.GetDailyRows(connection, date.ToString(DateFormat));
        }

        // -----------------------------
        // Inventory tab support
        // -----------------------------

        public List<StockItemDisplay> GetStockItemsForDisplay() => _stockItemRepository.GetAllForDisplay();

        public List<StockItem> GetActiveStockItems() => _stockItemRepository.GetActive();

        public List<PackOption> GetPackOptions(int stockItemId) => _stockItemRepository.GetPackOptions(stockItemId);

        // Consistency check: cached quantities must equal the ledger totals.
        public List<string> FindQuantityMismatches()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            return _stockMovementRepository.FindQuantityMismatches(connection);
        }
    }
}
