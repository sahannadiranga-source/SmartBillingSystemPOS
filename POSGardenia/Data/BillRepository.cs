using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class BillRepository
    {
        public int Create(Bill bill)
        {
            try
            {
                if (bill == null)
                    throw new Exception("Bill is null.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                string billDate = GetBillDate(bill.CreatedAt);
                int dailyBillNumber = GetNextDailyBillNumber(connection, transaction, billDate);
                int billId = InsertBill(connection, transaction, bill, billDate, dailyBillNumber);

                transaction.Commit();
                return billId;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to create bill. " + ex.Message, ex);
            }
        }

        public (int BillId, decimal Total) CreateQuickSaleAndSettle(
            Bill bill,
            List<BillItem> billItems,
            string paymentMethod,
            DateTime paidAt)
        {
            try
            {
                if (bill == null)
                    throw new Exception("Bill is null.");

                if (billItems == null || billItems.Count == 0)
                    throw new Exception("No bill items to save.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                string billDate = GetBillDate(bill.CreatedAt);
                int dailyBillNumber = GetNextDailyBillNumber(connection, transaction, billDate);
                int billId = InsertBill(connection, transaction, bill, billDate, dailyBillNumber);

                foreach (var item in billItems)
                {
                    if (item == null)
                        continue;

                    item.BillId = billId;
                    InsertBillItem(connection, transaction, item);
                }

                decimal total = GetBillTotal(connection, transaction, billId);
                InsertPayment(connection, transaction, new Payment
                {
                    BillId = billId,
                    PaymentMethod = paymentMethod,
                    Amount = total,
                    PaidAt = paidAt
                });
                SettleBill(connection, transaction, billId);

                transaction.Commit();
                return (billId, total);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to create and settle quick sale. " + ex.Message, ex);
            }
        }

        public decimal AddItemsAndSettleBill(
            int billId,
            List<BillItem> newBillItems,
            string paymentMethod,
            DateTime paidAt)
        {
            try
            {
                if (billId <= 0)
                    throw new Exception("Invalid bill id.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                if (newBillItems != null)
                {
                    foreach (var item in newBillItems)
                    {
                        if (item == null)
                            continue;

                        item.BillId = billId;
                        InsertBillItem(connection, transaction, item);
                    }
                }

                decimal total = GetBillTotal(connection, transaction, billId);
                InsertPayment(connection, transaction, new Payment
                {
                    BillId = billId,
                    PaymentMethod = paymentMethod,
                    Amount = total,
                    PaidAt = paidAt
                });
                SettleBill(connection, transaction, billId);

                transaction.Commit();
                return total;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to settle bill. " + ex.Message, ex);
            }
        }

        public void SettleBill(int billId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            SettleBill(connection, transaction, billId);
            transaction.Commit();
        }

        public int GetOpenBillsCount()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT COUNT(*)
        FROM Bills
        WHERE Status = 'OPEN';";

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        public List<OpenBillDisplay> GetOpenBillsForDisplay()
        {
            var bills = new List<OpenBillDisplay>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT 
            b.Id,
            b.BillType,
            IFNULL(dt.TableName, 'Quick Sale') as TableName,
            b.CreatedAt,
            b.Status,
            IFNULL(SUM(
                CASE 
                    WHEN bi.Status = 'ACTIVE' THEN bi.UnitPrice * bi.Quantity
                    ELSE 0
                END
            ), 0) as TotalAmount,
            IFNULL(b.BillDate, '') as BillDate,
            IFNULL(b.DailyBillNumber, 0) as DailyBillNumber
        FROM Bills b
        LEFT JOIN DiningTables dt ON b.DiningTableId = dt.Id
        LEFT JOIN BillItems bi ON b.Id = bi.BillId
        WHERE b.Status = 'OPEN'
        GROUP BY b.Id, b.BillType, dt.TableName, b.CreatedAt, b.Status, b.BillDate, b.DailyBillNumber
        ORDER BY b.Id DESC;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                bills.Add(new OpenBillDisplay
                {
                    Id = reader.GetInt32(0),
                    BillType = reader.GetString(1),
                    TableName = reader.GetString(2),
                    CreatedAt = reader.GetString(3),
                    Status = reader.GetString(4),
                    TotalAmount = reader.GetDecimal(5),
                    BillDate = reader.GetString(6),
                    DailyBillNumber = reader.GetInt32(7)
                });
            }

            return bills;
        }

        public string GetVisibleBillNumber(int billId)
        {
            try
            {
                if (billId <= 0)
                    return $"#{billId}";

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
        SELECT IFNULL(BillDate, ''), IFNULL(DailyBillNumber, 0)
        FROM Bills
        WHERE Id = @billId;";

                command.Parameters.AddWithValue("@billId", billId);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return $"#{billId}";

                string billDate = reader.GetString(0);
                int dailyBillNumber = reader.GetInt32(1);

                if (string.IsNullOrWhiteSpace(billDate) || dailyBillNumber <= 0)
                    return $"#{billId}";

                return $"{billDate.Replace("-", "")}-{dailyBillNumber:D3}";
            }
            catch
            {
                return $"#{billId}";
            }
        }

        public void VoidBill(int billId)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            UPDATE Bills
            SET Status = 'VOID'
            WHERE Id = @id
              AND Status = 'OPEN';";

                command.Parameters.AddWithValue("@id", billId);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to void bill. " + ex.Message, ex);
            }
        }

        private int InsertBill(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Bill bill,
            string billDate,
            int dailyBillNumber)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
            INSERT INTO Bills (DiningTableId, BillType, Status, CreatedAt, BillDate, DailyBillNumber)
            VALUES (@diningTableId, @billType, @status, @createdAt, @billDate, @dailyBillNumber);
            SELECT last_insert_rowid();";

            if (bill.DiningTableId.HasValue)
                command.Parameters.AddWithValue("@diningTableId", bill.DiningTableId.Value);
            else
                command.Parameters.AddWithValue("@diningTableId", DBNull.Value);

            command.Parameters.AddWithValue("@billType", bill.BillType ?? "");
            command.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(bill.Status) ? "OPEN" : bill.Status);
            command.Parameters.AddWithValue("@createdAt", bill.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@billDate", billDate);
            command.Parameters.AddWithValue("@dailyBillNumber", dailyBillNumber);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        private void InsertBillItem(
            SqliteConnection connection,
            SqliteTransaction transaction,
            BillItem billItem)
        {
            if (billItem.ProductId <= 0 || billItem.Quantity <= 0)
                return;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
            INSERT INTO BillItems
            (BillId, ProductId, UnitPrice, Quantity, Status, IsKitchenPrinted)
            VALUES
            (@billId, @productId, @unitPrice, @quantity, @status, @isKitchenPrinted);";

            command.Parameters.AddWithValue("@billId", billItem.BillId);
            command.Parameters.AddWithValue("@productId", billItem.ProductId);
            command.Parameters.AddWithValue("@unitPrice", billItem.UnitPrice);
            command.Parameters.AddWithValue("@quantity", billItem.Quantity);
            command.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(billItem.Status) ? "ACTIVE" : billItem.Status);
            command.Parameters.AddWithValue("@isKitchenPrinted", billItem.IsKitchenPrinted ? 1 : 0);

            command.ExecuteNonQuery();
        }

        private void InsertPayment(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Payment payment)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO Payments (BillId, PaymentMethod, Amount, PaidAt)
                VALUES (@billId, @paymentMethod, @amount, @paidAt);";

            command.Parameters.AddWithValue("@billId", payment.BillId);
            command.Parameters.AddWithValue("@paymentMethod", payment.PaymentMethod ?? "");
            command.Parameters.AddWithValue("@amount", payment.Amount);
            command.Parameters.AddWithValue("@paidAt", payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss"));

            command.ExecuteNonQuery();
        }

        private decimal GetBillTotal(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT IFNULL(SUM(UnitPrice * Quantity), 0)
        FROM BillItems
        WHERE BillId = @billId
          AND Status = 'ACTIVE';";

            command.Parameters.AddWithValue("@billId", billId);

            var result = command.ExecuteScalar();
            return Convert.ToDecimal(result);
        }

        private void SettleBill(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        UPDATE Bills
        SET Status = 'PAID'
        WHERE Id = @billId
          AND Status = 'OPEN';";

            command.Parameters.AddWithValue("@billId", billId);

            if (command.ExecuteNonQuery() == 0)
                throw new Exception("Bill is not open or was not found.");
        }

        private int GetNextDailyBillNumber(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string billDate)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT IFNULL(MAX(DailyBillNumber), 0) + 1
        FROM Bills
        WHERE BillDate = @billDate;";

            command.Parameters.AddWithValue("@billDate", billDate);

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        private string GetBillDate(DateTime createdAt)
        {
            var date = createdAt == default ? DateTime.Now : createdAt;
            return date.ToString("yyyy-MM-dd");
        }

        public List<BillHistoryDisplay> GetBillHistoryByDate(string reportDate)
        {
            try
            {
                var bills = new List<BillHistoryDisplay>();

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                b.Id,
                b.BillType,
                IFNULL(dt.TableName, 'Quick Sale') AS TableName,
                b.Status,
                b.CreatedAt,
                IFNULL(b.BillDate, '') AS BillDate,
                IFNULL(b.DailyBillNumber, 0) AS DailyBillNumber,
                IFNULL(pay.PaymentMethod, '') AS PaymentMethod,
                IFNULL(pay.PaidAt, '') AS PaidAt,
                IFNULL(SUM(
                    CASE 
                        WHEN bi.Status = 'ACTIVE' THEN bi.UnitPrice * bi.Quantity
                        ELSE 0
                    END
                ), 0) AS TotalAmount
            FROM Bills b
            LEFT JOIN DiningTables dt ON b.DiningTableId = dt.Id
            LEFT JOIN BillItems bi ON b.Id = bi.BillId
            LEFT JOIN Payments pay ON b.Id = pay.BillId
            WHERE date(b.CreatedAt) = date(@reportDate)
               OR date(pay.PaidAt) = date(@reportDate)
            GROUP BY 
                b.Id, b.BillType, dt.TableName, b.Status, b.CreatedAt,
                b.BillDate, b.DailyBillNumber, pay.PaymentMethod, pay.PaidAt
            ORDER BY b.Id DESC;";

                command.Parameters.AddWithValue("@reportDate", reportDate);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string billDate = reader.GetString(5);
                    int dailyNo = reader.GetInt32(6);

                    string visibleBillNo = dailyNo > 0 && !string.IsNullOrWhiteSpace(billDate)
                        ? $"{billDate.Replace("-", "")}-{dailyNo:D3}"
                        : $"#{reader.GetInt32(0)}";

                    bills.Add(new BillHistoryDisplay
                    {
                        Id = reader.GetInt32(0),
                        BillType = reader.GetString(1),
                        TableName = reader.GetString(2),
                        Status = reader.GetString(3),
                        CreatedAt = reader.GetString(4),
                        VisibleBillNumber = visibleBillNo,
                        PaymentMethod = reader.GetString(7),
                        PaidAt = reader.GetString(8),
                        TotalAmount = reader.GetDecimal(9)
                    });
                }

                return bills;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load bill history by date. " + ex.Message, ex);
            }
        }

        public List<BillHistoryDisplay> SearchBillHistoryByVisibleBillNumber(string searchText)
        {
            try
            {
                var bills = new List<BillHistoryDisplay>();

                if (string.IsNullOrWhiteSpace(searchText))
                    return bills;

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                b.Id,
                b.BillType,
                IFNULL(dt.TableName, 'Quick Sale') AS TableName,
                b.Status,
                b.CreatedAt,
                IFNULL(b.BillDate, '') AS BillDate,
                IFNULL(b.DailyBillNumber, 0) AS DailyBillNumber,
                IFNULL(pay.PaymentMethod, '') AS PaymentMethod,
                IFNULL(pay.PaidAt, '') AS PaidAt,
                IFNULL(SUM(
                    CASE 
                        WHEN bi.Status = 'ACTIVE' THEN bi.UnitPrice * bi.Quantity
                        ELSE 0
                    END
                ), 0) AS TotalAmount
            FROM Bills b
            LEFT JOIN DiningTables dt ON b.DiningTableId = dt.Id
            LEFT JOIN BillItems bi ON b.Id = bi.BillId
            LEFT JOIN Payments pay ON b.Id = pay.BillId
            WHERE 
                (REPLACE(IFNULL(b.BillDate, ''), '-', '') || '-' || printf('%03d', IFNULL(b.DailyBillNumber, 0))) LIKE @search
                OR CAST(b.Id AS TEXT) LIKE @search
            GROUP BY 
                b.Id, b.BillType, dt.TableName, b.Status, b.CreatedAt,
                b.BillDate, b.DailyBillNumber, pay.PaymentMethod, pay.PaidAt
            ORDER BY b.Id DESC;";

                command.Parameters.AddWithValue("@search", "%" + searchText.Trim() + "%");

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string billDate = reader.GetString(5);
                    int dailyNo = reader.GetInt32(6);

                    string visibleBillNo = dailyNo > 0 && !string.IsNullOrWhiteSpace(billDate)
                        ? $"{billDate.Replace("-", "")}-{dailyNo:D3}"
                        : $"#{reader.GetInt32(0)}";

                    bills.Add(new BillHistoryDisplay
                    {
                        Id = reader.GetInt32(0),
                        BillType = reader.GetString(1),
                        TableName = reader.GetString(2),
                        Status = reader.GetString(3),
                        CreatedAt = reader.GetString(4),
                        VisibleBillNumber = visibleBillNo,
                        PaymentMethod = reader.GetString(7),
                        PaidAt = reader.GetString(8),
                        TotalAmount = reader.GetDecimal(9)
                    });
                }

                return bills;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to search bill history. " + ex.Message, ex);
            }
        }
    }
}
