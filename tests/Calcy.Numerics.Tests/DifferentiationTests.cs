namespace Calcy.Numerics.Tests;

public class DifferentiationTests
{
    [Fact]
    public void FirstDerivativeOfSine()
    {
        var d = Differentiation.Derivative(Math.Sin, 1);
        Approx.Equal(Math.Cos(1), d.Value, 1e-13);
        Assert.True(d.ErrorEstimate < 1e-10);
        Assert.True(Math.Abs(d.Value - Math.Cos(1)) <= 100 * d.ErrorEstimate + 1e-15);
    }

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.0)]
    [InlineData(3, 1.0)]
    [InlineData(4, 1.0)]
    public void DerivativesOfExpAtZero(int order, double expected)
    {
        var d = Differentiation.Derivative(Math.Exp, 0, order);
        double tol = order switch { 1 => 1e-13, 2 => 1e-10, 3 => 1e-7, _ => 1e-5 };
        Approx.Equal(expected, d.Value, tol);
    }

    [Fact]
    public void ThirdDerivativeOfQuartic()
    {
        var d = Differentiation.Derivative(x => Math.Pow(x, 4), 2, 3);
        Approx.Equal(48, d.Value, 1e-8);
    }

    [Fact]
    public void FourthDerivativeOfSine()
    {
        var d = Differentiation.Derivative(Math.Sin, 0.5, 4);
        Approx.Equal(Math.Sin(0.5), d.Value, 1e-5);
    }

    [Fact]
    public void NearDomainBoundary()
    {
        var d = Differentiation.Derivative(Math.Log, 0.05);
        Approx.Equal(20, d.Value, 1e-9);
        var s = Differentiation.Derivative(Math.Sqrt, 1e-4);
        Approx.Equal(0.5 / Math.Sqrt(1e-4), s.Value, 1e-8);
    }

    [Fact]
    public void LargeArgument()
    {
        var d = Differentiation.Derivative(Math.Sin, 1e6);
        Approx.Equal(Math.Cos(1e6), d.Value, 1e-7, 1e-8);
    }

    [Fact]
    public void LinearFunctionIsExact()
    {
        var d = Differentiation.Derivative(x => 3 * x + 1, 7);
        Approx.Equal(3, d.Value, 1e-14);
    }

    [Fact]
    public void InvalidOrderThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Differentiation.Derivative(Math.Sin, 0, 5));
    }

    [Fact]
    public void Gradient()
    {
        var g = Differentiation.Gradient(v => v[0] * v[0] * v[1] + Math.Sin(v[1]), [1.0, 2.0]);
        Approx.Equal(4, g[0], 1e-12);
        Approx.Equal(1 + Math.Cos(2), g[1], 1e-12);
    }

    [Fact]
    public void Jacobian()
    {
        var J = Differentiation.Jacobian(v => [v[0] * v[1], v[0] + v[1] * v[1], Math.Exp(v[0])], [2.0, 3.0]);
        Approx.Equal(Matrix.FromRows([3, 2], [1, 6], [Math.Exp(2), 0]), J, 1e-9);
    }

    [Fact]
    public void Hessian()
    {
        var H = Differentiation.Hessian(v => v[0] * v[0] * v[1] + v[1] * v[1] * v[1], [1.0, 2.0]);
        Approx.Equal(Matrix.FromRows([4, 2], [2, 12]), H, 1e-7);
        Assert.Equal(H[0, 1], H[1, 0]);
    }

    [Fact]
    public void PartialDerivative()
    {
        var d = Differentiation.PartialDerivative(v => Math.Exp(v[0] * v[1]), [1.0, 2.0], 1);
        Approx.Equal(Math.Exp(2), d.Value, 1e-12);
    }
}
