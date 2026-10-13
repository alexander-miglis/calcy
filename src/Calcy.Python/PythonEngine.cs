using System.Text;
using Calcy.Python.Nodes;
using Calcy.Python.Compilation;
using Calcy.Python.Modules;
using Calcy.Python.Parsing;
using Calcy.Python.Runtime;

namespace Calcy.Python;

/// <summary>A variable in the global namespace, described without running Python code.</summary>
public sealed record PythonVariable(string Name, string TypeName, string Repr);

/// <summary>
/// A sandboxed Python 3 interpreter. Code runs on a dedicated thread with a large stack and has no access to
/// files, network, processes, environment or reflection. Only allowlisted and host registered modules can be imported.
/// An engine runs one piece of code at a time; callbacks (<see cref="Output"/>, <see cref="Input"/>, host functions)
/// are invoked on the interpreter thread. Calls through a <see cref="PythonCallable"/> made while the engine is idle
/// run directly on the calling thread instead (see <see cref="GetCallable"/>).
/// </summary>
public sealed class PythonEngine
{
    private static readonly HashSet<string> BuiltinModuleNames = new(StringComparer.Ordinal)
    {
        "math", "cmath", "random", "time", "string", "itertools", "functools", "collections", "operator", "sys",
    };

    private readonly Interp _interp;
    private readonly Dictionary<string, Action<ModuleBuilder>> _hostModules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sourceModules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PyModule> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _sources = new(StringComparer.Ordinal);
    private Namespace _globals = new();
    private volatile bool _running;
    private volatile int _ownerThread;

    /// <summary>Creates an engine.</summary>
    public PythonEngine(PythonEngineOptions? options = null)
    {
        Options = options ?? PythonEngineOptions.Default;
        _interp = new Interp(this);
        InitGlobals();
    }

    /// <summary>Limits and behaviour switches; changes apply to the next run.</summary>
    public PythonEngineOptions Options { get; set; }

    /// <summary>Receives text written by print() and sys.stdout.</summary>
    public Action<string>? Output { get; set; }

    /// <summary>Receives text written to sys.stderr; falls back to <see cref="Output"/> when null.</summary>
    public Action<string>? ErrorOutput { get; set; }

    /// <summary>Supplies a line for input(prompt); returning null raises EOFError.</summary>
    public Func<string, string?>? Input { get; set; }

    /// <summary>Implements time.sleep; it should return early when the token is cancelled. Defaults to a cancellable wait.</summary>
    public Func<TimeSpan, CancellationToken, bool>? Sleep { get; set; }

    /// <summary>True while code is running (a run, or a <see cref="PythonCallable"/> call made from outside a run).</summary>
    public bool IsExecuting => _running;

    internal Namespace Globals => _globals;

    internal PyObject? LastInteractiveValue { get; set; }

    /// <summary>Names of the modules that can be imported.</summary>
    public IReadOnlyCollection<string> AvailableModules =>
        BuiltinModuleNames.Concat(_hostModules.Keys).Concat(_sourceModules.Keys).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------ running code

    /// <summary>
    /// Runs a program in the engine's global namespace (call <see cref="Reset"/> first for a fresh run).
    /// Blocks the calling thread until the program ends; UI hosts should use <see cref="ExecuteAsync"/>.
    /// </summary>
    public ExecutionResult Execute(string source, string fileName = "<main>", CancellationToken cancellationToken = default) =>
        ExecuteAsync(source, fileName, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Runs one REPL input: expression statements at the top level are echoed with repr() like CPython's interactive
    /// prompt, and globals persist between calls. <see cref="ExecutionResult.Value"/> holds the last echoed value.
    /// </summary>
    public ExecutionResult ExecuteInteractive(string source, CancellationToken cancellationToken = default) =>
        ExecuteInteractiveAsync(source, cancellationToken).GetAwaiter().GetResult();

    /// <summary>Evaluates a single expression in the global namespace and returns its value.</summary>
    public ExecutionResult Evaluate(string expression, CancellationToken cancellationToken = default) =>
        EvaluateAsync(expression, cancellationToken).GetAwaiter().GetResult();

    /// <summary>Calls a global Python function (for example one the user defined) with converted arguments.</summary>
    public ExecutionResult Call(string functionName, object?[] arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(functionName);
        ArgumentNullException.ThrowIfNull(arguments);
        return StartRun(ctx =>
        {
            var fn = _globals.Get(functionName) ?? throw PyErr.NameError($"name '{functionName}' is not defined");
            return Ops.Call(fn, arguments.Select(PyConvert.FromClr).ToArray(), null);
        }, cancellationToken, true).GetAwaiter().GetResult();
    }

    // ------------------------------------------------------------------ calling Python from the host

    /// <summary>
    /// Gets a global callable (for example a function the program defined) or a builtin function for repeated fast
    /// calls from host code, such as plotting it. See <see cref="PythonCallable"/> for threading and limits.
    /// <code>
    /// engine.Execute("def f(x):\n    return x * x");
    /// var f = engine.GetCallable("f");
    /// double y = f.InvokeDouble(3);   // 9, runs on this thread
    /// </code>
    /// </summary>
    /// <exception cref="KeyNotFoundException">The global is not defined.</exception>
    /// <exception cref="ArgumentException">The global is not callable.</exception>
    public PythonCallable GetCallable(string name, PythonCallOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var v = LookupName(name) ?? throw new KeyNotFoundException($"name '{name}' is not defined");
        return AsCallable(v, options);
    }

    /// <summary>Gets a global callable; returns false when it is undefined or not callable.</summary>
    public bool TryGetCallable(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PythonCallable? callable, PythonCallOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var v = LookupName(name);
        callable = v != null && Ops.IsCallable(v) ? new PythonCallable(this, v, options) : null;
        return callable != null;
    }

    /// <summary>Wraps a Python value of this engine (for example from <see cref="GetGlobalObject"/>) as a <see cref="PythonCallable"/>.</summary>
    /// <exception cref="ArgumentException">The value is not callable.</exception>
    public PythonCallable AsCallable(PyObject function, PythonCallOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!Ops.IsCallable(function)) throw new ArgumentException($"'{function.TypeName}' object is not callable", nameof(function));
        return new PythonCallable(this, function, options);
    }

    /// <summary>
    /// Stops the current run's time limit until the returned object is disposed, for host code that waits for the user
    /// (keys, frames, animations). Steps, cancellation and the other limits still apply. Pauses nest. Only valid on the
    /// thread running this engine's code, that is inside a host function or callback; host functions can also use
    /// <see cref="PythonArgs.PauseTimeLimit"/>.
    /// <code>using (engine.PauseTimeLimit()) { frameReady.Wait(engine.CancellationToken); }</code>
    /// </summary>
    /// <exception cref="InvalidOperationException">Called while this engine's code is not running on this thread.</exception>
    public TimeLimitPause PauseTimeLimit() => new(RequireCurrent(nameof(PauseTimeLimit)));

    /// <summary>
    /// Runs <paramref name="wait"/> with the time limit paused, passing the run's cancellation token, and stops the run
    /// (KeyboardInterrupt, status Cancelled) when it was cancelled meanwhile. Only valid inside a host function or callback.
    /// <code>var key = engine.WaitOutsideTimeLimit(ct => keys.Take(ct));</code>
    /// </summary>
    /// <exception cref="InvalidOperationException">Called while this engine's code is not running on this thread.</exception>
    public T WaitOutsideTimeLimit<T>(Func<CancellationToken, T> wait)
    {
        ArgumentNullException.ThrowIfNull(wait);
        return TimeLimitPause.Run(RequireCurrent(nameof(WaitOutsideTimeLimit)), wait);
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

    /// <summary>
    /// The cancellation token of the code running on this thread (a run, or a call from outside a run), for callbacks
    /// that wait; <see cref="CancellationToken.None"/> when this engine's code is not running on this thread.
    /// </summary>
    public CancellationToken CancellationToken => Interp.CurrentOrNull == _interp ? _interp.Ct : CancellationToken.None;

    private PyObject? LookupName(string name) => _globals.Get(name) ?? (Builtins.Table.TryGetValue(name, out var b) ? b : null);

    private Interp RequireCurrent(string member) =>
        Interp.CurrentOrNull == _interp
            ? _interp
            : throw new InvalidOperationException($"{member} can only be used on the thread running this engine's code (inside a host function or callback).");

    /// <summary>
    /// Calls a Python callable for host code: nested in the current run when called on the thread running this engine,
    /// otherwise directly on the calling thread with per call limits. With <paramref name="catchErrors"/>, Python
    /// exceptions derived from Exception make <paramref name="ok"/> false instead of throwing.
    /// </summary>
    internal T InvokeCallable<T>(PyObject fn, PyObject[] args, PythonCallOptions o, Func<PyObject, T> convert, bool catchErrors, out bool ok)
    {
        ok = true;
        if (_running && _ownerThread == Environment.CurrentManagedThreadId)
        {
            if (Interp.CurrentOrNull == _interp) return CallNested(fn, args, convert, catchErrors, out ok);
            using (_interp.EnterThread()) return CallNested(fn, args, convert, catchErrors, out ok);
        }
        lock (_interp)
        {
            if (_running) throw new InvalidOperationException("The engine is executing code on another thread; call Python callables from outside a run only while the engine is idle.");
            _running = true;
            _ownerThread = Environment.CurrentManagedThreadId;
        }
        var ctx = _interp;
        try
        {
            ctx.BeginDirect(Options, o.MaxSteps, o.Timeout, Math.Min(o.MaxRecursionDepth, Options.MaxRecursionDepth), o.CancellationToken);
            using var scope = ctx.EnterThread();
            try
            {
                if (o.CancellationToken.IsCancellationRequested) throw ctx.Terminate(TerminationReason.Cancelled, "execution was cancelled");
                return convert(Ops.Call(fn, args, null));
            }
            catch (PythonException e) when (catchErrors && IsCatchable(e))
            {
                ok = false;
                return default!;
            }
            catch (PythonException e)
            {
                throw CallError(e);
            }
            catch (TerminationException t)
            {
                throw TerminationError(t);
            }
            catch (OperationCanceledException) when (o.CancellationToken.IsCancellationRequested)
            {
                throw TerminationError(ctx.Terminate(TerminationReason.Cancelled, "execution was cancelled"));
            }
            catch (Exception e) when (ExcHelpers.Convert(e) is { } pe)
            {
                if (catchErrors && IsCatchable(pe))
                {
                    ok = false;
                    return default!;
                }
                throw CallError(pe);
            }
            finally
            {
                ctx.End();
            }
        }
        finally
        {
            _ownerThread = 0;
            _running = false;
        }
    }

    private static T CallNested<T>(PyObject fn, PyObject[] args, Func<PyObject, T> convert, bool catchErrors, out bool ok)
    {
        ok = true;
        if (!catchErrors) return convert(Ops.Call(fn, args, null));
        try
        {
            return convert(Ops.Call(fn, args, null));
        }
        catch (PythonException e) when (IsCatchable(e))
        {
            ok = false;
            return default!;
        }
    }

    private static bool IsCatchable(PythonException e) => e.Value.Type.IsSubtypeOf(Exc.Exception);

    private PythonCallException CallError(PythonException e) =>
        new(e.Value.Type.IsSubtypeOf(Exc.SystemExit) ? ExecutionStatus.Exited : ExecutionStatus.Error, MakeError(e.Value));

    private PythonCallException TerminationError(TerminationException t)
    {
        var (status, typeName) = TerminationKind(t.Reason);
        return new PythonCallException(status, MakeTerminationError(typeName, t.Message, t.Frames));
    }

    private static (ExecutionStatus Status, string TypeName) TerminationKind(TerminationReason reason) => reason switch
    {
        TerminationReason.Cancelled => (ExecutionStatus.Cancelled, "KeyboardInterrupt"),
        TerminationReason.Timeout => (ExecutionStatus.TimedOut, "TimeoutError"),
        _ => (ExecutionStatus.StepLimitExceeded, "StepLimitError"),
    };

    /// <summary>Returns false when the source is an incomplete statement that needs more lines (for a REPL).</summary>
    public bool IsInputComplete(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return true;
        try
        {
            Parser.ParseModule(source);
        }
        catch (ParseError e)
        {
            return !e.Incomplete;
        }
        var text = source.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        bool compound = lines.Any(l =>
        {
            var t = StripComment(l).TrimEnd();
            return t.EndsWith(':') || (l.Length > 0 && (l[0] == ' ' || l[0] == '\t') && l.Trim().Length > 0);
        });
        if (!compound) return true;
        return text.EndsWith("\n\n") || (lines.Length >= 2 && lines[^1].Trim().Length == 0 && lines[^2].Trim().Length == 0);
    }

    private static string StripComment(string line)
    {
        int i = line.IndexOf('#');
        return i >= 0 ? line.Substring(0, i) : line;
    }

    /// <summary>Clears all globals, imported modules and module state (for example the random generator).</summary>
    public void Reset()
    {
        if (_running) throw new InvalidOperationException("Cannot reset the engine while code is executing.");
        _globals = new Namespace();
        _modules.Clear();
        _sources.Clear();
        InitGlobals();
    }

    private void InitGlobals()
    {
        _globals.Set("__name__", PyStr.New("__main__"));
        _globals.Set("__doc__", Py.None);
    }

    // ------------------------------------------------------------------ modules

    /// <summary>Registers a native host module; it is built on first import after each <see cref="Reset"/>.</summary>
    public void RegisterModule(string name, Action<ModuleBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        ValidateModuleName(name);
        _hostModules[name] = build;
        _modules.Remove(name);
    }

    /// <summary>Registers a module written in Python; it is compiled and run (sandboxed) on first import.</summary>
    public void RegisterSourceModule(string name, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateModuleName(name);
        _sourceModules[name] = source;
        _modules.Remove(name);
    }

    private static void ValidateModuleName(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.Split('.').All(StrType.IsIdentifier))
            throw new ArgumentException($"'{name}' is not a valid module name.", nameof(name));
    }

    internal PyModule Import(string name)
    {
        if (_modules.TryGetValue(name, out var cached)) return cached;
        if (name.StartsWith('.')) throw PyErr.ImportError("attempted relative import with no known parent package");
        PyModule? mod = null;
        if (_hostModules.TryGetValue(name, out var build))
        {
            var mb = new ModuleBuilder(name);
            build(mb);
            mod = mb.Build();
        }
        else if (_sourceModules.TryGetValue(name, out var src))
        {
            var ns = new Namespace();
            mod = new PyModule(name, ns, false);
            _modules[name] = mod;
            try
            {
                var ctx = Interp.Current;
                var code = Compile(src, name + ".py", ns, name, false);
                ctx.RunFrame(new Frame(ctx, code, ns, new PyObject?[code.NumLocals], null));
            }
            catch
            {
                _modules.Remove(name);
                throw;
            }
            return mod;
        }
        else if (BuiltinModuleNames.Contains(name)) mod = NativeModules.Create(name, this);
        if (mod == null)
        {
            var e = PyErr.ModuleNotFoundError($"No module named '{name}'");
            e.Value.SetExtra("name", PyStr.New(name));
            throw e;
        }
        _modules[name] = mod;
        return mod;
    }

    // ------------------------------------------------------------------ globals

    /// <summary>Gets a global converted with <see cref="PyConvert.ToClr"/>, or null when undefined.</summary>
    public object? GetGlobal(string name) => TryGetGlobal(name, out var v) ? v : null;

    /// <summary>Gets a global converted with <see cref="PyConvert.ToClr"/>.</summary>
    public bool TryGetGlobal(string name, out object? value)
    {
        var v = _globals.Get(name);
        value = v == null ? null : PyConvert.ToClr(v);
        return v != null;
    }

    /// <summary>Gets the raw Python value of a global.</summary>
    public PyObject? GetGlobalObject(string name) => _globals.Get(name);

    /// <summary>Sets a global from a CLR value (see <see cref="PyConvert.FromClr"/>).</summary>
    public void SetGlobal(string name, object? value)
    {
        if (_running && Interp.CurrentOrNull?.Engine != this)
            throw new InvalidOperationException("Cannot change globals from another thread while code is executing.");
        if (string.IsNullOrEmpty(name) || !StrType.IsIdentifier(name))
            throw new ArgumentException($"'{name}' is not a valid Python identifier.", nameof(name));
        _globals.Set(name, PyConvert.FromClr(value));
    }

    /// <summary>Removes a global; returns false when it was not defined.</summary>
    public bool RemoveGlobal(string name) => _globals.Remove(name);

    /// <summary>Names of the user visible globals (dunder names excluded).</summary>
    public IReadOnlyList<string> GetGlobalNames() =>
        _globals.Items.Select(kv => kv.Key).Where(n => !(n.StartsWith("__") && n.EndsWith("__"))).ToList();

    /// <summary>Describes the user visible globals for a variables inspector, without running Python code.</summary>
    public IReadOnlyList<PythonVariable> GetVariables() =>
        _globals.Items.Where(kv => !(kv.Key.StartsWith("__") && kv.Key.EndsWith("__")))
            .Select(kv => new PythonVariable(kv.Key, kv.Value.TypeName, Truncate(Ops.SafeRepr(kv.Value), 1000))).ToList();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "...";

    // ------------------------------------------------------------------ callbacks used by the runtime

    internal void WriteOutput(string text) => Output?.Invoke(text);

    internal void WriteError(string text)
    {
        if (ErrorOutput != null) ErrorOutput(text);
        else Output?.Invoke(text);
    }

    internal string? ReadInput(string prompt) => Input?.Invoke(prompt);

    internal void DoSleep(TimeSpan duration, CancellationToken ct)
    {
        if (duration <= TimeSpan.Zero) return;
        if (Sleep != null) Sleep(duration, ct);
        else ct.WaitHandle.WaitOne(duration);
    }

    // ------------------------------------------------------------------ compilation

    private void RegisterSource(string file, string source) =>
        _sources[file] = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    internal string? SourceLine(string file, int line) =>
        _sources.TryGetValue(file, out var lines) && line >= 1 && line <= lines.Length ? lines[line - 1] : null;

    internal Code Compile(string source, string file, Namespace ns, string module, bool interactive, bool dynamicLocals = false)
    {
        RegisterSource(file, source);
        try
        {
            var ast = Parser.ParseModule(source);
            return Compiler.CompileModule(ast, file, ns, module, interactive, dynamicLocals);
        }
        catch (ParseError pe)
        {
            throw ToSyntaxError(pe, file);
        }
    }

    internal Code CompileExpression(string source, string file, Namespace ns, bool dynamicLocals)
    {
        RegisterSource(file, source);
        try
        {
            var ast = Parser.ParseEval(source);
            return Compiler.CompileExpression(ast, file, ns, "__main__", dynamicLocals);
        }
        catch (ParseError pe)
        {
            throw ToSyntaxError(pe, file);
        }
    }

    internal Code CompileForEval(string source, Namespace ns, bool isExec, bool dynamicLocals) =>
        isExec ? Compile(source, "<string>", ns, "__main__", false, dynamicLocals) : CompileExpression(source, "<string>", ns, dynamicLocals);

    private PythonException ToSyntaxError(ParseError pe, string file)
    {
        int line = Math.Max(1, pe.Line);
        var type = pe.Indentation ? Exc.IndentationError : Exc.SyntaxError;
        return PyErr.SyntaxError(pe.Message, file, line, pe.Col + 1, SourceLine(file, line), type);
    }

    // ------------------------------------------------------------------ execution core

    /// <summary>Starts a run on a dedicated interpreter thread with a large stack.</summary>
    private Task<ExecutionResult> StartRun(Func<Interp, PyObject?> body, CancellationToken ct, bool wantValue)
    {
        if (Interp.CurrentOrNull != null)
            throw new InvalidOperationException("The engine cannot be re-entered from a callback while code is executing.");
        lock (_interp)
        {
            if (_running) throw new InvalidOperationException("The engine is already executing code.");
            _running = true;
        }
        var tcs = new TaskCompletionSource<ExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ExecutionResult? result = null;
            Exception? fatal = null;
            _ownerThread = Environment.CurrentManagedThreadId;
            try
            {
                result = RunCore(body, ct, wantValue);
            }
            catch (Exception e)
            {
                fatal = e;
            }
            finally
            {
                _ownerThread = 0;
                _running = false;
            }
            if (fatal != null) tcs.SetException(fatal);
            else tcs.SetResult(result!);
        }, Math.Max(1024 * 1024, Options.ThreadStackSize))
        {
            IsBackground = true,
            Name = "Calcy Python interpreter",
        };
        try
        {
            thread.Start();
        }
        catch
        {
            _running = false;
            throw;
        }
        return tcs.Task;
    }

    /// <summary>Asynchronous <see cref="Execute"/>: the returned task completes when the program ends.</summary>
    public Task<ExecutionResult> ExecuteAsync(string source, string fileName = "<main>", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return StartRun(ctx =>
        {
            var code = Compile(source, fileName, _globals, "__main__", false);
            ctx.RunFrame(new Frame(ctx, code, _globals, new PyObject?[code.NumLocals], null));
            return null;
        }, cancellationToken, false);
    }

    /// <summary>Asynchronous <see cref="ExecuteInteractive"/>.</summary>
    public Task<ExecutionResult> ExecuteInteractiveAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return StartRun(ctx =>
        {
            LastInteractiveValue = null;
            var code = Compile(source, "<stdin>", _globals, "__main__", true);
            ctx.RunFrame(new Frame(ctx, code, _globals, new PyObject?[code.NumLocals], null));
            return LastInteractiveValue;
        }, cancellationToken, true);
    }

    /// <summary>Asynchronous <see cref="Evaluate"/>.</summary>
    public Task<ExecutionResult> EvaluateAsync(string expression, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return StartRun(ctx =>
        {
            var code = CompileExpression(expression, "<expr>", _globals, false);
            return ctx.RunFrame(new Frame(ctx, code, _globals, new PyObject?[code.NumLocals], null));
        }, cancellationToken, true);
    }

    private ExecutionResult RunCore(Func<Interp, PyObject?> body, CancellationToken ct, bool wantValue)
    {
        var ctx = _interp;
        ctx.Begin(Options, ct);
        using var scope = ctx.EnterThread();
        try
        {
            var value = body(ctx);
            object? clr = null;
            string? repr = null;
            if (wantValue && value != null)
            {
                repr = Ops.Repr(value);
                clr = PyConvert.ToClr(value);
            }
            return Done(ExecutionStatus.Completed, null, clr, repr);
        }
        catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.SystemExit))
        {
            return Exit(e.Value);
        }
        catch (PythonException e)
        {
            return Done(ExecutionStatus.Error, MakeError(e.Value), null, null);
        }
        catch (TerminationException t)
        {
            var (status, typeName) = TerminationKind(t.Reason);
            return Done(status, MakeTerminationError(typeName, t.Message, t.Frames), null, null);
        }
        catch (OperationCanceledException)
        {
            return Done(ExecutionStatus.Cancelled, MakeTerminationError("KeyboardInterrupt", "execution was cancelled", ctx.SnapshotStack()), null, null);
        }
        catch (Exception e) when (ExcHelpers.Convert(e) is { } pe)
        {
            return Done(ExecutionStatus.Error, MakeError(pe.Value), null, null);
        }
        finally
        {
            ctx.End();
        }

        ExecutionResult Done(ExecutionStatus st, PythonError? err, object? val, string? repr) => new()
        {
            Status = st,
            Error = err,
            Value = val,
            ValueRepr = repr,
            Steps = ctx.Steps,
            Elapsed = ctx.Elapsed,
        };

        ExecutionResult Exit(PyBaseException e)
        {
            int code = 0;
            var arg = e.Args.Count == 0 ? Py.None : e.Args.Items[0];
            if (arg == Py.None) code = 0;
            else if (arg is PyInt ai) code = ai.IsBig ? 1 : (int)Math.Clamp(ai.V, int.MinValue, int.MaxValue);
            else
            {
                WriteError(Ops.SafeStr(arg) + "\n");
                code = 1;
            }
            return new ExecutionResult
            {
                Status = ExecutionStatus.Exited,
                ExitCode = code,
                Steps = ctx.Steps,
                Elapsed = ctx.Elapsed,
            };
        }
    }

    private PythonError MakeError(PyBaseException e)
    {
        bool isSyntax = e.Type.IsSubtypeOf(Exc.SyntaxError);
        var frames = new List<PythonStackFrame>();
        for (int i = e.Traceback.Count - 1; i >= 0; i--)
        {
            var t = e.Traceback[i];
            frames.Add(new PythonStackFrame(t.FileName, t.Line, t.FunctionName, SourceLine(t.FileName, t.Line)?.Trim()));
        }
        string message;
        try
        {
            message = isSyntax ? (e.GetExtra("msg") as PyStr)?.V ?? e.BuiltinStr(true) : Ops.Str(e);
        }
        catch (Exception)
        {
            message = e.BuiltinStr(true);
        }
        string traceback;
        try
        {
            traceback = Runtime.Traceback.Format(e, SourceLine);
        }
        catch (Exception)
        {
            traceback = Runtime.Traceback.TypeLabel(e.Type) + ": " + message + "\n";
        }
        if (isSyntax)
        {
            var file = (e.GetExtra("filename") as PyStr)?.V;
            var line = e.GetExtra("lineno") is PyInt li ? (int)li.V : (int?)null;
            var col = e.GetExtra("offset") is PyInt oi ? (int)oi.V : (int?)null;
            var text = e.GetExtra("text") as PyStr;
            return new PythonError
            {
                TypeName = e.Type.Name,
                Message = message,
                FileName = file,
                Line = line,
                Column = col,
                SourceLine = text?.V.TrimEnd(),
                StackTrace = frames,
                Traceback = traceback,
                IsSyntaxError = true,
            };
        }
        var last = frames.Count > 0 ? frames[^1] : null;
        return new PythonError
        {
            TypeName = Runtime.Traceback.TypeLabel(e.Type),
            Message = message,
            FileName = last?.FileName,
            Line = last?.Line,
            SourceLine = last?.SourceLine,
            StackTrace = frames,
            Traceback = traceback,
        };
    }

    private PythonError MakeTerminationError(string typeName, string message, List<TracebackEntry> entries)
    {
        var frames = entries.Select(t => new PythonStackFrame(t.FileName, t.Line, t.FunctionName, SourceLine(t.FileName, t.Line)?.Trim())).ToList();
        var sb = new StringBuilder();
        if (frames.Count > 0)
        {
            sb.Append("Traceback (most recent call last):\n");
            foreach (var f in frames)
            {
                sb.Append("  File \"").Append(f.FileName).Append("\", line ").Append(f.Line).Append(", in ").Append(f.FunctionName).Append('\n');
                if (!string.IsNullOrEmpty(f.SourceLine)) sb.Append("    ").Append(f.SourceLine).Append('\n');
            }
        }
        sb.Append(typeName).Append(": ").Append(message).Append('\n');
        var last = frames.Count > 0 ? frames[^1] : null;
        return new PythonError
        {
            TypeName = typeName,
            Message = message,
            FileName = last?.FileName,
            Line = last?.Line,
            SourceLine = last?.SourceLine,
            StackTrace = frames,
            Traceback = sb.ToString(),
        };
    }
}

/// <summary>Creates the builtin native modules.</summary>
internal static class NativeModules
{
    public static PyModule? Create(string name, PythonEngine engine) => name switch
    {
        "math" => MathModule.Create(),
        "cmath" => CMathModule.Create(),
        "random" => RandomModule.Create(),
        "time" => TimeModule.Create(engine),
        "string" => StringModule.Create(),
        "itertools" => ItertoolsModule.Create(),
        "functools" => FunctoolsModule.Create(),
        "collections" => CollectionsModule.Create(),
        "operator" => OperatorModule.Create(),
        "sys" => SysModule.Create(engine),
        _ => null,
    };
}
