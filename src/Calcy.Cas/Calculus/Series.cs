using System.Numerics;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Calculus;

/// <summary>
/// A truncated generalized power series in t (as t → 0+): Σ c_k t^e_k + O(t^Order), with rational exponents in
/// increasing order and coefficients free of t. Coefficients may contain <see cref="SeriesExpander.LogT"/>, which
/// stands for ln t.
/// </summary>
internal sealed class PS
{
    /// <summary>The order used for series that are exact (not truncated).</summary>
    public static readonly BigRational Infinite = new(1_000_000_000);

    public PS(List<(BigRational E, Expr C)> terms, BigRational order)
    {
        Terms = terms;
        Order = order;
    }

    public List<(BigRational E, Expr C)> Terms { get; }
    public BigRational Order { get; }

    public static PS Zero => new([], Infinite);
    public static PS Const(Expr c) => Simplifier.IsZero(c) ? Zero : new([(BigRational.Zero, c)], Infinite);
    public static PS Monomial(Expr c, BigRational e) => Simplifier.IsZero(c) ? Zero : new([(e, c)], Infinite);

    public bool IsExact => Order >= Infinite;
    public bool HasTerms => Terms.Count > 0;

    /// <summary>Exponent of the first term, or the order when no term is known.</summary>
    public BigRational LeadExp => Terms.Count > 0 ? Terms[0].E : Order;

    public Expr LeadCoef => Terms[0].C;

    /// <summary>The coefficient of t^0 (zero when absent).</summary>
    public Expr ConstantTerm => Terms.FirstOrDefault(t => t.E.IsZero).C ?? Expr.Zero;
}

/// <summary>An asymptotic value: a power series times an optional exp(G) with G divergent, or a bounded oscillation.</summary>
internal sealed class Asym
{
    public Asym(PS p, PS? g = null, bool bounded = false)
    {
        P = p;
        G = g;
        Bounded = bounded;
    }

    public PS P { get; }

    /// <summary>The exponent of an exponential scale factor (only negative powers of t), or null.</summary>
    public PS? G { get; }

    /// <summary>A bounded quantity without a limit (for example sin(1/t)).</summary>
    public bool Bounded { get; }

    public static Asym Of(PS p) => new(p);
    public static readonly Asym Oscillating = new(PS.Zero, null, bounded: true);
}

/// <summary>
/// Expands expressions in a symbol t as t → 0+ (Gruntz-style exp-log asymptotics without the full algorithm).
/// Used for limits and Taylor series.
/// </summary>
internal sealed class SeriesExpander
{
    /// <summary>The marker for ln t inside coefficients.</summary>
    public static readonly SymbolExpr LogT = new("⁣L");

    private readonly string _t;
    private readonly int _maxTerms;
    private readonly BigRational _maxOrder;

    public SeriesExpander(string t, int maxTerms, BigRational maxOrder)
    {
        _t = t;
        _maxTerms = maxTerms;
        _maxOrder = maxOrder;
    }

    // ------------------------------------------------------------------ series arithmetic

    private PS Normalize(IEnumerable<(BigRational E, Expr C)> raw, BigRational order)
    {
        var groups = new SortedDictionary<BigRational, List<Expr>>();
        foreach (var (e, c) in raw)
        {
            if (e >= order) continue;
            if (!groups.TryGetValue(e, out var list)) groups[e] = list = [];
            list.Add(c);
        }
        var terms = new List<(BigRational, Expr)>();
        var ord = QMath.Min(order, _maxOrder);
        foreach (var (e, list) in groups)
        {
            if (e >= ord) break;
            var c = list.Count == 1 ? list[0] : Algebra.Add(list);
            if (c.NodeCount() > 60) c = Simplifier.Cancel(c);
            if (Simplifier.IsZero(c)) continue;
            if (terms.Count >= _maxTerms)
            {
                ord = QMath.Min(ord, e);
                break;
            }
            terms.Add((e, c));
        }
        return new PS(terms, ord);
    }

    public PS Add(PS a, PS b) => Normalize(a.Terms.Concat(b.Terms), QMath.Min(a.Order, b.Order));

    public PS Neg(PS a) => new([.. a.Terms.Select(t => (t.E, Algebra.Neg(t.C)))], a.Order);

    public PS Scale(PS a, Expr c) => Simplifier.IsZero(c) ? PS.Zero : new([.. a.Terms.Select(t => (t.E, Algebra.Mul(c, t.C)))], a.Order);

    public PS Shift(PS a, BigRational e) => new([.. a.Terms.Select(t => (t.E + e, t.C))], a.Order >= PS.Infinite ? PS.Infinite : a.Order + e);

    public PS Mul(PS a, PS b)
    {
        CasEnv.Check();
        var order = QMath.Min(Add(a.LeadExp, b.Order), Add(b.LeadExp, a.Order));
        var raw = new List<(BigRational, Expr)>();
        foreach (var (ea, ca) in a.Terms)
            foreach (var (eb, cb) in b.Terms)
            {
                var e = ea + eb;
                if (e >= order) continue;
                raw.Add((e, Algebra.Mul(ca, cb)));
            }
        return Normalize(raw, order);
    }

    private static BigRational Add(BigRational x, BigRational y) => x >= PS.Infinite || y >= PS.Infinite ? PS.Infinite : x + y;

    /// <summary>Σ a_k U^k for k = 0..K where U has a positive leading exponent.</summary>
    private PS Compose(Func<int, Expr> coefficient, PS u, int count)
    {
        if (u.HasTerms && u.LeadExp <= 0) throw new InvalidOperationException("Compose needs a series tending to 0");
        var result = PS.Const(coefficient(0));
        var power = PS.Const(Expr.One);
        bool exhausted = false;
        for (int k = 1; k <= count; k++)
        {
            power = Mul(power, u);
            var ak = coefficient(k);
            if (!Simplifier.IsZero(ak))
            {
                var scaled = power.HasTerms ? Scale(power, ak) : new PS([], power.Order);
                result = Add(result, scaled);
            }
            if (!power.HasTerms)
            {
                exhausted = true;
                break;
            }
        }
        if (exhausted || !u.HasTerms) return result;
        var truncation = u.LeadExp * (count + 1);
        return new PS(result.Terms.Where(t => t.E < truncation).ToList(), QMath.Min(result.Order, truncation));
    }

    private int TermsFor(PS u)
    {
        if (!u.HasTerms) return 1;
        var u0 = u.LeadExp.ToDouble();
        if (u0 <= 0) return _maxTerms;
        double target = Math.Min(_maxOrder.ToDouble(), 64);
        int k = (int)Math.Ceiling(target / u0) + 1;
        return Math.Clamp(k, 1, _maxTerms + 2);
    }

    public PS Inverse(PS a)
    {
        if (!a.HasTerms) throw new CasLimitException("Not enough terms to invert a series");
        var (e, c) = a.Terms[0];
        var cInv = Algebra.Pow(c, Expr.MinusOne);
        var u = Normalize(a.Terms.Skip(1).Select(t => (t.E - e, Algebra.Mul(t.C, cInv))), a.Order - e);
        var inv = Compose(k => k % 2 == 0 ? Expr.One : Expr.MinusOne, u, TermsFor(u));
        return Shift(Scale(inv, cInv), -e);
    }

    /// <summary>a^r for an exponent free of t (rational, or symbolic when a tends to a nonzero constant).</summary>
    public PS? Pow(PS a, Expr r)
    {
        if (!a.HasTerms) return r is NumberExpr { Value.IsPositiveReal: true } && a.Order > 0 ? new PS([], a.Order * ((NumberExpr)r).Value.ReQ) : null;
        var (e, c) = a.Terms[0];
        BigRational? rq = r is NumberExpr { Value.IsExactRational: true } rn ? rn.Value.ReQ : null;
        if (rq is null && !e.IsZero) return null;
        if (rq is { } q && q.IsInteger && q.Num >= 0 && q.Num <= 64)
        {
            var acc = PS.Const(Expr.One);
            for (int i = 0; i < (int)q.Num; i++) acc = Mul(acc, a);
            return acc;
        }
        var cInv = Algebra.Pow(c, Expr.MinusOne);
        var u = Normalize(a.Terms.Skip(1).Select(t => (t.E - e, Algebra.Mul(t.C, cInv))), a.Order - e);
        var series = Compose(k => GeneralBinomial(r, k), u, TermsFor(u));
        var cr = Algebra.Pow(c, r);
        if (ContainsImaginary(cr) && !ContainsImaginary(c)) throw new CasLimitException("The power is not real here");
        var shift = rq is { } qq ? e * qq : BigRational.Zero;
        return Shift(Scale(series, cr), shift);
    }

    private static bool ContainsImaginary(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsReal,
        _ => e.Children.Any(ContainsImaginary),
    };

    private static Expr GeneralBinomial(Expr r, int k)
    {
        Expr acc = Expr.One;
        for (int i = 0; i < k; i++) acc = Algebra.Mul(acc, Algebra.Sub(r, Expr.Int(i)));
        return Algebra.Div(acc, Algebra.Q(new BigRational(IntegerMath.Factorial(k))));
    }

    // ------------------------------------------------------------------ asymptotic arithmetic

    /// <summary>Sign of the limit of a coefficient as ln t → -∞ (null when unknown).</summary>
    public static int? CoefficientSign(Expr c)
    {
        if (!c.ContainsSymbol(LogT.Name)) return CasEnv.SignOf(c);
        var l = LimitInLog(c);
        if (l is ConstantExpr { Constant: ConstantKind.Infinity }) return 1;
        if (l is ConstantExpr { Constant: ConstantKind.NegativeInfinity }) return -1;
        if (l is null) return null;
        var s = CasEnv.SignOf(l);
        if (s is 0)
        {
            // Tends to 0 through values of a definite sign: use the leading behaviour in L.
            var rat = Simplifier.Rational(c, LogT.Name);
            if (rat is null) return null;
            var (km, r) = rat.Value;
            int v = km.IndexOf(LogT);
            var ln = r.Num.LeadingCoefficient(v);
            var ld = r.Den.LeadingCoefficient(v);
            int degDiff = r.Num.Degree(v) - r.Den.Degree(v);
            var sn = CasEnv.SignOf(km.ToExpr(ln));
            var sd = CasEnv.SignOf(km.ToExpr(ld));
            if (sn is null || sd is null) return null;
            int sign = sn.Value * sd.Value;
            return Math.Abs(degDiff) % 2 == 1 ? -sign : sign;
        }
        return s;
    }

    /// <summary>Limit of an expression rational in L as L → -∞.</summary>
    public static Expr? LimitInLog(Expr c)
    {
        if (!c.ContainsSymbol(LogT.Name)) return c;
        var rat = Simplifier.Rational(c, LogT.Name);
        if (rat is null) return null;
        var (km, r) = rat.Value;
        int v = km.IndexOf(LogT);
        if (km.Kernels.Skip(1).Any(k => k.ContainsSymbol(LogT.Name))) return null;
        int dn = r.Num.Degree(v), dd = r.Den.Degree(v);
        var ln = km.ToExpr(r.Num.LeadingCoefficient(v));
        var ld = km.ToExpr(r.Den.LeadingCoefficient(v));
        if (dn < dd) return Expr.Zero;
        if (dn == dd) return Algebra.Div(ln, ld);
        var s = CasEnv.SignOf(Algebra.Div(ln, ld));
        if (s is null or 0) return null;
        int sign = (dn - dd) % 2 == 1 ? -s.Value : s.Value;
        return sign > 0 ? ConstantExpr.Infinity : ConstantExpr.NegativeInfinity;
    }

    /// <summary>+1 when exp(G) → ∞, -1 when it → 0.</summary>
    private static int? ScaleDirection(PS g)
    {
        if (!g.HasTerms) return 0;
        return CoefficientSign(g.LeadCoef);
    }

    private PS? Difference(PS? a, PS? b)
    {
        if (a is null && b is null) return null;
        var d = Add(a ?? PS.Zero, Neg(b ?? PS.Zero));
        return d.HasTerms ? d : null;
    }

    /// <summary>The limit direction of an asymptotic value: true when it tends to 0.</summary>
    private static bool TendsToZero(Asym a)
    {
        if (a.Bounded) return false;
        if (a.G is not null) return ScaleDirection(a.G) == -1;
        if (!a.P.HasTerms) return a.P.Order > 0;
        return a.P.LeadExp > 0;
    }

    private static bool TendsToInfinity(Asym a)
    {
        if (a.Bounded) return false;
        if (a.G is not null) return ScaleDirection(a.G) == 1;
        return a.P.HasTerms && (a.P.LeadExp < 0 || (a.P.LeadExp.IsZero && LimitInLog(a.P.LeadCoef) is ConstantExpr { IsInfinite: true }));
    }

    public Asym AddA(Asym a, Asym b)
    {
        if (a.Bounded || b.Bounded)
        {
            if (!a.Bounded && TendsToInfinity(a)) return a;
            if (!b.Bounded && TendsToInfinity(b)) return b;
            return Asym.Oscillating;
        }
        var d = Difference(a.G, b.G);
        if (d is null) return new Asym(Add(a.P, b.P), a.G);
        var dir = ScaleDirection(d);
        if (dir == 1) return a;
        if (dir == -1) return b;
        throw new CasLimitException("Cannot compare the growth of the terms");
    }

    public Asym MulA(Asym a, Asym b)
    {
        if (a.Bounded || b.Bounded)
        {
            var other = a.Bounded ? b : a;
            if (a.Bounded && b.Bounded) return Asym.Oscillating;
            if (TendsToZero(other)) return new Asym(new PS([], QMath.Min(other.P.Order, other.P.HasTerms ? other.P.LeadExp : other.P.Order)));
            if (other.P.HasTerms && !TendsToInfinity(other)) return Asym.Oscillating;
            throw new CasLimitException("An oscillating factor times an unbounded one");
        }
        PS? g = null;
        if (a.G is not null || b.G is not null)
        {
            var sum = Add(a.G ?? PS.Zero, b.G ?? PS.Zero);
            g = sum.HasTerms ? sum : null;
        }
        return new Asym(Mul(a.P, b.P), g);
    }

    public Asym InverseA(Asym a)
    {
        if (a.Bounded) throw new CasLimitException("Cannot invert an oscillating value");
        return new Asym(Inverse(a.P), a.G is null ? null : Neg(a.G));
    }

    public Asym ExpA(Asym a)
    {
        if (a.Bounded) return Asym.Oscillating;
        if (a.G is not null) throw new CasLimitException("Iterated exponentials are not supported");
        var neg = a.P.Terms.Where(t => t.E < 0).ToList();
        var zero = a.P.ConstantTerm;
        var pos = new PS(a.P.Terms.Where(t => t.E > 0).ToList(), a.P.Order);
        if (neg.Any(t => t.C.ContainsSymbol(LogT.Name))) throw new CasLimitException("Unsupported exponential growth");
        BigRational tShift = BigRational.Zero;
        if (zero.ContainsSymbol(LogT.Name))
        {
            // exp(a·L + b) = t^a · e^b
            var poly = ExprPoly.From(zero, LogT.Name);
            if (poly is null || poly.Degree > 1 || poly[1] is not NumberExpr { Value.IsExactRational: true } an)
                throw new CasLimitException("Unsupported logarithmic exponent");
            tShift = an.Value.ReQ;
            zero = poly[0];
        }
        if (!pos.HasTerms && pos.Order <= 0) throw new CasLimitException("Not enough terms");
        var series = Compose(k => Algebra.Q(new BigRational(BigInteger.One, IntegerMath.Factorial(k))), pos, TermsFor(pos));
        var p = Shift(Scale(series, Algebra.Pow(ConstantExpr.E, zero)), tShift);
        PS? g = neg.Count > 0 ? new PS(neg, PS.Infinite) : null;
        return new Asym(p, g);
    }

    public Asym LnA(Asym a)
    {
        if (a.Bounded) throw new CasLimitException("ln of an oscillating value");
        var p = a.P;
        if (!p.HasTerms) throw new CasLimitException("Not enough terms for ln");
        var (e, c) = p.Terms[0];
        if (c.ContainsSymbol(LogT.Name)) throw new CasLimitException("Iterated logarithms are not supported");
        var sign = CasEnv.SignOf(c);
        if (sign is not 1) throw new CasLimitException("ln of a value that is not positive");
        var cInv = Algebra.Pow(c, Expr.MinusOne);
        var u = Normalize(p.Terms.Skip(1).Select(t => (t.E - e, Algebra.Mul(t.C, cInv))), p.Order - e);
        var log1p = Compose(k => k == 0 ? Expr.Zero : Expr.Rational(k % 2 == 1 ? 1 : -1, k), u, TermsFor(u));
        var head = Algebra.Add(CasEnv.Fn("ln", c), Algebra.Mul(Algebra.Q(e), LogT));
        var result = Add(PS.Const(head), log1p);
        if (a.G is not null) result = Add(result, a.G);
        return Asym.Of(result);
    }

    // ------------------------------------------------------------------ expansion of expressions

    public Asym Expand(Expr e)
    {
        using var _ = CasEnv.Deeper(200);
        if (!e.ContainsSymbol(_t)) return Asym.Of(PS.Const(e));
        switch (e)
        {
            case SymbolExpr:
                return Asym.Of(PS.Monomial(Expr.One, BigRational.One));
            case AddExpr add:
            {
                Asym? acc = null;
                foreach (var term in add.Terms)
                {
                    var s = Expand(term);
                    acc = acc is null ? s : AddA(acc, s);
                }
                return acc!;
            }
            case MulExpr mul:
            {
                Asym? acc = null;
                foreach (var f in mul.Factors)
                {
                    var s = Expand(f);
                    acc = acc is null ? s : MulA(acc, s);
                }
                return acc!;
            }
            case PowExpr p:
                return ExpandPow(p);
            case CallExpr c:
                return ExpandCall(c);
            default:
                throw new CasLimitException("Cannot expand this expression in a series");
        }
    }

    private Asym ExpandPow(PowExpr p)
    {
        if (p.Base is ConstantExpr { Constant: ConstantKind.E }) return ExpA(Expand(p.Exponent));
        if (!p.Exponent.ContainsSymbol(_t))
        {
            var b = Expand(p.Base);
            if (b.Bounded) throw new CasLimitException("Power of an oscillating value");
            var ex = p.Exponent;
            PS? g = null;
            if (b.G is not null)
            {
                if (ex is not NumberExpr) throw new CasLimitException("Unsupported power");
                g = Scale(b.G, ex);
            }
            if (ex is NumberExpr { Value.IsExactInteger: true } ni && ni.Value.IsNegativeReal)
            {
                var inv = Inverse(b.P);
                var pos = Pow(inv, Algebra.Neg(ex)) ?? throw new CasLimitException("Unsupported power");
                return new Asym(pos, g);
            }
            var r = Pow(b.P, ex);
            if (r is null)
            {
                if (b.G is null) return ExpA(MulA(Asym.Of(PS.Const(ex)), LnA(b)));
                throw new CasLimitException("Unsupported power");
            }
            return new Asym(r, g);
        }
        // u^v = e^(v ln u)
        return ExpA(MulA(Expand(p.Exponent), LnA(Expand(p.Base))));
    }

    private Asym ExpandCall(CallExpr c)
    {
        if (c.Args.Length == 2 && c.Name == "log")
        {
            if (c.Args[1].ContainsSymbol(_t)) throw new CasLimitException("Variable logarithm base");
            return MulA(LnA(Expand(c.Args[0])), Asym.Of(PS.Const(Algebra.Pow(CasEnv.Fn("ln", c.Args[1]), Expr.MinusOne))));
        }
        if (c.Args.Length != 1) throw new CasLimitException($"Cannot expand {c.Name} in a series");
        var a = Expand(c.Args[0]);
        switch (c.Name)
        {
            case "ln":
                return LnA(a);
            case "log":
                return MulA(LnA(a), Asym.Of(PS.Const(Algebra.Pow(CasEnv.Fn("ln", Expr.Int(10)), Expr.MinusOne))));
            case "log2":
                return MulA(LnA(a), Asym.Of(PS.Const(Algebra.Pow(CasEnv.Fn("ln", Expr.Int(2)), Expr.MinusOne))));
            case "exp":
                return ExpA(a);
            case "sinh" or "cosh" or "tanh" or "coth" or "sech" or "csch" when !Converges(a):
                return Hyperbolic(c.Name, a);
            case "sin" or "cos" or "tan" or "sec" or "csc" or "cot" when !Converges(a):
                return Asym.Oscillating;
            case "atan" or "acot" when !Converges(a):
            {
                // atan(A) = ±π/2 - atan(1/A)
                int? s = a.G is not null ? CoefficientSign(a.P.LeadCoef) : CoefficientSign(a.P.LeadCoef);
                if (s is null) throw new CasLimitException("Unknown sign");
                var inv = InverseA(a);
                var at = Function("atan", inv);
                var half = Algebra.Mul(Expr.Rational(s.Value, 2), ConstantExpr.Pi);
                var atanA = AddA(Asym.Of(PS.Const(half)), MulA(Asym.Of(PS.Const(Expr.MinusOne)), at));
                if (c.Name == "atan") return atanA;
                return AddA(Asym.Of(PS.Const(Algebra.Mul(Expr.HalfExpr, ConstantExpr.Pi))), MulA(Asym.Of(PS.Const(Expr.MinusOne)), atanA));
            }
            case "abs":
            {
                int? s = LeadSign(a);
                if (s is null) throw new CasLimitException("Unknown sign inside |...|");
                return s.Value >= 0 ? a : MulA(Asym.Of(PS.Const(Expr.MinusOne)), a);
            }
            case "sign":
            {
                int? s = LeadSign(a);
                if (s is null) throw new CasLimitException("Unknown sign");
                return Asym.Of(PS.Const(Expr.Int(s.Value)));
            }
            case "floor" or "ceil":
                return FloorCeil(c.Name == "floor", a);
        }
        if (!Converges(a)) throw new CasLimitException($"{c.Name} of a divergent argument");
        return Function(c.Name, a);
    }

    private static bool Converges(Asym a) => !a.Bounded && a.G is null && (!a.P.HasTerms || (a.P.LeadExp >= 0 && !a.P.ConstantTerm.ContainsSymbol(LogT.Name)));

    private static int? LeadSign(Asym a)
    {
        if (a.Bounded) return null;
        if (!a.P.HasTerms) return null;
        return CoefficientSign(a.P.LeadCoef);
    }

    private Asym FloorCeil(bool floor, Asym a)
    {
        if (!Converges(a)) throw new CasLimitException("floor of a divergent value");
        var c0 = a.P.ConstantTerm;
        var v = CasEnv.Numeric(c0);
        if (v is null || !v.Value.IsReal) throw new CasLimitException("floor of a symbolic value");
        double d = v.Value.Re;
        bool integer = c0 is NumberExpr { Value.IsExactInteger: true };
        if (!integer)
        {
            if (Math.Abs(d - Math.Round(d)) < 1e-12) throw new CasLimitException("Cannot decide floor");
            return Asym.Of(PS.Const(Expr.Int((long)(floor ? Math.Floor(d) : Math.Ceiling(d)))));
        }
        var rest = new PS(a.P.Terms.Where(t => !t.E.IsZero).ToList(), a.P.Order);
        long n = (long)d;
        if (!rest.HasTerms)
        {
            if (rest.Order >= PS.Infinite) return Asym.Of(PS.Const(Expr.Int(n)));
            throw new CasLimitException("Not enough terms");
        }
        var s = CoefficientSign(rest.LeadCoef);
        if (s is null) throw new CasLimitException("Unknown sign");
        long value = floor ? (s > 0 ? n : n - 1) : (s < 0 ? n : n + 1);
        return Asym.Of(PS.Const(Expr.Int(value)));
    }

    private Asym Hyperbolic(string name, Asym a)
    {
        var ea = ExpA(a);
        var em = ExpA(MulA(Asym.Of(PS.Const(Expr.MinusOne)), a));
        var half = Asym.Of(PS.Const(Expr.HalfExpr));
        var minusOne = Asym.Of(PS.Const(Expr.MinusOne));
        var sinh = MulA(half, AddA(ea, MulA(minusOne, em)));
        var cosh = MulA(half, AddA(ea, em));
        return name switch
        {
            "sinh" => sinh,
            "cosh" => cosh,
            "tanh" => MulA(sinh, InverseA(cosh)),
            "coth" => MulA(cosh, InverseA(sinh)),
            "sech" => InverseA(cosh),
            _ => InverseA(sinh),
        };
    }

    /// <summary>f(c0 + U) for an analytic function at c0, by Taylor expansion.</summary>
    private Asym Function(string name, Asym a)
    {
        var p = a.P;
        var c0 = p.ConstantTerm;
        var u = new PS(p.Terms.Where(t => !t.E.IsZero).ToList(), p.Order);
        if (!u.HasTerms && u.Order <= 0) throw new CasLimitException("Not enough terms");
        int count = TermsFor(u);
        switch (name)
        {
            case "sin" or "cos":
            {
                var s0 = CasEnv.Fn("sin", c0);
                var k0 = CasEnv.Fn("cos", c0);
                var sinU = Compose(k => k % 2 == 1 ? Signed((k - 1) / 2, k) : Expr.Zero, u, count);
                var cosU = Compose(k => k % 2 == 0 ? Signed(k / 2, k) : Expr.Zero, u, count);
                var result = name == "sin"
                    ? Add(Scale(cosU, s0), Scale(sinU, k0))
                    : Add(Scale(cosU, k0), Neg(Scale(sinU, s0)));
                return Asym.Of(result);
            }
            case "tan":
                return MulA(Function("sin", a), InverseA(Function("cos", a)));
            case "cot":
                return MulA(Function("cos", a), InverseA(Function("sin", a)));
            case "sec":
                return InverseA(Function("cos", a));
            case "csc":
                return InverseA(Function("sin", a));
            case "sinh" or "cosh" or "tanh" or "coth" or "sech" or "csch":
                return Hyperbolic(name, a);
            case "exp":
                return ExpA(a);
            case "ln":
                return LnA(a);
        }
        // Generic Taylor expansion from symbolic derivatives.
        var y = CasEnv.Temp();
        Expr f = new CallExpr(name, [y]);
        var coeffs = new List<Expr>();
        Expr fact = Expr.One;
        for (int k = 0; k <= count; k++)
        {
            CasEnv.Check();
            if (k > 0) fact = Algebra.Mul(fact, Expr.Int(k));
            var value = CasEnv.TrySubst(f, y.Name, c0);
            if (value is null || value is ConstantExpr { IsInfinite: true } or ConstantExpr { Constant: ConstantKind.Undefined } || value.ContainsSymbol(y.Name))
                throw new CasLimitException($"{name} is not analytic here");
            if (value is CallExpr { Name: "diff" }) throw new CasLimitException($"No derivative rule for {name}");
            coeffs.Add(Algebra.Div(value, fact));
            if (k < count) f = Simplifier.Tidy(Differentiator.D(f, y.Name));
        }
        return Asym.Of(Compose(k => k < coeffs.Count ? coeffs[k] : Expr.Zero, u, count));
    }

    private static Expr Signed(int half, int k)
    {
        var v = new BigRational(BigInteger.One, IntegerMath.Factorial(k));
        return Algebra.Q(half % 2 == 0 ? v : -v);
    }
}
