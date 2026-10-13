namespace Calcy.Modes.Statistics.Logic;

/// <summary>The hypothesis tests and confidence intervals of the Tests tab.</summary>
public enum TestKind
{
    /// <summary>1-sample z test.</summary>
    ZTest,

    /// <summary>1-sample t test.</summary>
    TTest,

    /// <summary>2-sample z test.</summary>
    TwoSampleZTest,

    /// <summary>2-sample t test (Welch or pooled).</summary>
    TwoSampleTTest,

    /// <summary>Paired t test.</summary>
    PairedTTest,

    /// <summary>1-proportion z test.</summary>
    OnePropZTest,

    /// <summary>2-proportion z test.</summary>
    TwoPropZTest,

    /// <summary>χ² goodness of fit.</summary>
    ChiSquareGof,

    /// <summary>χ² test of independence on a table of counts.</summary>
    ChiSquareTwoWay,

    /// <summary>2-sample F test.</summary>
    TwoSampleFTest,

    /// <summary>Linear regression t test.</summary>
    LinRegTTest,

    /// <summary>One-way ANOVA.</summary>
    Anova,

    /// <summary>z interval for a mean.</summary>
    ZInterval,

    /// <summary>t interval for a mean.</summary>
    TInterval,

    /// <summary>2-sample z interval.</summary>
    TwoSampleZInterval,

    /// <summary>2-sample t interval.</summary>
    TwoSampleTInterval,

    /// <summary>1-proportion z interval.</summary>
    OnePropZInterval,

    /// <summary>2-proportion z interval.</summary>
    TwoPropZInterval,

    /// <summary>t interval for the regression slope.</summary>
    LinRegTInterval,

    /// <summary>Sign test for a median.</summary>
    SignTest,

    /// <summary>Wilcoxon signed rank test.</summary>
    WilcoxonSignedRank,

    /// <summary>Mann-Whitney U test.</summary>
    MannWhitney,

    /// <summary>Kruskal-Wallis test.</summary>
    KruskalWallis,

    /// <summary>Spearman rank correlation test.</summary>
    Spearman,

    /// <summary>Kendall rank correlation test.</summary>
    Kendall,
}

/// <summary>What a test field holds.</summary>
public enum FieldKind
{
    /// <summary>A number typed in a box.</summary>
    Number,

    /// <summary>One list, picked from the lists.</summary>
    List,

    /// <summary>An optional frequency list.</summary>
    Frequency,

    /// <summary>Several lists (ANOVA groups, the columns of a table).</summary>
    Lists,

    /// <summary>On or off (pooled variance).</summary>
    Flag,
}

/// <summary>One input of a test.</summary>
/// <param name="Key">Settings key (mu0, list, xbar, ...).</param>
/// <param name="Label">Label on screen.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Default">The value when nothing was typed ("" for list fields means the first list, "2" the second).</param>
/// <param name="Tip">What it means.</param>
public sealed record TestField(string Key, string Label, FieldKind Kind, string Default, string Tip);

/// <summary>A test or interval of the menu.</summary>
/// <param name="Kind">The test.</param>
/// <param name="Name">Menu name.</param>
/// <param name="CardName">Card title (pixel font characters).</param>
/// <param name="IsInterval">True for confidence intervals.</param>
/// <param name="HasStats">True when summary statistics can be typed instead of lists.</param>
/// <param name="HasAlternative">True when the alternative hypothesis can be one-sided.</param>
public sealed record TestInfo(TestKind Kind, string Name, string CardName, bool IsInterval, bool HasStats, bool HasAlternative);

/// <summary>The menu of tests and intervals and the inputs each one needs (the TI-84 STAT TESTS layout).</summary>
public static class TestCatalog
{
    /// <summary>Every test and interval in menu order.</summary>
    public static readonly IReadOnlyList<TestInfo> All =
    [
        new(TestKind.ZTest, "1-sample z test", "Z-TEST", false, true, true),
        new(TestKind.TTest, "1-sample t test", "T-TEST", false, true, true),
        new(TestKind.TwoSampleZTest, "2-sample z test", "2-SAMP Z-TEST", false, true, true),
        new(TestKind.TwoSampleTTest, "2-sample t test", "2-SAMP T-TEST", false, true, true),
        new(TestKind.PairedTTest, "Paired t test", "PAIRED T-TEST", false, false, true),
        new(TestKind.OnePropZTest, "1-proportion z test", "1-PROP Z-TEST", false, false, true),
        new(TestKind.TwoPropZTest, "2-proportion z test", "2-PROP Z-TEST", false, false, true),
        new(TestKind.ChiSquareGof, "χ² goodness of fit", "CHI2 GOF-TEST", false, false, false),
        new(TestKind.ChiSquareTwoWay, "χ² test of independence", "CHI2-TEST", false, false, false),
        new(TestKind.TwoSampleFTest, "2-sample F test", "2-SAMP F-TEST", false, true, true),
        new(TestKind.LinRegTTest, "Linear regression t test", "LINREG T-TEST", false, false, true),
        new(TestKind.Anova, "One-way ANOVA", "ANOVA", false, false, false),
        new(TestKind.ZInterval, "z interval", "Z-INTERVAL", true, true, false),
        new(TestKind.TInterval, "t interval", "T-INTERVAL", true, true, false),
        new(TestKind.TwoSampleZInterval, "2-sample z interval", "2-SAMP Z-INT", true, true, false),
        new(TestKind.TwoSampleTInterval, "2-sample t interval", "2-SAMP T-INT", true, true, false),
        new(TestKind.OnePropZInterval, "1-proportion z interval", "1-PROP Z-INT", true, false, false),
        new(TestKind.TwoPropZInterval, "2-proportion z interval", "2-PROP Z-INT", true, false, false),
        new(TestKind.LinRegTInterval, "Regression slope t interval", "LINREG T-INT", true, false, false),
        new(TestKind.SignTest, "Sign test (median)", "SIGN TEST", false, false, true),
        new(TestKind.WilcoxonSignedRank, "Wilcoxon signed rank test", "WILCOXON", false, false, true),
        new(TestKind.MannWhitney, "Mann-Whitney U test", "MANN-WHITNEY", false, false, true),
        new(TestKind.KruskalWallis, "Kruskal-Wallis test", "KRUSKAL-WALLIS", false, false, false),
        new(TestKind.Spearman, "Spearman rank correlation", "SPEARMAN", false, false, true),
        new(TestKind.Kendall, "Kendall rank correlation", "KENDALL", false, false, true),
    ];

    /// <summary>The info of a test.</summary>
    public static TestInfo Info(TestKind kind) => All.First(t => t.Kind == kind);

    private static TestField Num(string key, string label, string def, string tip) => new(key, label, FieldKind.Number, def, tip);

    private static TestField List(string key, string label, string def, string tip) => new(key, label, FieldKind.List, def, tip);

    private static readonly TestField Mu0 = Num("mu0", "μ₀", "0", "The mean under H₀");
    private static readonly TestField Sigma = Num("sigma", "σ", "1", "The known population standard deviation");
    private static readonly TestField One = List("list", "List", "1", "The data");
    private static readonly TestField Freq = new("freq", "Freq", FieldKind.Frequency, "", "How often each value occurs (optional)");
    private static readonly TestField Xbar = Num("xbar", "x̄", "0", "The sample mean");
    private static readonly TestField Sx = Num("sx", "Sx", "1", "The sample standard deviation");
    private static readonly TestField N = Num("n", "n", "30", "The sample size");
    private static readonly TestField List1 = List("list1", "List 1", "1", "The first sample");
    private static readonly TestField List2 = List("list2", "List 2", "2", "The second sample");
    private static readonly TestField Pooled = new("pooled", "Pooled", FieldKind.Flag, "false", "Assume equal variances and pool them");
    private static readonly TestField XList = List("xlist", "X list", "1", "The explanatory values");
    private static readonly TestField YList = List("ylist", "Y list", "2", "The response values");
    private static readonly TestField Groups = new("lists", "Lists", FieldKind.Lists, "", "The groups (pick two or more lists)");

    private static TestField[] TwoSampleStats(bool sx) => sx
        ? [Num("xbar1", "x̄₁", "0", "Mean of sample 1"), Num("sx1", "Sx₁", "1", "Standard deviation of sample 1"), Num("n1", "n₁", "30", "Size of sample 1"),
           Num("xbar2", "x̄₂", "0", "Mean of sample 2"), Num("sx2", "Sx₂", "1", "Standard deviation of sample 2"), Num("n2", "n₂", "30", "Size of sample 2")]
        : [Num("xbar1", "x̄₁", "0", "Mean of sample 1"), Num("n1", "n₁", "30", "Size of sample 1"), Num("xbar2", "x̄₂", "0", "Mean of sample 2"), Num("n2", "n₂", "30", "Size of sample 2")];

    private static readonly TestField[] Sigmas = [Num("sigma1", "σ₁", "1", "Known standard deviation of population 1"), Num("sigma2", "σ₂", "1", "Known standard deviation of population 2")];

    private static readonly TestField[] TwoProps =
        [Num("x1", "x₁", "45", "Successes in sample 1"), Num("n1", "n₁", "100", "Size of sample 1"), Num("x2", "x₂", "30", "Successes in sample 2"), Num("n2", "n₂", "100", "Size of sample 2")];

    /// <summary>The inputs of a test, for list (data) or summary (stats) input.</summary>
    public static IReadOnlyList<TestField> Fields(TestKind kind, bool stats)
    {
        bool s = stats && Info(kind).HasStats;
        return kind switch
        {
            TestKind.ZTest => s ? [Mu0, Sigma, Xbar, N] : [Mu0, Sigma, One, Freq],
            TestKind.TTest => s ? [Mu0, Xbar, Sx, N] : [Mu0, One, Freq],
            TestKind.TwoSampleZTest => s ? [.. Sigmas, .. TwoSampleStats(false)] : [.. Sigmas, List1, List2],
            TestKind.TwoSampleTTest => s ? [.. TwoSampleStats(true), Pooled] : [List1, List2, Pooled],
            TestKind.PairedTTest => [List1, List2, Num("mu0", "μd₀", "0", "The mean difference under H₀")],
            TestKind.OnePropZTest => [Num("p0", "p₀", "0.5", "The proportion under H₀"), Num("x", "x", "60", "Number of successes"), Num("n", "n", "100", "Number of trials")],
            TestKind.TwoPropZTest => TwoProps,
            TestKind.ChiSquareGof => [List("observed", "Observed", "1", "Observed counts"), List("expected", "Expected", "2", "Expected counts"), Num("df", "df", "", "Degrees of freedom (empty: categories - 1)")],
            TestKind.ChiSquareTwoWay => [new TestField("lists", "Columns", FieldKind.Lists, "", "The columns of the table of counts")],
            TestKind.TwoSampleFTest => s
                ? [Num("sx1", "Sx₁", "1", "Standard deviation of sample 1"), Num("n1", "n₁", "30", "Size of sample 1"), Num("sx2", "Sx₂", "1", "Standard deviation of sample 2"), Num("n2", "n₂", "30", "Size of sample 2")]
                : [List1, List2],
            TestKind.LinRegTTest or TestKind.LinRegTInterval => [XList, YList, Freq],
            TestKind.Anova or TestKind.KruskalWallis => [Groups],
            TestKind.ZInterval => s ? [Sigma, Xbar, N] : [Sigma, One, Freq],
            TestKind.TInterval => s ? [Xbar, Sx, N] : [One, Freq],
            TestKind.TwoSampleZInterval => s ? [.. Sigmas, .. TwoSampleStats(false)] : [.. Sigmas, List1, List2],
            TestKind.TwoSampleTInterval => s ? [.. TwoSampleStats(true), Pooled] : [List1, List2, Pooled],
            TestKind.OnePropZInterval => [Num("x", "x", "60", "Number of successes"), Num("n", "n", "100", "Number of trials")],
            TestKind.TwoPropZInterval => TwoProps,
            TestKind.SignTest or TestKind.WilcoxonSignedRank => [One, Num("m0", "median₀", "0", "The median under H₀")],
            TestKind.MannWhitney => [List1, List2],
            TestKind.Spearman or TestKind.Kendall => [XList, YList],
            _ => [],
        };
    }

    /// <summary>
    /// The value of a field: what was typed, else the default. A list field's default "1" or "2" means the first or
    /// second list of <paramref name="lists"/>.
    /// </summary>
    public static string Value(TestSettings s, TestField f, IReadOnlyList<string> lists)
    {
        if (s.Fields.TryGetValue(f.Key, out var typed))
        {
            return f.Kind switch
            {
                FieldKind.List => lists.Contains(typed) ? typed : DefaultList(f, lists),
                FieldKind.Frequency => typed.Length == 0 || lists.Contains(typed) ? typed : "",
                FieldKind.Lists => string.Join(",", typed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(lists.Contains)),
                _ => typed,
            };
        }
        return f.Kind switch
        {
            FieldKind.List => DefaultList(f, lists),
            FieldKind.Lists => string.Join(",", lists.Take(Math.Min(lists.Count, 3))),
            _ => f.Default,
        };
    }

    private static string DefaultList(TestField f, IReadOnlyList<string> lists)
    {
        if (lists.Count == 0) return "";
        int i = f.Default == "2" ? Math.Min(1, lists.Count - 1) : 0;
        return lists[i];
    }
}
