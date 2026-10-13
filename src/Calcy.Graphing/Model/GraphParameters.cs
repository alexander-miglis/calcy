namespace Calcy.Graphing;

/// <summary>How a playing slider moves when it reaches the end of its range.</summary>
public enum AnimationMode
{
    /// <summary>Jump back to the start.</summary>
    Loop,

    /// <summary>Reverse direction.</summary>
    Bounce,

    /// <summary>Stop at the end.</summary>
    Once,
}

/// <summary>A slider for a free parameter: value, range, step and animation state.</summary>
public sealed class GraphParameter
{
    private double _value;

    internal GraphParameter(GraphParameters owner, string name, double value)
    {
        Owner = owner;
        Name = name;
        _value = value;
    }

    internal GraphParameters Owner { get; }

    /// <summary>The parameter name (a, b, k, ...).</summary>
    public string Name { get; }

    /// <summary>Current value. Setting it raises <see cref="GraphParameters.ValuesChanged"/>.</summary>
    public double Value
    {
        get => _value;
        set
        {
            if (!double.IsFinite(value) || value == _value) return;
            _value = value;
            Owner.RaiseChanged(this);
        }
    }

    /// <summary>Slider minimum.</summary>
    public double Min { get; set; } = -5;

    /// <summary>Slider maximum.</summary>
    public double Max { get; set; } = 5;

    /// <summary>Slider step (also the animation step).</summary>
    public double Step { get; set; } = 0.1;

    /// <summary>True while the play button animates the value.</summary>
    public bool IsPlaying { get; set; }

    /// <summary>What happens at the end of the range while playing.</summary>
    public AnimationMode Mode { get; set; } = AnimationMode.Loop;

    /// <summary>Steps per second while playing.</summary>
    public double Speed { get; set; } = 8;

    private int _direction = 1;

    /// <summary>Moves the value one animation step. Returns false when an Once animation has finished.</summary>
    public bool Advance()
    {
        double step = Math.Abs(Step) > 0 ? Math.Abs(Step) : (Max - Min) / 100;
        double next = Snap(_value + _direction * step);
        if (next > Max + step * 1e-9)
        {
            switch (Mode)
            {
                case AnimationMode.Loop: next = Min; break;
                case AnimationMode.Bounce: _direction = -1; next = Snap(Max - step); break;
                default: Value = Max; IsPlaying = false; return false;
            }
        }
        else if (next < Min - step * 1e-9)
        {
            if (Mode == AnimationMode.Bounce)
            {
                _direction = 1;
                next = Snap(Min + step);
            }
            else next = Max;
        }
        Value = next;
        return true;
    }

    /// <summary>Rounds a value to the slider grid (so 0.1 steps do not drift to 0.30000000000000004).</summary>
    public double Snap(double v)
    {
        if (!(Step > 0)) return v;
        double k = Math.Round((v - Min) / Step);
        double snapped = Min + k * Step;
        int decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(Step)) + 1, 0, 12);
        return Math.Round(snapped, decimals);
    }
}

/// <summary>The sliders of a graph document, by name.</summary>
public sealed class GraphParameters
{
    private readonly Dictionary<string, GraphParameter> _byName = new(StringComparer.Ordinal);
    private readonly List<GraphParameter> _order = [];

    /// <summary>Raised when a value changes (the argument is the parameter).</summary>
    public event EventHandler<GraphParameter>? ValuesChanged;

    /// <summary>All sliders in creation order.</summary>
    public IReadOnlyList<GraphParameter> All => _order;

    /// <summary>The default value of a new slider.</summary>
    public const double DefaultValue = 1;

    /// <summary>The slider for a name, or null.</summary>
    public GraphParameter? this[string name] => _byName.TryGetValue(name, out var p) ? p : null;

    /// <summary>Gets the slider for a name, creating it with <paramref name="initial"/> when missing.</summary>
    public GraphParameter GetOrAdd(string name, double initial = DefaultValue)
    {
        if (_byName.TryGetValue(name, out var p)) return p;
        p = new GraphParameter(this, name, initial);
        if (Math.Abs(initial) > 5)
        {
            double mag = Math.Pow(10, Math.Ceiling(Math.Log10(Math.Abs(initial) * 1.0001)));
            p.Min = -mag;
            p.Max = mag;
            p.Step = mag / 100;
        }
        _byName[name] = p;
        _order.Add(p);
        return p;
    }

    /// <summary>Removes sliders whose names are not in <paramref name="keep"/>.</summary>
    public void RemoveUnused(IReadOnlyCollection<string> keep)
    {
        foreach (var p in _order.Where(p => !keep.Contains(p.Name)).ToList())
        {
            _order.Remove(p);
            _byName.Remove(p.Name);
        }
    }

    /// <summary>The value of a parameter (the default when it has no slider).</summary>
    public double ValueOf(string name) => _byName.TryGetValue(name, out var p) ? p.Value : DefaultValue;

    /// <summary>A snapshot of the values for the given names, in order (used to bind compiled expressions).</summary>
    public double[] Vector(IReadOnlyList<string> names)
    {
        var v = new double[names.Count];
        for (int i = 0; i < v.Length; i++) v[i] = ValueOf(names[i]);
        return v;
    }

    /// <summary>A snapshot of every value by name.</summary>
    public IReadOnlyDictionary<string, double> Snapshot() => _order.ToDictionary(p => p.Name, p => p.Value);

    internal void RaiseChanged(GraphParameter p) => ValuesChanged?.Invoke(this, p);
}

/// <summary>Supplies parameter values for an item (a snapshot aligned with <see cref="GraphItem.Parameters"/>).</summary>
public delegate double[] ParameterSource(GraphItem item);
