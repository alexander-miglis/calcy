using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Scripting.Tests;

public class CalcModuleTests
{
    [Fact]
    public void ExactResultsBecomePythonNumbers()
    {
        using var t = new TestHost();
        var output = t.Out("""
import calc
r = calc.eval("1/3 + 1/6")
print(type(r).__name__, r, r.numerator, r.denominator, float(r))
print(type(calc.eval("2^100")).__name__, calc.eval("2^100"))
print(calc.eval("0.1 + 0.2"), calc.eval("6/3"), type(calc.eval("6/3")).__name__)
print(calc.eval("{1, 2, 3}"), calc.eval("[[1, 2], [3, 4]]^-1"))
print(calc.eval("(1, 2)"), calc.eval("2 + 3i"), calc.eval("1 < 2"), calc.eval("\"hi\""))
""");
        Assert.Equal("""
Fraction 1/2 1 2 0.5
int 1267650600228229401496703205376
3/10 2 int
[1, 2, 3] [[-2, 1], [3/2, -1/2]]
(1, 2) (2+3j) True hi

""".Replace("\r\n", "\n").TrimStart('\n'), output);
    }

    [Fact]
    public void SymbolicResultsAreExpressions()
    {
        using var t = new TestHost();
        var output = t.Out("""
import calc
r = calc.eval("sqrt(8)")
print(type(r).__name__, r, r.text, round(float(r), 6), r.latex)
print([calc.eval("pi"), calc.eval("x^2 + 1")])
f = calc.eval("x^3 - 3x")
print(f + " = 0", calc.solve(f + " = 0", "x"))
print(f * 2, f.subs(x=2), f(3), r == calc.eval("2sqrt(2)"))
print(calc.n("sqrt(2)"), calc.eval("1/4", numeric=True), calc.eval("pi", True))
""");
        var lines = output.Split('\n');
        Assert.Equal("Expr 2√2 2*sqrt(2) 2.828427 2 \\sqrt{2}", lines[0]);
        Assert.Equal("[π, x² + 1]", lines[1]);
        Assert.Equal("x^3 - 3*x = 0 [-√3, 0, √3]", lines[2]);
        Assert.Equal("2x³ - 6x 2 18 True", lines[3]);
        Assert.Equal("1.4142135623730951 0.25 3.141592653589793", lines[4]);
    }

    [Fact]
    public void ConvenienceWrappers()
    {
        using var t = new TestHost();
        var output = t.Out("""
from calc import solve, diff, integrate, factor, expand, exact, latex, simplify, limit
print(solve("x^2 = 2"))
print(solve(["x + y = 3", "x - y = 1"], ["x", "y"]))
print(solve("x^2 = -4"), solve("x = x + 1"))
print(diff("x^3 - 3x"), diff("sin(x)", "x", 2))
print(integrate("x^2"), integrate("x^2", "x", 0, 3))
print(factor("x^2 - 1"), factor("360"), expand("(x + 1)^3"))
print(exact("sqrt(8)"), exact("sqrt(8)", plain=True), latex("1/2"))
print(limit("sin(x)/x", "x", 0))
""");
        Assert.Equal("""
[-√2, √2]
[{'x': 2, 'y': 1}]
[-2j, 2j] []
3x² - 3 -sin(x)
x³/3 9
(x - 1)·(x + 1) 2³·3²·5 x³ + 3x² + 3x + 1
2√2 2*sqrt(2) \frac{1}{2}
1

""".Replace("\r\n", "\n").TrimStart('\n'), output);
    }

    [Fact]
    public void SetAndGetShareTheCalculatorSession()
    {
        using var t = new TestHost();
        t.Session.Evaluate("c = 7");
        t.Session.Evaluate("g(x) = x^2 + c");
        var output = t.Out("""
import calc
from fractions import Fraction
calc.set("a", 3)
calc.set("b", Fraction(1, 3))
calc.set("v", [1, 2, 3])
calc.set("m", [[1, 2], [3, 4]])
calc.set("h", 0.25)
print(calc.get("c"), calc.get("nothing"), calc.get("nothing", 0), calc.get("g")(2))
print(sorted(calc.variables().keys()))
print(calc.delete("v"), calc.delete("v"))
""");
        Assert.Equal("7 None 0 11\n['a', 'b', 'c', 'h', 'm', 'v']\nTrue False\n", output);
        Assert.Equal("4", Text(t.Session, "a + 1"));
        Assert.Equal("1", Text(t.Session, "b * 3"));
        Assert.Equal("-2", Text(t.Session, "det(m)"));
        Assert.Equal("0.5", Text(t.Session, "h * 2"));
        Assert.False(t.Session.Env.TryGetVariable("v", out _));
    }

    [Fact]
    public void CalcErrorsRaiseValueError()
    {
        using var t = new TestHost();
        var output = t.Out("""
import calc
try:
    calc.eval("1/0")
except calc.CalcError as e:
    print("error:", e)
try:
    calc.set("sin", 1)
except ValueError as e:
    print("error:", e)
""");
        Assert.Equal("error: calc: Division by zero in '1/0'\nerror: calc: 'sin' is a calculator function; pick another variable name\n", output);
    }

    [Fact]
    public void PythonFunctionsBecomeCalculatorFunctions()
    {
        using var t = new TestHost();
        var r = t.Run("""
import calc
from fractions import Fraction

@calc.function
def sq(x):
    "The square plus one."
    return x * x + 1

@calc.function(name="half")
def h(x):
    return Fraction(1, 2) * x

print(calc.eval("sq(3) + half(5)"))
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("25/2\n", t.Output);
        // After the program ended the calculator can still call them, exactly.
        Assert.Equal("10", Text(t.Session, "sq(3)"));
        Assert.Equal("5/2", Text(t.Session, "half(5)"));
        Assert.Equal("{2, 5, 10}", Text(t.Session, "{sq(1), sq(2), sq(3)}"));
        Assert.True(t.Session.Functions.TryGet("sq", out var def));
        Assert.Equal("The square plus one.", def.Description);
        Assert.Contains("sq", t.Host.CalcFunctions);
        // Numeric evaluation (for graphs) works with symbolic placeholders kept unevaluated.
        var approx = t.Session.Evaluate(t.Session.ParseExpression("sq(0.5)"), approximate: true);
        Assert.Equal(1.25, ((NumberExpr)approx).Value.Re, 12);
        var symbolic = t.Session.Evaluate(t.Session.ParseExpression("sq(x)"));
        Assert.IsType<CallExpr>(symbolic);
    }

    [Fact]
    public void CalculatorFunctionErrorsAndBuiltinsAreReported()
    {
        using var t = new TestHost();
        var r = t.Run("""
import calc
@calc.function
def bad(x):
    return 1 / 0
@calc.function
def nothing(x):
    pass
try:
    @calc.function
    def sin(x):
        return x
except ValueError as e:
    print(e)
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("calc: 'sin' is a built-in calculator function; pick another name\n", t.Output);
        var bad = t.Session.Evaluate("bad(1)");
        Assert.True(bad.IsError);
        Assert.Contains("ZeroDivisionError", bad.ErrorMessage);
        var none = t.Session.Evaluate("nothing(1)");
        Assert.True(none.IsError);
        Assert.Contains("returned None", none.ErrorMessage);
        Assert.True(t.Session.Functions.Contains("bad"));
        t.Host.Dispose();
        // Closing the host takes its functions out of the calculator again.
        Assert.False(t.Session.Functions.Contains("bad"));
        Assert.False(t.Session.Functions.Contains("nothing"));
        Assert.True(t.Session.Functions.Contains("sin"));
    }

    [Fact]
    public void ShowEmitsTypesetOutput()
    {
        using var t = new TestHost();
        var output = t.Out("import calc\ncalc.show(calc.eval('sqrt(8)'))\ncalc.show('1/2 + x', 'y =')\nfrom fractions import Fraction\ncalc.show(Fraction(3, 4))");
        Assert.Equal("[math 2*sqrt(2)]\n[math 1/2 + x]\n[math (3/4)]\n", output);
    }

    [Fact]
    public void ExpressionsAreStrings()
    {
        using var t = new TestHost();
        var output = t.Out("""
import calc
r = calc.eval("sqrt(8)")
print(isinstance(r, str), r, r.text, r.upper(), len(r), "f = " + r, r + " = 0", r == "2*sqrt(2)", r == "2√2")
d = calc.diff("x^3")
print(f"{d}", "%s" % d, [d], calc.eval(d + " + 1"), calc.solve(d + " = 12"))
calc.set("w", d)
print(calc.get("w"), {d: 1}[d], d * 2, d / 3, -d)
""");
        Assert.Equal("""
True 2√2 2*sqrt(2) 2*SQRT(2) 9 f = 2*sqrt(2) 2*sqrt(2) = 0 True True
3x² 3x² [3x²] 3x² + 1 [-2, 2]
3x² 1 6x² x² -3x²

""".Replace("\r\n", "\n").TrimStart('\n'), output);
        Assert.Equal("3x²", t.Session.Format(t.Session.Evaluate("w")).Primary);
    }

    [Fact]
    public void GraphsCallPythonFunctionsDirectly()
    {
        using var t = new TestHost();
        t.Out("import calc\n@calc.function\ndef sq(x):\n    return x * x + 1\n@calc.function\ndef bad(x):\n    return 1 / 0");
        var ctx = t.Session.Context(approximate: true);
        var f = Calcy.Core.Compilation.ExprCompiler.Compile1(t.Session.ParseExpression("sq(x) + 2"), "x", ctx);
        Assert.Equal(12, f(3), 12);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double sum = 0;
        for (int i = 0; i < 20000; i++) sum += f(i / 1000.0);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), sw.Elapsed.ToString());
        Assert.True(sum > 0);
        // Failing calls give NaN, which plots as a gap.
        var g = Calcy.Core.Compilation.ExprCompiler.Compile1(t.Session.ParseExpression("bad(x)"), "x", ctx);
        Assert.True(double.IsNaN(g(1)));
        // Several threads at once (graphs sample in parallel) are serialised.
        var results = new double[8];
        Parallel.For(0, 8, i => results[i] = f(i));
        for (int i = 0; i < 8; i++) Assert.Equal(i * i + 3, results[i], 12);
    }

    [Fact]
    public void StarImportKeepsPythonBuiltins()
    {
        using var t = new TestHost();
        var output = t.Out("from calc import *\nprint(sorted(set([3, 1, 3])), eval('1 + 1'), solve('x^2 = 9'), n('1/8'))");
        Assert.Equal("[1, 3] 2 [-3, 3] 0.125\n", output);
    }

    [Fact]
    public void FractionsModuleIsExact()
    {
        using var t = new TestHost();
        var output = t.Out("""
from fractions import Fraction
a = Fraction(1, 3) + Fraction(1, 6)
print(a, a == Fraction("1/2"), Fraction("0.75"), Fraction(0.5), Fraction(3, -6))
print(a * 2, a ** 2, a ** -1, 1 - a, a + 0.25, a < 1, sorted([Fraction(2, 3), Fraction(1, 4)]))
print(Fraction(355, 113).limit_denominator(10), int(Fraction(-7, 2)), round(Fraction(5, 2)), float(Fraction(1, 8)))
print(hash(Fraction(4, 2)) == hash(2), Fraction(4, 2) == 2, {Fraction(1, 2): 'half'}[Fraction(2, 4)])
""");
        Assert.Equal("""
1/2 True 3/4 1/2 -1/2
1 1/4 2 1/2 0.75 True [1/4, 2/3]
22/7 -3 2 0.125
True True half

""".Replace("\r\n", "\n").TrimStart('\n'), output);
    }

    [Fact]
    public void GraphRequestsAreRecorded()
    {
        using var t = new TestHost();
        var r = t.Run("""
import calc
calc.graph.add("y = x^2")
calc.graph.add("x^2 + y^2 = 9", color=2)
calc.graph.window(-5, 5, -4, 4)
try:
    calc.graph.add("y = (x + ")
except ValueError as e:
    print("bad graph")
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("bad graph\n", t.Output);
        Assert.Equal(["y = x^2", "x^2 + y^2 = 9"], t.Host.Graphs.Items.Select(g => g.Text));
        Assert.Equal(2, t.Host.Graphs.Items[1].ColorIndex);
        Assert.Equal(-5, t.Host.Graphs.Window!.Value.XMin);
        Assert.Equal(2, r.GraphCount);
        t.Run("x = 1");
        Assert.Empty(t.Host.Graphs.Items);
    }

    private static string Text(CalcSession s, string input)
    {
        var r = s.Evaluate(input);
        Assert.False(r.IsError, r.ErrorMessage);
        return s.Format(r).Primary;
    }
}
