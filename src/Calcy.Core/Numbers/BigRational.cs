using System.Globalization;
using System.Numerics;
using System.Text;

namespace Calcy.Core.Numbers;

/// <summary>Exact rational number with arbitrary precision. Always normalized: gcd(num, den) = 1 and den &gt; 0.</summary>
public readonly struct BigRational : IComparable<BigRational>, IEquatable<BigRational>
{
    private readonly BigInteger _num;
    private readonly BigInteger _den; // zero only for default(BigRational), which means 0/1

    public static readonly BigRational Zero = new(BigInteger.Zero);
    public static readonly BigRational One = new(BigInteger.One);
    public static readonly BigRational MinusOne = new(BigInteger.MinusOne);
    public static readonly BigRational Half = new(BigInteger.One, 2);

    public BigInteger Num => _num;
    public BigInteger Den => _den.IsZero ? BigInteger.One : _den;

    public BigRational(BigInteger value)
    {
        _num = value;
        _den = BigInteger.One;
    }

    public BigRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException("Division by zero");
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        var g = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (!g.IsOne && !g.IsZero)
        {
            numerator /= g;
            denominator /= g;
        }
        _num = numerator;
        _den = numerator.IsZero ? BigInteger.One : denominator;
    }

    public bool IsZero => _num.IsZero;
    public bool IsOne => _num.IsOne && Den.IsOne;
    public bool IsInteger => Den.IsOne;
    public int Sign => _num.Sign;
    public bool IsNegative => _num.Sign < 0;
    public bool IsPositive => _num.Sign > 0;

    public BigRational Abs() => _num.Sign < 0 ? new BigRational(-_num, Den) : this;
    public BigRational Reciprocal() => new(Den, _num);

    public static implicit operator BigRational(int v) => new(v);
    public static implicit operator BigRational(long v) => new(v);
    public static implicit operator BigRational(BigInteger v) => new(v);

    public static BigRational operator +(BigRational a, BigRational b)
    {
        if (a.Den.IsOne && b.Den.IsOne) return new BigRational(a._num + b._num);
        return new BigRational(a._num * b.Den + b._num * a.Den, a.Den * b.Den);
    }

    public static BigRational operator -(BigRational a, BigRational b)
    {
        if (a.Den.IsOne && b.Den.IsOne) return new BigRational(a._num - b._num);
        return new BigRational(a._num * b.Den - b._num * a.Den, a.Den * b.Den);
    }

    public static BigRational operator -(BigRational a) => new(-a._num, a.Den);

    public static BigRational operator *(BigRational a, BigRational b)
    {
        if (a.Den.IsOne && b.Den.IsOne) return new BigRational(a._num * b._num);
        return new BigRational(a._num * b._num, a.Den * b.Den);
    }

    public static BigRational operator /(BigRational a, BigRational b)
    {
        if (b._num.IsZero) throw new DivideByZeroException("Division by zero");
        return new BigRational(a._num * b.Den, a.Den * b._num);
    }

    public static bool operator ==(BigRational a, BigRational b) => a.Equals(b);
    public static bool operator !=(BigRational a, BigRational b) => !a.Equals(b);
    public static bool operator <(BigRational a, BigRational b) => a.CompareTo(b) < 0;
    public static bool operator >(BigRational a, BigRational b) => a.CompareTo(b) > 0;
    public static bool operator <=(BigRational a, BigRational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BigRational a, BigRational b) => a.CompareTo(b) >= 0;

    public int CompareTo(BigRational other)
    {
        if (Den.IsOne && other.Den.IsOne) return _num.CompareTo(other._num);
        return (_num * other.Den).CompareTo(other._num * Den);
    }

    public bool Equals(BigRational other) => _num.Equals(other._num) && Den.Equals(other.Den);
    public override bool Equals(object? obj) => obj is BigRational r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(_num, Den);

    /// <summary>Integer power. Negative exponents give the reciprocal power.</summary>
    public BigRational Pow(int exponent)
    {
        if (exponent == 0) return One;
        if (exponent < 0)
        {
            if (_num.IsZero) throw new DivideByZeroException("Division by zero");
            return Reciprocal().Pow(-exponent);
        }
        return new BigRational(BigInteger.Pow(_num, exponent), BigInteger.Pow(Den, exponent));
    }

    /// <summary>Approximate number of bits needed to hold this value's numerator and denominator.</summary>
    public long BitSize => (long)_num.GetBitLength() + (long)Den.GetBitLength();

    public BigInteger Floor()
    {
        if (Den.IsOne) return _num;
        var q = BigInteger.DivRem(_num, Den, out var r);
        return r.Sign < 0 ? q - 1 : q;
    }

    public BigInteger Ceiling()
    {
        if (Den.IsOne) return _num;
        var q = BigInteger.DivRem(_num, Den, out var r);
        return r.Sign > 0 ? q + 1 : q;
    }

    public BigInteger Truncate() => BigInteger.Divide(_num, Den);

    /// <summary>Round half away from zero (calculator convention).</summary>
    public BigInteger RoundHalfAwayFromZero()
    {
        var a = Abs();
        var r = (a + Half).Floor();
        return Sign < 0 ? -r : r;
    }

    /// <summary>Fractional part with the sign of the value (TI fPart semantics).</summary>
    public BigRational FractionalPart() => this - new BigRational(Truncate());

    public double ToDouble()
    {
        if (_num.IsZero) return 0.0;
        var den = Den;
        long nb = (long)BigInteger.Abs(_num).GetBitLength();
        long db = (long)den.GetBitLength();
        if (nb < 1000 && db < 1000)
        {
            double n = (double)_num, d = (double)den;
            if (!double.IsInfinity(n) && !double.IsInfinity(d) && nb <= 53 && db <= 53) return n / d;
        }
        return CorrectlyRounded(_num.Sign < 0, BigInteger.Abs(_num), den, nb - db);
    }

    /// <summary>
    /// a/b rounded to the nearest double (ties to even), including subnormals: the quotient is taken with a few guard
    /// bits plus a sticky bit for the remainder, then rounded once, so no double rounding happens.
    /// </summary>
    private static double CorrectlyRounded(bool negative, BigInteger a, BigInteger b, long bitDifference)
    {
        double Signed(double v) => negative ? -v : v;
        if (bitDifference > 1100) return Signed(double.PositiveInfinity);
        if (bitDifference < -1200) return Signed(0.0);
        int s = (int)(56 - bitDifference);
        BigInteger q, r;
        q = s >= 0 ? BigInteger.DivRem(a << s, b, out r) : BigInteger.DivRem(a, b << -s, out r);
        bool sticky = !r.IsZero;
        long length = (long)q.GetBitLength();
        long exponent = length - 1 - s;
        if (exponent > 1023) return Signed(double.PositiveInfinity);
        int keep = exponent >= -1022 ? 53 : (int)(exponent + 1075);
        if (keep <= 0)
        {
            // Below half the smallest subnormal rounds to zero; just above it rounds up to the smallest subnormal.
            bool aboveHalf = keep == 0 && (sticky || q != BigInteger.One << (int)(length - 1));
            return Signed(aboveHalf ? double.Epsilon : 0.0);
        }
        int drop = (int)(length - keep);
        var mantissa = q >> drop;
        var rest = q & ((BigInteger.One << drop) - 1);
        var half = BigInteger.One << (drop - 1);
        bool up = rest > half || (rest == half && (sticky || !mantissa.IsEven));
        if (up) mantissa += 1;
        return Signed(Math.ScaleB((double)mantissa, drop - s));
    }

    /// <summary>Exact conversion of a finite double.</summary>
    public static BigRational FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Cannot convert a non-finite value to an exact number");
        if (value == 0) return Zero;
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0) exponent++;
        else mantissa |= 1L << 52;
        exponent -= 1075;
        BigInteger m = mantissa;
        if (negative) m = -m;
        return exponent >= 0 ? new BigRational(m << exponent) : new BigRational(m, BigInteger.One << -exponent);
    }

    /// <summary>Best rational approximation with denominator at most <paramref name="maxDenominator"/> (continued fractions).</summary>
    public static BigRational Approximate(double value, long maxDenominator = 1_000_000, double tolerance = 1e-12)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Cannot convert a non-finite value to an exact number");
        if (Math.Abs(value) > 1e15) return FromDouble(Math.Round(value));
        bool neg = value < 0;
        double x = Math.Abs(value);
        BigInteger h0 = 0, h1 = 1, k0 = 1, k1 = 0;
        double frac = x;
        for (int i = 0; i < 64; i++)
        {
            double a = Math.Floor(frac);
            var ai = new BigInteger(a);
            var h2 = ai * h1 + h0;
            var k2 = ai * k1 + k0;
            if (k2 > maxDenominator) break;
            h0 = h1; h1 = h2; k0 = k1; k1 = k2;
            double approx = (double)h1 / (double)k1;
            if (Math.Abs(approx - x) <= tolerance * Math.Max(1.0, x)) break;
            double rem = frac - a;
            if (rem < 1e-18) break;
            frac = 1.0 / rem;
        }
        if (k1.IsZero) return FromDouble(value);
        var r = new BigRational(h1, k1);
        return neg ? -r : r;
    }

    /// <summary>Parses an exact decimal literal such as "12", "0.125", "1.5e-3" or "2E10".</summary>
    public static BigRational ParseDecimal(string text)
    {
        text = text.Trim();
        int ePos = text.IndexOfAny(['e', 'E']);
        string mantissa = ePos >= 0 ? text[..ePos] : text;
        int exp10 = 0;
        if (ePos >= 0)
        {
            exp10 = int.Parse(text[(ePos + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        bool negative = false;
        if (mantissa.StartsWith('-')) { negative = true; mantissa = mantissa[1..]; }
        else if (mantissa.StartsWith('+')) mantissa = mantissa[1..];
        int dot = mantissa.IndexOf('.');
        string digits;
        if (dot >= 0)
        {
            digits = mantissa[..dot] + mantissa[(dot + 1)..];
            exp10 -= mantissa.Length - dot - 1;
        }
        else digits = mantissa;
        if (digits.Length == 0) digits = "0";
        var n = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (negative) n = -n;
        if (Math.Abs(exp10) > 100_000) throw new OverflowException("Exponent too large");
        return exp10 >= 0
            ? new BigRational(n * BigInteger.Pow(10, exp10))
            : new BigRational(n, BigInteger.Pow(10, -exp10));
    }

    public override string ToString() => Den.IsOne ? _num.ToString(CultureInfo.InvariantCulture) : $"{_num.ToString(CultureInfo.InvariantCulture)}/{Den.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Exact decimal expansion if the denominator only has factors 2 and 5, otherwise null.</summary>
    public string? ToTerminatingDecimal(int maxDigits = 60)
    {
        var d = Den;
        int twos = 0, fives = 0;
        while ((d % 2).IsZero) { d /= 2; twos++; }
        while ((d % 5).IsZero) { d /= 5; fives++; }
        if (!d.IsOne) return null;
        int scale = Math.Max(twos, fives);
        if (scale > maxDigits) return null;
        var scaled = _num * BigInteger.Pow(10, scale) / Den;
        var s = BigInteger.Abs(scaled).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var sb = new StringBuilder();
        if (scaled.Sign < 0) sb.Append('-');
        sb.Append(s, 0, s.Length - scale);
        if (scale > 0)
        {
            sb.Append('.');
            sb.Append(s, s.Length - scale, scale);
        }
        return sb.ToString();
    }
}
