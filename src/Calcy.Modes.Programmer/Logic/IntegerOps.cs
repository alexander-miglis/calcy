using System.Numerics;

namespace Calcy.Modes.Programmer;

/// <summary>
/// Integer operations on a machine word (<see cref="IntegerFormat"/>). Inputs and results are canonical values
/// (signed formats use negative numbers for two's complement patterns with the top bit set).
/// Bitwise operations, shifts and rotates never overflow; arithmetic follows the format's <see cref="OverflowMode"/>.
/// </summary>
public static class IntegerOps
{
    // ---- overflow handling -------------------------------------------------------------------------------

    /// <summary>Fits an exact mathematical result into the word using the overflow mode.</summary>
    /// <exception cref="ProgrammerException">In <see cref="OverflowMode.Flag"/> mode when the result does not fit.</exception>
    public static BigInteger Fit(IntegerFormat f, BigInteger exact, out bool overflow)
    {
        overflow = !f.Fits(exact);
        if (!overflow) return exact;
        return f.Overflow switch
        {
            OverflowMode.Saturate => exact < f.MinValue ? f.MinValue : f.MaxValue,
            OverflowMode.Flag => throw new ProgrammerException($"Overflow: {exact} does not fit in {f.Describe()} (range {f.MinValue} to {f.MaxValue})"),
            _ => f.Wrap(exact),
        };
    }

    /// <summary>Fits an exact result into the word (see <see cref="Fit(IntegerFormat, BigInteger, out bool)"/>).</summary>
    public static BigInteger Fit(IntegerFormat f, BigInteger exact) => Fit(f, exact, out _);

    /// <summary>
    /// Value of a literal in the word. Hex, octal and binary literals that fit in n bits are bit patterns
    /// (0xFF is -1 in 8-bit signed); anything else must fit the range or is handled by the overflow mode.
    /// </summary>
    public static BigInteger Literal(IntegerFormat f, BigInteger value, bool isPattern, out bool overflow)
    {
        if (isPattern && value.Sign >= 0 && value <= f.Mask)
        {
            overflow = false;
            return f.FromPattern(value);
        }
        return Fit(f, value, out overflow);
    }

    /// <summary>Moves a value to another format, keeping the value where it fits and wrapping otherwise.</summary>
    public static BigInteger Convert(IntegerFormat to, BigInteger value) => to.Wrap(value);

    // ---- bitwise ------------------------------------------------------------------------------------------

    /// <summary>Bitwise AND.</summary>
    public static BigInteger And(IntegerFormat f, BigInteger a, BigInteger b) => f.FromPattern(f.ToPattern(a) & f.ToPattern(b));

    /// <summary>Bitwise OR.</summary>
    public static BigInteger Or(IntegerFormat f, BigInteger a, BigInteger b) => f.FromPattern(f.ToPattern(a) | f.ToPattern(b));

    /// <summary>Bitwise exclusive OR.</summary>
    public static BigInteger Xor(IntegerFormat f, BigInteger a, BigInteger b) => f.FromPattern(f.ToPattern(a) ^ f.ToPattern(b));

    /// <summary>Bitwise NOT (one's complement) of the whole word.</summary>
    public static BigInteger Not(IntegerFormat f, BigInteger a) => f.FromPattern(f.Mask ^ f.ToPattern(a));

    /// <summary>NOT (a AND b).</summary>
    public static BigInteger Nand(IntegerFormat f, BigInteger a, BigInteger b) => Not(f, And(f, a, b));

    /// <summary>NOT (a OR b).</summary>
    public static BigInteger Nor(IntegerFormat f, BigInteger a, BigInteger b) => Not(f, Or(f, a, b));

    /// <summary>NOT (a XOR b).</summary>
    public static BigInteger Xnor(IntegerFormat f, BigInteger a, BigInteger b) => Not(f, Xor(f, a, b));

    // ---- shifts and rotates -------------------------------------------------------------------------------

    private static int ShiftCount(IntegerFormat f, BigInteger count)
    {
        if (count.Sign < 0) throw new ProgrammerException($"Shift count must not be negative ({count})");
        return count >= f.Bits ? f.Bits : (int)count;
    }

    private static int RotateCount(BigInteger count, int width)
    {
        var r = BigInteger.Remainder(count, width);
        if (r.Sign < 0) r += width;
        return (int)r;
    }

    /// <summary>Logical shift left; bits shifted past the top are lost. Shifting by n or more gives 0.</summary>
    public static BigInteger ShiftLeft(IntegerFormat f, BigInteger a, BigInteger count)
    {
        int k = ShiftCount(f, count);
        return k >= f.Bits ? BigInteger.Zero : f.FromPattern(f.ToPattern(a) << k);
    }

    /// <summary>Logical shift right: zeros come in at the top. Shifting by n or more gives 0.</summary>
    public static BigInteger ShiftRightLogical(IntegerFormat f, BigInteger a, BigInteger count)
    {
        int k = ShiftCount(f, count);
        return k >= f.Bits ? BigInteger.Zero : f.FromPattern(f.ToPattern(a) >> k);
    }

    /// <summary>Arithmetic shift right: copies of the top bit come in. Shifting by n or more gives 0 or all ones.</summary>
    public static BigInteger ShiftRightArithmetic(IntegerFormat f, BigInteger a, BigInteger count)
    {
        int k = ShiftCount(f, count);
        var p = f.ToPattern(a);
        bool top = !(p >> (f.Bits - 1)).IsZero;
        if (k >= f.Bits) return f.FromPattern(top ? f.Mask : BigInteger.Zero);
        var shifted = p >> k;
        if (top) shifted |= f.Mask ^ (f.Mask >> k);
        return f.FromPattern(shifted);
    }

    /// <summary>The C operator &gt;&gt;: arithmetic for signed words, logical for unsigned words.</summary>
    public static BigInteger ShiftRight(IntegerFormat f, BigInteger a, BigInteger count) =>
        f.Signed ? ShiftRightArithmetic(f, a, count) : ShiftRightLogical(f, a, count);

    /// <summary>Rotate left by count bits (count is taken modulo n; negative rotates right).</summary>
    public static BigInteger RotateLeft(IntegerFormat f, BigInteger a, BigInteger count)
    {
        int k = RotateCount(count, f.Bits);
        if (k == 0) return f.Wrap(a);
        var p = f.ToPattern(a);
        return f.FromPattern((p << k) | (p >> (f.Bits - k)));
    }

    /// <summary>Rotate right by count bits (count is taken modulo n; negative rotates left).</summary>
    public static BigInteger RotateRight(IntegerFormat f, BigInteger a, BigInteger count) =>
        RotateLeft(f, a, -count);

    /// <summary>
    /// Rotate left through carry (x86 RCL): the word and the carry flag form an (n+1)-bit ring.
    /// The carry goes into bit 0 and the top bit goes into the carry.
    /// </summary>
    public static BigInteger RotateLeftThroughCarry(IntegerFormat f, BigInteger a, BigInteger count, ref bool carry)
    {
        int width = f.Bits + 1;
        int k = RotateCount(count, width);
        var ringMask = (BigInteger.One << width) - 1;
        var ring = f.ToPattern(a) | (carry ? BigInteger.One << f.Bits : BigInteger.Zero);
        if (k != 0) ring = ((ring << k) | (ring >> (width - k))) & ringMask;
        carry = !(ring >> f.Bits).IsZero;
        return f.FromPattern(ring);
    }

    /// <summary>Rotate right through carry (x86 RCR): the carry goes into the top bit and bit 0 goes into the carry.</summary>
    public static BigInteger RotateRightThroughCarry(IntegerFormat f, BigInteger a, BigInteger count, ref bool carry) =>
        RotateLeftThroughCarry(f, a, -count, ref carry);

    // ---- arithmetic ---------------------------------------------------------------------------------------

    /// <summary>a + b.</summary>
    public static BigInteger Add(IntegerFormat f, BigInteger a, BigInteger b, out bool overflow) => Fit(f, a + b, out overflow);

    /// <summary>a + b.</summary>
    public static BigInteger Add(IntegerFormat f, BigInteger a, BigInteger b) => Fit(f, a + b);

    /// <summary>a - b.</summary>
    public static BigInteger Subtract(IntegerFormat f, BigInteger a, BigInteger b, out bool overflow) => Fit(f, a - b, out overflow);

    /// <summary>a - b.</summary>
    public static BigInteger Subtract(IntegerFormat f, BigInteger a, BigInteger b) => Fit(f, a - b);

    /// <summary>a × b.</summary>
    public static BigInteger Multiply(IntegerFormat f, BigInteger a, BigInteger b, out bool overflow) => Fit(f, a * b, out overflow);

    /// <summary>a × b.</summary>
    public static BigInteger Multiply(IntegerFormat f, BigInteger a, BigInteger b) => Fit(f, a * b);

    /// <summary>Division truncating toward zero, like C. In signed words MIN / -1 overflows.</summary>
    public static BigInteger Divide(IntegerFormat f, BigInteger a, BigInteger b, out bool overflow)
    {
        if (b.IsZero) throw new ProgrammerException("Division by zero");
        return Fit(f, BigInteger.Divide(a, b), out overflow);
    }

    /// <summary>Division truncating toward zero, like C.</summary>
    public static BigInteger Divide(IntegerFormat f, BigInteger a, BigInteger b) => Divide(f, a, b, out _);

    /// <summary>Remainder with the sign of the dividend, like C's %. Never overflows (MIN % -1 is 0).</summary>
    public static BigInteger Remainder(IntegerFormat f, BigInteger a, BigInteger b)
    {
        if (b.IsZero) throw new ProgrammerException("Division by zero (remainder)");
        return BigInteger.Remainder(a, b);
    }

    /// <summary>-a. In signed words -MIN overflows; in unsigned words any non-zero value does.</summary>
    public static BigInteger Negate(IntegerFormat f, BigInteger a, out bool overflow) => Fit(f, -a, out overflow);

    /// <summary>-a.</summary>
    public static BigInteger Negate(IntegerFormat f, BigInteger a) => Fit(f, -a);

    /// <summary>|a|. In signed words |MIN| overflows.</summary>
    public static BigInteger Abs(IntegerFormat f, BigInteger a, out bool overflow) => Fit(f, BigInteger.Abs(a), out overflow);

    // ---- bit counting and reordering ------------------------------------------------------------------------

    /// <summary>Number of set bits.</summary>
    public static int PopCount(IntegerFormat f, BigInteger a)
    {
        var p = f.ToPattern(a);
        int count = 0;
        foreach (var b in p.ToByteArray(isUnsigned: true)) count += BitOperations.PopCount(b);
        return count;
    }

    /// <summary>Number of zero bits above the highest set bit (n for zero).</summary>
    public static int LeadingZeros(IntegerFormat f, BigInteger a)
    {
        var p = f.ToPattern(a);
        return p.IsZero ? f.Bits : f.Bits - (int)p.GetBitLength();
    }

    /// <summary>Number of zero bits below the lowest set bit (n for zero).</summary>
    public static int TrailingZeros(IntegerFormat f, BigInteger a)
    {
        var p = f.ToPattern(a);
        if (p.IsZero) return f.Bits;
        int count = 0;
        while (p.IsEven) { p >>= 1; count++; }
        return count;
    }

    /// <summary>1 when the number of set bits is odd, else 0.</summary>
    public static int Parity(IntegerFormat f, BigInteger a) => PopCount(f, a) & 1;

    /// <summary>Reverses the order of all n bits (bit 0 swaps with bit n-1).</summary>
    public static BigInteger BitReverse(IntegerFormat f, BigInteger a)
    {
        var p = f.ToPattern(a);
        var r = BigInteger.Zero;
        for (int i = 0; i < f.Bits; i++)
        {
            r <<= 1;
            if (!(p & BigInteger.One).IsZero) r |= BigInteger.One;
            p >>= 1;
        }
        return f.FromPattern(r);
    }

    /// <summary>Reverses the byte order (endianness swap). The word size must be a multiple of 8.</summary>
    public static BigInteger ByteSwap(IntegerFormat f, BigInteger a)
    {
        if (f.Bits % 8 != 0) throw new ProgrammerException($"Byte swap needs a word size that is a multiple of 8 bits (this word has {f.Bits})");
        var bytes = ToBytesLittleEndian(f, a);
        Array.Reverse(bytes);
        return f.FromPattern(new BigInteger(bytes, isUnsigned: true, isBigEndian: false));
    }

    /// <summary>The word as bytes, least significant first (the low n bits, rounded up to whole bytes).</summary>
    public static byte[] ToBytesLittleEndian(IntegerFormat f, BigInteger a)
    {
        int count = (f.Bits + 7) / 8;
        var raw = f.ToPattern(a).ToByteArray(isUnsigned: true, isBigEndian: false);
        var bytes = new byte[count];
        Array.Copy(raw, bytes, Math.Min(raw.Length, count));
        return bytes;
    }

    // ---- bit fields and masks --------------------------------------------------------------------------------

    private static void CheckBit(IntegerFormat f, BigInteger index, string what)
    {
        if (index.Sign < 0 || index >= f.Bits) throw new ProgrammerException($"{what} {index} is outside the word (bits 0 to {f.Bits - 1})");
    }

    private static (int Hi, int Lo) CheckRange(IntegerFormat f, BigInteger hi, BigInteger lo)
    {
        CheckBit(f, hi, "Bit");
        CheckBit(f, lo, "Bit");
        if (hi < lo) (hi, lo) = (lo, hi);
        return ((int)hi, (int)lo);
    }

    /// <summary>True when bit <paramref name="index"/> is set.</summary>
    public static bool GetBit(IntegerFormat f, BigInteger a, int index)
    {
        CheckBit(f, index, "Bit");
        return !((f.ToPattern(a) >> index) & BigInteger.One).IsZero;
    }

    /// <summary>The value with bit <paramref name="index"/> set or cleared.</summary>
    public static BigInteger SetBit(IntegerFormat f, BigInteger a, int index, bool on)
    {
        CheckBit(f, index, "Bit");
        var bit = BigInteger.One << index;
        var p = f.ToPattern(a);
        return f.FromPattern(on ? p | bit : p & ~bit);
    }

    /// <summary>The value with bit <paramref name="index"/> flipped.</summary>
    public static BigInteger FlipBit(IntegerFormat f, BigInteger a, int index)
    {
        CheckBit(f, index, "Bit");
        return f.FromPattern(f.ToPattern(a) ^ (BigInteger.One << index));
    }

    /// <summary>The low <paramref name="count"/> bits set (0 to n).</summary>
    public static BigInteger Mask(IntegerFormat f, BigInteger count)
    {
        if (count.Sign < 0 || count > f.Bits) throw new ProgrammerException($"Mask width must be 0 to {f.Bits}, not {count}");
        return f.FromPattern((BigInteger.One << (int)count) - 1);
    }

    /// <summary>Bits <paramref name="hi"/> down to <paramref name="lo"/> set (inclusive).</summary>
    public static BigInteger RangeMask(IntegerFormat f, BigInteger hi, BigInteger lo)
    {
        var (h, l) = CheckRange(f, hi, lo);
        return f.FromPattern(((BigInteger.One << (h - l + 1)) - 1) << l);
    }

    /// <summary>The unsigned value of bits <paramref name="hi"/> down to <paramref name="lo"/> (inclusive).</summary>
    public static BigInteger ExtractField(IntegerFormat f, BigInteger a, BigInteger hi, BigInteger lo)
    {
        var (h, l) = CheckRange(f, hi, lo);
        var field = (f.ToPattern(a) >> l) & ((BigInteger.One << (h - l + 1)) - 1);
        return f.Wrap(field);
    }

    /// <summary>The value with bits <paramref name="hi"/> down to <paramref name="lo"/> replaced by the low bits of <paramref name="field"/>.</summary>
    public static BigInteger InsertField(IntegerFormat f, BigInteger a, BigInteger hi, BigInteger lo, BigInteger field)
    {
        var (h, l) = CheckRange(f, hi, lo);
        var width = (BigInteger.One << (h - l + 1)) - 1;
        var m = width << l;
        var p = (f.ToPattern(a) & (f.Mask ^ m)) | ((field & width) << l);
        return f.FromPattern(p);
    }

    /// <summary>Sign extension: treats the low <paramref name="fromBits"/> bits as a signed number and widens it to the word.</summary>
    public static BigInteger SignExtend(IntegerFormat f, BigInteger a, BigInteger fromBits)
    {
        if (fromBits < 1 || fromBits > f.Bits) throw new ProgrammerException($"Sign extension width must be 1 to {f.Bits}, not {fromBits}");
        int b = (int)fromBits;
        var low = f.ToPattern(a) & ((BigInteger.One << b) - 1);
        if (!(low >> (b - 1)).IsZero) low -= BigInteger.One << b;
        return f.Wrap(low);
    }

    /// <summary>Zero extension: keeps the low <paramref name="fromBits"/> bits and clears the rest.</summary>
    public static BigInteger ZeroExtend(IntegerFormat f, BigInteger a, BigInteger fromBits)
    {
        if (fromBits < 1 || fromBits > f.Bits) throw new ProgrammerException($"Zero extension width must be 1 to {f.Bits}, not {fromBits}");
        return f.Wrap(f.ToPattern(a) & ((BigInteger.One << (int)fromBits) - 1));
    }
}
