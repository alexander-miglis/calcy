using System.Globalization;
using Calcy.Core;
using Calcy.Graphing;
using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>The distributions of the explorer.</summary>
public enum DistributionKind
{
    /// <summary>Normal N(μ, σ).</summary>
    Normal,

    /// <summary>Student t.</summary>
    StudentT,

    /// <summary>Chi-squared.</summary>
    ChiSquared,

    /// <summary>F.</summary>
    F,

    /// <summary>Exponential with rate λ.</summary>
    Exponential,

    /// <summary>Continuous uniform on [a, b].</summary>
    Uniform,

    /// <summary>Gamma with shape k and scale θ.</summary>
    Gamma,

    /// <summary>Beta.</summary>
    Beta,

    /// <summary>Log-normal (μ and σ of ln X).</summary>
    LogNormal,

    /// <summary>Weibull with shape k and scale λ.</summary>
    Weibull,

    /// <summary>Cauchy.</summary>
    Cauchy,

    /// <summary>Binomial: successes in n trials.</summary>
    Binomial,

    /// <summary>Poisson.</summary>
    Poisson,

    /// <summary>Geometric: the trial of the first success.</summary>
    Geometric,

    /// <summary>Hypergeometric: successes in n draws without replacement.</summary>
    Hypergeometric,

    /// <summary>Negative binomial: failures before the r-th success.</summary>
    NegativeBinomial,
}

/// <summary>A parameter of a distribution.</summary>
/// <param name="Key">Settings key.</param>
/// <param name="Label">Label on screen.</param>
/// <param name="Default">Default value as text.</param>
/// <param name="Tip">What it means.</param>
public sealed record DistributionParameter(string Key, string Label, string Default, string Tip);

/// <summary>A distribution of the explorer: its name, parameters and calculator function names.</summary>
/// <param name="Kind">The distribution.</param>
/// <param name="Name">Menu name.</param>
/// <param name="IsDiscrete">True for counts (bars, P(X = x)).</param>
/// <param name="Parameters">Its parameters.</param>
/// <param name="Prefix">Calculator function prefix (normal for normalcdf, ...), or null when the calculator has none.</param>
public sealed record DistributionInfo(DistributionKind Kind, string Name, bool IsDiscrete, IReadOnlyList<DistributionParameter> Parameters, string? Prefix);

/// <summary>A distribution with its parameters filled in, continuous or discrete behind one face.</summary>
public sealed class Dist
{
    private readonly ContinuousDistribution? _c;
    private readonly DiscreteDistribution? _d;

    /// <summary>Wraps a continuous distribution.</summary>
    public Dist(ContinuousDistribution c) => _c = c;

    /// <summary>Wraps a discrete distribution.</summary>
    public Dist(DiscreteDistribution d) => _d = d;

    /// <summary>True for counts.</summary>
    public bool IsDiscrete => _d is not null;

    /// <summary>The mean (NaN when it does not exist).</summary>
    public double Mean => _c?.Mean ?? _d!.Mean;

    /// <summary>The standard deviation (NaN or ∞ when it does not exist).</summary>
    public double StandardDeviation => _c?.StandardDeviation ?? _d!.StandardDeviation;

    /// <summary>Smallest possible value.</summary>
    public double SupportMin => _c?.SupportMin ?? _d!.SupportMin;

    /// <summary>Largest possible value.</summary>
    public double SupportMax => _c?.SupportMax ?? _d!.SupportMax;

    /// <summary>The density f(x), or P(X = x) for counts.</summary>
    public double Density(double x) => _c?.Pdf(x) ?? _d!.Pmf(x);

    /// <summary>P(X ≤ x).</summary>
    public double Cdf(double x) => _c?.Cdf(x) ?? _d!.Cdf(x);

    /// <summary>P(lower ≤ X ≤ upper) (bounds may be infinite; both ends included for counts).</summary>
    public double Probability(double lower, double upper) => _c?.Probability(lower, upper) ?? _d!.Probability(lower, upper);

    /// <summary>The smallest x with P(X ≤ x) ≥ p.</summary>
    public double Quantile(double p) => _c?.InverseCdf(p) ?? _d!.InverseCdf(p);

    /// <summary>The x with P(X > x) = q (continuous only; counts use the left quantile of 1 − q).</summary>
    public double UpperQuantile(double q) => _c?.InverseSurvival(q) ?? _d!.InverseCdf(1 - q);
}

/// <summary>The result of the distribution explorer.</summary>
public sealed class DistributionOutcome
{
    /// <summary>Why nothing could be computed, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The distribution.</summary>
    public required DistributionInfo Info { get; init; }

    /// <summary>The headline, for example "P(-1.96 &lt; X &lt; 1.96) = 0.950004".</summary>
    public string Headline { get; init; } = "";

    /// <summary>The computed value (the probability, density or x).</summary>
    public double Value { get; init; } = double.NaN;

    /// <summary>The lower and upper bound actually used (for shading).</summary>
    public (double Lower, double Upper) Bounds { get; init; }

    /// <summary>The tiles.</summary>
    public IReadOnlyList<Tile> Tiles { get; init; } = [];

    /// <summary>The curve with the shaded area (continuous distributions).</summary>
    public GraphSpec? Graph { get; init; }

    /// <summary>The bars (discrete distributions).</summary>
    public ChartScene? Scene { get; init; }

    /// <summary>The card.</summary>
    public required StatCard Card { get; init; }
}

/// <summary>The distribution explorer: densities, probabilities between bounds and inverses, with the area shaded.</summary>
public static class Distributions
{
    /// <summary>Every distribution in menu order.</summary>
    public static readonly IReadOnlyList<DistributionInfo> All =
    [
        new(DistributionKind.Normal, "Normal", false, [P("mu", "μ", "0", "Mean"), P("sigma", "σ", "1", "Standard deviation")], "normal"),
        new(DistributionKind.StudentT, "Student t", false, [P("df", "df", "10", "Degrees of freedom")], "t"),
        new(DistributionKind.ChiSquared, "Chi-squared χ²", false, [P("df", "df", "3", "Degrees of freedom")], "chi2"),
        new(DistributionKind.F, "F", false, [P("df1", "df₁", "5", "Numerator degrees of freedom"), P("df2", "df₂", "10", "Denominator degrees of freedom")], "f"),
        new(DistributionKind.Exponential, "Exponential", false, [P("lambda", "λ", "1", "Rate (1 / mean)")], null),
        new(DistributionKind.Uniform, "Uniform", false, [P("a", "a", "0", "Lower end"), P("b", "b", "1", "Upper end")], null),
        new(DistributionKind.Gamma, "Gamma", false, [P("k", "k", "2", "Shape"), P("theta", "θ", "1", "Scale")], null),
        new(DistributionKind.Beta, "Beta", false, [P("alpha", "α", "2", "First shape"), P("beta", "β", "5", "Second shape")], null),
        new(DistributionKind.LogNormal, "Log-normal", false, [P("mu", "μ", "0", "Mean of ln X"), P("sigma", "σ", "1", "Standard deviation of ln X")], null),
        new(DistributionKind.Weibull, "Weibull", false, [P("k", "k", "1.5", "Shape"), P("lambda", "λ", "1", "Scale")], null),
        new(DistributionKind.Cauchy, "Cauchy", false, [P("x0", "x₀", "0", "Location"), P("gamma", "γ", "1", "Scale")], null),
        new(DistributionKind.Binomial, "Binomial", true, [P("n", "n", "10", "Number of trials"), P("p", "p", "0.5", "Chance of success in one trial")], "binom"),
        new(DistributionKind.Poisson, "Poisson", true, [P("lambda", "λ", "3", "Mean number of events")], "poisson"),
        new(DistributionKind.Geometric, "Geometric", true, [P("p", "p", "0.2", "Chance of success in one trial")], "geomet"),
        new(DistributionKind.Hypergeometric, "Hypergeometric", true, [P("N", "N", "50", "Population size"), P("K", "K", "10", "Successes in the population"), P("n", "n", "5", "Number of draws")], null),
        new(DistributionKind.NegativeBinomial, "Negative binomial", true, [P("r", "r", "3", "Successes wanted"), P("p", "p", "0.5", "Chance of success in one trial")], null),
    ];

    private static DistributionParameter P(string key, string label, string def, string tip) => new(key, label, def, tip);

    /// <summary>The info of a kind.</summary>
    public static DistributionInfo Info(DistributionKind kind) => All.First(d => d.Kind == kind);

    /// <summary>Builds the distribution from parameter values. Throws <see cref="ArgumentException"/> for invalid values.</summary>
    public static Dist Create(DistributionKind kind, IReadOnlyDictionary<string, double> p) => kind switch
    {
        DistributionKind.Normal => new(new NormalDistribution(p["mu"], p["sigma"])),
        DistributionKind.StudentT => new(new StudentTDistribution(p["df"])),
        DistributionKind.ChiSquared => new(new ChiSquaredDistribution(p["df"])),
        DistributionKind.F => new(new FDistribution(p["df1"], p["df2"])),
        DistributionKind.Exponential => new(new ExponentialDistribution(p["lambda"])),
        DistributionKind.Uniform => new(new UniformDistribution(p["a"], p["b"])),
        DistributionKind.Gamma => new(new GammaDistribution(p["k"], p["theta"])),
        DistributionKind.Beta => new(new BetaDistribution(p["alpha"], p["beta"])),
        DistributionKind.LogNormal => new(new LogNormalDistribution(p["mu"], p["sigma"])),
        DistributionKind.Weibull => new(new WeibullDistribution(p["k"], p["lambda"])),
        DistributionKind.Cauchy => new(new CauchyDistribution(p["x0"], p["gamma"])),
        DistributionKind.Binomial => new(new BinomialDistribution(p["n"], p["p"])),
        DistributionKind.Poisson => new(new PoissonDistribution(p["lambda"])),
        DistributionKind.Geometric => new(new GeometricDistribution(p["p"])),
        DistributionKind.Hypergeometric => new(new HypergeometricDistribution(p["N"], p["K"], p["n"])),
        DistributionKind.NegativeBinomial => new(new NegativeBinomialDistribution(p["r"], p["p"])),
        _ => throw new ArgumentException("unknown distribution"),
    };

    /// <summary>
    /// The density as grapher input ("y = ..." with a domain restriction where needed). Densities are written in closed
    /// form with the constants worked out, so the grapher compiles them to fast code.
    /// </summary>
    public static string? PdfFormula(DistributionKind kind, IReadOnlyDictionary<string, double> p)
    {
        static string N(double v) => NumberText.Input(v, wrapNegative: true);
        switch (kind)
        {
            case DistributionKind.Normal:
            {
                double mu = p["mu"], s = p["sigma"];
                return $"y = {N(1 / (s * Math.Sqrt(2 * Math.PI)))}*e^(-(x - {N(mu)})^2/{N(2 * s * s)})";
            }
            case DistributionKind.StudentT:
                return TFormula(p["df"], 0, 1);
            case DistributionKind.ChiSquared:
            {
                double k = p["df"];
                double lc = -(k / 2) * Math.Log(2) - SpecialFunctions.LogGamma(k / 2);
                return $"y = e^({N(lc)} + {N(k / 2 - 1)}*ln(x) - x/2) {{x > 0}}";
            }
            case DistributionKind.F:
            {
                double d1 = p["df1"], d2 = p["df2"];
                double lc = d1 / 2 * Math.Log(d1 / d2) - SpecialFunctions.LogBeta(d1 / 2, d2 / 2);
                return $"y = e^({N(lc)} + {N(d1 / 2 - 1)}*ln(x) - {N((d1 + d2) / 2)}*ln(1 + {N(d1 / d2)}*x)) {{x > 0}}";
            }
            case DistributionKind.Exponential:
            {
                double l = p["lambda"];
                return $"y = {N(l)}*e^(-{N(l)}*x) {{x >= 0}}";
            }
            case DistributionKind.Uniform:
            {
                double a = p["a"], b = p["b"];
                return $"y = {N(1 / (b - a))} {{{N(a)} <= x <= {N(b)}}}";
            }
            case DistributionKind.Gamma:
            {
                double k = p["k"], th = p["theta"];
                double lc = -k * Math.Log(th) - SpecialFunctions.LogGamma(k);
                return $"y = e^({N(lc)} + {N(k - 1)}*ln(x) - x/{N(th)}) {{x > 0}}";
            }
            case DistributionKind.Beta:
            {
                double a = p["alpha"], b = p["beta"];
                return $"y = e^({N(-SpecialFunctions.LogBeta(a, b))} + {N(a - 1)}*ln(x) + {N(b - 1)}*ln(1 - x)) {{0 < x < 1}}";
            }
            case DistributionKind.LogNormal:
            {
                double mu = p["mu"], s = p["sigma"];
                return $"y = e^(-(ln(x) - {N(mu)})^2/{N(2 * s * s)})/({N(s * Math.Sqrt(2 * Math.PI))}*x) {{x > 0}}";
            }
            case DistributionKind.Weibull:
            {
                double k = p["k"], l = p["lambda"];
                return $"y = {N(k / l)}*(x/{N(l)})^{N(k - 1)}*e^(-(x/{N(l)})^{N(k)}) {{x >= 0}}";
            }
            case DistributionKind.Cauchy:
            {
                double x0 = p["x0"], g = p["gamma"];
                return $"y = {N(1 / (Math.PI * g))}/(1 + ((x - {N(x0)})/{N(g)})^2)";
            }
            default:
                return null;
        }
    }

    /// <summary>The density of a t distribution with df degrees of freedom, shifted to <paramref name="center"/> and scaled.</summary>
    public static string TFormula(double df, double center, double scale)
    {
        static string N(double v) => NumberText.Input(v, wrapNegative: true);
        double c = new StudentTDistribution(df).Pdf(0) / scale;
        string u = center == 0 && scale == 1 ? "x" : $"((x - {N(center)})/{N(scale)})";
        return $"y = {N(c)}*(1 + {u}^2/{N(df)})^(-{N((df + 1) / 2)})";
    }

    /// <summary>The density of N(center, scale) as grapher input.</summary>
    public static string NormalFormula(double center, double scale)
    {
        static string N(double v) => NumberText.Input(v, wrapNegative: true);
        return $"y = {N(1 / (scale * Math.Sqrt(2 * Math.PI)))}*e^(-(x - {N(center)})^2/{N(2 * scale * scale)})";
    }

    /// <summary>Reads a bound: empty, -inf or -∞ is no lower bound, inf or ∞ is no upper bound.</summary>
    public static double ReadBound(string? text, bool lower, CalcSession? session)
    {
        var t = (text ?? "").Trim().Replace('−', '-');
        if (t.Length == 0) return lower ? double.NegativeInfinity : double.PositiveInfinity;
        switch (t.ToLowerInvariant())
        {
            case "-inf" or "-∞" or "-infinity":
                return double.NegativeInfinity;
            case "inf" or "∞" or "+inf" or "infinity" or "+∞":
                return double.PositiveInfinity;
        }
        return ReadNumber(t, lower ? "the lower bound" : "the upper bound", session);
    }

    /// <summary>Reads a number typed in a field (plain numbers, or expressions such as 1/3 with a session).</summary>
    public static double ReadNumber(string? text, string what, CalcSession? session)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) throw new StatDataException($"Type a number for {what}");
        if (CellParser.PlainNumberValue(t) is double d) return d;
        var v = CellParser.Parse(t, session);
        if (v.IsNumber) return v.Number;
        throw new StatDataException($"{what} must be a number, not '{t}'");
    }

    /// <summary>Computes the explorer result for the settings.</summary>
    public static DistributionOutcome Compute(DistributionSettings s, CalcSession? session = null)
    {
        var info = Info(s.Kind);
        string title = info.Name.ToUpperInvariant();
        try
        {
            var values = new Dictionary<string, double>();
            foreach (var p in info.Parameters) values[p.Key] = ReadNumber(s.Parameter(p.Key, p.Default), p.Label, session);
            Dist d;
            try
            {
                d = Create(s.Kind, values);
            }
            catch (ArgumentException ex)
            {
                throw new StatDataException(Sentence(ex.Message));
            }
            return s.Mode switch
            {
                DistributionMode.Density => Density(info, d, values, s, session),
                DistributionMode.Inverse => Inverse(info, d, values, s, session),
                _ => Between(info, d, values, s, session),
            };
        }
        catch (StatDataException ex)
        {
            return new DistributionOutcome { Info = info, Error = ex.Message, Card = StatCard.Error(title, ex.Message) };
        }
        catch (ArgumentException ex)
        {
            return new DistributionOutcome { Info = info, Error = Sentence(ex.Message), Card = StatCard.Error(title, Sentence(ex.Message)) };
        }
    }

    private static string Sentence(string message)
    {
        int paren = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        if (paren > 0) message = message[..paren];
        return message.Length == 0 ? message : char.ToUpperInvariant(message[0]) + message[1..];
    }

    private static string ParameterText(DistributionInfo info, IReadOnlyDictionary<string, double> v) =>
        string.Join(", ", info.Parameters.Select(p => $"{p.Label} = {NumberText.Display(v[p.Key], 8)}"));

    private static string Args(DistributionInfo info, IReadOnlyDictionary<string, double> v) =>
        string.Join(", ", info.Parameters.Select(p => NumberText.Input(v[p.Key])));

    private static DistributionOutcome Between(DistributionInfo info, Dist d, IReadOnlyDictionary<string, double> v, DistributionSettings s, CalcSession? session)
    {
        double lo = ReadBound(s.Lower, true, session), hi = ReadBound(s.Upper, false, session);
        string? note = null;
        if (lo > hi)
        {
            (lo, hi) = (hi, lo);
            note = "The bounds were the wrong way round, so they were swapped";
        }
        double p = d.Probability(lo, hi);
        string rel = d.IsDiscrete ? "≤" : "<";
        string expr = (double.IsNegativeInfinity(lo), double.IsPositiveInfinity(hi)) switch
        {
            (true, true) => "P(any X)",
            (true, false) => $"P(X {rel} {B(hi)})",
            (false, true) => $"P(X {(d.IsDiscrete ? "≥" : ">")} {B(lo)})",
            _ => $"P({B(lo)} {rel} X {rel} {B(hi)})",
        };
        string headline = $"{expr} = {NumberText.Display(p, 7)}";
        string? use = info.Prefix switch
        {
            null => null,
            "binom" => $"binomcdf({Args(info, v)}, {NumberText.Input(lo)}, {NumberText.Input(hi)})",
            _ => $"{info.Prefix}cdf({(info.Prefix is "poisson" or "geomet" ? Args(info, v) + ", " + NumberText.Input(lo) + ", " + NumberText.Input(hi) : NumberText.Input(lo) + ", " + NumberText.Input(hi) + ", " + Args(info, v))})",
        };
        var tiles = new List<Tile>
        {
            new(expr, NumberText.Display(p), "The probability between the bounds", Highlight: true),
            new("outside", NumberText.Display(1 - p), "1 minus the probability"),
        };
        AddMoments(tiles, d);
        var card = Card(info, d.IsDiscrete ? $"{Upper(info)} P(A≤X≤B)" : $"{Upper(info)} P(A<X<B)", expr, p, v, use, s, note);
        return new DistributionOutcome
        {
            Info = info,
            Headline = headline,
            Value = p,
            Bounds = (lo, hi),
            Tiles = tiles,
            Graph = d.IsDiscrete ? null : CurveGraph(info, d, v, lo, hi),
            Scene = d.IsDiscrete ? Bars(d, lo, hi, null) : null,
            Card = card,
        };
    }

    private static DistributionOutcome Density(DistributionInfo info, Dist d, IReadOnlyDictionary<string, double> v, DistributionSettings s, CalcSession? session)
    {
        double x = ReadNumber(s.X, "x", session);
        double f = d.Density(x);
        string expr = d.IsDiscrete ? $"P(X = {B(x)})" : $"f({B(x)})";
        string? use = info.Prefix switch
        {
            null => null,
            "binom" or "poisson" or "geomet" => $"{info.Prefix}pdf({Args(info, v)}, {NumberText.Input(x)})",
            _ => $"{info.Prefix}pdf({NumberText.Input(x)}, {Args(info, v)})",
        };
        var tiles = new List<Tile>
        {
            new(expr, NumberText.Display(f), d.IsDiscrete ? "The probability of exactly this value" : "The height of the density curve (not a probability)", Highlight: true),
            new("P(X ≤ x)", NumberText.Display(d.Cdf(x)), "The cumulative probability"),
        };
        AddMoments(tiles, d);
        GraphSpec? graph = null;
        if (!d.IsDiscrete)
        {
            var g = CurveGraph(info, d, v, double.NaN, double.NaN);
            var marks = new List<GraphAnnotation>(g.Annotations)
            {
                new(AnnotationKind.Path, [new Point2(x, 0), new Point2(x, f)], 0),
                new(AnnotationKind.Point, [new Point2(x, f)], 0),
            };
            graph = g with { Annotations = marks };
        }
        var card = Card(info, d.IsDiscrete ? $"{Upper(info)} P(X=X)" : $"{Upper(info)} PDF", expr, f, v, use, s, null);
        return new DistributionOutcome
        {
            Info = info,
            Headline = $"{expr} = {NumberText.Display(f, 7)}",
            Value = f,
            Bounds = (x, x),
            Tiles = tiles,
            Graph = graph,
            Scene = d.IsDiscrete ? Bars(d, double.NaN, double.NaN, (int)Math.Round(x)) : null,
            Card = card,
        };
    }

    private static DistributionOutcome Inverse(DistributionInfo info, Dist d, IReadOnlyDictionary<string, double> v, DistributionSettings s, CalcSession? session)
    {
        double area = ReadNumber(s.Area, "the area", session);
        if (area > 1 && area < 100) area /= 100;
        if (!(area > 0 && area < 1)) throw new StatDataException("The area must be between 0 and 1 (or a percentage)");
        var tail = d.IsDiscrete ? Tail.Left : s.Tail;
        double lo, hi, x;
        string expr;
        switch (tail)
        {
            case Tail.Right:
                x = d.UpperQuantile(area);
                (lo, hi) = (x, double.PositiveInfinity);
                expr = $"P(X > x) = {B(area)}";
                break;
            case Tail.Center:
                lo = d.Quantile((1 - area) / 2);
                hi = d.Quantile((1 + area) / 2);
                x = hi;
                expr = $"P({B(lo)} < X < x) = {B(area)}";
                break;
            default:
                x = d.Quantile(area);
                (lo, hi) = (double.NegativeInfinity, x);
                expr = d.IsDiscrete ? $"smallest x with P(X ≤ x) ≥ {B(area)}" : $"P(X < x) = {B(area)}";
                break;
        }
        string tailArg = tail switch { Tail.Right => ", \"right\"", Tail.Center => ", \"center\"", _ => "" };
        string? use = info.Prefix switch
        {
            null or "geomet" => null,
            "normal" => $"invnorm({NumberText.Input(area)}, {Args(info, v)}{tailArg})",
            "binom" => $"invbinom({NumberText.Input(area)}, {Args(info, v)})",
            "poisson" => $"invpoisson({NumberText.Input(area)}, {Args(info, v)})",
            _ => $"inv{info.Prefix}({NumberText.Input(area)}, {Args(info, v)}{tailArg})",
        };
        var tiles = new List<Tile> { new("x", NumberText.Display(x), "The value with that area", Highlight: true) };
        if (tail == Tail.Center) tiles.Add(new Tile("lower", NumberText.Display(lo), "The other end of the central interval"));
        else tiles.Add(new Tile("P(X ≤ x)", NumberText.Display(d.Cdf(x)), "Check: the cumulative probability at x"));
        AddMoments(tiles, d);
        string headline = tail == Tail.Center
            ? $"{NumberText.Display(lo, 7)} < X < {NumberText.Display(hi, 7)}"
            : $"x = {NumberText.Display(x, 7)}";
        var card = Card(info, $"INV {Upper(info)}", expr, x, v, use, s, tail == Tail.Center ? $"lower = {NumberText.Display(lo, 10)}, upper = {NumberText.Display(hi, 10)}" : null);
        return new DistributionOutcome
        {
            Info = info,
            Headline = headline,
            Value = x,
            Bounds = (lo, hi),
            Tiles = tiles,
            Graph = d.IsDiscrete ? null : CurveGraph(info, d, v, lo, hi),
            Scene = d.IsDiscrete ? Bars(d, lo, hi, null) : null,
            Card = card,
        };
    }

    private static string Upper(DistributionInfo info) => info.Kind switch
    {
        DistributionKind.ChiSquared => "CHI2",
        DistributionKind.StudentT => "T",
        _ => info.Name.ToUpperInvariant(),
    };

    private static void AddMoments(List<Tile> tiles, Dist d)
    {
        tiles.Add(new Tile("mean", NumberText.Display(d.Mean), "The expected value"));
        tiles.Add(new Tile("std dev", NumberText.Display(d.StandardDeviation), "The standard deviation"));
    }

    private static string B(double v) => double.IsPositiveInfinity(v) ? "∞" : double.IsNegativeInfinity(v) ? "-∞" : NumberText.Display(v, 8);

    private static StatCard Card(DistributionInfo info, string title, string expr, double value, IReadOnlyDictionary<string, double> v, string? use,
        DistributionSettings s, string? note)
    {
        string parameters = ParameterText(info, v);
        string headline = $"{expr}\n= {NumberText.Display(value, NumberText.CardDigits)}";
        return new StatCard
        {
            Title = title,
            Headline = headline,
            Lines = [.. info.Parameters.Select(p => new CardLine(p.Label, NumberText.Display(v[p.Key], 10)))],
            Note = note,
            CopyText = $"{info.Name} distribution, {parameters}\n{expr} = {NumberText.Plain(value)}" + (note is null ? "" : "\n" + note),
            Latex = Latex(expr) + " = " + NumberText.Latex(value),
            UseText = use ?? NumberText.ShortInput(value),
            Recipe = new StatRecipe { Tab = StatTab.Distribution, Distribution = s.Clone() },
        };
    }

    private static string Latex(string expr) => expr.Replace("≤", "\\le ").Replace("≥", "\\ge ").Replace("∞", "\\infty");

    /// <summary>The density curve over a readable window, with the area between the bounds shaded.</summary>
    public static GraphSpec CurveGraph(DistributionInfo info, Dist d, IReadOnlyDictionary<string, double> v, double lo, double hi)
    {
        var formula = PdfFormula(info.Kind, v) ?? throw new StatDataException("No curve for this distribution");
        var (x0, x1) = Window(d, info.Kind, lo, hi);
        double ymax = CurveHeight(d.Density, x0, x1);
        var view = new Viewport(x0, x1, -0.2 * ymax, 1.18 * ymax);
        var shades = new List<GraphAnnotation>();
        if (double.IsFinite(lo) || double.IsFinite(hi))
        {
            double a = Math.Max(double.IsFinite(lo) ? lo : x0 - (x1 - x0), Math.Max(x0 - (x1 - x0), d.SupportMin));
            double b = Math.Min(double.IsFinite(hi) ? hi : x1 + (x1 - x0), Math.Min(x1 + (x1 - x0), d.SupportMax));
            var poly = GraphSpec.AreaUnder(x => Math.Min(d.Density(x), ymax * 4), a, b, 160);
            shades.AddRange(GraphSpec.Shade(poly, 0, layers: 2));
        }
        return new GraphSpec([new GraphLine(formula, 0)], view, shades);
    }

    /// <summary>The x window: the central 99.9% (98% for heavy tails), widened to show finite bounds.</summary>
    public static (double Min, double Max) Window(Dist d, DistributionKind kind, double lo, double hi)
    {
        double tail = kind == DistributionKind.Cauchy ? 0.04 : kind is DistributionKind.F or DistributionKind.LogNormal ? 0.005 : 0.0005;
        double a = d.Quantile(tail), b = d.Quantile(1 - tail);
        if (double.IsFinite(d.SupportMin) && d.SupportMin >= 0 && a < (b - a) * 0.15) a = d.SupportMin;
        if (kind == DistributionKind.Uniform) { double w = d.SupportMax - d.SupportMin; a = d.SupportMin - w * 0.15; b = d.SupportMax + w * 0.15; }
        double span = b - a;
        if (!(span > 0) || !double.IsFinite(span)) { a = -5; b = 5; span = 10; }
        if (double.IsFinite(lo)) a = Math.Min(a, Math.Max(lo - span * 0.08, a - span));
        if (double.IsFinite(hi)) b = Math.Max(b, Math.Min(hi + span * 0.08, b + span));
        double pad = (b - a) * 0.04;
        // A curve that starts at its support (0 for χ², F, exponential, ...) gets room on the left for the y labels.
        return (a - (a == d.SupportMin ? (b - a) * 0.09 : pad), b + pad);
    }

    /// <summary>The height to show: the tallest point of the density in the window, ignoring a spike at an end.</summary>
    public static double CurveHeight(Func<double, double> pdf, double x0, double x1)
    {
        var ys = new List<double>();
        for (int i = 1; i < 200; i++)
        {
            double y = pdf(x0 + (x1 - x0) * i / 200);
            if (double.IsFinite(y)) ys.Add(y);
        }
        if (ys.Count == 0) return 1;
        ys.Sort();
        double top = ys[^1];
        double typical = ys[(int)(ys.Count * 0.9)];
        if (top > typical * 4 && typical > 0) top = typical * 2.5;
        return top > 0 ? top : 1;
    }

    /// <summary>Bars of P(X = k) around the bulk of a discrete distribution, highlighting [lo, hi] or one marked value.</summary>
    public static ChartScene Bars(Dist d, double lo, double hi, int? marked)
    {
        double a = Math.Max(d.SupportMin, d.Quantile(0.0005)), b = Math.Min(d.SupportMax, d.Quantile(0.9995));
        if (double.IsFinite(lo)) a = Math.Max(d.SupportMin, Math.Min(a, lo));
        if (double.IsFinite(hi)) b = Math.Min(Math.Max(b, hi), Math.Max(b, a + 400));
        if (marked is int m) { a = Math.Max(d.SupportMin, Math.Min(a, m)); b = Math.Max(b, m); }
        int first = (int)Math.Max(-1e6, Math.Floor(a));
        int last = (int)Math.Min(first + 400, Math.Ceiling(b));
        var probs = Enumerable.Range(first, Math.Max(1, last - first + 1)).Select(k => d.Density(k)).ToList();
        return ChartBuilder.PmfScene(first, probs, lo, hi, marked);
    }

    /// <summary>Invariant text for a parameter default.</summary>
    public static string Format(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
