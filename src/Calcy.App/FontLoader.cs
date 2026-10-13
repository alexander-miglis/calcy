using Avalonia.Platform;

namespace Calcy.App;

/// <summary>
/// Uses bundled fonts from Assets/Fonts when they are present (Pixelify Sans, Silkscreen, JetBrains Mono, VT323),
/// otherwise the system monospace fonts. The built-in pixel font (PixelText) needs no files at all.
/// </summary>
internal static class FontLoader
{
    private static string? Find(string file, string family)
    {
        try
        {
            var uri = new Uri($"avares://Calcy/Assets/Fonts/{file}");
            return AssetLoader.Exists(uri) ? $"avares://Calcy/Assets/Fonts#{family}, {Calcy.UI.Theming.ThemeManager.MonoFallback}" : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string? Pixel => Find("PixelifySans.ttf", "Pixelify Sans");
    public static string? Label => Find("Silkscreen-Regular.ttf", "Silkscreen");
    public static string? Math => Find("JetBrainsMono.ttf", "JetBrains Mono");
    public static string? Lcd => Find("VT323-Regular.ttf", "VT323");
}
