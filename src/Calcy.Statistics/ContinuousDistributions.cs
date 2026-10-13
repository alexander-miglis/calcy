using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>Base record for continuous probability distributions.</summary>
public abstract record ContinuousDistribution
{
    /// <summary>The mean (NaN when it does not exist, infinity when it diverges).</summary>
    public abstract double Mean { get; }

    /// <summary>The variance (NaN when it does not exist, infinity when it diverges).</summary>
    public abstract double Variance { get; }

    /// <summary>The standard deviation, sqrt(Variance).</summary>
    public double StandardDeviation => Math.Sqrt(Variance);

    /// <summary>Smallest value of the support.</summary>
    public abstract double SupportMin { get; }

    /// <summary>Largest value of the support.</summary>
    public abstract double SupportMax { get; }

    /// <summary>Probability density at x.</summary>
    public abstract double Pdf(double x);

    /// <summary>Cumulative distribution P(X &lt;= x).</summary>
    public abstract double Cdf(double x);

    /// <summary>Survival function P(X &gt; x), accurate in the upper tail.</summary>
    public virtual double Survival(double x) => 1.0 - Cdf(x);

    /// <summary>Quantile function: the x with Cdf(x) = p, for 0 &lt;= p &lt;= 1.</summary>
    public abstract double InverseCdf(double p);

    /// <summary>Inverse survival function: the x with P(X &gt; x) = q, accurate for tiny q.</summary>
    public virtual double InverseSurvival(double q)
    {
        CheckProbability(q);
        return InverseCdf(1.0 - q);
    }

    /// <summary>The median, InverseCdf(0.5).</summary>
    public virtual double Median => InverseCdf(0.5);

    /// <summary>
    /// P(lower &lt; X &lt; upper). Bounds may be infinite. When lower &gt; upper the result is the negative of
    /// P(upper &lt; X &lt; lower), matching the integral of the density from lower to upper (TI-84 behaviour).
    /// The tail used for each difference is chosen to avoid cancellation, so far tail areas keep full precision.
    /// </summary>
    public double Probability(double lower, double upper)
    {
        if (double.IsNaN(lower) || double.IsNaN(upper)) throw new ArgumentException("the bounds must be numbers");
        if (lower > upper) return -Probability(upper, lower);
        if (lower == upper) return 0.0;
        double cu = Cdf(upper);
        if (cu <= 0.5) return Math.Max(0.0, cu - Cdf(lower));
        return Math.Max(0.0, Survival(lower) - Survival(upper));
    }

    /// <summary>Throws when p is not a probability.</summary>
    protected static void CheckProbability(double p)
    {
        if (!(p >= 0 && p <= 1)) throw new ArgumentException("the probability must be between 0 and 1", nameof(p));
    }
}

/// <summary>Normal (Gaussian) distribution N(mu, sigma^2).</summary>
public sealed record NormalDistribution : ContinuousDistribution
{
    /// <summary>Creates a normal distribution with mean mu and standard deviation sigma.</summary>
    public NormalDistribution(double mu = 0, double sigma = 1)
    {
        Guard.Finite(mu, "the mean", nameof(mu));
        Guard.PositiveFinite(sigma, "the standard deviation", nameof(sigma));
        Mu = mu;
        Sigma = sigma;
    }

    /// <summary>Location (mean) parameter.</summary>
    public double Mu { get; }

    /// <summary>Scale (standard deviation) parameter.</summary>
    public double Sigma { get; }

    /// <inheritdoc/>
    public override double Mean => Mu;
    /// <inheritdoc/>
    public override double Variance => Sigma * Sigma;
    /// <inheritdoc/>
    public override double SupportMin => double.NegativeInfinity;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;
    /// <inheritdoc/>
    public override double Median => Mu;

    /// <inheritdoc/>
    public override double Pdf(double x) => StdNormal.Pdf((x - Mu) / Sigma) / Sigma;

    /// <inheritdoc/>
    public override double Cdf(double x) => StdNormal.Cdf((x - Mu) / Sigma);

    /// <inheritdoc/>
    public override double Survival(double x) => StdNormal.Cdf((Mu - x) / Sigma);

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return Mu + Sigma * StdNormal.Quantile(p, 1.0 - p);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return Mu - Sigma * StdNormal.Quantile(q, 1.0 - q);
    }

    /// <summary>The standard normal distribution N(0, 1).</summary>
    public static NormalDistribution Standard { get; } = new();
}

/// <summary>Student's t distribution with (possibly non-integer, possibly infinite) degrees of freedom.</summary>
public sealed record StudentTDistribution : ContinuousDistribution
{
    /// <summary>Creates a t distribution with the given degrees of freedom (positive; +infinity gives the standard normal).</summary>
    public StudentTDistribution(double degreesOfFreedom)
    {
        Guard.Positive(degreesOfFreedom, "the degrees of freedom", nameof(degreesOfFreedom));
        DegreesOfFreedom = degreesOfFreedom;
    }

    /// <summary>Degrees of freedom.</summary>
    public double DegreesOfFreedom { get; }

    /// <inheritdoc/>
    public override double Mean => DegreesOfFreedom > 1 ? 0.0 : double.NaN;
    /// <inheritdoc/>
    public override double Variance => DegreesOfFreedom > 2 ? (double.IsPositiveInfinity(DegreesOfFreedom) ? 1.0 : DegreesOfFreedom / (DegreesOfFreedom - 2))
        : DegreesOfFreedom > 1 ? double.PositiveInfinity : double.NaN;
    /// <inheritdoc/>
    public override double SupportMin => double.NegativeInfinity;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;
    /// <inheritdoc/>
    public override double Median => 0.0;

    /// <inheritdoc/>
    public override double Pdf(double x)
    {
        double n = DegreesOfFreedom;
        if (double.IsNaN(x)) return x;
        if (double.IsInfinity(x)) return 0.0;
        if (double.IsPositiveInfinity(n)) return StdNormal.Pdf(x);
        double t = -DistKernels.Bd0(n / 2.0, (n + 1) / 2.0) + DistKernels.StirlingError((n + 1) / 2.0) - DistKernels.StirlingError(n / 2.0);
        double x2n = x * x / n;
        double lx2n, u;
        bool large = x2n > 1.0 / Num.Eps;
        double ax = Math.Abs(x);
        if (large)
        {
            lx2n = Math.Log(ax) - Math.Log(n) / 2.0;
            u = n * lx2n;
        }
        else if (x2n > 0.2)
        {
            lx2n = Math.Log(1 + x2n) / 2.0;
            u = n * lx2n;
        }
        else
        {
            lx2n = Num.Log1p(x2n) / 2.0;
            u = -DistKernels.Bd0(n / 2.0, (n + x * x) / 2.0) + x * x / 2.0;
        }
        double isqrt = large ? Math.Sqrt(n) / ax : Math.Exp(-lx2n);
        return Math.Exp(t - u) * Num.InvSqrt2Pi * isqrt;
    }

    /// <summary>P(T &gt; |t|) computed through the incomplete beta function.</summary>
    private double UpperTail(double t)
    {
        double n = DegreesOfFreedom;
        if (double.IsPositiveInfinity(n)) return StdNormal.Cdf(-Math.Abs(t));
        if (double.IsInfinity(t)) return 0.0;
        double r = t * t / n;
        double x, y;
        if (double.IsPositiveInfinity(r))
        {
            return 0.0;
        }
        x = 1.0 / (1.0 + r);
        y = r / (1.0 + r);
        SpecialFunctions.IncompleteBeta(n / 2.0, 0.5, x, y, out double ix, out _);
        return 0.5 * ix;
    }

    /// <inheritdoc/>
    public override double Cdf(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x == 0) return 0.5;
        double tail = UpperTail(x);
        return x < 0 ? tail : 1.0 - tail;
    }

    /// <inheritdoc/>
    public override double Survival(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x == 0) return 0.5;
        double tail = UpperTail(x);
        return x > 0 ? tail : 1.0 - tail;
    }

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return Quantile(p, 1.0 - p);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return -Quantile(q, 1.0 - q);
    }

    private double Quantile(double p, double q)
    {
        double n = DegreesOfFreedom;
        if (p == 0) return double.NegativeInfinity;
        if (q == 0) return double.PositiveInfinity;
        if (p == 0.5) return 0.0;
        if (double.IsPositiveInfinity(n)) return StdNormal.Quantile(p, q);
        double pm = Math.Min(p, q);
        double sign = p < q ? -1.0 : 1.0;
        double magnitude;
        if (n == 1)
        {
            magnitude = 1.0 / Num.TanPi(pm);
        }
        else if (n == 2)
        {
            magnitude = (1.0 - 2.0 * pm) / Math.Sqrt(2.0 * pm * (1.0 - pm));
        }
        else
        {
            double alpha = 2.0 * pm;
            var (bx, by) = SpecialFunctions.IncompleteBetaInverse(n / 2.0, 0.5, alpha, 1.0 - alpha);
            magnitude = bx == 0 ? double.PositiveInfinity : Math.Sqrt(n * (by / bx));
        }
        return sign * magnitude;
    }
}

/// <summary>Chi-squared distribution with k degrees of freedom.</summary>
public sealed record ChiSquaredDistribution : ContinuousDistribution
{
    /// <summary>Creates a chi-squared distribution with the given (positive, possibly non-integer) degrees of freedom.</summary>
    public ChiSquaredDistribution(double degreesOfFreedom)
    {
        Guard.PositiveFinite(degreesOfFreedom, "the degrees of freedom", nameof(degreesOfFreedom));
        DegreesOfFreedom = degreesOfFreedom;
    }

    /// <summary>Degrees of freedom.</summary>
    public double DegreesOfFreedom { get; }

    /// <inheritdoc/>
    public override double Mean => DegreesOfFreedom;
    /// <inheritdoc/>
    public override double Variance => 2 * DegreesOfFreedom;
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;

    /// <inheritdoc/>
    public override double Pdf(double x) => GammaDistribution.GammaPdf(x, DegreesOfFreedom / 2.0, 2.0);

    /// <inheritdoc/>
    public override double Cdf(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 0.0;
        SpecialFunctions.IncompleteGamma(DegreesOfFreedom / 2.0, x / 2.0, out double p, out _);
        return p;
    }

    /// <inheritdoc/>
    public override double Survival(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 1.0;
        SpecialFunctions.IncompleteGamma(DegreesOfFreedom / 2.0, x / 2.0, out _, out double q);
        return q;
    }

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return 2.0 * SpecialFunctions.GammaPInverse(DegreesOfFreedom / 2.0, p, 1.0 - p);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return 2.0 * SpecialFunctions.GammaPInverse(DegreesOfFreedom / 2.0, 1.0 - q, q);
    }
}

/// <summary>Fisher-Snedecor F distribution with d1 (numerator) and d2 (denominator) degrees of freedom.</summary>
public sealed record FDistribution : ContinuousDistribution
{
    /// <summary>Creates an F distribution.</summary>
    public FDistribution(double numeratorDegreesOfFreedom, double denominatorDegreesOfFreedom)
    {
        Guard.PositiveFinite(numeratorDegreesOfFreedom, "the numerator degrees of freedom", nameof(numeratorDegreesOfFreedom));
        Guard.PositiveFinite(denominatorDegreesOfFreedom, "the denominator degrees of freedom", nameof(denominatorDegreesOfFreedom));
        NumeratorDegreesOfFreedom = numeratorDegreesOfFreedom;
        DenominatorDegreesOfFreedom = denominatorDegreesOfFreedom;
    }

    /// <summary>Numerator degrees of freedom d1.</summary>
    public double NumeratorDegreesOfFreedom { get; }

    /// <summary>Denominator degrees of freedom d2.</summary>
    public double DenominatorDegreesOfFreedom { get; }

    /// <inheritdoc/>
    public override double Mean => DenominatorDegreesOfFreedom > 2 ? DenominatorDegreesOfFreedom / (DenominatorDegreesOfFreedom - 2) : double.NaN;

    /// <inheritdoc/>
    public override double Variance
    {
        get
        {
            double d1 = NumeratorDegreesOfFreedom, d2 = DenominatorDegreesOfFreedom;
            if (d2 > 4) return 2 * d2 * d2 * (d1 + d2 - 2) / (d1 * (d2 - 2) * (d2 - 2) * (d2 - 4));
            return d2 > 2 ? double.PositiveInfinity : double.NaN;
        }
    }

    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;

    /// <inheritdoc/>
    public override double Pdf(double x)
    {
        double m = NumeratorDegreesOfFreedom, n = DenominatorDegreesOfFreedom;
        if (double.IsNaN(x)) return x;
        if (x < 0 || double.IsPositiveInfinity(x)) return 0.0;
        if (x == 0) return m > 2 ? 0.0 : (m == 2 ? 1.0 : double.PositiveInfinity);
        double f = 1.0 / (n + x * m);
        double q = n * f;
        double p = x * m * f;
        double dens;
        if (m >= 2)
        {
            f = m * q / 2;
            dens = DistKernels.BinomRaw((m - 2) / 2, (m + n - 2) / 2, p, q);
        }
        else
        {
            f = m * m * q / (2 * p * (m + n));
            dens = DistKernels.BinomRaw(m / 2, (m + n) / 2, p, q);
        }
        return f * dens;
    }

    private void Tails(double x, out double lower, out double upper)
    {
        double d1 = NumeratorDegreesOfFreedom, d2 = DenominatorDegreesOfFreedom;
        double r = d1 * x / d2;
        double bx = r / (1 + r), by = 1 / (1 + r);
        if (double.IsPositiveInfinity(r)) { bx = 1; by = 0; }
        SpecialFunctions.IncompleteBeta(d1 / 2, d2 / 2, bx, by, out lower, out upper);
    }

    /// <inheritdoc/>
    public override double Cdf(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 0.0;
        Tails(x, out double lower, out _);
        return lower;
    }

    /// <inheritdoc/>
    public override double Survival(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 1.0;
        Tails(x, out _, out double upper);
        return upper;
    }

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return Quantile(p, 1.0 - p);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return Quantile(1.0 - q, q);
    }

    private double Quantile(double p, double q)
    {
        if (p == 0) return 0.0;
        if (q == 0) return double.PositiveInfinity;
        double d1 = NumeratorDegreesOfFreedom, d2 = DenominatorDegreesOfFreedom;
        var (bx, by) = SpecialFunctions.IncompleteBetaInverse(d1 / 2, d2 / 2, p, q);
        if (by == 0) return double.PositiveInfinity;
        return d2 / d1 * (bx / by);
    }
}

/// <summary>Exponential distribution with rate lambda (mean 1 / lambda).</summary>
public sealed record ExponentialDistribution : ContinuousDistribution
{
    /// <summary>Creates an exponential distribution with the given rate.</summary>
    public ExponentialDistribution(double rate = 1)
    {
        Guard.PositiveFinite(rate, "the rate", nameof(rate));
        Rate = rate;
    }

    /// <summary>Creates an exponential distribution with the given mean (scale).</summary>
    public static ExponentialDistribution FromMean(double mean)
    {
        Guard.PositiveFinite(mean, "the mean", nameof(mean));
        return new ExponentialDistribution(1.0 / mean);
    }

    /// <summary>Rate parameter lambda.</summary>
    public double Rate { get; }

    /// <inheritdoc/>
    public override double Mean => 1.0 / Rate;
    /// <inheritdoc/>
    public override double Variance => 1.0 / (Rate * Rate);
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;
    /// <inheritdoc/>
    public override double Pdf(double x) => double.IsNaN(x) ? x : x < 0 ? 0.0 : Rate * Math.Exp(-Rate * x);
    /// <inheritdoc/>
    public override double Cdf(double x) => double.IsNaN(x) ? x : x <= 0 ? 0.0 : -Num.Expm1(-Rate * x);
    /// <inheritdoc/>
    public override double Survival(double x) => double.IsNaN(x) ? x : x <= 0 ? 1.0 : Math.Exp(-Rate * x);

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return -Num.Log1p(-p) / Rate;
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return -Math.Log(q) / Rate;
    }
}

/// <summary>Continuous uniform distribution on [a, b].</summary>
public sealed record UniformDistribution : ContinuousDistribution
{
    /// <summary>Creates a uniform distribution on [lower, upper].</summary>
    public UniformDistribution(double lower = 0, double upper = 1)
    {
        Guard.Finite(lower, "the lower bound", nameof(lower));
        Guard.Finite(upper, "the upper bound", nameof(upper));
        if (!(upper > lower)) throw new ArgumentException("the upper bound must be greater than the lower bound", nameof(upper));
        Lower = lower;
        Upper = upper;
    }

    /// <summary>Lower bound a.</summary>
    public double Lower { get; }

    /// <summary>Upper bound b.</summary>
    public double Upper { get; }

    /// <inheritdoc/>
    public override double Mean => 0.5 * (Lower + Upper);
    /// <inheritdoc/>
    public override double Variance => (Upper - Lower) * (Upper - Lower) / 12.0;
    /// <inheritdoc/>
    public override double SupportMin => Lower;
    /// <inheritdoc/>
    public override double SupportMax => Upper;
    /// <inheritdoc/>
    public override double Pdf(double x) => double.IsNaN(x) ? x : (x < Lower || x > Upper) ? 0.0 : 1.0 / (Upper - Lower);
    /// <inheritdoc/>
    public override double Cdf(double x) => double.IsNaN(x) ? x : x <= Lower ? 0.0 : x >= Upper ? 1.0 : (x - Lower) / (Upper - Lower);
    /// <inheritdoc/>
    public override double Survival(double x) => double.IsNaN(x) ? x : x <= Lower ? 1.0 : x >= Upper ? 0.0 : (Upper - x) / (Upper - Lower);

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return p == 1 ? Upper : Lower + p * (Upper - Lower);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return q == 1 ? Lower : Upper - q * (Upper - Lower);
    }
}

/// <summary>Gamma distribution with shape k and scale theta (mean k theta).</summary>
public sealed record GammaDistribution : ContinuousDistribution
{
    /// <summary>Creates a gamma distribution with the given shape and scale.</summary>
    public GammaDistribution(double shape, double scale = 1)
    {
        Guard.PositiveFinite(shape, "the shape", nameof(shape));
        Guard.PositiveFinite(scale, "the scale", nameof(scale));
        Shape = shape;
        Scale = scale;
    }

    /// <summary>Creates a gamma distribution from shape and rate (rate = 1 / scale).</summary>
    public static GammaDistribution FromShapeRate(double shape, double rate)
    {
        Guard.PositiveFinite(rate, "the rate", nameof(rate));
        return new GammaDistribution(shape, 1.0 / rate);
    }

    /// <summary>Shape parameter k.</summary>
    public double Shape { get; }

    /// <summary>Scale parameter theta.</summary>
    public double Scale { get; }

    /// <inheritdoc/>
    public override double Mean => Shape * Scale;
    /// <inheritdoc/>
    public override double Variance => Shape * Scale * Scale;
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;

    internal static double GammaPdf(double x, double shape, double scale)
    {
        if (double.IsNaN(x)) return x;
        if (x < 0 || double.IsPositiveInfinity(x)) return 0.0;
        if (x == 0)
        {
            if (shape < 1) return double.PositiveInfinity;
            if (shape > 1) return 0.0;
            return 1.0 / scale;
        }
        if (shape < 1)
        {
            double pr = DistKernels.PoissonRaw(shape, x / scale);
            return pr * shape / x;
        }
        return DistKernels.PoissonRaw(shape - 1, x / scale) / scale;
    }

    /// <inheritdoc/>
    public override double Pdf(double x) => GammaPdf(x, Shape, Scale);

    /// <inheritdoc/>
    public override double Cdf(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 0.0;
        SpecialFunctions.IncompleteGamma(Shape, x / Scale, out double p, out _);
        return p;
    }

    /// <inheritdoc/>
    public override double Survival(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 1.0;
        SpecialFunctions.IncompleteGamma(Shape, x / Scale, out _, out double q);
        return q;
    }

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return Scale * SpecialFunctions.GammaPInverse(Shape, p, 1.0 - p);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return Scale * SpecialFunctions.GammaPInverse(Shape, 1.0 - q, q);
    }
}

/// <summary>Beta distribution on [0, 1] with shape parameters alpha and beta.</summary>
public sealed record BetaDistribution : ContinuousDistribution
{
    /// <summary>Creates a beta distribution.</summary>
    public BetaDistribution(double alpha, double beta)
    {
        Guard.PositiveFinite(alpha, "alpha", nameof(alpha));
        Guard.PositiveFinite(beta, "beta", nameof(beta));
        Alpha = alpha;
        Beta = beta;
    }

    /// <summary>First shape parameter.</summary>
    public double Alpha { get; }

    /// <summary>Second shape parameter.</summary>
    public double Beta { get; }

    /// <inheritdoc/>
    public override double Mean => Alpha / (Alpha + Beta);
    /// <inheritdoc/>
    public override double Variance => Alpha * Beta / ((Alpha + Beta) * (Alpha + Beta) * (Alpha + Beta + 1));
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => 1.0;

    /// <inheritdoc/>
    public override double Pdf(double x)
    {
        double a = Alpha, b = Beta;
        if (double.IsNaN(x)) return x;
        if (x < 0 || x > 1) return 0.0;
        if (x == 0) return a > 1 ? 0.0 : a < 1 ? double.PositiveInfinity : b;
        if (x == 1) return b > 1 ? 0.0 : b < 1 ? double.PositiveInfinity : a;
        double lval;
        if (a <= 2 || b <= 2) lval = (a - 1) * Math.Log(x) + (b - 1) * Num.Log1p(-x) - SpecialFunctions.LogBeta(a, b);
        else lval = Math.Log(a + b - 1) + DistKernels.BinomRaw(a - 1, a + b - 2, x, 1 - x, log: true);
        return Math.Exp(lval);
    }

    /// <inheritdoc/>
    public override double Cdf(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 0.0;
        if (x >= 1) return 1.0;
        SpecialFunctions.IncompleteBeta(Alpha, Beta, x, 1 - x, out double ix, out _);
        return ix;
    }

    /// <inheritdoc/>
    public override double Survival(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0) return 1.0;
        if (x >= 1) return 0.0;
        SpecialFunctions.IncompleteBeta(Alpha, Beta, x, 1 - x, out _, out double iy);
        return iy;
    }

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return SpecialFunctions.IncompleteBetaInverse(Alpha, Beta, p, 1 - p).X;
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return SpecialFunctions.IncompleteBetaInverse(Alpha, Beta, 1 - q, q).X;
    }
}

/// <summary>Log-normal distribution: ln X is normal with mean mu and standard deviation sigma.</summary>
public sealed record LogNormalDistribution : ContinuousDistribution
{
    /// <summary>Creates a log-normal distribution from the parameters of the underlying normal.</summary>
    public LogNormalDistribution(double mu = 0, double sigma = 1)
    {
        Guard.Finite(mu, "mu", nameof(mu));
        Guard.PositiveFinite(sigma, "the standard deviation sigma", nameof(sigma));
        Mu = mu;
        Sigma = sigma;
    }

    /// <summary>Mean of ln X.</summary>
    public double Mu { get; }

    /// <summary>Standard deviation of ln X.</summary>
    public double Sigma { get; }

    /// <inheritdoc/>
    public override double Mean => Math.Exp(Mu + 0.5 * Sigma * Sigma);
    /// <inheritdoc/>
    public override double Variance => Num.Expm1(Sigma * Sigma) * Math.Exp(2 * Mu + Sigma * Sigma);
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;
    /// <inheritdoc/>
    public override double Median => Math.Exp(Mu);

    /// <inheritdoc/>
    public override double Pdf(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0 || double.IsPositiveInfinity(x)) return 0.0;
        return StdNormal.Pdf((Math.Log(x) - Mu) / Sigma) / (x * Sigma);
    }

    /// <inheritdoc/>
    public override double Cdf(double x) => double.IsNaN(x) ? x : x <= 0 ? 0.0 : StdNormal.Cdf((Math.Log(x) - Mu) / Sigma);
    /// <inheritdoc/>
    public override double Survival(double x) => double.IsNaN(x) ? x : x <= 0 ? 1.0 : StdNormal.Cdf((Mu - Math.Log(x)) / Sigma);

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return Math.Exp(Mu + Sigma * StdNormal.Quantile(p, 1 - p));
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return Math.Exp(Mu - Sigma * StdNormal.Quantile(q, 1 - q));
    }
}

/// <summary>Weibull distribution with shape k and scale lambda.</summary>
public sealed record WeibullDistribution : ContinuousDistribution
{
    /// <summary>Creates a Weibull distribution.</summary>
    public WeibullDistribution(double shape, double scale = 1)
    {
        Guard.PositiveFinite(shape, "the shape", nameof(shape));
        Guard.PositiveFinite(scale, "the scale", nameof(scale));
        Shape = shape;
        Scale = scale;
    }

    /// <summary>Shape parameter k.</summary>
    public double Shape { get; }

    /// <summary>Scale parameter lambda.</summary>
    public double Scale { get; }

    /// <inheritdoc/>
    public override double Mean => Scale * SpecialFunctions.Gamma(1 + 1 / Shape);

    /// <inheritdoc/>
    public override double Variance
    {
        get
        {
            double g1 = SpecialFunctions.Gamma(1 + 1 / Shape), g2 = SpecialFunctions.Gamma(1 + 2 / Shape);
            return Scale * Scale * (g2 - g1 * g1);
        }
    }

    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;

    /// <inheritdoc/>
    public override double Pdf(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x < 0 || double.IsPositiveInfinity(x)) return 0.0;
        double k = Shape;
        if (x == 0) return k < 1 ? double.PositiveInfinity : k == 1 ? 1.0 / Scale : 0.0;
        double t = x / Scale;
        double tk1 = Math.Pow(t, k - 1);
        return k / Scale * tk1 * Math.Exp(-tk1 * t);
    }

    /// <inheritdoc/>
    public override double Cdf(double x) => double.IsNaN(x) ? x : x <= 0 ? 0.0 : -Num.Expm1(-Math.Pow(x / Scale, Shape));
    /// <inheritdoc/>
    public override double Survival(double x) => double.IsNaN(x) ? x : x <= 0 ? 1.0 : Math.Exp(-Math.Pow(x / Scale, Shape));

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        return Scale * Math.Pow(-Num.Log1p(-p), 1 / Shape);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        return Scale * Math.Pow(-Math.Log(q), 1 / Shape);
    }
}

/// <summary>Cauchy (Lorentz) distribution with location x0 and scale gamma. Mean and variance do not exist.</summary>
public sealed record CauchyDistribution : ContinuousDistribution
{
    /// <summary>Creates a Cauchy distribution.</summary>
    public CauchyDistribution(double location = 0, double scale = 1)
    {
        Guard.Finite(location, "the location", nameof(location));
        Guard.PositiveFinite(scale, "the scale", nameof(scale));
        Location = location;
        Scale = scale;
    }

    /// <summary>Location parameter x0 (the median).</summary>
    public double Location { get; }

    /// <summary>Scale parameter gamma.</summary>
    public double Scale { get; }

    /// <inheritdoc/>
    public override double Mean => double.NaN;
    /// <inheritdoc/>
    public override double Variance => double.NaN;
    /// <inheritdoc/>
    public override double SupportMin => double.NegativeInfinity;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;
    /// <inheritdoc/>
    public override double Median => Location;

    /// <inheritdoc/>
    public override double Pdf(double x)
    {
        double z = (x - Location) / Scale;
        return 1.0 / (Math.PI * Scale * (1 + z * z));
    }

    private static double LowerTail(double z)
    {
        if (double.IsNaN(z)) return z;
        if (Math.Abs(z) > 1)
        {
            double y = Math.Atan(1 / z) / Math.PI;
            return z > 0 ? 1.0 - y : -y;
        }
        return 0.5 + Math.Atan(z) / Math.PI;
    }

    /// <inheritdoc/>
    public override double Cdf(double x) => LowerTail((x - Location) / Scale);
    /// <inheritdoc/>
    public override double Survival(double x) => LowerTail((Location - x) / Scale);

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        if (p == 0) return double.NegativeInfinity;
        if (p == 1) return double.PositiveInfinity;
        if (p == 0.5) return Location;
        if (p < 0.5) return Location - Scale / Num.TanPi(p);
        return Location + Scale / Num.TanPi(1 - p);
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        if (q == 0) return double.PositiveInfinity;
        if (q == 1) return double.NegativeInfinity;
        if (q == 0.5) return Location;
        if (q < 0.5) return Location + Scale / Num.TanPi(q);
        return Location - Scale / Num.TanPi(1 - q);
    }
}

/// <summary>Triangular distribution on [a, b] with mode c.</summary>
public sealed record TriangularDistribution : ContinuousDistribution
{
    /// <summary>Creates a triangular distribution with lower bound a, mode c and upper bound b.</summary>
    public TriangularDistribution(double lower, double mode, double upper)
    {
        Guard.Finite(lower, "the lower bound", nameof(lower));
        Guard.Finite(mode, "the mode", nameof(mode));
        Guard.Finite(upper, "the upper bound", nameof(upper));
        if (!(upper > lower)) throw new ArgumentException("the upper bound must be greater than the lower bound", nameof(upper));
        if (mode < lower || mode > upper) throw new ArgumentException("the mode must lie between the bounds", nameof(mode));
        Lower = lower;
        Mode = mode;
        Upper = upper;
    }

    /// <summary>Lower bound a.</summary>
    public double Lower { get; }

    /// <summary>Mode c.</summary>
    public double Mode { get; }

    /// <summary>Upper bound b.</summary>
    public double Upper { get; }

    /// <inheritdoc/>
    public override double Mean => (Lower + Mode + Upper) / 3.0;
    /// <inheritdoc/>
    public override double Variance => (Lower * Lower + Mode * Mode + Upper * Upper - Lower * Mode - Lower * Upper - Mode * Upper) / 18.0;
    /// <inheritdoc/>
    public override double SupportMin => Lower;
    /// <inheritdoc/>
    public override double SupportMax => Upper;

    /// <inheritdoc/>
    public override double Pdf(double x)
    {
        double a = Lower, b = Upper, c = Mode;
        if (double.IsNaN(x)) return x;
        if (x < a || x > b) return 0.0;
        if (x < c) return 2 * (x - a) / ((b - a) * (c - a));
        if (x == c) return 2 / (b - a);
        return 2 * (b - x) / ((b - a) * (b - c));
    }

    /// <inheritdoc/>
    public override double Cdf(double x)
    {
        double a = Lower, b = Upper, c = Mode;
        if (double.IsNaN(x)) return x;
        if (x <= a) return 0.0;
        if (x >= b) return 1.0;
        if (x <= c) return (x - a) * (x - a) / ((b - a) * (c - a));
        return 1 - (b - x) * (b - x) / ((b - a) * (b - c));
    }

    /// <inheritdoc/>
    public override double Survival(double x)
    {
        double a = Lower, b = Upper, c = Mode;
        if (double.IsNaN(x)) return x;
        if (x <= a) return 1.0;
        if (x >= b) return 0.0;
        if (x > c) return (b - x) * (b - x) / ((b - a) * (b - c));
        return 1 - (x - a) * (x - a) / ((b - a) * (c - a));
    }

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        CheckProbability(p);
        double a = Lower, b = Upper, c = Mode;
        double fc = (c - a) / (b - a);
        if (p <= fc) return a + Math.Sqrt(p * (b - a) * (c - a));
        return b - Math.Sqrt((1 - p) * (b - a) * (b - c));
    }

    /// <inheritdoc/>
    public override double InverseSurvival(double q)
    {
        CheckProbability(q);
        double a = Lower, b = Upper, c = Mode;
        double fu = (b - c) / (b - a);
        if (q <= fu) return b - Math.Sqrt(q * (b - a) * (b - c));
        return a + Math.Sqrt((1 - q) * (b - a) * (c - a));
    }
}
