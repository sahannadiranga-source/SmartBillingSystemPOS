namespace POSGardenia.Models
{
    public class OpenBillDisplay
    {
        public int Id { get; set; }
        public string BillType { get; set; } = "";
        public string TableName { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }

        public decimal DueAmount => System.Math.Max(0, TotalAmount - PaidAmount);
        public bool IsPartiallyPaid => PaidAmount > 0;

        public string BillDate { get; set; } = "";
        public int DailyBillNumber { get; set; }

        public string VisibleBillNumber
        {
            get
            {
                if (string.IsNullOrWhiteSpace(BillDate) || DailyBillNumber <= 0)
                    return $"#{Id}";

                var compactDate = BillDate.Replace("-", "");
                return $"{compactDate}-{DailyBillNumber:D3}";
            }
        }

        public string DisplayText
        {
            get
            {
                var tableText = string.IsNullOrWhiteSpace(TableName) ? "Quick Sale" : TableName;
                var text = $"Bill No: {VisibleBillNumber} | Table: {tableText} | Type: {BillType}";
                return IsPartiallyPaid ? $"{text} | Due: {DueAmount:F2}" : text;
            }
        }

        public string CardTitle
        {
            get
            {
                var tableText = string.IsNullOrWhiteSpace(TableName) ? "Quick Sale" : TableName;
                return tableText;
            }
        }

        public string CardSubTitle => $"Bill No: {VisibleBillNumber} • {BillType}";
    }
}