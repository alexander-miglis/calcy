using System.Collections.Immutable;
using Calcy.Core.Expressions;

namespace Calcy.Core.Evaluation;

/// <summary>A user-defined function f(x, y) = body.</summary>
public sealed record UserFunction(string Name, ImmutableArray<string> Parameters, Expr Body);

/// <summary>Variables and user functions of a calculator session. Thread-safe for concurrent reads.</summary>
public sealed class CalcEnvironment
{
    private readonly object _gate = new();
    private ImmutableDictionary<string, Expr> _variables = ImmutableDictionary<string, Expr>.Empty;
    private ImmutableDictionary<string, UserFunction> _functions = ImmutableDictionary<string, UserFunction>.Empty;

    /// <summary>Raised after any variable or function changes (UI panels listen to this).</summary>
    public event EventHandler? Changed;

    public IReadOnlyDictionary<string, Expr> Variables => _variables;
    public IReadOnlyDictionary<string, UserFunction> Functions => _functions;

    public bool TryGetVariable(string name, out Expr value) => _variables.TryGetValue(name, out value!);
    public bool TryGetFunction(string name, out UserFunction function) => _functions.TryGetValue(name, out function!);

    public void SetVariable(string name, Expr value)
    {
        lock (_gate)
        {
            _variables = _variables.SetItem(name, value);
            _functions = _functions.Remove(name);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFunction(UserFunction f)
    {
        lock (_gate)
        {
            _functions = _functions.SetItem(f.Name, f);
            _variables = _variables.Remove(f.Name);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string name)
    {
        bool removed;
        lock (_gate)
        {
            removed = _variables.ContainsKey(name) || _functions.ContainsKey(name);
            _variables = _variables.Remove(name);
            _functions = _functions.Remove(name);
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _variables = ImmutableDictionary<string, Expr>.Empty;
            _functions = ImmutableDictionary<string, UserFunction>.Empty;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A snapshot copy (used to evaluate graphs without seeing later edits mid-render).</summary>
    public CalcEnvironment Snapshot()
    {
        var c = new CalcEnvironment();
        c._variables = _variables;
        c._functions = _functions;
        return c;
    }
}
