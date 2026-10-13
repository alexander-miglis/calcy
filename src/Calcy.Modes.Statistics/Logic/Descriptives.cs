using System.Globalization;
using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>A labelled value on the analysis panel.</summary>
/// <param name="Label">The label ("mean x̄").</param>
/// <param name="Value">The formatted value.</param>
/// <param name="Tip">A tooltip that explains the value.</param>
/// <param name="Highlight">Draw the tile in the selection colour (a decision to reject, say).</param>
public sealed record Tile(string Label, string Value, string Tip = "", bool Highlight = false);

/// <summary>The result of the 1-Var tab.</summary>
public sealed class OneVarOutcome
{
    /// <summary>Why nothing could be computed, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The statistics.</summary>
    public OneVarStats? Stats { get; init; }

    /// <summary>The tiles.</summary>
    public IReadOnlyList<Tile> Tiles { get; init; } = [];

    /// <summary>The box plot.</summary>
    public BoxPlotStats? Box { get; init; }

    /// <summary>The box plot scene.</summary>
    public ChartScene Scene { get; init; } = ChartScene.Empty("");

    /// <summary>Remarks (rows skipped, ...).</summary>
    public string? Note { get; init; }

    /// <summary>The card.</summary>
    public required StatCard Card { get; init; }
}

/// <summary>The result of the 2-Var tab.</summary>
public sealed class TwoVarOutcome
{
    /// <summary>Why nothing could be computed, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The statistics.</summary>
    public TwoVarStats? Stats { get; init; }

    /// <summary>The tiles.</summary>
    public IReadOnlyList<Tile> Tiles { get; init; } = [];

    /// <summary>The scatter plot (drawn by the grapher).</summary>
    public GraphSpec? Graph { get; init; }

    /// <summary>The scatter plot for many points (drawn by the statistics chart), or null.</summary>
    public ChartScene? Scene { get; init; }

    /// <summary>Remarks (rows skipped, ...).</summary>
    public string? Note { get; init; }

    /// <summary>The card.</summary>
    public required StatCard Card { get; init; }
}

/// <summary>One- and two-variable statistics for the tabs and their cards.</summary>
public static class Descriptives
{
    /// <summary>The quartile methods in menu order with their names.</summary>
    public static readonly IReadOnlyList<(QuartileMethod Method, string Name)> QuartileMethods =
    [
        (QuartileMethod.TI, "TI / Casio"),
        (QuartileMethod.TukeyHinges, "Tukey hinges"),
        (QuartileMethod.Inclusive, "Excel inclusive"),
        (QuartileMethod.Exclusive, "Excel exclusive"),
        (QuartileMethod.CasioOnData, "Casio OnData"),
    ];

    /// <summary>1-Var Stats of a list (with frequencies), its modified box plot and the card.</summary>
    public static OneVarOutcome OneVar(DataSet data, OneVarSettings s)
    {
        string title = "1-VAR " + s.List + (s.Freq is null ? "" : " FREQ " + s.Freq);
        Sample sample;
        OneVarStats st;
        try
        {
            sample = data.Single(s.List, s.Freq);
            st = Descriptive.OneVar(sample.Values, sample.Frequencies, s.Method);
        }
        catch (Exception ex) when (ex is StatDataException or ArgumentException)
        {
            return new OneVarOutcome { Error = ex.Message, Card = StatCard.Error(title, ex.Message), Scene = ChartScene.Empty(ex.Message) };
        }
        string D(double v) => NumberText.Display(v);
        string mode = ModeText(st, D);
        var tiles = new List<Tile>
        {
            new("n", NumberText.Count(st.N), "Number of values (the sum of the frequencies)"),
            new("mean x̄", D(st.Mean), "The average"),
            new("Sx", D(st.SampleStdDev), "Sample standard deviation (divides by n - 1)"),
            new("σx", D(st.PopulationStdDev), "Population standard deviation (divides by n)"),
            new("Σx", D(st.Sum), "Sum of the values"),
            new("Σx²", D(st.SumOfSquares), "Sum of the squares"),
            new("min", D(st.Min), "Smallest value"),
            new("max", D(st.Max), "Largest value"),
            new("Q1", D(st.Q1), "First quartile (" + MethodName(s.Method) + ")"),
            new("median", D(st.Median), "The middle value"),
            new("Q3", D(st.Q3), "Third quartile (" + MethodName(s.Method) + ")"),
            new("IQR", D(st.IQR), "Interquartile range Q3 - Q1"),
            new("range", D(st.Range), "max - min"),
            new("mode", mode, "The most frequent value(s)"),
            new("skewness", D(st.Skewness), "Sample skewness: 0 for symmetric data, positive for a long right tail"),
            new("kurtosis", D(st.ExcessKurtosis), "Sample excess kurtosis: 0 for normal data"),
        };
        var box = BoxPlot.Compute(s.List, sample.Values, sample.Frequencies, s.Method, modified: true);
        string C(double v) => NumberText.Display(v, NumberText.CardDigits);
        var lines = new List<CardLine>
        {
            new("x̄", C(st.Mean)), new("Sx", C(st.SampleStdDev)), new("σx", C(st.PopulationStdDev)), new("n", NumberText.Count(st.N)),
            new("Σx", C(st.Sum)), new("Σx²", C(st.SumOfSquares)),
            new("min", C(st.Min)), new("Q1", C(st.Q1)), new("median", C(st.Median)), new("Q3", C(st.Q3)), new("max", C(st.Max)),
        };
        if (mode != "none") lines.Add(new CardLine("mode", ModeText(st, C)));
        string? note = Skipped(sample.Skipped);
        if (box.Outliers.Count > 0)
            note = Join(note, $"Outliers beyond 1.5 IQR: {string.Join(", ", box.Outliers.Distinct().Take(8).Select(D))}{(box.Outliers.Distinct().Count() > 8 ? ", ..." : "")}");
        if (s.Method != QuartileMethod.TI) note = Join(note, "Quartiles: " + MethodName(s.Method));
        var card = new StatCard
        {
            Title = title,
            Lines = lines,
            Note = note,
            CopyText = StatCard.PlainText("1-Var Stats " + s.List + (s.Freq is null ? "" : ", frequencies " + s.Freq), null, lines, note),
            Latex = CardLatex.Aligned(lines.Select(l => (l.Label, LatexValue(l.Value)))),
            UseText = s.Freq is null ? $"onevar({s.List})" : $"onevar({s.List}, {s.Freq})",
            Recipe = new StatRecipe { Tab = StatTab.OneVar, OneVar = s.Clone() },
        };
        return new OneVarOutcome
        {
            Stats = st,
            Tiles = tiles,
            Box = box,
            Scene = ChartBuilder.BoxScene([box], data.HeaderOf(s.List)),
            Note = note,
            Card = card,
        };
    }

    /// <summary>2-Var Stats of two lists, the scatter plot and the card.</summary>
    public static TwoVarOutcome TwoVar(DataSet data, TwoVarSettings s)
    {
        string title = $"2-VAR {s.X},{s.Y}" + (s.Freq is null ? "" : " FREQ " + s.Freq);
        Paired p;
        TwoVarStats st;
        try
        {
            p = data.Pair(s.X, s.Y, s.Freq);
            st = Descriptive.TwoVar(p.X, p.Y, p.Frequencies);
        }
        catch (Exception ex) when (ex is StatDataException or ArgumentException)
        {
            return new TwoVarOutcome { Error = ex.Message, Card = StatCard.Error(title, ex.Message) };
        }
        string D(double v) => NumberText.Display(v);
        var tiles = new List<Tile>
        {
            new("n", NumberText.Count(st.N), "Number of pairs"),
            new("x̄", D(st.MeanX), "Mean of x"),
            new("ȳ", D(st.MeanY), "Mean of y"),
            new("r", D(st.R), "Correlation coefficient"),
            new("Σx", D(st.SumX)), new("Σx²", D(st.SumX2)), new("Σy", D(st.SumY)), new("Σy²", D(st.SumY2)),
            new("Σxy", D(st.SumXY)), new("cov", D(st.CovarianceSample), "Sample covariance"),
            new("Sx", D(st.Sx), "Sample standard deviation of x"), new("Sy", D(st.Sy), "Sample standard deviation of y"),
            new("σx", D(st.SigmaX)), new("σy", D(st.SigmaY)), new("minX", D(st.MinX)), new("maxX", D(st.MaxX)),
            new("minY", D(st.MinY)), new("maxY", D(st.MaxY)),
        };
        string C(double v) => NumberText.Display(v, NumberText.CardDigits);
        var lines = new List<CardLine>
        {
            new("n", NumberText.Count(st.N)), new("x̄", C(st.MeanX)), new("ȳ", C(st.MeanY)), new("Sx", C(st.Sx)), new("Sy", C(st.Sy)),
            new("Σxy", C(st.SumXY)), new("cov", C(st.CovarianceSample)), new("r", C(st.R)),
        };
        string? note = Skipped(p.Skipped, pairs: true);
        bool many = p.X.Length > GraphSpec.MaxGraphPoints;
        var graph = many ? null : new GraphSpec([new GraphLine(GraphSpec.Points(p.X, p.Y), 0)], GraphSpec.Around(p.X, p.Y), []);
        var scene = many ? ChartBuilder.ScatterScene(p.X, p.Y, null, p.XName, p.YName) : null;
        var card = new StatCard
        {
            Title = title,
            Lines = lines,
            Note = note,
            CopyText = StatCard.PlainText($"2-Var Stats {s.X}, {s.Y}", null, lines, note),
            Latex = CardLatex.Aligned(lines.Select(l => (l.Label, LatexValue(l.Value)))),
            UseText = s.Freq is null ? $"twovar({s.X}, {s.Y})" : $"twovar({s.X}, {s.Y}, {s.Freq})",
            Recipe = new StatRecipe { Tab = StatTab.TwoVar, TwoVar = s.Clone() },
        };
        return new TwoVarOutcome { Stats = st, Tiles = tiles, Graph = graph, Scene = scene, Note = note, Card = card };
    }

    /// <summary>The display name of a quartile method.</summary>
    public static string MethodName(QuartileMethod m) => QuartileMethods.FirstOrDefault(q => q.Method == m).Name ?? m.ToString();

    private static string ModeText(OneVarStats st, Func<double, string> fmt)
    {
        if (st.Modes.Count == 0 || st.Modes.Count >= st.N || st.ModeFrequency <= 1 && st.Modes.Count > 1) return "none";
        var shown = st.Modes.Take(3).Select(fmt);
        return string.Join(", ", shown) + (st.Modes.Count > 3 ? ", ..." : "");
    }

    /// <summary>"3 empty cells skipped" style remark, or null.</summary>
    public static string? Skipped(int skipped, bool pairs = false) => skipped switch
    {
        0 => null,
        1 => pairs ? "1 row with only one value was skipped" : "1 empty cell was skipped",
        _ => pairs ? $"{skipped} rows with only one value were skipped" : $"{skipped} empty cells were skipped",
    };

    /// <summary>Joins two remarks with a space.</summary>
    public static string? Join(string? a, string? b) => a is null ? b : b is null ? a : a + ". " + b;

    /// <summary>A displayed value turned into LaTeX (×10⁻⁵ becomes \times 10^{-5}).</summary>
    public static string LatexValue(string shown)
    {
        int i = shown.IndexOf("×10", StringComparison.Ordinal);
        if (i < 0) return shown.Replace("∞", "\\infty");
        const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";
        string exp = new([.. shown[(i + 3)..].Select(c => c == '⁻' ? '-' : Superscripts.IndexOf(c) is var k and >= 0 ? (char)('0' + k) : c)]);
        return shown[..i] + " \\times 10^{" + exp + "}";
    }

    /// <summary>Invariant integer text.</summary>
    public static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
}
