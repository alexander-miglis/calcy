using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>The analysis tabs.</summary>
public enum StatTab
{
    /// <summary>One-variable statistics with a box plot.</summary>
    OneVar,

    /// <summary>Two-variable statistics with a scatter plot.</summary>
    TwoVar,

    /// <summary>Regressions with the fitted curve.</summary>
    Regression,

    /// <summary>Hypothesis tests and confidence intervals.</summary>
    Tests,

    /// <summary>The distribution explorer.</summary>
    Distribution,

    /// <summary>Statistical plots.</summary>
    Plots,
}

/// <summary>Settings of the 1-Var tab.</summary>
public sealed class OneVarSettings
{
    /// <summary>The list.</summary>
    public string List { get; set; } = "L1";

    /// <summary>The frequency list, or null.</summary>
    public string? Freq { get; set; }

    /// <summary>How quartiles are computed.</summary>
    public QuartileMethod Method { get; set; } = QuartileMethod.TI;

    /// <summary>A copy.</summary>
    public OneVarSettings Clone() => (OneVarSettings)MemberwiseClone();
}

/// <summary>Settings of the 2-Var tab.</summary>
public sealed class TwoVarSettings
{
    /// <summary>The x list.</summary>
    public string X { get; set; } = "L1";

    /// <summary>The y list.</summary>
    public string Y { get; set; } = "L2";

    /// <summary>The frequency list, or null.</summary>
    public string? Freq { get; set; }

    /// <summary>A copy.</summary>
    public TwoVarSettings Clone() => (TwoVarSettings)MemberwiseClone();
}

/// <summary>Settings of the Regression tab.</summary>
public sealed class RegressionSettings
{
    /// <summary>The x list.</summary>
    public string X { get; set; } = "L1";

    /// <summary>The y list.</summary>
    public string Y { get; set; } = "L2";

    /// <summary>The frequency (weight) list, or null.</summary>
    public string? Freq { get; set; }

    /// <summary>The model.</summary>
    public RegressionKind Model { get; set; } = RegressionKind.LinearAxB;

    /// <summary>Degree of the polynomial model.</summary>
    public int Degree { get; set; } = 2;

    /// <summary>The custom model in x with parameters a, b, c, ... (for example a*e^(b*x) + c).</summary>
    public string Custom { get; set; } = "a*e^(b*x)";

    /// <summary>Start values for the custom model ("a = 1, b = 0.1"); parameters left out start at 1.</summary>
    public string CustomStart { get; set; } = "";

    /// <summary>The x to predict at (text as typed).</summary>
    public string PredictX { get; set; } = "";

    /// <summary>The function name STORE uses (f stores f(x) = the fitted equation).</summary>
    public string StoreName { get; set; } = "f";

    /// <summary>Show the residual plot instead of the scatter plot.</summary>
    public bool Residuals { get; set; }

    /// <summary>A copy.</summary>
    public RegressionSettings Clone() => (RegressionSettings)MemberwiseClone();
}

/// <summary>Settings of the Tests tab.</summary>
public sealed class TestSettings
{
    /// <summary>The test or interval.</summary>
    public TestKind Kind { get; set; } = TestKind.TTest;

    /// <summary>True to type summary statistics (x̄, Sx, n) instead of using lists.</summary>
    public bool Stats { get; set; }

    /// <summary>The field texts by key (mu0, list, xbar, ...).</summary>
    public Dictionary<string, string> Fields { get; set; } = [];

    /// <summary>The alternative hypothesis.</summary>
    public Alternative Alternative { get; set; } = Alternative.NotEqual;

    /// <summary>The significance level as typed.</summary>
    public string Alpha { get; set; } = "0.05";

    /// <summary>The confidence level as typed (0.95 or 95).</summary>
    public string Level { get; set; } = "0.95";

    /// <summary>A copy (with its own field dictionary).</summary>
    public TestSettings Clone()
    {
        var c = (TestSettings)MemberwiseClone();
        c.Fields = new Dictionary<string, string>(Fields);
        return c;
    }

    /// <summary>A field's text, or the default when it was never set.</summary>
    public string Field(string key, string fallback = "") => Fields.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>What the distribution explorer computes.</summary>
public enum DistributionMode
{
    /// <summary>P(lower ≤ X ≤ upper).</summary>
    Between,

    /// <summary>The density f(x), or P(X = x) for counts.</summary>
    Density,

    /// <summary>The x with a given area to its left, right or around the centre.</summary>
    Inverse,
}

/// <summary>Settings of the distribution explorer.</summary>
public sealed class DistributionSettings
{
    /// <summary>The distribution.</summary>
    public DistributionKind Kind { get; set; } = DistributionKind.Normal;

    /// <summary>Parameter texts by key (mu, sigma, df, ...).</summary>
    public Dictionary<string, string> Parameters { get; set; } = [];

    /// <summary>What to compute.</summary>
    public DistributionMode Mode { get; set; } = DistributionMode.Between;

    /// <summary>Lower bound (text; empty or -inf for no bound).</summary>
    public string Lower { get; set; } = "-1.96";

    /// <summary>Upper bound (text; empty or inf for no bound).</summary>
    public string Upper { get; set; } = "1.96";

    /// <summary>The x for the density.</summary>
    public string X { get; set; } = "0";

    /// <summary>The area for the inverse.</summary>
    public string Area { get; set; } = "0.95";

    /// <summary>Which area the inverse refers to.</summary>
    public Tail Tail { get; set; } = Tail.Left;

    /// <summary>A copy (with its own parameter dictionary).</summary>
    public DistributionSettings Clone()
    {
        var c = (DistributionSettings)MemberwiseClone();
        c.Parameters = new Dictionary<string, string>(Parameters);
        return c;
    }

    /// <summary>A parameter's text, or the default.</summary>
    public string Parameter(string key, string fallback) => Parameters.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>The plot types.</summary>
public enum PlotKind
{
    /// <summary>Histogram with adjustable bins.</summary>
    Histogram,

    /// <summary>Box plot (modified, with outliers, or plain).</summary>
    BoxPlot,

    /// <summary>Scatter plot with the regression line.</summary>
    Scatter,

    /// <summary>Normal probability plot.</summary>
    NormalProbability,

    /// <summary>Dot plot.</summary>
    DotPlot,

    /// <summary>Bar chart of the counts of each category.</summary>
    BarChart,
}

/// <summary>Settings of the Plots tab.</summary>
public sealed class PlotSettings
{
    /// <summary>The plot type.</summary>
    public PlotKind Kind { get; set; } = PlotKind.Histogram;

    /// <summary>The list (x list for scatter plots).</summary>
    public string List { get; set; } = "L1";

    /// <summary>A second list: y for scatter plots, a second box for box plots (null for none).</summary>
    public string? List2 { get; set; } = "L2";

    /// <summary>The frequency list, or null.</summary>
    public string? Freq { get; set; }

    /// <summary>Histogram bin width as typed (empty for automatic).</summary>
    public string BinWidth { get; set; } = "";

    /// <summary>Histogram start as typed (empty for automatic).</summary>
    public string BinStart { get; set; } = "";

    /// <summary>Box plots: show outliers beyond 1.5 IQR as points (modified box plot).</summary>
    public bool Outliers { get; set; } = true;

    /// <summary>Scatter plots: draw the least squares line.</summary>
    public bool FitLine { get; set; } = true;

    /// <summary>A copy.</summary>
    public PlotSettings Clone() => (PlotSettings)MemberwiseClone();
}
