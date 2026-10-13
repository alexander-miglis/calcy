using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>How quartiles and percentiles are computed. Calculators and textbooks disagree, so the method is selectable.</summary>
public enum QuartileMethod
{
    /// <summary>
    /// TI-84 and Casio "standard": Q1 and Q3 are the medians of the lower and upper halves, the median itself being
    /// excluded from both halves when n is odd.
    /// </summary>
    TI,

    /// <summary>Tukey's hinges: medians of the halves, the median being included in both halves when n is odd.</summary>
    TukeyHinges,

    /// <summary>Linear interpolation between order statistics at h = (n - 1) p + 1 (Excel QUARTILE.INC, R type 7).</summary>
    Inclusive,

    /// <summary>Linear interpolation at h = (n + 1) p (Excel QUARTILE.EXC, R type 6), clamped to the minimum and maximum.</summary>
    Exclusive,

    /// <summary>
    /// Casio "OnData": the data value whose cumulative relative frequency first reaches p; when it equals p exactly
    /// the mean of that value and the next one is used.
    /// </summary>
    CasioOnData,
}

/// <summary>One-variable statistics (TI-84 1-Var Stats with extras).</summary>
public sealed record OneVarStats
{
    /// <summary>Number of observations (sum of the frequencies).</summary>
    public required double N { get; init; }
    /// <summary>Mean.</summary>
    public required double Mean { get; init; }
    /// <summary>Sum of x.</summary>
    public required double Sum { get; init; }
    /// <summary>Sum of x squared.</summary>
    public required double SumOfSquares { get; init; }
    /// <summary>Sample standard deviation Sx (divisor n - 1). NaN when n &lt;= 1.</summary>
    public required double SampleStdDev { get; init; }
    /// <summary>Population standard deviation sigma x (divisor n).</summary>
    public required double PopulationStdDev { get; init; }
    /// <summary>Sample variance (divisor n - 1). NaN when n &lt;= 1.</summary>
    public required double SampleVariance { get; init; }
    /// <summary>Population variance (divisor n).</summary>
    public required double PopulationVariance { get; init; }
    /// <summary>Minimum.</summary>
    public required double Min { get; init; }
    /// <summary>First quartile under <see cref="Method"/>.</summary>
    public required double Q1 { get; init; }
    /// <summary>Median.</summary>
    public required double Median { get; init; }
    /// <summary>Third quartile under <see cref="Method"/>.</summary>
    public required double Q3 { get; init; }
    /// <summary>Maximum.</summary>
    public required double Max { get; init; }
    /// <summary>Max - Min.</summary>
    public required double Range { get; init; }
    /// <summary>Interquartile range Q3 - Q1.</summary>
    public required double IQR { get; init; }
    /// <summary>All values that share the highest frequency, ascending (every value when all frequencies tie).</summary>
    public required IReadOnlyList<double> Modes { get; init; }
    /// <summary>The frequency of the modes.</summary>
    public required double ModeFrequency { get; init; }
    /// <summary>Adjusted Fisher-Pearson sample skewness G1 (Excel SKEW). NaN when n &lt; 3 or the variance is zero.</summary>
    public required double Skewness { get; init; }
    /// <summary>Sample excess kurtosis G2 (Excel KURT). NaN when n &lt; 4 or the variance is zero.</summary>
    public required double ExcessKurtosis { get; init; }
    /// <summary>Population skewness g1 = m3 / m2^1.5.</summary>
    public required double PopulationSkewness { get; init; }
    /// <summary>Population excess kurtosis g2 = m4 / m2^2 - 3.</summary>
    public required double PopulationExcessKurtosis { get; init; }
    /// <summary>Quartile method used for Q1 and Q3.</summary>
    public required QuartileMethod Method { get; init; }
}

/// <summary>Two-variable statistics (TI-84 2-Var Stats with covariance and correlation).</summary>
public sealed record TwoVarStats
{
    /// <summary>Number of pairs (sum of the frequencies).</summary>
    public required double N { get; init; }
    /// <summary>Mean of x.</summary>
    public required double MeanX { get; init; }
    /// <summary>Sum of x.</summary>
    public required double SumX { get; init; }
    /// <summary>Sum of x squared.</summary>
    public required double SumX2 { get; init; }
    /// <summary>Sample standard deviation of x.</summary>
    public required double Sx { get; init; }
    /// <summary>Population standard deviation of x.</summary>
    public required double SigmaX { get; init; }
    /// <summary>Mean of y.</summary>
    public required double MeanY { get; init; }
    /// <summary>Sum of y.</summary>
    public required double SumY { get; init; }
    /// <summary>Sum of y squared.</summary>
    public required double SumY2 { get; init; }
    /// <summary>Sample standard deviation of y.</summary>
    public required double Sy { get; init; }
    /// <summary>Population standard deviation of y.</summary>
    public required double SigmaY { get; init; }
    /// <summary>Sum of x y.</summary>
    public required double SumXY { get; init; }
    /// <summary>Sample covariance (divisor n - 1).</summary>
    public required double CovarianceSample { get; init; }
    /// <summary>Population covariance (divisor n).</summary>
    public required double CovariancePopulation { get; init; }
    /// <summary>Pearson correlation coefficient r (NaN when either variable is constant).</summary>
    public required double R { get; init; }
    /// <summary>Minimum x.</summary>
    public required double MinX { get; init; }
    /// <summary>Maximum x.</summary>
    public required double MaxX { get; init; }
    /// <summary>Minimum y.</summary>
    public required double MinY { get; init; }
    /// <summary>Maximum y.</summary>
    public required double MaxY { get; init; }
}

/// <summary>Five number summary (box plot).</summary>
public sealed record FiveNumberSummary(double Min, double Q1, double Median, double Q3, double Max);

/// <summary>Outliers by the 1.5 IQR rule (TI-84 modified box plot).</summary>
public sealed record OutlierResult(double Q1, double Q3, double IQR, double LowerFence, double UpperFence, IReadOnlyList<double> Outliers, IReadOnlyList<int> Indices);

/// <summary>Descriptive statistics. Frequencies may be any non-negative real weights.</summary>
public static class Descriptive
{
    /// <summary>
    /// One-variable statistics. Frequencies (optional) are non-negative weights; quartiles of weighted data use the
    /// positions of the expanded data set, which is exact for integer frequencies.
    /// </summary>
    public static OneVarStats OneVar(IReadOnlyList<double> data, IReadOnlyList<double>? frequencies = null, QuartileMethod method = QuartileMethod.TI)
    {
        var s = new WeightedSample(data, frequencies);
        double n = s.Total;
        var sum = new Num.Accumulator();
        var sumSq = new Num.Accumulator();
        for (int i = 0; i < data.Count; i++)
        {
            double w = frequencies is null ? 1.0 : frequencies[i];
            if (w == 0) continue;
            sum.Add(w * data[i]);
            sumSq.Add(w * data[i] * data[i]);
        }
        double mean = sum.Value / n;
        Moments(data, frequencies, mean, n, out double m2, out double m3, out double m4, out double ss);
        double popVar = m2;
        double sampVar = n > 1 ? ss / (n - 1) : double.NaN;
        double g1 = m2 > 0 ? m3 / Math.Pow(m2, 1.5) : double.NaN;
        double g2 = m2 > 0 ? m4 / (m2 * m2) - 3.0 : double.NaN;
        double skew = n > 2 ? g1 * Math.Sqrt(n * (n - 1)) / (n - 2) : double.NaN;
        double kurt = n > 3 ? ((n + 1) * g2 + 6) * (n - 1) / ((n - 2) * (n - 3)) : double.NaN;
        var (q1, med, q3) = s.Quartiles(method);
        var (modes, modeFreq) = s.Modes();
        return new OneVarStats
        {
            N = n,
            Mean = mean,
            Sum = sum.Value,
            SumOfSquares = sumSq.Value,
            SampleStdDev = Math.Sqrt(sampVar),
            PopulationStdDev = Math.Sqrt(popVar),
            SampleVariance = sampVar,
            PopulationVariance = popVar,
            Min = s.Min,
            Q1 = q1,
            Median = med,
            Q3 = q3,
            Max = s.Max,
            Range = s.Max - s.Min,
            IQR = q3 - q1,
            Modes = modes,
            ModeFrequency = modeFreq,
            Skewness = skew,
            ExcessKurtosis = kurt,
            PopulationSkewness = g1,
            PopulationExcessKurtosis = g2,
            Method = method,
        };
    }

    private static void Moments(IReadOnlyList<double> data, IReadOnlyList<double>? freq, double mean, double n,
        out double m2, out double m3, out double m4, out double ss)
    {
        var a1 = new Num.Accumulator();
        var a2 = new Num.Accumulator();
        var a3 = new Num.Accumulator();
        var a4 = new Num.Accumulator();
        for (int i = 0; i < data.Count; i++)
        {
            double w = freq is null ? 1.0 : freq[i];
            if (w == 0) continue;
            double d = data[i] - mean;
            double d2 = d * d;
            a1.Add(w * d);
            a2.Add(w * d2);
            a3.Add(w * d2 * d);
            a4.Add(w * d2 * d2);
        }
        double corr = a1.Value;
        ss = Math.Max(0.0, a2.Value - corr * corr / n);
        m2 = ss / n;
        m3 = a3.Value / n;
        m4 = a4.Value / n;
    }

    /// <summary>
    /// The p-th quantile (0 &lt;= p &lt;= 1). Inclusive, Exclusive and CasioOnData accept any p. TI and TukeyHinges
    /// define only the quartiles, so they accept p in {0, 0.25, 0.5, 0.75, 1}.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> data, double p, QuartileMethod method = QuartileMethod.Inclusive, IReadOnlyList<double>? frequencies = null)
    {
        if (!(p >= 0 && p <= 1)) throw new ArgumentException("the percentile must be given as a fraction between 0 and 1", nameof(p));
        var s = new WeightedSample(data, frequencies);
        return s.Quantile(p, method);
    }

    /// <summary>Median of the data.</summary>
    public static double Median(IReadOnlyList<double> data, IReadOnlyList<double>? frequencies = null)
        => new WeightedSample(data, frequencies).Quantile(0.5, QuartileMethod.TI);

    /// <summary>Mean of the data (compensated summation).</summary>
    public static double Mean(IReadOnlyList<double> data, IReadOnlyList<double>? frequencies = null) => OneVar(data, frequencies).Mean;

    /// <summary>Variance, sample (n - 1) by default or population (n).</summary>
    public static double Variance(IReadOnlyList<double> data, bool sample = true, IReadOnlyList<double>? frequencies = null)
    {
        var st = OneVar(data, frequencies);
        return sample ? st.SampleVariance : st.PopulationVariance;
    }

    /// <summary>Standard deviation, sample (n - 1) by default or population (n).</summary>
    public static double StdDev(IReadOnlyList<double> data, bool sample = true, IReadOnlyList<double>? frequencies = null)
        => Math.Sqrt(Variance(data, sample, frequencies));

    /// <summary>Five number summary (min, Q1, median, Q3, max).</summary>
    public static FiveNumberSummary FiveNumberSummary(IReadOnlyList<double> data, QuartileMethod method = QuartileMethod.TI, IReadOnlyList<double>? frequencies = null)
    {
        var s = new WeightedSample(data, frequencies);
        var (q1, med, q3) = s.Quartiles(method);
        return new FiveNumberSummary(s.Min, q1, med, q3, s.Max);
    }

    /// <summary>Values outside [Q1 - 1.5 IQR, Q3 + 1.5 IQR] together with their indices in the input.</summary>
    public static OutlierResult Outliers(IReadOnlyList<double> data, QuartileMethod method = QuartileMethod.TI, IReadOnlyList<double>? frequencies = null)
    {
        var s = new WeightedSample(data, frequencies);
        var (q1, _, q3) = s.Quartiles(method);
        double iqr = q3 - q1;
        double lo = q1 - 1.5 * iqr, hi = q3 + 1.5 * iqr;
        var values = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i < data.Count; i++)
        {
            if (frequencies is not null && frequencies[i] == 0) continue;
            if (data[i] < lo || data[i] > hi)
            {
                values.Add(data[i]);
                idx.Add(i);
            }
        }
        return new OutlierResult(q1, q3, iqr, lo, hi, values, idx);
    }

    /// <summary>Two-variable statistics for paired data with optional frequencies.</summary>
    public static TwoVarStats TwoVar(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? frequencies = null)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.SameLength(x.Count, y.Count, "x and y");
        CheckFrequencies(frequencies, x.Count);
        if (x.Count == 0) throw new ArgumentException("at least one data pair is required", nameof(x));
        var n = new Num.Accumulator();
        var sx = new Num.Accumulator();
        var sy = new Num.Accumulator();
        var sxx = new Num.Accumulator();
        var syy = new Num.Accumulator();
        var sxy = new Num.Accumulator();
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        for (int i = 0; i < x.Count; i++)
        {
            double w = frequencies is null ? 1.0 : frequencies[i];
            if (w == 0) continue;
            n.Add(w);
            sx.Add(w * x[i]);
            sy.Add(w * y[i]);
            sxx.Add(w * x[i] * x[i]);
            syy.Add(w * y[i] * y[i]);
            sxy.Add(w * x[i] * y[i]);
            minX = Math.Min(minX, x[i]); maxX = Math.Max(maxX, x[i]);
            minY = Math.Min(minY, y[i]); maxY = Math.Max(maxY, y[i]);
        }
        double nn = n.Value;
        double mx = sx.Value / nn, my = sy.Value / nn;
        var dxx = new Num.Accumulator();
        var dyy = new Num.Accumulator();
        var dxy = new Num.Accumulator();
        var dx1 = new Num.Accumulator();
        var dy1 = new Num.Accumulator();
        for (int i = 0; i < x.Count; i++)
        {
            double w = frequencies is null ? 1.0 : frequencies[i];
            if (w == 0) continue;
            double a = x[i] - mx, b = y[i] - my;
            dx1.Add(w * a);
            dy1.Add(w * b);
            dxx.Add(w * a * a);
            dyy.Add(w * b * b);
            dxy.Add(w * a * b);
        }
        double vxx = Math.Max(0, dxx.Value - dx1.Value * dx1.Value / nn);
        double vyy = Math.Max(0, dyy.Value - dy1.Value * dy1.Value / nn);
        double vxy = dxy.Value - dx1.Value * dy1.Value / nn;
        double r = vxx > 0 && vyy > 0 ? Math.Clamp(vxy / Math.Sqrt(vxx * vyy), -1, 1) : double.NaN;
        return new TwoVarStats
        {
            N = nn,
            MeanX = mx,
            SumX = sx.Value,
            SumX2 = sxx.Value,
            Sx = nn > 1 ? Math.Sqrt(vxx / (nn - 1)) : double.NaN,
            SigmaX = Math.Sqrt(vxx / nn),
            MeanY = my,
            SumY = sy.Value,
            SumY2 = syy.Value,
            Sy = nn > 1 ? Math.Sqrt(vyy / (nn - 1)) : double.NaN,
            SigmaY = Math.Sqrt(vyy / nn),
            SumXY = sxy.Value,
            CovarianceSample = nn > 1 ? vxy / (nn - 1) : double.NaN,
            CovariancePopulation = vxy / nn,
            R = r,
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
        };
    }

    /// <summary>Ranks of the data (1 = smallest), ties receiving the average of the ranks they span.</summary>
    public static double[] Ranks(IReadOnlyList<double> data)
    {
        Guard.AllFinite(data, nameof(data));
        return RankHelper.AverageRanks(data, out _);
    }

    internal static void CheckFrequencies(IReadOnlyList<double>? frequencies, int count)
    {
        if (frequencies is null) return;
        Guard.SameLength(count, frequencies.Count, "the data and the frequencies");
        double total = 0;
        for (int i = 0; i < frequencies.Count; i++)
        {
            double f = frequencies[i];
            if (!(f >= 0) || !double.IsFinite(f)) throw new ArgumentException($"frequencies must be non-negative finite numbers (element {i} is {f})", nameof(frequencies));
            total += f;
        }
        if (!(total > 0)) throw new ArgumentException("the frequencies must not all be zero", nameof(frequencies));
    }
}

/// <summary>Sorted distinct values with cumulative weights, giving positions in the (conceptually) expanded data.</summary>
internal sealed class WeightedSample
{
    private readonly double[] _values;
    private readonly double[] _weights;
    private readonly double[] _cum;

    public WeightedSample(IReadOnlyList<double> data, IReadOnlyList<double>? frequencies)
    {
        Guard.AllFinite(data, nameof(data));
        if (data.Count == 0) throw new ArgumentException("the data list must not be empty", nameof(data));
        Descriptive.CheckFrequencies(frequencies, data.Count);
        var pairs = new List<(double V, double W)>(data.Count);
        for (int i = 0; i < data.Count; i++)
        {
            double w = frequencies is null ? 1.0 : frequencies[i];
            if (w > 0) pairs.Add((data[i], w));
        }
        pairs.Sort((p, q) => p.V.CompareTo(q.V));
        var vals = new List<double>();
        var wts = new List<double>();
        foreach (var (v, w) in pairs)
        {
            if (vals.Count > 0 && vals[^1] == v) wts[^1] += w;
            else { vals.Add(v); wts.Add(w); }
        }
        _values = vals.ToArray();
        _weights = wts.ToArray();
        _cum = new double[_values.Length];
        var acc = new Num.Accumulator();
        for (int i = 0; i < _values.Length; i++)
        {
            acc.Add(_weights[i]);
            _cum[i] = acc.Value;
        }
        Total = _cum[^1];
    }

    public double Total { get; }
    public double Min => _values[0];
    public double Max => _values[^1];

    private double Tol => 1e-12 * Math.Max(1.0, Total);

    /// <summary>Value at 1-based position pos of the expanded sorted data.</summary>
    public double ValueAt(double pos)
    {
        if (pos <= 1) return _values[0];
        if (pos >= Total) return _values[^1];
        int lo = 0, hi = _cum.Length - 1;
        double target = pos - Tol;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_cum[mid] >= target) hi = mid; else lo = mid + 1;
        }
        return _values[lo];
    }

    private double MedianOfRange(double start, double count)
    {
        if (count <= 0) return ValueAt(start);
        double m = start + (count - 1) / 2.0;
        double fl = Math.Floor(m + 1e-12);
        if (Math.Abs(m - fl) < 1e-12) return ValueAt(fl);
        return 0.5 * (ValueAt(fl) + ValueAt(fl + 1));
    }

    public (double Q1, double Median, double Q3) Quartiles(QuartileMethod method)
    {
        double n = Total;
        double median = MedianOfRange(1, n);
        switch (method)
        {
            case QuartileMethod.TI:
            {
                double half = Math.Floor(n / 2 + 1e-12);
                if (half < 1) return (median, median, median);
                return (MedianOfRange(1, half), median, MedianOfRange(n - half + 1, half));
            }
            case QuartileMethod.TukeyHinges:
            {
                double half = Math.Ceiling(n / 2 - 1e-12);
                return (MedianOfRange(1, half), median, MedianOfRange(n - half + 1, half));
            }
            default:
                return (Quantile(0.25, method), method == QuartileMethod.CasioOnData ? median : Quantile(0.5, method), Quantile(0.75, method));
        }
    }

    public double Quantile(double p, QuartileMethod method)
    {
        double n = Total;
        if (p == 0) return Min;
        if (p == 1) return Max;
        switch (method)
        {
            case QuartileMethod.TI:
            case QuartileMethod.TukeyHinges:
            {
                var (q1, med, q3) = Quartiles(method);
                if (p == 0.25) return q1;
                if (p == 0.5) return med;
                if (p == 0.75) return q3;
                throw new ArgumentException("the TI and Tukey methods define only the quartiles; use Inclusive, Exclusive or CasioOnData for other percentiles", nameof(p));
            }
            case QuartileMethod.Inclusive:
                return Interpolate((n - 1) * p + 1);
            case QuartileMethod.Exclusive:
            {
                double h = (n + 1) * p;
                if (h <= 1) return Min;
                if (h >= n) return Max;
                return Interpolate(h);
            }
            case QuartileMethod.CasioOnData:
            {
                double target = p * n;
                int i = 0;
                while (i < _cum.Length - 1 && _cum[i] < target - Tol) i++;
                if (Math.Abs(_cum[i] - target) <= Tol && i < _values.Length - 1) return 0.5 * (_values[i] + _values[i + 1]);
                return _values[i];
            }
            default:
                throw new ArgumentException("unknown quartile method", nameof(method));
        }
    }

    private double Interpolate(double h)
    {
        double lo = Math.Floor(h);
        double frac = h - lo;
        double a = ValueAt(lo);
        if (frac == 0 || lo >= Total) return a;
        double b = ValueAt(lo + 1);
        return a + frac * (b - a);
    }

    public (double[] Modes, double Frequency) Modes()
    {
        double best = 0;
        foreach (var w in _weights) best = Math.Max(best, w);
        var modes = new List<double>();
        for (int i = 0; i < _values.Length; i++)
        {
            if (Math.Abs(_weights[i] - best) <= 1e-12 * best) modes.Add(_values[i]);
        }
        return (modes.ToArray(), best);
    }
}

/// <summary>Ranking helpers for the nonparametric tests.</summary>
internal static class RankHelper
{
    /// <summary>Average ranks (1-based) and the tie correction terms sum(t^3 - t) over tie groups.</summary>
    public static double[] AverageRanks(IReadOnlyList<double> data, out double tieSum)
    {
        int n = data.Count;
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        Array.Sort(idx, (a, b) => data[a].CompareTo(data[b]));
        var ranks = new double[n];
        tieSum = 0;
        int k = 0;
        while (k < n)
        {
            int j = k;
            while (j + 1 < n && data[idx[j + 1]] == data[idx[k]]) j++;
            double avg = 0.5 * (k + j) + 1;
            for (int m = k; m <= j; m++) ranks[idx[m]] = avg;
            double t = j - k + 1;
            if (t > 1) tieSum += t * t * t - t;
            k = j + 1;
        }
        return ranks;
    }
}
