using System.Globalization;
using System.Numerics;
using System.Text;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Modes.Simple;

/// <summary>
/// How numbers look on the Simple display and tape.
/// Exact decimals that fit in 14 significant digits are shown in full (0.1 + 0.2 = 0.3, 19.99 × 3 = 59.97);
/// everything else is rounded to 10 significant digits (1/3 = 0.3333333333). Values of 10^14 and more,
/// or below 10^-5, use scientific notation (1.23456789E14). Thousands are grouped with commas.
/// </summary>
public static class DisplayFormat
{
    /// <summary>Significant digits shown for values that are not short exact decimals.</summary>
    public const int SignificantDigits = 10;

    /// <summary>The display holds 14 digits.</summary>
    public const int DisplayDigits = 14;

    private static readonly BigRational PlainLimit = new(BigInteger.Pow(10, DisplayDigits));
    private static readonly BigRational SmallLimit = new(BigInteger.One, 100_000);

    /// <summary>Formats a value for the display or the tape.</summary>
    public static string Format(BigRational v, bool grouping = true)
    {
        if (v.IsZero) return "0";
        var abs = v.Abs();
        string sign = v.IsNegative ? "-" : "";

        if (abs < PlainLimit && abs >= SmallLimit && abs.ToTerminatingDecimal(DisplayDigits + 6) is { } exact && SignificantCount(exact) <= DisplayDigits)
            return sign + GroupDecimal(exact, grouping);

        var (_, ds, e) = NumberFormatter.Significant(v, SignificantDigits);
        if (e >= SignificantDigits)
        {
            // A big number with a fraction: show the whole part if it still fits in the 14-digit display.
            var whole = BigInteger.Abs(v.RoundHalfAwayFromZero());
            if (whole < BigInteger.Pow(10, DisplayDigits))
                return sign + Group(whole.ToString(CultureInfo.InvariantCulture), grouping);
            return sign + Scientific(ds, e);
        }
        if (e < -5) return sign + Scientific(ds, e);

        ds = ds.TrimEnd('0');
        if (ds.Length == 0) ds = "0";
        if (e >= 0)
        {
            var intPart = ds.Length > e + 1 ? ds[..(e + 1)] : ds.PadRight(e + 1, '0');
            var frac = ds.Length > e + 1 ? ds[(e + 1)..] : "";
            return sign + Group(intPart, grouping) + (frac.Length > 0 ? "." + frac : "");
        }
        return sign + "0." + new string('0', -e - 1) + ds;
    }

    /// <summary>Formats a number while it is being typed: keeps trailing zeros and a trailing point ("1,234.50", "7.").</summary>
    public static string FormatEntry(string entry)
    {
        bool neg = entry.StartsWith('-');
        var body = neg ? entry[1..] : entry;
        int dot = body.IndexOf('.');
        var intPart = dot >= 0 ? body[..dot] : body;
        var rest = dot >= 0 ? body[dot..] : "";
        return (neg ? "-" : "") + Group(intPart.Length == 0 ? "0" : intPart, true) + rest;
    }

    /// <summary>Inserts thousands separators into a run of digits.</summary>
    public static string Group(string digits, bool grouping = true)
    {
        if (!grouping || digits.Length <= 3) return digits;
        var sb = new StringBuilder(digits.Length + digits.Length / 3);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(',');
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }

    private static string GroupDecimal(string plain, bool grouping)
    {
        int dot = plain.IndexOf('.');
        return dot < 0 ? Group(plain, grouping) : Group(plain[..dot], grouping) + plain[dot..];
    }

    private static int SignificantCount(string plain)
    {
        var digits = plain.Replace(".", "").TrimStart('0');
        return digits.Length == 0 ? 1 : digits.Length;
    }

    private static string Scientific(string ds, int e)
    {
        ds = ds.TrimEnd('0');
        if (ds.Length == 0) ds = "0";
        var mantissa = ds.Length > 1 ? ds[0] + "." + ds[1..] : ds;
        return mantissa + "E" + e.ToString(CultureInfo.InvariantCulture);
    }
}
