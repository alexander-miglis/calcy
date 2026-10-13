using System.Collections.Immutable;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Modes.Notebook.Runtime;

/// <summary>A change one cell made to the variables: a value, a function, or a removal (both null).</summary>
/// <param name="Name">The name.</param>
/// <param name="Value">The new value of a variable.</param>
/// <param name="Function">The new definition of a function.</param>
internal sealed record EnvEffect(string Name, Expr? Value, UserFunction? Function);

/// <summary>
/// A snapshot of a calculator environment (variables and functions). The environment keeps immutable dictionaries,
/// so a snapshot is two references and costs nothing to take.
/// </summary>
internal sealed class EnvState
{
    public static readonly EnvState Empty = new(ImmutableDictionary<string, Expr>.Empty, ImmutableDictionary<string, UserFunction>.Empty);

    private EnvState(ImmutableDictionary<string, Expr> vars, ImmutableDictionary<string, UserFunction> funcs)
    {
        Variables = vars;
        Functions = funcs;
    }

    public ImmutableDictionary<string, Expr> Variables { get; }

    public ImmutableDictionary<string, UserFunction> Functions { get; }

    public bool IsEmpty => Variables.IsEmpty && Functions.IsEmpty;

    public static EnvState Capture(CalcEnvironment env) => new(
        env.Variables as ImmutableDictionary<string, Expr> ?? env.Variables.ToImmutableDictionary(StringComparer.Ordinal),
        env.Functions as ImmutableDictionary<string, UserFunction> ?? env.Functions.ToImmutableDictionary(StringComparer.Ordinal));

    /// <summary>The same snapshot without some names.</summary>
    public EnvState Without(IEnumerable<string> names)
    {
        var v = Variables;
        var f = Functions;
        foreach (var n in names)
        {
            v = v.Remove(n);
            f = f.Remove(n);
        }
        return new EnvState(v, f);
    }

    /// <summary>The value (Expr or UserFunction) of a name, or null.</summary>
    public object? Lookup(string name) =>
        Variables.TryGetValue(name, out var v) ? v : Functions.TryGetValue(name, out var f) ? f : null;

    /// <summary>Makes <paramref name="env"/> hold exactly this snapshot, changing only what differs.</summary>
    public void RestoreInto(CalcEnvironment env)
    {
        if (IsEmpty)
        {
            if (env.Variables.Count > 0 || env.Functions.Count > 0) env.Clear();
            return;
        }
        var cur = Capture(env);
        foreach (var name in cur.Variables.Keys.Concat(cur.Functions.Keys).ToList())
            if (!Variables.ContainsKey(name) && !Functions.ContainsKey(name)) env.Remove(name);
        foreach (var (k, v) in Variables)
            if (!cur.Variables.TryGetValue(k, out var cv) || !ReferenceEquals(cv, v)) env.SetVariable(k, v);
        foreach (var (k, f) in Functions)
            if (!cur.Functions.TryGetValue(k, out var cf) || !ReferenceEquals(cf, f)) env.SetFunction(f);
    }

    /// <summary>What changed from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static List<EnvEffect> Diff(EnvState before, EnvState after)
    {
        var effects = new List<EnvEffect>();
        foreach (var (k, v) in after.Variables)
            if (!before.Variables.TryGetValue(k, out var old) || !ReferenceEquals(old, v)) effects.Add(new EnvEffect(k, v, null));
        foreach (var (k, f) in after.Functions)
            if (!before.Functions.TryGetValue(k, out var old) || !ReferenceEquals(old, f)) effects.Add(new EnvEffect(k, null, f));
        foreach (var k in before.Variables.Keys.Concat(before.Functions.Keys))
            if (!after.Variables.ContainsKey(k) && !after.Functions.ContainsKey(k)) effects.Add(new EnvEffect(k, null, null));
        return effects;
    }

    /// <summary>Applies recorded effects.</summary>
    public static void Apply(CalcEnvironment env, IEnumerable<EnvEffect> effects)
    {
        foreach (var e in effects)
        {
            if (e.Value is not null) env.SetVariable(e.Name, e.Value);
            else if (e.Function is not null) env.SetFunction(e.Function);
            else env.Remove(e.Name);
        }
    }

    /// <summary>True when two values of a name are the same (same object, or equal structure).</summary>
    public static bool SameValue(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        return (a, b) switch
        {
            (Expr x, Expr y) => x.Equals(y),
            (UserFunction f, UserFunction g) => f.Name == g.Name && f.Parameters.SequenceEqual(g.Parameters) && f.Body.Equals(g.Body),
            _ => false,
        };
    }
}
