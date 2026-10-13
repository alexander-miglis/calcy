using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>
/// Hypothesis tests and confidence intervals with TI-84 semantics (STAT TESTS menu), plus nonparametric tests.
/// Every test returns a <see cref="TestResult"/>; extra outputs use the TI names (xbar, Sx, n, phat, Sxp, ...).
/// </summary>
public static partial class Inference
{
    internal static double NormalP(double z, Alternative alt) => alt switch
    {
        Alternative.Less => StdNormal.Cdf(z),
        Alternative.Greater => StdNormal.Cdf(-z),
        _ => Math.Min(1.0, 2.0 * StdNormal.Cdf(-Math.Abs(z))),
    };

    internal static double TP(double t, double df, Alternative alt)
    {
        var d = new StudentTDistribution(df);
        return alt switch
        {
            Alternative.Less => d.Cdf(t),
            Alternative.Greater => d.Survival(t),
            _ => Math.Min(1.0, 2.0 * d.Survival(Math.Abs(t))),
        };
    }

    private static void CheckAlternative(Alternative alt)
    {
        if (alt is not (Alternative.NotEqual or Alternative.Less or Alternative.Greater))
            throw new ArgumentException("unknown alternative hypothesis", nameof(alt));
    }

    private static Dictionary<string, double> V(params (string Key, double Value)[] items)
    {
        var d = new Dictionary<string, double>();
        foreach (var (k, v) in items) d[k] = v;
        return d;
    }

    // ------------------------------------------------------------------ z and t tests

    /// <summary>1-sample z test (TI Z-Test, Stats input).</summary>
    public static TestResult ZTest(double mu0, double sigma, double mean, double n, Alternative alternative = Alternative.NotEqual)
    {
        Guard.Finite(mu0, "the hypothesised mean", nameof(mu0));
        Guard.PositiveFinite(sigma, "the population standard deviation", nameof(sigma));
        Guard.Finite(mean, "the sample mean", nameof(mean));
        Guard.PositiveFinite(n, "the sample size", nameof(n));
        CheckAlternative(alternative);
        double z = (mean - mu0) / (sigma / Math.Sqrt(n));
        return new TestResult
        {
            TestName = "1-Sample z test",
            StatisticName = "z",
            Statistic = z,
            PValue = NormalP(z, alternative),
            Alternative = alternative,
            Values = V(("xbar", mean), ("n", n), ("sigma", sigma), ("mu0", mu0)),
        };
    }

    /// <summary>1-sample z test from data (TI Z-Test, Data input).</summary>
    public static TestResult ZTest(double mu0, double sigma, IReadOnlyList<double> data, Alternative alternative = Alternative.NotEqual, IReadOnlyList<double>? frequencies = null)
    {
        var st = Descriptive.OneVar(data, frequencies);
        var r = ZTest(mu0, sigma, st.Mean, st.N, alternative);
        var v = new Dictionary<string, double>(r.Values) { ["Sx"] = st.SampleStdDev };
        return r with { Values = v };
    }

    /// <summary>1-sample t test (TI T-Test, Stats input).</summary>
    public static TestResult TTest(double mu0, double mean, double sx, double n, Alternative alternative = Alternative.NotEqual)
    {
        Guard.Finite(mu0, "the hypothesised mean", nameof(mu0));
        Guard.Finite(mean, "the sample mean", nameof(mean));
        Guard.PositiveFinite(sx, "the sample standard deviation", nameof(sx));
        if (!(n > 1) || !double.IsFinite(n)) throw new ArgumentException("the sample size must be greater than 1", nameof(n));
        CheckAlternative(alternative);
        double t = (mean - mu0) / (sx / Math.Sqrt(n));
        double df = n - 1;
        return new TestResult
        {
            TestName = "1-Sample t test",
            StatisticName = "t",
            Statistic = t,
            PValue = TP(t, df, alternative),
            DegreesOfFreedom = df,
            Alternative = alternative,
            Values = V(("xbar", mean), ("Sx", sx), ("n", n), ("mu0", mu0)),
        };
    }

    /// <summary>1-sample t test from data (TI T-Test, Data input).</summary>
    public static TestResult TTest(double mu0, IReadOnlyList<double> data, Alternative alternative = Alternative.NotEqual, IReadOnlyList<double>? frequencies = null)
    {
        var st = Descriptive.OneVar(data, frequencies);
        return TTest(mu0, st.Mean, st.SampleStdDev, st.N, alternative);
    }

    /// <summary>2-sample z test (TI 2-SampZTest, Stats input), H0: mu1 = mu2.</summary>
    public static TestResult TwoSampleZTest(double sigma1, double sigma2, double mean1, double n1, double mean2, double n2, Alternative alternative = Alternative.NotEqual)
    {
        Guard.PositiveFinite(sigma1, "sigma1", nameof(sigma1));
        Guard.PositiveFinite(sigma2, "sigma2", nameof(sigma2));
        Guard.Finite(mean1, "the first sample mean", nameof(mean1));
        Guard.Finite(mean2, "the second sample mean", nameof(mean2));
        Guard.PositiveFinite(n1, "the first sample size", nameof(n1));
        Guard.PositiveFinite(n2, "the second sample size", nameof(n2));
        CheckAlternative(alternative);
        double z = (mean1 - mean2) / Math.Sqrt(sigma1 * sigma1 / n1 + sigma2 * sigma2 / n2);
        return new TestResult
        {
            TestName = "2-Sample z test",
            StatisticName = "z",
            Statistic = z,
            PValue = NormalP(z, alternative),
            Alternative = alternative,
            Values = V(("xbar1", mean1), ("xbar2", mean2), ("n1", n1), ("n2", n2)),
        };
    }

    /// <summary>2-sample z test from data (TI 2-SampZTest, Data input).</summary>
    public static TestResult TwoSampleZTest(double sigma1, double sigma2, IReadOnlyList<double> data1, IReadOnlyList<double> data2, Alternative alternative = Alternative.NotEqual)
    {
        var a = Descriptive.OneVar(data1);
        var b = Descriptive.OneVar(data2);
        var r = TwoSampleZTest(sigma1, sigma2, a.Mean, a.N, b.Mean, b.N, alternative);
        var v = new Dictionary<string, double>(r.Values) { ["Sx1"] = a.SampleStdDev, ["Sx2"] = b.SampleStdDev };
        return r with { Values = v };
    }

    internal static (double Se, double Df, double Sp) TwoSampleSe(double sx1, double n1, double sx2, double n2, bool pooled)
    {
        double v1 = sx1 * sx1, v2 = sx2 * sx2;
        if (pooled)
        {
            double df = n1 + n2 - 2;
            double sp = Math.Sqrt(((n1 - 1) * v1 + (n2 - 1) * v2) / df);
            return (sp * Math.Sqrt(1 / n1 + 1 / n2), df, sp);
        }
        double a = v1 / n1, b = v2 / n2;
        double se2 = a + b;
        double dfw = se2 * se2 / (a * a / (n1 - 1) + b * b / (n2 - 1));
        return (Math.Sqrt(se2), dfw, double.NaN);
    }

    private static void CheckTwoSampleT(double sx1, double n1, double sx2, double n2)
    {
        if (!(sx1 >= 0) || !(sx2 >= 0) || !double.IsFinite(sx1) || !double.IsFinite(sx2)) throw new ArgumentException("the sample standard deviations must be non-negative finite numbers");
        if (sx1 == 0 && sx2 == 0) throw new ArgumentException("the sample standard deviations must not both be zero");
        if (!(n1 > 1) || !(n2 > 1)) throw new ArgumentException("each sample size must be greater than 1");
    }

    /// <summary>2-sample t test (TI 2-SampTTest, Stats input). Pooled uses the pooled variance, otherwise Welch's df.</summary>
    public static TestResult TwoSampleTTest(double mean1, double sx1, double n1, double mean2, double sx2, double n2, Alternative alternative = Alternative.NotEqual, bool pooled = false)
    {
        Guard.Finite(mean1, "the first sample mean", nameof(mean1));
        Guard.Finite(mean2, "the second sample mean", nameof(mean2));
        CheckTwoSampleT(sx1, n1, sx2, n2);
        CheckAlternative(alternative);
        var (se, df, sp) = TwoSampleSe(sx1, n1, sx2, n2, pooled);
        double t = (mean1 - mean2) / se;
        var v = V(("xbar1", mean1), ("xbar2", mean2), ("Sx1", sx1), ("Sx2", sx2), ("n1", n1), ("n2", n2));
        if (pooled) v["Sxp"] = sp;
        return new TestResult
        {
            TestName = pooled ? "2-Sample t test (pooled)" : "2-Sample t test (Welch)",
            StatisticName = "t",
            Statistic = t,
            PValue = TP(t, df, alternative),
            DegreesOfFreedom = df,
            Alternative = alternative,
            Values = v,
        };
    }

    /// <summary>2-sample t test from data (TI 2-SampTTest, Data input).</summary>
    public static TestResult TwoSampleTTest(IReadOnlyList<double> data1, IReadOnlyList<double> data2, Alternative alternative = Alternative.NotEqual, bool pooled = false)
    {
        var a = Descriptive.OneVar(data1);
        var b = Descriptive.OneVar(data2);
        return TwoSampleTTest(a.Mean, a.SampleStdDev, a.N, b.Mean, b.SampleStdDev, b.N, alternative, pooled);
    }

    /// <summary>Paired t test on the differences x1 - x2, H0: mean difference = mu0.</summary>
    public static TestResult PairedTTest(IReadOnlyList<double> x1, IReadOnlyList<double> x2, double mu0 = 0, Alternative alternative = Alternative.NotEqual)
    {
        Guard.AllFinite(x1, nameof(x1));
        Guard.AllFinite(x2, nameof(x2));
        Guard.SameLength(x1.Count, x2.Count, "the paired samples");
        var d = new double[x1.Count];
        for (int i = 0; i < d.Length; i++) d[i] = x1[i] - x2[i];
        var r = TTest(mu0, d, alternative);
        var v = new Dictionary<string, double>(r.Values);
        v["dbar"] = v["xbar"];
        v["Sd"] = v["Sx"];
        return r with { TestName = "Paired t test", Values = v };
    }

    // ------------------------------------------------------------------ proportions

    private static void CheckCount(double x, double n, string xName, string nName)
    {
        Guard.PositiveInteger(n, "the number of trials", nName);
        Guard.NonNegativeInteger(x, "the number of successes", xName);
        if (x > n) throw new ArgumentException("the number of successes cannot exceed the number of trials", xName);
    }

    /// <summary>1-proportion z test (TI 1-PropZTest), H0: p = p0.</summary>
    public static TestResult OnePropZTest(double p0, double x, double n, Alternative alternative = Alternative.NotEqual)
    {
        if (!(p0 > 0 && p0 < 1)) throw new ArgumentException("the hypothesised proportion must be between 0 and 1", nameof(p0));
        CheckCount(x, n, nameof(x), nameof(n));
        CheckAlternative(alternative);
        double phat = x / n;
        double z = (phat - p0) / Math.Sqrt(p0 * (1 - p0) / n);
        return new TestResult
        {
            TestName = "1-Proportion z test",
            StatisticName = "z",
            Statistic = z,
            PValue = NormalP(z, alternative),
            Alternative = alternative,
            Values = V(("phat", phat), ("n", n), ("x", x), ("p0", p0)),
        };
    }

    /// <summary>2-proportion z test with the pooled proportion (TI 2-PropZTest), H0: p1 = p2.</summary>
    public static TestResult TwoPropZTest(double x1, double n1, double x2, double n2, Alternative alternative = Alternative.NotEqual)
    {
        CheckCount(x1, n1, nameof(x1), nameof(n1));
        CheckCount(x2, n2, nameof(x2), nameof(n2));
        CheckAlternative(alternative);
        double p1 = x1 / n1, p2 = x2 / n2, p = (x1 + x2) / (n1 + n2);
        double se = Math.Sqrt(p * (1 - p) * (1 / n1 + 1 / n2));
        if (!(se > 0)) throw new ArgumentException("the pooled proportion is 0 or 1, so the test statistic is undefined");
        double z = (p1 - p2) / se;
        return new TestResult
        {
            TestName = "2-Proportion z test",
            StatisticName = "z",
            Statistic = z,
            PValue = NormalP(z, alternative),
            Alternative = alternative,
            Values = V(("p1hat", p1), ("p2hat", p2), ("phat", p), ("n1", n1), ("n2", n2)),
        };
    }

    // ------------------------------------------------------------------ chi-square

    /// <summary>Chi-square goodness of fit (TI chi2GOF-Test). Expected values are counts; df defaults to k - 1.</summary>
    public static ChiSquareGoodnessOfFitResult ChiSquareGoodnessOfFit(IReadOnlyList<double> observed, IReadOnlyList<double> expected, double? degreesOfFreedom = null)
    {
        Guard.AllFinite(observed, nameof(observed));
        Guard.AllFinite(expected, nameof(expected));
        Guard.SameLength(observed.Count, expected.Count, "the observed and expected lists");
        int k = observed.Count;
        if (k < 2) throw new ArgumentException("at least two categories are required", nameof(observed));
        double df = degreesOfFreedom ?? k - 1;
        Guard.PositiveFinite(df, "the degrees of freedom", nameof(degreesOfFreedom));
        var contrib = new double[k];
        var chi = new Num.Accumulator();
        for (int i = 0; i < k; i++)
        {
            if (!(observed[i] >= 0)) throw new ArgumentException("observed counts must be non-negative", nameof(observed));
            if (!(expected[i] > 0)) throw new ArgumentException("expected counts must be positive", nameof(expected));
            double d = observed[i] - expected[i];
            contrib[i] = d * d / expected[i];
            chi.Add(contrib[i]);
        }
        double x2 = chi.Value;
        return new ChiSquareGoodnessOfFitResult
        {
            TestName = "Chi-square goodness of fit",
            StatisticName = "chi2",
            Statistic = x2,
            PValue = new ChiSquaredDistribution(df).Survival(x2),
            DegreesOfFreedom = df,
            Alternative = Alternative.Greater,
            Contributions = contrib,
        };
    }

    /// <summary>Chi-square test of independence on an r x c table of observed counts (TI chi2-Test).</summary>
    public static ChiSquareTwoWayResult ChiSquareTwoWay(double[,] observed)
    {
        Guard.NotNull(observed, nameof(observed));
        int r = observed.GetLength(0), c = observed.GetLength(1);
        if (r < 2 || c < 2) throw new ArgumentException("the table must have at least 2 rows and 2 columns", nameof(observed));
        var rows = new double[r];
        var cols = new double[c];
        double total = 0;
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
            {
                double o = observed[i, j];
                if (!(o >= 0) || !double.IsFinite(o)) throw new ArgumentException("observed counts must be non-negative finite numbers", nameof(observed));
                rows[i] += o;
                cols[j] += o;
                total += o;
            }
        foreach (var s in rows) if (!(s > 0)) throw new ArgumentException("every row must have a positive total", nameof(observed));
        foreach (var s in cols) if (!(s > 0)) throw new ArgumentException("every column must have a positive total", nameof(observed));
        var expected = new double[r, c];
        var contrib = new double[r, c];
        var chi = new Num.Accumulator();
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
            {
                double e = rows[i] * cols[j] / total;
                expected[i, j] = e;
                double d = observed[i, j] - e;
                contrib[i, j] = d * d / e;
                chi.Add(contrib[i, j]);
            }
        double df = (r - 1) * (c - 1);
        return new ChiSquareTwoWayResult
        {
            TestName = "Chi-square test of independence",
            StatisticName = "chi2",
            Statistic = chi.Value,
            PValue = new ChiSquaredDistribution(df).Survival(chi.Value),
            DegreesOfFreedom = df,
            Alternative = Alternative.Greater,
            Expected = expected,
            Contributions = contrib,
        };
    }

    // ------------------------------------------------------------------ F test

    /// <summary>2-sample F test for equal variances (TI 2-SampFTest, Stats input), F = Sx1^2 / Sx2^2.</summary>
    public static TestResult TwoSampleFTest(double sx1, double n1, double sx2, double n2, Alternative alternative = Alternative.NotEqual)
    {
        Guard.PositiveFinite(sx1, "the first sample standard deviation", nameof(sx1));
        Guard.PositiveFinite(sx2, "the second sample standard deviation", nameof(sx2));
        if (!(n1 > 1) || !(n2 > 1)) throw new ArgumentException("each sample size must be greater than 1");
        CheckAlternative(alternative);
        double f = sx1 * sx1 / (sx2 * sx2);
        var d = new FDistribution(n1 - 1, n2 - 1);
        double p = alternative switch
        {
            Alternative.Less => d.Cdf(f),
            Alternative.Greater => d.Survival(f),
            _ => Math.Min(1.0, 2 * Math.Min(d.Cdf(f), d.Survival(f))),
        };
        return new TestResult
        {
            TestName = "2-Sample F test",
            StatisticName = "F",
            Statistic = f,
            PValue = p,
            DegreesOfFreedom = n1 - 1,
            DenominatorDegreesOfFreedom = n2 - 1,
            Alternative = alternative,
            Values = V(("Sx1", sx1), ("Sx2", sx2), ("n1", n1), ("n2", n2)),
        };
    }

    /// <summary>2-sample F test from data (TI 2-SampFTest, Data input).</summary>
    public static TestResult TwoSampleFTest(IReadOnlyList<double> data1, IReadOnlyList<double> data2, Alternative alternative = Alternative.NotEqual)
    {
        var a = Descriptive.OneVar(data1);
        var b = Descriptive.OneVar(data2);
        var r = TwoSampleFTest(a.SampleStdDev, a.N, b.SampleStdDev, b.N, alternative);
        var v = new Dictionary<string, double>(r.Values) { ["xbar1"] = a.Mean, ["xbar2"] = b.Mean };
        return r with { Values = v };
    }

    // ------------------------------------------------------------------ regression t test

    /// <summary>Linear regression t test for the slope (TI LinRegTTest), H0: beta = rho = 0, fitted line y = a + b x.</summary>
    public static LinRegTTestResult LinRegTTest(IReadOnlyList<double> x, IReadOnlyList<double> y, Alternative alternative = Alternative.NotEqual, IReadOnlyList<double>? frequencies = null)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.SameLength(x.Count, y.Count, "x and y");
        if (frequencies is not null) Descriptive.CheckFrequencies(frequencies, x.Count);
        CheckAlternative(alternative);
        var f = Regression.SimpleLinear(x, y, frequencies);
        if (!(f.Df > 0)) throw new ArgumentException("at least 3 data points are required");
        double t = f.Slope / f.SeSlope;
        if (f.SeSlope == 0) t = f.Slope == 0 ? 0 : Math.CopySign(double.PositiveInfinity, f.Slope);
        return new LinRegTTestResult
        {
            TestName = "Linear regression t test",
            StatisticName = "t",
            Statistic = t,
            PValue = TP(t, f.Df, alternative),
            DegreesOfFreedom = f.Df,
            Alternative = alternative,
            Values = V(("a", f.Intercept), ("b", f.Slope), ("s", f.S), ("r2", f.R * f.R), ("r", f.R)),
            A = f.Intercept,
            B = f.Slope,
            S = f.S,
            R = f.R,
            RSquared = f.R * f.R,
            SlopeStandardError = f.SeSlope,
            Residuals = f.Residuals,
        };
    }

    // ------------------------------------------------------------------ ANOVA

    /// <summary>One-way analysis of variance (TI ANOVA) for two or more groups.</summary>
    public static AnovaResult OneWayAnova(params IReadOnlyList<double>[] groups)
    {
        Guard.NotNull(groups, nameof(groups));
        int k = groups.Length;
        if (k < 2) throw new ArgumentException("at least two groups are required", nameof(groups));
        double n = 0;
        var grand = new Num.Accumulator();
        var means = new double[k];
        for (int g = 0; g < k; g++)
        {
            Guard.AllFinite(groups[g], nameof(groups));
            if (groups[g].Count == 0) throw new ArgumentException("every group must contain at least one value", nameof(groups));
            var acc = new Num.Accumulator();
            foreach (var v in groups[g]) { acc.Add(v); grand.Add(v); }
            means[g] = acc.Value / groups[g].Count;
            n += groups[g].Count;
        }
        if (n - k < 1) throw new ArgumentException("the error degrees of freedom must be positive (more values than groups are required)", nameof(groups));
        double gm = grand.Value / n;
        var ssf = new Num.Accumulator();
        var sse = new Num.Accumulator();
        for (int g = 0; g < k; g++)
        {
            double d = means[g] - gm;
            ssf.Add(groups[g].Count * d * d);
            foreach (var v in groups[g]) sse.Add((v - means[g]) * (v - means[g]));
        }
        double dff = k - 1, dfe = n - k;
        double msf = ssf.Value / dff, mse = sse.Value / dfe;
        double f = msf / mse;
        double p = mse > 0 ? new FDistribution(dff, dfe).Survival(f) : (msf > 0 ? 0.0 : double.NaN);
        if (mse == 0) f = msf > 0 ? double.PositiveInfinity : double.NaN;
        return new AnovaResult
        {
            TestName = "One-way ANOVA",
            StatisticName = "F",
            Statistic = f,
            PValue = p,
            DegreesOfFreedom = dff,
            DenominatorDegreesOfFreedom = dfe,
            Alternative = Alternative.Greater,
            Factor = new AnovaRow("Factor", dff, ssf.Value, msf, f, p),
            Error = new AnovaRow("Error", dfe, sse.Value, mse),
            Total = new AnovaRow("Total", n - 1, ssf.Value + sse.Value, (ssf.Value + sse.Value) / (n - 1)),
            PooledStdDev = Math.Sqrt(mse),
            Values = V(("Sxp", Math.Sqrt(mse)), ("SSfactor", ssf.Value), ("SSerror", sse.Value), ("MSfactor", msf), ("MSerror", mse)),
        };
    }

    /// <summary>
    /// Balanced two-way ANOVA with replication. data[i][j] holds the replicate observations for level i of factor A
    /// and level j of factor B; every cell must have the same number (at least 2) of observations.
    /// </summary>
    public static TwoWayAnovaResult TwoWayAnova(double[][][] data)
    {
        Guard.NotNull(data, nameof(data));
        int a = data.Length;
        if (a < 2) throw new ArgumentException("factor A needs at least two levels", nameof(data));
        int b = data[0]?.Length ?? 0;
        if (b < 2) throw new ArgumentException("factor B needs at least two levels", nameof(data));
        int r = data[0][0]?.Length ?? 0;
        if (r < 2) throw new ArgumentException("every cell needs at least two replicates", nameof(data));
        var cell = new double[a, b];
        var rowMean = new double[a];
        var colMean = new double[b];
        var grand = new Num.Accumulator();
        for (int i = 0; i < a; i++)
        {
            if (data[i] is null || data[i].Length != b) throw new ArgumentException("every level of factor A must have the same number of factor B levels", nameof(data));
            for (int j = 0; j < b; j++)
            {
                var c = data[i][j];
                if (c is null || c.Length != r) throw new ArgumentException("the design must be balanced: every cell needs the same number of replicates", nameof(data));
                Guard.AllFinite(c, nameof(data));
                var acc = new Num.Accumulator();
                foreach (var v in c) { acc.Add(v); grand.Add(v); }
                cell[i, j] = acc.Value / r;
            }
        }
        double gm = grand.Value / (a * b * r);
        for (int i = 0; i < a; i++)
        {
            double s = 0;
            for (int j = 0; j < b; j++) s += cell[i, j];
            rowMean[i] = s / b;
        }
        for (int j = 0; j < b; j++)
        {
            double s = 0;
            for (int i = 0; i < a; i++) s += cell[i, j];
            colMean[j] = s / a;
        }
        var ssa = new Num.Accumulator();
        var ssb = new Num.Accumulator();
        var ssab = new Num.Accumulator();
        var sse = new Num.Accumulator();
        for (int i = 0; i < a; i++) ssa.Add(b * r * (rowMean[i] - gm) * (rowMean[i] - gm));
        for (int j = 0; j < b; j++) ssb.Add(a * r * (colMean[j] - gm) * (colMean[j] - gm));
        for (int i = 0; i < a; i++)
            for (int j = 0; j < b; j++)
            {
                double d = cell[i, j] - rowMean[i] - colMean[j] + gm;
                ssab.Add(r * d * d);
                foreach (var v in data[i][j]) sse.Add((v - cell[i, j]) * (v - cell[i, j]));
            }
        double dfa = a - 1, dfb = b - 1, dfab = dfa * dfb, dfe = a * b * (r - 1);
        double mse = sse.Value / dfe;
        AnovaRow Row(string name, double df, double ss, out TestResult test)
        {
            double ms = ss / df;
            double f = ms / mse;
            double p = new FDistribution(df, dfe).Survival(f);
            test = new TestResult
            {
                TestName = "Two-way ANOVA: " + name,
                StatisticName = "F",
                Statistic = f,
                PValue = p,
                DegreesOfFreedom = df,
                DenominatorDegreesOfFreedom = dfe,
                Alternative = Alternative.Greater,
            };
            return new AnovaRow(name, df, ss, ms, f, p);
        }
        var rowA = Row("Factor A", dfa, ssa.Value, out var ta);
        var rowB = Row("Factor B", dfb, ssb.Value, out var tb);
        var rowAB = Row("Interaction", dfab, ssab.Value, out var tab);
        double sst = ssa.Value + ssb.Value + ssab.Value + sse.Value;
        return new TwoWayAnovaResult
        {
            FactorA = rowA,
            FactorB = rowB,
            Interaction = rowAB,
            Error = new AnovaRow("Error", dfe, sse.Value, mse),
            Total = new AnovaRow("Total", a * b * r - 1, sst, sst / (a * b * r - 1)),
            FactorATest = ta,
            FactorBTest = tb,
            InteractionTest = tab,
        };
    }

    // ------------------------------------------------------------------ confidence intervals

    private static double ZCrit(double c) => StdNormal.Quantile(0.5 + 0.5 * c, (1 - c) / 2);

    private static double TCrit(double c, double df) => new StudentTDistribution(df).InverseSurvival((1 - c) / 2);

    private static ConfidenceInterval Interval(string name, double est, double me, double c, double? df, Dictionary<string, double> values) => new()
    {
        Name = name,
        Lower = est - me,
        Upper = est + me,
        PointEstimate = est,
        MarginOfError = me,
        ConfidenceLevel = c,
        DegreesOfFreedom = df,
        Values = values,
    };

    /// <summary>1-sample z interval (TI ZInterval, Stats input).</summary>
    public static ConfidenceInterval ZInterval(double sigma, double mean, double n, double level = 0.95)
    {
        Guard.PositiveFinite(sigma, "the population standard deviation", nameof(sigma));
        Guard.Finite(mean, "the sample mean", nameof(mean));
        Guard.PositiveFinite(n, "the sample size", nameof(n));
        double c = ConfidenceInterval.NormalizeLevel(level);
        return Interval("1-Sample z interval", mean, ZCrit(c) * sigma / Math.Sqrt(n), c, null, V(("xbar", mean), ("n", n)));
    }

    /// <summary>1-sample z interval from data (TI ZInterval, Data input).</summary>
    public static ConfidenceInterval ZInterval(double sigma, IReadOnlyList<double> data, double level = 0.95, IReadOnlyList<double>? frequencies = null)
    {
        var st = Descriptive.OneVar(data, frequencies);
        var ci = ZInterval(sigma, st.Mean, st.N, level);
        var v = new Dictionary<string, double>(ci.Values) { ["Sx"] = st.SampleStdDev };
        return ci with { Values = v };
    }

    /// <summary>1-sample t interval (TI TInterval, Stats input).</summary>
    public static ConfidenceInterval TInterval(double mean, double sx, double n, double level = 0.95)
    {
        Guard.Finite(mean, "the sample mean", nameof(mean));
        if (!(sx >= 0) || !double.IsFinite(sx)) throw new ArgumentException("the sample standard deviation must be a non-negative finite number", nameof(sx));
        if (!(n > 1)) throw new ArgumentException("the sample size must be greater than 1", nameof(n));
        double c = ConfidenceInterval.NormalizeLevel(level);
        double df = n - 1;
        return Interval("1-Sample t interval", mean, TCrit(c, df) * sx / Math.Sqrt(n), c, df, V(("xbar", mean), ("Sx", sx), ("n", n)));
    }

    /// <summary>1-sample t interval from data (TI TInterval, Data input).</summary>
    public static ConfidenceInterval TInterval(IReadOnlyList<double> data, double level = 0.95, IReadOnlyList<double>? frequencies = null)
    {
        var st = Descriptive.OneVar(data, frequencies);
        return TInterval(st.Mean, st.SampleStdDev, st.N, level);
    }

    /// <summary>2-sample z interval for mu1 - mu2 (TI 2-SampZInt).</summary>
    public static ConfidenceInterval TwoSampleZInterval(double sigma1, double sigma2, double mean1, double n1, double mean2, double n2, double level = 0.95)
    {
        Guard.PositiveFinite(sigma1, "sigma1", nameof(sigma1));
        Guard.PositiveFinite(sigma2, "sigma2", nameof(sigma2));
        Guard.PositiveFinite(n1, "the first sample size", nameof(n1));
        Guard.PositiveFinite(n2, "the second sample size", nameof(n2));
        double c = ConfidenceInterval.NormalizeLevel(level);
        double se = Math.Sqrt(sigma1 * sigma1 / n1 + sigma2 * sigma2 / n2);
        return Interval("2-Sample z interval", mean1 - mean2, ZCrit(c) * se, c, null, V(("xbar1", mean1), ("xbar2", mean2), ("n1", n1), ("n2", n2)));
    }

    /// <summary>2-sample z interval from data.</summary>
    public static ConfidenceInterval TwoSampleZInterval(double sigma1, double sigma2, IReadOnlyList<double> data1, IReadOnlyList<double> data2, double level = 0.95)
    {
        var a = Descriptive.OneVar(data1);
        var b = Descriptive.OneVar(data2);
        return TwoSampleZInterval(sigma1, sigma2, a.Mean, a.N, b.Mean, b.N, level);
    }

    /// <summary>2-sample t interval for mu1 - mu2 (TI 2-SampTInt), pooled or Welch.</summary>
    public static ConfidenceInterval TwoSampleTInterval(double mean1, double sx1, double n1, double mean2, double sx2, double n2, double level = 0.95, bool pooled = false)
    {
        CheckTwoSampleT(sx1, n1, sx2, n2);
        double c = ConfidenceInterval.NormalizeLevel(level);
        var (se, df, sp) = TwoSampleSe(sx1, n1, sx2, n2, pooled);
        var v = V(("xbar1", mean1), ("xbar2", mean2), ("Sx1", sx1), ("Sx2", sx2), ("n1", n1), ("n2", n2));
        if (pooled) v["Sxp"] = sp;
        return Interval(pooled ? "2-Sample t interval (pooled)" : "2-Sample t interval (Welch)", mean1 - mean2, TCrit(c, df) * se, c, df, v);
    }

    /// <summary>2-sample t interval from data.</summary>
    public static ConfidenceInterval TwoSampleTInterval(IReadOnlyList<double> data1, IReadOnlyList<double> data2, double level = 0.95, bool pooled = false)
    {
        var a = Descriptive.OneVar(data1);
        var b = Descriptive.OneVar(data2);
        return TwoSampleTInterval(a.Mean, a.SampleStdDev, a.N, b.Mean, b.SampleStdDev, b.N, level, pooled);
    }

    /// <summary>1-proportion z interval (TI 1-PropZInt): phat +/- z* sqrt(phat (1 - phat) / n).</summary>
    public static ConfidenceInterval OnePropZInterval(double x, double n, double level = 0.95)
    {
        CheckCount(x, n, nameof(x), nameof(n));
        double c = ConfidenceInterval.NormalizeLevel(level);
        double p = x / n;
        return Interval("1-Proportion z interval", p, ZCrit(c) * Math.Sqrt(p * (1 - p) / n), c, null, V(("phat", p), ("n", n)));
    }

    /// <summary>2-proportion z interval for p1 - p2 (TI 2-PropZInt, unpooled standard error).</summary>
    public static ConfidenceInterval TwoPropZInterval(double x1, double n1, double x2, double n2, double level = 0.95)
    {
        CheckCount(x1, n1, nameof(x1), nameof(n1));
        CheckCount(x2, n2, nameof(x2), nameof(n2));
        double c = ConfidenceInterval.NormalizeLevel(level);
        double p1 = x1 / n1, p2 = x2 / n2;
        double se = Math.Sqrt(p1 * (1 - p1) / n1 + p2 * (1 - p2) / n2);
        return Interval("2-Proportion z interval", p1 - p2, ZCrit(c) * se, c, null, V(("p1hat", p1), ("p2hat", p2), ("n1", n1), ("n2", n2)));
    }

    /// <summary>t interval for the regression slope (TI LinRegTInt) of y = a + b x.</summary>
    public static ConfidenceInterval LinRegTInterval(IReadOnlyList<double> x, IReadOnlyList<double> y, double level = 0.95, IReadOnlyList<double>? frequencies = null)
    {
        var f = RegressionCheck(x, y, frequencies);
        double c = ConfidenceInterval.NormalizeLevel(level);
        return Interval("Linear regression slope interval", f.Slope, TCrit(c, f.Df) * f.SeSlope, c, f.Df,
            V(("a", f.Intercept), ("b", f.Slope), ("s", f.S), ("r2", f.R * f.R), ("r", f.R), ("SEb", f.SeSlope)));
    }

    /// <summary>Confidence interval for the mean response a + b x0 of a simple linear regression.</summary>
    public static ConfidenceInterval MeanResponseInterval(IReadOnlyList<double> x, IReadOnlyList<double> y, double x0, double level = 0.95, IReadOnlyList<double>? frequencies = null)
    {
        var f = RegressionCheck(x, y, frequencies);
        Guard.Finite(x0, "x0", nameof(x0));
        double c = ConfidenceInterval.NormalizeLevel(level);
        double yhat = f.Intercept + f.Slope * x0;
        double se = f.S * Math.Sqrt(1 / f.N + (x0 - f.MeanX) * (x0 - f.MeanX) / f.Sxx);
        return Interval("Mean response interval", yhat, TCrit(c, f.Df) * se, c, f.Df, V(("yhat", yhat), ("SE", se), ("x0", x0), ("s", f.S)));
    }

    /// <summary>Prediction interval for a new observation at x0 of a simple linear regression.</summary>
    public static ConfidenceInterval PredictionInterval(IReadOnlyList<double> x, IReadOnlyList<double> y, double x0, double level = 0.95, IReadOnlyList<double>? frequencies = null)
    {
        var f = RegressionCheck(x, y, frequencies);
        Guard.Finite(x0, "x0", nameof(x0));
        double c = ConfidenceInterval.NormalizeLevel(level);
        double yhat = f.Intercept + f.Slope * x0;
        double se = f.S * Math.Sqrt(1 + 1 / f.N + (x0 - f.MeanX) * (x0 - f.MeanX) / f.Sxx);
        return Interval("Prediction interval", yhat, TCrit(c, f.Df) * se, c, f.Df, V(("yhat", yhat), ("SE", se), ("x0", x0), ("s", f.S)));
    }

    private static Regression.SimpleFit RegressionCheck(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? frequencies)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.SameLength(x.Count, y.Count, "x and y");
        if (frequencies is not null) Descriptive.CheckFrequencies(frequencies, x.Count);
        var f = Regression.SimpleLinear(x, y, frequencies);
        if (!(f.Df > 0)) throw new ArgumentException("at least 3 data points are required");
        return f;
    }
}
