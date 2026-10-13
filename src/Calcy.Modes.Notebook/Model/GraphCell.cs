using Calcy.Core;
using Calcy.Graphing;
using Calcy.Modes.Notebook.Runtime;

namespace Calcy.Modes.Notebook.Model;

/// <summary>
/// A graph cell: its own expression rows and sliders over the variables of the cells above. Slider values are also
/// defined for the cells below, so a slider can drive later results. The live <see cref="Document"/> exists once the
/// cell belongs to a <see cref="NotebookRuntime"/>.
/// </summary>
public sealed class GraphCell : NotebookCell
{
    /// <summary>Default graph height in pixels.</summary>
    public const double DefaultHeight = 260;

    private readonly GraphState _initial;
    private GraphDocument? _doc;
    private Viewport _viewport;
    private double _height = DefaultHeight;
    private bool _applying;

    /// <summary>Creates a graph cell with the given expressions and window (the standard window by default).</summary>
    public GraphCell(IEnumerable<string>? expressions = null, Viewport? window = null, string? id = null) : base(id)
    {
        var v = (window ?? new Viewport(-10, 10, -6, 6)).Normalized();
        _initial = new GraphState
        {
            Items = [.. (expressions ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select((t, i) => new GraphItemState { Text = t.Trim(), Color = i % 5 })],
            XMin = v.XMin,
            XMax = v.XMax,
            YMin = v.YMin,
            YMax = v.YMax,
        };
        _viewport = v;
    }

    private GraphCell(GraphState state, double height, string? id) : base(id)
    {
        _initial = state;
        _viewport = state.Viewport.IsValid ? state.Viewport : new Viewport(-10, 10, -6, 6);
        _height = Math.Clamp(height, 120, 900);
    }

    /// <inheritdoc />
    public override CellKind Kind => CellKind.Graph;

    /// <summary>The live expression list and sliders (null until the cell is attached to a runtime).</summary>
    public GraphDocument? Document => _doc;

    /// <summary>The cell's private session: the variables of the cells above, without the names of its own sliders.</summary>
    public CalcSession? CellSession => _doc?.Session;

    /// <summary>The graph window.</summary>
    public Viewport Viewport
    {
        get => _viewport;
        set
        {
            value = value.Normalized();
            if (!value.IsValid || value == _viewport) return;
            _viewport = value;
            RaiseMetaChanged();
        }
    }

    /// <summary>Height of the graph in pixels.</summary>
    public double Height
    {
        get => _height;
        set
        {
            value = Math.Clamp(value, 120, 900);
            if (Math.Abs(value - _height) < 0.5) return;
            _height = value;
            RaiseMetaChanged();
        }
    }

    /// <summary>The expression texts in order.</summary>
    public IReadOnlyList<string> Expressions => _doc is { } d ? [.. d.Items.Select(i => i.Text)] : [.. _initial.Items.Select(i => i.Text)];

    /// <summary>The slider names and values (every slider is visible to later cells as a variable).</summary>
    public IReadOnlyList<(string Name, double Value)> Sliders => _doc is { } d
        ? [.. d.Parameters.All.Select(p => (p.Name, p.Value))]
        : [.. _initial.Parameters.Select(p => (p.Name, p.Value))];

    /// <summary>Names given a value by a slider row of this graph (a = 1.5); they hide definitions from cells above.</summary>
    public IReadOnlyList<string> OwnSliderNames => _doc is { } d
        ? [.. d.Items.Where(i => i.Kind == GraphKind.Variable && i.Label is not null).Select(i => i.Label!)]
        : [];

    /// <summary>Appends an expression row.</summary>
    public void AddExpression(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_doc is { } d) d.Add(text.Trim());
        else
        {
            _initial.Items.Add(new GraphItemState { Text = text.Trim(), Color = _initial.Items.Count % 5 });
            OnInputChanged();
        }
    }

    /// <summary>Replaces the text of a row.</summary>
    public void ReplaceExpression(int index, string text)
    {
        if (_doc is { } d)
        {
            if (index >= 0 && index < d.Items.Count) d.Replace(index, text ?? "");
        }
        else if (index >= 0 && index < _initial.Items.Count)
        {
            _initial.Items[index].Text = text ?? "";
            OnInputChanged();
        }
    }

    /// <summary>Removes a row.</summary>
    public void RemoveExpression(int index)
    {
        if (_doc is { } d)
        {
            if (index >= 0 && index < d.Items.Count) d.RemoveAt(index);
        }
        else if (index >= 0 && index < _initial.Items.Count)
        {
            _initial.Items.RemoveAt(index);
            OnInputChanged();
        }
    }

    /// <summary>Sets a slider (a parameter of the graph).</summary>
    public void SetSlider(string name, double value)
    {
        if (_doc?.Parameters[name] is { } p) p.Value = p.Snap(value);
    }

    /// <summary>Creates the live document on the cell's private session (called by the runtime).</summary>
    internal void Bind(CalcSession cellSession)
    {
        if (_doc is not null) return;
        _doc = new GraphDocument(cellSession);
        _applying = true;
        try
        {
            _doc.Load(_initial);
        }
        finally
        {
            _applying = false;
        }
        _doc.Changed += (_, _) =>
        {
            if (!_applying) OnInputChanged();
        };
        _doc.Parameters.ValuesChanged += (_, _) =>
        {
            if (!_applying) OnInputChanged();
        };
    }

    /// <summary>
    /// Gives the graph the variables of the cells above and re-reads its rows. Returns true when its set of sliders
    /// changed (then the cells below need another pass).
    /// </summary>
    internal bool ApplyEnvironment(EnvState env)
    {
        if (_doc is null) return false;
        var before = string.Join(",", _doc.Parameters.All.Select(p => p.Name));
        _applying = true;
        try
        {
            env.RestoreInto(_doc.Session.Env);
            _doc.Reparse();
        }
        finally
        {
            _applying = false;
        }
        return before != string.Join(",", _doc.Parameters.All.Select(p => p.Name));
    }

    /// <summary>The saved state (rows, sliders, window).</summary>
    public GraphState SaveState()
    {
        if (_doc is { } d) return d.Save(_viewport);
        var s = Copy(_initial);
        s.XMin = _viewport.XMin;
        s.XMax = _viewport.XMax;
        s.YMin = _viewport.YMin;
        s.YMax = _viewport.YMax;
        return s;
    }

    /// <inheritdoc />
    public override NotebookCell Clone() => CopyBase(new GraphCell(Copy(SaveState()), _height, null));

    internal override CellData ToData()
    {
        var d = BaseData();
        d.Graph = SaveState();
        d.GraphHeight = _height;
        return d;
    }

    internal static GraphCell Restore(CellData d)
    {
        var c = new GraphCell(d.Graph ?? new GraphState(), d.GraphHeight ?? DefaultHeight, d.Id);
        c.ReadBase(d);
        return c;
    }

    private static GraphState Copy(GraphState s) => new()
    {
        Items = [.. s.Items.Select(i => new GraphItemState { Text = i.Text, Visible = i.Visible, Color = i.Color, TMin = i.TMin, TMax = i.TMax })],
        Parameters = [.. s.Parameters.Select(p => new GraphParameterState { Name = p.Name, Value = p.Value, Min = p.Min, Max = p.Max, Step = p.Step })],
        XMin = s.XMin,
        XMax = s.XMax,
        YMin = s.YMin,
        YMax = s.YMax,
        PiTicks = s.PiTicks,
    };
}
