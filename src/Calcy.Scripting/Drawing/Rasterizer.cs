namespace Calcy.Scripting.Drawing;

/// <summary>
/// Draws <see cref="DrawCommand"/>s into a pixel buffer of packed <see cref="DrawColor"/> values, without anti-aliasing,
/// so pictures stay pixel crisp at any zoom. Coordinates outside the buffer are clipped.
/// </summary>
internal sealed class Rasterizer(uint[] pixels, int width, int height)
{
    private const double Huge = 1e7;

    public uint[] Pixels { get; } = pixels;
    public int Width { get; } = width;
    public int Height { get; } = height;

    public void Execute(DrawCommand c)
    {
        uint v = c.Color.Value;
        switch (c.Kind)
        {
            case DrawKind.Clear:
                Array.Fill(Pixels, v);
                break;
            case DrawKind.Background:
                break;
            case DrawKind.Pixel:
                if (Finite(c.X, c.Y)) Set(Round(c.X), Round(c.Y), v);
                break;
            case DrawKind.Line:
                Line(c.X, c.Y, c.X2, c.Y2, v, PenWidth(c.Width));
                break;
            case DrawKind.Rect:
                Rect(c.X, c.Y, c.X2, c.Y2, v, c.Fill, PenWidth(c.Width));
                break;
            case DrawKind.Circle:
                Circle(c.X, c.Y, c.Size, v, c.Fill, PenWidth(c.Width));
                break;
            case DrawKind.Arc:
                Arc(c, v);
                break;
            case DrawKind.Polygon:
                if (c.Points is { Count: >= 2 } pts) Polygon(pts, v, c.Fill, PenWidth(c.Width), closed: true);
                break;
            case DrawKind.Text:
                Text(c.X, c.Y, c.Text ?? "", v, Math.Clamp((int)Math.Round(c.Size), 1, 16));
                break;
        }
    }

    /// <summary>Replaces every pixel that has the old value (used when the background colour changes).</summary>
    public void Replace(uint from, uint to)
    {
        if (from == to) return;
        var p = Pixels;
        for (int i = 0; i < p.Length; i++)
            if (p[i] == from) p[i] = to;
    }

    private static int PenWidth(double w) => double.IsFinite(w) ? Math.Clamp((int)Math.Round(w), 1, 64) : 1;

    private static bool Finite(double a, double b) => double.IsFinite(a) && double.IsFinite(b);

    private static bool Finite(double a, double b, double c) => Finite(a, b) && double.IsFinite(c);

    private static bool Finite(double a, double b, double c, double d) => Finite(a, b) && Finite(c, d);

    private static bool Finite(double a, double b, double c, double d, double e, double f) => Finite(a, b, c, d) && Finite(e, f);

    private static int Round(double v) => (int)Math.Floor(Math.Clamp(v, -Huge, Huge) + 0.5);

    private void Set(int x, int y, uint v)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height) Pixels[y * Width + x] = v;
    }

    private void Span(int y, int x0, int x1, uint v)
    {
        if ((uint)y >= (uint)Height) return;
        if (x0 > x1) (x0, x1) = (x1, x0);
        x0 = Math.Max(0, x0);
        x1 = Math.Min(Width - 1, x1);
        if (x0 > x1) return;
        Array.Fill(Pixels, v, y * Width + x0, x1 - x0 + 1);
    }

    private void Dot(int x, int y, uint v, int w)
    {
        if (w <= 1)
        {
            Set(x, y, v);
            return;
        }
        // A square pen keeps thick lines crisp and joins without gaps.
        int a = -(w - 1) / 2, b = a + w - 1;
        for (int dy = a; dy <= b; dy++) Span(y + dy, x + a, x + b, v);
    }

    // ------------------------------------------------------------------ lines

    public void Line(double x0, double y0, double x1, double y1, uint v, int w)
    {
        if (!Finite(x0, y0, x1, y1)) return;
        double m = w + 2;
        if (!Clip(ref x0, ref y0, ref x1, ref y1, -m, -m, Width - 1 + m, Height - 1 + m)) return;
        int ix0 = Round(x0), iy0 = Round(y0), ix1 = Round(x1), iy1 = Round(y1);
        int dx = Math.Abs(ix1 - ix0), sx = ix0 < ix1 ? 1 : -1;
        int dy = -Math.Abs(iy1 - iy0), sy = iy0 < iy1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Dot(ix0, iy0, v, w);
            if (ix0 == ix1 && iy0 == iy1) break;
            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                ix0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                iy0 += sy;
            }
        }
    }

    /// <summary>Liang and Barsky clipping of a segment to a rectangle.</summary>
    private static bool Clip(ref double x0, ref double y0, ref double x1, ref double y1, double xmin, double ymin, double xmax, double ymax)
    {
        double t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
        bool Edge(double p, double q, ref double a, ref double b)
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0)
            {
                if (r > b) return false;
                if (r > a) a = r;
            }
            else
            {
                if (r < a) return false;
                if (r < b) b = r;
            }
            return true;
        }
        if (!Edge(-dx, x0 - xmin, ref t0, ref t1) || !Edge(dx, xmax - x0, ref t0, ref t1) ||
            !Edge(-dy, y0 - ymin, ref t0, ref t1) || !Edge(dy, ymax - y0, ref t0, ref t1)) return false;
        double nx0 = x0 + t0 * dx, ny0 = y0 + t0 * dy, nx1 = x0 + t1 * dx, ny1 = y0 + t1 * dy;
        x0 = nx0; y0 = ny0; x1 = nx1; y1 = ny1;
        return true;
    }

    // ------------------------------------------------------------------ shapes

    private void Rect(double x, double y, double w, double h, uint v, bool fill, int pen)
    {
        if (!Finite(x, y, w, h)) return;
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        int x0 = Round(x), y0 = Round(y);
        int x1 = Round(x + w) - 1, y1 = Round(y + h) - 1;
        if (x1 < x0) x1 = x0;
        if (y1 < y0) y1 = y0;
        if (fill)
        {
            for (int yy = Math.Max(0, y0); yy <= Math.Min(Height - 1, y1); yy++) Span(yy, x0, x1, v);
            return;
        }
        // The outline sits inside the rectangle, like on the calculators.
        for (int i = 0; i < pen; i++)
        {
            int ax = x0 + i, ay = y0 + i, bx = x1 - i, by = y1 - i;
            if (ax > bx || ay > by) break;
            Span(ay, ax, bx, v);
            Span(by, ax, bx, v);
            for (int yy = Math.Max(0, ay); yy <= Math.Min(Height - 1, by); yy++)
            {
                Set(ax, yy, v);
                Set(bx, yy, v);
            }
        }
    }

    private void Circle(double cx, double cy, double r, uint v, bool fill, int pen)
    {
        if (!Finite(cx, cy, r)) return;
        r = Math.Abs(r);
        if (r > Huge) return;
        int icx = Round(cx), icy = Round(cy);
        if (r < 0.5)
        {
            Set(icx, icy, v);
            return;
        }
        double outer = r + 0.5, inner = fill ? -1 : Math.Max(-1, r + 0.5 - pen);
        int ry = (int)Math.Ceiling(outer);
        int yStart = Math.Max(-ry, -icy), yEnd = Math.Min(ry, Height - 1 - icy);
        for (int dy = yStart; dy <= yEnd; dy++)
        {
            double o2 = outer * outer - dy * dy;
            if (o2 < 0) continue;
            int xo = (int)Math.Floor(Math.Sqrt(o2) - 1e-9);
            if (inner < 0 || inner * inner - dy * dy < 0)
            {
                Span(icy + dy, icx - xo, icx + xo, v);
                continue;
            }
            int xi = (int)Math.Ceiling(Math.Sqrt(inner * inner - dy * dy) + 1e-9);
            if (xi > xo)
            {
                // Thin rings: make sure every row keeps at least its outermost pixels.
                xi = xo;
            }
            Span(icy + dy, icx - xo, icx - xi, v);
            Span(icy + dy, icx + xi, icx + xo, v);
        }
    }

    private void Arc(DrawCommand c, uint v)
    {
        if (!Finite(c.X, c.Y, c.X2, c.Y2, c.Start, c.Extent)) return;
        double rx = Math.Abs(c.X2) / 2, ry = Math.Abs(c.Y2) / 2;
        double cx = c.X + c.X2 / 2, cy = c.Y + c.Y2 / 2;
        double extent = Math.Clamp(c.Extent, -3600, 3600);
        int steps = Math.Clamp((int)(Math.Abs(extent) / 360 * Math.Max(16, Math.Min(720, (rx + ry) * 2))), 2, 2000);
        var pts = new List<double>(steps * 2 + 4);
        if (c.Fill)
        {
            pts.Add(cx);
            pts.Add(cy);
        }
        for (int i = 0; i <= steps; i++)
        {
            double a = (c.Start + extent * i / steps) * Math.PI / 180;
            pts.Add(cx + rx * Math.Cos(a));
            pts.Add(cy - ry * Math.Sin(a));
        }
        if (c.Fill) Polygon(pts, v, true, 1, closed: true);
        else Polygon(pts, v, false, PenWidth(c.Width), closed: false);
    }

    private void Polygon(IReadOnlyList<double> p, uint v, bool fill, int pen, bool closed)
    {
        int n = p.Count / 2;
        for (int i = 0; i < n * 2; i++)
            if (!double.IsFinite(p[i])) return;
        if (fill && n >= 3)
        {
            FillPolygon(p, n, v);
            return;
        }
        if (n == 1)
        {
            Dot(Round(p[0]), Round(p[1]), v, pen);
            return;
        }
        for (int i = 0; i + 1 < n; i++) Line(p[2 * i], p[2 * i + 1], p[2 * i + 2], p[2 * i + 3], v, pen);
        if (closed && n > 2) Line(p[2 * n - 2], p[2 * n - 1], p[0], p[1], v, pen);
    }

    private void FillPolygon(IReadOnlyList<double> p, int n, uint v)
    {
        double minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            minY = Math.Min(minY, p[2 * i + 1]);
            maxY = Math.Max(maxY, p[2 * i + 1]);
        }
        int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
        var xs = new List<double>(8);
        for (int y = y0; y <= y1; y++)
        {
            double sy = y + 0.5;
            xs.Clear();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double ax = p[2 * i], ay = p[2 * i + 1], bx = p[2 * j], by = p[2 * j + 1];
                if ((ay <= sy && by > sy) || (by <= sy && ay > sy))
                    xs.Add(ax + (sy - ay) / (by - ay) * (bx - ax));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int a = (int)Math.Ceiling(Math.Clamp(xs[k], -Huge, Huge) - 0.5), b = (int)Math.Floor(Math.Clamp(xs[k + 1], -Huge, Huge) - 0.5);
                if (b >= a) Span(y, a, b, v);
            }
        }
        // Edges as well, so thin or tiny polygons never vanish.
        Polygon(p, v, false, 1, closed: true);
    }

    // ------------------------------------------------------------------ text

    private void Text(double x, double y, string text, uint v, int scale)
    {
        if (!Finite(x, y) || text.Length == 0) return;
        int ox = Round(x), cy = Round(y), cx = ox;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                cx = ox;
                cy += CanvasFont.LineHeight * scale;
                continue;
            }
            if (cx > Width || cy > Height) continue;
            var glyph = CanvasFont.Glyph(ch);
            for (int col = 0; col < CanvasFont.GlyphWidth; col++)
            {
                int bits = glyph[col];
                for (int row = 0; row < CanvasFont.GlyphHeight; row++)
                {
                    if ((bits & (1 << row)) == 0) continue;
                    int px = cx + col * scale, py = cy + row * scale;
                    for (int dy = 0; dy < scale; dy++) Span(py + dy, px, px + scale - 1, v);
                }
            }
            cx += CanvasFont.Advance * scale;
        }
    }
}
