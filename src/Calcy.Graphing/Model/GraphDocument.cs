using Calcy.Core;

namespace Calcy.Graphing;

/// <summary>Serializable state of a graph: the expression list, sliders and window (for preferences and files).</summary>
public sealed class GraphState
{
    /// <summary>The expression rows.</summary>
    public List<GraphItemState> Items { get; set; } = [];

    /// <summary>Slider settings.</summary>
    public List<GraphParameterState> Parameters { get; set; } = [];

    /// <summary>Window x minimum.</summary>
    public double XMin { get; set; } = -10;

    /// <summary>Window x maximum.</summary>
    public double XMax { get; set; } = 10;

    /// <summary>Window y minimum.</summary>
    public double YMin { get; set; } = -10;

    /// <summary>Window y maximum.</summary>
    public double YMax { get; set; } = 10;

    /// <summary>π-based ticks on the x-axis.</summary>
    public bool PiTicks { get; set; }

    /// <summary>The selected view (Graph, Table or Split) for screens that have one.</summary>
    public string? View { get; set; }

    /// <summary>Table start value.</summary>
    public double TableStart { get; set; } = -3;

    /// <summary>Table step.</summary>
    public double TableStep { get; set; } = 0.5;

    /// <summary>The window as a viewport.</summary>
    public Viewport Viewport => new Viewport(XMin, XMax, YMin, YMax).Normalized();
}

/// <summary>One saved expression row.</summary>
public sealed class GraphItemState
{
    /// <summary>The text.</summary>
    public string Text { get; set; } = "";

    /// <summary>Shown or hidden.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Colour slot.</summary>
    public int Color { get; set; }

    /// <summary>Custom t or θ range start.</summary>
    public double? TMin { get; set; }

    /// <summary>Custom t or θ range end.</summary>
    public double? TMax { get; set; }
}

/// <summary>One saved slider.</summary>
public sealed class GraphParameterState
{
    /// <summary>Parameter name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Value.</summary>
    public double Value { get; set; } = 1;

    /// <summary>Minimum.</summary>
    public double Min { get; set; } = -5;

    /// <summary>Maximum.</summary>
    public double Max { get; set; } = 5;

    /// <summary>Step.</summary>
    public double Step { get; set; } = 0.1;
}

/// <summary>
/// An editable list of graph expressions sharing one set of sliders, parsed against a calculator session.
/// Used by the Graphing screen, the MCP server and tests: add text, read classified items, sample or analyse them.
/// </summary>
public sealed class GraphDocument
{
    private readonly List<GraphItem> _items = [];
    private int _nextColor;
    private bool _reparsing;

    /// <summary>Creates an empty document on a session.</summary>
    public GraphDocument(CalcSession session)
    {
        Session = session;
        Parameters = new GraphParameters();
    }

    /// <summary>The session used for parsing, variables and exact values.</summary>
    public CalcSession Session { get; }

    /// <summary>The sliders.</summary>
    public GraphParameters Parameters { get; }

    /// <summary>The rows in order.</summary>
    public IReadOnlyList<GraphItem> Items => _items;

    /// <summary>Raised after rows are added, edited, removed or re-parsed.</summary>
    public event EventHandler? Changed;

    /// <summary>Parameter values for an item (a snapshot aligned with its parameter list).</summary>
    public double[] ParameterValues(GraphItem item) => Parameters.Vector(item.Parameters);

    /// <summary>A <see cref="ParameterSource"/> reading this document's sliders.</summary>
    public ParameterSource ParameterSource => ParameterValues;

    /// <summary>Appends a row.</summary>
    public GraphItem Add(string text) => Insert(_items.Count, text);

    /// <summary>Inserts a row at <paramref name="index"/>.</summary>
    public GraphItem Insert(int index, string text)
    {
        var item = GraphParser.Parse(text, Session);
        item.ColorIndex = _nextColor++ % 5;
        _items.Insert(Math.Clamp(index, 0, _items.Count), item);
        AfterChange(item.DefinedFunction is not null);
        return _items[Math.Clamp(index, 0, _items.Count - 1)];
    }

    /// <summary>Replaces the text of a row, keeping its colour, visibility and custom range.</summary>
    public GraphItem Replace(int index, string text)
    {
        var old = _items[index];
        if (old.Text == text) return old;
        bool hadDefinition = RemoveDefinition(old, text);
        var item = GraphParser.Parse(text, Session);
        Carry(old, item);
        _items[index] = item;
        AfterChange(hadDefinition || item.DefinedFunction is not null);
        return _items[index];
    }

    /// <summary>Removes a row (and the function it defined, if nothing else redefined it).</summary>
    public void RemoveAt(int index)
    {
        var old = _items[index];
        _items.RemoveAt(index);
        bool had = RemoveDefinition(old, null);
        AfterChange(had);
    }

    /// <summary>Moves a row.</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || from >= _items.Count) return;
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(Math.Clamp(to, 0, _items.Count), item);
        AfterChange(false);
    }

    /// <summary>Removes every row.</summary>
    public void Clear()
    {
        foreach (var item in _items) RemoveDefinition(item, null);
        _items.Clear();
        _nextColor = 0;
        AfterChange(false);
    }

    /// <summary>Re-parses every row (after session variables, functions or settings change). Keeps display settings.</summary>
    public void Reparse()
    {
        if (_reparsing) return;
        _reparsing = true;
        try
        {
            // Definitions first, so rows that use them see them whatever the order.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var old = _items[i];
                    var item = GraphParser.Parse(old.Text, Session);
                    Carry(old, item);
                    _items[i] = item;
                }
                if (!_items.Any(it => it.DefinedFunction is not null)) break;
            }
        }
        finally
        {
            _reparsing = false;
        }
        AfterChange(false);
    }

    /// <summary>The index of the first row that uses a parameter (its slider is shown under that row), or -1.</summary>
    public int ParameterOwner(string name)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Kind == GraphKind.Variable && _items[i].Label == name) return i;
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Parameters.Contains(name)) return i;
        return -1;
    }

    /// <summary>Saves the rows and sliders (and the given window).</summary>
    public GraphState Save(Viewport viewport, bool piTicks = false) => new()
    {
        Items = [.. _items.Select(i => new GraphItemState
        {
            Text = i.Text,
            Visible = i.IsVisible,
            Color = i.ColorIndex,
            TMin = i.HasRange && !i.RangeFromText ? i.TMin : null,
            TMax = i.HasRange && !i.RangeFromText ? i.TMax : null,
        })],
        Parameters = [.. Parameters.All.Select(p => new GraphParameterState { Name = p.Name, Value = p.Value, Min = p.Min, Max = p.Max, Step = p.Step })],
        XMin = viewport.XMin,
        XMax = viewport.XMax,
        YMin = viewport.YMin,
        YMax = viewport.YMax,
        PiTicks = piTicks,
    };

    /// <summary>Replaces the rows and sliders with saved state.</summary>
    public void Load(GraphState state)
    {
        _items.Clear();
        foreach (var p in state.Parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            var gp = Parameters.GetOrAdd(p.Name, p.Value);
            gp.Min = p.Min;
            gp.Max = p.Max;
            gp.Step = p.Step > 0 ? p.Step : 0.1;
            gp.Value = p.Value;
        }
        foreach (var s in state.Items)
        {
            var item = GraphParser.Parse(s.Text, Session);
            item.ColorIndex = ((s.Color % 5) + 5) % 5;
            item.IsVisible = s.Visible;
            if (item.HasRange && !item.RangeFromText && s.TMin is double a && s.TMax is double b && b > a)
            {
                item.TMin = a;
                item.TMax = b;
            }
            _items.Add(item);
        }
        _nextColor = _items.Count == 0 ? 0 : _items.Max(i => i.ColorIndex) + 1;
        Reparse();
    }

    private static void Carry(GraphItem old, GraphItem item)
    {
        item.ColorIndex = old.ColorIndex;
        item.IsVisible = old.IsVisible;
        if (old.HasRange && item.HasRange && !item.RangeFromText && !old.RangeFromText)
        {
            item.TMin = old.TMin;
            item.TMax = old.TMax;
        }
    }

    private bool RemoveDefinition(GraphItem old, string? newText)
    {
        if (old.DefinedFunction is not { } f) return false;
        if (_items.Any(i => i != old && i.DefinedFunction?.Name == f.Name)) return true;
        if (Session.Env.TryGetFunction(f.Name, out var current) && current.Body.Equals(f.Body) && current.Parameters.SequenceEqual(f.Parameters))
        {
            // The new text may define the same name again; Parse will set it.
            if (newText is null || !newText.TrimStart().StartsWith(f.Name, StringComparison.Ordinal)) Session.Env.Remove(f.Name);
        }
        return true;
    }

    private void AfterChange(bool definitionsChanged)
    {
        if (definitionsChanged && !_reparsing)
        {
            Reparse();
            return;
        }
        Rename();
        SyncParameters();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rename()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            it.Name = it.Label ?? $"y{i + 1}";
        }
    }

    private void SyncParameters()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in _items)
        {
            if (item.Kind == GraphKind.Variable && item.Label is { } name && item.VariableValue is double v)
            {
                var p = Parameters.GetOrAdd(name, v);
                if (Math.Abs(p.Value - v) > 1e-12)
                {
                    if (v < p.Min) p.Min = v;
                    if (v > p.Max) p.Max = v;
                    p.Value = v;
                }
                used.Add(name);
            }
            foreach (var n in item.Parameters)
            {
                Parameters.GetOrAdd(n);
                used.Add(n);
            }
        }
        Parameters.RemoveUnused(used);
    }
}
