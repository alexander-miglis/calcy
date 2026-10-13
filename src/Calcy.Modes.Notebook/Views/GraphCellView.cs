using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Graphing;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Controls;
using Calcy.UI.Graph;
using Calcy.UI.Math;
using Calcy.UI.Services;

namespace Calcy.Modes.Notebook.Views;

/// <summary>
/// A graph cell: an inline graph (pan, zoom, trace) over its own expressions, with sliders for free names. The
/// selected cell shows the expression list for editing; otherwise a compact legend.
/// </summary>
internal sealed class GraphCellView : CellView
{
    private readonly GraphCell _cell;
    private readonly GraphCanvas _canvas;
    private readonly Border _canvasHost;
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly WrapPanel _legend = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _sliders = new() { Spacing = 2 };
    private readonly TextBox _add;
    private readonly TextBlock _errors;
    private readonly StackPanel _editor;
    private readonly TextBlock _hint;
    private readonly List<(TextBox Box, Border Swatch)> _rowViews = [];
    private readonly Dictionary<string, (NbSlider Slider, TextBlock Value)> _sliderViews = [];

    public GraphCellView(NotebookView owner, GraphCell cell) : base(owner, cell)
    {
        _cell = cell;
        _canvas = new GraphCanvas
        {
            Session = cell.CellSession,
            Parameters = cell.Document?.Parameters,
            Viewport = cell.Viewport,
            Height = cell.Height,
            ShowPointsOfInterest = false,
        };
        Avalonia.Automation.AutomationProperties.SetName(_canvas, "Graph of " + string.Join(", ", cell.Expressions));
        _canvas.ViewportChanged += (_, e) => _cell.Viewport = e.NewViewport;
        _canvasHost = new Border { Child = _canvas, BorderThickness = new Thickness(0, 0, 0, 2) };
        _canvasHost.Res(BorderBrushProperty, "Calcy.Line");
        // The page scrolls with the wheel until the graph is clicked; then the wheel zooms the graph.
        _canvasHost.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (_canvas.IsFocused) return;
            Owner.ScrollBy(-e.Delta.Y * 60);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        _add = new TextBox
        {
            PlaceholderText = "+ add: y = x^2, (2, 3), x^2 + y^2 = 9, a = 2",
            FontSize = 15,
            MinHeight = 32,
            Padding = new Thickness(8, 3),
            BorderThickness = new Thickness(2),
        };
        _add.Res(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        Avalonia.Automation.AutomationProperties.SetName(_add, "Add a graph expression");
        _add.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(_add.Text)) return;
            e.Handled = true;
            _cell.AddExpression(_add.Text!);
            _add.Text = "";
        };
        _errors = Ui.Body("", 13, "Calcy.Err");
        _hint = Ui.Body("Drag to pan. Click the graph, then scroll to zoom; Shift and drag zooms into a box; T traces.", 12, "Calcy.Muted");
        _editor = new StackPanel { Spacing = 6, Children = { _rows, _add, _errors } };
        var lower = new StackPanel { Spacing = 8, Margin = new Thickness(12, 8, 12, 10), Children = { _editor, _legend, _sliders, _hint } };
        Body.Content = new StackPanel { Children = { _canvasHost, lower } };

        var png = Ui.Button("PNG", "Copy the graph as an image");
        png.Click += async (_, _) => await Owner.CopyGraphImageAsync(_canvas);
        AddAction(png);
        if (cell.Document is { } doc)
        {
            doc.Changed += OnDocumentChanged;
            doc.Parameters.ValuesChanged += OnParameterChanged;
        }
        SyncAll();
        ShowEditing(false);
        Refresh();
    }

    protected override string KindTitle => "Graph cell";

    /// <summary>The graph control.</summary>
    public GraphCanvas Canvas => _canvas;

    /// <summary>The expression boxes of the editing list.</summary>
    public IReadOnlyList<TextBox> ExpressionBoxes => [.. _rowViews.Select(r => r.Box)];

    /// <summary>The slider of a parameter, or null.</summary>
    public NbSlider? SliderFor(string name) => _sliderViews.TryGetValue(name, out var s) ? s.Slider : null;

    /// <summary>True while the graph samples in the background.</summary>
    public bool IsBusy => _canvas.IsEffectivelyVisible && _canvas.Bounds.Width >= 2 && _canvas.IsBusy;

    private GraphDocument? Doc => _cell.Document;

    private void OnDocumentChanged(object? sender, EventArgs e) => SyncAll();

    private void OnParameterChanged(object? sender, GraphParameter p)
    {
        if (_sliderViews.TryGetValue(p.Name, out var v))
        {
            v.Slider.Value = p.Value;
            v.Value.Text = Format(p.Value);
        }
    }

    private void SyncAll()
    {
        if (Doc is not { } doc) return;
        _canvas.Items = [.. doc.Items];
        _canvas.Session = _cell.CellSession;
        SyncRows(doc);
        SyncLegend(doc);
        SyncSliders(doc);
        var errors = doc.Items.Where(i => i.Kind == GraphKind.Error).Select(i => $"{(i.Text.Length > 30 ? i.Text[..27] + "..." : i.Text)}: {i.Error}").ToList();
        _errors.Text = string.Join("\n", errors);
        _errors.IsVisible = errors.Count > 0;
    }

    private void SyncRows(GraphDocument doc)
    {
        if (_rowViews.Count != doc.Items.Count)
        {
            _rows.Children.Clear();
            _rowViews.Clear();
            for (int i = 0; i < doc.Items.Count; i++)
            {
                int index = i;
                var box = new TextBox { FontSize = 15, MinHeight = 32, Padding = new Thickness(8, 3), BorderThickness = new Thickness(2) };
                box.Res(TextBox.FontFamilyProperty, "Calcy.Font.Math");
                Avalonia.Automation.AutomationProperties.SetName(box, $"Graph expression {i + 1}");
                box.TextChanged += (_, _) =>
                {
                    if (box.IsFocused && Doc is { } d && index < d.Items.Count && d.Items[index].Text != box.Text) _cell.ReplaceExpression(index, box.Text ?? "");
                };
                var swatch = new Border { Width = 18, Height = 18, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                swatch.Res(BorderBrushProperty, "Calcy.Line");
                var remove = Ui.IconButton("close", "Remove this expression");
                remove.Margin = new Thickness(6, 0, 0, 0);
                remove.Click += (_, _) => _cell.RemoveExpression(index);
                var row = new DockPanel();
                DockPanel.SetDock(swatch, Dock.Left);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(swatch);
                row.Children.Add(remove);
                row.Children.Add(box);
                _rows.Children.Add(row);
                _rowViews.Add((box, swatch));
            }
        }
        for (int i = 0; i < doc.Items.Count; i++)
        {
            var item = doc.Items[i];
            var (box, swatch) = _rowViews[i];
            if (!box.IsFocused && box.Text != item.Text) box.Text = item.Text;
            swatch.Res(BackgroundProperty, item.IsPlottable ? $"Calcy.G{item.ColorIndex % 5 + 1}" : "Calcy.Paper");
            box.Res(TextBox.BorderBrushProperty, item.Kind == GraphKind.Error ? "Calcy.Err" : "Calcy.Line");
            ToolTip.SetTip(box, item.Error ?? item.KindLabel);
        }
    }

    private void SyncLegend(GraphDocument doc)
    {
        _legend.Children.Clear();
        foreach (var item in doc.Items.Where(i => i.Kind != GraphKind.Empty))
        {
            var swatch = new Border { Width = 14, Height = 14, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            swatch.Res(BorderBrushProperty, "Calcy.Line").Res(BackgroundProperty, item.IsPlottable ? $"Calcy.G{item.ColorIndex % 5 + 1}" : "Calcy.Paper");
            Control label = item.Kind == GraphKind.Error
                ? Ui.Mono(item.Text, 14, "Calcy.Err")
                : new MathView { Text = item.Text, Session = _cell.CellSession, FontSize = 15, PreserveStructure = true, VerticalAlignment = VerticalAlignment.Center };
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 18, 2), Children = { swatch, label } };
            ToolTip.SetTip(entry, item.Error ?? item.KindLabel);
            _legend.Children.Add(entry);
        }
    }

    private void SyncSliders(GraphDocument doc)
    {
        var names = doc.Parameters.All.Select(p => p.Name).ToList();
        if (!names.SequenceEqual(_sliderViews.Keys))
        {
            _sliders.Children.Clear();
            _sliderViews.Clear();
            foreach (var p in doc.Parameters.All)
            {
                var param = p;
                var label = Ui.Mono(p.Name + " =", 16, bold: true);
                label.MinWidth = 44;
                var slider = new NbSlider { Minimum = p.Min, Maximum = p.Max, Step = p.Step, Value = p.Value, Width = 220, MinWidth = 120 };
                Avalonia.Automation.AutomationProperties.SetName(slider, "Slider for " + p.Name);
                var value = Ui.Mono(Format(p.Value), 16, bold: true);
                slider.UserChanged += (_, v) => param.Value = param.Snap(v);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { label, slider, value } };
                _sliders.Children.Add(row);
                _sliderViews[p.Name] = (slider, value);
            }
        }
        foreach (var p in doc.Parameters.All)
        {
            var (slider, value) = _sliderViews[p.Name];
            slider.Minimum = p.Min;
            slider.Maximum = p.Max;
            slider.Step = p.Step;
            slider.Value = p.Value;
            value.Text = Format(p.Value);
        }
        _sliders.IsVisible = _sliderViews.Count > 0;
    }

    private static string Format(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private void ShowEditing(bool editing)
    {
        _editor.IsVisible = editing;
        _hint.IsVisible = editing;
        _legend.IsVisible = !editing && _legend.Children.Count > 0;
    }

    protected override void OnSelectionChanged() => ShowEditing(IsSelected);

    protected override void OnCollapsedChanged()
    {
        _canvasHost.IsVisible = !Cell.Collapsed;
        if (!Cell.Collapsed) _canvas.Refresh();
    }

    public override bool FocusInput(bool atEnd = false)
    {
        Owner.Select(this, focus: false);
        ShowEditing(true);
        if (_rowViews.Count > 0 && !atEnd) _rowViews[0].Box.Focus();
        else _add.Focus();
        return true;
    }

    public override void Refresh()
    {
        base.Refresh();
        SyncAll();
        ShowEditing(IsSelected);
        _canvasHost.IsVisible = !Cell.Collapsed;
    }

    protected override IEnumerable<(string Header, Action Action)> MenuExtras() =>
    [
        ("Copy graph image", () => _ = Owner.CopyGraphImageAsync(_canvas)),
        ("Fit the y range", () => _canvas.Fit()),
        ("Open in Graphing", () => Owner.Services.Navigate("graphing", new PlotRequest([.. _cell.Expressions]))),
        ("Taller graph", () => SetHeight(_cell.Height + 80)),
        ("Shorter graph", () => SetHeight(_cell.Height - 80)),
    ];

    private void SetHeight(double h)
    {
        _cell.Height = h;
        _canvas.Height = _cell.Height;
    }

    public override void Detach()
    {
        base.Detach();
        if (Doc is { } doc)
        {
            doc.Changed -= OnDocumentChanged;
            doc.Parameters.ValuesChanged -= OnParameterChanged;
        }
        _canvas.Dispose();
    }
}
