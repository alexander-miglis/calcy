using System.Globalization;
using System.Text;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Python;
using Calcy.Scripting.Calc;
using Calcy.Scripting.Drawing;

namespace Calcy.Scripting;

/// <summary>
/// One sandboxed Python session for Calcy: a <see cref="PythonEngine"/> with safe limits, the calculator bridge (calc), the
/// drawing modules (turtle, ti_plotlib, ti_draw, casioplot, calcydraw), ti_system, a console output stream, an input
/// provider, a key queue and cancellation. Code mode, Notebook mode and the MCP server each create one.
/// </summary>
/// <remarks>
/// Every public member is safe to call from any thread. Runs execute on a dedicated interpreter thread; the
/// <see cref="Output"/> event, the <see cref="InputProvider"/> and graph change events are invoked on that thread, so a UI
/// marshals them to its own thread. One run executes at a time; further runs wait for it.
/// </remarks>
public sealed partial class ScriptHost : IDisposable
{
    /// <summary>The modules programs can import in addition to the interpreter's standard ones.</summary>
    public static readonly IReadOnlyList<string> HostModules =
        ["calc", "fractions", "turtle", "ce_turtl", "ti_plotlib", "ti_draw", "casioplot", "calcydraw", "ti_system"];

    private const string InteractiveFile = "<stdin>";

    [ThreadStatic] private static ScriptHost? t_active;

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Sources = new(LoadSources);
    private static readonly Lazy<IReadOnlyList<ModuleMemberDoc>> ReferenceDocs = new(BuildReference);

    private readonly PythonEngine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _outputGate = new();
    private readonly CalcBridge _calc;
    private readonly StringBuilder _output = new();
    private ScriptHostOptions _options;
    private CancellationTokenSource? _run;
    private string _runFile = "main.py";
    private bool _outputTruncated;
    private volatile bool _running;
    private volatile bool _disposed;
    private IReadOnlyList<PythonVariable> _variables = [];

    /// <summary>Creates a host bound to a calculator session (usually the app's shared session).</summary>
    public ScriptHost(CalcSession session, ScriptHostOptions? options = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? ScriptHostOptions.Default;
        Canvas = new DrawCanvas(_options.MaxLoggedDrawCommands);
        _calc = new CalcBridge(this);
        _engine = new PythonEngine(EngineOptions(_options))
        {
            Output = text => Emit(ScriptOutputKind.Standard, text),
            ErrorOutput = text => Emit(ScriptOutputKind.Error, text),
            Input = OnInput,
        };
        _engine.RegisterModule("_calcy", BuildNativeModule);
        foreach (var (name, source) in Sources.Value) _engine.RegisterSourceModule(name, source);
    }

    /// <summary>The calculator session that calc reads and writes.</summary>
    public CalcSession Session { get; }

    /// <summary>Limits and behaviour; a change applies from the next run.</summary>
    public ScriptHostOptions Options
    {
        get => _options;
        set => _options = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The drawing surface used by the turtle, ti_plotlib, ti_draw, casioplot and calcydraw modules.</summary>
    public DrawCanvas Canvas { get; }

    /// <summary>The graphs requested with calc.graph.add.</summary>
    public ScriptGraphs Graphs { get; } = new();

    /// <summary>The key queue read by get_key style functions; the screen feeds it.</summary>
    public ScriptKeyboard Keyboard { get; } = new();

    /// <summary>True while a program or REPL line runs.</summary>
    public bool IsRunning => _running;

    /// <summary>Raised for every piece of console output, on the interpreter thread.</summary>
    public event EventHandler<ScriptOutputEventArgs>? Output;

    /// <summary>
    /// Supplies lines for input(). Called on the interpreter thread; the returned task may complete on any thread.
    /// Returning null raises EOFError in the program. The token is cancelled when the run is stopped.
    /// </summary>
    public Func<ScriptInputRequest, CancellationToken, Task<string?>>? InputProvider { get; set; }

    /// <summary>Modules a program can import (the interpreter's standard modules plus <see cref="HostModules"/>).</summary>
    public IReadOnlyCollection<string> AvailableModules => _engine.AvailableModules.Where(m => !m.StartsWith('_')).ToList();

    /// <summary>Names of calculator functions registered with @calc.function by this host.</summary>
    public IReadOnlyList<string> CalcFunctions => _calc.FunctionNames;

    /// <summary>Documentation of every public function and class of the host modules, from their docstrings.</summary>
    public static IReadOnlyList<ModuleMemberDoc> Reference => ReferenceDocs.Value;

    internal bool IsOnInterpreterThread => t_active == this;

    // ------------------------------------------------------------------ running code

    /// <summary>Runs a program with fresh globals, a cleared canvas and no graphs.</summary>
    public async Task<ScriptRunResult> RunAsync(string source, string fileName = "main.py", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _engine.Reset();
            Canvas.Reset();
            Graphs.Clear();
            Keyboard.Clear();
            var token = BeginRun(fileName, cancellationToken);
            var r = await _engine.ExecuteAsync(source, fileName, token).ConfigureAwait(false);
            return Finish(ScriptRunKind.Program, fileName, r, null);
        }
        finally
        {
            EndRun();
            _gate.Release();
        }
    }

    /// <summary>Runs a program and blocks until it ends (see <see cref="RunAsync"/>).</summary>
    public ScriptRunResult Run(string source, string fileName = "main.py", CancellationToken cancellationToken = default) =>
        Task.Run(() => RunAsync(source, fileName, cancellationToken), CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Runs one REPL input in the current globals (those of the last program, like IDLE). Expression values are echoed
    /// with repr(); calculator results are also reported in <see cref="ScriptRunResult.MathValue"/> for typesetting.
    /// </summary>
    public async Task<ScriptRunResult> ExecuteInteractiveAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var token = BeginRun(InteractiveFile, cancellationToken);
            var r = await _engine.ExecuteInteractiveAsync(source, token).ConfigureAwait(false);
            string? math = null;
            if (r.Status == ExecutionStatus.Completed && r.ValueRepr is not null && _engine.GetGlobalObject("_")?.TypeName is "Expr" or "Fraction")
            {
                var t = await _engine.EvaluateAsync("_._calc_text()", token).ConfigureAwait(false);
                if (t.Status == ExecutionStatus.Completed) math = t.Value as string;
            }
            return Finish(ScriptRunKind.Interactive, InteractiveFile, r, math);
        }
        finally
        {
            EndRun();
            _gate.Release();
        }
    }

    /// <summary>Runs one REPL input and blocks until it ends.</summary>
    public ScriptRunResult ExecuteInteractive(string source, CancellationToken cancellationToken = default) =>
        Task.Run(() => ExecuteInteractiveAsync(source, cancellationToken), CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>False when the REPL input is an unfinished statement that needs more lines.</summary>
    public bool IsInputComplete(string source) => _engine.IsInputComplete(source);

    /// <summary>Stops the current run (KeyboardInterrupt); waiting input and key reads end at once.</summary>
    public void Stop()
    {
        try
        {
            _run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run just ended.
        }
    }

    /// <summary>Clears the globals, the canvas and the graphs (registered calculator functions stay).</summary>
    public void Reset()
    {
        ThrowIfDisposed();
        Stop();
        _gate.Wait();
        try
        {
            _engine.Reset();
            Canvas.Reset();
            Graphs.Clear();
            Keyboard.Clear();
            _variables = [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The program's global variables for an inspector (a snapshot from the end of the last run while code runs).</summary>
    public IReadOnlyList<PythonVariable> GetVariables()
    {
        if (!_gate.Wait(0)) return _variables;
        try
        {
            return _variables = _engine.GetVariables().Where(v => !IsModuleNoise(v)).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsModuleNoise(PythonVariable v) => v.TypeName == "module" && v.Name.StartsWith('_');

    /// <summary>Stops any run and detaches the host; calculator functions it registered report that they are gone.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _calc.Close();
        Output = null;
        InputProvider = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static PythonEngineOptions EngineOptions(ScriptHostOptions o) => new()
    {
        Timeout = o.Timeout,
        MaxSteps = o.MaxSteps,
        MaxMemoryBytes = o.MaxMemoryBytes,
        MaxRecursionDepth = o.MaxRecursionDepth,
        MaxCollectionSize = o.MaxCollectionSize,
        MaxIntBits = o.MaxIntBits,
        EchoInputPrompt = false,
        EchoInput = false,
    };

    private CancellationToken BeginRun(string fileName, CancellationToken external)
    {
        _engine.Options = EngineOptions(_options);
        lock (_outputGate)
        {
            _output.Clear();
            _outputTruncated = false;
        }
        _runFile = fileName;
        _run = CancellationTokenSource.CreateLinkedTokenSource(external);
        _running = true;
        return _run.Token;
    }

    private void EndRun()
    {
        _running = false;
        var run = _run;
        _run = null;
        run?.Dispose();
    }

    private ScriptRunResult Finish(ScriptRunKind kind, string fileName, ExecutionResult r, string? math)
    {
        // Double buffered drawings left unshown at the end are shown, as on the calculators.
        Canvas.ShowPending();
        try
        {
            _variables = _engine.GetVariables().Where(v => !IsModuleNoise(v)).ToList();
        }
        catch (InvalidOperationException)
        {
            // Keep the previous snapshot.
        }
        int? line = null, column = null;
        if (r.Error is { } err)
        {
            if (err.IsSyntaxError)
            {
                if (err.FileName == fileName)
                {
                    line = err.Line;
                    column = err.Column;
                }
            }
            else
            {
                for (int i = err.StackTrace.Count - 1; i >= 0; i--)
                {
                    if (err.StackTrace[i].FileName != fileName) continue;
                    line = err.StackTrace[i].Line;
                    break;
                }
            }
        }
        string output;
        bool truncated;
        lock (_outputGate)
        {
            if (r.Error is { } e && r.Status != ExecutionStatus.Exited)
            {
                if (_output.Length > 0 && _output[^1] != '\n') _output.Append('\n');
                _output.Append(e.Traceback);
            }
            output = _output.ToString();
            truncated = _outputTruncated;
        }
        return new ScriptRunResult
        {
            Kind = kind,
            FileName = fileName,
            Status = r.Status,
            Error = r.Error,
            ErrorLine = line,
            ErrorColumn = column,
            Elapsed = r.Elapsed,
            Steps = r.Steps,
            Output = output,
            OutputTruncated = truncated,
            Value = r.Value,
            ValueRepr = r.ValueRepr,
            MathValue = math,
            ExitCode = r.ExitCode,
            Drew = Canvas.HasDrawing,
            GraphCount = Graphs.Items.Count,
        };
    }

    // ------------------------------------------------------------------ console

    internal void Emit(ScriptOutputKind kind, string text, string? label = null)
    {
        if (text.Length == 0 && kind is not (ScriptOutputKind.Clear or ScriptOutputKind.Math)) return;
        lock (_outputGate)
        {
            if (_outputTruncated) return;
            int max = Math.Max(1000, _options.MaxOutputChars);
            if (_output.Length + text.Length > max)
            {
                int keep = Math.Max(0, max - _output.Length);
                text = text[..keep] + $"\n[output stopped: more than {max.ToString("N0", CultureInfo.InvariantCulture)} characters]\n";
                _outputTruncated = true;
                kind = kind == ScriptOutputKind.Math ? ScriptOutputKind.Standard : kind;
            }
            switch (kind)
            {
                case ScriptOutputKind.Math:
                    _output.Append(label is null ? "" : label + " ").Append(text).Append('\n');
                    break;
                case ScriptOutputKind.Clear:
                    break;
                default:
                    _output.Append(text);
                    break;
            }
        }
        Output?.Invoke(this, new ScriptOutputEventArgs(kind, text, label));
    }

    private string? OnInput(string prompt)
    {
        var token = _run?.Token ?? CancellationToken.None;
        if (prompt.Length > 0) Emit(ScriptOutputKind.Prompt, prompt);
        var provider = InputProvider;
        if (provider is null) return null;
        string? line;
        try
        {
            var task = provider(new ScriptInputRequest(prompt, _runFile), token);
            task.Wait(token);
            line = task.Result;
        }
        catch (OperationCanceledException)
        {
            return "";
        }
        catch (AggregateException e) when (e.InnerException is OperationCanceledException)
        {
            return "";
        }
        if (line is not null)
        {
            line = line.Replace("\r", "").Split('\n')[0];
            Emit(ScriptOutputKind.Input, line + "\n");
        }
        return line;
    }

    // ------------------------------------------------------------------ calculator callbacks

    /// <summary>Limits for one call of a Python function by the calculator from outside a run.</summary>
    internal PythonCallOptions CallOptions => new()
    {
        Timeout = _options.CallbackTimeout,
        MaxSteps = Math.Min(_options.MaxSteps <= 0 ? long.MaxValue : _options.MaxSteps, 50_000_000),
        MaxRecursionDepth = Math.Min(200, _options.MaxRecursionDepth),
    };

    /// <summary>
    /// Runs work that calls Python for the calculator (a function registered with @calc.function). Inside this host's
    /// Python code it runs nested in it; otherwise it runs directly on the calling thread, one call at a time, and fails
    /// at once while a program is running.
    /// </summary>
    internal T RunCallback<T>(string name, Func<T> work)
    {
        if (t_active == this) return work();
        if (_disposed) throw new CalcException($"{name} was defined by a Python program that is no longer loaded");
        // Another calculator call (a graph being sampled, say) only takes a moment; a running program can take forever.
        if (_running || !_gate.Wait(_options.CallbackTimeout))
            throw new CalcException($"{name} is a Python function and its program is still running; try again when it stops");
        var previous = t_active;
        t_active = this;
        try
        {
            return work();
        }
        catch (PythonCallException e)
        {
            throw new CalcException(e.Status switch
            {
                ExecutionStatus.TimedOut => $"{name} took longer than {_options.CallbackTimeout.TotalSeconds:0.#} s",
                ExecutionStatus.StepLimitExceeded => $"{name} ran too many steps",
                ExecutionStatus.Cancelled => $"{name} was stopped",
                _ => $"{name}: {e.Error.TypeName}: {e.Error.Message}",
            });
        }
        finally
        {
            t_active = previous;
            _gate.Release();
        }
    }

    /// <summary>
    /// Calls a Python function of numbers for plotting: NaN when it fails, returns something that is not a number, or
    /// cannot run now because a program is running.
    /// </summary>
    internal double CallForPlot(PythonCallable function, double x, double? y = null)
    {
        if (_disposed) return double.NaN;
        if (t_active == this) return Invoke();
        if (_running || !_gate.Wait(_options.CallbackTimeout)) return double.NaN;
        var previous = t_active;
        t_active = this;
        try
        {
            return Invoke();
        }
        catch (PythonCallException)
        {
            return double.NaN;
        }
        finally
        {
            t_active = previous;
            _gate.Release();
        }

        double Invoke()
        {
            double r;
            bool ok = y is { } yv ? function.TryInvokeDouble(x, yv, out r) : function.TryInvokeDouble(x, out r);
            return ok ? r : double.NaN;
        }
    }

    // ------------------------------------------------------------------ module sources and reference

    private static IReadOnlyDictionary<string, string> LoadSources()
    {
        var asm = typeof(ScriptHost).Assembly;
        const string prefix = "Calcy.Scripting.Python.";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".py", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            result[res[prefix.Length..^3]] = reader.ReadToEnd();
        }
        return result;
    }

    private static IReadOnlyList<ModuleMemberDoc> BuildReference()
    {
        var session = Engine.CalcyEngine.CreateSession();
        using var host = new ScriptHost(session, ScriptHostOptions.Default with { Timeout = TimeSpan.FromSeconds(20) });
        var modules = string.Join(", ", HostModules.Select(m => "'" + m + "'"));
        var code = $$"""
            import calc, fractions, turtle, ce_turtl, ti_plotlib, ti_draw, casioplot, calcydraw, ti_system
            _mods = {'calc': calc, 'fractions': fractions, 'turtle': turtle, 'ce_turtl': ce_turtl, 'ti_plotlib': ti_plotlib,
                     'ti_draw': ti_draw, 'casioplot': casioplot, 'calcydraw': calcydraw, 'ti_system': ti_system}
            _docs = []
            for _mn in [{{modules}}]:
                _m = _mods[_mn]
                for _n in dir(_m):
                    if _n.startswith('_'):
                        continue
                    _o = getattr(_m, _n)
                    _d = getattr(_o, '__doc__', None)
                    if not isinstance(_d, str) or type(_o).__name__ == 'module':
                        continue
                    if isinstance(_o, type):
                        _docs.append((_mn, _n, _d))
                        for _k in dir(_o):
                            if _k.startswith('_') or (issubclass(_o, str) and hasattr(str, _k)):
                                continue
                            _kd = getattr(getattr(_o, _k), '__doc__', None)
                            if isinstance(_kd, str) and callable(getattr(_o, _k)):
                                _docs.append((_mn, _n + '.' + _k, _kd))
                    elif callable(_o):
                        _docs.append((_mn, _n, _d))
            """;
        var r = host.Run(code, "reference.py");
        if (!r.Success) return [];
        var docs = new List<ModuleMemberDoc>();
        host._gate.Wait();
        try
        {
            if (host._engine.GetGlobal("_docs") is not IReadOnlyList<object?> list) return [];
            foreach (var item in list.OfType<IReadOnlyList<object?>>())
            {
                if (item.Count < 3 || item[0] is not string module || item[1] is not string name || item[2] is not string doc) continue;
                var text = doc.Trim();
                string signature = name.Split('.')[^1] + "()";
                string summary = text;
                int open = text.IndexOf('('), close = text.IndexOf("): ", StringComparison.Ordinal);
                if (open > 0 && close > open && !text[..open].Contains(' '))
                {
                    signature = text[..(close + 1)];
                    summary = text[(close + 3)..].Trim();
                }
                docs.Add(new ModuleMemberDoc(module, name, signature, summary));
            }
        }
        finally
        {
            host._gate.Release();
        }
        return docs;
    }
}
