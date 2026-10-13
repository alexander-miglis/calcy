using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Cas.Tests;

public class ExpandTests
{
    [Theory]
    [InlineData("expand((x + 1)^3)", "x³ + 3x² + 3x + 1")]
    [InlineData("expand((x + 1)(x - 1))", "x² - 1")]
    [InlineData("expand((x - 2)^2)", "x² - 4x + 4")]
    [InlineData("expand((2x + 3)^2)", "4x² + 12x + 9")]
    [InlineData("expand(x(x + 1)(x + 2))", "x³ + 3x² + 2x")]
    [InlineData("expand((x + 1)^2 - (x - 1)^2)", "4x")]
    [InlineData("expand((x^2 + 1)^2)", "x⁴ + 2x² + 1")]
    [InlineData("expand((1 - x)^4)", "x⁴ - 4x³ + 6x² - 4x + 1")]
    [InlineData("expand(3(x + 2) - 2(x - 1))", "x + 8")]
    [InlineData("expand((x + 1)^10 - (x + 1)^10)", "0")]
    [InlineData("expand(5)", "5")]
    [InlineData("expand(x)", "x")]
    public void Univariate(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("expand((a + b)^2)", "(a + b)^2")]
    [InlineData("expand((a + b)^3)", "(a + b)^3")]
    [InlineData("expand((a + b + c)^2)", "(a + b + c)^2")]
    [InlineData("expand((x + y)(x - y))", "(x + y)(x - y)")]
    [InlineData("expand((a - b)^5)", "(a - b)^5")]
    [InlineData("expand((x + 2y)^4)", "(x + 2y)^4")]
    public void MultivariateEqualsOriginal(string input, string original)
    {
        var s = Cas.Session();
        var expanded = Cas.Exact(input, s);
        Assert.IsType<AddExpr>(expanded);
        Assert.DoesNotContain("(", ExprPrinter.ToText(expanded));
        // Same value at a sample point.
        var check = Cas.Exact($"({ExprPrinter.ToInput(expanded)}) - ({original})", s);
        var cancel = Cas.Exact($"expand({ExprPrinter.ToInput(check)})", s);
        Assert.Equal("0", ExprPrinter.ToText(cancel));
    }

    [Fact]
    public void BinomialCoefficients()
    {
        var text = Cas.Text("expand((x + 1)^6)");
        Assert.Equal("x⁶ + 6x⁵ + 15x⁴ + 20x³ + 15x² + 6x + 1", text);
        Assert.Equal("4ab³ + 6a²b² + 4a³b + a⁴ + b⁴", Cas.Text("expand((a + b)^4)"));
    }

    [Fact]
    public void KeepsDenominators()
    {
        var s = Cas.Session();
        var r = Cas.Exact("expand((x + 2)^2/(x - 1))", s);
        Assert.IsType<AddExpr>(r);
        Assert.Equal(3, ((AddExpr)r).Terms.Length);
        Cas.AssertClose(Cas.Value(Cas.Exact("(x + 2)^2/(x - 1)", s), "x", 3.3, s), Cas.Value(r, "x", 3.3, s));
    }

    [Fact]
    public void ListsAreExpandedElementwise() => Assert.Equal("{x² + 2x + 1, x² - 1}", Cas.Text("expand({(x + 1)^2, (x + 1)(x - 1)})"));

    [Fact]
    public void HugeExpansionIsRefused()
    {
        var r = Cas.Exact("expand((a + b + c + d + e + f)^40)");
        Assert.IsType<CallExpr>(r);
    }

    [Theory]
    [InlineData("expandtrig(sin(2x))", "2cos(x)·sin(x)")]
    [InlineData("expandtrig(cos(2x))", "cos(x)² - sin(x)²")]
    [InlineData("expandtrig(sin(a + b))", "cos(a)·sin(b) + cos(b)·sin(a)")]
    [InlineData("expandtrig(cos(a + b))", "cos(a)·cos(b) - sin(a)·sin(b)")]
    [InlineData("tExpand(sin(3x))", "3cos(x)²·sin(x) - sin(x)³")]
    [InlineData("tcollect(sin(x) cos(x))", "sin(2x)/2")]
    [InlineData("tcollect(cos(x)^2)", "cos(2x)/2 + 1/2")]
    [InlineData("tcollect(sin(x)^2)", "-cos(2x)/2 + 1/2")]
    [InlineData("expandlog(ln(x^2 y))", "2ln(x) + ln(y)")]
    [InlineData("expandlog(ln(x/y))", "ln(x) - ln(y)")]
    [InlineData("expandlog(log(100x))", "log(x) + 2")]
    public void TrigAndLog(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("expandtrig(cos(3x))")]
    [InlineData("expandtrig(sin(x + 2y))")]
    [InlineData("expandtrig(tan(2x))")]
    [InlineData("tcollect(sin(x)^3 cos(x)^2)")]
    [InlineData("tcollect(sin(2x) sin(3x))")]
    [InlineData("expandtrig(cosh(2x))")]
    public void TrigRewritesKeepValue(string input)
    {
        var s = Cas.Session();
        var inner = input[(input.IndexOf('(') + 1)..^1];
        var original = Cas.Exact(inner, s);
        var rewritten = Cas.Exact(input, s);
        foreach (var x in new[] { 0.3, 1.1, -0.7 })
        {
            var ctx = s.Context(true).WithLocal("x", Algebra.Q(Calcy.Core.Numbers.BigRational.Approximate(x))).WithLocal("y", Algebra.Q(Calcy.Core.Numbers.BigRational.Approximate(0.4)));
            var a = Calcy.Core.Evaluation.Evaluator.Eval(original, ctx);
            var b = Calcy.Core.Evaluation.Evaluator.Eval(rewritten, ctx);
            Cas.AssertClose(((NumberExpr)a).Value.Re, ((NumberExpr)b).Value.Re, 1e-9);
        }
    }
}

public class FactorTests
{
    [Theory]
    [InlineData("factor(x^2 - 5x + 6)", "(x - 3)·(x - 2)")]
    [InlineData("factor(x^2 - 1)", "(x - 1)·(x + 1)")]
    [InlineData("factor(x^4 + 4)", "(x² - 2x + 2)·(x² + 2x + 2)")]
    [InlineData("factor(x^2 + 2x + 1)", "(x + 1)²")]
    [InlineData("factor(2x^2 - 2)", "2(x - 1)·(x + 1)")]
    [InlineData("factor(x^3 - x)", "x·(x - 1)·(x + 1)")]
    [InlineData("factor(-x^2 + 1)", "-(x - 1)·(x + 1)")]
    [InlineData("factor(x^2 + 1)", "x² + 1")]
    [InlineData("factor(x^2/4 - 1)", "(x - 2)·(x + 2)/4")]
    [InlineData("factor(360)", "2³·3²·5")]
    [InlineData("factor(-12)", "-2²·3")]
    [InlineData("factor(x^2 - y^2)", "(x - y)·(x + y)")]
    [InlineData("factor(a^2 + 2a b + b^2)", "(a + b)²")]
    [InlineData("factor(a x + a y + b x + b y)", "(a + b)·(x + y)")]
    [InlineData("factor((x^2 - 1)/(x^2 + 2x + 1))", "(x - 1)/(x + 1)")]
    [InlineData("factor(sin(x)^2 - 1)", "(sin(x) - 1)·(sin(x) + 1)")]
    [InlineData("factor(x^2 - 3, x)", "(x - √3)·(x + √3)")]
    public void Examples(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void SixthPowerMinusOneFactorsCompletely()
    {
        var s = Cas.Session();
        var (constant, factors) = Cas.FactorMultiset(Cas.Exact("factor(x^6 - 1)", s), s);
        Assert.Equal("1", constant);
        Assert.Equal(["x + 1", "x - 1", "x² + x + 1", "x² - x + 1"], factors.OrderBy(f => f.Length).ThenBy(f => f, StringComparer.Ordinal));
    }

    public static TheoryData<string> FactoredForms =>
    [
        "(x - 1)(x + 2)",
        "(x - 1)^2 (x + 3)",
        "(2x + 1)(3x - 2)",
        "(x^2 + 1)(x - 5)",
        "(x^2 - 2x + 2)(x^2 + 2x + 2)",
        "(x - 1)(x + 1)(x^2 + x + 1)(x^2 - x + 1)",
        "(x^2 + x + 1)(x^3 - x^2 + 1)",
        "(x - 2)^3 (x + 1)^2",
        "(x^4 + 1)(x^2 + 3)",
        "(5x^3 + 2x - 9)(x^2 - 3x + 9)",
        "(x^3 + 2)(x^3 - 3)",
        "3(x - 1)(x + 4)",
        "(x^5 - x - 1)(x^2 + 1)",
        "(x^2 - 2)(x^2 - 3)(x^2 - 5)",
        "(x^4 - 10x^2 + 1)(x + 7)",
        "(4x^2 - 3)(9x^2 + 2)",
        "(x - y)(x + y)",
        "(a + b)(x + y)",
        "(x + y)(x y + 1)",
        "(x + y)^3",
        "(x - y)(x^2 + x y + y^2)",
        "(2a - 3b)(2a + 3b)",
        "(x^2 + y^2)(x - y)(x + y)",
        "(a + b + c)(a - b)",
        "x y (x + 1)(y - 2)",
    ];

    [Theory]
    [MemberData(nameof(FactoredForms))]
    public void FactorOfExpandIsOriginal(string factored)
    {
        var s = Cas.Session();
        var expanded = Cas.Exact($"expand({factored})", s);
        var refactored = Cas.Exact($"factor({ExprPrinter.ToInput(expanded)})", s);
        var expected = Cas.FactorMultiset(Cas.Parse(factored, s), s);
        var actual = Cas.FactorMultiset(refactored, s);
        Assert.Equal(expected.Constant, actual.Constant);
        Assert.Equal(Cas.Join(expected.Factors), Cas.Join(actual.Factors));
    }

    [Theory]
    [MemberData(nameof(FactoredForms))]
    public void ExpandOfFactorIsExpand(string factored)
    {
        var s = Cas.Session();
        var expanded = ExprPrinter.ToInput(Cas.Exact($"expand({factored})", s));
        var f = ExprPrinter.ToInput(Cas.Exact($"factor({expanded})", s));
        Assert.Equal(Cas.Text($"expand({expanded})", s), Cas.Text($"expand({f})", s));
    }

    [Theory]
    [InlineData("x^4 + x^3 + x^2 + x + 1")]
    [InlineData("x^5 + x + 3")]
    [InlineData("x^4 - 2")]
    [InlineData("x^2 + x y + y^2 + 1")]
    [InlineData("x^3 + y^3 + 1")]
    public void IrreducibleStaysWhole(string p)
    {
        var s = Cas.Session();
        var f = Cas.Exact($"factor({p})", s);
        Assert.IsType<AddExpr>(f);
    }
}

public class SimplifyTests
{
    [Theory]
    [InlineData("simplify((x^2 - 1)/(x - 1))", "x + 1")]
    [InlineData("simplify(sin(x)^2 + cos(x)^2)", "1")]
    [InlineData("simplify(tan(x) cos(x))", "sin(x)")]
    [InlineData("simplify(sqrt(x^2))", "|x|")]
    [InlineData("simplify(ln(e^x))", "x")]
    [InlineData("simplify(e^(ln(x)))", "x")]
    [InlineData("simplify(e^(2ln(x)))", "x²")]
    [InlineData("simplify(sqrt(3 + 2sqrt(2)))", "1 + √2")]
    [InlineData("simplify(sqrt(5 - 2sqrt(6)))", "√3 - √2")]
    [InlineData("simplify((x^3 - 1)/(x^2 + x + 1))", "x - 1")]
    [InlineData("simplify(1/x + 1/y)", "1/x + 1/y")]
    [InlineData("simplify(1 - cos(x)^2)", "sin(x)²")]
    [InlineData("simplify(sec(x)^2 - tan(x)^2)", "1")]
    [InlineData("simplify(cosh(x)^2 - sinh(x)^2)", "1")]
    [InlineData("simplify(sin(asin(x)))", "x")]
    [InlineData("simplify(cos(asin(x)))", "√(-x² + 1)")]
    [InlineData("simplify((x + 1)^2 - x^2 - 2x)", "1")]
    [InlineData("simplify(x/(x^2 + x))", "1/(x + 1)")]
    [InlineData("simplify(abs(x^2 + 1))", "x² + 1")]
    [InlineData("simplify((a^2 - b^2)/(a - b))", "a + b")]
    public void Examples(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("together(1/x + 1/(x + 1))", "(2x + 1)/(x·(x + 1))")]
    [InlineData("together(1/(x - 1) - 1/(x + 1))", "2/((x - 1)·(x + 1))")]
    [InlineData("cancel((x^2 - 1)/(x + 1))", "x - 1")]
    [InlineData("cancel((x^2 + 2x + 1)/(x^2 - 1))", "(x + 1)/(x - 1)")]
    [InlineData("together(a/b + c/d)", "(ad + bc)/(bd)")]
    public void CommonDenominator(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void SimplifyNeverChangesValue()
    {
        var s = Cas.Session();
        foreach (var input in new[] { "(x^4 - 1)/(x^2 + 1)", "sin(x)^4 - cos(x)^4", "(x + 1)/(x^2 - 1) + 1/(x + 1)", "tan(x)^2 + 1", "x/(x + 1) + 1/(x + 1)" })
        {
            var original = Cas.Exact(input, s);
            var simplified = Cas.Exact($"simplify({input})", s);
            foreach (var x in new[] { 0.3, 1.7, -2.2 })
                Cas.AssertClose(Cas.Value(original, "x", x, s), Cas.Value(simplified, "x", x, s), 1e-9);
            Assert.True(simplified.NodeCount() <= original.NodeCount() + 2, $"{input} became {ExprPrinter.ToText(simplified)}");
        }
    }
}

public class PolynomialToolTests
{
    [Theory]
    [InlineData("coeffs(3x^2 + 2x + 1, x)", "{3, 2, 1}")]
    [InlineData("coeffs(x^3 - x, x)", "{1, 0, -1, 0}")]
    [InlineData("coeffs(a x^2 + b x + c, x)", "{a, b, c}")]
    [InlineData("degree(x^3 + x, x)", "3")]
    [InlineData("degree(x^2 y^3 + x, x)", "2")]
    [InlineData("degree(x^2 y^3 + x)", "5")]
    [InlineData("degree(7, x)", "0")]
    [InlineData("lcoeff(3x^2 + 1, x)", "3")]
    [InlineData("lcoeff(a x^3 - x, x)", "a")]
    [InlineData("collect(a x + b x + c, x)", "(a + b)·x + c")]
    [InlineData("polyquo(x^3 - 1, x - 1, x)", "x² + x + 1")]
    [InlineData("quo(x^3 - 1, x - 1, x)", "x² + x + 1")]
    [InlineData("polyrem(x^3 + 2, x - 1, x)", "3")]
    [InlineData("remainder(x^4 + 1, x^2 + 1, x)", "2")]
    [InlineData("polygcd(x^2 - 1, x^2 + 2x + 1)", "x + 1")]
    [InlineData("polygcd(2x^2 - 2, 4x - 4)", "2x - 2")]
    [InlineData("polylcm(x^2 - 1, x + 1)", "x² - 1")]
    [InlineData("polygcd(x^2 y - y, x y + y)", "xy + y")]
    [InlineData("discriminant(x^2 - 5x + 6, x)", "1")]
    [InlineData("discriminant(a x^2 + b x + c, x)", "-4ac + b²")]
    [InlineData("discriminant(x^3 + p x + q, x)", "-4p³ - 27q²")]
    [InlineData("resultant(x^2 - 1, x - 1, x)", "0")]
    [InlineData("resultant(x^2 + 1, x - 2, x)", "5")]
    [InlineData("compsquare(x^2 + 6x + 5, x)", "(x + 3)² - 4")]
    [InlineData("completesquare(2x^2 + 4x + 5, x)", "2(x + 1)² + 3")]
    [InlineData("compsquare(x^2 - x, x)", "(x - 1/2)² - 1/4")]
    public void Examples(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("1/(x^2 - 1)")]
    [InlineData("(x^2 + 1)/((x - 1)^2 (x + 2))")]
    [InlineData("1/(x^3 + x)")]
    [InlineData("(x^3 + 1)/(x^2 - 4)")]
    [InlineData("(3x + 5)/((x^2 + 1)^2 (x - 1))")]
    [InlineData("x^4/((x - 1)^3)")]
    public void PartialFractionsKeepValue(string input)
    {
        var s = Cas.Session();
        var original = Cas.Exact(input, s);
        var parts = Cas.Exact($"apart({input}, x)", s);
        Assert.IsType<AddExpr>(parts);
        foreach (var x in new[] { 0.37, 2.9, -3.3 })
            Cas.AssertClose(Cas.Value(original, "x", x, s), Cas.Value(parts, "x", x, s), 1e-9);
    }

    [Fact]
    public void PartialFractionShape()
    {
        Assert.Equal("-1/(2(x + 1)) + 1/(2(x - 1))", Cas.Text("apart(1/(x^2 - 1), x)"));
        Assert.Equal("1/x - x/(x² + 1)", Cas.Text("partfrac(1/(x^3 + x), x)"));
    }

    [Fact]
    public void NonPolynomialIsReported() => Assert.Contains("not a polynomial", Cas.Error("degree(sin(x), x)"));
}
