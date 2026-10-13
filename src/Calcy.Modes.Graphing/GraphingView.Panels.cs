using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Core.Printing;
using Calcy.Graphing;
using Calcy.UI.Controls;

namespace Calcy.Modes.Graphing;

public sealed partial class GraphingView
{
    private sealed class ResultEntry
    {
        public required AnalysisTool Tool { get; init; }
        public string? First { get; init; }
        public string? Second { get; init; }
        public string? ItemName { get; init; }
        public required AnalysisResult Result { get; set; }
        public bool Shown { get; set; } = true;
    }

    private readonly List<ResultEntry> _results = [];
    private readonly StackPanel _cards = new() { Spacing = 9, Margin = new Thickness(10) };
    private readonly Grid _tableGrid = new();
    private TextBox _startBox = null!;
    private TextBox _stepBox = null!;
    private PixelToggle _exactToggle = null!;
    private TextBlock _tableHint = null!;
    private double _tableStart = -3;
    private double _tableStep = 0.5;
    private bool _tableExact;
    private const int TableRows = 13;
    private Debouncer? _tableLater;
    private Debouncer? _resultsLater;
    private PixelPanel _rightPanel = null!;

    // ------------------------------------------------------------------ analysis cards

    private Control BuildAnalysisPanel()
    {
        _rightPanel = new PixelPanel
        {
            Header = "Analysis",
            Content = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            VerticalAlignment = VerticalAlignment.Top,
        };
        Bind(_rightPanel, PixelPanel.HeaderBrushProperty, "Calcy.Sel");
        return _rightPanel;
    }

    /// <summary>The analysis results currently listed (newest last).</summary>
    public IReadOnlyList<AnalysisResult> Results => [.. _results.Select(r => r.Result)];

    /// <summary>Runs an analysis tool on the selected graph and adds its card (and its shading or tangent to the graph).</summary>
    public AnalysisResult? AddAnalysis(AnalysisTool tool, string? first, string? second = null)
    {
        var target = AnalysisTarget(tool);
        var result = RunTool(tool, first, second, target);
        if (result is null || result.IsError) return result;
        _results.Add(new ResultEntry { Tool = tool, First = first, Second = second, ItemName = target?.Name, Result = result });
        UpdateAnnotations();
        RefreshCards();
        return result;
    }

    private void ClearResults()
    {
        _results.Clear();
        UpdateAnnotations();
        RefreshCards();
    }

    private void UpdateAnnotations() =>
        _canvas.Annotations = [.. _results.Where(r => r.Shown).SelectMany(r => r.Result.Annotations)];

    private void RefreshResultsLater()
    {
        if (_results.Count == 0) return;
        _resultsLater ??= new Debouncer(TimeSpan.FromMilliseconds(120), () =>
        {
            foreach (var r in _results)
            {
                var item = _doc.Items.FirstOrDefault(i => i.Name == r.ItemName);
                var fresh = RunTool(r.Tool, r.First, r.Second, item);
                if (fresh is not null) r.Result = fresh;
            }
            UpdateAnnotations();
            RefreshCards();
        });
        _resultsLater.Trigger();
    }

    private static int KindOrder(PoiKind k) => k switch
    {
        PoiKind.Root => 0,
        PoiKind.Minimum or PoiKind.Maximum => 1,
        PoiKind.Intersection => 2,
        PoiKind.YIntercept => 3,
        _ => 4,
    };

    private void RefreshCards()
    {
        _cards.Children.Clear();
        var v = _canvas.Viewport;
        var pois = _canvas.VisiblePointsOfInterest
            .Where(p => v.Contains(p.X, p.Y) && (_canvas.FocusItem is not null))
            .OrderBy(p => KindOrder(p.Kind)).ThenBy(p => p.X).ToList();
        const int max = 10;
        foreach (var p in pois.Take(max))
        {
            bool selected = _canvas.SelectedPoint is { } s && s.Id == p.Id;
            var show = new PixelButton { Content = selected ? "Shown" : "Show on graph", Classes = { "small", "flat" }, FontSize = 10, MinHeight = 28, Padding = new Thickness(8, 2), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(show, "Show " + p.Title + " on the graph");
            var point = p;
            show.Click += (_, _) =>
            {
                _canvas.SelectedPoint = point;
                if (_view == GraphViewMode.Table) SetView(GraphViewMode.Split);
            };
            _cards.Children.Add(MakeCard(p.Title, p.Text, selected ? "Calcy.Sel" : "Calcy.Key", show));
        }
        if (pois.Count > max)
        {
            var more = new TextBlock { Text = $"{pois.Count - max} more points in view; zoom in to see them.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Bind(more, TextBlock.ForegroundProperty, "Calcy.Muted");
            _cards.Children.Add(more);
        }
        foreach (var r in _results)
        {
            var entry = r;
            var toggle = new PixelButton { Content = r.Shown ? "Hide" : "Show on graph", Classes = { "small", "flat" }, FontSize = 10, MinHeight = 28, Padding = new Thickness(8, 2), BorderThickness = new Thickness(2) };
            toggle.Click += (_, _) =>
            {
                entry.Shown = !entry.Shown;
                UpdateAnnotations();
                RefreshCards();
            };
            var icon = new PixelIcon { Kind = "close", Width = 12, Height = 12 };
            Bind(icon, PixelIcon.ForegroundProperty, "Calcy.Ink");
            var remove = new PixelButton { Content = icon, Classes = { "small", "flat", "paper" }, Width = 28, MinWidth = 28, MinHeight = 28, Height = 28, Padding = new Thickness(0), BorderThickness = new Thickness(2) };
            AutomationProperties.SetName(remove, "Remove " + r.Result.Title);
            remove.Click += (_, _) =>
            {
                _results.Remove(entry);
                UpdateAnnotations();
                RefreshCards();
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { toggle, remove } };
            _cards.Children.Add(MakeCard(r.Result.Title, r.Result.Text, "Calcy.Head", r.Result.Annotations.Count > 0 ? buttons : remove));
        }
        if (_cards.Children.Count == 0)
        {
            var text = SelectedItem is { IsFunction: true }
                ? "No roots, extrema or intersections in view. Pan or zoom to find some, or use Analyse for values, integrals and slopes."
                : "Select a function to see its roots, extrema and intersections here. Analyse adds values, integrals, slopes and more.";
            var hint = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
            Bind(hint, TextBlock.ForegroundProperty, "Calcy.Muted");
            Bind(hint, TextBlock.FontFamilyProperty, "Calcy.Font.Body");
            _cards.Children.Add(hint);
        }
    }

    private static Card MakeCard(string title, string text, string headerToken, Control? action)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(MathText(text, 16, bold: true));
        if (action is not null) body.Children.Add(action);
        var card = new Card { Title = title, Content = body, Padding = new Thickness(10, 6) };
        Bind(card, Card.HeaderBrushProperty, headerToken);
        AutomationProperties.SetName(card, title + ": " + text);
        return card;
    }

    // ------------------------------------------------------------------ table

    private Control BuildTablePanel()
    {
        TextBox Field(string name, double value)
        {
            var tb = new TextBox { Text = GraphFormat.Number(value, 6), Width = 76, FontSize = 14, MinHeight = 32 };
            AutomationProperties.SetName(tb, name);
            tb.LostFocus += (_, _) => ApplyTableFields();
            tb.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) ApplyTableFields();
            };
            return tb;
        }
        _startBox = Field("Table start", _tableStart);
        _stepBox = Field("Table step", _tableStep);
        _exactToggle = new PixelToggle { Content = "EXACT", IsChecked = _tableExact, FontSize = 11, MinHeight = 32, Padding = new Thickness(8, 0) };
        ToolTip.SetTip(_exactToggle, "Show exact values (fractions, radicals) for rational x");
        _exactToggle.Click += (_, _) =>
        {
            _tableExact = _exactToggle.IsChecked == true;
            RefreshTable();
            _saveLater.Trigger();
        };
        var csv = new PixelButton { Content = "CSV", Classes = { "small", "flat" }, FontSize = 11, MinHeight = 32, BorderThickness = new Thickness(2) };
        ToolTip.SetTip(csv, "Copy the table as CSV");
        csv.Click += (_, _) => _ = CopyAsync(CurrentTable().ToCsv());
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        Control Group(params Control[] parts)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 10, 6) };
            foreach (var p in parts) g.Children.Add(p);
            return g;
        }
        controls.Children.Add(Group(Label("START", 11), _startBox));
        controls.Children.Add(Group(Label("STEP", 11), _stepBox));
        controls.Children.Add(Group(_exactToggle, csv));
        _tableHint = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        Bind(_tableHint, TextBlock.ForegroundProperty, "Calcy.Muted");
        Bind(_tableHint, TextBlock.FontFamilyProperty, "Calcy.Font.Body");
        var frame = new Border { Child = _tableGrid, BorderThickness = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        Bind(frame, Border.BorderBrushProperty, "Calcy.Line");
        Bind(frame, Border.BackgroundProperty, "Calcy.Card");
        var scroll = new ScrollViewer { Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(_tableHint, Dock.Bottom);
        dock.Children.Add(controls);
        dock.Children.Add(_tableHint);
        dock.Children.Add(scroll);
        return dock;
    }

    private void ApplyTableFields()
    {
        if (TryNumber(_startBox.Text) is double s) _tableStart = s;
        if (TryNumber(_stepBox.Text) is double st && st != 0) _tableStep = st;
        RefreshTable();
        _saveLater.Trigger();
    }

    private void RefreshTableLater()
    {
        _tableLater ??= new Debouncer(TimeSpan.FromMilliseconds(100), RefreshTable);
        _tableLater.Trigger();
    }

    /// <summary>The table for the current selection: the selected parametric or polar curve, otherwise every visible function.</summary>
    private GraphTable CurrentTable()
    {
        var sel = SelectedItem;
        if (sel is { HasRange: true, IsVisible: true })
        {
            double step = (sel.TMax - sel.TMin) / 12;
            return GraphTableBuilder.Parametric(sel, _doc.ParameterValues(sel), sel.TMin, step, TableRows);
        }
        return GraphTableBuilder.Functions(_doc.Items, _doc.ParameterSource, _tableStart, _tableStep, TableRows, _tableExact, _s.Session);
    }

    private static string Subscript(string name)
    {
        const string sub = "₀₁₂₃₄₅₆₇₈₉";
        var chars = name.ToCharArray();
        int i = chars.Length;
        while (i > 0 && char.IsDigit(chars[i - 1])) i--;
        if (i == 0 || i == chars.Length) return name;
        for (int k = i; k < chars.Length; k++) chars[k] = sub[chars[k] - '0'];
        return new string(chars);
    }

    private string Header(GraphTable t, int column)
    {
        string name = t.Headers[column];
        if (t.KeyHeader != "x") return name;
        var item = _doc.Items.FirstOrDefault(i => i.Name == name);
        if (item is { Members.Count: 1 } && item.Members[0].Main is { } main)
            return $"{Subscript(name)} = {GraphFormat.Typographic(ExprPrinter.ToText(main.Expression))}";
        return Subscript(name);
    }

    private void RefreshTable()
    {
        if (_startBox is null) return;
        var t = CurrentTable();
        _tableGrid.Children.Clear();
        _tableGrid.ColumnDefinitions.Clear();
        _tableGrid.RowDefinitions.Clear();
        int cols = t.Headers.Count + 1;
        bool compact = _view == GraphViewMode.Split;
        for (int c = 0; c < cols; c++)
            _tableGrid.ColumnDefinitions.Add(new ColumnDefinition(compact ? GridLength.Auto : c == 0 ? new GridLength(0.7, GridUnitType.Star) : GridLength.Star));
        for (int r = 0; r <= t.Rows.Count; r++) _tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        void Cell(int r, int c, string text, bool header, double key = double.NaN)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = header ? (compact ? 10 : 12) : (compact ? 13 : 15),
                FontWeight = header ? FontWeight.Bold : FontWeight.Normal,
                TextAlignment = header ? TextAlignment.Center : TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Bind(tb, TextBlock.FontFamilyProperty, header ? "Calcy.Font.Label" : "Calcy.Font.Math");
            Bind(tb, TextBlock.ForegroundProperty, "Calcy.Ink");
            if (header) ToolTip.SetTip(tb, text);
            var border = new Border
            {
                Child = tb,
                Padding = header ? new Thickness(compact ? 4 : 6) : compact ? new Thickness(5, 4) : new Thickness(8, 5),
                BorderThickness = new Thickness(0, 0, c == cols - 1 ? 0 : 2, r == t.Rows.Count ? 0 : 2),
            };
            Bind(border, Border.BorderBrushProperty, "Calcy.Line");
            if (header) Bind(border, Border.BackgroundProperty, "Calcy.Head");
            else
            {
                border.Background = Brushes.Transparent;
                border.Cursor = new Cursor(StandardCursorType.Hand);
                double k = key;
                border.PointerPressed += (_, _) => JumpTo(t, k);
            }
            Grid.SetRow(border, r);
            Grid.SetColumn(border, c);
            _tableGrid.Children.Add(border);
        }
        Cell(0, 0, t.KeyHeader, true);
        for (int c = 0; c < t.Headers.Count; c++) Cell(0, c + 1, Header(t, c), true);
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var row = t.Rows[r];
            Cell(r + 1, 0, row.KeyText, false, row.Key);
            for (int c = 0; c < row.Cells.Count; c++) Cell(r + 1, c + 1, row.Cells[c].Text, false, row.Key);
        }
        if (t.Headers.Count == 0)
            _tableHint.Text = "Add a function such as y = x² to see its table.";
        else
            _tableHint.Text = t.KeyHeader == "x"
                ? $"Start {GraphFormat.Number(_tableStart, 6, true)}, step {GraphFormat.Number(_tableStep, 6, true)}. Click a row to jump the trace there."
                : $"{t.KeyHeader} from {GraphFormat.Number(SelectedItem?.TMin ?? 0, 4, true)} to {GraphFormat.Number(SelectedItem?.TMax ?? 0, 4, true)}. Click a row to jump the trace there.";
        _startBox.IsEnabled = _stepBox.IsEnabled = _exactToggle.IsEnabled = t.KeyHeader == "x";
    }

    private void JumpTo(GraphTable t, double key)
    {
        var item = t.KeyHeader == "x" ? (SelectedItem is { IsFunction: true } s ? s : _doc.Items.FirstOrDefault(i => i.IsVisible && i.IsFunction)) : SelectedItem;
        if (item is null) return;
        if (_view == GraphViewMode.Table) SetView(GraphViewMode.Split);
        _canvas.TraceAt(item, key);
    }
}
