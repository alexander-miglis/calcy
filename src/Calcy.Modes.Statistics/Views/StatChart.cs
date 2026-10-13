using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Calcy.Graphing;
using Calcy.Modes.Statistics.Logic;

namespace Calcy.Modes.Statistics.Views;

/// <summary>
/// Draws a <see cref="ChartScene"/> (histograms, box plots, dot plots, normal probability plots, bar charts and the bars
/// of discrete distributions) in the 8-bit style: whole-pixel rectangles with hard outlines, scheme colours, an ink axis
/// and readable tick labels in the label font.
/// </summary>
public sealed class StatChart : Control
{
    /// <summary>The scene to draw.</summary>
    public static readonly StyledProperty<ChartScene?> SceneProperty =
        AvaloniaProperty.Register<StatChart, ChartScene?>(nameof(Scene));

    private readonly Dictionary<string, object?> _resources = [];

    static StatChart() => AffectsRender<StatChart>(SceneProperty);

    /// <summary>Creates an empty chart.</summary>
    public StatChart()
    {
        ClipToBounds = true;
        ResourcesChanged += (_, _) => { _resources.Clear(); InvalidateVisual(); };
        ActualThemeVariantChanged += (_, _) => { _resources.Clear(); InvalidateVisual(); };
        AutomationProperties.SetName(this, "Chart");
    }

    /// <summary>The scene to draw.</summary>
    public ChartScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>The plot area of the last render (for tests).</summary>
    public Rect PlotArea { get; private set; }

    private object? Resource(string key)
    {
        if (_resources.TryGetValue(key, out var v)) return v;
        this.TryFindResource(key, ActualThemeVariant, out v);
        _resources[key] = v;
        return v;
    }

    private IBrush Brush(string token) => Resource("Calcy." + token) as IBrush ?? Brushes.Gray;

    private Typeface Face(bool bold = false) => new(Resource("Calcy.Font.Label") as FontFamily ?? FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal);

    private FormattedText Text(string s, double size, IBrush brush, bool bold = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(bold), size, brush);

    /// <inheritdoc />
    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width < 4 || size.Height < 4) return;
        ctx.FillRectangle(Brush("Card"), new Rect(size));
        var scene = Scene;
        if (scene is null) return;
        if (scene.Message is { Length: > 0 } message && scene.Rects.Count == 0 && scene.Markers.Count == 0)
        {
            var ft = Text(message, 13, Brush("Muted"));
            ft.MaxTextWidth = Math.Max(40, size.Width - 40);
            ft.TextAlignment = TextAlignment.Center;
            ctx.DrawText(ft, new Point(20, Math.Max(8, (size.Height - ft.Height) / 2)));
            return;
        }

        const double labelSize = 11;
        var muted = Brush("Muted");
        double top = scene.YTitle is null ? 12 : 26, right = 16;
        double bottom = 24 + (scene.XTitle is null ? 0 : 16);
        double plotH = Math.Max(10, size.Height - top - bottom);
        var yTicks = scene.ShowYAxis ? YTicks(scene, plotH) : [];
        double left = 14;
        if (scene.ShowYAxis)
        {
            double widest = yTicks.Select(t => Text(t.Label, labelSize, muted).Width).DefaultIfEmpty(0).Max();
            left = Math.Ceiling(widest + 14);
        }
        double plotW = Math.Max(10, size.Width - left - right);
        var area = new Rect(left, top, plotW, plotH);
        PlotArea = area;
        double sx = plotW / (scene.XMax - scene.XMin), sy = plotH / (scene.YMax - scene.YMin);
        double X(double v) => left + (v - scene.XMin) * sx;
        double Y(double v) => top + (scene.YMax - v) * sy;

        // Grid lines on the y ticks.
        var grid = Brush("Grid");
        foreach (var t in yTicks)
        {
            double y = Math.Round(Y(t.Value));
            if (y < top - 0.5 || y > top + plotH + 0.5) continue;
            ctx.FillRectangle(grid, new Rect(left, y, plotW, 1));
        }

        using (ctx.PushClip(area.Inflate(1)))
        {
            foreach (var r in scene.Rects)
            {
                double x0 = X(Math.Min(r.X0, r.X1)), x1 = X(Math.Max(r.X0, r.X1));
                double y0 = Y(Math.Max(r.Y0, r.Y1)), y1 = Y(Math.Min(r.Y0, r.Y1));
                Box(ctx, x0, y0, x1 - x0, y1 - y0, Brush(r.Fill), r.Stroke is null ? null : Brush(r.Stroke), r.StrokeWidth);
            }
            foreach (var s in scene.Segments) Segment(ctx, X(s.X0), Y(s.Y0), X(s.X1), Y(s.Y1), Brush(s.Stroke), s.Width, s.Dashed);
            double unit = sy;
            var line = Brush("Line");
            foreach (var m in scene.Markers)
            {
                double s = m.Size;
                if (scene.FitMarkersToYUnit) s = Math.Max(4, Math.Min(s, Math.Floor(unit - 2)));
                double x = Math.Round(X(m.X) - s / 2), y = Math.Round(Y(m.Y) - s / 2);
                Box(ctx, x, y, s, s, Brush(m.Fill), m.Outline ? line : null, s >= 8 ? 2 : 1);
            }
            foreach (var s in scene.Overlay) Segment(ctx, X(s.X0), Y(s.Y0), X(s.X1), Y(s.Y1), Brush(s.Stroke), s.Width, s.Dashed);
        }

        // Axes over the data, then labels.
        var ink = Brush("Ink");
        double axisY = Math.Round(top + plotH);
        ctx.FillRectangle(ink, new Rect(left - (scene.ShowYAxis ? 0 : 4), axisY, plotW + (scene.ShowYAxis ? 0 : 8), 2));
        if (scene.ShowYAxis) ctx.FillRectangle(ink, new Rect(Math.Round(left) - 2, top, 2, plotH + 2));

        var xTicks = scene.XTicks ?? Ticks.Generate(scene.XMin, scene.XMax, plotW);
        double lastRight = double.NegativeInfinity;
        foreach (var t in xTicks)
        {
            double px = X(t.Value);
            if (px < left - 1 || px > left + plotW + 1) continue;
            ctx.FillRectangle(ink, new Rect(Math.Round(px) - 1, axisY + 2, 2, 4));
            var ft = Text(t.Label, labelSize, muted);
            double lx = Math.Clamp(px - ft.Width / 2, 2, size.Width - ft.Width - 2);
            if (lx < lastRight + 6) continue;
            ctx.DrawText(ft, new Point(lx, axisY + 7));
            lastRight = lx + ft.Width;
        }
        foreach (var t in yTicks)
        {
            double py = Y(t.Value);
            if (py < top - 1 || py > top + plotH + 1) continue;
            var ft = Text(t.Label, labelSize, muted);
            ctx.DrawText(ft, new Point(left - 8 - ft.Width, py - ft.Height / 2));
        }
        if (scene.XTitle is { } xt)
        {
            var ft = Text(xt, labelSize, muted, bold: true);
            ctx.DrawText(ft, new Point(left + (plotW - ft.Width) / 2, size.Height - ft.Height - 4));
        }
        if (scene.YTitle is { } yt)
        {
            var ft = Text(yt, labelSize, muted, bold: true);
            ctx.DrawText(ft, new Point(Math.Max(4, left - 8 - ft.Width / 2), 6));
        }
        foreach (var t in scene.Texts)
        {
            var ft = Text(t.Text, labelSize + 1, Brush(t.Brush), t.Bold);
            double px = X(t.X), py = Y(t.Y);
            var p = t.Anchor switch
            {
                TextAnchor.Below => new Point(px - ft.Width / 2, py + 4),
                TextAnchor.Right => new Point(px + 6, py - ft.Height / 2),
                TextAnchor.Left => new Point(px - 6 - ft.Width, py - ft.Height / 2),
                _ => new Point(px - ft.Width / 2, py - ft.Height - 3),
            };
            p = new Point(Math.Clamp(p.X, 2, Math.Max(2, size.Width - ft.Width - 2)), Math.Clamp(p.Y, 2, Math.Max(2, size.Height - ft.Height - 2)));
            ctx.DrawText(ft, p);
        }
    }

    private static List<Tick> YTicks(ChartScene scene, double pixels)
    {
        var ticks = scene.YTicks?.ToList() ?? [.. Ticks.Generate(scene.YMin, scene.YMax, pixels, TickStyle.Decimal, 30)];
        if (scene.IntegerY) ticks = [.. ticks.Where(t => t.Value == Math.Round(t.Value)).Select(t => new Tick(t.Value, ((long)t.Value).ToString(CultureInfo.InvariantCulture)))];
        return [.. ticks.Where(t => t.Value >= scene.YMin - 1e-12 && t.Value <= scene.YMax + 1e-12)];
    }

    /// <summary>A filled box with a hard outline, snapped to whole pixels.</summary>
    private static void Box(DrawingContext ctx, double x, double y, double w, double h, IBrush fill, IBrush? outline, double thickness)
    {
        double l = Math.Round(x), t = Math.Round(y), r = Math.Round(x + w), b = Math.Round(y + h);
        if (r - l < 1) r = l + 1;
        if (b - t < 1) b = t + 1;
        var rect = new Rect(l, t, r - l, b - t);
        ctx.FillRectangle(fill, rect);
        if (outline is null || thickness <= 0) return;
        double k = Math.Min(thickness, Math.Min(rect.Width, rect.Height) / 2);
        ctx.FillRectangle(outline, new Rect(rect.X, rect.Y, rect.Width, k));
        ctx.FillRectangle(outline, new Rect(rect.X, rect.Bottom - k, rect.Width, k));
        ctx.FillRectangle(outline, new Rect(rect.X, rect.Y, k, rect.Height));
        ctx.FillRectangle(outline, new Rect(rect.Right - k, rect.Y, k, rect.Height));
    }

    /// <summary>Horizontal and vertical segments are drawn as crisp rectangles; others as lines.</summary>
    private static void Segment(DrawingContext ctx, double x0, double y0, double x1, double y1, IBrush brush, double width, bool dashed)
    {
        if (!dashed && Math.Abs(y0 - y1) < 0.5)
        {
            double y = Math.Round(y0 - width / 2);
            ctx.FillRectangle(brush, new Rect(Math.Round(Math.Min(x0, x1)), y, Math.Max(1, Math.Round(Math.Abs(x1 - x0))), width));
            return;
        }
        if (!dashed && Math.Abs(x0 - x1) < 0.5)
        {
            double x = Math.Round(x0 - width / 2);
            ctx.FillRectangle(brush, new Rect(x, Math.Round(Math.Min(y0, y1)), width, Math.Max(1, Math.Round(Math.Abs(y1 - y0)))));
            return;
        }
        var pen = new Pen(brush, width, dashed ? new DashStyle([3, 2], 0) : null, PenLineCap.Square);
        ctx.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));
    }
}
