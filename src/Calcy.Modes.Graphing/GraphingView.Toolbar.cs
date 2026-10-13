using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Calcy.Core.Expressions;
using Calcy.Graphing;
using Calcy.UI.Controls;
using Calcy.UI.Graph;

namespace Calcy.Modes.Graphing;

/// <summary>The analysis tools offered by the Analyse menu.</summary>
public enum AnalysisTool
{
    /// <summary>Y-CAL: value at x.</summary>
    ValueAt,

    /// <summary>X-CAL: every x for a given y.</summary>
    SolveForX,

    /// <summary>Definite integral with shading.</summary>
    Integral,

    /// <summary>Area between the selected graph and the next function graph.</summary>
    AreaBetween,

    /// <summary>dy/dx and the tangent line.</summary>
    Slope,

    /// <summary>Arc length.</summary>
    ArcLength,

    /// <summary>Distance between two points.</summary>
    Distance,
}

public sealed partial class GraphingView
{
    private TextBlock _hint = null!;

    private Control BuildGraphPanel()
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var mode in new[] { GraphViewMode.Graph, GraphViewMode.Table, GraphViewMode.Split })
        {
            var tab = new PixelToggle
            {
                Content = mode.ToString(),
                MinHeight = 38,
                Padding = new Thickness(9, 0),
                Margin = new Thickness(0, 0, -2, 0),
                FontSize = 12,
            };
            AutomationProperties.SetName(tab, mode + " view");
            var m = mode;
            tab.Click += (_, _) => SetView(m);
            _viewTabs.Add(tab);
            tabs.Children.Add(tab);
        }

        var zoomIn = ToolButton("+", "Zoom in");
        zoomIn.FontSize = 16;
        zoomIn.Click += (_, _) => _canvas.ZoomIn();
        var zoomOut = ToolButton("−", "Zoom out");
        zoomOut.FontSize = 16;
        zoomOut.Click += (_, _) => _canvas.ZoomOut();
        var fit = ToolButton("Fit", "Fit the y range to the graphs");
        fit.Click += (_, _) => _canvas.Fit();
        var square = ToolButton("Square", "Equal scale on both axes");
        square.Click += (_, _) => _canvas.Square();
        var trig = ToolButton("Trig", "Trig window with π ticks");
        trig.Click += (_, _) =>
        {
            _canvas.TickStyle = TickStyle.Pi;
            _canvas.SetViewport(Viewport.Trig(_s.Settings.Angle));
        };
        var window = ToolButton("Window", "Set the exact window, ticks and t or θ range");
        var windowFlyout = new Flyout { Content = new Border(), Placement = PlacementMode.BottomEdgeAlignedRight };
        windowFlyout.Opening += (_, _) => windowFlyout.Content = WindowEditor(windowFlyout);
        window.Flyout = windowFlyout;
        var analyse = ToolButton("Analyse ▾", "Analysis tools: value, x for y, integral, area, slope, length, distance");
        analyse.Classes.Remove("paper");
        analyse.Classes.Add("sel");
        analyse.Flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight, ItemsSource = AnalyseMenu() };
        var export = ToolButton("Export", "Save the graph as PNG or SVG, or the table as CSV");
        export.Flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight, ItemsSource = ExportMenu() };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in new Control[] { zoomIn, zoomOut, fit, square, trig, window, analyse, export })
        {
            b.Margin = new Thickness(4, 0, 0, 0);
            tools.Children.Add(b);
        }
        var bar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(tabs, Dock.Left);
        DockPanel.SetDock(tools, Dock.Right);
        bar.Children.Add(tabs);
        bar.Children.Add(tools);
        var toolbar = new Border { Child = bar, Padding = new Thickness(10, 8), BorderThickness = new Thickness(0, 0, 0, 4) };
        Bind(toolbar, Border.BackgroundProperty, "Calcy.Key");
        Bind(toolbar, Border.BorderBrushProperty, "Calcy.Line");

        _hint = new TextBlock
        {
            Text = "Drag to pan, scroll to zoom. Grey dots are points of interest; pick one in the cards to see it.",
            FontSize = 13,
            Margin = new Thickness(12, 0, 12, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Bind(_hint, TextBlock.ForegroundProperty, "Calcy.Muted");
        Bind(_hint, TextBlock.FontFamilyProperty, "Calcy.Font.Body");

        var dock = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Bottom);
        dock.Children.Add(toolbar);
        dock.Children.Add(_hint);
        dock.Children.Add(_content);
        return new PixelPanel { Content = dock };
    }

    // ------------------------------------------------------------------ window dialog

    private Control WindowEditor(Flyout flyout)
    {
        var v = _canvas.Viewport;
        TextBox Field(string name, double value)
        {
            var tb = new TextBox { Text = GraphFormat.Number(value, 8), Width = 120, FontSize = 14, MinHeight = 32 };
            AutomationProperties.SetName(tb, name);
            return tb;
        }
        var xmin = Field("x min", v.XMin);
        var xmax = Field("x max", v.XMax);
        var ymin = Field("y min", v.YMin);
        var ymax = Field("y max", v.YMax);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
        void Row(int r, string label, Control a, Control b)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Label(label, 11);
            Grid.SetRow(l, r);
            Grid.SetRow(a, r);
            Grid.SetRow(b, r);
            Grid.SetColumn(a, 1);
            Grid.SetColumn(b, 2);
            grid.Children.Add(l);
            grid.Children.Add(a);
            grid.Children.Add(b);
        }
        Row(0, "X", xmin, xmax);
        Row(1, "Y", ymin, ymax);
        TextBox? tmin = null, tmax = null;
        var sel = SelectedItem;
        if (sel is { HasRange: true })
        {
            tmin = Field($"{sel.Variable} min", sel.TMin);
            tmax = Field($"{sel.Variable} max", sel.TMax);
            Row(2, (sel.Variable ?? "t").ToUpperInvariant(), tmin, tmax);
        }
        var pi = new CheckBox { Content = "π ticks on the x-axis", IsChecked = _canvas.TickStyle == TickStyle.Pi };
        var presets = new WrapPanel();
        void Preset(string name, Func<Viewport> make)
        {
            var b = new PixelButton { Content = name, Classes = { "small", "card", "flat" }, Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(2) };
            b.Click += (_, _) =>
            {
                _canvas.TickStyle = TickStyle.Decimal;
                _canvas.SetViewport(make());
                flyout.Hide();
            };
            presets.Children.Add(b);
        }
        var size = _canvas.Bounds.Size;
        Preset("Standard", () => Viewport.Standard.Square(size.Width, size.Height));
        Preset("Decimal", () => Viewport.Decimal(size.Width, size.Height));
        Preset("Integer", () => Viewport.Integer(size.Width, size.Height));
        var previous = new PixelButton { Content = "Previous", Classes = { "small", "card", "flat" }, Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(2) };
        previous.Click += (_, _) =>
        {
            _canvas.GoBack();
            flyout.Hide();
        };
        presets.Children.Add(previous);
        var apply = new PixelButton { Content = "APPLY", Classes = { "small", "primary" }, HorizontalAlignment = HorizontalAlignment.Right };
        var message = Label("", 11, "Calcy.Err");
        apply.Click += (_, _) =>
        {
            double? a = TryNumber(xmin.Text), b = TryNumber(xmax.Text), c = TryNumber(ymin.Text), d = TryNumber(ymax.Text);
            if (a is not double x0 || b is not double x1 || c is not double y0 || d is not double y1 || x1 <= x0 || y1 <= y0)
            {
                message.Text = "Each max must be larger than its min";
                return;
            }
            _canvas.TickStyle = pi.IsChecked == true ? TickStyle.Pi : TickStyle.Decimal;
            _canvas.SetViewport(new Viewport(x0, x1, y0, y1));
            if (sel is not null && tmin is not null && tmax is not null && TryNumber(tmin.Text) is double t0 && TryNumber(tmax.Text) is double t1 && t1 > t0)
            {
                sel.TMin = t0;
                sel.TMax = t1;
                _canvas.Refresh();
            }
            _saveLater.Trigger();
            flyout.Hide();
        };
        return new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(14),
            Children = { Label("WINDOW", 13), grid, pi, Label("PRESETS", 11, "Calcy.Muted"), presets, message, apply },
        };
    }

    // ------------------------------------------------------------------ analysis

    private IEnumerable<MenuItem> AnalyseMenu()
    {
        MenuItem Item(string header, AnalysisTool tool) => new() { Header = header, Command = new Relay(() => ShowToolEditor(tool)) };
        yield return Item("Value at x (Y-CAL)", AnalysisTool.ValueAt);
        yield return Item("x for a given y (X-CAL)", AnalysisTool.SolveForX);
        yield return Item("∫ Integral from a to b", AnalysisTool.Integral);
        yield return Item("Area between curves", AnalysisTool.AreaBetween);
        yield return Item("dy/dx and tangent", AnalysisTool.Slope);
        yield return Item("Arc length", AnalysisTool.ArcLength);
        yield return Item("Distance between points", AnalysisTool.Distance);
        yield return new MenuItem { Header = "Trace (T)", Command = new Relay(() => { _canvas.IsTracing = !_canvas.IsTracing; _canvas.Focus(); }) };
        yield return new MenuItem { Header = "Clear results", Command = new Relay(ClearResults) };
    }

    private void ShowToolEditor(AnalysisTool tool)
    {
        var item = AnalysisTarget(tool);
        var (first, second, hint) = tool switch
        {
            AnalysisTool.ValueAt => ("x", (string?)null, "the value of the graph at x"),
            AnalysisTool.SolveForX => ("y", null, "every x in view where the graph equals y"),
            AnalysisTool.Integral => ("from", "to", "the signed area under the graph"),
            AnalysisTool.AreaBetween => ("from", "to", "leave both empty to use the crossing points"),
            AnalysisTool.Slope => ("x", null, "the slope and the tangent line"),
            AnalysisTool.ArcLength => ("from", "to", "the length of the curve"),
            _ => ("point 1", "point 2", "for example (0, 0) and (3, 4)"),
        };
        var a = new TextBox { Width = 130, FontSize = 14, MinHeight = 32, Text = DefaultInput(tool, 0) };
        var b = new TextBox { Width = 130, FontSize = 14, MinHeight = 32, Text = DefaultInput(tool, 1), IsVisible = second is not null };
        AutomationProperties.SetName(a, first);
        AutomationProperties.SetName(b, second ?? "");
        var message = Label("", 11, "Calcy.Err");
        var run = new PixelButton { Content = "CALCULATE", Classes = { "small", "primary" }, HorizontalAlignment = HorizontalAlignment.Right };
        var target = item is null ? "Select a function row first" : $"on {item.Name}: {item.Text.Trim()}";
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
        var la = Label(first.ToUpperInvariant(), 11);
        var lb = Label((second ?? "").ToUpperInvariant(), 11);
        lb.IsVisible = second is not null;
        Grid.SetColumn(a, 1);
        Grid.SetRow(lb, 1);
        Grid.SetRow(b, 1);
        Grid.SetColumn(b, 1);
        fields.Children.Add(la);
        fields.Children.Add(a);
        fields.Children.Add(lb);
        fields.Children.Add(b);
        var info = new TextBlock { Text = $"{target}\n{hint}", FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        Bind(info, TextBlock.ForegroundProperty, "Calcy.Muted");
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(14), Width = 280, Children = { Label(ToolName(tool), 13), info, fields, message, run } };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
        run.Click += (_, _) =>
        {
            var result = AddAnalysis(tool, a.Text, b.IsVisible ? b.Text : null);
            if (result is null || result.IsError)
            {
                message.Text = result?.Text ?? "Enter numbers, for example -1 or pi/2";
                return;
            }
            flyout.Hide();
        };
        flyout.ShowAt(_canvas);
    }

    private static string ToolName(AnalysisTool t) => t switch
    {
        AnalysisTool.ValueAt => "VALUE AT X",
        AnalysisTool.SolveForX => "X FOR A GIVEN Y",
        AnalysisTool.Integral => "∫ INTEGRAL",
        AnalysisTool.AreaBetween => "AREA BETWEEN CURVES",
        AnalysisTool.Slope => "DY/DX AND TANGENT",
        AnalysisTool.ArcLength => "ARC LENGTH",
        _ => "DISTANCE",
    };

    private string DefaultInput(AnalysisTool tool, int field)
    {
        var v = _canvas.Viewport;
        return (tool, field) switch
        {
            (AnalysisTool.ValueAt or AnalysisTool.Slope, 0) => "0",
            (AnalysisTool.SolveForX, 0) => "0",
            (AnalysisTool.Integral or AnalysisTool.ArcLength, 0) => GraphFormat.Number(Math.Round(v.XMin + v.Width / 4)),
            (AnalysisTool.Integral or AnalysisTool.ArcLength, 1) => GraphFormat.Number(Math.Round(v.XMax - v.Width / 4)),
            (AnalysisTool.Distance, 0) => "(0, 0)",
            (AnalysisTool.Distance, 1) => "(3, 4)",
            _ => "",
        };
    }

    private GraphItem? AnalysisTarget(AnalysisTool tool)
    {
        var sel = SelectedItem;
        if (tool == AnalysisTool.Distance) return sel;
        if (tool == AnalysisTool.ArcLength && sel is { IsPlottable: true } && (sel.IsFunction || sel.HasRange)) return sel;
        if (sel is { IsFunction: true }) return sel;
        return _doc.Items.FirstOrDefault(i => i.IsVisible && i.IsFunction);
    }

    /// <summary>Runs an analysis tool on the selected graph with typed inputs; returns null when the inputs are not numbers.</summary>
    public AnalysisResult? RunTool(AnalysisTool tool, string? first, string? second, GraphItem? target = null)
    {
        var item = target ?? AnalysisTarget(tool);
        if (tool == AnalysisTool.Distance)
        {
            var p1 = TryPoint(first);
            var p2 = TryPoint(second);
            return p1 is { } q1 && p2 is { } q2 ? GraphAnalysis.Distance(q1, q2, item?.ColorIndex ?? 0) : null;
        }
        if (item is null) return new AnalysisResult { Title = ToolName(tool), Text = "Add a function graph first", IsError = true };
        var p = _doc.ParameterValues(item);
        var view = new ViewTransform(_canvas.Viewport, Math.Max(1, _canvas.Bounds.Width), Math.Max(1, _canvas.Bounds.Height));
        double? a = TryNumber(first), b = TryNumber(second);
        switch (tool)
        {
            case AnalysisTool.ValueAt when a is double x:
                return GraphAnalysis.ValueAt(item, p, x, _s.Session);
            case AnalysisTool.SolveForX when a is double y:
                return GraphAnalysis.SolveForX(item, p, y, _canvas.Viewport.XMin, _canvas.Viewport.XMax, _s.Session);
            case AnalysisTool.Integral when a is double lo && b is double hi:
                return GraphAnalysis.Integral(item, p, lo, hi, _s.Session, 0, view);
            case AnalysisTool.Slope when a is double x0:
                return GraphAnalysis.Slope(item, p, x0, _s.Session);
            case AnalysisTool.ArcLength when a is double lo2 && b is double hi2:
                return GraphAnalysis.ArcLength(item, p, lo2, hi2, 0, view);
            case AnalysisTool.AreaBetween:
            {
                int idx = _doc.Items.ToList().IndexOf(item);
                var other = _doc.Items.Skip(idx + 1).Concat(_doc.Items.Take(Math.Max(0, idx))).FirstOrDefault(i => i.IsVisible && i.IsFunction && i != item);
                if (other is null) return new AnalysisResult { Title = "Area between curves", Text = "Add a second function graph", IsError = true };
                var q = _doc.ParameterValues(other);
                if (a is double from && b is double to) return GraphAnalysis.AreaBetween(item, p, other, q, from, to, 0, 0, view);
                var crossings = _canvas.PointsOfInterest.Where(pt => pt.Kind == PoiKind.Intersection &&
                    ((pt.Item == item && pt.Other == other) || (pt.Item == other && pt.Other == item))).Select(pt => pt.X).OrderBy(x1 => x1).ToList();
                if (crossings.Count < 2) return new AnalysisResult { Title = "Area between curves", Text = "Enter from and to (fewer than two crossings in view)", IsError = true };
                return GraphAnalysis.AreaBetween(item, p, other, q, crossings[0], crossings[1], 0, 0, view);
            }
            default:
                return null;
        }
    }

    private Point2? TryPoint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var v = _s.Session.EvaluateExpression(text, approximate: true);
            if (v is TupleExpr { Items.Length: 2 } t && t.Items[0] is NumberExpr { Value.IsReal: true } x && t.Items[1] is NumberExpr { Value.IsReal: true } y)
                return new Point2(x.Value.Re, y.Value.Re);
        }
        catch (Calcy.Core.CalcException)
        {
            // Not a point.
        }
        return null;
    }

    private void OnPointPicked(object? sender, PointPickedEventArgs e)
    {
        if (e.Item is { } item)
        {
            int i = _doc.Items.ToList().IndexOf(item);
            if (i >= 0 && i != _selected) Select(i);
        }
    }

    // ------------------------------------------------------------------ export

    private IEnumerable<MenuItem> ExportMenu()
    {
        yield return new MenuItem { Header = "Save graph as PNG…", Command = new Relay(() => _ = ExportAsync("png")) };
        yield return new MenuItem { Header = "Save graph as SVG…", Command = new Relay(() => _ = ExportAsync("svg")) };
        yield return new MenuItem { Header = "Save table as CSV…", Command = new Relay(() => _ = ExportAsync("csv")) };
        yield return new MenuItem { Header = "Copy SVG", Command = new Relay(() => _ = CopyAsync(_canvas.ExportSvg())) };
        yield return new MenuItem { Header = "Copy table (CSV)", Command = new Relay(() => _ = CopyAsync(CurrentTable().ToCsv())) };
    }

    private async Task CopyAsync(string text)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null) return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clip, text);
        _s.ShowStatus("Copied to the clipboard");
    }

    private async Task ExportAsync(string kind)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanSave) return;
        var type = kind switch
        {
            "png" => new FilePickerFileType("PNG image") { Patterns = ["*.png"], MimeTypes = ["image/png"] },
            "svg" => new FilePickerFileType("SVG image") { Patterns = ["*.svg"], MimeTypes = ["image/svg+xml"] },
            _ => new FilePickerFileType("CSV table") { Patterns = ["*.csv"], MimeTypes = ["text/csv"] },
        };
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            SuggestedFileName = kind == "csv" ? "table.csv" : $"graph.{kind}",
            DefaultExtension = kind,
            FileTypeChoices = [type],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            if (kind == "png") _canvas.SavePng(stream, 2);
            else
            {
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(kind == "svg" ? _canvas.ExportSvg() : CurrentTable().ToCsv());
            }
            _s.ShowStatus($"Saved {file.Name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _s.ShowStatus("Could not save: " + ex.Message);
        }
    }

    /// <summary>A minimal command for menu items.</summary>
    private sealed class Relay(Action action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
