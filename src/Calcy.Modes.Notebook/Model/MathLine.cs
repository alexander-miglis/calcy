using System.Globalization;
using System.Text.RegularExpressions;

namespace Calcy.Modes.Notebook.Model;

/// <summary>
/// One line of maths in a maths cell or under a table: an expression (2 + 3), a definition (a = 5, f(x) = x^2) or an
/// equation to solve. A number definition can show a slider that rewrites the line as it moves.
/// </summary>
public sealed partial class MathLine
{
    private string _text;
    private bool _showSlider;
    private double _min, _max, _step;
    private bool? _decimalFirst;

    /// <summary>Creates a line.</summary>
    public MathLine(string text = "")
    {
        _text = text ?? "";
        (_min, _max, _step) = DefaultRange(TryGetLiteral(out _, out var v) ? v : 1);
    }

    /// <summary>The line as linear calculator input.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text) return;
            _text = value;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when a slider is shown (only used while the line is a number definition such as L = 0.8).</summary>
    public bool ShowSlider
    {
        get => _showSlider;
        set
        {
            if (value == _showSlider) return;
            _showSlider = value;
            if (value && TryGetLiteral(out _, out var v) && (v < _min || v > _max)) (_min, _max, _step) = DefaultRange(v);
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Slider minimum.</summary>
    public double SliderMin => _min;

    /// <summary>Slider maximum.</summary>
    public double SliderMax => _max;

    /// <summary>Slider step.</summary>
    public double SliderStep => _step;

    /// <summary>Show the decimal first (true), the exact form first (false) or follow the settings (null).</summary>
    public bool? DecimalFirst
    {
        get => _decimalFirst;
        set
        {
            if (value == _decimalFirst) return;
            _decimalFirst = value;
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The latest result (null for an empty line or before the first calculation).</summary>
    public LineResult? Result { get; internal set; }

    /// <summary>Raised when <see cref="Text"/> changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised when a display option (slider, exact or decimal first) changes.</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>True when the line is a number definition that can have a slider.</summary>
    public bool CanSlide => TryGetLiteral(out _, out _);

    /// <summary>Sets the slider range (min below max, step above zero).</summary>
    public void SetRange(double min, double max, double step)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || !double.IsFinite(step) || max <= min || step <= 0) return;
        (_min, _max, _step) = (min, max, step);
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads a number definition: "L = 0.8" gives L and 0.8.</summary>
    public bool TryGetLiteral(out string name, out double value)
    {
        var m = LiteralPattern().Match(_text);
        if (m.Success && double.TryParse(m.Groups["v"].Value.Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            name = m.Groups["n"].Value;
            return true;
        }
        name = "";
        value = 0;
        return false;
    }

    /// <summary>Moves the slider: the line becomes "name = value", snapped to the step.</summary>
    public void SetSliderValue(double value)
    {
        if (!TryGetLiteral(out var name, out _) || !double.IsFinite(value)) return;
        Text = $"{name} = {FormatValue(Snap(value))}";
    }

    /// <summary>Snaps a value to the slider grid and range.</summary>
    public double Snap(double v)
    {
        v = Math.Clamp(v, _min, _max);
        if (_step > 0) v = _min + Math.Round((v - _min) / _step) * _step;
        return Math.Round(Math.Clamp(v, _min, _max), Decimals(_step));
    }

    /// <summary>Formats a slider value with as many decimals as the step needs.</summary>
    public string FormatValue(double v)
    {
        var s = Math.Round(v, Decimals(_step)).ToString("0.##########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    /// <summary>A copy (results are not copied).</summary>
    public MathLine Clone()
    {
        var c = new MathLine(_text) { _showSlider = _showSlider, _decimalFirst = _decimalFirst };
        (c._min, c._max, c._step) = (_min, _max, _step);
        return c;
    }

    /// <summary>Saved form.</summary>
    public MathLineData ToData() => new()
    {
        Text = _text,
        Slider = _showSlider,
        Min = _min,
        Max = _max,
        Step = _step,
        DecimalFirst = _decimalFirst,
    };

    /// <summary>Restores a line from saved form.</summary>
    public static MathLine FromData(MathLineData d)
    {
        var line = new MathLine(d.Text) { _showSlider = d.Slider, _decimalFirst = d.DecimalFirst };
        if (d.Min is double a && d.Max is double b && d.Step is double s && b > a && s > 0 && double.IsFinite(a) && double.IsFinite(b))
            (line._min, line._max, line._step) = (a, b, s);
        return line;
    }

    /// <summary>A sensible slider range for a value: 0 to a round number at least twice as large (0.8 gives 0 to 2).</summary>
    public static (double Min, double Max, double Step) DefaultRange(double v)
    {
        if (!double.IsFinite(v) || v == 0) return (-10, 10, 0.1);
        double target = 2 * Math.Abs(v);
        double p = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double max = new[] { 1.0, 2, 5, 10 }.Select(k => k * p).First(x => x >= target * (1 - 1e-12));
        double step = NiceStep(max / 200);
        return v > 0 ? (0, max, step) : (-max, max, step);
    }

    private static double NiceStep(double raw)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var k in new[] { 1.0, 2, 5, 10 })
            if (k * p >= raw * (1 - 1e-12)) return k * p;
        return 10 * p;
    }

    private static int Decimals(double step) => step > 0 ? Math.Clamp((int)Math.Ceiling(-Math.Log10(step) - 1e-9), 0, 10) : 6;

    [GeneratedRegex(@"^\s*(?<n>[\p{L}_][\p{L}\p{N}_]*)\s*(=|:=)\s*(?<v>[-−]?\d+(\.\d+)?)\s*$")]
    private static partial Regex LiteralPattern();
}
