using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace POSGardenia.Data
{
    public static class DatabaseHelper
    {
        // POSGARDENIA_DATA_DIR lets tests and scripts point at a throwaway database folder
        // instead of the real %LocalAppData%\POSGardenia one. Unset in normal use.
        private static readonly string DbFolder =
            Environment.GetEnvironmentVariable("POSGARDENIA_DATA_DIR") is { Length: > 0 } overrideDir
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "POSGardenia");

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

                // IsDeleted is separate from IsActive: a deactivated row is hidden from
                // POS/pickers but still shown (unticked) in Management for reactivation.
                // A deleted row is hidden everywhere, but the row itself is kept forever
                // so bill history that references it never breaks a foreign key.
                using (var alterCategories1 = connection.CreateCommand())
                {
                    alterCategories1.CommandText = "ALTER TABLE Categories ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    try { alterCategories1.ExecuteNonQuery(); } catch { }
                }

                using (var alterProducts1 = connection.CreateCommand())
                {
                    alterProducts1.CommandText = "ALTER TABLE Products ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    try { alterProducts1.ExecuteNonQuery(); } catch { }
                }

                using (var alterDiningTables1 = connection.CreateCommand())
                {
                    alterDiningTables1.CommandText = "ALTER TABLE DiningTables ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    try { alterDiningTables1.ExecuteNonQuery(); } catch { }
                }

                // Inventory: link a sellable product to the physical stock it consumes.
                // StockItemId NULL = product is not stock-tracked (unchanged behaviour).
                using (var alterProducts2 = connection.CreateCommand())
                {
                    alterProducts2.CommandText = "ALTER TABLE Products ADD COLUMN StockItemId INTEGER NULL REFERENCES StockItems(Id);";
                    try { alterProducts2.ExecuteNonQuery(); } catch { }
                }

                using (var alterProducts3 = connection.CreateCommand())
                {
                    alterProducts3.CommandText = "ALTER TABLE Products ADD COLUMN UnitsPerSale REAL NULL;";
                    try { alterProducts3.ExecuteNonQuery(); } catch { }
                }

                // An earlier inventory attempt left StockItems in some databases without this column.
                // Bring it up to date (no-op on fresh installs, where CREATE TABLE already has it).
                using (var alterStockItems1 = connection.CreateCommand())
                {
                    alterStockItems1.CommandText = "ALTER TABLE StockItems ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    try { alterStockItems1.ExecuteNonQuery(); } catch { }
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

                string createStockItemsTable = @"
                    CREATE TABLE IF NOT EXISTS StockItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        CategoryId INTEGER NULL,
                        TrackingUnit TEXT NOT NULL,
                        PackName TEXT NULL,
                        PackSize REAL NULL,
                        CurrentQuantity REAL NOT NULL DEFAULT 0,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        IsDeleted INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
                    );";

                string createStockMovementsTable = @"
                    CREATE TABLE IF NOT EXISTS StockMovements (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        StockItemId INTEGER NOT NULL,
                        MovementDate TEXT NOT NULL,
                        MovementType TEXT NOT NULL,
                        QuantityChange REAL NOT NULL,
                        BillItemId INTEGER NULL,
                        Note TEXT NULL,
                        CreatedAt TEXT NOT NULL,
                        FOREIGN KEY (StockItemId) REFERENCES StockItems(Id),
                        FOREIGN KEY (BillItemId) REFERENCES BillItems(Id)
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

                command.CommandText = createStockItemsTable;
                command.ExecuteNonQuery();

                command.CommandText = createStockMovementsTable;
                command.ExecuteNonQuery();

                // Kitchen tickets: when a bill item was sent to the kitchen. Nothing was ever sent before this
                // column existed, so on first run every existing item counts as already handled; otherwise
                // the first ticket on any open bill would print its old items again.
                bool addedKotColumn = false;
                using (var alterKot = connection.CreateCommand())
                {
                    alterKot.CommandText = "ALTER TABLE BillItems ADD COLUMN KotSentAt TEXT NULL;";
                    try { alterKot.ExecuteNonQuery(); addedKotColumn = true; } catch { }
                }

                // The kitchen ticket number (1, 2, 3 ... restarting each day) the item was sent on.
                using (var alterKotNo = connection.CreateCommand())
                {
                    alterKotNo.CommandText = "ALTER TABLE BillItems ADD COLUMN KotNo INTEGER NULL;";
                    try { alterKotNo.ExecuteNonQuery(); } catch { }
                }

                if (addedKotColumn)
                {
                    using var markOld = connection.CreateCommand();
                    markOld.CommandText = "UPDATE BillItems SET IsKitchenPrinted = 1 WHERE IsKitchenPrinted = 0;";
                    try { markOld.ExecuteNonQuery(); } catch { }
                }

                // The kitchen flag moved from the product to its category. When the column is first
                // added, a category becomes a kitchen category if any of its products was a kitchen
                // item, and its products then follow the category (so nothing is missed on the
                // kitchen ticket). Products.IsKitchenItem stays as the copy the kitchen queries read.
                bool addedKitchenColumn = false;
                using (var alterCategoriesKitchen = connection.CreateCommand())
                {
                    alterCategoriesKitchen.CommandText = "ALTER TABLE Categories ADD COLUMN IsKitchenItem INTEGER NOT NULL DEFAULT 0;";
                    try { alterCategoriesKitchen.ExecuteNonQuery(); addedKitchenColumn = true; } catch { }
                }

                if (addedKitchenColumn)
                {
                    using var syncKitchen = connection.CreateCommand();
                    syncKitchen.CommandText = @"
                        UPDATE Categories SET IsKitchenItem = 1
                        WHERE Id IN (SELECT CategoryId FROM Products WHERE IsKitchenItem = 1);
                        UPDATE Products SET IsKitchenItem =
                            IFNULL((SELECT c.IsKitchenItem FROM Categories c WHERE c.Id = Products.CategoryId), 0);";
                    try { syncKitchen.ExecuteNonQuery(); } catch { }
                }

                // Colour of a category's product buttons on the POS. When the column is first added, beer
                // categories start yellow and cigarette categories light red; the owner can change any
                // category's colour on the Categories page. Blank = automatic (kitchen green, otherwise blue).
                bool addedColorColumn = false;
                using (var alterCategoriesColor = connection.CreateCommand())
                {
                    alterCategoriesColor.CommandText = "ALTER TABLE Categories ADD COLUMN ButtonColor TEXT NULL;";
                    try { alterCategoriesColor.ExecuteNonQuery(); addedColorColumn = true; } catch { }
                }

                if (addedColorColumn)
                {
                    using var presetColors = connection.CreateCommand();
                    presetColors.CommandText = @"
                        UPDATE Categories SET ButtonColor = 'Yellow' WHERE IsKitchenItem = 0 AND LOWER(Name) LIKE '%beer%';
                        UPDATE Categories SET ButtonColor = 'Light red' WHERE IsKitchenItem = 0 AND (LOWER(Name) LIKE '%cig%' OR LOWER(Name) LIKE '%tobacco%');";
                    try { presetColors.ExecuteNonQuery(); } catch { }
                }

                // A main item can have a bottle / pack size (750 ml, 20 units) so stock can be shown as
                // bottles / packs. It is only ever set by the owner (Inventory > Daily Stock); nothing is guessed.
                using (var alterPack1 = connection.CreateCommand())
                {
                    alterPack1.CommandText = "ALTER TABLE StockItems ADD COLUMN PackName TEXT NULL;";
                    try { alterPack1.ExecuteNonQuery(); } catch { }
                }

                using (var alterPack2 = connection.CreateCommand())
                {
                    alterPack2.CommandText = "ALTER TABLE StockItems ADD COLUMN PackSize REAL NULL;";
                    try { alterPack2.ExecuteNonQuery(); } catch { }
                }

                // Enforce name uniqueness among non-deleted rows only (partial index), so a
                // soft-deleted category/product never blocks reusing its name for a new one.
                // Wrapped/swallowed like the ALTER TABLE calls above: if existing data still
                // has duplicate names (e.g. from before this constraint existed), creating the
                // index fails harmlessly here and takes effect once those duplicates are cleaned up.
                using (var indexCategories = connection.CreateCommand())
                {
                    indexCategories.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_Name_NotDeleted ON Categories(Name) WHERE IsDeleted = 0;";
                    try { indexCategories.ExecuteNonQuery(); } catch { }
                }

                using (var indexProducts = connection.CreateCommand())
                {
                    indexProducts.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Products_Name_NotDeleted ON Products(Name) WHERE IsDeleted = 0;";
                    try { indexProducts.ExecuteNonQuery(); } catch { }
                }

                using (var indexPayments = connection.CreateCommand())
                {
                    indexPayments.CommandText = "CREATE INDEX IF NOT EXISTS IX_Payments_BillId ON Payments(BillId);";
                    try { indexPayments.ExecuteNonQuery(); } catch { }
                }

                using (var indexMovementsItemDate = connection.CreateCommand())
                {
                    indexMovementsItemDate.CommandText = "CREATE INDEX IF NOT EXISTS IX_StockMovements_Item_Date ON StockMovements(StockItemId, MovementDate);";
                    try { indexMovementsItemDate.ExecuteNonQuery(); } catch { }
                }

                using (var indexMovementsBillItem = connection.CreateCommand())
                {
                    indexMovementsBillItem.CommandText = "CREATE INDEX IF NOT EXISTS IX_StockMovements_BillItemId ON StockMovements(BillItemId);";
                    try { indexMovementsBillItem.ExecuteNonQuery(); } catch { }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Database initialization failed: " + ex.Message, ex);
            }
        }
    }
}
