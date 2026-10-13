using Calcy.Core;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>
/// One list of the editor (L1, L2, ...): the typed cell texts, an optional caption shown after the name ("hours") and an
/// optional live column formula (L3 = L2 / L1) whose values are recomputed whenever the lists it reads change.
/// </summary>
public sealed class StatColumn
{
    private readonly List<string> _cells = [];
    private readonly List<CellValue?> _parsed = [];

    /// <summary>Creates a column with the given name and cell texts.</summary>
    public StatColumn(string name, IEnumerable<string>? cells = null)
    {
        Name = name;
        if (cells is not null) foreach (var c in cells) Append(c);
        Trim();
    }

    /// <summary>The list name, which is also the session variable (L1, or a name such as hours).</summary>
    public string Name { get; internal set; }

    /// <summary>A free caption shown after the name in the header, for example "hours".</summary>
    public string Caption { get; set; } = "";

    /// <summary>The right-hand side of a live column formula (for example "L2 / L1"), or null for typed data.</summary>
    public string? Formula { get; internal set; }

    /// <summary>Why the formula could not be computed, or null.</summary>
    public string? FormulaError { get; internal set; }

    /// <summary>The cell texts (no trailing empty cells).</summary>
    public IReadOnlyList<string> Cells => _cells;

    /// <summary>Number of cells up to the last one that is not empty.</summary>
    public int Length => _cells.Count;

    /// <summary>The header text: the name and the caption.</summary>
    public string Header => Caption.Length == 0 ? Name : Name + " " + Caption;

    /// <summary>The text of a cell ("" beyond the end).</summary>
    public string this[int row] => row >= 0 && row < _cells.Count ? _cells[row] : "";

    /// <summary>The parsed value of a cell (cached until the cell changes).</summary>
    public CellValue Value(int row, CalcSession? session)
    {
        if (row < 0 || row >= _cells.Count) return CellValue.Empty;
        if (_parsed[row] is { } v) return v;
        var parsed = CellParser.Parse(_cells[row], session);
        _parsed[row] = parsed;
        return parsed;
    }

    /// <summary>All parsed values.</summary>
    public CellValue[] Values(CalcSession? session)
    {
        var r = new CellValue[_cells.Count];
        for (int i = 0; i < r.Length; i++) r[i] = Value(i, session);
        return r;
    }

    /// <summary>True when no cell has anything in it.</summary>
    public bool IsEmpty => _cells.Count == 0;

    /// <summary>Sets a cell, growing the list with empty cells when needed. Returns true when the text changed.</summary>
    internal bool Set(int row, string? text)
    {
        if (row < 0) return false;
        var t = text ?? "";
        if (row >= _cells.Count && t.Trim().Length == 0) return false;
        while (_cells.Count <= row) Append("");
        if (_cells[row] == t) return false;
        _cells[row] = t;
        _parsed[row] = null;
        Trim();
        return true;
    }

    /// <summary>Replaces every cell.</summary>
    internal void SetAll(IEnumerable<string> cells)
    {
        _cells.Clear();
        _parsed.Clear();
        foreach (var c in cells) Append(c);
        Trim();
    }

    /// <summary>Inserts an empty cell at a row (later cells move down).</summary>
    internal void Insert(int row)
    {
        if (row < 0 || row >= _cells.Count) return;
        _cells.Insert(row, "");
        _parsed.Insert(row, CellValue.Empty);
    }

    /// <summary>Removes a cell (later cells move up).</summary>
    internal void RemoveAt(int row)
    {
        if (row < 0 || row >= _cells.Count) return;
        _cells.RemoveAt(row);
        _parsed.RemoveAt(row);
        Trim();
    }

    /// <summary>Forgets cached parses (after variables that expressions use have changed).</summary>
    internal void InvalidateParses()
    {
        for (int i = 0; i < _parsed.Count; i++) _parsed[i] = null;
    }

    private void Append(string text)
    {
        _cells.Add(text);
        _parsed.Add(null);
    }

    private void Trim()
    {
        while (_cells.Count > 0 && _cells[^1].Trim().Length == 0)
        {
            _cells.RemoveAt(_cells.Count - 1);
            _parsed.RemoveAt(_parsed.Count - 1);
        }
    }
}
