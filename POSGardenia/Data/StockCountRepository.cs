using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    // Physical stock counts (one per item per day). Counts never change the stock ledger.
    public class StockCountRepository
    {
        // Saves the count; counting the same item and day again replaces it.
        public void Save(SqliteConnection connection, StockCount count)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO StockCounts (StockItemId, CountDate, FullBottles, OpenQuantity, TotalQuantity, CreatedAt)
                VALUES (@item, @date, @full, @open, @total, @createdAt)
                ON CONFLICT (StockItemId, CountDate) DO UPDATE SET
                    FullBottles = excluded.FullBottles,
                    OpenQuantity = excluded.OpenQuantity,
                    TotalQuantity = excluded.TotalQuantity,
                    CreatedAt = excluded.CreatedAt;";
            command.Parameters.AddWithValue("@item", count.StockItemId);
            command.Parameters.AddWithValue("@date", count.CountDate);
            command.Parameters.AddWithValue("@full", count.FullBottles);
            command.Parameters.AddWithValue("@open", count.OpenQuantity);
            command.Parameters.AddWithValue("@total", count.TotalQuantity);
            command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.ExecuteNonQuery();
        }

        public void Delete(SqliteConnection connection, int stockItemId, string date)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM StockCounts WHERE StockItemId = @item AND CountDate = @date;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@date", date);
            command.ExecuteNonQuery();
        }

        public StockCount? Get(SqliteConnection connection, int stockItemId, string date)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM StockCounts WHERE StockItemId = @item AND CountDate = @date;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@date", date);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        public Dictionary<int, StockCount> GetForDate(SqliteConnection connection, string date)
        {
            var result = new Dictionary<int, StockCount>();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM StockCounts WHERE CountDate = @date;";
            command.Parameters.AddWithValue("@date", date);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var count = Map(reader);
                result[count.StockItemId] = count;
            }

            return result;
        }

        // The most recent count of this item on an earlier day: the starting point for the next comparison.
        public StockCount? GetLatestBefore(SqliteConnection connection, int stockItemId, string date)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT {Columns} FROM StockCounts
                WHERE StockItemId = @item AND CountDate < @date
                ORDER BY CountDate DESC
                LIMIT 1;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@date", date);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        // Everything that moved after the day of the previous count, up to and including the counted day:
        // the net change, and how much was sold.
        public (decimal Net, decimal Sold) GetMovementsBetween(SqliteConnection connection, int stockItemId, string afterDate, string uptoDate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT IFNULL(SUM(QuantityChange), 0),
                       IFNULL(-SUM(CASE WHEN MovementType IN ('Sale', 'SaleReversal') THEN QuantityChange ELSE 0 END), 0)
                FROM StockMovements
                WHERE StockItemId = @item AND MovementDate > @after AND MovementDate <= @upto;";
            command.Parameters.AddWithValue("@item", stockItemId);
            command.Parameters.AddWithValue("@after", afterDate);
            command.Parameters.AddWithValue("@upto", uptoDate);

            using var reader = command.ExecuteReader();
            reader.Read();
            return (reader.GetDecimal(0), reader.GetDecimal(1));
        }

        // The smallest serving sold from each stock item (e.g. the 25 ml shot): its size and price,
        // used to value an extra or missing amount at the shot price.
        public Dictionary<int, (decimal Units, decimal Price)> GetSmallestServings(SqliteConnection connection)
        {
            var result = new Dictionary<int, (decimal, decimal)>();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT StockItemId, UnitsPerSale, SellingPrice
                FROM Products
                WHERE StockItemId IS NOT NULL AND UnitsPerSale > 0 AND IsDeleted = 0
                ORDER BY StockItemId, UnitsPerSale ASC, SellingPrice ASC;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                if (!result.ContainsKey(id))
                    result[id] = (reader.GetDecimal(1), reader.GetDecimal(2));
            }

            return result;
        }

        private const string Columns = "Id, StockItemId, CountDate, FullBottles, OpenQuantity, TotalQuantity";

        private static StockCount Map(SqliteDataReader reader) => new()
        {
            Id = reader.GetInt32(0),
            StockItemId = reader.GetInt32(1),
            CountDate = reader.GetString(2),
            FullBottles = reader.GetDecimal(3),
            OpenQuantity = reader.GetDecimal(4),
            TotalQuantity = reader.GetDecimal(5)
        };
    }
}
