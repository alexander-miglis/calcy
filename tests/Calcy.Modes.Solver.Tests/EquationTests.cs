using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Modes.Solver.Logic;
using static Calcy.Modes.Solver.Tests.TestSupport;

namespace Calcy.Modes.Solver.Tests;

public class EquationTests
{
    private static EquationOutcome Solve(string eq, string range = "", bool complex = false, bool steps = false, string? unknown = null, SolverEngine? engine = null) =>
        (engine ?? E()).SolveEquation(new EquationRequest { Equation = eq, Range = range, Complex = complex, ShowSteps = steps, Unknown = unknown }, default);

    [Fact]
    public void Quadratic_with_radicals_exact_first_with_decimals()
    {
        var o = Solve("x^2 - 5x + 3 = 0", steps: true);
        Assert.False(o.IsError, o.Error?.Message);
        Assert.Equal(EquationStatus.Solutions, o.Status);
        Assert.Equal("2 SOLUTIONS", o.Summary);
        Assert.Equal(["(5 - √13)/2", "(5 + √13)/2"], o.Solutions.Select(v => A(v.Text)));
        Assert.Equal(["0.6972243623", "4.302775638"], o.Solutions.Select(v => v.ApproxText));
        Assert.False(o.Numeric);
        Assert.Contains(o.Steps, s => s.Contains("Discriminant"));
        Assert.Equal(["x = (5 ± √13)/2"], o.Card.Lines.Select(A));
        Assert.Equal("≈ 0.6972243623,  4.302775638", o.Card.Approx);
        Assert.Equal(["y = x^2 - 5*x + 3"], o.PlotTexts);
    }

    [Fact]
    public void Numeric_search_in_a_range_lists_every_root_with_checks()
    {
        var o = Solve("cos(x) = x/3", "[-10, 10]");
        Assert.Equal(EquationStatus.Solutions, o.Status);
        Assert.True(o.Numeric);
        Assert.Equal("3 SOLUTIONS IN [-10, 10]", A(o.Summary));
        Assert.Equal(3, o.Solutions.Count);
        Close(-2.938100393970811, o.Solutions[0].Re, 1e-8);
        Close(1.1701209500026262, o.Solutions[2].Re, 1e-8);
        Assert.All(o.Solutions, v =>
        {
            Assert.True(v.IsNumeric);
            Assert.NotNull(v.Check);
            Assert.True(v.Check == "0" || v.Check!.Contains('e'), v.Check);
        });
        Assert.Contains(o.Notes, n => n.Contains("scanning and refining"));
        Assert.Equal(["y = cos(x)", "y = x/3"], o.PlotTexts);
        Assert.StartsWith("x ≈ ", o.Card.Lines[0]);
        Assert.Contains("[-10, 10]", A(o.Card.Input));
    }

    [Fact]
    public void Range_without_roots_says_so()
    {
        var o = Solve("x^2 = 9", "[4, 10]");
        Assert.Equal(EquationStatus.NoSolution, o.Status);
        Assert.Equal("NO SOLUTION IN [4, 10]", o.Summary);
    }

    [Fact]
    public void No_real_solution_explains_with_the_discriminant_and_offers_complex()
    {
        var o = Solve("x^2 + x + 1 = 0");
        Assert.Equal(EquationStatus.NoRealSolution, o.Status);
        Assert.Empty(o.Solutions);
        Assert.Equal(2, o.ComplexSolutions.Count);
        Assert.Equal("-3", A(E().Show(o.Discriminant!)));
        Assert.Contains(o.Notes, n => n.Contains("discriminant") && n.Contains("negative"));
        Assert.Contains(o.Notes, n => n.Contains("COMPLEX"));

        var complex = Solve("x^2 + x + 1 = 0", complex: true);
        Assert.Equal(EquationStatus.Solutions, complex.Status);
        Assert.Equal(["-1/2 - (√3/2)i", "-1/2 + (√3/2)i"], complex.Solutions.Select(v => A(v.Text)));
    }

    [Fact]
    public void No_real_solution_without_a_polynomial()
    {
        var o = Solve("e^x = -1");
        Assert.True(o.Status is EquationStatus.NoSolution or EquationStatus.NoRealSolution);
        Assert.Empty(o.Solutions);
        Assert.NotEmpty(o.Notes);
    }

    [Fact]
    public void Complex_option_lists_every_root()
    {
        var o = Solve("x^3 = 1", complex: true);
        Assert.Equal(3, o.Solutions.Count);
        Assert.Single(o.Solutions, v => v.IsReal);
        var real = Solve("x^3 = 1");
        Assert.Equal("1", Assert.Single(real.Solutions).Text);
        Assert.Equal("1 SOLUTION", real.Summary);
    }

    [Fact]
    public void Degree_mode_trig_equation_gives_degrees_and_the_integer_parameter()
    {
        var o = Solve("sin(x) = 1/2", engine: Degrees());
        Assert.Equal(EquationStatus.Solutions, o.Status);
        Assert.Equal("INFINITELY MANY SOLUTIONS", o.Summary);
        Assert.Equal(["n"], o.Parameters);
        Assert.Equal(["360n + 30", "360n + 150"], o.Solutions.Select(v => v.Text));
        Assert.Contains(o.Notes, n => n.StartsWith("n is any integer"));
        Assert.Contains(@"\mathbb{Z}", o.Card.LatexText);
        Assert.Equal("n is any integer.", o.Card.Note);
    }

    [Fact]
    public void Radian_trig_equation_shows_the_member_for_n_zero()
    {
        var o = Solve("sin(x) = 1/2");
        Assert.Equal(2, o.Solutions.Count);
        Assert.Equal("2πn + π/6", o.Solutions[0].Text);
        Assert.Equal("0.5235987756 when n = 0", o.Solutions[0].ApproxText);
    }

    [Fact]
    public void Unknown_is_picked_automatically_or_chosen()
    {
        var auto = Solve("a x + b = 0");
        Assert.Equal("x", auto.Unknown);
        Assert.Equal(["x", "a", "b"], auto.Candidates);
        Assert.Equal("-b/a", A(Assert.Single(auto.Solutions).Text));
        var forB = Solve("a x + b = 0", unknown: "b");
        Assert.Equal("b", forB.Unknown);
        Assert.Equal("-ax", A(Assert.Single(forB.Solutions).Text));
        var r = Solve("r^2 = 9");
        Assert.Equal("r", r.Unknown);
        Assert.Equal(["y = x^2", "y = 9"], r.PlotTexts);
    }

    [Fact]
    public void Stored_variables_are_known_numbers()
    {
        var e = E();
        e.Session.Evaluate("a = 2");
        var o = Solve("a x = 6", engine: e);
        Assert.Equal("x", o.Unknown);
        Assert.Equal("3", Assert.Single(o.Solutions).Text);
        Assert.Contains("Uses the stored value a = 2.", o.Notes);
        // A stored value never replaces the unknown itself.
        e.Session.Evaluate("x = 10");
        var again = Solve("a x = 6", unknown: "x", engine: e);
        Assert.Equal("3", Assert.Single(again.Solutions).Text);
    }

    [Fact]
    public void Identity_and_contradiction()
    {
        Assert.Equal(EquationStatus.AllValues, Solve("x + 1 = 1 + x").Status);
        var none = Solve("x = x + 1");
        Assert.Equal(EquationStatus.NoSolution, none.Status);
        Assert.Equal("NO SOLUTION", none.Summary);
    }

    [Fact]
    public void Expression_alone_means_equal_to_zero()
    {
        var o = Solve("x^2 - 4");
        Assert.Equal(["-2", "2"], o.Solutions.Select(v => A(v.Text)));
    }

    [Fact]
    public void Errors_are_explained()
    {
        var parse = Solve("x^2 = ");
        Assert.True(parse.IsError);
        Assert.Equal("equation", parse.Error!.Field);
        Assert.True(parse.Card.IsError);
        var ineq = Solve("x < 2");
        Assert.Contains("INEQUALITY", ineq.Error!.Message);
        var range = Solve("x = 2", "abc");
        Assert.Equal("range", range.Error!.Field);
        var empty = Solve("");
        Assert.True(empty.IsError);
    }

    [Fact]
    public void Steps_for_a_linear_equation()
    {
        var o = Solve("2x + 3 = 7", steps: true);
        Assert.Equal(["Solve 2x + 3 = 7 for x.", "Subtract 3 from both sides: 2x = 4", "Divide both sides by 2: x = 2", "Solution: x = 2"], o.Steps);
    }

    [Fact]
    public void Card_texts_for_use_copy_and_latex()
    {
        var o = Solve("x^2 = 2");
        Assert.Equal("{-sqrt(2), sqrt(2)}", o.Card.UseText);
        Assert.Equal(["x = -√2", "x = √2"], o.Card.Lines.Select(A));
        Assert.Contains(@"\sqrt{2}", o.Card.LatexText);
        Assert.Contains("or", o.Card.LatexText);
        var one = Solve("2x = 4");
        Assert.Equal("2", one.Card.UseText);
        Assert.Equal(["x = 2"], one.Card.Lines);
    }

    [Fact]
    public void Shared_settings_are_not_changed_by_a_complex_solve()
    {
        var e = E();
        Solve("x^2 = -1", complex: true, engine: e);
        Assert.Equal(ComplexMode.Auto, e.Settings.Complex);
    }

    [Fact]
    public void Cancellation_stops_a_solve()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => E().SolveEquation(new EquationRequest { Equation = "cos(x) = x/3", Range = "[-1000, 1000]" }, cts.Token));
    }

    [Fact]
    public void Solution_reader_shapes()
    {
        var s = E().Session;
        Assert.Equal(SolutionKind.All, SolutionReader.ReadSingle(BoolExpr.True, "x").Kind);
        Assert.Equal(SolutionKind.None, SolutionReader.ReadSingle(BoolExpr.False, "x").Kind);
        Assert.Equal(2, SolutionReader.ReadSingle(s.EvaluateExpression("solve(x^2 = 4, x)"), "x").Values.Count);
        Assert.Equal(SolutionKind.Unsolved, SolutionReader.ReadSingle(s.ParseExpression("solve(sin(x) > 0, x)"), "x").Kind);
        var sys = SolutionReader.ReadSystem(s.EvaluateExpression("solve({x y = 6, x + y = 5}, {x, y})"), ["x", "y"]);
        Assert.Equal(2, sys.Sets.Count);
        Assert.All(sys.Sets, set => Assert.Equal(2, set.Count));
    }
}
