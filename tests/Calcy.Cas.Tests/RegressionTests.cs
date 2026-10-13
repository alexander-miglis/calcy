using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Cas.Tests;

/// <summary>Cases found by fuzzing every function with unusual inputs.</summary>
public class RegressionTests
{
    [Theory]
    [InlineData("integrate(tan(x)/x, x, -1, 2)", "undefined")]
    [InlineData("integrate(1/(x^2 - 2), x, 0, 2)", "undefined")]
    [InlineData("integrate(1/(x^3 - 2), x, 0, 2)", "undefined")]
    [InlineData("integrate(1/(x - 2)^2, x, 0, 3)", "∞")]
    [InlineData("integrate(sec(x)^4, x, -1, 2)", "∞")]
    public void PolesInsideTheIntervalAreNotIntegratedThrough(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("integrate(sec(x)^4, x, 1, inf)")]
    [InlineData("integrate(x y^2 + y, x, 1, inf)")]
    public void UnknownImproperIntegralsStayUnevaluated(string input) => Assert.IsType<CallExpr>(Cas.Exact(input));

    [Theory]
    [InlineData("integrate(1/sqrt(abs(x)), x, -1, 1)", 4.0)]
    [InlineData("integrate(tan(x)/x, x, -1, 1)", 2.298302461)]
    [InlineData("integrate(1/(x^3 - 2), x, -1, 1)", -1.041540517)]
    [InlineData("integrate(1/(x^2 - 2), x, -1, 1)", -1.246450480)]
    public void IntegrableSingularities(string input, double expected)
    {
        var s = Cas.Session();
        Cas.AssertClose(expected, Cas.Value(Cas.Exact(input, s), s), 1e-8);
    }

    [Theory]
    [InlineData("taylor(floor(x), x, 0, 4)")]
    [InlineData("taylor(sign(x), x, 0, 4)")]
    [InlineData("taylor(abs(x), x, 0, 3)")]
    [InlineData("taylor(atan(1/x), x, 0, 4)")]
    public void NoTaylorSeriesAtADiscontinuity(string input) => Assert.IsType<CallExpr>(Cas.Exact(input));

    [Theory]
    [InlineData("taylor(abs(x), x, 1, 3)", "x")]
    [InlineData("taylor(sqrt(x), x, 0, 2)", "√x")]
    [InlineData("taylor(sin(x)/x, x, 0, 4)", "x⁴/120 - x²/6 + 1")]
    public void OneSidedOrSmoothSeries(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void ZeroPlateausAreNotListedAsRoots()
    {
        Assert.IsType<CallExpr>(Cas.Exact("solve(floor(x) = 2, x)"));
        Assert.Contains("whole intervals", Cas.Error("nsolve(floor(x) = 2, x)"));
    }

    [Theory]
    [InlineData("1/(x^2 - 2)")]
    [InlineData("(x + 3)/(x^2 - 2x - 1)")]
    [InlineData("2^x 3^x")]
    [InlineData("x/(x^2 + x + 1)^3")]
    [InlineData("(x^3 - 1)/(x - 1)")]
    [InlineData("x sin(x) cos(2x)")]
    public void MoreAntiderivatives(string f)
    {
        var s = Cas.Session();
        var integrand = Cas.Exact(f, s);
        var F = Cas.Exact($"integrate({f}, x)", s);
        Assert.False(F is CallExpr { Name: "integrate" }, $"no antiderivative for {f}");
        foreach (var x in new[] { 0.37, 1.71, -0.45 })
        {
            double expected = Cas.Value(integrand, "x", x, s);
            if (!double.IsFinite(expected)) continue;
            Cas.AssertClose(expected, Cas.NumericDerivative(F, "x", x, s), 1e-5);
        }
    }

    [Fact]
    public void AntiderivativesHaveNoConstantTerm() => Assert.Equal("x³/3 + x²/2 + x", Cas.Text("integrate((x^3 - 1)/(x - 1), x)"));

    [Fact]
    public void ComplexCoefficientsIntegrate() => Assert.DoesNotContain("integrate", Cas.Text("integrate(i x + 1, x)"));

    [Theory]
    [InlineData("factor(e^(-x^2))", "e^(-x²)")]
    [InlineData("factor(x sin(1/x))", "x·sin(1/x)")]
    [InlineData("factor(-x - 1)", "-(x + 1)")]
    public void FactorLeavesUnfactorableAlone(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void StepsForConstantEquations() => Assert.Equal("Solution: false", Cas.Text("steps(solve(0 = 1, x))").Split("\", \"")[^1].TrimEnd('}', '"'));

    [Fact]
    public void OddFunctionsOfNegativeNumbersAreNormalized()
    {
        var text = Cas.Text("integrate(sin(x)^2, x, -1, 2)");
        Assert.DoesNotContain("sin(-", text);
    }

    [Fact]
    public void LogsOfIntegersCombine() => Assert.Equal("ln(3)/2 - ln(2)/2", Cas.Text("integrate(1/(x^2 - 1), x, 2, 3)"));

    [Fact]
    public void ResultsPrintAsInput()
    {
        // Results must read back as the same value (round trip through the ASCII printer).
        var s = Cas.Session();
        foreach (var input in new[] { "factor(x^4 + 4)", "integrate(x e^x, x)", "taylor(ln(x), x, 1, 3)", "compsquare(x^2 + 6x + 5, x)", "apart(1/(x^2 - 1), x)", "diff(x^2 sin(x), x)" })
        {
            var r = Cas.Exact(input, s);
            var ascii = ExprPrinter.ToInput(r);
            var difference = Cas.Exact($"simplify(expand(({ascii}) - ({ExprPrinter.ToInput(s.Evaluate(r))})))", s);
            Assert.True(difference.IsZero, $"{input}: {ascii} does not read back ({ExprPrinter.ToText(difference)})");
        }
    }
}
