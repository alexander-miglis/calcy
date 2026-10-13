using Calcy.Modes.Notebook.Runtime;
using Calcy.Python;

namespace Calcy.Modes.Notebook.Model;

/// <summary>
/// A code cell: Python run by the notebook's one sandboxed interpreter, so variables carry from cell to cell, and
/// calc.get / calc.set read and write the notebook's maths variables. Code only runs when asked (RUN, Shift+Enter,
/// RUN ALL), never when a notebook is opened.
/// </summary>
public sealed class CodeCell : NotebookCell
{
    private string _code;

    /// <summary>Creates a code cell.</summary>
    public CodeCell(string code = "", string? id = null) : base(id)
    {
        _code = code ?? "";
        State = CellState.NotRun;
    }

    /// <inheritdoc />
    public override CellKind Kind => CellKind.Code;

    /// <summary>The Python source.</summary>
    public string Code
    {
        get => _code;
        set
        {
            value ??= "";
            if (value == _code) return;
            _code = value;
            OnInputChanged();
        }
    }

    /// <summary>The output of the last run (or of the run in progress).</summary>
    public CodeOutput Output { get; } = new();

    /// <summary>The console text of a run from an earlier session (shown until the cell runs again).</summary>
    public string? PreviousOutput { get; internal set; }

    /// <summary>True when the earlier session's run failed.</summary>
    public bool PreviousFailed { get; internal set; }

    /// <summary>True while the cell runs.</summary>
    public bool IsRunning { get; internal set; }

    /// <summary>How the last run ended (null before the first run in this session).</summary>
    public ExecutionStatus? LastStatus => LastRun?.Status;

    /// <summary>Wall clock time of the last run.</summary>
    public TimeSpan? LastElapsed => LastRun?.Elapsed;

    /// <summary>A short description of the last run for the header ("ran in 4 ms").</summary>
    public string? RunSummary { get; internal set; }

    /// <summary>The record of the last run (what it read and wrote), used to decide when it is stale.</summary>
    internal CodeRunRecord? LastRun { get; set; }

    /// <inheritdoc />
    public override NotebookCell Clone() => CopyBase(new CodeCell(_code));

    internal override CellData ToData()
    {
        var d = BaseData();
        d.Code = _code;
        string? text = LastRun is not null ? Output.PlainText() : PreviousOutput;
        if (!string.IsNullOrEmpty(text) && text.Length <= 4000)
        {
            d.Output = text;
            d.OutputFailed = LastRun is { } r ? !r.Success : PreviousFailed;
        }
        return d;
    }

    internal static CodeCell Restore(CellData d)
    {
        var c = new CodeCell(d.Code ?? "", d.Id)
        {
            PreviousOutput = string.IsNullOrEmpty(d.Output) ? null : d.Output,
            PreviousFailed = d.OutputFailed,
        };
        c.ReadBase(d);
        return c;
    }
}
