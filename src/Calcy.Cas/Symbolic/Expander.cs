using System.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Symbolic;

/// <summary>Full expansion of products and integer powers (multinomial theorem), distributing over sums.</summary>
internal static class Expander
{
    /// <summary>Upper bound on the number of terms an expansion may produce.</summary>
    public const int MaxTerms = 20_000;

    public static Expr Expand(Expr e)
    {
        using var _ = CasEnv.Deeper();
        switch (e)
        {
            case NumberExpr or ConstantExpr or SymbolExpr or StringExpr or BoolExpr:
                return e;
            case AddExpr a:
            {
                var terms = a.Terms.Select(Expand).ToList();
                CheckSize(terms.Sum(t => t is AddExpr s ? s.Terms.Length : 1));
                return Algebra.Add(terms);
            }
            case MulExpr m:
                return ExpandProduct([.. m.Factors.Select(Expand)]);
            case PowExpr p:
                return ExpandPow(Expand(p.Base), Expand(p.Exponent));
            case CallExpr c:
                if (CasModule.IsOwnFunction(c.Name)) return c;
                return CasEnv.Fn(c.Name, [.. c.Args.Select(Expand)]);
            case RelationExpr r:
                return new RelationExpr(r.Op, Expand(r.Left), Expand(r.Right));
            case LambdaExpr:
                return e;
            default:
                return e.WithChildren([.. e.Children.Select(Expand)]);
        }
    }

    private static void CheckSize(long terms)
    {
        if (terms > MaxTerms) throw new CasLimitException($"The expanded result would have more than {MaxTerms:N0} terms");
    }

    private static Expr ExpandPow(Expr b, Expr ex)
    {
        if (ex is NumberExpr { Value.IsExactInteger: true } n && b is AddExpr sum)
        {
            var k = n.Value.ToBigInteger();
            if (k > 1) return Multinomial(sum, k);
            return Algebra.Pow(b, ex);
        }
        var r = Algebra.Pow(b, ex);
        // (x(x + 1))^2 becomes x^2 (x + 1)^2: expand the pieces again.
        if (r is MulExpr rm && ex is NumberExpr { Value.IsExactInteger: true } && rm.Factors.Any(f => f is PowExpr { Base: AddExpr } || f is AddExpr))
            return ExpandProduct([.. rm.Factors.Select(Expand)]);
        return r;
    }

    private static Expr Multinomial(AddExpr sum, BigInteger power)
    {
        int t = sum.Terms.Length;
        if (power > 4000) throw new CasLimitException("The power is too large to expand");
        int n = (int)power;
        // Number of terms of (a1 + ... + at)^n is C(n + t - 1, t - 1).
        var count = IntegerMath.Binomial(n + t - 1, t - 1);
        CheckSize(count > MaxTerms ? MaxTerms + 1 : (long)count);
        var km = KernelMap.Build([sum]);
        if (km is not null)
        {
            var poly = km.ToPoly(sum);
            if (poly is not null) return km.ToExpr(poly.Pow(n));
        }
        Expr acc = sum;
        for (int i = 1; i < n; i++) acc = ExpandProduct([acc, sum]);
        return acc;
    }

    /// <summary>Distributes a product of expanded factors; factors with negative exponents stay as a common denominator.</summary>
    public static Expr ExpandProduct(List<Expr> factors)
    {
        var num = new List<Expr>();
        var den = new List<Expr>();
        foreach (var f in factors)
        {
            if (f is PowExpr { Exponent: NumberExpr ne } && ne.Value.IsNegativeReal) den.Add(f);
            else if (f is MulExpr fm)
            {
                foreach (var g in fm.Factors)
                {
                    if (g is PowExpr { Exponent: NumberExpr ge } && ge.Value.IsNegativeReal) den.Add(g);
                    else num.Add(g);
                }
            }
            else num.Add(f);
        }
        var terms = new List<Expr> { Expr.One };
        foreach (var f in num)
        {
            CasEnv.Check();
            var parts = f is AddExpr a ? (IReadOnlyList<Expr>)a.Terms : [f];
            CheckSize((long)terms.Count * parts.Count);
            var next = new List<Expr>(terms.Count * parts.Count);
            foreach (var t in terms)
                foreach (var p in parts)
                    next.Add(Algebra.Mul(t, p));
            terms = next;
        }
        var denominator = Algebra.Mul(den);
        if (denominator.IsOne) return Algebra.Add(terms);
        return Algebra.Add(terms.Select(t => Algebra.Mul(t, denominator)));
    }
}
