using System.Collections.Immutable;
using Calcy.Core.Expressions;

namespace Calcy.Core.Evaluation;

/// <summary>Everything an evaluation needs. Immutable; derive new contexts with the With methods.</summary>
public sealed class EvalContext
{
    public CalcSettings Settings { get; }
    public CalcEnvironment Env { get; }
    public FunctionRegistry Functions { get; }

    /// <summary>Numeric mode: numbers become decimals and functions evaluate numerically.</summary>
    public bool Approximate { get; }

    public CancellationToken Cancellation { get; }

    /// <summary>Locally bound names (function parameters, summation indices). A name bound to itself is free.</summary>
    public ImmutableDictionary<string, Expr> Locals { get; }

    public int Depth { get; }

    public const int MaxDepth = 400;

    public EvalContext(CalcSettings settings, CalcEnvironment env, FunctionRegistry functions, bool approximate = false,
        CancellationToken cancellation = default)
        : this(settings, env, functions, approximate, cancellation, ImmutableDictionary<string, Expr>.Empty, 0)
    {
    }

    private EvalContext(CalcSettings settings, CalcEnvironment env, FunctionRegistry functions, bool approximate,
        CancellationToken cancellation, ImmutableDictionary<string, Expr> locals, int depth)
    {
        Settings = settings;
        Env = env;
        Functions = functions;
        Approximate = approximate;
        Cancellation = cancellation;
        Locals = locals;
        Depth = depth;
    }

    public EvalContext WithApproximate(bool approximate = true) =>
        approximate == Approximate ? this : new(Settings, Env, Functions, approximate, Cancellation, Locals, Depth);

    public EvalContext WithLocal(string name, Expr value) =>
        new(Settings, Env, Functions, Approximate, Cancellation, Locals.SetItem(name, value), Depth);

    public EvalContext WithLocals(IEnumerable<KeyValuePair<string, Expr>> values) =>
        new(Settings, Env, Functions, Approximate, Cancellation, Locals.SetItems(values), Depth);

    /// <summary>Replaces all local bindings (used when entering a user function body).</summary>
    public EvalContext WithOnlyLocals(IEnumerable<KeyValuePair<string, Expr>> values) =>
        new(Settings, Env, Functions, Approximate, Cancellation, ImmutableDictionary.CreateRange(values), Depth);

    /// <summary>Makes a name free (unbound) inside, for example the variable of diff(f, x).</summary>
    public EvalContext WithFree(string name) => WithLocal(name, new SymbolExpr(name));

    public EvalContext WithFree(IEnumerable<string> names) =>
        WithLocals(names.Select(n => new KeyValuePair<string, Expr>(n, new SymbolExpr(n))));

    public EvalContext Deeper()
    {
        if (Depth >= MaxDepth) throw new CalcException("Too much recursion: does a function call itself without stopping?");
        Cancellation.ThrowIfCancellationRequested();
        return new(Settings, Env, Functions, Approximate, Cancellation, Locals, Depth + 1);
    }
}
