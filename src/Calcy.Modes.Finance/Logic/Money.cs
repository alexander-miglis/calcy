using System.Globalization;

namespace Calcy.Modes.Finance.Logic;

/// <summary>
/// Number formatting for the Finance screen. Digits always use "," between thousands and "." for decimals, as everywhere
/// else in Calcy. The currency symbol comes from the culture and is display only: it is never part of what is parsed.
/// </summary>
public static class Money
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Shown for values that are not finite numbers.</summary>
    public const string NotANumber = "?";

    /// <summary>The culture's currency symbol, or an empty string when the culture has none (the invariant culture).</summary>
    public static string CurrencySymbol(CultureInfo? culture = null)
    {
        var symbol = (culture ?? CultureInfo.CurrentCulture).NumberFormat.CurrencySymbol;
        return string.IsNullOrWhiteSpace(symbol) || symbol == "¤" ? "" : symbol.Trim();
    }

    /// <summary>Rounds to cents, turning -0.00 into 0.00.</summary>
    public static double Cents(double value)
    {
        double r = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return r == 0 ? 0 : r;
    }

    /// <summary>Grouped thousands and two decimals with a leading minus sign, no currency symbol: "-1,234.56".</summary>
    public static string Plain(double value)
    {
        if (!double.IsFinite(value)) return NotANumber;
        double r = Cents(value);
        return (r < 0 ? "-" : "") + Math.Abs(r).ToString("#,##0.00", Inv);
    }

    /// <summary>
    /// Money for display: grouped thousands, two decimals, a minus sign for outflows and the culture's currency symbol
    /// placed as the culture places it ("-$1,234.56", "-1,234.56 €").
    /// </summary>
    public static string Format(double value, CultureInfo? culture = null)
    {
        if (!double.IsFinite(value)) return NotANumber;
        culture ??= CultureInfo.CurrentCulture;
        var symbol = CurrencySymbol(culture);
        double r = Cents(value);
        var digits = Math.Abs(r).ToString("#,##0.00", Inv);
        var sign = r < 0 ? "-" : "";
        if (symbol.Length == 0) return sign + digits;
        return culture.NumberFormat.CurrencyPositivePattern switch
        {
            1 => sign + digits + symbol,
            2 => sign + symbol + " " + digits,
            3 => sign + digits + " " + symbol,
            _ => sign + symbol + digits,
        };
    }

    /// <summary>Two decimals without grouping, for CSV: "-1234.56".</summary>
    public static string Csv(double value) => !double.IsFinite(value) ? "" : Cents(value).ToString("0.00", Inv);

    /// <summary>A percentage with a fixed number of decimals and a space before the sign: "6.1678 %".</summary>
    public static string Percent(double percent, int decimals = 4)
    {
        if (!double.IsFinite(percent)) return NotANumber;
        double r = Math.Round(percent, decimals, MidpointRounding.AwayFromZero);
        if (r == 0) r = 0;
        return r.ToString("#,##0." + new string('0', decimals), Inv) + " %";
    }

    /// <summary>A count or other plain number with up to the given decimals, trailing zeros removed: "47.25", "360".</summary>
    public static string Number(double value, int maxDecimals = 4)
    {
        if (!double.IsFinite(value)) return NotANumber;
        double r = Math.Round(value, maxDecimals, MidpointRounding.AwayFromZero);
        if (r == 0) r = 0;
        return r.ToString("#,##0." + new string('#', maxDecimals), Inv);
    }

    /// <summary>
    /// The text put back into an input field after solving: ten significant digits like the TI-84 ("-733.7645739"),
    /// no grouping, so it can be read again as an expression.
    /// </summary>
    public static string Field(double value)
    {
        if (!double.IsFinite(value)) return "";
        double r = double.Parse(value.ToString("G10", Inv), Inv);
        if (r == 0) return "0";
        var s = r.ToString("G10", Inv);
        return s.Contains('E') ? s.Replace("E+", "e", StringComparison.Ordinal).Replace("E-", "e-", StringComparison.Ordinal) : s;
    }

    /// <summary>A full precision number for Calcy expressions (round trip, invariant): "-733.76457387937".</summary>
    public static string Exact(double value) => value.ToString("R", Inv);
}
