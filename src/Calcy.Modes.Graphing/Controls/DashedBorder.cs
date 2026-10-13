using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Calcy.Modes.Graphing.Controls;

/// <summary>A decorator with a dashed outline (the "+ type y = …" row of the expression list).</summary>
public sealed class DashedBorder : Decorator
{
    /// <summary>Outline colour.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<DashedBorder, IBrush?>(nameof(Stroke));

    /// <summary>Outline thickness.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<DashedBorder, double>(nameof(StrokeThickness), 3);

    /// <summary>Fill behind the content.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<DashedBorder, IBrush?>(nameof(Background));

    static DashedBorder() => AffectsRender<DashedBorder>(StrokeProperty, StrokeThicknessProperty, BackgroundProperty);

    /// <inheritdoc cref="StrokeProperty"/>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <inheritdoc cref="StrokeThicknessProperty"/>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <inheritdoc cref="BackgroundProperty"/>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double t = StrokeThickness;
        var r = new Rect(Bounds.Size).Deflate(t / 2);
        if (Background is { } bg) context.FillRectangle(bg, new Rect(Bounds.Size));
        if (Stroke is { } s && r.Width > 0 && r.Height > 0)
            context.DrawRectangle(null, new Pen(s, t, new DashStyle([2, 1.4], 0), PenLineCap.Flat), r);
        base.Render(context);
    }
}
