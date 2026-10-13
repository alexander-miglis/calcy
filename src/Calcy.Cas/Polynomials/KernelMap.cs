using System.Numerics;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Polynomials;

/// <summary>
/// The "variables" of a polynomial view of an expression: symbols and opaque subexpressions (kernels) such as sin(x),
/// e^x, √x or π. Powers are normalized so related kernels share one variable: e^(2x) is (e^x)², x^(3/2) is (√x)³.
/// </summary>
internal sealed class KernelMap
{
    private readonly List<Expr> _kernels = [];
    private readonly Dictionary<Expr, int> _index = [];

    public int Count => _kernels.Count;
    public Expr this[int i] => _kernels[i];
    public IReadOnlyList<Expr> Kernels => _kernels;

    public int IndexOf(Expr kernel) => _index.TryGetValue(kernel, out var i) ? i : -1;

    public int IndexOf(string symbol) => IndexOf(new SymbolExpr(symbol));

    public int Add(Expr kernel)
    {
        if (_index.TryGetValue(kernel, out var i)) return i;
        _index[kernel] = _kernels.Count;
        _kernels.Add(kernel);
        return _kernels.Count - 1;
    }

    /// <summary>
    /// Collects the kernels of the expressions and orders them: the given main variables first, then other symbols
    /// alphabetically, then the remaining kernels in canonical order. Returns null when an expression has no rational
    /// structure (lists, relations, infinities).
    /// </summary>
    public static KernelMap? Build(IEnumerable<Expr> exprs, params string[] mainVariables)
    {
        var found = new List<Expr>();
        var seen = new HashSet<Expr>();
        foreach (var e in exprs)
            if (!Collect(e, found, seen)) return null;
        var km = new KernelMap();
        foreach (var v in mainVariables) km.Add(new SymbolExpr(v));
        foreach (var s in found.OfType<SymbolExpr>().OrderBy(s => s.Name, StringComparer.Ordinal)) km.Add(s);
        var others = found.Where(k => k is not SymbolExpr).ToList();
        others.Sort(CompareKernels);
        foreach (var k in others) km.Add(k);
        return km;
    }

    private static int CompareKernels(Expr a, Expr b)
    {
        int ra = a is ConstantExpr ? 0 : 1, rb = b is ConstantExpr ? 0 : 1;
        if (ra != rb) return ra.CompareTo(rb);
        return CanonicalOrder.Compare(a, b);
    }

    private static bool Collect(Expr e, List<Expr> found, HashSet<Expr> seen)
    {
        switch (e)
        {
            case NumberExpr n:
                if (n.Value.IsExactRational) return true;
                if (n.Value.IsExact)
                {
                    if (seen.Add(Expr.ImaginaryUnit)) found.Add(Expr.ImaginaryUnit);
                    return true;
                }
                if (seen.Add(e)) found.Add(e);
                return true;
            case ConstantExpr c:
                if (c.IsInfinite || c.Constant == ConstantKind.Undefined) return false;
                if (seen.Add(e)) found.Add(e);
                return true;
            case AddExpr or MulExpr:
                foreach (var t in e.Children)
                    if (!Collect(t, found, seen)) return false;
                return true;
            case PowExpr p when p.Exponent is NumberExpr { Value.IsExactInteger: true }:
                return Collect(p.Base, found, seen);
            case PowExpr:
                foreach (var (k, _) in Atomize(e))
                {
                    if (k is NumberExpr { Value.IsExactRational: true }) continue;
                    if (seen.Add(k)) found.Add(k);
                }
                return true;
            case SymbolExpr or CallExpr:
                if (seen.Add(e)) found.Add(e);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Writes a non-integer power as a product of kernel powers: e^(x+1) = (e^x)·e, e^(-2x) = (e^x)^-2,
    /// x^(3/2) = (√x)^3. Kernels that are rational numbers stand for themselves.
    /// </summary>
    public static List<(Expr Kernel, BigInteger Power)> Atomize(Expr e)
    {
        var result = new List<(Expr, BigInteger)>();
        if (e is not PowExpr p)
        {
            result.Add((e, BigInteger.One));
            return result;
        }
        var b = p.Base;
        var ex = p.Exponent;
        bool positiveConstantBase = b is ConstantExpr { Constant: ConstantKind.E or ConstantKind.Pi } || b is NumberExpr { Value.IsExactRational: true, Value.IsPositiveReal: true };
        if (ex is AddExpr sum && positiveConstantBase)
        {
            foreach (var t in sum.Terms)
            {
                var part = Algebra.Pow(b, t);
                if (part is NumberExpr { Value.IsExactRational: true }) result.Add((part, BigInteger.One));
                else if (part is PowExpr) result.AddRange(Atomize(part));
                else if (part is MulExpr pm)
                {
                    foreach (var f in pm.Factors) result.AddRange(Atomize(f));
                }
                else result.Add((part, BigInteger.One));
            }
            return result;
        }
        if (ex is NumberExpr { Value.IsExactRational: true } q)
        {
            var r = q.Value.ReQ;
            if (r.IsInteger)
            {
                result.Add((b, r.Num));
                return result;
            }
            var kernel = new PowExpr(b, Algebra.Q(new BigRational(BigInteger.One, r.Den)));
            result.Add((kernel, r.Num));
            return result;
        }
        var (coef, rest) = Algebra.SplitCoefficient(ex);
        if (coef.IsExactRational && !coef.IsOne && BigInteger.Abs(coef.ReQ.Num) < 10_000)
        {
            var c = coef.ReQ;
            var inner = c.Den.IsOne ? rest : Algebra.Mul(rest, Algebra.Q(new BigRational(BigInteger.One, c.Den)));
            result.Add((Algebra.Pow(b, inner), c.Num));
            return result;
        }
        result.Add((e, BigInteger.One));
        return result;
    }

    // ------------------------------------------------------------------ conversion

    /// <summary>Converts an expression to a rational function in the kernels, or null when it does not fit.</summary>
    public RatFunc? ToRat(Expr e)
    {
        CasEnv.Check();
        switch (e)
        {
            case NumberExpr n:
                if (n.Value.IsExactRational) return RatFunc.Constant(Count, n.Value.ReQ);
                if (n.Value.IsExact)
                {
                    int iu = IndexOf(Expr.ImaginaryUnit);
                    if (iu < 0) return null;
                    var re = RatFunc.Constant(Count, n.Value.ReQ);
                    var im = RatFunc.FromPoly(MPoly.Var(Count, iu).Scale(n.Value.ImQ));
                    return re + im;
                }
                return Var(e);
            case ConstantExpr or SymbolExpr or CallExpr:
                return Var(e);
            case AddExpr a:
            {
                RatFunc? acc = null;
                foreach (var t in a.Terms)
                {
                    var r = ToRat(t);
                    if (r is null) return null;
                    acc = acc is null ? r : acc + r;
                }
                return acc;
            }
            case MulExpr m:
            {
                RatFunc? acc = null;
                foreach (var f in m.Factors)
                {
                    var r = ToRat(f);
                    if (r is null) return null;
                    acc = acc is null ? r : acc * r;
                }
                return acc;
            }
            case PowExpr p when p.Exponent is NumberExpr { Value.IsExactInteger: true } ie:
            {
                var b = ToRat(p.Base);
                if (b is null) return null;
                var k = ie.Value.ToBigInteger();
                if (BigInteger.Abs(k) > 2000) return null;
                return b.Pow((int)k);
            }
            case PowExpr:
            {
                RatFunc? acc = null;
                foreach (var (kernel, power) in Atomize(e))
                {
                    RatFunc? r;
                    if (kernel is NumberExpr { Value.IsExactRational: true } kn) r = RatFunc.Constant(Count, kn.Value.ReQ);
                    else r = Var(kernel);
                    if (r is null) return null;
                    if (BigInteger.Abs(power) > 2000) return null;
                    r = r.Pow((int)power);
                    acc = acc is null ? r : acc * r;
                }
                return acc;
            }
            default:
                return null;
        }
    }

    private RatFunc? Var(Expr kernel)
    {
        int i = IndexOf(kernel);
        return i < 0 ? null : RatFunc.FromPoly(MPoly.Var(Count, i));
    }

    /// <summary>Converts to a polynomial, or null when the expression has denominators or does not fit.</summary>
    public MPoly? ToPoly(Expr e)
    {
        var r = ToRat(e);
        if (r is null || !r.Den.IsConstant) return null;
        return r.Num.Scale(r.Den.ConstantValue.Reciprocal());
    }

    /// <summary>Converts a polynomial back to a canonical expression.</summary>
    public Expr ToExpr(MPoly p)
    {
        if (p.IsZero) return Expr.Zero;
        var terms = new List<Expr>(p.TermCount);
        foreach (var (m, c) in p.Terms)
        {
            var factors = new List<Expr> { Algebra.Q(c) };
            for (int i = 0; i < p.Vars; i++)
                if (m[i] > 0) factors.Add(Algebra.Pow(_kernels[i], Expr.Int(m[i])));
            terms.Add(Algebra.Mul(factors));
        }
        return Algebra.Add(terms);
    }

    public Expr ToExpr(RatFunc r)
    {
        if (r.Den.IsConstant) return ToExpr(r.Num.Scale(r.Den.ConstantValue.Reciprocal()));
        return Algebra.Mul(ToExpr(r.Num), Algebra.Pow(ToExpr(r.Den), Expr.MinusOne));
    }

    /// <summary>True when kernel i contains the symbol.</summary>
    public bool KernelContains(int i, string symbol) => _kernels[i].ContainsSymbol(symbol);
}

/// <summary>A quotient of polynomials over Q, kept reduced (gcd removed) with a normalized denominator.</summary>
internal sealed class RatFunc
{
    public MPoly Num { get; }
    public MPoly Den { get; }

    private RatFunc(MPoly num, MPoly den)
    {
        Num = num;
        Den = den;
    }

    public int Vars => Num.Vars;

    public static RatFunc Constant(int n, BigRational c) => new(MPoly.Constant(n, c), MPoly.Constant(n, BigRational.One));

    public static RatFunc FromPoly(MPoly p) => new(p, MPoly.Constant(p.Vars, BigRational.One));

    /// <summary>Builds num/den reduced to lowest terms.</summary>
    public static RatFunc Create(MPoly num, MPoly den)
    {
        if (den.IsZero) throw new DivideByZeroException("Division by zero");
        if (num.IsZero) return new RatFunc(num, MPoly.Constant(num.Vars, BigRational.One));
        if (den.IsConstant) return new RatFunc(num.Scale(den.ConstantValue.Reciprocal()), MPoly.Constant(num.Vars, BigRational.One));
        if (!num.IsConstant)
        {
            var g = MPoly.Gcd(num, den);
            if (!g.IsConstant)
            {
                num = num.DivExact(g)!;
                den = den.DivExact(g)!;
            }
        }
        var (c, prim) = den.RationalContent();
        return new RatFunc(num.Scale(c.Reciprocal()), prim);
    }

    public bool IsZero => Num.IsZero;
    public bool IsPolynomial => Den.IsConstant;

    public static RatFunc operator +(RatFunc a, RatFunc b)
    {
        if (a.IsPolynomial && b.IsPolynomial) return FromPoly(a.Num + b.Num);
        if (a.Den.StructurallyEquals(b.Den)) return Create(a.Num + b.Num, a.Den);
        return Create(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
    }

    public static RatFunc operator -(RatFunc a) => new(-a.Num, a.Den);
    public static RatFunc operator -(RatFunc a, RatFunc b) => a + -b;

    public static RatFunc operator *(RatFunc a, RatFunc b)
    {
        if (a.IsPolynomial && b.IsPolynomial) return FromPoly(a.Num * b.Num);
        return Create(a.Num * b.Num, a.Den * b.Den);
    }

    public static RatFunc operator /(RatFunc a, RatFunc b)
    {
        if (b.IsZero) throw new DivideByZeroException("Division by zero");
        return Create(a.Num * b.Den, a.Den * b.Num);
    }

    public RatFunc Pow(int k)
    {
        if (k == 0) return Constant(Vars, BigRational.One);
        if (k < 0)
        {
            if (IsZero) throw new DivideByZeroException("Division by zero");
            return Create(Den.Pow(-k), Num.Pow(-k));
        }
        return new RatFunc(Num.Pow(k), Den.Pow(k));
    }

    public RatFunc Extend(int n) => new(Num.Extend(n), Den.Extend(n));
}
