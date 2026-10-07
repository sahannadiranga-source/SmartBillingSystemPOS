using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    // A stock item is a "main item" (e.g. Arrack in ml). Products link to it through Products.StockItemId.
    public class StockItemRepository
    {
        // Columns read by Map(): keep this list and Map() in step.
        private const string ItemColumns =
            "Id, Name, CategoryId, TrackingUnit, CurrentQuantity, IsActive, PackName, PackSize, ExtraPerPack";

        // Quantity always starts at 0; it only ever moves through StockMovementRepository.Insert.
        public int Add(SqliteConnection connection, SqliteTransaction transaction, StockItem item)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO StockItems (Name, CategoryId, TrackingUnit, PackName, PackSize, CurrentQuantity, IsActive, IsDeleted)
                VALUES (@name, @categoryId, @trackingUnit, @packName, @packSize, 0, 1, 0);
                SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@name", item.Name);
            command.Parameters.AddWithValue("@categoryId", item.CategoryId.HasValue ? item.CategoryId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@trackingUnit", item.TrackingUnit);
            command.Parameters.AddWithValue("@packName", string.IsNullOrWhiteSpace(item.PackName) ? DBNull.Value : item.PackName);
            command.Parameters.AddWithValue("@packSize", item.PackSize.HasValue ? item.PackSize.Value : DBNull.Value);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        // Sets (or, with null, clears) the full bottle / pack of a main item, and the extra ml per bottle.
        public void UpdatePack(SqliteConnection connection, SqliteTransaction transaction, int id, string? packName, decimal? packSize, decimal? extraPerPack = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE StockItems SET PackName = @packName, PackSize = @packSize, ExtraPerPack = @extraPerPack WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@extraPerPack", packSize.HasValue && extraPerPack.HasValue ? extraPerPack.Value : DBNull.Value);
            command.Parameters.AddWithValue("@packName", packSize.HasValue && !string.IsNullOrWhiteSpace(packName) ? packName : DBNull.Value);
            command.Parameters.AddWithValue("@packSize", packSize.HasValue ? packSize.Value : DBNull.Value);
            command.ExecuteNonQuery();
        }

        // Sets (or, with null, clears) just the bottle / pack size. The extra-per-bottle setting is kept,
        // unless the size is removed (an extra needs a bottle size).
        public void UpdatePackSize(SqliteConnection connection, SqliteTransaction transaction, int id, string? packName, decimal? packSize)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                UPDATE StockItems
                SET PackName = @packName,
                    PackSize = @packSize,
                    ExtraPerPack = CASE WHEN @packSize IS NULL THEN NULL ELSE ExtraPerPack END
                WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@packName", packSize.HasValue && !string.IsNullOrWhiteSpace(packName) ? packName : DBNull.Value);
            command.Parameters.AddWithValue("@packSize", packSize.HasValue ? packSize.Value : DBNull.Value);
            command.ExecuteNonQuery();
        }

        public StockItem? GetById(int id)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT {ItemColumns}
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
            command.CommandText = $@"
                SELECT {ItemColumns}
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
            command.CommandText = $@"
                SELECT {ItemColumns}
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
                       (SELECT COUNT(*) FROM Products p WHERE p.StockItemId = si.Id AND p.IsDeleted = 0),
                       si.PackName, si.PackSize,
                       IFNULL((SELECT SUM(m.QuantityChange) FROM StockMovements m
                               WHERE m.StockItemId = si.Id AND m.MovementDate = @today
                                 AND m.MovementType IN ('Restock', 'InitialStock')), 0),
                       oc.CountedQuantity
                FROM StockItems si
                LEFT JOIN StockOpeningCounts oc ON oc.StockItemId = si.Id AND oc.CountDate = @today
                WHERE si.IsDeleted = 0
                ORDER BY si.Name;";
            command.Parameters.AddWithValue("@today", DateTime.Today.ToString("yyyy-MM-dd"));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new StockItemDisplay
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Unit = reader.GetString(2),
                    InStock = reader.GetDecimal(3),
                    LinkedProducts = reader.GetInt32(4),
                    PackName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    PackSize = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    ReceivedToday = reader.GetDecimal(7),
                    Counted = reader.IsDBNull(8) ? null : reader.GetDecimal(8)
                });
            }

            return items;
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
                IsActive = reader.GetInt32(5) == 1,
                PackName = reader.IsDBNull(6) ? null : reader.GetString(6),
                PackSize = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                ExtraPerPack = reader.IsDBNull(8) ? null : reader.GetDecimal(8)
            };
        }
    }
}
