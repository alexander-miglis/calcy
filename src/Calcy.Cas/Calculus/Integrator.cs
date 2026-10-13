using System.Numerics;
using Calcy.Cas.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Calculus;

/// <summary>
/// Indefinite integration (radian semantics): a table of standard forms with linear inner functions, polynomials,
/// rational functions by partial fractions, integration by parts (tabular and LIATE), u-substitution, powers of
/// sin and cos, tan/sec reductions and square roots of quadratics. Every antiderivative is checked by
/// differentiating it and comparing with the integrand at sample points; unverified results are discarded.
/// </summary>
internal static class Integrator
{
    /// <summary>When set, receives the rules used by the successful path (for step-by-step explanations).</summary>
    [ThreadStatic] public static List<(string Rule, Expr Expr)>? Log;

    private static void Trace(string rule, Expr e) => Log?.Add((rule, e));

    private static int Mark => Log?.Count ?? 0;

    private static void Rewind(int mark)
    {
        if (Log is not null && Log.Count > mark) Log.RemoveRange(mark, Log.Count - mark);
    }

    private const int MaxDepth = 7;

    /// <summary>A verified antiderivative of f with respect to x (without the constant), or null.</summary>
    public static Expr? Antiderivative(Expr f, string x)
    {
        Expr? raw;
        try { raw = Integrate(f, x, 0); }
        catch (CasLimitException) { return null; }
        if (raw is null) return null;
        var clean = DropConstants(Clean(raw), x);
        if (Verify(f, clean, x)) return clean;
        if (!ReferenceEquals(clean, raw) && Verify(f, raw, x)) return raw;
        return null;
    }

    private static Expr Clean(Expr e)
    {
        try
        {
            var mapped = CasEnv.Rebuild(RemoveAbs(e));
            return Simplifier.Tidy(mapped);
        }
        catch (CalcException) { return e; }
    }

    /// <summary>Removes terms free of x (the constant of integration is not shown).</summary>
    private static Expr DropConstants(Expr e, string x)
    {
        if (e is not AddExpr a || a.Terms.All(t => t.ContainsSymbol(x))) return e;
        var kept = a.Terms.Where(t => t.ContainsSymbol(x)).ToList();
        return kept.Count == 0 ? e : Algebra.Add(kept);
    }

    private static Expr RemoveAbs(Expr e)
    {
        if (e.Children.Count == 0) return e;
        var m = e.WithChildren([.. e.Children.Select(RemoveAbs)]);
        if (m is CallExpr { Name: "abs", Args.Length: 1 } c && Simplifier.IsNonNegative(c.Args[0])) return c.Args[0];
        return m;
    }

    // ------------------------------------------------------------------ verification

    private static readonly double[] Samples = [0.37, 1.23, 2.71, -0.83, 0.61, 3.3, -2.2, 0.05, -0.41, 1.77, 4.6, -3.7];

    /// <summary>Checks dF/dx = f numerically at sample points (other symbols get fixed sample values).</summary>
    public static bool Verify(Expr f, Expr antiderivative, string x)
    {
        Expr df;
        try { df = Differentiator.D(antiderivative, x); }
        catch (CalcException) { return false; }
        if (ContainsCall(df, "diff")) return false;
        var vars = new List<string> { x };
        foreach (var s in f.FreeSymbols().Concat(antiderivative.FreeSymbols()))
            if (!vars.Contains(s)) vars.Add(s);
        var cf = NumericFunction.Compile(f, vars);
        var cd = NumericFunction.Compile(df, vars);
        int valid = 0;
        var args = new double[vars.Count];
        for (int i = 1; i < vars.Count; i++) args[i] = 0.7 + 0.37 * i;
        if (cf is not null && cd is not null)
        {
            foreach (var s in Samples)
            {
                args[0] = s;
                double a = cf(args), b = cd(args);
                if (!double.IsFinite(a) || !double.IsFinite(b)) continue;
                if (Math.Abs(a - b) > 1e-6 * Math.Max(1, Math.Abs(a))) return false;
                if (++valid >= 6) break;
            }
            if (valid >= 2) return true;
        }
        // Complex-valued or non-compilable integrands: compare with exact-mode evaluation at the sample points.
        valid = 0;
        foreach (var s in Samples)
        {
            var values = new Dictionary<string, Expr>();
            args[0] = s;
            for (int i = 0; i < vars.Count; i++) values[vars[i]] = Algebra.Q(BigRational.Approximate(args[i]));
            var a = CasEnv.Numeric(SafeSubst(f, values));
            var b = CasEnv.Numeric(SafeSubst(df, values));
            if (a is not { IsFinite: true } av || b is not { IsFinite: true } bv || av.IsNaN || bv.IsNaN) continue;
            if (Number.Hypot(av.Re - bv.Re, av.Im - bv.Im) > 1e-6 * Math.Max(1, av.AbsDouble())) return false;
            if (++valid >= 6) break;
        }
        return valid >= 2;
    }

    private static Expr SafeSubst(Expr e, Dictionary<string, Expr> values)
    {
        try { return CasEnv.Subst(e, values); }
        catch (CalcException) { return ConstantExpr.Undefined; }
        catch (ArithmeticException) { return ConstantExpr.Undefined; }
    }

    private static bool ContainsCall(Expr e, string name) => (e is CallExpr c && c.Name == name) || e.Children.Any(ch => ContainsCall(ch, name));

    // ------------------------------------------------------------------ driver

    private static Expr X(string x) => new SymbolExpr(x);

    private static Expr? Integrate(Expr f, string x, int depth)
    {
        using var _ = CasEnv.Deeper(400);
        if (depth > MaxDepth) return null;
        if (!f.ContainsSymbol(x)) return Algebra.Mul(f, X(x));
        if (f is SymbolExpr)
        {
            Trace("power", f);
            return Algebra.Mul(Expr.HalfExpr, Algebra.Pow(f, Expr.Two));
        }
        if (f is MulExpr m)
        {
            var constant = m.Factors.Where(t => !t.ContainsSymbol(x)).ToList();
            if (constant.Count > 0)
            {
                Trace("constant", f);
                var rest = Integrate(Algebra.Mul(m.Factors.Where(t => t.ContainsSymbol(x))), x, depth);
                return rest is null ? null : Algebra.Mul(Algebra.Mul(constant), rest);
            }
        }
        if (f is AddExpr sum)
        {
            var poly = Polynomial(f, x);
            if (poly is not null) return poly;
            int sumMark = Mark;
            Trace("sum", f);
            var parts = new List<Expr>();
            foreach (var term in sum.Terms)
            {
                var r = Integrate(term, x, depth);
                if (r is null)
                {
                    parts.Clear();
                    break;
                }
                parts.Add(r);
            }
            if (parts.Count == sum.Terms.Length) return Algebra.Add(parts);
            Rewind(sumMark);
        }
        Func<Expr?>[] strategies =
        [
            () => Table(f, x),
            () => HasPowerOfSum(f, x) ? Substitution(f, x, depth) : null,
            () => Polynomial(f, x),
            () => Rational(f, x),
            () => Parts(f, x, depth),
            () => ExpTrig(f, x),
            () => ExpProduct(f, x),
            () => Substitution(f, x, depth),
            () => TrigPowers(f, x, depth),
            () => TrigProducts(f, x, depth),
            () => Radicals(f, x),
            () => Retry(f, x, depth),
        ];
        foreach (var strategy in strategies)
        {
            int mark = Mark;
            var r = strategy();
            if (r is not null) return r;
            Rewind(mark);
        }
        return null;
    }

    private static bool HasPowerOfSum(Expr f, string x) =>
        (f is PowExpr { Base: AddExpr b, Exponent: NumberExpr n } && b.ContainsSymbol(x) && n.Value.IsReal && Math.Abs(n.Value.Re) >= 2)
        || (f is MulExpr m && m.Factors.Any(t => HasPowerOfSum(t, x)));

    private static Expr? Retry(Expr f, string x, int depth)
    {
        if (f is MulExpr or PowExpr)
        {
            Expr ex;
            try { ex = Expander.Expand(f); }
            catch (CasLimitException) { return null; }
            if (!ex.Equals(f) && ex.NodeCount() < 500)
            {
                Trace("expand", f);
                var r = Integrate(ex, x, depth + 1);
                if (r is not null) return r;
            }
        }
        var rw = CasEnv.Rebuild(Simplifier.RewriteSinCos(f));
        if (!rw.Equals(f)) return Integrate(rw, x, depth + 1);
        return null;
    }

    /// <summary>u = a·x + b with a, b free of x and a ≠ 0.</summary>
    internal static (Expr A, Expr B)? Linear(Expr u, string x)
    {
        if (u is SymbolExpr s && s.Name == x) return (Expr.One, Expr.Zero);
        if (!u.ContainsSymbol(x)) return null;
        var p = ExprPoly.From(u, x, maxDegree: 2);
        if (p is null || p.Degree != 1) return null;
        return (p[1], p[0]);
    }

    private static Expr? Polynomial(Expr f, string x)
    {
        if (f.NodeCount() > 400) return null;
        var p = ExprPoly.From(f, x, maxDegree: 400);
        if (p is null) return null;
        Trace("polynomial", f);
        var terms = new List<Expr>();
        for (int k = 0; k <= p.Degree; k++)
            terms.Add(Algebra.Mul(p[k], Expr.Rational(1, k + 1), Algebra.Pow(X(x), Expr.Int(k + 1))));
        return Algebra.Add(terms);
    }

    // ------------------------------------------------------------------ table of standard forms

    private static Expr Ln(Expr u) => CasEnv.Fn("ln", u);
    private static Expr LnAbs(Expr u) => CasEnv.Fn("ln", CasEnv.Fn("abs", u));
    private static Expr F(string name, Expr u) => CasEnv.Fn(name, u);
    private static Expr Div(Expr a, Expr b) => Algebra.Div(a, b);

    private static Expr? Table(Expr f, string x)
    {
        switch (f)
        {
            case PowExpr p:
                return PowerTable(p, x);
            case CallExpr { Args.Length: 1 } c when Linear(c.Args[0], x) is (var a, _):
            {
                var r = CallTable(c.Name, c.Args[0]);
                if (r is null) return null;
                Trace(a.IsOne && c.Args[0] is SymbolExpr ? "table" : "table-linear", f);
                return Div(r, a);
            }
            case CallExpr { Name: "log", Args.Length: 2 } lc when !lc.Args[1].ContainsSymbol(x) && Linear(lc.Args[0], x) is (var a2, _):
            {
                var u = lc.Args[0];
                Trace("table", f);
                return Div(Algebra.Sub(Algebra.Mul(u, Ln(u)), u), Algebra.Mul(a2, Ln(lc.Args[1])));
            }
            case MulExpr { Factors.Length: 2 } m when m.Factors[0] is CallExpr { Args.Length: 1 } c1 && m.Factors[1] is CallExpr { Args.Length: 1 } c2
                                                      && c1.Args[0].Equals(c2.Args[0]) && Linear(c1.Args[0], x) is (var a3, _):
            {
                var u = c1.Args[0];
                var names = new HashSet<string> { c1.Name, c2.Name };
                Expr? r = null;
                if (names.SetEquals(["sec", "tan"])) r = F("sec", u);
                else if (names.SetEquals(["csc", "cot"])) r = Algebra.Neg(F("csc", u));
                else if (names.SetEquals(["sech", "tanh"])) r = Algebra.Neg(F("sech", u));
                else if (names.SetEquals(["csch", "coth"])) r = Algebra.Neg(F("csch", u));
                if (r is null) return null;
                Trace("table", f);
                return Div(r, a3);
            }
        }
        return null;
    }

    /// <summary>The table applied after pulling out constant factors.</summary>
    private static Expr? ScaledTable(Expr f, string x)
    {
        if (f is MulExpr m && m.Factors.Any(t => !t.ContainsSymbol(x)))
        {
            var rest = Table(Algebra.Mul(m.Factors.Where(t => t.ContainsSymbol(x))), x);
            return rest is null ? null : Algebra.Mul(Algebra.Mul(m.Factors.Where(t => !t.ContainsSymbol(x))), rest);
        }
        return Table(f, x);
    }

    private static Expr? PowerTable(PowExpr p, string x)
    {
        var n = p.Exponent;
        if (!n.ContainsSymbol(x) && Linear(p.Base, x) is (var a, _))
        {
            if (n is NumberExpr { Value.IsMinusOne: true })
            {
                Trace(p.Base is SymbolExpr ? "reciprocal" : "reciprocal-linear", p);
                return Div(LnAbs(p.Base), a);
            }
            if (Simplifier.IsZero(Algebra.Add(n, Expr.One))) return Div(LnAbs(p.Base), a);
            Trace(p.Base is SymbolExpr ? "power" : "power-linear", p);
            var n1 = Algebra.Add(n, Expr.One);
            return Div(Algebra.Pow(p.Base, n1), Algebra.Mul(a, n1));
        }
        if (!p.Base.ContainsSymbol(x) && Linear(p.Exponent, x) is (var a2, _))
        {
            Trace(p.Base is ConstantExpr { Constant: ConstantKind.E } ? "exp" : "exponential", p);
            if (p.Base is ConstantExpr { Constant: ConstantKind.E }) return Div(p, a2);
            return Div(p, Algebra.Mul(a2, Ln(p.Base)));
        }
        if (p.Base is CallExpr { Args.Length: 1 } c && Linear(c.Args[0], x) is (var a3, _) && p.Exponent is NumberExpr { Value.IsExactInteger: true } k)
        {
            var u = c.Args[0];
            Expr? r = (c.Name, (int)k.Value.Re) switch
            {
                ("sec", 2) or ("cos", -2) => F("tan", u),
                ("csc", 2) or ("sin", -2) => Algebra.Neg(F("cot", u)),
                ("sech", 2) or ("cosh", -2) => F("tanh", u),
                ("csch", 2) or ("sinh", -2) => Algebra.Neg(F("coth", u)),
                ("sin", 2) => Algebra.Sub(Algebra.Mul(Expr.HalfExpr, u), Algebra.Mul(Expr.Rational(1, 4), F("sin", Algebra.Mul(Expr.Two, u)))),
                ("cos", 2) => Algebra.Add(Algebra.Mul(Expr.HalfExpr, u), Algebra.Mul(Expr.Rational(1, 4), F("sin", Algebra.Mul(Expr.Two, u)))),
                ("tan", 2) or ("cot", -2) => Algebra.Sub(F("tan", u), u),
                ("cot", 2) or ("tan", -2) => Algebra.Neg(Algebra.Add(F("cot", u), u)),
                ("sinh", 2) => Algebra.Sub(Algebra.Mul(Expr.Rational(1, 4), F("sinh", Algebra.Mul(Expr.Two, u))), Algebra.Mul(Expr.HalfExpr, u)),
                ("cosh", 2) => Algebra.Add(Algebra.Mul(Expr.Rational(1, 4), F("sinh", Algebra.Mul(Expr.Two, u))), Algebra.Mul(Expr.HalfExpr, u)),
                ("tanh", 2) => Algebra.Sub(u, F("tanh", u)),
                ("sin", -1) => CallTable("csc", u),
                ("cos", -1) => CallTable("sec", u),
                ("tan", -1) => CallTable("cot", u),
                ("cot", -1) => CallTable("tan", u),
                ("sec", -1) => CallTable("cos", u),
                ("csc", -1) => CallTable("sin", u),
                ("sinh", -1) => CallTable("csch", u),
                ("cosh", -1) => CallTable("sech", u),
                _ => null,
            };
            if (r is null) return null;
            Trace("table", p);
            return Div(r, a3);
        }
        return null;
    }

    /// <summary>∫ f(u) du for the elementary functions.</summary>
    private static Expr? CallTable(string name, Expr u)
    {
        var u2 = Algebra.Mul(u, u);
        return name switch
        {
            "sin" => Algebra.Neg(F("cos", u)),
            "cos" => F("sin", u),
            "tan" => Algebra.Neg(LnAbs(F("cos", u))),
            "cot" => LnAbs(F("sin", u)),
            "sec" => LnAbs(Algebra.Add(F("sec", u), F("tan", u))),
            "csc" => Algebra.Neg(LnAbs(Algebra.Add(F("csc", u), F("cot", u)))),
            "sinh" => F("cosh", u),
            "cosh" => F("sinh", u),
            "tanh" => Ln(F("cosh", u)),
            "coth" => LnAbs(F("sinh", u)),
            "sech" => F("atan", F("sinh", u)),
            "csch" => LnAbs(F("tanh", Algebra.Mul(Expr.HalfExpr, u))),
            "ln" => Algebra.Sub(Algebra.Mul(u, Ln(u)), u),
            "log" => Div(Algebra.Sub(Algebra.Mul(u, Ln(u)), u), Ln(Expr.Int(10))),
            "log2" => Div(Algebra.Sub(Algebra.Mul(u, Ln(u)), u), Ln(Expr.Int(2))),
            "asin" => Algebra.Add(Algebra.Mul(u, F("asin", u)), Algebra.Sqrt(Algebra.Sub(Expr.One, u2))),
            "acos" => Algebra.Sub(Algebra.Mul(u, F("acos", u)), Algebra.Sqrt(Algebra.Sub(Expr.One, u2))),
            "atan" => Algebra.Sub(Algebra.Mul(u, F("atan", u)), Algebra.Mul(Expr.HalfExpr, Ln(Algebra.Add(Expr.One, u2)))),
            "acot" => Algebra.Add(Algebra.Mul(u, F("acot", u)), Algebra.Mul(Expr.HalfExpr, Ln(Algebra.Add(Expr.One, u2)))),
            "asinh" => Algebra.Sub(Algebra.Mul(u, F("asinh", u)), Algebra.Sqrt(Algebra.Add(u2, Expr.One))),
            "acosh" => Algebra.Sub(Algebra.Mul(u, F("acosh", u)), Algebra.Sqrt(Algebra.Sub(u2, Expr.One))),
            "atanh" => Algebra.Add(Algebra.Mul(u, F("atanh", u)), Algebra.Mul(Expr.HalfExpr, Ln(Algebra.Sub(Expr.One, u2)))),
            "abs" => Algebra.Mul(Expr.HalfExpr, u, F("abs", u)),
            "sign" => F("abs", u),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ rational functions

    private static Expr? Rational(Expr f, string x)
    {
        var km = KernelMap.Build([f], x);
        if (km is null || km.Count != 1) return null;
        var r = km.ToRat(f);
        if (r is null) return null;
        var num = r.Num.ToQPoly(0);
        var den = r.Den.ToQPoly(0);
        var parts = PolyTools.Apart(num, den);
        if (parts is null) return null;
        Trace("partial-fractions", f);
        var xs = X(x);
        Expr P(QPoly q) => km.ToExpr(MPoly.FromQPoly(1, 0, q));
        var terms = new List<Expr> { P(parts.Value.Polynomial.Integral()) };
        foreach (var (numer, b, k) in parts.Value.Fractions)
        {
            var t = Fraction(numer, b, k, xs, P);
            if (t is null) return null;
            terms.Add(t);
        }
        return Algebra.Add(terms);
    }

    /// <summary>∫ c(x)/d(x)^k for d linear or an irreducible quadratic.</summary>
    private static Expr? Fraction(QPoly c, QPoly d, int k, Expr x, Func<QPoly, Expr> toExpr)
    {
        var dExpr = toExpr(d);
        if (d.Degree == 1)
        {
            var p = d[1];
            var cc = c[0];
            if (k == 1) return Algebra.Mul(Algebra.Q(cc / p), LnAbs(dExpr));
            return Algebra.Mul(Algebra.Q(cc / (p * (1 - k))), Algebra.Pow(dExpr, Expr.Int(1 - k)));
        }
        if (d.Degree != 2) return null;
        var A = d[2];
        var B = d[1];
        var C = d[0];
        var h = B / (2 * A);
        var D = C / A - h * h;
        var M = c[1];
        var Nn = c[0] - M * h;
        var t = Algebra.Add(x, Algebra.Q(h));
        if (D.IsNegative)
        {
            // Real irrational roots t = ±K: ∫ dt/(t² - K²) = ln|(t - K)/(t + K)|/(2K).
            if (k != 1) return null;
            var K = Algebra.Sqrt(Algebra.Q(-D));
            var logPart = Algebra.Sub(LnAbs(Algebra.Sub(t, K)), LnAbs(Algebra.Add(t, K)));
            var parts2 = new List<Expr>();
            if (!M.IsZero) parts2.Add(Algebra.Mul(Algebra.Q(M / (2 * A)), LnAbs(dExpr)));
            if (!Nn.IsZero) parts2.Add(Algebra.Mul(Algebra.Q(Nn / A), Algebra.Pow(Algebra.Mul(Expr.Two, K), Expr.MinusOne), logPart));
            return Algebra.Add(parts2);
        }
        if (D.IsZero) return null;
        var tSq = Algebra.Div(dExpr, Algebra.Q(A));
        var Ak = A.Pow(k);
        var result = new List<Expr>();
        if (!M.IsZero)
        {
            if (k == 1) result.Add(Algebra.Mul(Algebra.Q(M / (2 * A)), Ln(dExpr)));
            else result.Add(Algebra.Mul(Algebra.Q(M / (2 * (1 - k) * A)), Algebra.Pow(dExpr, Expr.Int(1 - k))));
        }
        if (!Nn.IsZero)
        {
            var sqrtD = Algebra.Sqrt(Algebra.Q(D));
            Expr J = Algebra.Div(F("atan", Algebra.Div(t, sqrtD)), sqrtD);
            for (int j = 2; j <= k; j++)
            {
                var first = Algebra.Div(t, Algebra.Mul(Algebra.Q(2 * D * (j - 1)), Algebra.Pow(tSq, Expr.Int(j - 1))));
                J = Algebra.Add(first, Algebra.Mul(Algebra.Q(new BigRational(2 * j - 3) / (2 * D * (j - 1))), J));
            }
            result.Add(Algebra.Mul(Algebra.Q(Nn / Ak), J));
        }
        return Algebra.Add(result);
    }

    // ------------------------------------------------------------------ integration by parts

    private static bool IsPowerOfX(Expr e, string x) =>
        (e is SymbolExpr s && s.Name == x) || (e is PowExpr { Base: SymbolExpr b, Exponent: var n } && b.Name == x && !n.ContainsSymbol(x));

    private static bool IsPolynomialIn(Expr e, string x) => e.ContainsSymbol(x) && ExprPoly.From(e, x, maxDegree: 60) is not null;

    private static readonly HashSet<string> Tabular = ["sin", "cos", "sinh", "cosh"];
    private static readonly HashSet<string> LogLike = ["ln", "log", "log2", "asin", "acos", "atan", "acot", "asinh", "acosh", "atanh", "asec", "acsc"];

    private static bool IsTabularFactor(Expr h, string x) =>
        (h is PowExpr p && !p.Base.ContainsSymbol(x) && Linear(p.Exponent, x) is not null)
        || (h is CallExpr { Args.Length: 1 } c && Tabular.Contains(c.Name) && Linear(c.Args[0], x) is not null);

    private static bool IsLogFactor(Expr h, string x) =>
        (h is CallExpr { Args.Length: >= 1 } c && LogLike.Contains(c.Name) && c.Args[0].ContainsSymbol(x) && c.Args.Skip(1).All(a => !a.ContainsSymbol(x)))
        || (h is PowExpr { Base: CallExpr { Name: "ln" or "log" } lc, Exponent: NumberExpr { Value.IsExactInteger: true } k } && k.Value.IsPositiveReal && lc.Args[0].ContainsSymbol(x));

    private static Expr? Parts(Expr f, string x, int depth)
    {
        var factors = f is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [f];
        var poly = new List<Expr>();
        var others = new List<Expr>();
        foreach (var t in factors)
        {
            if (IsPowerOfX(t, x) || IsPolynomialIn(t, x)) poly.Add(t);
            else others.Add(t);
        }
        if (others.Count != 1) return null;
        var h = others[0];
        var P = Algebra.Mul(poly);
        if (poly.Count > 0 && IsTabularFactor(h, x) && ExprPoly.From(P, x, 60) is { Degree: >= 1 } pp)
        {
            Trace("parts-tabular", f);
            var terms = new List<Expr>();
            Expr H = h;
            Expr Pk = P;
            for (int k = 0; k <= pp.Degree; k++)
            {
                var Hk = ScaledTable(H, x);
                if (Hk is null) return null;
                var term = Algebra.Mul(Pk, Hk);
                terms.Add(k % 2 == 0 ? term : Algebra.Neg(term));
                Pk = Differentiator.D(Pk, x);
                if (Pk.IsZero) break;
                H = Hk;
            }
            return Algebra.Add(terms);
        }
        if (IsLogFactor(h, x))
        {
            if (P is PowExpr { Exponent: NumberExpr { Value.IsMinusOne: true } }) return null;
            var Q = poly.Count == 0 ? X(x) : Integrate(P, x, depth + 1);
            if (Q is null) return null;
            Trace("parts-log", f);
            var dh = Differentiator.D(h, x);
            var rest = Simplifier.Simplify(Algebra.Mul(Q, dh), trig: false);
            var r = Integrate(rest, x, depth + 1);
            if (r is null) return null;
            return Algebra.Sub(Algebra.Mul(Q, h), r);
        }
        return null;
    }

    /// <summary>∏ b_i^(a_i x + c_i) is one exponential: its integral is itself over Σ a_i·ln(b_i).</summary>
    private static Expr? ExpProduct(Expr f, string x)
    {
        if (f is not MulExpr m) return null;
        var rate = new List<Expr>();
        foreach (var t in m.Factors)
        {
            if (t is not PowExpr p || p.Base.ContainsSymbol(x) || Linear(p.Exponent, x) is not (var a, _)) return null;
            rate.Add(p.Base is ConstantExpr { Constant: ConstantKind.E } ? a : Algebra.Mul(a, Ln(p.Base)));
        }
        var total = Algebra.Add(rate);
        if (Simplifier.IsZero(total)) return null;
        Trace("exponential", f);
        return Algebra.Div(f, total);
    }

    /// <summary>∫ e^(ax+b)·sin(cx+d) and ∫ e^(ax+b)·cos(cx+d) (any constant base for the exponential).</summary>
    private static Expr? ExpTrig(Expr f, string x)
    {
        if (f is not MulExpr { Factors.Length: 2 } m) return null;
        PowExpr? ex = null;
        CallExpr? trig = null;
        foreach (var t in m.Factors)
        {
            if (t is PowExpr p && !p.Base.ContainsSymbol(x)) ex = p;
            else if (t is CallExpr { Name: "sin" or "cos", Args.Length: 1 } c) trig = c;
        }
        if (ex is null || trig is null) return null;
        var lu = Linear(ex.Exponent, x);
        var lv = Linear(trig.Args[0], x);
        if (lu is null || lv is null) return null;
        Trace("parts-cyclic", f);
        var a = ex.Base is ConstantExpr { Constant: ConstantKind.E } ? lu.Value.A : Algebra.Mul(lu.Value.A, Ln(ex.Base));
        var b = lv.Value.A;
        var v = trig.Args[0];
        var den = Algebra.Add(Algebra.Mul(a, a), Algebra.Mul(b, b));
        var sin = F("sin", v);
        var cos = F("cos", v);
        var num = trig.Name == "sin"
            ? Algebra.Sub(Algebra.Mul(a, sin), Algebra.Mul(b, cos))
            : Algebra.Add(Algebra.Mul(a, cos), Algebra.Mul(b, sin));
        return Algebra.Div(Algebra.Mul(ex, num), den);
    }

    // ------------------------------------------------------------------ substitution

    private static Expr? Substitution(Expr f, string x, int depth)
    {
        var candidates = new List<Expr>();
        var seen = new HashSet<Expr>();
        void Add(Expr g)
        {
            if (g is SymbolExpr || !g.ContainsSymbol(x) || g.Equals(f) || !seen.Add(g)) return;
            candidates.Add(g);
        }
        void Visit(Expr e)
        {
            switch (e)
            {
                case CallExpr c:
                    Add(c);
                    foreach (var a in c.Args) Add(a);
                    break;
                case PowExpr p:
                    Add(p.Base);
                    Add(p.Exponent);
                    if (p.Exponent is not NumberExpr { Value.IsExactInteger: true }) Add(p);
                    break;
            }
            foreach (var ch in e.Children) Visit(ch);
        }
        Visit(f);
        candidates = [.. candidates.OrderByDescending(g => g.NodeCount()).Take(16)];
        foreach (var g in candidates)
        {
            CasEnv.Check();
            Expr dg;
            try { dg = Simplifier.Tidy(Differentiator.D(g, x)); }
            catch (CalcException) { continue; }
            if (Simplifier.IsZero(dg)) continue;
            var q = Simplifier.Simplify(Algebra.Div(f, dg), trig: false);
            var u = CasEnv.Temp();
            var h = CasEnv.Rebuild(CasEnv.ReplaceExpr(q, g, u));
            if (h.ContainsSymbol(x)) h = Invert(h, g, x, u) ?? h;
            if (h.ContainsSymbol(x)) continue;
            if (h.Equals(CasEnv.Replace(f, new Dictionary<string, Expr> { [x] = u }))) continue;
            int mark = Mark;
            Trace("substitution", g);
            var G = Integrate(h, u.Name, depth + 1);
            if (G is null)
            {
                Rewind(mark);
                continue;
            }
            return CasEnv.Subst(G, u.Name, g);
        }
        return null;
    }

    /// <summary>Expresses the remaining x in terms of u when g = a·x + b or g = c·x^n.</summary>
    private static Expr? Invert(Expr h, Expr g, string x, SymbolExpr u)
    {
        if (Linear(g, x) is (var a, var b))
            return CasEnv.Subst(h, x, Algebra.Div(Algebra.Sub(u, b), a));
        var (coef, rest) = Algebra.SplitCoefficient(g);
        if (rest is PowExpr { Base: SymbolExpr bs, Exponent: NumberExpr { Value.IsExactRational: true } ne } && bs.Name == x)
        {
            var n = ne.Value.ReQ;
            var cExpr = new NumberExpr(coef);
            bool ok = true;
            Expr Map(Expr e)
            {
                if (!ok) return e;
                if (e is PowExpr { Base: SymbolExpr s, Exponent: NumberExpr { Value.IsExactRational: true } k } && s.Name == x)
                {
                    var ratio = k.Value.ReQ / n;
                    if (!ratio.IsInteger) { ok = false; return e; }
                    return Algebra.Pow(Algebra.Div(u, cExpr), Algebra.Q(ratio));
                }
                if (e is SymbolExpr s2 && s2.Name == x)
                {
                    var ratio = BigRational.One / n;
                    if (!ratio.IsInteger) { ok = false; return e; }
                    return Algebra.Pow(Algebra.Div(u, cExpr), Algebra.Q(ratio));
                }
                return e.Children.Count == 0 ? e : e.WithChildren([.. e.Children.Select(Map)]);
            }
            var mapped = Map(h);
            return ok ? CasEnv.Rebuild(mapped) : null;
        }
        return null;
    }

    // ------------------------------------------------------------------ powers of trig functions

    private static Expr? TrigPowers(Expr f, string x, int depth)
    {
        var factors = f is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [f];
        Expr? arg = null;
        int ms = 0, nc = 0;
        foreach (var t in factors)
        {
            var (b, e) = Algebra.SplitPower(t);
            if (b is not CallExpr { Args.Length: 1 } c || e is not NumberExpr { Value.IsExactInteger: true } ke) return null;
            if (arg is null) arg = c.Args[0];
            else if (!arg.Equals(c.Args[0])) return null;
            int k = (int)ke.Value.Re;
            switch (c.Name)
            {
                case "sin": ms += k; break;
                case "cos": nc += k; break;
                case "tan": ms += k; nc -= k; break;
                case "cot": ms -= k; nc += k; break;
                case "sec": nc -= k; break;
                case "csc": ms -= k; break;
                default: return null;
            }
        }
        if (arg is null || Linear(arg, x) is not (var a, _)) return null;
        var w = CasEnv.Temp();
        var oneMinus = Algebra.Sub(Expr.One, Algebra.Mul(w, w));
        if (ms > 0 && ms % 2 == 1)
        {
            Trace("trig-odd", f);
            var h = Algebra.Neg(Algebra.Mul(Algebra.Pow(oneMinus, Expr.Int((ms - 1) / 2)), Algebra.Pow(w, Expr.Int(nc))));
            var G = Integrate(Expander.Expand(h), w.Name, depth + 1);
            return G is null ? null : Algebra.Div(CasEnv.Subst(G, w.Name, F("cos", arg)), a);
        }
        if (nc > 0 && nc % 2 == 1)
        {
            Trace("trig-odd", f);
            var h = Algebra.Mul(Algebra.Pow(oneMinus, Expr.Int((nc - 1) / 2)), Algebra.Pow(w, Expr.Int(ms)));
            var G = Integrate(Expander.Expand(h), w.Name, depth + 1);
            return G is null ? null : Algebra.Div(CasEnv.Subst(G, w.Name, F("sin", arg)), a);
        }
        if (ms >= 0 && nc >= 0 && ms + nc >= 2 && ms + nc <= 16)
        {
            Trace("trig-even", f);
            var lin = TrigLog.CollectTrig(f);
            if (lin.Equals(f)) return null;
            return Integrate(lin, x, depth + 1);
        }
        // tan^k (k ≥ 3): ∫ tan^k = tan^(k-1)/(k-1) - ∫ tan^(k-2)
        if (ms >= 3 && nc == -ms)
        {
            Trace("trig-reduction", f);
            int k = ms;
            var lower = Integrate(Algebra.Pow(F("tan", arg), Expr.Int(k - 2)), x, depth + 1);
            if (lower is null) return null;
            return Algebra.Sub(Algebra.Div(Algebra.Pow(F("tan", arg), Expr.Int(k - 1)), Algebra.Mul(a, Expr.Int(k - 1))), lower);
        }
        // sec^k (k ≥ 3): ∫ sec^k = sec^(k-2) tan/(k-1) + (k-2)/(k-1) ∫ sec^(k-2)
        if (ms == 0 && nc <= -3)
        {
            Trace("trig-reduction", f);
            int k = -nc;
            var lower = Integrate(Algebra.Pow(F("sec", arg), Expr.Int(k - 2)), x, depth + 1);
            if (lower is null) return null;
            var first = Algebra.Div(Algebra.Mul(Algebra.Pow(F("sec", arg), Expr.Int(k - 2)), F("tan", arg)), Algebra.Mul(a, Expr.Int(k - 1)));
            return Algebra.Add(first, Algebra.Mul(Expr.Rational(k - 2, k - 1), lower));
        }
        // csc^k (k ≥ 3): ∫ csc^k = -csc^(k-2) cot/(k-1) + (k-2)/(k-1) ∫ csc^(k-2)
        if (nc == 0 && ms <= -3)
        {
            Trace("trig-reduction", f);
            int k = -ms;
            var lower = Integrate(Algebra.Pow(F("csc", arg), Expr.Int(k - 2)), x, depth + 1);
            if (lower is null) return null;
            var first = Algebra.Neg(Algebra.Div(Algebra.Mul(Algebra.Pow(F("csc", arg), Expr.Int(k - 2)), F("cot", arg)), Algebra.Mul(a, Expr.Int(k - 1))));
            return Algebra.Add(first, Algebra.Mul(Expr.Rational(k - 2, k - 1), lower));
        }
        return null;
    }

    /// <summary>Products of sines and cosines of different linear arguments: rewrite as a sum (product-to-sum).</summary>
    private static Expr? TrigProducts(Expr f, string x, int depth)
    {
        if (f is not MulExpr and not PowExpr) return null;
        var factors = f is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [f];
        int trig = 0;
        foreach (var t in factors)
        {
            // Polynomial factors are allowed: x·sin(x)·cos(2x) becomes a sum of x·sin(kx) terms.
            if (IsPowerOfX(t, x) && t is not PowExpr { Exponent: NumberExpr { Value.IsNegativeReal: true } } || IsPolynomialIn(t, x)) continue;
            var (b, e) = Algebra.SplitPower(t);
            if (b is not CallExpr { Name: "sin" or "cos" or "sinh" or "cosh", Args.Length: 1 } c || Linear(c.Args[0], x) is null) return null;
            if (e is not NumberExpr { Value.IsExactInteger: true, Value.IsPositiveReal: true } k || k.Value.Re > 8) return null;
            trig += (int)k.Value.Re;
        }
        if (trig < 2) return null;
        var lin = TrigLog.CollectTrig(f);
        if (lin.Equals(f)) return null;
        Trace("trig-even", f);
        return Integrate(lin, x, depth + 1);
    }

    // ------------------------------------------------------------------ square roots of quadratics

    private static Expr? Radicals(Expr f, string x)
    {
        if (f is not PowExpr { Exponent: NumberExpr { Value.IsExactRational: true } pe } p) return null;
        var pq = pe.Value.ReQ;
        if (pq != BigRational.Half && pq != -BigRational.Half) return null;
        var poly = ExprPoly.From(p.Base, x, 2);
        if (poly is null || poly.Degree != 2) return null;
        if (poly.Coefficients.Any(c => c is not NumberExpr { Value.IsExactRational: true })) return null;
        var A = ((NumberExpr)poly[2]).Value.ReQ;
        var B = ((NumberExpr)poly[1]).Value.ReQ;
        var C = ((NumberExpr)poly[0]).Value.ReQ;
        var h = B / (2 * A);
        var K = C - B * B / (4 * A);
        if (K.IsZero) return null;
        var t = Algebra.Add(X(x), Algebra.Q(h));
        var q = p.Base;
        var sqrtQ = Algebra.Sqrt(q);
        Trace("radical", f);
        if (A.IsPositive)
        {
            var sa = Algebra.Sqrt(Algebra.Q(A));
            Expr inner;
            if (K.IsPositive) inner = F("asinh", Algebra.Mul(t, Algebra.Sqrt(Algebra.Q(A / K))));
            else inner = LnAbs(Algebra.Add(Algebra.Mul(sa, t), sqrtQ));
            if (pq.IsNegative) return Algebra.Div(inner, sa);
            return Algebra.Add(Algebra.Mul(Expr.HalfExpr, t, sqrtQ), Algebra.Mul(Algebra.Q(K / 2), Algebra.Pow(sa, Expr.MinusOne), inner));
        }
        if (!K.IsPositive) return null;
        var absA = -A;
        var sab = Algebra.Sqrt(Algebra.Q(absA));
        var asin = F("asin", Algebra.Mul(t, Algebra.Sqrt(Algebra.Q(absA / K))));
        if (pq.IsNegative) return Algebra.Div(asin, sab);
        return Algebra.Add(Algebra.Mul(Expr.HalfExpr, t, sqrtQ), Algebra.Mul(Algebra.Q(K / 2), Algebra.Pow(sab, Expr.MinusOne), asin));
    }
}
