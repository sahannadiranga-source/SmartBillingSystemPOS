using System.Windows.Media;

namespace POSGardenia.Models
{
    // The colours a category can give its product buttons on the POS screen.
    // "Automatic" (no colour chosen) = green for kitchen categories, blue for everything else.
    public static class PosColors
    {
        public const string Automatic = "Automatic";

        public static readonly string[] Names = { "Blue", "Green", "Yellow", "Light red", "Orange", "Purple" };

        public static string[] Choices => new[] { Automatic }.Concat(Names).ToArray();

        // Anything unknown (or blank) counts as Automatic.
        public static string? Normalize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return Names.FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static (Color Background, Color Text) Resolve(string? name, bool isKitchen)
        {
            var white = Colors.White;
            var dark = Color.FromRgb(0x0F, 0x17, 0x2A);

            switch (Normalize(name) ?? (isKitchen ? "Green" : "Blue"))
            {
                case "Green": return (Color.FromRgb(14, 165, 164), white);     // the existing kitchen colour
                case "Yellow": return (Color.FromRgb(0xFA, 0xCC, 0x15), dark);
                case "Light red": return (Color.FromRgb(0xF8, 0x71, 0x71), dark);
                case "Orange": return (Color.FromRgb(0xFB, 0x92, 0x3C), dark);
                case "Purple": return (Color.FromRgb(0x8B, 0x5C, 0xF6), white);
                default: return (Color.FromRgb(31, 111, 235), white);          // Blue: the existing colour
            }
        }
    }
}
