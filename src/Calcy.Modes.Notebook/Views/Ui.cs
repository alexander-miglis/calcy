using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.UI.Controls;

namespace Calcy.Modes.Notebook.Views;

/// <summary>Small helpers for building the Notebook screen from scheme resources (never hard-coded colours).</summary>
internal static class Ui
{
    /// <summary>Binds a property to a resource (a colour token such as "Calcy.Ink" or a font such as "Calcy.Font.Math").</summary>
    public static T Res<T>(this T target, AvaloniaProperty property, string resource) where T : StyledElement
    {
        target.Bind(property, target.GetResourceObservable(resource));
        return target;
    }

    /// <summary>Text in the label font.</summary>
    public static TextBlock Label(string text, double size = 11, string brush = "Calcy.Ink")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        t.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Label").Res(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Text in the maths and code font.</summary>
    public static TextBlock Mono(string text, double size = 15, string brush = "Calcy.Ink", bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
        t.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Math").Res(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Wrapping text in the body font.</summary>
    public static TextBlock Body(string text, double size = 15, string brush = "Calcy.Ink")
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Body").Res(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>A pixel icon in a colour token.</summary>
    public static PixelIcon Icon(string kind, double size = 14, string brush = "Calcy.Ink")
    {
        var i = new PixelIcon { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        i.Res(PixelIcon.ForegroundProperty, brush);
        return i;
    }

    /// <summary>A small 8-bit button with a text label.</summary>
    public static PixelButton Button(string text, string tip, string colour = "paper", bool small = true)
    {
        var b = new PixelButton { Content = text };
        if (small)
        {
            b.Classes.Add("small");
            b.MinHeight = 30;
            b.Padding = new Thickness(8, 2);
        }
        if (colour.Length > 0) b.Classes.Add(colour);
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        return b;
    }

    /// <summary>A square icon button for cell actions.</summary>
    public static PixelButton IconButton(string icon, string tip, double rotate = 0)
    {
        var glyph = Icon(icon, 12);
        if (rotate != 0) glyph.RenderTransform = new RotateTransform(rotate);
        var b = new PixelButton { Content = glyph, Classes = { "small", "paper", "flat" }, Width = 28, MinWidth = 28, MinHeight = 26, Height = 26, Padding = new Thickness(0) };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        return b;
    }

    /// <summary>A label chip (RAD, FRAC, 1 ERROR ...) on a background token, or with an error border.</summary>
    public static Border Chip(string text, string background, bool error = false, double size = 10)
    {
        var label = Label(text, size);
        var b = new Border { Child = label, BorderThickness = new Thickness(2), Padding = new Thickness(6, 2), VerticalAlignment = VerticalAlignment.Center };
        b.Res(Border.BackgroundProperty, background).Res(Border.BorderBrushProperty, error ? "Calcy.Err" : "Calcy.Line");
        return b;
    }

    /// <summary>A bordered box in a background token.</summary>
    public static Border Box(Control? child, string background, double border = 2, Thickness? padding = null)
    {
        var b = new Border { Child = child, BorderThickness = new Thickness(border), Padding = padding ?? new Thickness(0) };
        b.Res(Border.BackgroundProperty, background).Res(Border.BorderBrushProperty, "Calcy.Line");
        return b;
    }

    /// <summary>A thin rule in a token.</summary>
    public static Border Rule(string brush = "Calcy.Key", double thickness = 2)
    {
        var b = new Border { Height = thickness };
        b.Res(Border.BackgroundProperty, brush);
        return b;
    }

    /// <summary>Copies text to the clipboard. Returns false when there is no clipboard.</summary>
    public static async Task<bool> CopyTextAsync(Visual anchor, string text)
    {
        var clip = TopLevel.GetTopLevel(anchor)?.Clipboard;
        if (clip is null) return false;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clip, text);
        return true;
    }
}

/// <summary>
/// Runs an action on the UI thread once calls to <see cref="Trigger"/> stop for a moment. Uses Task.Delay rather than a
/// dispatcher timer so it also works under the headless test dispatcher.
/// </summary>
internal sealed class Debouncer(TimeSpan delay, Action action)
{
    private int _generation;

    /// <summary>True while an action is scheduled.</summary>
    public bool Pending { get; private set; }

    /// <summary>Schedules the action (again).</summary>
    public void Trigger()
    {
        int g = ++_generation;
        Pending = true;
        Task.Delay(delay).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (g != _generation) return;
            Pending = false;
            action();
        }), TaskScheduler.Default);
    }

    /// <summary>Runs the action now when it is scheduled.</summary>
    public void Flush()
    {
        if (!Pending) return;
        _generation++;
        Pending = false;
        action();
    }

    /// <summary>Drops a scheduled action.</summary>
    public void Cancel()
    {
        _generation++;
        Pending = false;
    }
}
