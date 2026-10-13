using System.Globalization;

namespace Calcy.Imaging;

/// <summary>An sRGB colour with straight alpha.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Parses #RGB, #RRGGBB or #RRGGBBAA.</summary>
    public static Rgba Parse(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(ch => new string(ch, 2)));
        if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"'{hex}' is not a colour like #1a2b3c.");
        return s.Length == 6
            ? new Rgba((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : new Rgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public Rgba WithAlpha(byte a) => this with { A = a };

    public override string ToString() => A == 255 ? $"#{R:x2}{G:x2}{B:x2}" : $"#{R:x2}{G:x2}{B:x2}{A:x2}";
}

/// <summary>
/// A small software canvas: anti-aliased thick lines and polylines, rectangles, dots and 5x7 pixel text.
/// Used to render graphs to PNG where no UI toolkit is available (the MCP server and the command line).
/// </summary>
public sealed class RasterImage
{
    private readonly byte[] _px;
    private float[]? _coverage;

    public int Width { get; }
    public int Height { get; }

    public RasterImage(int width, int height)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(width), "Images are 1 to 8192 pixels on each side.");
        Width = width;
        Height = height;
        _px = new byte[width * height * 4];
    }

    /// <summary>An image from packed 0xAARRGGBB pixels, each enlarged to scale × scale (nearest neighbour keeps pixel art crisp).</summary>
    public static RasterImage FromArgb(int width, int height, ReadOnlySpan<uint> argb, int scale = 1)
    {
        if (argb.Length != width * height) throw new ArgumentException("The pixel buffer does not match the size.", nameof(argb));
        if (scale is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(scale));
        var img = new RasterImage(width * scale, height * scale);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint p = argb[y * width + x];
                for (int dy = 0; dy < scale; dy++)
                {
                    int i = (((y * scale + dy) * img.Width) + x * scale) * 4;
                    for (int dx = 0; dx < scale; dx++, i += 4)
                    {
                        img._px[i] = (byte)(p >> 16);
                        img._px[i + 1] = (byte)(p >> 8);
                        img._px[i + 2] = (byte)p;
                        img._px[i + 3] = (byte)(p >> 24);
                    }
                }
            }
        }
        return img;
    }

    /// <summary>The raw RGBA bytes, row by row from the top.</summary>
    public ReadOnlySpan<byte> Pixels => _px;

    public Rgba GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new Rgba(_px[i], _px[i + 1], _px[i + 2], _px[i + 3]);
    }

    public void Clear(Rgba c)
    {
        for (int i = 0; i < _px.Length; i += 4)
        {
            _px[i] = c.R;
            _px[i + 1] = c.G;
            _px[i + 2] = c.B;
            _px[i + 3] = c.A;
        }
    }

    /// <summary>Blends a colour over one pixel with the given coverage (0 to 1).</summary>
    public void Blend(int x, int y, Rgba c, double coverage)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || coverage <= 0) return;
        double a = c.A / 255.0 * Math.Min(1, coverage);
        if (a <= 0) return;
        int i = (y * Width + x) * 4;
        double da = _px[i + 3] / 255.0;
        double oa = a + da * (1 - a);
        if (oa <= 0) return;
        _px[i] = (byte)Math.Round((c.R * a + _px[i] * da * (1 - a)) / oa);
        _px[i + 1] = (byte)Math.Round((c.G * a + _px[i + 1] * da * (1 - a)) / oa);
        _px[i + 2] = (byte)Math.Round((c.B * a + _px[i + 2] * da * (1 - a)) / oa);
        _px[i + 3] = (byte)Math.Round(oa * 255);
    }

    /// <summary>Fills a rectangle; fractional edges are anti-aliased.</summary>
    public void FillRect(double x, double y, double w, double h, Rgba c)
    {
        if (w <= 0 || h <= 0) return;
        double x1 = x + w, y1 = y + h;
        int ix0 = Math.Max(0, (int)Math.Floor(x)), iy0 = Math.Max(0, (int)Math.Floor(y));
        int ix1 = Math.Min(Width, (int)Math.Ceiling(x1)), iy1 = Math.Min(Height, (int)Math.Ceiling(y1));
        for (int py = iy0; py < iy1; py++)
        {
            double cy = Math.Min(py + 1, y1) - Math.Max(py, y);
            for (int px = ix0; px < ix1; px++)
            {
                double cx = Math.Min(px + 1, x1) - Math.Max(px, x);
                Blend(px, py, c, cx * cy);
            }
        }
    }

    /// <summary>A crisp one-pixel line along a pixel row or column (grid lines and axes).</summary>
    public void HLine(int x0, int x1, int y, Rgba c, int thickness = 1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        FillRect(x0, y, x1 - x0 + 1, thickness, c);
    }

    public void VLine(int x, int y0, int y1, Rgba c, int thickness = 1)
    {
        if (y1 < y0) (y0, y1) = (y1, y0);
        FillRect(x, y0, thickness, y1 - y0 + 1, c);
    }

    /// <summary>An anti-aliased line of the given width with round caps.</summary>
    public void DrawLine(double x0, double y0, double x1, double y1, double width, Rgba c) =>
        DrawPolyline([(x0, y0), (x1, y1)], width, c);

    /// <summary>
    /// An anti-aliased polyline with round joins. Coverage is combined per pixel with max, so joints are not drawn twice
    /// and translucent colours stay even.
    /// </summary>
    public void DrawPolyline(IReadOnlyList<(double X, double Y)> points, double width, Rgba c)
    {
        if (points.Count == 0) return;
        double r = Math.Max(0.5, width / 2);
        _coverage ??= new float[Width * Height];
        var cov = _coverage;
        int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = int.MinValue, by1 = int.MinValue;
        void Segment(double ax, double ay, double bx, double by)
        {
            if (!double.IsFinite(ax) || !double.IsFinite(ay) || !double.IsFinite(bx) || !double.IsFinite(by)) return;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - r - 1));
            int maxX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(ax, bx) + r + 1));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - r - 1));
            int maxY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(ay, by) + r + 1));
            if (minX > maxX || minY > maxY) return;
            bx0 = Math.Min(bx0, minX); by0 = Math.Min(by0, minY);
            bx1 = Math.Max(bx1, maxX); by1 = Math.Max(by1, maxY);
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            for (int py = minY; py <= maxY; py++)
            {
                double cy = py + 0.5;
                for (int px = minX; px <= maxX; px++)
                {
                    double cx = px + 0.5;
                    double t = len2 > 0 ? Math.Clamp(((cx - ax) * dx + (cy - ay) * dy) / len2, 0, 1) : 0;
                    double ex = cx - (ax + t * dx), ey = cy - (ay + t * dy);
                    double d = Math.Sqrt(ex * ex + ey * ey);
                    float coverage = (float)Math.Clamp(r + 0.5 - d, 0, 1);
                    int i = py * Width + px;
                    if (coverage > cov[i]) cov[i] = coverage;
                }
            }
        }
        if (points.Count == 1) Segment(points[0].X, points[0].Y, points[0].X, points[0].Y);
        for (int k = 1; k < points.Count; k++) Segment(points[k - 1].X, points[k - 1].Y, points[k].X, points[k].Y);
        if (bx0 > bx1) return;
        for (int py = by0; py <= by1; py++)
        {
            for (int px = bx0; px <= bx1; px++)
            {
                int i = py * Width + px;
                if (cov[i] > 0)
                {
                    Blend(px, py, c, cov[i]);
                    cov[i] = 0;
                }
            }
        }
    }

    /// <summary>A filled square marker centred on a point (the 8-bit style point).</summary>
    public void Square(double cx, double cy, double size, Rgba c) => FillRect(cx - size / 2, cy - size / 2, size, size, c);

    /// <summary>Draws pixel text with its top-left corner at (x, y); scale is the size of one font pixel.</summary>
    public void DrawText(string text, int x, int y, int scale, Rgba c)
    {
        int cx = x;
        foreach (var ch in text)
        {
            var rows = BitmapFont.Glyph(ch);
            for (int row = 0; row < BitmapFont.Height; row++)
            {
                byte bits = rows[row];
                for (int col = 0; col < BitmapFont.Width; col++)
                    if ((bits & (0x10 >> col)) != 0) FillRect(cx + col * scale, y + row * scale, scale, scale, c);
            }
            cx += (BitmapFont.Width + 1) * scale;
        }
    }

    /// <summary>Width in pixels of text drawn with <see cref="DrawText"/>.</summary>
    public static int TextWidth(string text, int scale) => text.Length == 0 ? 0 : (text.Length * (BitmapFont.Width + 1) - 1) * scale;

    public static int TextHeight(int scale) => BitmapFont.Height * scale;

    public byte[] ToPng() => Png.Encode(Width, Height, _px);
}
