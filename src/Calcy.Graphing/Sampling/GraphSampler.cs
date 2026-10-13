using Calcy.Core.Expressions;
using Calcy.Graphing.Numerics;

namespace Calcy.Graphing;

/// <summary>Samples any <see cref="GraphItem"/> for a viewport: picks the right sampler for its kind.</summary>
public static class GraphSampler
{
    /// <summary>
    /// Samples one item. <paramref name="parameters"/> are the slider values aligned with <see cref="GraphItem.Parameters"/>.
    /// The geometry covers the viewport plus a margin (see <see cref="SamplingOptions.Margin"/>).
    /// </summary>
    public static ItemGeometry Sample(GraphItem item, double[] parameters, ViewTransform view, SamplingOptions? options = null, CancellationToken ct = default)
    {
        var o = options ?? SamplingOptions.Default;
        var region = view.Viewport.Expand(o.Margin);
        if (!item.IsPlottable) return ItemGeometry.Empty(item, region);
        var p = parameters.Length == item.Parameters.Count ? parameters : new double[item.Parameters.Count];
        switch (item.Kind)
        {
            case GraphKind.Function:
            case GraphKind.FunctionOfY:
            {
                bool vertical = item.Kind == GraphKind.FunctionOfY;
                var curves = new List<Polyline>();
                foreach (var m in item.Members)
                {
                    if (m.Main is null) continue;
                    var f = m.Main.Bind1(p);
                    curves.AddRange(vertical
                        ? CurveSampler.SampleFunction(f, region.YMin, region.YMax, view, o, true, m.Index, ct: ct)
                        : CurveSampler.SampleFunction(f, region.XMin, region.XMax, view, o, false, m.Index, ct: ct));
                }
                return new ItemGeometry { Item = item, Sampled = region, Curves = curves };
            }
            case GraphKind.VerticalLine:
            {
                var curves = new List<Polyline>();
                foreach (var m in item.Members)
                {
                    double c = m.Main?.Eval(p) ?? double.NaN;
                    if (double.IsFinite(c)) curves.Add(new Polyline([new Point2(c, region.YMin), new Point2(c, region.YMax)], false, m.Index));
                }
                return new ItemGeometry { Item = item, Sampled = region, Curves = curves };
            }
            case GraphKind.Polar:
            {
                var curves = new List<Polyline>();
                double k = item.AngleToRadians;
                int initial = InitialSegments((item.TMax - item.TMin) * k);
                foreach (var m in item.Members)
                {
                    if (m.Main is null) continue;
                    var r = m.Main.Bind1(p);
                    curves.AddRange(CurveSampler.SampleParametric(th =>
                    {
                        double rv = r(th), a = th * k;
                        return new Point2(rv * Math.Cos(a), rv * Math.Sin(a));
                    }, item.TMin, item.TMax, view, o, m.Index, initial, ct));
                }
                return new ItemGeometry { Item = item, Sampled = region, Curves = curves };
            }
            case GraphKind.Parametric:
            {
                var curves = new List<Polyline>();
                int initial = InitialSegments(item.TMax - item.TMin);
                foreach (var m in item.Members)
                {
                    if (m.X is null || m.Y is null) continue;
                    var fx = m.X.Bind1(p);
                    var fy = m.Y.Bind1(p);
                    curves.AddRange(CurveSampler.SampleParametric(t => new Point2(fx(t), fy(t)), item.TMin, item.TMax, view, o, m.Index, initial, ct));
                }
                return new ItemGeometry { Item = item, Sampled = region, Curves = curves };
            }
            case GraphKind.Implicit:
            {
                var area = view.Viewport.Expand(o.ImplicitMargin);
                var curves = new List<Polyline>();
                var points = new List<PlotPoint>();
                bool incomplete = false;
                foreach (var m in item.Members)
                {
                    if (m.Main is null) continue;
                    var (fi, reliable) = BindInterval(m.Main, p);
                    var res = ImplicitSampler.Sample(m.Main.Bind2(p), fi, reliable, area, view, o, null, m.Index, false, ct);
                    curves.AddRange(res.Curves);
                    points.AddRange(res.Points.Select(q => new PlotPoint(q, null, m.Index)));
                    incomplete |= res.MayBeIncomplete;
                }
                return new ItemGeometry { Item = item, Sampled = area, Curves = curves, Points = points, MayBeIncomplete = incomplete };
            }
            case GraphKind.InequalityY:
            case GraphKind.InequalityX:
                return Inequality(item, p, view, region, o, ct);
            case GraphKind.Region:
            {
                var area = view.Viewport.Expand(o.ImplicitMargin);
                var cells = new List<Rect2>();
                var curves = new List<Polyline>();
                bool incomplete = false;
                foreach (var m in item.Members)
                {
                    var conds = m.Conditions.Select(c =>
                    {
                        var (fi, reliable) = BindInterval(c.Function, p);
                        return new BoundCondition(c.Function.Bind2(p), fi, reliable, c.Op);
                    }).ToList();
                    var res = RegionSampler.Sample(conds, area, view, o, m.Index, ct);
                    cells.AddRange(res.Cells);
                    curves.AddRange(res.Boundaries);
                    incomplete |= res.MayBeIncomplete;
                }
                return new ItemGeometry { Item = item, Sampled = area, Curves = curves, FillCells = cells, MayBeIncomplete = incomplete };
            }
            case GraphKind.Point:
            case GraphKind.Points:
            {
                var pts = new List<PlotPoint>();
                foreach (var m in item.Members)
                {
                    foreach (var (cx, cy) in m.PointCoordinates)
                    {
                        double x = cx.Eval(p), y = cy.Eval(p);
                        if (!double.IsFinite(x) || !double.IsFinite(y)) continue;
                        string? label = item.Kind == GraphKind.Point && item.Label is not null ? $"{item.Label} {GraphFormat.Point(x, y)}" : null;
                        pts.Add(new PlotPoint(new Point2(x, y), label, m.Index));
                    }
                }
                return new ItemGeometry { Item = item, Sampled = region, Points = pts };
            }
            case GraphKind.SlopeField:
                return SlopeField(item, p, view, o, ct);
            default:
                return ItemGeometry.Empty(item, region);
        }
    }

    private static int InitialSegments(double range) => (int)Math.Clamp(256 * Math.Max(1, Math.Abs(range) / (2 * Math.PI)), 64, 4000);

    /// <summary>Binds an interval evaluator to fixed parameter values.</summary>
    public static (Func<Interval, Interval, Interval>? F, bool Reliable) BindInterval(CompiledExpr c, double[] p)
    {
        IntervalFunction fn;
        try
        {
            fn = c.Interval;
        }
        catch (Calcy.Core.CalcException)
        {
            return (null, false);
        }
        var pi = p.Select(Interval.Point).ToArray();
        return ((x, y) =>
        {
            var arr = new Interval[2 + pi.Length];
            arr[0] = x;
            arr[1] = y;
            Array.Copy(pi, 0, arr, 2, pi.Length);
            return fn.Evaluate(arr);
        }, fn.IsReliable);
    }

    private static ItemGeometry Inequality(GraphItem item, double[] p, ViewTransform view, Viewport region, SamplingOptions o, CancellationToken ct)
    {
        bool vertical = item.Kind == GraphKind.InequalityX;
        bool strict = item.Relation is RelOp.Less or RelOp.Greater;
        bool below = item.Relation is RelOp.Less or RelOp.LessEqual;
        var curves = new List<Polyline>();
        var fills = new List<Polygon2>();
        foreach (var m in item.Members)
        {
            if (m.Main is null) continue;
            var f = m.Main.Bind1(p);
            var lines = vertical
                ? CurveSampler.SampleFunction(f, region.YMin, region.YMax, view, o, true, m.Index, strict, ct)
                : CurveSampler.SampleFunction(f, region.XMin, region.XMax, view, o, false, m.Index, strict, ct);
            curves.AddRange(lines);
            foreach (var line in lines)
            {
                var pts = new List<Point2>(line.Points.Length + 2);
                if (!vertical)
                {
                    double edge = below ? region.YMin : region.YMax;
                    foreach (var q in line.Points) pts.Add(new Point2(q.X, Math.Clamp(q.Y, region.YMin, region.YMax)));
                    pts.Add(new Point2(line.Points[^1].X, edge));
                    pts.Add(new Point2(line.Points[0].X, edge));
                }
                else
                {
                    double edge = below ? region.XMin : region.XMax;
                    foreach (var q in line.Points) pts.Add(new Point2(Math.Clamp(q.X, region.XMin, region.XMax), q.Y));
                    pts.Add(new Point2(edge, line.Points[^1].Y));
                    pts.Add(new Point2(edge, line.Points[0].Y));
                }
                fills.Add(new Polygon2([.. pts], m.Index));
            }
        }
        return new ItemGeometry { Item = item, Sampled = region, Curves = curves, Fills = fills };
    }

    private static ItemGeometry SlopeField(GraphItem item, double[] p, ViewTransform view, SamplingOptions o, CancellationToken ct)
    {
        var region = view.Viewport.Expand(0.15);
        var segs = new List<Segment2>();
        var m = item.Members.Count > 0 ? item.Members[0].Main : null;
        if (m is null) return ItemGeometry.Empty(item, region);
        var f = m.Bind2(p);
        double sp = Math.Max(8, o.SlopeSpacing);
        double sx = sp / view.ScaleX, sy = sp / view.ScaleY;
        long i0 = (long)Math.Floor(region.XMin / sx), i1 = (long)Math.Ceiling(region.XMax / sx);
        long j0 = (long)Math.Floor(region.YMin / sy), j1 = (long)Math.Ceiling(region.YMax / sy);
        if ((i1 - i0) * (j1 - j0) > 20_000) return ItemGeometry.Empty(item, region);
        double half = 0.36 * sp;
        for (long i = i0; i <= i1; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (long j = j0; j <= j1; j++)
            {
                double x = (i + 0.5) * sx, y = (j + 0.5) * sy;
                double slope = f(x, y);
                if (double.IsNaN(slope)) continue;
                double dx, dy;
                if (double.IsInfinity(slope))
                {
                    dx = 0;
                    dy = 1;
                }
                else
                {
                    dx = view.ScaleX;
                    dy = slope * view.ScaleY;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len == 0 || !double.IsFinite(len))
                    {
                        dx = 0;
                        dy = 1;
                    }
                    else
                    {
                        dx /= len;
                        dy /= len;
                    }
                }
                double ux = dx * half / view.ScaleX, uy = dy * half / view.ScaleY;
                segs.Add(new Segment2(new Point2(x - ux, y - uy), new Point2(x + ux, y + uy)));
            }
        }
        return new ItemGeometry { Item = item, Sampled = region, Segments = segs };
    }
}
