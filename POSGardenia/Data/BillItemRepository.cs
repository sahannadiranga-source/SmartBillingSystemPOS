using POSGardenia.Models;
using System.Collections.Generic;
using System;
namespace POSGardenia.Data
{
    public class BillItemRepository
    {
        public void Add(BillItem billItem)
        {
            try
            {
                if (billItem == null)
                    throw new Exception("Bill item is null.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            INSERT INTO BillItems
            (BillId, ProductId, UnitPrice, Quantity, Status, IsKitchenPrinted)
            VALUES
            (@billId, @productId, @unitPrice, @quantity, @status, @isKitchenPrinted);";

                command.Parameters.AddWithValue("@billId", billItem.BillId);
                command.Parameters.AddWithValue("@productId", billItem.ProductId);
                command.Parameters.AddWithValue("@unitPrice", billItem.UnitPrice);
                command.Parameters.AddWithValue("@quantity", billItem.Quantity);
                command.Parameters.AddWithValue("@status", billItem.Status ?? "ACTIVE");
                command.Parameters.AddWithValue("@isKitchenPrinted", billItem.IsKitchenPrinted ? 1 : 0);

                command.ExecuteNonQuery();
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

        public List<ItemSalesReport> GetTodayItemSalesReport()
        {
            var items = new List<ItemSalesReport>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT 
            p.Name,
            IFNULL(SUM(bi.Quantity), 0) as QuantitySold,
            IFNULL(SUM(bi.UnitPrice * bi.Quantity), 0) as TotalSales
        FROM BillItems bi
        INNER JOIN Products p ON bi.ProductId = p.Id
        INNER JOIN Payments pay ON bi.BillId = pay.BillId
        WHERE bi.Status = 'ACTIVE'
          AND date(pay.PaidAt) = date('now', 'localtime')
        GROUP BY p.Name
        ORDER BY TotalSales DESC;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new ItemSalesReport
                {
                    ProductName = reader.GetString(0),
                    QuantitySold = reader.GetDecimal(1),
                    TotalSales = reader.GetDecimal(2)
                });
            }

            return items;
        }

        public List<BillItemDisplay> GetPendingKitchenItemsByBillId(int billId)
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
          AND bi.IsKitchenPrinted = 0
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

        public void MarkKitchenItemsAsPrinted(int billId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE BillItems
        SET IsKitchenPrinted = 1
        WHERE BillId = @billId
          AND Status = 'ACTIVE'
          AND IsKitchenPrinted = 0
          AND ProductId IN (
              SELECT Id
              FROM Products
              WHERE IsKitchenItem = 1
          );";

            command.Parameters.AddWithValue("@billId", billId);
            command.ExecuteNonQuery();
        }


        public void MarkSpecificKitchenItemsAsPrinted(int billId, List<int> productIds)
        {
            try
            {
                if (billId <= 0)
                    throw new Exception("Invalid bill id.");

                if (productIds == null || productIds.Count == 0)
                    return;

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                var placeholders = new List<string>();
                using var command = connection.CreateCommand();

                command.Parameters.AddWithValue("@billId", billId);

                for (int i = 0; i < productIds.Count; i++)
                {
                    string paramName = $"@p{i}";
                    placeholders.Add(paramName);
                    command.Parameters.AddWithValue(paramName, productIds[i]);
                }

                command.CommandText = $@"
            UPDATE BillItems
            SET IsKitchenPrinted = 1
            WHERE BillId = @billId
              AND Status = 'ACTIVE'
              AND IsKitchenPrinted = 0
              AND ProductId IN ({string.Join(",", placeholders)})
              AND ProductId IN (
                  SELECT Id
                  FROM Products
                  WHERE IsKitchenItem = 1
              );";

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to mark specific kitchen items as printed. " + ex.Message, ex);
            }
        }

        public List<ItemSalesReport> GetItemSalesReportByDateRange(string fromDate, string toDate)
        {
            try
            {
                var items = new List<ItemSalesReport>();

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                p.Name,
                IFNULL(SUM(bi.Quantity), 0) as QuantitySold,
                IFNULL(SUM(bi.UnitPrice * bi.Quantity), 0) as TotalSales
            FROM BillItems bi
            INNER JOIN Products p ON bi.ProductId = p.Id
            INNER JOIN Payments pay ON bi.BillId = pay.BillId
            WHERE bi.Status = 'ACTIVE'
              AND date(pay.PaidAt) >= date(@fromDate)
              AND date(pay.PaidAt) <= date(@toDate)
            GROUP BY p.Name
            ORDER BY TotalSales DESC;";

                command.Parameters.AddWithValue("@fromDate", fromDate);
                command.Parameters.AddWithValue("@toDate", toDate);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new ItemSalesReport
                    {
                        ProductName = reader.GetString(0),
                        QuantitySold = reader.GetDecimal(1),
                        TotalSales = reader.GetDecimal(2)
                    });
                }

                return items;
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
                var items = new List<ItemSalesReport>();

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                p.Name,
                IFNULL(SUM(bi.Quantity), 0) as QuantitySold,
                IFNULL(SUM(bi.UnitPrice * bi.Quantity), 0) as TotalSales
            FROM BillItems bi
            INNER JOIN Products p ON bi.ProductId = p.Id
            INNER JOIN Payments pay ON bi.BillId = pay.BillId
            WHERE bi.Status = 'ACTIVE'
              AND date(pay.PaidAt) = date(@reportDate)
            GROUP BY p.Name
            ORDER BY TotalSales DESC;";

                command.Parameters.AddWithValue("@reportDate", reportDate);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new ItemSalesReport
                    {
                        ProductName = reader.GetString(0),
                        QuantitySold = reader.GetDecimal(1),
                        TotalSales = reader.GetDecimal(2)
                    });
                }

                return items;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get item sales report by date. " + ex.Message, ex);
            }
        }



    }
}