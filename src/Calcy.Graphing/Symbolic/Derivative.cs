using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Graphing.Symbolic;

/// <summary>
/// Symbolic differentiation of the elementary functions, so slopes, extrema and inflection points can be found
/// precisely (and exactly at nice points). Returns null for parts it does not know; callers then use numeric derivatives.
/// </summary>
public static class Derivative
{
    /// <summary>The derivative of <paramref name="e"/> with respect to <paramref name="variable"/>, simplified, or null.</summary>
    public static Expr? Of(Expr e, string variable, EvalContext ctx)
    {
        try
        {
            var d = new Differ(variable, ctx).D(e);
            if (d is null) return null;
            return Evaluator.Eval(d, ctx.WithApproximate(false).WithFree(variable));
        }
        catch (CalcException)
        {
            return null;
        }
        catch (ArithmeticException)
        {
            return null;
        }
    }

    private sealed class Differ(string v, EvalContext ctx)
    {
        private readonly Expr _angle = ctx.Settings.Angle switch
        {
            AngleMode.Degree => Algebra.Div(ConstantExpr.Pi, Expr.Int(180)),
            AngleMode.Gradian => Algebra.Div(ConstantExpr.Pi, Expr.Int(200)),
            _ => Expr.One,
        };

        private bool Has(Expr e) => e.ContainsSymbol(v);

        public Expr? D(Expr e)
        {
            if (!Has(e)) return Expr.Zero;
            switch (e)
            {
                case SymbolExpr:
                    return Expr.One;
                case AddExpr a:
                {
                    var terms = new List<Expr>();
                    foreach (var t in a.Terms)
                    {
                        var dt = D(t);
                        if (dt is null) return null;
                        terms.Add(dt);
                    }
                    return Algebra.Add(terms);
                }
                case MulExpr m:
                {
                    var sum = new List<Expr>();
                    for (int i = 0; i < m.Factors.Length; i++)
                    {
                        if (!Has(m.Factors[i])) continue;
                        var di = D(m.Factors[i]);
                        if (di is null) return null;
                        var parts = new List<Expr>(m.Factors.Length) { di };
                        for (int j = 0; j < m.Factors.Length; j++)
                            if (j != i) parts.Add(m.Factors[j]);
                        sum.Add(Algebra.Mul(parts));
                    }
                    return Algebra.Add(sum);
                }
                case PowExpr p:
                {
                    bool baseVar = Has(p.Base), expVar = Has(p.Exponent);
                    if (baseVar && !expVar)
                    {
                        var db = D(p.Base);
                        if (db is null) return null;
                        return Algebra.Mul(p.Exponent, Algebra.Pow(p.Base, Algebra.Sub(p.Exponent, Expr.One)), db);
                    }
                    var de = D(p.Exponent);
                    if (de is null) return null;
                    if (!baseVar) return Algebra.Mul(e, CallExpr.Of("ln", p.Base), de);
                    var db2 = D(p.Base);
                    if (db2 is null) return null;
                    return Algebra.Mul(e, Algebra.Add(Algebra.Mul(de, CallExpr.Of("ln", p.Base)), Algebra.Mul(p.Exponent, db2, Algebra.Pow(p.Base, Expr.MinusOne))));
                }
                case CallExpr c:
                    return Call(c);
                case PiecewiseExpr pw:
                {
                    var cases = new List<(Expr, Expr)>();
                    foreach (var (cond, value) in pw.Cases)
                    {
                        var dv = D(value);
                        if (dv is null) return null;
                        cases.Add((cond, dv));
                    }
                    Expr? other = null;
                    if (pw.Otherwise is not null)
                    {
                        other = D(pw.Otherwise);
                        if (other is null) return null;
                    }
                    return new PiecewiseExpr([.. cases], other);
                }
                default:
                    return null;
            }
        }

        private Expr? Call(CallExpr c)
        {
            if (!ctx.Functions.TryGet(c.Name, out var def)) return null;
            string name = def.Name;
            if (name == "atan2" && c.Args.Length == 2)
            {
                var y = c.Args[0];
                var x = c.Args[1];
                var dy = D(y);
                var dx = D(x);
                if (dy is null || dx is null) return null;
                var num = Algebra.Sub(Algebra.Mul(x, dy), Algebra.Mul(y, dx));
                var den = Algebra.Add(Algebra.Pow(x, Expr.Two), Algebra.Pow(y, Expr.Two));
                return Algebra.Div(Algebra.Div(num, den), _angle);
            }
            if (name == "log" && c.Args.Length == 2)
            {
                if (Has(c.Args[1])) return D(Algebra.Div(CallExpr.Of("ln", c.Args[0]), CallExpr.Of("ln", c.Args[1])));
                var du0 = D(c.Args[0]);
                if (du0 is null) return null;
                return Algebra.Div(du0, Algebra.Mul(c.Args[0], CallExpr.Of("ln", c.Args[1])));
            }
            if (name == "root" && c.Args.Length == 2)
            {
                if (Has(c.Args[1])) return null;
                return D(Algebra.Pow(c.Args[0], Algebra.Pow(c.Args[1], Expr.MinusOne)));
            }
            if (c.Args.Length != 1) return null;
            var u = c.Args[0];
            var du = D(u);
            if (du is null) return null;
            Expr? outer = name switch
            {
                "sin" => Algebra.Mul(CallExpr.Of("cos", u), _angle),
                "cos" => Algebra.Neg(Algebra.Mul(CallExpr.Of("sin", u), _angle)),
                "tan" => Algebra.Mul(Algebra.Pow(CallExpr.Of("cos", u), Expr.Int(-2)), _angle),
                "sec" => Algebra.Mul(CallExpr.Of("sin", u), Algebra.Pow(CallExpr.Of("cos", u), Expr.Int(-2)), _angle),
                "csc" => Algebra.Neg(Algebra.Mul(CallExpr.Of("cos", u), Algebra.Pow(CallExpr.Of("sin", u), Expr.Int(-2)), _angle)),
                "cot" => Algebra.Neg(Algebra.Mul(Algebra.Pow(CallExpr.Of("sin", u), Expr.Int(-2)), _angle)),
                "asin" => Algebra.Div(Algebra.Pow(Algebra.Sub(Expr.One, Algebra.Pow(u, Expr.Two)), Expr.Rational(-1, 2)), _angle),
                "acos" => Algebra.Neg(Algebra.Div(Algebra.Pow(Algebra.Sub(Expr.One, Algebra.Pow(u, Expr.Two)), Expr.Rational(-1, 2)), _angle)),
                "atan" => Algebra.Div(Algebra.Pow(Algebra.Add(Expr.One, Algebra.Pow(u, Expr.Two)), Expr.MinusOne), _angle),
                "acot" => Algebra.Neg(Algebra.Div(Algebra.Pow(Algebra.Add(Expr.One, Algebra.Pow(u, Expr.Two)), Expr.MinusOne), _angle)),
                "asec" => Algebra.Div(Algebra.Pow(Algebra.Mul(CallExpr.Of("abs", u), CallExpr.Of("sqrt", Algebra.Sub(Algebra.Pow(u, Expr.Two), Expr.One))), Expr.MinusOne), _angle),
                "acsc" => Algebra.Neg(Algebra.Div(Algebra.Pow(Algebra.Mul(CallExpr.Of("abs", u), CallExpr.Of("sqrt", Algebra.Sub(Algebra.Pow(u, Expr.Two), Expr.One))), Expr.MinusOne), _angle)),
                "sinh" => CallExpr.Of("cosh", u),
                "cosh" => CallExpr.Of("sinh", u),
                "tanh" => Algebra.Pow(CallExpr.Of("cosh", u), Expr.Int(-2)),
                "sech" => Algebra.Neg(Algebra.Mul(CallExpr.Of("sinh", u), Algebra.Pow(CallExpr.Of("cosh", u), Expr.Int(-2)))),
                "csch" => Algebra.Neg(Algebra.Mul(CallExpr.Of("cosh", u), Algebra.Pow(CallExpr.Of("sinh", u), Expr.Int(-2)))),
                "coth" => Algebra.Neg(Algebra.Pow(CallExpr.Of("sinh", u), Expr.Int(-2))),
                "asinh" => Algebra.Pow(Algebra.Add(Algebra.Pow(u, Expr.Two), Expr.One), Expr.Rational(-1, 2)),
                "acosh" => Algebra.Pow(Algebra.Sub(Algebra.Pow(u, Expr.Two), Expr.One), Expr.Rational(-1, 2)),
                "atanh" => Algebra.Pow(Algebra.Sub(Expr.One, Algebra.Pow(u, Expr.Two)), Expr.MinusOne),
                "exp" => CallExpr.Of("exp", u),
                "ln" => Algebra.Pow(u, Expr.MinusOne),
                "log" => Algebra.Pow(Algebra.Mul(u, CallExpr.Of("ln", Expr.Int(10))), Expr.MinusOne),
                "log2" => Algebra.Pow(Algebra.Mul(u, CallExpr.Of("ln", Expr.Int(2))), Expr.MinusOne),
                "sqrt" => Algebra.Mul(Expr.HalfExpr, Algebra.Pow(u, Expr.Rational(-1, 2))),
                "cbrt" => Algebra.Mul(Expr.Rational(1, 3), Algebra.Pow(CallExpr.Of("cbrt", u), Expr.Int(-2))),
                "abs" => CallExpr.Of("sign", u),
                "sign" or "floor" or "ceil" or "trunc" or "round" => Expr.Zero,
                _ => null,
            };
            if (outer is null) return null;
            return Algebra.Mul(outer, du);
        }
    }
}
