using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Calcy.UI.Controls;

namespace Calcy.Modes.Simple;

/// <summary>A keypad key: a <see cref="PixelButton"/> with a pixel-font label that can flash pressed when typed on the keyboard.</summary>
internal sealed class KeyButton : PixelButton
{
    private readonly DispatcherTimer _flash = new() { Interval = TimeSpan.FromMilliseconds(110) };

    public KeyButton(CalcKey key)
    {
        Key = key;
        Focusable = false;
        // The theme's hard shadow and pressed offset are drawn outside the button's bounds.
        ClipToBounds = false;
        MinHeight = 56;
        Padding = new Avalonia.Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = key == CalcKey.Backspace
            ? new PixelGlyph(PixelGlyph.Backspace) { PixelSize = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new PixelText { Text = CalcKeys.Label(key), PixelSize = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(this, CalcKeys.AutomationName(key));
        var hint = CalcKeys.ShortcutHint(key);
        ToolTip.SetTip(this, hint.Length > 0 ? $"{Capitalize(CalcKeys.AutomationName(key))} ({hint})" : Capitalize(CalcKeys.AutomationName(key)));
        _flash.Tick += (_, _) => EndFlash();
    }

    protected override Type StyleKeyOverride => typeof(PixelButton);

    public CalcKey Key { get; }

    /// <summary>True while the key shows the pressed look after a keyboard press.</summary>
    public bool IsFlashing => _flash.IsEnabled;

    /// <summary>Shows the pressed look briefly, as feedback for a key typed on the keyboard.</summary>
    public void Flash()
    {
        _flash.Stop();
        PseudoClasses.Set(":pressed", true);
        _flash.Start();
    }

    /// <summary>Ends a flash early (also used by tests).</summary>
    public void EndFlash()
    {
        _flash.Stop();
        PseudoClasses.Set(":pressed", IsPressed);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
