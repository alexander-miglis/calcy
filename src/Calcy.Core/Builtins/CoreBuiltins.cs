using System.Numerics;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Builtins;

/// <summary>Registers the core builtin functions. Split across partial files by topic.</summary>
public static partial class CoreBuiltins
{
    public static void Register(FunctionRegistry r)
    {
        RegisterElementary(r);
        RegisterTrig(r);
        RegisterIntegers(r);
        RegisterLists(r);
        RegisterMatrices(r);
        RegisterMisc(r);
    }

    // ------------------------------------------------------------------ shared helpers

    internal static NumberExpr N(double x) => new(Number.Approx(x));
    internal static NumberExpr N(Number n) => new(n);
    internal static NumberExpr Q(BigRational q) => new(Number.Exact(q));
    internal static NumberExpr Z(BigInteger z) => new(Number.Exact(new BigRational(z)));

    internal static bool IsNumeric(Expr e) => e is NumberExpr;

    /// <summary>True when the call should produce a decimal: numeric mode or an approximate argument.</summary>
    internal static bool WantsNumeric(CallInfo c, Expr x) => x is NumberExpr n && (!n.Value.IsExact || c.Approximate);

    internal static Number NumberArg(CallInfo c, int i, string what = "a number")
    {
        if (c.Args[i] is NumberExpr n) return n.Value;
        if (c.Args[i].IsConstantValue())
        {
            var v = Evaluator.Numeric(c.Args[i], c.Context);
            if (v is not null) return v.Value;
        }
        throw c.Error($"argument {i + 1} must be {what}, not {c.Args[i]}");
    }

    internal static bool TryNumber(Expr e, out Number n)
    {
        if (e is NumberExpr ne) { n = ne.Value; return true; }
        n = default;
        return false;
    }

    internal static BigInteger IntegerArg(CallInfo c, int i, string what = "a whole number")
    {
        var n = NumberArg(c, i, what);
        if (!n.IsInteger) throw c.Error($"argument {i + 1} must be {what}, not {c.Args[i]}");
        return n.ToBigInteger();
    }

    internal static int SmallIntArg(CallInfo c, int i, int min = int.MinValue, int max = int.MaxValue)
    {
        var b = IntegerArg(c, i);
        if (b < min || b > max) throw c.Error($"argument {i + 1} must be between {min} and {max}");
        return (int)b;
    }

    internal static double RealArg(CallInfo c, int i)
    {
        var n = NumberArg(c, i);
        if (!n.IsReal) throw c.Error($"argument {i + 1} must be a real number");
        return n.Re;
    }

    internal static string SymbolArg(CallInfo c, int i)
    {
        if (c.Args[i] is SymbolExpr s) return s.Name;
        throw c.Error($"argument {i + 1} must be a variable name, not {c.Args[i]}");
    }

    internal static ListExpr ListArg(CallInfo c, int i)
    {
        if (c.Args[i] is ListExpr l) return l;
        throw c.Error($"argument {i + 1} must be a list like {{1, 2, 3}}");
    }

    internal static MatrixExpr MatrixArg(CallInfo c, int i)
    {
        return c.Args[i] switch
        {
            MatrixExpr m => m,
            ListExpr l when l.Items.All(x => x is ListExpr ll && ll.Count == ((ListExpr)l.Items[0]).Count) && l.Count > 0 =>
                MatrixExpr.FromRows([.. l.Items.Select(x => (IReadOnlyList<Expr>)((ListExpr)x).Items)]),
            _ => throw c.Error($"argument {i + 1} must be a matrix like [[1, 2], [3, 4]]"),
        };
    }

    /// <summary>Real result for real input when defined, otherwise the complex function.</summary>
    internal static Expr NumericUnary(Number v, Func<double, double> real, Func<Number, Number> complex, Func<double, bool>? realDomain = null)
    {
        if (v.IsReal)
        {
            double x = v.Re;
            if (realDomain is null || realDomain(x))
            {
                double r = real(x);
                if (!double.IsNaN(r) || double.IsNaN(x)) return N(r);
            }
        }
        return N(complex(v.ToApprox()));
    }

    internal static FunctionDefinition Def(string name, Func<CallInfo, Expr?> eval, string category, string signature, string description,
        string example = "", int min = 1, int max = 1, bool listable = true, bool hold = false, string[]? aliases = null,
        Func<double, double>? real1 = null, Func<double, double, double>? real2 = null, bool angleArg = false, bool angleResult = false) =>
        new()
        {
            Name = name,
            Evaluate = eval,
            Category = category,
            Signature = signature,
            Description = description,
            Example = example,
            MinArgs = min,
            MaxArgs = max,
            Listable = listable,
            HoldArgs = hold,
            Aliases = aliases ?? [],
            Real1 = real1,
            Real2 = real2,
            AngleArgument = angleArg,
            AngleResult = angleResult,
        };
}
