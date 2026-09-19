using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class StockMovementRepository
    {
        // The single place that writes the ledger. It also keeps StockItems.CurrentQuantity
        // (a cached running total) in step, inside the caller's transaction.
        public void Insert(SqliteConnection connection, SqliteTransaction transaction, StockMovement movement)
        {
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO StockMovements
                        (StockItemId, MovementDate, MovementType, QuantityChange, BillItemId, Note, CreatedAt)
                    VALUES
                        (@stockItemId, @movementDate, @movementType, @quantityChange, @billItemId, @note, @createdAt);";

                insert.Parameters.AddWithValue("@stockItemId", movement.StockItemId);
                insert.Parameters.AddWithValue("@movementDate", movement.MovementDate);
                insert.Parameters.AddWithValue("@movementType", movement.MovementType);
                insert.Parameters.AddWithValue("@quantityChange", movement.QuantityChange);
                insert.Parameters.AddWithValue("@billItemId", movement.BillItemId.HasValue ? movement.BillItemId.Value : DBNull.Value);
                insert.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(movement.Note) ? DBNull.Value : movement.Note);
                insert.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                insert.ExecuteNonQuery();
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE StockItems
                SET CurrentQuantity = CurrentQuantity + @delta
                WHERE Id = @stockItemId;";
            update.Parameters.AddWithValue("@delta", movement.QuantityChange);
            update.Parameters.AddWithValue("@stockItemId", movement.StockItemId);
            update.ExecuteNonQuery();
        }

        // Stock still "out" for a bill line: Sale movements plus their reversals.
        // Negative = stock currently deducted; 0 = nothing deducted / already reversed.
        public List<(int StockItemId, decimal Net)> GetNetSaleForBillItem(
            SqliteConnection connection, SqliteTransaction transaction, int billItemId)
        {
            var result = new List<(int, decimal)>();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                SELECT StockItemId, SUM(QuantityChange)
                FROM StockMovements
                WHERE BillItemId = @billItemId
                  AND MovementType IN ('Sale', 'SaleReversal')
                GROUP BY StockItemId;";
            command.Parameters.AddWithValue("@billItemId", billItemId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add((reader.GetInt32(0), reader.GetDecimal(1)));

            return result;
        }

        // One query for the whole Daily Stock sheet. Opening is everything before the date,
        // so it is always exact and never depends on a snapshot being present.
        public List<DailyStockRow> GetDailyRows(SqliteConnection connection, string date)
        {
            var rows = new List<DailyStockRow>();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    si.Id,
                    si.Name,
                    si.TrackingUnit,
                    IFNULL((SELECT SUM(m.QuantityChange) FROM StockMovements m
                            WHERE m.StockItemId = si.Id AND m.MovementDate < @date), 0) AS Opening,
                    IFNULL((SELECT SUM(m.QuantityChange) FROM StockMovements m
                            WHERE m.StockItemId = si.Id AND m.MovementDate = @date
                              AND m.MovementType IN ('Restock', 'InitialStock')), 0) AS Received,
                    IFNULL((SELECT -SUM(m.QuantityChange) FROM StockMovements m
                            WHERE m.StockItemId = si.Id AND m.MovementDate = @date
                              AND m.MovementType IN ('Sale', 'SaleReversal')), 0) AS Sold,
                    IFNULL((SELECT SUM(m.QuantityChange) FROM StockMovements m
                            WHERE m.StockItemId = si.Id AND m.MovementDate = @date
                              AND m.MovementType IN ('Adjustment', 'Wastage')), 0) AS Adjusted
                FROM StockItems si
                WHERE si.IsDeleted = 0
                  AND (si.IsActive = 1
                       OR EXISTS (SELECT 1 FROM StockMovements m WHERE m.StockItemId = si.Id AND m.MovementDate <= @date))
                ORDER BY si.Name;";
            command.Parameters.AddWithValue("@date", date);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new DailyStockRow
                {
                    StockItemId = reader.GetInt32(0),
                    StockItemName = reader.GetString(1),
                    TrackingUnit = reader.GetString(2),
                    OpeningQuantity = reader.GetDecimal(3),
                    ReceivedQuantity = reader.GetDecimal(4),
                    SoldQuantity = reader.GetDecimal(5),
                    AdjustedQuantity = reader.GetDecimal(6)
                });
            }

            return rows;
        }

        // Stock items whose cached CurrentQuantity no longer equals the sum of their ledger.
        public List<string> FindQuantityMismatches(SqliteConnection connection)
        {
            var problems = new List<string>();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT si.Id, si.Name, si.CurrentQuantity,
                       IFNULL((SELECT SUM(m.QuantityChange) FROM StockMovements m WHERE m.StockItemId = si.Id), 0)
                FROM StockItems si
                WHERE ABS(si.CurrentQuantity -
                          IFNULL((SELECT SUM(m.QuantityChange) FROM StockMovements m WHERE m.StockItemId = si.Id), 0)) > 0.0001;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                problems.Add($"StockItem {reader.GetInt32(0)} '{reader.GetString(1)}': cached {reader.GetDecimal(2)} but ledger sums to {reader.GetDecimal(3)}");
            }

            return problems;
        }
    }
}
