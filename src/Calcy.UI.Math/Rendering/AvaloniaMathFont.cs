using System.Globalization;
using Avalonia.Media;
using Calcy.UI.Math.Layout;

namespace Calcy.UI.Math.Rendering;

/// <summary>
/// Measures and draws maths text with Avalonia's <see cref="FormattedText"/>. Results are cached per text, size and style.
/// Use one instance per font family (see <see cref="Get"/>); it is meant for the UI thread.
/// </summary>
public sealed class AvaloniaMathFont : IMathTextMeasurer
{
    /// <summary>Monospace families that exist on Windows, macOS and Linux, in order of preference.</summary>
    public const string DefaultFamilyName = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace";

    private const int MaxCache = 4096;
    private static readonly Dictionary<string, AvaloniaMathFont> Instances = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Text, double Size, bool Italic), FormattedText> _texts = [];
    private readonly Dictionary<double, MathFontMetrics> _metrics = [];
    private readonly Typeface _regular;
    private readonly Typeface _italic;

    /// <summary>Creates a font for a family (null uses <see cref="DefaultFamilyName"/>).</summary>
    public AvaloniaMathFont(FontFamily? family = null)
    {
        Family = family ?? new FontFamily(DefaultFamilyName);
        _regular = new Typeface(Family, FontStyle.Normal, FontWeight.Normal);
        _italic = new Typeface(Family, FontStyle.Italic, FontWeight.Normal);
    }

    /// <summary>The font family.</summary>
    public FontFamily Family { get; }

    /// <summary>A shared instance for a family, so measurement caches are reused across controls.</summary>
    public static AvaloniaMathFont Get(FontFamily? family)
    {
        string key = family?.ToString() ?? DefaultFamilyName;
        if (!Instances.TryGetValue(key, out var font))
        {
            font = new AvaloniaMathFont(family);
            Instances[key] = font;
        }
        return font;
    }

    /// <summary>A cached formatted text (its brush is set when drawing).</summary>
    public FormattedText GetFormattedText(string text, double size, bool italic)
    {
        var key = (text, System.Math.Round(size, 2), italic);
        if (!_texts.TryGetValue(key, out var ft))
        {
            if (_texts.Count >= MaxCache) _texts.Clear();
            ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, italic ? _italic : _regular, key.Item2, Brushes.Black);
            _texts[key] = ft;
        }
        return ft;
    }

    /// <inheritdoc />
    public MathTextMetrics Measure(string text, double fontSize, bool italic)
    {
        var ft = GetFormattedText(text, fontSize, italic);
        double width = ft.WidthIncludingTrailingWhitespace;
        var (asc, desc) = Ink(ft);
        return new MathTextMetrics(width, asc, desc);
    }

    private static (double Ascent, double Descent) Ink(FormattedText ft)
    {
        double extent = ft.Extent;
        if (!(extent > 0) || double.IsNaN(ft.OverhangAfter)) return (0, 0);
        double inkBottom = ft.Height + ft.OverhangAfter;
        double inkTop = inkBottom - extent;
        return (System.Math.Max(0, ft.Baseline - inkTop), System.Math.Max(0, inkBottom - ft.Baseline));
    }

    /// <inheritdoc />
    public MathFontMetrics GetFontMetrics(double fontSize)
    {
        double size = System.Math.Round(fontSize, 2);
        if (_metrics.TryGetValue(size, out var m)) return m;
        var h = GetFormattedText("H", size, false);
        double ascent = h.Baseline, descent = h.Height - h.Baseline;
        var (cap, _) = Ink(h);
        var (xh, _) = Ink(GetFormattedText("x", size, false));
        var plus = GetFormattedText("+", size, false);
        double axis;
        if (plus.Extent > 0)
        {
            double bottom = plus.Height + plus.OverhangAfter;
            double top = bottom - plus.Extent;
            axis = plus.Baseline - (top + bottom) / 2;
        }
        else axis = 0;
        // Sanity limits in case a platform reports no ink metrics.
        if (cap <= 0.3 * size || cap > 1.2 * size) cap = 0.7 * size;
        if (xh <= 0.2 * size || xh > cap) xh = 0.52 * size;
        if (axis <= 0.1 * size || axis > 0.6 * size) axis = 0.3 * size;
        if (ascent <= 0) ascent = 0.9 * size;
        if (descent <= 0) descent = 0.25 * size;
        m = new MathFontMetrics(size, ascent, descent, xh, cap, axis);
        _metrics[size] = m;
        return m;
    }
}
