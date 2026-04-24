using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace POSGardenia.Data
{
    public class CategoryRepository
    {
        public void Add(string name)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Categories (Name, IsActive)
                VALUES (@name, 1);";
            command.Parameters.AddWithValue("@name", name);
            command.ExecuteNonQuery();
        }

        public List<Category> GetAll()
        {
            var categories = new List<Category>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, IsActive FROM Categories ORDER BY Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(new Category
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    IsActive = reader.GetInt32(2) == 1
                });
            }

            return categories;
        }

        public List<Category> GetActiveCategories()
        {
            var categories = new List<Category>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT Id, Name, IsActive
        FROM Categories
        WHERE IsActive = 1
        ORDER BY Name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(new Category
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    IsActive = reader.GetInt32(2) == 1
                });
            }

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
    }
}
