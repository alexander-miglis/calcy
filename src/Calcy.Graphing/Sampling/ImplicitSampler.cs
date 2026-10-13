using System.Buffers;
using Calcy.Graphing.Numerics;

namespace Calcy.Graphing;

/// <summary>The curves and isolated points of an implicit relation F(x, y) = 0.</summary>
public sealed class ImplicitResult
{
    /// <summary>Stitched curves (closed when they return to their start).</summary>
    public List<Polyline> Curves { get; } = [];

    /// <summary>Isolated points (touching zeros such as x² + y² = 0, or loops smaller than a cell).</summary>
    public List<Point2> Points { get; } = [];

    /// <summary>True when completeness cannot be guaranteed (no interval rule for part of F, or the cell budget was hit).</summary>
    public bool MayBeIncomplete { get; set; }
}

/// <summary>A uniform grid of leaf cells over a region, aligned to multiples of the cell size so it is stable while panning.</summary>
internal sealed class LeafGrid
{
    public double X0 { get; private init; }
    public double Y0 { get; private init; }
    public double Hx { get; private init; }
    public double Hy { get; private init; }
    public int Nx { get; private init; }
    public int Ny { get; private init; }
    public bool Coarsened { get; private init; }

    public double X(int i) => X0 + i * Hx;
    public double Y(int j) => Y0 + j * Hy;

    public static LeafGrid Create(Viewport region, ViewTransform t, SamplingOptions o)
    {
        double hx = o.ImplicitCell / t.ScaleX, hy = o.ImplicitCell / t.ScaleY;
        double nx = region.Width / hx, ny = region.Height / hy;
        bool coarse = false;
        if (nx * ny > o.ImplicitMaxCells)
        {
            double k = Math.Sqrt(nx * ny / o.ImplicitMaxCells);
            hx *= k;
            hy *= k;
            coarse = true;
        }
        // Offsets keep special lines such as x = 0 off the grid lines (exact zeros on corners are degenerate).
        double x0 = (Math.Floor(region.XMin / hx) + 0.3711) * hx;
        if (x0 > region.XMin) x0 -= hx;
        double y0 = (Math.Floor(region.YMin / hy) + 0.2637) * hy;
        if (y0 > region.YMin) y0 -= hy;
        int cx = (int)Math.Ceiling((region.XMax - x0) / hx), cy = (int)Math.Ceiling((region.YMax - y0) / hy);
        return new LeafGrid { X0 = x0, Y0 = y0, Hx = hx, Hy = hy, Nx = Math.Clamp(cx, 1, 1 << 15), Ny = Math.Clamp(cy, 1, 1 << 15), Coarsened = coarse };
    }
}

/// <summary>
/// Plots F(x, y) = 0 on a viewport. Interval arithmetic discards every quadtree cell where F cannot be zero, the rest is
/// subdivided to about two pixels, and marching squares with refined edge crossings traces the curve (saddles are
/// resolved with the cell centre). Thin features below the cell size are found by refining with intervals.
/// </summary>
public static class ImplicitSampler
{
    private const int BlockLevels = 5; // root blocks of 32 × 32 leaves

    /// <summary>Samples the zero set of <paramref name="f"/> over <paramref name="region"/>.</summary>
    /// <param name="f">Point evaluation.</param>
    /// <param name="fi">Interval evaluation over boxes, or null when unavailable.</param>
    /// <param name="intervalReliable">False when the interval rule is only partial (the result may then be incomplete).</param>
    /// <param name="region">The area to plot (graph coordinates).</param>
    /// <param name="view">The pixel mapping (sets the cell size).</param>
    /// <param name="options">Sampling settings.</param>
    /// <param name="keep">Optional filter: only curve pieces whose midpoint passes are kept (used for region boundaries).</param>
    /// <param name="member">Family member index for the produced polylines.</param>
    /// <param name="dashed">Marks the polylines as dashed.</param>
    /// <param name="ct">Cancellation.</param>
    public static ImplicitResult Sample(Func<double, double, double> f, Func<Interval, Interval, Interval>? fi, bool intervalReliable, Viewport region,
        ViewTransform view, SamplingOptions? options = null, Func<double, double, bool>? keep = null, int member = 0, bool dashed = false,
        CancellationToken ct = default)
    {
        var o = options ?? SamplingOptions.Default;
        var result = new ImplicitResult();
        var grid = LeafGrid.Create(region, view, o);
        result.MayBeIncomplete = fi is null || !intervalReliable || grid.Coarsened;
        var run = new Run(f, fi, grid, view, keep, ct);
        try
        {
            run.FindLeaves();
            run.March();
            run.Stitch(result, member, dashed);
            if (fi is not null && intervalReliable) run.ThinFeatures(result);
        }
        finally
        {
            run.Dispose();
        }
        return result;
    }

    private sealed class Run(Func<double, double, double> f, Func<Interval, Interval, Interval>? fi, LeafGrid g, ViewTransform view,
        Func<double, double, bool>? keep, CancellationToken ct) : IDisposable
    {
        private readonly List<(int I, int J)> _leaves = [];
        private readonly List<(int I, int J)> _flatLeaves = [];
        private readonly Dictionary<long, double> _sparse = [];
        private double[]? _dense;
        private byte[]? _denseSet;
        private readonly Dictionary<long, (Point2 P, bool Valid)> _crossings = [];
        private readonly List<(long A, long B)> _segments = [];
        private int _visits;

        private long CornerIndex(int i, int j) => (long)j * (g.Nx + 1) + i;

        public void FindLeaves()
        {
            int block = 1 << BlockLevels;
            for (int bj = 0; bj < g.Ny; bj += block)
                for (int bi = 0; bi < g.Nx; bi += block)
                    Visit(bi, bj, block);
            long corners = (long)(g.Nx + 1) * (g.Ny + 1);
            if (_leaves.Count > 20_000 && corners < 8_000_000)
            {
                _dense = ArrayPool<double>.Shared.Rent((int)corners);
                _denseSet = ArrayPool<byte>.Shared.Rent((int)corners);
                Array.Clear(_denseSet, 0, (int)corners);
            }
        }

        private void Visit(int i0, int j0, int size)
        {
            if (i0 >= g.Nx || j0 >= g.Ny) return;
            if ((++_visits & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (fi is not null)
            {
                int i1 = Math.Min(i0 + size, g.Nx), j1 = Math.Min(j0 + size, g.Ny);
                var v = fi(new Interval(g.X(i0), g.X(i1)), new Interval(g.Y(j0), g.Y(j1)));
                if (!v.ContainsZero) return;
            }
            if (size == 1)
            {
                _leaves.Add((i0, j0));
                return;
            }
            int h = size / 2;
            Visit(i0, j0, h);
            Visit(i0 + h, j0, h);
            Visit(i0, j0 + h, h);
            Visit(i0 + h, j0 + h, h);
        }

        private double Corner(int i, int j)
        {
            long k = CornerIndex(i, j);
            if (_dense is not null)
            {
                if (_denseSet![k] != 0) return _dense[k];
                double v = f(g.X(i), g.Y(j));
                _dense[k] = v;
                _denseSet[k] = 1;
                return v;
            }
            if (_sparse.TryGetValue(k, out var s)) return s;
            double val = f(g.X(i), g.Y(j));
            _sparse[k] = val;
            return val;
        }

        private static bool Pos(double v) => v > 0;

        public void March()
        {
            int n = 0;
            foreach (var (i, j) in _leaves)
            {
                if ((++n & 1023) == 0) ct.ThrowIfCancellationRequested();
                double v00 = Corner(i, j), v10 = Corner(i + 1, j), v11 = Corner(i + 1, j + 1), v01 = Corner(i, j + 1);
                if (double.IsNaN(v00) || double.IsNaN(v10) || double.IsNaN(v11) || double.IsNaN(v01)) continue;
                int c = (Pos(v00) ? 1 : 0) | (Pos(v10) ? 2 : 0) | (Pos(v11) ? 4 : 0) | (Pos(v01) ? 8 : 0);
                if (c == 0 || c == 15)
                {
                    _flatLeaves.Add((i, j));
                    continue;
                }
                long e0 = HKey(i, j), e1 = VKey(i + 1, j), e2 = HKey(i, j + 1), e3 = VKey(i, j);
                switch (c)
                {
                    case 1: case 14: Add(e3, e0, i, j); break;
                    case 2: case 13: Add(e0, e1, i, j); break;
                    case 3: case 12: Add(e3, e1, i, j); break;
                    case 4: case 11: Add(e1, e2, i, j); break;
                    case 6: case 9: Add(e0, e2, i, j); break;
                    case 7: case 8: Add(e3, e2, i, j); break;
                    case 5:
                    case 10:
                    {
                        double centre = f(g.X(i) + 0.5 * g.Hx, g.Y(j) + 0.5 * g.Hy);
                        bool centrePos = Pos(centre);
                        // Case 5: 00 and 11 positive. Case 10: 10 and 01 positive.
                        bool joinPositive = centrePos;
                        if ((c == 5) == joinPositive)
                        {
                            // Corners 10 and 01 are cut off.
                            Add(e0, e1, i, j);
                            Add(e2, e3, i, j);
                        }
                        else
                        {
                            // Corners 00 and 11 are cut off.
                            Add(e3, e0, i, j);
                            Add(e1, e2, i, j);
                        }
                        break;
                    }
                }
            }
        }

        private long HKey(int i, int j) => CornerIndex(i, j) * 2;
        private long VKey(int i, int j) => CornerIndex(i, j) * 2 + 1;

        private void Add(long a, long b, int i, int j)
        {
            var pa = Crossing(a);
            var pb = Crossing(b);
            if (!pa.Valid || !pb.Valid) return;
            if (keep is not null)
            {
                var mid = new Point2(0.5 * (pa.P.X + pb.P.X), 0.5 * (pa.P.Y + pb.P.Y));
                if (!keep(mid.X, mid.Y)) return;
            }
            _segments.Add((a, b));
        }

        /// <summary>The zero crossing on an edge, refined by regula falsi (Illinois); invalid across poles.</summary>
        private (Point2 P, bool Valid) Crossing(long key)
        {
            if (_crossings.TryGetValue(key, out var hit)) return hit;
            long corner = key >> 1;
            int i = (int)(corner % (g.Nx + 1)), j = (int)(corner / (g.Nx + 1));
            bool vertical = (key & 1) == 1;
            int i2 = vertical ? i : i + 1, j2 = vertical ? j + 1 : j;
            double ax = g.X(i), ay = g.Y(j), bx = g.X(i2), by = g.Y(j2);
            double va = Corner(i, j), vb = Corner(i2, j2);
            double lo = 0, hi = 1, flo = va, fhi = vb;
            double t = va / (va - vb);
            int side = 0;
            for (int k = 0; k < 4; k++)
            {
                double ft = f(ax + t * (bx - ax), ay + t * (by - ay));
                if (double.IsNaN(ft) || ft == 0) break;
                if (Pos(ft) == Pos(flo))
                {
                    lo = t;
                    flo = ft;
                    if (side == -1) fhi *= 0.5;
                    side = -1;
                }
                else
                {
                    hi = t;
                    fhi = ft;
                    if (side == 1) flo *= 0.5;
                    side = 1;
                }
                double denom = flo - fhi;
                t = denom == 0 ? 0.5 * (lo + hi) : lo + (hi - lo) * (flo / denom);
                if (t <= lo || t >= hi) t = 0.5 * (lo + hi);
            }
            var p = new Point2(ax + t * (bx - ax), ay + t * (by - ay));
            double vp = f(p.X, p.Y);
            // A sign change through a pole (1/x) is not a zero: |F| grows instead of vanishing.
            bool valid = double.IsFinite(vp) && Math.Abs(vp) <= Math.Max(Math.Abs(va), Math.Abs(vb));
            var res = (p, valid);
            _crossings[key] = res;
            return res;
        }

        public void Stitch(ImplicitResult result, int member, bool dashed)
        {
            if (_segments.Count == 0) return;
            var byKey = new Dictionary<long, List<int>>(_segments.Count * 2);
            for (int s = 0; s < _segments.Count; s++)
            {
                var (a, b) = _segments[s];
                if (a == b) continue;
                Link(byKey, a, s);
                Link(byKey, b, s);
            }
            var used = new bool[_segments.Count];
            for (int s = 0; s < _segments.Count; s++)
            {
                if (used[s] || _segments[s].A == _segments[s].B) continue;
                used[s] = true;
                var keys = new LinkedList<long>();
                keys.AddLast(_segments[s].A);
                keys.AddLast(_segments[s].B);
                Extend(keys, byKey, used, forward: true);
                bool closed = keys.Count > 2 && keys.First!.Value == keys.Last!.Value;
                if (!closed) Extend(keys, byKey, used, forward: false);
                var pts = new List<Point2>(keys.Count);
                foreach (var k in keys) pts.Add(_crossings[k].P);
                if (closed) pts.RemoveAt(pts.Count - 1);
                double length = 0;
                for (int k = 1; k < pts.Count; k++) length += view.PixelDistance(pts[k - 1], pts[k]);
                if (length < 1.5)
                {
                    result.Points.Add(new Point2(pts.Average(q => q.X), pts.Average(q => q.Y)));
                    continue;
                }
                result.Curves.Add(new Polyline([.. pts], closed, member, dashed));
            }
        }

        private static void Link(Dictionary<long, List<int>> map, long key, int s)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>(2);
            list.Add(s);
        }

        private void Extend(LinkedList<long> keys, Dictionary<long, List<int>> byKey, bool[] used, bool forward)
        {
            while (true)
            {
                long end = forward ? keys.Last!.Value : keys.First!.Value;
                int next = -1;
                if (byKey.TryGetValue(end, out var list))
                    foreach (var s in list)
                        if (!used[s]) { next = s; break; }
                if (next < 0) return;
                used[next] = true;
                var (a, b) = _segments[next];
                long other = a == end ? b : a;
                if (forward) keys.AddLast(other); else keys.AddFirst(other);
                if (forward && other == keys.First!.Value) return; // closed loop
            }
        }

        /// <summary>Leaves where F may vanish without a sign change at the corners: tiny loops and touching zeros.</summary>
        public void ThinFeatures(ImplicitResult result)
        {
            if (fi is null || _flatLeaves.Count == 0 || _flatLeaves.Count > 6000) return;
            var found = new Dictionary<(int, int), (Point2 P, double V)>();
            foreach (var (i, j) in _flatLeaves)
            {
                ct.ThrowIfCancellationRequested();
                double c00 = Corner(i, j), c10 = Corner(i + 1, j), c11 = Corner(i + 1, j + 1), c01 = Corner(i, j + 1);
                double scale = Math.Max(Math.Max(Math.Abs(c00), Math.Abs(c10)), Math.Max(Math.Abs(c11), Math.Abs(c01)));
                bool cornerPos = Pos(c00);
                Point2 best = default;
                double bestV = double.PositiveInfinity;
                bool loop = false;
                Refine(g.X(i), g.Y(j), g.Hx, g.Hy, 0, cornerPos, ref best, ref bestV, ref loop);
                if (loop || (double.IsFinite(bestV) && bestV <= 0.02 * scale)) found[(i, j)] = (best, loop ? 0 : bestV);
            }
            // One point per cluster of neighbouring leaves.
            var seen = new HashSet<(int, int)>();
            foreach (var start in found.Keys)
            {
                if (!seen.Add(start)) continue;
                var queue = new Queue<(int, int)>();
                queue.Enqueue(start);
                var bestP = found[start];
                while (queue.Count > 0)
                {
                    var (ci, cj) = queue.Dequeue();
                    var cur = found[(ci, cj)];
                    if (cur.V < bestP.V) bestP = cur;
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            var nb = (ci + di, cj + dj);
                            if (found.ContainsKey(nb) && seen.Add(nb)) queue.Enqueue(nb);
                        }
                }
                result.Points.Add(bestP.P);
            }
        }

        private void Refine(double x, double y, double w, double h, int depth, bool cornerPos, ref Point2 best, ref double bestV, ref bool loop)
        {
            var v = fi!(new Interval(x, x + w), new Interval(y, y + h));
            if (!v.ContainsZero) return;
            if (depth == 4)
            {
                double cx = x + 0.5 * w, cy = y + 0.5 * h;
                double fv = f(cx, cy);
                if (double.IsNaN(fv)) return;
                if (Pos(fv) != cornerPos && fv != 0) loop = true;
                double a = Math.Abs(fv);
                if (a < bestV)
                {
                    bestV = a;
                    best = new Point2(cx, cy);
                }
                return;
            }
            double hw = 0.5 * w, hh = 0.5 * h;
            Refine(x, y, hw, hh, depth + 1, cornerPos, ref best, ref bestV, ref loop);
            Refine(x + hw, y, hw, hh, depth + 1, cornerPos, ref best, ref bestV, ref loop);
            Refine(x, y + hh, hw, hh, depth + 1, cornerPos, ref best, ref bestV, ref loop);
            Refine(x + hw, y + hh, hw, hh, depth + 1, cornerPos, ref best, ref bestV, ref loop);
        }

        public void Dispose()
        {
            if (_dense is not null) ArrayPool<double>.Shared.Return(_dense);
            if (_denseSet is not null) ArrayPool<byte>.Shared.Return(_denseSet);
            _dense = null;
            _denseSet = null;
        }
    }
}
