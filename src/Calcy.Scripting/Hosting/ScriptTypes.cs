using Calcy.Python;

namespace Calcy.Scripting;

/// <summary>Limits and behaviour of a <see cref="ScriptHost"/>. The defaults are safe for unattended use (MCP, notebooks).</summary>
public sealed record ScriptHostOptions
{
    /// <summary>Wall clock limit per run. Time spent waiting for input, keys or frames does not count (see <see cref="PauseTimeoutWhileWaiting"/>).</summary>
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum statements per run; 0 means unlimited.</summary>
    public long MaxSteps { get; init; } = 2_000_000_000;

    /// <summary>Approximate memory a run may allocate before MemoryError.</summary>
    public long MaxMemoryBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Maximum Python call depth.</summary>
    public int MaxRecursionDepth { get; init; } = 1000;

    /// <summary>Maximum number of items in a list, dict, set or string.</summary>
    public int MaxCollectionSize { get; init; } = 10_000_000;

    /// <summary>Maximum size in bits of integers made by multiplication, powers and shifts.</summary>
    public int MaxIntBits { get; init; } = 1_000_000;

    /// <summary>Time limit for one call of a Python function registered with @calc.function when the calculator calls it.</summary>
    public TimeSpan CallbackTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Console text kept per run; later output is dropped with a notice.</summary>
    public int MaxOutputChars { get; init; } = 1_000_000;

    /// <summary>
    /// When true, waiting for a key, for the next animation frame or in turtle animation does not count towards
    /// <see cref="Timeout"/>, so games and animations can run until stopped. Set false for hard time limits.
    /// </summary>
    public bool PauseTimeoutWhileWaiting { get; init; } = true;

    /// <summary>When true, turtle moves are animated at the speed the program sets (the screen uses this; tests and servers do not).</summary>
    public bool AnimateTurtle { get; init; }

    /// <summary>
    /// When true, pure black and pure white from programs are drawn with the colour scheme's ink and background, so
    /// classic black on white drawings stay readable in dark schemes. Every other colour is drawn as given.
    /// </summary>
    public bool SchemeBlackAndWhite { get; init; } = true;

    /// <summary>Commands kept in <see cref="Drawing.DrawCanvas.Commands"/> between clears.</summary>
    public int MaxLoggedDrawCommands { get; init; } = 100_000;

    /// <summary>The defaults.</summary>
    public static ScriptHostOptions Default { get; } = new();
}

/// <summary>What a piece of console output is.</summary>
public enum ScriptOutputKind
{
    /// <summary>print() and sys.stdout.</summary>
    Standard,

    /// <summary>sys.stderr.</summary>
    Error,

    /// <summary>The prompt of input(); the host then asks the input provider for a line.</summary>
    Prompt,

    /// <summary>The line the user answered to input(), followed by a newline.</summary>
    Input,

    /// <summary>A calculator result to typeset (calc.show); <see cref="ScriptOutputEventArgs.Text"/> is calculator input text.</summary>
    Math,

    /// <summary>The program cleared the console (ti_system.disp_clr).</summary>
    Clear,
}

/// <summary>One piece of console output.</summary>
public sealed class ScriptOutputEventArgs(ScriptOutputKind kind, string text, string? label = null) : EventArgs
{
    /// <summary>The kind of output.</summary>
    public ScriptOutputKind Kind { get; } = kind;

    /// <summary>The text (for <see cref="ScriptOutputKind.Math"/> the calculator expression to typeset).</summary>
    public string Text { get; } = text;

    /// <summary>An optional label shown before a typeset result.</summary>
    public string? Label { get; } = label;
}

/// <summary>A request for a line of input.</summary>
/// <param name="Prompt">The prompt passed to input() (already shown as <see cref="ScriptOutputKind.Prompt"/> output).</param>
/// <param name="FileName">The running program.</param>
public sealed record ScriptInputRequest(string Prompt, string FileName);

/// <summary>What kind of run produced a result.</summary>
public enum ScriptRunKind
{
    /// <summary>A whole program run with fresh globals.</summary>
    Program,

    /// <summary>A REPL line that keeps the globals.</summary>
    Interactive,
}

/// <summary>The outcome of a run, with everything a run card or a tool result needs.</summary>
public sealed class ScriptRunResult
{
    /// <summary>Program or REPL.</summary>
    public required ScriptRunKind Kind { get; init; }

    /// <summary>The program's file name (for a REPL line, "&lt;stdin&gt;").</summary>
    public required string FileName { get; init; }

    /// <summary>How the run ended.</summary>
    public required ExecutionStatus Status { get; init; }

    /// <summary>True when the run completed or exited with code 0.</summary>
    public bool Success => Status == ExecutionStatus.Completed || (Status == ExecutionStatus.Exited && ExitCode is null or 0);

    /// <summary>The error for failed, stopped or limited runs.</summary>
    public PythonError? Error { get; init; }

    /// <summary>The line of the error in the program itself (the innermost frame in <see cref="FileName"/>), for editor markers.</summary>
    public int? ErrorLine { get; init; }

    /// <summary>The column of a syntax error (1 based).</summary>
    public int? ErrorColumn { get; init; }

    /// <summary>Wall clock time.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Statements executed.</summary>
    public long Steps { get; init; }

    /// <summary>Everything the run wrote to the console (prompts and answers included), followed by the traceback on error.</summary>
    public string Output { get; init; } = "";

    /// <summary>True when output was dropped because of <see cref="ScriptHostOptions.MaxOutputChars"/>.</summary>
    public bool OutputTruncated { get; init; }

    /// <summary>The value echoed by a REPL line, converted to a CLR value.</summary>
    public object? Value { get; init; }

    /// <summary>repr() of the echoed value.</summary>
    public string? ValueRepr { get; init; }

    /// <summary>When the echoed value is a calculator result (calc.Expr or Fraction), its calculator text for typesetting.</summary>
    public string? MathValue { get; init; }

    /// <summary>The exit code of sys.exit().</summary>
    public int? ExitCode { get; init; }

    /// <summary>True when the program drew on the canvas.</summary>
    public bool Drew { get; init; }

    /// <summary>Number of graphs the program requested.</summary>
    public int GraphCount { get; init; }

    /// <summary>A one line description of how the run ended, for run cards and logs.</summary>
    public string Summary
    {
        get
        {
            return Status switch
            {
                ExecutionStatus.Completed => "Finished",
                ExecutionStatus.Exited => ExitCode is null or 0 ? "Exited" : $"Exited with code {ExitCode}",
                ExecutionStatus.Cancelled => "Stopped",
                ExecutionStatus.TimedOut => "Time limit reached",
                ExecutionStatus.StepLimitExceeded => "Step limit reached",
                _ => Error is null ? "Error" : $"{Error.TypeName}: {Error.Message}" + (ErrorLine is { } l ? $" (line {l})" : ""),
            };
        }
    }
}

/// <summary>Documentation of one module member, taken from its docstring.</summary>
/// <param name="Module">The module name.</param>
/// <param name="Name">The member name (methods as "Class.method").</param>
/// <param name="Signature">The call signature, from the first line of the docstring.</param>
/// <param name="Summary">The rest of the docstring.</param>
public sealed record ModuleMemberDoc(string Module, string Name, string Signature, string Summary);
