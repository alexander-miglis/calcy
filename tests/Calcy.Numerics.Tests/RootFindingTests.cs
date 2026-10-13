namespace Calcy.Numerics.Tests;

public class RootFindingTests
{
    [Fact]
    public void BrentFindsSqrt2()
    {
        var r = RootFinding.Brent(x => x * x - 2, 0, 2);
        Assert.True(r.Converged);
        Approx.Equal(Math.Sqrt(2), r.Root, 1e-15);
    }

    [Fact]
    public void BrentDottieNumber()
    {
        var r = RootFinding.Brent(x => Math.Cos(x) - x, 0, 1);
        Approx.Equal(0.7390851332151607, r.Root, 1e-15);
        Assert.True(r.Iterations < 20);
    }

    [Fact]
    public void BrentRejectsNonBracket()
    {
        Assert.Throws<ArgumentException>(() => RootFinding.Brent(x => x * x + 1, -1, 1));
        Assert.Throws<ArgumentException>(() => RootFinding.Brent(x => Math.Log(x), -1, 2));
    }

    [Fact]
    public void Bisection()
    {
        var r = RootFinding.Bisection(x => x * x * x - x - 2, 1, 2);
        Assert.True(r.Converged);
        Approx.Equal(1.5213797068045676, r.Root, 1e-14);
    }

    [Fact]
    public void NewtonAnalyticAndNumeric()
    {
        var r1 = RootFinding.Newton(x => x * x - 2, 1, x => 2 * x);
        Assert.True(r1.Converged);
        Approx.Equal(Math.Sqrt(2), r1.Root, 1e-15);
        var r2 = RootFinding.Newton(x => Math.Exp(x) - 3, 0);
        Assert.True(r2.Converged);
        Approx.Equal(Math.Log(3), r2.Root, 1e-14);
    }

    [Fact]
    public void NewtonDampingTamesCubeRoot()
    {
        // Plain Newton diverges for cbrt(x); damping restores convergence.
        var r = RootFinding.Newton(Math.Cbrt, 1);
        Assert.True(r.Converged);
        Assert.True(Math.Abs(r.Root) < 1e-10);
    }

    [Fact]
    public void NewtonReportsZeroDerivative()
    {
        var r = RootFinding.Newton(x => x * x + 1, 0, x => 2 * x);
        Assert.False(r.Converged);
        Assert.NotNull(r.Message);
    }

    [Fact]
    public void Secant()
    {
        var r = RootFinding.Secant(x => x * x * x - 2 * x - 5, 2, 3);
        Assert.True(r.Converged);
        Approx.Equal(2.0945514815423265, r.Root, 1e-14);
    }

    [Fact]
    public void FindRootFromGuess()
    {
        var r = RootFinding.FindRoot(x => x * x - 2, 1);
        Approx.Equal(Math.Sqrt(2), r.Root, 1e-14);
        var t = RootFinding.FindRoot(Math.Tan, 1.5);
        Assert.True(t.Converged);
        Assert.True(Math.Abs(Math.Tan(t.Root)) < 1e-12);
    }

    [Fact]
    public void AllRootsOfSine()
    {
        var roots = RootFinding.FindAllRoots(Math.Sin, 0, 20);
        Assert.Equal(7, roots.Count);
        for (int k = 0; k < 7; k++)
        {
            Approx.Equal(k * Math.PI, roots[k].X, 1e-14, 1e-14);
            Assert.Equal(RootKind.SignChange, roots[k].Kind);
        }
    }

    [Fact]
    public void TouchingRootOfSquare()
    {
        var roots = RootFinding.FindAllRoots(x => x * x, -1, 1.3);
        var r = Assert.Single(roots);
        Assert.True(Math.Abs(r.X) < 1e-7, $"root at {r.X}");
        Assert.Equal(RootKind.Touching, r.Kind);
    }

    [Fact]
    public void TouchingRootOnGridPoint()
    {
        var roots = RootFinding.FindAllRoots(x => x * x, -1, 1);
        var r = Assert.Single(roots);
        Assert.Equal(0, r.X);
        Assert.Equal(RootKind.Touching, r.Kind);
    }

    [Fact]
    public void TouchingRootExpandedForm()
    {
        var roots = RootFinding.FindAllRoots(x => x * x - 2 * x + 1, 0, 3.1);
        var r = Assert.Single(roots);
        Approx.Equal(1, r.X, 1e-7);
        Assert.Equal(RootKind.Touching, r.Kind);
    }

    [Fact]
    public void TouchingRootOfSineSquared()
    {
        var roots = RootFinding.FindAllRoots(x => Math.Pow(Math.Sin(x), 2), 1, 10);
        Assert.Equal(3, roots.Count);
        for (int k = 0; k < 3; k++)
        {
            Approx.Equal((k + 1) * Math.PI, roots[k].X, 1e-8);
            Assert.Equal(RootKind.Touching, roots[k].Kind);
        }
    }

    [Fact]
    public void PolesOfTangentAreNotRoots()
    {
        var roots = RootFinding.FindAllRoots(Math.Tan, 0, 10);
        Assert.Equal(4, roots.Count);
        for (int k = 0; k < 4; k++) Approx.Equal(k * Math.PI, roots[k].X, 1e-13, 1e-13);
    }

    [Fact]
    public void ReciprocalHasNoRoots()
    {
        Assert.Empty(RootFinding.FindAllRoots(x => 1 / x, -1, 1.3));
        Assert.Empty(RootFinding.FindAllRoots(x => 1 / x, -1, 1));
    }

    [Fact]
    public void JumpIsNotARoot()
    {
        Assert.Empty(RootFinding.FindAllRoots(x => x < 0.3 ? -1 : 1, 0, 1));
        // sign(x - 0.3) genuinely vanishes at 0.3 when that point is evaluated.
        Assert.All(RootFinding.FindAllRoots(x => Math.Sign(x - 0.3), 0, 1), r => Approx.Equal(0.3, r.X, 1e-15));
        Assert.Empty(RootFinding.FindAllRoots(x => Math.Floor(x) - 0.5, 0, 3));
    }

    [Fact]
    public void NoRootsForPositiveFunctions()
    {
        Assert.Empty(RootFinding.FindAllRoots(Math.Exp, -5, 5));
        Assert.Empty(RootFinding.FindAllRoots(x => x * x + 1, -3, 3));
        Assert.Empty(RootFinding.FindAllRoots(x => x * x + 1e-10, -1, 1.3));
    }

    [Fact]
    public void TripleRootIsFound()
    {
        var roots = RootFinding.FindAllRoots(x => x * x * x, -1, 1.3);
        var r = Assert.Single(roots);
        Assert.True(Math.Abs(r.X) < 1e-5);
        Assert.Equal(RootKind.SignChange, r.Kind);
    }

    [Fact]
    public void CloseRootsBetweenSamplesAreSeparated()
    {
        var roots = RootFinding.FindAllRoots(x => (x - 0.5) * (x - 0.5000001), 0, 1);
        Assert.Equal(2, roots.Count);
        Approx.Equal(0.5, roots[0].X, 1e-12);
        Approx.Equal(0.5000001, roots[1].X, 1e-12);
    }

    [Fact]
    public void ProductPolynomialRoots()
    {
        Func<double, double> f = x =>
        {
            double p = 1;
            for (int k = 1; k <= 10; k++) p *= x - k;
            return p;
        };
        var roots = RootFinding.FindAllRoots(f, 0, 11);
        Assert.Equal(10, roots.Count);
        for (int k = 0; k < 10; k++) Approx.Equal(k + 1, roots[k].X, 1e-12);
    }

    [Fact]
    public void OscillatingFunction()
    {
        var roots = RootFinding.FindAllRoots(x => Math.Sin(1 / x), 0.1, 1, 4000);
        Assert.Equal(3, roots.Count);
        Approx.Equal(1 / (3 * Math.PI), roots[0].X, 1e-12);
        Approx.Equal(1 / (2 * Math.PI), roots[1].X, 1e-12);
        Approx.Equal(1 / Math.PI, roots[2].X, 1e-12);
    }

    [Fact]
    public void RootAtDomainBoundary()
    {
        var roots = RootFinding.FindAllRoots(x => Math.Sqrt(x - 0.5), 0, 1.37);
        var r = Assert.Single(roots);
        Approx.Equal(0.5, r.X, 1e-9);
        Assert.Equal(RootKind.Touching, r.Kind);
    }

    [Fact]
    public void LogarithmWithUndefinedRegion()
    {
        var roots = RootFinding.FindAllRoots(Math.Log, -1, 2);
        var r = Assert.Single(roots);
        Approx.Equal(1, r.X, 1e-14);
        Assert.Equal(RootKind.SignChange, r.Kind);
    }

    [Fact]
    public void SteepRootIsAccepted()
    {
        var roots = RootFinding.FindAllRoots(x => Math.Cbrt(x - 0.31), 0, 1);
        var r = Assert.Single(roots);
        Approx.Equal(0.31, r.X, 1e-12);
    }

    [Fact]
    public void NewtonSystemCircleAndLine()
    {
        var r = RootFinding.NewtonSystem(v => [v[0] * v[0] + v[1] * v[1] - 4, v[0] - v[1]], [1.0, 0.5]);
        Assert.True(r.Converged);
        Approx.Equal(Math.Sqrt(2), r.X[0], 1e-12);
        Approx.Equal(Math.Sqrt(2), r.X[1], 1e-12);
    }

    [Fact]
    public void NewtonSystemWithSingularJacobianAtSolution()
    {
        var r = RootFinding.NewtonSystem(v => [v[0] * v[0], v[1] - 1], [1.0, 0.0], 1e-12);
        Assert.True(r.Converged);
        Assert.True(Math.Abs(r.X[0]) < 1e-5);
        Approx.Equal(1, r.X[1], 1e-12);
    }

    [Fact]
    public void NewtonSystemThreeVariables()
    {
        // x + y + z = 6, x y z = 6, x^2 + y^2 + z^2 = 14 has the solution (1, 2, 3) up to order.
        var r = RootFinding.NewtonSystem(v => [v[0] + v[1] + v[2] - 6, v[0] * v[1] * v[2] - 6, v[0] * v[0] + v[1] * v[1] + v[2] * v[2] - 14], [0.8, 2.2, 3.5]);
        Assert.True(r.Converged);
        var sorted = r.X.OrderBy(v => v).ToArray();
        Approx.Equal(new[] { 1.0, 2.0, 3.0 }, sorted, 1e-10);
    }

    [Fact]
    public void MultiStartFindsAllIntersections()
    {
        var sols = RootFinding.FindSystemSolutions(v => [v[0] * v[0] + v[1] * v[1] - 4, v[0] * v[1] - 1], [-3.0, -3.0], [3.0, 3.0], 80);
        Assert.Equal(4, sols.Count);
        foreach (var s in sols)
        {
            Assert.True(s.ResidualNorm < 1e-10);
            Approx.Equal(1, s.X[0] * s.X[1], 1e-10);
        }
        var xs = sols.Select(s => s.X[0]).OrderBy(v => v).ToArray();
        Approx.Equal(-1.9318516525781366, xs[0], 1e-10);
        Approx.Equal(-0.5176380902050415, xs[1], 1e-10);
        Approx.Equal(0.5176380902050415, xs[2], 1e-10);
        Approx.Equal(1.9318516525781366, xs[3], 1e-10);
    }
}
