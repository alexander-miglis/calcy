using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Graphing;
using Calcy.Modes.Graphing.Controls;
using Calcy.UI.Controls;

namespace Calcy.Modes.Graphing;

public sealed partial class GraphingView
{
    private sealed class RowView
    {
        public required Border Root { get; init; }
        public required PixelToggle Swatch { get; init; }
        public required TextBox Box { get; init; }
        public required TextBlock Kind { get; init; }
        public required TextBlock Error { get; init; }
        public required StackPanel Sliders { get; init; }
        public required Debouncer ErrorDelay { get; init; }
        public List<SliderView> SliderViews { get; } = [];
        public IDisposable? SwatchBinding { get; set; }
        public string? SwatchToken { get; set; }
        public bool ErrorReady { get; set; } = true;
    }

    private sealed class SliderView
    {
        public required GraphParameter Parameter { get; init; }
        public required TextBlock Label { get; init; }
        public required PixelSlider Slider { get; init; }
        public required PixelIcon PlayIcon { get; init; }
        public required PixelButton Play { get; init; }
    }

    private readonly List<RowView> _rowViews = [];
    private readonly StackPanel _rows = new();
    private TextBox _addBox = null!;
    private TextBox? _lastBox;
    private bool _syncing;
    private DispatcherTimer? _animation;
    private PixelPanel _leftPanel = null!;

    private static readonly (string Glyph, string Insert, int Caret, string Tip)[] Chips =
    [
        ("□/□", "()/()", 1, "Fraction"),
        ("√□", "sqrt()", 5, "Square root"),
        ("□ⁿ", "^", 1, "Power"),
        ("θ", "θ", 1, "Theta, for polar curves r = f(θ)"),
        ("≤", "≤", 1, "Less than or equal (shades a region)"),
        ("{x>0}", " {x > 0}", 8, "Domain restriction"),
    ];

    private static readonly string[] Symbols =
    [
        "π", "θ", "√", "²", "³", "≤", "≥", "≠", "·", "∞", "|", "e", "t", "'", "^", "(", ")", "{", "}", ",",
        "sin(", "cos(", "tan(", "ln(", "log(", "abs(", "exp(", "floor(", "dy/dx = ",
    ];

    private Control BuildExpressionPanel()
    {
        _addBox = new TextBox
        {
            Classes = { "expr" },
            PlaceholderText = "+ type y = …, r = …, x² + y² = …",
            FontSize = 15,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_addBox, "New expression");
        _addBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitAddBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && _rowViews.Count > 0)
            {
                _rowViews[^1].Box.Focus();
                e.Handled = true;
            }
        };
        _addBox.GotFocus += (_, _) => _lastBox = _addBox;
        _addBox.LostFocus += (_, _) =>
        {
            // Typing then clicking away keeps the expression.
            if (!string.IsNullOrWhiteSpace(_addBox.Text)) CommitAddBox(keepFocus: false);
        };
        var dashed = new DashedBorder { Child = _addBox, Padding = new Thickness(6, 3), MinHeight = 44 };
        Bind(dashed, DashedBorder.StrokeProperty, "Calcy.Muted");

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (glyph, insert, caret, tip) in Chips)
        {
            var b = new PixelButton
            {
                Content = glyph,
                Classes = { "small", "card", "flat" },
                Margin = new Thickness(0, 0, 6, 6),
                MinHeight = 30,
                MinWidth = 30,
                Padding = new Thickness(6, 2),
                BorderThickness = new Thickness(2),
                FontSize = 13,
                FontWeight = FontWeight.Normal,
            };
            Bind(b, TemplatedControl.FontFamilyProperty, "Calcy.Font.Math");
            ToolTip.SetTip(b, tip);
            AutomationProperties.SetName(b, "Insert " + tip);
            b.Click += (_, _) => Insert(insert, caret);
            chips.Children.Add(b);
        }
        var symbols = new PixelButton
        {
            Content = "Symbols",
            Classes = { "small", "head", "flat" },
            Margin = new Thickness(0, 0, 6, 6),
            MinHeight = 30,
            Padding = new Thickness(6, 2),
            BorderThickness = new Thickness(2),
            FontSize = 11,
        };
        symbols.Flyout = new Flyout { Content = SymbolGrid(), Placement = PlacementMode.Bottom };
        chips.Children.Add(symbols);

        var addArea = new StackPanel { Margin = new Thickness(12, 10, 12, 12), Children = { dashed, chips } };
        var stack = new StackPanel { Children = { _rows, addArea } };
        _leftPanel = new PixelPanel
        {
            Header = "Expressions",
            Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            VerticalAlignment = VerticalAlignment.Top,
        };
        return _leftPanel;
    }

    private Control SymbolGrid()
    {
        var grid = new WrapPanel { Width = 300, Margin = new Thickness(10) };
        foreach (var s in Symbols)
        {
            var b = new PixelButton
            {
                Content = s.TrimEnd('(', ' ').Replace(" =", ""),
                Classes = { "small", "card", "flat" },
                MinWidth = 40,
                Height = 36,
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = s.Length > 3 ? 11 : 15,
                FontWeight = FontWeight.Normal,
            };
            Bind(b, TemplatedControl.FontFamilyProperty, "Calcy.Font.Math");
            var text = s;
            b.Click += (_, _) => Insert(text, text.Length);
            grid.Children.Add(b);
        }
        return grid;
    }

    /// <summary>Inserts text into the expression being edited (or the "+" row) at the caret, replacing any selection.</summary>
    private void Insert(string text, int caret)
    {
        var box = _lastBox ?? _addBox;
        var current = box.Text ?? "";
        int a = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        int b = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), a, current.Length);
        box.Text = current[..a] + text + current[b..];
        box.CaretIndex = a + Math.Clamp(caret, 0, text.Length);
        box.Focus();
    }

    private void CommitAddBox(bool keepFocus = true)
    {
        var text = (_addBox.Text ?? "").Trim();
        if (text.Length == 0) return;
        _addBox.Text = "";
        AddExpression(text);
        if (keepFocus) _addBox.Focus();
    }

    // ------------------------------------------------------------------ rows

    private void RebuildRows()
    {
        bool hadFocus = _rowViews.Any(r => r.Box.IsFocused);
        int focusIndex = _rowViews.FindIndex(r => r.Box.IsFocused);
        _rows.Children.Clear();
        _rowViews.Clear();
        for (int i = 0; i < _doc.Items.Count; i++)
        {
            var rv = CreateRow();
            _rowViews.Add(rv);
            _rows.Children.Add(rv.Root);
        }
        UpdateRows();
        if (hadFocus && focusIndex >= 0 && focusIndex < _rowViews.Count) _rowViews[focusIndex].Box.Focus();
    }

    private RowView CreateRow()
    {
        var swatch = new PixelToggle
        {
            Width = 30,
            Height = 30,
            MinHeight = 30,
            MinWidth = 30,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new TextBox { Classes = { "expr" }, FontSize = 18, VerticalContentAlignment = VerticalAlignment.Center };
        var kind = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Bind(kind, TextBlock.FontFamilyProperty, "Calcy.Font.Label");
        Bind(kind, TextBlock.ForegroundProperty, "Calcy.Muted");
        var icon = new PixelIcon { Kind = "close", Width = 10, Height = 10 };
        Bind(icon, PixelIcon.ForegroundProperty, "Calcy.Muted");
        var delete = new PixelButton
        {
            Content = icon,
            Classes = { "small", "flat" },
            Width = 24,
            MinWidth = 24,
            MinHeight = 24,
            Height = 24,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(delete, "Delete");
        AutomationProperties.SetName(delete, "Delete expression");
        var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(40, 0, 0, 0), IsVisible = false };
        Bind(error, TextBlock.ForegroundProperty, "Calcy.Err");
        Bind(error, TextBlock.FontFamilyProperty, "Calcy.Font.Body");
        var sliders = new StackPanel { Spacing = 6, Margin = new Thickness(40, 0, 0, 0) };

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(swatch, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(kind, 2);
        Grid.SetColumn(delete, 3);
        line.Children.Add(swatch);
        line.Children.Add(box);
        line.Children.Add(kind);
        line.Children.Add(delete);
        var body = new StackPanel { Spacing = 8, Children = { line, error, sliders } };
        var root = new Border { Child = body, Padding = new Thickness(12, 10), BorderThickness = new Thickness(0, 0, 0, 3) };
        Bind(root, Border.BorderBrushProperty, "Calcy.Line");

        RowView? rv = null;
        rv = new RowView
        {
            Root = root,
            Swatch = swatch,
            Box = box,
            Kind = kind,
            Error = error,
            Sliders = sliders,
            ErrorDelay = new Debouncer(TimeSpan.FromMilliseconds(700), () =>
            {
                rv!.ErrorReady = true;
                int i = _rowViews.IndexOf(rv);
                if (i >= 0 && i < _doc.Items.Count) UpdateRow(rv, _doc.Items[i], i);
            }),
        };
        swatch.Click += (_, _) => ToggleVisible(_rowViews.IndexOf(rv));
        delete.Click += (_, _) => RemoveRow(_rowViews.IndexOf(rv));
        box.GotFocus += (_, _) =>
        {
            _lastBox = box;
            int i = _rowViews.IndexOf(rv);
            if (i != _selected) Select(i);
        };
        box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            int i = _rowViews.IndexOf(rv);
            if (i < 0 || i >= _doc.Items.Count) return;
            rv.ErrorReady = false;
            rv.ErrorDelay.Trigger();
            _doc.Replace(i, box.Text ?? "");
        };
        box.LostFocus += (_, _) =>
        {
            rv.ErrorReady = true;
            int i = _rowViews.IndexOf(rv);
            if (i >= 0 && i < _doc.Items.Count) UpdateRow(rv, _doc.Items[i], i);
        };
        box.KeyDown += (_, e) => OnRowKey(rv, e);
        root.PointerPressed += (_, _) =>
        {
            int i = _rowViews.IndexOf(rv);
            if (i >= 0) Select(i);
        };
        return rv;
    }

    private void OnRowKey(RowView rv, KeyEventArgs e)
    {
        int i = _rowViews.IndexOf(rv);
        switch (e.Key)
        {
            case Key.Enter or Key.Down:
                if (i + 1 < _rowViews.Count) _rowViews[i + 1].Box.Focus();
                else _addBox.Focus();
                e.Handled = true;
                break;
            case Key.Up when i > 0:
                _rowViews[i - 1].Box.Focus();
                e.Handled = true;
                break;
            case Key.Back when string.IsNullOrEmpty(rv.Box.Text):
                RemoveRow(i);
                if (i - 1 >= 0 && i - 1 < _rowViews.Count) _rowViews[i - 1].Box.Focus();
                else _addBox.Focus();
                e.Handled = true;
                break;
        }
    }

    private void ToggleVisible(int index)
    {
        if (index < 0 || index >= _doc.Items.Count) return;
        var item = _doc.Items[index];
        item.IsVisible = !item.IsVisible;
        _canvas.Refresh();
        UpdateRows();
        RefreshCards();
        if (_view != GraphViewMode.Graph) RefreshTable();
        _saveLater.Trigger();
    }

    private void RemoveRow(int index)
    {
        if (index < 0 || index >= _doc.Items.Count) return;
        _doc.RemoveAt(index);
        if (_selected >= _doc.Items.Count) Select(_doc.Items.Count - 1);
    }

    private void UpdateRows()
    {
        for (int i = 0; i < _rowViews.Count && i < _doc.Items.Count; i++) UpdateRow(_rowViews[i], _doc.Items[i], i);
        UpdateRowSelection();
    }

    private void UpdateRowSelection()
    {
        for (int i = 0; i < _rowViews.Count; i++)
        {
            var root = _rowViews[i].Root;
            if (i == _selected) Bind(root, Border.BackgroundProperty, "Calcy.Card");
            else root.Background = Brushes.Transparent;
        }
    }

    private void UpdateRow(RowView rv, GraphItem item, int index)
    {
        _syncing = true;
        try
        {
            if (!rv.Box.IsFocused && rv.Box.Text != item.Text) rv.Box.Text = item.Text;
        }
        finally
        {
            _syncing = false;
        }
        AutomationProperties.SetName(rv.Box, $"Expression {index + 1}");
        string token = item.IsVisible && item.Kind != GraphKind.Error ? InkToken(item.ColorIndex) : "Calcy.Paper";
        if (rv.SwatchToken != token)
        {
            rv.SwatchBinding?.Dispose();
            rv.SwatchBinding = rv.Swatch.Bind(TemplatedControl.BackgroundProperty, rv.Swatch.GetResourceObservable(token));
            rv.SwatchToken = token;
        }
        rv.Swatch.IsChecked = item.IsVisible;
        rv.Swatch.IsEnabled = item.IsPlottable || item.Kind == GraphKind.Empty;
        AutomationProperties.SetName(rv.Swatch, (item.IsVisible ? "Hide " : "Show ") + (string.IsNullOrWhiteSpace(item.Text) ? $"expression {index + 1}" : item.Text));
        ToolTip.SetTip(rv.Swatch, item.IsVisible ? "Hide" : "Show");
        rv.Kind.Text = item.Kind == GraphKind.Empty ? "" : item.KindLabel;
        bool showError = item.Error is not null && (rv.ErrorReady || !rv.Box.IsFocused);
        rv.Error.IsVisible = showError;
        rv.Error.Text = showError ? item.Error : "";
        Bind(rv.Box, TextBox.ForegroundProperty, item.IsVisible ? "Calcy.Ink" : "Calcy.Muted");
        UpdateRowSliders(rv, item, index);
    }

    // ------------------------------------------------------------------ sliders

    private void UpdateRowSliders(RowView rv, GraphItem item, int index)
    {
        var owned = _doc.Parameters.All.Where(p => _doc.ParameterOwner(p.Name) == index).ToList();
        if (!owned.SequenceEqual(rv.SliderViews.Select(s => s.Parameter)))
        {
            rv.Sliders.Children.Clear();
            rv.SliderViews.Clear();
            foreach (var p in owned)
            {
                var sv = CreateSlider(p, item);
                rv.SliderViews.Add(sv);
                rv.Sliders.Children.Add(SliderRow(sv));
            }
        }
        foreach (var sv in rv.SliderViews)
        {
            Bind(sv.Slider, PixelSlider.AccentProperty, InkToken(item.ColorIndex));
            SyncSlider(sv);
        }
        rv.Sliders.IsVisible = rv.SliderViews.Count > 0;
    }

    private SliderView CreateSlider(GraphParameter p, GraphItem owner)
    {
        var label = MathText("", 15);
        label.MinWidth = 64;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.NoWrap;
        label.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(label, "Click to set the range and step");
        var slider = new PixelSlider { Minimum = p.Min, Maximum = p.Max, Step = p.Step, Value = p.Value, MinWidth = 60, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, $"Slider {p.Name}");
        var icon = new PixelIcon { Kind = "play", Width = 14, Height = 14 };
        Bind(icon, PixelIcon.ForegroundProperty, "Calcy.Ink");
        var play = new PixelButton
        {
            Content = icon,
            Classes = { "sel", "flat" },
            Width = 36,
            Height = 36,
            MinWidth = 36,
            MinHeight = 36,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(2),
        };
        AutomationProperties.SetName(play, $"Animate {p.Name}");
        var sv = new SliderView { Parameter = p, Label = label, Slider = slider, PlayIcon = icon, Play = play };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _syncing) return;
            p.Value = slider.Value;
        };
        play.Click += (_, _) =>
        {
            p.IsPlaying = !p.IsPlaying;
            SyncSlider(sv);
            EnsureAnimation();
        };
        label.PointerPressed += (_, _) => ShowRangeEditor(sv);
        return sv;
    }

    private static Control SliderRow(SliderView sv)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(sv.Label, 0);
        Grid.SetColumn(sv.Slider, 1);
        Grid.SetColumn(sv.Play, 2);
        g.Children.Add(sv.Label);
        g.Children.Add(sv.Slider);
        g.Children.Add(sv.Play);
        return g;
    }

    private void SyncSlider(SliderView sv)
    {
        var p = sv.Parameter;
        _syncing = true;
        try
        {
            sv.Slider.Minimum = Math.Min(p.Min, p.Value);
            sv.Slider.Maximum = Math.Max(p.Max, p.Value);
            sv.Slider.Step = p.Step;
            sv.Slider.Value = p.Value;
        }
        finally
        {
            _syncing = false;
        }
        sv.Label.Text = $"{p.Name} = {GraphFormat.Number(p.Value, 4, true)}";
        sv.PlayIcon.Kind = p.IsPlaying ? "pause" : "play";
        AutomationProperties.SetName(sv.Play, (p.IsPlaying ? "Pause " : "Animate ") + p.Name);
    }

    private void UpdateSliders(GraphParameter p)
    {
        foreach (var rv in _rowViews)
            foreach (var sv in rv.SliderViews)
                if (sv.Parameter == p) SyncSlider(sv);
        // A slider defined by a row such as "a = 1.5" keeps the row's text in step.
        int owner = _doc.ParameterOwner(p.Name);
        if (owner >= 0 && owner < _doc.Items.Count && _doc.Items[owner].Kind == GraphKind.Variable && !_rowViews[owner].Box.IsFocused)
        {
            var text = $"{p.Name} = {GraphFormat.Number(p.Value, 6)}";
            if (_doc.Items[owner].Text != text) _doc.Replace(owner, text);
        }
    }

    private void EnsureAnimation()
    {
        bool any = _doc.Parameters.All.Any(p => p.IsPlaying);
        if (any && _animation is null)
        {
            _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal, (_, _) => AnimationStep());
            _animation.Start();
        }
        else if (!any && _animation is not null)
        {
            _animation.Stop();
            _animation = null;
        }
    }

    /// <summary>Advances every playing slider by one step (the animation timer calls this).</summary>
    public void AnimationStep()
    {
        foreach (var p in _doc.Parameters.All.Where(p => p.IsPlaying).ToList())
        {
            if (!p.Advance())
            {
                foreach (var rv in _rowViews)
                    foreach (var sv in rv.SliderViews.Where(s => s.Parameter == p))
                        SyncSlider(sv);
            }
        }
        EnsureAnimation();
    }

    private void ShowRangeEditor(SliderView sv)
    {
        var p = sv.Parameter;
        TextBox Field(string label, double value, out Control row)
        {
            var tb = new TextBox { Text = GraphFormat.Number(value, 8), Width = 110, FontSize = 14, MinHeight = 32 };
            AutomationProperties.SetName(tb, $"{label} of {p.Name}");
            row = new DockPanel { Children = { Label(label, 11), tb } };
            DockPanel.SetDock(tb, Dock.Right);
            return tb;
        }
        var min = Field("MIN", p.Min, out var r1);
        var max = Field("MAX", p.Max, out var r2);
        var step = Field("STEP", p.Step, out var r3);
        var apply = new PixelButton { Content = "APPLY", Classes = { "small", "primary" }, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12), Width = 220, Children = { Label($"SLIDER {p.Name}", 12), r1, r2, r3, apply } };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        apply.Click += (_, _) =>
        {
            double? a = TryNumber(min.Text), b = TryNumber(max.Text), s = TryNumber(step.Text);
            if (a is double lo && b is double hi && hi > lo)
            {
                p.Min = lo;
                p.Max = hi;
            }
            if (s is double st && st > 0) p.Step = st;
            if (p.Value < p.Min) p.Value = p.Min;
            if (p.Value > p.Max) p.Value = p.Max;
            SyncSlider(sv);
            _saveLater.Trigger();
            flyout.Hide();
        };
        flyout.ShowAt(sv.Label);
    }

    /// <summary>Evaluates a typed number (fractions, π and functions allowed) with the session.</summary>
    private double? TryNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var v = _s.Session.EvaluateExpression(text, approximate: true);
            return v is Calcy.Core.Expressions.NumberExpr { Value.IsReal: true } n && double.IsFinite(n.Value.Re) ? n.Value.Re : null;
        }
        catch (Calcy.Core.CalcException)
        {
            return null;
        }
    }
}
