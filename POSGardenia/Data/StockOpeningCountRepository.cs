using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    // The opening count of an item for a day (one per item per day). The matching stock change is written to
    // the ledger by StockService; this table keeps what was counted and the saved Difference.
    public class StockOpeningCountRepository
    {
        // Counting the same item on the same day again replaces the record.
        public void Save(SqliteConnection connection, SqliteTransaction transaction, StockOpeningCount count)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO StockOpeningCounts (StockItemId, CountDate, CountedQuantity, PreviousClose, Difference, CreatedAt)
                VALUES (@item, @date, @counted, @previousClose, @difference, @createdAt)
                ON CONFLICT (StockItemId, CountDate) DO UPDATE SET
                    CountedQuantity = excluded.CountedQuantity,
                    PreviousClose = excluded.PreviousClose,
                    Difference = excluded.Difference,
                    CreatedAt = excluded.CreatedAt;";
            command.Parameters.AddWithValue("@item", count.StockItemId);
            command.Parameters.AddWithValue("@date", count.CountDate);
            command.Parameters.AddWithValue("@counted", count.CountedQuantity);
            command.Parameters.AddWithValue("@previousClose", count.PreviousClose);
            command.Parameters.AddWithValue("@difference", count.Difference);
            command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.ExecuteNonQuery();
        }

        public void Delete(SqliteConnection connection, SqliteTransaction transaction, int stockItemId, string date)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM StockOpeningCounts WHERE StockItemId = @item AND CountDate = @date;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@date", date);
            command.ExecuteNonQuery();
        }

        public StockOpeningCount? Get(SqliteConnection connection, int stockItemId, string date)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, StockItemId, CountDate, CountedQuantity, PreviousClose, Difference
                FROM StockOpeningCounts
                WHERE StockItemId = @item AND CountDate = @date;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@date", date);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            return new StockOpeningCount
            {
                Id = reader.GetInt32(0),
                StockItemId = reader.GetInt32(1),
                CountDate = reader.GetString(2),
                CountedQuantity = reader.GetDecimal(3),
                PreviousClose = reader.GetDecimal(4),
                Difference = reader.GetDecimal(5)
            };
        }

        // What the books say the item closed the day before with: everything in the ledger before this day.
        public decimal SumMovementsBefore(SqliteConnection connection, SqliteTransaction transaction, int stockItemId, string date)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT IFNULL(SUM(QuantityChange), 0) FROM StockMovements WHERE StockItemId = @item AND MovementDate < @date;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@date", date);
            return Convert.ToDecimal(command.ExecuteScalar());
        }

        // Stock already moved into this day's opening by earlier counts of the same day.
        public decimal SumOpeningCountMovements(SqliteConnection connection, SqliteTransaction transaction, int stockItemId, string date)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                SELECT IFNULL(SUM(QuantityChange), 0) FROM StockMovements
                WHERE StockItemId = @item AND MovementDate = @date AND MovementType = @type;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@date", date);
            command.Parameters.AddWithValue("@type", StockMovementTypes.OpeningCount);
            return Convert.ToDecimal(command.ExecuteScalar());
        }
    }
}
