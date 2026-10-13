using System.Numerics;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;
using Calcy.Python;

namespace Calcy.Scripting.Calc;

/// <summary>
/// Connects Python to a <see cref="CalcSession"/>: evaluates calculator text, converts results to Python values and back,
/// and registers Python functions as calculator functions.
/// </summary>
/// <remarks>
/// Conversion rule (documented in the calc module): exact integers become int, other exact rationals Fraction,
/// decimals float, complex numbers complex, true and false bool, text str, lists list, points tuple, matrices lists of
/// rows, and anything symbolic a calc.Expr. With numeric evaluation every number becomes a float (or complex).
/// </remarks>
internal sealed class CalcBridge
{
    /// <summary>Category of calculator functions defined in Python (used to tell them from built-in functions).</summary>
    public const string PythonCategory = "Python";

    private readonly ScriptHost _host;
    private readonly object _gate = new();
    private readonly Dictionary<string, Registered> _functions = new(StringComparer.Ordinal);
    private PythonCallable? _fractionType;
    private PythonCallable? _exprType;
    private volatile bool _closed;

    public CalcBridge(ScriptHost host)
    {
        _host = host;
    }

    private CalcSession Session => _host.Session;

    /// <summary>Names of the calculator functions this host registered.</summary>
    public IReadOnlyList<string> FunctionNames
    {
        get { lock (_gate) return [.. _functions.Keys.Order(StringComparer.Ordinal)]; }
    }

    /// <summary>The Python classes used for exact fractions and symbolic results (set when the calc module is imported).</summary>
    public void Bind(PythonCallable fraction, PythonCallable expr)
    {
        _fractionType = fraction;
        _exprType = expr;
    }

    /// <summary>A Python function in the calculator: the exact bridge, the plain function for plotting and its definition.</summary>
    private sealed record Registered(PythonCallable Bridge, PythonCallable? Plain, FunctionDefinition Definition);

    /// <summary>Removes this host's functions from the calculator; later calls report that they are gone.</summary>
    public void Close()
    {
        _closed = true;
        List<Registered> all;
        lock (_gate)
        {
            all = [.. _functions.Values];
            _functions.Clear();
        }
        foreach (var r in all)
        {
            // Only remove the definition if no other host replaced it since.
            if (Session.Functions.TryGet(r.Definition.Name, out var current) && ReferenceEquals(current, r.Definition))
                Session.Functions.Remove(r.Definition.Name);
        }
    }

    // ------------------------------------------------------------------ evaluation

    private static PythonRaiseException Error(string message) => new("ValueError", message);

    private CalcResult EvaluateLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw Error("calc: nothing to evaluate");
        var r = Session.Evaluate(text);
        if (r.IsError)
        {
            var msg = r.ErrorMessage ?? "error";
            if (!string.IsNullOrEmpty(r.Hint)) msg += " (" + r.Hint + ")";
            throw Error($"calc: {msg} in {Quote(text)}");
        }
        return r;
    }

    private static string Quote(string text) => text.Length > 80 ? "'" + text[..77] + "...'" : "'" + text + "'";

    /// <summary>calc.eval: evaluates a line (an expression or a definition) and converts the result.</summary>
    public object? Eval(string text, bool numeric)
    {
        var r = EvaluateLine(text);
        if (r.Kind == ResultKind.FunctionDefinition) return null;
        var value = r.Exact ?? throw Error("calc: no result");
        if (numeric) value = r.Approx ?? value;
        return ToPython(value, numeric);
    }

    /// <summary>calc.exact: the exact result as display text (or as plain input text).</summary>
    public string Exact(string text, bool plain)
    {
        var r = EvaluateLine(text);
        var value = r.Exact ?? throw Error("calc: no result");
        return plain ? ExprPrinter.ToInput(value) : Session.Print(value);
    }

    /// <summary>calc.latex: the LaTeX of the exact result.</summary>
    public string Latex(string text)
    {
        var r = EvaluateLine(text);
        var value = r.Exact ?? throw Error("calc: no result");
        return LatexPrinter.ToLatex(value, NumberFormatOptions.From(Session.Settings, false));
    }

    /// <summary>The numeric value of calculator text as a float or complex (for float() of a calc.Expr).</summary>
    public object Number(string text)
    {
        var r = EvaluateLine(text);
        var value = r.Approx ?? r.Exact ?? throw Error("calc: no result");
        if (value is not NumberExpr)
        {
            try
            {
                value = Session.Evaluate(value, approximate: true);
            }
            catch (CalcException e)
            {
                throw new PythonRaiseException("TypeError", $"calc: {e.Message}");
            }
        }
        if (value is NumberExpr n)
        {
            if (n.Value.IsReal) return n.Value.Re;
            return new Complex(n.Value.Re, n.Value.Im);
        }
        throw new PythonRaiseException("TypeError", $"{Session.Print(value)} is not a number");
    }

    /// <summary>calc.get: a session variable, a ("function", parameters) pair for user functions, or null.</summary>
    public object? Get(string name)
    {
        if (Session.Env.TryGetVariable(name, out var value)) return new List<object?> { "value", ToPython(value, false) };
        if (Session.Env.TryGetFunction(name, out var f)) return new List<object?> { "function", (long)f.Parameters.Length };
        lock (_gate)
        {
            if (_functions.ContainsKey(name)) return new List<object?> { "function", -1L };
        }
        return null;
    }

    /// <summary>calc.set: stores a Python value as a session variable.</summary>
    public void Set(string name, object? value)
    {
        CheckName(name, "variable");
        if (Session.Functions.Contains(name)) throw Error($"calc: '{name}' is a calculator function; pick another variable name");
        var expr = FromPython(value);
        if (expr.ContainsSymbol(name)) throw Error($"calc: {name} cannot be defined in terms of itself");
        Session.Env.SetVariable(name, expr);
    }

    /// <summary>calc.delete: removes a variable or user function; false when there was none.</summary>
    public bool Delete(string name) => Session.Env.Remove(name);

    /// <summary>calc.variables: every session variable (not ans) as a dict.</summary>
    public Dictionary<object, object?> Variables()
    {
        var d = new Dictionary<object, object?>();
        foreach (var (k, v) in Session.Env.Variables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (k == "ans" || v is LambdaExpr) continue;
            d[k] = ToPython(v, false);
        }
        return d;
    }

    /// <summary>
    /// calc.solve: a list of solutions for one variable, or a list of dicts for systems. Results that are not plain
    /// solutions (inequalities, general solutions) come back as calc.Expr.
    /// </summary>
    public List<object?> Solve(string equation, string variables)
    {
        var r = EvaluateLine($"solve({equation}, {variables})");
        var result = r.Exact ?? throw Error("calc: no result");
        var list = new List<object?>();
        if (result is BoolExpr { Value: false }) return list;
        IEnumerable<Expr> cases = result is LogicExpr { Op: LogicOp.Or } or ? or.Items : [result];
        bool single = !variables.TrimStart().StartsWith('{');
        foreach (var c in cases)
        {
            if (single && c is RelationExpr { Op: RelOp.Equal, Left: SymbolExpr } rel && rel.Right.FreeSymbols().Count == 0)
            {
                list.Add(ToPython(rel.Right, false));
                continue;
            }
            var assignment = Assignment(c);
            if (assignment is not null)
            {
                if (single && assignment.Count == 1) list.Add(assignment.Values.First());
                else list.Add(assignment);
                continue;
            }
            list.Add(MakeExpr(c));
        }
        return list;
    }

    private Dictionary<object, object?>? Assignment(Expr e)
    {
        IEnumerable<Expr> parts = e is LogicExpr { Op: LogicOp.And } and ? and.Items : [e];
        var d = new Dictionary<object, object?>();
        foreach (var p in parts)
        {
            if (p is not RelationExpr { Op: RelOp.Equal, Left: SymbolExpr s } rel) return null;
            d[s.Name] = ToPython(rel.Right, false);
        }
        return d.Count > 0 ? d : null;
    }

    /// <summary>Expr.subs: evaluates calculator text with some names bound to values.</summary>
    public object? Subs(string text, Dictionary<object, object?>? values)
    {
        var parsed = Parse(text);
        var locals = new List<KeyValuePair<string, Expr>>();
        if (values is not null)
        {
            foreach (var (k, v) in values)
            {
                if (k is not string name) throw new PythonRaiseException("TypeError", "subs() names must be strings");
                locals.Add(new(name, FromPython(v)));
            }
        }
        try
        {
            var result = Evaluator.Eval(parsed, Session.Context().WithLocals(locals));
            return ToPython(result, false);
        }
        catch (CalcException e)
        {
            throw Error($"calc: {e.Message}");
        }
    }

    private Expr Parse(string text)
    {
        try
        {
            return Session.ParseExpression(text);
        }
        catch (CalcException e)
        {
            throw Error($"calc: {e.Message} in {Quote(text)}");
        }
    }

    private static void CheckName(string name, string what)
    {
        bool ok = name.Length > 0 && char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_');
        if (!ok) throw Error($"calc: '{name}' is not a valid {what} name (use letters, digits and _)");
    }

    // ------------------------------------------------------------------ conversions

    /// <summary>Converts a calculator value to a Python value (Fraction and calc.Expr objects need the calc module).</summary>
    public object? ToPython(Expr e, bool numeric)
    {
        switch (e)
        {
            case NumberExpr n:
                return NumberToPython(n.Value, numeric);
            case BoolExpr b:
                return b.Value;
            case StringExpr s:
                return s.Value;
            case ListExpr l:
                return l.Items.Select(x => ToPython(x, numeric)).ToList();
            case MatrixExpr m:
                return m.RowLists().Select(row => (object?)row.Select(x => ToPython(x, numeric)).ToList()).ToList();
            case TupleExpr t:
                return MakeTuple(t.Items.Select(x => ToPython(x, numeric)).ToList());
            case ConstantExpr { Constant: ConstantKind.Pi or ConstantKind.E } c when numeric:
                return c.ApproxValue;
        }
        if (numeric)
        {
            try
            {
                var approx = Session.Evaluate(e, approximate: true);
                if (approx is NumberExpr an) return NumberToPython(an.Value, true);
            }
            catch (CalcException)
            {
                // Keep it symbolic.
            }
        }
        return MakeExpr(e);
    }

    private object NumberToPython(Number v, bool numeric)
    {
        if (!v.IsReal) return new Complex(v.Re, v.Im);
        if (numeric || !v.IsExact) return v.Re;
        if (v.IsExactInteger)
        {
            var big = v.ReQ.Num;
            return big >= long.MinValue && big <= long.MaxValue ? (long)big : big;
        }
        var q = v.ReQ;
        if (_fractionType is { } frac && CanCallPython) return frac.InvokeRaw(q.Num, q.Den);
        return v.Re;
    }

    private object MakeExpr(Expr e)
    {
        var text = ExprPrinter.ToInput(e);
        var pretty = Session.Print(e);
        if (_exprType is { } type && CanCallPython) return type.InvokeRaw(text, pretty);
        return pretty;
    }

    private bool CanCallPython => _host.IsOnInterpreterThread;

    private static object MakeTuple(List<object?> items) => items.Count switch
    {
        1 => new Tuple<object?>(items[0]),
        2 => (items[0], items[1]),
        3 => (items[0], items[1], items[2]),
        4 => (items[0], items[1], items[2], items[3]),
        _ => items,
    };

    /// <summary>
    /// Converts a Python value (already turned into CLR values) to a calculator value. Strings are read as calculator
    /// input, so the calc module passes Fraction and calc.Expr objects as their text.
    /// </summary>
    public Expr FromPython(object? value)
    {
        switch (value)
        {
            case null:
                throw new PythonRaiseException("TypeError", "None has no calculator value");
            case bool b:
                return BoolExpr.Of(b);
            case long l:
                return new NumberExpr(Core.Numbers.Number.FromInt(l));
            case int i:
                return new NumberExpr(Core.Numbers.Number.FromInt(i));
            case BigInteger big:
                return new NumberExpr(Core.Numbers.Number.Exact(new BigRational(big)));
            case double d:
                if (!double.IsFinite(d)) throw Error($"calc: {d} cannot be stored in the calculator");
                return new NumberExpr(Core.Numbers.Number.Approx(d));
            case Complex c:
                return new NumberExpr(Core.Numbers.Number.Approx(c.Real, c.Imaginary));
            case string s:
                try
                {
                    return Session.Evaluate(Parse(s));
                }
                catch (CalcException e)
                {
                    throw Error($"calc: {e.Message} in {Quote(s)}");
                }
            case IReadOnlyList<object?> list:
            {
                if (list.Count > 0 && list.All(x => x is IReadOnlyList<object?> { Count: > 0 }))
                {
                    var rows = list.Cast<IReadOnlyList<object?>>().ToList();
                    if (rows.All(r => r.Count == rows[0].Count) && rows.All(r => r.All(x => x is not IReadOnlyList<object?>)))
                        return MatrixExpr.FromRows(rows.Select(r => (IReadOnlyList<Expr>)r.Select(FromPython).ToList()).ToList());
                }
                return new ListExpr([.. list.Select(FromPython)]);
            }
            case PyObject p:
                throw new PythonRaiseException("TypeError", $"a {p.TypeName} has no calculator value");
        }
        throw new PythonRaiseException("TypeError", $"a {value.GetType().Name} has no calculator value");
    }

    // ------------------------------------------------------------------ Python functions in the calculator

    /// <summary>@calc.function: makes a Python callable usable in calculator expressions while the host lives.</summary>
    public void Register(string name, PythonCallable function, string? doc, PythonCallable? plain = null)
    {
        CheckName(name, "function");
        if (Session.Functions.TryGet(name, out var existing) && existing.Category != PythonCategory)
            throw Error($"calc: '{name}' is a built-in calculator function; pick another name");
        var def = new FunctionDefinition
        {
            Name = name,
            MinArgs = 0,
            MaxArgs = -1,
            Listable = false,
            Category = PythonCategory,
            Signature = $"{name}(...)",
            Description = string.IsNullOrWhiteSpace(doc) ? "A function defined in Python" : doc.Trim(),
            Evaluate = call => Invoke(name, call),
            // Graphs call the plain Python function with floats directly, without converting through the calculator.
            Real1 = plain is null ? null : x => _host.CallForPlot(plain, x),
            Real2 = plain is null ? null : (x, y) => _host.CallForPlot(plain, x, y),
        };
        var options = _host.CallOptions;
        lock (_gate) _functions[name] = new Registered(function.WithOptions(options), plain?.WithOptions(options), def);
        // A calculator definition of the same name would hide the Python function.
        if (Session.Env.TryGetFunction(name, out _) || Session.Env.TryGetVariable(name, out _)) Session.Env.Remove(name);
        Session.Functions.Add(def);
    }

    private Expr? Invoke(string name, CallInfo call)
    {
        if (_closed) throw new CalcException($"{name} was defined by a Python program that is no longer loaded");
        Registered? reg;
        lock (_gate) _functions.TryGetValue(name, out reg);
        if (reg is null) throw new CalcException($"{name} is no longer defined");
        var fn = reg.Bridge;
        // Symbolic arguments (x while preparing a graph) keep the call unevaluated; it runs again with numbers.
        foreach (var a in call.Args)
            if (a.FreeSymbols().Count > 0 || a is LambdaExpr) return null;
        bool numeric = call.Approximate;
        return _host.RunCallback(name, () =>
        {
            var args = call.Args.Select(a => ToPython(a, numeric)).ToArray();
            var result = fn.Invoke(args);
            var expr = result is PyObject p
                ? throw new PythonRaiseException("TypeError", $"{name}() returned a {p.TypeName}, which has no calculator value")
                : FromPython(result);
            return numeric ? Session.Evaluate(expr, approximate: true) : expr;
        });
    }
}
