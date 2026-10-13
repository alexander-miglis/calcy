using System.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Symbolic;

/// <summary>Polynomial utilities at the expression level.</summary>
internal static class PolyTools
{
    public static ExprPoly RequirePoly(Expr e, string x, string fn)
    {
        return ExprPoly.From(e, x) ?? throw new CalcException($"{fn}: {Core.Printing.ExprPrinter.ToText(e)} is not a polynomial in {x}");
    }

    /// <summary>Coefficients from the highest power down.</summary>
    public static Expr Coeffs(Expr e, string x)
    {
        var p = RequirePoly(e, x, "coeffs");
        if (p.IsZero) return new ListExpr([Expr.Zero]);
        return new ListExpr([.. Enumerable.Range(0, p.Degree + 1).Reverse().Select(i => Simplifier.Cancel(p[i]))]);
    }

    public static Expr Degree(Expr e, string? x)
    {
        if (x is not null)
        {
            var p = RequirePoly(e, x, "degree");
            return p.IsZero ? ConstantExpr.NegativeInfinity : Expr.Int(p.Degree);
        }
        var km = KernelMap.Build([e]);
        var poly = km?.ToPoly(e);
        if (km is null || poly is null || km.Kernels.Any(k => k is not SymbolExpr and not ConstantExpr))
            throw new CalcException($"degree: {Core.Printing.ExprPrinter.ToText(e)} is not a polynomial");
        if (poly.IsZero) return ConstantExpr.NegativeInfinity;
        // Constants such as π are coefficients, not variables.
        int best = 0;
        foreach (var (m, _) in poly.Terms)
        {
            int d = 0;
            for (int i = 0; i < km.Count; i++)
                if (km[i] is SymbolExpr) d += m[i];
            best = Math.Max(best, d);
        }
        return Expr.Int(best);
    }

    public static Expr LeadingCoefficient(Expr e, string x)
    {
        var p = RequirePoly(e, x, "lcoeff");
        return Simplifier.Cancel(p.Lc);
    }

    /// <summary>Groups terms by powers of x: a x + b x + c = (a + b)x + c.</summary>
    public static Expr Collect(Expr e, string x)
    {
        var p = ExprPoly.From(e, x);
        if (p is null) return e;
        var xs = new SymbolExpr(x);
        var terms = new List<Expr>();
        bool raw = false;
        for (int i = p.Degree; i >= 0; i--)
        {
            var c = Simplifier.Simplify(p[i], trig: false);
            if (c.IsZero) continue;
            var power = Algebra.Pow(xs, Expr.Int(i));
            if (i > 0 && c is AddExpr)
            {
                terms.Add(new MulExpr([c, power]));
                raw = true;
            }
            else terms.Add(Algebra.Mul(c, power));
        }
        if (terms.Count == 0) return Expr.Zero;
        if (terms.Count == 1) return terms[0];
        return raw ? new AddExpr([.. terms]) : Algebra.Add(terms);
    }

    public static (Expr Quotient, Expr Remainder) Divide(Expr a, Expr b, string x)
    {
        var pa = RequirePoly(a, x, "polyquo");
        var pb = RequirePoly(b, x, "polyquo");
        if (pb.IsZero) throw new CalcException("polyquo: division by the zero polynomial");
        var (q, r) = pa.DivRem(pb);
        return (Simplifier.Cancel(q.ToExpr()), Simplifier.Cancel(r.ToExpr()));
    }

    public static Expr Gcd(Expr a, Expr b)
    {
        var km = KernelMap.Build([a, b]) ?? throw new CalcException("polygcd: the arguments must be polynomials");
        var pa = km.ToPoly(a) ?? throw new CalcException("polygcd: the arguments must be polynomials");
        var pb = km.ToPoly(b) ?? throw new CalcException("polygcd: the arguments must be polynomials");
        if (pa.IsZero) return km.ToExpr(pb.Normalized());
        if (pb.IsZero) return km.ToExpr(pa.Normalized());
        var (ca, _) = pa.RationalContent();
        var (cb, _) = pb.RationalContent();
        var g = MPoly.Gcd(pa, pb);
        var content = new BigRational(BigInteger.GreatestCommonDivisor(ca.Num, cb.Num), Lcm(ca.Den, cb.Den));
        return km.ToExpr(g.Scale(content));
    }

    public static Expr Lcm(Expr a, Expr b)
    {
        var km = KernelMap.Build([a, b]) ?? throw new CalcException("polylcm: the arguments must be polynomials");
        var pa = km.ToPoly(a) ?? throw new CalcException("polylcm: the arguments must be polynomials");
        var pb = km.ToPoly(b) ?? throw new CalcException("polylcm: the arguments must be polynomials");
        if (pa.IsZero || pb.IsZero) return Expr.Zero;
        var (ca, na) = pa.RationalContent();
        var (cb, nb) = pb.RationalContent();
        var g = MPoly.Gcd(na, nb);
        var l = (na * nb).DivExact(g)!;
        var content = new BigRational(Lcm(ca.Num, cb.Num), BigInteger.GreatestCommonDivisor(ca.Den, cb.Den));
        return km.ToExpr(l.Scale(content));
    }

    private static BigInteger Lcm(BigInteger a, BigInteger b) =>
        a.IsZero || b.IsZero ? BigInteger.Zero : BigInteger.Abs(a / BigInteger.GreatestCommonDivisor(a, b) * b);

    /// <summary>Resultant of two polynomials in x (Sylvester determinant by fraction-free elimination).</summary>
    public static Expr Resultant(Expr f, Expr g, string x)
    {
        var km = KernelMap.Build([f, g], x) ?? throw new CalcException("resultant: the arguments must be polynomials");
        var pf = km.ToPoly(f) ?? throw new CalcException("resultant: the arguments must be polynomials");
        var pg = km.ToPoly(g) ?? throw new CalcException("resultant: the arguments must be polynomials");
        int v = km.IndexOf(x);
        if (km.Kernels.Skip(1).Any(k => k.ContainsSymbol(x)))
            throw new CalcException($"resultant: the arguments must be polynomials in {x}");
        return Simplifier.Simplify(km.ToExpr(Resultant(pf, pg, v)), trig: false);
    }

    public static MPoly Resultant(MPoly f, MPoly g, int v)
    {
        int n = f.Vars;
        int m = f.Degree(v), k = g.Degree(v);
        if (m < 0 || k < 0) return MPoly.Zero(n);
        if (m == 0) return f.Pow(Math.Max(k, 0));
        if (k == 0) return g.Pow(m);
        var fc = f.CoefficientsIn(v);
        var gc = g.CoefficientsIn(v);
        MPoly C(SortedDictionary<int, MPoly> c, int i) => c.TryGetValue(i, out var p) ? p : MPoly.Zero(n);
        int size = m + k;
        if (size > 40) throw new CasLimitException("The polynomials are too large for a resultant");
        var M = new MPoly[size, size];
        for (int i = 0; i < size; i++)
            for (int j = 0; j < size; j++) M[i, j] = MPoly.Zero(n);
        for (int i = 0; i < k; i++)
            for (int j = 0; j <= m; j++) M[i, i + j] = C(fc, m - j);
        for (int i = 0; i < m; i++)
            for (int j = 0; j <= k; j++) M[k + i, i + j] = C(gc, k - j);
        return Determinant(M, size);
    }

    /// <summary>Bareiss fraction-free determinant over Q[vars].</summary>
    public static MPoly Determinant(MPoly[,] M, int size)
    {
        int n = M[0, 0].Vars;
        var prev = MPoly.Constant(n, BigRational.One);
        int sign = 1;
        for (int k = 0; k < size - 1; k++)
        {
            CasEnv.Check();
            if (M[k, k].IsZero)
            {
                int swap = -1;
                for (int i = k + 1; i < size; i++)
                    if (!M[i, k].IsZero) { swap = i; break; }
                if (swap < 0) return MPoly.Zero(n);
                for (int j = 0; j < size; j++) (M[k, j], M[swap, j]) = (M[swap, j], M[k, j]);
                sign = -sign;
            }
            for (int i = k + 1; i < size; i++)
                for (int j = k + 1; j < size; j++)
                {
                    var num = M[i, j] * M[k, k] - M[i, k] * M[k, j];
                    M[i, j] = num.DivExact(prev) ?? throw new InvalidOperationException("Bareiss division was not exact");
                }
            prev = M[k, k];
        }
        var det = M[size - 1, size - 1];
        return sign < 0 ? -det : det;
    }

    /// <summary>Discriminant: (-1)^(n(n-1)/2) res(p, p') / lc(p).</summary>
    public static Expr Discriminant(Expr p, string x)
    {
        var km = KernelMap.Build([p], x) ?? throw new CalcException("discriminant: the argument must be a polynomial");
        var pp = km.ToPoly(p) ?? throw new CalcException("discriminant: the argument must be a polynomial");
        int v = km.IndexOf(x);
        if (km.Kernels.Skip(1).Any(k => k.ContainsSymbol(x)))
            throw new CalcException($"discriminant: the argument must be a polynomial in {x}");
        int n = pp.Degree(v);
        if (n < 1) throw new CalcException($"discriminant: the polynomial must have degree at least 1 in {x}");
        if (n == 1) return Expr.One;
        var res = Resultant(pp, pp.Diff(v), v);
        var lc = pp.LeadingCoefficient(v);
        var q = res.DivExact(lc);
        Expr value = q is not null ? km.ToExpr(q) : Algebra.Div(km.ToExpr(res), km.ToExpr(lc));
        if ((n * (n - 1) / 2) % 2 == 1) value = Algebra.Neg(value);
        return Simplifier.Simplify(value, trig: false);
    }

    /// <summary>a x² + b x + c written as a(x - h)² + k.</summary>
    public static Expr CompleteSquare(Expr e, string x)
    {
        var p = RequirePoly(e, x, "compsquare");
        if (p.Degree != 2) throw new CalcException($"compsquare: needs a quadratic in {x}, this has degree {p.Degree}");
        var a = p[2];
        var b = p[1];
        var c = p[0];
        var h = Simplifier.Cancel(Algebra.Div(b, Algebra.Mul(Expr.Two, a)));
        var k = Simplifier.Cancel(Algebra.Sub(c, Algebra.Div(Algebra.Mul(b, b), Algebra.Mul(Expr.Int(4), a))));
        var square = new PowExpr(Algebra.Add(new SymbolExpr(x), h), Expr.Two);
        var term = a.IsOne ? square : a is NumberExpr ? new MulExpr([a, square]) : Algebra.Mul(a, square);
        return k.IsZero ? term : new AddExpr([term, k]);
    }

    // ------------------------------------------------------------------ partial fractions

    /// <summary>Partial fraction decomposition in x, or null when the expression is not a rational function of x over Q.</summary>
    public static Expr? Apart(Expr e, string x)
    {
        var km = KernelMap.Build([e], x);
        if (km is null) return null;
        var r = km.ToRat(e);
        if (r is null) return null;
        if (r.IsPolynomial) return km.ToExpr(r);
        int v = km.IndexOf(x);
        if (r.Num.UsedVars().Any(u => u != v) || r.Den.UsedVars().Any(u => u != v)) return null;
        var parts = Apart(r.Num.ToQPoly(v), r.Den.ToQPoly(v));
        if (parts is null) return null;
        var xs = new SymbolExpr(x);
        Expr P(QPoly q) => km.ToExpr(MPoly.FromQPoly(km.Count, v, q));
        var terms = new List<Expr> { P(parts.Value.Polynomial) };
        foreach (var (numer, denBase, power) in parts.Value.Fractions)
            terms.Add(Algebra.Mul(P(numer), Algebra.Pow(P(denBase), Expr.Int(-power))));
        return Algebra.Add(terms);
    }

    /// <summary>N/D = polynomial + Σ numerator / base^power with deg numerator &lt; deg base and irreducible integer bases.</summary>
    public static (QPoly Polynomial, List<(QPoly Numerator, QPoly Base, int Power)> Fractions)? Apart(QPoly num, QPoly den)
    {
        if (den.IsZero) throw new DivideByZeroException();
        var (q, rem) = num.DivRem(den);
        var fractions = new List<(QPoly, QPoly, int)>();
        if (rem.IsZero) return (q, fractions);
        var (_, factors) = UniFactor.FactorQ(den);
        if (factors.Sum(f => f.Multiplicity * f.Factor.Degree) > 200) return null;
        foreach (var (zf, e) in factors)
        {
            CasEnv.Check();
            var d = zf.ToQ();
            var P = d.Pow(e);
            var (R, rr) = den.DivRem(P);
            if (!rr.IsZero) return null;
            var (g, s, t) = QPoly.ExtendedGcd(P, R);
            if (g.Degree != 0) return null;
            var Ni = (rem * t).Rem(P);
            var temp = Ni;
            for (int j = 0; j < e; j++)
            {
                var (qq, cj) = temp.DivRem(d);
                if (!cj.IsZero) fractions.Add((cj, d, e - j));
                temp = qq;
            }
        }
        return (q, fractions);
    }
}
