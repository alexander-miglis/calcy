using System.Globalization;
using System.Numerics;

namespace Calcy.Modes.Programmer;

/// <summary>
/// A fixed-point Qm.n format (ARM notation): m integer bits and n fraction bits, plus a sign bit when signed.
/// Q1.14 signed has 16 bits; UQ8.8 unsigned has 16 bits. Values are raw integers scaled by 2^-n.
/// </summary>
public readonly record struct FixedPointFormat
{
    /// <summary>Creates the format; the total width must be 1 to 256 bits.</summary>
    public FixedPointFormat(int integerBits, int fractionBits, bool signed)
    {
        if (integerBits < 0 || fractionBits < 0) throw new ProgrammerException("Qm.n needs m and n of 0 or more");
        int total = integerBits + fractionBits + (signed ? 1 : 0);
        if (total is < 1 or > IntegerFormat.MaxBits) throw new ProgrammerException($"Qm.n must have 1 to {IntegerFormat.MaxBits} bits in all (this one has {total})");
        IntegerBits = integerBits;
        FractionBits = fractionBits;
        Signed = signed;
    }

    /// <summary>m: integer bits (not counting the sign bit).</summary>
    public int IntegerBits { get; }

    /// <summary>n: fraction bits.</summary>
    public int FractionBits { get; }

    /// <summary>True for two's complement (Qm.n), false for unsigned (UQm.n).</summary>
    public bool Signed { get; }

    /// <summary>Total width in bits.</summary>
    public int TotalBits => IntegerBits + FractionBits + (Signed ? 1 : 0);

    /// <summary>The raw integer word.</summary>
    public IntegerFormat Word => new(TotalBits, Signed, OverflowMode.Saturate);

    /// <summary>Name such as "Q1.14" or "UQ8.8".</summary>
    public string Name => $"{(Signed ? "Q" : "UQ")}{IntegerBits}.{FractionBits}";

    /// <summary>Converts a real number (exact fraction) to the nearest raw value (ties to even), saturating at the ends.</summary>
    public FixedPointValue FromRational(bool negative, BigInteger numerator, BigInteger denominator)
    {
        var scaled = numerator << FractionBits;
        var q = BigInteger.DivRem(scaled, denominator, out var rem);
        var twice = rem << 1;
        if (twice > denominator || (twice == denominator && !q.IsEven)) q += 1;
        if (negative) q = -q;
        var w = Word;
        bool saturated = !w.Fits(q);
        var raw = IntegerOps.Fit(w, q);
        var errNum = (raw * denominator) - ((negative ? -numerator : numerator) << FractionBits);
        return new FixedPointValue(this, raw, saturated, errNum, denominator << FractionBits);
    }

    /// <summary>
    /// Reads a decimal such as -1.25, a fraction such as 1/3, or a raw pattern such as 0x4000 (taken as the raw bits).
    /// </summary>
    public FixedPointValue Parse(string text)
    {
        var s = (text ?? "").Trim().Replace("_", "", StringComparison.Ordinal).Replace('−', '-');
        if (s.Length == 0) throw new ProgrammerException("Type a number, for example 0.75 or 0x6000");
        bool negative = s[0] == '-';
        var body = s[0] is '-' or '+' ? s[1..].Trim() : s;
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || body.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            var b = char.ToLowerInvariant(body[1]) == 'x' ? NumberBase.Hex : NumberBase.Bin;
            var raw = BaseFormatter.ParseDigits(body[2..], b);
            if (raw.GetBitLength() > TotalBits) throw new ProgrammerException($"{body} has more than {TotalBits} bits; {Name} has {TotalBits}");
            var value = Word.FromPattern(raw);
            if (negative) value = IntegerOps.Fit(Word, -value);
            return new FixedPointValue(this, value, false, BigInteger.Zero, BigInteger.One);
        }
        var f = IeeeFloat.Parse(s, IeeeFormat.Quad);
        if (!f.IsFinite) throw new ProgrammerException("Fixed point has no infinity or NaN");
        var exact = ParseExact(body);
        return FromRational(negative, exact.Num, exact.Den);
    }

    private static (BigInteger Num, BigInteger Den) ParseExact(string s)
    {
        int slash = s.IndexOf('/');
        if (slash >= 0)
        {
            var a = ParseDecimal(s[..slash].Trim());
            var b = ParseDecimal(s[(slash + 1)..].Trim());
            if (b.Num.IsZero) throw new ProgrammerException("Division by zero");
            return (a.Num * b.Den, a.Den * b.Num);
        }
        return ParseDecimal(s);
    }

    private static (BigInteger Num, BigInteger Den) ParseDecimal(string s)
    {
        int ePos = s.IndexOfAny(['e', 'E']);
        string mant = ePos < 0 ? s : s[..ePos];
        int exp = 0;
        if (ePos >= 0 && !int.TryParse(s[(ePos + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exp))
            throw new ProgrammerException($"'{s}' is not a number");
        exp = Math.Clamp(exp, -400, 400);
        int dot = mant.IndexOf('.');
        string digits = dot < 0 ? mant : mant.Remove(dot, 1);
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) throw new ProgrammerException($"'{s}' is not a number");
        var m = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        exp -= dot < 0 ? 0 : mant.Length - dot - 1;
        return exp >= 0 ? (m * BigInteger.Pow(10, exp), BigInteger.One) : (m, BigInteger.Pow(10, -exp));
    }

    /// <summary>The smallest representable value as an exact decimal.</summary>
    public string MinText => new FixedPointValue(this, Word.MinValue, false, BigInteger.Zero, BigInteger.One).ExactDecimal();

    /// <summary>The largest representable value as an exact decimal.</summary>
    public string MaxText => new FixedPointValue(this, Word.MaxValue, false, BigInteger.Zero, BigInteger.One).ExactDecimal();

    /// <summary>The step between neighbouring values, 2^-n, as an exact decimal.</summary>
    public string ResolutionText => IeeeFloat.PowerOfTwoDecimal(-FractionBits);
}

/// <summary>A fixed-point value: the raw integer and what it represents.</summary>
/// <param name="Format">The Qm.n format.</param>
/// <param name="Raw">The raw integer (canonical in the format's word).</param>
/// <param name="Saturated">True when the input was outside the range and was clamped.</param>
/// <param name="ErrorNumerator">Stored minus input, numerator.</param>
/// <param name="ErrorDenominator">Stored minus input, denominator.</param>
public sealed record FixedPointValue(FixedPointFormat Format, BigInteger Raw, bool Saturated, BigInteger ErrorNumerator, BigInteger ErrorDenominator)
{
    /// <summary>The raw bits in hex, padded to the width.</summary>
    public string RawHex => "0x" + BaseFormatter.ToDigits(Format.Word.ToPattern(Raw), NumberBase.Hex).PadLeft((Format.TotalBits + 3) / 4, '0');

    /// <summary>The raw bits in binary, padded to the width and grouped in fours.</summary>
    public string RawBinary => BaseFormatter.Format(Format.Word, Raw, NumberBase.Bin);

    /// <summary>The represented value, Raw / 2^n, as an exact decimal.</summary>
    public string ExactDecimal() => DecimalOf(Raw, Format.FractionBits);

    /// <summary>Stored value minus the input as a short decimal (good to quad precision), or "0 (exact)".</summary>
    public string ErrorText
    {
        get
        {
            if (ErrorNumerator.IsZero) return "0 (exact)";
            var neg = ErrorNumerator.Sign < 0;
            var f = IeeeFloat.FromRational(IeeeFormat.Quad, neg, BigInteger.Abs(ErrorNumerator), ErrorDenominator);
            return f.ShortestDecimal();
        }
    }

    private static string DecimalOf(BigInteger raw, int n)
    {
        bool neg = raw.Sign < 0;
        var a = BigInteger.Abs(raw);
        var whole = a >> n;
        var frac = a - (whole << n);
        var s = whole.ToString(CultureInfo.InvariantCulture);
        if (!frac.IsZero)
        {
            // frac / 2^n = frac * 5^n / 10^n
            var digits = (frac * BigInteger.Pow(5, n)).ToString(CultureInfo.InvariantCulture).PadLeft(n, '0').TrimEnd('0');
            s += "." + digits;
        }
        return (neg ? "-" : "") + s;
    }
}
