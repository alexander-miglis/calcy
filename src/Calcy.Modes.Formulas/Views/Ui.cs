using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Calcy.UI.Controls;

namespace Calcy.Modes.Formulas.Views;

/// <summary>Small helpers for building the Formulas screen in code. Every colour comes from a scheme token.</summary>
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

    /// <summary>A text block with style classes from FormulasStyles ("body", "hint", "cap", ...).</summary>
    public static TextBlock Text(string text, string classes)
    {
        var t = new TextBlock { Text = text };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) t.Classes.Add(c);
        return t;
    }

    /// <summary>An 8-bit button with a pixel-font label.</summary>
    public static PixelButton Button(string label, string automationName, string? tip = null, params string[] classes)
    {
        var b = new PixelButton
        {
            Content = Pixel(label, 2),
            MinHeight = 38,
            Padding = new Thickness(12, 4),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false,
        };
        foreach (var c in classes) b.Classes.Add(c);
        AutomationProperties.SetName(b, automationName);
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>A small flat button for cards and lists (COPY, USE, OPEN).</summary>
    public static PixelButton Small(string label, string automationName, string? tip = null, params string[] classes)
    {
        var b = Button(label, automationName, tip, classes);
        b.Content = Pixel(label, 1.5);
        b.Classes.Add("small");
        b.Classes.Add("flat");
        b.MinHeight = 28;
        b.Padding = new Thickness(5, 3);
        return b;
    }

    /// <summary>A small coloured tag in the pixel font (SCHOOL, UNI, EXACT).</summary>
    public static Border Tag(string text, string kind, string? tip = null)
    {
        var tag = new Border { Child = Pixel(text, 1.5) };
        tag.Classes.Add("tag");
        tag.Classes.Add(kind);
        if (tip is not null) ToolTip.SetTip(tag, tip);
        AutomationProperties.SetName(tag, tip ?? text);
        return tag;
    }

    /// <summary>A segmented control: toggles side by side, one checked.</summary>
    public static (Border Group, List<PixelToggle> Items) Segmented(string automationName, IEnumerable<(string Label, string Name)> items)
    {
        var grid = new Grid();
        var list = new List<PixelToggle>();
        int i = 0;
        foreach (var (label, name) in items)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var t = new PixelToggle
            {
                Content = Pixel(label, 1.5),
                MinHeight = 32,
                BorderThickness = new Thickness(i == 0 ? 0 : 2, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(2, 2),
            };
            AutomationProperties.SetName(t, name);
            Grid.SetColumn(t, i++);
            grid.Children.Add(t);
            list.Add(t);
        }
        var border = new Border { Child = grid, BorderThickness = new Thickness(2) }.Token(Border.BorderBrushProperty, "Calcy.Line");
        AutomationProperties.SetName(border, automationName);
        return (border, list);
    }

    /// <summary>
    /// A variable or constant symbol typeset like the equation (k_B with a subscript B, h_bar as ħ). The constant pi
    /// is a name, not a symbol, in the engine, so it is shown as π here.
    /// </summary>
    public static Control Symbol(string symbol, double size) => new Calcy.UI.Math.MathView
    {
        Expression = symbol == "pi" ? Calcy.Core.Expressions.ConstantExpr.Pi : new Calcy.Core.Expressions.SymbolExpr(symbol),
        FontSize = size,
        KeepTermOrder = true,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>A section caption in the label font.</summary>
    public static TextBlock Caption(string text) => Text(text.ToUpperInvariant(), "cap");

    /// <summary>Runs an action when Enter is pressed in a text box (before the box itself sees the key).</summary>
    public static void OnEnter(TextBox box, Action action)
    {
        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                action();
            }
        }, RoutingStrategies.Tunnel);
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

    /// <summary>Handles keys before the control itself (a TextBox would otherwise consume Up, Down and Enter).</summary>
    public static void OnKeys(Control control, EventHandler<KeyEventArgs> handler) =>
        control.AddHandler(InputElement.KeyDownEvent, handler, RoutingStrategies.Tunnel);

    /// <summary>Shows a line of text, or hides it when there is nothing to show.</summary>
    public static void Show(TextBlock t, string? text)
    {
        t.Text = text ?? "";
        t.IsVisible = !string.IsNullOrEmpty(text);
    }
}
