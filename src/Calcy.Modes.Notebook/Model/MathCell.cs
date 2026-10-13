namespace Calcy.Modes.Notebook.Model;

/// <summary>A maths cell: lines of maths evaluated in order; definitions are visible to every later line and cell.</summary>
public sealed class MathCell : NotebookCell
{
    private readonly LineList _lines;

    /// <summary>Creates a maths cell with the given lines (one empty line when none are given).</summary>
    public MathCell(IEnumerable<string>? lines = null, string? id = null) : base(id)
    {
        _lines = new LineList(OnInputChanged, RaiseMetaChanged);
        foreach (var t in lines ?? [""]) _lines.Add(new MathLine(t));
        if (_lines.Items.Count == 0) _lines.Add(new MathLine());
    }

    private MathCell(string? id, IEnumerable<MathLine> lines) : base(id)
    {
        _lines = new LineList(OnInputChanged, RaiseMetaChanged);
        foreach (var l in lines) _lines.Add(l);
        if (_lines.Items.Count == 0) _lines.Add(new MathLine());
    }

    /// <inheritdoc />
    public override CellKind Kind => CellKind.Maths;

    /// <summary>The lines in order.</summary>
    public IReadOnlyList<MathLine> Lines => _lines.Items;

    /// <summary>Inserts a line.</summary>
    public MathLine InsertLine(int index, string text = "") => _lines.Insert(index, new MathLine(text));

    /// <summary>Appends a line.</summary>
    public MathLine AddLine(string text = "") => _lines.Insert(_lines.Items.Count, new MathLine(text));

    /// <summary>Removes a line (the last line is kept, emptied).</summary>
    public void RemoveLine(MathLine line) => _lines.Remove(line);

    /// <inheritdoc />
    public override NotebookCell Clone() => CopyBase(new MathCell(null, Lines.Select(l => l.Clone())));

    internal override CellData ToData()
    {
        var d = BaseData();
        d.Lines = [.. Lines.Select(l => l.ToData())];
        return d;
    }

    internal static MathCell Restore(CellData d)
    {
        var c = new MathCell(d.Id, (d.Lines ?? []).Select(MathLine.FromData));
        c.ReadBase(d);
        return c;
    }
}

/// <summary>An ordered list of maths lines that reports text changes and display changes to its owner.</summary>
internal sealed class LineList(Action inputChanged, Action displayChanged)
{
    private readonly List<MathLine> _items = [];

    public IReadOnlyList<MathLine> Items => _items;

    public void Add(MathLine line)
    {
        Hook(line);
        _items.Add(line);
    }

    public MathLine Insert(int index, MathLine line)
    {
        Hook(line);
        _items.Insert(Math.Clamp(index, 0, _items.Count), line);
        inputChanged();
        return line;
    }

    public void Remove(MathLine line, bool keepOne = true)
    {
        int i = _items.IndexOf(line);
        if (i < 0) return;
        if (keepOne && _items.Count == 1)
        {
            line.Text = "";
            return;
        }
        line.TextChanged -= OnText;
        line.DisplayChanged -= OnDisplay;
        _items.RemoveAt(i);
        inputChanged();
    }

    private void Hook(MathLine line)
    {
        line.TextChanged += OnText;
        line.DisplayChanged += OnDisplay;
    }

    private void OnText(object? sender, EventArgs e) => inputChanged();

    private void OnDisplay(object? sender, EventArgs e) => displayChanged();
}
