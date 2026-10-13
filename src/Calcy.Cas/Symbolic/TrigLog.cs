using System.Numerics;
using Calcy.Core.Expressions;

namespace Calcy.Cas.Symbolic;

/// <summary>Logarithm expansion, trigonometric expansion (sums and multiple angles) and collection (products to sums).</summary>
internal static class TrigLog
{
    // ------------------------------------------------------------------ logarithms

    /// <summary>ln(ab) = ln a + ln b, ln(a^n) = n ln a (also for log in any base).</summary>
    public static Expr ExpandLog(Expr e)
    {
        using var _ = CasEnv.Deeper();
        if (e.Children.Count == 0) return e;
        if (e is CallExpr { Name: "ln" or "log" } c && c.Args.Length is 1 or 2)
        {
            var arg = ExpandLog(c.Args[0]);
            var rest = c.Args.Skip(1).Select(ExpandLog).ToArray();
            return SplitLog(c.Name, arg, rest);
        }
        return CasEnv.Rebuild(e.WithChildren([.. e.Children.Select(ExpandLog)]));
    }

    private static Expr SplitLog(string name, Expr arg, Expr[] baseArg)
    {
        Expr Log(Expr x) => CasEnv.Fn(name, [x, .. baseArg]);
        switch (arg)
        {
            case MulExpr m:
                return Algebra.Add(m.Factors.Select(f => SplitLog(name, f, baseArg)).ToArray());
            case PowExpr p when !(p.Base is NumberExpr && p.Exponent is NumberExpr):
                return Algebra.Mul(p.Exponent, SplitLog(name, p.Base, baseArg));
            case NumberExpr { Value.IsExactRational: true } n when !n.Value.IsInteger && n.Value.IsPositiveReal:
            {
                var q = n.Value.ReQ;
                var num = q.Num.IsOne ? Expr.Zero : Log(Algebra.Q(q.Num));
                return Algebra.Sub(num, Log(Algebra.Q(q.Den)));
            }
            default:
                return Log(arg);
        }
    }

    // ------------------------------------------------------------------ trig expansion

    /// <summary>sin(a + b), cos(a + b), tan(a + b) and multiple angles sin(nx), cos(nx) in terms of sin and cos of the parts.</summary>
    public static Expr ExpandTrig(Expr e)
    {
        var r = ExpandTrigCore(e);
        return Expander.Expand(r);
    }

    private static Expr ExpandTrigCore(Expr e)
    {
        using var _ = CasEnv.Deeper();
        if (e.Children.Count == 0) return e;
        var mapped = e.WithChildren([.. e.Children.Select(ExpandTrigCore)]);
        if (mapped is CallExpr { Args.Length: 1 } c)
        {
            var u = c.Args[0];
            switch (c.Name)
            {
                case "sin": return Sin(u);
                case "cos": return Cos(u);
                case "tan":
                {
                    var s = Sin(u);
                    var co = Cos(u);
                    if (s is CallExpr { Name: "sin" } && co is CallExpr { Name: "cos" }) return mapped;
                    return Algebra.Div(s, co);
                }
                case "sec": return Algebra.Pow(Cos(u), Expr.MinusOne);
                case "csc": return Algebra.Pow(Sin(u), Expr.MinusOne);
                case "cot": return Algebra.Div(Cos(u), Sin(u));
                case "sinh": return Sinh(u);
                case "cosh": return Cosh(u);
            }
        }
        return CasEnv.Rebuild(mapped);
    }

    private static (Expr A, Expr B)? Split(Expr u)
    {
        if (u is AddExpr a && a.Terms.Length >= 2)
            return (a.Terms[0], Algebra.Add(a.Terms.Skip(1).ToArray()));
        var (coef, rest) = Algebra.SplitCoefficient(u);
        if (coef.IsExactInteger && coef.ToBigInteger() > 1 && coef.ToBigInteger() <= 20)
        {
            var n = coef.ToBigInteger();
            return (Algebra.Mul(Algebra.Q(n - BigInteger.One), rest), rest);
        }
        return null;
    }

    private static Expr Sin(Expr u)
    {
        var parts = Split(u);
        if (parts is null) return CasEnv.Fn("sin", u);
        var (a, b) = parts.Value;
        return Algebra.Add(Algebra.Mul(Sin(a), Cos(b)), Algebra.Mul(Cos(a), Sin(b)));
    }

    private static Expr Cos(Expr u)
    {
        var parts = Split(u);
        if (parts is null) return CasEnv.Fn("cos", u);
        var (a, b) = parts.Value;
        return Algebra.Sub(Algebra.Mul(Cos(a), Cos(b)), Algebra.Mul(Sin(a), Sin(b)));
    }

    private static Expr Sinh(Expr u)
    {
        var parts = Split(u);
        if (parts is null) return CasEnv.Fn("sinh", u);
        var (a, b) = parts.Value;
        return Algebra.Add(Algebra.Mul(Sinh(a), Cosh(b)), Algebra.Mul(Cosh(a), Sinh(b)));
    }

    private static Expr Cosh(Expr u)
    {
        var parts = Split(u);
        if (parts is null) return CasEnv.Fn("cosh", u);
        var (a, b) = parts.Value;
        return Algebra.Add(Algebra.Mul(Cosh(a), Cosh(b)), Algebra.Mul(Sinh(a), Sinh(b)));
    }

    // ------------------------------------------------------------------ trig collection

    private enum Kind { One, Sin, Cos, Sinh, Cosh }

    /// <summary>Products and powers of sin and cos (and sinh, cosh) become sums of single sines and cosines.</summary>
    public static Expr CollectTrig(Expr e)
    {
        using var _ = CasEnv.Deeper();
        switch (e)
        {
            case ListExpr or MatrixExpr or TupleExpr:
                return e.WithChildren([.. e.Children.Select(CollectTrig)]);
            case RelationExpr r:
                return new RelationExpr(r.Op, CollectTrig(r.Left), CollectTrig(r.Right));
        }
        var expanded = Expander.Expand(e);
        var terms = expanded is AddExpr a ? (IReadOnlyList<Expr>)a.Terms : [expanded];
        return Algebra.Add(terms.Select(LinearizeTerm).ToArray());
    }

    private static Expr LinearizeTerm(Expr term)
    {
        var factors = term is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [term];
        var others = new List<Expr>();
        var trig = new List<(Kind, Expr)>();
        foreach (var f in factors)
        {
            var (b, ex) = Algebra.SplitPower(f);
            if (b is CallExpr { Args.Length: 1 } c && KindOf(c.Name) is Kind k && ex is NumberExpr { Value.IsExactInteger: true } n
                && n.Value.IsPositiveReal && n.Value.ToBigInteger() <= 24)
            {
                for (int i = 0; i < (int)n.Value.ToBigInteger(); i++) trig.Add((k, c.Args[0]));
            }
            else others.Add(f);
        }
        if (trig.Count <= 1) return term;
        var sum = new List<(Expr Coef, Kind Kind, Expr Arg)> { (Expr.One, Kind.One, Expr.Zero) };
        foreach (var (k, arg) in trig)
        {
            CasEnv.Check();
            var next = new List<(Expr, Kind, Expr)>();
            foreach (var (coef, kind, a) in sum)
            {
                if (kind == Kind.One)
                {
                    next.Add((coef, k, arg));
                    continue;
                }
                var half = Algebra.Mul(Expr.HalfExpr, coef);
                var plus = Algebra.Add(a, arg);
                var minus = Algebra.Sub(a, arg);
                switch (kind, k)
                {
                    case (Kind.Cos, Kind.Cos):
                        next.Add((half, Kind.Cos, minus)); next.Add((half, Kind.Cos, plus)); break;
                    case (Kind.Sin, Kind.Sin):
                        next.Add((half, Kind.Cos, minus)); next.Add((Algebra.Neg(half), Kind.Cos, plus)); break;
                    case (Kind.Sin, Kind.Cos):
                        next.Add((half, Kind.Sin, plus)); next.Add((half, Kind.Sin, minus)); break;
                    case (Kind.Cos, Kind.Sin):
                        next.Add((half, Kind.Sin, plus)); next.Add((Algebra.Neg(half), Kind.Sin, minus)); break;
                    case (Kind.Cosh, Kind.Cosh):
                        next.Add((half, Kind.Cosh, plus)); next.Add((half, Kind.Cosh, minus)); break;
                    case (Kind.Sinh, Kind.Sinh):
                        next.Add((half, Kind.Cosh, plus)); next.Add((Algebra.Neg(half), Kind.Cosh, minus)); break;
                    case (Kind.Sinh, Kind.Cosh):
                        next.Add((half, Kind.Sinh, plus)); next.Add((half, Kind.Sinh, minus)); break;
                    case (Kind.Cosh, Kind.Sinh):
                        next.Add((half, Kind.Sinh, plus)); next.Add((Algebra.Neg(half), Kind.Sinh, minus)); break;
                    default:
                        return term;
                }
            }
            // Fold constants such as cos(0) = 1 back into the One kind so later factors multiply correctly.
            sum = [];
            foreach (var (coef, kind, a) in next)
            {
                var v = Value(kind, a);
                if (v is NumberExpr nv)
                {
                    if (!nv.Value.IsZero) sum.Add((Algebra.Mul(coef, nv), Kind.One, Expr.Zero));
                }
                else sum.Add((coef, kind, a));
            }
        }
        var rest = Algebra.Mul(others);
        return Algebra.Add(sum.Select(t => Algebra.Mul(rest, t.Coef, t.Kind == Kind.One ? Expr.One : Value(t.Kind, t.Arg))).ToArray());
    }

    private static Kind? KindOf(string name) => name switch
    {
        "sin" => Kind.Sin,
        "cos" => Kind.Cos,
        "sinh" => Kind.Sinh,
        "cosh" => Kind.Cosh,
        _ => null,
    };

    private static Expr Value(Kind k, Expr arg) => k switch
    {
        Kind.Sin => CasEnv.Fn("sin", arg),
        Kind.Cos => CasEnv.Fn("cos", arg),
        Kind.Sinh => CasEnv.Fn("sinh", arg),
        Kind.Cosh => CasEnv.Fn("cosh", arg),
        _ => Expr.One,
    };
}
