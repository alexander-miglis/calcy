using Calcy.Core;
using Calcy.Graphing;
using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>The result of the Tests tab.</summary>
public sealed class TestOutcome
{
    /// <summary>Why nothing could be computed, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The test.</summary>
    public required TestInfo Info { get; init; }

    /// <summary>The test result (tests only).</summary>
    public TestResult? Test { get; init; }

    /// <summary>The interval (intervals only).</summary>
    public ConfidenceInterval? Interval { get; init; }

    /// <summary>"H₀: μ = 65   H₁: μ ≠ 65" (tests only).</summary>
    public string Hypotheses { get; init; } = "";

    /// <summary>"Reject H₀" or "Keep H₀" (tests), or the interval.</summary>
    public string Decision { get; init; } = "";

    /// <summary>The decision in plain words.</summary>
    public string Sentence { get; init; } = "";

    /// <summary>True when H₀ is rejected at α.</summary>
    public bool Rejects { get; init; }

    /// <summary>The significance level used.</summary>
    public double Alpha { get; init; } = 0.05;

    /// <summary>The confidence level used (intervals).</summary>
    public double Level { get; init; } = 0.95;

    /// <summary>The tiles.</summary>
    public IReadOnlyList<Tile> Tiles { get; init; } = [];

    /// <summary>The distribution of the statistic with the rejection region (or the interval) shaded.</summary>
    public GraphSpec? Graph { get; init; }

    /// <summary>Why there is no picture, or a remark about it.</summary>
    public string? GraphNote { get; init; }

    /// <summary>Remarks about the data (rows skipped, ...).</summary>
    public string? Note { get; init; }

    /// <summary>The calculator expression for USE.</summary>
    public string? UseText { get; init; }

    /// <summary>The card.</summary>
    public required StatCard Card { get; init; }
}

/// <summary>
/// Runs the tests and intervals of the Tests tab: reads the fields (lists or summary statistics), calls
/// <see cref="Inference"/>, and words the decision at α in plain language with a picture of the rejection region.
/// </summary>
public static class TestRunner
{
    private sealed record Wording(string H0, string H1Symbol, string Subject, string Target, bool Tend = false);

    /// <summary>Runs the test or interval the settings describe on the data.</summary>
    public static TestOutcome Run(TestSettings s, DataSet data, CalcSession? session = null)
    {
        var info = TestCatalog.Info(s.Kind);
        try
        {
            return RunCore(s, info, data, session);
        }
        catch (Exception ex) when (ex is StatDataException or ArgumentException or InvalidOperationException)
        {
            var msg = Clean(ex.Message);
            return new TestOutcome { Info = info, Error = msg, Card = StatCard.Error(info.CardName, msg) };
        }
    }

    private static string Clean(string message)
    {
        int paren = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        if (paren > 0) message = message[..paren];
        return message.Length == 0 ? message : char.ToUpperInvariant(message[0]) + message[1..];
    }

    /// <summary>The symbol of an alternative: ≠, &lt; or &gt;.</summary>
    public static string Symbol(Alternative a) => a switch { Alternative.Less => "<", Alternative.Greater => ">", _ => "≠" };

    private static string AltArg(Alternative a, bool always = false) => a == Alternative.NotEqual && !always ? "" : $", \"{Symbol(a)}\"";

    private static string I(double v) => NumberText.Input(v);

    private static TestOutcome RunCore(TestSettings s, TestInfo info, DataSet data, CalcSession? session)
    {
        var fields = TestCatalog.Fields(s.Kind, s.Stats);
        bool stats = s.Stats && info.HasStats;
        var lists = data.Names;
        TestField F(string key) => fields.First(f => f.Key == key);
        string V(string key) => TestCatalog.Value(s, F(key), lists);
        double Num(string key) => Distributions.ReadNumber(V(key), F(key).Label, session);
        bool Flag(string key) => V(key).Equals("true", StringComparison.OrdinalIgnoreCase);
        string? Opt(string key) => V(key) is { Length: > 0 } v ? v : null;
        double alpha = Distributions.ReadNumber(s.Alpha, "α", session);
        if (!(alpha > 0 && alpha < 1)) throw new StatDataException("α must be between 0 and 1, for example 0.05");
        double level = Distributions.ReadNumber(s.Level, "the confidence level", session);
        if (level > 1 && level < 100) level /= 100;
        if (!(level > 0 && level < 1)) throw new StatDataException("The confidence level must be between 0 and 1 (or a percentage)");
        var alt = info.HasAlternative ? s.Alternative : Alternative.NotEqual;

        TestResult? r = null;
        ConfidenceInterval? ci = null;
        string? use = null;
        string? note = null;
        Wording? w = null;
        string subjectCi = "the mean";
        string titleLists = "";
        string sym = Symbol(alt);

        switch (s.Kind)
        {
            case TestKind.ZTest:
            {
                double mu0 = Num("mu0"), sigma = Num("sigma");
                if (stats)
                {
                    double xbar = Num("xbar"), n = Num("n");
                    r = Inference.ZTest(mu0, sigma, xbar, n, alt);
                    use = $"ztest({I(mu0)}, {I(sigma)}, {I(xbar)}, {I(n)}{AltArg(alt)})";
                    w = new($"μ = {D(mu0)}", $"μ {sym} {D(mu0)}", "the mean", D(mu0));
                }
                else
                {
                    var sm = data.Single(V("list"), Opt("freq"));
                    r = Inference.ZTest(mu0, sigma, sm.Values, alt, sm.Frequencies);
                    use = sm.Frequencies is null ? $"ztest({I(mu0)}, {I(sigma)}, {sm.Name}{AltArg(alt)})" : $"ztest({I(mu0)}, {I(sigma)}, {I(r.Value("xbar"))}, {I(r.Value("n"))}{AltArg(alt)})";
                    w = new($"μ = {D(mu0)}", $"μ {sym} {D(mu0)}", $"the mean of {sm.Name}", D(mu0));
                    note = Descriptives.Skipped(sm.Skipped);
                    titleLists = sm.Name;
                }
                break;
            }
            case TestKind.TTest:
            {
                double mu0 = Num("mu0");
                if (stats)
                {
                    double xbar = Num("xbar"), sx = Num("sx"), n = Num("n");
                    r = Inference.TTest(mu0, xbar, sx, n, alt);
                    use = $"ttest({I(mu0)}, {I(xbar)}, {I(sx)}, {I(n)}{AltArg(alt)})";
                    w = new($"μ = {D(mu0)}", $"μ {sym} {D(mu0)}", "the mean", D(mu0));
                }
                else
                {
                    var sm = data.Single(V("list"), Opt("freq"));
                    r = Inference.TTest(mu0, sm.Values, alt, sm.Frequencies);
                    use = sm.Frequencies is null ? $"ttest({I(mu0)}, {sm.Name}{AltArg(alt)})" : $"ttest({I(mu0)}, {I(r.Value("xbar"))}, {I(r.Value("Sx"))}, {I(r.Value("n"))}{AltArg(alt)})";
                    w = new($"μ = {D(mu0)}", $"μ {sym} {D(mu0)}", $"the mean of {sm.Name}", D(mu0));
                    note = Descriptives.Skipped(sm.Skipped);
                    titleLists = sm.Name;
                }
                break;
            }
            case TestKind.TwoSampleZTest:
            {
                double s1 = Num("sigma1"), s2 = Num("sigma2");
                if (stats)
                    r = Inference.TwoSampleZTest(s1, s2, Num("xbar1"), Num("n1"), Num("xbar2"), Num("n2"), alt);
                else
                {
                    var a = data.Single(V("list1"));
                    var b = data.Single(V("list2"));
                    r = Inference.TwoSampleZTest(s1, s2, a.Values, b.Values, alt);
                    titleLists = $"{a.Name}, {b.Name}";
                }
                w = TwoMeans(sym, stats, V("list1"), V("list2"));
                break;
            }
            case TestKind.TwoSampleTTest:
            {
                bool pooled = Flag("pooled");
                if (stats)
                    r = Inference.TwoSampleTTest(Num("xbar1"), Num("sx1"), Num("n1"), Num("xbar2"), Num("sx2"), Num("n2"), alt, pooled);
                else
                {
                    var a = data.Single(V("list1"));
                    var b = data.Single(V("list2"));
                    r = Inference.TwoSampleTTest(a.Values, b.Values, alt, pooled);
                    use = pooled ? $"ttest2({a.Name}, {b.Name}{AltArg(alt, always: true)}, true)" : $"ttest2({a.Name}, {b.Name}{AltArg(alt)})";
                    titleLists = $"{a.Name}, {b.Name}";
                }
                w = TwoMeans(sym, stats, V("list1"), V("list2"));
                break;
            }
            case TestKind.PairedTTest:
            {
                var p = data.Pair(V("list1"), V("list2"));
                double mu0 = Num("mu0");
                r = Inference.PairedTTest(p.X, p.Y, mu0, alt);
                use = mu0 == 0 && alt == Alternative.NotEqual ? $"pairedttest({p.XName}, {p.YName})" : $"pairedttest({p.XName}, {p.YName}, {I(mu0)}{AltArg(alt)})";
                w = new($"μd = {D(mu0)}", $"μd {sym} {D(mu0)}", $"the mean difference {p.XName} − {p.YName}", D(mu0));
                note = Descriptives.Skipped(p.Skipped, pairs: true);
                titleLists = $"{p.XName}, {p.YName}";
                break;
            }
            case TestKind.OnePropZTest:
            {
                double p0 = Num("p0"), x = Num("x"), n = Num("n");
                r = Inference.OnePropZTest(p0, x, n, alt);
                use = $"propztest({I(p0)}, {I(x)}, {I(n)}{AltArg(alt)})";
                w = new($"p = {D(p0)}", $"p {sym} {D(p0)}", "the proportion", D(p0));
                break;
            }
            case TestKind.TwoPropZTest:
            {
                double x1 = Num("x1"), n1 = Num("n1"), x2 = Num("x2"), n2 = Num("n2");
                r = Inference.TwoPropZTest(x1, n1, x2, n2, alt);
                use = $"propztest2({I(x1)}, {I(n1)}, {I(x2)}, {I(n2)}{AltArg(alt)})";
                w = new("p₁ = p₂", $"p₁ {sym} p₂", "the first proportion", "the second");
                break;
            }
            case TestKind.ChiSquareGof:
            {
                var o = data.Single(V("observed"), role: "the observed counts");
                var e = data.Single(V("expected"), role: "the expected counts");
                if (o.Values.Length != e.Values.Length)
                    throw new StatDataException($"{o.Name} has {o.Values.Length} counts and {e.Name} has {e.Values.Length}; they must match");
                double? df = V("df").Trim().Length == 0 ? null : Num("df");
                r = Inference.ChiSquareGoodnessOfFit(o.Values, e.Values, df);
                use = df is null ? $"chi2gof({o.Name}, {e.Name})" : $"chi2gof({o.Name}, {e.Name}, {I(df.Value)})";
                w = new("the counts follow the expected counts", "the counts do not follow the expected counts", "", "");
                titleLists = $"{o.Name}, {e.Name}";
                break;
            }
            case TestKind.ChiSquareTwoWay:
            {
                var names = Names(V("lists"));
                if (names.Count < 2) throw new StatDataException("Pick at least 2 lists: each list is one column of the table of counts");
                var cols = names.Select(n => data.Single(n).Values).ToList();
                int rows = cols[0].Length;
                if (cols.Any(c => c.Length != rows)) throw new StatDataException("Every column of the table needs the same number of counts");
                if (rows < 2) throw new StatDataException("The table needs at least 2 rows");
                var table = new double[rows, cols.Count];
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < cols.Count; j++) table[i, j] = cols[j][i];
                r = Inference.ChiSquareTwoWay(table);
                use = "chi2test([" + string.Join(", ", Enumerable.Range(0, rows).Select(i => "[" + string.Join(", ", cols.Select(c => I(c[i]))) + "]")) + "])";
                w = new("the row and column variables are independent", "the row and column variables are related", "", "");
                titleLists = string.Join(", ", names);
                break;
            }
            case TestKind.TwoSampleFTest:
            {
                if (stats)
                    r = Inference.TwoSampleFTest(Num("sx1"), Num("n1"), Num("sx2"), Num("n2"), alt);
                else
                {
                    var a = data.Single(V("list1"));
                    var b = data.Single(V("list2"));
                    r = Inference.TwoSampleFTest(a.Values, b.Values, alt);
                    use = $"ftest2({a.Name}, {b.Name}{AltArg(alt)})";
                    titleLists = $"{a.Name}, {b.Name}";
                }
                w = new("σ₁ = σ₂", $"σ₁ {sym} σ₂", stats ? "the first standard deviation" : $"the standard deviation of {V("list1")}", stats ? "the second" : $"that of {V("list2")}");
                break;
            }
            case TestKind.LinRegTTest:
            {
                var p = data.Pair(V("xlist"), V("ylist"), Opt("freq"));
                r = Inference.LinRegTTest(p.X, p.Y, alt, p.Frequencies);
                use = p.Frequencies is null ? $"linregttest({p.XName}, {p.YName}{AltArg(alt)})" : null;
                w = new("β = 0 and ρ = 0", $"β {sym} 0", $"the slope of {p.YName} on {p.XName}", "0");
                note = Descriptives.Skipped(p.Skipped, pairs: true);
                titleLists = $"{p.XName}, {p.YName}";
                break;
            }
            case TestKind.Anova:
            {
                var names = Names(V("lists"));
                var groups = data.Groups(names);
                r = Inference.OneWayAnova(groups);
                use = $"anova({string.Join(", ", names)})";
                w = new("all the group means are equal", "the group means are not all equal", "", "");
                titleLists = string.Join(", ", names);
                break;
            }
            case TestKind.ZInterval:
            {
                double sigma = Num("sigma");
                if (stats)
                {
                    double xbar = Num("xbar"), n = Num("n");
                    ci = Inference.ZInterval(sigma, xbar, n, level);
                    use = $"zinterval({I(sigma)}, {I(xbar)}, {I(n)}, {I(level)})";
                }
                else
                {
                    var sm = data.Single(V("list"), Opt("freq"));
                    ci = Inference.ZInterval(sigma, sm.Values, level, sm.Frequencies);
                    use = sm.Frequencies is null ? $"zinterval({I(sigma)}, {sm.Name}, {I(level)})" : null;
                    subjectCi = $"the mean of {sm.Name}";
                    note = Descriptives.Skipped(sm.Skipped);
                    titleLists = sm.Name;
                }
                break;
            }
            case TestKind.TInterval:
            {
                if (stats)
                {
                    double xbar = Num("xbar"), sx = Num("sx"), n = Num("n");
                    ci = Inference.TInterval(xbar, sx, n, level);
                    use = $"tinterval({I(xbar)}, {I(sx)}, {I(n)}, {I(level)})";
                }
                else
                {
                    var sm = data.Single(V("list"), Opt("freq"));
                    ci = Inference.TInterval(sm.Values, level, sm.Frequencies);
                    use = sm.Frequencies is null ? $"tinterval({sm.Name}, {I(level)})" : null;
                    subjectCi = $"the mean of {sm.Name}";
                    note = Descriptives.Skipped(sm.Skipped);
                    titleLists = sm.Name;
                }
                break;
            }
            case TestKind.TwoSampleZInterval:
            {
                double s1 = Num("sigma1"), s2 = Num("sigma2");
                if (stats) ci = Inference.TwoSampleZInterval(s1, s2, Num("xbar1"), Num("n1"), Num("xbar2"), Num("n2"), level);
                else
                {
                    var a = data.Single(V("list1"));
                    var b = data.Single(V("list2"));
                    ci = Inference.TwoSampleZInterval(s1, s2, a.Values, b.Values, level);
                    titleLists = $"{a.Name}, {b.Name}";
                }
                subjectCi = "the difference of the means μ₁ − μ₂";
                break;
            }
            case TestKind.TwoSampleTInterval:
            {
                bool pooled = Flag("pooled");
                if (stats) ci = Inference.TwoSampleTInterval(Num("xbar1"), Num("sx1"), Num("n1"), Num("xbar2"), Num("sx2"), Num("n2"), level, pooled);
                else
                {
                    var a = data.Single(V("list1"));
                    var b = data.Single(V("list2"));
                    ci = Inference.TwoSampleTInterval(a.Values, b.Values, level, pooled);
                    use = pooled ? $"tinterval2({a.Name}, {b.Name}, {I(level)}, true)" : $"tinterval2({a.Name}, {b.Name}, {I(level)})";
                    titleLists = $"{a.Name}, {b.Name}";
                }
                subjectCi = "the difference of the means μ₁ − μ₂";
                break;
            }
            case TestKind.OnePropZInterval:
            {
                double x = Num("x"), n = Num("n");
                ci = Inference.OnePropZInterval(x, n, level);
                use = $"propzint({I(x)}, {I(n)}, {I(level)})";
                subjectCi = "the proportion";
                break;
            }
            case TestKind.TwoPropZInterval:
            {
                double x1 = Num("x1"), n1 = Num("n1"), x2 = Num("x2"), n2 = Num("n2");
                ci = Inference.TwoPropZInterval(x1, n1, x2, n2, level);
                use = $"propzint2({I(x1)}, {I(n1)}, {I(x2)}, {I(n2)}, {I(level)})";
                subjectCi = "the difference of the proportions p₁ − p₂";
                break;
            }
            case TestKind.LinRegTInterval:
            {
                var p = data.Pair(V("xlist"), V("ylist"), Opt("freq"));
                ci = Inference.LinRegTInterval(p.X, p.Y, level, p.Frequencies);
                subjectCi = $"the slope of {p.YName} on {p.XName}";
                note = Descriptives.Skipped(p.Skipped, pairs: true);
                titleLists = $"{p.XName}, {p.YName}";
                break;
            }
            case TestKind.SignTest:
            case TestKind.WilcoxonSignedRank:
            {
                var sm = data.Single(V("list"));
                double m0 = Num("m0");
                r = s.Kind == TestKind.SignTest ? Inference.SignTest(sm.Values, m0, alt) : Inference.WilcoxonSignedRank(sm.Values, m0, alt);
                w = new($"median = {D(m0)}", $"median {sym} {D(m0)}", $"the median of {sm.Name}", D(m0));
                note = Descriptives.Skipped(sm.Skipped);
                titleLists = sm.Name;
                break;
            }
            case TestKind.MannWhitney:
            {
                var a = data.Single(V("list1"));
                var b = data.Single(V("list2"));
                r = Inference.MannWhitneyU(a.Values, b.Values, alt);
                w = new("both lists come from the same distribution",
                    alt == Alternative.NotEqual ? "one list tends to be larger" : $"{a.Name} tends to be {(alt == Alternative.Less ? "smaller" : "larger")}",
                    $"the values of {a.Name}", $"those of {b.Name}", Tend: true);
                titleLists = $"{a.Name}, {b.Name}";
                break;
            }
            case TestKind.KruskalWallis:
            {
                var names = Names(V("lists"));
                r = Inference.KruskalWallis(data.Groups(names));
                w = new("all the groups come from the same distribution", "the groups do not all come from the same distribution", "", "");
                titleLists = string.Join(", ", names);
                break;
            }
            case TestKind.Spearman:
            case TestKind.Kendall:
            {
                var p = data.Pair(V("xlist"), V("ylist"));
                bool sp = s.Kind == TestKind.Spearman;
                r = sp ? Inference.SpearmanCorrelation(p.X, p.Y, alt) : Inference.KendallTau(p.X, p.Y, alt);
                string param = sp ? "ρs" : "τ";
                w = new($"{param} = 0", $"{param} {sym} 0", $"the rank correlation of {p.XName} and {p.YName}", "0");
                note = Descriptives.Skipped(p.Skipped, pairs: true);
                titleLists = $"{p.XName}, {p.YName}";
                break;
            }
        }

        string title = (info.CardName + " " + titleLists.Replace(", ", ",")).Trim();
        var settings = s.Clone();
        return ci is not null
            ? IntervalOutcome(info, ci, level, subjectCi, use, note, title, settings)
            : TestOutcomeFor(info, r!, alt, alpha, w!, use, note, title, settings);
    }

    private static Wording TwoMeans(string sym, bool stats, string list1, string list2) =>
        new("μ₁ = μ₂", $"μ₁ {sym} μ₂", stats ? "the first mean" : $"the mean of {list1}", stats ? "the second" : $"the mean of {list2}");

    private static List<string> Names(string csv) => [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()];

    private static string D(double v) => NumberText.Display(v, 8);

    /// <summary>The H₁ claim in words: "the mean of L2 differs from 65".</summary>
    private static string Claim(Wording w, Alternative alt, TestInfo info)
    {
        if (w.Subject.Length == 0) return w.H1Symbol;
        string verb = w.Tend
            ? alt switch { Alternative.Less => "tend to be smaller than", Alternative.Greater => "tend to be larger than", _ => "tend to differ from" }
            : alt switch { Alternative.Less => "is less than", Alternative.Greater => "is greater than", _ => "differs from" };
        return $"{w.Subject} {verb} {w.Target}";
    }

    /// <summary>The decision at α in plain words.</summary>
    public static string DecisionSentence(double p, double alpha, string claim, bool rejects)
    {
        string ps = NumberText.Display(p, 4), a = NumberText.Display(alpha, 4);
        return rejects
            ? $"p = {ps} is at most α = {a}, so reject H₀: the data give evidence that {claim}."
            : $"p = {ps} is above α = {a}, so do not reject H₀: the data do not give enough evidence that {claim}.";
    }

    private static string StatisticLabel(string name) => name switch
    {
        "chi2" => "χ²",
        "rho" => "ρs",
        "tau" => "τ",
        _ => name,
    };

    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal) { "mu0", "sigma", "sigma1", "sigma2", "p0", "x", "tieCorrection", "t" };

    private static string ValueLabel(string key) => key switch
    {
        "xbar" => "x̄",
        "xbar1" => "x̄₁",
        "xbar2" => "x̄₂",
        "Sx1" => "Sx₁",
        "Sx2" => "Sx₂",
        "n1" => "n₁",
        "n2" => "n₂",
        "phat" => "p̂",
        "p1hat" => "p̂₁",
        "p2hat" => "p̂₂",
        "r2" => "r²",
        "dbar" => "d̄",
        "U1" => "U₁",
        "U2" => "U₂",
        "R1" => "R₁",
        _ => key,
    };

    private static TestOutcome TestOutcomeFor(TestInfo info, TestResult r, Alternative alt, double alpha, Wording w, string? use, string? note, string title, TestSettings settings)
    {
        bool rejects = r.PValue <= alpha;
        string sentence = DecisionSentence(r.PValue, alpha, Claim(w, alt, info), rejects);
        string hypotheses = w.Subject.Length > 0 ? $"H₀: {w.H0}   H₁: {w.H1Symbol}" : $"H₀: {w.H0}";
        string decision = rejects ? "Reject H₀" : "Keep H₀";
        string statLabel = StatisticLabel(r.StatisticName);
        string Dsp(double v) => NumberText.Display(v);
        var tiles = new List<Tile>
        {
            new(statLabel, Dsp(r.Statistic), "The test statistic"),
            new("p", NumberText.Probability(r.PValue), "The chance of a statistic at least this extreme if H₀ were true"),
        };
        string? df = Df(r);
        if (df is not null) tiles.Add(new Tile("df", df, "Degrees of freedom"));
        tiles.Add(new Tile($"at α = {NumberText.Display(alpha, 4)}", decision, rejects ? "p ≤ α: the result is significant" : "p > α: the result is not significant", Highlight: rejects));
        var extras = r.Values.Where(kv => !Hidden.Contains(kv.Key) && !(info.Kind == TestKind.PairedTTest && kv.Key is "xbar" or "Sx")).Take(4).ToList();
        foreach (var (k, v) in extras) tiles.Add(new Tile(ValueLabel(k), NumberText.Display(v)));

        string C(double v) => NumberText.Display(v, NumberText.CardDigits);
        var lines = new List<CardLine> { new(statLabel, C(r.Statistic)), new("p", C(r.PValue)) };
        if (df is not null) lines.Add(new CardLine("df", Df(r, NumberText.CardDigits)!));
        foreach (var (k, v) in extras.Take(5)) lines.Add(new CardLine(ValueLabel(k), C(v)));
        var graph = StatisticGraph(r, alpha, out var graphNote);
        string headline = $"{hypotheses}\n{decision} at α = {NumberText.Display(alpha, 4)}";
        use ??= "{" + string.Join(", ", new[] { $"{San(r.StatisticName)} = {I(r.Statistic)}", $"p = {I(r.PValue)}" }.Concat(r.DegreesOfFreedom is double d ? [$"df = {I(d)}"] : [])) + "}";
        var card = new StatCard
        {
            Title = title,
            Headline = headline,
            Lines = lines,
            Note = sentence,
            CopyText = StatCard.PlainText(r.TestName + (title.Length > info.CardName.Length + 1 ? " on " + title[(info.CardName.Length + 1)..] : ""), hypotheses, lines, sentence),
            Latex = CardLatex.Aligned(lines.Select(l => (l.Label, Descriptives.LatexValue(l.Value)))),
            UseText = use,
            Recipe = new StatRecipe { Tab = StatTab.Tests, Test = settings },
        };
        return new TestOutcome
        {
            Info = info,
            Test = r,
            Hypotheses = hypotheses,
            Decision = decision,
            Sentence = sentence,
            Rejects = rejects,
            Alpha = alpha,
            Tiles = tiles,
            Graph = graph,
            GraphNote = graphNote,
            Note = note,
            UseText = use,
            Card = card,
        };
    }

    private static string San(string name) => name switch { "chi2" => "chi2", "rho" => "rho", "tau" => "tau", _ => name };

    private static string? Df(TestResult r, int digits = NumberText.TileDigits)
    {
        if (r.DegreesOfFreedom is not double d) return null;
        string a = NumberText.Display(d, digits);
        return r.DenominatorDegreesOfFreedom is double d2 ? $"{a}, {NumberText.Display(d2, digits)}" : a;
    }

    private static TestOutcome IntervalOutcome(TestInfo info, ConfidenceInterval ci, double level, string subject, string? use, string? note, string title, TestSettings settings)
    {
        string pct = NumberText.Display(level * 100, 6) + "%";
        string Dsp(double v) => NumberText.Display(v);
        string sentence = $"With {pct} confidence, {subject} lies between {NumberText.Display(ci.Lower, 6)} and {NumberText.Display(ci.Upper, 6)}.";
        var tiles = new List<Tile>
        {
            new("lower", Dsp(ci.Lower), "Lower end of the interval"),
            new("upper", Dsp(ci.Upper), "Upper end of the interval"),
            new("estimate", Dsp(ci.PointEstimate), "The point estimate in the middle"),
            new("margin", Dsp(ci.MarginOfError), "Half the width: critical value times standard error"),
        };
        if (ci.DegreesOfFreedom is double df) tiles.Add(new Tile("df", Dsp(df), "Degrees of freedom"));
        tiles.Add(new Tile("level", pct, "The confidence level", Highlight: true));
        foreach (var (k, v) in ci.Values.Where(kv => !Hidden.Contains(kv.Key)).Take(4)) tiles.Add(new Tile(ValueLabel(k), Dsp(v)));
        string C(double v) => NumberText.Display(v, NumberText.CardDigits);
        var lines = new List<CardLine> { new("lower", C(ci.Lower)), new("upper", C(ci.Upper)), new("estimate", C(ci.PointEstimate)), new("margin", C(ci.MarginOfError)) };
        if (ci.DegreesOfFreedom is double d2) lines.Add(new CardLine("df", C(d2)));
        string headline = $"({NumberText.Display(ci.Lower, 7)}, {NumberText.Display(ci.Upper, 7)})";
        use ??= $"{{{I(ci.Lower)}, {I(ci.Upper)}}}";
        var card = new StatCard
        {
            Title = title,
            Headline = headline,
            Lines = lines,
            Note = sentence,
            CopyText = StatCard.PlainText($"{ci.Name}, {pct}", headline, lines, sentence),
            Latex = $"\\left({NumberText.Latex(ci.Lower)},\\ {NumberText.Latex(ci.Upper)}\\right)",
            UseText = use,
            Recipe = new StatRecipe { Tab = StatTab.Tests, Test = settings },
        };
        return new TestOutcome
        {
            Info = info,
            Interval = ci,
            Decision = headline,
            Sentence = sentence,
            Level = level,
            Tiles = tiles,
            Graph = IntervalGraph(ci, level),
            Note = note,
            UseText = use,
            Card = card,
        };
    }

    // ------------------------------------------------------------------ pictures

    /// <summary>
    /// The distribution of the test statistic under H₀ with the rejection region at α shaded (colour slot 1) and the
    /// observed statistic marked. Null (with a note) for statistics without a standard reference curve.
    /// </summary>
    public static GraphSpec? StatisticGraph(TestResult r, double alpha, out string? note)
    {
        note = null;
        double stat = r.Statistic;
        string formula;
        Func<double, double> pdf;
        double x0, x1;
        var regions = new List<(double A, double B)>();
        switch (r.StatisticName)
        {
            case "z":
            case "t":
            case "rho" when r.Values.ContainsKey("t") && r.DegreesOfFreedom is not null:
            {
                bool isT = r.StatisticName != "z";
                if (r.StatisticName == "rho") stat = r.Values["t"];
                double df = r.DegreesOfFreedom ?? double.PositiveInfinity;
                if (isT && !(df > 0)) return null;
                ContinuousDistribution d = isT && double.IsFinite(df) ? new StudentTDistribution(df) : NormalDistribution.Standard;
                formula = isT && double.IsFinite(df) ? Distributions.TFormula(df, 0, 1) : Distributions.NormalFormula(0, 1);
                pdf = d.Pdf;
                double crit2 = d.InverseSurvival(alpha / 2), crit1 = d.InverseSurvival(alpha);
                double half = Math.Max(4, Math.Max(r.Alternative == Alternative.NotEqual ? crit2 : crit1, 0) + 1);
                // The statistic widens the window a little, but a far-out value is reported instead of squashing the curve.
                if (double.IsFinite(stat) && Math.Abs(stat) + 0.8 <= half * 1.6) half = Math.Max(half, Math.Abs(stat) + 0.8);
                (x0, x1) = (-half, half);
                switch (r.Alternative)
                {
                    case Alternative.Less: regions.Add((x0, -crit1)); break;
                    case Alternative.Greater: regions.Add((crit1, x1)); break;
                    default: regions.Add((x0, -crit2)); regions.Add((crit2, x1)); break;
                }
                break;
            }
            case "chi2":
            case "H" when r.DegreesOfFreedom is not null:
            {
                double df = r.DegreesOfFreedom ?? 1;
                var d = new ChiSquaredDistribution(df);
                var p = new Dictionary<string, double> { ["df"] = df };
                formula = Distributions.PdfFormula(DistributionKind.ChiSquared, p)!;
                pdf = d.Pdf;
                double crit = d.InverseSurvival(alpha);
                x0 = 0;
                x1 = Math.Max(d.InverseCdf(0.999), crit * 1.2);
                if (double.IsFinite(stat) && stat * 1.12 <= x1 * 1.6) x1 = Math.Max(x1, stat * 1.12);
                regions.Add((crit, x1));
                break;
            }
            case "F" when r.DegreesOfFreedom is double d1 && r.DenominatorDegreesOfFreedom is double d2:
            {
                var d = new FDistribution(d1, d2);
                formula = Distributions.PdfFormula(DistributionKind.F, new Dictionary<string, double> { ["df1"] = d1, ["df2"] = d2 })!;
                pdf = d.Pdf;
                x0 = 0;
                x1 = Math.Max(d.InverseCdf(0.995), d.InverseSurvival(alpha / 2) * 1.15);
                if (double.IsFinite(stat) && stat * 1.12 <= x1 * 1.6) x1 = Math.Max(x1, stat * 1.12);
                switch (r.Alternative)
                {
                    case Alternative.Less: regions.Add((0, d.InverseCdf(alpha))); break;
                    case Alternative.Greater: regions.Add((d.InverseSurvival(alpha), x1)); break;
                    default: regions.Add((0, d.InverseCdf(alpha / 2))); regions.Add((d.InverseSurvival(alpha / 2), x1)); break;
                }
                break;
            }
            default:
                note = $"The {StatisticLabel(r.StatisticName)} statistic has no standard curve to draw; the p-value comes from its exact or approximate distribution.";
                return null;
        }
        double pad = (x1 - x0) * 0.03;
        // Curves that start at 0 get room on the left, so the grapher's y labels sit beside the axis, not on the curve.
        double left = x0 == 0 ? -(x1 - x0) * 0.09 : x0;
        double ymax = Distributions.CurveHeight(pdf, x0 == 0 ? (x1 - x0) * 0.01 : x0, x1);
        var view = new Viewport(left, x1 + pad, -0.2 * ymax, 1.2 * ymax);
        var annotations = new List<GraphAnnotation>();
        foreach (var (a, b) in regions)
        {
            double lo = Math.Max(a, x0 == 0 ? 1e-9 : x0), hi = Math.Min(b, x1 + pad);
            annotations.AddRange(GraphSpec.Shade(GraphSpec.AreaUnder(x => Math.Min(pdf(x), ymax * 4), lo, hi, 100), 1, layers: 3));
        }
        string label = StatisticLabel(r.StatisticName == "rho" ? "t" : r.StatisticName);
        string region = RegionText(label, regions, x0, x1 + pad);
        bool inside = r.PValue <= alpha;
        note = $"Shaded: the rejection region at α = {NumberText.Display(alpha, 4)}, {region}. The observed {label} = {NumberText.Display(stat, 4)} is {(inside ? "inside it" : "outside it")}.";
        if (double.IsFinite(stat) && stat >= view.XMin && stat <= view.XMax)
        {
            double h = Math.Min(pdf(stat), ymax * 1.1);
            if (!double.IsFinite(h)) h = ymax;
            annotations.Add(new GraphAnnotation(AnnotationKind.Path, [new Point2(stat, 0), new Point2(stat, Math.Max(h, ymax * 0.06))], 0));
            annotations.Add(new GraphAnnotation(AnnotationKind.Point, [new Point2(stat, h)], 0));
        }
        else note += " It lies off the picture, far in the tail.";
        return new GraphSpec([new GraphLine(formula, 0)], view, annotations);
    }

    /// <summary>"|t| ≥ 2.262", "t ≤ -1.833", "χ² ≥ 7.815" or "F ≤ 0.25 or F ≥ 4.03".</summary>
    private static string RegionText(string label, List<(double A, double B)> regions, double x0, double x1)
    {
        static string N(double v) => NumberText.Display(v, 4);
        if (regions.Count == 2 && Math.Abs(regions[0].B + regions[1].A) < 1e-9 * Math.Max(1, Math.Abs(regions[1].A)))
            return $"|{label}| ≥ {N(regions[1].A)}";
        return string.Join(" or ", regions.Select(g => g.A <= x0 + 1e-12 ? $"{label} ≤ {N(g.B)}" : $"{label} ≥ {N(g.A)}"));
    }

    /// <summary>The sampling distribution around the estimate with the interval shaded.</summary>
    public static GraphSpec? IntervalGraph(ConfidenceInterval ci, double level)
    {
        if (!(ci.MarginOfError > 0) || !double.IsFinite(ci.MarginOfError)) return null;
        bool isT = ci.DegreesOfFreedom is double df && double.IsFinite(df) && df > 0;
        double tail = (1 - level) / 2;
        double crit = isT ? new StudentTDistribution(ci.DegreesOfFreedom!.Value).InverseSurvival(tail) : NormalDistribution.Standard.InverseSurvival(tail);
        double se = ci.MarginOfError / crit;
        double m = ci.PointEstimate;
        string formula;
        Func<double, double> pdf;
        if (isT)
        {
            var t = new StudentTDistribution(ci.DegreesOfFreedom!.Value);
            formula = Distributions.TFormula(ci.DegreesOfFreedom!.Value, m, se);
            pdf = x => t.Pdf((x - m) / se) / se;
        }
        else
        {
            var n = new NormalDistribution(m, se);
            formula = Distributions.NormalFormula(m, se);
            pdf = n.Pdf;
        }
        double half = Math.Max(4 * se, ci.MarginOfError * 1.35);
        double ymax = pdf(m);
        var view = new Viewport(m - half, m + half, -0.2 * ymax, 1.2 * ymax);
        var annotations = new List<GraphAnnotation>
        {
            new(AnnotationKind.Shade, GraphSpec.AreaUnder(pdf, ci.Lower, ci.Upper, 120), 0),
            new(AnnotationKind.Path, [new Point2(m, 0), new Point2(m, ymax)], 0),
            new(AnnotationKind.Point, [new Point2(ci.Lower, 0), new Point2(ci.Upper, 0)], 0),
        };
        return new GraphSpec([new GraphLine(formula, 0)], view, annotations);
    }
}
