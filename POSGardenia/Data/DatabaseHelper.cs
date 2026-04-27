using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace POSGardenia.Data
{
    public static class DatabaseHelper
    {
        private static readonly string DbFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "POSGardenia");

        private static readonly string DbPath =
            Path.Combine(DbFolder, "posgardenia.db");

        private static readonly string ConnectionString =
            $"Data Source={DbPath};Default Timeout=10;Foreign Keys=True;";

        public static string GetDatabasePath() => DbPath;

        public static SqliteConnection GetConnection()
        {
            return new SqliteConnection(ConnectionString);
        }

        public static void InitializeDatabase()
        {
            try
            {
                if (!Directory.Exists(DbFolder))
                    Directory.CreateDirectory(DbFolder);

                using var connection = GetConnection();
                connection.Open();

                using (var alterBills1 = connection.CreateCommand())
                {
                    alterBills1.CommandText = "ALTER TABLE Bills ADD COLUMN BillDate TEXT NULL;";
                    try { alterBills1.ExecuteNonQuery(); } catch { }
                }

                using (var alterBills2 = connection.CreateCommand())
                {
                    alterBills2.CommandText = "ALTER TABLE Bills ADD COLUMN DailyBillNumber INTEGER NULL;";
                    try { alterBills2.ExecuteNonQuery(); } catch { }
                }

                using (var pragmaCommand = connection.CreateCommand())
                {
                    pragmaCommand.CommandText = @"
                        PRAGMA journal_mode = WAL;
                        PRAGMA synchronous = NORMAL;
                        PRAGMA foreign_keys = ON;";
                    pragmaCommand.ExecuteNonQuery();
                }

                string createCategoriesTable = @"
                    CREATE TABLE IF NOT EXISTS Categories (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        IsActive INTEGER NOT NULL DEFAULT 1
                    );";

                string createProductsTable = @"
                    CREATE TABLE IF NOT EXISTS Products (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        CategoryId INTEGER NOT NULL,
                        SellingPrice REAL NOT NULL,
                        IsKitchenItem INTEGER NOT NULL DEFAULT 0,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
                    );";

                string createDiningTablesTable = @"
                    CREATE TABLE IF NOT EXISTS DiningTables (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TableName TEXT NOT NULL,
                        IsActive INTEGER NOT NULL DEFAULT 1
                    );";

                string createBillsTable = @"
                    CREATE TABLE IF NOT EXISTS Bills (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DiningTableId INTEGER NULL,
                        BillType TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        BillDate TEXT NULL,
                        DailyBillNumber INTEGER NULL,
                        FOREIGN KEY (DiningTableId) REFERENCES DiningTables(Id)
                    );";

                string createBillItemsTable = @"
                    CREATE TABLE IF NOT EXISTS BillItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId INTEGER NOT NULL,
                        ProductId INTEGER NOT NULL,
                        UnitPrice REAL NOT NULL,
                        Quantity REAL NOT NULL,
                        Status TEXT NOT NULL,
                        IsKitchenPrinted INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (BillId) REFERENCES Bills(Id),
                        FOREIGN KEY (ProductId) REFERENCES Products(Id)
                    );";

                string createPaymentsTable = @"
                    CREATE TABLE IF NOT EXISTS Payments (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId INTEGER NOT NULL,
                        PaymentMethod TEXT NOT NULL,
                        Amount REAL NOT NULL,
                        PaidAt TEXT NOT NULL,
                        FOREIGN KEY (BillId) REFERENCES Bills(Id)
                    );";

                string createExpensesTable = @"
                    CREATE TABLE IF NOT EXISTS Expenses (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ExpenseDate TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        Amount REAL NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );";

                using var command = connection.CreateCommand();

                command.CommandText = createCategoriesTable;
                command.ExecuteNonQuery();

                command.CommandText = createProductsTable;
                command.ExecuteNonQuery();

                command.CommandText = createDiningTablesTable;
                command.ExecuteNonQuery();

                command.CommandText = createBillsTable;
                command.ExecuteNonQuery();

                command.CommandText = createBillItemsTable;
                command.ExecuteNonQuery();

                command.CommandText = createPaymentsTable;
                command.ExecuteNonQuery();

                command.CommandText = createExpensesTable;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Database initialization failed: " + ex.Message, ex);
            }
        }
    }
}
