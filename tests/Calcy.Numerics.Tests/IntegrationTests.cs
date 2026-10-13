namespace Calcy.Numerics.Tests;

public class IntegrationTests
{
    [Theory]
    [InlineData(0)] // x^2 on [0, 1]
    [InlineData(1)] // sin on [0, pi]
    [InlineData(2)] // exp on [0, 1]
    [InlineData(3)] // 1/(1+x^2) on [-1, 1]
    [InlineData(4)] // sin^2 on [0, 10 pi]
    public void SmoothIntegrals(int which)
    {
        (Func<double, double> f, double a, double b, double exact) = which switch
        {
            0 => ((Func<double, double>)(x => x * x), 0.0, 1.0, 1.0 / 3),
            1 => (Math.Sin, 0.0, Math.PI, 2.0),
            2 => (Math.Exp, 0.0, 1.0, Math.E - 1),
            3 => (x => 1 / (1 + x * x), -1.0, 1.0, Math.PI / 2),
            _ => (x => Math.Sin(x) * Math.Sin(x), 0.0, 10 * Math.PI, 5 * Math.PI),
        };
        var r = Integration.Integrate(f, a, b);
        Assert.True(r.Success, r.Message);
        Approx.Equal(exact, r.Value, 1e-12);
        Assert.True(r.ErrorEstimate < 1e-9);
        Assert.True(r.Evaluations > 0);
    }

    [Fact]
    public void InverseSqrtSingularity()
    {
        var r = Integration.Integrate(x => 1 / Math.Sqrt(x), 0, 1);
        Assert.True(r.Success, r.Message);
        Approx.Equal(2, r.Value, 1e-9);
    }

    [Fact]
    public void LogSingularity()
    {
        var r = Integration.Integrate(Math.Log, 0, 1);
        Assert.True(r.Success, r.Message);
        Approx.Equal(-1, r.Value, 1e-10);
    }

    [Fact]
    public void LogOverSqrtSingularity()
    {
        var r = Integration.Integrate(x => Math.Log(x) / Math.Sqrt(x), 0, 1);
        Assert.True(r.Success, r.Message);
        Approx.Equal(-4, r.Value, 1e-9);
    }

    [Fact]
    public void StrongAlgebraicSingularity()
    {
        var r = Integration.Integrate(x => Math.Pow(x, -0.9), 0, 1, 1e-8, 1e-10);
        Approx.Equal(10, r.Value, 1e-6);
    }

    [Fact]
    public void BothEndpointsSingular()
    {
        var r = Integration.Integrate(x => 1 / Math.Sqrt(1 - x * x), -1, 1);
        Assert.True(r.Success, r.Message);
        Approx.Equal(Math.PI, r.Value, 1e-8);
    }

    [Fact]
    public void QuarterCircle()
    {
        var r = Integration.Integrate(x => Math.Sqrt(1 - x * x), 0, 1);
        Approx.Equal(Math.PI / 4, r.Value, 1e-11);
    }

    [Fact]
    public void SemiInfiniteRanges()
    {
        Approx.Equal(1, Integration.Integrate(x => Math.Exp(-x), 0, double.PositiveInfinity).Value, 1e-10);
        Approx.Equal(1, Integration.Integrate(x => 1 / (x * x), 1, double.PositiveInfinity).Value, 1e-10);
        Approx.Equal(1, Integration.Integrate(Math.Exp, double.NegativeInfinity, 0).Value, 1e-10);
        Approx.Equal(Math.PI / 2, Integration.Integrate(x => 1 / (1 + x * x), 0, double.PositiveInfinity).Value, 1e-10);
    }

    [Fact]
    public void GaussianOverWholeLine()
    {
        var r = Integration.Integrate(x => Math.Exp(-x * x), double.NegativeInfinity, double.PositiveInfinity);
        Assert.True(r.Success, r.Message);
        Approx.Equal(Math.Sqrt(Math.PI), r.Value, 1e-10);
    }

    [Fact]
    public void NarrowPeak()
    {
        var r = Integration.Integrate(x => Math.Exp(-1e4 * x * x), -1, 1);
        Approx.Equal(Math.Sqrt(Math.PI / 1e4), r.Value, 1e-10);
    }

    [Fact]
    public void Kink()
    {
        var r = Integration.Integrate(x => Math.Abs(x - 1), 0, 2);
        Assert.True(r.Success);
        Approx.Equal(1, r.Value, 1e-12);
    }

    [Fact]
    public void DivergentIntegralReportsFailure()
    {
        var r = Integration.Integrate(x => 1 / x, 0, 1);
        Assert.False(r.Success);
        Assert.NotNull(r.Message);
    }

    [Fact]
    public void ReversedBounds()
    {
        Approx.Equal(-0.5, Integration.Integrate(x => x, 1, 0).Value, 1e-14);
        Assert.Equal(0, Integration.Integrate(x => x, 2, 2).Value);
    }

    [Fact]
    public void KronrodRuleIsExactForDegree22()
    {
        var r = Integration.GaussKronrod(x => Math.Pow(x, 20), -1, 1, 1e-15, 0, 1);
        Approx.Equal(2.0 / 21, r.Value, 1e-14);
        var c = Integration.GaussKronrod(_ => 1, -1, 1, 1e-15, 0, 1);
        Approx.Equal(2, c.Value, 1e-15);
    }

    [Fact]
    public void TanhSinhDirect()
    {
        var r = Integration.TanhSinh(x => 1 / Math.Sqrt(x), 0, 1);
        Assert.True(r.Success, r.Message);
        Approx.Equal(2, r.Value, 1e-10);
        var s = Integration.TanhSinh(x => Math.Log(x) * Math.Log(1 - x), 0, 1);
        Approx.Equal(2 - Math.PI * Math.PI / 6, s.Value, 1e-10);
    }

    [Fact]
    public void Romberg()
    {
        var r = Integration.Romberg(Math.Exp, 0, 1);
        Assert.True(r.Success);
        Approx.Equal(Math.E - 1, r.Value, 1e-12);
        Assert.False(Integration.Romberg(x => 1 / x, 0, 1).Success);
    }

    [Fact]
    public void CompositeRules()
    {
        Approx.Equal(0.25, Integration.Simpson(x => x * x * x, 0, 1, 2), 1e-15);
        Approx.Equal(1.0 / 3 + 1.0 / 60000, Integration.Trapezoid(x => x * x, 0, 1, 100), 1e-12);
        Assert.Throws<ArgumentOutOfRangeException>(() => Integration.Simpson(x => x, 0, 1, 3));
    }

    [Theory]
    [InlineData(RiemannRule.Left, 0.375)]
    [InlineData(RiemannRule.Right, 0.625)]
    [InlineData(RiemannRule.Midpoint, 0.5)]
    public void RiemannSums(RiemannRule rule, double expected)
    {
        Approx.Equal(expected, Integration.RiemannSum(x => x, 0, 1, 4, rule), 1e-15);
    }

    [Fact]
    public void DoubleIntegrals()
    {
        var r = Integration.Integrate2D((x, y) => x * y, 0, 1, 0, 1);
        Assert.True(r.Success);
        Approx.Equal(0.25, r.Value, 1e-10);
        var tri = Integration.Integrate2D((x, y) => 1, 0, 1, _ => 0, x => x);
        Approx.Equal(0.5, tri.Value, 1e-10);
        var disk = Integration.Integrate2D((x, y) => 1, -1, 1, x => -Math.Sqrt(1 - x * x), x => Math.Sqrt(1 - x * x));
        Approx.Equal(Math.PI, disk.Value, 1e-7);
    }

    [Fact]
    public void DoubleIntegralOverPlane()
    {
        var r = Integration.Integrate2D((x, y) => Math.Exp(-(x * x + y * y)), double.NegativeInfinity, double.PositiveInfinity,
            _ => double.NegativeInfinity, _ => double.PositiveInfinity);
        Approx.Equal(Math.PI, r.Value, 1e-7);
    }
}
