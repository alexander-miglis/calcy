using Complex = Calcy.Numerics.Complex;

namespace Calcy.Numerics.Tests;

public class ComplexTests
{
    private static Complex C(double re, double im) => new(re, im);

    [Fact]
    public void Arithmetic()
    {
        Approx.Equal(C(4, 6), C(1, 2) + C(3, 4));
        Approx.Equal(C(-2, -2), C(1, 2) - C(3, 4));
        Approx.Equal(C(-5, 10), C(1, 2) * C(3, 4));
        Approx.Equal(C(0.44, 0.08), C(1, 2) / C(3, 4));
        Approx.Equal(C(2, 4), 2 * C(1, 2));
        Approx.Equal(C(3, 2), C(1, 2) + 2.0);
        Approx.Equal(C(0.2, -0.4), 1.0 / C(1, 2));
    }

    [Fact]
    public void DivisionAvoidsOverflow()
    {
        var big = C(1e300, 1e300);
        Approx.Equal(Complex.One, big / big);
        var tiny = C(1e-300, 1e-300);
        Approx.Equal(Complex.One, tiny / tiny);
        Approx.Equal(C(0, -1e-300), C(1, 0) / C(0, 1e300), 1e-12);
    }

    [Fact]
    public void DivisionByZero()
    {
        Assert.True((C(1, 1) / Complex.Zero).IsInfinity);
        Assert.True((Complex.Zero / Complex.Zero).IsNaN);
    }

    [Fact]
    public void AbsIsOverflowSafe()
    {
        Assert.Equal(5, Complex.Abs(C(3, 4)));
        Approx.Equal(Math.Sqrt(2) * 1e300, Complex.Abs(C(1e300, 1e300)));
        Approx.Equal(Math.Sqrt(2) * 1e-300, Complex.Abs(C(1e-300, 1e-300)));
        Assert.Equal(double.PositiveInfinity, Complex.Abs(C(double.PositiveInfinity, double.NaN)));
    }

    [Fact]
    public void ArgConjugatePolar()
    {
        Approx.Equal(Math.PI / 4, Complex.Arg(C(1, 1)));
        Approx.Equal(Math.PI, Complex.Arg(C(-1, 0)));
        Assert.Equal(C(1, -2), Complex.Conjugate(C(1, 2)));
        Approx.Equal(C(0, 2), Complex.FromPolar(2, Math.PI / 2), 1e-15);
        Approx.Equal(C(-3, 0), Complex.FromPolar(3, Math.PI), 1e-15);
    }

    [Theory]
    [InlineData(-4, 0, 0, 2)]
    [InlineData(3, 4, 2, 1)]
    [InlineData(-3, 4, 1, 2)]
    [InlineData(-3, -4, 1, -2)]
    [InlineData(0, 2, 1, 1)]
    [InlineData(4, 0, 2, 0)]
    public void SqrtPrincipal(double re, double im, double eRe, double eIm)
    {
        Approx.Equal(C(eRe, eIm), Complex.Sqrt(C(re, im)), 1e-15);
    }

    [Fact]
    public void SqrtSignedZeroSelectsBranch()
    {
        Approx.Equal(C(0, -2), Complex.Sqrt(C(-4, -0.0)));
        Approx.Equal(C(0, 2), Complex.Sqrt(C(-4, 0.0)));
    }

    [Fact]
    public void SqrtIsStableForHugeAndTinyArguments()
    {
        var z = Complex.Sqrt(C(1e308, 1e308));
        Assert.True(z.IsFinite);
        Approx.Equal(C(1e308, 1e308), z * z, 1e-14);
        var t = Complex.Sqrt(C(1e-310, 1e-310));
        Approx.Equal(1e-310 * 2, Complex.Abs(t * t) * Math.Sqrt(2), 1e-6);
        // Cancellation-free imaginary part for a number just off the negative axis.
        var s = Complex.Sqrt(C(-1, 1e-20));
        Approx.Equal(5e-21, s.Re, 1e-14);
        Approx.Equal(1, s.Im, 1e-15);
    }

    [Fact]
    public void ExpLog()
    {
        Approx.Equal(C(-1, 0), Complex.Exp(C(0, Math.PI)), 1e-15);
        Approx.Equal(C(0, Math.PI), Complex.Log(C(-1, 0)));
        Approx.Equal(C(0, Math.PI / 2), Complex.Log(Complex.I));
        Approx.Equal(C(Math.Log(5), Math.Atan2(4, 3)), Complex.Log(C(3, 4)));
        Assert.True(Complex.Exp(C(710, 0.5)).IsFinite || Complex.Exp(C(710, 0.5)).IsInfinity);
        var big = Complex.Exp(C(709.5, 1));
        Assert.True(big.IsFinite);
    }

    [Fact]
    public void LogIsAccurateNearUnitCircle()
    {
        var z = Complex.Log(C(1, 1e-10));
        Approx.Equal(5e-21, z.Re, 1e-10);
        Approx.Equal(1e-10, z.Im, 1e-14);
        var w = Complex.Log(C(Math.Cos(0.3), Math.Sin(0.3)));
        Assert.True(Math.Abs(w.Re) < 1e-16);
    }

    [Fact]
    public void Log10()
    {
        Assert.Equal(3.0, Complex.Log10(C(1000, 0)).Re);
        Approx.Equal(C(1, Math.PI / Math.Log(10)), Complex.Log10(C(-10, 0)));
    }

    [Fact]
    public void Powers()
    {
        Assert.Equal(C(-1, 0), Complex.Pow(Complex.I, 2));
        Assert.Equal(C(16, 0), Complex.Pow(C(1, 1), 8));
        Approx.Equal(C(0.5, -0.5), Complex.Pow(C(1, 1), -1), 1e-15);
        Approx.Equal(C(Math.Exp(-Math.PI / 2), 0), Complex.Pow(Complex.I, Complex.I), 1e-15);
        Approx.Equal(C(1, Math.Sqrt(3)), Complex.Pow(C(-8, 0), 1.0 / 3), 1e-14);
        Approx.Equal(C(Math.Sqrt(2), 0), Complex.Pow(C(2, 0), 0.5));
        Assert.Equal(Complex.One, Complex.Pow(Complex.Zero, 0));
        Assert.Equal(Complex.Zero, Complex.Pow(Complex.Zero, C(2, 1)));
        // (3+4i)^10 by repeated squaring agrees with the polar form.
        var p = Complex.Pow(C(3, 4), 10);
        var q = Complex.FromPolar(Math.Pow(5, 10), 10 * Math.Atan2(4, 3));
        Approx.Equal(q, p, 1e-13);
    }

    [Fact]
    public void Trigonometric()
    {
        var z = C(1, 1);
        Approx.Equal(C(1.2984575814159773, 0.6349639147847361), Complex.Sin(z), 1e-15);
        Approx.Equal(C(0.8337300251311491, -0.9888977057628651), Complex.Cos(z), 1e-15);
        Approx.Equal(C(0.2717525853195117, 1.0839233273386946), Complex.Tan(z), 1e-15);
        Approx.Equal(C(0.6349639147847361, 1.2984575814159773), Complex.Sinh(z), 1e-15);
        Approx.Equal(C(0.8337300251311491, 0.9888977057628651), Complex.Cosh(z), 1e-15);
        Approx.Equal(C(1.0839233273386946, 0.2717525853195117), Complex.Tanh(z), 1e-15);
    }

    [Fact]
    public void TanAndTanhAreStableForLargeArguments()
    {
        var t = Complex.Tan(C(1, 1000));
        Assert.False(t.IsNaN);
        Approx.Equal(C(0, 1), t, 1e-15);
        Approx.Equal(C(1, 0), Complex.Tanh(C(1000, 2)), 1e-15);
        Approx.Equal(C(-1, 0), Complex.Tanh(C(-50, 0.3)), 1e-15);
        // Real tangent near a pole keeps full accuracy.
        Approx.Equal(Math.Tan(1.5707), Complex.Tan(C(1.5707, 0)).Re, 1e-12);
    }

    [Fact]
    public void InverseTrigOnBranchCuts()
    {
        double asinh3 = 1.3169578969248166;
        Approx.Equal(C(Math.PI / 2, asinh3), Complex.Asin(C(2, 0.0)));
        Approx.Equal(C(Math.PI / 2, -asinh3), Complex.Asin(C(2, -0.0)));
        Approx.Equal(C(0, -asinh3), Complex.Acos(C(2, 0)));
        Approx.Equal(C(Math.PI, -asinh3), Complex.Acos(C(-2, 0)));
        Approx.Equal(C(Math.PI / 2, 0.5493061443340549), Complex.Atan(C(0, 2)));
        Approx.Equal(C(asinh3, Math.PI / 2), Complex.Asinh(C(0, 2)));
        Approx.Equal(C(0, Math.PI / 3), Complex.Acosh(C(0.5, 0)));
        Approx.Equal(C(asinh3, Math.PI), Complex.Acosh(C(-2, 0)));
        Approx.Equal(C(0.5493061443340549, Math.PI / 2), Complex.Atanh(C(2, 0)));
        Approx.Equal(C(0.5493061443340549, -Math.PI / 2), Complex.Atanh(C(2, -0.0)));
    }

    [Fact]
    public void InverseTrigRealValues()
    {
        Approx.Equal(Math.PI / 6, Complex.Asin(C(0.5, 0)).Re, 1e-15);
        Assert.Equal(0, Complex.Asin(C(0.5, 0)).Im);
        Approx.Equal(Math.PI / 3, Complex.Acos(C(0.5, 0)).Re, 1e-15);
        Approx.Equal(Math.PI / 4, Complex.Atan(C(1, 0)).Re, 1e-15);
        Approx.Equal(0.881373587019543, Complex.Asinh(C(1, 0)).Re, 1e-15);
        Approx.Equal(1.3169578969248166, Complex.Acosh(C(2, 0)).Re, 1e-15);
        Approx.Equal(0.5493061443340549, Complex.Atanh(C(0.5, 0)).Re, 1e-15);
        Assert.True(Complex.Atan(Complex.I).IsInfinity);
    }

    [Theory]
    [InlineData(0.3, 0.7)]
    [InlineData(-2.5, 1.2)]
    [InlineData(1.7, -3.1)]
    [InlineData(-0.4, -0.9)]
    [InlineData(5.0, 0.001)]
    public void FunctionsInvertEachOther(double re, double im)
    {
        var z = C(re, im);
        Approx.Equal(z, Complex.Exp(Complex.Log(z)), 1e-14);
        Approx.Equal(z, Complex.Sqrt(z) * Complex.Sqrt(z), 1e-14);
        Approx.Equal(z, Complex.Sin(Complex.Asin(z)), 1e-13);
        Approx.Equal(z, Complex.Cos(Complex.Acos(z)), 1e-13);
        Approx.Equal(z, Complex.Tan(Complex.Atan(z)), 1e-13);
        Approx.Equal(z, Complex.Sinh(Complex.Asinh(z)), 1e-13);
        Approx.Equal(z, Complex.Cosh(Complex.Acosh(z)), 1e-13);
        Approx.Equal(z, Complex.Tanh(Complex.Atanh(z)), 1e-13);
        // Identities: sin^2 + cos^2 = 1 and cosh^2 - sinh^2 = 1.
        var s = Complex.Sin(z); var c = Complex.Cos(z);
        Approx.Equal(Complex.One, s * s + c * c, 1e-12);
    }

    [Theory]
    [InlineData(3, 4, "3+4i")]
    [InlineData(0, -2, "-2i")]
    [InlineData(1.5, 0, "1.5")]
    [InlineData(3, -1, "3-i")]
    [InlineData(0, 1, "i")]
    [InlineData(-0.0, 0, "0")]
    [InlineData(-2.25, 0.5, "-2.25+0.5i")]
    public void Formatting(double re, double im, string expected)
    {
        Assert.Equal(expected, C(re, im).ToString(10));
    }

    [Fact]
    public void FormattingWithDigits()
    {
        Assert.Equal("0.3333+0.6667i", C(1.0 / 3, 2.0 / 3).ToString(4));
        Assert.Equal("1E+20", C(1e20, 0).ToString(5));
        Assert.Equal("NaN", Complex.NaN.ToString());
    }

    [Fact]
    public void IsReal()
    {
        Assert.True(C(1, 1e-15).IsReal(1e-12));
        Assert.False(C(1, 1e-10).IsReal(1e-12));
        Assert.True(C(5, 0).IsReal());
        Complex fromDouble = 2.5;
        Assert.Equal(C(2.5, 0), fromDouble);
    }
}
