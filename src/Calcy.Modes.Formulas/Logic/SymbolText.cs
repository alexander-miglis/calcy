using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Calcy.Core.Printing;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>Plain-text forms of symbols, units and numbers for labels, cards and the clipboard.</summary>
public static class SymbolText
{
    private static readonly Regex ExponentZeros = new(@"e(-?)0+(\d)", RegexOptions.CultureInvariant);

    private const string SubscriptFrom = "0123456789aehijklmnoprstuvx+-=()";
    private const string SubscriptTo = "₀₁₂₃₄₅₆₇₈₉ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓ₊₋₌₍₎";

    /// <summary>
    /// A formula symbol as readers write it: <c>v_0</c> becomes v₀ and <c>E_k</c> becomes Eₖ. Subscripts without a
    /// Unicode form stay as typed (<c>k_B</c>), and <c>h_bar</c> becomes ħ.
    /// </summary>
    public static string Plain(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        if (symbol == "h_bar") return "ħ";
        if (symbol == "pi") return "π";
        int u = symbol.IndexOf('_');
        if (u <= 0 || u == symbol.Length - 1) return symbol;
        var sub = symbol[(u + 1)..];
        var sb = new StringBuilder(symbol[..u]);
        foreach (char ch in sub)
        {
            int i = SubscriptFrom.IndexOf(ch);
            if (i < 0) return symbol;
            sb.Append(SubscriptTo[i]);
        }
        return sb.ToString();
    }

    /// <summary>A number followed by its unit: "2.5 m/s²", "30°", or just the number for a quantity without a unit.</summary>
    public static string WithUnit(string number, string? unit)
    {
        ArgumentNullException.ThrowIfNull(number);
        if (string.IsNullOrWhiteSpace(unit)) return number;
        return unit is "°" or "′" or "″" ? number + unit : number + " " + unit;
    }

    /// <summary>"a = 2.5 m/s²".</summary>
    public static string Equation(string symbol, string number, string? unit) => $"{Plain(symbol)} = {WithUnit(number, unit)}";

    /// <summary>A number for reading, following the calculator settings (significant digits, ×10ⁿ exponents).</summary>
    public static string Number(double value, NumberFormatOptions? options = null) => NumberFormatter.Format(value, options ?? NumberFormatOptions.Default);

    /// <summary>
    /// A number as Calcy input that reads back as the same double: the shortest round-trip decimal, for example
    /// 3.6443385825e-19, 5.5e14 or 2.5. Infinite values become inf and -inf.
    /// </summary>
    public static string Literal(double value)
    {
        if (double.IsNaN(value)) return "undefined";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        if (value == 0) return "0";
        var text = value.ToString("R", CultureInfo.InvariantCulture).Replace("E+", "e", StringComparison.Ordinal).Replace('E', 'e');
        text = ExponentZeros.Replace(text, "e$1$2");
        // Large round numbers read better with an exponent: 550000000000000 becomes 5.5e14.
        var scientific = value.ToString("0.################e0", CultureInfo.InvariantCulture);
        if (text.Length > 10 && scientific.Length + 2 < text.Length && double.Parse(scientific, CultureInfo.InvariantCulture) == value) return scientific;
        return text;
    }
}
