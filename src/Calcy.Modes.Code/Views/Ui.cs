using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Code.Views;

/// <summary>Small helpers for building the Code screen from scheme resources (never hard-coded colours).</summary>
internal static class Ui
{
    /// <summary>Binds a property to a resource (a colour token such as "Calcy.Ink" or a font such as "Calcy.Font.Math").</summary>
    public static T Bind<T>(this T target, AvaloniaProperty property, string resource) where T : StyledElement
    {
        target.Bind(property, target.GetResourceObservable(resource));
        return target;
    }

    /// <summary>A label in the label font (Silkscreen style).</summary>
    public static TextBlock Label(string text, double size = 11, string brush = "Calcy.Ink")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        t.Bind(TextBlock.FontFamilyProperty, "Calcy.Font.Label");
        t.Bind(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Text in the code font.</summary>
    public static TextBlock Mono(string text, double size = 14, string brush = "Calcy.Ink", bool bold = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        t.Bind(TextBlock.FontFamilyProperty, "Calcy.Font.Math");
        t.Bind(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Text in the body font.</summary>
    public static TextBlock Body(string text, double size = 13, string brush = "Calcy.Ink")
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.Bind(TextBlock.FontFamilyProperty, "Calcy.Font.Body");
        t.Bind(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>A pixel icon in a colour token.</summary>
    public static PixelIcon Icon(string kind, double size = 14, string brush = "Calcy.Ink")
    {
        var i = new PixelIcon { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        i.Bind(PixelIcon.ForegroundProperty, brush);
        return i;
    }

    /// <summary>A small flat button for card and panel actions.</summary>
    public static PixelButton SmallButton(string text, string tip, string colour = "paper")
    {
        var b = new PixelButton { Content = text, Classes = { "small", colour }, MinHeight = 28, Padding = new Thickness(8, 2) };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        return b;
    }

    /// <summary>A bordered box in a background token.</summary>
    public static Border Box(Control child, string background, double border = 2, Thickness? padding = null)
    {
        var b = new Border { Child = child, BorderThickness = new Thickness(border), Padding = padding ?? new Thickness(0) };
        b.Bind(Border.BackgroundProperty, background);
        b.Bind(Border.BorderBrushProperty, "Calcy.Line");
        return b;
    }
}

/// <summary>A decorator with a dashed outline (the REPL input on the design board).</summary>
internal sealed class DashedFrame : Decorator
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<DashedFrame, IBrush?>(nameof(Stroke));

    static DashedFrame() => AffectsRender<DashedFrame>(StrokeProperty);

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Stroke is null) return;
        var pen = new Pen(Stroke, 2, new DashStyle([3, 2], 0), PenLineCap.Flat);
        context.DrawRectangle(null, pen, new Rect(1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2)));
    }
}
