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

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Expenses (ExpenseDate, Description, Amount, CreatedAt)
                    VALUES (@expenseDate, @description, @amount, @createdAt);";

                command.Parameters.AddWithValue("@expenseDate", expense.ExpenseDate ?? "");
                command.Parameters.AddWithValue("@description", expense.Description ?? "");
                command.Parameters.AddWithValue("@amount", expense.Amount);
                command.Parameters.AddWithValue("@createdAt", expense.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to add expense. " + ex.Message, ex);
            }
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
                    SELECT Id, ExpenseDate, Description, Amount, CreatedAt
                    FROM Expenses
                    WHERE ExpenseDate = @expenseDate
                    ORDER BY Id DESC;";

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
                        CreatedAt = DateTime.Parse(reader.GetString(4))
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
    }
}