using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>One histogram bin [Lower, Upper) and how many values fall in it.</summary>
/// <param name="Lower">Left edge (included).</param>
/// <param name="Upper">Right edge (excluded).</param>
/// <param name="Count">Number of values (sum of their frequencies).</param>
public sealed record HistogramBin(double Lower, double Upper, double Count);

/// <summary>A binned list.</summary>
/// <param name="Start">The left edge of the first bin.</param>
/// <param name="Width">The bin width.</param>
/// <param name="Bins">The bins, left to right.</param>
/// <param name="Total">Number of values in the bins.</param>
/// <param name="LeftOut">Values below <paramref name="Start"/> (not drawn).</param>
public sealed record HistogramResult(double Start, double Width, IReadOnlyList<HistogramBin> Bins, double Total, double LeftOut);

/// <summary>
/// Histogram binning with the TI convention: every bin includes its left edge and excludes its right edge, so a value on
/// an edge belongs to the bin on its right. Edges are computed as start + k·width, with a tolerance for binary rounding
/// (0.3 with width 0.1 lands in the bin [0.3, 0.4)).
/// </summary>
public static class Histogram
{
    /// <summary>The most bins drawn.</summary>
    public const int MaxBins = 400;

    /// <summary>
    /// A readable bin width (1, 2, 2.5 or 5 times a power of ten) giving about the Sturges number of bins;
    /// whole-number data never gets bins narrower than 1.
    /// </summary>
    public static double AutoWidth(IReadOnlyList<double> values, double count)
    {
        if (values.Count == 0) return 1;
        double min = values.Min(), max = values.Max();
        double span = max - min;
        if (!(span > 0)) return 1;
        int k = (int)Math.Clamp(Math.Ceiling(Math.Log2(Math.Max(1, count)) + 1), 4, 30);
        double raw = span / k;
        double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        // The round width closest to the Sturges width (on a log scale), so 1.4 becomes 1 and 6.4 becomes 5.
        double width = new[] { 1.0, 2.0, 2.5, 5.0, 10.0 }.Select(m => m * p).MinBy(w => Math.Abs(Math.Log(w / raw)));
        if (values.All(v => v == Math.Round(v)) && width < 1) width = 1;
        return width;
    }

    /// <summary>The smallest of 1, 2, 2.5, 5 × 10^e that is at least <paramref name="raw"/>.</summary>
    public static double Nice(double raw)
    {
        if (!(raw > 0) || !double.IsFinite(raw)) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var m in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
            if (m * p >= raw * (1 - 1e-12)) return m * p;
        return 10 * p;
    }

    /// <summary>The automatic start: the largest multiple of the width at or below the minimum.</summary>
    public static double AutoStart(double min, double width) => Math.Floor(min / width + 1e-9) * width;

    /// <summary>Bins the values. Throws <see cref="StatDataException"/> for a bad width or too many bins.</summary>
    public static HistogramResult Bin(IReadOnlyList<double> values, IReadOnlyList<double>? frequencies, double width, double? start = null)
    {
        if (!(width > 0) || !double.IsFinite(width)) throw new StatDataException("The bin width must be a positive number");
        if (values.Count == 0) throw new StatDataException("There are no values to put in bins");
        double min = values.Min(), max = values.Max();
        double s = start ?? AutoStart(min, width);
        if (!double.IsFinite(s)) throw new StatDataException("The bin start must be a number");
        int last = Index(max, s, width);
        if (last < 0) throw new StatDataException($"Every value is below the start {NumberText.Display(s)}");
        if (last + 1 > MaxBins) throw new StatDataException($"That makes {last + 1} bins; use a bin width of at least {NumberText.Display(Nice((max - s) / MaxBins))}");
        var counts = new double[last + 1];
        double total = 0, leftOut = 0;
        for (int i = 0; i < values.Count; i++)
        {
            double w = frequencies?[i] ?? 1;
            int k = Index(values[i], s, width);
            if (k < 0)
            {
                leftOut += w;
                continue;
            }
            counts[k] += w;
            total += w;
        }
        var bins = new HistogramBin[counts.Length];
        for (int k = 0; k < counts.Length; k++) bins[k] = new HistogramBin(Edge(s, width, k), Edge(s, width, k + 1), counts[k]);
        return new HistogramResult(s, width, bins, total, leftOut);
    }

    /// <summary>The bin index of a value (negative below the start).</summary>
    public static int Index(double x, double start, double width)
    {
        double pos = (x - start) / width;
        double k = Math.Floor(pos + 1e-9 * Math.Max(1, Math.Abs(pos)));
        return k > int.MaxValue / 2 ? int.MaxValue / 2 : (int)k;
    }

    /// <summary>Edge number k, rounded to the width's decimals so labels read 0.3 rather than 0.30000000000000004.</summary>
    public static double Edge(double start, double width, int k)
    {
        double e = start + k * width;
        int decimals = (int)Math.Clamp(Math.Ceiling(-Math.Log10(width)) + 2, 0, 15);
        return Math.Round(e, decimals);
    }
}

/// <summary>The numbers a box plot is drawn from.</summary>
/// <param name="Name">The list name.</param>
/// <param name="Min">Smallest value.</param>
/// <param name="Q1">First quartile.</param>
/// <param name="Median">Median.</param>
/// <param name="Q3">Third quartile.</param>
/// <param name="Max">Largest value.</param>
/// <param name="LowerWhisker">End of the left whisker.</param>
/// <param name="UpperWhisker">End of the right whisker.</param>
/// <param name="LowerFence">Q1 − 1.5·IQR.</param>
/// <param name="UpperFence">Q3 + 1.5·IQR.</param>
/// <param name="Outliers">Values beyond the fences (modified box plots only), smallest first.</param>
/// <param name="Modified">True when outliers are drawn as points.</param>
public sealed record BoxPlotStats(string Name, double Min, double Q1, double Median, double Q3, double Max,
    double LowerWhisker, double UpperWhisker, double LowerFence, double UpperFence, IReadOnlyList<double> Outliers, bool Modified)
{
    /// <summary>Interquartile range.</summary>
    public double IQR => Q3 - Q1;
}

/// <summary>Box plot geometry: quartiles by the chosen method, whiskers and outliers (TI modified box plot rules).</summary>
public static class BoxPlot
{
    /// <summary>
    /// Computes a box plot. A modified box plot ends each whisker at the most extreme value inside the fences
    /// Q1 − 1.5·IQR and Q3 + 1.5·IQR and returns the values beyond them as outliers; a plain box plot reaches the
    /// minimum and maximum.
    /// </summary>
    public static BoxPlotStats Compute(string name, IReadOnlyList<double> values, IReadOnlyList<double>? frequencies = null,
        QuartileMethod method = QuartileMethod.TI, bool modified = true)
    {
        if (values.Count == 0) throw new StatDataException($"{name} has no numbers");
        var five = Descriptive.FiveNumberSummary(values, method, frequencies);
        double iqr = five.Q3 - five.Q1;
        double lo = five.Q1 - 1.5 * iqr, hi = five.Q3 + 1.5 * iqr;
        var present = Enumerable.Range(0, values.Count).Where(i => frequencies is null || frequencies[i] > 0).Select(i => values[i]).ToList();
        if (!modified)
            return new BoxPlotStats(name, five.Min, five.Q1, five.Median, five.Q3, five.Max, five.Min, five.Max, lo, hi, [], false);
        var inside = present.Where(v => v >= lo && v <= hi).ToList();
        double lw = inside.Count > 0 ? inside.Min() : five.Q1;
        double uw = inside.Count > 0 ? inside.Max() : five.Q3;
        var outliers = present.Where(v => v < lo || v > hi).OrderBy(v => v).ToList();
        return new BoxPlotStats(name, five.Min, five.Q1, five.Median, five.Q3, five.Max, Math.Min(lw, five.Q1), Math.Max(uw, five.Q3), lo, hi, outliers, true);
    }
}

/// <summary>One stack of a dot plot.</summary>
/// <param name="X">The value (rounded to the resolution).</param>
/// <param name="Count">How many dots.</param>
public sealed record DotStack(double X, int Count);

/// <summary>Dot plot stacking: values rounded to a readable resolution, one dot per observation.</summary>
public static class DotPlot
{
    /// <summary>Stacks the values. Whole numbers get one stack per value (when the range is small enough).</summary>
    public static (double Resolution, IReadOnlyList<DotStack> Stacks) Stack(IReadOnlyList<double> values, IReadOnlyList<double>? frequencies = null)
    {
        if (values.Count == 0) throw new StatDataException("There are no values to plot");
        double min = values.Min(), max = values.Max(), span = max - min;
        bool whole = values.All(v => v == Math.Round(v));
        double res = whole && span <= 60 ? 1 : Histogram.Nice(span > 0 ? span / 50 : 1);
        var counts = new SortedDictionary<double, int>();
        for (int i = 0; i < values.Count; i++)
        {
            int f = frequencies is null ? 1 : (int)Math.Round(frequencies[i]);
            if (f <= 0) continue;
            double x = Math.Round(Math.Round(values[i] / res) * res, 12);
            counts[x] = counts.TryGetValue(x, out var c) ? c + f : f;
        }
        return (res, [.. counts.Select(kv => new DotStack(kv.Key, kv.Value))]);
    }
}

/// <summary>Normal probability (normal quantile) plot points.</summary>
public static class NormalProbability
{
    /// <summary>
    /// Sorted data against normal scores z = Φ⁻¹((i − 0.375) / (n + 0.25)) (Blom), with the reference line of a normal
    /// distribution with the sample mean and standard deviation (z = (x − x̄) / Sx).
    /// </summary>
    public static (double[] X, double[] Z, double Mean, double StdDev) Compute(IReadOnlyList<double> values)
    {
        if (values.Count < 2) throw new StatDataException("A normal probability plot needs at least 2 values");
        var x = values.OrderBy(v => v).ToArray();
        int n = x.Length;
        var z = new double[n];
        for (int i = 0; i < n; i++) z[i] = NormalDistribution.Standard.InverseCdf((i + 1 - 0.375) / (n + 0.25));
        double mean = x.Average();
        double sd = Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / (n - 1));
        return (x, z, mean, sd);
    }
}
