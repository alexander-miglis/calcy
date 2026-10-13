using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

/// <summary>TI-84 style distribution functions compared with the 10 digits a TI-84 displays.</summary>
public class CalcDistributionsTests
{
    [Fact] public void NormalCdfCentral95() => Tol.Digits(0.9500042097, CalcDistributions.NormalCdf(-1.96, 1.96));
    [Fact] public void InvNorm975() => Tol.Digits(1.959963985, CalcDistributions.InvNorm(0.975));
    [Fact] public void TCdfMinus2To2Df10() => Tol.Digits(0.9266119652, CalcDistributions.TCdf(-2, 2, 10));
    [Fact] public void NormalCdfLeftOpenTiIdiom() => Tol.Digits(0.9331927987, CalcDistributions.NormalCdf(-1e99, 1.5));
    [Fact] public void NormalCdfOneToTwo() => Tol.Digits(0.135905122, CalcDistributions.NormalCdf(1, 2));
    [Fact] public void NormalCdfWithParameters() => Tol.Digits(0.02271846071, CalcDistributions.NormalCdf(60, 70, 50, 5));
    [Fact] public void NormalPdfAtZero() => Tol.Digits(0.3989422804, CalcDistributions.NormalPdf(0));
    [Fact] public void NormalPdfWithParameters() => Tol.Digits(0.1933340584, CalcDistributions.NormalPdf(1.5, 1, 2));
    [Fact] public void InvNormWithParameters() => Tol.Digits(75.3271956, CalcDistributions.InvNorm(0.05, 100, 15));
    [Fact] public void InvT975Df10() => Tol.Digits(2.228138852, CalcDistributions.InvT(0.975, 10));
    [Fact] public void InvTLowerTail() => Tol.Digits(-1.71088208, CalcDistributions.InvT(0.05, 24));
    [Fact] public void TPdfAtZero() => Tol.Digits(0.3796066898, CalcDistributions.TPdf(0, 5));
    [Fact] public void TCdfUpperTail() => Tol.Digits(0.08864924349, CalcDistributions.TCdf(1.5, 1e99, 7));
    [Fact] public void Chi2CdfCritical() => Tol.Digits(0.95, CalcDistributions.Chi2Cdf(0, 3.841458821, 1));
    [Fact] public void Chi2CdfUpper() => Tol.Digits(0.7357588823, CalcDistributions.Chi2Cdf(2, 1e99, 4));
    [Fact] public void Chi2PdfValue() => Tol.Digits(0.3032653299, CalcDistributions.Chi2Pdf(1, 2));
    [Fact] public void FCdfLower() => Tol.Digits(0.8219925926, CalcDistributions.FCdf(0, 2, 3, 10));
    [Fact] public void FCdfUpper() => Tol.Digits(0.09816181117, CalcDistributions.FCdf(2.5, 1e99, 4, 12));
    [Fact] public void FPdfValue() => Tol.Digits(0.41472, CalcDistributions.FPdf(1, 4, 6));
    [Fact] public void BinomPdfHalf() => Tol.Digits(0.24609375, CalcDistributions.BinomPdf(10, 0.5, 5));
    [Fact] public void BinomCdfHalf() => Tol.Digits(0.623046875, CalcDistributions.BinomCdf(10, 0.5, 5));
    [Fact] public void BinomPdfValue() => Tol.Digits(0.1916389828, CalcDistributions.BinomPdf(20, 0.3, 6));
    [Fact] public void BinomCdfRange() => Tol.Digits(0.8511854048, CalcDistributions.BinomCdf(20, 0.3, 3, 8));
    [Fact] public void PoissonPdfValue() => Tol.Digits(0.2240418077, CalcDistributions.PoissonPdf(3, 2));
    [Fact] public void PoissonCdfValue() => Tol.Digits(0.4231900811, CalcDistributions.PoissonCdf(3, 2));
    [Fact] public void PoissonCdfRange() => Tol.Digits(0.7894869965, CalcDistributions.PoissonCdf(4.2, 2, 6));
    [Fact] public void GeometPdfValue() => Tol.Rel(0.128, CalcDistributions.GeometPdf(0.2, 3), 1e-14);
    [Fact] public void GeometCdfValue() => Tol.Rel(0.488, CalcDistributions.GeometCdf(0.2, 3), 1e-14);

    [Fact]
    public void ExtremeLeftTailIsComputedNotZero()
    {
        double v = CalcDistributions.NormalCdf(-40, -38);
        Assert.True(v > 0);
        Tol.Rel(2.88542836006878e-316, v, 1e-6);
        Tol.Rel(2.75362411860623e-89, CalcDistributions.NormalCdf(-1e99, -20), 1e-13);
        Tol.Rel(4.90671392714819e-198, CalcDistributions.NormalCdf(double.NegativeInfinity, -30), 1e-13);
        Tol.Rel(2.75362411860623e-89, CalcDistributions.NormalCdf(20, double.PositiveInfinity), 1e-13);
    }

    [Fact]
    public void InfiniteAndDegenerateBounds()
    {
        Assert.Equal(1.0, CalcDistributions.NormalCdf(double.NegativeInfinity, double.PositiveInfinity));
        Assert.Equal(0.0, CalcDistributions.NormalCdf(1.5, 1.5));
        Assert.Equal(1.0, CalcDistributions.TCdf(-1e99, 1e99, 3));
        Assert.Equal(1.0, CalcDistributions.Chi2Cdf(-5, 1e99, 3));
        Assert.Equal(0.0, CalcDistributions.Chi2Cdf(-5, 0, 3));
    }

    [Fact]
    public void SwappedBoundsGiveNegativeAreaLikeTi()
    {
        Tol.Rel(-0.3413447460685429, CalcDistributions.NormalCdf(1, 0), 1e-14);
        Tol.Rel(-CalcDistributions.TCdf(-1, 2, 5), CalcDistributions.TCdf(2, -1, 5), 1e-14);
    }

    [Fact]
    public void InvNormTails()
    {
        Tol.Rel(-1.959963984540054, CalcDistributions.InvNorm(0.025), 1e-14);
        Tol.Rel(1.959963984540054, CalcDistributions.InvNorm(0.025, tail: Tail.Right), 1e-14);
        Tol.Rel(1.959963984540054, CalcDistributions.InvNorm(0.95, tail: Tail.Center), 1e-14);
        var (lo, hi) = CalcDistributions.InvNormCenter(0.95, 10, 2);
        Tol.Rel(10 - 2 * 1.959963984540054, lo, 1e-14);
        Tol.Rel(10 + 2 * 1.959963984540054, hi, 1e-14);
        Assert.Equal(double.NegativeInfinity, CalcDistributions.InvNorm(0));
        Assert.Equal(double.PositiveInfinity, CalcDistributions.InvNorm(1));
        Assert.Equal(0.0, CalcDistributions.InvNorm(0.5));
        Tol.Rel(-37.0470962993612, CalcDistributions.InvNorm(1e-300), 1e-13);
        Tol.Rel(37.0470962993612, CalcDistributions.InvNorm(1e-300, tail: Tail.Right), 1e-13);
    }

    [Fact]
    public void InvTCenterAndRight()
    {
        Tol.Digits(2.17881283, CalcDistributions.InvT(0.95, 12, Tail.Center));
        Tol.Digits(2.17881283, CalcDistributions.InvT(0.025, 12, Tail.Right));
    }

    [Fact]
    public void InverseChiSquareAndFRoundTrip()
    {
        double x = CalcDistributions.InvChi2(0.05, 7, Tail.Right);
        Tol.Rel(0.05, CalcDistributions.Chi2Cdf(x, 1e99, 7), 1e-13);
        double f = CalcDistributions.InvF(0.01, 3, 15, Tail.Right);
        Tol.Rel(0.01, CalcDistributions.FCdf(f, 1e99, 3, 15), 1e-13);
        double c = CalcDistributions.InvF(0.9, 3, 15, Tail.Center);
        Tol.Rel(0.05, CalcDistributions.FCdf(c, 1e99, 3, 15), 1e-13);
    }

    [Fact]
    public void BinomialListsSumToOne()
    {
        var pdf = CalcDistributions.BinomPdf(25, 0.37);
        Assert.Equal(26, pdf.Length);
        Tol.Rel(1.0, pdf.Sum(), 1e-14);
        var cdf = CalcDistributions.BinomCdf(25, 0.37);
        Tol.Rel(1.0, cdf[^1], 1e-15);
        for (int k = 1; k < cdf.Length; k++) Assert.True(cdf[k] >= cdf[k - 1]);
        Tol.Rel(pdf[0] + pdf[1] + pdf[2], cdf[2], 1e-14);
    }

    [Fact]
    public void InvBinomIsSmallestKWithCdfAtLeastArea()
    {
        Assert.Equal(5, CalcDistributions.InvBinom(0.5, 10, 0.5));
        Assert.Equal(5, CalcDistributions.InvBinom(0.623046875, 10, 0.5));
        Assert.Equal(6, CalcDistributions.InvBinom(0.623046876, 10, 0.5));
        Assert.Equal(7, CalcDistributions.InvBinom(0.3, 20, 0.4));
        Assert.Equal(0, CalcDistributions.InvBinom(0, 20, 0.4));
        Assert.Equal(20, CalcDistributions.InvBinom(1, 20, 0.4));
        Assert.Equal(7, CalcDistributions.InvPoisson(0.9, 4));
    }

    [Fact]
    public void GeometricUsesTrialsUntilFirstSuccess()
    {
        Assert.Equal(0.0, CalcDistributions.GeometPdf(0.3, 0));
        Tol.Rel(0.3, CalcDistributions.GeometPdf(0.3, 1), 1e-15);
        Tol.Rel(0.3 * 0.7, CalcDistributions.GeometPdf(0.3, 2), 1e-15);
        Tol.Rel(0.7 * 0.7 * 0.3 + 0.7 * 0.7 * 0.7 * 0.3, CalcDistributions.GeometCdf(0.3, 3, 4), 1e-14);
        Assert.Equal(1.0 / 0.3, new GeometricDistribution(0.3).Mean, 12);
    }

    [Fact]
    public void InvalidArgumentsThrowClearErrors()
    {
        var ex = Assert.Throws<ArgumentException>(() => CalcDistributions.NormalPdf(0, 0, -1));
        Assert.Contains("standard deviation must be", ex.Message);
        Assert.Throws<ArgumentException>(() => CalcDistributions.InvNorm(1.5));
        Assert.Throws<ArgumentException>(() => CalcDistributions.TCdf(0, 1, 0));
        Assert.Throws<ArgumentException>(() => CalcDistributions.BinomPdf(10.5, 0.5, 3));
        Assert.Throws<ArgumentException>(() => CalcDistributions.BinomPdf(10, 1.5, 3));
        Assert.Throws<ArgumentException>(() => CalcDistributions.BinomPdf(10, 0.5, 2.5));
        Assert.Throws<ArgumentException>(() => CalcDistributions.PoissonPdf(-1, 2));
        Assert.Throws<ArgumentException>(() => CalcDistributions.GeometPdf(0, 2));
        Assert.Throws<ArgumentException>(() => CalcDistributions.NormalCdf(double.NaN, 1));
    }

    [Fact]
    public void BinomialEdgeProbabilities()
    {
        Assert.Equal(1.0, CalcDistributions.BinomPdf(10, 0, 0));
        Assert.Equal(0.0, CalcDistributions.BinomPdf(10, 0, 1));
        Assert.Equal(1.0, CalcDistributions.BinomPdf(10, 1, 10));
        Assert.Equal(1.0, CalcDistributions.BinomCdf(10, 0.3, 10));
        Assert.Equal(0.0, CalcDistributions.BinomCdf(10, 0.3, -1));
        Assert.Equal(0.0, CalcDistributions.BinomPdf(10, 0.3, 11));
        Assert.Equal(1.0, CalcDistributions.BinomPdf(0, 0.3, 0));
    }
}
