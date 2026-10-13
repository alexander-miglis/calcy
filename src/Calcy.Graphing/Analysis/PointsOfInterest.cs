using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Graphing.Numerics;
using Calcy.Graphing.Symbolic;

namespace Calcy.Graphing;

/// <summary>Kinds of automatically found points.</summary>
public enum PoiKind
{
    /// <summary>f(x) = 0 (sign change or touching).</summary>
    Root,

    /// <summary>Local minimum.</summary>
    Minimum,

    /// <summary>Local maximum.</summary>
    Maximum,

    /// <summary>Where the graph meets the y-axis.</summary>
    YIntercept,

    /// <summary>Where two graphs meet.</summary>
    Intersection,

    /// <summary>Where the concavity changes.</summary>
    Inflection,
}

/// <summary>A point of interest on a graph, located numerically and given an exact form when the session can prove one.</summary>
public sealed record PointOfInterest
{
    /// <summary>What kind of point it is.</summary>
    public required PoiKind Kind { get; init; }

    /// <summary>x coordinate (refined numerically).</summary>
    public required double X { get; init; }

    /// <summary>y coordinate.</summary>
    public required double Y { get; init; }

    /// <summary>The graph the point lies on.</summary>
    public required GraphItem Item { get; init; }

    /// <summary>Family member index.</summary>
    public int Member { get; init; }

    /// <summary>The other graph of an intersection.</summary>
    public GraphItem? Other { get; init; }

    /// <summary>Exact x when proven (for example √2 or π/2).</summary>
    public Expr? ExactX { get; init; }

    /// <summary>Exact y when it reads well.</summary>
    public Expr? ExactY { get; init; }

    /// <summary>True for a root where the graph touches the axis without crossing it.</summary>
    public bool Touching { get; init; }

    /// <summary>A stable identity for selection (kind, items and rounded position).</summary>
    public string Id => $"{Kind}|{Item.Name}|{Other?.Name}|{Member}|{Math.Round(X, 6)}";

    /// <summary>Card title, for example "Root of y1" or "y1 meets y2".</summary>
    public string Title => Kind switch
    {
        PoiKind.Root => $"Root of {Item.Name}",
        PoiKind.Minimum => $"Minimum of {Item.Name}",
        PoiKind.Maximum => $"Maximum of {Item.Name}",
        PoiKind.YIntercept => $"y-intercept of {Item.Name}",
        PoiKind.Intersection => $"{Item.Name} meets {Other?.Name}",
        _ => $"Inflection of {Item.Name}",
    };

    /// <summary>Card text, for example "x = −√2 ≈ −1.414" or "(0, −2)".</summary>
    public string Text
    {
        get
        {
            string xd = GraphFormat.Number(X, 4, true), yd = GraphFormat.Number(Y, 4, true);
            if (Kind == PoiKind.Root)
            {
                if (ExactX is null) return $"x ≈ {xd}";
                string xe = GraphFormat.Exact(ExactX);
                return xe == xd ? $"x = {xe}" : $"x = {xe} ≈ {xd}";
            }
            string xs = ExactX is null ? xd : GraphFormat.Exact(ExactX);
            string ys = ExactY is null ? yd : GraphFormat.Exact(ExactY);
            return $"({xs}, {ys})";
        }
    }

    /// <summary>A plain decimal "(x, y)" for tooltips and screen readers.</summary>
    public string DecimalText => GraphFormat.Point(X, Y, 6, true);
}

/// <summary>Settings for <see cref="PointsOfInterestFinder"/>.</summary>
public sealed record PoiOptions
{
    /// <summary>Grid samples across the visible x range used to bracket candidates.</summary>
    public int Samples { get; init; } = 1600;

    /// <summary>Look for exact forms through the session.</summary>
    public bool Exact { get; init; } = true;

    /// <summary>Include inflection points.</summary>
    public bool Inflections { get; init; } = true;

    /// <summary>Include intersections between pairs of graphs.</summary>
    public bool Intersections { get; init; } = true;

    /// <summary>At most this many points per graph and kind.</summary>
    public int MaxPerKind { get; init; } = 40;

    /// <summary>The default settings.</summary>
    public static readonly PoiOptions Default = new();
}

/// <summary>Finds roots, extrema, y-intercepts, intersections and inflection points of the visible function graphs.</summary>
public static class PointsOfInterestFinder
{
    private sealed record Fn(GraphItem Item, GraphMember Member, double[] P, Func<double, double> F, Func<double, double>? D1, Func<double, double>? D2, CompiledExpr Main);

    /// <summary>
    /// Finds the points of interest of the visible function items inside <paramref name="view"/>'s x range.
    /// When <paramref name="only"/> is given, only that item's points (and its intersections) are returned.
    /// </summary>
    public static IReadOnlyList<PointOfInterest> Find(IReadOnlyList<GraphItem> items, ParameterSource parameters, Viewport view, CalcSession? session,
        PoiOptions? options = null, GraphItem? only = null, CancellationToken ct = default)
    {
        var o = options ?? PoiOptions.Default;
        var fns = new List<Fn>();
        foreach (var item in items)
        {
            if (!item.IsVisible || !item.IsFunction) continue;
            var p = parameters(item);
            foreach (var m in item.Members)
            {
                if (m.Main is null) continue;
                fns.Add(new Fn(item, m, p, m.Main.Bind1(p), m.FirstDerivative?.Bind1(p), m.SecondDerivative?.Bind1(p), m.Main));
            }
        }
        var result = new List<PointOfInterest>();
        double a = view.XMin, b = view.XMax;
        foreach (var fn in fns)
        {
            ct.ThrowIfCancellationRequested();
            if (only is not null && fn.Item != only) continue;
            result.AddRange(ForFunction(fn, a, b, o, session, ct));
        }
        if (o.Intersections)
        {
            for (int i = 0; i < fns.Count; i++)
                for (int j = i + 1; j < fns.Count; j++)
                {
                    if (fns[i].Item == fns[j].Item) continue;
                    if (only is not null && fns[i].Item != only && fns[j].Item != only) continue;
                    ct.ThrowIfCancellationRequested();
                    result.AddRange(Intersections(fns[i], fns[j], a, b, o, session));
                }
            foreach (var line in items.Where(it => it.IsVisible && it.Kind == GraphKind.VerticalLine))
            {
                var lp = parameters(line);
                foreach (var fn in fns)
                {
                    if (only is not null && fn.Item != only && line != only) continue;
                    foreach (var lm in line.Members)
                    {
                        double c = lm.Main?.Eval(lp) ?? double.NaN;
                        if (!(c >= a && c <= b)) continue;
                        double y = fn.F(c);
                        if (double.IsFinite(y)) result.Add(new PointOfInterest { Kind = PoiKind.Intersection, X = c, Y = y, Item = fn.Item, Member = fn.Member.Index, Other = line });
                    }
                }
            }
        }
        return Dedupe(result, b - a);
    }

    private static List<PointOfInterest> Dedupe(List<PointOfInterest> list, double span)
    {
        static int Rank(PoiKind k) => k switch
        {
            PoiKind.Root => 0,
            PoiKind.Intersection => 1,
            PoiKind.Minimum or PoiKind.Maximum => 2,
            PoiKind.YIntercept => 3,
            _ => 4,
        };
        var ordered = list.OrderBy(p => Rank(p.Kind)).ToList();
        var kept = new List<PointOfInterest>();
        double tol = Math.Max(1e-9, span * 1e-7);
        foreach (var p in ordered)
        {
            bool dup = kept.Any(k => Math.Abs(k.X - p.X) <= tol && Math.Abs(k.Y - p.Y) <= tol * 10 + 1e-9 &&
                                     (k.Kind == p.Kind || Rank(p.Kind) >= 3) && (k.Item == p.Item || k.Other == p.Item || k.Item == p.Other));
            if (!dup) kept.Add(p);
        }
        return [.. kept.OrderBy(p => p.X)];
    }

    private static IEnumerable<PointOfInterest> ForFunction(Fn fn, double a, double b, PoiOptions o, CalcSession? session, CancellationToken ct)
    {
        var list = new List<PointOfInterest>();
        int n = Math.Max(64, o.Samples);
        double span = b - a;
        double h = span / n;
        var xs = new double[n + 1];
        var ys = new double[n + 1];
        for (int k = 0; k <= n; k++)
        {
            xs[k] = a + k * h;
            ys[k] = fn.F(xs[k]);
        }
        double yScale = 1e-300;
        foreach (var y in ys) if (double.IsFinite(y)) yScale = Math.Max(yScale, Math.Abs(y));
        double delta = Math.Max(span * 1e-6, 1e-12);
        var exactCtx = ExactContext(session, fn.Item, fn.P);

        // Roots with a sign change.
        var roots = new List<double>();
        var touchingRoots = new HashSet<double>();
        for (int k = 0; k < n && roots.Count < o.MaxPerKind; k++)
        {
            double y0 = ys[k], y1 = ys[k + 1];
            if (!double.IsFinite(y0) || !double.IsFinite(y1)) continue;
            if (y0 == 0)
            {
                if (k > 0 && ys[k - 1] == 0) continue;
                roots.Add(xs[k]);
                if (k > 0 && Math.Sign(ys[k - 1]) == Math.Sign(y1) && y1 != 0) touchingRoots.Add(xs[k]);
                continue;
            }
            if (Math.Sign(y0) == Math.Sign(y1) || y1 == 0) continue;
            var r = Solvers.BrentRoot(fn.F, xs[k], xs[k + 1]);
            if (r is not double x) continue;
            double fx = fn.F(x);
            if (!double.IsFinite(fx) || Math.Abs(fx) > 1e-6 * Math.Max(Math.Abs(y0), Math.Abs(y1)) + 1e-12) continue;
            roots.Add(x);
        }
        if (ys[n] == 0 && double.IsFinite(ys[n])) roots.Add(xs[n]);

        // Extrema: sign changes of the derivative.
        var derivative = fn.D1 ?? (x => Solvers.Derivative(fn.F, x, span / 200).Value);
        var dvals = new double[n + 1];
        for (int k = 0; k <= n; k++) dvals[k] = derivative(xs[k]);
        int extrema = 0;
        for (int k = 0; k < n && extrema < o.MaxPerKind; k++)
        {
            ct.ThrowIfCancellationRequested();
            double d0 = dvals[k], d1 = dvals[k + 1];
            if (!double.IsFinite(d0) || !double.IsFinite(d1) || Math.Sign(d0) == Math.Sign(d1) || d0 == 0) continue;
            double x = Solvers.BrentRoot(derivative, xs[k], xs[k + 1]) ?? 0.5 * (xs[k] + xs[k + 1]);
            double fx = fn.F(x), fl = fn.F(x - delta), fr = fn.F(x + delta);
            if (!double.IsFinite(fx) || !double.IsFinite(fl) || !double.IsFinite(fr)) continue;
            bool isMax = d0 > 0;
            double tol = 1e-12 * Math.Max(1, Math.Abs(fx));
            bool ok = isMax ? fl <= fx + tol && fr <= fx + tol : fl >= fx - tol && fr >= fx - tol;
            // Continuity: neighbours must be close (rules out poles of the derivative such as 1/x²).
            double local = Math.Max(Math.Abs(fl - fx), Math.Abs(fr - fx));
            if (!ok || local > 1e-3 * yScale + 1e-9) continue;
            extrema++;
            var kind = isMax ? PoiKind.Maximum : PoiKind.Minimum;
            if (Math.Abs(fx) <= 1e-9 * yScale && !roots.Any(r => Math.Abs(r - x) <= 1e3 * delta))
            {
                list.Add(MakePoint(PoiKind.Root, x, 0, fn, exactCtx, o, fn.Main.Expression, touching: true));
            }
            var dExpr = fn.Member.FirstDerivative?.Expression;
            list.Add(MakePoint(kind, x, fx, fn, exactCtx, o, dExpr));
        }

        foreach (var r in roots) list.Add(MakePoint(PoiKind.Root, r, 0, fn, exactCtx, o, fn.Main.Expression, touching: touchingRoots.Contains(r)));

        // y-intercept.
        if (a <= 0 && b >= 0)
        {
            double y0 = fn.F(0);
            if (double.IsFinite(y0)) list.Add(MakePoint(PoiKind.YIntercept, 0, y0, fn, exactCtx, o, null, exactX: Expr.Zero));
        }

        // Inflections: sign changes of the second derivative.
        if (o.Inflections)
        {
            Func<double, double>? second = fn.D2;
            if (second is null && fn.D1 is not null) second = x => Solvers.Derivative(fn.D1, x, span / 200).Value;
            if (second is not null)
            {
                var s = new double[n + 1];
                for (int k = 0; k <= n; k++) s[k] = second(xs[k]);
                int count = 0;
                for (int k = 0; k < n && count < o.MaxPerKind; k++)
                {
                    double s0 = s[k], s1 = s[k + 1];
                    if (!double.IsFinite(s0) || !double.IsFinite(s1) || Math.Sign(s0) == Math.Sign(s1) || s0 == 0) continue;
                    var r = Solvers.BrentRoot(second, xs[k], xs[k + 1]);
                    if (r is not double x) continue;
                    double sx = second(x);
                    if (!double.IsFinite(sx) || Math.Abs(sx) > 1e-6 * Math.Max(Math.Abs(s0), Math.Abs(s1)) + 1e-12) continue;
                    double fx = fn.F(x), fl = fn.F(x - delta), fr = fn.F(x + delta);
                    if (!double.IsFinite(fx) || !double.IsFinite(fl) || !double.IsFinite(fr)) continue;
                    if (Math.Max(Math.Abs(fl - fx), Math.Abs(fr - fx)) > 1e-3 * yScale + 1e-9) continue;
                    count++;
                    list.Add(MakePoint(PoiKind.Inflection, x, fx, fn, exactCtx, o, fn.Member.SecondDerivative?.Expression));
                }
            }
        }
        return list;
    }

    private static IEnumerable<PointOfInterest> Intersections(Fn f1, Fn f2, double a, double b, PoiOptions o, CalcSession? session)
    {
        var list = new List<PointOfInterest>();
        int n = Math.Max(64, o.Samples);
        double h = (b - a) / n;
        Func<double, double> g = x => f1.F(x) - f2.F(x);
        var xs = new double[n + 1];
        var gs = new double[n + 1];
        double scale = 1e-300;
        for (int k = 0; k <= n; k++)
        {
            xs[k] = a + k * h;
            gs[k] = g(xs[k]);
            double y1 = f1.F(xs[k]);
            if (double.IsFinite(y1)) scale = Math.Max(scale, Math.Abs(y1));
        }
        var found = new List<double>();
        for (int k = 0; k < n && found.Count < o.MaxPerKind; k++)
        {
            double g0 = gs[k], g1 = gs[k + 1];
            if (!double.IsFinite(g0) || !double.IsFinite(g1)) continue;
            if (g0 == 0)
            {
                found.Add(xs[k]);
                continue;
            }
            if (Math.Sign(g0) == Math.Sign(g1) || g1 == 0)
            {
                // Touching: a local minimum of |g| that reaches zero.
                if (k > 0 && double.IsFinite(gs[k - 1]) && Math.Abs(g0) <= Math.Abs(gs[k - 1]) && Math.Abs(g0) <= Math.Abs(g1) && Math.Sign(gs[k - 1]) == Math.Sign(g0))
                {
                    var (xm, vm) = Solvers.BrentMinimize(x => Math.Abs(g(x)), xs[k - 1], xs[k + 1]);
                    if (Math.Abs(vm) <= 1e-9 * scale + 1e-12) found.Add(xm);
                }
                continue;
            }
            var r = Solvers.BrentRoot(g, xs[k], xs[k + 1]);
            if (r is not double x) continue;
            double gx = g(x);
            if (!double.IsFinite(gx) || Math.Abs(gx) > 1e-6 * Math.Max(Math.Abs(g0), Math.Abs(g1)) + 1e-12) continue;
            found.Add(x);
        }
        var ctx1 = ExactContext(session, f1.Item, f1.P, f2.Item, f2.P);
        Expr? diff = null;
        if (ctx1 is not null && o.Exact)
        {
            var v1 = f1.Item.Variable ?? "x";
            var v2 = f2.Item.Variable ?? "x";
            var e2 = v1 == v2 ? f2.Main.Expression : f2.Main.Expression.Map(e => e is SymbolExpr s && s.Name == v2 ? new SymbolExpr(v1) : e);
            diff = Algebra.Sub(f1.Main.Expression, e2);
        }
        foreach (var x in found)
        {
            double y = f1.F(x);
            if (!double.IsFinite(y) || !double.IsFinite(f2.F(x))) continue;
            list.Add(MakePoint(PoiKind.Intersection, x, y, f1, ctx1, o, diff) with { Other = f2.Item });
        }
        return list;
    }

    private sealed record ExactCtx(EvalContext Context, List<KeyValuePair<string, Expr>> Locals);

    private static ExactCtx? ExactContext(CalcSession? session, GraphItem item, double[] p, GraphItem? other = null, double[]? q = null)
    {
        if (session is null) return null;
        var locals = new List<KeyValuePair<string, Expr>>();
        for (int i = 0; i < item.Parameters.Count && i < p.Length; i++) locals.Add(new(item.Parameters[i], ExactForms.ExactNumber(p[i])));
        if (other is not null && q is not null)
            for (int i = 0; i < other.Parameters.Count && i < q.Length; i++)
                if (!locals.Any(l => l.Key == other.Parameters[i])) locals.Add(new(other.Parameters[i], ExactForms.ExactNumber(q[i])));
        return new ExactCtx(session.Context(), locals);
    }

    private static PointOfInterest MakePoint(PoiKind kind, double x, double y, Fn fn, ExactCtx? ctx, PoiOptions o, Expr? zeroOf,
        bool touching = false, Expr? exactX = null)
    {
        Expr? ex = exactX, ey = null;
        string variable = fn.Item.Variable ?? "x";
        if (ctx is not null && o.Exact)
        {
            try
            {
                if (ex is null && zeroOf is not null)
                    ex = ExactForms.FindExactZero(zeroOf, variable, x, ctx.Context, ctx.Locals);
                if (ex is not null)
                {
                    ey = kind == PoiKind.Root ? Expr.Zero : ExactForms.NiceValue(fn.Main.Expression, variable, ex, ctx.Context, ctx.Locals);
                    // Snap the decimal to the exact value (removes rounding noise such as 1e-17 instead of 0).
                    var exD = Approx(ex, ctx);
                    if (exD is double dx && Math.Abs(dx - x) <= 1e-7 * Math.Max(1, Math.Abs(x))) x = dx;
                    if (ey is not null && Approx(ey, ctx) is double dy && Math.Abs(dy - y) <= 1e-7 * Math.Max(1, Math.Abs(y))) y = dy;
                }
            }
            catch (Exception e) when (e is CalcException or ArithmeticException or InvalidOperationException)
            {
                ex = null;
                ey = null;
            }
        }
        if (kind == PoiKind.Root) y = 0;
        return new PointOfInterest { Kind = kind, X = x, Y = y, Item = fn.Item, Member = fn.Member.Index, ExactX = ex, ExactY = ey, Touching = touching };
    }

    private static double? Approx(Expr e, ExactCtx ctx)
    {
        try
        {
            var v = Evaluator.Eval(e, ctx.Context.WithApproximate());
            return v is NumberExpr { Value.IsReal: true } n ? n.Value.Re : null;
        }
        catch (CalcException)
        {
            return null;
        }
    }
}
