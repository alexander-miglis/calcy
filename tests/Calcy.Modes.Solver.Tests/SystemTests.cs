using Calcy.Modes.Solver.Logic;
using static Calcy.Modes.Solver.Tests.TestSupport;

namespace Calcy.Modes.Solver.Tests;

public class LinearSystemTests
{
    private static LinearSystemRequest Grid(params string[][] rows)
    {
        var r = new LinearSystemRequest { Equations = rows.Length, Unknowns = rows[0].Length - 1 };
        r.Grid = [.. Enumerable.Range(0, LinearSystemRequest.Max).Select(_ => Enumerable.Repeat("", LinearSystemRequest.Max + 1).ToList())];
        for (int i = 0; i < rows.Length; i++)
        {
            for (int j = 0; j < rows[i].Length - 1; j++) r.Grid[i][j] = rows[i][j];
            r.Grid[i][LinearSystemRequest.Max] = rows[i][^1];
        }
        return r;
    }

    private static LinearSystemRequest Typed(params string[] lines)
    {
        var r = new LinearSystemRequest { Typed = true, Equations = lines.Length };
        for (int i = 0; i < LinearSystemRequest.Max; i++) r.Lines[i] = i < lines.Length ? lines[i] : "";
        return r;
    }

    [Fact]
    public void Unique_solution_of_the_design_grid()
    {
        var o = E().SolveLinearSystem(new LinearSystemRequest(), default);
        Assert.False(o.IsError, o.Error?.Message);
        Assert.Equal(LinearKind.Unique, o.Kind);
        Assert.Equal(["x", "y", "z"], o.Unknowns);
        Assert.Equal(["2", "3", "-1"], o.Values.Select(v => A(v.Value.Text)));
        Assert.Equal(["2x + y - z = 8", "-3x - y + 2z = -11", "-2x + y + 2z = -3"], o.EquationTexts.Select(A));
        Assert.Equal("-1", A(E().Show(o.Determinant!)));
        Assert.Equal(3, o.Rank);
        Assert.Equal("UNIQUE SOLUTION", o.Summary);
        Assert.Contains(o.Notes, n => n.StartsWith("det A"));
        // The design's card: x = 2, y = 3, z = −1.
        Assert.Equal("SYSTEM 3 × 3", o.Card.Title);
        Assert.Equal(["x = 2,  y = 3,  z = -1"], o.Card.Lines.Select(A));
        Assert.Equal("{2, 3, -1}", o.Card.UseText);
        Assert.Empty(o.PlotTexts);
    }

    [Fact]
    public void Exact_fractions_in_a_unique_solution()
    {
        var o = E().SolveLinearSystem(Grid(["1", "2", "1", "4"], ["2", "-1", "1", "3"], ["1", "1", "-1", "0"]), default);
        Assert.Equal(["1", "2/3", "5/3"], o.Values.Select(v => v.Value.Text));
        Assert.NotNull(o.Card.Approx);
    }

    [Fact]
    public void No_solution_is_explained_with_ranks()
    {
        var o = E().SolveLinearSystem(Grid(["1", "1", "1"], ["1", "1", "2"]), default);
        Assert.Equal(LinearKind.None, o.Kind);
        Assert.Equal("NO SOLUTION", o.Summary);
        Assert.Equal(1, o.Rank);
        Assert.Equal(2, o.AugmentedRank);
        Assert.Contains(o.Notes, n => n.Contains("contradict"));
        Assert.Equal(["no solution"], o.Card.Lines);
        Assert.Null(o.Card.UseText);
        Assert.Equal(["x + y = 1", "x + y = 2"], o.PlotTexts.Select(p => p.Replace("*", "")));
    }

    [Fact]
    public void Infinitely_many_solutions_in_parametric_form()
    {
        var o = E().SolveLinearSystem(Grid(["1", "1", "1"], ["2", "2", "2"]), default);
        Assert.Equal(LinearKind.Infinite, o.Kind);
        Assert.Equal("INFINITELY MANY SOLUTIONS", o.Summary);
        Assert.Equal(["t"], o.Parameters);
        Assert.Equal(["-t + 1", "t"], o.Values.Select(v => A(v.Value.Text)));
        Assert.Contains(o.Notes, n => n.Contains("1 free parameter"));
        Assert.Equal("t can be any real number.", o.Card.Note);
        Assert.Equal("{-t + 1, t}", o.Card.UseText);
    }

    [Fact]
    public void Three_unknowns_with_two_free_parameters()
    {
        var o = E().SolveLinearSystem(Grid(["1", "1", "1", "1"], ["2", "2", "2", "2"], ["3", "3", "3", "3"]), default);
        Assert.Equal(LinearKind.Infinite, o.Kind);
        Assert.Equal(2, o.Parameters.Count);
        Assert.Contains(o.Notes, n => n.Contains("2 free parameters"));
    }

    [Fact]
    public void Typed_equations_are_checked_for_linearity()
    {
        var o = E().SolveLinearSystem(Typed("x + y = 3", "x - y = 1"), default);
        Assert.Equal(LinearKind.Unique, o.Kind);
        Assert.Equal(["2", "1"], o.Values.Select(v => v.Value.Text));
        Assert.Equal(["x + y = 3", "x - y = 1"], o.PlotTexts.Select(p => p.Replace("*", "")));
        var bad = E().SolveLinearSystem(Typed("x y = 3", "x - y = 1"), default);
        Assert.True(bad.IsError);
        Assert.Contains("NONLINEAR", bad.Error!.Message);
        Assert.Equal("equation 1", bad.Error.Field);
    }

    [Fact]
    public void Six_unknowns()
    {
        var rows = new List<string[]>();
        for (int i = 0; i < 6; i++)
        {
            var row = new string[7];
            for (int j = 0; j < 6; j++) row[j] = i == j ? "6" : "1";
            row[6] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            rows.Add(row);
        }
        var o = E().SolveLinearSystem(Grid([.. rows]), default);
        Assert.Equal(LinearKind.Unique, o.Kind);
        Assert.Equal(["x", "y", "z", "w", "u", "v"], o.Unknowns);
    }

    [Fact]
    public void Grid_and_typed_conversions_round_trip()
    {
        var grid = new LinearSystemRequest();
        var lines = SolverEngine.GridToLines(grid);
        Assert.Equal("2x + y - z = 8", lines[0]);
        Assert.Equal("-3x - y + 2z = -11", lines[1]);
        var typed = grid.Clone();
        typed.Typed = true;
        for (int i = 0; i < 6; i++) typed.Lines[i] = lines[i];
        var back = E().LinesToGrid(typed);
        Assert.NotNull(back);
        Assert.Equal("-3", back!.Cell(1, 0));
        Assert.Equal("-11", back.Rhs(1));
        Assert.Null(E().LinesToGrid(Typed("x^2 + y = 1", "x - y = 0")));
    }

    [Fact]
    public void Bad_cell_names_its_position()
    {
        var o = E().SolveLinearSystem(Grid(["1", "k", "3"], ["1", "-1", "1"]), default);
        Assert.True(o.IsError);
        Assert.Equal("coefficient of y in equation 1", o.Error!.Field);
    }
}

public class NonlinearSystemTests
{
    private static NonlinearSystemRequest Sys(params string[] lines)
    {
        var r = new NonlinearSystemRequest { Count = lines.Length };
        for (int i = 0; i < NonlinearSystemRequest.Max; i++) r.Lines[i] = i < lines.Length ? lines[i] : "";
        return r;
    }

    [Fact]
    public void Circle_and_line_meet_twice()
    {
        var o = E().SolveNonlinearSystem(Sys("x^2 + y^2 = 25", "y = x + 1"), default);
        Assert.False(o.IsError, o.Error?.Message);
        Assert.Equal(["x", "y"], o.Unknowns);
        Assert.Equal(2, o.Sets.Count);
        Assert.Equal("2 SOLUTIONS", o.Summary);
        Assert.False(o.Numeric);
        var pairs = o.Sets.Select(s => (s.Values[0].Value.Re, s.Values[1].Value.Re)).OrderBy(p => p.Item1).ToList();
        Assert.Equal((-4.0, -3.0), pairs[0]);
        Assert.Equal((3.0, 4.0), pairs[1]);
        Assert.Equal(["x^2 + y^2 = 25", "y = x + 1"], o.PlotTexts);
        Assert.Equal("{{-4, -3}, {3, 4}}", o.Card.UseText);
        Assert.Equal(2, o.Card.Lines.Count);
    }

    [Fact]
    public void Numeric_system_with_checks()
    {
        var o = E().SolveNonlinearSystem(Sys("sin(x) + y = 1", "x - cos(y) = 0"), default);
        Assert.NotEmpty(o.Sets);
        Assert.True(o.Numeric);
        Assert.All(o.Sets, s =>
        {
            Assert.NotNull(s.Check);
            double x = s.Values[0].Value.Re, y = s.Values[1].Value.Re;
            Assert.True(Math.Abs(Math.Sin(x) + y - 1) < 1e-8 && Math.Abs(x - Math.Cos(y)) < 1e-8);
        });
    }

    [Fact]
    public void Starting_guesses_refine_a_solution()
    {
        var r = Sys("x^2 + y^2 = 25", "y = x + 1");
        r.Guesses["x"] = "2";
        r.Guesses["y"] = "5";
        var o = E().SolveNonlinearSystem(r, default);
        Assert.Equal(2, o.Sets.Count);
        Assert.Contains(o.Notes, n => n.Contains("starting guesses"));
        r.Guesses.Remove("y");
        var missing = E().SolveNonlinearSystem(r, default);
        Assert.True(missing.IsError);
        Assert.Contains("missing: y", missing.Error!.Message);
    }

    [Fact]
    public void No_real_intersection()
    {
        var o = E().SolveNonlinearSystem(Sys("x^2 + y^2 = 1", "x^2 + y^2 = 4"), default);
        Assert.Empty(o.Sets);
        Assert.Equal("NO REAL SOLUTION", o.Summary);
    }

    [Fact]
    public void Three_unknowns()
    {
        var o = E().SolveNonlinearSystem(Sys("x^2 + y^2 + z^2 = 3", "x + y + z = 3", "x - y = 0"), default);
        var set = Assert.Single(o.Sets);
        Assert.Equal(["1", "1", "1"], set.Values.Select(v => v.Value.Text));
        Assert.Empty(o.PlotTexts);
    }

    [Fact]
    public void Unknowns_are_found_in_order()
    {
        Assert.Equal(["x", "y"], E().UnknownsOf(["y = x^2", "x + y = 3"]));
        Assert.Equal(["p", "q"], E().UnknownsOf(["p q = 2", "p + q = 3"]));
        Assert.Empty(E().UnknownsOf(["", "x +"]));
    }

    [Fact]
    public void Other_names_are_renamed_for_plotting()
    {
        var o = E().SolveNonlinearSystem(Sys("p q = 6", "p + q = 5"), default);
        Assert.Equal(2, o.Sets.Count);
        Assert.Equal(["x*y = 6", "x + y = 5"], o.PlotTexts);
    }
}
