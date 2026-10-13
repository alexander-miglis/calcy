namespace Calcy.Graphing;

/// <summary>A point in graph (world) coordinates.</summary>
public readonly record struct Point2(double X, double Y)
{
    /// <summary>True when both coordinates are finite numbers.</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    /// <summary>Euclidean distance to another point.</summary>
    public double DistanceTo(Point2 o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

/// <summary>A line segment in graph coordinates.</summary>
public readonly record struct Segment2(Point2 A, Point2 B);

/// <summary>An axis-aligned rectangle in graph coordinates (X0 ≤ X1, Y0 ≤ Y1).</summary>
public readonly record struct Rect2(double X0, double Y0, double X1, double Y1)
{
    /// <summary>Width of the rectangle.</summary>
    public double Width => X1 - X0;

    /// <summary>Height of the rectangle.</summary>
    public double Height => Y1 - Y0;
}

/// <summary>A connected run of curve points in graph coordinates.</summary>
/// <param name="Points">The points in drawing order.</param>
/// <param name="Closed">True when the last point joins back to the first (closed implicit curves).</param>
/// <param name="Member">Index of the family member the curve belongs to.</param>
/// <param name="Dashed">Drawn dashed (the boundary of a strict inequality).</param>
public sealed record Polyline(Point2[] Points, bool Closed = false, int Member = 0, bool Dashed = false);

/// <summary>A filled polygon in graph coordinates (inequality shading, integral regions).</summary>
public sealed record Polygon2(Point2[] Points, int Member = 0);

/// <summary>A labelled point to draw (user points and isolated points of implicit curves).</summary>
public sealed record PlotPoint(Point2 Position, string? Label = null, int Member = 0);

/// <summary>Everything sampled for one graph item in one viewport, in graph coordinates.</summary>
public sealed class ItemGeometry
{
    /// <summary>The item this geometry belongs to.</summary>
    public required GraphItem Item { get; init; }

    /// <summary>The region that was sampled (the viewport plus a margin).</summary>
    public required Viewport Sampled { get; init; }

    /// <summary>Curves (functions, boundaries, implicit curves).</summary>
    public IReadOnlyList<Polyline> Curves { get; init; } = [];

    /// <summary>Filled polygons (inequalities in y or x).</summary>
    public IReadOnlyList<Polygon2> Fills { get; init; } = [];

    /// <summary>Filled cells (implicit inequality regions), merged into rows.</summary>
    public IReadOnlyList<Rect2> FillCells { get; init; } = [];

    /// <summary>Points to draw as squares.</summary>
    public IReadOnlyList<PlotPoint> Points { get; init; } = [];

    /// <summary>Short segments (slope field ticks).</summary>
    public IReadOnlyList<Segment2> Segments { get; init; } = [];

    /// <summary>True when the sampler could not guarantee every feature was found (shown as a warning).</summary>
    public bool MayBeIncomplete { get; init; }

    /// <summary>A geometry with nothing in it.</summary>
    public static ItemGeometry Empty(GraphItem item, Viewport v) => new() { Item = item, Sampled = v };
}
