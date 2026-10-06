namespace POSGardenia.Models
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }

        // Kitchen category: every product in it is a kitchen item (goes on the kitchen ticket).
        public bool IsKitchenItem { get; set; }

        // Colour of this category's product buttons on the POS ("Blue", "Yellow" ...); blank = automatic.
        public string? ButtonColor { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
