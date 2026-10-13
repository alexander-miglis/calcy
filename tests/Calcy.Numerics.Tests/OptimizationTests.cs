namespace Calcy.Numerics.Tests;

public class OptimizationTests
{
    private static double Rosenbrock(double[] v) => 100 * Math.Pow(v[1] - v[0] * v[0], 2) + Math.Pow(1 - v[0], 2);

    private static double[] RosenbrockGradient(double[] v) =>
        [-400 * v[0] * (v[1] - v[0] * v[0]) - 2 * (1 - v[0]), 200 * (v[1] - v[0] * v[0])];

    [Fact]
    public void BrentMinimizeParabola()
    {
        var r = Optimization.BrentMinimize(x => (x - 2) * (x - 2) + 1, 0, 5);
        Assert.True(r.Converged);
        Approx.Equal(2, r.X, 1e-8);
        Approx.Equal(1, r.Value, 1e-15);
    }

    [Fact]
    public void BrentMinimizeCosine()
    {
        var r = Optimization.BrentMinimize(Math.Cos, 3, 4);
        Approx.Equal(Math.PI, r.X, 1e-8);
        Approx.Equal(-1, r.Value, 1e-15);
    }

    [Fact]
    public void BrentMinimizeAvoidsUndefinedRegion()
    {
        var r = Optimization.BrentMinimize(x => x * Math.Log(x), -1, 2);
        Approx.Equal(1 / Math.E, r.X, 1e-7);
    }

    [Fact]
    public void GlobalMinimumOfMultimodalFunction()
    {
        // Classic test problem: f(x) = sin(x) + sin(10 x / 3) on [2.7, 7.5].
        var r = Optimization.FindGlobalMinimum(x => Math.Sin(x) + Math.Sin(10 * x / 3), 2.7, 7.5);
        Approx.Equal(5.145735285, r.X, 1e-8);
        Approx.Equal(-1.899599349, r.Value, 1e-9);
        Assert.False(r.AtEndpoint);
        Assert.True(r.LocalExtrema.Count >= 4);
    }

    [Fact]
    public void GlobalMaximum()
    {
        var r = Optimization.FindGlobalMaximum(x => -(x - 1) * (x - 1), 0, 3);
        Approx.Equal(1, r.X, 1e-8);
        Approx.Equal(0, r.Value, 0, 1e-15);
    }

    [Fact]
    public void GlobalMinimumAtEndpoint()
    {
        var r = Optimization.FindGlobalMinimum(x => x, 0, 1);
        Assert.Equal(0, r.X);
        Assert.True(r.AtEndpoint);
        Assert.Empty(r.LocalExtrema);
    }

    [Fact]
    public void LocalExtremaOfCubic()
    {
        var r = Optimization.FindGlobalMinimum(x => x * x * x - 3 * x, -3, 2.5);
        Assert.Equal(-3, r.X);
        Approx.Equal(-18, r.Value);
        Assert.Equal(2, r.LocalExtrema.Count);
        Assert.Equal(ExtremumKind.Maximum, r.LocalExtrema[0].Kind);
        Approx.Equal(-1, r.LocalExtrema[0].X, 1e-8);
        Approx.Equal(2, r.LocalExtrema[0].Value, 1e-14);
        Assert.Equal(ExtremumKind.Minimum, r.LocalExtrema[1].Kind);
        Approx.Equal(1, r.LocalExtrema[1].X, 1e-8);
        Approx.Equal(-2, r.LocalExtrema[1].Value, 1e-14);
    }

    [Fact]
    public void PoleIsReportedAsUnbounded()
    {
        var r = Optimization.FindGlobalMinimum(x => 1 / x, -1, 1.3);
        Assert.True(r.IsUnbounded);
        Assert.Equal(double.NegativeInfinity, r.Value);
        Assert.True(Math.Abs(r.X) < 1e-3);
        var s = Optimization.FindGlobalMaximum(x => 1 / (x * x), -1, 1.3);
        Assert.True(s.IsUnbounded);
    }

    [Fact]
    public void NarrowGenuineMinimumIsNotAPole()
    {
        var r = Optimization.FindGlobalMinimum(x => -1 / (x * x + 1e-8), -1, 1.3);
        Assert.False(r.IsUnbounded);
        Approx.Equal(-1e8, r.Value, 1e-6);
    }

    [Fact]
    public void NelderMeadRosenbrock()
    {
        var r = Optimization.NelderMead(Rosenbrock, [-1.2, 1.0]);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(1, r.X[0], 1e-5);
        Approx.Equal(1, r.X[1], 1e-5);
        Assert.True(r.Value < 1e-10);
    }

    [Fact]
    public void NelderMeadFourDimensions()
    {
        var r = Optimization.NelderMead(v =>
        {
            double s = 0;
            for (int i = 0; i < v.Length; i++) s += (i + 1) * Math.Pow(v[i] - (i + 1), 2);
            return s;
        }, [0.0, 0.0, 0.0, 0.0]);
        Assert.True(r.Converged);
        for (int i = 0; i < 4; i++) Approx.Equal(i + 1, r.X[i], 1e-5);
    }

    [Fact]
    public void BfgsRosenbrockNumericGradient()
    {
        var r = Optimization.Bfgs(Rosenbrock, [-1.2, 1.0]);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(1, r.X[0], 1e-6);
        Approx.Equal(1, r.X[1], 1e-6);
    }

    [Fact]
    public void BfgsRosenbrockAnalyticGradient()
    {
        var r = Optimization.Bfgs(Rosenbrock, [-1.2, 1.0], RosenbrockGradient, 1e-10);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(1, r.X[0], 1e-8);
        Approx.Equal(1, r.X[1], 1e-8);
        Assert.True(r.Iterations < 200);
    }

    [Fact]
    public void BfgsIllConditionedQuadratic()
    {
        int n = 10;
        var r = Optimization.Bfgs(v =>
        {
            double s = 0;
            for (int i = 0; i < n; i++) s += Math.Pow(10, i / 3.0) * Math.Pow(v[i] - 1, 2);
            return s;
        }, new double[n]);
        Assert.True(r.Converged, r.Message);
        for (int i = 0; i < n; i++) Approx.Equal(1, r.X[i], 1e-6);
    }

    [Fact]
    public void LevenbergMarquardtExponentialFit()
    {
        double[] xs = Enumerable.Range(0, 20).Select(i => i * 0.1).ToArray();
        double[] ys = xs.Select(x => 2 * Math.Exp(-1.5 * x)).ToArray();
        var r = Optimization.LevenbergMarquardt(p => xs.Select((x, i) => p[0] * Math.Exp(p[1] * x) - ys[i]).ToArray(), [1.0, 0.0]);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(2, r.Parameters[0], 1e-8);
        Approx.Equal(-1.5, r.Parameters[1], 1e-8);
        Assert.True(r.SumOfSquares < 1e-20);
    }

    [Fact]
    public void LevenbergMarquardtNoisyFitHasCovariance()
    {
        var rng = new Random(3);
        double[] xs = Enumerable.Range(0, 50).Select(i => i * 0.2).ToArray();
        double[] ys = xs.Select(x => 3 * Math.Sin(0.7 * x + 0.2) + 0.01 * (rng.NextDouble() - 0.5)).ToArray();
        var r = Optimization.LevenbergMarquardt(p => xs.Select((x, i) => p[0] * Math.Sin(p[1] * x + p[2]) - ys[i]).ToArray(), [2.5, 0.65, 0.0]);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(3, r.Parameters[0], 1e-2);
        Approx.Equal(0.7, r.Parameters[1], 1e-3);
        Assert.NotNull(r.Covariance);
        Assert.True(r.Covariance![0, 0] > 0);
    }

    [Fact]
    public void LevenbergMarquardtRosenbrockResiduals()
    {
        var r = Optimization.LevenbergMarquardt(p => [10 * (p[1] - p[0] * p[0]), 1 - p[0]], [-1.2, 1.0]);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(1, r.Parameters[0], 1e-9);
        Approx.Equal(1, r.Parameters[1], 1e-9);
    }
}

public class LinearProgrammingTests
{
    private static LinearConstraint Le(double rhs, params double[] a) => new(a, ConstraintRelation.LessOrEqual, rhs);
    private static LinearConstraint Ge(double rhs, params double[] a) => new(a, ConstraintRelation.GreaterOrEqual, rhs);
    private static LinearConstraint Eq(double rhs, params double[] a) => new(a, ConstraintRelation.Equal, rhs);

    [Fact]
    public void ClassicMaximization()
    {
        var r = LinearProgramming.Maximize([3, 5], [Le(4, 1, 0), Le(12, 0, 2), Le(18, 3, 2)]);
        Assert.Equal(LpStatus.Optimal, r.Status);
        Approx.Equal(36, r.ObjectiveValue, 1e-12);
        Approx.Equal(new[] { 2.0, 6.0 }, r.X, 1e-12);
    }

    [Fact]
    public void DietProblemWithGreaterOrEqual()
    {
        var r = LinearProgramming.Minimize([0.6, 1], [Ge(20, 10, 4), Ge(20, 5, 5), Ge(12, 2, 6)]);
        Assert.Equal(LpStatus.Optimal, r.Status);
        Approx.Equal(2.8, r.ObjectiveValue, 1e-12);
        Approx.Equal(new[] { 3.0, 1.0 }, r.X, 1e-12);
    }

    [Fact]
    public void EqualityConstraint()
    {
        var r = LinearProgramming.Minimize([1, 1], [Eq(4, 1, 2), Le(1, 1, -1)]);
        Assert.Equal(LpStatus.Optimal, r.Status);
        Approx.Equal(2, r.ObjectiveValue, 1e-12);
        Approx.Equal(new[] { 0.0, 2.0 }, r.X, 1e-12, 1e-12);
    }

    [Fact]
    public void Infeasible()
    {
        var r = LinearProgramming.Minimize([1], [Ge(5, 1), Le(3, 1)]);
        Assert.Equal(LpStatus.Infeasible, r.Status);
    }

    [Fact]
    public void Unbounded()
    {
        var r = LinearProgramming.Maximize([1, 1], [Le(1, 1, -1)]);
        Assert.Equal(LpStatus.Unbounded, r.Status);
    }

    [Fact]
    public void BealeCyclingExampleTerminates()
    {
        var r = LinearProgramming.Minimize([-0.75, 20, -0.5, 6],
        [
            Le(0, 0.25, -8, -1, 9),
            Le(0, 0.5, -12, -0.5, 3),
            Le(1, 0, 0, 1, 0),
        ]);
        Assert.Equal(LpStatus.Optimal, r.Status);
        Approx.Equal(-1.25, r.ObjectiveValue, 1e-12);
        Approx.Equal(1, r.X[0], 1e-12);
        Approx.Equal(1, r.X[2], 1e-12);
    }

    [Fact]
    public void NegativeRightHandSide()
    {
        var r = LinearProgramming.Maximize([1, 1], [Ge(-4, -1, -1), Le(3, 1, 0)]);
        Assert.Equal(LpStatus.Optimal, r.Status);
        Approx.Equal(4, r.ObjectiveValue, 1e-12);
    }

    [Fact]
    public void RedundantEqualities()
    {
        var r = LinearProgramming.Minimize([1, 0], [Eq(2, 1, 1), Eq(4, 2, 2)]);
        Assert.Equal(LpStatus.Optimal, r.Status);
        Approx.Equal(0, r.ObjectiveValue, 0, 1e-12);
        Approx.Equal(2, r.X[1], 1e-12);
    }
}
