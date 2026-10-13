using Avalonia;
using Avalonia.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>
/// Lays children out side by side when they fit (each at least its <see cref="MinColumnWidthProperty"/>, sharing the
/// rest by <see cref="WeightProperty"/>), and stacks them full width when they do not, like the design's flex-wrap rows.
/// </summary>
public sealed class FlowColumns : Panel
{
    /// <summary>The smallest width of a child when side by side.</summary>
    public static readonly AttachedProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.RegisterAttached<FlowColumns, Control, double>("MinColumnWidth", 200);

    /// <summary>How much of the spare width a child takes when side by side.</summary>
    public static readonly AttachedProperty<double> WeightProperty =
        AvaloniaProperty.RegisterAttached<FlowColumns, Control, double>("Weight", 1);

    /// <summary>Space between the columns (and between stacked rows).</summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<FlowColumns, double>(nameof(Gap), 20);

    static FlowColumns()
    {
        AffectsMeasure<FlowColumns>(GapProperty);
        AffectsParentMeasure<FlowColumns>(MinColumnWidthProperty, WeightProperty);
    }

    /// <summary>Space between the columns.</summary>
    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Sets a child's minimum column width.</summary>
    public static void SetMinColumnWidth(Control c, double v) => c.SetValue(MinColumnWidthProperty, v);

    /// <summary>Sets a child's weight.</summary>
    public static void SetWeight(Control c, double v) => c.SetValue(WeightProperty, v);

    /// <summary>True when the children are currently side by side.</summary>
    public bool IsSideBySide { get; private set; }

    private List<Control> Visible => [.. Children.Where(c => c.IsVisible)];

    private double[]? Widths(double available, List<Control> kids)
    {
        if (kids.Count == 0) return [];
        double gaps = Gap * (kids.Count - 1);
        double min = kids.Sum(c => c.GetValue(MinColumnWidthProperty)) + gaps;
        if (double.IsInfinity(available)) available = min;
        if (available + 0.5 < min) return null;
        var widths = kids.Select(c => c.GetValue(MinColumnWidthProperty)).ToArray();
        double spare = available - min;
        double weights = kids.Sum(c => Math.Max(0, c.GetValue(WeightProperty)));
        for (int i = 0; i < kids.Count && weights > 0; i++)
            widths[i] += spare * Math.Max(0, kids[i].GetValue(WeightProperty)) / weights;
        return widths;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var kids = Visible;
        var widths = Widths(availableSize.Width, kids);
        IsSideBySide = widths is not null;
        if (widths is not null)
        {
            double h = 0, w = 0;
            for (int i = 0; i < kids.Count; i++)
            {
                kids[i].Measure(new Size(widths[i], availableSize.Height));
                h = Math.Max(h, kids[i].DesiredSize.Height);
                w += widths[i];
            }
            return new Size(w + Gap * Math.Max(0, kids.Count - 1), h);
        }
        double total = 0, width = 0;
        foreach (var k in kids)
        {
            k.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            total += k.DesiredSize.Height;
            width = Math.Max(width, k.DesiredSize.Width);
        }
        return new Size(width, total + Gap * Math.Max(0, kids.Count - 1));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var kids = Visible;
        var widths = Widths(finalSize.Width, kids);
        if (widths is not null)
        {
            double x = 0;
            for (int i = 0; i < kids.Count; i++)
            {
                kids[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
                x += widths[i] + Gap;
            }
            return finalSize;
        }
        double y = 0;
        foreach (var k in kids)
        {
            k.Arrange(new Rect(0, y, finalSize.Width, k.DesiredSize.Height));
            y += k.DesiredSize.Height + Gap;
        }
        return finalSize;
    }
}
