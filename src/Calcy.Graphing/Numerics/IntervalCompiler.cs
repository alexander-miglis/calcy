using System.Numerics;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Graphing.Numerics;

/// <summary>An expression compiled for interval evaluation over boxes.</summary>
public sealed class IntervalFunction
{
    private readonly Func<Interval[], Interval> _eval;

    internal IntervalFunction(Func<Interval[], Interval> eval, int arity, bool isTight)
    {
        _eval = eval;
        Arity = arity;
        IsReliable = isTight;
    }

    /// <summary>Number of variables (plot variables followed by parameters).</summary>
    public int Arity { get; }

    /// <summary>False when some part of the expression had no interval rule (bounds are then "anything").</summary>
    public bool IsReliable { get; }

    /// <summary>Encloses every value the expression takes when each variable ranges over its interval.</summary>
    public Interval Evaluate(Interval[] variables) => _eval(variables);
}

/// <summary>
/// Compiles a prepared expression (session values already substituted) into an interval evaluator.
/// Unsupported parts evaluate to <see cref="Interval.Entire"/> so results stay conservative.
/// </summary>
public static class IntervalCompiler
{
    /// <summary>Compiles <paramref name="e"/> with the given variable order.</summary>
    public static IntervalFunction Compile(Expr e, IReadOnlyList<string> variables, EvalContext ctx)
    {
        var builder = new Builder(variables, ctx);
        var f = builder.Build(e);
        return new IntervalFunction(f, variables.Count, builder.Reliable);
    }

    /// <summary>Compares an interval with zero: is "value op 0" true on the whole box, false on the whole box, or unknown?</summary>
    public static Truth CompareWithZero(Interval v, RelOp op)
    {
        if (v.IsEmpty) return Truth.False;
        Truth t = op switch
        {
            RelOp.Less => v.Hi < 0 ? Truth.True : v.Lo >= 0 ? Truth.False : Truth.Maybe,
            RelOp.LessEqual => v.Hi <= 0 ? Truth.True : v.Lo > 0 ? Truth.False : Truth.Maybe,
            RelOp.Greater => v.Lo > 0 ? Truth.True : v.Hi <= 0 ? Truth.False : Truth.Maybe,
            RelOp.GreaterEqual => v.Lo >= 0 ? Truth.True : v.Hi < 0 ? Truth.False : Truth.Maybe,
            RelOp.Equal => v.Lo == 0 && v.Hi == 0 ? Truth.True : v.ContainsZero ? Truth.Maybe : Truth.False,
            _ => v.Lo == 0 && v.Hi == 0 ? Truth.False : v.ContainsZero ? Truth.Maybe : Truth.True,
        };
        if (t == Truth.True && v.MaybeUndefined) return Truth.Maybe;
        return t;
    }

    private sealed class Builder(IReadOnlyList<string> variables, EvalContext ctx)
    {
        public bool Reliable { get; private set; } = true;

        private Func<Interval[], Interval> Unknown()
        {
            Reliable = false;
            return _ => Interval.Entire;
        }

        private static Interval Widen(double v) =>
            double.IsFinite(v) ? new Interval(Math.BitDecrement(v), Math.BitIncrement(v)) : Interval.Point(v);

        public Func<Interval[], Interval> Build(Expr e)
        {
            switch (e)
            {
                case NumberExpr n:
                {
                    if (!n.Value.IsReal) return _ => Interval.Empty;
                    var iv = n.Value.IsExactInteger && Math.Abs(n.Value.Re) < 9e15 ? Interval.Point(n.Value.Re) : Widen(n.Value.Re);
                    return _ => iv;
                }
                case ConstantExpr c:
                {
                    var iv = c.Constant switch
                    {
                        ConstantKind.Pi => Widen(Math.PI),
                        ConstantKind.E => Widen(Math.E),
                        ConstantKind.Infinity => Interval.Point(double.PositiveInfinity),
                        ConstantKind.NegativeInfinity => Interval.Point(double.NegativeInfinity),
                        _ => Interval.Empty,
                    };
                    return _ => iv;
                }
                case SymbolExpr s:
                {
                    int idx = IndexOf(s.Name);
                    if (idx < 0) return Unknown();
                    return v => v[idx];
                }
                case BoolExpr b:
                {
                    var iv = Interval.Point(b.Value ? 1 : 0);
                    return _ => iv;
                }
                case AddExpr a:
                {
                    var terms = a.Terms.Select(Build).ToArray();
                    return v =>
                    {
                        var acc = terms[0](v);
                        for (int i = 1; i < terms.Length; i++) acc += terms[i](v);
                        return acc;
                    };
                }
                case MulExpr m:
                    return BuildMul(m);
                case PowExpr p:
                    return BuildPow(p);
                case CallExpr call:
                    return BuildCall(call);
                case PiecewiseExpr pw:
                    return BuildPiecewise(pw);
                default:
                    return Unknown();
            }
        }

        private int IndexOf(string name)
        {
            for (int i = 0; i < variables.Count; i++)
                if (variables[i] == name) return i;
            return -1;
        }

        private Func<Interval[], Interval> BuildMul(MulExpr m)
        {
            // Group equal factors into powers so x·x gets the tight bound of x².
            var groups = new List<(Expr Base, int Count)>();
            foreach (var f in m.Factors)
            {
                int k = groups.FindIndex(g => g.Base.Equals(f));
                if (k >= 0 && f is SymbolExpr) groups[k] = (groups[k].Base, groups[k].Count + 1);
                else groups.Add((f, 1));
            }
            var parts = groups.Select(g =>
            {
                var b = Build(g.Base);
                int c = g.Count;
                return c == 1 ? b : v => Interval.PowInt(b(v), c);
            }).ToArray();
            return v =>
            {
                var acc = parts[0](v);
                for (int i = 1; i < parts.Length; i++) acc *= parts[i](v);
                return acc;
            };
        }

        private Func<Interval[], Interval> BuildPow(PowExpr p)
        {
            var b = Build(p.Base);
            if (p.Exponent is NumberExpr { Value.IsExactRational: true } ne)
            {
                var q = ne.Value.ReQ;
                if (q.IsInteger && BigInteger.Abs(q.Num) <= 64)
                {
                    int k = (int)q.Num;
                    return v => Interval.PowInt(b(v), k);
                }
                if (q.Num.GetBitLength() < 30 && q.Den.GetBitLength() < 30)
                {
                    long num = (long)q.Num, den = (long)q.Den;
                    return v => Interval.PowRational(b(v), num, den);
                }
            }
            if (p.Base is ConstantExpr { Constant: ConstantKind.E })
            {
                var ex = Build(p.Exponent);
                return v => Interval.Exp(ex(v));
            }
            var e = Build(p.Exponent);
            return v => Interval.Pow(b(v), e(v));
        }

        private Func<Interval[], Interval> BuildCall(CallExpr call)
        {
            if (!ctx.Functions.TryGet(call.Name, out var def)) return Unknown();
            var args = call.Args.Select(Build).ToArray();
            double toRad = ctx.Settings.ToRadians;
            var radians = new Interval(toRad, toRad);
            Func<Interval[], Interval> A0 = args.Length > 0 ? args[0] : (_ => Interval.Entire);
            Func<Interval[], Interval> AngleArg = toRad == 1.0 ? A0 : v => A0(v) * radians;
            Func<Interval, Interval> AngleOut = toRad == 1.0 ? (r => r) : (r => r / radians);
            switch (def.Name)
            {
                case "sin" when args.Length == 1: return v => Interval.Sin(AngleArg(v));
                case "cos" when args.Length == 1: return v => Interval.Cos(AngleArg(v));
                case "tan" when args.Length == 1: return v => Interval.Tan(AngleArg(v));
                case "sec" when args.Length == 1: return v => Interval.Reciprocal(Interval.Cos(AngleArg(v)));
                case "csc" when args.Length == 1: return v => Interval.Reciprocal(Interval.Sin(AngleArg(v)));
                case "cot" when args.Length == 1: return v => Interval.Reciprocal(Interval.Tan(AngleArg(v)));
                case "asin" when args.Length == 1: return v => AngleOut(Interval.Asin(A0(v)));
                case "acos" when args.Length == 1: return v => AngleOut(Interval.Acos(A0(v)));
                case "atan" when args.Length == 1: return v => AngleOut(Interval.Monotone(A0(v), Math.Atan));
                case "atan2" when args.Length == 2: return v => AngleOut(Interval.Atan2(args[0](v), args[1](v)));
                case "sinh" when args.Length == 1: return v => Interval.Monotone(A0(v), Math.Sinh);
                case "cosh" when args.Length == 1: return v => Interval.Cosh(A0(v));
                case "tanh" when args.Length == 1: return v => Interval.Monotone(A0(v), Math.Tanh);
                case "asinh" when args.Length == 1: return v => Interval.Monotone(A0(v), Math.Asinh);
                case "acosh" when args.Length == 1:
                    return v =>
                    {
                        var d = Interval.ClampDomain(A0(v), 1, double.PositiveInfinity, out bool empty);
                        return empty ? Interval.Empty : Interval.Monotone(d, Math.Acosh).WithMaybeUndefined(d.MaybeUndefined);
                    };
                case "atanh" when args.Length == 1:
                    return v =>
                    {
                        var d = Interval.ClampDomain(A0(v), -1, 1, out bool empty);
                        return empty ? Interval.Empty : Interval.Monotone(d, Math.Atanh).WithMaybeUndefined(d.MaybeUndefined);
                    };
                case "exp" when args.Length == 1: return v => Interval.Exp(A0(v));
                case "ln" when args.Length == 1: return v => Interval.Log(A0(v));
                case "log" when args.Length == 1: return v => Interval.Log(A0(v)) * new Interval(1 / Math.Log(10), 1 / Math.Log(10));
                case "log" when args.Length == 2: return v => Interval.Log(args[0](v)) / Interval.Log(args[1](v));
                case "log2" when args.Length == 1: return v => Interval.Log(A0(v)) * new Interval(1 / Math.Log(2), 1 / Math.Log(2));
                case "sqrt" when args.Length == 1: return v => Interval.Sqrt(A0(v));
                case "cbrt" when args.Length == 1: return v => Interval.Root(A0(v), 3);
                case "root" when args.Length == 2 && call.Args[1] is NumberExpr { Value.IsExactInteger: true } rn && rn.Value.Re is >= 1 and <= 64:
                {
                    long n = (long)rn.Value.Re;
                    return v => Interval.Root(A0(v), n);
                }
                case "abs" when args.Length == 1: return v => Interval.Abs(A0(v));
                case "sign" when args.Length == 1: return v => Interval.Monotone(A0(v), x => Math.Sign(x));
                case "floor" when args.Length == 1: return v => Interval.Monotone(A0(v), Math.Floor);
                case "ceil" when args.Length == 1: return v => Interval.Monotone(A0(v), Math.Ceiling);
                case "trunc" when args.Length == 1:
                    return v => Interval.Hull(Interval.Monotone(A0(v), Math.Floor), Interval.Monotone(A0(v), Math.Ceiling));
                case "round" when args.Length == 1: return v => Interval.Monotone(A0(v), x => Math.Round(x, MidpointRounding.AwayFromZero));
                case "min" when args.Length == 2: return v => Interval.Min(args[0](v), args[1](v));
                case "max" when args.Length == 2: return v => Interval.Max(args[0](v), args[1](v));
                case "re" when args.Length == 1: return A0;
                default:
                    return Unknown();
            }
        }

        private Func<Interval[], Interval> BuildPiecewise(PiecewiseExpr pw)
        {
            var cases = pw.Cases.Select(c => (Cond: BuildCondition(c.Condition), Value: Build(c.Value))).ToArray();
            Func<Interval[], Interval>? otherwise = pw.Otherwise is null ? null : Build(pw.Otherwise);
            return v =>
            {
                Interval acc = Interval.Empty;
                bool any = false;
                foreach (var (cond, value) in cases)
                {
                    var t = cond(v);
                    if (t == Truth.False) continue;
                    var val = value(v);
                    if (t == Truth.Maybe) val = val.WithMaybeUndefined();
                    acc = any ? Interval.Hull(acc, val) : val;
                    any = true;
                    if (t == Truth.True) return acc;
                }
                if (otherwise is not null)
                {
                    var o = otherwise(v);
                    return any ? Interval.Hull(acc, o) : o;
                }
                return any ? acc.WithMaybeUndefined() : Interval.Empty;
            };
        }

        public Func<Interval[], Truth> BuildCondition(Expr cond)
        {
            switch (cond)
            {
                case BoolExpr b:
                {
                    var t = b.Value ? Truth.True : Truth.False;
                    return _ => t;
                }
                case RelationExpr r:
                {
                    var l = Build(r.Left);
                    var rr = Build(r.Right);
                    var op = r.Op;
                    return v => CompareWithZero(l(v) - rr(v), op);
                }
                case LogicExpr { Op: LogicOp.Not } n:
                {
                    var inner = BuildCondition(n.Operands[0]);
                    return v => inner(v) switch { Truth.True => Truth.False, Truth.False => Truth.True, _ => Truth.Maybe };
                }
                case LogicExpr { Op: LogicOp.And } a:
                {
                    var parts = a.Operands.Select(BuildCondition).ToArray();
                    return v =>
                    {
                        var res = Truth.True;
                        foreach (var p in parts)
                        {
                            var t = p(v);
                            if (t == Truth.False) return Truth.False;
                            if (t == Truth.Maybe) res = Truth.Maybe;
                        }
                        return res;
                    };
                }
                case LogicExpr { Op: LogicOp.Or } o:
                {
                    var parts = o.Operands.Select(BuildCondition).ToArray();
                    return v =>
                    {
                        var res = Truth.False;
                        foreach (var p in parts)
                        {
                            var t = p(v);
                            if (t == Truth.True) return Truth.True;
                            if (t == Truth.Maybe) res = Truth.Maybe;
                        }
                        return res;
                    };
                }
                default:
                    Reliable = false;
                    return _ => Truth.Maybe;
            }
        }
    }
}
