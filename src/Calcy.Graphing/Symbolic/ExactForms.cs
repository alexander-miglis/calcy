using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Graphing.Symbolic;

/// <summary>
/// Finds exact forms for numerically located points (roots, extrema, intersections). Candidates such as
/// integers, fractions, square roots, simple surds and multiples of π are proposed from the decimal value and
/// accepted only when the session proves them exactly (the equation evaluates to exactly 0).
/// When the registry has a <c>solve</c> function, its exact solutions are used first.
/// </summary>
public static class ExactForms
{
    private static readonly int[] SquareFree = [2, 3, 5, 6, 7, 10, 11, 13, 14, 15, 17, 19, 21];

    /// <summary>Exact expressions whose value is close to <paramref name="v"/>, simplest first.</summary>
    public static IEnumerable<Expr> Candidates(double v)
    {
        if (!double.IsFinite(v) || Math.Abs(v) > 1e9) yield break;
        double tol = 1e-9 * Math.Max(1, Math.Abs(v));
        double r = Math.Round(v);
        if (Math.Abs(v - r) <= tol) yield return Expr.Int((long)r);
        for (int q = 2; q <= 24; q++)
        {
            double p = Math.Round(v * q);
            if (Math.Abs(v * q - p) <= tol * q && Math.Abs(p) < 1e12) yield return Algebra.Q(new BigRational((long)p, q));
        }
        // ±√(p/q)
        double sq = v * v;
        for (int q = 1; q <= 24; q++)
        {
            double p = Math.Round(sq * q);
            if (p <= 0 || Math.Abs(sq * q - p) > 2 * tol * Math.Abs(v) * q) continue;
            double rootP = Math.Sqrt(p / q);
            if (Math.Abs(rootP - Math.Round(rootP)) < 1e-12) continue; // a rational, already proposed
            var root = Algebra.Sqrt(Algebra.Q(new BigRational((long)p, q)));
            yield return v < 0 ? Algebra.Neg(root) : root;
            break;
        }
        // Multiples of π
        double vp = v / Math.PI;
        for (int q = 1; q <= 12; q++)
        {
            double p = Math.Round(vp * q);
            if (p != 0 && Math.Abs(vp * q - p) <= tol * q)
            {
                yield return Algebra.Mul(Algebra.Q(new BigRational((long)p, q)), ConstantExpr.Pi);
                break;
            }
        }
        // (p + k√c)/d
        foreach (int c in SquareFree)
        {
            double sc = Math.Sqrt(c);
            for (int d = 1; d <= 12; d++)
            {
                for (int k = -12; k <= 12; k++)
                {
                    if (k == 0) continue;
                    double p = v * d - k * sc;
                    double pr = Math.Round(p);
                    if (Math.Abs(p - pr) <= tol * d * 4 && pr != 0)
                    {
                        yield return Algebra.Div(Algebra.Add(Expr.Int((long)pr), Algebra.Mul(Expr.Int(k), Algebra.Sqrt(Expr.Int(c)))), Expr.Int(d));
                        goto nextC;
                    }
                }
            }
        nextC:;
        }
    }

    /// <summary>
    /// Returns an exact x near <paramref name="approx"/> where the prepared expression <paramref name="f"/> is exactly 0, or null.
    /// <paramref name="locals"/> supplies exact values for the parameters (sliders).
    /// </summary>
    public static Expr? FindExactZero(Expr f, string variable, double approx, EvalContext ctx, IEnumerable<KeyValuePair<string, Expr>> locals, int maxCandidates = 12)
    {
        var localList = locals.ToList();
        string localsKey = string.Join(";", localList.Select(l => l.Key + "=" + l.Value));
        var key = (f, variable, localsKey, Math.Round(approx, 9));
        if (ZeroCache.TryGetValue(key, out var cached)) return cached;
        var baseCtx = ctx.WithApproximate(false).WithLocals(localList);
        Expr? found = TrySolve(f, variable, approx, baseCtx, localsKey);
        if (found is null)
        {
            int tried = 0;
            foreach (var cand in Candidates(approx))
            {
                if (++tried > maxCandidates) break;
                if (IsExactZero(f, variable, cand, baseCtx))
                {
                    found = cand;
                    break;
                }
            }
        }
        if (ZeroCache.Count > 4000) ZeroCache.Clear();
        ZeroCache[key] = found;
        return found;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Expr, string, string, double), Expr?> ZeroCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Expr, string, string), List<(Expr Value, double Approx)>> SolveCache = new();

    /// <summary>True when f evaluated at variable = value is exactly zero.</summary>
    public static bool IsExactZero(Expr f, string variable, Expr value, EvalContext ctx)
    {
        var r = TryEval(f, ctx.WithLocal(variable, value));
        if (r is NumberExpr { Value.IsExact: true, Value.IsZero: true }) return true;
        // Surds such as 1 + √2: the session does not expand powers, so check in Q(√c) when f is rational.
        var field = FieldValue(f, variable, value, ctx);
        return field is { IsZero: true };
    }

    private static QuadraticNumber? FieldValue(Expr f, string variable, Expr value, EvalContext ctx)
    {
        int c = QuadraticField.RadicandOf(value);
        if (c == 0) return null;
        var x = QuadraticField.Eval(value, c, null, null);
        if (x is null) return null;
        var fx = TryEval(f, ctx.WithFree(variable));
        return fx is null ? null : QuadraticField.Eval(fx, c, variable, x);
    }

    /// <summary>The exact value of f at variable = value when it reads well (a fraction, radical or π multiple), else null.</summary>
    public static Expr? NiceValue(Expr f, string variable, Expr value, EvalContext ctx, IEnumerable<KeyValuePair<string, Expr>> locals)
    {
        var withLocals = ctx.WithApproximate(false).WithLocals(locals);
        var field = FieldValue(f, variable, value, withLocals);
        if (field is not null)
        {
            int c = QuadraticField.RadicandOf(value);
            var fe = TryEval(QuadraticField.ToExpr(field.Value, c), withLocals);
            if (fe is not null) return fe;
        }
        var r = TryEval(f, withLocals.WithLocal(variable, value));
        if (r is null || !r.IsConstantValue() || ContainsDecimal(r) || ContainsNonReal(r)) return null;
        return CalcSession.IsNiceExact(r) && r.NodeCount() <= 14 ? r : null;
    }

    /// <summary>Exact rational for a slider value (1.5 becomes 3/2).</summary>
    public static Expr ExactNumber(double v)
    {
        if (!double.IsFinite(v)) return new NumberExpr(Number.Approx(v));
        var q = BigRational.Approximate(v, 10_000, 1e-12);
        if (Math.Abs(q.ToDouble() - v) > 1e-12 * Math.Max(1, Math.Abs(v))) q = BigRational.FromDouble(v);
        return Algebra.Q(q);
    }

    private static bool ContainsNonReal(Expr e) =>
        e is NumberExpr { Value.IsReal: false } || e.Children.Any(ContainsNonReal);

    private static bool ContainsDecimal(Expr e) =>
        e is NumberExpr { Value.IsExact: false } || e.Children.Any(ContainsDecimal);

    private static Expr? TryEval(Expr e, EvalContext ctx, int timeoutMs = 250)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            var timed = new EvalContext(ctx.Settings, ctx.Env, ctx.Functions, ctx.Approximate, cts.Token);
            timed = timed.WithLocals(ctx.Locals);
            return Evaluator.Eval(e, timed);
        }
        catch (Exception ex) when (ex is CalcException or ArithmeticException or OperationCanceledException or InvalidOperationException or InsufficientExecutionStackException)
        {
            return null;
        }
    }

    /// <summary>Exact solutions from the registry's solve (computed once per equation), the one nearest to approx.</summary>
    private static Expr? TrySolve(Expr f, string variable, double approx, EvalContext ctx, string localsKey)
    {
        if (!ctx.Functions.TryGet("solve", out _)) return null;
        var solutions = SolveCache.GetOrAdd((f, variable, localsKey), _ =>
        {
            var list = new List<(Expr, double)>();
            var call = CallExpr.Of("solve", new RelationExpr(RelOp.Equal, f, Expr.Zero), new SymbolExpr(variable));
            var r = TryEval(call, ctx.WithFree(variable), 600);
            if (r is null) return list;
            foreach (var value in Solutions(r, variable))
            {
                // Only exact answers: a decimal from a numeric fallback is no better than our own root.
                if (!value.IsConstantValue() || ContainsDecimal(value) || ContainsNonReal(value) || value.NodeCount() > 30) continue;
                var approxValue = TryEval(value, ctx.WithApproximate());
                if (approxValue is NumberExpr { Value.IsReal: true } n) list.Add((value, n.Value.Re));
            }
            return list;
        });
        if (SolveCache.Count > 500) SolveCache.Clear();
        Expr? best = null;
        double bestDist = double.PositiveInfinity;
        foreach (var (value, a) in solutions)
        {
            double dist = Math.Abs(a - approx);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = value;
            }
        }
        return bestDist <= 1e-7 * Math.Max(1, Math.Abs(approx)) ? best : null;
    }

    private static IEnumerable<Expr> Solutions(Expr r, string variable)
    {
        switch (r)
        {
            case ListExpr l:
                foreach (var item in l.Items)
                    foreach (var s in Solutions(item, variable))
                        yield return s;
                break;
            case LogicExpr { Op: LogicOp.Or } or:
                foreach (var item in or.Operands)
                    foreach (var s in Solutions(item, variable))
                        yield return s;
                break;
            case RelationExpr { Op: RelOp.Equal, Left: SymbolExpr s } rel when s.Name == variable:
                yield return rel.Right;
                break;
            case BoolExpr:
                break;
            default:
                if (r.IsConstantValue()) yield return r;
                break;
        }
    }
}
