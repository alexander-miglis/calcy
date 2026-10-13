using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Modes.Statistics.Logic;

namespace Calcy.Modes.Statistics.Views;

/// <summary>A decorator with a dashed outline (the column formula box of the design).</summary>
public sealed class DashedBorder : Decorator
{
    /// <summary>Outline colour.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<DashedBorder, IBrush?>(nameof(Stroke));

    static DashedBorder() => AffectsRender<DashedBorder>(StrokeProperty);

    /// <summary>Creates the border in the muted colour.</summary>
    public DashedBorder() => this.Token(StrokeProperty, "Calcy.Muted");

    /// <summary>Outline colour.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Stroke is not { } s) return;
        var r = new Rect(Bounds.Size).Deflate(1);
        context.DrawRectangle(null, new Pen(s, 2, new DashStyle([2, 1.5], 0), PenLineCap.Flat), r);
    }
}

/// <summary>
/// A framed plot that is drawn by the grapher (zoomable, with the fitted curve) or, for many points, by the statistics
/// chart, whichever the analysis produced.
/// </summary>
public sealed class PlotHost : Border
{
    /// <summary>Creates the frame around a grapher and a chart of the same height.</summary>
    public PlotHost(Calcy.UI.Graph.GraphCanvas canvas, double height)
    {
        Canvas = canvas;
        Chart = new StatChart { Height = height, IsVisible = false };
        Child = new Panel { Children = { canvas, Chart } };
        BorderThickness = new Thickness(2);
        this.Token(BackgroundProperty, "Calcy.Card");
        this.Token(BorderBrushProperty, "Calcy.Line");
    }

    /// <summary>The grapher.</summary>
    public Calcy.UI.Graph.GraphCanvas Canvas { get; }

    /// <summary>The chart for many points.</summary>
    public StatChart Chart { get; }

    /// <summary>Shows the grapher plot when there is one, else the chart scene.</summary>
    public void Show(Calcy.Graphing.Viewport? viewport, IReadOnlyList<Calcy.Graphing.GraphAnnotation>? annotations, IReadOnlyList<Calcy.Graphing.GraphItem>? items, ChartScene? scene)
    {
        bool graph = items is not null && viewport is not null;
        Canvas.IsVisible = graph || scene is null;
        Chart.IsVisible = !Canvas.IsVisible;
        if (graph)
        {
            Canvas.Viewport = viewport!.Value;
            Canvas.Annotations = annotations ?? [];
            Canvas.Items = items;
        }
        else
        {
            Canvas.Items = [];
            Canvas.Annotations = [];
        }
        Chart.Scene = scene;
    }
}

/// <summary>The value tiles of the design: a grid of boxes with a label and a bold value, four to a row.</summary>
public sealed class TileGrid : Grid
{
    private readonly int _columns;
    private readonly double _valueSize;

    /// <summary>Creates the grid.</summary>
    public TileGrid(int columns = 4, double valueSize = 18)
    {
        _columns = columns;
        _valueSize = valueSize;
        ColumnSpacing = 8;
        RowSpacing = 8;
        for (int i = 0; i < columns; i++) ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
    }

    /// <summary>The tiles shown.</summary>
    public IReadOnlyList<Tile> Tiles { get; private set; } = [];

    /// <summary>Shows tiles.</summary>
    public void SetTiles(IReadOnlyList<Tile> tiles)
    {
        Tiles = tiles;
        Children.Clear();
        RowDefinitions.Clear();
        int rows = (tiles.Count + _columns - 1) / _columns;
        for (int r = 0; r < rows; r++) RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var label = Ui.Label(t.Label, 11);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var value = Ui.Math(t.Value, _valueSize, bold: true);
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            var box = Ui.Frame(new StackPanel { Spacing = 2, Children = { label, value } }, 3, t.Highlight ? "Calcy.Sel" : "Calcy.Card");
            box.Padding = new Thickness(8, 5, 6, 6);
            if (t.Tip.Length > 0) ToolTip.SetTip(box, t.Tip);
            AutomationProperties.SetName(box, $"{t.Label}: {t.Value}");
            SetRow(box, i / _columns);
            SetColumn(box, i % _columns);
            Children.Add(box);
        }
    }

    /// <summary>The value shown under a label, or null (for tests).</summary>
    public string? ValueOf(string label) => Tiles.FirstOrDefault(t => t.Label == label)?.Value;
}

/// <summary>A row of toggles that behave like radio buttons (tabs, segmented controls).</summary>
public sealed class ChoiceRow<T> : WrapPanel where T : notnull
{
    private readonly List<(T Value, Calcy.UI.Controls.PixelToggle Toggle)> _items = [];
    private T _selected;

    /// <summary>Raised when the user picks another item.</summary>
    public event Action<T>? Picked;

    /// <summary>Creates the row with the given items (value, label, tooltip).</summary>
    public ChoiceRow(IEnumerable<(T Value, string Label, string Tip)> items, T selected, bool shadow = false, double fontSize = 11, double minHeight = 34)
    {
        ItemSpacing = 6;
        LineSpacing = 6;
        _selected = selected;
        foreach (var (value, label, tip) in items)
        {
            var t = Ui.Toggle(label, tip, shadow);
            t.FontSize = fontSize;
            t.MinHeight = minHeight;
            var v = value;
            t.Click += (_, _) => Select(v, raise: true);
            _items.Add((value, t));
            Children.Add(t);
        }
        Sync();
    }

    /// <summary>The selected value.</summary>
    public T Selected => _selected;

    /// <summary>The toggle of an item.</summary>
    public Calcy.UI.Controls.PixelToggle Toggle(T value) => _items.First(i => EqualityComparer<T>.Default.Equals(i.Value, value)).Toggle;

    /// <summary>Selects an item.</summary>
    public void Select(T value, bool raise = false)
    {
        bool changed = !EqualityComparer<T>.Default.Equals(_selected, value);
        _selected = value;
        Sync();
        if (raise && changed) Picked?.Invoke(value);
    }

    private void Sync()
    {
        foreach (var (v, t) in _items) t.IsChecked = EqualityComparer<T>.Default.Equals(v, _selected);
    }
}
