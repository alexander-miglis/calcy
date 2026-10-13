namespace Calcy.Statistics;

/// <summary>Alternative hypothesis of a test.</summary>
public enum Alternative
{
    /// <summary>Two-sided: the parameter differs from the hypothesised value.</summary>
    NotEqual,
    /// <summary>One-sided: the parameter is less than the hypothesised value.</summary>
    Less,
    /// <summary>One-sided: the parameter is greater than the hypothesised value.</summary>
    Greater,
}

/// <summary>Result of a hypothesis test.</summary>
public record TestResult
{
    private static readonly IReadOnlyDictionary<string, double> Empty = new Dictionary<string, double>();

    /// <summary>Name of the test (for example "1-Sample t test").</summary>
    public required string TestName { get; init; }
    /// <summary>Name of the test statistic ("z", "t", "chi2", "F", "U", "W", "H", "S", "rho", "tau").</summary>
    public required string StatisticName { get; init; }
    /// <summary>Value of the test statistic.</summary>
    public required double Statistic { get; init; }
    /// <summary>The p-value.</summary>
    public required double PValue { get; init; }
    /// <summary>Degrees of freedom (numerator df for F tests); may be non-integer (Welch); null when not applicable.</summary>
    public double? DegreesOfFreedom { get; init; }
    /// <summary>Denominator degrees of freedom for F tests.</summary>
    public double? DenominatorDegreesOfFreedom { get; init; }
    /// <summary>The alternative hypothesis used for the p-value.</summary>
    public Alternative Alternative { get; init; }
    /// <summary>Extra named outputs, using TI style names: xbar, Sx, n, phat, Sxp, a, b, s, r, r2, and so on.</summary>
    public IReadOnlyDictionary<string, double> Values { get; init; } = Empty;

    /// <summary>True when the null hypothesis is rejected at significance level alpha (p-value &lt;= alpha).</summary>
    public bool RejectsNull(double alpha = 0.05)
    {
        if (!(alpha > 0 && alpha < 1)) throw new ArgumentException("the significance level must be between 0 and 1", nameof(alpha));
        return PValue <= alpha;
    }

    /// <summary>"Reject H0" or "Fail to reject H0" at significance level alpha.</summary>
    public string Decision(double alpha = 0.05) => RejectsNull(alpha) ? "Reject H0" : "Fail to reject H0";

    /// <summary>Looks up one of the named extra values.</summary>
    public double Value(string name)
    {
        if (Values.TryGetValue(name, out double v)) return v;
        throw new ArgumentException($"the result has no value named '{name}'", nameof(name));
    }
}

/// <summary>Chi-square goodness of fit result with the per-category contributions (TI CNTRB list).</summary>
public sealed record ChiSquareGoodnessOfFitResult : TestResult
{
    /// <summary>(O - E)^2 / E for every category.</summary>
    public required IReadOnlyList<double> Contributions { get; init; }
}

/// <summary>Chi-square test of independence (two-way table) with the expected counts.</summary>
public sealed record ChiSquareTwoWayResult : TestResult
{
    /// <summary>Expected counts (row total x column total / grand total).</summary>
    public required double[,] Expected { get; init; }
    /// <summary>(O - E)^2 / E for every cell.</summary>
    public required double[,] Contributions { get; init; }
}

/// <summary>One line of an ANOVA table.</summary>
public sealed record AnovaRow(string Source, double DegreesOfFreedom, double SumOfSquares, double MeanSquare, double? F = null, double? PValue = null);

/// <summary>One-way ANOVA result (TI ANOVA output).</summary>
public sealed record AnovaResult : TestResult
{
    /// <summary>Between groups (factor) row.</summary>
    public required AnovaRow Factor { get; init; }
    /// <summary>Within groups (error) row.</summary>
    public required AnovaRow Error { get; init; }
    /// <summary>Total row.</summary>
    public required AnovaRow Total { get; init; }
    /// <summary>Pooled standard deviation Sxp = sqrt(MS error).</summary>
    public required double PooledStdDev { get; init; }
}

/// <summary>Two-way ANOVA with replication.</summary>
public sealed record TwoWayAnovaResult
{
    /// <summary>Row factor (A).</summary>
    public required AnovaRow FactorA { get; init; }
    /// <summary>Column factor (B).</summary>
    public required AnovaRow FactorB { get; init; }
    /// <summary>Interaction A x B.</summary>
    public required AnovaRow Interaction { get; init; }
    /// <summary>Error (within cells).</summary>
    public required AnovaRow Error { get; init; }
    /// <summary>Total.</summary>
    public required AnovaRow Total { get; init; }
    /// <summary>F test for factor A.</summary>
    public required TestResult FactorATest { get; init; }
    /// <summary>F test for factor B.</summary>
    public required TestResult FactorBTest { get; init; }
    /// <summary>F test for the interaction.</summary>
    public required TestResult InteractionTest { get; init; }
}

/// <summary>Linear regression t test (TI LinRegTTest) with the fitted line y = a + b x.</summary>
public sealed record LinRegTTestResult : TestResult
{
    /// <summary>Intercept a.</summary>
    public required double A { get; init; }
    /// <summary>Slope b.</summary>
    public required double B { get; init; }
    /// <summary>Residual standard error s.</summary>
    public required double S { get; init; }
    /// <summary>Correlation coefficient r.</summary>
    public required double R { get; init; }
    /// <summary>r squared.</summary>
    public required double RSquared { get; init; }
    /// <summary>Standard error of the slope.</summary>
    public required double SlopeStandardError { get; init; }
    /// <summary>Residuals y - (a + b x).</summary>
    public required IReadOnlyList<double> Residuals { get; init; }
}

/// <summary>A confidence (or prediction) interval.</summary>
public sealed record ConfidenceInterval
{
    private static readonly IReadOnlyDictionary<string, double> Empty = new Dictionary<string, double>();

    /// <summary>Name of the procedure.</summary>
    public required string Name { get; init; }
    /// <summary>Lower bound.</summary>
    public required double Lower { get; init; }
    /// <summary>Upper bound.</summary>
    public required double Upper { get; init; }
    /// <summary>Point estimate at the centre of the interval.</summary>
    public required double PointEstimate { get; init; }
    /// <summary>Half width (critical value times standard error).</summary>
    public required double MarginOfError { get; init; }
    /// <summary>Confidence level as a fraction (0.95).</summary>
    public required double ConfidenceLevel { get; init; }
    /// <summary>Degrees of freedom of the t procedures, null for z procedures.</summary>
    public double? DegreesOfFreedom { get; init; }
    /// <summary>Extra named outputs (xbar, Sx, n, phat, ...).</summary>
    public IReadOnlyDictionary<string, double> Values { get; init; } = Empty;

    /// <summary>Accepts a fraction in (0, 1) or a percentage in (1, 100) like the TI-84 C-Level field.</summary>
    internal static double NormalizeLevel(double level)
    {
        if (level > 0 && level < 1) return level;
        if (level > 1 && level < 100) return level / 100.0;
        throw new ArgumentException("the confidence level must be between 0 and 1 (or a percentage between 1 and 100)", nameof(level));
    }
}
