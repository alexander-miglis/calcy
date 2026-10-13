using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Simple;

/// <summary>Small helpers for building the Simple screen in code. Every colour comes from a scheme token.</summary>
internal static class Ui
{
    /// <summary>Binds a property to a theme resource ("Calcy.Ink", "Calcy.Font.Math", ...) so it follows scheme changes.</summary>
    public static T Token<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    /// <summary>A hard 8-bit shadow (or inset shadow) in a token colour, for example the calculator body's 10 px drop.</summary>
    public static Border Shadow(this Border border, string colorKey, double offset, bool inset = false)
    {
        border.Bind(Border.BoxShadowProperty, border.GetResourceObservable(colorKey, v => v is Color c
            ? new BoxShadows(new BoxShadow { OffsetX = offset, OffsetY = offset, Blur = 0, Spread = 0, Color = c, IsInset = inset })
            : default(BoxShadows)));
        return border;
    }

    /// <summary>Text in the built-in pixel font, coloured with a token.</summary>
    public static PixelText Pixel(string text, double size, string colorKey = "Calcy.Ink") =>
        new PixelText { Text = text, PixelSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            .Token(PixelText.ForegroundProperty, colorKey);

    /// <summary>A small flat 8-bit button with a pixel-font label (Export, Clear, Use, Edit), as in the shared card style.</summary>
    public static PixelButton SmallButton(string label, string automationName, string tip, params string[] classes)
    {
        var b = new PixelButton
        {
            Content = Pixel(label.ToUpperInvariant(), 2),
            MinHeight = 34,
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false,
        };
        b.Classes.Add("small");
        b.Classes.Add("flat");
        foreach (var c in classes) b.Classes.Add(c);
        AutomationProperties.SetName(b, automationName);
        ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>A text block in one of the Calcy fonts.</summary>
    public static TextBlock Text(string text, double size, string fontKey = "Calcy.Font.Body", string? colorKey = null)
    {
        var t = new TextBlock { Text = text, FontSize = size }.Token(TextBlock.FontFamilyProperty, fontKey);
        if (colorKey is not null) t.Token(TextBlock.ForegroundProperty, colorKey);
        return t;
    }
}
