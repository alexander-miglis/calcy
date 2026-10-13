using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Solver.Views;

/// <summary>Small helpers for building the Solver screen in code. Every colour comes from a scheme token.</summary>
internal static class Ui
{
    /// <summary>Binds a property to a theme resource ("Calcy.Ink", "Calcy.Font.Math", ...) so it follows scheme changes.</summary>
    public static T Token<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    /// <summary>A caption in the built-in pixel font (DEGREE, ROOTS, SOLVE FOR).</summary>
    public static PixelText Caption(string text, double size = 2, string colorKey = "Calcy.Ink") =>
        new PixelText { Text = text.ToUpperInvariant(), PixelSize = size, VerticalAlignment = VerticalAlignment.Center }
            .Token(PixelText.ForegroundProperty, colorKey);

    /// <summary>A text block with style classes from SolverStyles ("hint", "note", "term" ...).</summary>
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
            Content = new PixelText { Text = label.ToUpperInvariant(), PixelSize = 2, VerticalAlignment = VerticalAlignment.Center },
            MinHeight = 40,
            Padding = new Thickness(14, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var c in classes) b.Classes.Add(c);
        AutomationProperties.SetName(b, automationName);
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>A small card button with a plain label (COPY, USE, PLOT), like the other modes' cards.</summary>
    public static PixelButton Small(string label, string automationName, string? tip = null, params string[] classes)
    {
        var b = new PixelButton { Content = label, Classes = { "small" } };
        foreach (var c in classes) b.Classes.Add(c);
        AutomationProperties.SetName(b, automationName);
        ToolTip.SetTip(b, tip ?? automationName);
        return b;
    }

    /// <summary>A toggle with a pixel-font label (tabs, degree buttons, option switches).</summary>
    public static PixelToggle Toggle(string label, string automationName, double minWidth = 0, double minHeight = 40, string? tip = null)
    {
        var t = new PixelToggle
        {
            Content = new PixelText { Text = label.ToUpperInvariant(), PixelSize = 2, VerticalAlignment = VerticalAlignment.Center },
            MinWidth = minWidth,
            MinHeight = minHeight,
            Padding = new Thickness(12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(t, automationName);
        if (tip is not null) ToolTip.SetTip(t, tip);
        return t;
    }

    /// <summary>Runs an action when Enter is pressed in a text box (and marks the key handled).</summary>
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

    /// <summary>Runs an action whenever a text box's text changes, whether typed or set in code (synchronously).</summary>
    public static void OnText(TextBox box, Action<string> changed)
    {
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) changed(box.Text ?? "");
        };
    }

    /// <summary>Sets a text box's text without moving the caret to the start.</summary>
    public static void SetText(TextBox box, string text)
    {
        if (box.Text == text) return;
        box.Text = text;
        box.CaretIndex = text.Length;
    }

    /// <summary>A horizontal row with even spacing that wraps when it runs out of room.</summary>
    public static WrapPanel Row(double spacing = 10) => new() { Orientation = Orientation.Horizontal, ItemSpacing = spacing, LineSpacing = spacing };

    /// <summary>Resolves a font family resource, falling back to the default font.</summary>
    public static FontFamily Font(Control c, string key) =>
        c.TryFindResource(key, c.ActualThemeVariant, out var v) && v is FontFamily f ? f : FontFamily.Default;
}
