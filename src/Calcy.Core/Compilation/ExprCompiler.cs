using System.Linq.Expressions;
using System.Reflection;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using LinqExpr = System.Linq.Expressions.Expression;

namespace Calcy.Core.Compilation;

/// <summary>
/// Compiles an expression into a fast double-valued delegate for plotting and numerics.
/// Variables of the session are substituted first; the remaining free names must be the given parameters.
/// Undefined points (division by zero, sqrt of a negative) give NaN.
/// </summary>
public static class ExprCompiler
{
    public static Func<double, double> Compile1(Expr e, string variable, EvalContext ctx)
    {
        var f = Compile(e, [variable], ctx);
        return x => f([x]);
    }

    public static Func<double, double, double> Compile2(Expr e, string v1, string v2, EvalContext ctx)
    {
        var p1 = LinqExpr.Parameter(typeof(double), v1);
        var p2 = LinqExpr.Parameter(typeof(double), v2);
        var body = BuildBody(e, new Dictionary<string, LinqExpr> { [v1] = p1, [v2] = p2 }, ctx);
        return LinqExpr.Lambda<Func<double, double, double>>(body, p1, p2).Compile();
    }

    public static Func<double[], double> Compile(Expr e, IReadOnlyList<string> variables, EvalContext ctx)
    {
        var arr = LinqExpr.Parameter(typeof(double[]), "args");
        var map = new Dictionary<string, LinqExpr>();
        for (int i = 0; i < variables.Count; i++) map[variables[i]] = LinqExpr.ArrayIndex(arr, LinqExpr.Constant(i));
        var body = BuildBody(e, map, ctx);
        return LinqExpr.Lambda<Func<double[], double>>(body, arr).Compile();
    }

    /// <summary>Substitutes session variables (keeping the parameters free) and returns the prepared expression.</summary>
    public static Expr Prepare(Expr e, IEnumerable<string> variables, EvalContext ctx) =>
        Evaluator.Eval(e, ctx.WithApproximate(false).WithFree(variables));

    private static LinqExpr BuildBody(Expr e, Dictionary<string, LinqExpr> vars, EvalContext ctx)
    {
        var prepared = Prepare(e, vars.Keys, ctx);
        var free = prepared.FreeSymbols().Where(n => !vars.ContainsKey(n)).ToList();
        if (free.Count > 0)
            throw new UnknownVariablesException(free);
        var builder = new Builder(vars, ctx);
        return builder.Build(prepared);
    }

    private sealed class Builder(Dictionary<string, LinqExpr> vars, EvalContext ctx)
    {
        private static readonly MethodInfo PowMethod = typeof(Math).GetMethod(nameof(Math.Pow))!;
        private static readonly MethodInfo RootMethod = typeof(Runtime).GetMethod(nameof(Runtime.RealRoot))!;
        private static readonly MethodInfo PowIntMethod = typeof(Runtime).GetMethod(nameof(Runtime.PowInt))!;
        private static readonly MethodInfo FallbackMethod = typeof(Runtime).GetMethod(nameof(Runtime.Fallback))!;

        public LinqExpr Build(Expr e)
        {
            switch (e)
            {
                case NumberExpr n:
                    return LinqExpr.Constant(n.Value.IsReal ? n.Value.Re : double.NaN);
                case ConstantExpr c:
                    return LinqExpr.Constant(c.ApproxValue);
                case SymbolExpr s:
                    if (vars.TryGetValue(s.Name, out var p)) return p;
                    throw new UnknownVariablesException([s.Name]);
                case AddExpr a:
                    return a.Terms.Select(Build).Aggregate(LinqExpr.Add);
                case MulExpr m:
                    return m.Factors.Select(Build).Aggregate(LinqExpr.Multiply);
                case PowExpr pw:
                    return BuildPow(pw);
                case CallExpr call:
                    return BuildCall(call);
                case PiecewiseExpr pc:
                {
                    LinqExpr result = pc.Otherwise is null ? LinqExpr.Constant(double.NaN) : Build(pc.Otherwise);
                    for (int i = pc.Cases.Length - 1; i >= 0; i--)
                        result = LinqExpr.Condition(BuildBool(pc.Cases[i].Condition), Build(pc.Cases[i].Value), result);
                    return result;
                }
                case BoolExpr b:
                    return LinqExpr.Constant(b.Value ? 1.0 : 0.0);
                default:
                    return Fallback(e);
            }
        }

        private LinqExpr BuildPow(PowExpr pw)
        {
            var b = Build(pw.Base);
            if (pw.Exponent is NumberExpr { Value.IsExactRational: true } ne)
            {
                var q = ne.Value.ReQ;
                if (q.IsInteger && q.Num >= -64 && q.Num <= 64)
                {
                    int k = (int)q.Num;
                    switch (k)
                    {
                        case 1: return b;
                        case 2:
                        {
                            var t = LinqExpr.Variable(typeof(double));
                            return LinqExpr.Block([t], LinqExpr.Assign(t, b), LinqExpr.Multiply(t, t));
                        }
                        case -1: return LinqExpr.Divide(LinqExpr.Constant(1.0), b);
                        default: return LinqExpr.Call(PowIntMethod, b, LinqExpr.Constant(k));
                    }
                }
                if (q.Num.GetBitLength() < 30 && q.Den.GetBitLength() < 30)
                {
                    if (q == BigRational.Half) return LinqExpr.Call(typeof(Math).GetMethod(nameof(Math.Sqrt))!, b);
                    // Odd roots of negative numbers stay real.
                    return LinqExpr.Call(RootMethod, b, LinqExpr.Constant((double)q.Num), LinqExpr.Constant((double)q.Den));
                }
            }
            return LinqExpr.Call(PowMethod, b, Build(pw.Exponent));
        }

        private LinqExpr BuildCall(CallExpr call)
        {
            if (!ctx.Functions.TryGet(call.Name, out var def)) return Fallback(call);
            double toRad = ctx.Settings.ToRadians;
            if (call.Args.Length == 1 && def.Real1 is not null)
            {
                var arg = Build(call.Args[0]);
                if (def.AngleArgument && toRad != 1.0) arg = LinqExpr.Multiply(arg, LinqExpr.Constant(toRad));
                LinqExpr result = LinqExpr.Invoke(LinqExpr.Constant(def.Real1), arg);
                if (def.AngleResult && toRad != 1.0) result = LinqExpr.Divide(result, LinqExpr.Constant(toRad));
                return result;
            }
            if (call.Args.Length == 2 && def.Real2 is not null)
            {
                LinqExpr result = LinqExpr.Invoke(LinqExpr.Constant(def.Real2), Build(call.Args[0]), Build(call.Args[1]));
                if (def.AngleResult && toRad != 1.0) result = LinqExpr.Divide(result, LinqExpr.Constant(toRad));
                return result;
            }
            return Fallback(call);
        }

        private LinqExpr BuildBool(Expr cond)
        {
            switch (cond)
            {
                case BoolExpr b:
                    return LinqExpr.Constant(b.Value);
                case RelationExpr r:
                {
                    var l = Build(r.Left);
                    var rr = Build(r.Right);
                    return r.Op switch
                    {
                        RelOp.Less => LinqExpr.LessThan(l, rr),
                        RelOp.LessEqual => LinqExpr.LessThanOrEqual(l, rr),
                        RelOp.Greater => LinqExpr.GreaterThan(l, rr),
                        RelOp.GreaterEqual => LinqExpr.GreaterThanOrEqual(l, rr),
                        RelOp.Equal => LinqExpr.LessThanOrEqual(LinqExpr.Call(typeof(Math).GetMethod(nameof(Math.Abs), [typeof(double)])!, LinqExpr.Subtract(l, rr)), LinqExpr.Constant(1e-12)),
                        _ => LinqExpr.GreaterThan(LinqExpr.Call(typeof(Math).GetMethod(nameof(Math.Abs), [typeof(double)])!, LinqExpr.Subtract(l, rr)), LinqExpr.Constant(1e-12)),
                    };
                }
                case LogicExpr { Op: LogicOp.Not } n:
                    return LinqExpr.Not(BuildBool(n.Operands[0]));
                case LogicExpr { Op: LogicOp.And } a:
                    return a.Operands.Select(BuildBool).Aggregate(LinqExpr.AndAlso);
                case LogicExpr { Op: LogicOp.Or } o:
                    return o.Operands.Select(BuildBool).Aggregate(LinqExpr.OrElse);
                case LogicExpr x:
                    return x.Operands.Select(BuildBool).Aggregate(LinqExpr.ExclusiveOr);
                default:
                    return LinqExpr.NotEqual(Build(cond), LinqExpr.Constant(0.0));
            }
        }

        /// <summary>Slow path: evaluate with the full engine for each point.</summary>
        private LinqExpr Fallback(Expr e)
        {
            var names = vars.Keys.ToArray();
            var values = LinqExpr.NewArrayInit(typeof(double), names.Select(n => vars[n]));
            var state = new FallbackState(e, names, ctx.WithApproximate());
            return LinqExpr.Call(FallbackMethod, LinqExpr.Constant(state), values);
        }
    }

    public sealed record FallbackState(Expr Expression, string[] Names, EvalContext Context);

    /// <summary>Helpers called from compiled code.</summary>
    public static class Runtime
    {
        public static double RealRoot(double x, double p, double q)
        {
            if (x >= 0) return Math.Pow(x, p / q);
            // q odd: real root of a negative number; q even: not real.
            if ((long)q % 2 == 0) return double.NaN;
            double mag = Math.Pow(-x, p / q);
            return (long)p % 2 == 0 ? mag : -mag;
        }

        public static double PowInt(double x, int n)
        {
            if (n < 0) return 1 / PowInt(x, -n);
            double r = 1, b = x;
            while (n > 0)
            {
                if ((n & 1) != 0) r *= b;
                b *= b;
                n >>= 1;
            }
            return r;
        }

        public static double Fallback(FallbackState s, double[] values)
        {
            try
            {
                var ctx = s.Context.WithLocals(s.Names.Select((n, i) => new KeyValuePair<string, Expr>(n, new NumberExpr(Number.Approx(values[i])))));
                var r = Evaluator.Eval(s.Expression, ctx);
                return r is NumberExpr { Value.IsReal: true } n ? n.Value.Re : double.NaN;
            }
            catch (CalcException)
            {
                return double.NaN;
            }
            catch (ArithmeticException)
            {
                return double.NaN;
            }
        }
    }
}

/// <summary>Thrown when an expression to compile still has names without values (the UI offers sliders for them).</summary>
public sealed class UnknownVariablesException(IReadOnlyList<string> names)
    : CalcException(names.Count == 1 ? $"'{names[0]}' has no value yet" : $"These names have no value yet: {string.Join(", ", names)}")
{
    public IReadOnlyList<string> Names { get; } = names;
}
