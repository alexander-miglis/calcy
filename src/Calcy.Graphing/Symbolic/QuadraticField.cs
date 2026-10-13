using System.Numerics;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Graphing.Symbolic;

/// <summary>
/// Exact arithmetic in Q(√c): numbers a + b√c with rational a and b. Used to prove roots such as 1 + √2 of
/// polynomial and rational expressions, which the session leaves unexpanded.
/// </summary>
internal readonly record struct QuadraticNumber(BigRational A, BigRational B)
{
    public bool IsZero => A.IsZero && B.IsZero;
}

/// <summary>Evaluates rational expressions exactly in Q(√c).</summary>
internal static class QuadraticField
{
    /// <summary>The square-free integer c of the first √c in <paramref name="e"/>, or 0.</summary>
    public static int RadicandOf(Expr e)
    {
        if (e is PowExpr { Base: NumberExpr { Value.IsExactInteger: true } n, Exponent: NumberExpr { Value.IsExactRational: true } ex } &&
            ex.Value.ReQ.Den == 2 && n.Value.Re is > 1 and < 1_000_000)
            return (int)n.Value.Re;
        foreach (var c in e.Children)
        {
            int r = RadicandOf(c);
            if (r != 0) return r;
        }
        return 0;
    }

    /// <summary>Value of <paramref name="e"/> with <paramref name="variable"/> bound to <paramref name="x"/>, or null when outside the supported forms.</summary>
    public static QuadraticNumber? Eval(Expr e, int c, string? variable, QuadraticNumber? x)
    {
        switch (e)
        {
            case NumberExpr { Value.IsExactRational: true } n:
                return new QuadraticNumber(n.Value.ReQ, BigRational.Zero);
            case SymbolExpr s when s.Name == variable && x is not null:
                return x;
            case AddExpr a:
            {
                var acc = new QuadraticNumber(BigRational.Zero, BigRational.Zero);
                foreach (var t in a.Terms)
                {
                    var v = Eval(t, c, variable, x);
                    if (v is null) return null;
                    acc = new QuadraticNumber(acc.A + v.Value.A, acc.B + v.Value.B);
                }
                return acc;
            }
            case MulExpr m:
            {
                var acc = new QuadraticNumber(BigRational.One, BigRational.Zero);
                foreach (var f in m.Factors)
                {
                    var v = Eval(f, c, variable, x);
                    if (v is null) return null;
                    acc = Mul(acc, v.Value, c);
                }
                return acc;
            }
            case PowExpr { Exponent: NumberExpr { Value.IsExactRational: true } ex } p:
            {
                var q = ex.Value.ReQ;
                if (q.IsInteger && BigInteger.Abs(q.Num) <= 64)
                {
                    var b = Eval(p.Base, c, variable, x);
                    if (b is null) return null;
                    return Pow(b.Value, (int)q.Num, c);
                }
                if (q.Den == 2 && BigInteger.Abs(q.Num) == 1 && p.Base is NumberExpr { Value.IsExactRational: true } rb)
                {
                    var root = Sqrt(rb.Value.ReQ, c);
                    if (root is null) return null;
                    return q.Num.Sign > 0 ? root : Inverse(root.Value, c);
                }
                return null;
            }
            default:
                return null;
        }
    }

    private static QuadraticNumber Mul(QuadraticNumber u, QuadraticNumber v, int c) =>
        new(u.A * v.A + new BigRational(c) * u.B * v.B, u.A * v.B + u.B * v.A);

    private static QuadraticNumber? Inverse(QuadraticNumber u, int c)
    {
        var den = u.A * u.A - new BigRational(c) * u.B * u.B;
        if (den.IsZero) return null;
        return new QuadraticNumber(u.A / den, -u.B / den);
    }

    private static QuadraticNumber? Pow(QuadraticNumber u, int n, int c)
    {
        if (n < 0)
        {
            var inv = Inverse(u, c);
            if (inv is null) return null;
            u = inv.Value;
            n = -n;
        }
        var acc = new QuadraticNumber(BigRational.One, BigRational.Zero);
        var b = u;
        while (n > 0)
        {
            if ((n & 1) == 1) acc = Mul(acc, b, c);
            b = Mul(b, b, c);
            n >>= 1;
        }
        return acc;
    }

    /// <summary>√r as s or s√c with rational s, or null.</summary>
    private static QuadraticNumber? Sqrt(BigRational r, int c)
    {
        if (r.IsNegative) return null;
        var s = RationalSqrt(r);
        if (s is not null) return new QuadraticNumber(s.Value, BigRational.Zero);
        var t = RationalSqrt(r / new BigRational(c));
        if (t is not null) return new QuadraticNumber(BigRational.Zero, t.Value);
        return null;
    }

    private static BigRational? RationalSqrt(BigRational r)
    {
        var n = IntSqrt(r.Num);
        var d = IntSqrt(r.Den);
        if (n is null || d is null) return null;
        return new BigRational(n.Value, d.Value);
    }

    private static BigInteger? IntSqrt(BigInteger v)
    {
        if (v.Sign < 0) return null;
        if (v.IsZero) return BigInteger.Zero;
        var x = (BigInteger)Math.Sqrt((double)v);
        for (int i = 0; i < 4; i++) x = (x + v / x) / 2;
        while (x * x > v) x--;
        while ((x + 1) * (x + 1) <= v) x++;
        return x * x == v ? x : null;
    }

    /// <summary>The value as an expression a + b√c.</summary>
    public static Expr ToExpr(QuadraticNumber q, int c) =>
        Algebra.Add(Algebra.Q(q.A), Algebra.Mul(Algebra.Q(q.B), Algebra.Sqrt(Expr.Int(c))));
}
