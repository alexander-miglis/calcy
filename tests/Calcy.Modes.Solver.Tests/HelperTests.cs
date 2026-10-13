using System.Text.Json;
using Calcy.Modes.Solver.Logic;
using static Calcy.Modes.Solver.Tests.TestSupport;

namespace Calcy.Modes.Solver.Tests;

public class HelperTests
{
    [Fact]
    public void Unknown_picker_prefers_x_and_skips_stored_names()
    {
        Assert.Equal("x", UnknownPicker.Pick(["a", "x", "b"]));
        Assert.Equal("y", UnknownPicker.Pick(["a", "y"]));
        Assert.Equal("r", UnknownPicker.Pick(["r"]));
        Assert.Equal("a", UnknownPicker.Pick(["a", "x"], preferred: "a"));
        Assert.Equal("x", UnknownPicker.Pick(["a", "x"], preferred: "q"));
        Assert.Equal("b", UnknownPicker.Pick(["a", "b"], n => n == "a"));
        Assert.Equal("a", UnknownPicker.Pick(["a"], n => n == "a"));
        Assert.Equal("x", UnknownPicker.Pick([]));
        Assert.Equal(["x", "y", "z", "a"], UnknownPicker.Order(["a", "z", "y", "x"]));
    }

    [Theory]
    [InlineData("[-10, 10]", "-10", "10")]
    [InlineData("-10, 10", "-10", "10")]
    [InlineData("(0, 2pi)", "0", "2pi")]
    [InlineData("0 to 5", "0", "5")]
    [InlineData("1..2", "1", "2")]
    [InlineData("[log(8, 2), 9]", "log(8, 2)", "9")]
    [InlineData("[−3, 3]", "-3", "3")]
    public void Range_text_splits(string text, string low, string high)
    {
        var r = RangeText.Split(text);
        Assert.NotNull(r);
        Assert.Equal(low, r!.Value.Low);
        Assert.Equal(high, r.Value.High);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("[, 3]")]
    public void Range_text_rejects(string text) => Assert.Null(RangeText.Split(text));

    [Fact]
    public void Plus_minus_merge()
    {
        Assert.Equal("(5 ± √13)/2", MathText.PlusMinus("(5 − √13)/2", "(5 + √13)/2"));
        Assert.Equal("−1/2 ± (√3/2)i", MathText.PlusMinus("−1/2 − (√3/2)i", "−1/2 + (√3/2)i"));
        Assert.Null(MathText.PlusMinus("−√2", "√2"));
        Assert.Null(MathText.PlusMinus("1", "2"));
        Assert.Null(MathText.PlusMinus("a + b", "a + b"));
        Assert.Equal("x₁₂", MathText.Subscript("x", 12));
    }

    [Fact]
    public void Route_sends_text_to_the_right_tool()
    {
        var e = E();
        Assert.Equal(SolverTool.Inequality, e.Route("x^2 < 4").Tool);
        var solveCall = e.Route("solve(x^2 = 2, x)");
        Assert.Equal(SolverTool.Equation, solveCall.Tool);
        Assert.Equal("x^2 = 2", solveCall.Lines[0]);
        Assert.Equal("x", solveCall.Unknown);
        var system = e.Route("{x + y = 3, x - y = 1}");
        Assert.Equal(SolverTool.NonlinearSystem, system.Tool);
        Assert.Equal(2, system.Lines.Count);
        Assert.Equal(SolverTool.Equation, e.Route("cos(x) = x").Tool);
        Assert.Equal(SolverTool.Equation, e.Route("((").Tool);
    }

    [Fact]
    public void State_survives_json_and_is_repaired()
    {
        var state = new SolverState { Tool = SolverTool.Inequality };
        state.Polynomial.Degree = 4;
        state.Cards.Add(new SolverCard { Number = 7, Title = "POLYNOMIAL", Lines = ["x = 1"], Request = new SolverSnapshot { Polynomial = state.Polynomial.Clone() } });
        var json = JsonSerializer.Serialize(state);
        var back = JsonSerializer.Deserialize<SolverState>(json)!.Normalize();
        Assert.Equal(SolverTool.Inequality, back.Tool);
        Assert.Equal(4, back.Polynomial.Degree);
        Assert.Equal(7, back.Counter);
        Assert.Equal(4, back.Cards[0].Request!.Polynomial!.Degree);

        var broken = JsonSerializer.Deserialize<SolverState>("""{"Polynomial":{"Degree":40,"Coefficients":["1"]},"Linear":null,"Cards":[null]}""")!.Normalize();
        Assert.Equal(6, broken.Polynomial.Degree);
        Assert.Equal(7, broken.Polynomial.Coefficients.Count);
        Assert.Equal(6, broken.Linear.Grid.Count);
        Assert.Empty(broken.Cards);
    }

    [Fact]
    public void Polynomial_plot_window_shows_roots_and_turning_points()
    {
        double[] asc = [-6, 11, -6, 1];
        var w = PlotWindow.ForPolynomial(asc, [1, 2, 3], []);
        Assert.True(w.XMin < 1 && w.XMax > 3);
        Assert.True(w.XMin <= 0, "the y-axis is near, so it is shown");
        Assert.True(w.YMin < 0 && w.YMax > 0);
        var empty = PlotWindow.ForPolynomial([1, 0, 1], [], []);
        Assert.Equal(-4, empty.XMin);
    }

    [Fact]
    public void Check_text()
    {
        Assert.Equal("0", SolverEngine.CheckText(0));
        Assert.Equal("2.2e−16", SolverEngine.CheckText(2.2e-16));
        Assert.Equal("?", SolverEngine.CheckText(double.NaN));
    }

    [Fact]
    public void Coefficient_names()
    {
        Assert.Equal("constant term", SolverEngine.CoefficientName(0));
        Assert.Equal("coefficient of x", SolverEngine.CoefficientName(1));
        Assert.Equal("coefficient of x⁵", SolverEngine.CoefficientName(5));
    }

    [Fact]
    public void Mode_metadata()
    {
        var m = new SolverMode();
        Assert.Equal("solver", m.Id);
        Assert.Equal("Solver", m.Title);
        Assert.Equal("Polynomials, systems, any equation", m.Description);
        Assert.Equal("Ctrl+7", m.Shortcut);
        Assert.Equal("Primary", m.AccentToken);
        Assert.Equal("solver", m.Icon);
    }

    [Fact]
    public void No_en_or_em_dashes_in_the_sources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Calcy.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var roots = new[] { Path.Combine(dir!.FullName, "src", "Calcy.Modes.Solver"), Path.Combine(dir.FullName, "tests", "Calcy.Modes.Solver.Tests") };
        foreach (var file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.*", SearchOption.AllDirectories))
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains((char)0x2013) || text.Contains((char)0x2014), $"{file} contains an en or em dash");
        }
    }
}
