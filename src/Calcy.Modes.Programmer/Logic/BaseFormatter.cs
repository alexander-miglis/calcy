using System.Numerics;
using System.Text;

namespace Calcy.Modes.Programmer;

/// <summary>Converts word values to text in hex, decimal, octal and binary, with digit grouping, and parses digits back.</summary>
public static class BaseFormatter
{
    private const string Digits = "0123456789ABCDEF";

    /// <summary>Short upper-case name: HEX, DEC, OCT or BIN.</summary>
    public static string Name(NumberBase b) => b switch
    {
        NumberBase.Hex => "HEX",
        NumberBase.Oct => "OCT",
        NumberBase.Bin => "BIN",
        _ => "DEC",
    };

    /// <summary>The C-style prefix for a base ("0x", "0o", "0b"; empty for decimal).</summary>
    public static string Prefix(NumberBase b) => b switch
    {
        NumberBase.Hex => "0x",
        NumberBase.Oct => "0o",
        NumberBase.Bin => "0b",
        _ => "",
    };

    /// <summary>True when <paramref name="c"/> is a digit of the base (either case for hex).</summary>
    public static bool IsDigit(char c, NumberBase b)
    {
        int v = DigitValue(c);
        return v >= 0 && v < (int)b;
    }

    /// <summary>Value of a digit character 0-9, A-F (either case), or -1.</summary>
    public static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };

    /// <summary>Unsigned digits of a non-negative integer, without grouping or prefix.</summary>
    public static string ToDigits(BigInteger value, NumberBase b)
    {
        if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value), "Digits are only defined for non-negative numbers");
        if (value.IsZero) return "0";
        if (b == NumberBase.Dec) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int bitsPerDigit = b switch { NumberBase.Hex => 4, NumberBase.Oct => 3, _ => 1 };
        int mask = (int)b - 1;
        var sb = new StringBuilder();
        while (!value.IsZero)
        {
            sb.Insert(0, Digits[(int)(value & mask)]);
            value >>= bitsPerDigit;
        }
        return sb.ToString();
    }

    /// <summary>Inserts <paramref name="separator"/> every <paramref name="size"/> characters, counting from the right.</summary>
    public static string Group(string digits, int size, char separator = ' ')
    {
        if (size <= 0 || digits.Length <= size) return digits;
        var sb = new StringBuilder(digits.Length + digits.Length / size);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % size == 0) sb.Append(separator);
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The value as shown in the value panel: hex in groups of 4, binary padded to the word size in groups of 4,
    /// octal as plain digits, decimal with its sign (signed words show negative numbers).
    /// Hex, octal and binary always show the two's complement bit pattern.
    /// </summary>
    public static string Format(IntegerFormat f, BigInteger value, NumberBase b, bool group = true)
    {
        switch (b)
        {
            case NumberBase.Dec:
                return f.Wrap(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case NumberBase.Hex:
                {
                    var d = ToDigits(f.ToPattern(value), b);
                    return group ? Group(d, 4) : d;
                }
            case NumberBase.Bin:
                {
                    var d = ToDigits(f.ToPattern(value), b).PadLeft(f.Bits, '0');
                    return group ? Group(d, 4) : d;
                }
            default:
                return ToDigits(f.ToPattern(value), b);
        }
    }

    /// <summary>
    /// A form that can be typed back in: prefix plus digits, long values grouped with underscores
    /// (for example 0xDEAD_BEEF, 0b1010_0101, 0o377, -42).
    /// </summary>
    public static string FormatLiteral(IntegerFormat f, BigInteger value, NumberBase b)
    {
        if (b == NumberBase.Dec) return f.Wrap(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var d = ToDigits(f.ToPattern(value), b);
        int groupSize = b == NumberBase.Oct ? 0 : 4;
        if (groupSize > 0 && d.Length > 8) d = Group(d, groupSize, '_');
        return Prefix(b) + d;
    }

    /// <summary>Parses digits in a base (underscores between digits are allowed). Throws on a bad digit.</summary>
    public static BigInteger ParseDigits(string digits, NumberBase b)
    {
        if (string.IsNullOrEmpty(digits)) throw new ProgrammerException("A number needs at least one digit");
        var value = BigInteger.Zero;
        bool any = false;
        for (int i = 0; i < digits.Length; i++)
        {
            char c = digits[i];
            if (c == '_')
            {
                if (!any || i == digits.Length - 1 || digits[i + 1] == '_') throw new ProgrammerException("Underscores must sit between digits, as in 1_000", i, 1);
                continue;
            }
            int v = DigitValue(c);
            if (v < 0 || v >= (int)b) throw new ProgrammerException($"'{c}' is not a {DigitWord(b)} digit", i, 1);
            value = value * (int)b + v;
            any = true;
        }
        return value;
    }

    /// <summary>True when every character is a digit of the base or a separating underscore.</summary>
    public static bool AllDigits(ReadOnlySpan<char> text, NumberBase b)
    {
        if (text.IsEmpty || text[0] == '_' || text[^1] == '_') return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '_') { if (text[i - 1] == '_') return false; continue; }
            if (!IsDigit(c, b)) return false;
        }
        return true;
    }

    /// <summary>"hex", "decimal", "octal" or "binary".</summary>
    public static string DigitWord(NumberBase b) => b switch
    {
        NumberBase.Hex => "hex",
        NumberBase.Oct => "octal",
        NumberBase.Bin => "binary",
        _ => "decimal",
    };
}
