using System.Diagnostics;
using System.Runtime.CompilerServices;
using Calcy.Python.Nodes;

namespace Calcy.Python.Runtime;

internal enum Flow : byte
{
    Normal,
    Break,
    Continue,
    Return,
}

internal enum CodeKind : byte
{
    Module,
    Function,
    Lambda,
    Class,
    Comprehension,
    Eval,
}

/// <summary>Compiled code of a module, function, class body or comprehension.</summary>
internal sealed class Code
{
    public string Name = "";
    public string QualName = "";
    public string FileName = "";
    public CodeKind Kind;
    public int FirstLine;
    public int ArgCount;
    public int PosOnlyCount;
    public int KwOnlyCount;
    public bool HasVarArgs;
    public bool HasKwArgs;
    public int VarArgsSlot = -1;
    public int KwArgsSlot = -1;
    public string[] ParamNames = Array.Empty<string>();
    public int NumLocals;
    public string[] LocalNames = Array.Empty<string>();
    public string[] CellNames = Array.Empty<string>();
    public string[] FreeNames = Array.Empty<string>();
    public (int Slot, int Cell)[] CellParams = Array.Empty<(int, int)>();
    public Block Body = null!;
    public bool IsGenerator;
    public PyObject Doc = Py.None;
    public bool FastArgs;
    public int ClassCellIndex = -1;

    public int NumCells => CellNames.Length + FreeNames.Length;
}

/// <summary>An activation record.</summary>
internal sealed class Frame
{
    public readonly Code Code;
    public readonly Namespace Globals;
    public readonly PyObject?[] Locals;
    public readonly Cell[]? Cells;
    public readonly Interp Ctx;
    public Dictionary<string, PyObject>? ClassNs;
    public PyDict? DynLocals;
    public Frame? Back;
    public int Line;
    public PyObject? ReturnValue;
    public PyBaseException? Handled;
    public PyObject? Sent;
    public PyBaseException? ThrowIn;
    public Flow GenFlow;
    public PyFunction? Function;

    public Frame(Interp ctx, Code code, Namespace globals, PyObject?[] locals, Cell[]? cells)
    {
        Ctx = ctx;
        Code = code;
        Globals = globals;
        Locals = locals;
        Cells = cells;
        Line = code.FirstLine;
    }

    /// <summary>Returns the exception currently being handled in this frame or its callers.</summary>
    public PyBaseException? CurrentHandled()
    {
        for (var f = this; f != null; f = f.Back)
            if (f.Handled != null) return f.Handled;
        return null;
    }
}

/// <summary>Per engine execution state: limits, counters and the frame stack.</summary>
internal sealed class Interp
{
    [ThreadStatic] private static Interp? t_current;

    public static Interp? CurrentOrNull => t_current;

    public static Interp Current => t_current ?? throw new InvalidOperationException("No Python code is executing on this thread.");

    public readonly PythonEngine Engine;
    public long Steps;
    public long NextCheck = 1024;
    public long MaxSteps;
    public int Depth;
    public int MaxDepth = 1000;
    public int MaxCollection = Limits.DefaultMaxCollection;
    public int MaxIntBits = Limits.DefaultMaxIntBits;
    public long MaxMemory;
    public CancellationToken Ct;
    public Frame? CurrentFrame;
    private readonly Stopwatch _clock = new();
    private TimeSpan? _timeout;
    private TimeSpan _paused;
    private int _pauseDepth;
    private TimeSpan _pauseStart;
    private long _memoryBaseline;
    private int _checks;
    private HashSet<PyObject>? _reprGuard;

    public Interp(PythonEngine engine)
    {
        Engine = engine;
    }

    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>Prepares counters for a direct call from host code (no memory baseline, no thread).</summary>
    public void BeginDirect(PythonEngineOptions o, long maxSteps, TimeSpan? timeout, int maxDepth, CancellationToken ct)
    {
        Steps = 0;
        MaxSteps = maxSteps;
        NextCheck = MaxSteps > 0 ? Math.Min(1024, MaxSteps + 1) : 1024;
        Depth = 0;
        MaxDepth = Math.Max(10, maxDepth);
        MaxCollection = Math.Max(16, o.MaxCollectionSize);
        MaxIntBits = Math.Max(64, o.MaxIntBits);
        MaxMemory = 0;
        Ct = ct;
        _timeout = timeout;
        _paused = TimeSpan.Zero;
        _pauseDepth = 0;
        _checks = 0;
        CurrentFrame = null;
        _reprGuard = null;
        _memoryBaseline = 0;
        if (ct.IsCancellationRequested) NextCheck = 0;
        _clock.Restart();
    }

    /// <summary>Prepares counters for a new top level execution.</summary>
    public void Begin(PythonEngineOptions o, CancellationToken ct)
    {
        Steps = 0;
        MaxSteps = o.MaxSteps;
        NextCheck = MaxSteps > 0 ? Math.Min(1024, MaxSteps + 1) : 1024;
        Depth = 0;
        MaxDepth = Math.Max(10, o.MaxRecursionDepth);
        MaxCollection = Math.Max(16, o.MaxCollectionSize);
        MaxIntBits = Math.Max(64, o.MaxIntBits);
        MaxMemory = o.MaxMemoryBytes;
        Ct = ct;
        _timeout = o.Timeout;
        _paused = TimeSpan.Zero;
        _pauseDepth = 0;
        _checks = 0;
        CurrentFrame = null;
        _reprGuard = null;
        _memoryBaseline = 0;
        if (MaxMemory > 0)
        {
            // Garbage left over from earlier work could hide growth; collect only when it could matter.
            long heap = GC.GetTotalMemory(false);
            if (heap > MaxMemory / 2) heap = GC.GetTotalMemory(true);
            _memoryBaseline = heap;
        }
        _clock.Restart();
    }

    public void End()
    {
        _clock.Stop();
        CurrentFrame = null;
    }

    public Scope EnterThread()
    {
        var prev = t_current;
        t_current = this;
        return new Scope(prev);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly Interp? _prev;
        public Scope(Interp? prev) { _prev = prev; }
        public void Dispose() => t_current = _prev;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick()
    {
        if (++Steps >= NextCheck) Check();
    }

    public void Check()
    {
        NextCheck = Steps + 1024;
        if (MaxSteps > 0)
        {
            if (Steps > MaxSteps)
                throw Terminate(TerminationReason.StepLimit, $"step limit of {MaxSteps} exceeded");
            NextCheck = Math.Min(NextCheck, MaxSteps + 1);
        }
        if (Ct.IsCancellationRequested)
            throw Terminate(TerminationReason.Cancelled, "execution was cancelled");
        if (_timeout.HasValue && LimitedElapsed > _timeout.Value)
            throw Terminate(TerminationReason.Timeout, $"execution time limit of {_timeout.Value.TotalSeconds:0.###} s exceeded");
        if (MaxMemory > 0 && (++_checks & 31) == 0)
        {
            long used = GC.GetTotalMemory(false) - _memoryBaseline;
            if (used > MaxMemory)
            {
                used = GC.GetTotalMemory(true) - _memoryBaseline;
                if (used > MaxMemory)
                    throw PyErr.MemoryError($"memory limit of {MaxMemory / (1024 * 1024)} MB exceeded");
            }
        }
    }

    /// <summary>Time that counts against the time limit (pauses excluded).</summary>
    private TimeSpan LimitedElapsed
    {
        get
        {
            var now = _clock.Elapsed;
            var t = now - _paused;
            if (_pauseDepth > 0) t -= now - _pauseStart;
            return t;
        }
    }

    /// <summary>Stops counting wall clock time against the time limit until <see cref="ResumeTimeLimit"/>. Nests.</summary>
    public void PauseTimeLimit()
    {
        if (_pauseDepth++ == 0) _pauseStart = _clock.Elapsed;
    }

    public void ResumeTimeLimit()
    {
        if (_pauseDepth == 0) return;
        if (--_pauseDepth == 0) _paused += _clock.Elapsed - _pauseStart;
        // check cancellation at the next statement
        NextCheck = Math.Min(NextCheck, Steps + 1);
    }

    /// <summary>Excludes time spent waiting for the host (for example in input()) from the timeout.</summary>
    public T Paused<T>(Func<T> f)
    {
        PauseTimeLimit();
        try { return f(); }
        finally { ResumeTimeLimit(); }
    }

    public TimeSpan? RemainingTime => _timeout.HasValue ? _timeout.Value - LimitedElapsed : null;

    public TerminationException Terminate(TerminationReason reason, string message) =>
        new(reason, message, SnapshotStack());

    public List<TracebackEntry> SnapshotStack()
    {
        var list = new List<TracebackEntry>();
        for (var f = CurrentFrame; f != null; f = f.Back)
            list.Add(new TracebackEntry(f.Code.FileName, f.Line, f.Code.Name));
        list.Reverse();
        return list;
    }

    public PyObject RunFrame(Frame f)
    {
        if (++Depth > MaxDepth)
        {
            Depth--;
            throw PyErr.RecursionError("maximum recursion depth exceeded");
        }
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Depth--;
            throw PyErr.RecursionError("maximum recursion depth exceeded (interpreter stack exhausted)");
        }
        f.Back = CurrentFrame;
        CurrentFrame = f;
        try
        {
            f.Code.Body.Exec(f);
            return f.ReturnValue ?? Py.None;
        }
        catch (PythonException e) when (Traceback.Record(e, f))
        {
            throw;
        }
        finally
        {
            CurrentFrame = f.Back;
            Depth--;
        }
    }

    /// <summary>Guards native recursion (repr, comparisons of nested containers).</summary>
    public static void CheckStack()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw PyErr.RecursionError("maximum recursion depth exceeded");
    }

    public bool ReprEnter(PyObject o)
    {
        _reprGuard ??= new HashSet<PyObject>(ReferenceEqualityComparer.Instance);
        return _reprGuard.Add(o);
    }

    public void ReprLeave(PyObject o) => _reprGuard?.Remove(o);
}
