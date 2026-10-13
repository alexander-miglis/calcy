namespace Calcy.Modes.Notebook.Model;

/// <summary>What changed in a notebook.</summary>
public enum NotebookChangeKind
{
    /// <summary>Cells were added, removed or moved.</summary>
    Structure,

    /// <summary>A cell's input changed (later cells need recalculating).</summary>
    Input,

    /// <summary>A name, caption, folding or display option changed (only saving is needed).</summary>
    Meta,
}

/// <summary>Describes a notebook change.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="Cell">The cell concerned, if any.</param>
/// <param name="Index">The first index affected (recalculation starts there).</param>
public sealed record NotebookChangedEventArgs(NotebookChangeKind Kind, NotebookCell? Cell, int Index);

/// <summary>A notebook: a name and cells in order, with undo for adding, removing and moving cells.</summary>
public sealed class NotebookDocument
{
    private const int MaxUndo = 60;

    private readonly List<NotebookCell> _cells = [];
    private readonly List<UndoStep> _undo = [];
    private readonly List<UndoStep> _redo = [];
    private string _name;

    private sealed record UndoStep(string Label, Action Undo, Action Redo);

    /// <summary>Creates an empty notebook.</summary>
    public NotebookDocument(string name = "Untitled notebook", string? id = null)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "Untitled notebook" : name.Trim();
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..12] : id;
        Modified = DateTime.UtcNow;
    }

    /// <summary>Creates a notebook with cells (no undo history).</summary>
    public NotebookDocument(string name, IEnumerable<NotebookCell> cells) : this(name)
    {
        foreach (var c in cells) InsertCore(_cells.Count, c);
    }

    /// <summary>Stable id.</summary>
    public string Id { get; }

    /// <summary>The name shown in the notebook list.</summary>
    public string Name
    {
        get => _name;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? _name : value.Trim();
            if (v == _name) return;
            _name = v;
            Raise(NotebookChangeKind.Meta, null, 0);
        }
    }

    /// <summary>When it last changed (UTC).</summary>
    public DateTime Modified { get; private set; }

    /// <summary>The cells in order.</summary>
    public IReadOnlyList<NotebookCell> Cells => _cells;

    /// <summary>Raised after every change.</summary>
    public event EventHandler<NotebookChangedEventArgs>? Changed;

    /// <summary>True when there is a change to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there is an undone change to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>What Undo would undo ("delete maths cell"), or null.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    /// <summary>The position of a cell, or -1.</summary>
    public int IndexOf(NotebookCell cell) => _cells.IndexOf(cell);

    /// <summary>Appends a cell.</summary>
    public T Add<T>(T cell) where T : NotebookCell => Insert(_cells.Count, cell);

    /// <summary>Inserts a cell (undoable).</summary>
    public T Insert<T>(int index, T cell) where T : NotebookCell
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_cells.Contains(cell)) throw new InvalidOperationException("The cell is already in the notebook.");
        index = Math.Clamp(index, 0, _cells.Count);
        InsertCore(index, cell);
        Push($"add {NotebookCell.KindName(cell.Kind).ToLowerInvariant()} cell", () => RemoveCore(cell), () => InsertCore(index, cell));
        return cell;
    }

    /// <summary>Removes a cell (undoable).</summary>
    public bool Remove(NotebookCell cell)
    {
        int index = _cells.IndexOf(cell);
        if (index < 0) return false;
        RemoveCore(cell);
        Push($"delete {NotebookCell.KindName(cell.Kind).ToLowerInvariant()} cell", () => InsertCore(index, cell), () => RemoveCore(cell));
        return true;
    }

    /// <summary>Moves a cell to a new position (undoable). Returns false when it does not move.</summary>
    public bool Move(NotebookCell cell, int newIndex)
    {
        int from = _cells.IndexOf(cell);
        newIndex = Math.Clamp(newIndex, 0, _cells.Count - 1);
        if (from < 0 || from == newIndex) return false;
        MoveCore(cell, newIndex);
        Push($"move {NotebookCell.KindName(cell.Kind).ToLowerInvariant()} cell", () => MoveCore(cell, from), () => MoveCore(cell, newIndex));
        return true;
    }

    /// <summary>Inserts a copy of a cell right below it (undoable).</summary>
    public NotebookCell Duplicate(NotebookCell cell)
    {
        int index = _cells.IndexOf(cell);
        if (index < 0) throw new InvalidOperationException("The cell is not in the notebook.");
        var copy = cell.Clone();
        InsertCore(index + 1, copy);
        Push($"duplicate {NotebookCell.KindName(cell.Kind).ToLowerInvariant()} cell", () => RemoveCore(copy), () => InsertCore(index + 1, copy));
        return copy;
    }

    /// <summary>Undoes the last structural change. Returns false when there is none.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        step.Undo();
        _redo.Add(step);
        return true;
    }

    /// <summary>Redoes the last undone change. Returns false when there is none.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        step.Redo();
        _undo.Add(step);
        return true;
    }

    /// <summary>A deep copy with new ids (results are not copied).</summary>
    public NotebookDocument Clone(string name)
    {
        var copy = new NotebookDocument(name);
        foreach (var c in _cells) copy.InsertCore(copy._cells.Count, c.Clone());
        return copy;
    }

    /// <summary>Saved form.</summary>
    public NotebookData ToData() => new()
    {
        Id = Id,
        Name = _name,
        Modified = Modified,
        Cells = [.. _cells.Select(c => c.ToData())],
    };

    /// <summary>Restores a notebook from saved form.</summary>
    public static NotebookDocument FromData(NotebookData d)
    {
        var doc = new NotebookDocument(d.Name, d.Id);
        foreach (var c in d.Cells ?? []) doc.InsertCore(doc._cells.Count, NotebookCell.FromData(c));
        doc.Modified = d.Modified == default ? DateTime.UtcNow : d.Modified;
        return doc;
    }

    private void Push(string label, Action undo, Action redo)
    {
        _undo.Add(new UndoStep(label, undo, redo));
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void InsertCore(int index, NotebookCell cell)
    {
        index = Math.Clamp(index, 0, _cells.Count);
        _cells.Insert(index, cell);
        cell.InputChanged += OnCellInput;
        cell.MetaChanged += OnCellMeta;
        Raise(NotebookChangeKind.Structure, cell, index);
    }

    private void RemoveCore(NotebookCell cell)
    {
        int index = _cells.IndexOf(cell);
        if (index < 0) return;
        _cells.RemoveAt(index);
        cell.InputChanged -= OnCellInput;
        cell.MetaChanged -= OnCellMeta;
        Raise(NotebookChangeKind.Structure, cell, index);
    }

    private void MoveCore(NotebookCell cell, int to)
    {
        int from = _cells.IndexOf(cell);
        if (from < 0) return;
        _cells.RemoveAt(from);
        to = Math.Clamp(to, 0, _cells.Count);
        _cells.Insert(to, cell);
        Raise(NotebookChangeKind.Structure, cell, Math.Min(from, to));
    }

    private void OnCellInput(object? sender, EventArgs e)
    {
        if (sender is NotebookCell c) Raise(NotebookChangeKind.Input, c, _cells.IndexOf(c));
    }

    private void OnCellMeta(object? sender, EventArgs e)
    {
        if (sender is NotebookCell c) Raise(NotebookChangeKind.Meta, c, _cells.IndexOf(c));
    }

    private void Raise(NotebookChangeKind kind, NotebookCell? cell, int index)
    {
        Modified = DateTime.UtcNow;
        Changed?.Invoke(this, new NotebookChangedEventArgs(kind, cell, index));
    }
}
