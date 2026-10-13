namespace Calcy.Graphing;

/// <summary>
/// Adaptive sampling of explicit curves (y = f(x), x = g(y)) and parametric curves in pixel space.
/// Intervals are halved where the curve bends, where segments are long, near domain edges and near jumps.
/// A jump that does not shrink when its interval is halved is a pole or a discontinuity: the polyline is split there,
/// so no false vertical line is drawn across an asymptote.
/// </summary>
public static class CurveSampler
{
    /// <summary>
    /// Samples y = f(x) for x in [a, b] (or x = f(y) for y in [a, b] when <paramref name="vertical"/> is true).
    /// Returns polylines in graph coordinates.
    /// </summary>
    public static List<Polyline> SampleFunction(Func<double, double> f, double a, double b, ViewTransform view, SamplingOptions? options = null,
        bool vertical = false, int member = 0, bool dashed = false, CancellationToken ct = default)
    {
        var run = new FunctionRun(f, view, options ?? SamplingOptions.Default, vertical, member, dashed, ct);
        run.Run(a, b);
        return run.Result;
    }

    /// <summary>Samples a parametric curve p(t) for t in [t0, t1]. Returns polylines in graph coordinates.</summary>
    public static List<Polyline> SampleParametric(Func<double, Point2> p, double t0, double t1, ViewTransform view, SamplingOptions? options = null,
        int member = 0, int initialSegments = 256, CancellationToken ct = default)
    {
        var run = new ParametricRun(p, view, options ?? SamplingOptions.Default, member, ct);
        run.Run(t0, t1, initialSegments);
        return run.Result;
    }

    private sealed class FunctionRun(Func<double, double> f, ViewTransform view, SamplingOptions o, bool vertical, int member, bool dashed, CancellationToken ct)
    {
        public readonly List<Polyline> Result = [];
        private List<Point2> _current = [];
        private int _count;
        private readonly double _valueScale = vertical ? view.ScaleX : view.ScaleY;
        private readonly double _argScale = vertical ? view.ScaleY : view.ScaleX;
        private readonly double _valueSpan = vertical ? view.PixelWidth : view.PixelHeight;

        public void Run(double a, double b)
        {
            if (!(b > a)) return;
            double h = o.InitialSpacing / _argScale;
            // A grid aligned to multiples of h (stable while panning), offset so special points like 0 are not hit exactly.
            double start = (Math.Floor(a / h) + 0.318309886) * h;
            while (start > a) start -= h;
            double u = start, fu = f(u);
            if (double.IsFinite(fu)) Add(u, fu);
            int steps = 0;
            while (u < b)
            {
                if ((++steps & 63) == 0) ct.ThrowIfCancellationRequested();
                double u1 = u + h;
                if (u1 <= u) break; // beyond double precision
                double f1 = f(u1);
                Seg(u, fu, u1, f1, 0, double.PositiveInfinity);
                u = u1;
                fu = f1;
                if (_count > o.MaxPoints * 2) break;
            }
            Break();
        }

        /// <summary>Pixel coordinate along the value axis (y for functions, x for x = g(y)).</summary>
        private double ValuePixel(double v) => vertical ? view.ToPixelX(v) : view.ToPixelY(v);

        /// <summary>-1 or +1 when the value is well outside the canvas on that side, 0 when near or inside.</summary>
        private int Side(double v)
        {
            double p = ValuePixel(v);
            if (p < -0.5 * _valueSpan) return -1;
            if (p > 1.5 * _valueSpan) return 1;
            return 0;
        }

        private void Add(double u, double v)
        {
            _current.Add(vertical ? new Point2(v, u) : new Point2(u, v));
            _count++;
        }

        private void Break()
        {
            if (_current.Count >= 2) Result.Add(new Polyline([.. _current], false, member, dashed));
            _current = [];
        }

        private void Seg(double ua, double fa, double ub, double fb, int depth, double prevJump)
        {
            bool da = double.IsFinite(fa), db = double.IsFinite(fb);
            double um = 0.5 * (ua + ub);
            bool budget = _count < o.MaxPoints;
            if (!da && !db)
            {
                // Look for a narrow defined island between two undefined samples.
                if (depth < 3 && budget)
                {
                    double fm = f(um);
                    if (double.IsFinite(fm) || depth < 2)
                    {
                        Seg(ua, fa, um, fm, depth + 1, prevJump);
                        Seg(um, fm, ub, fb, depth + 1, prevJump);
                    }
                }
                return;
            }
            if (da != db)
            {
                // Edge of the domain: bisect towards it so the curve ends where it really ends.
                if (depth < o.MaxDepth + 8 && budget)
                {
                    double fm = f(um);
                    Seg(ua, fa, um, fm, depth + 1, prevJump);
                    Seg(um, fm, ub, fb, depth + 1, prevJump);
                    return;
                }
                if (db)
                {
                    Break();
                    Add(ub, fb);
                }
                else Break();
                return;
            }

            double pa = ValuePixel(fa), pb = ValuePixel(fb);
            double dv = Math.Abs(pb - pa);
            double du = (ub - ua) * _argScale;
            int sa = Side(fa), sb = Side(fb);
            double fMid = f(um);
            if (sa != 0 && sa == sb && depth >= 1 && double.IsFinite(fMid) && Side(fMid) == sa)
            {
                // Far off the canvas on one side: nothing to draw here. Splitting (rather than joining) also means
                // a pole between the two samples, as in 1/x², can never be bridged.
                Break();
                Add(ub, fb);
                return;
            }
            if (!double.IsFinite(fMid))
            {
                if (depth < o.MaxDepth + 8 && budget)
                {
                    Seg(ua, fa, um, fMid, depth + 1, dv);
                    Seg(um, fMid, ub, fb, depth + 1, dv);
                    return;
                }
                Break();
                Add(ub, fb);
                return;
            }
            double pm = ValuePixel(fMid);
            double deviation = Math.Abs(pm - 0.5 * (pa + pb));
            double length = Math.Sqrt(dv * dv + du * du);
            bool refine = length > o.MaxSegment || deviation > o.Flatness;
            if (refine && depth < o.MaxDepth && budget)
            {
                Seg(ua, fa, um, fMid, depth + 1, dv);
                Seg(um, fMid, ub, fb, depth + 1, dv);
                return;
            }
            if (dv > o.JumpPixels && depth >= o.MaxDepth && dv > 0.75 * prevJump)
            {
                // The jump does not shrink with the interval: a pole or a jump discontinuity.
                Break();
                Add(ub, fb);
                return;
            }
            if (!budget && sa != 0 && sb != 0 && sa != sb)
            {
                // Out of budget across opposite sides: never connect through an asymptote.
                Break();
                Add(ub, fb);
                return;
            }
            Add(ub, fb);
        }
    }

    private sealed class ParametricRun(Func<double, Point2> p, ViewTransform view, SamplingOptions o, int member, CancellationToken ct)
    {
        public readonly List<Polyline> Result = [];
        private List<Point2> _current = [];
        private int _count;

        public void Run(double t0, double t1, int initial)
        {
            if (!(t1 > t0) || !double.IsFinite(t0) || !double.IsFinite(t1)) return;
            int n = Math.Clamp(initial, 8, 20000);
            double h = (t1 - t0) / n;
            double ta = t0;
            var pa = p(ta);
            if (pa.IsFinite) Add(pa);
            for (int i = 1; i <= n; i++)
            {
                if ((i & 63) == 0) ct.ThrowIfCancellationRequested();
                double tb = i == n ? t1 : t0 + i * h;
                var pb = p(tb);
                Seg(ta, pa, tb, pb, 0, double.PositiveInfinity);
                ta = tb;
                pa = pb;
                if (_count > o.MaxPoints * 2) break;
            }
            Break();
        }

        private (double X, double Y) Px(Point2 q) => (view.ToPixelX(q.X), view.ToPixelY(q.Y));

        private int SideX(double px) => px < -0.5 * view.PixelWidth ? -1 : px > 1.5 * view.PixelWidth ? 1 : 0;
        private int SideY(double py) => py < -0.5 * view.PixelHeight ? -1 : py > 1.5 * view.PixelHeight ? 1 : 0;

        private void Add(Point2 q)
        {
            _current.Add(q);
            _count++;
        }

        private void Break()
        {
            if (_current.Count >= 2) Result.Add(new Polyline([.. _current], false, member));
            _current = [];
        }

        private void Seg(double ta, Point2 a, double tb, Point2 b, int depth, double prevDist)
        {
            bool da = a.IsFinite, db = b.IsFinite;
            double tm = 0.5 * (ta + tb);
            bool budget = _count < o.MaxPoints;
            if (!da && !db)
            {
                if (depth < 3 && budget)
                {
                    var m0 = p(tm);
                    if (m0.IsFinite || depth < 2)
                    {
                        Seg(ta, a, tm, m0, depth + 1, prevDist);
                        Seg(tm, m0, tb, b, depth + 1, prevDist);
                    }
                }
                return;
            }
            if (da != db)
            {
                if (depth < o.MaxDepth + 8 && budget)
                {
                    var m0 = p(tm);
                    Seg(ta, a, tm, m0, depth + 1, prevDist);
                    Seg(tm, m0, tb, b, depth + 1, prevDist);
                    return;
                }
                if (db)
                {
                    Break();
                    Add(b);
                }
                else Break();
                return;
            }
            var (ax, ay) = Px(a);
            var (bx, by) = Px(b);
            double dist = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            var m = p(tm);
            if (!m.IsFinite)
            {
                if (depth < o.MaxDepth + 8 && budget)
                {
                    Seg(ta, a, tm, m, depth + 1, dist);
                    Seg(tm, m, tb, b, depth + 1, dist);
                    return;
                }
                Break();
                Add(b);
                return;
            }
            var (mx, my) = Px(m);
            bool off = depth >= 1 &&
                       ((SideX(ax) != 0 && SideX(ax) == SideX(bx) && SideX(mx) == SideX(ax)) ||
                        (SideY(ay) != 0 && SideY(ay) == SideY(by) && SideY(my) == SideY(ay)));
            if (off)
            {
                Add(b);
                return;
            }
            double deviation = Math.Sqrt((mx - 0.5 * (ax + bx)) * (mx - 0.5 * (ax + bx)) + (my - 0.5 * (ay + by)) * (my - 0.5 * (ay + by)));
            bool refine = dist > o.MaxSegment || deviation > o.Flatness;
            if (refine && depth < o.MaxDepth && budget)
            {
                Seg(ta, a, tm, m, depth + 1, dist);
                Seg(tm, m, tb, b, depth + 1, dist);
                return;
            }
            if (dist > o.JumpPixels && depth >= o.MaxDepth && dist > 0.75 * prevDist)
            {
                Break();
                Add(b);
                return;
            }
            Add(b);
        }
    }
}
