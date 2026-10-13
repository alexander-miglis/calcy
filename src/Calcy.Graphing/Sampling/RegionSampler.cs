using Calcy.Core.Expressions;
using Calcy.Graphing.Numerics;

namespace Calcy.Graphing;

/// <summary>A condition of a region bound to parameter values, ready for sampling.</summary>
/// <param name="F">Point evaluation of the left side minus the right side.</param>
/// <param name="FI">Interval evaluation over boxes, or null.</param>
/// <param name="Reliable">False when the interval rule is only partial.</param>
/// <param name="Op">The comparison with zero.</param>
public sealed record BoundCondition(Func<double, double, double> F, Func<Interval, Interval, Interval>? FI, bool Reliable, RelOp Op)
{
    /// <summary>True for strict comparisons.</summary>
    public bool IsStrict => Op is RelOp.Less or RelOp.Greater;

    /// <summary>Tests the condition at a point (optionally counting the boundary as inside).</summary>
    public bool Holds(double x, double y, bool includeBoundary = false)
    {
        double v = F(x, y);
        if (double.IsNaN(v)) return false;
        if (includeBoundary && Math.Abs(v) <= 1e-9 * Math.Max(1, Math.Abs(v))) return true;
        return Op switch
        {
            RelOp.Less => v < 0,
            RelOp.LessEqual => v <= 0,
            RelOp.Greater => v > 0,
            RelOp.GreaterEqual => v >= 0,
            _ => false,
        };
    }
}

/// <summary>The shaded cells and boundaries of an implicit inequality region.</summary>
public sealed class RegionResult
{
    /// <summary>Filled rectangles (whole quadtree cells proven inside, and boundary leaves whose centre is inside).</summary>
    public List<Rect2> Cells { get; } = [];

    /// <summary>Boundary curves (dashed for strict conditions), kept only where the other conditions hold.</summary>
    public List<Polyline> Boundaries { get; } = [];

    /// <summary>True when completeness cannot be guaranteed.</summary>
    public bool MayBeIncomplete { get; set; }
}

/// <summary>
/// Shades regions such as x² + y² &lt; 9 or 0 &lt; y &lt; x: quadtree cells proven inside by interval arithmetic are filled
/// whole, cells proven outside are dropped, and boundary cells are refined to the leaf size and tested at their centre.
/// </summary>
public static class RegionSampler
{
    /// <summary>Samples the region where every condition holds.</summary>
    public static RegionResult Sample(IReadOnlyList<BoundCondition> conditions, Viewport region, ViewTransform view, SamplingOptions? options = null,
        int member = 0, CancellationToken ct = default)
    {
        var o = options ?? SamplingOptions.Default;
        var result = new RegionResult();
        if (conditions.Count == 0) return result;
        var grid = LeafGrid.Create(region, view, o);
        bool reliable = conditions.All(c => c.FI is not null && c.Reliable);
        result.MayBeIncomplete = !reliable || grid.Coarsened;
        var leafRows = new Dictionary<int, List<int>>();
        int visits = 0;

        void Visit(int i0, int j0, int size)
        {
            if (i0 >= grid.Nx || j0 >= grid.Ny) return;
            if ((++visits & 1023) == 0) ct.ThrowIfCancellationRequested();
            int i1 = Math.Min(i0 + size, grid.Nx), j1 = Math.Min(j0 + size, grid.Ny);
            var truth = Truth.True;
            var bx = new Interval(grid.X(i0), grid.X(i1));
            var by = new Interval(grid.Y(j0), grid.Y(j1));
            foreach (var c in conditions)
            {
                var t = c.FI is null ? Truth.Maybe : IntervalCompiler.CompareWithZero(c.FI(bx, by), c.Op);
                if (!c.Reliable && t == Truth.False) t = Truth.Maybe;
                if (t == Truth.False) return;
                if (t == Truth.Maybe) truth = Truth.Maybe;
            }
            if (truth == Truth.True)
            {
                result.Cells.Add(new Rect2(grid.X(i0), grid.Y(j0), grid.X(i1), grid.Y(j1)));
                return;
            }
            if (size == 1)
            {
                double cx = grid.X(i0) + 0.5 * grid.Hx, cy = grid.Y(j0) + 0.5 * grid.Hy;
                foreach (var c in conditions)
                    if (!c.Holds(cx, cy)) return;
                if (!leafRows.TryGetValue(j0, out var row)) leafRows[j0] = row = [];
                row.Add(i0);
                return;
            }
            int h = size / 2;
            Visit(i0, j0, h);
            Visit(i0 + h, j0, h);
            Visit(i0, j0 + h, h);
            Visit(i0 + h, j0 + h, h);
        }

        const int block = 32;
        for (int bj = 0; bj < grid.Ny; bj += block)
            for (int bi = 0; bi < grid.Nx; bi += block)
                Visit(bi, bj, block);

        // Merge runs of leaves in each row into rectangles.
        foreach (var (j, cols) in leafRows)
        {
            cols.Sort();
            int start = cols[0], prev = cols[0];
            for (int k = 1; k <= cols.Count; k++)
            {
                if (k < cols.Count && cols[k] == prev + 1)
                {
                    prev = cols[k];
                    continue;
                }
                result.Cells.Add(new Rect2(grid.X(start), grid.Y(j), grid.X(prev + 1), grid.Y(j + 1)));
                if (k < cols.Count) start = prev = cols[k];
            }
        }

        // Boundaries: each condition's zero set, kept where all the other conditions hold.
        for (int k = 0; k < conditions.Count; k++)
        {
            var c = conditions[k];
            var others = conditions.Where((_, idx) => idx != k).ToArray();
            Func<double, double, bool>? keep = others.Length == 0 ? null : (x, y) => others.All(oc => oc.Holds(x, y, includeBoundary: true));
            var curves = ImplicitSampler.Sample(c.F, c.FI, c.Reliable, region, view, o, keep, member, c.IsStrict, ct);
            result.Boundaries.AddRange(curves.Curves);
            if (curves.MayBeIncomplete) result.MayBeIncomplete = true;
        }
        return result;
    }
}
