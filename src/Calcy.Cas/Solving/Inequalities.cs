using Calcy.Cas.Numerics;
using Calcy.Cas.Symbolic;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Solving;

/// <summary>
/// One-variable inequalities by a sign chart: the critical points are the zeros of f = left - right and the points
/// where f is undefined (denominators, logarithm and root domains); the sign of f is tested on every interval.
/// </summary>
internal static class Inequalities
{
    private sealed record Point(Expr Value, double Approx);

    /// <summary>The solution set as relations joined with and/or, true, false, or null when unsupported.</summary>
    public static Expr? Solve(RelationExpr rel, string x)
    {
        var f = Algebra.Sub(rel.Left, rel.Right);
        if (f.FreeSymbols().Any(s => s != x)) return null;
        var xs = new SymbolExpr(x);
        var solver = new Solver(x, "⁣n");
        var zeros = solver.Solve(f);
        if (zeros is null) return null;
        if (zeros.All) return rel.Op is RelOp.Equal or RelOp.LessEqual or RelOp.GreaterEqual ? BoolExpr.True : BoolExpr.False;
        if (zeros.Values.Any(v => v.FreeSymbols().Count > 0)) return null;
        var points = new List<Point>();
        foreach (var v in zeros.Values)
        {
            if (!Solver.IsRealValue(v)) continue;
            double d = CasEnv.Real(v);
            if (double.IsFinite(d)) points.Add(new Point(v, d));
        }
        var domain = DomainPoints(f, x);
        if (domain is null) return null;
        points.AddRange(domain);
        points = Dedupe(points);
        var test = Tester(f, x);
        if (test is null) return null;

        if (rel.Op == RelOp.NotEqual)
        {
            var excluded = points.Where(p => !Defined(f, x, p.Value) || Satisfied(test(p.Approx), RelOp.Equal)).ToList();
            if (excluded.Count == 0) return BoolExpr.True;
            var parts = excluded.Select(p => (Expr)new RelationExpr(RelOp.NotEqual, xs, p.Value)).ToList();
            return parts.Count == 1 ? parts[0] : new LogicExpr(LogicOp.And, [.. parts]);
        }

        // Intervals between consecutive points; each piece records whether it holds.
        var pieces = new List<(double Lo, double Hi, bool Holds)>();
        var bounds = new List<double> { double.NegativeInfinity };
        bounds.AddRange(points.Select(p => p.Approx));
        bounds.Add(double.PositiveInfinity);
        var intervalHolds = new bool[bounds.Count - 1];
        for (int i = 0; i + 1 < bounds.Count; i++)
        {
            double t = TestPoint(bounds[i], bounds[i + 1]);
            intervalHolds[i] = Satisfied(test(t), rel.Op);
        }
        var pointHolds = points.Select(p => double.IsFinite(test(p.Approx)) && Satisfied(test(p.Approx), rel.Op) && Defined(f, x, p.Value)).ToArray();
        return Build(xs, points, intervalHolds, pointHolds);
    }

    private static List<Point> Dedupe(List<Point> points)
    {
        var sorted = points.OrderBy(p => p.Approx).ToList();
        var result = new List<Point>();
        foreach (var p in sorted)
        {
            if (result.Count > 0 && Math.Abs(result[^1].Approx - p.Approx) <= 1e-9 * Math.Max(1, Math.Abs(p.Approx)))
            {
                // Prefer the exact form.
                if (result[^1].Value is NumberExpr { Value.IsExact: false } && !(p.Value is NumberExpr { Value.IsExact: false })) result[^1] = p;
                continue;
            }
            result.Add(p);
        }
        return result;
    }

    /// <summary>True when f has a finite value at the exact point (not a pole or outside a logarithm's domain).</summary>
    private static bool Defined(Expr f, string x, Expr point)
    {
        var v = CasEnv.TrySubst(f, x, point);
        if (v is null) return false;
        var n = CasEnv.Numeric(v);
        return n is { } nv && nv.IsFinite && !nv.IsNaN && (nv.IsReal || Math.Abs(nv.Im) < 1e-12);
    }

    private static double TestPoint(double lo, double hi)
    {
        if (double.IsNegativeInfinity(lo) && double.IsPositiveInfinity(hi)) return 0.5;
        if (double.IsNegativeInfinity(lo)) return hi - Math.Max(1, Math.Abs(hi));
        if (double.IsPositiveInfinity(hi)) return lo + Math.Max(1, Math.Abs(lo));
        return 0.5 * (lo + hi);
    }

    private static bool Satisfied(double v, RelOp op)
    {
        if (double.IsNaN(v)) return false;
        double eps = 1e-12;
        return op switch
        {
            RelOp.Less => v < -eps,
            RelOp.LessEqual => v <= eps,
            RelOp.Greater => v > eps,
            RelOp.GreaterEqual => v >= -eps,
            RelOp.Equal => Math.Abs(v) <= eps,
            _ => Math.Abs(v) > eps,
        };
    }

    private static Func<double, double>? Tester(Expr f, string x)
    {
        var compiled = NumericFunction.Compile(f, x);
        if (compiled is not null) return compiled;
        return v =>
        {
            var r = CasEnv.TrySubst(f, x, new NumberExpr(Number.Approx(v)));
            if (r is null) return double.NaN;
            var n = CasEnv.Numeric(r);
            if (n is null || !n.Value.IsReal) return double.NaN;
            return n.Value.Re;
        };
    }

    /// <summary>Where the expression stops being defined: zeros of denominators and of log and even-root arguments.</summary>
    private static List<Point>? DomainPoints(Expr f, string x)
    {
        var result = new List<Point>();
        var targets = new List<Expr>();
        void Visit(Expr e)
        {
            switch (e)
            {
                case PowExpr { Exponent: NumberExpr ne } p when p.Base.ContainsSymbol(x) && (ne.Value.IsNegativeReal || !ne.Value.IsInteger):
                    targets.Add(p.Base);
                    break;
                case CallExpr { Name: "ln" or "log" or "log2" } c when c.Args[0].ContainsSymbol(x):
                    targets.Add(c.Args[0]);
                    break;
                case CallExpr { Name: "tan" or "sec" or "csc" or "cot" } when e.ContainsSymbol(x):
                    throw new CasLimitException("Periodic inequalities are not supported");
            }
            foreach (var ch in e.Children) Visit(ch);
        }
        try { Visit(f); }
        catch (CasLimitException) { return null; }
        foreach (var t in targets)
        {
            var zeros = new Solver(x, "⁣n").Solve(t);
            if (zeros is null) return null;
            if (zeros.All) continue;
            foreach (var v in zeros.Values)
            {
                if (v.FreeSymbols().Count > 0) return null;
                if (!Solver.IsRealValue(v)) continue;
                double d = CasEnv.Real(v);
                if (double.IsFinite(d)) result.Add(new Point(v, d));
            }
        }
        return result;
    }

    private static Expr Build(Expr x, List<Point> points, bool[] intervals, bool[] pointHolds)
    {
        // Walk left to right, merging intervals joined by included points.
        var parts = new List<Expr>();
        int n = points.Count;
        bool allTrue = intervals.All(b => b) && pointHolds.All(b => b);
        if (allTrue) return BoolExpr.True;
        if (!intervals.Any(b => b) && !pointHolds.Any(b => b)) return BoolExpr.False;
        // Whole line except some points.
        if (intervals.All(b => b))
            return Join(LogicOp.And, points.Where((_, i) => !pointHolds[i]).Select(p => (Expr)new RelationExpr(RelOp.NotEqual, x, p.Value)).ToList());
        int k = 0;
        while (k < intervals.Length)
        {
            if (!intervals[k])
            {
                // An isolated point that holds: x = p.
                if (k < n && pointHolds[k] && (k + 1 >= intervals.Length || !intervals[k + 1]))
                    parts.Add(new RelationExpr(RelOp.Equal, x, points[k].Value));
                k++;
                continue;
            }
            int start = k;
            bool leftClosed = start > 0 && pointHolds[start - 1];
            while (k + 1 < intervals.Length && intervals[k + 1] && pointHolds[k]) k++;
            int end = k;
            bool rightClosed = end < n && pointHolds[end];
            Expr? lower = start == 0 ? null : points[start - 1].Value;
            Expr? upper = end == n ? null : points[end].Value;
            if (lower is null && upper is null) parts.Add(BoolExpr.True);
            else if (lower is null) parts.Add(new RelationExpr(rightClosed ? RelOp.LessEqual : RelOp.Less, x, upper!));
            else if (upper is null) parts.Add(new RelationExpr(leftClosed ? RelOp.GreaterEqual : RelOp.Greater, x, lower));
            else parts.Add(new LogicExpr(LogicOp.And, [new RelationExpr(leftClosed ? RelOp.LessEqual : RelOp.Less, lower, x), new RelationExpr(rightClosed ? RelOp.LessEqual : RelOp.Less, x, upper)]));
            // Points strictly inside the merged run that do not hold are excluded already by the merge rule.
            k++;
        }
        return Join(LogicOp.Or, parts);
    }

    private static Expr Join(LogicOp op, List<Expr> parts) =>
        parts.Count == 0 ? (op == LogicOp.And ? BoolExpr.True : BoolExpr.False) : parts.Count == 1 ? parts[0] : new LogicExpr(op, [.. parts]);
}
