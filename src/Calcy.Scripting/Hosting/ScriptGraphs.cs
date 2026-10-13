using Calcy.Graphing;

namespace Calcy.Scripting;

/// <summary>A graph a program asked for with calc.graph.add (shown as a native, interactive graph by the screen).</summary>
/// <param name="Text">The graph text, exactly as accepted by Graphing mode (for example "y = sin(x)").</param>
/// <param name="ColorIndex">Graph colour 0 to 4 (G1 to G5), or null for the next free colour.</param>
public sealed record GraphRequest(string Text, int? ColorIndex = null);

/// <summary>The graphs requested by the current program (PR-012, PR-016). Safe to read from any thread.</summary>
public sealed class ScriptGraphs
{
    private readonly object _gate = new();
    private readonly List<GraphRequest> _items = [];
    private Viewport? _window;
    private int _version;

    /// <summary>Raised (on the interpreter thread) after the requests change.</summary>
    public event EventHandler? Changed;

    /// <summary>The requested graphs in order.</summary>
    public IReadOnlyList<GraphRequest> Items
    {
        get { lock (_gate) return [.. _items]; }
    }

    /// <summary>The graph window set with calc.graph.window, or null to fit the graphs.</summary>
    public Viewport? Window
    {
        get { lock (_gate) return _window; }
    }

    /// <summary>Increases with every change.</summary>
    public int Version
    {
        get { lock (_gate) return _version; }
    }

    internal int Add(GraphRequest request)
    {
        int index;
        lock (_gate)
        {
            _items.Add(request);
            index = _items.Count - 1;
            _version++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return index;
    }

    internal bool RemoveAt(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _items.Count) return false;
            _items.RemoveAt(index);
            _version++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal void SetWindow(Viewport? window)
    {
        lock (_gate)
        {
            _window = window;
            _version++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes every request and the window.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_items.Count == 0 && _window is null) return;
            _items.Clear();
            _window = null;
            _version++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
