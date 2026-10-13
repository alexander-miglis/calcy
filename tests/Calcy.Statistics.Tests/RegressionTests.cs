using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

public class RegressionTests
{
    private static readonly double[] X = [1, 2, 3, 4, 5, 6, 7];
    private static readonly double[] Y = [2.3, 4.1, 6.2, 8.4, 9.9, 12.5, 13.9];

    [Fact]
    public void LinearAxBMatchesReference()
    {
        var r = Regression.Fit(RegressionModel.LinearAxB, X, Y);
        Tol.Rel(1.975, r["a"], 1e-14);
        Tol.Rel(0.2857142857142855, r["b"], 1e-12);
        Tol.Rel(0.9985789433445107, r.R!.Value, 1e-14);
        Tol.Rel(0.9971599060910395, r.RSquared, 1e-14);
        Tol.Rel(0.9965918873092474, r.AdjustedRSquared, 1e-14);
        Tol.Rel(0.24942791687035695, r.StandardError, 1e-13);
        Tol.Rel(0.0471374455768469, r.SlopeStandardError!.Value, 1e-13);
        Tol.Rel(0.2108050651910529, r.InterceptStandardError!.Value, 1e-13);
        Assert.Equal(5, r.DegreesOfFreedom);
        Assert.Equal("y = a*x + b", r.EquationTemplate);
        Assert.Equal(7, r.Residuals.Count);
        Tol.Abs(0, r.Residuals.Sum(), 1e-13);
        Tol.Rel(1.975 * 10 + 0.2857142857142855, r.Predict(10), 1e-13);
    }

    [Fact]
    public void LinearABxSwapsParameterNames()
    {
        var r = Regression.Fit(RegressionModel.LinearABx, X, Y);
        Tol.Rel(0.2857142857142855, r["a"], 1e-12);
        Tol.Rel(1.975, r["b"], 1e-14);
        Assert.Equal("y = a + b*x", r.EquationTemplate);
    }

    [Fact]
    public void EquationTextSubstitutesValuesWithSigns()
    {
        var r = Regression.Fit(RegressionModel.LinearAxB, [0, 1, 2], [-3, -1, 1]);
        Assert.Equal("y = 2*x - 3", r.Equation);
        var r2 = Regression.Fit(RegressionModel.LinearABx, [0, 1, 2], [-3, -1, 1]);
        Assert.Equal("y = -3 + 2*x", r2.Equation);
        var q = Regression.Fit(RegressionModel.Quadratic, [-1, 0, 1, 2], [6, 1, -2, -3]);
        Assert.Equal("y = 1*x^2 - 4*x + 1", q.Equation);
        var e = Regression.Fit(RegressionModel.Exponential, [0, 1, 2, 3], [2, 6, 18, 54]);
        Assert.Equal("y = 2*3^x", e.Equation);
        var l = Regression.Fit(RegressionModel.LinearAxB, X, Y);
        Assert.Equal("y = 1.975*x + 0.2857", l.FormatEquation(4));
    }

    [Fact]
    public void QuadraticMatchesExactLeastSquares()
    {
        double[] px = [0, 1, 2, 3, 4, 5, 6, 7, 8];
        double[] py = [1.2, 1.9, 4.1, 8.8, 16.5, 26.0, 37.1, 50.3, 65.2];
        var r = Regression.Fit(RegressionModel.Quadratic, px, py);
        Tol.Rel(1.0534632034632034, r["a"], 1e-13);
        Tol.Rel(-0.3543722943722946, r["b"], 1e-12);
        Tol.Rel(0.9945454545454547, r["c"], 1e-12);
        Tol.Rel(0.024240201660638346, r.Parameters[0].StandardError!.Value, 1e-11);
        Tol.Rel(0.20154868882079177, r.Parameters[1].StandardError!.Value, 1e-11);
        Tol.Rel(0.3457664600300574, r.Parameters[2].StandardError!.Value, 1e-11);
        Tol.Rel(0.9997447207544223, r.RSquared, 1e-13);
        Assert.Null(r.R);
        Assert.Equal("y = a*x^2 + b*x + c", r.EquationTemplate);
    }

    [Fact]
    public void CubicAndQuarticMatchReference()
    {
        double[] px = [0, 1, 2, 3, 4, 5, 6, 7, 8];
        double[] py = [1.2, 1.9, 4.1, 8.8, 16.5, 26.0, 37.1, 50.3, 65.2];
        var c = Regression.Fit(RegressionModel.Cubic, px, py);
        double[] cRef = [-0.022390572390572457, 1.3221500721500736, -1.164911014911022, 1.3707070707070794];
        for (int i = 0; i < 4; i++) Tol.Rel(cRef[i], c.Coefficients[i], 1e-9);
        Tol.Rel(0.999912743919943, c.RSquared, 1e-12);
        var q = Regression.Fit(RegressionModel.Quartic, px, py);
        double[] qRef = [-0.002797202797202732, 0.022364672364671268, 1.0995726495726559, -0.8164594664594793, 1.3035742035742122];
        for (int i = 0; i < 5; i++) Tol.Rel(qRef[i], q.Coefficients[i], 1e-8);
        Tol.Rel(0.9999235662176528, q.RSquared, 1e-12);
        Assert.Equal(["a", "b", "c", "d", "e"], q.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void PolynomialWithLargeXOffsetIsAccurate()
    {
        double[] x = [1990, 1995, 2000, 2005, 2010, 2015, 2020];
        double[] y = x.Select(t => 0.5 * Math.Pow(t - 2000, 3) - 2 * (t - 2000) + 7).ToArray();
        var r = Regression.Fit(RegressionModel.Cubic, x, y);
        Tol.Rel(0.5, r["a"], 1e-9);
        Tol.Rel(-3000, r["b"], 1e-9);
        Tol.Rel(5999998, r["c"], 1e-9);
        Tol.Rel(-3999995993, r["d"], 1e-9);
        Tol.Rel(0.5 * 27000 - 60 + 7, r.Predict(2030), 1e-11);
        foreach (var res in r.Residuals) Tol.Abs(0, res, 1e-8);
        Tol.Rel(1.0, r.RSquared, 1e-14);
    }

    [Fact]
    public void PolynomialOfHigherDegree()
    {
        double[] x = Enumerable.Range(-5, 11).Select(i => i * 0.3).ToArray();
        double[] y = x.Select(t => 1 - 2 * t + 0.5 * Math.Pow(t, 3) - 0.1 * Math.Pow(t, 5) + 0.01 * Math.Pow(t, 6)).ToArray();
        var r = Regression.FitPolynomial(6, x, y);
        Assert.Equal(RegressionModel.Polynomial, r.Model);
        double[] expected = [0.01, -0.1, 0, 0.5, 0, -2, 1];
        for (int i = 0; i < 7; i++) Tol.Abs(expected[i], r.Coefficients[i], 1e-10);
        Assert.Equal("y = a*x^6 + b*x^5 + c*x^4 + d*x^3 + e*x^2 + f*x + g", r.EquationTemplate);
        var lin = Regression.FitPolynomial(1, X, Y);
        Tol.Rel(1.975, lin["a"], 1e-13);
    }

    [Fact]
    public void ExponentialPowerAndLogarithmicUseTransformedFits()
    {
        double[] ex = [1, 2, 3, 4, 5, 6];
        double[] ey = [3.1, 4.9, 8.2, 13.9, 22.5, 37.8];
        var e = Regression.Fit(RegressionModel.Exponential, ex, ey);
        Tol.Rel(1.8341254827678914, e["a"], 1e-13);
        Tol.Rel(1.6536810012331673, e["b"], 1e-13);
        Tol.Rel(0.9998358369957953, e.R!.Value, 1e-13);
        Tol.Rel(0.9998358369957953 * 0.9998358369957953, e.RSquared, 1e-13);
        Tol.Rel(ey[0] - e.Predict(ex[0]), e.Residuals[0], 1e-12);
        var ee = Regression.Fit(RegressionModel.ExponentialE, ex, ey);
        Tol.Rel(1.8341254827678914, ee["a"], 1e-13);
        Tol.Rel(0.5030037129662864, ee["b"], 1e-13);
        Tol.Rel(e.Predict(4.5), ee.Predict(4.5), 1e-13);
        var p = Regression.Fit(RegressionModel.Power, ex, ey);
        Tol.Rel(2.3750748023022066, p["a"], 1e-13);
        Tol.Rel(1.3698080277308782, p["b"], 1e-13);
        Tol.Rel(0.964413167828354, p.R!.Value, 1e-13);
        var l = Regression.Fit(RegressionModel.Logarithmic, ex, ey);
        Tol.Rel(-3.157409055595721, l["a"], 1e-12);
        Tol.Rel(16.619589495833715, l["b"], 1e-13);
        Tol.Rel(0.8362618646424693, l.R!.Value, 1e-13);
        Assert.Equal("y = a + b*ln(x)", l.EquationTemplate);
    }

    [Fact]
    public void ExponentialAcceptsAllNegativeY()
    {
        var e = Regression.Fit(RegressionModel.Exponential, [0, 1, 2], [-2, -4, -8]);
        Tol.Rel(-2, e["a"], 1e-14);
        Tol.Rel(2, e["b"], 1e-14);
    }

    [Fact]
    public void MedianMedianLine()
    {
        var r = Regression.Fit(RegressionModel.MedianMedian, [1, 2, 3, 4, 5, 6, 7, 8, 9], [2, 4, 5, 4, 5, 7, 8, 9, 10]);
        Tol.Rel(5.0 / 6, r["a"], 1e-14);
        Tol.Rel(5.5 / 3, r["b"], 1e-14);
        Assert.Null(r.R);
        double[] x10 = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();
        var r10 = Regression.Fit(RegressionModel.MedianMedian, x10, x10.Select(v => v * v).ToArray());
        Tol.Rel(11, r10["a"], 1e-14);
        Tol.Rel(-22, r10["b"], 1e-14);
        double[] x11 = Enumerable.Range(1, 11).Select(i => (double)i).ToArray();
        var r11 = Regression.Fit(RegressionModel.MedianMedian, x11.Reverse().ToArray(), x11.Reverse().Select(v => v * v).ToArray());
        Tol.Rel(12, r11["a"], 1e-14);
        Tol.Rel(-83.0 / 3, r11["b"], 1e-14);
    }

    [Fact]
    public void LogisticRecoversExactParameters()
    {
        double[] x = Enumerable.Range(0, 20).Select(i => (double)i).ToArray();
        double[] y = x.Select(t => 10.0 / (1 + 25 * Math.Exp(-0.7 * t))).ToArray();
        var r = Regression.Fit(RegressionModel.Logistic, x, y);
        Assert.True(r.Converged);
        Tol.Rel(25, r["a"], 1e-8);
        Tol.Rel(0.7, r["b"], 1e-8);
        Tol.Rel(10, r["c"], 1e-8);
        Assert.Equal("y = c/(1 + a*e^(-b*x))", r.EquationTemplate);
        Assert.Equal("y = 10/(1 + 25*e^(-0.7*x))", r.Equation);
    }

    [Fact]
    public void LogisticOnNoisyDataIsALeastSquaresMinimum()
    {
        double[] x = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        double[] y = [5, 9, 16, 28, 45, 70, 98, 125, 147, 162, 171, 176];
        var r = Regression.Fit(RegressionModel.Logistic, x, y);
        Assert.True(r.Converged);
        AssertLocalMinimum(x, y, r.Coefficients, (p, t) => p[2] / (1 + p[0] * Math.Exp(-p[1] * t)));
        Assert.True(r.RSquared > 0.99);
    }

    [Fact]
    public void SinusoidalRecoversExactParameters()
    {
        double[] x = Enumerable.Range(0, 25).Select(i => i * 0.5).ToArray();
        double[] y = x.Select(t => 3 * Math.Sin(1.3 * t + 0.4) + 2).ToArray();
        var r = Regression.Fit(RegressionModel.Sinusoidal, x, y);
        Tol.Rel(3, r["a"], 1e-9);
        Tol.Rel(1.3, r["b"], 1e-9);
        Tol.Rel(0.4, r["c"], 1e-8);
        Tol.Rel(2, r["d"], 1e-9);
        Assert.Equal("y = a*sin(b*x + c) + d", r.EquationTemplate);
    }

    [Fact]
    public void SinusoidalNormalisesSignsAndPhase()
    {
        double[] x = Enumerable.Range(0, 30).Select(i => i * 0.37).ToArray();
        double[] y = x.Select(t => -2 * Math.Sin(-0.9 * t + 1.0) - 1).ToArray();
        var r = Regression.FitSinusoidal(x, y);
        Assert.True(r["a"] > 0);
        Assert.True(r["b"] > 0);
        Assert.InRange(r["c"], -Math.PI, Math.PI);
        for (int i = 0; i < x.Length; i++) Tol.Abs(y[i], r.Predict(x[i]), 1e-8);
    }

    [Fact]
    public void SinusoidalMonthlyTemperaturesWithPeriodGuess()
    {
        double[] m = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        double[] t = [-5, -3, 2, 9, 15, 20, 23, 22, 17, 10, 3, -2];
        var auto = Regression.Fit(RegressionModel.Sinusoidal, m, t);
        var guided = Regression.FitSinusoidal(m, t, period: 12);
        AssertLocalMinimum(m, t, auto.Coefficients, (p, v) => p[0] * Math.Sin(p[1] * v + p[2]) + p[3]);
        Tol.Rel(auto.ResidualSumOfSquares, guided.ResidualSumOfSquares, 1e-6);
        Assert.InRange(2 * Math.PI / auto["b"], 11, 13.5);
    }

    [Fact]
    public void WeightsActAsFrequencies()
    {
        double[] x = [1, 2, 3, 4];
        double[] y = [1.5, 2.1, 3.9, 4.2];
        double[] w = [2, 1, 3, 1];
        var weighted = Regression.Fit(RegressionModel.LinearAxB, x, y, w);
        var expanded = Regression.Fit(RegressionModel.LinearAxB, [1, 1, 2, 3, 3, 3, 4], [1.5, 1.5, 2.1, 3.9, 3.9, 3.9, 4.2]);
        Tol.Rel(expanded["a"], weighted["a"], 1e-14);
        Tol.Rel(expanded["b"], weighted["b"], 1e-13);
        Tol.Rel(expanded.StandardError, weighted.StandardError, 1e-13);
        Tol.Rel(expanded.R!.Value, weighted.R!.Value, 1e-14);
        Assert.Equal(7, weighted.N);
        var wq = Regression.Fit(RegressionModel.Quadratic, x, y, w);
        var eq = Regression.Fit(RegressionModel.Quadratic, [1, 1, 2, 3, 3, 3, 4], [1.5, 1.5, 2.1, 3.9, 3.9, 3.9, 4.2]);
        Tol.Rel(eq["a"], wq["a"], 1e-12);
        Tol.Rel(eq.Parameters[0].StandardError!.Value, wq.Parameters[0].StandardError!.Value, 1e-11);
    }

    [Fact]
    public void MultipleLinearRegression()
    {
        double[] x1 = [1, 2, 3, 4, 5, 6, 7, 8];
        double[] x2 = [2, 1, 4, 3, 6, 5, 8, 9];
        double[] y = [3.1, 3.9, 7.2, 7.8, 11.1, 11.0, 14.8, 16.9];
        var r = Regression.MultipleLinear([x1, x2], y);
        double[] coef = [0.647058823529412, 1.16078431372549, 0.7588235294117647];
        double[] se = [0.20059120615744092, 0.10957258544088434, 0.09531912848576832];
        double[] t = [3.2257586756896295, 10.593747597128175, 7.960873556718065];
        double[] p = [0.02331562717051931, 0.00012955758984019303, 0.0005043651951885726];
        for (int i = 0; i < 3; i++)
        {
            Tol.Rel(coef[i], r.Coefficients[i], 1e-12);
            Tol.Rel(se[i], r.StandardErrors[i], 1e-12);
            Tol.Rel(t[i], r.TStatistics[i], 1e-12);
            Tol.Rel(p[i], r.PValues[i], 1e-10);
        }
        Tol.Rel(0.9980316946141544, r.RSquared, 1e-14);
        Tol.Rel(0.9972443724598162, r.AdjustedRSquared, 1e-14);
        Tol.Rel(1267.6281101895759, r.F, 1e-11);
        Tol.Rel(1.7188231805797754e-07, r.FPValue, 1e-9);
        Assert.Equal(2, r.RegressionDegreesOfFreedom);
        Assert.Equal(5, r.ResidualDegreesOfFreedom);
        Tol.Rel(coef[0] + coef[1] * 2 + coef[2] * 3, r.Predict([2, 3]), 1e-13);
        var ci = r.CoefficientIntervals(0.95);
        Assert.Equal(3, ci.Count);
        Assert.True(ci[1].Lower < coef[1] && coef[1] < ci[1].Upper);
        Tol.Rel(r.TotalSumOfSquares, r.RegressionSumOfSquares + r.ResidualSumOfSquares, 1e-13);
    }

    [Fact]
    public void LevenbergMarquardtGeneralModel()
    {
        double[] x = Enumerable.Range(0, 15).Select(i => i * 0.4).ToArray();
        double[] y = x.Select(t => 4.2 * Math.Exp(-0.8 * t) + 0.5).ToArray();
        Func<double[], double, double> model = (p, t) => p[0] * Math.Exp(-p[1] * t) + p[2];
        var fit = NonlinearFit.LevenbergMarquardt(model, x, y, [1, 0.1, 0]);
        Assert.True(fit.Converged);
        Tol.Rel(4.2, fit.Parameters[0], 1e-9);
        Tol.Rel(0.8, fit.Parameters[1], 1e-9);
        Tol.Rel(0.5, fit.Parameters[2], 1e-9);
        Assert.True(fit.ResidualSumOfSquares < 1e-18);
        var withGradient = NonlinearFit.LevenbergMarquardt(model, x, y, [1, 0.1, 0],
            gradient: (p, t) => [Math.Exp(-p[1] * t), -p[0] * t * Math.Exp(-p[1] * t), 1]);
        Tol.Rel(0.8, withGradient.Parameters[1], 1e-10);
    }

    [Fact]
    public void LevenbergMarquardtStandardErrorsMatchLinearTheory()
    {
        // For a model that is linear in its parameters the LM standard errors equal the OLS ones.
        var fit = NonlinearFit.LevenbergMarquardt((p, t) => p[0] * t + p[1], X, Y, [0, 0]);
        Tol.Rel(1.975, fit.Parameters[0], 1e-10);
        Tol.Rel(0.0471374455768469, fit.StandardErrors[0], 1e-7);
        Tol.Rel(0.2108050651910529, fit.StandardErrors[1], 1e-7);
        Tol.Rel(0.24942791687035695, fit.StandardError, 1e-9);
        Assert.Equal(5, fit.DegreesOfFreedom);
    }

    [Fact]
    public void InvalidRegressionInputsThrow()
    {
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.LinearAxB, [2, 2, 2], [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.LinearAxB, [1], [1]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.Logarithmic, [0, 1, 2], [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.Exponential, [0, 1, 2], [1, -2, 3]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.Power, [-1, 1, 2], [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.Quartic, [1, 2, 3, 4], [1, 2, 3, 4]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.Cubic, [1, 1, 2, 2, 3, 3], [1, 2, 3, 4, 5, 6]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.Polynomial, X, Y));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.Sinusoidal, [1, 2, 3], [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => Regression.MultipleLinear([[1, 2, 3, 4], [2, 4, 6, 8]], [1, 2, 3, 5]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.LinearAxB, [1, 2], [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.MedianMedian, [1, 2, 3], [1, 2, 3], [0.5, 1, 1]));
        Assert.Throws<ArgumentException>(() => Regression.Fit(RegressionModel.LinearAxB, X, Y)["z"]);
    }

    private static void AssertLocalMinimum(double[] x, double[] y, double[] p, Func<double[], double, double> f)
    {
        double Sse(double[] q)
        {
            double s = 0;
            for (int i = 0; i < x.Length; i++) { double r = y[i] - f(q, x[i]); s += r * r; }
            return s;
        }
        double s0 = Sse(p);
        for (int j = 0; j < p.Length; j++)
        {
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var q = (double[])p.Clone();
                q[j] += sign * 1e-4 * Math.Max(Math.Abs(p[j]), 1e-3);
                Assert.True(Sse(q) >= s0 * (1 - 1e-12), $"parameter {j} perturbed by {sign} reduces the SSE");
            }
        }
    }
}
