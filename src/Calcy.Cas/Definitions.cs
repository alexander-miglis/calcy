using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Cas;

/// <summary>Catalog entries and argument handling for every CAS function.</summary>
internal static partial class Definitions
{
    private const string AlgebraCat = "Algebra";
    private const string CalculusCat = "Calculus";
    private const string EquationsCat = "Equations";

    public static IEnumerable<FunctionDefinition> All(FunctionRegistry r)
    {
        foreach (var d in AlgebraFunctions(r)) yield return d;
        foreach (var d in CalculusFunctions(r)) yield return d;
        foreach (var d in EquationFunctions(r)) yield return d;
    }

    internal static FunctionDefinition Def(string name, Func<CallInfo, Expr?> eval, string category, string signature, string description,
        string example, int min = 1, int max = 1, bool hold = true, bool listable = false, string[]? aliases = null) => new()
    {
        Name = name,
        Evaluate = eval,
        Category = category,
        Signature = signature,
        Description = description,
        Example = example,
        MinArgs = min,
        MaxArgs = max,
        HoldArgs = hold,
        Listable = listable,
        Aliases = aliases ?? [],
    };

    /// <summary>Runs a CAS computation in its own scope; size or depth overflows leave the call unevaluated.</summary>
    internal static Expr? Run(CallInfo c, Func<CasEnv, Expr?> body)
    {
        using var scope = CasEnv.Enter(c.Context);
        var env = CasEnv.Require;
        try
        {
            var r = body(env);
            return r is null ? null : env.Finish(r);
        }
        catch (CasLimitException)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException
                                       or NullReferenceException or KeyNotFoundException or InvalidCastException)
        {
            // Never let an internal failure crash the calculator; report it as a normal error.
            throw new CalcException($"{c.Function.Name}: this could not be computed");
        }
    }

    /// <summary>The variable named by a held argument.</summary>
    internal static string VarArg(CallInfo c, int i)
    {
        if (c.Args[i] is SymbolExpr s) return s.Name;
        throw c.Error($"argument {i + 1} must be a variable name, not {c.Args[i]}");
    }

    /// <summary>The single free variable of an expression (x when several are present and x is one of them).</summary>
    internal static string DefaultVar(CallInfo c, Expr e)
    {
        var free = e.FreeSymbols();
        if (free.Count == 1) return free[0];
        if (free.Contains("x")) return "x";
        if (free.Count == 0) return "x";
        throw c.Error($"say which variable to use, for example {c.Function.Name}({ExprPrinterShort(e)}, {free[0]})");
    }

    private static string ExprPrinterShort(Expr e)
    {
        var s = Core.Printing.ExprPrinter.ToInput(e);
        return s.Length > 30 ? "expr" : s;
    }

    /// <summary>Applies f to each element when the argument is a list (held functions are not listable automatically).</summary>
    internal static Expr MapList(Expr e, Func<Expr, Expr> f) => e is ListExpr l ? new ListExpr([.. l.Items.Select(x => MapList(x, f))]) : f(e);

    // ------------------------------------------------------------------ algebra

    private static IEnumerable<FunctionDefinition> AlgebraFunctions(FunctionRegistry r)
    {
        yield return Def("expand", c => Run(c, env => MapList(env.EvalArg(c.Args[0]), Expander.Expand)),
            AlgebraCat, "expand(expr)", "Multiplies out products and powers: expand((x + 1)^3) = x³ + 3x² + 3x + 1.", "expand((x + 1)^3)");
        yield return Def("expandlog", c => Run(c, env => MapList(env.EvalArg(c.Args[0]), TrigLog.ExpandLog)),
            AlgebraCat, "expandlog(expr)", "Splits logarithms of products and powers: ln(x²y) = 2ln(x) + ln(y) (valid for positive values).", "expandlog(ln(x^2 y))",
            aliases: ["logexpand"]);
        yield return Def("expandtrig", c => Run(c, env => MapList(env.EvalArg(c.Args[0]), TrigLog.ExpandTrig)),
            AlgebraCat, "expandtrig(expr)", "Expands trig functions of sums and multiples: sin(2x) = 2sin(x)cos(x).", "expandtrig(cos(2x))",
            aliases: ["tExpand", "texpand", "trigexpand"]);
        yield return Def("tcollect", c => Run(c, env => MapList(env.EvalArg(c.Args[0]), TrigLog.CollectTrig)),
            AlgebraCat, "tcollect(expr)", "Turns products and powers of sin and cos into sums of multiple angles: sin(x)cos(x) = sin(2x)/2.", "tcollect(cos(x)^2)",
            aliases: ["tCollect", "trigcollect", "trigreduce"]);
        yield return Def("factor", c => Run(c, env =>
            {
                string? v = c.Count > 1 ? VarArg(c, 1) : null;
                var e = v is null ? env.EvalArg(c.Args[0]) : env.EvalArg(c.Args[0], v);
                return Factoring.Factor(e, v);
            }),
            AlgebraCat, "factor(expr) or factor(expr, x)", "Factors polynomials and fractions over the rationals (whole numbers into primes). With x, quadratics in x with real roots are split using radicals.",
            "factor(x^2 - 5x + 6)", min: 1, max: 2);
        yield return Def("simplify", c => Run(c, env => Simplifier.Simplify(env.EvalArg(c.Args[0]))),
            AlgebraCat, "simplify(expr)", "Simplifies: cancels fractions, combines terms, uses identities such as sin² + cos² = 1.", "simplify((x^2 - 1)/(x - 1))");
        yield return Def("together", c => Run(c, env => MapList(env.EvalArg(c.Args[0]), Simplifier.Together)),
            AlgebraCat, "together(expr)", "Writes a sum of fractions over a common denominator: 1/x + 1/y = (x + y)/(xy).", "together(1/x + 1/(x + 1))",
            aliases: ["comdenom", "comDenom"]);
        yield return Def("cancel", c => Run(c, env => MapList(env.EvalArg(c.Args[0]), Simplifier.Cancel)),
            AlgebraCat, "cancel(expr)", "Cancels common factors of a numerator and denominator.", "cancel((x^2 - 1)/(x + 1))");
        foreach (var d in PolynomialFunctions(r)) yield return d;
    }
}
