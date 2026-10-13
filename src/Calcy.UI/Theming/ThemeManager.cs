using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Calcy.UI.Theming;

/// <summary>
/// Applies a colour scheme by writing brush resources ("Calcy.Ink", "Calcy.Paper", ...) into the application resources.
/// Controls reference them with DynamicResource so a scheme switch recolours everything at once.
/// </summary>
public sealed class ThemeManager
{
    public const string MonoFallback = "Cascadia Mono, Consolas, SF Mono, Menlo, DejaVu Sans Mono, Liberation Mono, Courier New";

    private readonly IResourceDictionary _resources;
    private readonly Application? _app;

    public ColorScheme Current { get; private set; } = ColorSchemes.Get(ColorSchemes.DefaultId);

    public event EventHandler<ColorScheme>? SchemeChanged;

    public ThemeManager(Application app)
    {
        _app = app;
        _resources = app.Resources;
    }

    /// <summary>For tests and previews: write resources into any dictionary.</summary>
    public ThemeManager(IResourceDictionary resources)
    {
        _resources = resources;
    }

    public void Apply(string? schemeId) => Apply(ColorSchemes.Get(schemeId));

    public void Apply(ColorScheme scheme)
    {
        Current = scheme;
        WriteScheme(_resources, scheme);
        if (_app is not null) _app.RequestedThemeVariant = scheme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        SchemeChanged?.Invoke(this, scheme);
    }

    public static void WriteScheme(IResourceDictionary r, ColorScheme scheme)
    {
        foreach (var token in ColorSchemes.Tokens)
        {
            var c = scheme[token];
            r[$"Calcy.{token}"] = new SolidColorBrush(c);
            r[$"Calcy.{token}.Color"] = c;
        }
        var line = scheme["Line"];
        r["Calcy.Shadow"] = Shadow(4, line);
        r["Calcy.ShadowSmall"] = Shadow(3, line);
        r["Calcy.ShadowLarge"] = Shadow(6, line);
        r["Calcy.ShadowHover"] = Shadow(5, line);
        r["Calcy.ShadowNone"] = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 0, Blur = 0, Color = Colors.Transparent });

        // Recolour the standard (Fluent) controls: accents, text and corners.
        r["SystemAccentColor"] = scheme["Head"];
        r["SystemAccentColorDark1"] = scheme["Head"];
        r["SystemAccentColorDark2"] = scheme["Head"];
        r["SystemAccentColorDark3"] = scheme["Head"];
        r["SystemAccentColorLight1"] = scheme["Head"];
        r["SystemAccentColorLight2"] = scheme["Head"];
        r["SystemAccentColorLight3"] = scheme["Head"];
        r["ControlCornerRadius"] = new CornerRadius(0);
        r["OverlayCornerRadius"] = new CornerRadius(0);
        r["TextControlBackground"] = new SolidColorBrush(scheme["Card"]);
        r["TextControlBackgroundPointerOver"] = new SolidColorBrush(scheme["Card"]);
        r["TextControlBackgroundFocused"] = new SolidColorBrush(scheme["Card"]);
        r["TextControlForeground"] = new SolidColorBrush(scheme["Ink"]);
        r["TextControlForegroundPointerOver"] = new SolidColorBrush(scheme["Ink"]);
        r["TextControlForegroundFocused"] = new SolidColorBrush(scheme["Ink"]);
        r["TextControlBorderBrush"] = new SolidColorBrush(scheme["Line"]);
        r["TextControlBorderBrushPointerOver"] = new SolidColorBrush(scheme["Line"]);
        r["TextControlBorderBrushFocused"] = new SolidColorBrush(scheme["Ink"]);
        r["TextControlSelectionHighlightColor"] = new SolidColorBrush(scheme["Sel"]);
        r["TextControlPlaceholderForeground"] = new SolidColorBrush(scheme["Muted"]);
        r["ComboBoxBackground"] = new SolidColorBrush(scheme["Card"]);
        r["ComboBoxForeground"] = new SolidColorBrush(scheme["Ink"]);
        r["ComboBoxBorderBrush"] = new SolidColorBrush(scheme["Line"]);
        r["ComboBoxDropDownBackground"] = new SolidColorBrush(scheme["Paper"]);
        r["ComboBoxItemForeground"] = new SolidColorBrush(scheme["Ink"]);
        r["ComboBoxItemBackgroundSelected"] = new SolidColorBrush(scheme["Sel"]);
        r["ComboBoxItemBackgroundPointerOver"] = new SolidColorBrush(scheme["Key"]);
        r["ComboBoxItemForegroundSelected"] = new SolidColorBrush(scheme["Ink"]);
        r["ComboBoxItemForegroundPointerOver"] = new SolidColorBrush(scheme["Ink"]);
        r["FlyoutPresenterBackground"] = new SolidColorBrush(scheme["Paper"]);
        r["FlyoutBorderThemeBrush"] = new SolidColorBrush(scheme["Line"]);
        r["MenuFlyoutPresenterBackground"] = new SolidColorBrush(scheme["Paper"]);
        r["MenuFlyoutPresenterBorderBrush"] = new SolidColorBrush(scheme["Line"]);
        r["MenuFlyoutItemForeground"] = new SolidColorBrush(scheme["Ink"]);
        r["MenuFlyoutItemBackgroundPointerOver"] = new SolidColorBrush(scheme["Sel"]);
        r["MenuFlyoutItemForegroundPointerOver"] = new SolidColorBrush(scheme["Ink"]);
        r["ToolTipBackground"] = new SolidColorBrush(scheme["Paper"]);
        r["ToolTipForeground"] = new SolidColorBrush(scheme["Ink"]);
        r["ToolTipBorderBrush"] = new SolidColorBrush(scheme["Line"]);
        r["ScrollBarThumbFill"] = new SolidColorBrush(scheme["Muted"]);
    }

    private static BoxShadows Shadow(double offset, Color c) =>
        new(new BoxShadow { OffsetX = offset, OffsetY = offset, Blur = 0, Spread = 0, Color = c });

    /// <summary>Writes the default fonts. Bundled pixel fonts are used when present, otherwise system monospace fonts.</summary>
    public static void WriteFonts(IResourceDictionary r, string? pixelFont = null, string? labelFont = null, string? mathFont = null, string? lcdFont = null)
    {
        r["Calcy.Font.Title"] = new FontFamily(pixelFont ?? MonoFallback);
        r["Calcy.Font.Label"] = new FontFamily(labelFont ?? MonoFallback);
        r["Calcy.Font.Math"] = new FontFamily(mathFont ?? MonoFallback);
        r["Calcy.Font.Lcd"] = new FontFamily(lcdFont ?? MonoFallback);
        r["Calcy.Font.Body"] = new FontFamily(pixelFont ?? MonoFallback);
    }
}
