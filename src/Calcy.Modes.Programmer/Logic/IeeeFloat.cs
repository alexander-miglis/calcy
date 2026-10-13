using System.Globalization;
using System.Numerics;
using System.Text;

namespace Calcy.Modes.Programmer;

/// <summary>An IEEE 754 binary interchange format: half, single, double or quad precision.</summary>
public sealed record IeeeFormat
{
    private IeeeFormat(string name, int exponentBits, int fractionBits)
    {
        Name = name;
        ExponentBits = exponentBits;
        FractionBits = fractionBits;
    }

    /// <summary>binary16: 1 sign, 5 exponent, 10 fraction bits.</summary>
    public static IeeeFormat Half { get; } = new("half", 5, 10);

    /// <summary>binary32 (float): 1 sign, 8 exponent, 23 fraction bits.</summary>
    public static IeeeFormat Single { get; } = new("single", 8, 23);

    /// <summary>binary64 (double): 1 sign, 11 exponent, 52 fraction bits.</summary>
    public static IeeeFormat Double { get; } = new("double", 11, 52);

    /// <summary>binary128 (quad): 1 sign, 15 exponent, 112 fraction bits.</summary>
    public static IeeeFormat Quad { get; } = new("quad", 15, 112);

    /// <summary>The four formats, narrowest first.</summary>
    public static IReadOnlyList<IeeeFormat> All { get; } = [Half, Single, Double, Quad];

    /// <summary>"half", "single", "double" or "quad".</summary>
    public string Name { get; }

    /// <summary>Width of the exponent field.</summary>
    public int ExponentBits { get; }

    /// <summary>Width of the fraction (trailing significand) field.</summary>
    public int FractionBits { get; }

    /// <summary>Total width: 16, 32, 64 or 128.</summary>
    public int TotalBits => 1 + ExponentBits + FractionBits;

    /// <summary>Significand precision in bits, including the hidden bit.</summary>
    public int Precision => FractionBits + 1;

    /// <summary>Exponent bias (15, 127, 1023, 16383).</summary>
    public int Bias => (1 << (ExponentBits - 1)) - 1;

    /// <summary>Smallest normal exponent (1 - bias).</summary>
    public int MinExponent => 1 - Bias;

    /// <summary>Largest finite exponent (= bias).</summary>
    public int MaxExponent => Bias;

    /// <summary>All-ones exponent field (infinities and NaNs).</summary>
    public int MaxBiasedExponent => (1 << ExponentBits) - 1;

    /// <summary>Text such as "IEEE 754 double (64 bits)".</summary>
    public string Describe() => $"IEEE 754 {Name} ({TotalBits} bits)";
}

/// <summary>What kind of number a bit pattern holds.</summary>
public enum FloatClass
{
    /// <summary>+0 or -0.</summary>
    Zero,

    /// <summary>Below the normal range: exponent field 0, no hidden bit.</summary>
    Subnormal,

    /// <summary>A normal number.</summary>
    Normal,

    /// <summary>+infinity or -infinity.</summary>
    Infinity,

    /// <summary>Quiet NaN (top fraction bit set).</summary>
    QuietNaN,

    /// <summary>Signalling NaN (top fraction bit clear, payload not zero).</summary>
    SignalingNaN,
}

/// <summary>
/// An IEEE 754 value as a bit pattern in one of the binary formats, with exact decoding (no floating point is used,
/// so half and quad precision work the same way as single and double).
/// </summary>
public readonly record struct IeeeFloat
{
    /// <summary>Creates a value from its bit pattern (only the low <see cref="IeeeFormat.TotalBits"/> bits are used).</summary>
    public IeeeFloat(IeeeFormat format, BigInteger bits)
    {
        Format = format;
        Bits = bits & ((BigInteger.One << format.TotalBits) - 1);
    }

    /// <summary>The format.</summary>
    public IeeeFormat Format { get; }

    /// <summary>The raw bit pattern (sign in the top bit).</summary>
    public BigInteger Bits { get; }

    /// <summary>True when the sign bit is set (negative numbers, -0, negative NaNs).</summary>
    public bool Sign => !(Bits >> (Format.TotalBits - 1)).IsZero;

    /// <summary>The exponent field as stored (biased).</summary>
    public int BiasedExponent => (int)((Bits >> Format.FractionBits) & Format.MaxBiasedExponent);

    /// <summary>The fraction field (trailing significand bits).</summary>
    public BigInteger Fraction => Bits & ((BigInteger.One << Format.FractionBits) - 1);

    /// <summary>The kind of value.</summary>
    public FloatClass Class
    {
        get
        {
            int e = BiasedExponent;
            if (e == 0) return Fraction.IsZero ? FloatClass.Zero : FloatClass.Subnormal;
            if (e < Format.MaxBiasedExponent) return FloatClass.Normal;
            if (Fraction.IsZero) return FloatClass.Infinity;
            return (Fraction >> (Format.FractionBits - 1)).IsZero ? FloatClass.SignalingNaN : FloatClass.QuietNaN;
        }
    }

    /// <summary>True for zeros, subnormals and normals.</summary>
    public bool IsFinite => BiasedExponent != Format.MaxBiasedExponent;

    /// <summary>True for quiet and signalling NaNs.</summary>
    public bool IsNaN => Class is FloatClass.QuietNaN or FloatClass.SignalingNaN;

    /// <summary>The power of two the significand is scaled by: biased - bias for normals, the minimum exponent for subnormals and zero.</summary>
    public int UnbiasedExponent => BiasedExponent == 0 ? Format.MinExponent : BiasedExponent - Format.Bias;

    /// <summary>The significand as an integer, including the hidden bit for normal numbers.</summary>
    public BigInteger Significand => BiasedExponent == 0 ? Fraction : Fraction | (BigInteger.One << Format.FractionBits);

    /// <summary>NaN payload: the fraction bits below the quiet bit.</summary>
    public BigInteger NaNPayload => IsNaN ? Fraction & ((BigInteger.One << (Format.FractionBits - 1)) - 1) : BigInteger.Zero;

    // ---- construction -------------------------------------------------------------------------------------

    /// <summary>Positive or negative zero.</summary>
    public static IeeeFloat Zero(IeeeFormat f, bool negative = false) => new(f, negative ? SignBit(f) : BigInteger.Zero);

    /// <summary>Positive or negative infinity.</summary>
    public static IeeeFloat Infinity(IeeeFormat f, bool negative = false) => new(f, (negative ? SignBit(f) : BigInteger.Zero) | ExponentMask(f));

    /// <summary>A NaN: quiet by default; a signalling NaN needs a non-zero payload (1 is used for payload 0).</summary>
    public static IeeeFloat NaN(IeeeFormat f, bool negative = false, bool quiet = true, BigInteger? payload = null)
    {
        var maxPayload = (BigInteger.One << (f.FractionBits - 1)) - 1;
        var p = payload ?? BigInteger.Zero;
        if (p.Sign < 0 || p > maxPayload) throw new ProgrammerException($"A {f.Name} NaN payload must be 0 to 0x{BaseFormatter.ToDigits(maxPayload, NumberBase.Hex)}");
        if (!quiet && p.IsZero) p = BigInteger.One;
        var fraction = p | (quiet ? BigInteger.One << (f.FractionBits - 1) : BigInteger.Zero);
        return new IeeeFloat(f, (negative ? SignBit(f) : BigInteger.Zero) | ExponentMask(f) | fraction);
    }

    /// <summary>The bit pattern of a .NET double.</summary>
    public static IeeeFloat FromDouble(double d) => new(IeeeFormat.Double, new BigInteger(unchecked((ulong)BitConverter.DoubleToInt64Bits(d))));

    /// <summary>The bit pattern of a .NET float.</summary>
    public static IeeeFloat FromSingle(float s) => new(IeeeFormat.Single, new BigInteger(BitConverter.SingleToUInt32Bits(s)));

    /// <summary>The bit pattern of a .NET Half.</summary>
    public static IeeeFloat FromHalf(Half h) => new(IeeeFormat.Half, new BigInteger(BitConverter.HalfToUInt16Bits(h)));

    /// <summary>
    /// The nearest representable value to sign × numerator / denominator (round to nearest, ties to even), with
    /// overflow to infinity and gradual underflow to subnormals and zero.
    /// </summary>
    public static IeeeFloat FromRational(IeeeFormat f, bool negative, BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign <= 0 || numerator.Sign < 0) throw new ArgumentOutOfRangeException(nameof(denominator), "Use a non-negative numerator and a positive denominator");
        if (numerator.IsZero) return Zero(f, negative);
        int p = f.Precision;

        // e = floor(log2(n / d))
        long e = (long)numerator.GetBitLength() - (long)denominator.GetBitLength();
        if (e >= 0 ? numerator < denominator << (int)e : numerator << (int)-e < denominator) e--;

        long scale = Math.Max(e, f.MinExponent);
        if (scale > f.MaxExponent) return Infinity(f, negative);
        long shift = p - 1 - scale;
        BigInteger num = numerator, den = denominator;
        if (shift >= 0) num <<= (int)shift; else den <<= (int)-shift;
        var m = BigInteger.DivRem(num, den, out var rem);
        var twice = rem << 1;
        if (twice > den || (twice == den && !m.IsEven)) m += 1;

        if (m >> p != BigInteger.Zero)
        {
            m >>= 1;
            scale++;
            if (scale > f.MaxExponent) return Infinity(f, negative);
        }
        BigInteger bits;
        if (m >> (p - 1) != BigInteger.Zero)
            bits = (new BigInteger(scale + f.Bias) << f.FractionBits) | (m - (BigInteger.One << (p - 1)));
        else
            bits = m; // subnormal (or zero after underflow)
        if (negative) bits |= SignBit(f);
        return new IeeeFloat(f, bits);
    }

    /// <summary>
    /// Reads a number: decimal (0.1, -2.5e-3), a fraction (1/3), a C hex float (0x1.8p3), a raw bit pattern
    /// (0x3FB999999999999A, 0b...), or a special value (inf, -inf, nan, -nan, snan, nan(0x12)).
    /// </summary>
    public static IeeeFloat Parse(string text, IeeeFormat f)
    {
        var s = (text ?? "").Trim().Replace("_", "", StringComparison.Ordinal).Replace('−', '-');
        if (s.Length == 0) throw new ProgrammerException("Type a number, for example 0.1, 1/3, inf or 0x3FB999999999999A");
        if (s.Length > 4000) throw new ProgrammerException("That number is too long");
        bool negative = false;
        var body = s;
        if (body[0] is '+' or '-') { negative = body[0] == '-'; body = body[1..].TrimStart(); }
        var lower = body.ToLowerInvariant();

        if (lower is "inf" or "infinity" or "∞") return Infinity(f, negative);
        if (lower is "nan" or "qnan") return NaN(f, negative);
        if (lower == "snan") return NaN(f, negative, quiet: false);
        if ((lower.StartsWith("nan(", StringComparison.Ordinal) || lower.StartsWith("snan(", StringComparison.Ordinal)) && lower.EndsWith(')'))
        {
            bool quiet = lower[0] == 'n';
            var inner = lower[(lower.IndexOf('(') + 1)..^1].Trim();
            var payload = inner.StartsWith("0x", StringComparison.Ordinal) ? BaseFormatter.ParseDigits(inner[2..], NumberBase.Hex)
                : inner.Length == 0 ? BigInteger.Zero : BaseFormatter.ParseDigits(inner, NumberBase.Dec);
            return NaN(f, negative, quiet, payload);
        }

        if (lower.StartsWith("0x", StringComparison.Ordinal))
        {
            var hex = lower[2..];
            if (hex.Contains('p')) return ParseHexFloat(hex, negative, f);
            if (hex.Contains('.')) throw new ProgrammerException("A hex float needs a binary exponent, as in 0x1.8p3");
            var raw = BaseFormatter.ParseDigits(hex, NumberBase.Hex);
            return FromRawBits(raw, negative, f, s);
        }
        if (lower.StartsWith("0b", StringComparison.Ordinal))
            return FromRawBits(BaseFormatter.ParseDigits(lower[2..], NumberBase.Bin), negative, f, s);

        int slash = lower.IndexOf('/');
        if (slash >= 0)
        {
            var (n1, d1) = ParseDecimalRational(lower[..slash].Trim());
            var (n2, d2) = ParseDecimalRational(lower[(slash + 1)..].Trim());
            if (n2.IsZero) throw new ProgrammerException("Division by zero");
            return FromRational(f, negative, n1 * d2, d1 * n2);
        }
        var (num, den) = ParseDecimalRational(lower);
        return FromRational(f, negative, num, den);
    }

    private static IeeeFloat FromRawBits(BigInteger raw, bool negative, IeeeFormat f, string text)
    {
        if (raw.GetBitLength() > f.TotalBits) throw new ProgrammerException($"{text} has more than {f.TotalBits} bits; a {f.Name} pattern has {f.TotalBits}");
        var v = new IeeeFloat(f, raw);
        return negative ? v.Negate() : v;
    }

    private static IeeeFloat ParseHexFloat(string hex, bool negative, IeeeFormat f)
    {
        int p = hex.IndexOf('p');
        var mantissa = hex[..p];
        var expText = hex[(p + 1)..];
        if (!int.TryParse(expText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int exp))
            throw new ProgrammerException($"'{expText}' is not a binary exponent");
        int dot = mantissa.IndexOf('.');
        string intPart = dot < 0 ? mantissa : mantissa[..dot];
        string fracPart = dot < 0 ? "" : mantissa[(dot + 1)..];
        if (intPart.Length + fracPart.Length == 0) throw new ProgrammerException("A hex float needs digits, as in 0x1.8p3");
        var m = BaseFormatter.ParseDigits(intPart + fracPart, NumberBase.Hex);
        long e2 = (long)exp - 4L * fracPart.Length;
        if (m.IsZero) return Zero(f, negative);
        e2 = Math.Clamp(e2, -40000, 40000);
        return e2 >= 0 ? FromRational(f, negative, m << (int)e2, BigInteger.One) : FromRational(f, negative, m, BigInteger.One << (int)-e2);
    }

    /// <summary>Parses an unsigned decimal such as 12.5e-3 into an exact fraction.</summary>
    private static (BigInteger Num, BigInteger Den) ParseDecimalRational(string s)
    {
        if (s.Length == 0) throw new ProgrammerException("A number is missing");
        int ePos = s.IndexOfAny(['e', 'E']);
        string mant = ePos < 0 ? s : s[..ePos];
        long exp = 0;
        if (ePos >= 0)
        {
            var et = s[(ePos + 1)..];
            if (!long.TryParse(et, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exp))
                throw new ProgrammerException($"'{et}' is not an exponent");
        }
        int dot = mant.IndexOf('.');
        string digits = dot < 0 ? mant : mant[..dot] + mant[(dot + 1)..];
        if (digits.Length == 0 || mant.Count(c => c == '.') > 1) throw new ProgrammerException($"'{s}' is not a number");
        foreach (var c in digits)
            if (!char.IsAsciiDigit(c)) throw new ProgrammerException($"'{c}' cannot be used in a number (try 0.1, 1/3, inf, nan or 0x...)");
        var m = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (m.IsZero) return (BigInteger.Zero, BigInteger.One);
        exp -= dot < 0 ? 0 : mant.Length - dot - 1;
        // Beyond quad range: clamp so the exact arithmetic stays small; the result rounds the same way.
        int sigDigits = m.ToString(CultureInfo.InvariantCulture).Length;
        long sci = exp + sigDigits - 1;
        if (sci > 5000) exp = 5000 - sigDigits + 1;
        if (sci < -5100) exp = -5100 - sigDigits + 1;
        return exp >= 0 ? (m * BigInteger.Pow(10, (int)exp), BigInteger.One) : (m, BigInteger.Pow(10, (int)-exp));
    }

    // ---- neighbours ---------------------------------------------------------------------------------------

    /// <summary>The same value with the sign bit flipped.</summary>
    public IeeeFloat Negate() => new(Format, Bits ^ SignBit(Format));

    /// <summary>The value with one bit flipped (for the clickable bit grid).</summary>
    public IeeeFloat FlipBit(int index)
    {
        if (index < 0 || index >= Format.TotalBits) throw new ProgrammerException($"Bit {index} is outside the {Format.TotalBits}-bit pattern");
        return new IeeeFloat(Format, Bits ^ (BigInteger.One << index));
    }

    /// <summary>The next representable value toward +infinity (NaN stays NaN; +inf stays +inf).</summary>
    public IeeeFloat NextUp()
    {
        if (IsNaN) return this;
        if (Class == FloatClass.Infinity) return Sign ? new IeeeFloat(Format, Bits - 1) : this;
        if (Class == FloatClass.Zero) return new IeeeFloat(Format, BigInteger.One);
        return Sign ? new IeeeFloat(Format, Bits - 1) : new IeeeFloat(Format, Bits + 1);
    }

    /// <summary>The next representable value toward -infinity.</summary>
    public IeeeFloat NextDown() => Negate().NextUp().Negate();

    // ---- exact values ---------------------------------------------------------------------------------------

    /// <summary>The value as an exact fraction (finite values only). The denominator is a power of two.</summary>
    public (bool Negative, BigInteger Numerator, BigInteger Denominator) ToRational()
    {
        if (!IsFinite) throw new InvalidOperationException("Infinities and NaNs have no exact value");
        int k = UnbiasedExponent - (Format.Precision - 1);
        var m = Significand;
        return k >= 0 ? (Sign, m << k, BigInteger.One) : (Sign, m, BigInteger.One << -k);
    }

    /// <summary>
    /// The exact decimal value of the stored number, every digit (0.1 in double is
    /// 0.1000000000000000055511151231257827021181583404541015625). Specials give "inf", "-inf", "nan" or "-nan".
    /// </summary>
    public string ExactDecimal()
    {
        if (!IsFinite) return SpecialText();
        var (digits, pow) = ExactDigits();
        return (Sign ? "-" : "") + Positional(digits, pow);
    }

    /// <summary>
    /// The shortest decimal that reads back to exactly this value (0.1 for the double nearest 0.1).
    /// Very large or small values use scientific notation (1.5e-7).
    /// </summary>
    public string ShortestDecimal()
    {
        if (!IsFinite) return SpecialText();
        var (digits, pow) = ExactDigits();
        if (digits == "0") return Sign ? "-0" : "0";
        int max = (int)Math.Ceiling(Format.Precision * Math.Log10(2)) + 2;
        for (int d = 1; d <= max && d <= digits.Length; d++)
        {
            var (rd, rp) = RoundDigits(digits, pow, d);
            var back = FromRational(Format, Sign, rp >= 0 ? BigInteger.Parse(rd, CultureInfo.InvariantCulture) * BigInteger.Pow(10, rp) : BigInteger.Parse(rd, CultureInfo.InvariantCulture),
                rp >= 0 ? BigInteger.One : BigInteger.Pow(10, -rp));
            if (back.Bits == Bits) return (Sign ? "-" : "") + Pretty(rd, rp);
        }
        return (Sign ? "-" : "") + Pretty(digits, pow);
    }

    /// <summary>The distance to the next value away from zero (unit in the last place), as an exact decimal.</summary>
    public string? Ulp()
    {
        if (!IsFinite) return null;
        int k = UnbiasedExponent - (Format.Precision - 1);
        return PowerOfTwoDecimal(k);
    }

    /// <summary>The power of two of <see cref="Ulp"/> (for example -52 for 1.0 in double), or null for specials.</summary>
    public int? UlpExponent => IsFinite ? UnbiasedExponent - (Format.Precision - 1) : null;

    /// <summary>The significand in binary: 1.1001100... for normals, 0.0001... for subnormals.</summary>
    public string BinarySignificand()
    {
        var frac = BaseFormatter.ToDigits(Fraction, NumberBase.Bin).PadLeft(Format.FractionBits, '0');
        return (BiasedExponent == 0 ? "0." : "1.") + frac;
    }

    /// <summary>The bit pattern in hex, padded to the format width.</summary>
    public string Hex(bool group = true)
    {
        var d = BaseFormatter.ToDigits(Bits, NumberBase.Hex).PadLeft(Format.TotalBits / 4, '0');
        return group ? BaseFormatter.Group(d, 4) : d;
    }

    /// <summary>The bit pattern in binary, padded to the format width (no separators).</summary>
    public string Binary() => BaseFormatter.ToDigits(Bits, NumberBase.Bin).PadLeft(Format.TotalBits, '0');

    /// <summary>A short name for the class: "normal", "subnormal", "zero", "infinity", "quiet NaN", "signalling NaN".</summary>
    public string ClassName => Class switch
    {
        FloatClass.Zero => Sign ? "negative zero" : "zero",
        FloatClass.Subnormal => "subnormal",
        FloatClass.Normal => "normal",
        FloatClass.Infinity => Sign ? "negative infinity" : "infinity",
        FloatClass.QuietNaN => "quiet NaN",
        _ => "signalling NaN",
    };

    private string SpecialText() => Class == FloatClass.Infinity ? (Sign ? "-inf" : "inf") : (Sign ? "-nan" : "nan");

    /// <summary>Exact digits D and power P such that |value| = D × 10^P (D has no trailing zeros, or is "0").</summary>
    private (string Digits, int Pow) ExactDigits()
    {
        int k = UnbiasedExponent - (Format.Precision - 1);
        var m = Significand;
        if (m.IsZero) return ("0", 0);
        BigInteger d;
        int pow;
        if (k >= 0) { d = m << k; pow = 0; }
        else { d = m * BigInteger.Pow(5, -k); pow = k; }
        var s = d.ToString(CultureInfo.InvariantCulture);
        int trim = s.Length;
        while (trim > 1 && s[trim - 1] == '0') trim--;
        pow += s.Length - trim;
        return (s[..trim], pow);
    }

    private static (string Digits, int Pow) RoundDigits(string digits, int pow, int keep)
    {
        if (digits.Length <= keep) return (digits, pow);
        var head = digits[..keep].ToCharArray();
        char next = digits[keep];
        bool restNonZero = digits.AsSpan(keep + 1).ContainsAnyExcept('0');
        bool up = next > '5' || (next == '5' && (restNonZero || (head[^1] - '0') % 2 == 1));
        int newPow = pow + (digits.Length - keep);
        if (up)
        {
            int i = head.Length - 1;
            while (i >= 0 && head[i] == '9') { head[i] = '0'; i--; }
            if (i < 0) return ("1", newPow + keep);
            head[i]++;
        }
        var s = new string(head).TrimEnd('0');
        newPow += head.Length - s.Length;
        return (s.Length == 0 ? "0" : s, newPow);
    }

    private static string Positional(string digits, int pow)
    {
        if (digits == "0") return "0";
        if (pow >= 0) return digits + new string('0', pow);
        int point = digits.Length + pow;
        return point > 0 ? digits[..point] + "." + digits[point..] : "0." + new string('0', -point) + digits;
    }

    private static string Pretty(string digits, int pow)
    {
        int sci = digits.Length - 1 + pow;
        if (sci is >= -6 and < 21) return Positional(digits, pow);
        var sb = new StringBuilder();
        sb.Append(digits[0]);
        if (digits.Length > 1) sb.Append('.').Append(digits, 1, digits.Length - 1);
        sb.Append('e').Append(sci.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>2^k as an exact decimal string.</summary>
    public static string PowerOfTwoDecimal(int k)
    {
        if (k >= 0) return (BigInteger.One << k).ToString(CultureInfo.InvariantCulture);
        var d = BigInteger.Pow(5, -k).ToString(CultureInfo.InvariantCulture);
        return Positional(d, k);
    }

    private static BigInteger SignBit(IeeeFormat f) => BigInteger.One << (f.TotalBits - 1);

    private static BigInteger ExponentMask(IeeeFormat f) => new BigInteger(f.MaxBiasedExponent) << f.FractionBits;
}
