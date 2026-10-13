using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Graphing.Numerics;
using Calcy.Graphing.Symbolic;

namespace Calcy.Graphing;

/// <summary>What an analysis annotation draws.</summary>
public enum AnnotationKind
{
    /// <summary>A filled polygon (integral or area between curves).</summary>
    Shade,

    /// <summary>A full line through two points (tangent line).</summary>
    Line,

    /// <summary>A polyline (distance segment, arc being measured).</summary>
    Path,

    /// <summary>Marked points.</summary>
    Point,
}

/// <summary>Geometry drawn on the graph to show an analysis result.</summary>
/// <param name="Kind">How to draw it.</param>
/// <param name="Points">Polygon, line or path points in graph coordinates.</param>
/// <param name="ColorIndex">Graph ink slot (0 to 4).</param>
/// <param name="Label">Optional label near the first point.</param>
public sealed record GraphAnnotation(AnnotationKind Kind, Point2[] Points, int ColorIndex, string? Label = null);

/// <summary>The result of an analysis tool, ready for a card.</summary>
public sealed record AnalysisResult
{
    /// <summary>Card title, for example "∫ y1 from -1 to 2".</summary>
    public required string Title { get; init; }

    /// <summary>Card text, for example "= -3 (signed area)".</summary>
    public required string Text { get; init; }

    /// <summary>The main numeric value (NaN when there is none).</summary>
    public double Value { get; init; } = double.NaN;

    /// <summary>Further values (for example every x of an X-CAL).</summary>
    public IReadOnlyList<double> Values { get; init; } = [];

    /// <summary>The exact value when one is known.</summary>
    public Expr? Exact { get; init; }

    /// <summary>What to draw on the graph.</summary>
    public IReadOnlyList<GraphAnnotation> Annotations { get; init; } = [];

    /// <summary>True when the tool could not produce a value.</summary>
    public bool IsError { get; init; }
}

/// <summary>
/// Graph analysis tools (TI CALC menu, Casio G-Solve): value at x, x for a given y, definite integral with shading,
/// area between curves, slope and tangent line, arc length and distance.
/// </summary>
public static class GraphAnalysis
{
    private static string N(double v) => GraphFormat.Number(v, 6, true);

    private static Func<double, double>? Function(GraphItem item, double[] p, int member)
    {
        if (!item.IsFunction || member < 0 || member >= item.Members.Count) return null;
        return item.Members[member].Main?.Bind1(p);
    }

    private static AnalysisResult Fail(string title, string message) => new() { Title = title, Text = message, IsError = true };

    private static List<KeyValuePair<string, Expr>> Locals(GraphItem item, double[] p)
    {
        var list = new List<KeyValuePair<string, Expr>>();
        for (int i = 0; i < item.Parameters.Count && i < p.Length; i++) list.Add(new(item.Parameters[i], ExactForms.ExactNumber(p[i])));
        return list;
    }

    /// <summary>Y-CAL: the value of a function (or the point of a parametric or polar curve) at x (or t, θ).</summary>
    public static AnalysisResult ValueAt(GraphItem item, double[] p, double x, CalcSession? session = null, int member = 0)
    {
        string v = item.Variable ?? "x";
        string title = $"{item.Name} at {v} = {N(x)}";
        if (item.HasRange && member < item.Members.Count)
        {
            var m = item.Members[member];
            Point2 q;
            if (item.Kind == GraphKind.Polar)
            {
                double r = m.Main?.Eval(x, p) ?? double.NaN;
                q = new Point2(r * Math.Cos(x * item.AngleToRadians), r * Math.Sin(x * item.AngleToRadians));
            }
            else q = new Point2(m.X?.Eval(x, p) ?? double.NaN, m.Y?.Eval(x, p) ?? double.NaN);
            if (!q.IsFinite) return Fail(title, "undefined here");
            return new AnalysisResult
            {
                Title = title,
                Text = GraphFormat.Point(q.X, q.Y, 6, true),
                Value = q.Y,
                Annotations = [new GraphAnnotation(AnnotationKind.Point, [q], item.ColorIndex)],
            };
        }
        var f = Function(item, p, member);
        if (f is null) return Fail(title, "Value at x needs a function graph");
        double y = f(x);
        if (!double.IsFinite(y)) return Fail(title, "undefined here");
        Expr? exact = null;
        if (session is not null && item.Members[member].Main is { } main)
            exact = ExactForms.NiceValue(main.Expression, v, ExactForms.ExactNumber(x), session.Context(), Locals(item, p));
        string text = exact is null ? $"y = {N(y)}" : Exactly(exact, y, "y = ");
        return new AnalysisResult
        {
            Title = title,
            Text = text,
            Value = y,
            Exact = exact,
            Annotations =
            [
                new GraphAnnotation(AnnotationKind.Path, [new Point2(x, 0), new Point2(x, y)], item.ColorIndex),
                new GraphAnnotation(AnnotationKind.Point, [new Point2(x, y)], item.ColorIndex),
            ],
        };
    }

    private static string Exactly(Expr exact, double value, string prefix)
    {
        string e = GraphFormat.Exact(exact), d = N(value);
        return e == d ? prefix + e : $"{prefix}{e} ≈ {d}";
    }

    /// <summary>X-CAL: every x in [a, b] where the function equals <paramref name="y"/>.</summary>
    public static AnalysisResult SolveForX(GraphItem item, double[] p, double y, double a, double b, CalcSession? session = null, int member = 0)
    {
        string title = $"x where {item.Name} = {N(y)}";
        var f = Function(item, p, member);
        if (f is null) return Fail(title, "X-CAL needs a function graph");
        var xs = Roots(x => f(x) - y, a, b);
        if (xs.Count == 0) return Fail(title, "no solution in view");
        var texts = new List<string>();
        foreach (var x in xs.Take(8))
        {
            Expr? ex = null;
            if (session is not null && item.Members[member].Main is { } main)
                ex = ExactForms.FindExactZero(Algebra.Sub(main.Expression, ExactForms.ExactNumber(y)), item.Variable ?? "x", x, session.Context(), Locals(item, p), 8);
            texts.Add(ex is null ? N(x) : GraphFormat.Exact(ex));
        }
        string text = "x = " + string.Join(", ", texts) + (xs.Count > 8 ? ", …" : "");
        return new AnalysisResult
        {
            Title = title,
            Text = text,
            Value = xs[0],
            Values = xs,
            Annotations = [new GraphAnnotation(AnnotationKind.Point, [.. xs.Select(x => new Point2(x, y))], item.ColorIndex)],
        };
    }

    /// <summary>All roots of g in [a, b] (sign changes refined with Brent's method, poles rejected).</summary>
    public static List<double> Roots(Func<double, double> g, double a, double b, int samples = 2000)
    {
        var roots = new List<double>();
        if (!(b > a)) return roots;
        double h = (b - a) / samples;
        double x0 = a, g0 = g(a);
        for (int k = 1; k <= samples; k++)
        {
            double x1 = a + k * h, g1 = g(x1);
            if (double.IsFinite(g0) && double.IsFinite(g1))
            {
                if (g0 == 0) roots.Add(x0);
                else if (g1 != 0 && Math.Sign(g0) != Math.Sign(g1))
                {
                    var r = Solvers.BrentRoot(g, x0, x1);
                    if (r is double x && double.IsFinite(g(x)) && Math.Abs(g(x)) <= 1e-6 * Math.Max(Math.Abs(g0), Math.Abs(g1)) + 1e-12) roots.Add(x);
                }
            }
            x0 = x1;
            g0 = g1;
        }
        if (g0 == 0) roots.Add(x0);
        return roots;
    }

    /// <summary>The definite integral of a function from a to b, with the signed region to shade.</summary>
    public static AnalysisResult Integral(GraphItem item, double[] p, double a, double b, CalcSession? session = null, int member = 0, ViewTransform? view = null)
    {
        string title = $"∫ {item.Name} from {N(a)} to {N(b)}";
        var f = Function(item, p, member);
        if (f is null) return Fail(title, "∫ needs a function graph");
        var q = Quadrature.Integrate(f, a, b);
        if (double.IsNaN(q.Value)) return Fail(title, "the function is undefined somewhere in this range");
        Expr? exact = session is null ? null : CasIntegral(item, p, a, b, session, member);
        if (exact is null && session is not null)
        {
            var cand = ExactForms.Candidates(q.Value).FirstOrDefault();
            if (cand is not null && IsSimpleRational(cand, q.Value)) exact = cand;
        }
        string text = (exact is null ? $"= {N(q.Value)}" : Exactly(exact, q.Value, "= ")) + "  (signed area)";
        if (!q.Converged) text += ", may be inaccurate";
        return new AnalysisResult
        {
            Title = title,
            Text = text,
            Value = q.Value,
            Exact = exact,
            Annotations = [.. Region(f, null, Math.Min(a, b), Math.Max(a, b), view).Select(poly => new GraphAnnotation(AnnotationKind.Shade, poly, item.ColorIndex))],
        };
    }

    /// <summary>The area between two function graphs from a to b (the integral of |f - g|), with the region to shade.</summary>
    public static AnalysisResult AreaBetween(GraphItem first, double[] p1, GraphItem second, double[] p2, double a, double b, int member1 = 0, int member2 = 0, ViewTransform? view = null)
    {
        string title = $"Area between {first.Name} and {second.Name}";
        var f = Function(first, p1, member1);
        var g = Function(second, p2, member2);
        if (f is null || g is null) return Fail(title, "Area between curves needs two function graphs");
        if (a > b) (a, b) = (b, a);
        // Split at the crossings so |f - g| is smooth on each piece.
        var cuts = new List<double> { a };
        cuts.AddRange(Roots(x => f(x) - g(x), a, b, 1000).Where(x => x > a && x < b));
        cuts.Add(b);
        double total = 0;
        bool converged = true;
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            var q = Quadrature.Integrate(x => Math.Abs(f(x) - g(x)), cuts[i], cuts[i + 1]);
            if (double.IsNaN(q.Value)) return Fail(title, "a graph is undefined somewhere in this range");
            total += q.Value;
            converged &= q.Converged;
        }
        string text = $"= {N(total)}" + (converged ? "" : ", may be inaccurate");
        return new AnalysisResult
        {
            Title = title + $" from {N(a)} to {N(b)}",
            Text = text,
            Value = total,
            Annotations = [.. Region(f, g, a, b, view).Select(poly => new GraphAnnotation(AnnotationKind.Shade, poly, first.ColorIndex))],
        };
    }

    /// <summary>dy/dx at x0 and the tangent line there (exact when the derivative can be evaluated exactly).</summary>
    public static AnalysisResult Slope(GraphItem item, double[] p, double x0, CalcSession? session = null, int member = 0)
    {
        string title = $"d{item.Name}/dx at x = {N(x0)}";
        var f = Function(item, p, member);
        if (f is null) return Fail(title, "dy/dx needs a function graph");
        double y0 = f(x0);
        if (!double.IsFinite(y0)) return Fail(title, "undefined here");
        var m = item.Members[member];
        double slope;
        Expr? exact = null;
        string? symbolic = null;
        if (m.FirstDerivative is { } d)
        {
            slope = d.Eval(x0, p);
            if (session is not null)
            {
                exact = ExactForms.NiceValue(d.Expression, item.Variable ?? "x", ExactForms.ExactNumber(x0), session.Context(), Locals(item, p));
                symbolic = SymbolicAt(d.Expression, item.Variable ?? "x", x0, session.Context());
            }
        }
        else slope = Solvers.Derivative(f, x0).Value;
        if (!double.IsFinite(slope)) return Fail(title, "not differentiable here");
        string value = exact is null ? N(slope) : GraphFormat.Exact(exact);
        string text = symbolic is not null && symbolic != value ? $"= {symbolic} = {value}" : exact is null ? $"= {N(slope)}" : Exactly(exact, slope, "= ");
        double c = y0 - slope * x0;
        text += "\ntangent " + LineText(slope, c);
        return new AnalysisResult
        {
            Title = title,
            Text = text,
            Value = slope,
            Exact = exact,
            Annotations =
            [
                new GraphAnnotation(AnnotationKind.Line, [new Point2(x0, y0), new Point2(x0 + 1, y0 + slope)], item.ColorIndex),
                new GraphAnnotation(AnnotationKind.Point, [new Point2(x0, y0)], item.ColorIndex),
            ],
        };
    }

    /// <summary>"y = mx + c" with zero terms left out.</summary>
    public static string LineText(double m, double c)
    {
        if (Math.Abs(m) < 1e-12) return $"y = {N(c)}";
        string mx = Math.Abs(m - 1) < 1e-12 ? "x" : Math.Abs(m + 1) < 1e-12 ? $"{GraphFormat.Minus}x" : $"{N(m)}x";
        if (Math.Abs(c) < 1e-12 * Math.Max(1, Math.Abs(m))) return $"y = {mx}";
        return $"y = {mx} {(c < 0 ? GraphFormat.Minus : '+')} {N(Math.Abs(c))}";
    }

    /// <summary>The derivative at x0 with the parameters left as names (for example "a"), when that is short.</summary>
    private static string? SymbolicAt(Expr derivative, string variable, double x0, EvalContext ctx)
    {
        if (derivative.FreeSymbols().All(n => n == variable)) return null;
        try
        {
            var free = derivative.FreeSymbols().Where(n => n != variable).ToArray();
            var r = Evaluator.Eval(derivative, ctx.WithApproximate(false).WithFree(free).WithLocal(variable, ExactForms.ExactNumber(x0)));
            if (r.NodeCount() > 8 || r.FreeSymbols().Count == 0) return null;
            return GraphFormat.Exact(r);
        }
        catch (CalcException)
        {
            return null;
        }
    }

    /// <summary>Arc length of a function from a to b, or of a parametric or polar curve over [a, b] of its parameter.</summary>
    public static AnalysisResult ArcLength(GraphItem item, double[] p, double a, double b, int member = 0, ViewTransform? view = null)
    {
        string v = item.Variable ?? "x";
        string title = $"Length of {item.Name} for {v} from {N(a)} to {N(b)}";
        if (member < 0 || member >= item.Members.Count) return Fail(title, "nothing to measure");
        var m = item.Members[member];
        Func<double, double> speed;
        Func<double, Point2> point;
        if (item.IsFunction && m.Main is not null)
        {
            var f = m.Main.Bind1(p);
            var d = m.FirstDerivative?.Bind1(p);
            double h = Math.Max(1e-6, Math.Abs(b - a) / 400);
            speed = x =>
            {
                double s = d is not null ? d(x) : Solvers.Derivative(f, x, h).Value;
                return Math.Sqrt(1 + s * s);
            };
            point = x => new Point2(x, f(x));
        }
        else if (item.HasRange)
        {
            Func<double, Point2> pt;
            if (item.Kind == GraphKind.Polar && m.Main is not null)
            {
                var r = m.Main.Bind1(p);
                double k = item.AngleToRadians;
                pt = t => new Point2(r(t) * Math.Cos(t * k), r(t) * Math.Sin(t * k));
            }
            else if (m.X is not null && m.Y is not null)
            {
                var fx = m.X.Bind1(p);
                var fy = m.Y.Bind1(p);
                pt = t => new Point2(fx(t), fy(t));
            }
            else return Fail(title, "nothing to measure");
            double h = Math.Max(1e-6, Math.Abs(b - a) / 400);
            speed = t =>
            {
                double dx = Solvers.Derivative(u => pt(u).X, t, h).Value, dy = Solvers.Derivative(u => pt(u).Y, t, h).Value;
                return Math.Sqrt(dx * dx + dy * dy);
            };
            point = pt;
        }
        else return Fail(title, "Arc length needs a function, parametric or polar graph");
        var q = Quadrature.Integrate(speed, Math.Min(a, b), Math.Max(a, b), 1e-10, 1e-10, 2000);
        if (double.IsNaN(q.Value)) return Fail(title, "the curve is undefined somewhere in this range");
        int n = 200;
        var path = Enumerable.Range(0, n + 1).Select(i => point(Math.Min(a, b) + (Math.Abs(b - a)) * i / n)).Where(pt => pt.IsFinite).ToArray();
        return new AnalysisResult
        {
            Title = title,
            Text = $"= {N(q.Value)}" + (q.Converged ? "" : ", may be inaccurate"),
            Value = q.Value,
            Annotations = [new GraphAnnotation(AnnotationKind.Path, path, item.ColorIndex)],
        };
    }

    /// <summary>The straight-line distance between two points.</summary>
    public static AnalysisResult Distance(Point2 a, Point2 b, int colorIndex = 0)
    {
        double d = a.DistanceTo(b);
        return new AnalysisResult
        {
            Title = $"Distance {GraphFormat.Point(a.X, a.Y, 4, true)} to {GraphFormat.Point(b.X, b.Y, 4, true)}",
            Text = $"= {N(d)}",
            Value = d,
            Annotations =
            [
                new GraphAnnotation(AnnotationKind.Path, [a, b], colorIndex),
                new GraphAnnotation(AnnotationKind.Point, [a, b], colorIndex),
            ],
        };
    }

    /// <summary>Polygons between f and g (or the x-axis) over [a, b], one per defined piece.</summary>
    public static List<Point2[]> Region(Func<double, double> f, Func<double, double>? g, double a, double b, ViewTransform? view = null)
    {
        var polys = new List<Point2[]>();
        if (!(b > a)) return polys;
        int n = 600;
        var top = new List<Point2>();
        var bottom = new List<Point2>();
        void Flush()
        {
            if (top.Count >= 2)
            {
                bottom.Reverse();
                polys.Add([.. top, .. bottom]);
            }
            top.Clear();
            bottom.Clear();
        }
        double clampLo = double.NegativeInfinity, clampHi = double.PositiveInfinity;
        if (view is not null)
        {
            var ext = view.Viewport.Expand(1);
            clampLo = ext.YMin;
            clampHi = ext.YMax;
        }
        for (int i = 0; i <= n; i++)
        {
            double x = a + (b - a) * i / n;
            double y1 = f(x), y2 = g?.Invoke(x) ?? 0;
            if (!double.IsFinite(y1) || !double.IsFinite(y2))
            {
                Flush();
                continue;
            }
            top.Add(new Point2(x, Math.Clamp(y1, clampLo, clampHi)));
            bottom.Add(new Point2(x, Math.Clamp(y2, clampLo, clampHi)));
        }
        Flush();
        return polys;
    }

    private static Expr? CasIntegral(GraphItem item, double[] p, double a, double b, CalcSession session, int member)
    {
        if (!session.Functions.TryGet("integrate", out _) || item.Members[member].Main is not { } main) return null;
        try
        {
            var call = CallExpr.Of("integrate", main.Expression, new SymbolExpr(item.Variable ?? "x"), ExactForms.ExactNumber(a), ExactForms.ExactNumber(b));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var ctx = new EvalContext(session.Settings, session.Env, session.Functions, false, cts.Token).WithLocals(Locals(item, p));
            var r = Evaluator.Eval(call, ctx);
            return r.IsConstantValue() && CalcSession.IsNiceExact(r) && r is not CallExpr && !HasDecimal(r) ? r : null;
        }
        catch (Exception e) when (e is CalcException or ArithmeticException or OperationCanceledException)
        {
            return null;
        }
    }

    private static bool HasDecimal(Expr e) => e is NumberExpr { Value.IsExact: false } || e.Children.Any(HasDecimal);

    private static bool IsSimpleRational(Expr e, double v) =>
        e is NumberExpr { Value.IsExactRational: true } n && n.Value.ReQ.Den <= 12 && Math.Abs(n.Value.Re - v) <= 1e-10 * Math.Max(1, Math.Abs(v));
}
