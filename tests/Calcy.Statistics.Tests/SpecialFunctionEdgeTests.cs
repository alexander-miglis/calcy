using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

public class SpecialFunctionEdgeTests
{
    [Fact]
    public void GammaPolesAndLimits()
    {
        Assert.Equal(double.PositiveInfinity, SpecialFunctions.Gamma(0));
        Assert.Equal(double.NegativeInfinity, SpecialFunctions.Gamma(-0.0));
        Assert.True(double.IsNaN(SpecialFunctions.Gamma(-1)));
        Assert.True(double.IsNaN(SpecialFunctions.Gamma(-20)));
        Assert.Equal(double.PositiveInfinity, SpecialFunctions.Gamma(172));
        Assert.Equal(double.PositiveInfinity, SpecialFunctions.LogGamma(-2));
        Assert.Equal(0.0, SpecialFunctions.LogGamma(1));
        Assert.Equal(0.0, SpecialFunctions.LogGamma(2));
        Tol.Rel(Math.PI, SpecialFunctions.Gamma(0.5) * SpecialFunctions.Gamma(0.5), 1e-15);
        Assert.Equal(120, SpecialFunctions.Gamma(6));
        Assert.True(double.IsNaN(SpecialFunctions.Factorial(-1)));
        Assert.Equal(double.PositiveInfinity, SpecialFunctions.Factorial(171));
        Assert.True(double.IsNaN(SpecialFunctions.Digamma(0)));
        Assert.True(double.IsNaN(SpecialFunctions.Digamma(-3)));
        Tol.Rel(-SpecialFunctions.EulerGamma, SpecialFunctions.Digamma(1), 1e-15);
    }

    [Fact]
    public void ErrorFunctionLimitsAndRoundTrips()
    {
        Assert.Equal(1.0, SpecialFunctions.Erf(double.PositiveInfinity));
        Assert.Equal(-1.0, SpecialFunctions.Erf(double.NegativeInfinity));
        Assert.Equal(2.0, SpecialFunctions.Erfc(double.NegativeInfinity));
        Assert.Equal(0.0, SpecialFunctions.Erfc(double.PositiveInfinity));
        Assert.Equal(0.0, SpecialFunctions.Erf(0));
        Assert.Equal(double.PositiveInfinity, SpecialFunctions.ErfInv(1));
        Assert.Equal(double.NegativeInfinity, SpecialFunctions.ErfInv(-1));
        Assert.Equal(0.0, SpecialFunctions.ErfcInv(1));
        // Round trips only where they are well conditioned: erf for small |x|, erfc in the right tail.
        foreach (double x in new[] { -0.7, 0.1, 0.45, 1.2 })
            Tol.Rel(x, SpecialFunctions.ErfInv(SpecialFunctions.Erf(x)), 1e-14);
        foreach (double x in new[] { 0.1, 0.45, 1.2, 2.5, 4.0, 10.0, 25.0 })
            Tol.Rel(x, SpecialFunctions.ErfcInv(SpecialFunctions.Erfc(x)), 1e-14);
        Assert.Throws<ArgumentException>(() => SpecialFunctions.ErfInv(1.5));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.ErfcInv(-0.1));
    }

    [Fact]
    public void IncompleteFunctionsBoundariesAndValidation()
    {
        Assert.Equal(0.0, SpecialFunctions.RegularizedGammaP(2, 0));
        Assert.Equal(1.0, SpecialFunctions.RegularizedGammaQ(2, 0));
        Assert.Equal(1.0, SpecialFunctions.RegularizedGammaP(2, double.PositiveInfinity));
        Tol.Rel(1 - Math.Exp(-3), SpecialFunctions.RegularizedGammaP(1, 3), 1e-15);
        Assert.Equal(0.0, SpecialFunctions.RegularizedIncompleteBeta(2, 3, 0));
        Assert.Equal(1.0, SpecialFunctions.RegularizedIncompleteBeta(2, 3, 1));
        Tol.Rel(0.3, SpecialFunctions.RegularizedIncompleteBeta(1, 1, 0.3), 1e-15);
        Assert.Equal(0.0, SpecialFunctions.InverseRegularizedIncompleteBeta(2, 3, 0));
        Assert.Equal(1.0, SpecialFunctions.InverseRegularizedIncompleteBeta(2, 3, 1));
        Assert.Equal(double.PositiveInfinity, SpecialFunctions.InverseRegularizedGammaP(2, 1));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.RegularizedGammaP(-1, 1));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.RegularizedGammaP(1, -1));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.RegularizedIncompleteBeta(1, 1, 1.5));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.RegularizedIncompleteBeta(0, 1, 0.5));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.InverseRegularizedGammaP(2, 1.1));
    }

    [Fact]
    public void LambertWSpecialPoints()
    {
        Assert.Equal(0.0, SpecialFunctions.LambertW(0));
        Assert.Equal(-1.0, SpecialFunctions.LambertW(-1 / Math.E));
        Assert.Equal(-1.0, SpecialFunctions.LambertW(-1 / Math.E, -1));
        Tol.Rel(1.0, SpecialFunctions.LambertW(Math.E), 1e-15);
        Tol.Rel(0.5671432904097838, SpecialFunctions.LambertW(1), 1e-15);
        foreach (double x in new[] { -0.3, -0.01, -1e-50 })
        {
            double w = SpecialFunctions.LambertW(x, -1);
            Assert.True(w < -1);
            Tol.Rel(x, w * Math.Exp(w), 1e-14);
        }
        Assert.Throws<ArgumentException>(() => SpecialFunctions.LambertW(-1));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.LambertW(0.5, -1));
        Assert.Throws<ArgumentException>(() => SpecialFunctions.LambertW(1, 2));
    }

    [Fact]
    public void ZetaSpecialValues()
    {
        Assert.True(double.IsNaN(SpecialFunctions.RiemannZeta(1)));
        Assert.Equal(-0.5, SpecialFunctions.RiemannZeta(0));
        Assert.Equal(0.0, SpecialFunctions.RiemannZeta(-2));
        Assert.Equal(0.0, SpecialFunctions.RiemannZeta(-10));
        Tol.Rel(Math.PI * Math.PI / 6, SpecialFunctions.RiemannZeta(2), 1e-15);
        Tol.Rel(Math.Pow(Math.PI, 4) / 90, SpecialFunctions.RiemannZeta(4), 1e-15);
        Tol.Rel(-1.0 / 12, SpecialFunctions.RiemannZeta(-1), 1e-14);
        Assert.Equal(1.0, SpecialFunctions.RiemannZeta(double.PositiveInfinity));
    }

    [Fact]
    public void BinomialPermutationsAndBeta()
    {
        Assert.Equal(-1, SpecialFunctions.Binomial(-1, 3));
        Assert.Equal(1, SpecialFunctions.Binomial(-1, 4));
        Assert.Equal(0, SpecialFunctions.Binomial(5, 7));
        Assert.Equal(252, SpecialFunctions.Binomial(10, 5));
        Assert.Equal(double.PositiveInfinity, SpecialFunctions.Binomial(1030, 515));
        Assert.Equal(720, SpecialFunctions.Permutations(10, 3));
        Assert.Equal(0, SpecialFunctions.Permutations(3, 5));
        Assert.Equal(3628800, SpecialFunctions.Permutations(10, 10));
        double expected = SpecialFunctions.Gamma(-0.5) * SpecialFunctions.Gamma(0.25) / SpecialFunctions.Gamma(-0.25);
        Tol.Rel(expected, SpecialFunctions.Beta(-0.5, 0.25), 1e-13);
        Assert.Equal(0.0, SpecialFunctions.Beta(-0.5, -0.5));
        Assert.True(double.IsNaN(SpecialFunctions.Beta(-1, 2.5)));
        Tol.Rel(1.0 / 30, SpecialFunctions.Beta(2, 5), 1e-15);
        Assert.Equal(0.0, SpecialFunctions.LogBeta(1, 1));
    }
}
