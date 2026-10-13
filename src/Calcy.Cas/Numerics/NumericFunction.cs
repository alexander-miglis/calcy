using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Cas.Numerics;

/// <summary>
/// Compiles internal expressions (radian semantics) to fast real-valued delegates using the builtins' Real1/Real2
/// implementations. Undefined points give NaN; odd roots of negative numbers are real.
/// </summary>
internal static class NumericFunction
{
    /// <summary>Compiles e as a function of x, or returns null when it contains other symbols or unsupported parts.</summary>
    public static Func<double, double>? Compile(Expr e, string x)
    {
        var f = Compile(e, [x]);
        if (f is null) return null;
        return v => f([v]);
    }

    /// <summary>Compiles e as a function of the given variables (values passed in the same order).</summary>
    public static Func<double[], double>? Compile(Expr e, IReadOnlyList<string> vars)
    {
        var env = CasEnv.Current;
        if (env is null) return null;
        try
        {
            return Build(e, vars, env.Ctx.Functions);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static Func<double[], double> Build(Expr e, IReadOnlyList<string> vars, FunctionRegistry fns)
    {
        switch (e)
        {
            case NumberExpr n:
            {
                double v = n.Value.IsReal ? n.Value.Re : double.NaN;
                return _ => v;
            }
            case ConstantExpr c:
            {
                double v = c.ApproxValue;
                return _ => v;
            }
            case SymbolExpr s:
            {
                int i = IndexOf(vars, s.Name);
                if (i < 0) throw new NotSupportedException();
                return a => a[i];
            }
            case AddExpr add:
            {
                var parts = add.Terms.Select(t => Build(t, vars, fns)).ToArray();
                return a =>
                {
                    double s = 0;
                    foreach (var p in parts) s += p(a);
                    return s;
                };
            }
            case MulExpr mul:
            {
                var parts = mul.Factors.Select(t => Build(t, vars, fns)).ToArray();
                return a =>
                {
                    double s = 1;
                    foreach (var p in parts) s *= p(a);
                    return s;
                };
            }
            case PowExpr pow:
                return BuildPow(pow, vars, fns);
            case CallExpr call:
                return BuildCall(call, vars, fns);
            case PiecewiseExpr pw:
            {
                var cases = pw.Cases.Select(c => (Cond: BuildCondition(c.Condition, vars, fns), Value: Build(c.Value, vars, fns))).ToArray();
                var otherwise = pw.Otherwise is null ? null : Build(pw.Otherwise, vars, fns);
                return a =>
                {
                    foreach (var (cond, value) in cases)
                        if (cond(a)) return value(a);
                    return otherwise is null ? double.NaN : otherwise(a);
                };
            }
            default:
                throw new NotSupportedException();
        }
    }

    private static int IndexOf(IReadOnlyList<string> vars, string name)
    {
        for (int i = 0; i < vars.Count; i++)
            if (vars[i] == name) return i;
        return -1;
    }

    private static Func<double[], double> BuildPow(PowExpr p, IReadOnlyList<string> vars, FunctionRegistry fns)
    {
        var b = Build(p.Base, vars, fns);
        if (p.Exponent is NumberExpr { Value.IsExactRational: true } q)
        {
            var r = q.Value.ReQ;
            if (r.IsInteger && r.Num >= -64 && r.Num <= 64)
            {
                int k = (int)r.Num;
                return a => IntPow(b(a), k);
            }
            if (!r.Den.IsEven && r.Den < 1000 && r.Num.GetBitLength() < 30)
            {
                double num = (double)r.Num, den = (double)r.Den;
                bool evenNum = r.Num.IsEven;
                return a =>
                {
                    double v = b(a);
                    if (v >= 0) return Math.Pow(v, num / den);
                    double m = Math.Pow(-v, num / den);
                    return evenNum ? m : -m;
                };
            }
            double ex = r.ToDouble();
            return a => Math.Pow(b(a), ex);
        }
        var e = Build(p.Exponent, vars, fns);
        return a =>
        {
            double bv = b(a), ev = e(a);
            if (bv < 0 && Math.Floor(ev) != ev) return double.NaN;
            return Math.Pow(bv, ev);
        };
    }

    private static double IntPow(double x, int k)
    {
        if (k < 0) return 1 / IntPow(x, -k);
        double r = 1;
        while (k > 0)
        {
            if ((k & 1) != 0) r *= x;
            x *= x;
            k >>= 1;
        }
        return r;
    }

    private static Func<double[], double> BuildCall(CallExpr c, IReadOnlyList<string> vars, FunctionRegistry fns)
    {
        if (!fns.TryGet(c.Name, out var def)) throw new NotSupportedException();
        if (c.Args.Length == 1 && def.Real1 is { } r1)
        {
            var arg = Build(c.Args[0], vars, fns);
            return a =>
            {
                double v = arg(a);
                if (double.IsNaN(v)) return double.NaN;
                try { return r1(v); }
                catch (ArithmeticException) { return double.NaN; }
            };
        }
        if (c.Args.Length == 2 && def.Real2 is { } r2)
        {
            var a0 = Build(c.Args[0], vars, fns);
            var a1 = Build(c.Args[1], vars, fns);
            return a =>
            {
                double v0 = a0(a), v1 = a1(a);
                if (double.IsNaN(v0) || double.IsNaN(v1)) return double.NaN;
                try { return r2(v0, v1); }
                catch (ArithmeticException) { return double.NaN; }
            };
        }
        throw new NotSupportedException();
    }

    private static Func<double[], bool> BuildCondition(Expr cond, IReadOnlyList<string> vars, FunctionRegistry fns)
    {
        switch (cond)
        {
            case BoolExpr b:
                return _ => b.Value;
            case RelationExpr r:
            {
                var l = Build(r.Left, vars, fns);
                var rr = Build(r.Right, vars, fns);
                return r.Op switch
                {
                    RelOp.Less => a => l(a) < rr(a),
                    RelOp.LessEqual => a => l(a) <= rr(a),
                    RelOp.Greater => a => l(a) > rr(a),
                    RelOp.GreaterEqual => a => l(a) >= rr(a),
                    RelOp.Equal => a => l(a) == rr(a),
                    _ => a => l(a) != rr(a),
                };
            }
            case LogicExpr { Op: LogicOp.And } la:
            {
                var parts = la.Operands.Select(o => BuildCondition(o, vars, fns)).ToArray();
                return a => parts.All(p => p(a));
            }
            case LogicExpr { Op: LogicOp.Or } lo:
            {
                var parts = lo.Operands.Select(o => BuildCondition(o, vars, fns)).ToArray();
                return a => parts.Any(p => p(a));
            }
            case LogicExpr { Op: LogicOp.Not } ln:
            {
                var p = BuildCondition(ln.Operands[0], vars, fns);
                return a => !p(a);
            }
            default:
                throw new NotSupportedException();
        }
    }
}
