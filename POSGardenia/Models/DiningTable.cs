namespace POSGardenia.Models
{
    public class DiningTable
    {
        public int Id { get; set; }
        public string TableName { get; set; } = "";
        public bool IsActive { get; set; }

        public override string ToString()
        {
            return TableName;
        }
    }
}