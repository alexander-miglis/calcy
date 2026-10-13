using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>Which tail an inverse distribution function refers to (TI-84 invNorm LEFT, CENTER, RIGHT).</summary>
public enum Tail
{
    /// <summary>The area lies to the left of the returned value.</summary>
    Left,

    /// <summary>The area lies symmetrically around the mean; the upper bound is returned.</summary>
    Center,

    /// <summary>The area lies to the right of the returned value.</summary>
    Right,
}

/// <summary>
/// TI-84 and Casio style distribution functions (normalpdf, normalcdf, invNorm, tcdf, binompdf, ...).
/// Argument order follows the TI-84. Interval functions accept infinite bounds (or the TI idiom -1E99, 1E99).
/// Continuous interval functions return the integral of the density from lower to upper, so swapped bounds give
/// a negative value as on the TI-84. Discrete interval functions include both ends.
/// </summary>
public static class CalcDistributions
{
    // ------------------------------------------------------------------ normal

    /// <summary>normalpdf(x, mu, sigma).</summary>
    public static double NormalPdf(double x, double mu = 0, double sigma = 1) => new NormalDistribution(mu, sigma).Pdf(x);

    /// <summary>normalcdf(lower, upper, mu, sigma): P(lower &lt; X &lt; upper).</summary>
    public static double NormalCdf(double lower, double upper, double mu = 0, double sigma = 1)
        => new NormalDistribution(mu, sigma).Probability(lower, upper);

    /// <summary>
    /// invNorm(area, mu, sigma, tail). Left: P(X &lt; x) = area. Right: P(X &gt; x) = area.
    /// Center: P(2 mu - x &lt; X &lt; x) = area, and the upper bound x is returned (see <see cref="InvNormCenter"/>).
    /// </summary>
    public static double InvNorm(double area, double mu = 0, double sigma = 1, Tail tail = Tail.Left)
    {
        var d = new NormalDistribution(mu, sigma);
        CheckArea(area);
        return tail switch
        {
            Tail.Left => d.InverseCdf(area),
            Tail.Right => d.InverseSurvival(area),
            _ => d.InverseSurvival((1 - area) / 2),
        };
    }

    /// <summary>Both bounds of the central interval that holds the given area of N(mu, sigma).</summary>
    public static (double Lower, double Upper) InvNormCenter(double area, double mu = 0, double sigma = 1)
    {
        double upper = InvNorm(area, mu, sigma, Tail.Center);
        return (2 * mu - upper, upper);
    }

    // ------------------------------------------------------------------ t

    /// <summary>tpdf(x, df).</summary>
    public static double TPdf(double x, double df) => new StudentTDistribution(df).Pdf(x);

    /// <summary>tcdf(lower, upper, df).</summary>
    public static double TCdf(double lower, double upper, double df) => new StudentTDistribution(df).Probability(lower, upper);

    /// <summary>invT(area, df, tail). Center returns the positive bound of the central interval.</summary>
    public static double InvT(double area, double df, Tail tail = Tail.Left)
    {
        var d = new StudentTDistribution(df);
        CheckArea(area);
        return tail switch
        {
            Tail.Left => d.InverseCdf(area),
            Tail.Right => d.InverseSurvival(area),
            _ => d.InverseSurvival((1 - area) / 2),
        };
    }

    // ------------------------------------------------------------------ chi-squared

    /// <summary>chi2pdf(x, df).</summary>
    public static double Chi2Pdf(double x, double df) => new ChiSquaredDistribution(df).Pdf(x);

    /// <summary>chi2cdf(lower, upper, df).</summary>
    public static double Chi2Cdf(double lower, double upper, double df) => new ChiSquaredDistribution(df).Probability(lower, upper);

    /// <summary>
    /// Inverse chi-squared (Casio InvChiCD). Left: P(X &lt; x) = area. Right (the Casio convention): P(X &gt; x) = area.
    /// Center returns the upper bound of the equal tailed interval.
    /// </summary>
    public static double InvChi2(double area, double df, Tail tail = Tail.Left)
    {
        var d = new ChiSquaredDistribution(df);
        CheckArea(area);
        return tail switch
        {
            Tail.Left => d.InverseCdf(area),
            Tail.Right => d.InverseSurvival(area),
            _ => d.InverseSurvival((1 - area) / 2),
        };
    }

    // ------------------------------------------------------------------ F

    /// <summary>Fpdf(x, numerator df, denominator df).</summary>
    public static double FPdf(double x, double d1, double d2) => new FDistribution(d1, d2).Pdf(x);

    /// <summary>Fcdf(lower, upper, numerator df, denominator df).</summary>
    public static double FCdf(double lower, double upper, double d1, double d2) => new FDistribution(d1, d2).Probability(lower, upper);

    /// <summary>Inverse F (Casio InvFCD). Right: P(X &gt; x) = area. Center returns the upper bound of the equal tailed interval.</summary>
    public static double InvF(double area, double d1, double d2, Tail tail = Tail.Left)
    {
        var d = new FDistribution(d1, d2);
        CheckArea(area);
        return tail switch
        {
            Tail.Left => d.InverseCdf(area),
            Tail.Right => d.InverseSurvival(area),
            _ => d.InverseSurvival((1 - area) / 2),
        };
    }

    // ------------------------------------------------------------------ binomial

    /// <summary>binompdf(trials, p, x): P(X = x).</summary>
    public static double BinomPdf(double n, double p, double x)
    {
        var d = new BinomialDistribution(n, p);
        Guard.Integer(x, "x", nameof(x));
        return d.Pmf(x);
    }

    /// <summary>binompdf(trials, p): the list of P(X = 0), ..., P(X = n).</summary>
    public static double[] BinomPdf(double n, double p)
    {
        var d = new BinomialDistribution(n, p);
        CheckListSize(n);
        var r = new double[(int)n + 1];
        for (int k = 0; k <= (int)n; k++) r[k] = d.Pmf(k);
        return r;
    }

    /// <summary>binomcdf(trials, p, x): P(X &lt;= x).</summary>
    public static double BinomCdf(double n, double p, double x)
    {
        var d = new BinomialDistribution(n, p);
        Guard.Integer(x, "x", nameof(x));
        return d.Cdf(x);
    }

    /// <summary>binomcdf(trials, p, lower, upper): P(lower &lt;= X &lt;= upper) (TI-84 CE and Casio form).</summary>
    public static double BinomCdf(double n, double p, double lower, double upper) => new BinomialDistribution(n, p).Probability(lower, upper);

    /// <summary>binomcdf(trials, p): the list of P(X &lt;= 0), ..., P(X &lt;= n).</summary>
    public static double[] BinomCdf(double n, double p)
    {
        var d = new BinomialDistribution(n, p);
        CheckListSize(n);
        var r = new double[(int)n + 1];
        for (int k = 0; k <= (int)n; k++) r[k] = d.Cdf(k);
        return r;
    }

    /// <summary>invBinom(area, trials, p): the smallest x with P(X &lt;= x) &gt;= area.</summary>
    public static double InvBinom(double area, double n, double p)
    {
        var d = new BinomialDistribution(n, p);
        CheckArea(area);
        return d.InverseCdf(area);
    }

    // ------------------------------------------------------------------ Poisson

    /// <summary>poissonpdf(mu, x).</summary>
    public static double PoissonPdf(double mu, double x)
    {
        var d = new PoissonDistribution(mu);
        Guard.Integer(x, "x", nameof(x));
        return d.Pmf(x);
    }

    /// <summary>poissoncdf(mu, x): P(X &lt;= x).</summary>
    public static double PoissonCdf(double mu, double x)
    {
        var d = new PoissonDistribution(mu);
        Guard.Integer(x, "x", nameof(x));
        return d.Cdf(x);
    }

    /// <summary>poissoncdf(mu, lower, upper): P(lower &lt;= X &lt;= upper).</summary>
    public static double PoissonCdf(double mu, double lower, double upper) => new PoissonDistribution(mu).Probability(lower, upper);

    /// <summary>invPoisson(area, mu): the smallest x with P(X &lt;= x) &gt;= area.</summary>
    public static double InvPoisson(double area, double mu)
    {
        var d = new PoissonDistribution(mu);
        CheckArea(area);
        return d.InverseCdf(area);
    }

    // ------------------------------------------------------------------ geometric

    /// <summary>geometpdf(p, x): probability that the first success is on trial x (x = 1, 2, ...).</summary>
    public static double GeometPdf(double p, double x)
    {
        var d = new GeometricDistribution(p);
        Guard.Integer(x, "x", nameof(x));
        return d.Pmf(x);
    }

    /// <summary>geometcdf(p, x): probability that the first success is on or before trial x.</summary>
    public static double GeometCdf(double p, double x)
    {
        var d = new GeometricDistribution(p);
        Guard.Integer(x, "x", nameof(x));
        return d.Cdf(x);
    }

    /// <summary>geometcdf(p, lower, upper): P(lower &lt;= X &lt;= upper).</summary>
    public static double GeometCdf(double p, double lower, double upper) => new GeometricDistribution(p).Probability(lower, upper);

    private static void CheckArea(double area)
    {
        if (!(area >= 0 && area <= 1)) throw new ArgumentException("the area must be between 0 and 1", nameof(area));
    }

    private static void CheckListSize(double n)
    {
        if (n > 10_000_000) throw new ArgumentException("the number of trials is too large to list every probability", nameof(n));
    }
}
