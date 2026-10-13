using System.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Cas.Symbolic;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Calculus;

/// <summary>
/// Closed forms of sums and products: polynomial sums (Faulhaber), geometric and arithmetico-geometric series,
/// telescoping rational terms, ζ(2k) series, and the series of e^x, -ln(1 - x) (so the alternating harmonic series);
/// products of constants, powers and linear factors (factorials, telescoping ratios). Returns null when unknown.
/// </summary>
internal static class Sums
{
    private static bool IsPosInf(Expr e) => e is ConstantExpr { Constant: ConstantKind.Infinity };

    public static Expr? Sum(Expr f, string k, Expr a, Expr b)
    {
        using var _ = CasEnv.Deeper(50);
        if (a is ConstantExpr { IsInfinite: true }) return null;
        if (b is ConstantExpr { Constant: ConstantKind.NegativeInfinity }) return null;
        if (a is NumberExpr { Value.IsExactInteger: true } an && b is NumberExpr { Value.IsExactInteger: true } bn && Number.CompareReal(bn.Value, an.Value) < 0)
            return Expr.Zero;
        if (!f.ContainsSymbol(k))
        {
            if (Simplifier.IsZero(f)) return Expr.Zero;
            if (IsPosInf(b)) return Divergent(f);
            return Simplifier.Simplify(Algebra.Mul(f, Algebra.Add(Algebra.Sub(b, a), Expr.One)));
        }
        return PolynomialSum(f, k, a, b)
            ?? GeometricSum(f, k, a, b)
            ?? RationalSum(f, k, a, b)
            ?? ExponentialSeries(f, k, a, b);
    }

    private static Expr? Divergent(Expr leading)
    {
        var s = CasEnv.SignOf(leading);
        return s switch
        {
            > 0 => ConstantExpr.Infinity,
            < 0 => ConstantExpr.NegativeInfinity,
            _ => null,
        };
    }

    // ------------------------------------------------------------------ polynomials

    private static Expr? PolynomialSum(Expr f, string k, Expr a, Expr b)
    {
        var p = ExprPoly.From(f, k, 60);
        if (p is null) return null;
        if (IsPosInf(b)) return Divergent(p.Lc);
        Expr S(Expr n)
        {
            var terms = new List<Expr>();
            for (int m = 0; m <= p.Degree; m++)
                if (!Simplifier.IsZero(p[m])) terms.Add(Algebra.Mul(p[m], PowerSum(m, n)));
            return Algebra.Add(terms);
        }
        var result = Expander.Expand(Algebra.Sub(S(b), S(Algebra.Sub(a, Expr.One))));
        return Nicest(result);
    }

    private static Expr Nicest(Expr result)
    {
        result = Simplifier.Cancel(result);
        if (result.FreeSymbols().Count == 0) return result;
        try
        {
            var factored = Factoring.Factor(result);
            if (Simplifier.Complexity(factored) <= Simplifier.Complexity(result)) return factored;
        }
        catch (CasLimitException) { }
        return result;
    }

    /// <summary>Σ_{j=1}^{n} j^m as a polynomial in n (Faulhaber's formula; m = 0 gives n).</summary>
    public static Expr PowerSum(int m, Expr n)
    {
        var B = Bernoulli(m);
        var terms = new List<Expr>();
        for (int j = 0; j <= m; j++)
        {
            var bj = j == 1 ? BigRational.Half : B[j];
            if (bj.IsZero) continue;
            var c = new BigRational(IntegerMath.Binomial(m + 1, j)) * bj / (m + 1);
            terms.Add(Algebra.Mul(Algebra.Q(c), Algebra.Pow(n, Expr.Int(m + 1 - j))));
        }
        return Algebra.Add(terms);
    }

    private static BigRational[] Bernoulli(int m)
    {
        var B = new BigRational[m + 1];
        B[0] = BigRational.One;
        for (int i = 1; i <= m; i++)
        {
            var s = BigRational.Zero;
            for (int j = 0; j < i; j++) s += new BigRational(IntegerMath.Binomial(i + 1, j)) * B[j];
            B[i] = -s / (i + 1);
        }
        return B;
    }

    // ------------------------------------------------------------------ geometric

    /// <summary>f = c·P(k)·r^k with P a polynomial: geometric and arithmetico-geometric sums.</summary>
    private static Expr? GeometricSum(Expr f, string k, Expr a, Expr b)
    {
        var factors = f is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [f];
        Expr r = Expr.One, c = Expr.One;
        var poly = new List<Expr>();
        bool any = false;
        foreach (var t in factors)
        {
            if (!t.ContainsSymbol(k)) { c = Algebra.Mul(c, t); continue; }
            if (t is PowExpr p && !p.Base.ContainsSymbol(k) && Integrator.Linear(p.Exponent, k) is (var alpha, var beta))
            {
                r = Algebra.Mul(r, Algebra.Pow(p.Base, alpha));
                c = Algebra.Mul(c, Algebra.Pow(p.Base, beta));
                any = true;
                continue;
            }
            if (ExprPoly.From(t, k, 20) is not null) { poly.Add(t); continue; }
            return null;
        }
        if (!any) return null;
        var P = ExprPoly.From(Algebra.Mul(poly), k, 20);
        if (P is null) return null;
        if (Simplifier.IsZero(Algebra.Sub(r, Expr.One))) return PolynomialSum(Algebra.Mul(c, Algebra.Mul(poly)), k, a, b);
        bool infinite = IsPosInf(b);
        if (infinite)
        {
            var rv = CasEnv.Numeric(r);
            if (rv is null || r.FreeSymbols().Count > 0) return null;
            double mag = rv.Value.AbsDouble();
            if (mag >= 1)
            {
                if (rv.Value.IsReal && rv.Value.Re >= 1) return Divergent(Algebra.Mul(c, P.Lc));
                return ConstantExpr.Undefined;
            }
        }
        // S0(ρ) = Σ_{k=a}^{b} ρ^k, then Σ k^m ρ^k = (ρ d/dρ)^m S0.
        var rho = CasEnv.Temp();
        Expr S0 = infinite
            ? Algebra.Div(Algebra.Pow(rho, a), Algebra.Sub(Expr.One, rho))
            : Algebra.Div(Algebra.Sub(Algebra.Pow(rho, a), Algebra.Pow(rho, Algebra.Add(b, Expr.One))), Algebra.Sub(Expr.One, rho));
        var terms = new List<Expr>();
        Expr current = S0;
        for (int j = 0; j <= P.Degree; j++)
        {
            if (j > 0) current = Algebra.Mul(rho, Differentiator.D(current, rho.Name));
            if (!Simplifier.IsZero(P[j])) terms.Add(Algebra.Mul(P[j], current));
        }
        var total = CasEnv.Subst(Algebra.Mul(c, Algebra.Add(terms)), rho.Name, r);
        return Simplifier.Simplify(total, trig: false);
    }

    // ------------------------------------------------------------------ rational terms

    private static readonly Dictionary<int, Expr> Zeta = new()
    {
        [2] = Algebra.Div(Algebra.Pow(ConstantExpr.Pi, Expr.Int(2)), Expr.Int(6)),
        [4] = Algebra.Div(Algebra.Pow(ConstantExpr.Pi, Expr.Int(4)), Expr.Int(90)),
        [6] = Algebra.Div(Algebra.Pow(ConstantExpr.Pi, Expr.Int(6)), Expr.Int(945)),
        [8] = Algebra.Div(Algebra.Pow(ConstantExpr.Pi, Expr.Int(8)), Expr.Int(9450)),
        [10] = Algebra.Div(Algebra.Pow(ConstantExpr.Pi, Expr.Int(10)), Expr.Int(93555)),
    };

    private static Expr? RationalSum(Expr f, string k, Expr a, Expr b)
    {
        var km = KernelMap.Build([f], k);
        if (km is null || km.Count != 1) return null;
        var r = km.ToRat(f);
        if (r is null || r.IsPolynomial) return null;
        var parts = PolyTools.Apart(r.Num.ToQPoly(0), r.Den.ToQPoly(0));
        if (parts is null) return null;
        if (!parts.Value.Polynomial.IsZero) return null;
        bool infinite = IsPosInf(b);
        if (a is not NumberExpr { Value.IsExactInteger: true } aNum) return null;
        var aInt = aNum.Value.ToBigInteger();
        // Terms A/(k + c)^j with rational c.
        var terms = new List<(BigRational A, BigRational C, int J)>();
        foreach (var (num, bse, j) in parts.Value.Fractions)
        {
            if (bse.Degree != 1 || num.Degree > 0) return null;
            var p = bse[1];
            var q = bse[0];
            terms.Add((num[0] / p.Pow(j), q / p, j));
        }
        // Poles inside the range make the sum undefined.
        foreach (var (_, c, _) in terms)
            if (c.IsInteger && -c.Num >= aInt && (infinite || b is not NumberExpr bnum || -c.Num <= bnum.Value.ToBigInteger()))
                return ConstantExpr.Undefined;
        var result = new List<Expr>();
        foreach (var group in terms.GroupBy(t => (t.J, Frac(t.C))))
        {
            var items = group.ToList();
            var c0 = group.Key.Item2;
            int j = group.Key.J;
            var total = items.Aggregate(BigRational.Zero, (s, t) => s + t.A);
            if (!total.IsZero)
            {
                if (!infinite) return null;
                if (j == 1) return Divergent(Algebra.Q(total));
                if (!c0.IsZero || !Zeta.TryGetValue(j, out var zeta)) return null;
                // Σ_{k=a}^{∞} 1/(k + c)^j = ζ(j) - Σ_{m=1}^{a+c-1} 1/m^j for integer c.
                foreach (var (A, c, _) in items)
                {
                    var start = aInt + c.Num;
                    if (start < 1) return null;
                    Expr head = Expr.Zero;
                    for (var mm = BigInteger.One; mm < start; mm++) head = Algebra.Add(head, Algebra.Q(new BigRational(BigInteger.One, BigInteger.Pow(mm, j))));
                    result.Add(Algebra.Mul(Algebra.Q(A), Algebra.Sub(zeta, head)));
                }
                continue;
            }
            // Telescoping: shifts d_i = c_i - c0 are integers.
            var d = items.Select(t => (t.A, Shift: (int)(t.C - c0).Num)).ToList();
            int mmin = d.Min(t => t.Shift);
            foreach (var (A, shift) in d)
            {
                // lower part: -A Σ_{j=a+mmin}^{a+shift-1} 1/(j + c0)^J
                for (int jj = mmin; jj <= shift - 1; jj++)
                {
                    var den = Algebra.Add(a, Expr.Int(jj), Algebra.Q(c0));
                    result.Add(Algebra.Neg(Algebra.Mul(Algebra.Q(A), Algebra.Pow(den, Expr.Int(-j)))));
                }
                if (!infinite)
                {
                    // upper part: A Σ_{j=b+mmin+1}^{b+shift} 1/(j + c0)^J
                    for (int jj = mmin + 1; jj <= shift; jj++)
                    {
                        var den = Algebra.Add(b, Expr.Int(jj), Algebra.Q(c0));
                        result.Add(Algebra.Mul(Algebra.Q(A), Algebra.Pow(den, Expr.Int(-j))));
                    }
                }
            }
        }
        return Nicest(Simplifier.Simplify(Algebra.Add(result), trig: false));
    }

    private static BigRational Frac(BigRational c) => c - new BigRational(c.Floor());

    // ------------------------------------------------------------------ known power series

    /// <summary>Σ x^k/k! = e^x and Σ x^k/k = -ln(1 - x) (with initial terms removed when the sum starts later).</summary>
    private static Expr? ExponentialSeries(Expr f, string k, Expr a, Expr b)
    {
        if (!IsPosInf(b) || a is not NumberExpr { Value.IsExactInteger: true } an) return null;
        var start = an.Value.ToBigInteger();
        if (start < 0 || start > 50) return null;
        var factors = f is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [f];
        Expr r = Expr.One, c = Expr.One;
        bool factorial = false, reciprocalK = false;
        foreach (var t in factors)
        {
            if (!t.ContainsSymbol(k)) { c = Algebra.Mul(c, t); continue; }
            if (t is PowExpr { Base: CallExpr { Name: "factorial", Args.Length: 1 } fc, Exponent: NumberExpr { Value.IsMinusOne: true } } && fc.Args[0] is SymbolExpr fs && fs.Name == k)
            {
                factorial = true;
                continue;
            }
            if (t is PowExpr { Base: SymbolExpr ks, Exponent: NumberExpr { Value.IsMinusOne: true } } && ks.Name == k)
            {
                reciprocalK = true;
                continue;
            }
            if (t is PowExpr p && !p.Base.ContainsSymbol(k) && Integrator.Linear(p.Exponent, k) is (var alpha, var beta))
            {
                r = Algebra.Mul(r, Algebra.Pow(p.Base, alpha));
                c = Algebra.Mul(c, Algebra.Pow(p.Base, beta));
                continue;
            }
            return null;
        }
        if (factorial == reciprocalK) return null;
        Expr total;
        if (factorial) total = Algebra.Pow(ConstantExpr.E, r);
        else
        {
            if (start < 1) return null;
            var rv = CasEnv.Numeric(r);
            if (rv is null || r.FreeSymbols().Count > 0) return null;
            if (rv.Value.IsReal && rv.Value.Re >= 1) return Divergent(c);
            if (rv.Value.AbsDouble() > 1) return ConstantExpr.Undefined;
            total = Algebra.Neg(CasEnv.Fn("ln", Algebra.Sub(Expr.One, r)));
        }
        var lowest = factorial ? BigInteger.Zero : BigInteger.One;
        for (var j = lowest; j < start; j++)
        {
            var term = factorial
                ? Algebra.Div(Algebra.Pow(r, Algebra.Q(j)), Algebra.Q(IntegerMath.Factorial(j)))
                : Algebra.Div(Algebra.Pow(r, Algebra.Q(j)), Algebra.Q(j));
            total = Algebra.Sub(total, term);
        }
        return Simplifier.Simplify(Algebra.Mul(c, total), trig: false);
    }

    // ------------------------------------------------------------------ products

    public static Expr? Product(Expr f, string k, Expr a, Expr b)
    {
        using var _ = CasEnv.Deeper(50);
        if (IsPosInf(b) || a is ConstantExpr) return null;
        var count = Algebra.Add(Algebra.Sub(b, a), Expr.One);
        if (!f.ContainsSymbol(k)) return Simplifier.Simplify(Algebra.Pow(f, count));
        var factors = f is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [f];
        var result = new List<Expr>();
        var rational = new List<Expr>();
        foreach (var t in factors)
        {
            if (!t.ContainsSymbol(k)) { result.Add(Algebra.Pow(t, count)); continue; }
            if (t is PowExpr p && !p.Base.ContainsSymbol(k) && Integrator.Linear(p.Exponent, k) is (var alpha, var beta))
            {
                var sumK = Algebra.Div(Algebra.Mul(Algebra.Add(a, b), count), Expr.Two);
                result.Add(Algebra.Pow(p.Base, Algebra.Add(Algebra.Mul(alpha, sumK), Algebra.Mul(beta, count))));
                continue;
            }
            rational.Add(t);
        }
        if (rational.Count > 0)
        {
            var g = Algebra.Mul(rational);
            var km = KernelMap.Build([g], k);
            if (km is null || km.Count != 1) return null;
            var rf = km.ToRat(g);
            if (rf is null) return null;
            var (unitN, numF) = UniFactor.FactorQ(rf.Num.ToQPoly(0));
            var (unitD, denF) = UniFactor.FactorQ(rf.Den.ToQPoly(0));
            result.Add(Algebra.Pow(Algebra.Q(unitN / unitD), count));
            // Each linear factor p·k + q contributes p^count·Γ-ratio; collect factorial(b + c) powers.
            var factorials = new Dictionary<BigRational, int>();
            foreach (var (list, sign) in new[] { (numF, 1), (denF, -1) })
            {
                foreach (var (z, e) in list)
                {
                    if (z.Degree != 1) return null;
                    var p = new BigRational(z[1]);
                    var c = new BigRational(z[0]) / p;
                    if (!c.IsInteger) return null;
                    result.Add(Algebra.Pow(Algebra.Q(p), Algebra.Mul(Expr.Int(sign * e), count)));
                    // Π_{k=a}^{b} (k + c) = (b + c)! / (a + c - 1)!
                    if (a is not NumberExpr { Value.IsExactInteger: true } an) return null;
                    var low = an.Value.ToBigInteger() + c.Num - 1;
                    if (low < 0) return null;
                    factorials[c] = factorials.GetValueOrDefault(c) + sign * e;
                    result.Add(Algebra.Pow(Algebra.Q(new BigRational(IntegerMath.Factorial(low))), Expr.Int(-sign * e)));
                }
            }
            result.Add(FactorialRatio(b, factorials));
        }
        return Nicest(Simplifier.Simplify(Algebra.Mul(result), trig: false));
    }

    /// <summary>∏ (b + c)!^e, cancelling factorials whose arguments differ by integers.</summary>
    private static Expr FactorialRatio(Expr b, Dictionary<BigRational, int> powers)
    {
        var active = powers.Where(kv => kv.Value != 0).ToList();
        if (active.Count == 0) return Expr.One;
        var cmin = active.Min(kv => kv.Key);
        int baseExp = active.Sum(kv => kv.Value);
        var parts = new List<Expr>();
        if (baseExp != 0) parts.Add(Algebra.Pow(CasEnv.Fn("factorial", Algebra.Add(b, Algebra.Q(cmin))), Expr.Int(baseExp)));
        foreach (var (c, e) in active)
        {
            // (b + c)! = (b + cmin)! · Π_{j=cmin+1}^{c} (b + j)
            for (var j = cmin + 1; j <= c; j += 1)
                parts.Add(Algebra.Pow(Algebra.Add(b, Algebra.Q(j)), Expr.Int(e)));
        }
        return Algebra.Mul(parts);
    }
}
