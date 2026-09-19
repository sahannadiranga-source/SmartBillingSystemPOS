using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class PaymentRepository
    {
        public void Add(Payment payment)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Payments (BillId, PaymentMethod, Amount, PaidAt)
                VALUES (@billId, @paymentMethod, @amount, @paidAt);";

            command.Parameters.AddWithValue("@billId", payment.BillId);
            command.Parameters.AddWithValue("@paymentMethod", payment.PaymentMethod);
            command.Parameters.AddWithValue("@amount", payment.Amount);
            command.Parameters.AddWithValue("@paidAt", payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss"));

            command.ExecuteNonQuery();
        }

        public decimal GetPaidTotalForBill(int billId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT IFNULL(SUM(Amount), 0) FROM Payments WHERE BillId = @billId;";
            command.Parameters.AddWithValue("@billId", billId);

            return Convert.ToDecimal(command.ExecuteScalar());
        }

        // Cumulative CASH and CARD received for one bill, across every payment made on it.
        public (decimal Cash, decimal Card) GetPaidByMethodForBill(int billId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            IFNULL(SUM(CASE WHEN PaymentMethod = 'CASH' THEN Amount ELSE 0 END), 0),
            IFNULL(SUM(CASE WHEN PaymentMethod = 'CARD' THEN Amount ELSE 0 END), 0)
        FROM Payments
        WHERE BillId = @billId;";
            command.Parameters.AddWithValue("@billId", billId);

            using var reader = command.ExecuteReader();
            reader.Read();
            return (reader.GetDecimal(0), reader.GetDecimal(1));
        }

        // Open bills that already have some payment but still owe a balance.
        public (int PartiallyPaidBills, decimal TotalDue) GetOutstandingSummary()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT COUNT(*), IFNULL(SUM(Total - Paid), 0)
        FROM (
            SELECT
                b.Id,
                IFNULL((SELECT SUM(bi.UnitPrice * bi.Quantity) FROM BillItems bi WHERE bi.BillId = b.Id AND bi.Status = 'ACTIVE'), 0) AS Total,
                (SELECT IFNULL(SUM(pay.Amount), 0) FROM Payments pay WHERE pay.BillId = b.Id) AS Paid
            FROM Bills b
            WHERE b.Status = 'OPEN'
        )
        WHERE Paid > 0 AND Total - Paid > 0.01;";

            using var reader = command.ExecuteReader();
            reader.Read();
            return (reader.GetInt32(0), reader.GetDecimal(1));
        }

        public decimal GetTodaySalesTotal()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT IFNULL(SUM(Amount), 0)
        FROM Payments
        WHERE date(PaidAt) = date('now', 'localtime');";

            var result = command.ExecuteScalar();
            return Convert.ToDecimal(result);
        }

        public int GetTodayPaidBillCount()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT COUNT(DISTINCT BillId)
        FROM Payments
        WHERE date(PaidAt) = date('now', 'localtime');";

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        public decimal GetSalesTotalByDateRange(string fromDate, string toDate)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT IFNULL(SUM(Amount), 0)
            FROM Payments
            WHERE date(PaidAt) >= date(@fromDate)
              AND date(PaidAt) <= date(@toDate);";

                command.Parameters.AddWithValue("@fromDate", fromDate);
                command.Parameters.AddWithValue("@toDate", toDate);

                var result = command.ExecuteScalar();
                return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get sales total by date range. " + ex.Message, ex);
            }
        }

        public int GetPaidBillCountByDateRange(string fromDate, string toDate)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT COUNT(DISTINCT BillId)
            FROM Payments
            WHERE date(PaidAt) >= date(@fromDate)
              AND date(PaidAt) <= date(@toDate);";

                command.Parameters.AddWithValue("@fromDate", fromDate);
                command.Parameters.AddWithValue("@toDate", toDate);

                var result = command.ExecuteScalar();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get paid bill count by date range. " + ex.Message, ex);
            }
        }

        public decimal GetSalesTotalBySingleDate(string reportDate)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT IFNULL(SUM(Amount), 0)
            FROM Payments
            WHERE date(PaidAt) = date(@reportDate);";

                command.Parameters.AddWithValue("@reportDate", reportDate);

                var result = command.ExecuteScalar();
                return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get sales total by date. " + ex.Message, ex);
            }
        }

        public int GetPaidBillCountBySingleDate(string reportDate)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT COUNT(DISTINCT BillId)
            FROM Payments
            WHERE date(PaidAt) = date(@reportDate);";

                command.Parameters.AddWithValue("@reportDate", reportDate);

                var result = command.ExecuteScalar();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get paid bill count by date. " + ex.Message, ex);
            }
        }

        public List<PaymentBreakdownReport> GetPaymentBreakdownBySingleDate(string reportDate)
        {
            try
            {
                var result = new List<PaymentBreakdownReport>();

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                PaymentMethod,
                IFNULL(SUM(Amount), 0) AS TotalAmount,
                COUNT(DISTINCT BillId) AS BillCount
            FROM Payments
            WHERE date(PaidAt) = date(@reportDate)
            GROUP BY PaymentMethod
            ORDER BY PaymentMethod;";

                command.Parameters.AddWithValue("@reportDate", reportDate);

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    result.Add(new PaymentBreakdownReport
                    {
                        PaymentMethod = reader.GetString(0),
                        TotalAmount = reader.GetDecimal(1),
                        BillCount = reader.GetInt32(2)
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load payment breakdown. " + ex.Message, ex);
            }
        }
    }
}