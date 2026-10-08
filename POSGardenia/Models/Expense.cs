using System;

namespace POSGardenia.Models
{
    public class Expense
    {
        public int Id { get; set; }
        public string ExpenseDate { get; set; } = "";
        public string Description { get; set; } = "";

        // Shown in the Expenses table: normal expenses and stock purchases are told apart here.
        public string Type => IsStockPurchase ? "Stock purchase" : "Expense";

        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }

        // True for the money spent buying stock: shown with the day's expenses, not deducted from Net Sales.
        [System.ComponentModel.Browsable(false)]
        public bool IsStockPurchase { get; set; }
    }
}