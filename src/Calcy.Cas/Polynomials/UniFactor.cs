using System.Numerics;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Polynomials;

/// <summary>
/// Factorization of univariate polynomials over the integers and rationals:
/// square-free decomposition, rational roots, then Berlekamp-Zassenhaus (Cantor-Zassenhaus modular factoring,
/// Hensel lifting and recombination of the lifted factors).
/// </summary>
internal static class UniFactor
{
    private const int MaxDegree = 400;
    private const int MaxRecombinationTests = 200_000;

    public static readonly ZPoly XPoly = new([BigInteger.Zero, BigInteger.One]);

    /// <summary>
    /// Factors f over Q: f = unit · ∏ factor^multiplicity, every factor a primitive integer polynomial with a positive
    /// leading coefficient that is irreducible over Q (best effort beyond the size limits).
    /// </summary>
    public static (BigRational Unit, List<(ZPoly Factor, int Multiplicity)> Factors) FactorQ(QPoly f)
    {
        if (f.IsZero) throw new ArgumentException("Cannot factor the zero polynomial");
        var result = new List<(ZPoly, int)>();
        if (f.Degree == 0) return (f[0], result);
        var (content, prim) = f.ToPrimitive();
        int k = 0;
        while (prim[k].IsZero) k++;
        if (k > 0)
        {
            result.Add((XPoly, k));
            prim = new ZPoly([.. prim.Coefficients.Skip(k)]);
        }
        if (prim.Degree > 0)
        {
            foreach (var (sf, m) in prim.ToQ().SquareFree())
            {
                var (_, z) = sf.ToPrimitive();
                foreach (var irr in FactorSquareFree(z)) result.Add((irr, m));
            }
        }
        return (content, Merge(result));
    }

    /// <summary>Factors over Z a primitive, square-free polynomial with positive leading coefficient.</summary>
    public static List<ZPoly> FactorSquareFree(ZPoly f)
    {
        var result = new List<ZPoly>();
        if (f.Degree <= 0) return result;
        if (f.Degree == 1)
        {
            result.Add(f.Primitive());
            return result;
        }
        var rest = f;
        if (rest[0].IsZero)
        {
            result.Add(XPoly);
            rest = rest.DivExact(XPoly)!;
        }
        foreach (var lin in RationalRootFactors(rest))
        {
            var q = rest.DivExact(lin);
            if (q is null) continue;
            result.Add(lin);
            rest = q;
            if (rest.Degree <= 1) break;
        }
        if (rest.Degree == 1) result.Add(rest.Primitive());
        else if (rest.Degree >= 2)
        {
            if (rest.Degree > MaxDegree) result.Add(rest.Primitive());
            else result.AddRange(Zassenhaus(rest.Primitive()));
        }
        return result;
    }

    private static List<(ZPoly, int)> Merge(List<(ZPoly F, int M)> list)
    {
        var merged = new List<(ZPoly, int)>();
        foreach (var (f, m) in list)
        {
            int idx = merged.FindIndex(t => t.Item1.Equals(f));
            if (idx >= 0) merged[idx] = (f, merged[idx].Item2 + m);
            else merged.Add((f, m));
        }
        return merged;
    }

    /// <summary>Linear factors q·x - p for the rational roots p/q (only when the end coefficients are small enough to enumerate).</summary>
    public static List<ZPoly> RationalRootFactors(ZPoly f)
    {
        var found = new List<ZPoly>();
        if (f.Degree < 1) return found;
        var a0 = BigInteger.Abs(f[0]);
        var an = BigInteger.Abs(f.Lc);
        if (a0.IsZero) return found;
        if (a0.GetBitLength() > 48 || an.GetBitLength() > 48) return found;
        var ps = IntegerMath.Divisors(a0);
        var qs = IntegerMath.Divisors(an);
        if ((long)ps.Count * qs.Count > 20_000) return found;
        var seen = new HashSet<BigRational>();
        var cur = f;
        foreach (var q in qs)
        {
            foreach (var p in ps)
            {
                CasEnv.Check();
                foreach (var sign in new[] { 1, -1 })
                {
                    var r = new BigRational(sign * p, q);
                    if (!seen.Add(r)) continue;
                    if (!cur.ToQ().Eval(r).IsZero) continue;
                    var lin = new ZPoly([-r.Num, r.Den]);
                    found.Add(lin);
                    var div = cur.DivExact(lin);
                    if (div is not null) cur = div;
                    if (cur.Degree < 1) return found;
                }
            }
        }
        return found;
    }

    // ------------------------------------------------------------------ Zassenhaus

    private static IEnumerable<long> OddPrimes()
    {
        for (long n = 3; n < 100_000; n += 2)
        {
            bool prime = true;
            for (long d = 3; d * d <= n; d += 2)
                if (n % d == 0) { prime = false; break; }
            if (prime) yield return n;
        }
    }

    private static List<ZPoly> Zassenhaus(ZPoly f)
    {
        int n = f.Degree;
        long bestP = 0;
        List<long[]>? best = null;
        int good = 0;
        foreach (var p in OddPrimes())
        {
            CasEnv.Check();
            if ((f.Lc % p).IsZero) continue;
            var fp = ModP.FromZ(f, p);
            if (fp.Length - 1 != n) continue;
            var g = ModP.Gcd(fp, ModP.Derivative(fp, p), p);
            if (g.Length != 1) continue;
            var facs = FactorModP(ModP.Monic(fp, p), p);
            if (facs.Count == 1) return [f];
            if (best is null || facs.Count < best.Count)
            {
                best = facs;
                bestP = p;
            }
            if (++good >= 5) break;
        }
        if (best is null) return [f];

        // Bound on the coefficients of lc·(any factor).
        BigInteger sumSq = BigInteger.Zero;
        foreach (var c in f.Coefficients) sumSq += c * c;
        var norm = IntegerMath.IntegerRoot(sumSq, 2) + 1;
        var bound = BigInteger.Abs(f.Lc) * (BigInteger.One << n) * norm;
        int k = 1;
        BigInteger mod = bestP;
        while (mod <= 2 * bound)
        {
            mod *= bestP;
            k++;
        }
        var lifted = HenselLift(f, best, bestP, k, mod);
        return Recombine(f, lifted, mod);
    }

    /// <summary>Monic irreducible factors over GF(p) of a monic square-free polynomial.</summary>
    internal static List<long[]> FactorModP(long[] f, long p)
    {
        var result = new List<long[]>();
        long[] x = [0, 1];
        var g = f;
        long[] h = x;
        int i = 0;
        var rng = new Random(20261013);
        while (g.Length - 1 >= 2 * (i + 1))
        {
            CasEnv.Check();
            i++;
            h = ModP.PowMod(h, p, g, p);
            var d = ModP.Gcd(ModP.Sub(h, x, p), g, p);
            if (d.Length > 1)
            {
                EqualDegree(d, i, p, rng, result);
                g = ModP.DivRem(g, d, p).Q;
                h = ModP.Rem(h, g, p);
            }
        }
        if (g.Length > 1) result.Add(ModP.Monic(g, p));
        return result;
    }

    private static void EqualDegree(long[] f, int d, long p, Random rng, List<long[]> output)
    {
        if (f.Length - 1 == d)
        {
            output.Add(ModP.Monic(f, p));
            return;
        }
        var exponent = (BigInteger.Pow(p, d) - 1) / 2;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            CasEnv.Check();
            var a = new long[f.Length - 1];
            for (int j = 0; j < a.Length; j++) a[j] = rng.NextInt64(p);
            a = ModP.Trim(a);
            if (a.Length <= 1) continue;
            var b = ModP.PowMod(a, exponent, f, p);
            var c = ModP.Gcd(ModP.Sub(b, [1], p), f, p);
            if (c.Length > 1 && c.Length < f.Length)
            {
                EqualDegree(c, d, p, rng, output);
                EqualDegree(ModP.DivRem(f, c, p).Q, d, p, rng, output);
                return;
            }
        }
        output.Add(ModP.Monic(f, p));
    }

    private static List<ZPoly> HenselLift(ZPoly f, List<long[]> factors, long p, int k, BigInteger mod)
    {
        var lcInv = IntegerMath.ModInverse(f.Lc, mod);
        var target = f.Scale(lcInv).Mod(mod);
        var lifted = new List<ZPoly>();
        for (int i = 0; i < factors.Count - 1; i++)
        {
            var g = factors[i];
            long[] h = [1];
            for (int j = i + 1; j < factors.Count; j++) h = ModP.Mul(h, factors[j], p);
            var (G, H) = LiftPair(target, g, h, p, k, mod);
            lifted.Add(G);
            target = H;
        }
        lifted.Add(target.Mod(mod));
        return lifted;
    }

    private static (ZPoly G, ZPoly H) LiftPair(ZPoly target, long[] g, long[] h, long p, int k, BigInteger mod)
    {
        var (_, s, t) = ModP.ExtendedGcd(g, h, p);
        var G = ModP.ToZ(g);
        var H = ModP.ToZ(h);
        BigInteger pj = p;
        for (int j = 1; j < k; j++)
        {
            CasEnv.Check();
            var diff = target - G * H;
            var e = new long[diff.Degree + 1];
            for (int i = 0; i <= diff.Degree; i++)
            {
                var q = BigInteger.DivRem(diff[i], pj, out _);
                var v = (long)BigInteger.Remainder(q, p);
                e[i] = v < 0 ? v + p : v;
            }
            var ee = ModP.Trim(e);
            if (ee.Length > 0)
            {
                var te = ModP.Mul(t, ee, p);
                var (q2, tau) = ModP.DivRem(te, g, p);
                var sigma = ModP.Add(ModP.Mul(s, ee, p), ModP.Mul(q2, h, p), p);
                G += ModP.ToZ(tau).Scale(pj);
                H += ModP.ToZ(sigma).Scale(pj);
            }
            pj *= p;
        }
        return (G.Mod(mod), H.Mod(mod));
    }

    private static List<ZPoly> Recombine(ZPoly f, List<ZPoly> lifted, BigInteger mod)
    {
        var result = new List<ZPoly>();
        var g = f;
        var pool = new List<ZPoly>(lifted);
        int s = 1;
        int tests = 0;
        while (2 * s <= pool.Count)
        {
            bool found = false;
            foreach (var subset in Combinations(pool.Count, s))
            {
                if (++tests > MaxRecombinationTests)
                {
                    result.Add(g);
                    return result;
                }
                CasEnv.Check();
                var prod = new ZPoly([g.Lc]);
                foreach (var idx in subset) prod = (prod * pool[idx]).Mod(mod);
                var cand = prod.SymmetricMod(mod).Primitive();
                if (cand.Degree < 1) continue;
                var q = g.DivExact(cand);
                if (q is null) continue;
                result.Add(cand);
                g = q.Primitive();
                var remove = new HashSet<int>(subset);
                pool = [.. pool.Where((_, i) => !remove.Contains(i))];
                found = true;
                break;
            }
            if (!found) s++;
        }
        if (g.Degree > 0) result.Add(g.Primitive());
        return result;
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
