using Calcy.Core;
using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>The result of the Plots tab.</summary>
public sealed class PlotOutcome
{
    /// <summary>Why nothing could be drawn, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The plot drawn by the statistics chart (histogram, box plot, dot plot, normal probability, bars).</summary>
    public ChartScene? Scene { get; init; }

    /// <summary>The plot drawn by the grapher (scatter plots).</summary>
    public GraphSpec? Graph { get; init; }

    /// <summary>A sentence that reads the plot.</summary>
    public string Summary { get; init; } = "";

    /// <summary>The tiles.</summary>
    public IReadOnlyList<Tile> Tiles { get; init; } = [];

    /// <summary>The bins, for histograms.</summary>
    public HistogramResult? Histogram { get; init; }

    /// <summary>The boxes, for box plots.</summary>
    public IReadOnlyList<BoxPlotStats> Boxes { get; init; } = [];

    /// <summary>The card.</summary>
    public required StatCard Card { get; init; }
}

/// <summary>Builds the statistical plots of the Plots tab and their cards.</summary>
public static class Plots
{
    /// <summary>The plot types in menu order with their button names.</summary>
    public static readonly IReadOnlyList<(PlotKind Kind, string Name, string Card)> Kinds =
    [
        (PlotKind.Histogram, "Histogram", "HISTOGRAM"),
        (PlotKind.BoxPlot, "Box plot", "BOX PLOT"),
        (PlotKind.Scatter, "Scatter", "SCATTER"),
        (PlotKind.NormalProbability, "Normal prob.", "NORMAL PROB PLOT"),
        (PlotKind.DotPlot, "Dot plot", "DOT PLOT"),
        (PlotKind.BarChart, "Bar chart", "BAR CHART"),
    ];

    /// <summary>Builds the plot the settings describe.</summary>
    public static PlotOutcome Build(DataSet data, PlotSettings s, QuartileMethod method = QuartileMethod.TI, CalcSession? session = null)
    {
        var kind = Kinds.First(k => k.Kind == s.Kind);
        string title = $"{kind.Card} {s.List}" + (s.Kind is PlotKind.Scatter && s.List2 is not null ? "," + s.List2 : "") +
                       (s.Kind is PlotKind.BoxPlot && s.List2 is not null && s.List2 != s.List ? "," + s.List2 : "");
        try
        {
            return s.Kind switch
            {
                PlotKind.Histogram => Histogram(data, s, title, session),
                PlotKind.BoxPlot => Box(data, s, method, title),
                PlotKind.Scatter => Scatter(data, s, title),
                PlotKind.NormalProbability => Normal(data, s, title),
                PlotKind.DotPlot => Dots(data, s, title),
                _ => Bars(data, s, title),
            };
        }
        catch (Exception ex) when (ex is StatDataException or ArgumentException or InvalidOperationException)
        {
            return new PlotOutcome { Error = ex.Message, Scene = ChartScene.Empty(ex.Message), Card = StatCard.Error(title, ex.Message) };
        }
    }

    private static StatCard Card(string title, PlotSettings s, IReadOnlyList<CardLine> lines, string summary, string? use, string what) => new()
    {
        Title = title,
        Lines = [.. lines],
        Note = summary,
        CopyText = StatCard.PlainText(what, null, lines, summary),
        Latex = CardLatex.Aligned(lines.Select(l => (l.Label, Descriptives.LatexValue(l.Value)))),
        UseText = use,
        Recipe = new StatRecipe { Tab = StatTab.Plots, Plot = s.Clone() },
    };

    private static PlotOutcome Histogram(DataSet data, PlotSettings s, string title, CalcSession? session)
    {
        var sm = data.Single(s.List, s.Freq);
        double width = s.BinWidth.Trim().Length == 0 ? Logic.Histogram.AutoWidth(sm.Values, sm.Count) : Distributions.ReadNumber(s.BinWidth, "the bin width", session);
        double? start = s.BinStart.Trim().Length == 0 ? null : Distributions.ReadNumber(s.BinStart, "the bin start", session);
        var h = Logic.Histogram.Bin(sm.Values, sm.Frequencies, width, start);
        var tallest = h.Bins.MaxBy(b => b.Count)!;
        string D(double v) => NumberText.Display(v);
        string summary = $"{h.Bins.Count} bins of width {D(h.Width)} from {D(h.Start)}. The tallest bin, [{D(tallest.Lower)}, {D(tallest.Upper)}), holds {NumberText.Count(tallest.Count)} of {NumberText.Count(h.Total)} values.";
        if (h.LeftOut > 0) summary += $" {NumberText.Count(h.LeftOut)} values below the start are not drawn.";
        var lines = new List<CardLine> { new("bins", Descriptives.Int(h.Bins.Count)), new("width", D(h.Width)), new("start", D(h.Start)) };
        foreach (var b in h.Bins.Take(12)) lines.Add(new CardLine($"[{D(b.Lower)}, {D(b.Upper)})", NumberText.Count(b.Count)));
        var tiles = new List<Tile>
        {
            new("bins", Descriptives.Int(h.Bins.Count)), new("width", D(h.Width)), new("start", D(h.Start)), new("n", NumberText.Count(h.Total)),
        };
        string use = "{" + string.Join(", ", h.Bins.Select(b => NumberText.Input(b.Count))) + "}";
        return new PlotOutcome
        {
            Scene = ChartBuilder.HistogramScene(h, data.HeaderOf(s.List)),
            Histogram = h,
            Summary = Descriptives.Join(summary.TrimEnd('.'), Descriptives.Skipped(sm.Skipped)) + ".",
            Tiles = tiles,
            Card = Card(title, s, lines, summary, use, $"Histogram of {s.List}"),
        };
    }

    private static PlotOutcome Box(DataSet data, PlotSettings s, QuartileMethod method, string title)
    {
        var names = new List<string> { s.List };
        if (s.List2 is { Length: > 0 } second && second != s.List) names.Add(second);
        var boxes = names.Select((n, i) =>
        {
            var sm = data.Single(n, i == 0 ? s.Freq : null);
            return BoxPlot.Compute(n, sm.Values, sm.Frequencies, method, s.Outliers);
        }).ToList();
        string D(double v) => NumberText.Display(v);
        var b0 = boxes[0];
        string summary = $"{b0.Name}: min {D(b0.Min)}, Q1 {D(b0.Q1)}, median {D(b0.Median)}, Q3 {D(b0.Q3)}, max {D(b0.Max)}.";
        if (s.Outliers) summary += b0.Outliers.Count == 0 ? " No outliers beyond 1.5 IQR." : $" Outliers: {string.Join(", ", b0.Outliers.Distinct().Take(6).Select(D))}.";
        if (boxes.Count > 1) summary += $" {boxes[1].Name}: median {D(boxes[1].Median)}, IQR {D(boxes[1].IQR)}.";
        var lines = new List<CardLine>();
        foreach (var b in boxes)
        {
            string p = boxes.Count > 1 ? b.Name + " " : "";
            lines.Add(new CardLine(p + "min", D(b.Min)));
            lines.Add(new CardLine(p + "Q1", D(b.Q1)));
            lines.Add(new CardLine(p + "median", D(b.Median)));
            lines.Add(new CardLine(p + "Q3", D(b.Q3)));
            lines.Add(new CardLine(p + "max", D(b.Max)));
            if (b.Outliers.Count > 0) lines.Add(new CardLine(p + "outliers", string.Join(", ", b.Outliers.Distinct().Take(6).Select(D))));
        }
        var tiles = new List<Tile>
        {
            new("Q1", D(b0.Q1)), new("median", D(b0.Median)), new("Q3", D(b0.Q3)), new("IQR", D(b0.IQR)),
            new("whiskers", $"{D(b0.LowerWhisker)} to {D(b0.UpperWhisker)}", "Where the whiskers end"),
            new("fences", $"{D(b0.LowerFence)}, {D(b0.UpperFence)}", "Q1 - 1.5 IQR and Q3 + 1.5 IQR"),
            new("outliers", b0.Outliers.Count == 0 ? "none" : Descriptives.Int(b0.Outliers.Count), "Values beyond the fences", Highlight: b0.Outliers.Count > 0),
            new("method", Descriptives.MethodName(method), "How the quartiles are computed"),
        };
        return new PlotOutcome
        {
            Scene = ChartBuilder.BoxScene(boxes, boxes.Count == 1 ? data.HeaderOf(s.List) : string.Join(" and ", names)),
            Boxes = boxes,
            Summary = summary,
            Tiles = tiles,
            Card = Card(title, s, lines, summary, $"quartiles({s.List})", $"Box plot of {string.Join(" and ", names)}"),
        };
    }

    private static PlotOutcome Scatter(DataSet data, PlotSettings s, string title)
    {
        var p = data.Pair(s.List, s.List2 ?? s.List, s.Freq);
        string? curve = null;
        Func<double, double>? line = null;
        string summary;
        var lines = new List<CardLine> { new("n", Descriptives.Int(p.X.Length)) };
        var tiles = new List<Tile> { new("n", Descriptives.Int(p.X.Length), "Number of points") };
        if (s.FitLine && p.X.Length >= 2 && p.X.Distinct().Count() >= 2)
        {
            var fit = Calcy.Statistics.Regression.Fit(RegressionModel.LinearAxB, p.X, p.Y, p.Frequencies);
            double a = fit["a"], b = fit["b"];
            curve = "y = " + NumberText.Input(a) + "*x + " + NumberText.Input(b, wrapNegative: true);
            line = v => a * v + b;
            string eq = $"{NumberText.Display(a)}x {(b < 0 ? "-" : "+")} {NumberText.Display(Math.Abs(b))}";
            summary = $"Least squares line y = {eq}, r = {NumberText.Display(fit.R ?? double.NaN, 4)}.";
            lines.Add(new CardLine("a", NumberText.Display(a, NumberText.CardDigits)));
            lines.Add(new CardLine("b", NumberText.Display(b, NumberText.CardDigits)));
            lines.Add(new CardLine("r", NumberText.Display(fit.R ?? double.NaN, NumberText.CardDigits)));
            tiles.Add(new Tile("a (slope)", NumberText.Display(a)));
            tiles.Add(new Tile("b (intercept)", NumberText.Display(b)));
            tiles.Add(new Tile("r", NumberText.Display(fit.R ?? double.NaN)));
        }
        else summary = $"{p.X.Length} points of {p.YName} against {p.XName}.";
        summary = Descriptives.Join(summary.TrimEnd('.'), Descriptives.Skipped(p.Skipped, pairs: true)) + ".";
        return new PlotOutcome
        {
            Graph = p.X.Length > GraphSpec.MaxGraphPoints ? null : Regressions.Scatter(p, curve, []),
            Scene = p.X.Length > GraphSpec.MaxGraphPoints ? ChartBuilder.ScatterScene(p.X, p.Y, line, data.HeaderOf(p.XName), data.HeaderOf(p.YName)) : null,
            Summary = summary,
            Tiles = tiles,
            Card = Card(title, s, lines, summary, curve is null ? $"twovar({p.XName}, {p.YName})" : $"linreg({p.XName}, {p.YName})", $"Scatter plot of {p.YName} against {p.XName}"),
        };
    }

    private static PlotOutcome Normal(DataSet data, PlotSettings s, string title)
    {
        var sm = data.Single(s.List, s.Freq);
        var values = sm.Expanded();
        var (x, z, mean, sd) = NormalProbability.Compute(values);
        double r = Correlation(x, z);
        string verdict = r >= 0.98 ? "close to the line: the data look normal" : r >= 0.95 ? "fairly close to the line: roughly normal" : "curved away from the line: the data do not look normal";
        string summary = $"The points lie {verdict} (r = {NumberText.Display(r, 4)} between the data and the normal scores).";
        var lines = new List<CardLine> { new("n", Descriptives.Int(x.Length)), new("x̄", NumberText.Display(mean, 10)), new("Sx", NumberText.Display(sd, 10)), new("r", NumberText.Display(r, 10)) };
        var tiles = new List<Tile>
        {
            new("n", Descriptives.Int(x.Length)), new("x̄", NumberText.Display(mean)), new("Sx", NumberText.Display(sd)),
            new("r", NumberText.Display(r), "Correlation of the sorted data with the normal scores: near 1 for normal data", Highlight: r >= 0.98),
        };
        return new PlotOutcome
        {
            Scene = ChartBuilder.NormalProbabilityScene(x, z, mean, sd, data.HeaderOf(s.List)),
            Summary = summary,
            Tiles = tiles,
            Card = Card(title, s, lines, summary, null, $"Normal probability plot of {s.List}"),
        };
    }

    private static double Correlation(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }
        return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : double.NaN;
    }

    private static PlotOutcome Dots(DataSet data, PlotSettings s, string title)
    {
        var sm = data.Single(s.List, s.Freq);
        var (res, stacks) = DotPlot.Stack(sm.Values, sm.Frequencies);
        var top = stacks.MaxBy(t => t.Count)!;
        string D(double v) => NumberText.Display(v);
        string summary = $"{stacks.Sum(t => t.Count)} dots in {stacks.Count} stacks{(res != 1 ? $" (values rounded to {D(res)})" : "")}. The tallest stack is at {D(top.X)} with {top.Count}.";
        var lines = new List<CardLine> { new("dots", Descriptives.Int(stacks.Sum(t => t.Count))), new("stacks", Descriptives.Int(stacks.Count)), new("tallest", $"{D(top.X)} ({top.Count})") };
        var tiles = new List<Tile>
        {
            new("dots", Descriptives.Int(stacks.Sum(t => t.Count))), new("stacks", Descriptives.Int(stacks.Count)),
            new("tallest at", D(top.X)), new("resolution", D(res), "Values are rounded to multiples of this"),
        };
        return new PlotOutcome
        {
            Scene = ChartBuilder.DotScene(res, stacks, data.HeaderOf(s.List)),
            Summary = summary,
            Tiles = tiles,
            Card = Card(title, s, lines, summary, null, $"Dot plot of {s.List}"),
        };
    }

    private static PlotOutcome Bars(DataSet data, PlotSettings s, string title)
    {
        var cats = data.Categories(s.List, s.Freq);
        var top = cats.MaxBy(c => c.Count);
        double total = cats.Sum(c => c.Count);
        string summary = $"{cats.Count} categories, {NumberText.Count(total)} values. The most common is {top.Label} ({NumberText.Count(top.Count)}, {NumberText.Display(top.Count / total * 100, 3)}%).";
        var lines = cats.Take(14).Select(c => new CardLine(c.Label, NumberText.Count(c.Count))).ToList();
        var tiles = new List<Tile>
        {
            new("categories", Descriptives.Int(cats.Count)), new("values", NumberText.Count(total)),
            new("most common", top.Label), new("its share", NumberText.Display(top.Count / total * 100, 3) + "%"),
        };
        string use = "{" + string.Join(", ", cats.Select(c => NumberText.Input(c.Count))) + "}";
        return new PlotOutcome
        {
            Scene = ChartBuilder.BarScene(cats, data.HeaderOf(s.List)),
            Summary = summary,
            Tiles = tiles,
            Card = Card(title, s, lines, summary, use, $"Bar chart of {s.List}"),
        };
    }
}
