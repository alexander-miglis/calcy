namespace Calcy.Numerics.Tests;

public class OdeTests
{
    private static double[] Growth(double t, double[] y) => [y[0]];

    [Fact]
    public void EulerMatchesClosedForm()
    {
        var s = Ode.Euler(Growth, 0, [1.0], 1, 1000);
        Assert.True(s.Success);
        Approx.Equal(Math.Pow(1.001, 1000), s.FinalState[0], 1e-12);
        Assert.Equal(1001, s.T.Count);
    }

    [Fact]
    public void HeunIsSecondOrder()
    {
        double e1 = Math.Abs(Ode.Heun(Growth, 0, [1.0], 1, 50).FinalState[0] - Math.E);
        double e2 = Math.Abs(Ode.Heun(Growth, 0, [1.0], 1, 100).FinalState[0] - Math.E);
        Approx.Equal(4, e1 / e2, 0.05);
        Assert.True(e2 < 1e-4);
    }

    [Fact]
    public void RungeKutta4IsFourthOrder()
    {
        double e1 = Math.Abs(Ode.RungeKutta4(Growth, 0, [1.0], 1, 20).FinalState[0] - Math.E);
        double e2 = Math.Abs(Ode.RungeKutta4(Growth, 0, [1.0], 1, 40).FinalState[0] - Math.E);
        Approx.Equal(16, e1 / e2, 0.05);
        Assert.True(e2 < 1e-7);
    }

    [Fact]
    public void RungeKutta4HarmonicOscillator()
    {
        var s = Ode.RungeKutta4((t, y) => [y[1], -y[0]], 0, [1.0, 0.0], 2 * Math.PI, 1000);
        Approx.Equal(1, s.FinalState[0], 1e-10);
        Assert.True(Math.Abs(s.FinalState[1]) < 1e-10);
    }

    [Fact]
    public void DormandPrinceGaussian()
    {
        var opts = new OdeOptions { RelativeTolerance = 1e-10, AbsoluteTolerance = 1e-12 };
        var s = Ode.DormandPrince((t, y) => [-2 * t * y[0]], 0, [1.0], 2, opts);
        Assert.True(s.Success, s.Message);
        Approx.Equal(Math.Exp(-4), s.FinalState[0], 1e-8);
        Assert.Equal(2, s.FinalTime);
    }

    [Fact]
    public void DenseOutputIsAccurate()
    {
        var opts = new OdeOptions { RelativeTolerance = 1e-10, AbsoluteTolerance = 1e-12 };
        var s = Ode.DormandPrince((t, y) => [-2 * t * y[0]], 0, [1.0], 2, opts);
        foreach (double t in new[] { 0.0, 0.37, 1.01, 1.5, 1.999 })
            Approx.Equal(Math.Exp(-t * t), s.Evaluate(t)[0], 1e-8, 1e-10);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Evaluate(2.5));
    }

    [Fact]
    public void OutputGrid()
    {
        var opts = new OdeOptions { OutputTimes = [0, 0.5, 1, 1.5, 2], RelativeTolerance = 1e-9 };
        var s = Ode.DormandPrince((t, y) => [y[1], -y[0]], 0, [0.0, 1.0], 2, opts);
        Assert.Equal(5, s.T.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(opts.OutputTimes![i], s.T[i]);
            Approx.Equal(Math.Sin(s.T[i]), s.Y[i][0], 1e-7, 1e-8);
        }
    }

    [Fact]
    public void BackwardIntegration()
    {
        var opts = new OdeOptions { RelativeTolerance = 1e-10, AbsoluteTolerance = 1e-12 };
        var s = Ode.DormandPrince(Growth, 1, [Math.E], 0, opts);
        Assert.True(s.Success);
        Approx.Equal(1, s.FinalState[0], 1e-9);
        Approx.Equal(Math.Exp(0.5), s.Evaluate(0.5)[0], 1e-8);
    }

    [Fact]
    public void TerminalEventProjectile()
    {
        var opts = new OdeOptions
        {
            RelativeTolerance = 1e-10,
            AbsoluteTolerance = 1e-12,
            Events = [new OdeEvent((t, y) => y[0], EventDirection.Falling, Terminal: true)],
        };
        var s = Ode.DormandPrince((t, y) => [y[1], -9.81], 0, [0.0, 10.0], 10, opts);
        Assert.True(s.Success);
        var hit = Assert.Single(s.Events);
        Approx.Equal(20 / 9.81, hit.T, 1e-10);
        Approx.Equal(20 / 9.81, s.FinalTime, 1e-10);
        Approx.Equal(-10, hit.Y[1], 1e-8);
    }

    [Fact]
    public void NonTerminalEventsFindZeroCrossings()
    {
        var opts = new OdeOptions
        {
            RelativeTolerance = 1e-10,
            AbsoluteTolerance = 1e-12,
            Events = [new OdeEvent((t, y) => y[0])],
        };
        var s = Ode.DormandPrince((t, y) => [y[1], -y[0]], 0, [0.0, 1.0], 10, opts);
        Assert.Equal(3, s.Events.Count);
        for (int k = 0; k < 3; k++) Approx.Equal((k + 1) * Math.PI, s.Events[k].T, 1e-8);
        Assert.Equal(10, s.FinalTime);
    }

    [Fact]
    public void EnergyIsConservedOverManyPeriods()
    {
        var opts = new OdeOptions { RelativeTolerance = 1e-11, AbsoluteTolerance = 1e-13 };
        var s = Ode.DormandPrince((t, y) => [y[1], -y[0]], 0, [1.0, 0.0], 200 * Math.PI, opts);
        var y = s.FinalState;
        Approx.Equal(1, y[0] * y[0] + y[1] * y[1], 1e-7);
        Approx.Equal(1, y[0], 1e-6);
    }

    [Theory]
    [InlineData(RosenbrockScheme.Rodas3)]
    [InlineData(RosenbrockScheme.KapsRentrop4)]
    public void RosenbrockStiffLinearProblem(RosenbrockScheme scheme)
    {
        const double lambda = 1000;
        double Exact(double t) => lambda / (lambda * lambda + 1) * (lambda * Math.Cos(t) + Math.Sin(t)) - lambda * lambda / (lambda * lambda + 1) * Math.Exp(-lambda * t);
        var s = Ode.Rosenbrock((t, y) => [-lambda * (y[0] - Math.Cos(t))], 0, [0.0], 2, new OdeOptions { RelativeTolerance = 1e-6, AbsoluteTolerance = 1e-8 }, null, scheme);
        Assert.True(s.Success, s.Message);
        Approx.Equal(Exact(2), s.FinalState[0], 1e-6);
        Approx.Equal(Exact(1), s.Evaluate(1)[0], 1e-5);
        Assert.True(s.AcceptedSteps < 1500, $"{s.AcceptedSteps} steps");
    }

    [Theory]
    [InlineData(RosenbrockScheme.Rodas3, 3)]
    [InlineData(RosenbrockScheme.KapsRentrop4, 4)]
    public void RosenbrockConvergenceOrder(RosenbrockScheme scheme, int expectedOrder)
    {
        // Fixed-size steps (huge tolerance and a max step) on y' = -y + sin(3t), y(0) = 1.
        double Run(double h)
        {
            var o = new OdeOptions { RelativeTolerance = 1e3, AbsoluteTolerance = 1e3, InitialStep = h, MaxStep = h };
            return Ode.Rosenbrock((t, y) => [-y[0] + Math.Sin(3 * t)], 0, [1.0], 1, o, null, scheme).FinalState[0];
        }
        double exact = (Math.Sin(3.0) - 3 * Math.Cos(3.0)) / 10 + 1.3 * Math.Exp(-1.0);
        double order = Math.Log2(Math.Abs(Run(0.05) - exact) / Math.Abs(Run(0.025) - exact));
        Assert.True(Math.Abs(order - expectedOrder) < 0.3, $"observed order {order}");
    }

    private static double[] Robertson(double t, double[] y) =>
    [
        -0.04 * y[0] + 1e4 * y[1] * y[2],
        0.04 * y[0] - 1e4 * y[1] * y[2] - 3e7 * y[1] * y[1],
        3e7 * y[1] * y[1],
    ];

    [Theory]
    [InlineData(RosenbrockScheme.Rodas3)]
    [InlineData(RosenbrockScheme.KapsRentrop4)]
    public void RobertsonChemicalKinetics(RosenbrockScheme scheme)
    {
        var opts = new OdeOptions { RelativeTolerance = 1e-8, AbsoluteTolerance = 1e-12 };
        var s = Ode.Rosenbrock(Robertson, 0, [1.0, 0.0, 0.0], 40, opts, null, scheme);
        Assert.True(s.Success, s.Message);
        var y = s.FinalState;
        Approx.Equal(0.7158270687, y[0], 1e-7);
        Approx.Equal(9.185534764e-6, y[1], 1e-6);
        Approx.Equal(0.2841637457, y[2], 1e-7);
        // Rosenbrock methods conserve linear invariants.
        Approx.Equal(1, y[0] + y[1] + y[2], 1e-12);
        Assert.True(s.AcceptedSteps < 3000, $"{s.AcceptedSteps} steps");
    }

    [Fact]
    public void RobertsonLongHorizon()
    {
        var opts = new OdeOptions { RelativeTolerance = 1e-6, AbsoluteTolerance = 1e-14 };
        var s = Ode.Rosenbrock(Robertson, 0, [1.0, 0.0, 0.0], 4e10, opts);
        Assert.True(s.Success, s.Message);
        var y = s.FinalState;
        Approx.Equal(5.2083e-8, y[0], 2e-2);
        Approx.Equal(2.0833e-13, y[1], 2e-2);
        Approx.Equal(1, y[0] + y[1] + y[2], 1e-12);
    }

    [Fact]
    public void RosenbrockAnalyticJacobian()
    {
        var s = Ode.Rosenbrock((t, y) => [-50 * y[0] + 50 * y[1], y[0] - y[1]], 0, [1.0, 0.0], 1,
            new OdeOptions { RelativeTolerance = 1e-8, AbsoluteTolerance = 1e-10 },
            (t, y) => Matrix.FromRows([-50, 50], [1, -1]));
        Assert.True(s.Success);
        // Conserved quantity y0 + 50 y1 and the equilibrium y0 = y1.
        Approx.Equal(1, s.FinalState[0] + 50 * s.FinalState[1], 1e-10);
        Approx.Equal(1.0 / 51, s.FinalState[0], 1e-6);
        Assert.True(s.JacobianEvaluations > 0);
    }
    [Fact]
    public void VanDerPolMu1000()
    {
        const double mu = 1000;
        var opts = new OdeOptions
        {
            RelativeTolerance = 1e-6,
            AbsoluteTolerance = 1e-6,
            Events = [new OdeEvent((t, y) => y[0], EventDirection.Falling)],
        };
        var s = Ode.Rosenbrock((t, y) => [y[1], mu * (1 - y[0] * y[0]) * y[1] - y[0]], 0, [2.0, 0.0], 3000, opts);
        Assert.True(s.Success, s.Message);
        Assert.True(s.AcceptedSteps < 20000, $"{s.AcceptedSteps} steps");
        Assert.True(s.Y.All(v => Math.Abs(v[0]) < 2.05));
        Assert.Equal(2, s.Events.Count);
        // Relaxation oscillation: period about (3 - 2 ln 2) mu + 7.014 mu^(-1/3).
        double period = s.Events[1].T - s.Events[0].T;
        Approx.Equal(1614.4, period, 2e-3);
        Approx.Equal(807.2, s.Events[0].T, 3e-3);
    }

    [Fact]
    public void ShootingLinear()
    {
        var r = Ode.Shooting((x, y, dy) => -y, 0, 0, Math.PI / 2, 1);
        Assert.True(r.Converged);
        Approx.Equal(1, r.InitialSlope, 1e-8);
        Approx.Equal(Math.Sin(Math.PI / 4), r.Solution.Evaluate(Math.PI / 4)[0], 1e-8);
    }

    [Fact]
    public void ShootingNonlinear()
    {
        // y'' = 1.5 y^2, y(0) = 4, y(1) = 1 has the solution y = 4 / (1 + x)^2 (slope -8).
        var r = Ode.Shooting((x, y, dy) => 1.5 * y * y, 0, 4, 1, 1);
        Assert.True(r.Converged);
        Assert.True(Math.Abs(r.BoundaryError) < 1e-7);
        Approx.Equal(-8, r.InitialSlope, 1e-6);
        Approx.Equal(4 / 2.25, r.Solution.Evaluate(0.5)[0], 1e-6);
    }
}
