using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using Calcy.Python.Runtime;

namespace Calcy.Python;

/// <summary>
/// Thrown by host functions to raise a Python exception of a builtin type, for example
/// <c>throw new PythonRaiseException("ValueError", "x must be positive")</c>.
/// </summary>
public sealed class PythonRaiseException : Exception
{
    /// <summary>Name of a builtin Python exception type; unknown names map to RuntimeError.</summary>
    public string PythonType { get; }

    /// <summary>Creates the exception.</summary>
    public PythonRaiseException(string pythonType, string message) : base(message)
    {
        PythonType = pythonType;
    }
}

/// <summary>A Python exception propagating through host code (for example out of <see cref="PythonCallable.Invoke"/>).</summary>
public abstract class PythonScriptException : Exception
{
    private protected PythonScriptException() : base(null) { }

    /// <summary>The Python exception type name.</summary>
    public abstract string PythonType { get; }
}

/// <summary>Conversions between Python values and CLR values.</summary>
public static class PyConvert
{
    /// <summary>
    /// Converts a Python value to a natural CLR value: None to null, bool, int to long (or BigInteger when large),
    /// float to double, complex to <see cref="Complex"/>, str to string, list/tuple/set to <c>List&lt;object?&gt;</c>,
    /// dict to <c>Dictionary&lt;object, object?&gt;</c> (None keys become <see cref="DBNull"/>), opaque host objects to the
    /// wrapped object. Any other value is returned as the <see cref="PyObject"/> itself.
    /// </summary>
    public static object? ToClr(PyObject? value)
    {
        Interp.CheckStack();
        switch (value)
        {
            case null:
            case PyNone:
                return null;
            case PyBool b:
                return b.Value;
            case PyInt i:
                return i.IsBig ? i.Big : (object)i.V;
            case PyFloat f:
                return f.V;
            case PyComplex c:
                return new Complex(c.Real, c.Imag);
            case PyStr s:
                return s.V;
            case PyList l:
                return l.ToArray().Select(ToClr).ToList();
            case PyTuple t:
                return t.Items.Select(ToClr).ToList();
            case PySetBase st:
                return st.Table.Keys().Select(ToClr).ToList();
            case PyDict d:
            {
                var r = new Dictionary<object, object?>();
                foreach (var (k, v) in d.Pairs()) r[ToClr(k) ?? DBNull.Value] = ToClr(v);
                return r;
            }
            case PyHostObject h:
                return h.Value;
        }
        return value;
    }

    /// <summary>
    /// Converts a CLR value to Python: null, bool, integral types, BigInteger, float/double/decimal, Complex, string,
    /// char, dictionaries, tuples, enumerables (to list), <see cref="PyObject"/> (unchanged),
    /// <c>Func&lt;PythonArgs, object?&gt;</c>, <c>Func&lt;double, double&gt;</c> and <c>Func&lt;double, double, double&gt;</c>
    /// (to callables). Anything else becomes an opaque host object that Python code can pass around but not inspect.
    /// </summary>
    public static PyObject FromClr(object? value)
    {
        switch (value)
        {
            case null: return Py.None;
            case PyObject p: return p;
            case bool b: return Py.Bool(b);
            case int i: return PyInt.From(i);
            case long l: return PyInt.From(l);
            case short s: return PyInt.From(s);
            case byte by: return PyInt.From(by);
            case sbyte sb: return PyInt.From(sb);
            case ushort us: return PyInt.From(us);
            case uint ui: return PyInt.From(ui);
            case ulong ul: return PyInt.From(new BigInteger(ul));
            case BigInteger bi: return PyInt.From(bi);
            case double d: return new PyFloat(d);
            case float f: return new PyFloat(f);
            case decimal m: return new PyFloat((double)m);
            case Complex c: return new PyComplex(c.Real, c.Imaginary);
            case string str: return PyStr.New(str);
            case char ch: return PyStr.FromChar(ch);
            case PythonCallable pc: return pc.Raw;
            case Func<PythonArgs, object?> fa: return HostBridge.MakeFunction("host_function", fa);
            case Func<double, double> f1: return HostBridge.MakeFunction("host_function", a => f1(a.GetDouble(0)), 1);
            case Func<double, double, double> f2: return HostBridge.MakeFunction("host_function", a => f2(a.GetDouble(0), a.GetDouble(1)), 2);
            case IDictionary dict:
            {
                var r = new PyDict();
                foreach (DictionaryEntry e in dict) r.SetItem(FromClr(e.Key), FromClr(e.Value));
                return r;
            }
            case ITuple tup:
            {
                var items = new PyObject[tup.Length];
                for (int i = 0; i < tup.Length; i++) items[i] = FromClr(tup[i]);
                return PyTuple.Create(items);
            }
            case IEnumerable en:
            {
                var l = new PyList();
                foreach (var x in en) l.Append(FromClr(x));
                return l;
            }
        }
        return new PyHostObject(value);
    }

    /// <summary>Wraps any CLR object as an opaque Python value.</summary>
    public static PyObject Wrap(object hostObject) => new PyHostObject(hostObject ?? throw new ArgumentNullException(nameof(hostObject)));

    /// <summary>Gets the CLR object wrapped by an opaque host value.</summary>
    public static bool TryGetHostObject<T>(PyObject value, out T? result) where T : class
    {
        if (value is PyHostObject h && h.Value is T t)
        {
            result = t;
            return true;
        }
        result = null;
        return false;
    }

    /// <summary>Returns repr() of a value without running any Python code.</summary>
    public static string SafeRepr(PyObject value) => Ops.SafeRepr(value);
}

/// <summary>Arguments passed from Python to a host function, with typed accessors.</summary>
public sealed class PythonArgs
{
    private readonly PyObject[] _args;
    private readonly KwArgs? _kw;
    private readonly Interp? _ctx;

    internal PythonArgs(string functionName, PyObject[] args, KwArgs? kw)
    {
        FunctionName = functionName;
        _args = args;
        _kw = kw;
        _ctx = Interp.CurrentOrNull;
    }

    /// <summary>The cancellation token of the current run; host functions that wait should observe it.</summary>
    public CancellationToken CancellationToken => _ctx?.Ct ?? default;

    /// <summary>
    /// Stops the run's time limit while the returned object is not disposed, for host functions that wait for the
    /// user (keys, frames, animations). Steps, cancellation and the other limits still apply.
    /// <code>using (args.PauseTimeLimit()) { WaitForKey(args.CancellationToken); }</code>
    /// </summary>
    public TimeLimitPause PauseTimeLimit() =>
        new(_ctx ?? throw new InvalidOperationException("PauseTimeLimit can only be used while the host function is running."));

    /// <summary>
    /// Runs <paramref name="wait"/> with the time limit paused and the run's cancellation token, then stops the run
    /// if it was cancelled meanwhile.
    /// </summary>
    public T WaitOutsideTimeLimit<T>(Func<CancellationToken, T> wait)
    {
        ArgumentNullException.ThrowIfNull(wait);
        var ctx = _ctx ?? throw new InvalidOperationException("WaitOutsideTimeLimit can only be used while the host function is running.");
        return TimeLimitPause.Run(ctx, wait);
    }

    /// <summary>Runs <paramref name="wait"/> with the time limit paused (see <see cref="WaitOutsideTimeLimit{T}"/>).</summary>
    public void WaitOutsideTimeLimit(Action<CancellationToken> wait)
    {
        ArgumentNullException.ThrowIfNull(wait);
        WaitOutsideTimeLimit(ct =>
        {
            wait(ct);
            return true;
        });
    }

    /// <summary>The Python name of the called function.</summary>
    public string FunctionName { get; }

    /// <summary>Number of positional arguments.</summary>
    public int Count => _args.Length;

    /// <summary>Names of the keyword arguments.</summary>
    public IReadOnlyList<string> KeywordNames => _kw?.Names ?? Array.Empty<string>();

    /// <summary>The raw positional argument.</summary>
    public PyObject this[int index] => _args[Check(index)];

    private int Check(int i)
    {
        if (i < 0 || i >= _args.Length)
            throw new PythonRaiseException("TypeError", $"{FunctionName}() missing required argument {i + 1}");
        return i;
    }

    /// <summary>Raises TypeError unless the number of positional arguments is within range.</summary>
    public void ExpectCount(int min, int max = int.MaxValue)
    {
        if (_args.Length < min || _args.Length > max)
        {
            string expected = min == max ? $"exactly {min}" : max == int.MaxValue ? $"at least {min}" : $"{min} to {max}";
            throw new PythonRaiseException("TypeError", $"{FunctionName}() takes {expected} argument{(max == 1 ? "" : "s")} ({_args.Length} given)");
        }
    }

    /// <summary>Converts an argument with <see cref="PyConvert.ToClr"/>.</summary>
    public object? Get(int index) => PyConvert.ToClr(this[index]);

    /// <summary>Returns the argument, or the default when it was not passed or is None.</summary>
    public object? GetOptional(int index, object? defaultValue) =>
        index < _args.Length && _args[index] != Py.None ? PyConvert.ToClr(_args[index]) : defaultValue;

    /// <summary>Gets an argument as a double (accepts int, float, bool and objects with __float__).</summary>
    public double GetDouble(int index) => Ops.ToDouble(this[index]);

    /// <summary>Gets an optional double argument.</summary>
    public double GetDouble(int index, double defaultValue) =>
        index < _args.Length && _args[index] != Py.None ? Ops.ToDouble(_args[index]) : defaultValue;

    /// <summary>Gets an argument as a 64 bit integer (raises OverflowError when too large).</summary>
    public long GetInt64(int index) => Ops.Index(this[index]).ToLong("Python int too large to convert to C long");

    /// <summary>Gets an argument as an arbitrary precision integer.</summary>
    public BigInteger GetBigInteger(int index) => Ops.Index(this[index]).ToBig();

    /// <summary>Gets a str argument.</summary>
    public string GetString(int index) => Arg.Str(this[index], $"{FunctionName}() argument {index + 1}");

    /// <summary>Gets the truth value of an argument.</summary>
    public bool GetBool(int index) => Ops.Truthy(this[index]);

    /// <summary>Gets an iterable of numbers as a list of doubles.</summary>
    public IReadOnlyList<double> GetDoubleList(int index) => Ops.Iterate(this[index]).Select(Ops.ToDouble).ToList();

    /// <summary>Gets an iterable as a list of converted values.</summary>
    public IReadOnlyList<object?> GetList(int index) => Ops.Iterate(this[index]).Select(PyConvert.ToClr).ToList();

    /// <summary>Gets an opaque host object of the given type, raising TypeError otherwise.</summary>
    public T GetHostObject<T>(int index) where T : class
    {
        if (PyConvert.TryGetHostObject<T>(this[index], out var r)) return r!;
        throw new PythonRaiseException("TypeError", $"{FunctionName}() argument {index + 1} must be a {typeof(T).Name} host object, not {this[index].TypeName}");
    }

    /// <summary>Gets a callable argument that the host can call now or keep and call later (see <see cref="PythonCallable"/>).</summary>
    public PythonCallable GetCallable(int index)
    {
        var o = this[index];
        if (!Ops.IsCallable(o)) throw new PythonRaiseException("TypeError", $"{FunctionName}() argument {index + 1} must be callable, not {o.TypeName}");
        var engine = _ctx?.Engine ?? throw new InvalidOperationException("GetCallable can only be used while the host function is running.");
        return new PythonCallable(engine, o);
    }

    /// <summary>Looks up a keyword argument.</summary>
    public bool TryGetKeyword(string name, out object? value)
    {
        var v = _kw?.Get(name);
        value = v == null ? null : PyConvert.ToClr(v);
        return v != null;
    }

    /// <summary>Gets a raw keyword argument or null.</summary>
    public PyObject? GetKeywordObject(string name) => _kw?.Get(name);

    /// <summary>Raises TypeError if keyword arguments other than the allowed ones were passed.</summary>
    public void ExpectKeywords(params string[] allowed)
    {
        if (_kw == null) return;
        foreach (var n in _kw.Names)
            if (Array.IndexOf(allowed, n) < 0)
                throw new PythonRaiseException("TypeError", $"{FunctionName}() got an unexpected keyword argument '{n}'");
    }
}

/// <summary>
/// A Python callable (function, lambda, bound method, class or callable object) that host code can call any number of
/// times. Get one from <see cref="PythonArgs.GetCallable"/>, <see cref="PythonEngine.GetCallable"/> or
/// <see cref="PythonEngine.AsCallable"/>.
/// <para>
/// From a host function or callback of a run, a call runs nested in that run: it shares the run's limits and a Python
/// exception propagates as a <see cref="PythonScriptException"/> (rethrow it to let Python code catch it).
/// </para>
/// <para>
/// Outside a run, a call runs directly on the calling thread, without starting a thread, under the limits in
/// <see cref="Options"/> (the memory limit of <see cref="PythonEngineOptions.MaxMemoryBytes"/> is not checked). Errors,
/// timeouts and cancellation throw <see cref="PythonCallException"/>. Such calls must not overlap with a run of the same
/// engine or with each other; they throw <see cref="InvalidOperationException"/> while the engine is busy on another
/// thread. Output and host functions are invoked on the calling thread.
/// </para>
/// <code>
/// var f = engine.GetCallable("f");
/// for (int i = 0; i &lt; xs.Length; i++) ys[i] = f.TryInvokeDouble(xs[i], out var y) ? y : double.NaN;
/// </code>
/// </summary>
public sealed class PythonCallable
{
    private static readonly Func<PyObject, PyObject> AsRaw = r => r;
    private static readonly Func<PyObject, object?> AsClr = PyConvert.ToClr;
    private static readonly Func<PyObject, double> AsDouble = Ops.ToDouble;

    internal PythonCallable(PythonEngine engine, PyObject fn, PythonCallOptions? options = null)
    {
        Engine = engine;
        Raw = fn;
        Options = options ?? PythonCallOptions.Default;
    }

    /// <summary>The underlying Python object.</summary>
    public PyObject Raw { get; }

    /// <summary>The engine the callable belongs to.</summary>
    public PythonEngine Engine { get; }

    /// <summary>Limits for calls made from outside a run.</summary>
    public PythonCallOptions Options { get; }

    /// <summary>Returns the same callable with other limits for calls made from outside a run.</summary>
    public PythonCallable WithOptions(PythonCallOptions options) => new(Engine, Raw, options ?? throw new ArgumentNullException(nameof(options)));

    /// <summary>Calls with arguments converted by <see cref="PyConvert.FromClr"/> and returns the result converted by <see cref="PyConvert.ToClr"/>.</summary>
    public object? Invoke(params object?[] args) => Engine.InvokeCallable(Raw, Convert(args), Options, AsClr, false, out _);

    /// <summary>Calls with converted arguments and returns the raw Python result.</summary>
    public PyObject InvokeRaw(params object?[] args) => Engine.InvokeCallable(Raw, Convert(args), Options, AsRaw, false, out _);

    /// <summary>Calls a function of one number that returns a real number (int, float, bool or an object with __float__).</summary>
    public double InvokeDouble(double x) => Engine.InvokeCallable(Raw, new PyObject[] { new PyFloat(x) }, Options, AsDouble, false, out _);

    /// <summary>Calls a function of two numbers that returns a real number.</summary>
    public double InvokeDouble(double x, double y) =>
        Engine.InvokeCallable(Raw, new PyObject[] { new PyFloat(x), new PyFloat(y) }, Options, AsDouble, false, out _);

    /// <summary>
    /// Calls a function of one number. Returns false (with NaN) when the call raises an exception or returns something
    /// that is not a real number, which suits plotting. Timeouts, step limits, cancellation and SystemExit still throw.
    /// </summary>
    public bool TryInvokeDouble(double x, out double result)
    {
        result = Engine.InvokeCallable(Raw, new PyObject[] { new PyFloat(x) }, Options, AsDouble, true, out bool ok);
        if (!ok) result = double.NaN;
        return ok;
    }

    /// <summary>Like <see cref="TryInvokeDouble(double, out double)"/> for a function of two numbers.</summary>
    public bool TryInvokeDouble(double x, double y, out double result)
    {
        result = Engine.InvokeCallable(Raw, new PyObject[] { new PyFloat(x), new PyFloat(y) }, Options, AsDouble, true, out bool ok);
        if (!ok) result = double.NaN;
        return ok;
    }

    private static PyObject[] Convert(object?[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var a = new PyObject[args.Length];
        for (int i = 0; i < a.Length; i++) a[i] = PyConvert.FromClr(args[i]);
        return a;
    }
}

/// <summary>A call of a <see cref="PythonCallable"/> made from outside a run failed, timed out or was cancelled.</summary>
public sealed class PythonCallException : Exception
{
    internal PythonCallException(ExecutionStatus status, PythonError error)
        : base(error.TypeName + ": " + error.Message)
    {
        Status = status;
        Error = error;
    }

    /// <summary>How the call ended: Error, Exited, Cancelled, TimedOut or StepLimitExceeded.</summary>
    public ExecutionStatus Status { get; }

    /// <summary>The Python error with its traceback.</summary>
    public PythonError Error { get; }
}

/// <summary>
/// Excludes wall clock time from the run's time limit until disposed. Pauses nest; disposing twice is harmless.
/// Obtain one from <see cref="PythonArgs.PauseTimeLimit"/> or <see cref="PythonEngine.PauseTimeLimit"/>.
/// </summary>
public sealed class TimeLimitPause : IDisposable
{
    private Interp? _ctx;

    internal TimeLimitPause(Interp ctx)
    {
        _ctx = ctx;
        ctx.PauseTimeLimit();
    }

    /// <summary>Resumes the time limit.</summary>
    public void Dispose()
    {
        var c = Interlocked.Exchange(ref _ctx, null);
        c?.ResumeTimeLimit();
    }

    internal static T Run<T>(Interp ctx, Func<CancellationToken, T> wait)
    {
        T result;
        using (new TimeLimitPause(ctx))
        {
            try
            {
                result = wait(ctx.Ct);
            }
            catch (OperationCanceledException) when (ctx.Ct.IsCancellationRequested)
            {
                throw ctx.Terminate(TerminationReason.Cancelled, "execution was cancelled");
            }
        }
        if (ctx.Ct.IsCancellationRequested) throw ctx.Terminate(TerminationReason.Cancelled, "execution was cancelled");
        return result;
    }
}

/// <summary>Builds a host module that Python code can import.</summary>
public sealed class ModuleBuilder
{
    private readonly List<(string Name, Func<PyObject> Make)> _members = new();
    private string? _doc;

    internal ModuleBuilder(string name)
    {
        Name = name;
    }

    /// <summary>The module name.</summary>
    public string Name { get; }

    /// <summary>Adds a function receiving all arguments.</summary>
    public ModuleBuilder AddFunction(string name, Func<PythonArgs, object?> function, string? doc = null)
    {
        Validate(name);
        ArgumentNullException.ThrowIfNull(function);
        _members.Add((name, () => HostBridge.MakeFunction(name, function, -1, doc, Name)));
        return this;
    }

    /// <summary>Adds a function of one number.</summary>
    public ModuleBuilder AddFunction(string name, Func<double, double> function, string? doc = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        Validate(name);
        _members.Add((name, () => HostBridge.MakeFunction(name, a => function(a.GetDouble(0)), 1, doc, Name)));
        return this;
    }

    /// <summary>Adds a function of two numbers.</summary>
    public ModuleBuilder AddFunction(string name, Func<double, double, double> function, string? doc = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        Validate(name);
        _members.Add((name, () => HostBridge.MakeFunction(name, a => function(a.GetDouble(0), a.GetDouble(1)), 2, doc, Name)));
        return this;
    }

    /// <summary>Adds a constant converted with <see cref="PyConvert.FromClr"/>.</summary>
    public ModuleBuilder AddConstant(string name, object? value)
    {
        Validate(name);
        _members.Add((name, () => PyConvert.FromClr(value)));
        return this;
    }

    /// <summary>Sets the module docstring.</summary>
    public ModuleBuilder SetDoc(string doc)
    {
        _doc = doc;
        return this;
    }

    private static void Validate(string name)
    {
        if (string.IsNullOrEmpty(name) || !StrType.IsIdentifier(name))
            throw new ArgumentException($"'{name}' is not a valid Python identifier.", nameof(name));
    }

    internal PyModule Build()
    {
        var m = new PyModule(Name, new Namespace(), true);
        m.Add("__doc__", _doc == null ? Py.None : PyStr.New(_doc));
        foreach (var (n, make) in _members) m.Add(n, make());
        return m;
    }
}

/// <summary>Glue between host functions and the interpreter.</summary>
internal static class HostBridge
{
    public static PyObject MakeFunction(string name, Func<PythonArgs, object?> fn, int arity = -1, string? doc = null, string? module = null)
    {
        var f = new PyBuiltinFunction(name, (a, kw) =>
        {
            if (arity >= 0 && (a.Length != arity || (kw != null && kw.Count > 0)))
                throw PyErr.TypeError($"{name}() takes exactly {arity} argument{(arity == 1 ? "" : "s")} ({a.Length} given)");
            return Invoke(name, fn, a, kw);
        }, module);
        if (doc != null) f.Doc = PyStr.New(doc);
        return f;
    }

    public static PyObject Invoke(string name, Func<PythonArgs, object?> fn, PyObject[] a, KwArgs? kw)
    {
        try
        {
            return PyConvert.FromClr(fn(new PythonArgs(name, a, kw)));
        }
        catch (PythonException)
        {
            throw;
        }
        catch (TerminationException)
        {
            throw;
        }
        catch (PythonRaiseException e)
        {
            throw ToPython(e);
        }
        catch (OperationCanceledException) when (Interp.CurrentOrNull is { } ctx && ctx.Ct.IsCancellationRequested)
        {
            throw ctx.Terminate(TerminationReason.Cancelled, "execution was cancelled");
        }
        catch (ArgumentException e)
        {
            throw PyErr.ValueError(e.Message);
        }
        catch (InvalidCastException e)
        {
            throw PyErr.TypeError(e.Message);
        }
        catch (DivideByZeroException)
        {
            throw PyErr.ZeroDivisionError("division by zero");
        }
        catch (OverflowException e)
        {
            throw PyErr.OverflowError(e.Message);
        }
        catch (KeyNotFoundException e)
        {
            throw PyErr.KeyError(e.Message);
        }
        catch (NotImplementedException e)
        {
            throw PyErr.NotImplementedError(e.Message);
        }
        catch (NotSupportedException e)
        {
            throw PyErr.NotImplementedError(e.Message);
        }
        catch (InsufficientExecutionStackException)
        {
            throw PyErr.RecursionError("maximum recursion depth exceeded");
        }
        catch (OutOfMemoryException)
        {
            throw PyErr.MemoryError("out of memory");
        }
        catch (Exception e)
        {
            throw PyErr.RuntimeError($"{name}() failed: {e.Message}");
        }
        finally
        {
            // Time spent in the host counts unless it was paused, so look at the clock before the next statement
            // rather than up to 1024 steps later (a long call followed by a short ending would otherwise finish).
            Interp.CurrentOrNull?.CheckSoon();
        }
    }

    public static PythonException ToPython(PythonRaiseException e)
    {
        var t = Exc.All.FirstOrDefault(x => x.Name == e.PythonType) ?? Exc.RuntimeError;
        return PyErr.New(t, e.Message);
    }
}
