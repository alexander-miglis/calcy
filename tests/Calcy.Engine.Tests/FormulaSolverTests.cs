using System.Globalization;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Engine;
using Calcy.Formulas;

namespace Calcy.Engine.Tests;

public class FormulaSolverTests
{
    public static IEnumerable<object[]> Examples() =>
        FormulaLibrary.Default.All.Where(f => f.Example?.SolveFor is not null && !f.IsIdentity).Select(f => new object[] { f.Id });

    [Theory]
    [MemberData(nameof(Examples))]
    public void WorkedExampleSolves(string id)
    {
        var formula = FormulaLibrary.Default.Get(id)!;
        var ex = formula.Example!;
        var session = CalcyEngine.CreateSession();
        var known = ex.Given.ToDictionary(kv => kv.Key, kv => (Expr)new NumberExpr(Number.Approx(kv.Value)));
        var result = FormulaSolver.Solve(session, formula, known, ex.SolveFor!);
        Assert.Contains(result.Solutions, s => Math.Abs(s.Approx - ex.Result) <= Math.Max(ex.Tolerance, 1e-9 * Math.Abs(ex.Result)));
    }

    [Fact]
    public void SolvesForAnyVariable()
    {
        var f = FormulaLibrary.Default.Get("suvat-v-u-a-t")!;
        var s = CalcyEngine.CreateSession();
        var r = FormulaSolver.Solve(s, f, new Dictionary<string, Expr> { ["v"] = Expr.Int(25), ["u"] = Expr.Int(5), ["a"] = Expr.Rational(5, 2) }, "t");
        Assert.Equal(8, r.Solutions.Single().Approx, 12);
        Assert.Equal("s", r.Unit);
    }

    [Fact]
    public void MissingValueIsExplained()
    {
        var f = FormulaLibrary.Default.Get("suvat-v-u-a-t")!;
        var ex = Assert.Throws<CalcException>(() => FormulaSolver.Solve(CalcyEngine.CreateSession(), f, new Dictionary<string, Expr> { ["v"] = Expr.Int(25) }, "a"));
        Assert.Contains("Give a value for", ex.Message);
    }

    [Fact]
    public void LimitsDropInvalidSolutions()
    {
        var f = FormulaLibrary.Default.Get("suvat-v-u-a-t")!;
        // t must be at least 0, so a negative time is rejected.
        var ex = Assert.Throws<CalcException>(() => FormulaSolver.Solve(CalcyEngine.CreateSession(), f,
            new Dictionary<string, Expr> { ["v"] = Expr.Int(5), ["u"] = Expr.Int(25), ["a"] = Expr.Int(4) }, "t"));
        Assert.Contains("No valid value", ex.Message);
    }

    [Fact]
    public void GeneralSolutionsDoNotClashWithAFormulaVariableN()
    {
        // d sin(θ) = n λ with exact inputs: the algebra module answers θ = 2πn + asin(...) with its own counter n,
        // which must not be confused with the order n of the formula.
        var f = FormulaLibrary.Default.Get("diffraction-grating")!;
        var s = CalcyEngine.CreateSession();
        var known = new Dictionary<string, Expr>
        {
            ["d"] = Expr.Rational(1, 500000),
            ["n"] = Expr.Int(1),
            ["λ"] = Expr.Rational(1, 1000000),
        };
        var r = FormulaSolver.Solve(s, f, known, "θ");
        Assert.Contains(r.Solutions, x => Math.Abs(x.Approx - Math.PI / 6) < 1e-12);
    }
}