using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class ExpenseRepository
    {
        public void Add(Expense expense)
        {
            try
            {
                if (expense == null)
                    throw new Exception("Expense is null.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                Add(connection, null, expense);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to add expense. " + ex.Message, ex);
            }
        }

        // Inside the caller's transaction (used when buying stock, so the stock and its expense save together).
        public void Add(SqliteConnection connection, SqliteTransaction? transaction, Expense expense)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO Expenses (ExpenseDate, Description, Amount, CreatedAt, IsStockPurchase)
                VALUES (@expenseDate, @description, @amount, @createdAt, @isStockPurchase);";

            command.Parameters.AddWithValue("@expenseDate", expense.ExpenseDate ?? "");
            command.Parameters.AddWithValue("@description", expense.Description ?? "");
            command.Parameters.AddWithValue("@amount", expense.Amount);
            command.Parameters.AddWithValue("@createdAt", expense.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@isStockPurchase", expense.IsStockPurchase ? 1 : 0);

            command.ExecuteNonQuery();
        }

        public List<Expense> GetByDate(string expenseDate)
        {
            try
            {
                var expenses = new List<Expense>();

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, ExpenseDate, Description, Amount, CreatedAt, IsStockPurchase
                    FROM Expenses
                    WHERE ExpenseDate = @expenseDate
                    ORDER BY IsStockPurchase, Id DESC;";

                command.Parameters.AddWithValue("@expenseDate", expenseDate);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    expenses.Add(new Expense
                    {
                        Id = reader.GetInt32(0),
                        ExpenseDate = reader.GetString(1),
                        Description = reader.GetString(2),
                        Amount = reader.GetDecimal(3),
                        CreatedAt = DateTime.Parse(reader.GetString(4)),
                        IsStockPurchase = reader.GetInt32(5) == 1
                    });
                }

                return expenses;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load expenses by date. " + ex.Message, ex);
            }
        }

        public decimal GetTotalByDate(string expenseDate)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT IFNULL(SUM(Amount), 0)
                    FROM Expenses
                    WHERE ExpenseDate = @expenseDate;";

                command.Parameters.AddWithValue("@expenseDate", expenseDate);

                var result = command.ExecuteScalar();
                return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get total expenses by date. " + ex.Message, ex);
            }
        }

        // The part of the day's expenses that is taken off Net Sales (everything except stock purchases).
        public decimal GetDeductibleTotalByDate(string expenseDate) => TotalByDate(expenseDate, stockPurchase: false);

        // Money spent buying stock that day: an expense, but not deducted from Net Sales.
        public decimal GetStockPurchaseTotalByDate(string expenseDate) => TotalByDate(expenseDate, stockPurchase: true);

        private decimal TotalByDate(string expenseDate, bool stockPurchase)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT IFNULL(SUM(Amount), 0)
                    FROM Expenses
                    WHERE ExpenseDate = @expenseDate AND IsStockPurchase = @flag;";

                command.Parameters.AddWithValue("@expenseDate", expenseDate);
                command.Parameters.AddWithValue("@flag", stockPurchase ? 1 : 0);

                return Convert.ToDecimal(command.ExecuteScalar());
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get total expenses by date. " + ex.Message, ex);
            }
        }

        public void Delete(int id)
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Expenses WHERE Id = @id";
                command.Parameters.AddWithValue("@id", id);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to delete expense. " + ex.Message, ex);
            }
        }

        public void Update(Expense expense)
        {
            try
            {
                if (expense == null)
                    throw new Exception("Expense is null.");

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            UPDATE Expenses
            SET Description = @description,
                Amount = @amount
            WHERE Id = @id;";

                command.Parameters.AddWithValue("@description", expense.Description ?? "");
                command.Parameters.AddWithValue("@amount", expense.Amount);
                command.Parameters.AddWithValue("@id", expense.Id);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to update expense. " + ex.Message, ex);
            }
        }
    }
}