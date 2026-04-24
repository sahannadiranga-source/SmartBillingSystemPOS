using POSGardenia.Models;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class DiningTableRepository
    {
        public void Add(string tableName)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO DiningTables (TableName, IsActive)
                VALUES (@tableName, 1);";

            command.Parameters.AddWithValue("@tableName", tableName);
            command.ExecuteNonQuery();
        }

        public List<DiningTable> GetActiveTables()
        {
            var tables = new List<DiningTable>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, TableName, IsActive
                FROM DiningTables
                WHERE IsActive = 1
                ORDER BY TableName;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(new DiningTable
                {
                    Id = reader.GetInt32(0),
                    TableName = reader.GetString(1),
                    IsActive = reader.GetInt32(2) == 1
                });
            }

            return tables;
        }

        public void Deactivate(int tableId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE DiningTables
        SET IsActive = 0
        WHERE Id = @id;";

            command.Parameters.AddWithValue("@id", tableId);
            command.ExecuteNonQuery();
        }
    }
}