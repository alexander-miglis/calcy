using Calcy.Cas.Calculus;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Cas;

internal static partial class Definitions
{
    private static IEnumerable<FunctionDefinition> CalculusFunctions(FunctionRegistry r)
    {
        yield return Def("diff", Diff, CalculusCat, "diff(expr, x) or diff(expr, x, n)",
            "Derivative with respect to x (n-th derivative, or partial derivatives diff(f, x, y)). In degree mode d/dx sin(x) = π/180·cos(x).",
            "diff(x^2 sin(x), x)", min: 1, max: -1, aliases: ["derivative", "d"]);
        yield return Def("limit", Limit, CalculusCat, "limit(expr, x, a) or limit(expr, x, a, \"+\")",
            "Limit as x tends to a (∞ and -∞ allowed). A fourth argument \"+\" or \"-\" (or 1, -1) gives a one-sided limit. Undefined means the limit does not exist.",
            "limit(sin(x)/x, x, 0)", min: 3, max: 4, aliases: ["lim"]);
        yield return Def("taylor", Taylor, CalculusCat, "taylor(expr, x, a, n) or taylor(expr, x, n)",
            "Taylor polynomial of expr about x = a up to (x - a)^n (about 0 with three arguments).", "taylor(e^x, x, 0, 4)", min: 3, max: 4,
            aliases: ["series"]);
        yield return Def("tangent", c => TangentOrNormal(c, normal: false), CalculusCat, "tangent(expr, x, a)",
            "Tangent line to y = expr at x = a, as an equation y = m·x + b.", "tangent(x^2, x, 1)", min: 3, max: 3,
            aliases: ["tangentline", "tangentLine"]);
        yield return Def("normal", c => TangentOrNormal(c, normal: true), CalculusCat, "normal(expr, x, a)",
            "Normal line (perpendicular to the tangent) to y = expr at x = a.", "normal(x^2, x, 1)", min: 3, max: 3,
            aliases: ["normalline", "normalLine"]);
        yield return Def("impdiff", ImpDiff, CalculusCat, "impdiff(equation, x, y)",
            "Implicit derivative dy/dx of a curve F(x, y) = 0: impdiff(x^2 + y^2 = 1, x, y) = -x/y.", "impdiff(x^2 + y^2 = 25, x, y)", min: 3, max: 3,
            aliases: ["impDif", "implicitdiff"]);
        foreach (var d in IntegralFunctions(r)) yield return d;
    }

    private static Expr? Diff(CallInfo c)
    {
        return Run(c, env =>
        {
            var vars = new List<string>();
            int order = 1;
            if (c.Count == 1)
            {
                var e0 = env.EvalArg(c.Args[0]);
                vars.Add(DefaultVar(c, e0));
            }
            else
            {
                vars.Add(VarArg(c, 1));
                if (c.Count == 3 && c.Args[2] is not SymbolExpr)
                    order = SmallInt(c, 2, 0, 100, "the order");
                else
                    for (int i = 2; i < c.Count; i++) vars.Add(VarArg(c, i));
            }
            var e = env.EvalArg(c.Args[0], [.. vars]);
            foreach (var v in vars) e = MapList(e, x => Differentiator.Diff(x, v, vars.Count == 1 ? order : 1));
            return e;
        });
    }

    internal static int SmallInt(CallInfo c, int i, int min, int max, string what)
    {
        var n = Evaluator.Eval(c.Args[i], c.Context.WithApproximate(false));
        if (n is not NumberExpr { Value.IsExactInteger: true } ni || ni.Value.Re < min || ni.Value.Re > max)
            throw c.Error($"{what} must be a whole number from {min} to {max}");
        return (int)ni.Value.Re;
    }

    private static int Direction(CallInfo c, int i)
    {
        if (c.Count <= i) return 0;
        var d = Evaluator.Eval(c.Args[i], c.Context.WithApproximate(false));
        switch (d)
        {
            case StringExpr s:
                return s.Value.Trim() switch
                {
                    "+" or "right" or "r" or "+0" => 1,
                    "-" or "left" or "l" or "-0" => -1,
                    "" or "both" => 0,
                    _ => throw c.Error("the direction must be \"+\" or \"-\""),
                };
            case NumberExpr n when n.Value.IsReal:
                return Math.Sign(n.Value.Re);
            default:
                throw c.Error("the direction must be \"+\" (from the right) or \"-\" (from the left)");
        }
    }

    private static Expr? Limit(CallInfo c)
    {
        return Run(c, env =>
        {
            var x = VarArg(c, 1);
            var f = env.EvalArg(c.Args[0], x);
            var point = env.EvalArg(c.Args[2]);
            int dir = Direction(c, 3);
            if (point is ConstantExpr { IsInfinite: true }) dir = 0;
            return MapList(f, g => Limits.Limit(g, x, point, dir) ?? new CallExpr("limit", [g, new SymbolExpr(x), point, .. c.Args.Skip(3)]));
        });
    }

    private static Expr? Taylor(CallInfo c)
    {
        return Run(c, env =>
        {
            var x = VarArg(c, 1);
            var f = env.EvalArg(c.Args[0], x);
            Expr a = Expr.Zero;
            int order;
            if (c.Count == 3) order = SmallInt(c, 2, 0, 60, "the order");
            else
            {
                a = env.EvalArg(c.Args[2]);
                order = SmallInt(c, 3, 0, 60, "the order");
            }
            return MapList(f, g => Limits.Taylor(g, x, a, order) ?? new CallExpr("taylor", [g, new SymbolExpr(x), a, Expr.Int(order)]));
        });
    }

    private static Expr? TangentOrNormal(CallInfo c, bool normal)
    {
        return Run(c, env =>
        {
            var x = VarArg(c, 1);
            var f = env.EvalArg(c.Args[0], x);
            string y = "y";
            if (f is RelationExpr { Op: RelOp.Equal, Left: SymbolExpr ys } rel)
            {
                y = ys.Name;
                f = rel.Right;
            }
            var a = env.EvalArg(c.Args[2]);
            var fa = CasEnv.TrySubst(f, x, a) ?? throw c.Error($"the function is not defined at {x} = {Core.Printing.ExprPrinter.ToText(a)}");
            var df = Differentiator.D(f, x);
            var slope = Limits.Limit(df, x, a, 0);
            if (slope is null or ConstantExpr { Constant: ConstantKind.Undefined })
            {
                // One-sided domains (√x at 0): use the side where the function exists.
                var right = Limits.Limit(df, x, a, 1);
                var left = Limits.Limit(df, x, a, -1);
                slope = right is not null && left is null ? right : left is not null && right is null ? left : slope;
            }
            var xs = new SymbolExpr(x);
            var ysym = new SymbolExpr(y);
            if (slope is null) return null;
            bool vertical = slope is ConstantExpr { IsInfinite: true } || slope is ConstantExpr { Constant: ConstantKind.Undefined };
            if (normal)
            {
                if (vertical) return new RelationExpr(RelOp.Equal, ysym, Simplifier.Simplify(fa));
                if (Simplifier.IsZero(slope)) return new RelationExpr(RelOp.Equal, xs, a);
                slope = Algebra.Neg(Algebra.Pow(slope, Expr.MinusOne));
            }
            else if (vertical) return new RelationExpr(RelOp.Equal, xs, a);
            var line = Expander.Expand(Algebra.Add(Algebra.Mul(slope, Algebra.Sub(xs, a)), fa));
            return new RelationExpr(RelOp.Equal, ysym, Simplifier.Simplify(line));
        });
    }

    private static Expr? ImpDiff(CallInfo c)
    {
        return Run(c, env =>
        {
            var x = VarArg(c, 1);
            var y = VarArg(c, 2);
            var eq = env.EvalArg(c.Args[0], x, y);
            return Differentiator.Implicit(eq, x, y);
        });
    }
}
