using Calcy.Core.Expressions;

namespace Calcy.Core.Evaluation;

/// <summary>The arguments of a builtin call plus the evaluation context.</summary>
public readonly record struct CallInfo(FunctionDefinition Function, IReadOnlyList<Expr> Args, EvalContext Context)
{
    public Expr this[int i] => Args[i];
    public int Count => Args.Count;
    public bool Approximate => Context.Approximate;

    /// <summary>Evaluates an argument (for functions that hold their arguments).</summary>
    public Expr Eval(int i) => Evaluator.Eval(Args[i], Context);

    public CalcException Error(string message) => new($"{Function.Name}: {message}");
}

/// <summary>A builtin function: metadata for the catalog plus its implementation.</summary>
public sealed class FunctionDefinition
{
    public required string Name { get; init; }
    public string[] Aliases { get; init; } = [];
    public int MinArgs { get; init; } = 1;

    /// <summary>Maximum number of arguments, or -1 for any number.</summary>
    public int MaxArgs { get; init; } = 1;

    /// <summary>When true and the first argument is a list, the function is applied to each element.</summary>
    public bool Listable { get; init; } = true;

    /// <summary>When true the implementation receives unevaluated arguments (needed for diff, sum, solve...).</summary>
    public bool HoldArgs { get; init; }

    /// <summary>Returns the result, or null to keep the call unevaluated (symbolic).</summary>
    public required Func<CallInfo, Expr?> Evaluate { get; init; }

    /// <summary>Fast real-valued implementation used when compiling graphs (argument and result in radians for trig).</summary>
    public Func<double, double>? Real1 { get; init; }

    public Func<double, double, double>? Real2 { get; init; }

    /// <summary>The (first) argument is an angle in the current angle unit.</summary>
    public bool AngleArgument { get; init; }

    /// <summary>The result is an angle in the current angle unit.</summary>
    public bool AngleResult { get; init; }

    public string Category { get; init; } = "Other";
    public string Signature { get; init; } = "";
    public string Description { get; init; } = "";
    public string Example { get; init; } = "";

    /// <summary>Shown in the catalog and the symbol map.</summary>
    public bool Hidden { get; init; }
}

/// <summary>
/// All builtin functions, by name and alias. Modules (CAS, statistics, finance) add their own, and programs can add
/// and remove functions while graphs evaluate on other threads: reads are lock-free over immutable snapshots, writes
/// are serialised.
/// </summary>
public sealed class FunctionRegistry
{
    private readonly object _gate = new();
    private System.Collections.Immutable.ImmutableDictionary<string, FunctionDefinition> _byName =
        System.Collections.Immutable.ImmutableDictionary.Create<string, FunctionDefinition>(StringComparer.Ordinal);
    private System.Collections.Immutable.ImmutableList<FunctionDefinition> _all = [];

    public IReadOnlyList<FunctionDefinition> All => _all;

    public void Add(FunctionDefinition f)
    {
        lock (_gate)
        {
            var byName = _byName;
            var all = _all;
            if (byName.TryGetValue(f.Name, out var existing)) all = all.Remove(existing);
            byName = byName.SetItem(f.Name, f);
            foreach (var a in f.Aliases) byName = byName.SetItem(a, f);
            _byName = byName;
            _all = all.Add(f);
        }
    }

    /// <summary>Removes a function with all its aliases (for functions a program registered and no longer provides).</summary>
    public bool Remove(string name)
    {
        lock (_gate)
        {
            if (!_byName.TryGetValue(name, out var f)) return false;
            var byName = _byName.Remove(f.Name);
            foreach (var a in f.Aliases)
                if (byName.TryGetValue(a, out var owner) && ReferenceEquals(owner, f)) byName = byName.Remove(a);
            _byName = byName;
            _all = _all.Remove(f);
            return true;
        }
    }

    /// <summary>Removes an alias (never a function's own name), so the word is free again for variables.</summary>
    public bool RemoveAlias(string alias)
    {
        lock (_gate)
        {
            if (!_byName.TryGetValue(alias, out var f) || f.Name == alias) return false;
            _byName = _byName.Remove(alias);
            return true;
        }
    }

    public bool TryGet(string name, out FunctionDefinition f) => _byName.TryGetValue(name, out f!);
    public bool Contains(string name) => _byName.ContainsKey(name);

    /// <summary>A registry preloaded with the core builtins.</summary>
    public static FunctionRegistry CreateDefault()
    {
        var r = new FunctionRegistry();
        Builtins.CoreBuiltins.Register(r);
        return r;
    }
}