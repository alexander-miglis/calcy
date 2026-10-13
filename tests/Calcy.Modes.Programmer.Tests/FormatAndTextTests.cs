using System.Numerics;
using System.Text;

namespace Calcy.Modes.Programmer.Tests;

public class FormatterTests
{
    private static readonly IntegerFormat S16 = new(16, true);

    [Theory]
    [InlineData(NumberBase.Hex, "B5")]
    [InlineData(NumberBase.Dec, "181")]
    [InlineData(NumberBase.Oct, "265")]
    [InlineData(NumberBase.Bin, "0000 0000 1011 0101")]
    public void Value_rows(NumberBase b, string expected) => Assert.Equal(expected, BaseFormatter.Format(S16, 0xB5, b));

    [Fact]
    public void Negative_values_show_their_pattern_except_in_decimal()
    {
        var f = new IntegerFormat(8, true);
        Assert.Equal("FF", BaseFormatter.Format(f, -1, NumberBase.Hex));
        Assert.Equal("-1", BaseFormatter.Format(f, -1, NumberBase.Dec));
        Assert.Equal("377", BaseFormatter.Format(f, -1, NumberBase.Oct));
        Assert.Equal("1111 1111", BaseFormatter.Format(f, -1, NumberBase.Bin));
    }

    [Fact]
    public void Grouping_and_literals()
    {
        Assert.Equal("1 2345", BaseFormatter.Format(new IntegerFormat(32, false), 0x12345, NumberBase.Hex));
        Assert.Equal("18446744073709551615", BaseFormatter.Format(new IntegerFormat(64, false), BigInteger.Pow(2, 64) - 1, NumberBase.Dec));
        Assert.Equal("0xDEADBEEF", BaseFormatter.FormatLiteral(new IntegerFormat(32, false), 0xDEADBEEF, NumberBase.Hex));
        Assert.Equal("0x1_0000_0000", BaseFormatter.FormatLiteral(new IntegerFormat(64, false), BigInteger.Pow(2, 32), NumberBase.Hex));
        Assert.Equal("0b101", BaseFormatter.FormatLiteral(S16, 5, NumberBase.Bin));
        Assert.Equal("0o17", BaseFormatter.FormatLiteral(S16, 15, NumberBase.Oct));
        Assert.Equal("-5", BaseFormatter.FormatLiteral(S16, -5, NumberBase.Dec));
        Assert.Equal(new string('0', 256), BaseFormatter.Format(new IntegerFormat(256, false), 0, NumberBase.Bin, group: false));
    }

    [Fact]
    public void Parsing_digits()
    {
        Assert.Equal(1000, BaseFormatter.ParseDigits("1_000", NumberBase.Dec));
        Assert.Equal(0xBEEF, BaseFormatter.ParseDigits("beef", NumberBase.Hex));
        Assert.Throws<ProgrammerException>(() => BaseFormatter.ParseDigits("12", NumberBase.Bin));
        Assert.Throws<ProgrammerException>(() => BaseFormatter.ParseDigits("_1", NumberBase.Dec));
    }
}

public class FixedPointTests
{
    [Fact]
    public void Q1_14_round_trip()
    {
        var q = new FixedPointFormat(1, 14, signed: true);
        Assert.Equal(16, q.TotalBits);
        Assert.Equal("Q1.14", q.Name);
        var v = q.Parse("0.75");
        Assert.Equal(new BigInteger(0x3000), v.Raw);
        Assert.Equal("0x3000", v.RawHex);
        Assert.Equal("0.75", v.ExactDecimal());
        Assert.Equal("0 (exact)", v.ErrorText);
        Assert.Equal("1", q.Parse("0x4000").ExactDecimal());
        Assert.Equal("-2", q.MinText);
        Assert.Equal("1.99993896484375", q.MaxText);
        Assert.Equal("0.00006103515625", q.ResolutionText);
    }

    [Fact]
    public void Q15_saturates_at_one()
    {
        var q = new FixedPointFormat(0, 15, signed: true);
        var minus = q.Parse("-1");
        Assert.Equal(new BigInteger(-32768), minus.Raw);
        Assert.Equal("0x8000", minus.RawHex);
        var one = q.Parse("1");
        Assert.True(one.Saturated);
        Assert.Equal("0.999969482421875", one.ExactDecimal());
    }

    [Fact]
    public void Unsigned_and_rounding()
    {
        var uq = new FixedPointFormat(8, 8, signed: false);
        Assert.Equal("UQ8.8", uq.Name);
        Assert.Equal(new BigInteger(0x0180), uq.Parse("1.5").Raw);
        var tiny = new FixedPointFormat(0, 2, signed: false);
        Assert.Equal("0.5", tiny.Parse("0.375").ExactDecimal());
        Assert.Equal("0", tiny.Parse("0.125").ExactDecimal());
        Assert.Equal("0.25", tiny.Parse("0.3").ExactDecimal());
        var third = new FixedPointFormat(1, 14, true).Parse("1/3");
        Assert.Equal(new BigInteger(5461), third.Raw);
        Assert.StartsWith("-0.0000203", third.ErrorText);
        Assert.Throws<ProgrammerException>(() => new FixedPointFormat(200, 100, true));
        Assert.Throws<ProgrammerException>(() => uq.Parse("inf"));
    }
}

public class TextCodecTests
{
    [Fact]
    public void Euro_sign_encodings()
    {
        Assert.Equal("E2 82 AC", TextCodec.Hex(TextCodec.Encode("€", TextEncodingKind.Utf8)));
        Assert.Equal("20 AC", TextCodec.Hex(TextCodec.Encode("€", TextEncodingKind.Utf16BigEndian)));
        Assert.Equal("AC 20", TextCodec.Hex(TextCodec.Encode("€", TextEncodingKind.Utf16LittleEndian)));
        var c = Assert.Single(TextCodec.Analyze("€"));
        Assert.Equal(0x20AC, c.CodePoint);
        Assert.Equal("U+20AC", c.UPlus);
        Assert.Equal("E2 82 AC", c.Utf8);
        Assert.Equal("20AC", c.Utf16);
    }

    [Fact]
    public void Astral_characters_are_one_code_point()
    {
        var c = Assert.Single(TextCodec.Analyze("\U0001F600"));
        Assert.Equal(0x1F600, c.CodePoint);
        Assert.Equal("F0 9F 98 80", c.Utf8);
        Assert.Equal("D83D DE00", c.Utf16);
    }

    [Fact]
    public void Control_characters_get_names_and_lone_surrogates_are_flagged()
    {
        var list = TextCodec.Analyze("A\n \uD800");
        Assert.Equal(4, list.Count);
        Assert.Equal("A", list[0].Display);
        Assert.Equal("LF", list[1].Display);
        Assert.Equal("SP", list[2].Display);
        Assert.True(list[3].IsLoneSurrogate);
        Assert.Equal("invalid", list[3].Utf8);
    }

    [Fact]
    public void Crc32_and_adler32()
    {
        Assert.Equal(0xCBF43926u, Checksums.Crc32(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0x11E60398u, Checksums.Adler32(Encoding.ASCII.GetBytes("Wikipedia")));
        Assert.Equal(0u, Checksums.Crc32([]));
        Assert.Equal(1u, Checksums.Adler32([]));
        Assert.Equal(0x414FA339u, Checksums.Crc32(Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog")));
    }

    [Theory]
    [InlineData("E2 82 AC")]
    [InlineData("E282AC")]
    [InlineData("0xE2, 0x82, 0xAC")]
    [InlineData("\\xE2\\x82\\xAC")]
    [InlineData("e2-82-ac")]
    public void Hex_bytes_in_many_forms(string text)
    {
        var bytes = TextCodec.ParseHexBytes(text);
        Assert.Equal(new byte[] { 0xE2, 0x82, 0xAC }, bytes);
        Assert.Equal("€", TextCodec.Decode(bytes, TextEncodingKind.Utf8));
    }

    [Fact]
    public void Decoding_errors_say_where()
    {
        Assert.Throws<ProgrammerException>(() => TextCodec.ParseHexBytes("E28"));
        Assert.Throws<ProgrammerException>(() => TextCodec.ParseHexBytes("GG"));
        var ex = Assert.Throws<ProgrammerException>(() => TextCodec.Decode([0xC3, 0x28], TextEncodingKind.Utf8));
        Assert.Contains("byte", ex.Message);
        Assert.Throws<ProgrammerException>(() => TextCodec.Decode([0x20], TextEncodingKind.Utf16BigEndian));
        Assert.Equal("€", TextCodec.Decode([0xAC, 0x20], TextEncodingKind.Utf16LittleEndian));
    }
}
