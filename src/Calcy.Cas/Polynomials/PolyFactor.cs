using System.Numerics;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Polynomials;

/// <summary>
/// Factorization of multivariate polynomials over Q: rational content, monomial content, recursive content in each
/// variable (which also covers factoring by grouping), square-free decomposition, homogeneous polynomials by
/// dehomogenizing, and a Kronecker substitution with factor recombination for the remaining small cases.
/// </summary>
internal static class PolyFactor
{
    private const int MaxKroneckerDegree = 120;
    private const int MaxKroneckerTests = 20_000;

    /// <summary>p = unit · ∏ factor^multiplicity with normalized factors (primitive integer, positive leading coefficient).</summary>
    public static (BigRational Unit, List<(MPoly Factor, int Multiplicity)> Factors) Factor(MPoly p)
    {
        if (p.IsZero) throw new ArgumentException("Cannot factor zero");
        var factors = new List<(MPoly, int)>();
        var (content, prim) = p.RationalContent();
        if (prim.IsConstant) return (content * prim.ConstantValue, factors);
        for (int v = 0; v < prim.Vars; v++)
        {
            int k = prim.MinDegree(v);
            if (k <= 0) continue;
            factors.Add((MPoly.Var(prim.Vars, v), k));
            prim = prim.DivExact(MPoly.Var(prim.Vars, v, k))!;
        }
        FactorPrimitive(prim, 1, factors);
        return (content, Merge(factors));
    }

    private static List<(MPoly, int)> Merge(List<(MPoly F, int M)> list)
    {
        var merged = new List<(MPoly, int)>();
        foreach (var (f, m) in list)
        {
            if (f.IsConstant) continue;
            int idx = merged.FindIndex(t => t.Item1.StructurallyEquals(f));
            if (idx >= 0) merged[idx] = (f, merged[idx].Item2 + m);
            else merged.Add((f, m));
        }
        return merged;
    }

    private static void FactorPrimitive(MPoly p, int mult, List<(MPoly, int)> output)
    {
        using var _ = CasEnv.Deeper(100);
        p = p.Normalized();
        if (p.IsConstant) return;
        for (int v = 0; v < p.Vars; v++)
        {
            int k = p.MinDegree(v);
            if (k <= 0) continue;
            output.Add((MPoly.Var(p.Vars, v), k * mult));
            p = p.DivExact(MPoly.Var(p.Vars, v, k))!.Normalized();
        }
        if (p.IsConstant) return;
        var used = p.UsedVars();
        if (used.Count == 1)
        {
            int v = used[0];
            var (_, list) = UniFactor.FactorQ(p.ToQPoly(v));
            foreach (var (f, m) in list) output.Add((MPoly.FromZPoly(p.Vars, v, f), m * mult));
            return;
        }
        int main = used.OrderBy(v => p.Degree(v)).ThenBy(v => v).First();
        var cont = p.ContentIn(main);
        if (!cont.IsConstant)
        {
            FactorPrimitive(cont, mult, output);
            p = p.DivExact(cont)!.Normalized();
        }
        foreach (var (sf, m) in SquareFree(p, main))
            foreach (var irr in FactorSquareFree(sf))
                output.Add((irr, m * mult));
    }

    /// <summary>Yun's square-free decomposition with respect to variable v of a polynomial primitive in v.</summary>
    public static List<(MPoly Factor, int Multiplicity)> SquareFree(MPoly f, int v)
    {
        var result = new List<(MPoly, int)>();
        if (f.Degree(v) <= 0)
        {
            if (!f.IsConstant) result.Add((f.Normalized(), 1));
            return result;
        }
        var fp = f.Diff(v);
        var a = MPoly.Gcd(f, fp);
        var b = f.DivExact(a)!;
        var c = fp.DivExact(a)!;
        var d = c - b.Diff(v);
        int i = 1;
        while (b.Degree(v) > 0)
        {
            CasEnv.Check();
            var g = MPoly.Gcd(b, d);
            if (g.Degree(v) > 0) result.Add((g.Normalized(), i));
            b = b.DivExact(g)!;
            c = d.DivExact(g)!;
            d = c - b.Diff(v);
            i++;
            if (i > 1000) break;
        }
        return result;
    }

    private static List<MPoly> FactorSquareFree(MPoly f)
    {
        f = f.Normalized();
        var used = f.UsedVars();
        if (used.Count == 0) return [];
        if (used.Count == 1)
        {
            int v = used[0];
            var (_, z) = f.ToQPoly(v).ToPrimitive();
            return [.. UniFactor.FactorSquareFree(z).Select(g => MPoly.FromZPoly(f.Vars, v, g))];
        }
        foreach (var v in used)
            if (f.Degree(v) == 1 && f.ContentIn(v).IsConstant) return [f];
        if (f.IsHomogeneous)
        {
            var h = Homogeneous(f, used);
            if (h is not null) return h;
        }
        return Kronecker(f, used) ?? [f];
    }

    private static List<MPoly>? Homogeneous(MPoly f, List<int> used)
    {
        int w = used[^1];
        var g = f.Substitute(w, BigRational.One);
        var (_, parts) = Factor(g);
        var result = new List<MPoly>();
        var remaining = f;
        foreach (var (h, m) in parts)
        {
            var hh = Homogenize(h, w);
            for (int k = 0; k < m; k++)
            {
                var q = remaining.DivExact(hh);
                if (q is null) return null;
                remaining = q;
                result.Add(hh.Normalized());
            }
        }
        // Whatever is left is a constant times a power of w (lost when w was set to 1).
        int wDeg = remaining.Degree(w);
        if (wDeg > 0)
        {
            var q = remaining.DivExact(MPoly.Var(f.Vars, w, wDeg));
            if (q is null || !q.IsConstant) return null;
            for (int k = 0; k < wDeg; k++) result.Add(MPoly.Var(f.Vars, w));
        }
        else if (!remaining.IsConstant) return null;
        return result;
    }

    private static MPoly Homogenize(MPoly h, int w)
    {
        int d = h.TotalDegree;
        return MPoly.FromTerms(h.Vars, h.Terms.Select(t => (t.Mono.With(w, t.Mono[w] + d - t.Mono.Total), t.Coef)));
    }

    private static List<MPoly>? Kronecker(MPoly f, List<int> used)
    {
        int D = used.Max(v => f.Degree(v)) + 1;
        long bound = 0, place = 1;
        foreach (var v in used)
        {
            bound += f.Degree(v) * place;
            place *= D;
            if (bound > MaxKroneckerDegree || place > 1_000_000) return null;
        }
        var coeffs = new BigInteger[bound + 1];
        foreach (var (m, c) in f.Terms)
        {
            if (!c.IsInteger) return null;
            long e = 0, pl = 1;
            foreach (var v in used)
            {
                e += m[v] * pl;
                pl *= D;
            }
            coeffs[e] += c.Num;
        }
        var image = new ZPoly(coeffs);
        var (_, imageFactors) = UniFactor.FactorQ(image.ToQ());
        var items = new List<ZPoly>();
        foreach (var (z, m) in imageFactors)
            for (int k = 0; k < m; k++) items.Add(z);
        if (items.Count <= 1) return [f];

        var result = new List<MPoly>();
        var rest = f;
        int tests = 0;
        int s = 1;
        while (2 * s <= items.Count)
        {
            bool found = false;
            var tried = new HashSet<string>();
            foreach (var subset in Combinations(items.Count, s))
            {
                CasEnv.Check();
                var key = string.Join(",", subset.Select(i => items[i].ToString()).Order(StringComparer.Ordinal));
                if (!tried.Add(key)) continue;
                if (++tests > MaxKroneckerTests) return null;
                var prod = ZPoly.One;
                foreach (var i in subset) prod *= items[i];
                var cand = Decode(prod, used, D, f.Vars);
                if (cand.IsConstant) continue;
                var q = rest.DivExact(cand);
                if (q is null) continue;
                result.Add(cand.Normalized());
                rest = q;
                var remove = new HashSet<int>(subset);
                items = [.. items.Where((_, i) => !remove.Contains(i))];
                found = true;
                break;
            }
            if (!found) s++;
        }
        if (!rest.IsConstant) result.Add(rest.Normalized());
        return result;
    }

    private static MPoly Decode(ZPoly u, List<int> used, int D, int n)
    {
        var terms = new List<(Mono, BigRational)>();
        for (int e = 0; e <= u.Degree; e++)
        {
            if (u[e].IsZero) continue;
            var exps = new int[n];
            long rem = e;
            foreach (var v in used)
            {
                exps[v] = (int)(rem % D);
                rem /= D;
            }
            if (rem != 0) continue;
            terms.Add((new Mono(exps), new BigRational(u[e])));
        }
        return MPoly.FromTerms(n, terms);
    }

    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var idx = new int[k];
        for (int i = 0; i < k; i++) idx[i] = i;
        while (true)
        {
            yield return (int[])idx.Clone();
            int j = k - 1;
            while (j >= 0 && idx[j] == n - k + j) j--;
            if (j < 0) yield break;
            idx[j]++;
            for (int l = j + 1; l < k; l++) idx[l] = idx[l - 1] + 1;
        }
    }
}
