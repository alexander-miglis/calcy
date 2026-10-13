using System.Globalization;
using Calcy.Core.Printing;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>Number formatting for tiles, cards, Calcy input text, LaTeX and list cells. Always culture independent.</summary>
public static class NumberText
{
    /// <summary>Significant digits on the analysis tiles.</summary>
    public const int TileDigits = 6;

    /// <summary>Significant digits on cards and in copied text.</summary>
    public const int CardDigits = 10;

    /// <summary>A value for reading: <paramref name="digits"/> significant digits, ×10ⁿ exponents, "undefined" for NaN.</summary>
    public static string Display(double v, int digits = TileDigits)
    {
        if (double.IsNaN(v)) return "undefined";
        if (double.IsPositiveInfinity(v)) return "∞";
        if (double.IsNegativeInfinity(v)) return "-∞";
        return NumberFormatter.Format(v, new NumberFormatOptions { Digits = digits });
    }

    /// <summary>A value as plain ASCII text with e exponents (copied text, cells).</summary>
    public static string Plain(double v, int digits = CardDigits)
    {
        if (double.IsNaN(v)) return "undefined";
        if (double.IsPositiveInfinity(v)) return "inf";
        if (double.IsNegativeInfinity(v)) return "-inf";
        return NumberFormatter.Format(v, new NumberFormatOptions { Digits = digits, Unicode = false });
    }

    /// <summary>
    /// A value as Calcy input that reads back to the same double: shortest round trip digits, e exponents, infinities as
    /// inf. Negative values are wrapped in parentheses when <paramref name="wrapNegative"/> is set (for use inside a product).
    /// </summary>
    public static string Input(double v, bool wrapNegative = false)
    {
        if (double.IsNaN(v)) return "undefined";
        if (double.IsPositiveInfinity(v)) return "inf";
        if (double.IsNegativeInfinity(v)) return wrapNegative ? "(-inf)" : "-inf";
        if (v == 0) return "0";
        var s = v.ToString("R", CultureInfo.InvariantCulture);
        return wrapNegative && v < 0 ? "(" + s + ")" : s;
    }

    /// <summary>A rounded value as Calcy input (<paramref name="digits"/> significant digits), for readable USE text.</summary>
    public static string ShortInput(double v, int digits = CardDigits)
    {
        if (!double.IsFinite(v) || v == 0) return Input(v);
        var s = Plain(v, digits);
        return s;
    }

    /// <summary>A value in LaTeX: 1.5 \times 10^{-7} instead of 1.5×10⁻⁷.</summary>
    public static string Latex(double v, int digits = CardDigits)
    {
        if (double.IsNaN(v)) return "\\text{undefined}";
        if (double.IsPositiveInfinity(v)) return "\\infty";
        if (double.IsNegativeInfinity(v)) return "-\\infty";
        var s = Plain(v, digits);
        int e = s.IndexOf('e');
        if (e < 0) return s;
        return s[..e] + " \\times 10^{" + s[(e + 1)..] + "}";
    }

    /// <summary>Text for a list cell: up to 12 significant digits, no grouping, e exponents.</summary>
    public static string Cell(double v) => Plain(v, 12);

    /// <summary>A probability or p-value: fixed 4 to 6 decimals when readable, scientific when tiny.</summary>
    public static string Probability(double p, int digits = TileDigits) => Display(p, digits);

    /// <summary>Whole numbers without decimals ("10"), others as <see cref="Display"/>.</summary>
    public static string Count(double v) =>
        double.IsFinite(v) && Math.Abs(v) < 1e15 && v == Math.Round(v) ? ((long)v).ToString(CultureInfo.InvariantCulture) : Display(v);
}
