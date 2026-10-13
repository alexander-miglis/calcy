using System.Numerics;

namespace Calcy.Modes.Programmer;

/// <summary>What happens when an arithmetic result does not fit in the word.</summary>
public enum OverflowMode
{
    /// <summary>Two's complement wrap-around (what CPUs do). The default.</summary>
    Wrap,

    /// <summary>Clamp to the smallest or largest value of the word.</summary>
    Saturate,

    /// <summary>Report the overflow as an error instead of producing a value.</summary>
    Flag,
}

/// <summary>The input and display base of the integer screen.</summary>
public enum NumberBase
{
    /// <summary>Base 2.</summary>
    Bin = 2,

    /// <summary>Base 8.</summary>
    Oct = 8,

    /// <summary>Base 10.</summary>
    Dec = 10,

    /// <summary>Base 16.</summary>
    Hex = 16,
}

/// <summary>An error the user can fix: a typo, a division by zero, an overflow in flag mode.</summary>
public sealed class ProgrammerException : Exception
{
    /// <summary>Creates the error with a message written for the user.</summary>
    public ProgrammerException(string message, int position = -1, int length = 0) : base(message)
    {
        Position = position;
        Length = length;
    }

    /// <summary>Character position in the input where the problem is, or -1.</summary>
    public int Position { get; }

    /// <summary>Length of the offending text (0 when unknown).</summary>
    public int Length { get; }
}

/// <summary>
/// A machine word: width in bits (1 to 256), signed (two's complement) or unsigned, and the overflow mode.
/// Values are kept as <see cref="BigInteger"/> in canonical form: in [MinValue, MaxValue] for this format.
/// </summary>
public readonly record struct IntegerFormat
{
    /// <summary>The widest supported word.</summary>
    public const int MaxBits = 256;

    /// <summary>Creates a format; throws when the width is outside 1 to 256.</summary>
    public IntegerFormat(int bits, bool signed, OverflowMode overflow = OverflowMode.Wrap)
    {
        if (bits is < 1 or > MaxBits) throw new ProgrammerException($"Word size must be 1 to {MaxBits} bits, not {bits}");
        Bits = bits;
        Signed = signed;
        Overflow = overflow;
    }

    /// <summary>Word width in bits.</summary>
    public int Bits { get; }

    /// <summary>True for two's complement signed, false for unsigned.</summary>
    public bool Signed { get; }

    /// <summary>What arithmetic does when a result does not fit.</summary>
    public OverflowMode Overflow { get; }

    /// <summary>2 to the power <see cref="Bits"/>.</summary>
    public BigInteger Modulus => BigInteger.One << Bits;

    /// <summary>All bits of the word set.</summary>
    public BigInteger Mask => Modulus - 1;

    /// <summary>The smallest value: -2^(n-1) when signed, 0 when unsigned.</summary>
    public BigInteger MinValue => Signed ? -(BigInteger.One << (Bits - 1)) : BigInteger.Zero;

    /// <summary>The largest value: 2^(n-1)-1 when signed, 2^n-1 when unsigned.</summary>
    public BigInteger MaxValue => Signed ? (BigInteger.One << (Bits - 1)) - 1 : Mask;

    /// <summary>Common presets shown as buttons.</summary>
    public static IReadOnlyList<int> Presets { get; } = [8, 16, 32, 64, 128];

    /// <summary>The same word with another width (other settings kept).</summary>
    public IntegerFormat WithBits(int bits) => new(bits, Signed, Overflow);

    /// <summary>The same word, signed or unsigned.</summary>
    public IntegerFormat WithSigned(bool signed) => new(Bits, signed, Overflow);

    /// <summary>The same word with another overflow mode.</summary>
    public IntegerFormat WithOverflow(OverflowMode overflow) => new(Bits, Signed, overflow);

    /// <summary>True when the value is representable without overflow.</summary>
    public bool Fits(BigInteger value) => value >= MinValue && value <= MaxValue;

    /// <summary>The bit pattern of a value (0 to 2^n-1); negative values give their two's complement.</summary>
    public BigInteger ToPattern(BigInteger value) => value & Mask;

    /// <summary>The canonical value of a bit pattern (only the low n bits are used).</summary>
    public BigInteger FromPattern(BigInteger pattern)
    {
        var p = pattern & Mask;
        return Signed && !(p >> (Bits - 1)).IsZero ? p - Modulus : p;
    }

    /// <summary>Reduces any integer modulo 2^n into the canonical range (wrap-around).</summary>
    public BigInteger Wrap(BigInteger value) => FromPattern(value);

    /// <summary>Text such as "16-bit signed".</summary>
    public string Describe() => $"{Bits}-bit {(Signed ? "signed" : "unsigned")}";

    /// <inheritdoc />
    public override string ToString() => Describe();
}
