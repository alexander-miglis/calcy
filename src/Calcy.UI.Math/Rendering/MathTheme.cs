using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Rendering;

/// <summary>Resource keys the app provides for the maths controls (the colour scheme tokens).</summary>
public static class MathThemeKeys
{
    /// <summary>Main text.</summary>
    public const string Ink = "Calcy.Ink";

    /// <summary>Secondary text, placeholders and empty slots.</summary>
    public const string Muted = "Calcy.Muted";

    /// <summary>Card and editor background.</summary>
    public const string Card = "Calcy.Card";

    /// <summary>Page background.</summary>
    public const string Paper = "Calcy.Paper";

    /// <summary>Borders.</summary>
    public const string Line = "Calcy.Line";

    /// <summary>Fill of the slot that holds the caret.</summary>
    public const string Sel = "Calcy.Sel";

    /// <summary>Accent colour.</summary>
    public const string Head = "Calcy.Head";

    /// <summary>Errors.</summary>
    public const string Err = "Calcy.Err";

    /// <summary>Key caps.</summary>
    public const string Key = "Calcy.Key";

    /// <summary>The maths font family (a FontFamily or a family name string).</summary>
    public const string Font = "Calcy.Font.Math";
}

/// <summary>The brushes the maths controls draw with, looked up from the theme with sensible fallbacks.</summary>
public sealed class MathPalette
{
    private static readonly IBrush DefaultInk = new ImmutableSolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x24));
    private static readonly IBrush DefaultMuted = new ImmutableSolidColorBrush(Color.FromRgb(0x86, 0x86, 0x92));
    private static readonly IBrush DefaultCard = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IBrush DefaultPaper = new ImmutableSolidColorBrush(Color.FromRgb(0xF4, 0xEE, 0xDC));
    private static readonly IBrush DefaultLine = new ImmutableSolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x24));
    private static readonly IBrush DefaultSel = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x3F));
    private static readonly IBrush DefaultHead = new ImmutableSolidColorBrush(Color.FromRgb(0x2B, 0x50, 0xAA));
    private static readonly IBrush DefaultErr = new ImmutableSolidColorBrush(Color.FromRgb(0xD6, 0x28, 0x28));
    private static readonly IBrush DefaultKey = new ImmutableSolidColorBrush(Color.FromRgb(0xE4, 0xE0, 0xD4));

    /// <summary>Main text.</summary>
    public required IBrush Ink { get; init; }

    /// <summary>Secondary text.</summary>
    public required IBrush Muted { get; init; }

    /// <summary>Card background.</summary>
    public required IBrush Card { get; init; }

    /// <summary>Page background.</summary>
    public required IBrush Paper { get; init; }

    /// <summary>Borders.</summary>
    public required IBrush Line { get; init; }

    /// <summary>Active slot fill.</summary>
    public required IBrush Sel { get; init; }

    /// <summary>Accent.</summary>
    public required IBrush Head { get; init; }

    /// <summary>Errors.</summary>
    public required IBrush Err { get; init; }

    /// <summary>Key caps.</summary>
    public required IBrush Key { get; init; }

    /// <summary>The palette used when no theme resources are available.</summary>
    public static MathPalette Default { get; } = new()
    {
        Ink = DefaultInk, Muted = DefaultMuted, Card = DefaultCard, Paper = DefaultPaper, Line = DefaultLine,
        Sel = DefaultSel, Head = DefaultHead, Err = DefaultErr, Key = DefaultKey,
    };

    /// <summary>The brush for a colour role.</summary>
    public IBrush For(MathColorRole role) => role switch
    {
        MathColorRole.Muted => Muted,
        MathColorRole.Head => Head,
        MathColorRole.Error => Err,
        _ => Ink,
    };

    /// <summary>Looks up the theme brushes for an element, falling back to the defaults.</summary>
    public static MathPalette Resolve(StyledElement element) => new()
    {
        Ink = Find(element, MathThemeKeys.Ink, DefaultInk),
        Muted = Find(element, MathThemeKeys.Muted, DefaultMuted),
        Card = Find(element, MathThemeKeys.Card, DefaultCard),
        Paper = Find(element, MathThemeKeys.Paper, DefaultPaper),
        Line = Find(element, MathThemeKeys.Line, DefaultLine),
        Sel = Find(element, MathThemeKeys.Sel, DefaultSel),
        Head = Find(element, MathThemeKeys.Head, DefaultHead),
        Err = Find(element, MathThemeKeys.Err, DefaultErr),
        Key = Find(element, MathThemeKeys.Key, DefaultKey),
    };

    /// <summary>Looks up a brush resource (a brush or a colour), or returns <paramref name="fallback"/>.</summary>
    public static IBrush Find(StyledElement element, string key, IBrush fallback)
    {
        if (element.TryFindResource(key, element.ActualThemeVariant, out var value))
        {
            if (value is IBrush b) return b;
            if (value is Color c) return new ImmutableSolidColorBrush(c);
        }
        return fallback;
    }

    /// <summary>The maths font for an element: the "Calcy.Font.Math" resource or the default monospace list.</summary>
    public static AvaloniaMathFont FindFont(StyledElement element)
    {
        FontFamily? family = null;
        if (element.TryFindResource(MathThemeKeys.Font, element.ActualThemeVariant, out var value))
        {
            family = value switch
            {
                FontFamily ff => ff,
                string s when !string.IsNullOrWhiteSpace(s) => new FontFamily(s),
                _ => null,
            };
        }
        return AvaloniaMathFont.Get(family);
    }
}
