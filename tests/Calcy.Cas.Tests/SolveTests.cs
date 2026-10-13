using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Cas.Tests;

public class SolveTests
{
    [Theory]
    [InlineData("solve(2x + 3 = 7, x)", "x = 2")]
    [InlineData("solve(3x - 5 = x + 1, x)", "x = 3")]
    [InlineData("solve(x/2 + 1/3 = 1, x)", "x = 4/3")]
    [InlineData("solve(x^2 - 5x + 6 = 0, x)", "x = 2 or x = 3")]
    [InlineData("solve(x^2 = 4, x)", "x = -2 or x = 2")]
    [InlineData("solve(x^2 - 2x + 1 = 0, x)", "x = 1")]
    [InlineData("solve(x^2 + 1 = 0, x)", "x = -i or x = i")]
    [InlineData("solve(x^3 - 6x^2 + 11x - 6 = 0, x)", "x = 1 or x = 2 or x = 3")]
    [InlineData("solve(2x^3 - 3x^2 - 3x + 2 = 0, x)", "x = -1 or x = 1/2 or x = 2")]
    [InlineData("solve(x^3 - 2 = 0, x)", "x = ∛2")]
    [InlineData("solve(x^4 - 5x^2 + 4 = 0, x)", "x = -2 or x = -1 or x = 1 or x = 2")]
    [InlineData("solve(x^3 = x, x)", "x = -1 or x = 0 or x = 1")]
    [InlineData("solve(x^2 = 2, x)", "x = -√2 or x = √2")]
    [InlineData("solve(e^x = 5, x)", "x = ln(5)")]
    [InlineData("solve(2^x = 8, x)", "x = 3")]
    [InlineData("solve(3^(x + 1) = 27, x)", "x = 2")]
    [InlineData("solve(e^(2x) - 3e^x + 2 = 0, x)", "x = 0 or x = ln(2)")]
    [InlineData("solve(ln(x) = 2, x)", "x = e²")]
    [InlineData("solve(log(x) = 2, x)", "x = 100")]
    [InlineData("solve(sqrt(x + 3) = x - 3, x)", "x = 6")]
    [InlineData("solve(x - 3sqrt(x) + 2 = 0, x)", "x = 1 or x = 4")]
    [InlineData("solve(sqrt(x) = -2, x)", "false")]
    [InlineData("solve(x^(2/3) = 4, x)", "x = -8 or x = 8")]
    [InlineData("solve(abs(x - 1) = 3, x)", "x = -2 or x = 4")]
    [InlineData("solve(abs(x - 1) = 2x, x)", "x = 1/3")]
    [InlineData("solve(abs(2x + 1) = -1, x)", "false")]
    [InlineData("solve(x e^x - e^x = 0, x)", "x = 1")]
    [InlineData("solve(sin(x) = 2, x)", "false")]
    [InlineData("solve(e^x = -1, x)", "false")]
    [InlineData("solve(0 = 0, x)", "true")]
    [InlineData("solve(x = x + 1, x)", "false")]
    [InlineData("solve(a x + b = 0, x)", "x = -b/a")]
    [InlineData("solve(x^2 - a^2 = 0, x)", "x = a or x = -a")]
    [InlineData("solve(x^2 - 5x + 6, x)", "x = 2 or x = 3")]
    [InlineData("solve(x^2 = 9)", "x = -3 or x = 3")]
    public void Exact(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void QuadraticWithRadicals()
    {
        var s = Cas.Session();
        var r = Cas.Exact("solve(x^2 - 5x + 3 = 0, x)", s);
        var text = ExprPrinter.ToText(r);
        // (5 ± √13)/2, printed by ExprPrinter (sum order may put the radical first).
        Assert.Contains("√13", text);
        Assert.Contains("/2", text);
        Assert.True(text is "x = (-√13 + 5)/2 or x = (√13 + 5)/2" or "x = (5 - √13)/2 or x = (5 + √13)/2", text);
        Cas.AssertSameValues([(5 - Math.Sqrt(13)) / 2, (5 + Math.Sqrt(13)) / 2], Cas.SolutionValues(r, s));
    }

    [Theory]
    [InlineData("solve(x^2 - 4x + 1 = 0, x)", new[] { 0.2679491924311228, 3.732050807568877 })]
    [InlineData("solve(2x^2 + 3x - 1 = 0, x)", new[] { -1.7807764064044151, 0.28077640640441515 })]
    [InlineData("solve(x^4 - 4x^2 + 1 = 0, x)", new[] { -1.9318516525781366, -0.5176380902050415, 0.5176380902050415, 1.9318516525781366 })]
    [InlineData("solve(x^5 - x - 1 = 0, x)", new[] { 1.1673039782614187 })]
    [InlineData("solve(x^3 + x + 1 = 0, x)", new[] { -0.6823278038280193 })]
    [InlineData("solve(cos(x) = x, x)", new[] { 0.7390851332151607 })]
    [InlineData("solve(e^x + e^(-x) = 4, x)", new[] { -1.3169578969248166, 1.3169578969248166 })]
    [InlineData("solve(x^x = 4, x)", new[] { 2.0 })]
    [InlineData("solve(x^3 - 3x + 1 = 0, x)", new[] { -1.8793852415718169, 0.34729635533386066, 1.532088886237956 })]
    public void Values(string input, double[] expected)
    {
        var s = Cas.Session();
        Cas.AssertSameValues(expected, Cas.SolutionValues(Cas.Exact(input, s), s));
    }

    [Fact]
    public void ComplexRootsOnlyWhenThereAreNoRealOnes()
    {
        var s = Cas.Session();
        Assert.Equal("x = 1", Cas.Text("solve(x^3 - 1 = 0, x)", s));
        var text = Cas.Text("solve(x^2 + x + 1 = 0, x)", s);
        Assert.Contains("i", text);
        Assert.Contains("√3", text);
        var complex = Cas.Session(new CalcSettings { Complex = ComplexMode.Complex });
        var all = Cas.Text("solve(x^3 - 1 = 0, x)", complex);
        Assert.Equal(3, all.Split(" or ").Length);
    }

    [Theory]
    [InlineData("solve(1/(x - 1) + 1/(x + 1) = 2/(x^2 - 1), x)", "false")]
    [InlineData("solve(x/(x - 2) = 2/(x - 2), x)", "false")]
    [InlineData("solve(x^2/(x - 1) = 1/(x - 1), x)", "x = -1")]
    [InlineData("solve((x^2 - 4)/(x - 2) = 0, x)", "x = -2")]
    [InlineData("solve(1/x = 2, x)", "x = 1/2")]
    [InlineData("solve(x + 1/x = 2, x)", "x = 1")]
    public void RationalEquationsRejectExtraneousRoots(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("solve(sin(x) = 1/2, x)", "x = 2πn + π/6 or x = 2πn + 5π/6")]
    [InlineData("solve(cos(x) = 0, x)", "x = πn + π/2")]
    [InlineData("solve(tan(x) = 1, x)", "x = πn + π/4")]
    [InlineData("solve(sin(x) = 0, x)", "x = πn")]
    [InlineData("solve(cos(x) = 1, x)", "x = 2πn")]
    [InlineData("solve(sin(x) = cos(x), x)", "x = πn + π/4")]
    [InlineData("solve(sin(2x) = 1, x)", "x = πn + π/4")]
    public void TrigGeneralSolutions(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("solve(sin(2x) = cos(x), x)", 3)]
    [InlineData("solve(2cos(x)^2 + sin(x) - 1 = 0, x)", 3)]
    [InlineData("solve(sin(x)^2 - 3sin(x)cos(x) + 2cos(x)^2 = 0, x)", 2)]
    [InlineData("solve(sin(x) + cos(x) = 1, x)", 2)]
    [InlineData("solve(2sin(x)^2 = 1, x)", 4)]
    public void TrigFamiliesAreValid(string input, int families)
    {
        var s = Cas.Session();
        var r = Cas.Exact(input, s);
        var parts = r is LogicExpr l ? l.Operands.ToList() : [r];
        Assert.Equal(families, parts.Count);
        var eq = (RelationExpr)s.ParseExpression(input[(input.IndexOf('(') + 1)..input.LastIndexOf(',')]);
        var f = Cas.Exact($"({ExprPrinter.ToInput(eq.Left)}) - ({ExprPrinter.ToInput(eq.Right)})", s);
        foreach (RelationExpr part in parts)
            foreach (var n in new[] { -1, 0, 2 })
            {
                var x = Cas.Value(part.Right, "n", n, s);
                Assert.True(Math.Abs(Cas.Value(f, "x", x, s)) < 1e-9, $"{ExprPrinter.ToText(part)} with n = {n} does not solve {input}");
            }
    }

    [Fact]
    public void DegreeModeGivesDegrees()
    {
        var s = Cas.Degrees();
        Assert.Equal("x = 360n + 30 or x = 360n + 150", Cas.Text("solve(sin(x) = 1/2, x)", s));
        Assert.Equal("x = 180n + 45", Cas.Text("solve(tan(x) = 1, x)", s));
    }

    [Fact]
    public void ParameterNameAvoidsCollisions()
    {
        var text = Cas.Text("solve(sin(x) = n, x)");
        Assert.Contains("n1", text);
    }

    [Fact]
    public void SymbolicQuadratic()
    {
        var s = Cas.Session();
        var r = Cas.Exact("solve(a x^2 + b x + c = 0, x)", s);
        var text = ExprPrinter.ToText(r);
        Assert.Equal("x = (-b - √(-4ac + b²))/(2a) or x = (-b + √(-4ac + b²))/(2a)", Cas.Normalize(text));
    }

    [Fact]
    public void ResultShapes()
    {
        var s = Cas.Session();
        Assert.IsType<RelationExpr>(Cas.Exact("solve(2x = 4, x)", s));
        Assert.Equal(LogicOp.Or, Assert.IsType<LogicExpr>(Cas.Exact("solve(x^2 = 4, x)", s)).Op);
        Assert.Same(BoolExpr.False, Cas.Exact("solve(x^2 = -4, x)", Cas.Session(new CalcSettings { Complex = ComplexMode.RealOnly })));
        Assert.Same(BoolExpr.True, Cas.Exact("solve(x + 1 = 1 + x, x)", s));
    }

    [Theory]
    [InlineData("nsolve(cos(x) = x, x)", "x = 0.7390851332")]
    [InlineData("nsolve(x^2 = 2, x, 1)", "x = 1.414213562")]
    [InlineData("nsolve(x^3 - x = 0, x, -2, 2)", "x = -1 or x = 0 or x = 1")]
    [InlineData("nsolve(e^x = 3, x)", "x = 1.098612289")]
    [InlineData("nsolve(x^2 + 1 = 0, x)", "false")]
    [InlineData("nsolve((x - 1)^2 = 0, x)", "x = 1")]
    public void Numeric(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Theory]
    [InlineData("zeros(x^2 - 4, x)", "{-2, 2}")]
    [InlineData("zeros(x^3 - x, x)", "{-1, 0, 1}")]
    [InlineData("zeros(x^2 + 1, x)", "{-i, i}")]
    [InlineData("zeros(2, 3)", "[[0, 0, 0], [0, 0, 0]]")]
    [InlineData("roots(x^2 - 5x + 6, x)", "{2, 3}")]
    public void Zeros(string input, string expected) => Assert.Equal(expected, Cas.Text(input));
}

public class InequalityTests
{
    [Theory]
    [InlineData("solve(x^2 - 5x + 6 > 0, x)", "x < 2 or x > 3")]
    [InlineData("solve(x^2 - x - 6 <= 0, x)", "-2 ≤ x and x ≤ 3")]
    [InlineData("solve(x^2 - x - 6 < 0, x)", "-2 < x and x < 3")]
    [InlineData("solve(x^2 - 4 >= 0, x)", "x ≤ -2 or x ≥ 2")]
    [InlineData("solve(2x + 1 > 5, x)", "x > 2")]
    [InlineData("solve(-3x <= 6, x)", "x ≥ -2")]
    [InlineData("solve((x - 1)/(x + 2) >= 0, x)", "x < -2 or x ≥ 1")]
    [InlineData("solve((x - 1)/(x + 2) < 0, x)", "-2 < x and x < 1")]
    [InlineData("solve(1/x > 0, x)", "x > 0")]
    [InlineData("solve((x - 1)^2 > 0, x)", "x ≠ 1")]
    [InlineData("solve((x - 1)^2 <= 0, x)", "x = 1")]
    [InlineData("solve((x - 1)^2 >= 0, x)", "true")]
    [InlineData("solve(x^2 + 1 < 0, x)", "false")]
    [InlineData("solve(x^2 != 4, x)", "x ≠ -2 and x ≠ 2")]
    [InlineData("solve(abs(x - 1) < 3, x)", "-2 < x and x < 4")]
    [InlineData("solve(abs(x) >= 2, x)", "x ≤ -2 or x ≥ 2")]
    [InlineData("solve(ln(x) < 1, x)", "0 < x and x < e")]
    [InlineData("solve(e^x > 2, x)", "x > ln(2)")]
    [InlineData("solve(x^3 - x > 0, x)", "-1 < x and x < 0 or x > 1")]
    [InlineData("solve(x^2 (x - 3) >= 0, x)", "x = 0 or x ≥ 3")]
    [InlineData("solve(sqrt(x) < 2, x)", "0 ≤ x and x < 4")]
    [InlineData("solve(x^2 - 2 > 0, x)", "x < -√2 or x > √2")]
    public void SignChart(string input, string expected) => Assert.Equal(expected, Cas.Text(input));
}

public class SystemTests
{
    [Theory]
    [InlineData("solve({x + y = 3, x - y = 1}, {x, y})", "x = 2 and y = 1")]
    [InlineData("solve(x + y = 3 and x - y = 1, {x, y})", "x = 2 and y = 1")]
    [InlineData("solve({2x + 3y = 7, 4x - y = 0}, {x, y})", "x = 1/2 and y = 2")]
    [InlineData("solve({x + 2y + z = 4, 2x - y + z = 3, x + y - z = 0}, {x, y, z})", "x = 1 and y = 2/3 and z = 5/3")]
    [InlineData("solve({x + y = 1, x + y = 2}, {x, y})", "false")]
    [InlineData("solve({x + y = 1, 2x + 2y = 2}, {x, y})", "x = -t + 1 and y = t")]
    [InlineData("solve({x + y + z = 1, x - y = 0}, {x, y, z})", "x = -t/2 + 1/2 and y = -t/2 + 1/2 and z = t")]
    [InlineData("solve({a x + y = 1, x - y = 0}, {x, y})", "x = 1/(a + 1) and y = 1/(a + 1)")]
    [InlineData("solve({x^2 + y^2 = 25, x + y = 7}, {x, y})", "x = 4 and y = 3 or x = 3 and y = 4")]
    [InlineData("solve({x y = 6, x + y = 5}, {x, y})", "x = 3 and y = 2 or x = 2 and y = 3")]
    [InlineData("solve({y = x^2, y = 2x + 3}, {x, y})", "x = -1 and y = 1 or x = 3 and y = 9")]
    [InlineData("solve({x^2 + y^2 = 5, x^2 - y^2 = 3}, {x, y})", "x = -2 and y = -1 or x = -2 and y = 1 or x = 2 and y = -1 or x = 2 and y = 1")]
    public void Exact(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void SystemShape()
    {
        var r = Cas.Exact("solve({x + y = 3, x - y = 1}, {x, y})");
        var and = Assert.IsType<LogicExpr>(r);
        Assert.Equal(LogicOp.And, and.Op);
        var many = Assert.IsType<LogicExpr>(Cas.Exact("solve({x y = 6, x + y = 5}, {x, y})"));
        Assert.Equal(LogicOp.Or, many.Op);
        Assert.All(many.Operands, o => Assert.Equal(LogicOp.And, Assert.IsType<LogicExpr>(o).Op));
    }

    [Fact]
    public void IrrationalIntersection()
    {
        var s = Cas.Session();
        var r = Cas.Exact("solve({x^2 + y^2 = 1, y = x^2}, {x, y})", s);
        var sets = ((LogicExpr)r).Operands;
        Assert.Equal(2, sets.Length);
        foreach (LogicExpr set in sets)
        {
            double x = Cas.Value(((RelationExpr)set.Operands[0]).Right, s);
            double y = Cas.Value(((RelationExpr)set.Operands[1]).Right, s);
            Cas.AssertClose(1, x * x + y * y, 1e-10);
            Cas.AssertClose(y, x * x, 1e-10);
        }
    }

    [Fact]
    public void NumericSystem()
    {
        var s = Cas.Session();
        var r = Cas.Exact("solve({sin(x) + y = 1, x - cos(y) = 0}, {x, y})", s);
        var sets = r is LogicExpr { Op: LogicOp.Or } or ? or.Operands.ToList() : [r];
        Assert.NotEmpty(sets);
        foreach (LogicExpr set in sets)
        {
            double x = Cas.Value(((RelationExpr)set.Operands[0]).Right, s);
            double y = Cas.Value(((RelationExpr)set.Operands[1]).Right, s);
            Assert.True(Math.Abs(Math.Sin(x) + y - 1) < 1e-8 && Math.Abs(x - Math.Cos(y)) < 1e-8);
        }
    }
}

public class SumTests
{
    [Theory]
    [InlineData("symsum(k, k, 1, n)", "n·(n + 1)/2")]
    [InlineData("symsum(k^2, k, 1, n)", "n·(n + 1)·(2n + 1)/6")]
    [InlineData("symsum(k^3, k, 1, n)", "n²·(n + 1)²/4")]
    [InlineData("symsum(k^3, k, 1, 10)", "3025")]
    [InlineData("symsum(2k - 1, k, 1, n)", "n²")]
    [InlineData("symsum(1, k, 1, n)", "n")]
    [InlineData("symsum(1/2^k, k, 0, inf)", "2")]
    [InlineData("symsum((1/3)^k, k, 1, inf)", "1/2")]
    [InlineData("symsum(2^k, k, 0, n)", "2·2^n - 1")]
    [InlineData("symsum(k/2^k, k, 1, inf)", "2")]
    [InlineData("symsum(1/(k(k + 1)), k, 1, n)", "n/(n + 1)")]
    [InlineData("symsum(1/(k(k + 1)), k, 1, inf)", "1")]
    [InlineData("symsum(1/(k(k + 2)), k, 1, inf)", "3/4")]
    [InlineData("symsum(1/k^2, k, 1, inf)", "π²/6")]
    [InlineData("symsum(1/k^4, k, 1, inf)", "π⁴/90")]
    [InlineData("symsum(1/k!, k, 0, inf)", "e")]
    [InlineData("symsum(x^k/k!, k, 0, inf)", "e^x")]
    [InlineData("symsum((-1)^(k + 1)/k, k, 1, inf)", "ln(2)")]
    [InlineData("symsum(1/k, k, 1, inf)", "∞")]
    [InlineData("symsum(k, k, 1, inf)", "∞")]
    [InlineData("symsum(2^k, k, 0, inf)", "∞")]
    [InlineData("sum(1/k^2, k, 1, inf)", "π²/6")]
    [InlineData("sum(k, k, 1, n)", "n·(n + 1)/2")]
    [InlineData("symproduct(k, k, 1, n)", "n!")]
    [InlineData("symproduct((k + 1)/k, k, 1, n)", "n + 1")]
    [InlineData("symproduct(2, k, 1, n)", "2^n")]
    [InlineData("symproduct(k, k, 1, 5)", "120")]
    public void ClosedForms(string input, string expected) => Assert.Equal(expected, Cas.Text(input));

    [Fact]
    public void UnknownSumStaysUnevaluated()
    {
        var r = Cas.Exact("symsum(1/k^3, k, 1, inf)");
        Assert.IsType<CallExpr>(r);
        Assert.Equal("symsum", ((CallExpr)r).Name);
    }

    [Fact]
    public void FaulhaberAgreesWithDirectSums()
    {
        var s = Cas.Session();
        for (int m = 0; m <= 8; m++)
        {
            var closed = Cas.Exact($"symsum(k^{m}, k, 1, n)", s);
            for (int n = 1; n <= 6; n++)
            {
                double direct = Enumerable.Range(1, n).Sum(k => Math.Pow(k, m));
                Cas.AssertClose(direct, Cas.Value(closed, "n", n, s), 1e-12);
            }
        }
    }
}

public class StepsTests
{
    private static List<string> Steps(string input)
    {
        var s = Cas.Session();
        var r = Cas.Exact($"steps({input})", s);
        return [.. ((ListExpr)r).Items.Select(i => ((StringExpr)i).Value)];
    }

    [Fact]
    public void LinearEquation()
    {
        var steps = Steps("solve(2x + 3 = 7, x)");
        Assert.Equal(["Solve 2x + 3 = 7 for x.", "Subtract 3 from both sides: 2x = 4", "Divide both sides by 2: x = 2", "Solution: x = 2"], steps);
    }

    [Fact]
    public void QuadraticByFactoring()
    {
        var steps = Steps("solve(x^2 - 5x + 6 = 0, x)");
        Assert.Contains(steps, s => s.StartsWith("Factor the left side"));
        Assert.Equal("Solution: x = 2 or x = 3", steps[^1]);
    }

    [Fact]
    public void QuadraticFormula()
    {
        var steps = Steps("solve(x^2 - 5x + 3 = 0, x)");
        Assert.Contains("Discriminant: Δ = b² - 4ac = 13", steps);
        Assert.Contains(steps, s => s.Contains("quadratic formula"));
        Assert.Contains(steps, s => s.Contains("(5 ± √13)/2"));
    }

    [Fact]
    public void NegativeDiscriminant() => Assert.Contains(Steps("solve(x^2 + x + 1 = 0, x)"), s => s.Contains("no real solutions"));

    [Fact]
    public void Derivative()
    {
        var steps = Steps("diff(x^2 sin(x), x)");
        Assert.Contains(steps, s => s.StartsWith("Product rule"));
        Assert.Contains(steps, s => s.StartsWith("Power rule"));
        Assert.Equal("Result: x²·cos(x) + 2x·sin(x)", steps[^1]);
    }

    [Fact]
    public void ChainRule() => Assert.Contains(Steps("diff(sin(x^2), x)"), s => s.StartsWith("Chain rule"));

    [Fact]
    public void QuotientRule() => Assert.Contains(Steps("diff(x/(x + 1), x)"), s => s.StartsWith("Quotient rule"));

    [Fact]
    public void Integrals()
    {
        Assert.Contains(Steps("integrate(x e^x, x)"), s => s.Contains("by parts"));
        Assert.Contains(Steps("integrate(2x cos(x^2), x)"), s => s.StartsWith("Substitute u = x²"));
        Assert.Contains(Steps("integrate(1/(x^2 - 1), x)"), s => s.Contains("partial fractions"));
        Assert.Equal("Result: x³/3 + C", Steps("integrate(x^2, x)")[^1]);
        Assert.Equal("Result: 1/3", Steps("integrate(x^2, x, 0, 1)")[^1]);
    }

    [Fact]
    public void Limits()
    {
        var factoring = Steps("limit((x^2 - 1)/(x - 1), x, 1)");
        Assert.Contains("Substituting gives 0/0, an indeterminate form.", factoring);
        Assert.Contains(factoring, s => s.StartsWith("Factor and cancel"));
        Assert.Equal("Result: 2", factoring[^1]);
        var hopital = Steps("limit(sin(x)/x, x, 0)");
        Assert.Contains(hopital, s => s.StartsWith("Apply L'Hôpital's rule"));
        Assert.Equal("Result: 1", hopital[^1]);
        Assert.Contains(Steps("limit(x^2, x, 3)"), s => s.Contains("substitute x = 3"));
    }

    [Fact]
    public void ApiWorksWithoutTheCalculatorFunction()
    {
        var s = Cas.Session();
        var steps = CasSteps.Explain("solve(3x = 12, x)", s);
        Assert.Equal("Solution: x = 4", steps[^1]);
        Assert.NotEmpty(CasSteps.DiffSteps(s.ParseExpression("x^3"), "x", s.Context()));
        Assert.NotEmpty(CasSteps.IntegrateSteps(s.ParseExpression("cos(x)"), "x", s.Context()));
        Assert.NotEmpty(CasSteps.LimitSteps(s.ParseExpression("1/x"), "x", ConstantExpr.Infinity, s.Context()));
    }
}
