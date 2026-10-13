using System.Globalization;
using System.Text;

namespace Calcy.Modes.Programmer;

/// <summary>Checksums of byte sequences.</summary>
public static class Checksums
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>CRC-32 (IEEE 802.3, as in zip and PNG): reflected polynomial 0xEDB88320, initial and final XOR 0xFFFFFFFF.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Adler-32 (as in zlib): two sums modulo 65521.</summary>
    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        foreach (var x in data)
        {
            a = (a + x) % Mod;
            b = (b + a) % Mod;
        }
        return (b << 16) | a;
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
}

/// <summary>Byte order and width of a Unicode encoding for <see cref="TextCodec"/>.</summary>
public enum TextEncodingKind
{
    /// <summary>UTF-8.</summary>
    Utf8,

    /// <summary>UTF-16, most significant byte first.</summary>
    Utf16BigEndian,

    /// <summary>UTF-16, least significant byte first (Windows).</summary>
    Utf16LittleEndian,
}

/// <summary>One code point of a text with its encodings.</summary>
/// <param name="Display">What to show for the character (control characters get names such as LF).</param>
/// <param name="CodePoint">The Unicode scalar value, or the lone surrogate code unit.</param>
/// <param name="Utf8">UTF-8 bytes in hex ("E2 82 AC"), or "invalid" for a lone surrogate.</param>
/// <param name="Utf16">UTF-16 code units in hex ("20AC", "D83D DE00").</param>
/// <param name="IsLoneSurrogate">True for an unpaired surrogate (not a valid character).</param>
public sealed record CharInfo(string Display, int CodePoint, string Utf8, string Utf16, bool IsLoneSurrogate)
{
    /// <summary>"U+20AC".</summary>
    public string UPlus => "U+" + CodePoint.ToString("X4", CultureInfo.InvariantCulture);
}

/// <summary>Characters, code points and their UTF-8 and UTF-16 bytes, both ways.</summary>
public static class TextCodec
{
    private static readonly string[] ControlNames =
    [
        "NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "TAB", "LF", "VT", "FF", "CR", "SO", "SI",
        "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US",
    ];

    /// <summary>Short name for a control character or space (NUL, LF, SP, DEL), or null for printable characters.</summary>
    public static string? ControlName(int codePoint) => codePoint switch
    {
        < 0x20 and >= 0 => ControlNames[codePoint],
        0x20 => "SP",
        0x7F => "DEL",
        0xA0 => "NBSP",
        _ => null,
    };

    /// <summary>Splits text into code points with their encodings (surrogate pairs become one code point).</summary>
    public static IReadOnlyList<CharInfo> Analyze(string text)
    {
        var list = new List<CharInfo>();
        int i = 0;
        while (i < text.Length)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out int used) == System.Buffers.OperationStatus.Done)
            {
                var utf8 = new byte[4];
                int n = rune.EncodeToUtf8(utf8);
                var units = text.Substring(i, used);
                list.Add(new CharInfo(ControlName(rune.Value) ?? rune.ToString(), rune.Value, Hex(utf8.AsSpan(0, n)), Units(units), false));
                i += used;
            }
            else
            {
                int unit = text[i];
                list.Add(new CharInfo("�", unit, "invalid", unit.ToString("X4", CultureInfo.InvariantCulture), true));
                i++;
            }
        }
        return list;
    }

    /// <summary>The text encoded as bytes. Lone surrogates become U+FFFD (EF BF BD in UTF-8).</summary>
    public static byte[] Encode(string text, TextEncodingKind kind) => Encoding(kind, strict: false).GetBytes(text);

    /// <summary>Bytes as upper-case hex pairs separated by spaces.</summary>
    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads hex bytes in common forms: "E2 82 AC", "E282AC", "0xE2, 0x82, 0xAC", "\xE2\x82\xAC", "e2-82-ac".
    /// </summary>
    public static byte[] ParseHexBytes(string text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return [];
        s = s.Replace("\\x", " ", StringComparison.OrdinalIgnoreCase).Replace("0x", " ", StringComparison.OrdinalIgnoreCase);
        var parts = s.Split([' ', ',', ';', ':', '-', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var bytes = new List<byte>();
        foreach (var part in parts)
        {
            foreach (var c in part)
                if (!char.IsAsciiHexDigit(c)) throw new ProgrammerException($"'{c}' is not a hex digit");
            if (part.Length == 1 && parts.Length > 1)
            {
                bytes.Add(byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                continue;
            }
            if (part.Length % 2 != 0) throw new ProgrammerException($"'{part}' has an odd number of hex digits; write bytes as pairs such as 0A");
            for (int k = 0; k < part.Length; k += 2)
                bytes.Add(byte.Parse(part.AsSpan(k, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        return [.. bytes];
    }

    /// <summary>Decodes bytes strictly; invalid sequences raise an error that says where.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, TextEncodingKind kind)
    {
        if (kind != TextEncodingKind.Utf8 && bytes.Length % 2 != 0) throw new ProgrammerException($"UTF-16 needs an even number of bytes (there are {bytes.Length})");
        try
        {
            return Encoding(kind, strict: true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            var at = ex.Index >= 0 ? $" at byte {ex.Index + 1}" : "";
            var seen = ex.BytesUnknown is { Length: > 0 } u ? $" ({Hex(u)})" : "";
            throw new ProgrammerException($"Not valid {Name(kind)}{at}{seen}");
        }
    }

    /// <summary>"UTF-8", "UTF-16 BE" or "UTF-16 LE".</summary>
    public static string Name(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf16BigEndian => "UTF-16 BE",
        TextEncodingKind.Utf16LittleEndian => "UTF-16 LE",
        _ => "UTF-8",
    };

    private static Encoding Encoding(TextEncodingKind kind, bool strict) => kind switch
    {
        TextEncodingKind.Utf16BigEndian => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: strict),
        TextEncodingKind.Utf16LittleEndian => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: strict),
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: strict),
    };

    private static string Units(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
