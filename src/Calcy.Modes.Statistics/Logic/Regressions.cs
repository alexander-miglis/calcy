using System.Globalization;
using System.Text.RegularExpressions;
using Calcy.Core;
using Calcy.Core.Compilation;
using Calcy.Core.Expressions;
using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>The regression models of the Regression tab (every TI-84 model plus any polynomial degree and a custom model).</summary>
public enum RegressionKind
{
    /// <summary>y = ax + b.</summary>
    LinearAxB,

    /// <summary>y = a + bx.</summary>
    LinearABx,

    /// <summary>Median-median line y = ax + b.</summary>
    MedianMedian,

    /// <summary>y = ax² + bx + c.</summary>
    Quadratic,

    /// <summary>y = ax³ + bx² + cx + d.</summary>
    Cubic,

    /// <summary>y = ax⁴ + bx³ + cx² + dx + e.</summary>
    Quartic,

    /// <summary>A polynomial of any degree.</summary>
    Polynomial,

    /// <summary>y = a + b ln x.</summary>
    Logarithmic,

    /// <summary>y = a·bˣ.</summary>
    Exponential,

    /// <summary>y = a·eᵇˣ.</summary>
    ExponentialE,

    /// <summary>y = a·xᵇ.</summary>
    Power,

    /// <summary>y = c / (1 + a·e^(−bx)).</summary>
    Logistic,

    /// <summary>y = a·sin(bx + c) + d.</summary>
    Sinusoidal,

    /// <summary>Any model typed by the user, fitted by Levenberg-Marquardt.</summary>
    Custom,
}

/// <summary>How a regression model is shown and sent to the calculator.</summary>
/// <param name="Kind">The model.</param>
/// <param name="Name">Menu name ("Quadratic").</param>
/// <param name="Form">The model with letters ("y = ax² + bx + c").</param>
/// <param name="CardName">Card title (pixel font characters).</param>
/// <param name="Function">The calculator function (linreg, quadreg, ...), or null.</param>
public sealed record RegressionModelInfo(RegressionKind Kind, string Name, string Form, string CardName, string? Function);

/// <summary>The result of the Regression tab.</summary>
public sealed class RegressionOutcome
{
    /// <summary>Why nothing could be fitted, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The model.</summary>
    public required RegressionModelInfo Model { get; init; }

    /// <summary>The fitted parameters, in order (a, b, c, ...).</summary>
    public IReadOnlyList<(string Name, double Value)> Parameters { get; init; } = [];

    /// <summary>The equation as Calcy input with full precision ("y = 4.542619543*x + 47.96673597").</summary>
    public string Equation { get; init; } = "";

    /// <summary>The right-hand side of <see cref="Equation"/> (for f(x) = ... and the grapher).</summary>
    public string Expression { get; init; } = "";

    /// <summary>The equation rounded for reading ("y = 4.54262x + 47.9667").</summary>
    public string DisplayEquation { get; init; } = "";

    /// <summary>Correlation coefficient, where the model has one.</summary>
    public double? R { get; init; }

    /// <summary>Coefficient of determination.</summary>
    public double RSquared { get; init; } = double.NaN;

    /// <summary>The fitted function.</summary>
    public Func<double, double>? Predict { get; init; }

    /// <summary>The data that was fitted.</summary>
    public Paired? Data { get; init; }

    /// <summary>Residuals y − f(x), one per pair of <see cref="Data"/>.</summary>
    public IReadOnlyList<double> Residuals { get; init; } = [];

    /// <summary>The tiles (parameters, r, r²).</summary>
    public IReadOnlyList<Tile> Tiles { get; init; } = [];

    /// <summary>The scatter plot with the fitted curve, or the residual plot (drawn by the grapher).</summary>
    public GraphSpec? Graph { get; init; }

    /// <summary>The same plot for many points, drawn by the statistics chart (null when <see cref="Graph"/> is used).</summary>
    public ChartScene? Scene { get; init; }

    /// <summary>Remarks (rows skipped, did not converge, ...).</summary>
    public string? Note { get; init; }

    /// <summary>The card.</summary>
    public required StatCard Card { get; init; }
}

/// <summary>Fits the regression models and builds their tiles, plots and cards.</summary>
public static partial class Regressions
{
    /// <summary>Every model in menu order.</summary>
    public static readonly IReadOnlyList<RegressionModelInfo> Models =
    [
        new(RegressionKind.LinearAxB, "Linear", "y = ax + b", "LINREG AX+B", "linreg"),
        new(RegressionKind.LinearABx, "Linear", "y = a + bx", "LINREG A+BX", "linregabx"),
        new(RegressionKind.MedianMedian, "Median-median", "y = ax + b", "MED-MED", "medmed"),
        new(RegressionKind.Quadratic, "Quadratic", "y = ax² + bx + c", "QUADREG", "quadreg"),
        new(RegressionKind.Cubic, "Cubic", "y = ax³ + bx² + cx + d", "CUBICREG", "cubicreg"),
        new(RegressionKind.Quartic, "Quartic", "y = ax⁴ + bx³ + cx² + dx + e", "QUARTREG", "quartreg"),
        new(RegressionKind.Polynomial, "Polynomial", "degree n", "POLYREG", "polyreg"),
        new(RegressionKind.Logarithmic, "Logarithmic", "y = a + b·ln x", "LNREG", "lnreg"),
        new(RegressionKind.Exponential, "Exponential", "y = a·bˣ", "EXPREG", "expreg"),
        new(RegressionKind.ExponentialE, "Exponential", "y = a·eᵇˣ", "EXPREG A·E^BX", "expereg"),
        new(RegressionKind.Power, "Power", "y = a·xᵇ", "PWRREG", "pwrreg"),
        new(RegressionKind.Logistic, "Logistic", "y = c / (1 + a·e^(−bx))", "LOGISTIC", "logisticreg"),
        new(RegressionKind.Sinusoidal, "Sinusoidal", "y = a·sin(bx + c) + d", "SINREG", "sinreg"),
        new(RegressionKind.Custom, "Custom model", "your own formula", "CUSTOM FIT", null),
    ];

    /// <summary>The model info for a kind.</summary>
    public static RegressionModelInfo Info(RegressionKind kind) => Models.First(m => m.Kind == kind);

    /// <summary>
    /// Fits the chosen model to the chosen lists and builds the tiles, the plot (scatter with the curve, or residuals) and
    /// the card. The session is needed for custom models only.
    /// </summary>
    public static RegressionOutcome Fit(DataSet data, RegressionSettings s, CalcSession? session = null, CancellationToken ct = default)
    {
        var info = Info(s.Model);
        string title = $"{CardTitle(info, s)} {s.X},{s.Y}";
        Paired p;
        try
        {
            p = data.Pair(s.X, s.Y, s.Freq);
        }
        catch (StatDataException ex)
        {
            return new RegressionOutcome { Model = info, Error = ex.Message, Card = StatCard.Error(title, ex.Message) };
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            return s.Model == RegressionKind.Custom ? FitCustom(p, s, info, title, session, ct) : FitBuiltIn(data, p, s, info, title);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or StatDataException or CalcException or ArithmeticException)
        {
            bool many = p.X.Length > GraphSpec.MaxGraphPoints;
            return new RegressionOutcome
            {
                Model = info,
                Data = p,
                Error = ex.Message,
                Card = StatCard.Error(title, ex.Message),
                Graph = many ? null : Scatter(p, null, []),
                Scene = many ? ChartBuilder.ScatterScene(p.X, p.Y, null, p.XName, p.YName) : null,
            };
        }
    }

    private static string CardTitle(RegressionModelInfo info, RegressionSettings s) =>
        info.Kind == RegressionKind.Polynomial ? "POLYREG DEGREE " + s.Degree.ToString(CultureInfo.InvariantCulture) : info.CardName;

    private static RegressionOutcome FitBuiltIn(DataSet data, Paired p, RegressionSettings s, RegressionModelInfo info, string title)
    {
        RegressionResult r = s.Model switch
        {
            RegressionKind.Polynomial => Calcy.Statistics.Regression.FitPolynomial(Math.Clamp(s.Degree, 1, 10), p.X, p.Y, p.Frequencies),
            RegressionKind.Logistic => Calcy.Statistics.Regression.FitLogistic(p.X, p.Y, p.Frequencies),
            RegressionKind.Sinusoidal => Calcy.Statistics.Regression.FitSinusoidal(p.X, p.Y, null, p.Frequencies),
            _ => Calcy.Statistics.Regression.Fit(ToModel(s.Model), p.X, p.Y, p.Frequencies),
        };
        var parameters = r.Parameters.Select(q => (q.Name, Clean(q.Value, r.Parameters.Select(z => z.Value)))).ToList();
        string expr = Rhs(r.FormatEquation(15));
        string display = Readable(Rhs(r.FormatEquation(6)));
        string? note = Descriptives.Skipped(p.Skipped, pairs: true);
        if (!r.Converged) note = Descriptives.Join(note, "The fit did not fully converge; check the curve against the points");
        if (s.Model is RegressionKind.Logarithmic or RegressionKind.Power && p.X.Any(x => x <= 0))
            note = Descriptives.Join(note, "ln x needs x > 0");
        string? use = UseText(info, s);
        return Build(p, s, info, title, parameters, expr, display, r.R, r.RSquared, r.Predict, r.Residuals, note, use, r.AdjustedRSquared, r.StandardError);
    }

    private static RegressionModel ToModel(RegressionKind k) => k switch
    {
        RegressionKind.LinearAxB => RegressionModel.LinearAxB,
        RegressionKind.LinearABx => RegressionModel.LinearABx,
        RegressionKind.MedianMedian => RegressionModel.MedianMedian,
        RegressionKind.Quadratic => RegressionModel.Quadratic,
        RegressionKind.Cubic => RegressionModel.Cubic,
        RegressionKind.Quartic => RegressionModel.Quartic,
        RegressionKind.Logarithmic => RegressionModel.Logarithmic,
        RegressionKind.Exponential => RegressionModel.Exponential,
        RegressionKind.ExponentialE => RegressionModel.ExponentialE,
        RegressionKind.Power => RegressionModel.Power,
        RegressionKind.Logistic => RegressionModel.Logistic,
        RegressionKind.Sinusoidal => RegressionModel.Sinusoidal,
        _ => throw new ArgumentException("use FitPolynomial for polynomials"),
    };

    /// <summary>The calculator expression for USE: linreg(L1, L2), polyreg(L1, L2, 5), weights as the third argument.</summary>
    public static string? UseText(RegressionModelInfo info, RegressionSettings s)
    {
        if (info.Function is null) return null;
        if (info.Kind == RegressionKind.Polynomial)
            return s.Freq is null ? $"polyreg({s.X}, {s.Y}, {s.Degree.ToString(CultureInfo.InvariantCulture)})" : null;
        return s.Freq is null ? $"{info.Function}({s.X}, {s.Y})" : $"{info.Function}({s.X}, {s.Y}, {s.Freq})";
    }

    private static RegressionOutcome FitCustom(Paired p, RegressionSettings s, RegressionModelInfo info, string title, CalcSession? session, CancellationToken ct)
    {
        if (session is null) throw new InvalidOperationException("Custom models need the calculator session");
        var text = s.Custom.Trim();
        if (text.StartsWith("y=", StringComparison.Ordinal) || text.StartsWith("y =", StringComparison.Ordinal)) text = text[(text.IndexOf('=') + 1)..].Trim();
        if (text.Length == 0) throw new StatDataException("Type a model in x, for example a*e^(b*x) + c");
        var parsed = session.ParseExpression(text);
        var names = parsed.FreeSymbols()
            .Where(n => n != "x" && !Constants.Contains(n) && !(session.Env.TryGetVariable(n, out _) && !SingleLetter(n)))
            .Distinct().ToList();
        if (!parsed.ContainsSymbol("x")) throw new StatDataException("The model must use x");
        if (names.Count == 0) throw new StatDataException("The model has no parameters to fit: use letters such as a, b and c");
        if (names.Count > 8) throw new StatDataException("Use at most 8 parameters");
        var ctx = session.Context(approximate: true, ct);
        var variables = names.Prepend("x").ToArray();
        var f = ExprCompiler.Compile(parsed, variables, ctx);
        var start = StartValues(s.CustomStart, names);
        double Model(double[] q, double x)
        {
            var args = new double[q.Length + 1];
            args[0] = x;
            Array.Copy(q, 0, args, 1, q.Length);
            return f(args);
        }
        var fit = NonlinearFit.LevenbergMarquardt(Model, p.X, p.Y, start, p.Frequencies);
        var values = fit.Parameters.ToArray();
        var parameters = names.Select((n, i) => (n, values[i])).ToList();
        string expr = Substitute(text, parameters, 15);
        string display = Readable(Substitute(text, parameters, 6));
        string? note = Descriptives.Skipped(p.Skipped, pairs: true);
        if (!fit.Converged) note = Descriptives.Join(note, "The fit did not converge; try other start values");
        double Predict(double x) => Model(values, x);
        return Build(p, s, info, title, parameters, expr, display, null, fit.RSquared, Predict, fit.Residuals, note, null, double.NaN, fit.StandardError);
    }

    private static bool SingleLetter(string n) => n.Length == 1;

    private static readonly HashSet<string> Constants = new(StringComparer.Ordinal) { "e", "pi", "π", "i", "inf", "∞" };

    private static double[] StartValues(string text, IReadOnlyList<string> names)
    {
        var start = names.Select(_ => 1.0).ToArray();
        foreach (Match m in StartPattern().Matches(text ?? ""))
        {
            int i = names.ToList().IndexOf(m.Groups["n"].Value);
            if (i >= 0 && CellParser.PlainNumberValue(m.Groups["v"].Value) is double v) start[i] = v;
        }
        return start;
    }

    /// <summary>Puts parameter values into a model text: a*e^(b*x) with a = 2, b = -0.5 gives 2*e^((-0.5)*x).</summary>
    public static string Substitute(string model, IReadOnlyList<(string Name, double Value)> parameters, int digits)
    {
        var result = model;
        foreach (var (name, value) in parameters.OrderByDescending(q => q.Name.Length))
        {
            var number = NumberText.Plain(value, digits);
            if (value < 0 || number.Contains('e')) number = "(" + number + ")";
            result = Regex.Replace(result, $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_(])", number);
        }
        return result;
    }

    private static RegressionOutcome Build(Paired p, RegressionSettings s, RegressionModelInfo info, string title,
        IReadOnlyList<(string Name, double Value)> parameters, string expr, string display, double? r, double r2,
        Func<double, double> predict, IReadOnlyList<double> residuals, string? note, string? use, double adjR2, double se)
    {
        string D(double v) => NumberText.Display(v);
        var tiles = new List<Tile>();
        foreach (var (name, value) in parameters) tiles.Add(new Tile(ParameterLabel(info.Kind, name), D(value), $"Fitted value of {name}"));
        if (r is double rr) tiles.Add(new Tile("r", D(rr), "Correlation coefficient (for the linearised model)"));
        tiles.Add(new Tile("r²", D(r2), "Coefficient of determination: the share of the variation in y that the model explains"));
        string C(double v) => NumberText.Display(v, NumberText.CardDigits);
        var lines = parameters.Select(q => new CardLine(q.Name, C(q.Value))).ToList();
        if (r is double r1) lines.Add(new CardLine("r", C(r1)));
        lines.Add(new CardLine("r²", C(r2)));
        if (double.IsFinite(adjR2) && parameters.Count > 2) lines.Add(new CardLine("adj r²", C(adjR2)));
        if (double.IsFinite(se)) lines.Add(new CardLine("s", C(se)));
        string headline = "y = " + display;
        string latex = "y = " + FormulaText.Latex(display);
        var card = new StatCard
        {
            Title = title,
            Headline = headline,
            MathHeadline = true,
            Lines = lines,
            Note = note,
            CopyText = StatCard.PlainText($"{info.Name} regression {info.Form}, x = {s.X}, y = {s.Y}", "y = " + expr, lines, note),
            Latex = latex,
            UseText = use ?? "y = " + expr,
            Recipe = new StatRecipe { Tab = StatTab.Regression, Regression = s.Clone() },
        };
        bool many = p.X.Length > GraphSpec.MaxGraphPoints;
        var graph = many ? null : s.Residuals ? ResidualPlot(p, residuals) : Scatter(p, "y = " + expr, []);
        var scene = !many ? null : s.Residuals
            ? ChartBuilder.ScatterScene(p.X, residuals, null, p.XName, "residual", zeroLine: true)
            : ChartBuilder.ScatterScene(p.X, p.Y, predict, p.XName, p.YName);
        return new RegressionOutcome
        {
            Model = info,
            Parameters = parameters,
            Equation = "y = " + expr,
            Expression = expr,
            DisplayEquation = headline,
            R = r,
            RSquared = r2,
            Predict = predict,
            Data = p,
            Residuals = residuals,
            Tiles = tiles,
            Graph = graph,
            Scene = scene,
            Note = note,
            Card = card,
        };
    }

    private static string ParameterLabel(RegressionKind k, string name) => (k, name) switch
    {
        (RegressionKind.LinearAxB or RegressionKind.MedianMedian, "a") => "a (slope)",
        (RegressionKind.LinearAxB or RegressionKind.MedianMedian, "b") => "b (intercept)",
        (RegressionKind.LinearABx, "a") => "a (intercept)",
        (RegressionKind.LinearABx, "b") => "b (slope)",
        _ => name,
    };

    /// <summary>The scatter plot of the pairs with an optional curve.</summary>
    public static GraphSpec Scatter(Paired p, string? curve, IReadOnlyList<Calcy.Graphing.GraphAnnotation> annotations)
    {
        var lines = new List<GraphLine> { new(GraphSpec.Points(p.X, p.Y), 0) };
        if (curve is not null) lines.Add(new GraphLine(curve, 1));
        return new GraphSpec(lines, GraphSpec.Around(p.X, p.Y), annotations);
    }

    /// <summary>The residual plot: (x, residual) with the zero line.</summary>
    public static GraphSpec ResidualPlot(Paired p, IReadOnlyList<double> residuals)
    {
        var (x0, x1) = GraphSpec.Range(p.X);
        double m = residuals.Count == 0 ? 1 : residuals.Max(Math.Abs);
        if (!(m > 0)) m = 1;
        var lines = new List<GraphLine> { new(GraphSpec.Points(p.X, residuals), 3) };
        return new GraphSpec(lines, new Calcy.Graphing.Viewport(x0, x1, -m * 1.3, m * 1.3), []);
    }

    /// <summary>Rounding noise next to large coefficients becomes 0 (as the calculator functions do).</summary>
    private static double Clean(double v, IEnumerable<double> all)
    {
        double scale = all.Where(double.IsFinite).Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (double.IsFinite(v) && Math.Abs(v) <= 1e-12 * scale) return 0;
        return v;
    }

    private static string Rhs(string equation)
    {
        int i = equation.IndexOf('=');
        return i < 0 ? equation.Trim() : equation[(i + 1)..].Trim();
    }

    /// <summary>"4.54262*x + 47.9667" reads as "4.54262x + 47.9667": a product with a name loses its star.</summary>
    public static string Readable(string expr) => StarBeforeName().Replace(expr, "$1$2");

    [GeneratedRegex(@"(\d|\))\*(?=[A-Za-z(√])(x|ln|sin|cos|e\^|\()?")]
    private static partial Regex StarBeforeName();

    [GeneratedRegex(@"(?<n>[A-Za-z]\w*)\s*=\s*(?<v>[-+]?[0-9.eE+-]+)")]
    private static partial Regex StartPattern();
}
