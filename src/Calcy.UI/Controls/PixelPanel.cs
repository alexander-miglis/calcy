using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Calcy.UI.Controls;

/// <summary>The standard Calcy panel: thick border, hard shadow, a coloured header strip and content.</summary>
public class PixelPanel : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<PixelPanel, string?>(nameof(Header));

    public static readonly StyledProperty<IBrush?> HeaderBrushProperty =
        AvaloniaProperty.Register<PixelPanel, IBrush?>(nameof(HeaderBrush));

    /// <summary>Extra controls shown at the right end of the header (buttons, chips).</summary>
    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<PixelPanel, object?>(nameof(HeaderContent));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public IBrush? HeaderBrush
    {
        get => GetValue(HeaderBrushProperty);
        set => SetValue(HeaderBrushProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }
}

/// <summary>A result card: a numbered header strip, the body, and an optional row of actions.</summary>
public class Card : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Card, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MetaProperty =
        AvaloniaProperty.Register<Card, string?>(nameof(Meta));

    public static readonly StyledProperty<IBrush?> HeaderBrushProperty =
        AvaloniaProperty.Register<Card, IBrush?>(nameof(HeaderBrush));

    public static readonly StyledProperty<bool> IsErrorProperty =
        AvaloniaProperty.Register<Card, bool>(nameof(IsError));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<Card, object?>(nameof(Actions));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public IBrush? HeaderBrush
    {
        get => GetValue(HeaderBrushProperty);
        set => SetValue(HeaderBrushProperty, value);
    }

    public bool IsError
    {
        get => GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    static Card()
    {
        IsErrorProperty.Changed.AddClassHandler<Card>((c, _) => c.PseudoClasses.Set(":error", c.IsError));
    }
}

/// <summary>The dithered 8-bit background (two-colour checkerboard of 4 px cells).</summary>
public class DitherBackground : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<DitherBackground, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> Background2Property =
        AvaloniaProperty.Register<DitherBackground, IBrush?>(nameof(Background2));

    static DitherBackground() => AffectsRender<DitherBackground>(BackgroundProperty, Background2Property);

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? Background2
    {
        get => GetValue(Background2Property);
        set => SetValue(Background2Property, value);
    }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        context.FillRectangle(Background ?? Brushes.Black, r);
        if (Background2 is not ISolidColorBrush b2) return;
        // A tile brush would be cheaper, but a cached checker geometry keeps it simple and crisp.
        var tile = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            SourceRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            Drawing = new GeometryDrawing
            {
                Brush = b2,
                Geometry = Geometry.Parse("M0 0h4v4H0z M4 4h4v4H4z"),
            },
        };
        context.FillRectangle(tile, r);
    }
}
