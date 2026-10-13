using System.Text;
using Calcy.Graphing;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>One line of grapher input with its colour slot (0 to 4 are the scheme's graph inks G1 to G5).</summary>
/// <param name="Text">Grapher input, for example "{(1, 52), (2, 55)}" or "y = 4.5*x + 48".</param>
/// <param name="ColorIndex">Colour slot.</param>
public sealed record GraphLine(string Text, int ColorIndex);

/// <summary>
/// A plot drawn with the function grapher (scatter plots, residual plots, distribution curves): the grapher input
/// lines, the window and shaded areas. The screen turns it into graph items for a <c>GraphCanvas</c>.
/// </summary>
/// <param name="Lines">The items to draw.</param>
/// <param name="Viewport">The window.</param>
/// <param name="Annotations">Shaded areas and marks.</param>
public sealed record GraphSpec(IReadOnlyList<GraphLine> Lines, Viewport Viewport, IReadOnlyList<GraphAnnotation> Annotations)
{
    /// <summary>
    /// The most points drawn with the grapher; larger scatter plots use the statistics chart, whose smaller squares
    /// keep thousands of points readable.
    /// </summary>
    public const int MaxGraphPoints = 250;

    /// <summary>Grapher input for a list of points: {(x1, y1), (x2, y2), ...}.</summary>
    public static string Points(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var sb = new StringBuilder("{");
        int n = Math.Min(x.Count, y.Count);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('(').Append(NumberText.Input(x[i])).Append(", ").Append(NumberText.Input(y[i])).Append(')');
        }
        return sb.Append('}').ToString();
    }

    /// <summary>
    /// A window around the data. The margins leave room for the grapher's tick labels, which sit along the bottom and
    /// left edges when the axes are out of view: more space below and to the left than above and to the right.
    /// </summary>
    public static Viewport Around(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var (x0, x1) = Range(x, 0);
        var (y0, y1) = Range(y, 0);
        double w = x1 - x0, h = y1 - y0;
        return new Viewport(x0 - w * 0.12, x1 + w * 0.07, y0 - h * 0.3, y1 + h * 0.1);
    }

    /// <summary>A padded range; a single value gets a range of ±1 (or ±10% of its size).</summary>
    public static (double Min, double Max) Range(IReadOnlyList<double> v, double margin = 0.1)
    {
        var finite = v.Where(double.IsFinite).ToList();
        if (finite.Count == 0) return (-10, 10);
        double lo = finite.Min(), hi = finite.Max(), span = hi - lo;
        if (!(span > 0))
        {
            double d = Math.Max(1, Math.Abs(lo) * 0.1);
            return (lo - d, hi + d);
        }
        return (lo - span * margin, hi + span * margin);
    }

    /// <summary>
    /// A shaded area drawn <paramref name="layers"/> times over itself: the grapher shades lightly, which is right for
    /// large areas but makes a thin tail hard to see, so tails are layered for a stronger colour.
    /// </summary>
    public static IEnumerable<GraphAnnotation> Shade(Point2[] polygon, int colorIndex, int layers = 1)
    {
        if (polygon.Length < 3) yield break;
        for (int i = 0; i < layers; i++) yield return new GraphAnnotation(AnnotationKind.Shade, polygon, colorIndex);
    }

    /// <summary>Points along a function from a to b followed by the baseline, as a closed polygon for shading.</summary>
    public static Point2[] AreaUnder(Func<double, double> f, double a, double b, int samples = 120)
    {
        if (!(b > a)) return [];
        var pts = new List<Point2>(samples + 3) { new(a, 0) };
        for (int i = 0; i <= samples; i++)
        {
            double x = i == samples ? b : a + (b - a) * i / samples;
            double y = f(x);
            if (!double.IsFinite(y)) y = 0;
            pts.Add(new Point2(x, y));
        }
        pts.Add(new Point2(b, 0));
        return [.. pts];
    }
}
