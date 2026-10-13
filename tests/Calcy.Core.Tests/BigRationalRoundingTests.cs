using System.Globalization;
using System.Numerics;
using Calcy.Core.Numbers;

namespace Calcy.Core.Tests;

/// <summary>Exact fractions convert to the nearest double, like .NET's own (correctly rounded) parsing.</summary>
public class BigRationalRoundingTests
{
    private static void Same(string decimalText)
    {
        var exact = BigRational.ParseDecimal(decimalText);
        double expected = double.Parse(decimalText, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(exact.ToDouble()));
    }

    [Theory]
    [InlineData("3.6443385825e-19")]
    [InlineData("0.1")]
    [InlineData("1e308")]
    [InlineData("1.7976931348623157e308")]
    [InlineData("2.2250738585072014e-308")]
    [InlineData("4.9406564584124654e-324")]
    [InlineData("2.4703282292062328e-324")]
    [InlineData("123456789012345678901234567890")]
    [InlineData("9007199254740993")]
    public void KnownHardCases(string text) => Same(text);

    [Fact]
    public void RandomDecimals()
    {
        var rng = new Random(20261013);
        for (int i = 0; i < 4000; i++)
        {
            var digits = new string([.. Enumerable.Range(0, rng.Next(1, 30)).Select(_ => (char)('0' + rng.Next(10)))]).TrimStart('0');
            if (digits.Length == 0) digits = "1";
            int exp = rng.Next(-340, 300);
            Same($"{digits}e{exp}");
        }
    }

    [Fact]
    public void FractionsMatchDivisionWhenSmall()
    {
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            long n = rng.NextInt64(-1L << 52, 1L << 52), d = rng.NextInt64(1, 1L << 52);
            Assert.Equal((double)n / d, new BigRational(new BigInteger(n), new BigInteger(d)).ToDouble());
        }
    }
}