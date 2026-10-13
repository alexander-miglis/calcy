using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>
/// Base record for discrete distributions on the integers. Non-integer arguments are handled as a step
/// function: Pmf is 0 off the integers and Cdf(k) = Cdf(floor(k)).
/// </summary>
public abstract record DiscreteDistribution
{
    /// <summary>The mean.</summary>
    public abstract double Mean { get; }

    /// <summary>The variance.</summary>
    public abstract double Variance { get; }

    /// <summary>The standard deviation.</summary>
    public double StandardDeviation => Math.Sqrt(Variance);

    /// <summary>Smallest value of the support.</summary>
    public abstract double SupportMin { get; }

    /// <summary>Largest value of the support (may be +infinity).</summary>
    public abstract double SupportMax { get; }

    /// <summary>P(X = k). Zero for non-integer k or k outside the support.</summary>
    public double Pmf(double k)
    {
        if (double.IsNaN(k)) return k;
        if (!Num.IsInteger(k) || k < SupportMin || k > SupportMax) return 0.0;
        return PmfCore(k);
    }

    /// <summary>P(X &lt;= k).</summary>
    public double Cdf(double k)
    {
        if (double.IsNaN(k)) return k;
        k = Math.Floor(k);
        if (k < SupportMin) return 0.0;
        if (k >= SupportMax) return 1.0;
        return CdfCore(k);
    }

    /// <summary>P(X &gt; k), accurate in the upper tail.</summary>
    public double Survival(double k)
    {
        if (double.IsNaN(k)) return k;
        k = Math.Floor(k);
        if (k < SupportMin) return 1.0;
        if (k >= SupportMax) return 0.0;
        return SurvivalCore(k);
    }

    /// <summary>
    /// P(lower &lt;= X &lt;= upper), both ends inclusive (Casio and TI interval convention). Bounds may be infinite.
    /// Returns 0 when no integer lies in the interval.
    /// </summary>
    public double Probability(double lower, double upper)
    {
        if (double.IsNaN(lower) || double.IsNaN(upper)) throw new ArgumentException("the bounds must be numbers");
        double lo = Math.Ceiling(lower), hi = Math.Floor(upper);
        if (lo > hi) return 0.0;
        double cu = Cdf(hi);
        if (cu <= 0.5) return Math.Max(0.0, cu - Cdf(lo - 1));
        return Math.Max(0.0, Survival(lo - 1) - Survival(hi));
    }

    /// <summary>
    /// The smallest k in the support with Cdf(k) &gt;= p. A relative tolerance of 64 machine epsilons on p
    /// absorbs rounding, so that exact cumulative values are found reliably.
    /// </summary>
    public virtual double InverseCdf(double p)
    {
        if (!(p >= 0 && p <= 1)) throw new ArgumentException("the probability must be between 0 and 1", nameof(p));
        double min = SupportMin, max = SupportMax;
        if (p == 0) return min;
        if (p == 1) return max;
        double target = p * (1 - 64 * Num.Eps);
        double sd = StandardDeviation;
        double guess = Mean + (double.IsFinite(sd) ? sd * StdNormal.Quantile(p, 1 - p) : 0.0);
        if (!double.IsFinite(guess)) guess = Mean;
        if (!double.IsFinite(guess)) guess = min;
        double k = Math.Round(Math.Clamp(guess, min, Math.Min(max, 9e15)));
        double lo, hi;
        if (Cdf(k) >= target)
        {
            hi = k;
            double step = 1;
            while (true)
            {
                double cand = hi - step;
                if (cand < min) { lo = min - 1; break; }
                if (Cdf(cand) < target) { lo = cand; break; }
                hi = cand;
                step *= 2;
            }
        }
        else
        {
            lo = k;
            double step = 1;
            while (true)
            {
                double cand = lo + step;
                if (cand >= max) { hi = max; break; }
                if (Cdf(cand) >= target) { hi = cand; break; }
                lo = cand;
                step *= 2;
            }
        }
        while (hi - lo > 1)
        {
            double mid = Math.Floor(lo + (hi - lo) / 2);
            if (Cdf(mid) >= target) hi = mid; else lo = mid;
        }
        return hi;
    }

    /// <summary>P(X = k) for an integer k inside the support.</summary>
    protected abstract double PmfCore(double k);

    /// <summary>P(X &lt;= k) for an integer k with SupportMin &lt;= k &lt; SupportMax.</summary>
    protected abstract double CdfCore(double k);

    /// <summary>P(X &gt; k) for an integer k with SupportMin &lt;= k &lt; SupportMax.</summary>
    protected virtual double SurvivalCore(double k) => 1.0 - CdfCore(k);
}

/// <summary>Binomial distribution: number of successes in n independent trials with success probability p.</summary>
public sealed record BinomialDistribution : DiscreteDistribution
{
    /// <summary>Creates a binomial distribution.</summary>
    public BinomialDistribution(double trials, double probability)
    {
        Guard.NonNegativeInteger(trials, "the number of trials", nameof(trials));
        Guard.Probability(probability, "the success probability", nameof(probability));
        Trials = trials;
        P = probability;
    }

    /// <summary>Number of trials n.</summary>
    public double Trials { get; }

    /// <summary>Success probability p.</summary>
    public double P { get; }

    /// <inheritdoc/>
    public override double Mean => Trials * P;
    /// <inheritdoc/>
    public override double Variance => Trials * P * (1 - P);
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => Trials;

    /// <inheritdoc/>
    protected override double PmfCore(double k) => DistKernels.BinomRaw(k, Trials, P, 1 - P);

    /// <inheritdoc/>
    protected override double CdfCore(double k)
    {
        if (P == 0) return 1.0;
        if (P == 1) return 0.0;
        SpecialFunctions.IncompleteBeta(Trials - k, k + 1, 1 - P, P, out double ix, out _);
        return ix;
    }

    /// <inheritdoc/>
    protected override double SurvivalCore(double k)
    {
        if (P == 0) return 0.0;
        if (P == 1) return 1.0;
        SpecialFunctions.IncompleteBeta(Trials - k, k + 1, 1 - P, P, out _, out double iy);
        return iy;
    }
}

/// <summary>Poisson distribution with mean lambda.</summary>
public sealed record PoissonDistribution : DiscreteDistribution
{
    /// <summary>Creates a Poisson distribution with the given mean.</summary>
    public PoissonDistribution(double lambda)
    {
        Guard.PositiveFinite(lambda, "the mean lambda", nameof(lambda));
        Lambda = lambda;
    }

    /// <summary>Mean lambda.</summary>
    public double Lambda { get; }

    /// <inheritdoc/>
    public override double Mean => Lambda;
    /// <inheritdoc/>
    public override double Variance => Lambda;
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => double.PositiveInfinity;

    /// <inheritdoc/>
    protected override double PmfCore(double k) => DistKernels.PoissonRaw(k, Lambda);

    /// <inheritdoc/>
    protected override double CdfCore(double k)
    {
        SpecialFunctions.IncompleteGamma(k + 1, Lambda, out _, out double q);
        return q;
    }

    /// <inheritdoc/>
    protected override double SurvivalCore(double k)
    {
        SpecialFunctions.IncompleteGamma(k + 1, Lambda, out double p, out _);
        return p;
    }
}

/// <summary>
/// Geometric distribution in the TI-84 and Casio convention: the number of the trial on which the first
/// success occurs, support 1, 2, 3, ...
/// </summary>
public sealed record GeometricDistribution : DiscreteDistribution
{
    /// <summary>Creates a geometric distribution with success probability p (0 &lt; p &lt;= 1).</summary>
    public GeometricDistribution(double probability)
    {
        if (!(probability > 0 && probability <= 1)) throw new ArgumentException("the success probability must be greater than 0 and at most 1", nameof(probability));
        P = probability;
    }

    /// <summary>Success probability p.</summary>
    public double P { get; }

    /// <inheritdoc/>
    public override double Mean => 1.0 / P;
    /// <inheritdoc/>
    public override double Variance => (1 - P) / (P * P);
    /// <inheritdoc/>
    public override double SupportMin => 1.0;
    /// <inheritdoc/>
    public override double SupportMax => P == 1 ? 1.0 : double.PositiveInfinity;

    /// <inheritdoc/>
    protected override double PmfCore(double k) => P * Math.Exp((k - 1) * Num.Log1p(-P));
    /// <inheritdoc/>
    protected override double CdfCore(double k) => -Num.Expm1(k * Num.Log1p(-P));
    /// <inheritdoc/>
    protected override double SurvivalCore(double k) => Math.Exp(k * Num.Log1p(-P));

    /// <inheritdoc/>
    public override double InverseCdf(double p)
    {
        if (!(p >= 0 && p <= 1)) throw new ArgumentException("the probability must be between 0 and 1", nameof(p));
        if (p == 0 || P == 1) return 1.0;
        if (p == 1) return double.PositiveInfinity;
        double target = p * (1 - 64 * Num.Eps);
        double k = Math.Max(1, Math.Ceiling(Num.Log1p(-target) / Num.Log1p(-P)));
        while (k > 1 && Cdf(k - 1) >= target) k--;
        while (Cdf(k) < target) k++;
        return k;
    }
}

/// <summary>
/// Hypergeometric distribution: number of successes in n draws without replacement from a population of
/// size N that contains K successes.
/// </summary>
public sealed record HypergeometricDistribution : DiscreteDistribution
{
    /// <summary>Creates a hypergeometric distribution.</summary>
    public HypergeometricDistribution(double populationSize, double successes, double draws)
    {
        Guard.NonNegativeInteger(populationSize, "the population size", nameof(populationSize));
        Guard.NonNegativeInteger(successes, "the number of successes in the population", nameof(successes));
        Guard.NonNegativeInteger(draws, "the number of draws", nameof(draws));
        if (successes > populationSize) throw new ArgumentException("the number of successes cannot exceed the population size", nameof(successes));
        if (draws > populationSize) throw new ArgumentException("the number of draws cannot exceed the population size", nameof(draws));
        PopulationSize = populationSize;
        Successes = successes;
        Draws = draws;
    }

    /// <summary>Population size N.</summary>
    public double PopulationSize { get; }

    /// <summary>Number of successes K in the population.</summary>
    public double Successes { get; }

    /// <summary>Number of draws n.</summary>
    public double Draws { get; }

    /// <inheritdoc/>
    public override double Mean => PopulationSize == 0 ? 0 : Draws * Successes / PopulationSize;

    /// <inheritdoc/>
    public override double Variance
    {
        get
        {
            double nn = PopulationSize;
            if (nn <= 1) return 0.0;
            double f = Successes / nn;
            return Draws * f * (1 - f) * (nn - Draws) / (nn - 1);
        }
    }

    /// <inheritdoc/>
    public override double SupportMin => Math.Max(0, Draws + Successes - PopulationSize);
    /// <inheritdoc/>
    public override double SupportMax => Math.Min(Draws, Successes);

    private static double Density(double x, double r, double b, double n)
    {
        if (x < 0 || x < n - b || x > r || x > n) return 0.0;
        if (n == 0) return x == 0 ? 1.0 : 0.0;
        double p = n / (r + b), q = (r + b - n) / (r + b);
        double p1 = DistKernels.BinomRaw(x, r, p, q);
        double p2 = DistKernels.BinomRaw(n - x, b, p, q);
        double p3 = DistKernels.BinomRaw(n, r + b, p, q);
        return p1 * p2 / p3;
    }

    private static double Tail(double x, double r, double b, double n, bool lowerTail)
    {
        if (x * (r + b) > n * r)
        {
            (r, b) = (b, r);
            x = n - x - 1;
            lowerTail = !lowerTail;
        }
        if (x < 0 || x < n - b) return lowerTail ? 0.0 : 1.0;
        if (x >= r || x >= n) return lowerTail ? 1.0 : 0.0;
        double d = Density(x, r, b, n);
        double sum = 0, term = 1;
        double xx = x;
        while (xx > 0 && term >= Num.Eps * sum)
        {
            term *= xx * (b - n + xx) / (n + 1 - xx) / (r + 1 - xx);
            sum += term;
            xx--;
        }
        double v = d * (1 + sum);
        return lowerTail ? v : 1.0 - v;
    }

    /// <inheritdoc/>
    protected override double PmfCore(double k) => Density(k, Successes, PopulationSize - Successes, Draws);
    /// <inheritdoc/>
    protected override double CdfCore(double k) => Math.Clamp(Tail(k, Successes, PopulationSize - Successes, Draws, true), 0, 1);
    /// <inheritdoc/>
    protected override double SurvivalCore(double k) => Math.Clamp(Tail(k, Successes, PopulationSize - Successes, Draws, false), 0, 1);
}

/// <summary>
/// Negative binomial distribution: the number of failures before the r-th success (support 0, 1, 2, ...),
/// the convention of Excel NEGBINOM.DIST and R. The size r may be any positive real number.
/// </summary>
public sealed record NegativeBinomialDistribution : DiscreteDistribution
{
    /// <summary>Creates a negative binomial distribution with r successes and success probability p.</summary>
    public NegativeBinomialDistribution(double successes, double probability)
    {
        Guard.PositiveFinite(successes, "the number of successes r", nameof(successes));
        if (!(probability > 0 && probability <= 1)) throw new ArgumentException("the success probability must be greater than 0 and at most 1", nameof(probability));
        R = successes;
        P = probability;
    }

    /// <summary>Number of successes r.</summary>
    public double R { get; }

    /// <summary>Success probability p.</summary>
    public double P { get; }

    /// <inheritdoc/>
    public override double Mean => R * (1 - P) / P;
    /// <inheritdoc/>
    public override double Variance => R * (1 - P) / (P * P);
    /// <inheritdoc/>
    public override double SupportMin => 0.0;
    /// <inheritdoc/>
    public override double SupportMax => P == 1 ? 0.0 : double.PositiveInfinity;

    /// <inheritdoc/>
    protected override double PmfCore(double k)
    {
        if (k == 0) return Math.Exp(R * Math.Log(P));
        double ans = DistKernels.BinomRaw(R, k + R, P, 1 - P);
        return R / (R + k) * ans;
    }

    /// <inheritdoc/>
    protected override double CdfCore(double k)
    {
        SpecialFunctions.IncompleteBeta(R, k + 1, P, 1 - P, out double ix, out _);
        return ix;
    }

    /// <inheritdoc/>
    protected override double SurvivalCore(double k)
    {
        SpecialFunctions.IncompleteBeta(R, k + 1, P, 1 - P, out _, out double iy);
        return iy;
    }
}

/// <summary>Discrete uniform distribution on the integers a, a + 1, ..., b.</summary>
public sealed record DiscreteUniformDistribution : DiscreteDistribution
{
    /// <summary>Creates a discrete uniform distribution on [lower, upper] (integers).</summary>
    public DiscreteUniformDistribution(double lower, double upper)
    {
        Guard.Integer(lower, "the lower bound", nameof(lower));
        Guard.Integer(upper, "the upper bound", nameof(upper));
        if (upper < lower) throw new ArgumentException("the upper bound must not be less than the lower bound", nameof(upper));
        Lower = lower;
        Upper = upper;
    }

    /// <summary>Lower bound a.</summary>
    public double Lower { get; }

    /// <summary>Upper bound b.</summary>
    public double Upper { get; }

    private double Count => Upper - Lower + 1;

    /// <inheritdoc/>
    public override double Mean => 0.5 * (Lower + Upper);
    /// <inheritdoc/>
    public override double Variance => (Count * Count - 1) / 12.0;
    /// <inheritdoc/>
    public override double SupportMin => Lower;
    /// <inheritdoc/>
    public override double SupportMax => Upper;
    /// <inheritdoc/>
    protected override double PmfCore(double k) => 1.0 / Count;
    /// <inheritdoc/>
    protected override double CdfCore(double k) => (k - Lower + 1) / Count;
    /// <inheritdoc/>
    protected override double SurvivalCore(double k) => (Upper - k) / Count;
}
