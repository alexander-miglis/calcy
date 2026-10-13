using Calcy.Core.Expressions;
using Calcy.Modes.Solver.Logic;
using static Calcy.Modes.Solver.Tests.TestSupport;

namespace Calcy.Modes.Solver.Tests;

public class InequalityTests
{
    private static InequalityOutcome Solve(string text, string? unknown = null, string range = "") =>
        E().SolveInequality(new InequalityRequest { Inequality = text, Unknown = unknown, Range = range }, default);

    [Fact]
    public void Square_less_than_four_is_an_open_interval()
    {
        var o = Solve("x^2 < 4");
        Assert.False(o.IsError, o.Error?.Message);
        var iv = Assert.Single(o.Solution.Intervals);
        Assert.Equal(-2, iv.Lower!.Approx);
        Assert.Equal(2, iv.Upper!.Approx);
        Assert.False(iv.Lower.Closed);
        Assert.False(iv.Upper.Closed);
        Assert.Equal(["-2 < x < 2"], o.Parts.Select(A));
        Assert.Equal("(-2, 2)", A(o.IntervalNotation));
        Assert.Contains(o.Notes, n => n.StartsWith("Open squares"));
        Assert.Equal("ONE INTERVAL", o.Summary);
        Assert.Equal(["-2 < x < 2"], o.Card.Lines.Select(A));
        Assert.Equal("x ∈ (-2, 2)", A(o.Card.Note!));
        Assert.Equal("-2 < x and x < 2", o.Card.UseText);
        Assert.Equal("-2 < x < 2", o.Card.LatexText);
        Assert.Equal(["y = x^2", "y = 4"], o.PlotTexts);
    }

    [Fact]
    public void Rational_inequality_has_an_open_and_a_closed_end()
    {
        var o = Solve("(x - 1)/(x + 2) >= 0");
        Assert.Equal(2, o.Solution.Intervals.Count);
        var left = o.Solution.Intervals[0];
        var right = o.Solution.Intervals[1];
        Assert.Null(left.Lower);
        Assert.Equal(-2, left.Upper!.Approx);
        Assert.False(left.Upper.Closed);
        Assert.Equal(1, right.Lower!.Approx);
        Assert.True(right.Lower.Closed);
        Assert.Null(right.Upper);
        Assert.Equal("(-∞, -2) ∪ [1, ∞)", A(o.IntervalNotation));
        Assert.Equal("x < -2 or x ≥ 1", A(o.Solution.InequalityText("x", E().Show)));
        Assert.Contains(o.Notes, n => n.StartsWith("Filled squares") && n.Contains("Open squares"));
        Assert.Equal("2 INTERVALS", o.Summary);
        Assert.Equal("2 PIECES", Solve("x^2 (x - 3) >= 0").Summary);
        Assert.Equal("x < -2 or x >= 1", o.Card.UseText);
        Assert.Equal(@"x < -2 \quad\text{or}\quad x \geq 1", o.Card.LatexText);
    }

    [Theory]
    [InlineData("x^2 - x - 6 < 0", "-2 < x < 3", "(-2, 3)")]
    [InlineData("x^2 - x - 6 <= 0", "-2 ≤ x ≤ 3", "[-2, 3]")]
    [InlineData("2x + 1 > 5", "x > 2", "(2, ∞)")]
    [InlineData("x^3 - x > 0", "-1 < x < 0 or x > 1", "(-1, 0) ∪ (1, ∞)")]
    [InlineData("x^2 (x - 3) >= 0", "x = 0 or x ≥ 3", "{0} ∪ [3, ∞)")]
    [InlineData("(x - 1)^2 > 0", "x ≠ 1", "(-∞, 1) ∪ (1, ∞)")]
    [InlineData("x^2 != 4", "x ≠ -2 and x ≠ 2", "(-∞, -2) ∪ (-2, 2) ∪ (2, ∞)")]
    [InlineData("x^2 - 2 > 0", "x < -√2 or x > √2", "(-∞, -√2) ∪ (√2, ∞)")]
    [InlineData("ln(x) < 1", "0 < x < e", "(0, e)")]
    public void Sign_charts(string input, string text, string notation)
    {
        var o = Solve(input);
        Assert.False(o.IsError, o.Error?.Message);
        Assert.Equal(text, A(o.Solution.InequalityText("x", E().Show)));
        Assert.Equal(notation, A(o.IntervalNotation));
    }

    [Fact]
    public void Empty_and_whole_line()
    {
        var none = Solve("x^2 + 1 < 0");
        Assert.True(none.Solution.IsEmpty);
        Assert.Equal("NO SOLUTION", none.Summary);
        Assert.Null(none.Card.UseText);
        var all = Solve("x^2 >= 0");
        Assert.True(all.Solution.IsAll);
        Assert.Equal("EVERY X", all.Summary);
    }

    [Fact]
    public void Periodic_inequality_falls_back_to_a_numeric_sign_chart()
    {
        var o = Solve("sin(x) > 0", range: "[0, 10]");
        Assert.False(o.IsError, o.Error?.Message);
        Assert.True(o.Numeric);
        Assert.Equal((0.0, 10.0), o.Range);
        Assert.Equal(2, o.Solution.Intervals.Count);
        Close(Math.PI, o.Solution.Intervals[0].Upper!.Approx, 1e-8);
        Close(2 * Math.PI, o.Solution.Intervals[1].Lower!.Approx, 1e-8);
        Assert.True(o.Solution.Contains(1));
        Assert.False(o.Solution.Contains(4));
        Assert.Contains(o.Notes, n => n.Contains("numerically"));
    }

    [Fact]
    public void Errors()
    {
        Assert.Contains("ANY EQUATION", Solve("x = 2").Error!.Message);
        Assert.True(Solve("x^2 <").IsError);
        Assert.True(Solve("x + 2").IsError);
    }

    [Fact]
    public void Unknown_other_than_x()
    {
        var o = Solve("t^2 <= 9");
        Assert.Equal("t", o.Unknown);
        Assert.Equal(["-3 ≤ t ≤ 3"], o.Parts.Select(A));
    }
}

public class IntervalSetTests
{
    private static Bound B(double v, bool closed) => new(new NumberExpr(Calcy.Core.Numbers.Number.FromInt((long)v)), v, closed);

    [Fact]
    public void Union_merges_touching_intervals_when_an_end_is_included()
    {
        var set = IntervalSet.Union([new SolutionInterval(null, B(1, false)), new SolutionInterval(B(1, true), B(3, false))]);
        var iv = Assert.Single(set.Intervals);
        Assert.Null(iv.Lower);
        Assert.Equal(3, iv.Upper!.Approx);
        var apart = IntervalSet.Union([new SolutionInterval(null, B(1, false)), new SolutionInterval(B(1, false), null)]);
        Assert.Equal(2, apart.Intervals.Count);
        Assert.NotNull(apart.ExcludedPoints);
    }

    [Fact]
    public void Intersection_keeps_the_tighter_ends()
    {
        var a = IntervalSet.Union([new SolutionInterval(B(-2, true), null)]);
        var b = IntervalSet.Union([new SolutionInterval(null, B(3, false))]);
        var both = a.And(b);
        var iv = Assert.Single(both.Intervals);
        Assert.True(iv.Lower!.Closed);
        Assert.False(iv.Upper!.Closed);
        Assert.True(both.Contains(-2));
        Assert.False(both.Contains(3));
        Assert.True(a.And(IntervalSet.Union([new SolutionInterval(null, B(-5, true))])).IsEmpty);
    }

    [Fact]
    public void Reader_handles_every_shape()
    {
        var s = TestSupport.E().Session;
        double V(Expr e) => Calcy.Core.Evaluation.Evaluator.Numeric(e, s.Context(true))?.Re ?? double.NaN;
        Assert.True(IntervalReader.Read(BoolExpr.True, "x", V)!.IsAll);
        Assert.True(IntervalReader.Read(BoolExpr.False, "x", V)!.IsEmpty);
        var flipped = IntervalReader.Read(s.ParseExpression("3 > x"), "x", V)!;
        Assert.Equal(3, flipped.Intervals[0].Upper!.Approx);
        var point = IntervalReader.Read(s.ParseExpression("x = 4"), "x", V)!;
        Assert.True(point.Intervals[0].IsPoint);
        Assert.Null(IntervalReader.Read(s.ParseExpression("x + y < 2"), "x", V));
        Assert.Null(IntervalReader.Read(s.ParseExpression("solve(sin(x) > 0, x)"), "x", V));
    }

    [Fact]
    public void Number_line_range_covers_the_end_points()
    {
        var set = IntervalSet.Union([new SolutionInterval(B(-2, false), B(3, false))]);
        var (min, max) = PlotWindow.ForNumberLine(set);
        Assert.True(min < -2 && max > 3);
        Assert.Equal((-5.0, 5.0), PlotWindow.ForNumberLine(IntervalSet.All));
        var (lo, hi) = PlotWindow.ForNumberLine(IntervalSet.Empty, (0, 10));
        Assert.True(lo < 0 && hi > 10);
    }
}
