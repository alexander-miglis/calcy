using System.Numerics;
using Calcy.Numerics;
using Complex = Calcy.Numerics.Complex;

namespace Calcy.Numerics.Tests;

internal static class Approx
{
    /// <summary>Asserts |expected - actual| &lt;= max(absTol, relTol * |expected|).</summary>
    public static void Equal(double expected, double actual, double relTol = 1e-12, double absTol = 0)
    {
        double tol = Math.Max(absTol, relTol * Math.Abs(expected));
        if (double.IsNaN(expected) && double.IsNaN(actual)) return;
        if (expected == actual) return;
        Assert.True(Math.Abs(expected - actual) <= tol,
            $"Expected {expected:R} but got {actual:R} (difference {Math.Abs(expected - actual):E3}, tolerance {tol:E3}).");
    }

    public static void Equal(Complex expected, Complex actual, double tol = 1e-12)
    {
        double scale = Math.Max(1.0, Complex.Abs(expected));
        Assert.True(Complex.Abs(expected - actual) <= tol * scale,
            $"Expected {expected.ToString(17)} but got {actual.ToString(17)}.");
    }

    public static void Equal(double[] expected, double[] actual, double relTol = 1e-12, double absTol = 0)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++) Equal(expected[i], actual[i], relTol, absTol);
    }

    public static void Equal(Matrix expected, Matrix actual, double tol)
    {
        Assert.Equal(expected.Rows, actual.Rows);
        Assert.Equal(expected.Columns, actual.Columns);
        Assert.True(expected.ApproximatelyEquals(actual, tol), $"Expected {expected} but got {actual}.");
    }
}

/// <summary>Exact rational arithmetic on doubles for checking interval enclosures.</summary>
internal readonly record struct Rational(BigInteger Num, BigInteger Den)
{
    public static Rational FromDouble(double d)
    {
        if (!double.IsFinite(d)) throw new ArgumentException("finite only");
        long bits = BitConverter.DoubleToInt64Bits(d);
        bool neg = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0) exponent++;
        else mantissa |= 1L << 52;
        exponent -= 1075;
        BigInteger m = mantissa;
        if (neg) m = -m;
        return exponent >= 0 ? new Rational(m << exponent, BigInteger.One) : new Rational(m, BigInteger.One << -exponent);
    }

    public static Rational operator +(Rational a, Rational b) => new(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
    public static Rational operator -(Rational a, Rational b) => new(a.Num * b.Den - b.Num * a.Den, a.Den * b.Den);
    public static Rational operator *(Rational a, Rational b) => new(a.Num * b.Num, a.Den * b.Den);

    public static Rational operator /(Rational a, Rational b)
    {
        var num = a.Num * b.Den;
        var den = a.Den * b.Num;
        if (den.Sign < 0) { num = -num; den = -den; }
        return new Rational(num, den);
    }

    public static bool operator <=(Rational a, Rational b) => a.Num * b.Den <= b.Num * a.Den;
    public static bool operator >=(Rational a, Rational b) => a.Num * b.Den >= b.Num * a.Den;
    public static bool operator <(Rational a, Rational b) => a.Num * b.Den < b.Num * a.Den;
    public static bool operator >(Rational a, Rational b) => a.Num * b.Den > b.Num * a.Den;
}
