using Calcy.Core;

namespace Calcy.Graphing;

/// <summary>The visible part of the plane: x and y ranges. Immutable; every operation returns a new viewport.</summary>
public readonly record struct Viewport(double XMin, double XMax, double YMin, double YMax)
{
    /// <summary>Smallest allowed span (prevents zooming into nothing).</summary>
    public const double MinSpan = 1e-12;

    /// <summary>Largest allowed span.</summary>
    public const double MaxSpan = 1e15;

    /// <summary>Width in graph units.</summary>
    public double Width => XMax - XMin;

    /// <summary>Height in graph units.</summary>
    public double Height => YMax - YMin;

    /// <summary>Centre x.</summary>
    public double CenterX => 0.5 * (XMin + XMax);

    /// <summary>Centre y.</summary>
    public double CenterY => 0.5 * (YMin + YMax);

    /// <summary>True when both ranges are finite and non-empty.</summary>
    public bool IsValid => double.IsFinite(XMin) && double.IsFinite(XMax) && double.IsFinite(YMin) && double.IsFinite(YMax) && XMax > XMin && YMax > YMin;

    /// <summary>The standard window: -10 to 10 on both axes.</summary>
    public static Viewport Standard => new(-10, 10, -10, 10);

    /// <summary>The trig window: -2π to 2π (or -360 to 360 in degrees) and -4 to 4.</summary>
    public static Viewport Trig(AngleMode angle = AngleMode.Radian) => angle switch
    {
        AngleMode.Degree => new(-360, 360, -4, 4),
        AngleMode.Gradian => new(-400, 400, -4, 4),
        _ => new(-2 * Math.PI, 2 * Math.PI, -4, 4),
    };

    /// <summary>Each pixel is 0.1 units, centred on the origin (TI ZDecimal).</summary>
    public static Viewport Decimal(double pixelWidth, double pixelHeight) => Scaled(pixelWidth, pixelHeight, 0.1);

    /// <summary>Each pixel is 1 unit, centred on the origin (TI ZInteger).</summary>
    public static Viewport Integer(double pixelWidth, double pixelHeight) => Scaled(pixelWidth, pixelHeight, 1);

    private static Viewport Scaled(double w, double h, double unitsPerPixel)
    {
        double hw = Math.Max(1, w) * unitsPerPixel / 2, hh = Math.Max(1, h) * unitsPerPixel / 2;
        return new Viewport(-hw, hw, -hh, hh);
    }

    /// <summary>The same viewport shifted by (dx, dy) graph units.</summary>
    public Viewport Pan(double dx, double dy) => new(XMin + dx, XMax + dx, YMin + dy, YMax + dy);

    /// <summary>Scales both ranges by <paramref name="factor"/> around (cx, cy). A factor below 1 zooms in.</summary>
    public Viewport Zoom(double factor, double cx, double cy) => Zoom(factor, factor, cx, cy);

    /// <summary>Scales the x and y ranges separately around (cx, cy).</summary>
    public Viewport Zoom(double fx, double fy, double cx, double cy)
    {
        double w = Math.Clamp(Width * fx, MinSpan, MaxSpan), h = Math.Clamp(Height * fy, MinSpan, MaxSpan);
        double rx = Width > 0 ? (cx - XMin) / Width : 0.5, ry = Height > 0 ? (cy - YMin) / Height : 0.5;
        double x0 = cx - rx * w, y0 = cy - ry * h;
        return new Viewport(x0, x0 + w, y0, y0 + h);
    }

    /// <summary>Equal scale on both axes for a canvas of the given pixel size: the axis with less room per unit is widened.</summary>
    public Viewport Square(double pixelWidth, double pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return this;
        double upx = Width / pixelWidth, upy = Height / pixelHeight;
        double u = Math.Max(upx, upy);
        double w = u * pixelWidth, h = u * pixelHeight;
        return new Viewport(CenterX - w / 2, CenterX + w / 2, CenterY - h / 2, CenterY + h / 2);
    }

    /// <summary>The viewport grown by a fraction of its size on every side.</summary>
    public Viewport Expand(double fraction) =>
        new(XMin - Width * fraction, XMax + Width * fraction, YMin - Height * fraction, YMax + Height * fraction);

    /// <summary>True when the other viewport lies inside this one.</summary>
    public bool Covers(Viewport o) => o.XMin >= XMin && o.XMax <= XMax && o.YMin >= YMin && o.YMax <= YMax;

    /// <summary>True when the point is inside.</summary>
    public bool Contains(double x, double y) => x >= XMin && x <= XMax && y >= YMin && y <= YMax;

    /// <summary>A valid viewport: swaps reversed ranges and widens empty ones.</summary>
    public Viewport Normalized()
    {
        double x0 = Math.Min(XMin, XMax), x1 = Math.Max(XMin, XMax), y0 = Math.Min(YMin, YMax), y1 = Math.Max(YMin, YMax);
        if (!double.IsFinite(x0) || !double.IsFinite(x1)) (x0, x1) = (-10, 10);
        if (!double.IsFinite(y0) || !double.IsFinite(y1)) (y0, y1) = (-10, 10);
        if (x1 - x0 < MinSpan) { x0 -= 0.5; x1 += 0.5; }
        if (y1 - y0 < MinSpan) { y0 -= 0.5; y1 += 0.5; }
        return new Viewport(x0, x1, y0, y1);
    }
}

/// <summary>Maps graph coordinates to pixels for a viewport drawn on a canvas of a given size (y grows upwards in graph space).</summary>
public sealed class ViewTransform
{
    /// <summary>Creates the mapping for a viewport and a pixel size.</summary>
    public ViewTransform(Viewport viewport, double pixelWidth, double pixelHeight)
    {
        Viewport = viewport;
        PixelWidth = Math.Max(1, pixelWidth);
        PixelHeight = Math.Max(1, pixelHeight);
        ScaleX = PixelWidth / viewport.Width;
        ScaleY = PixelHeight / viewport.Height;
    }

    /// <summary>The visible region.</summary>
    public Viewport Viewport { get; }

    /// <summary>Canvas width in pixels.</summary>
    public double PixelWidth { get; }

    /// <summary>Canvas height in pixels.</summary>
    public double PixelHeight { get; }

    /// <summary>Pixels per graph unit along x.</summary>
    public double ScaleX { get; }

    /// <summary>Pixels per graph unit along y.</summary>
    public double ScaleY { get; }

    /// <summary>Pixel x of a graph x.</summary>
    public double ToPixelX(double x) => (x - Viewport.XMin) * ScaleX;

    /// <summary>Pixel y of a graph y (0 at the top).</summary>
    public double ToPixelY(double y) => (Viewport.YMax - y) * ScaleY;

    /// <summary>Graph x of a pixel x.</summary>
    public double ToWorldX(double px) => Viewport.XMin + px / ScaleX;

    /// <summary>Graph y of a pixel y.</summary>
    public double ToWorldY(double py) => Viewport.YMax - py / ScaleY;

    /// <summary>Pixel distance between two graph points.</summary>
    public double PixelDistance(Point2 a, Point2 b)
    {
        double dx = (a.X - b.X) * ScaleX, dy = (a.Y - b.Y) * ScaleY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
