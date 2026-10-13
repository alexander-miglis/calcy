using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Modes.Simple;

/// <summary>
/// Exact desk-calculator arithmetic on <see cref="BigRational"/>. Every operation is exact except square roots
/// of non-squares, which are computed to 45 significant digits (far beyond the 10 digits shown).
/// A null result means "Error" (division by zero, square root of a negative number, overflow).
/// </summary>
public static partial class DeskMath
{
    /// <summary>Results with this magnitude or more are an overflow error (like a desk calculator's 10^100 limit).</summary>
    public static readonly BigRational Overflow = new(BigInteger.Pow(10, 100));

    /// <summary>Results smaller than this in magnitude become zero.</summary>
    public static readonly BigRational Underflow = new(BigInteger.One, BigInteger.Pow(10, 99));

    private static readonly BigRational Hundred = new(100);

    // Exact rationals stay exact; only values whose numerator and denominator grow past this many bits
    // (for example after squaring a fraction many times) are rounded to 50 significant digits.
    private const long MaxBits = 4096;
    private const int GuardDigits = 50;
    private const int RootDigits = 45;

    /// <summary>Applies a binary operator. Returns null on division by zero or overflow.</summary>
    public static BigRational? Apply(BigRational a, CalcOp op, BigRational b) => op switch
    {
        CalcOp.Add => Bound(a + b),
        CalcOp.Subtract => Bound(a - b),
        CalcOp.Multiply => Bound(a * b),
        _ => b.IsZero ? null : Bound(a / b),
    };

    /// <summary>Applies a one-number function. Returns null for √ of a negative number, 1/0 and overflow.</summary>
    public static BigRational? Apply(TermFunction f, BigRational v) => f switch
    {
        TermFunction.SquareRoot => SquareRoot(v),
        TermFunction.Square => Bound(v * v),
        _ => v.IsZero ? null : Bound(v.Reciprocal()),
    };

    /// <summary>
    /// Desk percent: the percentage operand for <c>acc op v%</c>.
    /// For + and − it is v percent of the running total (200 + 10% = 220, 200 − 10% = 180);
    /// for × and ÷ (and with no operator) it is v / 100 (200 × 10% = 20, 200 ÷ 10% = 2000).
    /// </summary>
    public static BigRational PercentOperand(BigRational acc, CalcOp? op, BigRational v) =>
        op is CalcOp.Add or CalcOp.Subtract ? acc * v / Hundred : v / Hundred;

    /// <summary>Square root: exact for squares of rationals, otherwise 45 significant digits. Null for negative input.</summary>
    public static BigRational? SquareRoot(BigRational v)
    {
        if (v.IsNegative) return null;
        if (v.IsZero) return BigRational.Zero;
        var rn = IntegerSqrt(v.Num);
        var rd = IntegerSqrt(v.Den);
        if (rn * rn == v.Num && rd * rd == v.Den) return new BigRational(rn, rd);
        int e = NumberFormatter.Significant(v, 1).Exponent;
        int k = Math.Max(0, RootDigits - e / 2);
        var scaled = v.Num * BigInteger.Pow(10, 2 * k) / v.Den;
        return Bound(new BigRational(IntegerSqrt(scaled), BigInteger.Pow(10, k)));
    }

    /// <summary>Keeps a value inside the calculator's range: null on overflow, zero on underflow, rounded when huge.</summary>
    public static BigRational? Bound(BigRational v)
    {
        if (v.IsZero) return v;
        var a = v.Abs();
        if (a >= Overflow) return null;
        if (a < Underflow) return BigRational.Zero;
        return v.BitSize > MaxBits ? RoundSignificant(v, GuardDigits) : v;
    }

    /// <summary>Rounds to the given number of significant digits (half away from zero).</summary>
    public static BigRational RoundSignificant(BigRational v, int digits)
    {
        if (v.IsZero) return v;
        var (neg, ds, e) = NumberFormatter.Significant(v, digits);
        var n = BigInteger.Parse(ds, CultureInfo.InvariantCulture);
        if (neg) n = -n;
        int exp = e - (ds.Length - 1);
        return exp >= 0 ? new BigRational(n * BigInteger.Pow(10, exp)) : new BigRational(n, BigInteger.Pow(10, -exp));
    }

    /// <summary>
    /// Parses a number as people type it: "1234.5", "-0.25", ".5", "1,5" (comma as decimal point),
    /// "1,234.5" (comma grouping), "1.5E14", with spaces and the − sign allowed. Out-of-range values fail.
    /// </summary>
    public static bool TryParseNumber(string? text, out BigRational value)
    {
        value = BigRational.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Replace(" ", "").Replace(" ", "").Replace('−', '-');
        if (s.StartsWith('+')) s = s[1..];
        int commas = s.Count(c => c == ',');
        if (commas > 0)
        {
            // Both separators, or several commas: commas group thousands. A single comma alone is a decimal point.
            s = s.Contains('.') || commas > 1 ? s.Replace(",", "") : s.Replace(',', '.');
        }
        if (!NumberPattern().IsMatch(s)) return false;
        int ePos = s.IndexOfAny(['e', 'E']);
        if (ePos >= 0 && (!int.TryParse(s[(ePos + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exp) || Math.Abs(exp) > 400))
            return false;
        BigRational parsed;
        try
        {
            parsed = BigRational.ParseDecimal(s);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return false;
        }
        if (Bound(parsed) is not { } bounded) return false;
        value = bounded;
        return true;
    }

    /// <summary>Integer square root (floor) by Newton's method.</summary>
    public static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n), "Square root of a negative integer");
        if (n < 2) return n;
        var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x) return x;
            x = y;
        }
    }

    [GeneratedRegex(@"^-?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
