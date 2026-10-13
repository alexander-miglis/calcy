using Avalonia;
using Avalonia.Controls;

namespace Calcy.Modes.Solver.Views;

/// <summary>
/// The design's graph and roots layout: the first child on the left (about half the width, between
/// <see cref="LeftMinWidth"/> and <see cref="LeftMaxWidth"/>), the second filling the rest. Below
/// <see cref="StackWidth"/> the second child goes underneath the first instead.
/// </summary>
public sealed class SideBySide : Panel
{
    /// <summary>The narrowest the left child gets.</summary>
    public double LeftMinWidth { get; init; } = 280;

    /// <summary>The widest the left child gets.</summary>
    public double LeftMaxWidth { get; init; } = 470;

    /// <summary>The space between the two.</summary>
    public double Gap { get; init; } = 16;

    /// <summary>Below this width the two are stacked.</summary>
    public double StackWidth { get; init; } = 720;

    /// <summary>Creates the layout.</summary>
    public SideBySide(Control left, Control right)
    {
        Children.Add(left);
        Children.Add(right);
    }

    /// <summary>True when the two children are stacked (for tests).</summary>
    public bool IsStacked { get; private set; }

    private double LeftWidth(double width) => Math.Clamp(width * 0.5, LeftMinWidth, LeftMaxWidth);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : LeftMaxWidth * 2 + Gap;
        IsStacked = width < StackWidth;
        var left = Children[0];
        var right = Children[1];
        if (IsStacked)
        {
            left.Measure(new Size(width, double.PositiveInfinity));
            right.Measure(new Size(width, double.PositiveInfinity));
            return new Size(width, left.DesiredSize.Height + Gap + right.DesiredSize.Height);
        }
        double lw = LeftWidth(width);
        left.Measure(new Size(lw, double.PositiveInfinity));
        right.Measure(new Size(Math.Max(0, width - lw - Gap), double.PositiveInfinity));
        return new Size(width, Math.Max(left.DesiredSize.Height, right.DesiredSize.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = Children[0];
        var right = Children[1];
        if (IsStacked)
        {
            left.Arrange(new Rect(0, 0, finalSize.Width, left.DesiredSize.Height));
            right.Arrange(new Rect(0, left.DesiredSize.Height + Gap, finalSize.Width, right.DesiredSize.Height));
            return finalSize;
        }
        double lw = LeftWidth(finalSize.Width);
        left.Arrange(new Rect(0, 0, lw, left.DesiredSize.Height));
        right.Arrange(new Rect(lw + Gap, 0, Math.Max(0, finalSize.Width - lw - Gap), right.DesiredSize.Height));
        return finalSize;
    }
}
