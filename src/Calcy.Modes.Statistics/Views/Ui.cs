using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Statistics.Views;

/// <summary>Small helpers that build controls in the Calcy look with scheme tokens.</summary>
internal static class Ui
{
    /// <summary>Binds a property to a scheme resource ("Calcy.Ink").</summary>
    public static T Token<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    /// <summary>A label in the label font ("LIST", "MODEL").</summary>
    public static TextBlock Label(string text, double size = 12) => new()
    {
        Text = text,
        FontSize = size,
        Classes = { "label" },
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Body text that wraps.</summary>
    public static TextBlock Body(string text, double size = 13, bool muted = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Classes = { "body" } };
        if (muted) t.Classes.Add("muted");
        return t;
    }

    /// <summary>Maths font text.</summary>
    public static TextBlock Math(string text, double size = 15, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        Classes = { "math" },
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A small pixel button with a tooltip and an accessible name.</summary>
    public static PixelButton Small(string text, string tip, Action click, string color = "")
    {
        var b = new PixelButton { Content = text, Classes = { "small" } };
        if (color.Length > 0) b.Classes.Add(color);
        ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A full-size pixel button (the design's 44 px action buttons).</summary>
    public static PixelButton Action(string text, string tip, Action click, string color = "key")
    {
        var b = new PixelButton { Content = text, Classes = { color }, MinHeight = 44, FontSize = 12, Padding = new Thickness(12, 4) };
        ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A text box in the maths font for numbers and lists.</summary>
    public static TextBox Field(string text, string name, double width = 76)
    {
        var t = new TextBox
        {
            Text = text,
            Width = width,
            MinHeight = 36,
            Height = 36,
            FontSize = 15,
            Padding = new Thickness(6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        t.Token(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        AutomationProperties.SetName(t, name);
        return t;
    }

    /// <summary>A combo box with string items in the maths font.</summary>
    public static ComboBox Combo(string name, double minWidth = 90)
    {
        var c = new ComboBox { MinWidth = minWidth, MinHeight = 36, FontSize = 13, Padding = new Thickness(8, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
        c.Token(ComboBox.FontFamilyProperty, "Calcy.Font.Math");
        AutomationProperties.SetName(c, name);
        return c;
    }

    /// <summary>A labelled control: "LABEL [control]".</summary>
    public static StackPanel Labelled(string label, Control control, string? tip = null)
    {
        var l = Label(label, 11);
        if (tip is not null) ToolTip.SetTip(l, tip);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { l, control }, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>A box with the line colour border and the card colour (the design's value tiles and plot frames).</summary>
    public static Border Frame(Control? child, double thickness = 2, string background = "Calcy.Card")
    {
        var b = new Border { Child = child, BorderThickness = new Thickness(thickness) };
        b.Token(Border.BackgroundProperty, background);
        b.Token(Border.BorderBrushProperty, "Calcy.Line");
        b.Token(TextElement.ForegroundProperty, "Calcy.Ink");
        return b;
    }

    /// <summary>A pixel toggle (tab, segmented control item, chip).</summary>
    public static PixelToggle Toggle(string text, string tip, bool shadow = false)
    {
        var t = new PixelToggle { Content = text, FontSize = 11, MinHeight = 34, Padding = new Thickness(10, 4) };
        if (shadow) t.Classes.Add("shadow");
        ToolTip.SetTip(t, tip);
        AutomationProperties.SetName(t, tip);
        return t;
    }
}
