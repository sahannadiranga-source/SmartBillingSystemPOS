using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class ProductRepository
    {
        public void Add(Product product)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            Add(connection, null, product);
        }

        // Transaction-aware version so a stock item and its products can be created together.
        public int Add(SqliteConnection connection, SqliteTransaction? transaction, Product product)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO Products
                (Name, CategoryId, SellingPrice, IsKitchenItem, IsActive, StockItemId, UnitsPerSale)
                VALUES
                (@name, @categoryId, @sellingPrice, IFNULL((SELECT IsKitchenItem FROM Categories WHERE Id = @categoryId), 0), @isActive, @stockItemId, @unitsPerSale);
                SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@name", product.Name);
            command.Parameters.AddWithValue("@categoryId", product.CategoryId);
            command.Parameters.AddWithValue("@sellingPrice", product.SellingPrice);
            command.Parameters.AddWithValue("@isActive", product.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("@stockItemId", product.StockItemId.HasValue ? product.StockItemId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@unitsPerSale", product.UnitsPerSale.HasValue ? product.UnitsPerSale.Value : DBNull.Value);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        // Used to enforce name uniqueness among non-deleted products before Add/Update.
        public Product? GetByName(string name)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, CategoryId, SellingPrice, IsKitchenItem, IsActive
                FROM Products
                WHERE Name = @name AND IsDeleted = 0
                LIMIT 1;";
            command.Parameters.AddWithValue("@name", name);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            return new Product
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CategoryId = reader.GetInt32(2),
                SellingPrice = reader.GetDecimal(3),
                IsKitchenItem = reader.GetInt32(4) == 1,
                IsActive = reader.GetInt32(5) == 1
            };
        }

        public List<ProductDisplay> GetAllForDisplay()
        {
            var products = new List<ProductDisplay>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
    SELECT
        p.Id,
        p.Name,
        c.Name as CategoryName,
        p.SellingPrice,
        p.IsKitchenItem,
        p.IsActive,
        p.StockItemId,
        si.Name,
        p.UnitsPerSale
    FROM Products p
    INNER JOIN Categories c ON p.CategoryId = c.Id
    LEFT JOIN StockItems si ON si.Id = p.StockItemId
    WHERE p.IsDeleted = 0
    ORDER BY p.Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                products.Add(new ProductDisplay
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    CategoryName = reader.GetString(2),
                    SellingPrice = reader.GetDecimal(3),
                    IsKitchenItem = reader.GetInt32(4) == 1,
                    IsActive = reader.GetInt32(5) == 1,
                    StockItemId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    MainItem = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    UnitsPerSale = reader.IsDBNull(8) ? null : reader.GetDecimal(8)
                });
            }

            return products;
        }


        public List<Product> GetActiveProducts()
        {
            var products = new List<Product>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT p.Id, p.Name, p.CategoryId, p.SellingPrice, p.IsKitchenItem, p.IsActive,
               p.StockItemId, p.UnitsPerSale, si.Name, c.ButtonColor
        FROM Products p
        INNER JOIN Categories c ON p.CategoryId = c.Id
        LEFT JOIN StockItems si ON si.Id = p.StockItemId
        WHERE p.IsActive = 1
          AND p.IsDeleted = 0
          AND c.IsActive = 1
          AND c.IsDeleted = 0
        ORDER BY p.Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                products.Add(new Product
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    CategoryId = reader.GetInt32(2),
                    SellingPrice = reader.GetDecimal(3),
                    IsKitchenItem = reader.GetInt32(4) == 1,
                    IsActive = reader.GetInt32(5) == 1,
                    StockItemId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    UnitsPerSale = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                    MainItemName = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CategoryButtonColor = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }

            return products;
        }

        public void Deactivate(int productId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Products
        SET IsActive = 0
        WHERE Id = @id;";

            command.Parameters.AddWithValue("@id", productId);
            command.ExecuteNonQuery();
        }

        public void Reactivate(int productId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Products
        SET IsActive = 1
        WHERE Id = @id;";

            command.Parameters.AddWithValue("@id", productId);
            command.ExecuteNonQuery();
        }

        public void MarkDeleted(int productId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Products
        SET IsDeleted = 1,
            IsActive = 0
        WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", productId);
            command.ExecuteNonQuery();
        }

        public void Update(Product product)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            Update(connection, null, product);
        }

        public void Update(SqliteConnection connection, SqliteTransaction? transaction, Product product)
        {
            try
            {
                if (product == null)
                    throw new Exception("Product is null.");

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
            UPDATE Products
            SET Name = @name,
                CategoryId = @categoryId,
                SellingPrice = @sellingPrice,
                IsKitchenItem = IFNULL((SELECT IsKitchenItem FROM Categories WHERE Id = @categoryId), 0),
                StockItemId = @stockItemId,
                UnitsPerSale = @unitsPerSale
            WHERE Id = @id;";

                command.Parameters.AddWithValue("@id", product.Id);
                command.Parameters.AddWithValue("@name", product.Name ?? "");
                command.Parameters.AddWithValue("@categoryId", product.CategoryId);
                command.Parameters.AddWithValue("@sellingPrice", product.SellingPrice);
                command.Parameters.AddWithValue("@stockItemId", product.StockItemId.HasValue ? product.StockItemId.Value : DBNull.Value);
                command.Parameters.AddWithValue("@unitsPerSale", product.UnitsPerSale.HasValue ? product.UnitsPerSale.Value : DBNull.Value);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to update product. " + ex.Message, ex);
            }
        }

    }
}
