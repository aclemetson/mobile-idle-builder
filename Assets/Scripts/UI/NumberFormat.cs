using System;
using System.Text;

namespace MobileIdleBuilder
{
    /// <summary>Pure number-to-text helpers for the HUD, so EditMode tests need no UI.</summary>
    public static class NumberFormat
    {
        /// <summary>
        /// Compact idle-game number.
        ///   0.05 - 9.9 (not whole) -> "2.5" (one decimal, for per-second rates)
        ///   &lt; 10 000            -> "9,999"
        ///   10 000 - 999 999    -> "12.3K"
        ///   1 M - 999 M         -> "1.23M"
        ///   1 B - 999 B         -> "1.23B"
        ///   1 T - 999 T         -> "1.23T"
        ///   >= 1 000 T          -> "1.23x10^15" in superscript digits
        /// Whole numbers match the old IdleReturnSubController.FormatLargeNumber output exactly.
        /// </summary>
        public static string Compact(double value)
        {
            if (double.IsNaN(value) || value < 0.05) return "0";

            if (value < 10d)
            {
                bool whole = Math.Abs(value - Math.Round(value)) < 1e-9;
                return whole ? Math.Round(value).ToString("N0") : value.ToString("F1");
            }

            if (value < 10_000d)
                return Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0");

            if (value < 1_000_000d)
                return $"{value / 1_000.0:F1}K";

            if (value < 1_000_000_000d)
                return $"{value / 1_000_000.0:F2}M";

            if (value < 1_000_000_000_000d)
                return $"{value / 1_000_000_000.0:F2}B";

            if (value < 1_000_000_000_000_000d)
                return $"{value / 1_000_000_000_000.0:F2}T";

            if (double.IsInfinity(value)) return "∞";

            int    exp  = (int)Math.Floor(Math.Log10(value));
            double coef = value / Math.Pow(10, exp);
            return $"{coef:F2}×10{ToSuperscript(exp)}";
        }

        /// <summary>Entropy amount with the currency glyph, e.g. "◈ 12.3K".</summary>
        public static string Entropy(double value) => "◈ " + Compact(value);

        private static string ToSuperscript(int n)
        {
            var sb = new StringBuilder();
            foreach (char c in n.ToString())
            {
                sb.Append(c switch
                {
                    '-' => '⁻',
                    '0' => '⁰',
                    '1' => '¹',
                    '2' => '²',
                    '3' => '³',
                    '4' => '⁴',
                    '5' => '⁵',
                    '6' => '⁶',
                    '7' => '⁷',
                    '8' => '⁸',
                    '9' => '⁹',
                    _   => c
                });
            }
            return sb.ToString();
        }
    }
}
