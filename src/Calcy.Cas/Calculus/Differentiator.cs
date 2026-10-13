using System.Collections.Immutable;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Cas.Calculus;

/// <summary>
/// Symbolic differentiation (radian semantics; angle units are handled by <see cref="CasEnv.Internalize"/>).
/// Unknown functions f(x) give f'(x); builtins without a rule stay as an unevaluated diff.
/// </summary>
internal static class Differentiator
{
    /// <summary>Optional observer that records which rule was applied (used for step-by-step explanations).</summary>
    [ThreadStatic] public static Action<string, Expr>? Trace;

    public static Expr Diff(Expr e, string x, int order)
    {
        var r = e;
        for (int i = 0; i < order; i++)
        {
            r = Simplifier.Tidy(D(r, x));
            CasEnv.Check();
        }
        return r;
    }

    public static Expr D(Expr e, string x)
    {
        using var _ = CasEnv.Deeper(300);
        switch (e)
        {
            case ListExpr or MatrixExpr or TupleExpr:
                return e.WithChildren([.. e.Children.Select(c => D(c, x))]);
            case RelationExpr r:
                return new RelationExpr(r.Op, D(r.Left, x), D(r.Right, x));
            case PiecewiseExpr pw:
                return new PiecewiseExpr([.. pw.Cases.Select(c => (c.Condition, D(c.Value, x)))], pw.Otherwise is null ? null : D(pw.Otherwise, x));
        }
        if (!e.ContainsSymbol(x)) return Expr.Zero;
        switch (e)
        {
            case SymbolExpr:
                return Expr.One;
            case AddExpr a:
                Trace?.Invoke("sum", e);
                return Algebra.Add(a.Terms.Select(t => D(t, x)).ToArray());
            case MulExpr m:
                return Product(m, x);
            case PowExpr p:
                return Power(p, x);
            case CallExpr c:
                return Call(c, x);
            default:
                throw new CalcException($"diff: cannot differentiate {e}");
        }
    }

    private static Expr Product(MulExpr m, string x)
    {
        var constant = new List<Expr>();
        var varying = new List<Expr>();
        foreach (var f in m.Factors)
        {
            if (f.ContainsSymbol(x)) varying.Add(f);
            else constant.Add(f);
        }
        var c = Algebra.Mul(constant);
        if (varying.Count == 1)
        {
            if (constant.Count > 0) Trace?.Invoke("constant", m);
            return Algebra.Mul(c, D(varying[0], x));
        }
        bool quotient = varying.Any(f => f is PowExpr { Exponent: NumberExpr n } && n.Value.IsNegativeReal);
        Trace?.Invoke(quotient ? "quotient" : "product", m);
        var terms = new List<Expr>();
        for (int i = 0; i < varying.Count; i++)
        {
            var parts = new List<Expr> { c, D(varying[i], x) };
            for (int j = 0; j < varying.Count; j++)
                if (j != i) parts.Add(varying[j]);
            terms.Add(Algebra.Mul(parts));
        }
        return Algebra.Add(terms);
    }

    private static Expr Power(PowExpr p, string x)
    {
        var u = p.Base;
        var v = p.Exponent;
        bool baseVaries = u.ContainsSymbol(x), expVaries = v.ContainsSymbol(x);
        if (!expVaries)
        {
            Trace?.Invoke(u is SymbolExpr ? "power" : "chain-power", p);
            return Algebra.Mul(v, Algebra.Pow(u, Algebra.Sub(v, Expr.One)), D(u, x));
        }
        if (!baseVaries)
        {
            Trace?.Invoke(u is ConstantExpr { Constant: ConstantKind.E } ? "exp" : "exponential", p);
            if (u is ConstantExpr { Constant: ConstantKind.E }) return Algebra.Mul(p, D(v, x));
            return Algebra.Mul(p, CasEnv.Fn("ln", u), D(v, x));
        }
        Trace?.Invoke("general-power", p);
        // d/dx u^v = u^v (v' ln u + v u'/u)
        var lnU = CasEnv.Fn("ln", u);
        return Algebra.Mul(p, Algebra.Add(Algebra.Mul(D(v, x), lnU), Algebra.Mul(v, D(u, x), Algebra.Pow(u, Expr.MinusOne))));
    }

    private static Expr Call(CallExpr c, string x)
    {
        var name = c.Name;
        if (name == "fderiv" && c.Args.Length == 3 && c.Args[0] is SymbolExpr && c.Args[1] is NumberExpr ord)
        {
            var inner = c.Args[2];
            Trace?.Invoke("chain", c);
            return Algebra.Mul(new CallExpr("fderiv", [c.Args[0], Algebra.Add(ord, Expr.One), inner]), D(inner, x));
        }
        if (name == "log" && c.Args.Length == 2)
        {
            if (c.Args[1].ContainsSymbol(x))
                return D(Algebra.Div(CasEnv.Fn("ln", c.Args[0]), CasEnv.Fn("ln", c.Args[1])), x);
            Trace?.Invoke("log", c);
            return Algebra.Mul(D(c.Args[0], x), Algebra.Pow(Algebra.Mul(c.Args[0], CasEnv.Fn("ln", c.Args[1])), Expr.MinusOne));
        }
        if (name == "atan2" && c.Args.Length == 2)
        {
            var yv = c.Args[0];
            var xv = c.Args[1];
            var num = Algebra.Sub(Algebra.Mul(xv, D(yv, x)), Algebra.Mul(yv, D(xv, x)));
            return Algebra.Mul(num, Algebra.Pow(Algebra.Add(Algebra.Mul(xv, xv), Algebra.Mul(yv, yv)), Expr.MinusOne));
        }
        if (name == "root" && c.Args.Length == 2)
            return D(Algebra.Pow(c.Args[0], Algebra.Pow(c.Args[1], Expr.MinusOne)), x);
        if (c.Args.Length == 1)
        {
            var u = c.Args[0];
            var outer = Outer(name, u);
            if (outer is not null)
            {
                Trace?.Invoke(u is SymbolExpr ? "function" : "chain", c);
                return Algebra.Mul(outer, D(u, x));
            }
            var env = CasEnv.Current;
            bool known = env is not null && env.Ctx.Functions.Contains(name);
            if (!known)
            {
                Trace?.Invoke("unknown", c);
                return Algebra.Mul(new CallExpr("fderiv", [new SymbolExpr(name), Expr.One, u]), D(u, x));
            }
        }
        return new CallExpr("diff", [c, new SymbolExpr(x)]);
    }

    /// <summary>f'(u) for the elementary functions (radians), or null when there is no rule.</summary>
    public static Expr? Outer(string name, Expr u)
    {
        Expr F(string n) => CasEnv.Fn(n, u);
        Expr Sq(Expr a) => Algebra.Pow(a, Expr.Two);
        Expr Inv(Expr a) => Algebra.Pow(a, Expr.MinusOne);
        Expr InvSqrt(Expr a) => Algebra.Pow(a, Expr.Rational(-1, 2));
        var u2 = Algebra.Mul(u, u);
        return name switch
        {
            "sin" => F("cos"),
            "cos" => Algebra.Neg(F("sin")),
            "tan" => Sq(F("sec")),
            "sec" => Algebra.Mul(F("sec"), F("tan")),
            "csc" => Algebra.Neg(Algebra.Mul(F("csc"), F("cot"))),
            "cot" => Algebra.Neg(Sq(F("csc"))),
            "asin" => InvSqrt(Algebra.Sub(Expr.One, u2)),
            "acos" => Algebra.Neg(InvSqrt(Algebra.Sub(Expr.One, u2))),
            "atan" => Inv(Algebra.Add(Expr.One, u2)),
            "acot" => Algebra.Neg(Inv(Algebra.Add(Expr.One, u2))),
            "asec" => Inv(Algebra.Mul(CasEnv.Fn("abs", u), Algebra.Sqrt(Algebra.Sub(u2, Expr.One)))),
            "acsc" => Algebra.Neg(Inv(Algebra.Mul(CasEnv.Fn("abs", u), Algebra.Sqrt(Algebra.Sub(u2, Expr.One))))),
            "sinh" => F("cosh"),
            "cosh" => F("sinh"),
            "tanh" => Sq(F("sech")),
            "sech" => Algebra.Neg(Algebra.Mul(F("sech"), F("tanh"))),
            "csch" => Algebra.Neg(Algebra.Mul(F("csch"), F("coth"))),
            "coth" => Algebra.Neg(Sq(F("csch"))),
            "asinh" => InvSqrt(Algebra.Add(u2, Expr.One)),
            "acosh" => InvSqrt(Algebra.Sub(u2, Expr.One)),
            "atanh" => Inv(Algebra.Sub(Expr.One, u2)),
            "ln" => Inv(u),
            "log" => Inv(Algebra.Mul(u, CasEnv.Fn("ln", Expr.Int(10)))),
            "log2" => Inv(Algebra.Mul(u, CasEnv.Fn("ln", Expr.Int(2)))),
            "exp" => CasEnv.Fn("exp", u),
            "sqrt" => Algebra.Mul(Expr.HalfExpr, InvSqrt(u)),
            "cbrt" => Algebra.Mul(Expr.Rational(1, 3), Algebra.Pow(u, Expr.Rational(-2, 3))),
            "abs" => F("sign"),
            "sign" or "floor" or "ceil" or "round" or "trunc" => Expr.Zero,
            "fpart" => Expr.One,
            _ => null,
        };
    }

    /// <summary>dy/dx for an implicit curve F(x, y) = 0: -F_x / F_y.</summary>
    public static Expr Implicit(Expr equation, string x, string y)
    {
        var f = equation is RelationExpr { Op: RelOp.Equal } r ? Algebra.Sub(r.Left, r.Right) : equation;
        var fx = D(f, x);
        var fy = D(f, y);
        if (Simplifier.IsZero(fy)) throw new CalcException("impdiff: the equation does not depend on " + y);
        return Simplifier.Simplify(Algebra.Neg(Algebra.Div(fx, fy)), trig: false);
    }
}
