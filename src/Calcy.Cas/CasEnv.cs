using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas;

/// <summary>
/// The environment of one CAS call: the caller's context, an internal context (exact, radian angles) used to rebuild
/// expressions, cancellation and work limits. Internally every trigonometric function works in radians; inputs are
/// mapped in with <see cref="Internalize"/> and results mapped back with <see cref="Externalize"/>.
/// </summary>
internal sealed class CasEnv
{
    [ThreadStatic] private static CasEnv? _current;

    private int _depth;

    private CasEnv(EvalContext user)
    {
        User = user;
        var settings = user.Settings.Clone();
        settings.Angle = AngleMode.Radian;
        Ctx = new EvalContext(settings, user.Env, user.Functions, approximate: false, user.Cancellation);
        Angle = user.Settings.Angle;
        Complex = user.Settings.Complex;
    }

    /// <summary>The context of the caller (its settings, variables and cancellation).</summary>
    public EvalContext User { get; }

    /// <summary>Exact context in radian mode used for rebuilding internal expressions.</summary>
    public EvalContext Ctx { get; }

    public AngleMode Angle { get; }
    public ComplexMode Complex { get; }

    /// <summary>True when the result should be turned into decimals before it is returned.</summary>
    public bool ApproximateResult { get; set; }

    public static CasEnv? Current => _current;

    public static CasEnv Require => _current ?? throw new InvalidOperationException("No CAS scope is active");

    /// <summary>Starts a CAS scope for a call; dispose it to restore the previous one.</summary>
    public static Scope Enter(EvalContext ctx)
    {
        var previous = _current;
        var env = new CasEnv(ctx);
        if (ctx.Approximate) env.ApproximateResult = true;
        _current = env;
        return new Scope(previous);
    }

    public readonly struct Scope(CasEnv? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }

    /// <summary>Throws when the user cancelled the computation or the stack is nearly exhausted.</summary>
    public static void Check()
    {
        var c = _current;
        if (c is null) return;
        c.User.Cancellation.ThrowIfCancellationRequested();
    }

    /// <summary>Guards a recursive algorithm: increments the depth and throws when it is too deep.</summary>
    public static DepthGuard Deeper(int limit = 200)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        var c = _current;
        if (c is null) return new DepthGuard(null);
        c.User.Cancellation.ThrowIfCancellationRequested();
        if (++c._depth > limit)
        {
            c._depth--;
            throw new CasLimitException("The expression is too complicated");
        }
        return new DepthGuard(c);
    }

    public readonly struct DepthGuard(CasEnv? env) : IDisposable
    {
        public void Dispose()
        {
            if (env is not null) env._depth--;
        }
    }

    // ------------------------------------------------------------------ rebuilding

    /// <summary>Applies a builtin function (exact, radians) to arguments, or builds the raw call when unknown.</summary>
    public static Expr Fn(string name, params Expr[] args)
    {
        var env = _current;
        if (env is not null && !CasModule.IsOwnFunction(name) && env.Ctx.Functions.TryGet(name, out var def))
        {
            if (args.Length < def.MinArgs || (def.MaxArgs >= 0 && args.Length > def.MaxArgs)) return new CallExpr(name, [.. args]);
            return Evaluator.Apply(def, args, env.Ctx);
        }
        return new CallExpr(name, [.. args]);
    }

    /// <summary>Like <see cref="Fn"/> but returns null instead of throwing when the function is undefined at the arguments.</summary>
    public static Expr? TryFn(string name, params Expr[] args)
    {
        try { return Fn(name, args); }
        catch (CalcException) { return null; }
        catch (DivideByZeroException) { return null; }
        catch (ArithmeticException) { return null; }
    }

    /// <summary>Rebuilds an expression bottom-up with the simplifying constructors and builtin functions.</summary>
    public static Expr Rebuild(Expr e)
    {
        switch (e)
        {
            case NumberExpr or ConstantExpr or SymbolExpr or StringExpr or BoolExpr:
                return e;
            case AddExpr a:
            {
                var t = new Expr[a.Terms.Length];
                for (int i = 0; i < t.Length; i++) t[i] = Rebuild(a.Terms[i]);
                return Algebra.Add(t);
            }
            case MulExpr m:
            {
                var f = new Expr[m.Factors.Length];
                for (int i = 0; i < f.Length; i++) f[i] = Rebuild(m.Factors[i]);
                return Algebra.Mul(f);
            }
            case PowExpr p:
                return Algebra.Pow(Rebuild(p.Base), Rebuild(p.Exponent));
            case CallExpr c:
            {
                var args = new Expr[c.Args.Length];
                for (int i = 0; i < args.Length; i++) args[i] = Rebuild(c.Args[i]);
                return Fn(c.Name, args);
            }
            case RelationExpr r:
            {
                var l = Rebuild(r.Left);
                var rr = Rebuild(r.Right);
                var env = _current;
                if (env is not null && l.IsConstantValue() && rr.IsConstantValue())
                {
                    try
                    {
                        var decided = Evaluator.Compare(r.Op, l, rr, env.Ctx);
                        if (decided is bool b) return BoolExpr.Of(b);
                    }
                    catch (CalcException) { }
                }
                return new RelationExpr(r.Op, l, rr);
            }
            case PiecewiseExpr pw:
            {
                var env = _current;
                var rebuilt = (PiecewiseExpr)pw.WithChildren([.. pw.Children.Select(Rebuild)]);
                if (env is null) return rebuilt;
                return Evaluator.Eval(rebuilt, env.Ctx.WithFree(rebuilt.FreeSymbols()));
            }
            case LambdaExpr:
                return e;
            default:
                return e.WithChildren([.. e.Children.Select(Rebuild)]);
        }
    }

    /// <summary>Replaces symbols and rebuilds the result.</summary>
    public static Expr Subst(Expr e, string name, Expr value) => Rebuild(Replace(e, new Dictionary<string, Expr> { [name] = value }));

    public static Expr Subst(Expr e, IReadOnlyDictionary<string, Expr> values) => Rebuild(Replace(e, values));

    /// <summary>Substitutes, returning null when the expression is undefined there (division by zero, ln 0...).</summary>
    public static Expr? TrySubst(Expr e, string name, Expr value)
    {
        try { return Subst(e, name, value); }
        catch (CalcException) { return null; }
        catch (DivideByZeroException) { return null; }
        catch (ArithmeticException) { return null; }
    }

    /// <summary>Substitutes several symbols, returning null when the expression is undefined there.</summary>
    public static Expr? TrySubst(Expr e, IReadOnlyDictionary<string, Expr> values)
    {
        try { return Subst(e, values); }
        catch (CalcException) { return null; }
        catch (DivideByZeroException) { return null; }
        catch (ArithmeticException) { return null; }
    }

    /// <summary>Replaces symbols without simplifying.</summary>
    public static Expr Replace(Expr e, IReadOnlyDictionary<string, Expr> values)
    {
        if (e is SymbolExpr s) return values.TryGetValue(s.Name, out var v) ? v : e;
        if (e is LambdaExpr) return e;
        var children = e.Children;
        if (children.Count == 0) return e;
        Expr[]? copy = null;
        for (int i = 0; i < children.Count; i++)
        {
            var nc = Replace(children[i], values);
            if (!ReferenceEquals(nc, children[i]))
            {
                copy ??= [.. children];
                copy[i] = nc;
            }
        }
        return copy is null ? e : e.WithChildren(copy);
    }

    /// <summary>Replaces every occurrence of a subexpression (structural match) without simplifying.</summary>
    public static Expr ReplaceExpr(Expr e, Expr target, Expr with)
    {
        if (e.Equals(target)) return with;
        var children = e.Children;
        if (children.Count == 0) return e;
        Expr[]? copy = null;
        for (int i = 0; i < children.Count; i++)
        {
            var nc = ReplaceExpr(children[i], target, with);
            if (!ReferenceEquals(nc, children[i]))
            {
                copy ??= [.. children];
                copy[i] = nc;
            }
        }
        return copy is null ? e : e.WithChildren(copy);
    }

    // ------------------------------------------------------------------ angle modes

    /// <summary>Multiplier from the user's angle unit to radians, as an exact expression (null in radian mode).</summary>
    public Expr? AngleFactor => Angle switch
    {
        AngleMode.Degree => Algebra.Div(ConstantExpr.Pi, Expr.Int(180)),
        AngleMode.Gradian => Algebra.Div(ConstantExpr.Pi, Expr.Int(200)),
        _ => null,
    };

    /// <summary>Rewrites user-unit trigonometry into radian semantics: sin(x) in degree mode becomes sin(πx/180).</summary>
    public Expr Internalize(Expr e)
    {
        var k = AngleFactor;
        if (k is null || !e.ContainsKind(ExprKind.Call)) return e;
        var inv = Algebra.Pow(k, Expr.MinusOne);
        return Rebuild(MapCalls(e, c =>
        {
            if (!Ctx.Functions.TryGet(c.Name, out var def)) return c;
            Expr r = c;
            if (def.AngleArgument && c.Args.Length >= 1)
                r = new CallExpr(c.Name, c.Args.SetItem(0, Algebra.Mul(k, c.Args[0])));
            if (def.AngleResult) r = Algebra.Mul(inv, r);
            return r;
        }));
    }

    /// <summary>Maps radian semantics back to the user's angle unit (inverse of <see cref="Internalize"/>).</summary>
    public Expr Externalize(Expr e)
    {
        var k = AngleFactor;
        if (k is null || !e.ContainsKind(ExprKind.Call)) return e;
        var inv = Algebra.Pow(k, Expr.MinusOne);
        var mapped = MapCalls(e, c =>
        {
            if (CasModule.IsOwnFunction(c.Name) || !Ctx.Functions.TryGet(c.Name, out var def)) return c;
            Expr r = c;
            if (def.AngleArgument && c.Args.Length >= 1)
                r = new CallExpr(c.Name, c.Args.SetItem(0, Algebra.Mul(inv, c.Args[0])));
            if (def.AngleResult) r = Algebra.Mul(k, r);
            return r;
        });
        // Rebuild with the user's angle unit so exact special values stay correct.
        return RebuildUser(mapped);
    }

    private Expr RebuildUser(Expr e)
    {
        switch (e)
        {
            case NumberExpr or ConstantExpr or SymbolExpr or StringExpr or BoolExpr:
                return e;
            case AddExpr a:
                return Algebra.Add(a.Terms.Select(RebuildUser).ToArray());
            case MulExpr m:
                return Algebra.Mul(m.Factors.Select(RebuildUser).ToArray());
            case PowExpr p:
                return Algebra.Pow(RebuildUser(p.Base), RebuildUser(p.Exponent));
            case CallExpr c:
            {
                var args = c.Args.Select(RebuildUser).ToArray();
                if (!CasModule.IsOwnFunction(c.Name) && User.Functions.TryGet(c.Name, out var def))
                {
                    try { return Evaluator.Apply(def, args, User.WithApproximate(false)); }
                    catch (CalcException) { return new CallExpr(c.Name, [.. args]); }
                }
                return new CallExpr(c.Name, [.. args]);
            }
            default:
                return e.WithChildren([.. e.Children.Select(RebuildUser)]);
        }
    }

    private static Expr MapCalls(Expr e, Func<CallExpr, Expr> f)
    {
        if (e.Children.Count == 0) return e;
        var mapped = e.WithChildren([.. e.Children.Select(c => MapCalls(c, f))]);
        return mapped is CallExpr call ? f(call) : mapped;
    }

    // ------------------------------------------------------------------ inputs and outputs

    /// <summary>Evaluates a held argument exactly with the given names free.</summary>
    public Expr EvalArg(Expr raw, params string[] free)
    {
        var ctx = User.WithApproximate(false);
        if (free.Length > 0) ctx = ctx.WithFree(free);
        return PrepareInput(Evaluator.Eval(raw, ctx));
    }

    /// <summary>Turns decimals into exact fractions (remembering to approximate the result) and maps trig to radians.</summary>
    public Expr PrepareInput(Expr e)
    {
        if (ContainsApprox(e))
        {
            ApproximateResult = true;
            e = Rebuild(e.Map(x => x is NumberExpr { Value.IsExact: false } n && n.Value.IsFinite
                ? new NumberExpr(Number.Exact(BigRational.Approximate(n.Value.Re), BigRational.Approximate(n.Value.Im)))
                : x));
        }
        return Internalize(e);
    }

    public static bool ContainsApprox(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsExact,
        _ => e.Children.Any(ContainsApprox),
    };

    /// <summary>Maps a result back to the user's angle unit and approximates it when the call was numeric.</summary>
    public Expr Finish(Expr result)
    {
        var r = Externalize(result);
        if (ApproximateResult && r is not (BoolExpr or StringExpr)) r = Approximate(r);
        return r;
    }

    /// <summary>Decimal form of an expression; unevaluated CAS calls keep their shape (only their arguments are approximated).</summary>
    private Expr Approximate(Expr e)
    {
        if (!ContainsOwnCall(e))
        {
            try { return Evaluator.Eval(e, User.WithApproximate().WithFree(e.FreeSymbols())); }
            catch (CalcException) { return e; }
        }
        if (e is CallExpr c && CasModule.IsOwnFunction(c.Name))
            return new CallExpr(c.Name, [.. c.Args.Select(Approximate)]);
        return e.WithChildren([.. e.Children.Select(Approximate)]);
    }

    private static bool ContainsOwnCall(Expr e) =>
        (e is CallExpr c && CasModule.IsOwnFunction(c.Name)) || e.Children.Any(ContainsOwnCall);

    // ------------------------------------------------------------------ numbers

    /// <summary>The numeric value of a constant expression (radian semantics), or null.</summary>
    public static Number? Numeric(Expr e)
    {
        if (e is NumberExpr n) return n.Value;
        var env = _current;
        if (env is null || !e.IsConstantValue()) return null;
        try
        {
            var v = Evaluator.Eval(e, env.Ctx.WithApproximate());
            return v is NumberExpr nv ? nv.Value : null;
        }
        catch (CalcException) { return null; }
        catch (ArithmeticException) { return null; }
    }

    /// <summary>A real double value of a constant expression, or NaN.</summary>
    public static double Real(Expr e)
    {
        if (e is ConstantExpr { Constant: ConstantKind.Infinity }) return double.PositiveInfinity;
        if (e is ConstantExpr { Constant: ConstantKind.NegativeInfinity }) return double.NegativeInfinity;
        var n = Numeric(e);
        if (n is null) return double.NaN;
        var v = n.Value;
        if (!v.IsReal)
        {
            if (Math.Abs(v.Im) <= 1e-12 * Math.Max(1, Math.Abs(v.Re))) return v.Re;
            return double.NaN;
        }
        return v.Re;
    }

    /// <summary>Sign of a constant real expression (-1, 0, 1), or null when it cannot be decided.</summary>
    public static int? SignOf(Expr e)
    {
        if (e is NumberExpr n)
        {
            if (!n.Value.IsReal) return null;
            return n.Value.RealSign;
        }
        if (e is ConstantExpr { Constant: ConstantKind.Infinity }) return 1;
        if (e is ConstantExpr { Constant: ConstantKind.NegativeInfinity }) return -1;
        if (!e.IsConstantValue()) return null;
        double v = Real(e);
        if (double.IsNaN(v)) return null;
        if (Math.Abs(v) < 1e-13) return Simplifier.IsZero(e) ? 0 : null;
        return Math.Sign(v);
    }

    /// <summary>A fresh symbol name that does not occur in the given expressions.</summary>
    public static string FreshName(string stem, params Expr[] avoid)
    {
        var used = new HashSet<string>(avoid.SelectMany(a => a.FreeSymbols()));
        if (!used.Contains(stem)) return stem;
        for (int i = 1; ; i++)
            if (!used.Contains(stem + i)) return stem + i;
    }

    private static int _tempCounter;

    /// <summary>A private symbol for internal substitutions (never shown to users).</summary>
    public static SymbolExpr Temp() => new("⁣t" + Interlocked.Increment(ref _tempCounter));
}

/// <summary>Raised when a CAS algorithm exceeds its size or depth limits; the call then stays unevaluated.</summary>
internal sealed class CasLimitException(string message) : CalcException(message);
