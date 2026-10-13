using System.Numerics;
using Calcy.Cas.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Solving;

/// <summary>The solutions of one equation in one variable.</summary>
internal sealed class RootSet
{
    public List<Expr> Values { get; } = [];

    /// <summary>Every value of the variable is a solution.</summary>
    public bool All { get; set; }

    /// <summary>False when some values are numeric approximations.</summary>
    public bool Exact { get; set; } = true;

    /// <summary>True when the roots came from a polynomial (so complex roots are meaningful).</summary>
    public bool Polynomial { get; set; }

    public static RootSet None() => new();

    public static RootSet Everything() => new() { All = true };
}

/// <summary>
/// Solves one equation f(x) = 0: polynomials (factoring over Q, exact linear and quadratic roots with radicals,
/// binomials and polynomials in x^k, numeric roots for the rest), rational equations (rejecting roots of the
/// denominator), isolation of a single occurrence through inverse functions (general solutions with an integer
/// parameter for trigonometric equations), factoring, substitution of a single kernel such as e^x or sin(x),
/// absolute values, trigonometric rewriting, and a numeric scan as the last resort.
/// </summary>
internal sealed class Solver
{
    private readonly string _x;
    private readonly string _param;
    private int _depth;

    /// <summary>Optional observer for step-by-step explanations.</summary>
    [ThreadStatic] public static Action<string, Expr>? Trace;

    public Solver(string x, string paramName)
    {
        _x = x;
        _param = paramName;
    }

    public SymbolExpr Parameter => new(_param);

    private Expr X => new SymbolExpr(_x);

    // ------------------------------------------------------------------ entry

    /// <summary>The solutions of f = 0 (complex roots filtered by the complex-number setting), or null when unsolved.</summary>
    public RootSet? Solve(Expr f)
    {
        var r = Roots(f);
        if (r is null || r.All) return r;
        var filtered = new RootSet { Exact = r.Exact, Polynomial = r.Polynomial };
        var env = CasEnv.Current;
        var mode = env?.Complex ?? ComplexMode.Auto;
        var values = Dedupe(r.Values);
        var real = values.Where(IsRealValue).ToList();
        if (mode == ComplexMode.Complex) filtered.Values.AddRange(values);
        else if (real.Count > 0 || mode == ComplexMode.RealOnly || !r.Polynomial) filtered.Values.AddRange(real);
        else filtered.Values.AddRange(values);
        filtered.Values.Sort(CompareValues);
        return filtered;
    }

    private static List<Expr> Dedupe(List<Expr> values)
    {
        var result = new List<Expr>();
        foreach (var v in values)
        {
            bool dup = false;
            foreach (var w in result)
            {
                if (v.Equals(w)) { dup = true; break; }
                if (v.FreeSymbols().Count == 0 && w.FreeSymbols().Count == 0)
                {
                    var a = CasEnv.Numeric(v);
                    var b = CasEnv.Numeric(w);
                    if (a is { } av && b is { } bv && av.IsFinite && bv.IsFinite &&
                        Number.Hypot(av.Re - bv.Re, av.Im - bv.Im) <= 1e-9 * Math.Max(1, av.AbsDouble()))
                    {
                        dup = true;
                        break;
                    }
                }
            }
            if (!dup) result.Add(v);
        }
        return result;
    }

    public static bool IsRealValue(Expr v)
    {
        if (v.FreeSymbols().Count > 0) return !ContainsImaginary(v);
        var n = CasEnv.Numeric(v);
        if (n is null) return !ContainsImaginary(v);
        return n.Value.IsReal || Math.Abs(n.Value.Im) <= 1e-9 * Math.Max(1, Math.Abs(n.Value.Re));
    }

    private static bool ContainsImaginary(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsReal,
        _ => e.Children.Any(ContainsImaginary),
    };

    public static int CompareValues(Expr a, Expr b)
    {
        var na = a.FreeSymbols().Count == 0 ? CasEnv.Numeric(a) : null;
        var nb = b.FreeSymbols().Count == 0 ? CasEnv.Numeric(b) : null;
        if (na is { } x && nb is { } y)
        {
            int c = x.Re.CompareTo(y.Re);
            if (c != 0) return c;
            return x.Im.CompareTo(y.Im);
        }
        if (na is not null) return -1;
        if (nb is not null) return 1;
        return CanonicalOrder.Compare(a, b);
    }

    // ------------------------------------------------------------------ general roots

    private readonly HashSet<Expr> _active = [];

    private RootSet? Roots(Expr f)
    {
        using var guard = CasEnv.Deeper(100);
        if (++_depth > 40) throw new CasLimitException("The equation is too complicated");
        if (!_active.Add(f))
        {
            _depth--;
            return null;
        }
        try
        {
            if (!f.ContainsSymbol(_x)) return Simplifier.IsZero(f) ? RootSet.Everything() : RootSet.None();
            return RationalRoots(f)
                ?? (Occurrences(f, _x) == 1 ? Isolate(f, Expr.Zero, peeled: false) : null)
                ?? FactorSplit(f)
                ?? KernelSubstitution(f)
                ?? RadicalSubstitution(f)
                ?? AbsoluteValues(f)
                ?? TrigRewrite(f)
                ?? Isolate(f, Expr.Zero, peeled: false)
                ?? NumericRoots(f);
        }
        finally
        {
            _depth--;
            _active.Remove(f);
        }
    }

    private static int Occurrences(Expr e, string x) => e is SymbolExpr s ? (s.Name == x ? 1 : 0) : e.Children.Sum(c => Occurrences(c, x));

    // ------------------------------------------------------------------ polynomial and rational equations

    private RootSet? RationalRoots(Expr f)
    {
        var rat = Simplifier.Rational(f, _x);
        if (rat is null) return null;
        var (km, r) = rat.Value;
        int v = km.IndexOf(_x);
        for (int i = 0; i < km.Count; i++)
            if (i != v && km.KernelContains(i, _x)) return null;
        Trace?.Invoke(r.IsPolynomial ? "polynomial" : "rational", km.ToExpr(r.Num));
        if (r.Num.IsZero) return RootSet.Everything();
        if (r.Num.Degree(v) <= 0) return RootSet.None();
        var roots = PolynomialRoots(r.Num, km, v);
        if (roots is null) return null;
        var result = new RootSet { Polynomial = true };
        var den = km.ToExpr(r.Den);
        foreach (var root in roots)
        {
            if (!r.IsPolynomial && VanishesAt(den, root)) continue;
            result.Values.Add(root);
            if (root is NumberExpr { Value.IsExact: false }) result.Exact = false;
        }
        return result;
    }

    private bool VanishesAt(Expr den, Expr root)
    {
        var d = CasEnv.TrySubst(den, _x, root);
        if (d is null) return true;
        if (Simplifier.IsZero(d)) return true;
        if (d.FreeSymbols().Count == 0)
        {
            var n = CasEnv.Numeric(d);
            if (n is { } nv && nv.AbsDouble() < 1e-10) return true;
        }
        return false;
    }

    /// <summary>All roots (real and complex) of a polynomial in variable v; other kernels are parameters.</summary>
    private List<Expr>? PolynomialRoots(MPoly p, KernelMap km, int v)
    {
        var roots = new List<Expr>();
        if (p.UsedVars().All(u => u == v))
        {
            var q = p.ToQPoly(v);
            foreach (var (factor, _) in UniFactor.FactorQ(q).Factors)
                roots.AddRange(IrreducibleRoots(factor.ToQ()));
            return roots;
        }
        var (_, factors) = PolyFactor.Factor(p);
        foreach (var (factor, _) in factors)
        {
            int d = factor.Degree(v);
            if (d <= 0) continue;
            if (factor.UsedVars().All(u => u == v))
            {
                roots.AddRange(IrreducibleRoots(factor.ToQPoly(v)));
                continue;
            }
            var coeffs = factor.CoefficientsIn(v);
            Expr C(int k) => coeffs.TryGetValue(k, out var c) ? km.ToExpr(c) : Expr.Zero;
            if (d == 1)
            {
                roots.Add(Simplifier.Simplify(Algebra.Neg(Algebra.Div(C(0), C(1))), trig: false));
                continue;
            }
            if (d == 2)
            {
                var a = C(2);
                var b = C(1);
                var c0 = C(0);
                var disc = Simplifier.Simplify(Algebra.Sub(Algebra.Mul(b, b), Algebra.Mul(Expr.Int(4), a, c0)), trig: false);
                var sq = Simplifier.Simplify(Algebra.Sqrt(disc), trig: false);
                var twoA = Algebra.Mul(Expr.Two, a);
                roots.Add(Algebra.Mul(Algebra.Sub(Algebra.Neg(b), sq), Algebra.Pow(twoA, Expr.MinusOne)));
                roots.Add(Algebra.Mul(Algebra.Add(Algebra.Neg(b), sq), Algebra.Pow(twoA, Expr.MinusOne)));
                continue;
            }
            // Polynomial in x^k of degree at most 2 in x^k with symbolic coefficients.
            return null;
        }
        return roots;
    }

    /// <summary>Roots of an irreducible rational polynomial: exact for degrees 1 and 2, binomials and quadratics in x^k.</summary>
    public static List<Expr> IrreducibleRoots(QPoly g)
    {
        var roots = new List<Expr>();
        int n = g.Degree;
        if (n < 1) return roots;
        if (n == 1)
        {
            roots.Add(Algebra.Q(-g[0] / g[1]));
            return roots;
        }
        if (n == 2)
        {
            roots.AddRange(QuadraticRoots(g[2], g[1], g[0]));
            return roots;
        }
        // Binomial a x^n + b.
        if (Enumerable.Range(1, n - 1).All(i => g[i].IsZero))
        {
            roots.AddRange(BinomialRoots(-g[0] / g[n], n));
            return roots;
        }
        // Quadratic in x^k.
        int k = n / 2;
        if (n % 2 == 0 && Enumerable.Range(1, n - 1).All(i => i == k || g[i].IsZero))
        {
            foreach (var y in QuadraticRoots(g[n], g[k], g[0]))
                roots.AddRange(RootsOf(y, k));
            return roots;
        }
        foreach (var z in RootFinding.PolynomialRoots(g))
            roots.Add(new NumberExpr(z.Imaginary == 0 ? Number.Approx(z.Real) : Number.Approx(z.Real, z.Imaginary)));
        return roots;
    }

    /// <summary>The roots of a x² + b x + c with exact radicals, written as (p ± q√m)/r.</summary>
    public static List<Expr> QuadraticRoots(BigRational a, BigRational b, BigRational c)
    {
        var disc = b * b - 4 * a * c;
        var p = -b / (2 * a);
        if (disc.IsZero) return [Algebra.Q(p)];
        var sq = Algebra.Sqrt(Algebra.Q(disc.Abs()));
        // sq is k√m (or rational when the discriminant is a perfect square).
        var (k, m) = Algebra.SplitCoefficient(sq);
        if (sq is NumberExpr sn)
        {
            var h = sn.Value.ReQ / (2 * a);
            if (disc.IsNegative)
                return [new NumberExpr(Number.Exact(p, -h.Abs())), new NumberExpr(Number.Exact(p, h.Abs()))];
            return [Algebra.Q(p - h), Algebra.Q(p + h)];
        }
        var q = k.ReQ / (2 * a).Abs();
        Expr radical = disc.IsNegative ? Algebra.Mul(Expr.ImaginaryUnit, m) : m;
        var den = BigInteger.Abs(Lcm(p.Den, q.Den));
        var P = p * den;
        var Q = q * den;
        Expr Root(int sign)
        {
            var part = Algebra.Mul(Algebra.Q(sign * Q), radical);
            if (den.IsOne) return Algebra.Add(Algebra.Q(P), part);
            var sum = P.IsZero ? part : new AddExpr([Algebra.Q(P), part]);
            if (P.IsZero) return Algebra.Mul(Algebra.Q(new BigRational(1, den)), part);
            return new MulExpr([Algebra.Q(new BigRational(BigInteger.One, den)), sum]);
        }
        return [Root(-1), Root(1)];
    }

    private static BigInteger Lcm(BigInteger a, BigInteger b) => a / BigInteger.GreatestCommonDivisor(a, b) * b;

    /// <summary>All n roots of x^n = c for rational c (exact real roots; complex ones exact when the angles are special).</summary>
    private static List<Expr> BinomialRoots(BigRational c, int n)
    {
        var roots = new List<Expr>();
        var r = Algebra.Pow(Algebra.Q(c.Abs()), Algebra.Q(new BigRational(1, n)));
        for (int j = 0; j < n; j++)
        {
            // angle (arg(c) + 2πj)/n as a multiple of π
            var k = (c.IsNegative ? BigRational.One : BigRational.Zero) + new BigRational(2 * j);
            var angle = k / n;
            if (angle.IsZero) { roots.Add(r); continue; }
            if (angle.IsOne) { roots.Add(Algebra.Neg(r)); continue; }
            var theta = Algebra.Mul(Algebra.Q(angle), ConstantExpr.Pi);
            var cos = CasEnv.Fn("cos", theta);
            var sin = CasEnv.Fn("sin", theta);
            if (cos is CallExpr || sin is CallExpr)
            {
                double rr = Math.Pow(c.Abs().ToDouble(), 1.0 / n), th = angle.ToDouble() * Math.PI;
                roots.Add(new NumberExpr(Number.Approx(rr * Math.Cos(th), rr * Math.Sin(th))));
            }
            else roots.Add(Algebra.Mul(r, Algebra.Add(cos, Algebra.Mul(Expr.ImaginaryUnit, sin))));
        }
        return roots;
    }

    /// <summary>All solutions of x^k = y.</summary>
    private static List<Expr> RootsOf(Expr y, int k)
    {
        var list = new List<Expr>();
        var yn = CasEnv.Numeric(y);
        if (yn is { } v && v.IsReal && v.Re >= 0)
        {
            var root = Simplifier.Simplify(Algebra.Pow(y, Algebra.Q(new BigRational(1, k))));
            list.Add(root);
            if (k % 2 == 0) list.Add(Algebra.Neg(root));
            if (k > 2)
            {
                double rr = Math.Pow(v.Re, 1.0 / k);
                for (int j = 1; j < k; j++)
                {
                    if (k % 2 == 0 && j == k / 2) continue;
                    double th = 2 * Math.PI * j / k;
                    list.Add(new NumberExpr(Number.Approx(rr * Math.Cos(th), rr * Math.Sin(th))));
                }
            }
            return list;
        }
        if (yn is { } w)
        {
            if (k == 2 && w.IsReal)
            {
                var root = Simplifier.Simplify(Algebra.Mul(Expr.ImaginaryUnit, Algebra.Sqrt(Algebra.Neg(y))));
                list.Add(root);
                list.Add(Algebra.Neg(root));
                return list;
            }
            if (k % 2 == 1 && w.IsReal)
            {
                list.Add(Simplifier.Simplify(Algebra.Pow(y, Algebra.Q(new BigRational(1, k)))));
                double rr = Math.Pow(Math.Abs(w.Re), 1.0 / k);
                for (int j = 1; j < k; j++)
                {
                    double th = (Math.PI + 2 * Math.PI * j) / k;
                    if (Math.Abs(Math.Sin(th)) < 1e-12) continue;
                    list.Add(new NumberExpr(Number.Approx(rr * Math.Cos(th), rr * Math.Sin(th))));
                }
                return list;
            }
            double mod = Math.Pow(w.AbsDouble(), 1.0 / k), arg = w.ArgDouble();
            for (int j = 0; j < k; j++)
            {
                double th = (arg + 2 * Math.PI * j) / k;
                list.Add(new NumberExpr(Number.Approx(mod * Math.Cos(th), mod * Math.Sin(th))));
            }
        }
        return list;
    }

    // ------------------------------------------------------------------ isolation

    /// <summary>Solves lhs = rhs (rhs free of x) by undoing the outermost operation around the single occurrence of x.</summary>
    private RootSet? Isolate(Expr lhs, Expr rhs, bool peeled)
    {
        using var guard = CasEnv.Deeper(100);
        if (lhs is SymbolExpr s && s.Name == _x)
        {
            var r = new RootSet();
            r.Values.Add(rhs);
            return r;
        }
        switch (lhs)
        {
            case AddExpr a:
            {
                var with = a.Terms.Where(t => t.ContainsSymbol(_x)).ToList();
                if (with.Count == 1)
                {
                    var rest = Algebra.Add(a.Terms.Where(t => !t.ContainsSymbol(_x)));
                    return Isolate(with[0], Algebra.Sub(rhs, rest), true);
                }
                break;
            }
            case MulExpr m:
            {
                var with = m.Factors.Where(t => t.ContainsSymbol(_x)).ToList();
                if (with.Count == 1)
                {
                    var c = Algebra.Mul(m.Factors.Where(t => !t.ContainsSymbol(_x)));
                    return Isolate(with[0], Simplifier.Tidy(Algebra.Div(rhs, c)), true);
                }
                break;
            }
            case PowExpr p:
                return IsolatePower(p, rhs) ?? (peeled ? Roots(Algebra.Sub(lhs, rhs)) : null);
            case CallExpr c:
                return IsolateCall(c, rhs) ?? (peeled ? Roots(Algebra.Sub(lhs, rhs)) : null);
        }
        return peeled ? Roots(Algebra.Sub(lhs, rhs)) : null;
    }

    private RootSet? Union(IEnumerable<(Expr Lhs, Expr Rhs)> cases)
    {
        var result = new RootSet();
        foreach (var (l, r) in cases)
        {
            var sub = Isolate(l, r, true);
            if (sub is null) return null;
            if (sub.All) return sub;
            result.Values.AddRange(sub.Values);
            result.Exact &= sub.Exact;
        }
        return result;
    }

    private static int? Sign(Expr e) => e.FreeSymbols().Count == 0 ? CasEnv.SignOf(e) : null;

    private RootSet? IsolatePower(PowExpr p, Expr rhs)
    {
        var b = p.Base;
        var e = p.Exponent;
        if (b.ContainsSymbol(_x) && !e.ContainsSymbol(_x))
        {
            Trace?.Invoke("root", p);
            if (e is NumberExpr { Value.IsExactInteger: true } ie)
            {
                var k = ie.Value.ToBigInteger();
                if (k < 0)
                {
                    if (Simplifier.IsZero(rhs)) return RootSet.None();
                    return Isolate(Algebra.Pow(b, new NumberExpr(-ie.Value)), Algebra.Pow(rhs, Expr.MinusOne), true);
                }
                if (Simplifier.IsZero(rhs)) return Isolate(b, Expr.Zero, true);
                if (k > 64) return null;
                int n = (int)k;
                var root = Simplifier.Simplify(Algebra.Pow(rhs, Algebra.Q(new BigRational(1, n))));
                if (n % 2 == 1) return Isolate(b, root, true);
                var sg = Sign(rhs);
                if (sg is < 0) return RootSet.None();
                return Union([(b, root), (b, Algebra.Neg(root))]);
            }
            if (e is NumberExpr { Value.IsExactRational: true } re)
            {
                var q = re.Value.ReQ;
                // b^(p/q) = rhs: first w^p = rhs for w = b^(1/q), then b = w^q (w ≥ 0 when q is even).
                var outer = PowerRoots(q.Num, rhs);
                if (outer is null) return null;
                var result = new RootSet();
                foreach (var w in outer)
                {
                    if (!q.Den.IsEven || Sign(w) is null or >= 0)
                    {
                        var sub = Isolate(b, Algebra.Pow(w, Algebra.Q(q.Den)), true);
                        if (sub is null) return null;
                        result.Values.AddRange(sub.Values);
                    }
                }
                return result;
            }
            // Symbolic exponent: b = rhs^(1/e).
            return Isolate(b, Algebra.Pow(rhs, Algebra.Pow(e, Expr.MinusOne)), true);
        }
        if (!b.ContainsSymbol(_x) && e.ContainsSymbol(_x))
        {
            Trace?.Invoke("log", p);
            var sg = Sign(rhs);
            if (sg is <= 0 && Sign(b) is > 0) return RootSet.None();
            Expr exponent = b is ConstantExpr { Constant: ConstantKind.E }
                ? CasEnv.Fn("ln", rhs)
                : b is NumberExpr { Value.IsExactRational: true } bn && bn.Value.ReQ == 10 ? CasEnv.Fn("log", rhs) : CasEnv.Fn("log", rhs, b);
            if (exponent is CallExpr { Name: "log", Args.Length: 2 }) exponent = Algebra.Div(CasEnv.Fn("ln", rhs), CasEnv.Fn("ln", b));
            return Isolate(e, exponent, true);
        }
        return null;
    }

    /// <summary>Real solutions w of w^k = rhs for an integer k ≠ 0.</summary>
    private static List<Expr>? PowerRoots(BigInteger k, Expr rhs)
    {
        if (k.IsZero || BigInteger.Abs(k) > 64) return null;
        if (k < 0)
        {
            if (Simplifier.IsZero(rhs)) return [];
            return PowerRoots(-k, Algebra.Pow(rhs, Expr.MinusOne));
        }
        if (Simplifier.IsZero(rhs)) return [Expr.Zero];
        int n = (int)k;
        var root = Simplifier.Simplify(Algebra.Pow(rhs, Algebra.Q(new BigRational(1, n))));
        if (n % 2 == 1) return [root];
        var sg = Sign(rhs);
        if (sg is < 0) return [];
        return [root, Algebra.Neg(root)];
    }

    private RootSet? IsolateCall(CallExpr c, Expr rhs)
    {
        if (c.Name == "log" && c.Args.Length == 2)
        {
            if (c.Args[1].ContainsSymbol(_x)) return null;
            return Isolate(c.Args[0], Algebra.Pow(c.Args[1], rhs), true);
        }
        if (c.Args.Length != 1) return null;
        var u = c.Args[0];
        var n = Parameter;
        var pi = ConstantExpr.Pi;
        Expr TwoPiN() => Algebra.Mul(Expr.Two, pi, n);
        Expr PiN() => Algebra.Mul(pi, n);
        Trace?.Invoke(c.Name, c);
        switch (c.Name)
        {
            case "ln": return Isolate(u, Algebra.Pow(ConstantExpr.E, rhs), true);
            case "log": return Isolate(u, Algebra.Pow(Expr.Int(10), rhs), true);
            case "log2": return Isolate(u, Algebra.Pow(Expr.Two, rhs), true);
            case "abs":
            {
                var sg = Sign(rhs);
                if (sg is < 0) return RootSet.None();
                if (sg is 0) return Isolate(u, Expr.Zero, true);
                return Union([(u, rhs), (u, Algebra.Neg(rhs))]);
            }
            case "sin":
            {
                var sg = Sign(Algebra.Sub(CasEnv.Fn("abs", rhs), Expr.One));
                if (sg is > 0) return RootSet.None();
                if (Simplifier.IsZero(rhs)) return Union([(u, PiN())]);
                var alpha = CasEnv.Fn("asin", rhs);
                if (sg is 0) return Union([(u, Algebra.Add(alpha, TwoPiN()))]);
                return Union([(u, Algebra.Add(alpha, TwoPiN())), (u, Algebra.Add(Algebra.Sub(pi, alpha), TwoPiN()))]);
            }
            case "cos":
            {
                var sg = Sign(Algebra.Sub(CasEnv.Fn("abs", rhs), Expr.One));
                if (sg is > 0) return RootSet.None();
                if (Simplifier.IsZero(rhs)) return Union([(u, Algebra.Add(Algebra.Mul(Expr.HalfExpr, pi), PiN()))]);
                var alpha = CasEnv.Fn("acos", rhs);
                if (sg is 0) return Union([(u, Algebra.Add(alpha, TwoPiN()))]);
                return Union([(u, Algebra.Add(alpha, TwoPiN())), (u, Algebra.Add(Algebra.Neg(alpha), TwoPiN()))]);
            }
            case "tan":
                return Union([(u, Algebra.Add(CasEnv.Fn("atan", rhs), PiN()))]);
            case "cot":
                if (Simplifier.IsZero(rhs)) return Union([(u, Algebra.Add(Algebra.Mul(Expr.HalfExpr, pi), PiN()))]);
                return Union([(u, Algebra.Add(CasEnv.Fn("atan", Algebra.Pow(rhs, Expr.MinusOne)), PiN()))]);
            case "sec":
                if (Simplifier.IsZero(rhs)) return RootSet.None();
                return IsolateCall(new CallExpr("cos", [u]), Algebra.Pow(rhs, Expr.MinusOne));
            case "csc":
                if (Simplifier.IsZero(rhs)) return RootSet.None();
                return IsolateCall(new CallExpr("sin", [u]), Algebra.Pow(rhs, Expr.MinusOne));
            case "asin":
            {
                var sg = Sign(Algebra.Sub(CasEnv.Fn("abs", rhs), Algebra.Mul(Expr.HalfExpr, pi)));
                if (sg is > 0) return RootSet.None();
                return Isolate(u, CasEnv.Fn("sin", rhs), true);
            }
            case "acos":
            {
                if (Sign(rhs) is < 0 || Sign(Algebra.Sub(rhs, pi)) is > 0) return RootSet.None();
                return Isolate(u, CasEnv.Fn("cos", rhs), true);
            }
            case "atan":
            {
                if (Sign(Algebra.Sub(CasEnv.Fn("abs", rhs), Algebra.Mul(Expr.HalfExpr, pi))) is >= 0) return RootSet.None();
                return Isolate(u, CasEnv.Fn("tan", rhs), true);
            }
            case "sinh": return Isolate(u, CasEnv.Fn("asinh", rhs), true);
            case "cosh":
            {
                var sg = Sign(Algebra.Sub(rhs, Expr.One));
                if (sg is < 0) return RootSet.None();
                if (sg is 0) return Isolate(u, Expr.Zero, true);
                var a = CasEnv.Fn("acosh", rhs);
                return Union([(u, a), (u, Algebra.Neg(a))]);
            }
            case "tanh":
                if (Sign(Algebra.Sub(CasEnv.Fn("abs", rhs), Expr.One)) is >= 0) return RootSet.None();
                return Isolate(u, CasEnv.Fn("atanh", rhs), true);
            case "asinh": return Isolate(u, CasEnv.Fn("sinh", rhs), true);
            case "acosh":
                if (Sign(rhs) is < 0) return RootSet.None();
                return Isolate(u, CasEnv.Fn("cosh", rhs), true);
            case "atanh": return Isolate(u, CasEnv.Fn("tanh", rhs), true);
            case "exp": return IsolatePower(new PowExpr(ConstantExpr.E, u), rhs);
            case "sqrt": return IsolatePower(new PowExpr(u, Expr.HalfExpr), rhs);
        }
        return null;
    }

    // ------------------------------------------------------------------ other strategies

    private RootSet? FactorSplit(Expr f)
    {
        var rat = Simplifier.Rational(f, _x);
        if (rat is null) return null;
        var (km, r) = rat.Value;
        var (_, factors) = PolyFactor.Factor(r.Num.IsZero ? MPoly.Constant(r.Vars, BigRational.One) : r.Num);
        var withX = factors.Where(t => Enumerable.Range(0, km.Count).Any(i => t.Factor.Contains(i) && km.KernelContains(i, _x))).ToList();
        if (withX.Count < 2) return null;
        Trace?.Invoke("factor", km.ToExpr(r.Num));
        var result = new RootSet();
        var den = km.ToExpr(r.Den);
        foreach (var (factor, _) in withX)
        {
            var sub = Roots(km.ToExpr(factor));
            if (sub is null) return null;
            if (sub.All) return sub;
            result.Exact &= sub.Exact;
            result.Polynomial |= sub.Polynomial;
            foreach (var v in sub.Values)
                if (r.IsPolynomial || !VanishesAt(den, v)) result.Values.Add(v);
        }
        return result;
    }

    /// <summary>A polynomial in one kernel such as e^x, sin(x) or ln(x): solve for the kernel, then isolate.</summary>
    private RootSet? KernelSubstitution(Expr f)
    {
        var rat = Simplifier.Rational(f, _x);
        if (rat is null) return null;
        var (km, r) = rat.Value;
        var withX = Enumerable.Range(0, km.Count).Where(i => km.KernelContains(i, _x) && (r.Num.Contains(i) || r.Den.Contains(i))).ToList();
        if (withX.Count != 1 || km[withX[0]] is SymbolExpr) return null;
        int kv = withX[0];
        var kernel = km[kv];
        if (r.Num.Degree(kv) <= 0) return r.Num.IsZero ? RootSet.Everything() : RootSet.None();
        Trace?.Invoke("substitute", kernel);
        var w = PolynomialRoots(r.Num, km, kv);
        if (w is null) return null;
        var result = new RootSet();
        var den = km.ToExpr(r.Den);
        foreach (var value in w)
        {
            if (!IsRealValue(value)) continue;
            var sub = Isolate(kernel, value, true);
            if (sub is null) return null;
            result.Exact &= sub.Exact;
            foreach (var root in sub.Values)
                if (r.IsPolynomial || !VanishesAt(den, root)) result.Values.Add(root);
        }
        return Validate(f, result);
    }

    /// <summary>Equations in x and roots of x (x - 3√x + 2 = 0): substitute y = x^(1/L).</summary>
    private RootSet? RadicalSubstitution(Expr f)
    {
        var km = KernelMap.Build([f], _x);
        if (km is null) return null;
        BigInteger lcm = BigInteger.One;
        bool any = false;
        for (int i = 0; i < km.Count; i++)
        {
            if (!km.KernelContains(i, _x)) continue;
            if (km[i] is SymbolExpr) continue;
            if (km[i] is PowExpr { Base: SymbolExpr b, Exponent: NumberExpr { Value.IsExactRational: true } e } && b.Name == _x)
            {
                lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, e.Value.ReQ.Den) * e.Value.ReQ.Den;
                any = true;
            }
            else return null;
        }
        if (!any || lcm > 12) return null;
        int L = (int)lcm;
        var y = CasEnv.Temp();
        var g = CasEnv.Subst(f, _x, Algebra.Pow(y, Expr.Int(L)));
        Trace?.Invoke("substitute", Algebra.Pow(X, Algebra.Q(new BigRational(1, L))));
        var inner = new Solver(y.Name, _param).Roots(g);
        if (inner is null || inner.All) return null;
        var result = new RootSet { Exact = inner.Exact };
        foreach (var v in inner.Values)
        {
            if (!IsRealValue(v)) continue;
            if (L % 2 == 0 && Sign(v) is < 0) continue;
            result.Values.Add(Simplifier.Simplify(Algebra.Pow(v, Expr.Int(L))));
        }
        return Validate(f, result);
    }

    /// <summary>Splits |u| into the cases u ≥ 0 and u &lt; 0.</summary>
    private RootSet? AbsoluteValues(Expr f)
    {
        CallExpr? abs = null;
        void Find(Expr e)
        {
            if (abs is not null) return;
            if (e is CallExpr { Name: "abs", Args.Length: 1 } c && c.Args[0].ContainsSymbol(_x)) { abs = c; return; }
            foreach (var ch in e.Children) Find(ch);
        }
        Find(f);
        if (abs is null) return null;
        Trace?.Invoke("abs-cases", abs);
        var u = abs.Args[0];
        var result = new RootSet();
        foreach (int sign in new[] { 1, -1 })
        {
            var g = CasEnv.Rebuild(CasEnv.ReplaceExpr(f, abs, sign > 0 ? u : Algebra.Neg(u)));
            var sub = Roots(g);
            if (sub is null) return null;
            if (sub.All) return null;
            result.Exact &= sub.Exact;
            foreach (var v in sub.Values)
            {
                if (!IsRealValue(v)) continue;
                var uv = CasEnv.TrySubst(u, _x, v);
                if (uv is null) continue;
                var s = Sign(uv);
                if (s is null || (sign > 0 ? s >= 0 : s < 0)) result.Values.Add(v);
            }
        }
        return Validate(f, result);
    }

    /// <summary>Rewrites multiple angles, then uses sin² + cos² = 1, tan = sin/cos or a·sin + b·cos = R·sin(u + φ).</summary>
    private RootSet? TrigRewrite(Expr f)
    {
        if (!HasTrig(f)) return null;
        var g = CasEnv.Rebuild(TrigLog.ExpandTrig(Simplifier.RewriteSinCos(f)));
        if (!g.Equals(f))
        {
            // The expanded form may factor (sin 2x - cos x = cos x (2 sin x - 1)).
            var direct = FactorSplit(g);
            if (direct is not null) return Validate(f, direct);
        }
        var rat = Simplifier.Rational(g, _x);
        if (rat is null) return null;
        var (km, r) = rat.Value;
        var withX = Enumerable.Range(0, km.Count).Where(i => km.KernelContains(i, _x) && (r.Num.Contains(i) || r.Den.Contains(i))).ToList();
        if (withX.Count != 2) return g.Equals(f) ? null : Roots(g);
        var k1 = km[withX[0]] as CallExpr;
        var k2 = km[withX[1]] as CallExpr;
        if (k1 is null || k2 is null || !k1.Args[0].Equals(k2.Args[0])) return g.Equals(f) ? null : Roots(g);
        var names = new[] { k1.Name, k2.Name };
        if (!names.Contains("sin") || !names.Contains("cos")) return null;
        int s = k1.Name == "sin" ? withX[0] : withX[1];
        int c = k1.Name == "cos" ? withX[0] : withX[1];
        var u = k1.Args[0];
        var num = r.Num;
        var den = km.ToExpr(r.Den);
        Trace?.Invoke("trig-identity", g);
        RootSet? candidate = null;
        bool onlyEven(int v) => num.Terms.All(t => t.Mono[v] % 2 == 0);
        if (onlyEven(c) || onlyEven(s))
        {
            int even = onlyEven(c) ? c : s, other = even == c ? s : c;
            var repl = MPoly.Constant(num.Vars, BigRational.One) - MPoly.Var(num.Vars, other, 2);
            var reduced = MPoly.Zero(num.Vars);
            foreach (var (m, coef) in num.Terms)
                reduced += MPoly.FromTerms(num.Vars, [(m.With(even, 0), coef)]) * repl.Pow(m[even] / 2);
            candidate = Roots(km.ToExpr(reduced));
        }
        else if (IsHomogeneousIn(num, s, c, out int degree))
        {
            // Divide by cos^n: a polynomial in tan(u); cos(u) = 0 also solves it when the sin^n coefficient vanishes.
            var t = CasEnv.Temp();
            var terms = new List<Expr>();
            foreach (var (m, coef) in num.Terms)
            {
                var rest = km.ToExpr(MPoly.FromTerms(num.Vars, [(m.With(s, 0).With(c, 0), coef)]));
                terms.Add(Algebra.Mul(rest, Algebra.Pow(t, Expr.Int(m[s]))));
            }
            var inner = new Solver(t.Name, _param).Solve(Algebra.Add(terms));
            if (inner is null || inner.All) return null;
            var result = new RootSet { Exact = inner.Exact };
            foreach (var tv in inner.Values)
            {
                if (!IsRealValue(tv)) continue;
                var sub = Isolate(CallExpr.Of("tan", u), tv, true);
                if (sub is null) return null;
                result.Values.AddRange(sub.Values);
            }
            if (num.CoefficientOf(s, degree).IsZero)
            {
                var sub = Isolate(CallExpr.Of("cos", u), Expr.Zero, true);
                if (sub is not null) result.Values.AddRange(sub.Values);
            }
            return Validate(f, result);
        }
        else if (num.Degree(s) == 1 && num.Degree(c) == 1 && num.TotalDegree == 1)
        {
            var a = km.ToExpr(num.CoefficientOf(s, 1).CoefficientOf(c, 0));
            var b = km.ToExpr(num.CoefficientOf(c, 1).CoefficientOf(s, 0));
            var d = km.ToExpr(num.CoefficientOf(s, 0).CoefficientOf(c, 0));
            if (Simplifier.IsZero(d)) candidate = Isolate(CallExpr.Of("tan", u), Simplifier.Simplify(Algebra.Neg(Algebra.Div(b, a))), true);
            else if (Sign(a) is > 0 || Sign(a) is < 0)
            {
                var R = Simplifier.Simplify(Algebra.Sqrt(Algebra.Add(Algebra.Mul(a, a), Algebra.Mul(b, b))));
                var phi = CasEnv.Fn("atan", Simplifier.Simplify(Algebra.Div(b, a)));
                if (Sign(a) is < 0) phi = Algebra.Add(phi, ConstantExpr.Pi);
                candidate = Isolate(CallExpr.Of("sin", Algebra.Add(u, phi)), Simplifier.Simplify(Algebra.Neg(Algebra.Div(d, R))), true);
            }
        }
        if (candidate is null) return null;
        var filtered = new RootSet { Exact = candidate.Exact };
        foreach (var v in candidate.Values)
            if (!VanishesAt(den, v)) filtered.Values.Add(v);
        return Validate(f, filtered);
    }

    private static bool IsHomogeneousIn(MPoly p, int s, int c, out int degree)
    {
        degree = -1;
        foreach (var (m, _) in p.Terms)
        {
            int d = m[s] + m[c];
            if (degree < 0) degree = d;
            else if (d != degree) return false;
        }
        return degree > 0;
    }

    private static bool HasTrig(Expr e) =>
        e is CallExpr { Name: "sin" or "cos" or "tan" or "sec" or "csc" or "cot" } || e.Children.Any(HasTrig);

    /// <summary>Numeric roots on [-100, 100] when nothing symbolic works (no other symbols allowed).</summary>
    private RootSet? NumericRoots(Expr f)
    {
        if (f.FreeSymbols().Any(s => s != _x)) return null;
        var fn = NumericFunction.Compile(f, _x);
        if (fn is null) return null;
        Trace?.Invoke("numeric", f);
        const int samples = 4000;
        var roots = RootFinding.ScanRoots(fn, -100, 100, samples);
        if (roots.Count == 0 || roots.Count > 60) return null;
        // Neighbouring sample points that are both roots mean f is zero on a whole interval (floor(x) = 2).
        double step = 200.0 / samples;
        for (int i = 1; i < roots.Count; i++)
            if (roots[i] - roots[i - 1] < 1.5 * step) return null;
        var result = new RootSet { Exact = false };
        foreach (var r in roots) result.Values.Add(new NumberExpr(Number.Approx(r)));
        return result;
    }

    /// <summary>Keeps candidates that satisfy f = 0 (checked numerically, with the parameter set to 0 and 1).</summary>
    private RootSet Validate(Expr f, RootSet set)
    {
        var result = new RootSet { Exact = set.Exact, Polynomial = set.Polynomial, All = set.All };
        foreach (var v in set.Values)
        {
            if (Satisfies(f, v)) result.Values.Add(v);
        }
        return result;
    }

    public bool Satisfies(Expr f, Expr v)
    {
        var others = v.FreeSymbols().Where(s => s != _param).ToList();
        if (others.Count > 0 || f.FreeSymbols().Any(s => s != _x)) return true;
        foreach (var nv in new[] { 0, 1 })
        {
            var value = v.ContainsSymbol(_param) ? CasEnv.Subst(v, _param, Expr.Int(nv)) : v;
            var fv = CasEnv.TrySubst(f, _x, value);
            if (fv is null) return false;
            var n = CasEnv.Numeric(fv);
            if (n is null) return true;
            var num = n.Value;
            if (!num.IsFinite || num.IsNaN) return false;
            double scale = Math.Max(1, CasEnv.Numeric(value)?.AbsDouble() ?? 1);
            if (num.AbsDouble() > 1e-7 * scale) return false;
            if (!v.ContainsSymbol(_param)) break;
        }
        return true;
    }
}
