namespace Calcy.Modes.Notebook.Model;

/// <summary>One named row of a table cell; it defines a list variable with its values (Tm = {1.81, 1.79, ...}).</summary>
public sealed class TableRow
{
    internal TableRow(string name, IEnumerable<string> values)
    {
        Name = name;
        Values = [.. values];
    }

    /// <summary>The variable name.</summary>
    public string Name { get; internal set; }

    /// <summary>The values as typed (empty strings are skipped).</summary>
    public List<string> Values { get; }

    /// <summary>The result of defining the list (null before the first calculation or when the row is empty).</summary>
    public LineResult? Result { get; internal set; }

    /// <summary>The calculator line this row stands for, or null when it has no name or no values.</summary>
    public string? Definition
    {
        get
        {
            var name = Name.Trim();
            var values = Values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
            if (name.Length == 0 || values.Count == 0) return null;
            return $"{name} = {{{string.Join(", ", values)}}}";
        }
    }
}

/// <summary>
/// A table cell: named rows of values across numbered columns (as on the design board), each row a list variable,
/// plus summary lines under the table (mean(Tm), for example).
/// </summary>
public sealed class TableCell : NotebookCell
{
    /// <summary>Most value columns a table can have.</summary>
    public const int MaxColumns = 60;

    /// <summary>Most rows a table can have.</summary>
    public const int MaxRows = 24;

    private readonly List<TableRow> _rows = [];
    private readonly LineList _summary;

    /// <summary>Creates a table with the given number of columns and one empty row named "data".</summary>
    public TableCell(int columns = 5, string? id = null) : base(id)
    {
        Columns = Math.Clamp(columns, 1, MaxColumns);
        _summary = new LineList(OnInputChanged, RaiseMetaChanged);
    }

    /// <inheritdoc />
    public override CellKind Kind => CellKind.Table;

    /// <summary>The number of value columns.</summary>
    public int Columns { get; private set; }

    /// <summary>The rows in order.</summary>
    public IReadOnlyList<TableRow> Rows => _rows;

    /// <summary>The summary lines.</summary>
    public IReadOnlyList<MathLine> Summary => _summary.Items;

    /// <summary>Appends a row.</summary>
    public TableRow AddRow(string name, IEnumerable<string>? values = null)
    {
        if (_rows.Count >= MaxRows) return _rows[^1];
        var list = (values ?? []).Take(MaxColumns).ToList();
        if (list.Count > Columns) Columns = list.Count;
        while (list.Count < Columns) list.Add("");
        var row = new TableRow(name, list);
        _rows.Add(row);
        OnInputChanged();
        return row;
    }

    /// <summary>Removes a row.</summary>
    public void RemoveRow(TableRow row)
    {
        if (_rows.Remove(row)) OnInputChanged();
    }

    /// <summary>Renames a row (its variable).</summary>
    public void SetName(TableRow row, string name)
    {
        name = (name ?? "").Trim();
        if (row.Name == name) return;
        row.Name = name;
        OnInputChanged();
    }

    /// <summary>Sets one value.</summary>
    public void SetValue(TableRow row, int column, string value)
    {
        if (column < 0 || column >= Columns) return;
        value = (value ?? "").Trim();
        if (row.Values[column] == value) return;
        row.Values[column] = value;
        OnInputChanged();
    }

    /// <summary>Adds an empty column at the end.</summary>
    public void AddColumn()
    {
        if (Columns >= MaxColumns) return;
        Columns++;
        foreach (var r in _rows) r.Values.Add("");
        OnInputChanged();
    }

    /// <summary>Removes the last column (at least one stays).</summary>
    public void RemoveLastColumn()
    {
        if (Columns <= 1) return;
        Columns--;
        foreach (var r in _rows) r.Values.RemoveAt(r.Values.Count - 1);
        OnInputChanged();
    }

    /// <summary>Appends a summary line.</summary>
    public MathLine AddSummary(string text = "") => _summary.Insert(_summary.Items.Count, new MathLine(text));

    /// <summary>Inserts a summary line.</summary>
    public MathLine InsertSummary(int index, string text = "") => _summary.Insert(index, new MathLine(text));

    /// <summary>Removes a summary line.</summary>
    public void RemoveSummary(MathLine line) => _summary.Remove(line, keepOne: false);

    /// <inheritdoc />
    public override NotebookCell Clone()
    {
        var c = new TableCell(Columns);
        foreach (var r in _rows) c._rows.Add(new TableRow(r.Name, r.Values));
        foreach (var l in Summary) c._summary.Add(l.Clone());
        return CopyBase(c);
    }

    internal override CellData ToData()
    {
        var d = BaseData();
        d.Columns = Columns;
        d.Rows = [.. _rows.Select(r => new TableRowData { Name = r.Name, Values = [.. r.Values] })];
        d.Summary = [.. Summary.Select(l => l.ToData())];
        return d;
    }

    internal static TableCell Restore(CellData d)
    {
        var c = new TableCell(Math.Max(1, d.Columns ?? 5), d.Id);
        foreach (var r in d.Rows ?? [])
        {
            var values = r.Values.Take(MaxColumns).ToList();
            if (values.Count > c.Columns) c.Columns = values.Count;
            c._rows.Add(new TableRow(r.Name ?? "", values));
        }
        foreach (var r in c._rows)
            while (r.Values.Count < c.Columns) r.Values.Add("");
        foreach (var l in d.Summary ?? []) c._summary.Add(MathLine.FromData(l));
        c.ReadBase(d);
        return c;
    }
}
