using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

/// <summary>Structural properties shared by all distributions: moments, round trips, complements, integrals.</summary>
public class DistributionPropertyTests
{
    public static IEnumerable<object[]> Continuous()
    {
        yield return [new NormalDistribution(1.5, 2.5), 1.5, 6.25];
        yield return [new StudentTDistribution(7.5), 0.0, 7.5 / 5.5];
        yield return [new ChiSquaredDistribution(4.5), 4.5, 9.0];
        yield return [new FDistribution(5, 12), 12.0 / 10, 2 * 144.0 * 15 / (5 * 100 * 8)];
        yield return [new ExponentialDistribution(0.5), 2.0, 4.0];
        yield return [new UniformDistribution(-1, 3), 1.0, 16.0 / 12];
        yield return [new GammaDistribution(2.5, 1.5), 3.75, 5.625];
        yield return [new BetaDistribution(2, 5), 2.0 / 7, 10.0 / (49 * 8)];
        yield return [new LogNormalDistribution(0.2, 0.5), Math.Exp(0.2 + 0.125), (Math.Exp(0.25) - 1) * Math.Exp(0.4 + 0.25)];
        yield return [new WeibullDistribution(2, 3), 3 * SpecialFunctions.Gamma(1.5), 9 * (1 - Math.Pow(SpecialFunctions.Gamma(1.5), 2))];
        yield return [new TriangularDistribution(0, 1, 4), 5.0 / 3, (0 + 1 + 16 - 0 - 0 - 4) / 18.0];
    }

    [Theory]
    [MemberData(nameof(Continuous))]
    public void ContinuousMoments(ContinuousDistribution d, double mean, double variance)
    {
        Tol.Rel(mean, d.Mean, 1e-13);
        Tol.Rel(variance, d.Variance, 1e-12);
    }

    [Theory]
    [MemberData(nameof(Continuous))]
    public void ContinuousRoundTripAndComplement(ContinuousDistribution d, double mean, double variance)
    {
        _ = mean; _ = variance;
        // Allowed error: a small constant plus the unavoidable error from rounding x itself (eps |x| pdf(x) / p).
        double Allowed(double x, double p) => 1e-11 + 4 * 2.2e-16 * Math.Abs(x) * d.Pdf(x) / p;
        foreach (double p in new[] { 1e-12, 1e-6, 0.01, 0.2, 0.5, 0.8, 0.99, 1 - 1e-9 })
        {
            double x = d.InverseCdf(p);
            Tol.Rel(p, d.Cdf(x), Allowed(x, p));
            Tol.Rel(1.0, d.Cdf(x) + d.Survival(x), 1e-14);
        }
        foreach (double q in new[] { 1e-15, 1e-8, 0.3 })
        {
            double x = d.InverseSurvival(q);
            Tol.Rel(q, d.Survival(x), 10 * Allowed(x, q));
        }
    }

    [Theory]
    [MemberData(nameof(Continuous))]
    public void DensityIntegratesToCdfDifference(ContinuousDistribution d, double mean, double variance)
    {
        _ = mean; _ = variance;
        double a = d.InverseCdf(0.1), b = d.InverseCdf(0.9);
        int n = 4000;
        double h = (b - a) / n, s = d.Pdf(a) + d.Pdf(b);
        for (int i = 1; i < n; i++) s += (i % 2 == 1 ? 4 : 2) * d.Pdf(a + i * h);
        Tol.Rel(0.8, s * h / 3, 1e-6);
        Tol.Rel(0.8, d.Probability(a, b), 1e-12);
    }

    public static IEnumerable<object[]> Discrete()
    {
        yield return [new BinomialDistribution(30, 0.35), 10.5, 30 * 0.35 * 0.65];
        yield return [new PoissonDistribution(6.5), 6.5, 6.5];
        yield return [new GeometricDistribution(0.25), 4.0, 0.75 / 0.0625];
        yield return [new HypergeometricDistribution(60, 20, 15), 5.0, 15 * (1.0 / 3) * (2.0 / 3) * 45 / 59];
        yield return [new NegativeBinomialDistribution(4, 0.4), 6.0, 4 * 0.6 / 0.16];
        yield return [new DiscreteUniformDistribution(3, 12), 7.5, (100 - 1) / 12.0];
    }

    [Theory]
    [MemberData(nameof(Discrete))]
    public void DiscreteMomentsMatchPmfSums(DiscreteDistribution d, double mean, double variance)
    {
        Tol.Rel(mean, d.Mean, 1e-13);
        Tol.Rel(variance, d.Variance, 1e-13);
        double hi = Math.Min(d.SupportMax, 400);
        double s0 = 0, s1 = 0, s2 = 0;
        for (double k = d.SupportMin; k <= hi; k++)
        {
            double p = d.Pmf(k);
            s0 += p; s1 += k * p; s2 += k * k * p;
        }
        Tol.Rel(1.0, s0, 1e-12);
        Tol.Rel(mean, s1, 1e-11);
        Tol.Rel(variance, s2 - s1 * s1, 1e-9);
    }

    [Theory]
    [MemberData(nameof(Discrete))]
    public void DiscreteCdfIsCumulativePmfAndInverseIsSmallest(DiscreteDistribution d, double mean, double variance)
    {
        _ = mean; _ = variance;
        double cum = 0;
        double hi = Math.Min(d.SupportMax, d.SupportMin + 40);
        for (double k = d.SupportMin; k <= hi; k++)
        {
            cum += d.Pmf(k);
            Tol.Rel(cum, d.Cdf(k), 1e-12);
            Tol.Rel(1.0, d.Cdf(k) + d.Survival(k), 1e-14);
            double c = d.Cdf(k);
            if (c < 1 - 1e-9 && c > 1e-9)
            {
                Assert.Equal(k, d.InverseCdf(c));
                Assert.Equal(k + 1, d.InverseCdf(Math.Min(1, c * (1 + 1e-9))));
            }
        }
        Assert.Equal(0.0, d.Pmf(d.SupportMin + 0.5));
        Assert.Equal(d.Cdf(d.SupportMin + 2), d.Cdf(d.SupportMin + 2.7));
    }

    [Fact]
    public void DiscreteIntervalProbabilityIsInclusive()
    {
        var b = new BinomialDistribution(12, 0.4);
        Tol.Rel(b.Pmf(3) + b.Pmf(4) + b.Pmf(5), b.Probability(3, 5), 1e-14);
        Tol.Rel(b.Pmf(4), b.Probability(3.5, 4.5), 1e-14);
        Assert.Equal(0.0, b.Probability(4.2, 4.8));
        Tol.Rel(1.0, b.Probability(double.NegativeInfinity, double.PositiveInfinity), 1e-15);
        var p = new PoissonDistribution(2);
        Tol.Rel(p.Survival(4), p.Probability(5, double.PositiveInfinity), 1e-14);
    }

    [Fact]
    public void StudentTWithInfiniteDfIsNormal()
    {
        var t = new StudentTDistribution(double.PositiveInfinity);
        Tol.Rel(NormalDistribution.Standard.Cdf(1.3), t.Cdf(1.3), 1e-15);
        Tol.Rel(NormalDistribution.Standard.Pdf(0.7), t.Pdf(0.7), 1e-15);
        Tol.Rel(1.959963984540054, t.InverseCdf(0.975), 1e-14);
    }

    [Fact]
    public void StudentTSpecialCasesAndMoments()
    {
        var cauchy = new StudentTDistribution(1);
        Tol.Rel(new CauchyDistribution().Cdf(2.5), cauchy.Cdf(2.5), 1e-14);
        Tol.Rel(Math.Tan(Math.PI * 0.4), cauchy.InverseCdf(0.9), 1e-14);
        Assert.True(double.IsNaN(cauchy.Mean));
        Assert.True(double.IsPositiveInfinity(new StudentTDistribution(1.5).Variance));
        // Closed form for df = 2 evaluated at the double nearest 0.95 (1 - 0.95 rounds to 0.050000000000000044).
        Tol.Rel(2.919985580355516, new StudentTDistribution(2).InverseCdf(0.95), 1e-11);
        double q = 1 - 0.95;
        Tol.Rel((1 - 2 * q) / Math.Sqrt(2 * q * (1 - q)), new StudentTDistribution(2).InverseCdf(0.95), 1e-15);
    }

    [Fact]
    public void InvalidParametersThrow()
    {
        Assert.Throws<ArgumentException>(() => new NormalDistribution(0, 0));
        Assert.Throws<ArgumentException>(() => new StudentTDistribution(-1));
        Assert.Throws<ArgumentException>(() => new ChiSquaredDistribution(0));
        Assert.Throws<ArgumentException>(() => new FDistribution(1, double.NaN));
        Assert.Throws<ArgumentException>(() => new UniformDistribution(2, 1));
        Assert.Throws<ArgumentException>(() => new GammaDistribution(-2));
        Assert.Throws<ArgumentException>(() => new BetaDistribution(1, 0));
        Assert.Throws<ArgumentException>(() => new HypergeometricDistribution(10, 11, 2));
        Assert.Throws<ArgumentException>(() => new DiscreteUniformDistribution(1.5, 3));
        Assert.Throws<ArgumentException>(() => new BinomialDistribution(-1, 0.5));
        Assert.Throws<ArgumentException>(() => new NormalDistribution().InverseCdf(-0.1));
        Assert.Throws<ArgumentException>(() => new PoissonDistribution(3).InverseCdf(2));
        Assert.Throws<ArgumentException>(() => new TriangularDistribution(0, 5, 4));
    }

    [Fact]
    public void RecordsCompareByValue()
    {
        Assert.Equal(new NormalDistribution(1, 2), new NormalDistribution(1, 2));
        Assert.NotEqual(new NormalDistribution(1, 2), new NormalDistribution(1, 3));
        Assert.Equal(new BinomialDistribution(10, 0.5), new BinomialDistribution(10, 0.5));
    }

    [Fact]
    public void UniformAndExponentialClosedForms()
    {
        var u = new UniformDistribution(2, 6);
        Assert.Equal(0.25, u.Pdf(3));
        Assert.Equal(0.75, u.Cdf(5));
        Assert.Equal(5.0, u.InverseCdf(0.75));
        var e = new ExponentialDistribution(2);
        Tol.Rel(1 - Math.Exp(-1), e.Cdf(0.5), 1e-15);
        Tol.Rel(1e-20 / 2, e.InverseCdf(1e-20), 1e-14);
        Tol.Rel(Math.Exp(-200), e.Survival(100), 1e-14);
        Tol.Rel(2.0, ExponentialDistribution.FromMean(0.5).Rate, 1e-15);
    }
}
