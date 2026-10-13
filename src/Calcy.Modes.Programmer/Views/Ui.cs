using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Programmer;

/// <summary>Small helpers for building the Programmer screen in code. Every colour comes from a scheme token.</summary>
internal static class Ui
{
    /// <summary>Binds a property to a scheme token ("Ink" binds Calcy.Ink) so it follows colour scheme switches.</summary>
    public static T Token<T>(this T control, AvaloniaProperty property, string token) where T : Control
    {
        control.Bind(property, control.GetResourceObservable("Calcy." + token));
        return control;
    }

    /// <summary>The standard paper section: thick line border and a hard shadow (as in the design).</summary>
    public static Border Section(Control child, Thickness padding)
    {
        var b = new Border { BorderThickness = new Thickness(4), Padding = padding, Child = child };
        b.Token(Border.BackgroundProperty, "Paper").Token(Border.BorderBrushProperty, "Line").Token(Border.BoxShadowProperty, "ShadowLarge");
        return b;
    }

    /// <summary>A text block in one of the Calcy fonts ("Math", "Label" or "Body").</summary>
    public static TextBlock Text(string text, double size, string font = "Math", string ink = "Ink", FontWeight weight = FontWeight.Normal)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        t.Token(TextBlock.FontFamilyProperty, "Font." + font).Token(TextBlock.ForegroundProperty, ink);
        return t;
    }

    /// <summary>Text in the built-in 5x7 pixel font (labels, keys, captions).</summary>
    public static PixelText Pixel(string text, double pixelSize = 2, string? ink = "Ink")
    {
        var p = new PixelText { Text = text, PixelSize = pixelSize, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (ink is not null) p.Token(PixelText.ForegroundProperty, ink);
        return p;
    }

    /// <summary>A segmented-control item: an 8-bit toggle with a pixel-font caption.</summary>
    public static PixelToggle Segment(string caption, string tip, double minWidth = 44)
    {
        var t = new PixelToggle
        {
            Content = Pixel(caption, 1.5),
            MinWidth = minWidth,
            MinHeight = 34,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, -2, 0),
            Focusable = false,
        };
        ToolTip.SetTip(t, tip);
        return t;
    }

    /// <summary>A small 8-bit button with a pixel-font caption.</summary>
    public static PixelButton SmallButton(string caption, string tip, string colourClass = "paper")
    {
        var b = new PixelButton { Content = Pixel(caption, 1.5), Classes = { "small", colourClass }, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>A text box in the maths font.</summary>
    public static TextBox Input(double fontSize, double width, string placeholder)
    {
        var t = new TextBox { FontSize = fontSize, Width = width, PlaceholderText = placeholder, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        t.Token(TextBox.FontFamilyProperty, "Font.Math");
        return t;
    }

    /// <summary>A caption to the left of an input.</summary>
    public static Control Caption(string text) => new Border { Child = Pixel(text, 2), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Puts a control in a grid cell.</summary>
    public static T Cell<T>(this T control, int row, int column, int columnSpan = 1) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        if (columnSpan > 1) Grid.SetColumnSpan(control, columnSpan);
        return control;
    }
}

/// <summary>
/// A property bound to a scheme token that can be switched to another token (selected/unselected cells and rows).
/// Rebinds only when the token actually changes.
/// </summary>
internal sealed class TokenSlot(Control target, AvaloniaProperty property)
{
    private IDisposable? _binding;
    private string? _token;

    /// <summary>The current token name, such as "Sel".</summary>
    public string? Current => _token;

    /// <summary>Binds the property to Calcy.{token}.</summary>
    public void Set(string token)
    {
        if (_token == token) return;
        _binding?.Dispose();
        _token = token;
        _binding = target.Bind(property, target.GetResourceObservable("Calcy." + token));
    }
}

/// <summary>A button with no chrome of its own: a bordered box whose colours the owner sets (value rows, bit cells).</summary>
internal class FlatButton : Button
{
    protected override Type StyleKeyOverride => typeof(FlatButton);

    public FlatButton()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        Template = new FuncControlTemplate<FlatButton>((b, _) =>
        {
            var cp = new ContentPresenter { Name = "PART_ContentPresenter" };
            cp.Bind(ContentPresenter.ContentProperty, b.GetObservable(ContentProperty));
            cp.Bind(ContentPresenter.HorizontalContentAlignmentProperty, b.GetObservable(HorizontalContentAlignmentProperty));
            cp.Bind(ContentPresenter.VerticalContentAlignmentProperty, b.GetObservable(VerticalContentAlignmentProperty));
            var border = new Border { Name = "PART_Border", Child = cp };
            border.Bind(Border.BackgroundProperty, b.GetObservable(BackgroundProperty));
            border.Bind(Border.BorderBrushProperty, b.GetObservable(BorderBrushProperty));
            border.Bind(Border.BorderThicknessProperty, b.GetObservable(BorderThicknessProperty));
            border.Bind(Border.PaddingProperty, b.GetObservable(PaddingProperty));
            return border;
        });
    }
}
