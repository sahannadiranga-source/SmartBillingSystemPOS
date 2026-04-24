using POSGardenia.Models;
using System;

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
    }
}