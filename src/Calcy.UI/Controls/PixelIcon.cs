using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Calcy.UI.Controls;

/// <summary>Pixel-art icons on a 16×16 grid (from the Calcy design board).</summary>
public class PixelIcon : Control
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calc"] = "F0 M2 1h12v14H2z M4 3h8v3H4z M4 8h2v2H4z M7 8h2v2H7z M10 8h2v2h-2z M4 11h2v2H4z M7 11h2v2H7z M10 11h2v2h-2z",
        ["graph"] = "M1 13h14v2H1z M1 1h2v12H1z M4 10h2v2H4z M6 8h2v2H6z M8 5h2v3H8z M10 3h2v2h-2z M12 2h2v1h-2z",
        ["stats"] = "M2 12h3v3H2z M6 8h3v7H6z M10 4h3v11h-3z M1 15h14v1H1z",
        ["matrix"] = "M1 2h2v12H1z M3 2h2v2H3z M3 12h2v2H3z M13 2h2v12h-2z M11 2h2v2h-2z M11 12h2v2h-2z M6 5h2v2H6z M9 5h2v2H9z M6 9h2v2H6z M9 9h2v2H9z",
        ["solver"] = "M1 3h2v2H1z M5 3h2v2H5z M3 5h2v2H3z M1 7h2v2H1z M5 7h2v2H5z M9 4h6v2H9z M9 8h6v2H9z",
        ["code"] = "M5 3h2v2H5z M3 5h2v2H3z M1 7h2v2H1z M3 9h2v2H3z M5 11h2v2H5z M9 3h2v2H9z M11 5h2v2h-2z M13 7h2v2h-2z M11 9h2v2h-2z M9 11h2v2H9z",
        ["run"] = "M4 2h2v12H4z M6 3h2v10H6z M8 4h2v8H8z M10 6h2v4h-2z",
        ["play"] = "M4 2h2v12H4z M6 3h2v10H6z M8 4h2v8H8z M10 6h2v4h-2z",
        ["stop"] = "M3 3h10v10H3z",
        ["pause"] = "M3 3h4v10H3z M9 3h4v10H9z",
        ["add"] = "M7 3h2v10H7z M3 7h10v2H3z",
        ["minus"] = "M3 7h10v2H3z",
        ["show"] = "F0 M5 4h6v2h2v4h-2v2H5v-2H3V6h2z M7 7h2v2H7z",
        ["settings"] = "F0 M6 1h4v2h2v2h2v6h-2v2h-2v2H6v-2H4v-2H2V5h2V3h2z M6 6h4v4H6z",
        ["help"] = "M5 2h6v2H5z M3 4h2v2H3z M11 4h2v3h-2z M9 7h2v2H9z M7 9h2v2H7z M7 12h2v2H7z",
        ["chevron"] = "M3 5h2v2H3z M5 7h2v2H5z M7 9h2v2H7z M9 7h2v2H9z M11 5h2v2h-2z",
        ["use"] = "M7 3h2v10H7z M5 5h2v2H5z M9 5h2v2H9z M3 7h2v2H3z M11 7h2v2h-2z",
        ["close"] = "M3 3h2v2H3z M5 5h2v2H5z M7 7h2v2H7z M9 9h2v2H9z M11 11h2v2h-2z M11 3h2v2h-2z M9 5h2v2H9z M5 9h2v2H5z M3 11h2v2H3z",
        ["copy"] = "F0 M2 5h8v9H2z M4 7h4v5H4z M6 2h8v9h-2V4H6z",
        ["trash"] = "M5 2h6v2H5z M2 4h12v2H2z M3 6h2v8H3z M11 6h2v8h-2z M5 13h6v2H5z M7 7h2v5H7z",
        ["swap"] = "M2 4h10v2H2z M10 2h2v2h-2z M12 4h2v2h-2z M10 6h2v2h-2z M4 10h10v2H4z M4 8h2v2H4z M2 10h2v2H2z M4 12h2v2H4z",
        ["finance"] = "M6 2h4v2H6z M4 4h2v8H4z M10 4h2v8h-2z M6 12h4v2H6z M7 5h2v6H7z",
        ["convert"] = "M2 4h10v2H2z M10 2h2v2h-2z M12 4h2v2h-2z M10 6h2v2h-2z M4 10h10v2H4z M4 8h2v2H4z M2 10h2v2H2z M4 12h2v2H4z",
        ["notebook"] = "F0 M3 1h10v14H3z M5 4h6v1H5z M5 7h6v1H5z M5 10h4v1H5z",
        ["formula"] = "M8 2h5v2H8z M6 3h2v11H6z M4 14h2v1H4z M3 7h7v2H3z",
        ["programmer"] = "F0 M1 3h5v10H1z M3 5h1v6H3z M9 3h2v10H9z M8 4h1v2H8z M13 3h2v10h-2z M12 4h1v2h-2z",
        ["colours"] = "M2 2h5v5H2z M9 2h5v5H9z M2 9h5v5H2z M9 9h5v5H9z",
        ["simple"] = "F0 M2 1h12v14H2z M4 3h8v3H4z M4 8h2v2H4z M7 8h2v2H7z M10 8h2v2h-2z M4 11h2v2H4z M7 11h2v2H7z M10 11h2v2h-2z",
        ["advanced"] = "M2 3h5v2H2z M4 1h1v6H4z M9 3h5v2H9z M2 10h5v2H2z M9 9h5v1H9z M9 12h5v1H9z M11 7h1v1h-1z M11 13h1v1h-1z",
        ["back"] = "M8 3h2v2H8z M6 5h2v2H6z M4 7h2v2H4z M6 9h2v2H6z M8 11h2v2H8z",
        ["forward"] = "M6 3h2v2H6z M8 5h2v2H8z M10 7h2v2h-2z M8 9h2v2H8z M6 11h2v2H6z",
        ["edit"] = "M11 1h2v2h2v2h-2v2h-2v2H9v2H7v2H5v2H1v-4h2v-2h2V7h2V5h2V3h2z",
        ["save"] = "F0 M1 1h12l2 2v12H1z M4 2h7v4H4z M3 9h10v5H3z",
        ["folder"] = "M1 3h5l2 2h7v9H1z",
        ["zoomin"] = "F0 M4 1h5v1h2v2h1v5h-1v2h-2v1H4v-1H2V9H1V4h1V2h2z M4 3v1H3v5h1v1h5V9h1V4H9V3z M6 4h1v2h2v1H7v2H6V7H4V6h2z M11 11h2v2h2v2h-2v-2h-2z",
        ["zoomout"] = "F0 M4 1h5v1h2v2h1v5h-1v2h-2v1H4v-1H2V9H1V4h1V2h2z M4 3v1H3v5h1v1h5V9h1V4H9V3z M4 6h5v1H4z M11 11h2v2h2v2h-2v-2h-2z",
        ["home"] = "M7 1h2v2h2v2h2v2h2v2h-2v6H9v-4H7v4H3V9H1V7h2V5h2V3h2z",
        ["table"] = "F0 M1 2h14v12H1z M3 4h4v3H3z M9 4h4v3H9z M3 9h4v3H3z M9 9h4v3H9z",
        ["star"] = "M7 1h2v4h5v2h-2v2h1v5h-2v-2H9v-1H7v1H5v2H3V9h1V7H2V5h5z",
        ["warning"] = "M7 1h2v2h1v2h1v2h1v2h1v2h1v2h1v2H1v-2h1v-2h1V9h1V7h1V5h1V3h1z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<PixelIcon, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<PixelIcon>();

    static PixelIcon()
    {
        AffectsRender<PixelIcon>(KindProperty, ForegroundProperty);
        WidthProperty.OverrideDefaultValue<PixelIcon>(16);
        HeightProperty.OverrideDefaultValue<PixelIcon>(16);
    }

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static IEnumerable<string> Kinds => Paths.Keys;

    public static Geometry? GetGeometry(string? kind)
    {
        if (kind is null || !Paths.TryGetValue(kind, out var data)) return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue(kind, out var g))
            {
                g = Geometry.Parse(data);
                Cache[kind] = g;
            }
            return g;
        }
    }

    public override void Render(DrawingContext context)
    {
        var g = GetGeometry(Kind);
        if (g is null) return;
        double s = Math.Min(Bounds.Width, Bounds.Height) / 16.0;
        using (context.PushTransform(Matrix.CreateScale(s, s)))
            context.DrawGeometry(Foreground ?? Brushes.Black, null, g);
    }
}
