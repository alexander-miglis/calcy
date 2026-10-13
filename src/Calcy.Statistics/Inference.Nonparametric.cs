using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

public static partial class Inference
{
    private static double DoubledTwoSided(double lower, double upper) => Math.Min(1.0, 2.0 * Math.Min(lower, upper));

    /// <summary>
    /// Sign test for the median, H0: median = median0. Values equal to median0 are discarded; the statistic S is the
    /// number of values above median0 and the p-value is exact (binomial, two-sided by doubling the smaller tail).
    /// </summary>
    public static TestResult SignTest(IReadOnlyList<double> data, double median0 = 0, Alternative alternative = Alternative.NotEqual)
    {
        Guard.AllFinite(data, nameof(data));
        Guard.Finite(median0, "the hypothesised median", nameof(median0));
        CheckAlternative(alternative);
        int pos = 0, neg = 0, ties = 0;
        foreach (var v in data)
        {
            if (v > median0) pos++;
            else if (v < median0) neg++;
            else ties++;
        }
        int n = pos + neg;
        if (n == 0) throw new ArgumentException("every value equals the hypothesised median, so the sign test is undefined", nameof(data));
        var bin = new BinomialDistribution(n, 0.5);
        double lower = bin.Cdf(pos), upper = bin.Survival(pos - 1);
        double p = alternative switch
        {
            Alternative.Greater => upper,
            Alternative.Less => lower,
            _ => DoubledTwoSided(lower, upper),
        };
        return new TestResult
        {
            TestName = "Sign test",
            StatisticName = "S",
            Statistic = pos,
            PValue = p,
            Alternative = alternative,
            Values = V(("n", n), ("positive", pos), ("negative", neg), ("ties", ties)),
        };
    }

    /// <summary>Paired sign test on the differences x - y.</summary>
    public static TestResult SignTest(IReadOnlyList<double> x, IReadOnlyList<double> y, Alternative alternative = Alternative.NotEqual)
        => SignTest(Differences(x, y), 0, alternative) with { TestName = "Paired sign test" };

    private static double[] Differences(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.SameLength(x.Count, y.Count, "the paired samples");
        var d = new double[x.Count];
        for (int i = 0; i < d.Length; i++) d[i] = x[i] - y[i];
        return d;
    }

    /// <summary>
    /// Wilcoxon signed rank test, H0: the distribution of data - mu0 is symmetric about 0. Zero differences are
    /// dropped and tied absolute values get average ranks. The statistic W is the sum of the positive ranks. By default
    /// the p-value uses the normal approximation with tie and continuity corrections; exact = true computes the exact
    /// null distribution (no ties allowed, at most 1000 non-zero differences).
    /// </summary>
    public static TestResult WilcoxonSignedRank(IReadOnlyList<double> data, double mu0 = 0, Alternative alternative = Alternative.NotEqual, bool exact = false, bool continuityCorrection = true)
    {
        Guard.AllFinite(data, nameof(data));
        Guard.Finite(mu0, "the hypothesised centre", nameof(mu0));
        CheckAlternative(alternative);
        var d = new List<double>();
        foreach (var v in data) if (v - mu0 != 0) d.Add(v - mu0);
        int n = d.Count;
        if (n == 0) throw new ArgumentException("every difference is zero, so the signed rank test is undefined", nameof(data));
        var abs = d.Select(Math.Abs).ToArray();
        var ranks = RankHelper.AverageRanks(abs, out double tieSum);
        double w = 0;
        for (int i = 0; i < n; i++) if (d[i] > 0) w += ranks[i];
        double mean = n * (n + 1) / 4.0;
        double var = n * (n + 1) * (2.0 * n + 1) / 24.0 - tieSum / 48.0;
        double p, z = double.NaN;
        if (exact)
        {
            if (tieSum > 0) throw new ArgumentException("the exact signed rank distribution requires data without tied absolute differences", nameof(data));
            if (n > 1000) throw new ArgumentException("the exact signed rank distribution is limited to 1000 non-zero differences", nameof(data));
            var dist = SignedRankDistribution(n);
            int wi = (int)Math.Round(w);
            double lower = 0, upper = 0;
            for (int s = 0; s < dist.Length; s++)
            {
                if (s <= wi) lower += dist[s];
                if (s >= wi) upper += dist[s];
            }
            p = alternative switch
            {
                Alternative.Greater => Math.Min(1, upper),
                Alternative.Less => Math.Min(1, lower),
                _ => DoubledTwoSided(lower, upper),
            };
        }
        else
        {
            if (!(var > 0)) throw new ArgumentException("the variance of the signed rank statistic is zero");
            double diff = w - mean;
            double cc = !continuityCorrection ? 0 : alternative switch
            {
                Alternative.Greater => 0.5,
                Alternative.Less => -0.5,
                _ => Math.Sign(diff) * 0.5,
            };
            z = (diff - cc) / Math.Sqrt(var);
            p = NormalP(z, alternative);
        }
        var values = V(("n", n), ("mean", mean), ("variance", var));
        if (!double.IsNaN(z)) values["z"] = z;
        return new TestResult
        {
            TestName = "Wilcoxon signed rank test",
            StatisticName = "W",
            Statistic = w,
            PValue = p,
            Alternative = alternative,
            Values = values,
        };
    }

    /// <summary>Paired Wilcoxon signed rank test on the differences x - y.</summary>
    public static TestResult WilcoxonSignedRank(IReadOnlyList<double> x, IReadOnlyList<double> y, Alternative alternative = Alternative.NotEqual, bool exact = false, bool continuityCorrection = true)
        => WilcoxonSignedRank(Differences(x, y), 0, alternative, exact, continuityCorrection);

    /// <summary>P(W+ = s), s = 0 .. n(n+1)/2, for the signed rank statistic without ties.</summary>
    private static double[] SignedRankDistribution(int n)
    {
        int max = n * (n + 1) / 2;
        var dist = new double[max + 1];
        dist[0] = 1;
        int top = 0;
        for (int k = 1; k <= n; k++)
        {
            top += k;
            for (int s = top; s >= 0; s--) dist[s] = 0.5 * (dist[s] + (s >= k ? dist[s - k] : 0));
        }
        return dist;
    }

    /// <summary>
    /// Mann-Whitney U (Wilcoxon rank sum) test comparing two independent samples. U is the number of pairs with
    /// x &gt; y (ties count one half), so Greater means x tends to be larger. Normal approximation with tie and
    /// continuity corrections by default; exact = true uses the exact null distribution (no ties, n1 * n2 &lt;= 10000).
    /// </summary>
    public static TestResult MannWhitneyU(IReadOnlyList<double> x, IReadOnlyList<double> y, Alternative alternative = Alternative.NotEqual, bool exact = false, bool continuityCorrection = true)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        CheckAlternative(alternative);
        int n1 = x.Count, n2 = y.Count;
        if (n1 == 0 || n2 == 0) throw new ArgumentException("both samples must contain at least one value");
        var all = new double[n1 + n2];
        for (int i = 0; i < n1; i++) all[i] = x[i];
        for (int i = 0; i < n2; i++) all[n1 + i] = y[i];
        var ranks = RankHelper.AverageRanks(all, out double tieSum);
        double r1 = 0;
        for (int i = 0; i < n1; i++) r1 += ranks[i];
        double u = r1 - n1 * (n1 + 1) / 2.0;
        double n = n1 + n2;
        double mean = n1 * (double)n2 / 2.0;
        double var = n1 * (double)n2 / 12.0 * ((n + 1) - tieSum / (n * (n - 1)));
        double p, z = double.NaN;
        if (exact)
        {
            if (tieSum > 0) throw new ArgumentException("the exact Mann-Whitney distribution requires data without ties");
            if ((long)n1 * n2 > 10000) throw new ArgumentException("the exact Mann-Whitney distribution is limited to n1 * n2 <= 10000");
            var dist = MannWhitneyDistribution(n1, n2);
            int ui = (int)Math.Round(u);
            double lower = 0, upper = 0;
            for (int s = 0; s < dist.Length; s++)
            {
                if (s <= ui) lower += dist[s];
                if (s >= ui) upper += dist[s];
            }
            p = alternative switch
            {
                Alternative.Greater => Math.Min(1, upper),
                Alternative.Less => Math.Min(1, lower),
                _ => DoubledTwoSided(lower, upper),
            };
        }
        else
        {
            if (!(var > 0)) throw new ArgumentException("the variance of U is zero (all values are tied)");
            double diff = u - mean;
            double cc = !continuityCorrection ? 0 : alternative switch
            {
                Alternative.Greater => 0.5,
                Alternative.Less => -0.5,
                _ => Math.Sign(diff) * 0.5,
            };
            z = (diff - cc) / Math.Sqrt(var);
            p = NormalP(z, alternative);
        }
        var values = V(("n1", n1), ("n2", n2), ("R1", r1), ("U1", u), ("U2", n1 * (double)n2 - u));
        if (!double.IsNaN(z)) values["z"] = z;
        return new TestResult
        {
            TestName = "Mann-Whitney U test",
            StatisticName = "U",
            Statistic = u,
            PValue = p,
            Alternative = alternative,
            Values = values,
        };
    }

    /// <summary>P(U = u), u = 0 .. n1 n2, for the Mann-Whitney statistic without ties.</summary>
    private static double[] MannWhitneyDistribution(int n1, int n2)
    {
        // f(i, j, u) = i/(i+j) f(i-1, j, u-j) + j/(i+j) f(i, j-1, u); probabilities keep every value bounded.
        var prev = new double[n2 + 1][];
        for (int j = 0; j <= n2; j++) prev[j] = [1.0];
        for (int i = 1; i <= n1; i++)
        {
            var cur = new double[n2 + 1][];
            cur[0] = [1.0];
            for (int j = 1; j <= n2; j++)
            {
                var arr = new double[i * j + 1];
                double wi = i / (double)(i + j), wj = j / (double)(i + j);
                var a = prev[j];
                for (int u = 0; u < a.Length; u++) arr[u + j] += wi * a[u];
                var b = cur[j - 1];
                for (int u = 0; u < b.Length; u++) arr[u] += wj * b[u];
                cur[j] = arr;
            }
            prev = cur;
        }
        return prev[n2];
    }

    /// <summary>Kruskal-Wallis H test for k independent samples (tie corrected), chi-square with k - 1 df.</summary>
    public static TestResult KruskalWallis(params IReadOnlyList<double>[] groups)
    {
        Guard.NotNull(groups, nameof(groups));
        int k = groups.Length;
        if (k < 2) throw new ArgumentException("at least two groups are required", nameof(groups));
        var all = new List<double>();
        foreach (var g in groups)
        {
            Guard.AllFinite(g, nameof(groups));
            if (g.Count == 0) throw new ArgumentException("every group must contain at least one value", nameof(groups));
            all.AddRange(g);
        }
        double n = all.Count;
        var ranks = RankHelper.AverageRanks(all, out double tieSum);
        double sum = 0;
        int offset = 0;
        foreach (var g in groups)
        {
            double r = 0;
            for (int i = 0; i < g.Count; i++) r += ranks[offset + i];
            sum += r * r / g.Count;
            offset += g.Count;
        }
        double h = 12.0 / (n * (n + 1)) * sum - 3 * (n + 1);
        double c = 1 - tieSum / (n * n * n - n);
        if (!(c > 0)) throw new ArgumentException("all values are tied, so the Kruskal-Wallis statistic is undefined");
        h /= c;
        double df = k - 1;
        return new TestResult
        {
            TestName = "Kruskal-Wallis test",
            StatisticName = "H",
            Statistic = h,
            PValue = new ChiSquaredDistribution(df).Survival(h),
            DegreesOfFreedom = df,
            Alternative = Alternative.Greater,
            Values = V(("n", n), ("tieCorrection", c)),
        };
    }

    /// <summary>
    /// Spearman rank correlation rho (Pearson correlation of the average ranks). The p-value uses the t approximation
    /// t = rho sqrt((n - 2) / (1 - rho^2)) with n - 2 degrees of freedom.
    /// </summary>
    public static TestResult SpearmanCorrelation(IReadOnlyList<double> x, IReadOnlyList<double> y, Alternative alternative = Alternative.NotEqual)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.SameLength(x.Count, y.Count, "x and y");
        CheckAlternative(alternative);
        int n = x.Count;
        if (n < 3) throw new ArgumentException("at least 3 pairs are required", nameof(x));
        var rx = RankHelper.AverageRanks(x, out _);
        var ry = RankHelper.AverageRanks(y, out _);
        double rho = Descriptive.TwoVar(rx, ry).R;
        if (double.IsNaN(rho)) throw new ArgumentException("one of the variables is constant, so the rank correlation is undefined");
        double df = n - 2;
        double t = Math.Abs(rho) >= 1 ? Math.CopySign(double.PositiveInfinity, rho) : rho * Math.Sqrt(df / (1 - rho * rho));
        return new TestResult
        {
            TestName = "Spearman rank correlation",
            StatisticName = "rho",
            Statistic = rho,
            PValue = TP(t, df, alternative),
            DegreesOfFreedom = df,
            Alternative = alternative,
            Values = V(("n", n), ("t", t)),
        };
    }

    /// <summary>
    /// Kendall rank correlation tau-b with the tie corrected normal approximation for S = concordant - discordant.
    /// exact = true uses the exact permutation distribution (no ties, n &lt;= 100).
    /// </summary>
    public static TestResult KendallTau(IReadOnlyList<double> x, IReadOnlyList<double> y, Alternative alternative = Alternative.NotEqual, bool exact = false)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.SameLength(x.Count, y.Count, "x and y");
        CheckAlternative(alternative);
        int n = x.Count;
        if (n < 2) throw new ArgumentException("at least 2 pairs are required", nameof(x));
        double s = 0;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                s += Math.Sign(x[i] - x[j]) * Math.Sign(y[i] - y[j]);
        static (double T1, double V1, double W1) TieTerms(IReadOnlyList<double> v)
        {
            double t1 = 0, v1 = 0, w1 = 0;
            foreach (var grp in v.GroupBy(a => a))
            {
                double t = grp.Count();
                if (t > 1)
                {
                    t1 += t * (t - 1) / 2;
                    v1 += t * (t - 1) * (2 * t + 5);
                    w1 += t * (t - 1) * (t - 2);
                }
            }
            return (t1, v1, w1);
        }
        var (tx, vx, wx) = TieTerms(x);
        var (ty, vy, wy) = TieTerms(y);
        double n0 = n * (n - 1) / 2.0;
        double denom = Math.Sqrt((n0 - tx) * (n0 - ty));
        if (!(denom > 0)) throw new ArgumentException("one of the variables is constant, so Kendall's tau is undefined");
        double tau = s / denom;
        double p, z = double.NaN;
        if (exact)
        {
            if (tx > 0 || ty > 0) throw new ArgumentException("the exact Kendall distribution requires data without ties");
            if (n > 100) throw new ArgumentException("the exact Kendall distribution is limited to n <= 100");
            var dist = InversionDistribution(n);
            int inv = (int)Math.Round((n0 - s) / 2);
            double lower = 0, upper = 0;
            // S = n0 - 2 inv; S >= s means inv <= observed inversions.
            for (int k = 0; k < dist.Length; k++)
            {
                if (k <= inv) upper += dist[k];
                if (k >= inv) lower += dist[k];
            }
            p = alternative switch
            {
                Alternative.Greater => Math.Min(1, upper),
                Alternative.Less => Math.Min(1, lower),
                _ => DoubledTwoSided(lower, upper),
            };
        }
        else
        {
            double v0 = n * (n - 1.0) * (2.0 * n + 5);
            double var = (v0 - vx - vy) / 18.0
                + (2 * tx) * (2 * ty) / (2.0 * n * (n - 1))
                + (n > 2 ? wx * wy / (9.0 * n * (n - 1) * (n - 2)) : 0);
            z = s / Math.Sqrt(var);
            p = NormalP(z, alternative);
        }
        var values = V(("n", n), ("S", s));
        if (!double.IsNaN(z)) values["z"] = z;
        return new TestResult
        {
            TestName = "Kendall rank correlation",
            StatisticName = "tau",
            Statistic = tau,
            PValue = p,
            Alternative = alternative,
            Values = values,
        };
    }

    /// <summary>Probability that a random permutation of n items has k inversions (Mahonian numbers / n!).</summary>
    private static double[] InversionDistribution(int n)
    {
        var dist = new double[] { 1.0 };
        for (int m = 2; m <= n; m++)
        {
            int max = m * (m - 1) / 2;
            var next = new double[max + 1];
            // next[k] = (1/m) sum_{j=0}^{m-1} dist[k - j], via a running window sum.
            double window = 0;
            for (int k = 0; k <= max; k++)
            {
                if (k < dist.Length) window += dist[k];
                if (k - m >= 0 && k - m < dist.Length) window -= dist[k - m];
                next[k] = window / m;
            }
            dist = next;
        }
        return dist;
    }
}
