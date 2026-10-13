using Calcy.Core;

namespace Calcy.Graphing.Tests;

public class SamplingTests
{
    private static readonly ViewTransform Standard = new(Viewport.Standard, 760, 570);

    private static ItemGeometry Sample(string text, Viewport? v = null, double[]? p = null)
    {
        var item = GraphParser.Parse(text, new CalcSession());
        Assert.True(item.IsPlottable, item.Error);
        var view = v is null ? Standard : new ViewTransform(v.Value, 760, 570);
        return GraphSampler.Sample(item, p ?? new double[item.Parameters.Count], view);
    }

    /// <summary>No drawn segment may cross a pole: both ends on either side of it.</summary>
    private static void AssertNoSegmentCrosses(ItemGeometry g, IEnumerable<double> poles)
    {
        var list = poles.ToList();
        foreach (var line in g.Curves)
            for (int i = 1; i < line.Points.Length; i++)
            {
                double a = line.Points[i - 1].X, b = line.Points[i].X;
                foreach (var pole in list)
                    Assert.False((a < pole && b > pole) || (a > pole && b < pole), $"segment {a}..{b} crosses the pole at {pole}");
            }
    }

    [Fact]
    public void TanIsSplitAtEveryPole()
    {
        var g = Sample("y = tan(x)");
        var region = Viewport.Standard.Expand(SamplingOptions.Default.Margin);
        var poles = Enumerable.Range(-10, 21).Select(k => Math.PI / 2 + k * Math.PI).Where(p => p > region.XMin && p < region.XMax).ToList();
        AssertNoSegmentCrosses(g, poles);
        // One branch per gap between poles (the branches at the edges included).
        Assert.True(g.Curves.Count >= poles.Count + 1, $"{g.Curves.Count} curves for {poles.Count} poles");
        // The branches reach far up and down near each pole (no early cut-off).
        Assert.Contains(g.Curves.SelectMany(c => c.Points), q => q.Y > 15);
        Assert.Contains(g.Curves.SelectMany(c => c.Points), q => q.Y < -15);
    }

    [Fact]
    public void ReciprocalIsSplitAtZero()
    {
        var g = Sample("y = 1/x");
        AssertNoSegmentCrosses(g, [0]);
        Assert.Equal(2, g.Curves.Count);
    }

    [Fact]
    public void ReciprocalSquaredDoesNotConnectThroughThePole()
    {
        var g = Sample("y = 1/x^2 - 3");
        AssertNoSegmentCrosses(g, [0]);
    }

    [Fact]
    public void JumpDiscontinuitiesAreSplit()
    {
        var g = Sample("y = floor(x)");
        foreach (var line in g.Curves)
            for (int i = 1; i < line.Points.Length; i++)
                Assert.True(Math.Abs(line.Points[i].Y - line.Points[i - 1].Y) < 0.5, "a step was joined by a vertical segment");
    }

    [Fact]
    public void SmoothCurvesAreFineEnough()
    {
        var g = Sample("y = sin(5x)");
        var t = Standard;
        foreach (var line in g.Curves)
            for (int i = 1; i < line.Points.Length; i++)
                Assert.True(t.PixelDistance(line.Points[i - 1], line.Points[i]) <= SamplingOptions.Default.MaxSegment + 1e-6);
        Assert.Single(g.Curves);
    }

    [Fact]
    public void SamplesStayOnTheCurve()
    {
        var g = Sample("y = x^3 - 2x");
        foreach (var q in g.Curves.SelectMany(c => c.Points)) Assert.Equal(q.X * q.X * q.X - 2 * q.X, q.Y, 9);
    }

    [Fact]
    public void DomainEdgesAreReachedClosely()
    {
        var g = Sample("y = sqrt(x)");
        Assert.Single(g.Curves);
        Assert.True(g.Curves[0].Points[0].X < 1e-3, $"starts at {g.Curves[0].Points[0].X}");
        var r = Sample("y = x^2 {0 < x < 3}");
        Assert.Single(r.Curves);
        Assert.True(r.Curves[0].Points[0].X < 1e-3 && r.Curves[0].Points[^1].X > 3 - 1e-3);
    }

    [Fact]
    public void ImplicitCircleIsClosedAndOnTheCircle()
    {
        var g = Sample("x^2 + y^2 = 9");
        Assert.Single(g.Curves);
        var c = g.Curves[0];
        Assert.True(c.Closed);
        Assert.True(c.Points.Length > 100);
        foreach (var q in c.Points) Assert.Equal(3, Math.Sqrt(q.X * q.X + q.Y * q.Y), 6);
        Assert.Empty(g.Points);
    }

    [Fact]
    public void ImplicitHyperbolaHasBothBranches()
    {
        var g = Sample("x^2 - y^2 = 1");
        Assert.Equal(2, g.Curves.Count);
        foreach (var q in g.Curves.SelectMany(c => c.Points)) Assert.Equal(1, q.X * q.X - q.Y * q.Y, 5);
    }

    [Fact]
    public void ImplicitThinEllipseIsFound()
    {
        var g = Sample("x^2/64 + 400y^2 = 1");
        Assert.NotEmpty(g.Curves);
        Assert.Contains(g.Curves, c => c.Closed);
    }

    [Fact]
    public void ImplicitSinglePointIsReportedAsOnePoint()
    {
        var g = Sample("x^2 + y^2 = 0");
        Assert.Empty(g.Curves);
        Assert.Single(g.Points);
        var t = Standard;
        Assert.True(t.PixelDistance(g.Points[0].Position, new Point2(0, 0)) < 1, $"point at {g.Points[0].Position}");
    }

    [Fact]
    public void ImplicitPoleIsNotDrawnAsACurve()
    {
        // 1/x = y has no zero crossing on x = 0 even though the sign of 1/x - y flips there.
        var g = Sample("1/x = y");
        foreach (var q in g.Curves.SelectMany(c => c.Points)) Assert.Equal(1 / q.X, q.Y, 4);
    }

    [Fact]
    public void ImplicitFamilyGivesConcentricCircles()
    {
        var g = Sample("x^2 + y^2 = {1, 4, 9}");
        Assert.Equal(3, g.Curves.Count);
        Assert.Equal(new[] { 0, 1, 2 }, g.Curves.Select(c => c.Member).OrderBy(m => m));
    }

    [Fact]
    public void InequalityBelowAFunctionFillsDownwards()
    {
        var g = Sample("y < x/2");
        Assert.Single(g.Curves);
        Assert.True(g.Curves[0].Dashed);
        Assert.Single(g.Fills);
        var region = Viewport.Standard.Expand(SamplingOptions.Default.Margin);
        Assert.Contains(g.Fills[0].Points, q => q.Y == region.YMin);
        Assert.False(Sample("y <= x/2").Curves[0].Dashed);
    }

    [Fact]
    public void InequalityRightOfACurve()
    {
        var g = Sample("x > y^2 - 4");
        var region = Viewport.Standard.Expand(SamplingOptions.Default.Margin);
        Assert.Contains(g.Fills[0].Points, q => q.X == region.XMax);
    }

    [Fact]
    public void ImplicitRegionCellsAreInsideAndBoundaryIsDashed()
    {
        var g = Sample("x^2 + y^2 < 9");
        Assert.NotEmpty(g.FillCells);
        double area = 0;
        foreach (var c in g.FillCells)
        {
            area += c.Width * c.Height;
            // Every cell lies inside the disc (its centre does, and it is small next to the radius).
            double mx = 0.5 * (c.X0 + c.X1), my = 0.5 * (c.Y0 + c.Y1);
            Assert.True(Math.Sqrt(mx * mx + my * my) < 3, $"cell centred at ({mx}, {my})");
        }
        Assert.Equal(9 * Math.PI, area, 0);
        Assert.Contains(g.Curves, c => c.Dashed && c.Closed);
    }

    [Fact]
    public void ChainedInequalityShadesTheIntersection()
    {
        var g = Sample("0 < y < x");
        double area = g.FillCells.Sum(c => Math.Max(0, Math.Min(c.X1, 10) - Math.Max(c.X0, 0)) * Math.Max(0, Math.Min(c.Y1, 10) - Math.Max(c.Y0, 0)));
        Assert.Equal(50, area, 0);
        foreach (var c in g.FillCells) Assert.True(c.Y0 >= -0.1 && c.Y0 <= c.X1 + 0.1);
    }

    [Fact]
    public void ParametricCircleIsSmooth()
    {
        var g = Sample("(3cos(t), 3sin(t))");
        Assert.Single(g.Curves);
        foreach (var q in g.Curves[0].Points) Assert.Equal(3, Math.Sqrt(q.X * q.X + q.Y * q.Y), 9);
        var first = g.Curves[0].Points[0];
        var last = g.Curves[0].Points[^1];
        Assert.True(first.DistanceTo(last) < 1e-9);
    }

    [Fact]
    public void PolarRoseHasThePetals()
    {
        var g = Sample("r = 4cos(3θ)");
        Assert.NotEmpty(g.Curves);
        double maxR = g.Curves.SelectMany(c => c.Points).Max(q => Math.Sqrt(q.X * q.X + q.Y * q.Y));
        Assert.Equal(4, maxR, 2);
    }

    [Fact]
    public void FunctionOfYAndVerticalLine()
    {
        var g = Sample("x = y^2 - 4");
        foreach (var q in g.Curves.SelectMany(c => c.Points)) Assert.Equal(q.Y * q.Y - 4, q.X, 9);
        var v = Sample("x = 3");
        Assert.Single(v.Curves);
        Assert.All(v.Curves[0].Points, q => Assert.Equal(3, q.X));
    }

    [Fact]
    public void PointsAndLabels()
    {
        var g = Sample("A = (2, 2)");
        Assert.Single(g.Points);
        Assert.Equal("A (2, 2)", g.Points[0].Label);
        var list = Sample("{(1, 2), (3, 4)}");
        Assert.Equal(2, list.Points.Count);
    }

    [Fact]
    public void SliderValuesMoveTheCurve()
    {
        var g = Sample("y = a*x", null, [2.0]);
        foreach (var q in g.Curves[0].Points) Assert.Equal(2 * q.X, q.Y, 9);
    }

    [Fact]
    public void SlopeFieldTicksFollowTheSlope()
    {
        var g = Sample("y' = x");
        Assert.True(g.Segments.Count > 100);
        foreach (var s in g.Segments)
        {
            double mx = 0.5 * (s.A.X + s.B.X);
            double slope = (s.B.Y - s.A.Y) / (s.B.X - s.A.X);
            Assert.Equal(mx, slope, 6);
        }
    }
}
