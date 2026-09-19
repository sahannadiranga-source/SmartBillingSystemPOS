using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    // A stock item is a "main item" (e.g. Arrack in ml). Products link to it through Products.StockItemId.
    public class StockItemRepository
    {
        // Quantity always starts at 0; it only ever moves through StockMovementRepository.Insert.
        public int Add(SqliteConnection connection, SqliteTransaction transaction, StockItem item)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO StockItems (Name, CategoryId, TrackingUnit, CurrentQuantity, IsActive, IsDeleted)
                VALUES (@name, @categoryId, @trackingUnit, 0, 1, 0);
                SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@name", item.Name);
            command.Parameters.AddWithValue("@categoryId", item.CategoryId.HasValue ? item.CategoryId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@trackingUnit", item.TrackingUnit);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        public StockItem? GetById(int id)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, CategoryId, TrackingUnit, CurrentQuantity, IsActive
                FROM StockItems
                WHERE Id = @id AND IsDeleted = 0;";
            command.Parameters.AddWithValue("@id", id);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        // Main items are matched by name (case-insensitive) so "arrack" and "Arrack" are one item.
        public StockItem? GetByName(string name)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            return GetByName(connection, null, name);
        }

        public StockItem? GetByName(SqliteConnection connection, SqliteTransaction? transaction, string name)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                SELECT Id, Name, CategoryId, TrackingUnit, CurrentQuantity, IsActive
                FROM StockItems
                WHERE Name = @name COLLATE NOCASE AND IsDeleted = 0
                LIMIT 1;";
            command.Parameters.AddWithValue("@name", name);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        public List<StockItem> GetActive()
        {
            var items = new List<StockItem>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, CategoryId, TrackingUnit, CurrentQuantity, IsActive
                FROM StockItems
                WHERE IsActive = 1 AND IsDeleted = 0
                ORDER BY Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(Map(reader));

            return items;
        }

        // The Stock Items grid: one row per main item.
        public List<StockItemDisplay> GetAllForDisplay()
        {
            var items = new List<StockItemDisplay>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT si.Id, si.Name, si.TrackingUnit,
                       si.CurrentQuantity,
                       (SELECT COUNT(*) FROM Products p WHERE p.StockItemId = si.Id AND p.IsDeleted = 0)
                FROM StockItems si
                WHERE si.IsDeleted = 0
                ORDER BY si.Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new StockItemDisplay
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Unit = reader.GetString(2),
                    InStock = reader.GetDecimal(3),
                    LinkedProducts = reader.GetInt32(4)
                });
            }

            return items;
        }

        // Bigger packs sold from this stock (e.g. "Whiskey (Bottle)" = 750 ml), biggest first.
        public List<PackOption> GetPackOptions(int stockItemId)
        {
            var options = new List<PackOption>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Name, UnitsPerSale
                FROM Products
                WHERE StockItemId = @id AND IsDeleted = 0 AND UnitsPerSale > 1
                ORDER BY UnitsPerSale DESC, Name;";
            command.Parameters.AddWithValue("@id", stockItemId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                options.Add(new PackOption { Label = reader.GetString(0), Units = reader.GetDecimal(1) });

            return options;
        }

        private static StockItem Map(SqliteDataReader reader)
        {
            return new StockItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CategoryId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                TrackingUnit = reader.GetString(3),
                CurrentQuantity = reader.GetDecimal(4),
                IsActive = reader.GetInt32(5) == 1
            };
        }
    }
}
