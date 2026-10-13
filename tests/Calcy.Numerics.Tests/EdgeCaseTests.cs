namespace Calcy.Numerics.Tests;

public class EdgeCaseTests
{
    [Fact]
    public void NewtonSystemRestartsEscapeALocalMinimum()
    {
        // Newton on x^3 - 2x + 2 from 0 stalls near the local minimum of |f|; restarts find the real root.
        Func<double[], double[]> F = v => [v[0] * v[0] * v[0] - 2 * v[0] + 2];
        var plain = RootFinding.NewtonSystem(F, [0.0]);
        Assert.False(plain.Converged);
        Assert.NotNull(plain.Message);
        var r = RootFinding.NewtonSystem(F, [0.0], restarts: 20);
        Assert.True(r.Converged);
        Approx.Equal(-1.769292354238631, r.X[0], 1e-12);
    }

    [Fact]
    public void CancellationIsHonoured()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => RootFinding.FindAllRoots(Math.Sin, 0, 10, 2000, cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => Integration.Integrate(Math.Sin, 0, 10, cancellationToken: cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => Ode.DormandPrince((t, y) => [y[0]], 0, [1.0], 1, null, cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => Polynomial.AllRoots([1, 2, 3, 4, 5], cts.Token));
    }

    [Fact]
    public void IntegrandUndefinedOnPartOfTheRange()
    {
        var r = Integration.Integrate(Math.Sqrt, -1, 1);
        Assert.False(r.Success);
        Assert.Contains("not finite", r.Message);
    }

    [Fact]
    public void OdeBlowUpIsReported()
    {
        var s = Ode.DormandPrince((t, y) => [y[0] * y[0]], 0, [1.0], 2);
        Assert.False(s.Success);
        Assert.NotNull(s.Message);
        Assert.InRange(s.FinalTime, 0.99, 1.001);
    }

    [Fact]
    public void DerivativeWithoutDefinedNeighbourhood()
    {
        var d = Differentiation.Derivative(Math.Sqrt, 0);
        Assert.True(double.IsNaN(d.Value));
        Assert.Equal(double.PositiveInfinity, d.ErrorEstimate);
    }

    [Fact]
    public void IterationCapsAreReported()
    {
        Assert.False(Optimization.BrentMinimize(x => x * x, -10, 7, 1e-12, 3).Converged);
        Assert.False(RootFinding.Bisection(x => x - 0.3, 0, 1, 0, 5).Converged);
        var lp = LinearProgramming.Maximize([3, 5], [new LinearConstraint([1, 0], ConstraintRelation.LessOrEqual, 4), new LinearConstraint([3, 2], ConstraintRelation.LessOrEqual, 18)], 1);
        Assert.Equal(LpStatus.IterationLimit, lp.Status);
        var nm = Optimization.NelderMead(v => v[0] * v[0] + v[1] * v[1], [5.0, 5.0], 0, 1e-12, 20);
        Assert.False(nm.Converged);
    }

    [Fact]
    public void SmallAndEmptyMatrices()
    {
        Assert.Equal(1, new Matrix(0, 0).Determinant());
        Assert.Equal(new Complex(7, 0), Matrix.FromRows([7.0]).Eigenvalues()[0]);
        Assert.Equal(new Matrix(2, 3), new Matrix(2, 3).Rref());
        Assert.Equal(0, new Matrix(2, 2).Norm2());
        Assert.Empty(new Matrix(0, 0).Eigenvalues());
    }

    [Fact]
    public void IntervalProperties()
    {
        var x = new Interval(1, 3);
        Assert.Equal(2, x.Width);
        Assert.Equal(2, x.Midpoint);
        Assert.True(x.Contains(1) && x.Contains(3) && !x.Contains(3.0000001));
        Assert.True(x.IsStrictlyPositive);
        Assert.False(x.IsPoint);
        Assert.Equal(0, Interval.Entire.Midpoint);
        Assert.True(double.IsNaN(Interval.Empty.Width));
        Assert.True(Interval.Point(double.NaN).IsEmpty);
        Assert.Equal("[1, 3]", x.ToString());
    }

    [Fact]
    public void ComplexSpecialValues()
    {
        Assert.True(Complex.Pow(Complex.Zero, -1.0).IsInfinity);
        Assert.True(Complex.Pow(Complex.Zero, new Complex(-1, 1)).IsNaN);
        var e = Complex.Exp(new Complex(-1000, 3));
        Assert.Equal(0, e.Re);
        Assert.True(Complex.Log(Complex.Zero).Re == double.NegativeInfinity);
        Approx.Equal(new Complex(Math.Log(2), Math.PI), Complex.Log(new Complex(-2, 0)));
    }

    [Fact]
    public void FourierEmptyAndSingle()
    {
        Assert.Empty(Fourier.Forward([]));
        Assert.Empty(Fourier.Inverse([]));
        Assert.Equal(new Complex(3, 1), Assert.Single(Fourier.Forward([new Complex(3, 1)])));
        Assert.Empty(Fourier.Convolve(Array.Empty<double>(), [1.0]));
    }

    [Fact]
    public void GlobalExtremaOfConstantFunction()
    {
        var r = Optimization.FindGlobalMinimum(_ => 2, -1, 1);
        Assert.Equal(2, r.Value);
        Assert.Empty(r.LocalExtrema);
        Assert.False(r.IsUnbounded);
    }

    [Fact]
    public void RootsOfFunctionWithZeroPlateau()
    {
        // f = max(0, x - 0.25) vanishes on [-1, 0.25]: the ends of the zero run are reported.
        var roots = RootFinding.FindAllRoots(x => Math.Max(0, x - 0.25), -1, 1, 1000);
        Assert.Equal(2, roots.Count);
        Assert.Equal(-1, roots[0].X);
        Approx.Equal(0.25, roots[1].X, 1e-12);
    }
}
