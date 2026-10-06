using POSGardenia.Models;
using POSGardenia.Services;
using System.Collections.Generic;
using System;
namespace POSGardenia.Data
{
    public class BillItemRepository
    {
        private readonly StockService _stockService = new();

        // The line and its stock deduction are saved together, or not at all.
        public void Add(BillItem billItem)
        {
            try
            {
                if (billItem == null)
                    throw new Exception("Bill item is null.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
            INSERT INTO BillItems
            (BillId, ProductId, UnitPrice, Quantity, Status, IsKitchenPrinted)
            VALUES
            (@billId, @productId, @unitPrice, @quantity, @status, @isKitchenPrinted);
            SELECT last_insert_rowid();";

                    command.Parameters.AddWithValue("@billId", billItem.BillId);
                    command.Parameters.AddWithValue("@productId", billItem.ProductId);
                    command.Parameters.AddWithValue("@unitPrice", billItem.UnitPrice);
                    command.Parameters.AddWithValue("@quantity", billItem.Quantity);
                    command.Parameters.AddWithValue("@status", billItem.Status ?? "ACTIVE");
                    command.Parameters.AddWithValue("@isKitchenPrinted", billItem.IsKitchenPrinted ? 1 : 0);

                    int billItemId = Convert.ToInt32(command.ExecuteScalar());

                    if ((billItem.Status ?? "ACTIVE") == "ACTIVE")
                        _stockService.RecordSale(connection, transaction, billItemId, billItem.ProductId, billItem.Quantity, DateTime.Today);
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to add bill item. " + ex.Message, ex);
            }
        }

        public List<BillItemDisplay> GetByBillIdForDisplay(int billId)
        {
            var items = new List<BillItemDisplay>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT 
            bi.Id,
            p.Name,
            bi.UnitPrice,
            bi.Quantity,
            (bi.UnitPrice * bi.Quantity) as LineTotal,
            bi.Status,
            bi.IsKitchenPrinted
        FROM BillItems bi
        INNER JOIN Products p ON bi.ProductId = p.Id
        WHERE bi.BillId = @billId
        ORDER BY bi.Id;";

            command.Parameters.AddWithValue("@billId", billId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new BillItemDisplay
                {
                    Id = reader.GetInt32(0),
                    ProductName = reader.GetString(1),
                    UnitPrice = reader.GetDecimal(2),
                    Quantity = reader.GetDecimal(3),
                    LineTotal = reader.GetDecimal(4),
                    Status = reader.GetString(5),
                    IsKitchenPrinted = reader.GetInt32(6) == 1
                });
            }

            return items;
        }

        // ACTIVE-only variant used when loading an open bill into the POS cart, so the cart
        // total (and the default amount to pay) matches what the bill is actually charged.
        public List<BillItemDisplay> GetActiveByBillIdForDisplay(int billId)
        {
            var items = new List<BillItemDisplay>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            bi.Id,
            p.Name,
            bi.UnitPrice,
            bi.Quantity,
            (bi.UnitPrice * bi.Quantity) as LineTotal,
            bi.Status,
            bi.IsKitchenPrinted
        FROM BillItems bi
        INNER JOIN Products p ON bi.ProductId = p.Id
        WHERE bi.BillId = @billId
          AND bi.Status = 'ACTIVE'
        ORDER BY bi.Id;";

            command.Parameters.AddWithValue("@billId", billId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new BillItemDisplay
                {
                    Id = reader.GetInt32(0),
                    ProductName = reader.GetString(1),
                    UnitPrice = reader.GetDecimal(2),
                    Quantity = reader.GetDecimal(3),
                    LineTotal = reader.GetDecimal(4),
                    Status = reader.GetString(5),
                    IsKitchenPrinted = reader.GetInt32(6) == 1
                });
            }

            return items;
        }

        public decimal GetBillTotal(int billId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT IFNULL(SUM(UnitPrice * Quantity), 0)
        FROM BillItems
        WHERE BillId = @billId
          AND Status = 'ACTIVE';";

            command.Parameters.AddWithValue("@billId", billId);

            var result = command.ExecuteScalar();
            return Convert.ToDecimal(result);
        }

        // Item sales are grouped by STOCK item (a liquor's bottle and shots together, in ml).
        // Products that are not tracked in stock keep their own row. A bill counts on the day of its first payment.
        // dateFilter is a SQL condition on pay.FirstPaidAt.
        private List<ItemSalesReport> QueryItemSales(string dateFilter, Action<Microsoft.Data.Sqlite.SqliteCommand> addParameters)
        {
            var items = new List<ItemSalesReport>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT
                    CASE WHEN si.Id IS NOT NULL AND p.UnitsPerSale > 0 THEN si.Name ELSE p.Name END AS ItemName,
                    CASE WHEN si.Id IS NOT NULL AND p.UnitsPerSale > 0 THEN si.TrackingUnit ELSE '' END AS Unit,
                    IFNULL(SUM(CASE WHEN si.Id IS NOT NULL AND p.UnitsPerSale > 0
                                    THEN bi.Quantity * p.UnitsPerSale
                                    ELSE bi.Quantity END), 0) AS QuantitySold,
                    IFNULL(SUM(bi.UnitPrice * bi.Quantity), 0) AS TotalSales,
                    si.PackName,
                    si.PackSize
                FROM BillItems bi
                INNER JOIN Products p ON bi.ProductId = p.Id
                LEFT JOIN StockItems si ON si.Id = p.StockItemId AND si.IsDeleted = 0
                INNER JOIN (SELECT BillId, MIN(PaidAt) AS FirstPaidAt FROM Payments GROUP BY BillId) pay ON bi.BillId = pay.BillId
                WHERE bi.Status = 'ACTIVE'
                  AND {dateFilter}
                GROUP BY CASE WHEN si.Id IS NOT NULL AND p.UnitsPerSale > 0 THEN 'S' || si.Id ELSE 'P' || p.Id END
                ORDER BY TotalSales DESC;";

            addParameters(command);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new ItemSalesReport
                {
                    Item = reader.GetString(0),
                    Unit = reader.GetString(1),
                    QuantitySold = reader.GetDecimal(2),
                    TotalSales = reader.GetDecimal(3),
                    PackName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    PackSize = reader.IsDBNull(5) ? null : reader.GetDecimal(5)
                });
            }

            return items;
        }

        public List<ItemSalesReport> GetTodayItemSalesReport()
        {
            return QueryItemSales("date(pay.FirstPaidAt) = date('now', 'localtime')", _ => { });
        }

        // Kitchen items of a bill (active lines only). pendingOnly = just the ones not yet sent to the kitchen.
        public List<BillItemDisplay> GetPendingKitchenItemsByBillId(int billId, bool pendingOnly = true)
        {
            var items = new List<BillItemDisplay>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT 
            bi.Id,
            p.Name,
            bi.UnitPrice,
            bi.Quantity,
            (bi.UnitPrice * bi.Quantity) as LineTotal,
            bi.Status,
            bi.IsKitchenPrinted
        FROM BillItems bi
        INNER JOIN Products p ON bi.ProductId = p.Id
        WHERE bi.BillId = @billId
          AND bi.Status = 'ACTIVE'
          AND p.IsKitchenItem = 1
          AND (@pendingOnly = 0 OR bi.IsKitchenPrinted = 0)
        ORDER BY bi.Id;";

            command.Parameters.AddWithValue("@billId", billId);
            command.Parameters.AddWithValue("@pendingOnly", pendingOnly ? 1 : 0);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new BillItemDisplay
                {
                    Id = reader.GetInt32(0),
                    ProductName = reader.GetString(1),
                    UnitPrice = reader.GetDecimal(2),
                    Quantity = reader.GetDecimal(3),
                    LineTotal = reader.GetDecimal(4),
                    Status = reader.GetString(5),
                    IsKitchenPrinted = reader.GetInt32(6) == 1
                });
            }

            return items;
        }

        // True when this bill already sent kitchen items (so the next ticket is an add-on).
        public bool HasSentKitchenItems(int billId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT 1
        FROM BillItems bi
        INNER JOIN Products p ON bi.ProductId = p.Id
        WHERE bi.BillId = @billId
          AND bi.Status = 'ACTIVE'
          AND p.IsKitchenItem = 1
          AND bi.IsKitchenPrinted = 1
        LIMIT 1;";
            command.Parameters.AddWithValue("@billId", billId);

            return command.ExecuteScalar() != null;
        }

        // The next kitchen ticket number: 1 for the first ticket of the day, then 2, 3 ...
        // A number is only used up once its ticket is recorded as sent, so a failed print leaves no gap.
        public int GetNextKotNumber()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT IFNULL(MAX(KotNo), 0) FROM BillItems WHERE date(KotSentAt) = date(@today);";
            command.Parameters.AddWithValue("@today", DateTime.Now.ToString("yyyy-MM-dd"));

            return Convert.ToInt32(command.ExecuteScalar()) + 1;
        }

        // The ticket numbers this bill's kitchen items were sent on (for a reprint).
        public List<int> GetKotNumbersForBill(int billId)
        {
            var numbers = new List<int>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT DISTINCT KotNo FROM BillItems
        WHERE BillId = @billId AND Status = 'ACTIVE' AND KotNo IS NOT NULL
        ORDER BY KotNo;";
            command.Parameters.AddWithValue("@billId", billId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                numbers.Add(reader.GetInt32(0));

            return numbers;
        }

        // Marks exactly these bill lines as sent to the kitchen on ticket kotNo (only ever called after the ticket printed).
        public void MarkKitchenItemsSent(List<int> billItemIds, int kotNo)
        {
            if (billItemIds == null || billItemIds.Count == 0)
                return;

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            string sentAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (int id in billItemIds)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE BillItems SET IsKitchenPrinted = 1, KotSentAt = @sentAt, KotNo = @kotNo WHERE Id = @id AND IsKitchenPrinted = 0;";
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@kotNo", kotNo);
                command.Parameters.AddWithValue("@sentAt", sentAt);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public List<ItemSalesReport> GetItemSalesReportByDateRange(string fromDate, string toDate)
        {
            try
            {
                return QueryItemSales(
                    "date(pay.FirstPaidAt) >= date(@fromDate) AND date(pay.FirstPaidAt) <= date(@toDate)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@fromDate", fromDate);
                        command.Parameters.AddWithValue("@toDate", toDate);
                    });
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get item sales report by date range. " + ex.Message, ex);
            }
        }

        public List<ItemSalesReport> GetItemSalesReportBySingleDate(string reportDate)
        {
            try
            {
                return QueryItemSales(
                    "date(pay.FirstPaidAt) = date(@reportDate)",
                    command => command.Parameters.AddWithValue("@reportDate", reportDate));
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get item sales report by date. " + ex.Message, ex);
            }
        }

        public List<ReceiptLine> GetReceiptLinesByBillId(int billId)
        {
            try
            {
                var lines = new List<ReceiptLine>();

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                p.Name,
                bi.Quantity,
                bi.UnitPrice
            FROM BillItems bi
            INNER JOIN Products p ON bi.ProductId = p.Id
            WHERE bi.BillId = @billId
              AND bi.Status = 'ACTIVE'
            ORDER BY bi.Id;";

                command.Parameters.AddWithValue("@billId", billId);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    lines.Add(new ReceiptLine
                    {
                        ProductName = reader.GetString(0),
                        Quantity = reader.GetDecimal(1),
                        UnitPrice = reader.GetDecimal(2)
                    });
                }

                return lines;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load receipt lines. " + ex.Message, ex);
            }
        }

        public void CancelItem(int billItemId)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
            UPDATE BillItems
            SET Status = 'CANCELLED'
            WHERE Id = @id;";

                    command.Parameters.AddWithValue("@id", billItemId);
                    command.ExecuteNonQuery();
                }

                // Puts the stock back (does nothing for untracked products or an already-reversed line).
                _stockService.ReverseSale(connection, transaction, billItemId, DateTime.Today);

                transaction.Commit();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to cancel bill item. " + ex.Message, ex);
            }
        }

    }
}