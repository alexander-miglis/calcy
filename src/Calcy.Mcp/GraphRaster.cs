using System.Globalization;
using Calcy.Graphing;
using Calcy.Imaging;

namespace Calcy.Mcp;

/// <summary>
/// Draws a sampled graph scene into a PNG without any UI toolkit, matching the SVG export and the app: hatched
/// regions, thick curves, square points, crisp grid and axes, and pixel-font tick labels.
/// </summary>
public static class GraphRaster
{
    public static byte[] RenderPng(GraphScene scene, GraphPalette palette) => Render(scene, palette).ToPng();

    public static RasterImage Render(GraphScene scene, GraphPalette palette)
    {
        int w = scene.Width, h = scene.Height;
        var img = new RasterImage(w, h);
        var t = scene.Transform;
        img.Clear(Rgba.Parse(palette.Background));

        var grid = Rgba.Parse(palette.Grid);
        foreach (var tick in scene.XTicks) img.VLine((int)Math.Round(t.ToPixelX(tick.Value)), 0, h - 1, grid);
        foreach (var tick in scene.YTicks) img.HLine(0, w - 1, (int)Math.Round(t.ToPixelY(tick.Value)), grid);

        var axis = Rgba.Parse(palette.Axis);
        double ax = t.ToPixelX(0), ay = t.ToPixelY(0);
        if (ay >= 0 && ay <= h) img.HLine(0, w - 1, (int)Math.Round(ay) - 1, axis, 2);
        if (ax >= 0 && ax <= w) img.VLine((int)Math.Round(ax) - 1, 0, h - 1, axis, 2);

        int scale = w >= 560 ? 2 : 1;
        var label = Rgba.Parse(palette.Label);
        var paper = Rgba.Parse(palette.Background);
        int labelY = (int)Math.Clamp(ay + 6, 4, h - RasterImage.TextHeight(scale) - 4);
        foreach (var tick in scene.XTicks)
        {
            if (tick.Value == 0) continue;
            var text = tick.Label;
            int tw = RasterImage.TextWidth(text, scale);
            int x = (int)Math.Round(t.ToPixelX(tick.Value) - tw / 2.0);
            Label(img, text, x, labelY, scale, label, paper);
        }
        foreach (var tick in scene.YTicks)
        {
            if (tick.Value == 0) continue;
            var text = tick.Label;
            int tw = RasterImage.TextWidth(text, scale);
            int x = (int)Math.Clamp(ax - 6 - tw, 2, w - tw - 2);
            int y = (int)Math.Round(t.ToPixelY(tick.Value) - RasterImage.TextHeight(scale) / 2.0);
            Label(img, text, x, y, scale, label, paper);
        }

        var line = Rgba.Parse(palette.Line);
        foreach (var geo in scene.Items)
        {
            var ink = Rgba.Parse(palette.Ink(geo.Item.ColorIndex));
            var shade = ink.WithAlpha(150);
            foreach (var c in geo.FillCells)
            {
                double x0 = t.ToPixelX(c.X0), x1 = t.ToPixelX(c.X1), y0 = t.ToPixelY(c.Y1), y1 = t.ToPixelY(c.Y0);
                Hatch(img, x0, y0, x1, y1, shade);
            }
            foreach (var poly in geo.Fills)
                FillPolygon(img, poly.Points.Select(p => (t.ToPixelX(p.X), t.ToPixelY(p.Y))).ToList(), shade, hatched: true);

            double width = geo.Item.Kind is GraphKind.Function or GraphKind.FunctionOfY or GraphKind.Polar or GraphKind.Parametric ? 4 : 3;
            foreach (var curve in geo.Curves)
            {
                var px = curve.Points.Select(q => (t.ToPixelX(q.X), t.ToPixelY(q.Y))).ToList();
                foreach (var run in PolylineClipper.Clip(px, -20, -20, w + 20, h + 20, curve.Closed))
                {
                    var pts = run.Select(q => (q.X, q.Y)).ToList();
                    if (curve.Dashed) DrawDashed(img, pts, width, ink, 10, 6);
                    else img.DrawPolyline(pts, width, ink);
                }
            }
            foreach (var s in geo.Segments)
                img.DrawLine(t.ToPixelX(s.A.X), t.ToPixelY(s.A.Y), t.ToPixelX(s.B.X), t.ToPixelY(s.B.Y), 2, ink);
            foreach (var p in geo.Points)
            {
                double x = t.ToPixelX(p.Position.X), y = t.ToPixelY(p.Position.Y);
                if (x < -20 || x > w + 20 || y < -20 || y > h + 20) continue;
                img.Square(x, y, 14, line);
                img.Square(x, y, 10, ink);
                if (p.Label is not null) Label(img, p.Label, (int)x + 12, (int)y - 16, scale, axis, paper);
            }
        }

        var selection = Rgba.Parse(palette.Selection);
        foreach (var a in scene.Annotations)
        {
            var ink = Rgba.Parse(palette.Ink(a.ColorIndex));
            var px = a.Points.Select(q => (t.ToPixelX(q.X), t.ToPixelY(q.Y))).ToList();
            switch (a.Kind)
            {
                case AnnotationKind.Shade:
                    FillPolygon(img, px, ink.WithAlpha(77), hatched: false);
                    break;
                case AnnotationKind.Line when px.Count >= 2:
                {
                    double dx = px[1].Item1 - px[0].Item1, dy = px[1].Item2 - px[0].Item2, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len == 0) break;
                    double s = 4 * (w + h) / len;
                    DrawDashed(img, [(px[0].Item1 - dx * s, px[0].Item2 - dy * s), (px[0].Item1 + dx * s, px[0].Item2 + dy * s)], 2, ink, 8, 5);
                    break;
                }
                case AnnotationKind.Path:
                    DrawDashed(img, px, 3, ink, 6, 4);
                    break;
                case AnnotationKind.Point:
                    foreach (var q in px)
                    {
                        img.Square(q.Item1, q.Item2, 12, line);
                        img.Square(q.Item1, q.Item2, 8, selection);
                    }
                    break;
            }
        }
        foreach (var poi in scene.PointsOfInterest)
        {
            double x = t.ToPixelX(poi.X), y = t.ToPixelY(poi.Y);
            img.Square(x, y, 12, line);
            img.Square(x, y, 8, Rgba.Parse(palette.Marker));
        }
        return img;
    }

    /// <summary>Text on a small background patch so it stays readable over curves and grid lines.</summary>
    private static void Label(RasterImage img, string text, int x, int y, int scale, Rgba ink, Rgba background)
    {
        int tw = RasterImage.TextWidth(text, scale), th = RasterImage.TextHeight(scale);
        img.FillRect(x - 2, y - 1, tw + 4, th + 2, background.WithAlpha(220));
        img.DrawText(text, x, y, scale, ink);
    }

    /// <summary>The 8-bit diagonal hatch used for regions (2 px blocks on a diagonal, like the SVG pattern).</summary>
    private static bool HatchPixel(int x, int y) => (((x >> 1) & 3) + ((y >> 1) & 3)) % 4 == 3;

    private static void Hatch(RasterImage img, double x0, double y0, double x1, double y1, Rgba ink)
    {
        int ix0 = Math.Max(0, (int)Math.Floor(x0)), ix1 = Math.Min(img.Width - 1, (int)Math.Ceiling(x1) - 1);
        int iy0 = Math.Max(0, (int)Math.Floor(y0)), iy1 = Math.Min(img.Height - 1, (int)Math.Ceiling(y1) - 1);
        for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
                if (HatchPixel(x, y)) img.Blend(x, y, ink, 1);
    }

    /// <summary>Even-odd scanline fill, plain or hatched.</summary>
    private static void FillPolygon(RasterImage img, IReadOnlyList<(double X, double Y)> pts, Rgba ink, bool hatched)
    {
        if (pts.Count < 3) return;
        double minY = Math.Max(0, pts.Min(p => p.Y)), maxY = Math.Min(img.Height - 1, pts.Max(p => p.Y));
        var xs = new List<double>();
        for (int y = (int)Math.Floor(minY); y <= (int)Math.Ceiling(maxY); y++)
        {
            double cy = y + 0.5;
            xs.Clear();
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                var (xi, yi) = pts[i];
                var (xj, yj) = pts[j];
                if (!double.IsFinite(xi) || !double.IsFinite(yi) || !double.IsFinite(xj) || !double.IsFinite(yj)) continue;
                if ((yi > cy) != (yj > cy)) xs.Add(xi + (cy - yi) * (xj - xi) / (yj - yi));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int a = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5)), b = Math.Min(img.Width - 1, (int)Math.Floor(xs[k + 1] - 0.5));
                for (int x = a; x <= b; x++)
                    if (!hatched || HatchPixel(x, y)) img.Blend(x, y, ink, 1);
            }
        }
    }

    private static void DrawDashed(RasterImage img, IReadOnlyList<(double X, double Y)> pts, double width, Rgba ink, double on, double off)
    {
        double phase = 0;
        var dash = new List<(double, double)>();
        bool drawing = true;
        for (int i = 1; i < pts.Count; i++)
        {
            var (x0, y0) = pts[i - 1];
            var (x1, y1) = pts[i];
            double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            double pos = 0;
            if (dash.Count == 0 && drawing) dash.Add((x0, y0));
            while (len - pos > 1e-9)
            {
                double remaining = (drawing ? on : off) - phase;
                double step = Math.Min(remaining, len - pos);
                pos += step;
                phase += step;
                double fx = x0 + (x1 - x0) * pos / len, fy = y0 + (y1 - y0) * pos / len;
                if (drawing) dash.Add((fx, fy));
                if (phase >= (drawing ? on : off) - 1e-9)
                {
                    if (drawing && dash.Count > 1) img.DrawPolyline(dash, width, ink);
                    dash.Clear();
                    drawing = !drawing;
                    phase = 0;
                    if (drawing) dash.Add((fx, fy));
                }
            }
        }
        if (drawing && dash.Count > 1) img.DrawPolyline(dash, width, ink);
    }

    internal static string Num(double v) => v.ToString("G6", CultureInfo.InvariantCulture);
}
