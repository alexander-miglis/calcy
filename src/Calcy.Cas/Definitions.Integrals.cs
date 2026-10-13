using Calcy.Cas.Calculus;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Cas;

internal static partial class Definitions
{
    private static IEnumerable<FunctionDefinition> IntegralFunctions(FunctionRegistry r)
    {
        r.TryGet("floor", out var floor);
        yield return Def("integrate", c => Integrate(c, floor), CalculusCat, "integrate(expr, x) or integrate(expr, x, a, b)",
            "Antiderivative (without + C), or the definite integral from a to b (∞ allowed). Falls back to numeric integration when there is no closed form. int(x) of a plain number is still the whole-number part.",
            "integrate(x^2, x, 0, 1)", min: 1, max: 4, aliases: ["int", "integral", "antiderivative"]);
    }

    private static Expr? Integrate(CallInfo c, FunctionDefinition? floor)
    {
        if (c.Count == 1 && floor is not null)
        {
            // int(2.7) keeps its TI meaning (integer part) for plain numbers.
            var v = Evaluator.Eval(c.Args[0], c.Context);
            if (v is NumberExpr) return Evaluator.Apply(floor, [v], c.Context);
        }
        if (c.Count == 3) throw c.Error("use integrate(expr, x) or integrate(expr, x, a, b)");
        return Run(c, env =>
        {
            var f = EvalWithVar(c, env, 1, out var x);
            if (c.Count <= 2)
                return MapList(f, g => Integrator.Antiderivative(g, x) ?? new CallExpr("integrate", [g, new SymbolExpr(x)]));
            var a = env.EvalArg(c.Args[2]);
            var b = env.EvalArg(c.Args[3]);
            return MapList(f, g =>
            {
                if (a.ContainsSymbol(x) || b.ContainsSymbol(x)) throw c.Error($"the bounds cannot contain the variable {x}");
                return DefiniteIntegrals.Integrate(g, x, a, b) ?? new CallExpr("integrate", [g, new SymbolExpr(x), a, b]);
            });
        });
    }
}
