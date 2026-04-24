namespace POSGardenia.Models
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
