using Calcy.Cas.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Cas.Symbolic;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Solving;

/// <summary>
/// Systems of equations: linear systems exactly by Gaussian elimination over rational functions of the parameters
/// (no solution, a unique solution, or infinitely many with free parameters), nonlinear systems by substitution
/// (an equation in one unknown, or linear in one unknown), elimination with resultants for two polynomial equations,
/// and multivariate Newton from many starting points as the last resort.
/// </summary>
internal static class Systems
{
    /// <summary>The solution sets (each maps every unknown to a value), or null when the system could not be solved.</summary>
    public static List<Dictionary<string, Expr>>? Solve(List<Expr> equations, List<string> unknowns, string param)
    {
        var eqs = equations.Select(e => Simplifier.Cancel(e)).ToList();
        var linear = Linear(eqs, unknowns);
        if (linear is not null) return linear;
        var exact = Substitute(eqs, unknowns, param, 0);
        if (exact is not null)
        {
            var verified = exact.Where(s => Verify(equations, s)).ToList();
            return verified;
        }
        return Numeric(equations, unknowns);
    }

    // ------------------------------------------------------------------ linear systems

    private static List<Dictionary<string, Expr>>? Linear(List<Expr> eqs, List<string> unknowns)
    {
        var km = KernelMap.Build(eqs, [.. unknowns]);
        if (km is null) return null;
        int n = unknowns.Count;
        for (int i = n; i < km.Count; i++)
            if (unknowns.Any(u => km.KernelContains(i, u))) return null;
        var polys = new List<MPoly>();
        foreach (var e in eqs)
        {
            var r = km.ToRat(e);
            if (r is null) return null;
            // Denominators free of the unknowns can be cleared.
            for (int i = 0; i < n; i++)
                if (r.Den.Contains(i)) return null;
            var p = r.Num;
            foreach (var (m, _) in p.Terms)
            {
                int deg = 0;
                for (int i = 0; i < n; i++) deg += m[i];
                if (deg > 1) return null;
            }
            polys.Add(p);
        }
        int rows = polys.Count;
        // Augmented matrix of rational functions in the parameters: a·x = b.
        var M = new RatFunc[rows, n + 1];
        for (int r = 0; r < rows; r++)
        {
            for (int j = 0; j < n; j++) M[r, j] = RatFunc.FromPoly(polys[r].CoefficientOf(j, 1).CoefficientsInAllZero(n));
            M[r, n] = RatFunc.FromPoly(-polys[r].ConstantPart(n));
        }
        var pivots = new List<int>();
        int row = 0;
        for (int col = 0; col < n && row < rows; col++)
        {
            CasEnv.Check();
            int piv = -1;
            for (int r = row; r < rows; r++)
            {
                if (M[r, col].IsZero) continue;
                if (piv < 0 || (M[r, col].Num.IsConstant && M[r, col].Den.IsConstant && !(M[piv, col].Num.IsConstant && M[piv, col].Den.IsConstant))) piv = r;
            }
            if (piv < 0) continue;
            for (int j = 0; j <= n; j++) (M[row, j], M[piv, j]) = (M[piv, j], M[row, j]);
            var inv = M[row, col];
            for (int j = 0; j <= n; j++) M[row, j] = M[row, j] / inv;
            for (int r = 0; r < rows; r++)
            {
                if (r == row || M[r, col].IsZero) continue;
                var factor = M[r, col];
                for (int j = 0; j <= n; j++) M[r, j] = M[r, j] - factor * M[row, j];
            }
            pivots.Add(col);
            row++;
        }
        for (int r = row; r < rows; r++)
            if (!M[r, n].IsZero) return [];
        var free = Enumerable.Range(0, n).Where(c => !pivots.Contains(c)).ToList();
        var parameterNames = new Dictionary<int, Expr>();
        var avoid = eqs.ToArray();
        var used = new HashSet<string>(eqs.SelectMany(e => e.FreeSymbols()));
        int counter = 0;
        foreach (var c in free)
        {
            string name = free.Count == 1 && !used.Contains("t") ? "t" : NextName(used, ref counter);
            used.Add(name);
            parameterNames[c] = new SymbolExpr(name);
        }
        var solution = new Dictionary<string, Expr>();
        for (int i = 0; i < pivots.Count; i++)
        {
            int col = pivots[i];
            var value = km.ToExpr(M[i, n]);
            foreach (var c in free)
                value = Algebra.Sub(value, Algebra.Mul(km.ToExpr(M[i, c]), parameterNames[c]));
            solution[unknowns[col]] = Simplifier.Simplify(value, trig: false);
        }
        foreach (var c in free) solution[unknowns[c]] = parameterNames[c];
        return [solution];
    }

    private static string NextName(HashSet<string> used, ref int counter)
    {
        while (true)
        {
            counter++;
            var name = "t" + counter;
            if (!used.Contains(name)) return name;
        }
    }

    // ------------------------------------------------------------------ substitution

    private static List<Dictionary<string, Expr>>? Substitute(List<Expr> eqs, List<string> unknowns, string param, int depth)
    {
        using var guard = CasEnv.Deeper(60);
        if (depth > 8) return null;
        var live = new List<Expr>();
        foreach (var e in eqs)
        {
            var s = Simplifier.Cancel(e);
            if (unknowns.All(u => !s.ContainsSymbol(u)))
            {
                if (Simplifier.IsZero(s) || NearZero(s)) continue;
                if (s.FreeSymbols().Count == 0) return [];
                continue;
            }
            live.Add(s);
        }
        if (unknowns.Count == 0) return [new Dictionary<string, Expr>()];
        if (live.Count == 0) return null;

        // 1. An equation in a single unknown: solve it and branch.
        foreach (var e in live)
        {
            var present = unknowns.Where(u => e.ContainsSymbol(u)).ToList();
            if (present.Count != 1) continue;
            var u = present[0];
            var roots = new Solver(u, param).Solve(e);
            if (roots is null || roots.All) continue;
            if (roots.Values.Any(v => v.ContainsSymbol(param))) continue;
            return Branch(live, unknowns, u, roots.Values, param, depth);
        }
        // 2. An equation linear in some unknown: express it and substitute.
        foreach (bool constantCoefficient in new[] { true, false })
        {
            foreach (var e in live)
            {
                foreach (var u in unknowns)
                {
                    var p = ExprPoly.From(e, u, 2);
                    if (p is null || p.Degree != 1) continue;
                    var coef = p[1];
                    if (constantCoefficient && unknowns.Any(v => coef.ContainsSymbol(v))) continue;
                    if (Simplifier.IsZero(coef)) continue;
                    var value = Simplifier.Simplify(Algebra.Neg(Algebra.Div(p[0], coef)), trig: false);
                    var rest = live.Where(o => !ReferenceEquals(o, e)).Select(o => Simplifier.Cancel(CasEnv.Subst(o, u, value))).ToList();
                    var others = unknowns.Where(v => v != u).ToList();
                    var sub = Substitute(NumeratorsOf(rest), others, param, depth + 1);
                    if (sub is null) return null;
                    var result = new List<Dictionary<string, Expr>>();
                    foreach (var s in sub)
                    {
                        var full = new Dictionary<string, Expr>(s) { [u] = Simplifier.Simplify(CasEnv.Subst(value, s), trig: false) };
                        result.Add(full);
                    }
                    return result;
                }
            }
        }
        // 3. Two polynomial equations in two unknowns: eliminate with a resultant.
        if (live.Count >= 2 && unknowns.Count == 2)
        {
            var km = KernelMap.Build(live.Take(2), [.. unknowns]);
            if (km is not null && km.Count == 2)
            {
                var f = km.ToPoly(live[0]);
                var g = km.ToPoly(live[1]);
                if (f is not null && g is not null)
                {
                    var res = PolyTools.Resultant(f, g, 1);
                    if (!res.IsZero)
                    {
                        var xRoots = new Solver(unknowns[0], param).Solve(km.ToExpr(res));
                        if (xRoots is not null && !xRoots.All && xRoots.Values.All(v => v is NumberExpr { Value.IsExactRational: true }))
                            return Branch(live, unknowns, unknowns[0], xRoots.Values, param, depth);
                    }
                }
            }
        }
        return null;
    }

    /// <summary>A tiny residual left by a numeric root.</summary>
    private static bool NearZero(Expr e)
    {
        if (e.FreeSymbols().Count > 0 || !CasEnv.ContainsApprox(e)) return false;
        var n = CasEnv.Numeric(e);
        return n is { } v && v.IsFinite && v.AbsDouble() < 1e-9;
    }

    private static List<Expr> NumeratorsOf(List<Expr> eqs)
    {
        var result = new List<Expr>();
        foreach (var e in eqs)
        {
            var rat = Simplifier.Rational(e);
            result.Add(rat is null ? e : rat.Value.Km.ToExpr(rat.Value.R.Num));
        }
        return result;
    }

    private static List<Dictionary<string, Expr>>? Branch(List<Expr> eqs, List<string> unknowns, string u, List<Expr> values, string param, int depth)
    {
        var result = new List<Dictionary<string, Expr>>();
        var others = unknowns.Where(v => v != u).ToList();
        foreach (var value in values)
        {
            if (!Solver.IsRealValue(value)) continue;
            var rest = eqs.Select(e => CasEnv.TrySubst(e, u, value)).ToList();
            if (rest.Any(r => r is null)) continue;
            var sub = Substitute(NumeratorsOf(rest!), others, param, depth + 1);
            if (sub is null) return null;
            foreach (var s in sub)
            {
                var full = new Dictionary<string, Expr>(s) { [u] = value };
                result.Add(full);
            }
        }
        return result;
    }

    private static bool Verify(List<Expr> equations, Dictionary<string, Expr> solution)
    {
        foreach (var e in equations)
        {
            var v = CasEnv.TrySubst(e, solution);
            if (v is null) return false;
            if (v.FreeSymbols().Count > 0) continue;
            var n = CasEnv.Numeric(v);
            if (n is null) continue;
            if (!n.Value.IsFinite || n.Value.AbsDouble() > 1e-7) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ numeric

    private static List<Dictionary<string, Expr>>? Numeric(List<Expr> equations, List<string> unknowns)
    {
        if (equations.Any(e => e.FreeSymbols().Any(s => !unknowns.Contains(s)))) return null;
        var fs = new List<Func<double[], double>>();
        foreach (var e in equations)
        {
            var f = NumericFunction.Compile(e, unknowns);
            if (f is null) return null;
            fs.Add(f);
        }
        int n = unknowns.Count;
        double[] grid = n <= 2 ? [-10, -3, -1, -0.3, 0.3, 1, 3, 10] : n == 3 ? [-5, -1, 0.5, 2, 6] : [-3, 0.5, 2];
        var found = new List<double[]>();
        var starts = new List<double[]>();
        void Gen(int i, double[] cur)
        {
            if (starts.Count > 600) return;
            if (i == n) { starts.Add((double[])cur.Clone()); return; }
            foreach (var g in grid)
            {
                cur[i] = g + 0.0137 * (i + 1);
                Gen(i + 1, cur);
            }
        }
        Gen(0, new double[n]);
        foreach (var s in starts)
        {
            var root = RootFinding.NewtonSystem([.. fs], s);
            if (root is null) continue;
            if (found.Any(r => r.Zip(root, (a, b) => Math.Abs(a - b)).Max() < 1e-6 * Math.Max(1, root.Max(Math.Abs)))) continue;
            found.Add(root);
            if (found.Count > 40) break;
        }
        if (found.Count == 0) return null;
        return [.. found.OrderBy(r => r[0]).Select(r =>
        {
            var d = new Dictionary<string, Expr>();
            for (int i = 0; i < n; i++) d[unknowns[i]] = new NumberExpr(Number.Approx(Math.Abs(r[i]) < 1e-13 ? 0 : r[i]));
            return d;
        })];
    }
}

internal static class MPolyLinearExtensions
{
    /// <summary>Drops the exponents of the first n variables (which are known to be zero here).</summary>
    public static MPoly CoefficientsInAllZero(this MPoly p, int n)
    {
        var acc = p;
        for (int i = 0; i < n; i++) acc = acc.CoefficientOf(i, 0);
        return acc;
    }

    /// <summary>The part of a linear polynomial without any of the first n variables.</summary>
    public static MPoly ConstantPart(this MPoly p, int n)
    {
        var acc = p;
        for (int i = 0; i < n; i++) acc = acc.CoefficientOf(i, 0);
        return acc;
    }
}
