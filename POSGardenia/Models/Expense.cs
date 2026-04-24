using System;

namespace POSGardenia.Models
{
    public class Expense
    {
        public int Id { get; set; }
        public string ExpenseDate { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}