using POSGardenia.Models;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class ProductRepository
    {
        public void Add(Product product)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Products
                (Name, CategoryId, SellingPrice, IsKitchenItem, IsActive)
                VALUES
                (@name, @categoryId, @sellingPrice, @isKitchenItem, @isActive);";

            command.Parameters.AddWithValue("@name", product.Name);
            command.Parameters.AddWithValue("@categoryId", product.CategoryId);
            command.Parameters.AddWithValue("@sellingPrice", product.SellingPrice);
            command.Parameters.AddWithValue("@isKitchenItem", product.IsKitchenItem ? 1 : 0);
            command.Parameters.AddWithValue("@isActive", product.IsActive ? 1 : 0);

            command.ExecuteNonQuery();
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
        p.IsActive
    FROM Products p
    INNER JOIN Categories c ON p.CategoryId = c.Id
    WHERE p.IsActive = 1
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
                    IsActive = reader.GetInt32(5) == 1
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
        SELECT Id, Name, CategoryId, SellingPrice, IsKitchenItem, IsActive
        FROM Products
        WHERE IsActive = 1
        ORDER BY Name;";

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
                    IsActive = reader.GetInt32(5) == 1
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

        public void Update(Product product)
        {
            try
            {
                if (product == null)
                    throw new Exception("Product is null.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            UPDATE Products
            SET Name = @name,
                CategoryId = @categoryId,
                SellingPrice = @sellingPrice,
                IsKitchenItem = @isKitchenItem
            WHERE Id = @id;";

                command.Parameters.AddWithValue("@id", product.Id);
                command.Parameters.AddWithValue("@name", product.Name ?? "");
                command.Parameters.AddWithValue("@categoryId", product.CategoryId);
                command.Parameters.AddWithValue("@sellingPrice", product.SellingPrice);
                command.Parameters.AddWithValue("@isKitchenItem", product.IsKitchenItem ? 1 : 0);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to update product. " + ex.Message, ex);
            }
        }

    }
}