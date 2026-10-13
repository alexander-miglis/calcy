namespace Calcy.Python;

/// <summary>Resource limits and behaviour switches for a <see cref="PythonEngine"/>.</summary>
public sealed record PythonEngineOptions
{
    /// <summary>Maximum number of executed statements per run; 0 means unlimited.</summary>
    public long MaxSteps { get; init; } = 1_000_000_000;

    /// <summary>Maximum Python call depth; deeper recursion raises RecursionError.</summary>
    public int MaxRecursionDepth { get; init; } = 1000;

    /// <summary>Wall clock limit per run (time spent waiting in input() is excluded); null means unlimited.</summary>
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Maximum number of elements in a list, tuple, dict or set, and maximum string length.</summary>
    public int MaxCollectionSize { get; init; } = 10_000_000;

    /// <summary>Maximum size in bits of an integer produced by multiplication, power or shift.</summary>
    public int MaxIntBits { get; init; } = 1_000_000;

    /// <summary>Approximate managed heap growth allowed during a run before MemoryError is raised; 0 disables the check.</summary>
    public long MaxMemoryBytes { get; init; } = 1L << 30;

    /// <summary>When true, the input() prompt is written to <see cref="PythonEngine.Output"/> before the host is asked for input.</summary>
    public bool EchoInputPrompt { get; init; } = true;

    /// <summary>When true, the text returned by the host for input() is echoed to the output followed by a newline.</summary>
    public bool EchoInput { get; init; }

    /// <summary>Stack size in bytes of the dedicated interpreter thread.</summary>
    public int ThreadStackSize { get; init; } = 256 * 1024 * 1024;

    /// <summary>The default options.</summary>
    public static PythonEngineOptions Default { get; } = new();
}

/// <summary>
/// Limits for one call of a <see cref="PythonCallable"/> made from outside a run, which executes directly on the
/// calling thread. Calls made from inside a run (from a host function) share that run's limits instead.
/// </summary>
public sealed record PythonCallOptions
{
    /// <summary>Maximum number of executed statements per call; 0 means unlimited.</summary>
    public long MaxSteps { get; init; } = 1_000_000;

    /// <summary>Wall clock limit per call; null means unlimited.</summary>
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum Python call depth per call (capped by <see cref="PythonEngineOptions.MaxRecursionDepth"/>). Recursion also
    /// stops with RecursionError before the calling thread's stack runs out.
    /// </summary>
    public int MaxRecursionDepth { get; init; } = 200;

    /// <summary>Stops a call in progress; it then fails with <see cref="ExecutionStatus.Cancelled"/>.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>The default limits.</summary>
    public static PythonCallOptions Default { get; } = new();
}
