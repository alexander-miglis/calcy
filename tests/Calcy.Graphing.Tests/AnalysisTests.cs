using Calcy.Core;

namespace Calcy.Graphing.Tests;

public class AnalysisTests
{
    private static (GraphDocument Doc, CalcSession Session) Doc(params string[] lines)
    {
        var s = new CalcSession();
        var d = new GraphDocument(s);
        foreach (var l in lines) d.Add(l);
        return (d, s);
    }

    private static IReadOnlyList<PointOfInterest> Pois(GraphDocument d, Viewport? v = null) =>
        PointsOfInterestFinder.Find(d.Items, d.ParameterSource, v ?? Viewport.Standard, d.Session);

    [Fact]
    public void RootsAndMinimumOfAParabola()
    {
        var (d, _) = Doc("y = x^2 - 2");
        var pois = Pois(d);
        var roots = pois.Where(p => p.Kind == PoiKind.Root).Select(p => p.X).OrderBy(x => x).ToList();
        Assert.Equal(2, roots.Count);
        Assert.Equal(-Math.Sqrt(2), roots[0], 1e-9);
        Assert.Equal(Math.Sqrt(2), roots[1], 1e-9);
        var min = Assert.Single(pois, p => p.Kind == PoiKind.Minimum);
        Assert.Equal(0, min.X, 1e-9);
        Assert.Equal(-2, min.Y, 1e-9);
        Assert.Equal("(0, −2)", min.Text);
        var root = pois.First(p => p.Kind == PoiKind.Root && p.X > 0);
        Assert.Equal("x = √2 ≈ 1.414", root.Text);
        Assert.Equal("Root of y1", root.Title);
        // The y-intercept coincides with the minimum and is not listed twice.
        Assert.DoesNotContain(pois, p => p.Kind == PoiKind.YIntercept);
    }

    [Fact]
    public void ExtremaOfSine()
    {
        var (d, _) = Doc("y = sin(x)");
        var pois = Pois(d);
        var maxima = pois.Where(p => p.Kind == PoiKind.Maximum).Select(p => p.X).OrderBy(x => x).ToList();
        Assert.Equal(3, maxima.Count);
        Assert.Equal(Math.PI / 2, maxima.First(x => x > 0), 1e-9);
        var minima = pois.Where(p => p.Kind == PoiKind.Minimum).ToList();
        Assert.Contains(minima, p => Math.Abs(p.X + Math.PI / 2) < 1e-9 && Math.Abs(p.Y + 1) < 1e-12);
        var piRoot = pois.Single(p => p.Kind == PoiKind.Root && Math.Abs(p.X - Math.PI) < 1e-6);
        Assert.Equal(Math.PI, piRoot.X, 1e-9);
        Assert.Equal("x = π ≈ 3.142", piRoot.Text);
        Assert.Equal(7, pois.Count(p => p.Kind == PoiKind.Root));
    }

    [Fact]
    public void TouchingRootIsFound()
    {
        var (d, _) = Doc("y = (x - 1)^2");
        var root = Assert.Single(Pois(d), p => p.Kind == PoiKind.Root);
        Assert.Equal(1, root.X, 1e-9);
        Assert.True(root.Touching);
    }

    [Fact]
    public void IntersectionsOfLineAndParabola()
    {
        var (d, _) = Doc("y = x^2", "y = x + 2");
        var xs = Pois(d).Where(p => p.Kind == PoiKind.Intersection).Select(p => p.X).OrderBy(x => x).ToList();
        Assert.Equal(2, xs.Count);
        Assert.Equal(-1, xs[0], 1e-9);
        Assert.Equal(2, xs[1], 1e-9);
        var pt = Pois(d).First(p => p.Kind == PoiKind.Intersection && p.X > 0);
        Assert.Equal("y1 meets y2", pt.Title);
        Assert.Equal("(2, 4)", pt.Text);
    }

    [Fact]
    public void IntersectionsWithSlider()
    {
        var (d, _) = Doc("y = x^2 - 2", "y = a*sin(x)");
        d.Parameters["a"]!.Value = 1.5;
        var xs = Pois(d, new Viewport(-6, 6, -4.5, 4.5)).Where(p => p.Kind == PoiKind.Intersection).ToList();
        Assert.Equal(2, xs.Count);
        foreach (var p in xs) Assert.Equal(p.X * p.X - 2, 1.5 * Math.Sin(p.X), 1e-9);
    }

    [Fact]
    public void TangentIntersectionIsFound()
    {
        var (d, _) = Doc("y = x^2", "y = 2x - 1");
        var p = Assert.Single(Pois(d), q => q.Kind == PoiKind.Intersection);
        Assert.Equal(1, p.X, 1e-7);
    }

    [Fact]
    public void InflectionOfCubic()
    {
        var (d, _) = Doc("y = x^3 - 3x");
        var pois = Pois(d);
        var infl = Assert.Single(pois, p => p.Kind == PoiKind.Inflection || (p.Kind == PoiKind.Root && Math.Abs(p.X) < 1e-9));
        Assert.Equal(0, infl.X, 1e-9);
        var max = Assert.Single(pois, p => p.Kind == PoiKind.Maximum);
        Assert.Equal(-1, max.X, 1e-9);
        Assert.Equal(2, max.Y, 1e-9);
    }

    [Fact]
    public void PolesAreNotRootsOrExtrema()
    {
        var (d, _) = Doc("y = tan(x)");
        var pois = Pois(d);
        Assert.All(pois.Where(p => p.Kind == PoiKind.Root), p => Assert.True(Math.Abs(Math.Tan(p.X)) < 1e-9));
        Assert.DoesNotContain(pois, p => p.Kind is PoiKind.Minimum or PoiKind.Maximum);
        var (r, _) = Doc("y = 1/x^2");
        Assert.DoesNotContain(Pois(r), p => p.Kind is PoiKind.Minimum or PoiKind.Maximum or PoiKind.Root);
    }

    [Fact]
    public void YInterceptOfALine()
    {
        var (d, _) = Doc("y = 3x + 1/2");
        var y = Assert.Single(Pois(d), p => p.Kind == PoiKind.YIntercept);
        Assert.Equal(0.5, y.Y, 12);
        Assert.Equal("(0, 1/2)", y.Text);
    }

    [Fact]
    public void IntegralOfXSquared()
    {
        var (d, s) = Doc("y = x^2");
        var r = GraphAnalysis.Integral(d.Items[0], [], 0, 3, s);
        Assert.Equal(9, r.Value, 1e-12);
        Assert.StartsWith("= 9", r.Text);
        Assert.NotEmpty(r.Annotations);
        Assert.Equal(-9, GraphAnalysis.Integral(d.Items[0], [], 3, 0).Value, 1e-12);
    }

    [Fact]
    public void IntegralOfSineOverAPeriodAndAGaussian()
    {
        var (d, _) = Doc("y = sin(x)", "y = exp(-x^2)");
        Assert.Equal(0, GraphAnalysis.Integral(d.Items[0], [], 0, 2 * Math.PI).Value, 1e-12);
        Assert.Equal(2, GraphAnalysis.Integral(d.Items[0], [], 0, Math.PI).Value, 1e-12);
        Assert.Equal(Math.Sqrt(Math.PI), GraphAnalysis.Integral(d.Items[1], [], -10, 10).Value, 1e-10);
    }

    [Fact]
    public void AreaBetweenCurves()
    {
        var (d, _) = Doc("y = x^2", "y = x + 2");
        var r = GraphAnalysis.AreaBetween(d.Items[0], [], d.Items[1], [], -1, 2);
        Assert.Equal(4.5, r.Value, 1e-10);
        // Crossing curves: |sin - cos| over [0, π].
        var (e, _) = Doc("y = sin(x)", "y = cos(x)");
        Assert.Equal(2 * Math.Sqrt(2), GraphAnalysis.AreaBetween(e.Items[0], [], e.Items[1], [], 0, Math.PI).Value, 1e-9);
    }

    [Fact]
    public void SlopeAndTangent()
    {
        var (d, s) = Doc("y = x^2", "y = a*sin(x)");
        var r = GraphAnalysis.Slope(d.Items[0], [], 3, s);
        Assert.Equal(6, r.Value, 1e-12);
        Assert.Contains(r.Annotations, a => a.Kind == AnnotationKind.Line);
        Assert.Contains("tangent y = 6x", r.Text);
        var a = GraphAnalysis.Slope(d.Items[1], [1.5], 0, s);
        Assert.Equal(1.5, a.Value, 1e-12);
        Assert.StartsWith("= a = 3/2", a.Text);
    }

    [Fact]
    public void ValueAtAndSolveForX()
    {
        var (d, s) = Doc("y = x^2 - 2");
        var v = GraphAnalysis.ValueAt(d.Items[0], [], 0.5, s);
        Assert.Equal(-1.75, v.Value, 12);
        Assert.Equal("y = −7/4 ≈ −1.75", v.Text);
        var x = GraphAnalysis.SolveForX(d.Items[0], [], 2, -10, 10, s);
        Assert.Equal(2, x.Values.Count);
        Assert.Equal(-2, x.Values[0], 1e-12);
        Assert.Equal("x = −2, 2", x.Text);
    }

    [Fact]
    public void ArcLengthAndDistance()
    {
        var (d, _) = Doc("y = 2x", "(cos(t), sin(t))");
        Assert.Equal(Math.Sqrt(5) * 3, GraphAnalysis.ArcLength(d.Items[0], [], 0, 3).Value, 1e-9);
        Assert.Equal(2 * Math.PI, GraphAnalysis.ArcLength(d.Items[1], [], 0, 2 * Math.PI).Value, 1e-8);
        Assert.Equal(5, GraphAnalysis.Distance(new Point2(0, 0), new Point2(3, 4)).Value, 12);
    }

    [Fact]
    public void FitFindsAUsefulWindow()
    {
        var (d, _) = Doc("y = x^2");
        var v = GraphFit.FitY(d.Items, d.ParameterSource, new Viewport(-3, 3, -1, 1));
        Assert.True(v.YMin < 0.01 && v.YMin > -1.5);
        Assert.True(v.YMax > 9 && v.YMax < 10.5);
        var (t, _) = Doc("y = tan(x)");
        var vt = GraphFit.FitY(t.Items, t.ParameterSource, Viewport.Standard);
        Assert.True(vt.Height < 1000, $"tan fit too tall: {vt}");
    }
}
