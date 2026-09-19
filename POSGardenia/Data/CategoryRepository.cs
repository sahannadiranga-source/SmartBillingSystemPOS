using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class CategoryRepository
    {
        // New categories are always added active; use Deactivate / Reactivate to change that.
        public void Add(string name, bool isKitchenItem)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Categories (Name, IsActive, IsKitchenItem)
                VALUES (@name, 1, @isKitchenItem);";
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@isKitchenItem", isKitchenItem ? 1 : 0);
            command.ExecuteNonQuery();
        }

        // Renames a category and sets its kitchen flag. The kitchen flag lives on the category:
        // every product in it copies it (Products.IsKitchenItem is kept in step, so the
        // kitchen-ticket queries keep working unchanged). One transaction: all or nothing.
        public void Update(int categoryId, string name, bool isKitchenItem)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE Categories SET Name = @name, IsKitchenItem = @flag WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", categoryId);
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@flag", isKitchenItem ? 1 : 0);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE Products SET IsKitchenItem = @flag WHERE CategoryId = @id;";
                command.Parameters.AddWithValue("@id", categoryId);
                command.Parameters.AddWithValue("@flag", isKitchenItem ? 1 : 0);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        // Used to enforce name uniqueness among non-deleted categories before Add/Update.
        public Category? GetByName(string name)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, IsActive, IsKitchenItem
                FROM Categories
                WHERE Name = @name AND IsDeleted = 0
                LIMIT 1;";
            command.Parameters.AddWithValue("@name", name);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        public List<Category> GetAll()
        {
            var categories = new List<Category>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, IsActive, IsKitchenItem FROM Categories WHERE IsDeleted = 0 ORDER BY Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
                categories.Add(Map(reader));

            return categories;
        }

        public List<Category> GetActiveCategories()
        {
            var categories = new List<Category>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, IsActive, IsKitchenItem
                FROM Categories
                WHERE IsActive = 1 AND IsDeleted = 0
                ORDER BY Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
                categories.Add(Map(reader));

            return categories;
        }

        public void Deactivate(int categoryId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Categories
        SET IsActive = 0
        WHERE Id = @id;";

            command.Parameters.AddWithValue("@id", categoryId);
            command.ExecuteNonQuery();
        }

        public void Reactivate(int categoryId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Categories
        SET IsActive = 1
        WHERE Id = @id;";

            command.Parameters.AddWithValue("@id", categoryId);
            command.ExecuteNonQuery();
        }

        public void MarkDeleted(int categoryId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Categories
        SET IsDeleted = 1,
            IsActive = 0
        WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", categoryId);
            command.ExecuteNonQuery();
        }

        private static Category Map(SqliteDataReader reader)
        {
            return new Category
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                IsActive = reader.GetInt32(2) == 1,
                IsKitchenItem = reader.GetInt32(3) == 1
            };
        }
    }
}
