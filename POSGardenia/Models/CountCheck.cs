using System;
using System.Globalization;

namespace POSGardenia.Models
{
    public enum CountVerdict { None, FirstCount, Ok, OkExtra, Check, Missing }

    public class CountCheckResult
    {
        public CountVerdict Verdict { get; set; }
        public decimal Difference { get; set; }
        public string Text { get; set; } = "";

        // Row colour on the sheet: "" (none), "red" (missing), "amber" (check).
        public string Tone { get; set; } = "";
    }

    // Compares a physical count with what the books expect and says what the difference means.
    // The bottle size is never changed: the extra a bottle gives is only an ALLOWANCE for a small plus.
    public static class CountCheck
    {
        // Measuring by eye is never exact; less than this is "OK".
        public const decimal Tolerance = 0.5m;

        // counted / expected: in the stock unit. soldInWindow: sold since the previous count.
        // hasBaseline: false for the first count of an item (nothing earlier to compare with).
        // pricePerUnit / shotSize: price of the smallest serving per stock unit, and its size (e.g. 6.4 per ml, 25 ml).
        public static CountCheckResult Evaluate(
            decimal counted,
            decimal expected,
            bool hasBaseline,
            decimal soldInWindow,
            decimal? packSize,
            decimal? extraPerPack,
            string unit,
            decimal? pricePerUnit,
            decimal? shotSize)
        {
            decimal diff = counted - expected;
            var result = new CountCheckResult { Difference = diff };

            if (!hasBaseline)
            {
                result.Verdict = CountVerdict.FirstCount;
                result.Text = $"FIRST COUNT ({Signed(diff)} {unit} vs books)";
                return result;
            }

            if (Math.Abs(diff) < Tolerance)
            {
                result.Verdict = CountVerdict.Ok;
                result.Text = "OK";
                return result;
            }

            if (diff > 0)
            {
                decimal allowance = 0;
                if (extraPerPack is > 0 && packSize is > 0)
                    allowance = extraPerPack.Value * Math.Ceiling(soldInWindow / packSize.Value);

                if (diff <= allowance + Tolerance)
                {
                    result.Verdict = CountVerdict.OkExtra;
                    result.Text = $"OK: {Signed(diff)} {unit} extra" + ExtraDetail(diff, pricePerUnit, shotSize);
                    return result;
                }

                result.Verdict = CountVerdict.Check;
                result.Tone = "amber";
                result.Text = allowance > 0
                    ? $"CHECK: {Signed(diff)} {unit}, allowed {allowance:0.##}"
                    : $"CHECK: {Signed(diff)} {unit} more than expected";
                return result;
            }

            result.Verdict = CountVerdict.Missing;
            result.Tone = "red";
            result.Text = $"MISSING: {Signed(diff)} {unit}" +
                (pricePerUnit is > 0 ? $" (Rs {Math.Abs(diff) * pricePerUnit.Value:#,0.00})" : "");
            return result;
        }

        private static string ExtraDetail(decimal diff, decimal? pricePerUnit, decimal? shotSize)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (shotSize is > 0)
                parts.Add($"~{diff / shotSize.Value:0.#} shots");

            if (pricePerUnit is > 0)
                parts.Add($"Rs {diff * pricePerUnit.Value:#,0.00}");

            return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
        }

        private static string Signed(decimal value) =>
            value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
    }
}
