using System.Globalization;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Graphing;

/// <summary>Short number and point formatting for graph labels, cards and tables.</summary>
public static class GraphFormat
{
    /// <summary>The minus sign used in display text (U+2212).</summary>
    public const char Minus = '−';

    /// <summary>
    /// A compact decimal with at most <paramref name="significant"/> significant digits (trailing zeros removed).
    /// With <paramref name="unicodeMinus"/> negative numbers use the typographic minus sign.
    /// </summary>
    public static string Number(double v, int significant = 4, bool unicodeMinus = false)
    {
        if (double.IsNaN(v)) return "undefined";
        if (double.IsPositiveInfinity(v)) return "∞";
        if (double.IsNegativeInfinity(v)) return (unicodeMinus ? Minus.ToString() : "-") + "∞";
        if (v == 0 || Math.Abs(v) < 1e-14) return "0";
        double av = Math.Abs(v);
        string s;
        if (av >= 1e7 || av < 1e-4)
        {
            s = v.ToString("0.###e0", CultureInfo.InvariantCulture);
        }
        else
        {
            int intDigits = av >= 1 ? (int)Math.Floor(Math.Log10(av)) + 1 : 0;
            int decimals = Math.Max(0, significant - intDigits);
            if (av < 1) decimals = Math.Max(decimals, (int)Math.Ceiling(-Math.Log10(av)) + significant - 1);
            decimals = Math.Min(decimals, 12);
            s = Math.Round(v, decimals).ToString("F" + decimals, CultureInfo.InvariantCulture);
            if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
            if (s == "-0") s = "0";
        }
        return unicodeMinus ? s.Replace('-', Minus) : s;
    }

    /// <summary>"(x, y)" with compact numbers.</summary>
    public static string Point(double x, double y, int significant = 4, bool unicodeMinus = false) =>
        $"({Number(x, significant, unicodeMinus)}, {Number(y, significant, unicodeMinus)})";

    /// <summary>An exact expression for display (√, π, fractions) with the typographic minus sign.</summary>
    public static string Exact(Expr e) => ExprPrinter.ToText(e).Replace('-', Minus);

    /// <summary>Replaces ASCII hyphen-minus with the typographic minus sign.</summary>
    public static string Typographic(string s) => s.Replace('-', Minus);
}
