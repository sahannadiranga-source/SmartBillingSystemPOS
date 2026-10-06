using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace POSGardenia.Services
{
    public enum PdfAlign { Left, Right, Center }

    // A tiny PDF writer for text reports: A4 pages, the built-in Helvetica fonts (nothing to install or
    // embed), lines and filled boxes. No third-party package, works offline.
    // Only plain ASCII text is drawn; any other character is written as '?'.
    public class SimplePdf
    {
        public const double PageWidth = 595;
        public const double PageHeight = 842;

        private readonly List<StringBuilder> _pages = new();
        private StringBuilder _page = null!;

        private static readonly Dictionary<(bool, char), double> WidthCache = new();
        private static GlyphTypeface? _regular;
        private static GlyphTypeface? _bold;
        private static bool _fontsLoaded;

        public int PageCount => _pages.Count;

        public SimplePdf()
        {
            NewPage();
        }

        public void NewPage()
        {
            _page = new StringBuilder();
            _pages.Add(_page);
        }

        // Drawing continues on the given page (0-based); used to add "Page x of y" once the count is known.
        public void GoToPage(int index)
        {
            _page = _pages[index];
        }

        // All y values are measured from the TOP of the page, in points.

        public void Text(double x, double y, string text, double size, bool bold = false, PdfAlign align = PdfAlign.Left, string color = "000000")
        {
            text = Clean(text);
            if (text.Length == 0)
                return;

            double width = Measure(text, size, bold);
            if (align == PdfAlign.Right)
                x -= width;
            else if (align == PdfAlign.Center)
                x -= width / 2;

            // y is the top of the text; the PDF wants the baseline
            double baseline = PageHeight - y - size * 0.8;

            _page.Append(Rgb(color, false));
            _page.Append($"BT /{(bold ? "F2" : "F1")} {F(size)} Tf {F(x)} {F(baseline)} Td ({Escape(text)}) Tj ET\n");
        }

        public void Line(double x1, double y, double x2, double thickness = 0.5, string color = "BBBBBB")
        {
            _page.Append(Rgb(color, true));
            _page.Append($"{F(thickness)} w {F(x1)} {F(PageHeight - y)} m {F(x2)} {F(PageHeight - y)} l S\n");
        }

        public void Box(double x, double y, double width, double height, string fillColor)
        {
            _page.Append(Rgb(fillColor, false));
            _page.Append($"{F(x)} {F(PageHeight - y - height)} {F(width)} {F(height)} re f\n");
        }

        public static double Measure(string text, double size, bool bold = false)
        {
            text = Clean(text);
            LoadFonts();
            var font = bold ? _bold : _regular;

            double total = 0;
            foreach (char c in text)
            {
                if (!WidthCache.TryGetValue((bold, c), out double w))
                {
                    w = 0.55;   // fallback: average Helvetica character
                    if (font != null && font.CharacterToGlyphMap.TryGetValue(c, out ushort glyph))
                        w = font.AdvanceWidths[glyph];
                    WidthCache[(bold, c)] = w;
                }
                total += w;
            }

            return total * size;
        }

        // Shortens text with ".." so it fits in the given width.
        public static string Fit(string text, double size, bool bold, double maxWidth)
        {
            text = Clean(text);
            if (Measure(text, size, bold) <= maxWidth)
                return text;

            while (text.Length > 1 && Measure(text + "..", size, bold) > maxWidth)
                text = text.Substring(0, text.Length - 1);

            return text + "..";
        }

        public byte[] ToBytes()
        {
            var latin1 = Encoding.Latin1;
            using var ms = new MemoryStream();
            var offsets = new List<long>();

            void Write(string s)
            {
                var bytes = latin1.GetBytes(s);
                ms.Write(bytes, 0, bytes.Length);
            }

            void BeginObject(int number)
            {
                while (offsets.Count < number)
                    offsets.Add(0);
                offsets[number - 1] = ms.Position;
                Write($"{number} 0 obj\n");
            }

            // objects: 1 catalog, 2 pages, 3 regular font, 4 bold font, then (page, content) pairs
            Write("%PDF-1.4\n");

            BeginObject(1);
            Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            var kids = new StringBuilder();
            for (int i = 0; i < _pages.Count; i++)
                kids.Append($"{5 + i * 2} 0 R ");

            BeginObject(2);
            Write($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>\nendobj\n");

            BeginObject(3);
            Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

            BeginObject(4);
            Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

            for (int i = 0; i < _pages.Count; i++)
            {
                int pageObject = 5 + i * 2;
                int contentObject = pageObject + 1;
                string content = _pages[i].ToString();

                BeginObject(pageObject);
                Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] " +
                      $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObject} 0 R >>\nendobj\n");

                BeginObject(contentObject);
                Write($"<< /Length {latin1.GetByteCount(content)} >>\nstream\n");
                Write(content);
                Write("\nendstream\nendobj\n");
            }

            long xref = ms.Position;
            int count = offsets.Count + 1;
            Write($"xref\n0 {count}\n");
            Write("0000000000 65535 f \n");
            foreach (long offset in offsets)
                Write($"{offset:0000000000} 00000 n \n");
            Write($"trailer\n<< /Size {count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

            return ms.ToArray();
        }

        public void Save(string path)
        {
            // write next to the target and rename, so a half-written file never replaces a good one
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, ToBytes());
            File.Move(temp, path, overwrite: true);
        }

        private static void LoadFonts()
        {
            if (_fontsLoaded)
                return;

            _fontsLoaded = true;

            // Arial has the same character widths as Helvetica, so text measured here fits the PDF font.
            try
            {
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out _regular);
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal).TryGetGlyphTypeface(out _bold);
            }
            catch
            {
                _regular = null;
                _bold = null;
            }
        }

        private static string Clean(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if ((c >= 32 && c <= 126) || (c >= 160 && c <= 255))   // ASCII + Latin-1 letters such as e-acute
                    sb.Append(c);
                else if (c == '‘' || c == '’')
                    sb.Append('\'');
                else if (c == '“' || c == '”')
                    sb.Append('"');
                else if (c == '–' || c == '—')
                    sb.Append('-');
                else if (c == '\t' || c == '\r' || c == '\n')
                    sb.Append(' ');
                else
                    sb.Append('?');
            }

            return sb.ToString();
        }

        private static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        private static string F(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // "RRGGBB" as a PDF colour command (stroke or fill)
        private static string Rgb(string hex, bool stroke)
        {
            double r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255.0;
            double g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0;
            double b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;
            return $"{F(r)} {F(g)} {F(b)} {(stroke ? "RG" : "rg")}\n";
        }
    }
}
