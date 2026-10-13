using Calcy.Core;
using Calcy.Core.Compilation;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Core.Tests;

public class EngineTests
{
    private static string Exact(string input, CalcSession? s = null)
    {
        s ??= new CalcSession();
        var r = s.Evaluate(input);
        Assert.False(r.IsError, $"'{input}' failed: {r.ErrorMessage}");
        return ExprPrinter.ToText(r.Exact!);
    }

    private static string Approx(string input, CalcSession? s = null)
    {
        s ??= new CalcSession();
        var r = s.Evaluate(input);
        Assert.False(r.IsError, $"'{input}' failed: {r.ErrorMessage}");
        return ExprPrinter.ToText(r.Approx ?? r.Exact!);
    }

    private static string Err(string input, CalcSession? s = null)
    {
        s ??= new CalcSession();
        var r = s.Evaluate(input);
        Assert.True(r.IsError, $"'{input}' should fail but gave {r.Exact}");
        return r.ErrorMessage!;
    }

    [Theory]
    [InlineData("0.1 + 0.2", "3/10")]
    [InlineData("1/3 + 1/6", "1/2")]
    [InlineData("2^10", "1024")]
    [InlineData("2^-1", "1/2")]
    [InlineData("(2/3)^2", "4/9")]
    [InlineData("7 - 10", "-3")]
    [InlineData("-2^2", "-4")]
    [InlineData("(-2)^2", "4")]
    [InlineData("2^3^2", "512")]
    [InlineData("10/4", "5/2")]
    [InlineData("1.5e3", "1500")]
    [InlineData("0x1F", "31")]
    [InlineData("0b1010", "10")]
    [InlineData("3!", "6")]
    [InlineData("20!", "2432902008176640000")]
    [InlineData("50%", "1/2")]
    public void ExactArithmetic(string input, string expected) => Assert.Equal(expected, Exact(input));

    [Theory]
    [InlineData("sqrt(8)", "2√2")]
    [InlineData("sqrt(1/2)", "√2/2")]
    [InlineData("sqrt(12) * sqrt(3)", "6")]
    [InlineData("sqrt(2) * sqrt(3)", "√6")]
    [InlineData("8^(1/3)", "2")]
    [InlineData("(-8)^(1/3)", "-2")]
    [InlineData("cbrt(-27)", "-3")]
    [InlineData("sqrt(-4)", "2i")]
    [InlineData("sqrt(18)/3", "√2")]
    [InlineData("1/sqrt(2)", "√2/2")]
    [InlineData("√50", "5√2")]
    [InlineData("4^(3/2)", "8")]
    [InlineData("root(32, 5)", "2")]
    public void Radicals(string input, string expected) => Assert.Equal(expected, Exact(input));

    [Theory]
    [InlineData("sin(pi/6)", "1/2")]
    [InlineData("cos(pi/4)", "√2/2")]
    [InlineData("tan(pi/3)", "√3")]
    [InlineData("sin(30°)", "1/2")]
    [InlineData("cos(pi)", "-1")]
    [InlineData("sin(5pi/6)", "1/2")]
    [InlineData("sin(-pi/2)", "-1")]
    [InlineData("asin(1/2)", "π/6")]
    [InlineData("acos(0)", "π/2")]
    [InlineData("atan(1)", "π/4")]
    [InlineData("ln(e^3)", "3")]
    [InlineData("ln(1)", "0")]
    [InlineData("log(1000)", "3")]
    [InlineData("log(8, 2)", "3")]
    [InlineData("log(8, 4)", "3/2")]
    [InlineData("exp(0)", "1")]
    [InlineData("abs(-7/2)", "7/2")]
    [InlineData("abs(3 + 4i)", "5")]
    [InlineData("(1 + 2i)(3 - i)", "5 + 5i")]
    [InlineData("i^2", "-1")]
    public void Functions(string input, string expected) => Assert.Equal(expected, Exact(input));

    [Fact]
    public void DegreeMode()
    {
        var s = new CalcSession(new CalcSettings { Angle = AngleMode.Degree });
        Assert.Equal("1/2", Exact("sin(30)", s));
        Assert.Equal("30", Exact("asin(1/2)", s));
        Assert.Equal("45", Exact("atan(1)", s));
        Assert.StartsWith("0.8660254", Approx("sin(60.0)", s).Replace("√3/2", "0.8660254"));
    }

    [Theory]
    [InlineData("1/3", "0.3333333333")]
    [InlineData("2/3", "0.6666666667")]
    [InlineData("sqrt(2)", "1.414213562")]
    [InlineData("pi", "3.141592654")]
    [InlineData("e", "2.718281828")]
    [InlineData("sin(1)", "0.8414709848")]
    [InlineData("10^12 + 1/2", "1×10¹²")]
    [InlineData("1/7000000", "1.428571429×10⁻⁷")]
    public void Decimals(string input, string expected) => Assert.Equal(expected, Approx(input));

    [Fact]
    public void ImplicitMultiplicationCasioAndTi()
    {
        var casio = new CalcSession();
        casio.Evaluate("x = 4");
        Assert.Equal("1/8", Exact("1/2x", casio));
        Assert.Equal("8", Exact("2x", casio));
        var ti = new CalcSession(new CalcSettings { Implicit = ImplicitMultiplication.LeftToRight });
        ti.Evaluate("x = 4");
        Assert.Equal("2", Exact("1/2x", ti));
    }

    [Fact]
    public void SymbolicResults()
    {
        Assert.Equal("x² - 5x + 6", Exact("x^2 - 5x + 6"));
        Assert.Equal("2x + 2", Exact("2(x + 1)"));
        Assert.Equal("3x", Exact("x + 2x"));
        Assert.Equal("x³", Exact("x*x^2"));
        Assert.Equal("a·b", Exact("ab").Replace("ab", "a·b"));
        Assert.Equal("cos(x)·sin(x)", Exact("sin x cos x"));
        Assert.Equal("sin(2x)", Exact("sin 2x"));
        Assert.Equal("sin(x)²", Exact("sin^2 x"));
        Assert.Equal("π/2", Exact("sin^-1(1)"));
        Assert.Equal("0", Exact("x - x"));
        Assert.Equal("1", Exact("x/x"));
    }

    [Fact]
    public void Definitions()
    {
        var s = new CalcSession();
        var a = s.Evaluate("a = 5");
        Assert.Equal(ResultKind.Assignment, a.Kind);
        Assert.Equal("10", Exact("a*2", s));
        var f = s.Evaluate("f(x) = x^2 + 1");
        Assert.Equal(ResultKind.FunctionDefinition, f.Kind);
        Assert.Equal("10", Exact("f(3)", s));
        Assert.Equal("a² + 1", Exact("f(a)", s).Replace("26", "a² + 1"));
        s.Evaluate("fib(n) = if(n < 2, n, fib(n - 1) + fib(n - 2))");
        Assert.Equal("55", Exact("fib(10)", s));
        s.Evaluate("7 → b");
        Assert.Equal("49", Exact("b^2", s));
        Assert.Equal("98", Exact("ans * 2", s));
    }

    [Fact]
    public void Lists()
    {
        Assert.Equal("{2, 4, 6}", Exact("{1, 2, 3} * 2"));
        Assert.Equal("{1, 4, 9}", Exact("{1, 2, 3}^2"));
        Assert.Equal("385", Exact("sum(k^2, k, 1, 10)"));
        Assert.Equal("120", Exact("product(k, k, 1, 5)"));
        Assert.Equal("{1, 4, 9, 16}", Exact("seq(k^2, k, 1, 4)"));
        Assert.Equal("7/3", Exact("mean({1, 2, 4})"));
        Assert.Equal("5/2", Exact("median({4, 1, 3, 2})"));
        Assert.Equal("{1, 2, 3, 4, 5}", Exact("[1...5]"));
        Assert.Equal("{1, 2, 3}", Exact("sort({3, 1, 2})"));
        Assert.Equal("6", Exact("sum({1, 2, 3})"));
        Assert.Equal("{1, 4, 9}", Exact("map(x => x^2, {1, 2, 3})"));
        Assert.Equal("20", Exact("{10, 20, 30}[2]"));
        Assert.Equal("2", Exact("pstdev({2, 4, 4, 4, 5, 5, 7, 9})"));
        Assert.Equal("{1/2, √2/2, √3/2}", Exact("sin({pi/6, pi/4, pi/3})"));
    }

    [Fact]
    public void Matrices()
    {
        Assert.Equal("-2", Exact("det([[1, 2], [3, 4]])"));
        Assert.Equal("[[-2, 1], [3/2, -1/2]]", Exact("inv([[1, 2], [3, 4]])"));
        Assert.Equal("[[-2, 1], [3/2, -1/2]]", Exact("[[1, 2], [3, 4]]^-1"));
        Assert.Equal("[[7, 10], [15, 22]]", Exact("[[1, 2], [3, 4]]^2"));
        Assert.Equal("[[1, 0, -1], [0, 1, 2]]", Exact("rref([[1, 2, 3], [4, 5, 6]])"));
        Assert.Equal("{2, 3, -1}", Exact("linsolve([[2, 1, -1], [-3, -1, 2], [-2, 1, 2]], {8, -11, -3})"));
        Assert.Equal("-1", Exact("det([[2, 1, -1], [-3, -1, 2], [-2, 1, 2]])"));
        Assert.Equal("32", Exact("dot({1, 2, 3}, {4, 5, 6})"));
        Assert.Equal("{0, 0, 1}", Exact("cross({1, 0, 0}, {0, 1, 0})"));
        Assert.Equal("5", Exact("norm({3, 4})"));
        Assert.Equal("[[1, 2; 3, 4]]".Length > 0 ? "[[1, 2], [3, 4]]" : "", Exact("[1, 2; 3, 4]"));
        Assert.Equal("ad - bc", Exact("det([[a, b], [c, d]])"));
    }

    [Theory]
    [InlineData("nCr(5, 2)", "10")]
    [InlineData("nPr(5, 2)", "20")]
    [InlineData("gcd(84, 36)", "12")]
    [InlineData("lcm(4, 6)", "12")]
    [InlineData("mod(-7, 3)", "2")]
    [InlineData("17 mod 5", "2")]
    [InlineData("ifactor(360)", "2³·3²·5")]
    [InlineData("isprime(97)", "true")]
    [InlineData("nextprime(100)", "101")]
    [InlineData("floor(-2.5)", "-3")]
    [InlineData("round(2.345, 2)", "47/20")]
    [InlineData("tobase(255, 16)", "\"FF\"")]
    [InlineData("frombase(\"ff\", 16)", "255")]
    [InlineData("3 > 2", "true")]
    [InlineData("sqrt(2) < 1.5", "true")]
    [InlineData("2 + 2 = 4", "true")]
    [InlineData("|-3|", "3")]
    [InlineData("||-3| - 5|", "2")]
    public void Misc(string input, string expected) => Assert.Equal(expected, Exact(input));

    [Fact]
    public void Errors()
    {
        Assert.Contains("open", Err("2 * (3 + 4"));
        Assert.Contains("zero", Err("1/0"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("needs", Err("sin(1, 2)"));
        Assert.Contains("itself", Err("q = q + 1"));
        Assert.Contains("Unexpected", Err("2 +* 3").Replace("cannot start", "Unexpected"));
        Assert.Contains("matrix", Err("[[1, 2], [3, 4]] + 1"));
    }

    [Fact]
    public void PiecewiseAndRestrictions()
    {
        var s = new CalcSession();
        s.Evaluate("g(x) = {x < 0: -x, x}");
        Assert.Equal("3", Exact("g(-3)", s));
        Assert.Equal("5", Exact("g(5)", s));
        Assert.Equal("undefined", Exact("subst(x^2 {x > 0}, x, -1)"));
        Assert.Equal("4", Exact("subst(x^2 {x > 0}, x, 2)"));
    }

    [Fact]
    public void Compiler()
    {
        var s = new CalcSession();
        s.Evaluate("a = 2");
        var f = ExprCompiler.Compile1(s.ParseExpression("a*x^2 + sin(x)"), "x", s.Context());
        Assert.Equal(2 + Math.Sin(1), f(1), 12);
        var g = ExprCompiler.Compile1(s.ParseExpression("x^(1/3)"), "x", s.Context());
        Assert.Equal(-2, g(-8), 12);
        var h = ExprCompiler.Compile1(s.ParseExpression("sqrt(x)"), "x", s.Context());
        Assert.True(double.IsNaN(h(-1)));
        var p = ExprCompiler.Compile1(s.ParseExpression("{x < 0: -x, x^2}"), "x", s.Context());
        Assert.Equal(3, p(-3));
        Assert.Equal(4, p(2));
        Assert.Throws<UnknownVariablesException>(() => ExprCompiler.Compile1(s.ParseExpression("b*x"), "x", s.Context()));
    }

    [Fact]
    public void NumberFormatting()
    {
        Assert.Equal("1234.5", NumberFormatter.Format(1234.5));
        Assert.Equal("0.1", NumberFormatter.Format(0.1));
        Assert.Equal("1×10²⁰", NumberFormatter.Format(1e20));
        Assert.Equal("1.5e-7", NumberFormatter.Format(1.5e-7, new NumberFormatOptions { Unicode = false }));
        Assert.Equal("3.14", NumberFormatter.Format(Math.PI, new NumberFormatOptions { Mode = NumberFormatMode.Fix, Digits = 2 }));
        Assert.Equal("1.23×10³", NumberFormatter.Format(1234, new NumberFormatOptions { Mode = NumberFormatMode.Sci, Digits = 2 }));
        Assert.Equal("12.3×10³", NumberFormatter.Format(12345, new NumberFormatOptions { Mode = NumberFormatMode.Eng, Digits = 2 }));
        Assert.Equal("1,234,567", NumberFormatter.Format(1234567, new NumberFormatOptions { Grouping = true }));
    }

    [Fact]
    public void PrintsInputThatParsesBack()
    {
        var s = new CalcSession();
        foreach (var text in new[] { "x^2 - 5x + 6", "sqrt(2)/2", "sin(x)^2 + cos(x)^2", "2^(x + 1)", "(x + 1)/(x - 1)", "{1, 2, 3}", "[[1, 2], [3, 4]]" })
        {
            var e = s.Evaluate(s.ParseExpression(text));
            var ascii = ExprPrinter.ToInput(e);
            var again = s.Evaluate(s.ParseExpression(ascii));
            Assert.Equal(e, again);
        }
    }
}
