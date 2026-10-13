using System.Globalization;
using System.Numerics;

namespace Calcy.Modes.Programmer.Tests;

public class IeeeFloatTests
{
    private static BigInteger H(string hex) => BigInteger.Parse("0" + hex, NumberStyles.HexNumber);

    private static ulong Bits(double d) => unchecked((ulong)BitConverter.DoubleToInt64Bits(d));

    [Theory]
    [InlineData("half", "0.1", "2E66")]
    [InlineData("single", "0.1", "3DCCCCCD")]
    [InlineData("double", "0.1", "3FB999999999999A")]
    [InlineData("quad", "0.1", "3FFB999999999999999999999999999A")]
    [InlineData("half", "1", "3C00")]
    [InlineData("half", "65504", "7BFF")]
    [InlineData("half", "65520", "7C00")]
    [InlineData("half", "-2", "C000")]
    [InlineData("quad", "1", "3FFF0000000000000000000000000000")]
    [InlineData("quad", "-2", "C0000000000000000000000000000000")]
    [InlineData("quad", "1/3", "3FFD5555555555555555555555555555")]
    [InlineData("single", "16777217", "4B800000")]
    [InlineData("double", "-0.0", "8000000000000000")]
    [InlineData("double", "inf", "7FF0000000000000")]
    [InlineData("double", "-inf", "FFF0000000000000")]
    [InlineData("double", "nan", "7FF8000000000000")]
    [InlineData("double", "snan", "7FF0000000000001")]
    [InlineData("double", "nan(0x12)", "7FF8000000000012")]
    [InlineData("double", "4.9406564584124654e-324", "0000000000000001")]
    [InlineData("double", "1e-400", "0000000000000000")]
    [InlineData("double", "1e400", "7FF0000000000000")]
    [InlineData("double", "0x1.8p1", "4008000000000000")]
    [InlineData("double", "0x3FF0000000000000", "3FF0000000000000")]
    [InlineData("half", "5.960464477539063e-8", "0001")]
    [InlineData("half", "2.98e-8", "0000")]
    [InlineData("half", "3e-8", "0001")]
    [InlineData("single", "1_000.5", "447A2000")]
    public void Bit_patterns(string format, string text, string hex)
    {
        var f = IeeeFormat.All.Single(x => x.Name == format);
        Assert.Equal(H(hex), IeeeFloat.Parse(text, f).Bits);
    }

    [Fact]
    public void Exact_decimal_of_point_one()
    {
        Assert.Equal("0.1000000000000000055511151231257827021181583404541015625", IeeeFloat.Parse("0.1", IeeeFormat.Double).ExactDecimal());
        Assert.Equal("0.100000001490116119384765625", IeeeFloat.Parse("0.1", IeeeFormat.Single).ExactDecimal());
        Assert.Equal("0.0999755859375", IeeeFloat.Parse("0.1", IeeeFormat.Half).ExactDecimal());
        Assert.StartsWith("0.1" + new string('0', 34) + "48148", IeeeFloat.Parse("0.1", IeeeFormat.Quad).ExactDecimal());
    }

    [Fact]
    public void Shortest_decimal_round_trips()
    {
        Assert.Equal("0.1", IeeeFloat.Parse("0.1", IeeeFormat.Double).ShortestDecimal());
        Assert.Equal("0.1", IeeeFloat.Parse("0.1", IeeeFormat.Quad).ShortestDecimal());
        Assert.Equal("0.1", IeeeFloat.Parse("0.1", IeeeFormat.Half).ShortestDecimal());
        Assert.Equal("-0", IeeeFloat.Parse("-0", IeeeFormat.Double).ShortestDecimal());
        Assert.Equal("5e-324", new IeeeFloat(IeeeFormat.Double, 1).ShortestDecimal());
        Assert.Equal("65500", IeeeFloat.Parse("65504", IeeeFormat.Half).ShortestDecimal());
        Assert.Equal("65504", IeeeFloat.Parse("65504", IeeeFormat.Half).ExactDecimal());
        Assert.Equal("1.7976931348623157e308", IeeeFloat.FromDouble(double.MaxValue).ShortestDecimal());
    }

    [Fact]
    public void Matches_dotnet_for_many_doubles_and_floats()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 400; i++)
        {
            double d = BitConverter.Int64BitsToDouble(rng.NextInt64());
            if (!double.IsFinite(d)) continue;
            var text = d.ToString("R", CultureInfo.InvariantCulture);
            var parsed = IeeeFloat.Parse(text, IeeeFormat.Double);
            Assert.Equal(Bits(d), (ulong)parsed.Bits);
            var shortest = parsed.ShortestDecimal();
            Assert.Equal(Bits(d), Bits(double.Parse(shortest, CultureInfo.InvariantCulture)));

            float s = BitConverter.Int32BitsToSingle(rng.Next() ^ (rng.Next() << 1));
            if (!float.IsFinite(s)) continue;
            var st = s.ToString("R", CultureInfo.InvariantCulture);
            Assert.Equal(BitConverter.SingleToUInt32Bits(s), (uint)IeeeFloat.Parse(st, IeeeFormat.Single).Bits);
        }
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(1.0 / 3)]
    [InlineData(1e-5)]
    [InlineData(6.1e-5)]
    [InlineData(-1234.5)]
    public void Half_matches_dotnet(double d)
    {
        var expected = BitConverter.HalfToUInt16Bits((Half)d);
        Assert.Equal(expected, (ushort)IeeeFloat.Parse(d.ToString("R", CultureInfo.InvariantCulture), IeeeFormat.Half).Bits);
        Assert.Equal(expected, (ushort)IeeeFloat.FromHalf((Half)d).Bits);
    }

    [Fact]
    public void Fields_of_point_one_in_double()
    {
        var v = IeeeFloat.Parse("0.1", IeeeFormat.Double);
        Assert.False(v.Sign);
        Assert.Equal(1019, v.BiasedExponent);
        Assert.Equal(-4, v.UnbiasedExponent);
        Assert.Equal(H("999999999999A"), v.Fraction);
        Assert.Equal(FloatClass.Normal, v.Class);
        Assert.StartsWith("1.1001100110011", v.BinarySignificand());
        Assert.Equal("3FB9 9999 9999 999A", v.Hex());
    }

    [Fact]
    public void Special_values_are_classified()
    {
        Assert.Equal(FloatClass.Zero, IeeeFloat.Parse("-0", IeeeFormat.Double).Class);
        Assert.True(IeeeFloat.Parse("-0", IeeeFormat.Double).Sign);
        Assert.Equal(FloatClass.Infinity, IeeeFloat.Parse("inf", IeeeFormat.Half).Class);
        Assert.Equal(FloatClass.QuietNaN, IeeeFloat.Parse("nan", IeeeFormat.Quad).Class);
        var snan = IeeeFloat.Parse("snan(5)", IeeeFormat.Single);
        Assert.Equal(FloatClass.SignalingNaN, snan.Class);
        Assert.Equal(new BigInteger(5), snan.NaNPayload);
        Assert.Equal(new BigInteger(0x12), IeeeFloat.Parse("-nan(0x12)", IeeeFormat.Double).NaNPayload);
        Assert.True(IeeeFloat.Parse("-nan", IeeeFormat.Double).Sign);
        Assert.Equal("nan", IeeeFloat.Parse("nan", IeeeFormat.Double).ExactDecimal());
        Assert.Equal(FloatClass.QuietNaN, IeeeFloat.FromDouble(double.NaN).Class);
    }

    [Fact]
    public void Subnormals()
    {
        var min = new IeeeFloat(IeeeFormat.Double, 1);
        Assert.Equal(FloatClass.Subnormal, min.Class);
        Assert.Equal(-1022, min.UnbiasedExponent);
        Assert.Equal(Bits(double.Epsilon), (ulong)min.Bits);
        var exact = min.ExactDecimal();
        Assert.StartsWith("0.000", exact);
        Assert.Equal("0." + BigInteger.Pow(5, 1074).ToString(CultureInfo.InvariantCulture).PadLeft(1074, '0'), exact);
        Assert.Contains("49406564584124654417656879286822137236505980", exact);
        Assert.Equal(Bits(1e-310), (ulong)IeeeFloat.Parse("1e-310", IeeeFormat.Double).Bits);
        Assert.Equal(FloatClass.Subnormal, IeeeFloat.Parse("1e-310", IeeeFormat.Double).Class);
        Assert.Equal(new BigInteger(1), IeeeFloat.Parse("6.5e-4966", IeeeFormat.Quad).Bits);
        Assert.StartsWith("0.", new IeeeFloat(IeeeFormat.Quad, 1).ExactDecimal());
    }

    [Fact]
    public void Next_and_previous_match_Math_BitIncrement()
    {
        foreach (var d in new[] { 1.0, -1.0, 0.1, 0.0, -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue, 1e-310, double.PositiveInfinity, double.NegativeInfinity })
        {
            var v = IeeeFloat.FromDouble(d);
            Assert.Equal(Bits(Math.BitIncrement(d)), (ulong)v.NextUp().Bits);
            Assert.Equal(Bits(Math.BitDecrement(d)), (ulong)v.NextDown().Bits);
        }
        var nan = IeeeFloat.Parse("nan", IeeeFormat.Double);
        Assert.Equal(nan.Bits, nan.NextUp().Bits);
    }

    [Fact]
    public void Ulp()
    {
        Assert.Equal("0.0000000000000002220446049250313080847263336181640625", IeeeFloat.Parse("1", IeeeFormat.Double).Ulp());
        Assert.Equal(-52, IeeeFloat.Parse("1", IeeeFormat.Double).UlpExponent);
        Assert.Equal(-1074, new IeeeFloat(IeeeFormat.Double, 1).UlpExponent);
        Assert.Equal("0.0009765625", IeeeFloat.Parse("1", IeeeFormat.Half).Ulp());
        Assert.Null(IeeeFloat.Parse("inf", IeeeFormat.Double).Ulp());
    }

    [Fact]
    public void Quad_conversions()
    {
        var q = IeeeFloat.Parse("1.18973149535723176508575932662800702e4932", IeeeFormat.Quad);
        Assert.Equal(H("7FFEFFFFFFFFFFFFFFFFFFFFFFFFFFFF"), q.Bits);
        Assert.Equal(FloatClass.Infinity, IeeeFloat.Parse("1.2e4932", IeeeFormat.Quad).Class);
        Assert.Equal(IeeeFloat.Parse("1", IeeeFormat.Quad).Bits + 1, IeeeFloat.Parse("1", IeeeFormat.Quad).NextUp().Bits);
        Assert.Equal("1.0000000000000000000000000000000002", IeeeFloat.Parse("1", IeeeFormat.Quad).NextUp().ShortestDecimal());
    }

    [Fact]
    public void Flipping_a_bit_changes_the_value()
    {
        var one = IeeeFloat.Parse("1", IeeeFormat.Double);
        Assert.Equal(Bits(-1.0), (ulong)one.FlipBit(63).Bits);
        Assert.Equal(Bits(1.5), (ulong)one.FlipBit(51).Bits);
        Assert.Throws<ProgrammerException>(() => one.FlipBit(64));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("0x1.8")]
    [InlineData("0x1FFFF")]
    [InlineData("nan(0x400)")]
    [InlineData("1/0")]
    public void Bad_input_is_rejected(string text) =>
        Assert.Throws<ProgrammerException>(() => IeeeFloat.Parse(text, IeeeFormat.Half));
}
