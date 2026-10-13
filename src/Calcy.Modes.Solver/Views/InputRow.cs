using Avalonia;
using Avalonia.Controls;

namespace Calcy.Modes.Solver.Views;

/// <summary>
/// An input on the left that takes the free width and its options on the right (design: equation box, SOLVE FOR,
/// range, SOLVE on one line). When the input would get narrower than <see cref="MinMainWidth"/>, the options move
/// to a second line, right aligned, so the input keeps the full width.
/// </summary>
public sealed class InputRow : Panel
{
    /// <summary>The narrowest the input may get before the options wrap.</summary>
    public static readonly StyledProperty<double> MinMainWidthProperty = AvaloniaProperty.Register<InputRow, double>(nameof(MinMainWidth), 320);

    /// <summary>The space between the input and the options.</summary>
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<InputRow, double>(nameof(Gap), 10);

    static InputRow() => AffectsMeasure<InputRow>(MinMainWidthProperty, GapProperty);

    /// <summary>Creates the row with the input and its options.</summary>
    public InputRow(Control main, Control side)
    {
        Children.Add(main);
        Children.Add(side);
    }

    /// <inheritdoc cref="MinMainWidthProperty"/>
    public double MinMainWidth { get => GetValue(MinMainWidthProperty); set => SetValue(MinMainWidthProperty, value); }

    /// <inheritdoc cref="GapProperty"/>
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private Control Main => Children[0];
    private Control Side => Children[1];

    /// <summary>True when the options sit on their own line (for tests).</summary>
    public bool IsWrapped { get; private set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Side.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var side = Side.DesiredSize;
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinMainWidth + Gap + side.Width;
        IsWrapped = width - side.Width - Gap < MinMainWidth;
        if (IsWrapped)
        {
            Main.Measure(new Size(width, availableSize.Height));
            return new Size(width, Main.DesiredSize.Height + Gap + side.Height);
        }
        Main.Measure(new Size(width - side.Width - Gap, availableSize.Height));
        return new Size(width, Math.Max(Main.DesiredSize.Height, side.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var side = Side.DesiredSize;
        if (IsWrapped)
        {
            double mainHeight = Main.DesiredSize.Height;
            Main.Arrange(new Rect(0, 0, finalSize.Width, mainHeight));
            Side.Arrange(new Rect(Math.Max(0, finalSize.Width - side.Width), mainHeight + Gap, Math.Min(side.Width, finalSize.Width), side.Height));
            return finalSize;
        }
        double height = finalSize.Height;
        double mainWidth = Math.Max(0, finalSize.Width - side.Width - Gap);
        Main.Arrange(new Rect(0, (height - Main.DesiredSize.Height) / 2, mainWidth, Main.DesiredSize.Height));
        Side.Arrange(new Rect(mainWidth + Gap, (height - side.Height) / 2, side.Width, side.Height));
        return finalSize;
    }
}
