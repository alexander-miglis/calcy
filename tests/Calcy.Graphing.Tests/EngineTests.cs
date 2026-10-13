using System.Xml.Linq;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Graphing.Numerics;
using Calcy.Graphing.Symbolic;

namespace Calcy.Graphing.Tests;

public class EngineTests
{
    [Theory]
    [InlineData(-10, 10, 760, 2)]
    [InlineData(-1, 1, 760, 0.2)]
    [InlineData(0, 1000, 500, 200)]
    [InlineData(-0.003, 0.003, 600, 0.001)]
    public void NiceTickSteps(double min, double max, double pixels, double expected) =>
        Assert.Equal(expected, Ticks.StepFor(min, max, pixels), 12);

    [Fact]
    public void TicksAndLabels()
    {
        var t = Ticks.Generate(-6, 6, 760);
        Assert.Equal(["-6", "-5", "-4", "-3", "-2", "-1", "0", "1", "2", "3", "4", "5", "6"], t.Select(x => x.Label));
        var small = Ticks.Generate(-1, 1, 400);
        Assert.Contains(small, x => x.Label == "0.5");
        Assert.DoesNotContain(small, x => x.Label.Contains("0000"));
        var big = Ticks.Generate(0, 5e7, 400);
        Assert.Contains(big, x => x.Label == "2e7");
    }

    [Fact]
    public void PiTicks()
    {
        var t = Ticks.Generate(-2 * Math.PI, 2 * Math.PI, 760, TickStyle.Pi);
        var labels = t.Select(x => x.Label).ToList();
        Assert.Contains("π/2", labels);
        Assert.Contains("-π", labels);
        Assert.Contains("3π/2", labels);
        Assert.Contains("2π", labels);
        Assert.All(t, x => Assert.Equal(0, Math.IEEERemainder(x.Value, Math.PI / 2), 9));
        var wide = Ticks.Generate(-20, 20, 400, TickStyle.Pi);
        Assert.Contains(wide, x => x.Label == "4π");
    }

    [Fact]
    public void ViewportOperations()
    {
        var v = Viewport.Standard;
        var z = v.Zoom(0.5, 2, 2);
        Assert.Equal(new Viewport(-4, 6, -4, 6), z);
        Assert.Equal(new Viewport(-9, 11, -12, 8), v.Pan(1, -2));
        var sq = new Viewport(-10, 10, -10, 10).Square(800, 400);
        Assert.Equal(sq.Width / 800, sq.Height / 400, 12);
        Assert.Equal(0.1, Viewport.Decimal(760, 570).Width / 760, 12);
        Assert.Equal(2 * Math.PI, Viewport.Trig().XMax, 12);
        var t = new ViewTransform(v, 760, 570);
        Assert.Equal(380, t.ToPixelX(0), 9);
        Assert.Equal(285, t.ToPixelY(0), 9);
        Assert.Equal(3, t.ToWorldX(t.ToPixelX(3)), 12);
    }

    [Fact]
    public void BrentRootAndMinimum()
    {
        Assert.Equal(Math.Sqrt(2), Solvers.BrentRoot(x => x * x - 2, 0, 2)!.Value, 15);
        Assert.Null(Solvers.BrentRoot(x => x * x + 1, -1, 2));
        var (x, v) = Solvers.BrentMinimize(t => (t - 1.5) * (t - 1.5) + 2, 0, 4);
        Assert.Equal(1.5, x, 7);
        Assert.Equal(2, v, 12);
        Assert.Equal(Math.Cos(1), Solvers.Derivative(Math.Sin, 1).Value, 11);
    }

    [Fact]
    public void QuadratureIsAccurate()
    {
        Assert.Equal(9, Quadrature.Integrate(x => x * x, 0, 3).Value, 13);
        Assert.Equal(2, Quadrature.Integrate(x => 1 / Math.Sqrt(x), 0, 1, 1e-9, 1e-9).Value, 6);
        Assert.Equal(Math.Log(10), Quadrature.Integrate(x => 1 / x, 1, 10).Value, 12);
        Assert.True(double.IsNaN(Quadrature.Integrate(Math.Sqrt, -1, 1).Value));
    }

    [Fact]
    public void IntervalArithmeticEnclosesValues()
    {
        var a = new Interval(-1, 2);
        var sq = Interval.PowInt(a, 2);
        Assert.Equal(0, sq.Lo);
        Assert.True(sq.Hi >= 4);
        var c = Interval.Cos(new Interval(-0.5, 0.5));
        Assert.Equal(1, c.Hi);
        Assert.True(c.Lo <= Math.Cos(0.5));
        var s = Interval.Sin(new Interval(1, 2));
        Assert.True(s.Hi >= 1 - 1e-15);
        Assert.True(Interval.Tan(new Interval(1.5, 1.7)).MaybeUndefined);
        Assert.True(Interval.Sqrt(new Interval(-4, -1)).IsEmpty);
        Assert.True(Interval.Reciprocal(new Interval(-1, 1)).MaybeUndefined);
        var r = new Random(1);
        for (int i = 0; i < 200; i++)
        {
            double lo = r.NextDouble() * 6 - 3, hi = lo + r.NextDouble() * 2;
            var box = new Interval(lo, hi);
            var e = Interval.Exp(Interval.Sin(box) * box) + Interval.PowInt(box, 3);
            for (int k = 0; k <= 10; k++)
            {
                double x = lo + (hi - lo) * k / 10;
                double v = Math.Exp(Math.Sin(x) * x) + x * x * x;
                Assert.True(e.Contains(v), $"{v} not in {e} for x = {x}");
            }
        }
    }

    [Fact]
    public void IntervalCompilerHandlesPiecewiseAndFunctions()
    {
        var s = new CalcSession();
        var e = Calcy.Core.Compilation.ExprCompiler.Prepare(s.ParseExpression("x^2 {0 < x < 5}"), ["x"], s.Context());
        var f = IntervalCompiler.Compile(e, ["x"], s.Context());
        Assert.True(f.IsReliable);
        Assert.True(f.Evaluate([new Interval(-3, -1)]).IsEmpty);
        var inside = f.Evaluate([new Interval(1, 2)]);
        Assert.True(inside.Lo <= 1 && inside.Hi >= 4);
    }

    [Fact]
    public void SymbolicDerivatives()
    {
        var s = new CalcSession();
        string D(string text) => Derivative.Of(s.ParseExpression(text), "x", s.Context().WithFree("x"))!.ToString();
        Assert.Equal("2x", D("x^2"));
        Assert.Equal("cos(x)", D("sin(x)"));
        Assert.Equal("1/x", D("ln(x)"));
        var chain = Derivative.Of(s.ParseExpression("sin(x^2)"), "x", s.Context())!;
        var f = Calcy.Core.Compilation.ExprCompiler.Compile1(chain, "x", s.Context());
        Assert.Equal(2 * 1.3 * Math.Cos(1.69), f(1.3), 12);
        var q = Derivative.Of(s.ParseExpression("x^x"), "x", s.Context())!;
        var g = Calcy.Core.Compilation.ExprCompiler.Compile1(q, "x", s.Context());
        Assert.Equal(Math.Pow(2, 2) * (Math.Log(2) + 1), g(2), 12);
    }

    [Fact]
    public void ExactFormsAreProvenBySession()
    {
        var s = new CalcSession();
        var f = s.Evaluate(s.ParseExpression("x^2 - 2"));
        var r = ExactForms.FindExactZero(f, "x", Math.Sqrt(2), s.Context(), []);
        Assert.NotNull(r);
        Assert.Equal("√2", r!.ToString());
        var sin = s.Evaluate(s.ParseExpression("sin(x)"));
        Assert.Equal("π", ExactForms.FindExactZero(sin, "x", Math.PI, s.Context(), [])!.ToString());
        // A value with no simple exact form is left alone.
        var cosx = s.Evaluate(s.ParseExpression("cos(x) - x"));
        Assert.Null(ExactForms.FindExactZero(cosx, "x", 0.7390851332151607, s.Context(), []));
        var quad = s.Evaluate(s.ParseExpression("x^2 - 2x - 1"));
        var surd = ExactForms.FindExactZero(quad, "x", 1 + Math.Sqrt(2), s.Context(), [])!;
        Assert.Contains(surd.ToString(), new[] { "√2 + 1", "1 + √2" });
        Assert.Equal(1 + Math.Sqrt(2), ((Calcy.Core.Expressions.NumberExpr)s.Evaluate(surd, approximate: true)).Value.Re, 12);
    }

    [Fact]
    public void TablesWithExactValues()
    {
        var s = new CalcSession();
        var d = new GraphDocument(s);
        d.Add("y = x^2 - 2");
        d.Add("y = sqrt(x)");
        var t = GraphTableBuilder.Functions(d.Items, d.ParameterSource, -3, 0.5, 13, exact: true, s);
        Assert.Equal(["y1", "y2"], t.Headers);
        Assert.Equal(13, t.Rows.Count);
        var row = t.Rows.First(r => Math.Abs(r.Key - 0.5) < 1e-12);
        Assert.Equal("1/2", row.KeyText);
        Assert.Equal("−7/4", row.Cells[0].Text);
        Assert.Equal("√2/2", row.Cells[1].Text);
        var first = t.Rows[0];
        Assert.Equal("undefined", first.Cells[1].Text);
        var dec = GraphTableBuilder.Functions(d.Items, d.ParameterSource, -3, 0.5, 13, exact: false, s);
        Assert.Equal("−1.75", dec.Rows.First(r => Math.Abs(r.Key - 0.5) < 1e-12).Cells[0].Text);
        Assert.StartsWith("x,y1,y2\n-3,7,", dec.ToCsv());
    }

    [Fact]
    public void ParametricTable()
    {
        var s = new CalcSession();
        var item = GraphParser.Parse("(cos(t), sin(t))", s);
        var t = GraphTableBuilder.Parametric(item, [], 0, Math.PI / 2, 5);
        Assert.Equal("t", t.KeyHeader);
        Assert.Equal(1, t.Rows[0].Cells[0].Value, 12);
        Assert.Equal(1, t.Rows[1].Cells[1].Value, 12);
    }

    [Fact]
    public void SvgExportIsWellFormed()
    {
        var s = new CalcSession();
        var d = new GraphDocument(s);
        foreach (var line in new[] { "y = x^2 - 2", "y = a*sin(x)", "y < x/2", "x^2 + y^2 = 9", "A = (2, 2)", "x^2 + y^2 < 1", "y' = x" })
            d.Add(line);
        var v = new Viewport(-6, 6, -4.5, 4.5);
        var pois = PointsOfInterestFinder.Find(d.Items, d.ParameterSource, v, s);
        var scene = GraphScene.Build(d.Items, d.ParameterSource, v, 760, 570, points: pois,
            annotations: GraphAnalysis.Integral(d.Items[0], [], -1, 2).Annotations);
        var svg = SvgExporter.Export(scene, GraphPalette.Nes);
        var doc = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(ns + "svg", doc.Root!.Name);
        Assert.Equal("760", doc.Root.Attribute("width")!.Value);
        Assert.True(doc.Descendants(ns + "polyline").Count() >= 4);
        Assert.Contains(doc.Descendants(ns + "polyline"), p => p.Attribute("stroke-dasharray") is not null);
        Assert.Contains(doc.Descendants(ns + "pattern"), p => p.Attribute("id")!.Value.StartsWith("hatch"));
        Assert.Contains(doc.Descendants(ns + "text"), t => t.Value == "A (2, 2)");
        Assert.Contains("#0058F8", svg);
        Assert.DoesNotContain("NaN", svg);
        Assert.DoesNotContain("Infinity", svg);
    }

    [Fact]
    public void SvgEscapesText()
    {
        Assert.Equal("a &lt; b &amp; &quot;c&quot;", SvgExporter.Escape("a < b & \"c\""));
    }

    [Fact]
    public void ClipperSplitsAtTheEdges()
    {
        var runs = PolylineClipper.Clip([(-10, 5), (5, 5), (5, 50), (6, 50), (6, 5), (8, 5)], 0, 0, 10, 10);
        Assert.Equal(2, runs.Count);
        Assert.Equal((0, 5), runs[0][0]);
        Assert.Equal((5, 10), runs[0][^1]);
    }

    [Fact]
    public void FormatsNumbers()
    {
        Assert.Equal("1.414", GraphFormat.Number(Math.Sqrt(2)));
        Assert.Equal("−0.9011", GraphFormat.Number(-0.901137, 4, true));
        Assert.Equal("0", GraphFormat.Number(1e-17));
        Assert.Equal("1234567", GraphFormat.Number(1234567, 4));
        Assert.Equal("(2, −3.5)", GraphFormat.Point(2, -3.5, 4, true));
    }
}
