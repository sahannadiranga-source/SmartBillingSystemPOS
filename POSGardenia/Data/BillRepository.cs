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
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                string billDate = DateTime.Now.ToString("yyyy-MM-dd");
                int dailyBillNumber = GetNextDailyBillNumber(billDate);

                using var command = connection.CreateCommand();
                command.CommandText = @"
            INSERT INTO Bills (DiningTableId, BillType, Status, CreatedAt, BillDate, DailyBillNumber)
            VALUES (@diningTableId, @billType, @status, @createdAt, @billDate, @dailyBillNumber);
            SELECT last_insert_rowid();";

                if (bill.DiningTableId.HasValue)
                    command.Parameters.AddWithValue("@diningTableId", bill.DiningTableId.Value);
                else
                    command.Parameters.AddWithValue("@diningTableId", DBNull.Value);

                command.Parameters.AddWithValue("@billType", bill.BillType ?? "");
                command.Parameters.AddWithValue("@status", bill.Status ?? "");
                command.Parameters.AddWithValue("@createdAt", bill.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@billDate", billDate);
                command.Parameters.AddWithValue("@dailyBillNumber", dailyBillNumber);

                return Convert.ToInt32(command.ExecuteScalar());
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to create bill. " + ex.Message, ex);
            }
        }
        public void SettleBill(int billId)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Bills
        SET Status = 'PAID'
        WHERE Id = @billId
          AND Status = 'OPEN';";

            command.Parameters.AddWithValue("@billId", billId);
            command.ExecuteNonQuery();
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
        private int GetNextDailyBillNumber(string billDate)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
        SELECT IFNULL(MAX(DailyBillNumber), 0) + 1
        FROM Bills
        WHERE BillDate = @billDate;";

            command.Parameters.AddWithValue("@billDate", billDate);

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
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
    }

}