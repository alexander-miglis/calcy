using Avalonia;
using Avalonia.Controls;

namespace Calcy.Modes.Matrix.Views;

/// <summary>
/// A vertical stack whose first child takes only the height left over by the others. The editor stays snug above the
/// operation buttons for small matrices and scrolls instead of pushing them away for big ones.
/// </summary>
public sealed class FlexStack : Panel
{
    /// <summary>Gap between children.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FlexStack, double>(nameof(Spacing), 16);

    static FlexStack() => AffectsMeasure<FlexStack>(SpacingProperty);

    /// <summary>Gap between children.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        if (visible.Count == 0) return default;
        double width = 0, rest = 0;
        for (int i = 1; i < visible.Count; i++)
        {
            visible[i].Measure(new Size(availableSize.Width, double.PositiveInfinity));
            rest += visible[i].DesiredSize.Height;
            width = Math.Max(width, visible[i].DesiredSize.Width);
        }
        rest += Spacing * (visible.Count - 1);
        double firstMax = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : Math.Max(0, availableSize.Height - rest);
        visible[0].Measure(new Size(availableSize.Width, firstMax));
        width = Math.Max(width, visible[0].DesiredSize.Width);
        return new Size(width, visible[0].DesiredSize.Height + rest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        if (visible.Count == 0) return finalSize;
        double rest = visible.Skip(1).Sum(c => c.DesiredSize.Height) + Spacing * (visible.Count - 1);
        double firstHeight = Math.Min(visible[0].DesiredSize.Height, Math.Max(0, finalSize.Height - rest));
        double y = 0;
        for (int i = 0; i < visible.Count; i++)
        {
            double h = i == 0 ? firstHeight : visible[i].DesiredSize.Height;
            visible[i].Arrange(new Rect(0, y, finalSize.Width, h));
            y += h + Spacing;
        }
        return finalSize;
    }
}
