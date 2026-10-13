using System.Globalization;
using System.Numerics;
using System.Text;
using Calcy.Core.Numbers;

namespace Calcy.Core.Printing;

public sealed record NumberFormatOptions
{
    public NumberFormatMode Mode { get; init; } = NumberFormatMode.Float;

    /// <summary>Significant digits (Float) or decimals (Fix, Sci, Eng).</summary>
    public int Digits { get; init; } = 10;

    /// <summary>Use ×10⁵ style exponents instead of e5.</summary>
    public bool Unicode { get; init; } = true;

    /// <summary>Insert thousands separators in the integer part.</summary>
    public bool Grouping { get; init; }

    public static readonly NumberFormatOptions Default = new();
    public static readonly NumberFormatOptions Ascii = new() { Unicode = false, Digits = 15 };

    public static NumberFormatOptions From(CalcSettings s, bool unicode = true) =>
        new() { Mode = s.Format, Digits = s.Digits, Unicode = unicode };
}

/// <summary>Decimal formatting of exact rationals and doubles. Rounding is exact (no binary rounding surprises).</summary>
public static class NumberFormatter
{
    private const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    public static string Format(double x, NumberFormatOptions? o = null)
    {
        o ??= NumberFormatOptions.Default;
        if (double.IsNaN(x)) return "undefined";
        if (double.IsPositiveInfinity(x)) return "∞";
        if (double.IsNegativeInfinity(x)) return o.Unicode ? "-∞" : "-inf";
        if (x == 0) return "0";
        return Format(BigRational.FromDouble(x), o, approximate: true);
    }

    public static string FormatRational(BigRational q, NumberFormatOptions? o = null) => Format(q, o ?? NumberFormatOptions.Default, approximate: false);

    private static string Format(BigRational q, NumberFormatOptions o, bool approximate)
    {
        if (q.IsZero) return "0";
        int digits = Math.Clamp(o.Digits, 1, 60);
        switch (o.Mode)
        {
            case NumberFormatMode.Fix:
            {
                var a = q.Abs();
                if (a >= BigRational.ParseDecimal("1e15")) goto case NumberFormatMode.Sci;
                var scaled = RoundHalfUp(a * new BigRational(BigInteger.Pow(10, digits)));
                var s = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
                var intPart = s[..^digits];
                var frac = digits > 0 ? s[^digits..] : "";
                var text = Group(intPart, o) + (digits > 0 ? "." + frac : "");
                return q.IsNegative && scaled > 0 ? "-" + text : text;
            }
            case NumberFormatMode.Sci:
            {
                var (neg, ds, e) = Significant(q, digits + 1);
                var mant = ds[0] + (ds.Length > 1 ? "." + ds[1..] : "");
                return (neg ? "-" : "") + mant + Exponent(e, o);
            }
            case NumberFormatMode.Eng:
            {
                var (neg, ds, e) = Significant(q, digits + 1);
                int shift = ((e % 3) + 3) % 3;
                int e3 = e - shift;
                var all = ds.PadRight(shift + 1, '0');
                var mant = all[..(shift + 1)] + (all.Length > shift + 1 ? "." + all[(shift + 1)..] : "");
                return (neg ? "-" : "") + mant + Exponent(e3, o);
            }
            default:
            {
                var (neg, ds, e) = Significant(q, digits);
                ds = ds.TrimEnd('0');
                if (ds.Length == 0) ds = "0";
                string body;
                if (e >= digits || e < -5)
                {
                    body = ds[0] + (ds.Length > 1 ? "." + ds[1..] : "") + Exponent(e, o);
                }
                else if (e >= 0)
                {
                    var intPart = ds.Length > e + 1 ? ds[..(e + 1)] : ds.PadRight(e + 1, '0');
                    var frac = ds.Length > e + 1 ? ds[(e + 1)..] : "";
                    body = Group(intPart, o) + (frac.Length > 0 ? "." + frac : "");
                }
                else
                {
                    body = "0." + new string('0', -e - 1) + ds;
                }
                return (neg ? "-" : "") + body;
            }
        }
    }

    private static string Group(string intPart, NumberFormatOptions o)
    {
        if (!o.Grouping || intPart.Length <= 3) return intPart;
        var sb = new StringBuilder();
        for (int k = 0; k < intPart.Length; k++)
        {
            if (k > 0 && (intPart.Length - k) % 3 == 0) sb.Append(',');
            sb.Append(intPart[k]);
        }
        return sb.ToString();
    }

    private static string Exponent(int e, NumberFormatOptions o)
    {
        if (!o.Unicode) return "e" + e.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder("×10");
        foreach (var c in e.ToString(CultureInfo.InvariantCulture))
            sb.Append(c == '-' ? '⁻' : Sup[c - '0']);
        return sb.ToString();
    }

    private static BigInteger RoundHalfUp(BigRational a)
    {
        var floor = a.Floor();
        var frac = a - new BigRational(floor);
        return frac >= BigRational.Half ? floor + 1 : floor;
    }

    /// <summary>
    /// Returns the sign, exactly rounded significant digits and the decimal exponent e so that |q| ≈ d.ddd × 10^e.
    /// </summary>
    public static (bool Negative, string Digits, int Exponent) Significant(BigRational q, int sig)
    {
        bool neg = q.IsNegative;
        var a = q.Abs();
        long nb = (long)a.Num.GetBitLength(), db = (long)a.Den.GetBitLength();
        int e = (int)Math.Floor((nb - db) * 0.30102999566398120);
        // Correct the estimate exactly.
        while (a >= Pow10(e + 1)) e++;
        while (a < Pow10(e)) e--;
        var scaled = a * Pow10(sig - 1 - e);
        var n = RoundHalfUp(scaled);
        if (n >= BigInteger.Pow(10, sig))
        {
            n /= 10;
            e++;
        }
        return (neg, n.ToString(CultureInfo.InvariantCulture), e);
    }

    private static BigRational Pow10(int k) =>
        k >= 0 ? new BigRational(BigInteger.Pow(10, k)) : new BigRational(BigInteger.One, BigInteger.Pow(10, -k));

    /// <summary>Formats a complex or real <see cref="Number"/> as a decimal approximation.</summary>
    public static string FormatDecimal(Number n, NumberFormatOptions? o = null)
    {
        o ??= NumberFormatOptions.Default;
        if (n.IsReal) return n.IsExact ? FormatRational(n.ReQ, o) : Format(n.Re, o);
        string re = n.IsExact ? FormatRational(n.ReQ, o) : Format(n.Re, o);
        string im = n.IsExact ? FormatRational(n.ImQ.Abs(), o) : Format(Math.Abs(n.Im), o);
        bool imNeg = n.IsExact ? n.ImQ.IsNegative : n.Im < 0;
        bool reZero = n.IsExact ? n.ReQ.IsZero : n.Re == 0;
        string imPart = (im == "1" ? "" : im) + "i";
        if (reZero) return (imNeg ? "-" : "") + imPart;
        return re + (imNeg ? " - " : " + ") + imPart;
    }

    /// <summary>A short decimal, for example to show next to an exact result (≈ 0.7071067812).</summary>
    public static string Short(double x, int digits = 10) => Format(x, new NumberFormatOptions { Digits = digits });
}
