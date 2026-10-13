using System.Globalization;
using Calcy.Formulas.Tests.Evaluation;

namespace Calcy.Formulas.Tests;

/// <summary>
/// Proves the content is right: every worked example is solved numerically from its given values
/// and must reproduce the stated answer within its tolerance. Identities are checked at random points.
/// </summary>
public class WorkedExampleTests
{
    private static FormulaLibrary Lib => TestData.Library;

    [Theory]
    [MemberData(nameof(TestData.FormulaIds), MemberType = typeof(TestData))]
    public void Worked_example_solves_to_the_stated_result(string id)
    {
        var f = Lib.Get(id)!;
        var ex = f.Example!;
        var (left, right) = Expression.ParseEquation(f.Equation);

        if (f.IsIdentity)
        {
            var env = TestData.ConstantValues(f);
            foreach (var (k, v) in ex.Given) env[k] = v;
            double l = left.Evaluate(env), r = right.Evaluate(env);
            Assert.True(Math.Abs(l - ex.Result) <= ex.Tolerance, $"Left side is {Fmt(l)}, expected {Fmt(ex.Result)} ± {Fmt(ex.Tolerance)}.");
            Assert.True(Math.Abs(r - ex.Result) <= ex.Tolerance, $"Right side is {Fmt(r)}, expected {Fmt(ex.Result)} ± {Fmt(ex.Tolerance)}.");
            return;
        }

        var roots = TestData.Solve(f, ex.Given, ex.SolveFor!);
        bool found = roots.Any(x => Math.Abs(x - ex.Result) <= ex.Tolerance);
        Assert.True(found,
            $"Solving '{f.Equation}' for {ex.SolveFor} gave [{string.Join("; ", roots.Take(12).Select(Fmt))}], " +
            $"expected {Fmt(ex.Result)} ± {Fmt(ex.Tolerance)}.");
    }

    [Theory]
    [MemberData(nameof(TestData.FormulaIds), MemberType = typeof(TestData))]
    public void Equation_holds_at_the_worked_example(string id)
    {
        // Substituting the answer back must balance the equation (a direct check, independent of the root finder).
        var f = Lib.Get(id)!;
        var ex = f.Example!;
        var (left, right) = Expression.ParseEquation(f.Equation);
        var env = TestData.ConstantValues(f);
        foreach (var (k, v) in ex.Given) env[k] = v;
        if (ex.SolveFor is null) return;

        // Look for a sign change of left - right across [result - tol, result + tol], or an exact hit.
        double g(double x) { env[ex.SolveFor] = x; return left.Evaluate(env) - right.Evaluate(env); }
        double lo = g(ex.Result - ex.Tolerance), hi = g(ex.Result + ex.Tolerance), mid = g(ex.Result);
        bool balanced = mid == 0 || Math.Sign(lo) != Math.Sign(hi) || Math.Sign(lo) != Math.Sign(mid) || Math.Sign(mid) != Math.Sign(hi);
        Assert.True(balanced, $"Left minus right does not change sign within the tolerance: {Fmt(lo)}, {Fmt(mid)}, {Fmt(hi)}.");
    }

    [Theory]
    [MemberData(nameof(TestData.IdentityIds), MemberType = typeof(TestData))]
    public void Identity_holds_at_random_points(string id)
    {
        var f = Lib.Get(id)!;
        var (left, right) = Expression.ParseEquation(f.Equation);
        var rng = new Random(id.Aggregate(17, (h, ch) => unchecked(h * 31 + ch)) & 0x7fffffff);
        int checkedPoints = 0;
        for (int trial = 0; trial < 400 && checkedPoints < 40; trial++)
        {
            var env = TestData.ConstantValues(f);
            foreach (var v in f.Variables) env[v.Symbol] = Sample(v, rng);
            double l = left.Evaluate(env), r = right.Evaluate(env);
            if (!double.IsFinite(l) || !double.IsFinite(r) || Math.Abs(l) > 1e6 || Math.Abs(r) > 1e6) continue;
            double scale = Math.Max(1, Math.Max(Math.Abs(l), Math.Abs(r)));
            Assert.True(Math.Abs(l - r) <= 1e-9 * scale,
                $"Identity fails at {string.Join(", ", f.Variables.Select(v => $"{v.Symbol}={Fmt(env[v.Symbol])}"))}: {Fmt(l)} vs {Fmt(r)}.");
            checkedPoints++;
        }
        Assert.True(checkedPoints >= 20, $"Only {checkedPoints} valid sample points.");
    }

    private static double Sample(FormulaVariable v, Random rng)
    {
        double lo = v.Min ?? (v.Positive ? 0.2 : -3);
        double hi = v.Max ?? lo + (v.Positive || v.Min is not null ? 4 : 6);
        if (v.Positive) lo = Math.Max(lo, 1e-3);
        double x = lo + (hi - lo) * rng.NextDouble();
        if (v.Integer)
        {
            x = Math.Round(x);
            if (v.Positive && x < 1) x = 1;
        }
        return x;
    }

    private static string Fmt(double x) => x.ToString("G10", CultureInfo.InvariantCulture);
}
