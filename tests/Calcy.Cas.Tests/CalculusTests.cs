using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Cas.Tests;

public class DiffTests
{
    /// <summary>Every elementary function: the symbolic derivative must match a numeric derivative at sample points.</summary>
    [Theory]
    [InlineData("x^5")]
    [InlineData("x^(-3)")]
    [InlineData("sqrt(x)")]
    [InlineData("root(x, 3)")]
    [InlineData("cbrt(x)")]
    [InlineData("1/x")]
    [InlineData("e^x")]
    [InlineData("exp(2x)")]
    [InlineData("2^x")]
    [InlineData("x^x")]
    [InlineData("ln(x)")]
    [InlineData("log(x)")]
    [InlineData("log(x, 2)")]
    [InlineData("log2(x)")]
    [InlineData("sin(x)")]
    [InlineData("cos(x)")]
    [InlineData("tan(x)")]
    [InlineData("sec(x)")]
    [InlineData("csc(x)")]
    [InlineData("cot(x)")]
    [InlineData("asin(x/3)")]
    [InlineData("acos(x/3)")]
    [InlineData("atan(x)")]
    [InlineData("acot(x)")]
    [InlineData("asec(x + 3)")]
    [InlineData("acsc(x + 3)")]
    [InlineData("sinh(x)")]
    [InlineData("cosh(x)")]
    [InlineData("tanh(x)")]
    [InlineData("sech(x)")]
    [InlineData("csch(x)")]
    [InlineData("coth(x)")]
    [InlineData("asinh(x)")]
    [InlineData("acosh(x + 3)")]
    [InlineData("atanh(x/3)")]
    [InlineData("abs(x)")]
    [InlineData("x^2 sin(x)")]
    [InlineData("x/(x + 1)")]
    [InlineData("sin(x)/x")]
    [InlineData("e^(x^2) cos(3x)")]
    [InlineData("ln(x^2 + 1)")]
    [InlineData("sqrt(1 + sin(x)^2)")]
    [InlineData("atan(1/x)")]
    [InlineData("(x^2 + 1)^10")]
    [InlineData("sin(cos(tan(x)))")]
    [InlineData("x^sin(x)")]
    [InlineData("ln(ln(x + 3))")]
    [InlineData("atan2(x, 2)")]
    [InlineData("tan(x)^2 sec(x)")]
    [InlineData("(x - 1)/(x^2 + x + 1)")]
    public void MatchesNumericDerivative(string f)
    {
        var s = Cas.Session();
        var expr = Cas.Exact(f, s);
        var d = Cas.Exact($"diff({f}, x)", s);
        Assert.DoesNotContain("diff(", ExprPrinter.ToText(d));
        int checkedPoints = 0;
        foreach (var x in new[] { 0.37, 1.21, 2.13, -0.61, 0.83 })
        {
            double numeric = Cas.NumericDerivative(expr, "x", x, s);
            double symbolic = Cas.Value(d, "x", x, s);
            if (!double.IsFinite(numeric) || !double.IsFinite(symbolic)) continue;
            Assert.True(Math.Abs(numeric - symbolic) <= 1e-5 * Math.Max(1, Math.Abs(numeric)), $"d/dx {f} at {x}: {symbolic} vs numeric {numeric}");
            checkedPoints++;
        }
        Assert.True(checkedPoints >= 2);
    }

    [Theory]
    [InlineData("diff(x^2, x)", "2x")]
    [InlineData("diff(x^3 - 2x, x)", "3x² - 2")]
    [InlineData("diff(sin(x), x)", "cos(x)")]
    [InlineData("diff(ln(x), x)", "1/x")]
    [InlineData("diff(e^(3x), x)", "3e^(3x)")]
    [InlineData("diff(x/(x + 1), x)", "1/(x + 1)²")]
    [InlineData("diff(x^x, x)", "x^x·(ln(x) + 1)")]
    [InlineData("diff(atan(x), x)", "1/(x² + 1)")]
    [InlineData("diff(abs(x), x)", "sign(x)")]
    [InlineData("diff(tan(x), x)", "sec(x)²")]
    [InlineData("diff(x^4, x, 2)", "12x²")]
    [InlineData("diff(x^4, x, 4)", "24")]
    [InlineData("diff(x^4, x, 5)", "0")]
    [InlineData("diff(sin(x), x, 3)", "-cos(x)")]
    [InlineData("diff(x^2 y^3, x)", "2xy³")]
    [InlineData("diff(x^2 y^3, y)", "3x²y²")]
    [InlineData("diff(x^2 y^3, x, y)", "6xy²")]
    [InlineData("diff({x^2, x^3}, x)", "{2x, 3x²}")]
    [InlineData("derivative(cos(x), x)", "-sin(x)")]
    [InlineData("d(x^3, x)", "3x²")]
    [InlineData("diff(5, x)", "0")]
    [InlineData("diff(floor(x), x)", "0")]
    [InlineData("diff(log(x), x)", "1/(x·ln(10))")]
    public void Exact(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void UserFunctionsAreInlined()
    {
        var s = Cas.Session();
        s.Evaluate("f(x) = x^3 + 2x");
        Assert.Equal("3x² + 2", Cas.Text("diff(f(x), x)", s));
        Assert.Equal("14", Cas.Text("f'(2)", s));
        Assert.Equal("6x", Cas.Text("f''(x)", s));
    }

    [Fact]
    public void VariableWithAValueIsStillFree()
    {
        var s = Cas.Session();
        s.Evaluate("x = 5");
        Assert.Equal("2x", Cas.Text("diff(x^2, x)", s));
    }

    [Fact]
    public void UnknownFunctionGivesPrime()
    {
        using var scope = CasEnv.Enter(Cas.Session().Context());
        var r = Calculus.Differentiator.D(CallExpr.Of("g", Algebra.Mul(Expr.Two, Expr.Sym("x"))), "x");
        Assert.Equal("2g'(2x)", ExprPrinter.ToText(r));
    }

    [Fact]
    public void DegreeModeFollowsTiNspire()
    {
        var s = Cas.Degrees();
        Assert.Equal("π·cos(x)/180", Cas.Text("diff(sin(x), x)", s));
        Assert.Equal("-π·sin(x)/180", Cas.Text("diff(cos(x), x)", s));
        Assert.Equal("180/(π·(x² + 1))", Cas.Text("diff(atan(x), x)", s));
    }

    [Theory]
    [InlineData("tangent(x^2, x, 1)", "y = 2x - 1")]
    [InlineData("tangent(x^3, x, 0)", "y = 0")]
    [InlineData("tangent(sin(x), x, 0)", "y = x")]
    [InlineData("tangent(sqrt(x), x, 0)", "x = 0")]
    [InlineData("normal(x^2, x, 1)", "y = -x/2 + 3/2")]
    [InlineData("normal(x^2, x, 0)", "x = 0")]
    [InlineData("impdiff(x^2 + y^2 = 25, x, y)", "-x/y")]
    [InlineData("impdiff(x y = 1, x, y)", "-y/x")]
    [InlineData("impdiff(y^2 = x^3, x, y)", "3x²/(2y)")]
    public void TangentsAndImplicit(string input, string expected) => Assert.Equal(expected, Cas.Text(input));
}

public class IntegrateTests
{
    /// <summary>Each antiderivative is checked by comparing its numeric derivative with the integrand.</summary>
    [Theory]
    [InlineData("x^2")]
    [InlineData("x^2 + 3x + 1")]
    [InlineData("1/x")]
    [InlineData("x^(-1/2)")]
    [InlineData("(3x - 1)^5")]
    [InlineData("1/(2x + 5)")]
    [InlineData("e^(3x)")]
    [InlineData("e^(-x/2 + 1)")]
    [InlineData("2^x")]
    [InlineData("sin(2x + 1)")]
    [InlineData("cos(x/3)")]
    [InlineData("tan(x)")]
    [InlineData("sec(x)^2")]
    [InlineData("csc(x)^2")]
    [InlineData("sec(x) tan(x)")]
    [InlineData("csc(x) cot(x)")]
    [InlineData("sec(x)")]
    [InlineData("csc(x)")]
    [InlineData("cot(x)")]
    [InlineData("sinh(x)")]
    [InlineData("cosh(2x)")]
    [InlineData("tanh(x)")]
    [InlineData("1/(x^2 + 1)")]
    [InlineData("1/(x^2 + 4)")]
    [InlineData("1/(x^2 - 1)")]
    [InlineData("1/sqrt(1 - x^2)")]
    [InlineData("1/sqrt(4 - x^2)")]
    [InlineData("1/sqrt(x^2 + 1)")]
    [InlineData("sqrt(1 - x^2)")]
    [InlineData("sqrt(x^2 + 1)")]
    [InlineData("sqrt(2x + 1)")]
    [InlineData("x e^x")]
    [InlineData("x^2 e^x")]
    [InlineData("x^3 e^(2x)")]
    [InlineData("x sin(x)")]
    [InlineData("x^2 sin(x)")]
    [InlineData("x cos(3x)")]
    [InlineData("x^2 cosh(x)")]
    [InlineData("ln(x)")]
    [InlineData("x ln(x)")]
    [InlineData("x^2 ln(x)")]
    [InlineData("ln(x)^2")]
    [InlineData("ln(x)/x")]
    [InlineData("sqrt(x) ln(x)")]
    [InlineData("e^x sin(x)")]
    [InlineData("e^(2x) cos(3x)")]
    [InlineData("asin(x)")]
    [InlineData("acos(x)")]
    [InlineData("atan(x)")]
    [InlineData("x atan(x)")]
    [InlineData("2x cos(x^2)")]
    [InlineData("x e^(x^2)")]
    [InlineData("x/(x^2 + 1)")]
    [InlineData("cos(x) e^(sin(x))")]
    [InlineData("sin(x) cos(x)")]
    [InlineData("1/(x ln(x))")]
    [InlineData("e^x/(1 + e^x)")]
    [InlineData("x^3 e^(x^2)")]
    [InlineData("x sqrt(x + 1)")]
    [InlineData("1/(1 + sqrt(x))")]
    [InlineData("sin(x)^2")]
    [InlineData("cos(x)^2")]
    [InlineData("sin(x)^3")]
    [InlineData("sin(x)^2 cos(x)^3")]
    [InlineData("sin(x)^2 cos(x)^2")]
    [InlineData("tan(x)^2")]
    [InlineData("tan(x)^3")]
    [InlineData("sec(x)^3")]
    [InlineData("sin(2x) cos(3x)")]
    [InlineData("(x + 1)/(x^2 + 2x + 5)")]
    [InlineData("1/(x^2 + x + 1)")]
    [InlineData("1/(x^2 + 1)^2")]
    [InlineData("x/(x^2 + 1)^2")]
    [InlineData("1/((x - 1)^2 (x + 2))")]
    [InlineData("(x^3 + 1)/(x^2 - 4)")]
    [InlineData("1/(x^3 + 1)")]
    [InlineData("(2x + 3)/(x^2 + 3x - 4)")]
    [InlineData("abs(x)")]
    [InlineData("(x^2 + 1)^3 x")]
    public void AntiderivativeDifferentiatesBack(string f)
    {
        var s = Cas.Session();
        var integrand = Cas.Exact(f, s);
        var F = Cas.Exact($"integrate({f}, x)", s);
        Assert.False(F is CallExpr { Name: "integrate" }, $"no antiderivative for {f}");
        int checkedPoints = 0;
        foreach (var x in new[] { 0.37, 0.71, 1.43, 2.6, -0.45, -1.3 })
        {
            double expected = Cas.Value(integrand, "x", x, s);
            double derivative = Cas.NumericDerivative(F, "x", x, s);
            if (!double.IsFinite(expected) || !double.IsFinite(derivative)) continue;
            Assert.True(Math.Abs(expected - derivative) <= 1e-5 * Math.Max(1, Math.Abs(expected)), $"∫{f}: F' = {derivative} but f = {expected} at {x}");
            checkedPoints++;
        }
        Assert.True(checkedPoints >= 2, $"too few valid points for {f}");
    }

    [Theory]
    [InlineData("integrate(x^2, x)", "x³/3")]
    [InlineData("integrate(1/x, x)", "ln(|x|)")]
    [InlineData("integrate(sin(x), x)", "-cos(x)")]
    [InlineData("integrate(e^x, x)", "e^x")]
    [InlineData("integrate(1/(x^2 + 1), x)", "atan(x)")]
    [InlineData("integrate(1/sqrt(1 - x^2), x)", "asin(x)")]
    [InlineData("integrate(ln(x), x)", "x·ln(x) - x")]
    [InlineData("integrate(2x cos(x^2), x)", "sin(x²)")]
    [InlineData("integrate(x/(x^2 + 1), x)", "ln(x² + 1)/2")]
    [InlineData("int(3, x)", "3x")]
    [InlineData("antiderivative(cos(x), x)", "sin(x)")]
    [InlineData("integral(sec(x)^2, x)", "tan(x)")]
    [InlineData("integrate(a x, x)", "ax²/2")]
    public void Exact(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("sin(x^2)")]
    [InlineData("e^(x^2)")]
    [InlineData("sin(x)/x")]
    [InlineData("x^x")]
    public void NonElementaryStaysUnevaluated(string f)
    {
        var r = Cas.Exact($"integrate({f}, x)");
        Assert.True(r is CallExpr { Name: "integrate" }, ExprPrinter.ToText(r));
    }

    [Fact]
    public void DegreeModeIntegral()
    {
        var s = Cas.Degrees();
        Assert.Equal("-180cos(x)/π", Cas.Text("integrate(sin(x), x)", s));
        Assert.Equal("360/π", Cas.Text("integrate(sin(x), x, 0, 180)", s));
    }

    [Fact]
    public void IntKeepsIntegerPartForNumbers() => Assert.Equal("2", Cas.Text("int(2.7)"));
}

public class DefiniteIntegralTests
{
    [Theory]
    [InlineData("integrate(sin(x), x, 0, pi)", "2")]
    [InlineData("integrate(x^2, x, 0, 1)", "1/3")]
    [InlineData("integrate(1/x, x, 1, e)", "1")]
    [InlineData("integrate(e^(-x), x, 0, inf)", "1")]
    [InlineData("integrate(1/x^2, x, 1, inf)", "1")]
    [InlineData("integrate(1/sqrt(x), x, 0, 1)", "2")]
    [InlineData("integrate(1/(1 + x^2), x, -inf, inf)", "π")]
    [InlineData("integrate(sqrt(1 - x^2), x, -1, 1)", "π/2")]
    [InlineData("integrate(x e^(-x), x, 0, inf)", "1")]
    [InlineData("integrate(abs(x), x, -1, 1)", "1")]
    [InlineData("integrate(x^3, x, -2, 2)", "0")]
    [InlineData("integrate(cos(x), x, 0, pi/2)", "1")]
    [InlineData("integrate(x^2, x, 1, 0)", "-1/3")]
    [InlineData("integrate(x^2, x, 0, b)", "b³/3")]
    [InlineData("integrate(1/(x^2 - 1), x, 2, 3)", "ln(3)/2 - ln(2)/2")]
    [InlineData("integrate(1/x, x, 0, 1)", "∞")]
    [InlineData("integrate(1/x^2, x, -1, 1)", "∞")]
    public void ExactValues(string input, string expected)
    {
        var s = Cas.Session();
        var text = Cas.Text(input, s);
        if (text == expected) return;
        // Equivalent exact forms are accepted when they agree numerically.
        var expectedValue = Cas.Value(Cas.Exact(expected.Replace("π", "pi").Replace("∞", "inf"), s), s);
        Cas.AssertClose(expectedValue, Cas.Value(Cas.Exact(input, s), s), 1e-12);
    }

    [Fact]
    public void SingularIntegralIsNotZero()
    {
        var r = Cas.Exact("integrate(1/x, x, -1, 1)");
        Assert.False(r.IsZero, "∫_-1^1 1/x must not be 0");
        Assert.Equal("undefined", ExprPrinter.ToText(r));
    }

    [Theory]
    [InlineData("integrate(1/(x - 2), x, 0, 3)")]
    [InlineData("integrate(tan(x), x, 0, pi)")]
    public void DivergentThroughPole(string input) => Assert.Equal("undefined", Cas.Text(input));

    [Theory]
    [InlineData("integrate(sin(x^2), x, 0, 1)", 0.310268301723381)]
    [InlineData("integrate(e^(-x^2), x, -inf, inf)", 1.772453850905516)]
    [InlineData("integrate(1/(x^4 + 1), x, 0, 1)", 0.866972987339911)]
    [InlineData("integrate(sin(x)/x, x, 1, 2)", 0.659329906435512)]
    [InlineData("integrate(1/(1 + cos(x)^2), x, 0, 2pi)", 4.442882938158366)]
    [InlineData("integrate(x^x, x, 0, 1)", 0.783430510712134)]
    public void NumericFallback(string input, double expected)
    {
        var r = Cas.Exact(input);
        var n = Assert.IsType<NumberExpr>(r);
        Assert.False(n.Value.IsExact);
        Cas.AssertClose(expected, n.Value.Re, 1e-8);
    }

    [Fact]
    public void NumericIntegratorHookIsUsed()
    {
        var saved = CasModule.NumericIntegrator;
        try
        {
            CasModule.NumericIntegrator = (f, a, b) => 42;
            Assert.Equal("42", Cas.Text("integrate(sin(x^2), x, 0, 1)"));
        }
        finally
        {
            CasModule.NumericIntegrator = saved;
        }
    }

    [Fact]
    public void GaussKronrodIsAccurate()
    {
        Cas.AssertClose(2, Numerics.GaussKronrod.Integrate(Math.Sin, 0, Math.PI), 1e-12);
        Cas.AssertClose(Math.Sqrt(Math.PI), Numerics.GaussKronrod.Integrate(x => Math.Exp(-x * x), double.NegativeInfinity, double.PositiveInfinity), 1e-10);
        Cas.AssertClose(1, Numerics.GaussKronrod.Integrate(x => Math.Exp(-x), 0, double.PositiveInfinity), 1e-10);
        Assert.True(double.IsNaN(Numerics.GaussKronrod.Integrate(x => 1 / x, -1, 1)));
    }
}

public class LimitTests
{
    [Theory]
    [InlineData("limit(sin(x)/x, x, 0)", "1")]
    [InlineData("limit((1 + 1/x)^x, x, inf)", "e")]
    [InlineData("limit((e^x - 1)/x, x, 0)", "1")]
    [InlineData("limit((x^2 - 1)/(x - 1), x, 1)", "2")]
    [InlineData("limit((x^3 - 8)/(x - 2), x, 2)", "12")]
    [InlineData("limit(1/x, x, 0)", "undefined")]
    [InlineData("limit(1/x, x, 0, \"+\")", "∞")]
    [InlineData("limit(1/x, x, 0, \"-\")", "-∞")]
    [InlineData("limit(1/x, x, 0, 1)", "∞")]
    [InlineData("limit(1/x^2, x, 0)", "∞")]
    [InlineData("limit(-1/x^2, x, 0)", "-∞")]
    [InlineData("limit((3x^2 + 1)/(2x^2 - x), x, inf)", "3/2")]
    [InlineData("limit((x + 1)/(x^2 + 1), x, inf)", "0")]
    [InlineData("limit((x^3 + 1)/(x^2 + 1), x, -inf)", "-∞")]
    [InlineData("limit(x e^(-x), x, inf)", "0")]
    [InlineData("limit(e^x/x^100, x, inf)", "∞")]
    [InlineData("limit(x ln(x), x, 0, \"+\")", "0")]
    [InlineData("limit(sqrt(x^2 + x) - x, x, inf)", "1/2")]
    [InlineData("limit((1 - cos(x))/x^2, x, 0)", "1/2")]
    [InlineData("limit(sin(1/x), x, 0)", "undefined")]
    [InlineData("limit(abs(x)/x, x, 0)", "undefined")]
    [InlineData("limit(abs(x)/x, x, 0, \"-\")", "-1")]
    [InlineData("limit(floor(x), x, 1, \"+\")", "1")]
    [InlineData("limit(floor(x), x, 1, \"-\")", "0")]
    [InlineData("limit(x^x, x, 0, \"+\")", "1")]
    [InlineData("limit(ln(x)/x, x, inf)", "0")]
    [InlineData("limit(atan(x), x, -inf)", "-π/2")]
    [InlineData("limit((tan(x) - x)/x^3, x, 0)", "1/3")]
    [InlineData("limit(x sin(1/x), x, 0)", "0")]
    [InlineData("limit((2^x - 1)/x, x, 0)", "ln(2)")]
    [InlineData("limit((1 + x)^(1/x), x, 0)", "e")]
    [InlineData("limit(n sin(1/n), n, inf)", "1")]
    [InlineData("limit(e^(1/x), x, 0, \"-\")", "0")]
    [InlineData("limit(e^(1/x), x, 0, \"+\")", "∞")]
    [InlineData("limit(ln(x), x, 0, \"+\")", "-∞")]
    [InlineData("limit(x^(1/x), x, inf)", "1")]
    [InlineData("limit(sin(x)/x, x, inf)", "0")]
    [InlineData("limit(x^2 + 3, x, 2)", "7")]
    [InlineData("limit(sin(x), x, pi/6)", "1/2")]
    [InlineData("limit((x^2 - a^2)/(x - a), x, a)", "2a")]
    [InlineData("limit((1 + a/x)^x, x, inf)", "e^a")]
    [InlineData("limit(sin(3x)/sin(5x), x, 0)", "3/5")]
    [InlineData("lim(x/(x + 1), x, inf)", "1")]
    [InlineData("limit({sin(x)/x, x}, x, 0)", "{1, 0}")]
    public void ClassicLimits(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void DegreeModeStandardLimit() => Assert.Equal("π/180", Cas.Text("limit(sin(x)/x, x, 0)", Cas.Degrees()));

    [Fact]
    public void OscillationAtInfinityHasNoLimit() => Assert.Equal("undefined", Cas.Text("limit(sin(x), x, inf)"));
}

public class SeriesTests
{
    [Theory]
    [InlineData("taylor(e^x, x, 0, 4)", "x⁴/24 + x³/6 + x²/2 + x + 1")]
    [InlineData("taylor(sin(x), x, 0, 7)", "-x⁷/5040 + x⁵/120 - x³/6 + x")]
    [InlineData("taylor(cos(x), x, 0, 6)", "-x⁶/720 + x⁴/24 - x²/2 + 1")]
    [InlineData("taylor(ln(x), x, 1, 3)", "(x - 1)³/3 - (x - 1)²/2 + (x - 1)")]
    [InlineData("taylor(1/(1 - x), x, 5)", "x⁵ + x⁴ + x³ + x² + x + 1")]
    [InlineData("taylor(tan(x), x, 0, 7)", "17x⁷/315 + 2x⁵/15 + x³/3 + x")]
    [InlineData("taylor(sqrt(1 + x), x, 0, 3)", "x³/16 - x²/8 + x/2 + 1")]
    [InlineData("taylor(atan(x), x, 0, 7)", "-x⁷/7 + x⁵/5 - x³/3 + x")]
    [InlineData("series(ln(1 + x), x, 0, 4)", "-x⁴/4 + x³/3 - x²/2 + x")]
    [InlineData("taylor(e^x sin(x), x, 0, 5)", "-x⁵/30 + x³/3 + x² + x")]
    [InlineData("taylor(x^3 + 2x, x, 1, 3)", "(x - 1)³ + 3(x - 1)² + 5(x - 1) + 3")]
    [InlineData("taylor(sinh(x), x, 0, 5)", "x⁵/120 + x³/6 + x")]
    [InlineData("taylor(1/cos(x), x, 0, 4)", "5x⁴/24 + x²/2 + 1")]
    [InlineData("taylor(asin(x), x, 0, 5)", "3x⁵/40 + x³/6 + x")]
    public void Coefficients(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void SeriesCoefficientsMatchDerivatives()
    {
        var s = Cas.Session();
        var t = Cas.Exact("taylor(e^(2x) cos(x), x, 0, 6)", s);
        var coeffs = (ListExpr)Cas.Exact($"coeffs({ExprPrinter.ToInput(t)}, x)", s);
        var f = Cas.Exact("e^(2x) cos(x)", s);
        for (int k = 0; k <= 6; k++)
        {
            var dk = Cas.Exact($"diff(e^(2x) cos(x), x, {k})", s);
            double expected = Cas.Value(dk, "x", 0, s) / Factorial(k);
            double actual = Cas.Value(coeffs.Items[6 - k], s);
            Cas.AssertClose(expected, actual, 1e-12);
        }
        Assert.NotNull(f);
    }

    private static double Factorial(int k) => k <= 1 ? 1 : k * Factorial(k - 1);

    [Fact]
    public void DegreeModeSeries() => Assert.Equal("-π³x³/34992000 + πx/180", Cas.Text("taylor(sin(x), x, 0, 3)", Cas.Degrees()).Replace("π³·x³", "π³x³"));
}
