using System.Globalization;
using Calcy.Graphing;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>A filled rectangle in data coordinates. Fill and stroke are scheme token names ("G1", "Head", "Line").</summary>
public sealed record ChartRect(double X0, double Y0, double X1, double Y1, string Fill, string? Stroke = "Line", double StrokeWidth = 2);

/// <summary>A line segment in data coordinates.</summary>
public sealed record ChartSegment(double X0, double Y0, double X1, double Y1, string Stroke = "Ink", double Width = 3, bool Dashed = false);

/// <summary>A square marker (a data point) in data coordinates; <paramref name="Size"/> is in pixels.</summary>
public sealed record ChartMarker(double X, double Y, string Fill = "G1", double Size = 10, bool Outline = true);

/// <summary>How a text label sits on its point.</summary>
public enum TextAnchor
{
    /// <summary>Centred above the point.</summary>
    Above,

    /// <summary>Centred below the point.</summary>
    Below,

    /// <summary>Left aligned, to the right of the point.</summary>
    Right,

    /// <summary>Right aligned, to the left of the point.</summary>
    Left,
}

/// <summary>A text label at a data point.</summary>
public sealed record ChartText(double X, double Y, string Text, TextAnchor Anchor = TextAnchor.Above, string Brush = "Ink", bool Bold = false);

/// <summary>
/// Everything a <c>StatChart</c> draws, in data coordinates: the visible ranges, axis ticks, rectangles (bars, boxes),
/// segments (whiskers, reference lines), markers and labels. Built by <see cref="ChartBuilder"/> so plot geometry can be
/// tested without a screen.
/// </summary>
public sealed class ChartScene
{
    /// <summary>Left end of the x range.</summary>
    public double XMin { get; init; }

    /// <summary>Right end of the x range.</summary>
    public double XMax { get; init; } = 1;

    /// <summary>Bottom of the y range.</summary>
    public double YMin { get; init; }

    /// <summary>Top of the y range.</summary>
    public double YMax { get; init; } = 1;

    /// <summary>x ticks; null lets the chart choose them.</summary>
    public IReadOnlyList<Tick>? XTicks { get; init; }

    /// <summary>y ticks; null lets the chart choose them.</summary>
    public IReadOnlyList<Tick>? YTicks { get; init; }

    /// <summary>Draw the y axis with labels (off for box plots and dot plots).</summary>
    public bool ShowYAxis { get; init; } = true;

    /// <summary>Only whole-number y ticks (counts).</summary>
    public bool IntegerY { get; init; }

    /// <summary>Title under the x axis.</summary>
    public string? XTitle { get; init; }

    /// <summary>Title of the y axis (drawn at the top left).</summary>
    public string? YTitle { get; init; }

    /// <summary>Rectangles, drawn first.</summary>
    public List<ChartRect> Rects { get; } = [];

    /// <summary>Segments, drawn over the rectangles.</summary>
    public List<ChartSegment> Segments { get; } = [];

    /// <summary>Markers, drawn over the segments.</summary>
    public List<ChartMarker> Markers { get; } = [];

    /// <summary>Segments drawn over the markers (a fitted curve through a cloud of points).</summary>
    public List<ChartSegment> Overlay { get; } = [];

    /// <summary>Labels, drawn last.</summary>
    public List<ChartText> Texts { get; } = [];

    /// <summary>Shrink markers so stacked dots (one y unit apart) do not overlap.</summary>
    public bool FitMarkersToYUnit { get; init; }

    /// <summary>A message shown instead of a plot (no data, or why the plot cannot be drawn).</summary>
    public string? Message { get; init; }

    /// <summary>A scene that only shows a message.</summary>
    public static ChartScene Empty(string message) => new() { Message = message, ShowYAxis = false };
}

/// <summary>Builds chart scenes for the plots that are drawn without the function grapher.</summary>
public static class ChartBuilder
{
    /// <summary>A histogram: bars on the bins, x ticks on the bin edges, counts on the y axis.</summary>
    public static ChartScene HistogramScene(HistogramResult h, string xTitle)
    {
        double maxCount = h.Bins.Count == 0 ? 1 : Math.Max(1, h.Bins.Max(b => b.Count));
        double x0 = h.Bins[0].Lower, x1 = h.Bins[^1].Upper;
        double pad = (x1 - x0) * 0.04;
        bool whole = h.Bins.All(b => b.Count == Math.Round(b.Count));
        var scene = new ChartScene
        {
            XMin = x0 - pad,
            XMax = x1 + pad,
            YMin = 0,
            YMax = maxCount * 1.12,
            XTicks = EdgeTicks(h),
            IntegerY = whole,
            XTitle = xTitle,
            YTitle = "count",
        };
        foreach (var b in h.Bins)
            if (b.Count > 0) scene.Rects.Add(new ChartRect(b.Lower, 0, b.Upper, b.Count, "G1"));
        return scene;
    }

    private static List<Tick> EdgeTicks(HistogramResult h)
    {
        var edges = h.Bins.Select(b => b.Lower).Append(h.Bins[^1].Upper).ToList();
        int every = Math.Max(1, (int)Math.Ceiling(edges.Count / 9.0));
        var ticks = new List<Tick>();
        for (int i = 0; i < edges.Count; i += every) ticks.Add(new Tick(edges[i], Label(edges[i], h.Width * every)));
        return ticks;
    }

    /// <summary>One or more horizontal box plots on a shared value axis, with whiskers, medians and outliers.</summary>
    public static ChartScene BoxScene(IReadOnlyList<BoxPlotStats> boxes, string xTitle)
    {
        if (boxes.Count == 0) return ChartScene.Empty("No data");
        double lo = boxes.Min(b => Math.Min(b.Min, b.LowerWhisker)), hi = boxes.Max(b => Math.Max(b.Max, b.UpperWhisker));
        double span = hi - lo;
        if (!(span > 0)) span = Math.Max(1, Math.Abs(lo));
        var scene = new ChartScene
        {
            XMin = lo - span * 0.06,
            XMax = hi + span * 0.06,
            YMin = 0,
            YMax = boxes.Count,
            ShowYAxis = false,
            XTitle = xTitle,
        };
        string[] fills = ["Head", "Sel", "Ok"];
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[boxes.Count - 1 - i];
            double y = i + 0.5, h = boxes.Count == 1 ? 0.28 : 0.22;
            scene.Segments.Add(new ChartSegment(b.LowerWhisker, y, b.Q1, y, "Ink", 3));
            scene.Segments.Add(new ChartSegment(b.Q3, y, b.UpperWhisker, y, "Ink", 3));
            scene.Segments.Add(new ChartSegment(b.LowerWhisker, y - h * 0.45, b.LowerWhisker, y + h * 0.45, "Ink", 6));
            scene.Segments.Add(new ChartSegment(b.UpperWhisker, y - h * 0.45, b.UpperWhisker, y + h * 0.45, "Ink", 6));
            scene.Rects.Add(new ChartRect(b.Q1, y - h, b.Q3, y + h, fills[(boxes.Count - 1 - i) % fills.Length], "Line", 3));
            scene.Segments.Add(new ChartSegment(b.Median, y - h, b.Median, y + h, "Line", 4));
            foreach (var o in b.Outliers.Distinct()) scene.Markers.Add(new ChartMarker(o, y, "Err", 10));
            if (boxes.Count > 1) scene.Texts.Add(new ChartText(b.Q1, y + h, b.Name, TextAnchor.Above, "Muted"));
        }
        return scene;
    }

    /// <summary>A dot plot: one square per observation, stacked over its value.</summary>
    public static ChartScene DotScene(double resolution, IReadOnlyList<DotStack> stacks, string xTitle)
    {
        if (stacks.Count == 0) return ChartScene.Empty("No data");
        double lo = stacks[0].X, hi = stacks[^1].X;
        double span = Math.Max(hi - lo, resolution * 4);
        int tallest = stacks.Max(s => s.Count);
        var scene = new ChartScene
        {
            XMin = lo - span * 0.06 - resolution / 2,
            XMax = hi + span * 0.06 + resolution / 2,
            YMin = 0,
            YMax = Math.Max(tallest, 4) + 0.6,
            ShowYAxis = false,
            FitMarkersToYUnit = true,
            XTitle = xTitle,
        };
        foreach (var s in stacks)
            for (int k = 0; k < s.Count; k++) scene.Markers.Add(new ChartMarker(s.X, k + 0.5, "G1", 14));
        return scene;
    }

    /// <summary>A normal probability plot: the data against normal scores, with the reference line.</summary>
    public static ChartScene NormalProbabilityScene(double[] x, double[] z, double mean, double sd, string xTitle)
    {
        double lo = x[0], hi = x[^1], span = hi - lo;
        if (!(span > 0)) span = 1;
        double zmax = Math.Max(2.5, z.Max(Math.Abs) + 0.4);
        var scene = new ChartScene
        {
            XMin = lo - span * 0.08,
            XMax = hi + span * 0.08,
            YMin = -zmax,
            YMax = zmax,
            XTitle = xTitle,
            YTitle = "z",
        };
        if (sd > 0)
        {
            double xa = scene.XMin, xb = scene.XMax;
            scene.Segments.Add(new ChartSegment(xa, (xa - mean) / sd, xb, (xb - mean) / sd, "G2", 3));
        }
        for (int i = 0; i < x.Length; i++) scene.Markers.Add(new ChartMarker(x[i], z[i], "G1", 10));
        return scene;
    }

    /// <summary>A bar chart of category counts with the labels under the bars.</summary>
    public static ChartScene BarScene(IReadOnlyList<(string Label, double Count, double? Value)> categories, string xTitle)
    {
        if (categories.Count == 0) return ChartScene.Empty("No data");
        double max = Math.Max(1, categories.Max(c => c.Count));
        int every = Math.Max(1, (int)Math.Ceiling(categories.Count / 14.0));
        var ticks = new List<Tick>();
        for (int i = 0; i < categories.Count; i += every) ticks.Add(new Tick(i, Shorten(categories[i].Label)));
        var scene = new ChartScene
        {
            XMin = -0.7,
            XMax = categories.Count - 0.3,
            YMin = 0,
            YMax = max * 1.15,
            XTicks = ticks,
            IntegerY = categories.All(c => c.Count == Math.Round(c.Count)),
            XTitle = xTitle,
            YTitle = "count",
        };
        string[] fills = ["G1", "G2", "G3", "G4", "G5"];
        for (int i = 0; i < categories.Count; i++)
        {
            scene.Rects.Add(new ChartRect(i - 0.36, 0, i + 0.36, categories[i].Count, fills[i % fills.Length]));
            if (categories.Count <= 16) scene.Texts.Add(new ChartText(i, categories[i].Count, NumberText.Count(categories[i].Count), TextAnchor.Above, "Ink", Bold: true));
        }
        return scene;
    }

    /// <summary>
    /// Bars of a discrete probability function P(X = k) for k from <paramref name="first"/>; bars from
    /// <paramref name="lower"/> to <paramref name="upper"/> are highlighted (the probability being computed).
    /// </summary>
    public static ChartScene PmfScene(int first, IReadOnlyList<double> probabilities, double lower, double upper, int? marked = null)
    {
        if (probabilities.Count == 0) return ChartScene.Empty("Nothing to draw for these parameters");
        double max = Math.Max(1e-12, probabilities.Max());
        int last = first + probabilities.Count - 1;
        var scene = new ChartScene
        {
            XMin = first - 0.8,
            XMax = last + 0.8,
            YMin = 0,
            YMax = max * 1.15,
            XTitle = "x",
            YTitle = "P(X = x)",
            XTicks = IntegerTicks(first, last),
        };
        bool thin = probabilities.Count > 60;
        for (int i = 0; i < probabilities.Count; i++)
        {
            int k = first + i;
            if (probabilities[i] <= max * 1e-4 && thin) continue;
            bool on = k >= lower - 1e-9 && k <= upper + 1e-9;
            bool mark = marked == k;
            double half = thin ? 0.5 : 0.36;
            scene.Rects.Add(new ChartRect(k - half, 0, k + half, probabilities[i], on || mark ? "Sel" : "Key", thin ? null : "Line", 2));
        }
        return scene;
    }

    private static List<Tick> IntegerTicks(int first, int last)
    {
        int span = last - first;
        int step = span <= 12 ? 1 : (int)Math.Max(1, Histogram.Nice(span / 10.0));
        var ticks = new List<Tick>();
        int start = (int)Math.Ceiling(first / (double)step) * step;
        for (int k = start; k <= last; k += step) ticks.Add(new Tick(k, k.ToString(CultureInfo.InvariantCulture)));
        return ticks;
    }

    /// <summary>
    /// A scatter plot for many points (the grapher's large point squares would merge into a blob): small squares, an
    /// optional fitted curve in the second graph colour and an optional zero line (residual plots).
    /// </summary>
    public static ChartScene ScatterScene(IReadOnlyList<double> x, IReadOnlyList<double> y, Func<double, double>? curve, string xTitle, string? yTitle, bool zeroLine = false)
    {
        var (x0, x1) = GraphSpec.Range(x, 0.04);
        var (y0, y1) = GraphSpec.Range(zeroLine ? y.Append(0).ToList() : y, 0.06);
        var scene = new ChartScene { XMin = x0, XMax = x1, YMin = y0, YMax = y1, XTitle = xTitle, YTitle = yTitle };
        if (zeroLine) scene.Segments.Add(new ChartSegment(x0, 0, x1, 0, "Ink", 2));
        int n = Math.Min(x.Count, y.Count);
        double size = n > 5000 ? 3 : n > 1500 ? 4 : 6;
        for (int i = 0; i < n; i++)
            if (double.IsFinite(x[i]) && double.IsFinite(y[i])) scene.Markers.Add(new ChartMarker(x[i], y[i], zeroLine ? "G4" : "G1", size, Outline: false));
        if (curve is not null)
        {
            const int Samples = 160;
            double? px = null, py = null;
            for (int i = 0; i <= Samples; i++)
            {
                double cx = x0 + (x1 - x0) * i / Samples, cy = curve(cx);
                bool ok = double.IsFinite(cy) && cy > y0 - (y1 - y0) && cy < y1 + (y1 - y0);
                if (ok && px is double ax && py is double ay) scene.Overlay.Add(new ChartSegment(ax, ay, cx, cy, "G2", 3));
                px = ok ? cx : null;
                py = ok ? cy : null;
            }
        }
        return scene;
    }

    /// <summary>A short tick label with as many decimals as the step needs.</summary>
    public static string Label(double v, double step) => Ticks.Format(v, step);

    private static string Shorten(string s) => s.Length > 10 ? s[..9] + "…" : s;
}
