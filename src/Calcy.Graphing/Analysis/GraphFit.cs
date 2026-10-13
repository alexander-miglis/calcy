namespace Calcy.Graphing;

/// <summary>Zoom Fit: chooses a y range that shows the visible graphs over the current x range.</summary>
public static class GraphFit
{
    /// <summary>
    /// Keeps the x range and fits the y range to the visible functions, curves and points. Outliers near poles are ignored,
    /// so tan(x) or 1/x still give a useful window.
    /// </summary>
    public static Viewport FitY(IReadOnlyList<GraphItem> items, ParameterSource parameters, Viewport v, int samples = 800)
    {
        var ys = new List<double>();
        foreach (var item in items)
        {
            if (!item.IsVisible || !item.IsPlottable) continue;
            var p = parameters(item);
            foreach (var m in item.Members)
            {
                switch (item.Kind)
                {
                    case GraphKind.Function or GraphKind.InequalityY when m.Main is not null:
                        for (int i = 0; i <= samples; i++)
                        {
                            double y = m.Main.Eval(v.XMin + v.Width * i / samples, p);
                            if (double.IsFinite(y)) ys.Add(y);
                        }
                        break;
                    case GraphKind.Parametric or GraphKind.Polar:
                        for (int i = 0; i <= samples; i++)
                        {
                            double t = item.TMin + (item.TMax - item.TMin) * i / samples;
                            Point2 q;
                            if (item.Kind == GraphKind.Polar)
                            {
                                double r = m.Main?.Eval(t, p) ?? double.NaN;
                                q = new Point2(r * Math.Cos(t * item.AngleToRadians), r * Math.Sin(t * item.AngleToRadians));
                            }
                            else q = new Point2(m.X?.Eval(t, p) ?? double.NaN, m.Y?.Eval(t, p) ?? double.NaN);
                            if (q.IsFinite && q.X >= v.XMin && q.X <= v.XMax) ys.Add(q.Y);
                        }
                        break;
                    case GraphKind.Point or GraphKind.Points:
                        foreach (var (cx, cy) in m.PointCoordinates)
                        {
                            double x = cx.Eval(p), y = cy.Eval(p);
                            if (double.IsFinite(x) && double.IsFinite(y) && x >= v.XMin && x <= v.XMax) ys.Add(y);
                        }
                        break;
                }
            }
        }
        if (ys.Count == 0) return v;
        ys.Sort();
        double lo = ys[0], hi = ys[^1];
        double q02 = ys[(int)(0.02 * (ys.Count - 1))], q98 = ys[(int)(0.98 * (ys.Count - 1))];
        if (hi - lo > 20 * Math.Max(q98 - q02, 1e-12))
        {
            lo = q02;
            hi = q98;
        }
        if (hi - lo < 1e-9 * Math.Max(1, Math.Abs(hi)))
        {
            lo -= 1;
            hi += 1;
        }
        double pad = 0.08 * (hi - lo);
        return new Viewport(v.XMin, v.XMax, lo - pad, hi + pad);
    }
}
