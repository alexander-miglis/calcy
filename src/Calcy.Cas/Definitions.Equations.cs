using Calcy.Cas.Calculus;
using Calcy.Cas.Numerics;
using Calcy.Cas.Solving;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas;

internal static partial class Definitions
{
    private static IEnumerable<FunctionDefinition> EquationFunctions(FunctionRegistry r)
    {
        yield return Def("solve", Solve, EquationsCat, "solve(equation, x) or solve({eq1, eq2}, {x, y})",
            "Solves equations, inequalities and systems exactly (numerically when needed). Trigonometric equations give all solutions with an integer n. Several solutions are joined with 'or'.",
            "solve(x^2 - 5x + 6 = 0, x)", min: 1, max: 2);
        yield return Def("nsolve", NSolve, EquationsCat, "nsolve(equation, x), nsolve(equation, x, guess) or nsolve(equation, x, a, b)",
            "Numeric solutions: every root between a and b (default -100 to 100), or the root nearest a starting guess.",
            "nsolve(cos(x) = x, x)", min: 1, max: 4, aliases: ["nSolve", "fsolve"]);
        r.TryGet("zeros", out var matrixZeros);
        yield return Def("zeros", c => Zeros(c, matrixZeros), EquationsCat, "zeros(expr, x)",
            "The list of values of x where expr is 0 (zeros(r, c) with numbers is still a zero matrix).", "zeros(x^2 - 4, x)", min: 1, max: 2,
            aliases: ["roots"]);
        yield return Def("symsum", c => SymSumProduct(c, product: false), CalculusCat, "symsum(expr, k, a, b)",
            "Closed form of the sum of expr for k from a to b (b may be ∞ or a symbol): polynomial, geometric, telescoping and standard series.",
            "symsum(k^2, k, 1, n)", min: 4, max: 4);
        yield return Def("symproduct", c => SymSumProduct(c, product: true), CalculusCat, "symproduct(expr, k, a, b)",
            "Closed form of the product of expr for k from a to b.", "symproduct(k, k, 1, n)", min: 4, max: 4, aliases: ["symprod"]);
        yield return Def("steps", c => new ListExpr([.. CasSteps.Explain(c.Args[0], c.Context).Select(s => (Expr)new StringExpr(s))]),
            AlgebraCat, "steps(solve(...)) or steps(diff(...)) or steps(integrate(...)) or steps(limit(...))",
            "Step-by-step explanation of solving a linear or quadratic equation, a derivative, an integral or a limit.",
            "steps(solve(2x + 3 = 7, x))");
    }

    // ------------------------------------------------------------------ solve

    private static List<string> Unknowns(CallInfo c, int i, out bool system)
    {
        system = false;
        var raw = c.Args[i];
        switch (raw)
        {
            case SymbolExpr s:
                return [s.Name];
            case ListExpr or TupleExpr:
                system = true;
                return [.. raw.Children.Select(x => x is SymbolExpr s ? s.Name : throw c.Error("the unknowns must be variable names, like {x, y}"))];
            default:
                throw c.Error($"argument {i + 1} must be a variable name or a list of them, like {{x, y}}");
        }
    }

    private static List<Expr> EquationList(Expr e) => e switch
    {
        ListExpr l => [.. l.Items.SelectMany(EquationList)],
        LogicExpr { Op: LogicOp.And } a => [.. a.Operands.SelectMany(EquationList)],
        _ => [e],
    };

    private static Expr? Solve(CallInfo c)
    {
        return Run(c, env =>
        {
            List<string> unknowns;
            bool system;
            if (c.Count == 2) unknowns = Unknowns(c, 1, out system);
            else
            {
                var probe = env.EvalArg(c.Args[0]);
                var free = probe.FreeSymbols().ToList();
                if (free.Count == 0) free = ["x"];
                system = c.Args[0] is ListExpr || free.Count > 1 && EquationList(probe).Count > 1;
                unknowns = system ? free : [DefaultVar(c, probe)];
            }
            var e = env.EvalArg(c.Args[0], [.. unknowns]);
            var eqs = EquationList(e);
            if (eqs.Count > 1 || system)
            {
                if (eqs.Any(q => q is BoolExpr { Value: false })) return BoolExpr.False;
                var list = new List<Expr>();
                foreach (var q in eqs)
                {
                    if (q is BoolExpr { Value: true }) continue;
                    if (q is RelationExpr { Op: RelOp.Equal } rel) list.Add(Algebra.Sub(rel.Left, rel.Right));
                    else if (q is RelationExpr) throw c.Error("systems of inequalities are not supported");
                    else list.Add(q);
                }
                if (list.Count == 0) return BoolExpr.True;
                if (unknowns.Count == 1 && list.Count > 1)
                {
                    // Several equations in one unknown: solutions common to all of them.
                    var first = new Solver(unknowns[0], CasEnv.FreshName("n", e)).Solve(list[0]);
                    if (first is null) return null;
                    var solver = new Solver(unknowns[0], CasEnv.FreshName("n", e));
                    var common = new RootSet();
                    foreach (var v in first.Values)
                        if (list.Skip(1).All(g => solver.Satisfies(g, v))) common.Values.Add(v);
                    return SolveFormat.Single(common, unknowns[0]);
                }
                var solutions = Systems.Solve(list, unknowns, CasEnv.FreshName("n", e));
                if (solutions is null) return null;
                return SolveFormat.System(solutions, unknowns);
            }
            return SolveOne(eqs[0], unknowns[0], e);
        });
    }

    private static Expr? SolveOne(Expr eq, string x, Expr whole)
    {
        switch (eq)
        {
            case BoolExpr:
                return eq;
            case RelationExpr { Op: not RelOp.Equal } ineq:
                return Inequalities.Solve(ineq, x);
            case RelationExpr rel:
                eq = Algebra.Sub(rel.Left, rel.Right);
                break;
            case LogicExpr or ListExpr:
                return null;
        }
        var roots = new Solver(x, CasEnv.FreshName("n", whole)).Solve(eq);
        return roots is null ? null : SolveFormat.Single(roots, x);
    }

    // ------------------------------------------------------------------ nsolve

    private static Expr? NSolve(CallInfo c)
    {
        return Run(c, env =>
        {
            var e = EvalWithVar(c, env, 1, out var x);
            var f = e is RelationExpr { Op: RelOp.Equal } rel ? Algebra.Sub(rel.Left, rel.Right) : e;
            if (f is RelationExpr) throw c.Error("nsolve needs an equation, not an inequality");
            var fn = NumericFunction.Compile(f, x) ?? throw c.Error($"the equation must contain only the variable {x} and numbers");
            env.ApproximateResult = false;
            var roots = new List<double>();
            if (c.Count == 3)
            {
                double guess = CasEnv.Real(env.EvalArg(c.Args[2]));
                if (double.IsNaN(guess)) throw c.Error("the starting guess must be a number");
                double r0 = RootFinding.Newton(fn, guess);
                if (double.IsNaN(r0))
                {
                    var near = RootFinding.ScanRoots(fn, guess - 10, guess + 10, 2000);
                    if (near.Count > 0) r0 = near.OrderBy(v => Math.Abs(v - guess)).First();
                }
                if (!double.IsNaN(r0)) roots.Add(r0);
            }
            else
            {
                double a = -100, b = 100;
                if (c.Count == 4)
                {
                    a = CasEnv.Real(env.EvalArg(c.Args[2]));
                    b = CasEnv.Real(env.EvalArg(c.Args[3]));
                    if (double.IsNaN(a) || double.IsNaN(b)) throw c.Error("the interval ends must be numbers");
                    if (a > b) (a, b) = (b, a);
                }
                roots = RootFinding.ScanRoots(fn, a, b, 20_000);
                double step = (b - a) / 20_000;
                for (int i = 1; i < roots.Count; i++)
                    if (roots[i] - roots[i - 1] < 1.5 * step) throw c.Error("the equation holds on whole intervals, not at single points");
            }
            var set = new RootSet { Exact = false };
            foreach (var root in roots) set.Values.Add(new NumberExpr(Number.Approx(root)));
            return SolveFormat.Single(set, x);
        });
    }

    // ------------------------------------------------------------------ zeros

    private static Expr? Zeros(CallInfo c, FunctionDefinition? matrixZeros)
    {
        if (matrixZeros is not null && c.Count >= 1)
        {
            var first = Evaluator.Eval(c.Args[0], c.Context);
            if (first is NumberExpr && (c.Count == 1 || Evaluator.Eval(c.Args[1], c.Context) is NumberExpr))
                return Evaluator.CallBuiltin(matrixZeros, c.Args, c.Context);
        }
        return Run(c, env =>
        {
            var e = EvalWithVar(c, env, 1, out var x);
            var f = e is RelationExpr { Op: RelOp.Equal } rel ? Algebra.Sub(rel.Left, rel.Right) : e;
            var roots = new Solver(x, CasEnv.FreshName("n", f)).Solve(f);
            if (roots is null) return null;
            if (roots.All) throw c.Error("the expression is zero for every value");
            return new ListExpr([.. roots.Values]);
        });
    }

    // ------------------------------------------------------------------ sums and products

    private static Expr? SymSumProduct(CallInfo c, bool product)
    {
        return Run(c, env =>
        {
            var k = VarArg(c, 1);
            var f = env.EvalArg(c.Args[0], k);
            var a = env.EvalArg(c.Args[2]);
            var b = env.EvalArg(c.Args[3]);
            return product ? Sums.Product(f, k, a, b) : Sums.Sum(f, k, a, b);
        });
    }
}
