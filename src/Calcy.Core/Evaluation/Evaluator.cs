using System.Collections.Immutable;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Evaluation;

/// <summary>Evaluates expressions: substitutes variables, applies functions and simplifies.</summary>
public static class Evaluator
{
    public static Expr Eval(Expr e, EvalContext ctx)
    {
        switch (e)
        {
            case NumberExpr n:
                return ctx.Approximate && n.Value.IsExact ? new NumberExpr(n.Value.ToApprox()) : n;
            case ConstantExpr c:
                if (ctx.Approximate && c.Constant is ConstantKind.Pi or ConstantKind.E) return new NumberExpr(Number.Approx(c.ApproxValue));
                return c;
            case SymbolExpr s:
                return ResolveSymbol(s, ctx);
            case AddExpr a:
            {
                var d = ctx.Deeper();
                var terms = new Expr[a.Terms.Length];
                for (int i = 0; i < terms.Length; i++) terms[i] = Eval(a.Terms[i], d);
                return Algebra.Add(terms);
            }
            case MulExpr m:
            {
                var d = ctx.Deeper();
                var factors = new Expr[m.Factors.Length];
                for (int i = 0; i < factors.Length; i++) factors[i] = Eval(m.Factors[i], d);
                return Algebra.Mul(factors);
            }
            case PowExpr p:
            {
                var d = ctx.Deeper();
                // Keep exact rational exponents in numeric mode so (-8)^(1/3) stays -2.
                var ex = ctx.Approximate && p.Exponent is NumberExpr { Value.IsExactRational: true } ? p.Exponent : Eval(p.Exponent, d);
                // e^x stays e^x in decimal results; 2.718281828^x helps nobody.
                if (ctx.Approximate && !ex.IsConstantValue() && p.Base is ConstantExpr { Constant: ConstantKind.E } or SymbolExpr { Name: "e" })
                {
                    var exactBase = Eval(p.Base, d.WithApproximate(false));
                    if (exactBase is ConstantExpr { Constant: ConstantKind.E }) return Algebra.Pow(exactBase, ex);
                }
                var b = Eval(p.Base, d);
                var r = Algebra.Pow(b, ex);
                if (ctx.Approximate && r is PowExpr rp && rp.Base is NumberExpr && rp.Exponent is NumberExpr)
                    return Algebra.Pow(Eval(rp.Base, d), Eval(rp.Exponent, d));
                return r;
            }
            case CallExpr c:
                return EvalCall(c, ctx);
            case RelationExpr r:
                return EvalRelation(r, ctx);
            case LogicExpr l:
                return EvalLogic(l, ctx);
            case ListExpr l:
            {
                var d = ctx.Deeper();
                return new ListExpr([.. l.Items.Select(x => Eval(x, d))]);
            }
            case MatrixExpr mx:
            {
                var d = ctx.Deeper();
                return new MatrixExpr(mx.Rows, mx.Cols, [.. mx.Items.Select(x => Eval(x, d))]);
            }
            case TupleExpr t:
            {
                var d = ctx.Deeper();
                return new TupleExpr([.. t.Items.Select(x => Eval(x, d))]);
            }
            case PiecewiseExpr pw:
                return EvalPiecewise(pw, ctx);
            case IndexExpr ix:
                return EvalIndex(ix, ctx);
            default:
                return e;
        }
    }

    private static Expr ResolveSymbol(SymbolExpr s, EvalContext ctx)
    {
        if (ctx.Locals.TryGetValue(s.Name, out var local))
        {
            if (local is SymbolExpr ls && ls.Name == s.Name) return s;
            return ctx.Approximate ? ApproximateValue(local, ctx) : local;
        }
        if (ctx.Env.TryGetVariable(s.Name, out var value))
        {
            if (value is SymbolExpr vs && vs.Name == s.Name) return s;
            // Stored values are already simplified; re-evaluate only to resolve later definitions or approximate.
            return Eval(value, ctx.Deeper());
        }
        switch (s.Name)
        {
            case "e":
                return ctx.Approximate ? new NumberExpr(Number.Approx(Math.E)) : ConstantExpr.E;
            case "π":
                // From split names such as πr (π times r).
                return ctx.Approximate ? new NumberExpr(Number.Approx(Math.PI)) : ConstantExpr.Pi;
            case "i":
                return new NumberExpr(ctx.Approximate ? Number.Approx(0, 1) : Number.I);
            case "ans":
                throw new CalcException("There is no previous answer yet");
        }
        return s;
    }

    private static Expr ApproximateValue(Expr v, EvalContext ctx) => v switch
    {
        NumberExpr n => n.Value.IsExact ? new NumberExpr(n.Value.ToApprox()) : n,
        _ => Eval(v, ctx.Deeper()),
    };

    // ------------------------------------------------------------------ calls

    private static Expr EvalCall(CallExpr c, EvalContext ctx)
    {
        var name = c.Name;
        if (ctx.Locals.TryGetValue(name, out var localFn) && localFn is LambdaExpr localLambda)
            return ApplyLambda(localLambda, c.Args.Select(a => Eval(a, ctx)).ToList(), ctx, name);
        if (ctx.Env.TryGetFunction(name, out var uf))
        {
            var args = c.Args.Select(a => Eval(a, ctx)).ToList();
            return ApplyUser(uf, args, ctx);
        }
        if (ctx.Env.TryGetVariable(name, out var fnValue) && fnValue is LambdaExpr lambda)
            return ApplyLambda(lambda, c.Args.Select(a => Eval(a, ctx)).ToList(), ctx, name);
        if (ctx.Functions.TryGet(name, out var def))
            return CallBuiltin(def, c.Args, ctx);
        throw new CalcException($"Unknown function '{name}'");
    }

    /// <summary>Calls a builtin with unevaluated arguments.</summary>
    public static Expr CallBuiltin(FunctionDefinition def, IReadOnlyList<Expr> rawArgs, EvalContext ctx)
    {
        if (rawArgs.Count < def.MinArgs || (def.MaxArgs >= 0 && rawArgs.Count > def.MaxArgs))
            throw new CalcException(ArgCountMessage(def, rawArgs.Count));
        var d = ctx.Deeper();
        IReadOnlyList<Expr> args = def.HoldArgs ? rawArgs : rawArgs.Select(a => Eval(a, d)).ToList();
        return Apply(def, args, d);
    }

    private static int IndexOfList(IReadOnlyList<Expr> args)
    {
        for (int i = 0; i < args.Count; i++)
            if (args[i] is ListExpr) return i;
        return -1;
    }

    /// <summary>Calls a builtin with already evaluated arguments.</summary>
    public static Expr Apply(FunctionDefinition def, IReadOnlyList<Expr> args, EvalContext ctx)
    {
        // Listable functions map over a list in any argument: binompdf(10, 0.5, {2, 3}) gives two values.
        // Other list arguments of the same length are walked in step; everything else is repeated.
        int listAt = def.Listable && !def.HoldArgs ? IndexOfList(args) : -1;
        if (listAt >= 0)
        {
            var list = (ListExpr)args[listAt];
            var mapped = new Expr[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var a = new List<Expr>(args.Count);
                for (int k = 0; k < args.Count; k++)
                    a.Add(k == listAt ? list.Items[i] : args[k] is ListExpr l2 && l2.Count == list.Count ? l2.Items[i] : args[k]);
                mapped[i] = Apply(def, a, ctx);
            }
            return new ListExpr([.. mapped]);
        }
        var result = def.Evaluate(new CallInfo(def, args, ctx));
        return result ?? new CallExpr(def.Name, [.. args]);
    }

    private static string ArgCountMessage(FunctionDefinition def, int got)
    {
        string need = def.MaxArgs < 0 ? $"at least {def.MinArgs}" : def.MinArgs == def.MaxArgs ? $"{def.MinArgs}" : $"{def.MinArgs} to {def.MaxArgs}";
        string usage = string.IsNullOrEmpty(def.Signature) ? "" : $". Use {def.Signature}";
        return $"{def.Name} needs {need} argument{(need == "1" ? "" : "s")}, you gave {got}{usage}";
    }

    public static Expr ApplyUser(UserFunction f, IReadOnlyList<Expr> args, EvalContext ctx)
    {
        if (args.Count != f.Parameters.Length)
            throw new CalcException($"{f.Name} needs {f.Parameters.Length} argument{(f.Parameters.Length == 1 ? "" : "s")}, you gave {args.Count}");
        // User functions see their parameters and the globals, not the caller's local names.
        var inner = ctx.Deeper().WithOnlyLocals(f.Parameters.Zip(args, (p, a) => new KeyValuePair<string, Expr>(p, a)));
        return Eval(f.Body, inner);
    }

    public static Expr ApplyLambda(LambdaExpr lambda, IReadOnlyList<Expr> args, EvalContext ctx, string? name = null)
    {
        if (args.Count != lambda.Parameters.Length)
            throw new CalcException($"{name ?? "This function"} needs {lambda.Parameters.Length} argument{(lambda.Parameters.Length == 1 ? "" : "s")}, you gave {args.Count}");
        var inner = ctx.Deeper().WithLocals(lambda.Parameters.Zip(args, (p, a) => new KeyValuePair<string, Expr>(p, a)));
        return Eval(lambda.Body, inner);
    }

    /// <summary>Applies anything callable (a lambda, a user function name or a builtin name) to arguments.</summary>
    public static Expr Invoke(Expr callable, IReadOnlyList<Expr> args, EvalContext ctx)
    {
        switch (callable)
        {
            case LambdaExpr l:
                return ApplyLambda(l, args, ctx);
            case SymbolExpr s when ctx.Env.TryGetFunction(s.Name, out var uf):
                return ApplyUser(uf, args, ctx);
            case SymbolExpr s when ctx.Functions.TryGet(s.Name, out var def):
                return Apply(def, args, ctx.Deeper());
            default:
                throw new CalcException("Expected a function, for example x => x^2 or the name of a function");
        }
    }

    // ------------------------------------------------------------------ relations and logic

    private static Expr EvalRelation(RelationExpr r, EvalContext ctx)
    {
        var d = ctx.Deeper();
        var l = Eval(r.Left, d);
        var rr = Eval(r.Right, d);
        var decided = Compare(r.Op, l, rr, d);
        return decided is bool b ? BoolExpr.Of(b) : new RelationExpr(r.Op, l, rr);
    }

    /// <summary>Decides a comparison when both sides are known values, otherwise returns null.</summary>
    public static bool? Compare(RelOp op, Expr l, Expr r, EvalContext ctx)
    {
        if (l is StringExpr ls && r is StringExpr rs)
        {
            int c = string.CompareOrdinal(ls.Value, rs.Value);
            return Holds(op, c);
        }
        if (l is BoolExpr lb && r is BoolExpr rb && op is RelOp.Equal or RelOp.NotEqual)
            return (lb.Value == rb.Value) == (op == RelOp.Equal);
        if (op is RelOp.Equal or RelOp.NotEqual)
        {
            if (l.Equals(r) && l.IsConstantValue()) return op == RelOp.Equal;
            if (l is ListExpr ll && r is ListExpr rl)
            {
                if (ll.Count != rl.Count) return op == RelOp.NotEqual;
                bool all = true;
                for (int i = 0; i < ll.Count; i++)
                {
                    var e = Compare(RelOp.Equal, ll.Items[i], rl.Items[i], ctx);
                    if (e is null) return null;
                    all &= e.Value;
                }
                return all == (op == RelOp.Equal);
            }
        }
        if (!IsNumericValue(l) || !IsNumericValue(r)) return null;
        var lv = Numeric(l, ctx);
        var rv = Numeric(r, ctx);
        if (lv is null || rv is null) return null;
        var a = lv.Value;
        var b = rv.Value;
        if (op is RelOp.Equal or RelOp.NotEqual)
        {
            bool eq;
            if (a.IsExact && b.IsExact) eq = a.Equals(b);
            else
            {
                double scale = Math.Max(1, Math.Max(a.AbsDouble(), b.AbsDouble()));
                eq = Number.Hypot(a.Re - b.Re, a.Im - b.Im) <= 1e-12 * scale;
            }
            if (!eq && l is not NumberExpr && r is not NumberExpr && (a.IsExact || b.IsExact))
            {
                // Exact symbolic forms such as √2 and 2^(1/2) are compared numerically above.
            }
            return eq == (op == RelOp.Equal);
        }
        if (!a.IsReal || !b.IsReal) throw new CalcException("Complex numbers cannot be compared with < or >");
        int cmp = a.IsExact && b.IsExact ? Number.CompareReal(a, b) : a.Re.CompareTo(b.Re);
        return Holds(op, cmp);
    }

    private static bool Holds(RelOp op, int c) => op switch
    {
        RelOp.Equal => c == 0,
        RelOp.NotEqual => c != 0,
        RelOp.Less => c < 0,
        RelOp.LessEqual => c <= 0,
        RelOp.Greater => c > 0,
        RelOp.GreaterEqual => c >= 0,
        _ => false,
    };

    private static bool IsNumericValue(Expr e) => e is NumberExpr || (e.IsConstantValue() && !e.ContainsKind(ExprKind.Symbol));

    /// <summary>The numeric value of a constant expression (exact when it is an exact number).</summary>
    public static Number? Numeric(Expr e, EvalContext ctx)
    {
        if (e is NumberExpr n) return n.Value;
        if (e is ConstantExpr { Constant: ConstantKind.Infinity }) return Number.Approx(double.PositiveInfinity);
        if (e is ConstantExpr { Constant: ConstantKind.NegativeInfinity }) return Number.Approx(double.NegativeInfinity);
        var approx = Eval(e, ctx.WithApproximate());
        return approx is NumberExpr an ? an.Value : null;
    }

    private static Expr EvalLogic(LogicExpr l, EvalContext ctx)
    {
        var d = ctx.Deeper();
        switch (l.Op)
        {
            case LogicOp.Not:
            {
                var v = Eval(l.Operands[0], d);
                return v is BoolExpr b ? BoolExpr.Of(!b.Value) : new LogicExpr(LogicOp.Not, [v]);
            }
            case LogicOp.And:
            {
                var rest = new List<Expr>();
                foreach (var o in l.Operands)
                {
                    var v = Eval(o, d);
                    if (v is BoolExpr b)
                    {
                        if (!b.Value) return BoolExpr.False;
                    }
                    else rest.Add(v);
                }
                return rest.Count == 0 ? BoolExpr.True : rest.Count == 1 ? rest[0] : new LogicExpr(LogicOp.And, [.. rest]);
            }
            case LogicOp.Or:
            {
                var rest = new List<Expr>();
                foreach (var o in l.Operands)
                {
                    var v = Eval(o, d);
                    if (v is BoolExpr b)
                    {
                        if (b.Value) return BoolExpr.True;
                    }
                    else rest.Add(v);
                }
                return rest.Count == 0 ? BoolExpr.False : rest.Count == 1 ? rest[0] : new LogicExpr(LogicOp.Or, [.. rest]);
            }
            default:
            {
                var vals = l.Operands.Select(o => Eval(o, d)).ToList();
                if (vals.All(v => v is BoolExpr)) return BoolExpr.Of(vals.Count(v => ((BoolExpr)v).Value) % 2 == 1);
                return new LogicExpr(LogicOp.Xor, [.. vals]);
            }
        }
    }

    private static Expr EvalPiecewise(PiecewiseExpr pw, EvalContext ctx)
    {
        var d = ctx.Deeper();
        var remaining = ImmutableArray.CreateBuilder<(Expr, Expr)>();
        foreach (var (cond, value) in pw.Cases)
        {
            var c = Eval(cond, d);
            if (remaining.Count == 0)
            {
                if (c is BoolExpr { Value: true }) return Eval(value, d);
                if (c is BoolExpr { Value: false }) continue;
            }
            if (c is BoolExpr { Value: false }) continue;
            remaining.Add((c, Eval(value, d)));
        }
        if (remaining.Count == 0) return pw.Otherwise is null ? ConstantExpr.Undefined : Eval(pw.Otherwise, d);
        return new PiecewiseExpr(remaining.ToImmutable(), pw.Otherwise is null ? null : Eval(pw.Otherwise, d));
    }

    private static Expr EvalIndex(IndexExpr ix, EvalContext ctx)
    {
        var d = ctx.Deeper();
        var target = Eval(ix.Target, d);
        var idx = ix.Indices.Select(i => Eval(i, d)).ToList();
        int Index(Expr e, int count, string what)
        {
            if (e is not NumberExpr n || !n.Value.IsInteger)
                throw new CalcException($"An index must be a whole number, not {e}");
            var k = (int)n.Value.Re;
            if (k < 0) k = count + 1 + k; // L[-1] is the last element
            if (k < 1 || k > count) throw new CalcException($"Index {n.Value} is out of range: the {what} has {count} element{(count == 1 ? "" : "s")} (indices start at 1)");
            return k - 1;
        }
        switch (target)
        {
            case ListExpr l when idx.Count == 1:
                if (idx[0] is ListExpr sel) return new ListExpr([.. sel.Items.Select(s => l.Items[Index(s, l.Count, "list")])]);
                return l.Items[Index(idx[0], l.Count, "list")];
            case TupleExpr t when idx.Count == 1:
                return t.Items[Index(idx[0], t.Items.Length, "point")];
            case MatrixExpr m when idx.Count == 2:
                return m[Index(idx[0], m.Rows, "matrix"), Index(idx[1], m.Cols, "matrix")];
            case MatrixExpr m when idx.Count == 1:
            {
                int r = Index(idx[0], m.Rows, "matrix");
                return new ListExpr([.. Enumerable.Range(0, m.Cols).Select(c => m[r, c])]);
            }
            case StringExpr s when idx.Count == 1:
                return new StringExpr(s.Value[Index(idx[0], s.Value.Length, "text")].ToString());
            case SymbolExpr:
                return new IndexExpr(target, [.. idx]);
            default:
                throw new CalcException("Only lists, points, matrices and text can be indexed");
        }
    }
}
