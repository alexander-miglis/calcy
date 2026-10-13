using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>Small helpers for building the Finance screen in code. Every colour comes from a scheme token.</summary>
internal static class Ui
{
    /// <summary>Binds a property to a theme resource ("Calcy.Ink", "Calcy.Font.Math", ...) so it follows scheme changes.</summary>
    public static T Token<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    /// <summary>Text in the built-in pixel font, coloured with a token.</summary>
    public static PixelText Pixel(string text, double size = 2, string colorKey = "Calcy.Ink") =>
        new PixelText { Text = text.ToUpperInvariant(), PixelSize = size, VerticalAlignment = VerticalAlignment.Center }
            .Token(PixelText.ForegroundProperty, colorKey);

    /// <summary>A text block with a style class from FinanceStyles ("hint", "cap", "big", ...).</summary>
    public static TextBlock Text(string text, string cls)
    {
        var t = new TextBlock { Text = text };
        foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) t.Classes.Add(c);
        return t;
    }

    /// <summary>An 8-bit button with a pixel-font label.</summary>
    public static PixelButton Button(string label, string automationName, string? tip = null, params string[] classes)
    {
        var b = new PixelButton
        {
            Content = Pixel(label, 2),
            MinHeight = 34,
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false,
        };
        foreach (var c in classes) b.Classes.Add(c);
        AutomationProperties.SetName(b, automationName);
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>A small flat button for cards and toolbars (COPY, USE, EXPORT CSV).</summary>
    public static PixelButton Small(string label, string automationName, string? tip = null, params string[] classes)
    {
        var b = Button(label, automationName, tip, classes);
        b.Classes.Add("small");
        b.Classes.Add("flat");
        b.Padding = new Thickness(8, 4);
        return b;
    }

    /// <summary>A finance input: monospace, right aligned, accepts expressions.</summary>
    public static TextBox Input(string automationName, string? tip = null, double width = double.NaN, bool small = false)
    {
        var box = new TextBox { Width = width, AcceptsReturn = false };
        box.Classes.Add("fin");
        if (small) box.Classes.Add("small");
        AutomationProperties.SetName(box, automationName);
        if (tip is not null) ToolTip.SetTip(box, tip);
        return box;
    }

    /// <summary>A segmented control: toggles side by side, one checked.</summary>
    public static (Border Group, List<PixelToggle> Items) Segmented(string automationName, IEnumerable<(string Label, string Name)> items, double minHeight = 44)
    {
        var grid = new Grid();
        var list = new List<PixelToggle>();
        int i = 0;
        foreach (var (label, name) in items)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var t = new PixelToggle
            {
                Content = Pixel(label, 2),
                MinHeight = minHeight,
                BorderThickness = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(10, 4),
            };
            t.Token(PixelToggle.BackgroundProperty, "Calcy.Card");
            AutomationProperties.SetName(t, name);
            Grid.SetColumn(t, i++);
            grid.Children.Add(t);
            list.Add(t);
        }
        var border = new Border { Child = grid, BorderThickness = new Thickness(3) }.Token(Border.BorderBrushProperty, "Calcy.Line");
        AutomationProperties.SetName(border, automationName);
        return (border, list);
    }

    /// <summary>Marks the checked item of a segmented control (the selection colour for it, the card colour for the rest).</summary>
    public static void Check(IReadOnlyList<PixelToggle> items, int index)
    {
        for (int i = 0; i < items.Count; i++)
        {
            items[i].IsChecked = i == index;
            items[i].Token(PixelToggle.BackgroundProperty, i == index ? "Calcy.Sel" : "Calcy.Card");
        }
    }

    /// <summary>A labelled tile: caption on top, a big value, and an optional line below.</summary>
    public static (Border Tile, TextBlock Value, TextBlock Sub) Tile(string caption, bool selected = false)
    {
        var value = Text("?", "tilevalue");
        var sub = Text("", "meaning");
        sub.IsVisible = false;
        var tile = new Border { Child = new StackPanel { Spacing = 3, Children = { Text(caption.ToUpperInvariant(), "cap"), value, sub } } };
        tile.Classes.Add("tile");
        if (selected) tile.Classes.Add("sel");
        return (tile, value, sub);
    }

    /// <summary>Shows a line of text, or hides it when there is nothing to show.</summary>
    public static void Show(TextBlock t, string? text)
    {
        t.Text = text ?? "";
        t.IsVisible = !string.IsNullOrEmpty(text);
    }

    /// <summary>
    /// Runs an action as soon as the text of a box changes. Unlike TextBox.TextChanged (raised later by the dispatcher),
    /// this runs synchronously, so programmatic updates can be told apart from typing.
    /// </summary>
    public static void OnText(TextBox box, Action changed) =>
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) changed();
        };

    /// <summary>Runs an action when Enter is pressed in a text box.</summary>
    public static void OnEnter(TextBox box, Action action)
    {
        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                action();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Sets an input's text without moving the caret to the start.</summary>
    public static void SetText(TextBox box, string text)
    {
        if (box.Text == text) return;
        box.Text = text;
        box.CaretIndex = text.Length;
    }

    /// <summary>Marks an input as invalid (red border, message as tooltip) or valid.</summary>
    public static void SetError(TextBox box, string? message, string? tip = null)
    {
        box.Classes.Set("error", message is not null);
        ToolTip.SetTip(box, message ?? tip);
    }

    /// <summary>A small square in a colour token, for chart legends.</summary>
    public static Border Swatch(string key)
    {
        var b = new Border { VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("swatch");
        return b.Token(Border.BackgroundProperty, key);
    }

    /// <summary>A column of a label above a control (the design's "label over input" layout).</summary>
    public static StackPanel Labelled(string label, Control control, double spacing = 4) => new()
    {
        Spacing = spacing,
        Children = { Text(label.ToUpperInvariant(), "cap"), control },
    };
}
