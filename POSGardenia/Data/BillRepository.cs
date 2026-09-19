using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using POSGardenia.Services;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class BillRepository
    {
        private readonly StockService _stockService = new();

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

        public (int BillId, decimal Total, decimal Paid, decimal Due) CreateQuickSaleAndPay(
            Bill bill,
            List<BillItem> billItems,
            string paymentMethod,
            decimal amount,
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
                var (paid, due) = ApplyPayment(connection, transaction, billId, total, paymentMethod, amount, paidAt);

                transaction.Commit();
                return (billId, total, paid, due);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to create quick sale and record payment. " + ex.Message, ex);
            }
        }

        public (decimal Total, decimal Paid, decimal Due) AddItemsAndPayBill(
            int billId,
            List<BillItem> newBillItems,
            string paymentMethod,
            decimal amount,
            DateTime paidAt)
        {
            try
            {
                if (billId <= 0)
                    throw new Exception("Invalid bill id.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                EnsureBillIsOpen(connection, transaction, billId);

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
                var (paid, due) = ApplyPayment(connection, transaction, billId, total, paymentMethod, amount, paidAt);

                transaction.Commit();
                return (total, paid, due);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to record payment. " + ex.Message, ex);
            }
        }

        // Records one payment against a bill. The bill only flips to PAID once the
        // payments received so far cover the total; otherwise it stays OPEN with a due balance.
        private (decimal Paid, decimal Due) ApplyPayment(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billId,
            decimal total,
            string paymentMethod,
            decimal amount,
            DateTime paidAt)
        {
            decimal alreadyPaid = GetPaidTotal(connection, transaction, billId);
            decimal dueBefore = total - alreadyPaid;
            amount = Math.Round(amount, 2);

            if (amount <= 0)
                throw new Exception("Payment amount must be greater than zero.");

            if (amount > dueBefore + 0.01m)
                throw new Exception($"Payment amount ({amount:F2}) is more than the amount due ({dueBefore:F2}).");

            InsertPayment(connection, transaction, new Payment
            {
                BillId = billId,
                PaymentMethod = paymentMethod,
                Amount = amount,
                PaidAt = paidAt
            });

            decimal paid = alreadyPaid + amount;
            decimal due = Math.Max(0, total - paid);

            if (due <= 0.01m)
            {
                SettleBill(connection, transaction, billId);
                due = 0;
            }

            return (paid, due);
        }

        private decimal GetPaidTotal(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT IFNULL(SUM(Amount), 0) FROM Payments WHERE BillId = @billId;";
            command.Parameters.AddWithValue("@billId", billId);

            return Convert.ToDecimal(command.ExecuteScalar());
        }

        private void EnsureBillIsOpen(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT Status FROM Bills WHERE Id = @billId;";
            command.Parameters.AddWithValue("@billId", billId);

            var status = command.ExecuteScalar() as string;
            if (status != "OPEN")
                throw new Exception("Bill is not open or was not found.");
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
            IFNULL(b.DailyBillNumber, 0) as DailyBillNumber,
            (SELECT IFNULL(SUM(pay.Amount), 0) FROM Payments pay WHERE pay.BillId = b.Id) as PaidAmount
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
                    DailyBillNumber = reader.GetInt32(7),
                    PaidAmount = reader.GetDecimal(8)
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

                using (var checkCommand = connection.CreateCommand())
                {
                    checkCommand.CommandText = "SELECT COUNT(*) FROM Payments WHERE BillId = @id;";
                    checkCommand.Parameters.AddWithValue("@id", billId);

                    if (Convert.ToInt32(checkCommand.ExecuteScalar()) > 0)
                        throw new Exception("Cannot void a bill that already has payments.");
                }

                using var transaction = connection.BeginTransaction();

                int voided;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
            UPDATE Bills
            SET Status = 'VOID'
            WHERE Id = @id
              AND Status = 'OPEN';";

                    command.Parameters.AddWithValue("@id", billId);
                    voided = command.ExecuteNonQuery();
                }

                // A voided bill never happened: put back the stock of every line on it.
                if (voided > 0)
                {
                    var itemIds = new List<int>();
                    using (var itemsCommand = connection.CreateCommand())
                    {
                        itemsCommand.Transaction = transaction;
                        itemsCommand.CommandText = "SELECT Id FROM BillItems WHERE BillId = @id;";
                        itemsCommand.Parameters.AddWithValue("@id", billId);

                        using var reader = itemsCommand.ExecuteReader();
                        while (reader.Read())
                            itemIds.Add(reader.GetInt32(0));
                    }

                    foreach (var itemId in itemIds)
                        _stockService.ReverseSale(connection, transaction, itemId, DateTime.Today);
                }

                transaction.Commit();
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

        // Saves the line and deducts its stock inside the caller's transaction.
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
            (@billId, @productId, @unitPrice, @quantity, @status, @isKitchenPrinted);
            SELECT last_insert_rowid();";

            string status = string.IsNullOrWhiteSpace(billItem.Status) ? "ACTIVE" : billItem.Status;

            command.Parameters.AddWithValue("@billId", billItem.BillId);
            command.Parameters.AddWithValue("@productId", billItem.ProductId);
            command.Parameters.AddWithValue("@unitPrice", billItem.UnitPrice);
            command.Parameters.AddWithValue("@quantity", billItem.Quantity);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@isKitchenPrinted", billItem.IsKitchenPrinted ? 1 : 0);

            int billItemId = Convert.ToInt32(command.ExecuteScalar());

            if (status == "ACTIVE")
                _stockService.RecordSale(connection, transaction, billItemId, billItem.ProductId, billItem.Quantity, DateTime.Today);
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

        private const string BillHistorySelectSql = @"
            SELECT 
                b.Id,
                b.BillType,
                IFNULL(dt.TableName, 'Quick Sale') AS TableName,
                b.Status,
                b.CreatedAt,
                IFNULL(b.BillDate, '') AS BillDate,
                IFNULL(b.DailyBillNumber, 0) AS DailyBillNumber,
                IFNULL((SELECT SUM(pay.Amount) FROM Payments pay WHERE pay.BillId = b.Id AND pay.PaymentMethod = 'CASH'), 0) AS CashAmount,
                IFNULL((SELECT SUM(pay.Amount) FROM Payments pay WHERE pay.BillId = b.Id AND pay.PaymentMethod = 'CARD'), 0) AS CardAmount,
                IFNULL((SELECT MAX(pay.PaidAt) FROM Payments pay WHERE pay.BillId = b.Id), '') AS PaidAt,
                IFNULL((SELECT SUM(bi.UnitPrice * bi.Quantity) FROM BillItems bi WHERE bi.BillId = b.Id AND bi.Status = 'ACTIVE'), 0) AS TotalAmount,
                IFNULL((SELECT SUM(pay.Amount) FROM Payments pay WHERE pay.BillId = b.Id), 0) AS PaidAmount
            FROM Bills b
            LEFT JOIN DiningTables dt ON b.DiningTableId = dt.Id ";

        // One row per bill. Payments and items are aggregated with subqueries (not joins),
        // so a bill with several payments is never duplicated or double-summed.
        private static BillHistoryDisplay MapBillHistory(SqliteDataReader reader)
        {
            string billDate = reader.GetString(5);
            int dailyNo = reader.GetInt32(6);

            string visibleBillNo = dailyNo > 0 && !string.IsNullOrWhiteSpace(billDate)
                ? $"{billDate.Replace("-", "")}-{dailyNo:D3}"
                : $"#{reader.GetInt32(0)}";

            string status = reader.GetString(3);
            decimal total = reader.GetDecimal(10);
            decimal paid = reader.GetDecimal(11);
            decimal due = status == "OPEN" ? Math.Max(0, total - paid) : 0;

            string paymentStatus;
            if (status == "VOID")
                paymentStatus = "VOID";
            else if (paid <= 0)
                paymentStatus = "Unpaid";
            else if (due > 0.01m)
                paymentStatus = "Partially Paid";
            else
                paymentStatus = "Paid";

            return new BillHistoryDisplay
            {
                Id = reader.GetInt32(0),
                BillType = reader.GetString(1),
                TableName = reader.GetString(2),
                Status = status,
                CreatedAt = reader.GetString(4),
                VisibleBillNumber = visibleBillNo,
                CashAmount = reader.GetDecimal(7),
                CardAmount = reader.GetDecimal(8),
                PaidAt = reader.GetString(9),
                TotalAmount = total,
                DueAmount = due,
                PaymentStatus = paymentStatus
            };
        }

        public List<BillHistoryDisplay> GetBillHistoryByDate(string reportDate)
        {
            try
            {
                var bills = new List<BillHistoryDisplay>();

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = BillHistorySelectSql + @"
            WHERE date(b.CreatedAt) = date(@reportDate)
               OR EXISTS (
                    SELECT 1 FROM Payments pay
                    WHERE pay.BillId = b.Id
                      AND date(pay.PaidAt) = date(@reportDate)
               )
            ORDER BY b.Id DESC;";

                command.Parameters.AddWithValue("@reportDate", reportDate);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    bills.Add(MapBillHistory(reader));

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
                command.CommandText = BillHistorySelectSql + @"
            WHERE 
                (REPLACE(IFNULL(b.BillDate, ''), '-', '') || '-' || printf('%03d', IFNULL(b.DailyBillNumber, 0))) LIKE @search
                OR CAST(b.Id AS TEXT) LIKE @search
            ORDER BY b.Id DESC;";

                command.Parameters.AddWithValue("@search", "%" + searchText.Trim() + "%");

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    bills.Add(MapBillHistory(reader));

                return bills;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to search bill history. " + ex.Message, ex);
            }
        }
    }
}
