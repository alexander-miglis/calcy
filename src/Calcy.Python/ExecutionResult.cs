namespace Calcy.Python;

/// <summary>How a run ended.</summary>
public enum ExecutionStatus
{
    /// <summary>The code ran to completion.</summary>
    Completed,

    /// <summary>An uncaught Python exception (including SyntaxError) ended the run.</summary>
    Error,

    /// <summary>The code called sys.exit() or exit(); see <see cref="ExecutionResult.ExitCode"/>.</summary>
    Exited,

    /// <summary>The cancellation token was signalled.</summary>
    Cancelled,

    /// <summary>The wall clock limit was exceeded.</summary>
    TimedOut,

    /// <summary>The step limit was exceeded.</summary>
    StepLimitExceeded,
}

/// <summary>One entry of a Python traceback, outermost call first.</summary>
public sealed record PythonStackFrame(string FileName, int Line, string FunctionName, string? SourceLine);

/// <summary>Details of an error that ended a run.</summary>
public sealed class PythonError
{
    /// <summary>The Python exception type name, for example <c>ZeroDivisionError</c>.</summary>
    public required string TypeName { get; init; }

    /// <summary>The exception message (str of the exception).</summary>
    public required string Message { get; init; }

    /// <summary>File of the innermost frame, or of the syntax error.</summary>
    public string? FileName { get; init; }

    /// <summary>1 based line of the innermost frame, or of the syntax error.</summary>
    public int? Line { get; init; }

    /// <summary>1 based column for syntax errors.</summary>
    public int? Column { get; init; }

    /// <summary>The source line at <see cref="Line"/>.</summary>
    public string? SourceLine { get; init; }

    /// <summary>Call stack, outermost first.</summary>
    public IReadOnlyList<PythonStackFrame> StackTrace { get; init; } = Array.Empty<PythonStackFrame>();

    /// <summary>The traceback formatted like CPython.</summary>
    public required string Traceback { get; init; }

    /// <summary>True for SyntaxError and IndentationError.</summary>
    public bool IsSyntaxError { get; init; }

    /// <inheritdoc />
    public override string ToString() => Traceback;
}

/// <summary>The outcome of running Python code.</summary>
public sealed class ExecutionResult
{
    /// <summary>How the run ended.</summary>
    public required ExecutionStatus Status { get; init; }

    /// <summary>True when the run completed, or exited with code 0.</summary>
    public bool Success => Status == ExecutionStatus.Completed || (Status == ExecutionStatus.Exited && ExitCode == 0);

    /// <summary>Error details for failed, cancelled and limited runs.</summary>
    public PythonError? Error { get; init; }

    /// <summary>Statements executed.</summary>
    public long Steps { get; init; }

    /// <summary>Wall clock time of the run.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Result of <see cref="PythonEngine.Evaluate"/> or <see cref="PythonEngine.Call"/>, or the last echoed value in interactive mode, converted with <see cref="PyConvert.ToClr"/>.</summary>
    public object? Value { get; init; }

    /// <summary>repr() of <see cref="Value"/>, or null when there is no value.</summary>
    public string? ValueRepr { get; init; }

    /// <summary>Exit code for <see cref="ExecutionStatus.Exited"/>.</summary>
    public int? ExitCode { get; init; }

    /// <inheritdoc />
    public override string ToString() => Error?.Traceback ?? Status.ToString();
}
