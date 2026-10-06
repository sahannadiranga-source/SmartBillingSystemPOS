using System;
using System.Collections.Generic;

namespace POSGardenia.Models
{
    // Shows a stock quantity the way people count it: "10 bottles", "10 bottles + 250 ml", "2 packs + 5 units".
    // Needs the main item's full-pack size (e.g. 750 ml per bottle, 20 units per pack of cigarettes); blank when there is none.
    public static class PackFormatter
    {
        public static string Describe(decimal quantity, string unit, string? packName, decimal? packSize)
        {
            if (packSize is null || packSize.Value <= 0 || string.IsNullOrWhiteSpace(packName))
                return "";

            decimal size = packSize.Value;
            decimal abs = Math.Abs(quantity);
            decimal whole = Math.Floor(abs / size);
            decimal rest = abs - whole * size;

            var parts = new List<string>();
            if (whole > 0 || rest == 0)
                parts.Add($"{whole:0} {Plural(packName.Trim(), whole)}");
            if (rest > 0)
                parts.Add($"{rest:0.##} {Plural(unit, rest)}");

            string text = string.Join(" + ", parts);
            return quantity < 0 ? "-" + text : text;
        }

        // What a full pack is called, decided by what the item is counted in (never typed):
        // ml -> bottle (liquor), unit -> pack (e.g. cigarettes). Beer is counted in bottles already.
        public static string PackNameFor(string unit) =>
            string.Equals(unit?.Trim(), "unit", StringComparison.OrdinalIgnoreCase) ? "pack" : "bottle";

        // Beer and other items counted in bottles need no bottle / pack size.
        public static bool IsCountedInBottles(string unit) =>
            string.Equals(unit?.Trim(), "bottle", StringComparison.OrdinalIgnoreCase);

        // "ml" stays "ml"; "bottle" -> "bottles" unless the amount is exactly 1.
        public static string Plural(string word, decimal quantity)
        {
            if (quantity == 1 || word.Length <= 2 || word.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return word;

            return word + "s";
        }
    }
}
