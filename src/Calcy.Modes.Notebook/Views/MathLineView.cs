using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Core;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Controls;
using Calcy.UI.Math;

namespace Calcy.Modes.Notebook.Views;

/// <summary>
/// One maths line: the structured editor, then the result in a chip (exact or decimal first, the other form next to it),
/// an inline error under the line, and a slider for number definitions such as L = 0.8.
/// </summary>
internal sealed class MathLineView : StackPanel
{
    private readonly CalcSession _session;
    private readonly CalcSettings _settings;
    private readonly WrapPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly Border _chip;
    private readonly TextBlock _chipSign;
    private readonly MathView _primary;
    private readonly TextBlock _otherSign;
    private readonly MathView _secondary;
    private readonly PixelButton _swap;
    private readonly PixelButton _slideToggle;
    private readonly StackPanel _sliderRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly NbSlider _slider = new() { Width = 220, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error;
    private readonly PixelButton _range;
    private const int MaxSecondaryNodes = 12;

    private bool _syncing;
    private bool _showResults = true;
    private bool _active;

    public MathLineView(MathLine line, CalcSession session, CalcSettings settings, double fontSize = 20)
    {
        Line = line;
        _session = session;
        _settings = settings;
        Spacing = 4;
        Editor = new MathEditor
        {
            Text = line.Text,
            Session = session,
            FontSize = fontSize,
            BorderThickness = 0,
            Padding = new Thickness(4, 3),
            MinHeight = fontSize + 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            PlaceholderText = "type maths: a = 5, f(x) = x^2, 2 + 3",
            Margin = new Thickness(-4, 0, 12, 0),
        };
        Avalonia.Automation.AutomationProperties.SetName(Editor, "Maths line");
        Editor.TextChanged += (_, e) =>
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                Line.Text = e.NewText;
            }
            finally
            {
                _syncing = false;
            }
        };
        Editor.GotFocus += (_, _) => StyleEditor(true);
        Editor.LostFocus += (_, _) => StyleEditor(false);
        Editor.AddHandler(KeyDownEvent, OnEditorPreviewKey, RoutingStrategies.Tunnel);
        Editor.KeyDown += OnEditorKey;
        Editor.Submitted += (_, _) => Submitted?.Invoke(this, EventArgs.Empty);
        StyleEditor(false);

        _chipSign = Ui.Mono("=", fontSize, bold: true);
        _primary = new MathView { Session = session, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center, OverflowMode = MathOverflowMode.Scale };
        var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _chipSign, _primary } };
        _chip = Ui.Box(chipContent, "Calcy.Sel", 2, new Thickness(10, 3));
        _chip.VerticalAlignment = VerticalAlignment.Center;
        _chip.Margin = new Thickness(0, 2, 10, 2);
        _otherSign = Ui.Mono("≈", fontSize * 0.8, "Calcy.Muted");
        _otherSign.Margin = new Thickness(0, 0, 6, 0);
        _secondary = new MathView { Session = session, FontSize = fontSize * 0.8, VerticalAlignment = VerticalAlignment.Center, OverflowMode = MathOverflowMode.Scale, Margin = new Thickness(0, 0, 10, 0) };
        _secondary.Res(MathView.ForegroundProperty, "Calcy.Muted");
        _swap = Ui.Button("S⇔D", "Show the exact form or the decimal first", "mem");
        _swap.VerticalAlignment = VerticalAlignment.Center;
        _swap.Click += (_, _) =>
        {
            if (Line.Result is not { } r || r.IsError) return;
            var (_, _, exactFirst) = ResultForms.Forms(r.Result, _settings, Line.DecimalFirst);
            Line.DecimalFirst = exactFirst;
        };
        _slideToggle = Ui.Button("SLIDER", "Show a slider for this number");
        _slideToggle.VerticalAlignment = VerticalAlignment.Center;
        _slideToggle.Click += (_, _) => Line.ShowSlider = !Line.ShowSlider;

        _slider.UserChanged += (_, v) => Line.SetSliderValue(v);
        var range = Ui.Button("RANGE", "Set the slider range");
        range.Click += (_, _) => ShowRange(range);
        range.VerticalAlignment = VerticalAlignment.Center;
        _sliderRow.Children.Add(_slider);
        _sliderRow.Children.Add(range);
        _sliderRow.Margin = new Thickness(0, 0, 12, 0);
        _range = range;

        _row.Children.Add(Editor);
        _row.Children.Add(_sliderRow);
        _row.Children.Add(_chip);
        _row.Children.Add(_otherSign);
        _row.Children.Add(_secondary);
        _row.Children.Add(_swap);
        _row.Children.Add(_slideToggle);
        Children.Add(_row);
        _error = Ui.Body("", 14, "Calcy.Err");
        _error.Margin = new Thickness(0, -2, 0, 2);
        Children.Add(_error);

        line.TextChanged += OnLineText;
        line.DisplayChanged += OnLineDisplay;
        Refresh();
    }

    /// <summary>The line.</summary>
    public MathLine Line { get; }

    /// <summary>The editor.</summary>
    public MathEditor Editor { get; }

    /// <summary>The slider (visible for number definitions with a slider).</summary>
    public NbSlider Slider => _slider;

    /// <summary>The result chip.</summary>
    public Border ResultChip => _chip;

    /// <summary>Enter was pressed.</summary>
    public event EventHandler? Submitted;

    /// <summary>Backspace in an empty line.</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>Up (-1) or Down (+1) at the edge of the editor.</summary>
    public event EventHandler<int>? MoveRequested;

    /// <summary>False hides the result (a folded cell).</summary>
    public bool ShowResults
    {
        get => _showResults;
        set
        {
            _showResults = value;
            Refresh();
        }
    }

    /// <summary>True on the selected cell: line actions (S⇔D, SLIDER, RANGE) are shown.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            _active = value;
            Refresh();
        }
    }

    private void StyleEditor(bool focused)
    {
        Editor.BorderThickness = focused ? 2 : 0;
        Editor.Res(MathEditor.BorderBrushProperty, "Calcy.Ink");
        if (focused) Editor.Res(MathEditor.BackgroundProperty, "Calcy.Paper");
        else Editor.Res(MathEditor.BackgroundProperty, "Calcy.Card");
    }

    private void OnEditorPreviewKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && string.IsNullOrEmpty(Editor.Text))
        {
            e.Handled = true;
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;
            MoveRequested?.Invoke(this, e.Key == Key.Up ? -1 : 1);
        }
    }

    private void OnLineText(object? sender, EventArgs e)
    {
        if (_syncing) return;
        if (Editor.Text != Line.Text)
        {
            _syncing = true;
            try
            {
                Editor.Text = Line.Text;
            }
            finally
            {
                _syncing = false;
            }
        }
        UpdateSlider();
    }

    private void OnLineDisplay(object? sender, EventArgs e) => Refresh();

    /// <summary>Shows the latest result.</summary>
    public void Refresh()
    {
        UpdateSlider();
        var lr = Line.Result;
        bool blank = string.IsNullOrWhiteSpace(Line.Text);
        _error.IsVisible = false;
        _chip.IsVisible = _otherSign.IsVisible = _secondary.IsVisible = _swap.IsVisible = false;
        _slideToggle.IsVisible = _active && Line.CanSlide && !Line.ShowSlider;
        _range.IsVisible = _active;
        if (blank || lr is null || !_showResults) return;
        var r = lr.Result;
        if (r.IsError)
        {
            _error.Text = r.ErrorMessage + (r.Hint is { } h ? $" (try {h})" : "");
            _error.IsVisible = true;
            _error.Res(TextBlock.ForegroundProperty, lr.Tainted ? "Calcy.Muted" : "Calcy.Err");
            if (lr.Tainted) _error.Text = "Waiting for a code cell above to run. " + _error.Text;
            return;
        }
        if (!ResultForms.ShowsResult(r, Line.Text)) return;
        var (primary, secondary, exactFirst) = ResultForms.Forms(r, _settings, Line.DecimalFirst);
        _primary.Expression = primary;
        _chipSign.Text = exactFirst ? "=" : "≈";
        // Solutions (x = 2 or x = 3) and true or false read on their own, without a leading sign.
        _chipSign.IsVisible = primary is not (Calcy.Core.Expressions.RelationExpr or Calcy.Core.Expressions.LogicExpr or Calcy.Core.Expressions.BoolExpr);
        _chip.IsVisible = true;
        _chip.Res(Border.BackgroundProperty, lr.Tainted ? "Calcy.Paper" : "Calcy.Sel");
        _primary.Res(MathView.ForegroundProperty, lr.Tainted ? "Calcy.Muted" : "Calcy.Ink");
        ToolTip.SetTip(_chip, lr.Tainted ? "Uses a value from a code cell that has to run (again)" : null);
        if (secondary is not null)
        {
            _secondary.Expression = secondary;
            _otherSign.Text = exactFirst ? "≈" : "=";
            // A sprawling exact form next to its decimal is noise: it stays one S⇔D click away (and in the tooltip).
            bool compact = exactFirst || secondary.NodeCount() <= MaxSecondaryNodes;
            _otherSign.IsVisible = _secondary.IsVisible = compact;
            if (!compact && !lr.Tainted) ToolTip.SetTip(_chip, "Exact: " + _session.Print(secondary));
            _swap.IsVisible = _active;
        }
    }

    private void UpdateSlider()
    {
        bool show = Line.ShowSlider && Line.TryGetLiteral(out _, out var value);
        _sliderRow.IsVisible = show;
        if (!show) return;
        _slider.Minimum = Line.SliderMin;
        _slider.Maximum = Line.SliderMax;
        _slider.Step = Line.SliderStep;
        Line.TryGetLiteral(out var name, out value);
        _slider.Value = Math.Clamp(value, Line.SliderMin, Line.SliderMax);
        Avalonia.Automation.AutomationProperties.SetName(_slider, "Slider for " + name);
    }

    private void ShowRange(Control anchor)
    {
        TextBox Box(double v)
        {
            var t = new TextBox { Text = v.ToString("0.##########", CultureInfo.InvariantCulture), Width = 90, FontSize = 14, MinHeight = 30 };
            return t;
        }
        var min = Box(Line.SliderMin);
        var max = Box(Line.SliderMax);
        var step = Box(Line.SliderStep);
        var ok = Ui.Button("OK", "Use this range", "ok");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Thickness(12) };
        void Row(int r, string label, Control c)
        {
            var l = Ui.Label(label, 11);
            l.Margin = new Thickness(0, 4, 10, 4);
            Grid.SetRow(l, r);
            Grid.SetRow(c, r);
            Grid.SetColumn(c, 1);
            c.Margin = new Thickness(0, 4);
            grid.Children.Add(l);
            grid.Children.Add(c);
        }
        Row(0, "MIN", min);
        Row(1, "MAX", max);
        Row(2, "STEP", step);
        Row(3, "", ok);
        var panel = Ui.Box(grid, "Calcy.Paper", 0);
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        ok.Click += (_, _) =>
        {
            static double Parse(TextBox t) => double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
            Line.SetRange(Parse(min), Parse(max), Parse(step));
            flyout.Hide();
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Reads the text again with the names the notebook now defines (Tm is one name once a table defines it, not T
    /// times m). Skipped while the line is being edited, so the caret never jumps.
    /// </summary>
    public void Reparse()
    {
        if (Editor.IsKeyboardFocusWithin || string.IsNullOrWhiteSpace(Editor.Text)) return;
        _syncing = true;
        try
        {
            Editor.Session = null;
            Editor.Session = _session;
            if (Editor.Text != Line.Text) Editor.Text = Line.Text;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Stops listening to the line.</summary>
    public void Detach()
    {
        Line.TextChanged -= OnLineText;
        Line.DisplayChanged -= OnLineDisplay;
    }
}
