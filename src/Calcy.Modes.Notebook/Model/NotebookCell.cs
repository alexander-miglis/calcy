using Calcy.Modes.Notebook.Runtime;

namespace Calcy.Modes.Notebook.Model;

/// <summary>
/// One cell of a notebook. Inputs (text, maths lines, table data, graph rows, code) live here; results are filled in by
/// <see cref="NotebookRuntime"/>. Events are raised on the thread that changes the cell (the UI thread in the app).
/// </summary>
public abstract class NotebookCell
{
    private string? _caption;
    private bool _collapsed;

    /// <summary>Creates a cell with a new id, or the given one when restoring a saved notebook.</summary>
    protected NotebookCell(string? id)
    {
        Id = string.IsNullOrWhiteSpace(id) ? NewId() : id;
    }

    /// <summary>A stable id (used for outline links and to match saved state).</summary>
    public string Id { get; }

    /// <summary>The kind of cell.</summary>
    public abstract CellKind Kind { get; }

    /// <summary>A short name shown in the cell header and the outline (null for none).</summary>
    public string? Caption
    {
        get => _caption;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (v == _caption) return;
            _caption = v;
            RaiseMetaChanged();
        }
    }

    /// <summary>True when the cell's output is folded away.</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (value == _collapsed) return;
            _collapsed = value;
            RaiseMetaChanged();
        }
    }

    /// <summary>Increases with every input change (results computed for an older version are not applied).</summary>
    public int Version { get; private set; }

    /// <summary>Where the results stand.</summary>
    public CellState State { get; internal set; } = CellState.Calculating;

    /// <summary>A short explanation of <see cref="State"/> (for example why the cell is stale).</summary>
    public string? StateMessage { get; internal set; }

    /// <summary>Raised when the input changes, so later cells have to be recalculated.</summary>
    public event EventHandler? InputChanged;

    /// <summary>Raised when the caption, the folding or a display option changes (nothing to recalculate).</summary>
    public event EventHandler? MetaChanged;

    /// <summary>Raised when the runtime has new results or a new state for the cell.</summary>
    public event EventHandler? ResultsChanged;

    /// <summary>The variables and functions just before this cell, from the last recalculation.</summary>
    internal EnvState? EnvBefore { get; set; }

    /// <summary>A deep copy with a new id (for duplicating).</summary>
    public abstract NotebookCell Clone();

    /// <summary>Marks the input as changed.</summary>
    protected void OnInputChanged()
    {
        Version++;
        if (Kind != CellKind.Code) State = CellState.Calculating;
        InputChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="MetaChanged"/>.</summary>
    protected void RaiseMetaChanged() => MetaChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="ResultsChanged"/>.</summary>
    internal void RaiseResultsChanged() => ResultsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Writes the cell into its saved form.</summary>
    internal abstract CellData ToData();

    /// <summary>Copies the shared fields into saved form.</summary>
    protected CellData BaseData() => new() { Id = Id, Kind = Kind, Caption = Caption, Collapsed = Collapsed };

    /// <summary>Copies caption and folding from saved form.</summary>
    protected void ReadBase(CellData d)
    {
        _caption = string.IsNullOrWhiteSpace(d.Caption) ? null : d.Caption.Trim();
        _collapsed = d.Collapsed;
    }

    /// <summary>Copies caption and folding from another cell (for cloning).</summary>
    protected T CopyBase<T>(T clone) where T : NotebookCell
    {
        clone._caption = _caption;
        clone._collapsed = _collapsed;
        return clone;
    }

    /// <summary>Restores a cell from saved form.</summary>
    internal static NotebookCell FromData(CellData d) => d.Kind switch
    {
        CellKind.Text => TextCell.Restore(d),
        CellKind.Maths => MathCell.Restore(d),
        CellKind.Table => TableCell.Restore(d),
        CellKind.Graph => GraphCell.Restore(d),
        CellKind.Code => CodeCell.Restore(d),
        _ => TextCell.Restore(d),
    };

    /// <summary>The label used in headers and menus ("Maths", "Code" ...).</summary>
    public static string KindName(CellKind kind) => kind switch
    {
        CellKind.Text => "Text",
        CellKind.Maths => "Maths",
        CellKind.Table => "Table",
        CellKind.Graph => "Graph",
        _ => "Code",
    };

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
