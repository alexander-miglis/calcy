namespace Calcy.Numerics.Tests;

public class InterpolationTests
{
    [Fact]
    public void LinearInterpolationAndExtrapolation()
    {
        var f = Interpolation.Linear([3, 0, 1], [3, 0, 2]); // unsorted input
        Assert.Equal(2.5, f.Evaluate(2));
        Assert.Equal(1, f.Evaluate(0.5));
        Assert.Equal(3.5, f.Evaluate(4));
        Assert.Equal(-2, f.Evaluate(-1));
        Assert.Equal(2, f.AsFunc()(1));
    }

    [Fact]
    public void LagrangeReproducesPolynomials()
    {
        double[] xs = [0, 1, 2, 3];
        double[] ys = xs.Select(x => x * x * x - 2 * x).ToArray();
        var p = Interpolation.Lagrange(xs, ys);
        Approx.Equal(1.5 * 1.5 * 1.5 - 3, p.Evaluate(1.5), 1e-14);
        Approx.Equal(ys[2], p.Evaluate(2), 0);
        Approx.Equal(-1, Interpolation.Lagrange(xs, ys, 1), 1e-15);
    }

    [Fact]
    public void NewtonDividedDifferences()
    {
        double[] xs = [1, 2, 3];
        double[] ys = [1, 4, 9];
        var c = Interpolation.NewtonDividedDifferences(xs, ys);
        Assert.Equal(new double[] { 1, 3, 1 }, c);
        Approx.Equal(6.25, Interpolation.EvaluateNewton(xs, c, 2.5), 1e-15);
        var mono = Interpolation.InterpolatingPolynomial(xs, ys);
        Approx.Equal(new[] { 0.0, 0.0, 1.0 }, mono, 1e-14, 1e-14);
        Assert.Throws<ArgumentException>(() => Interpolation.NewtonDividedDifferences([1, 1], [2, 3]));
    }

    [Fact]
    public void ChebyshevNodesBeatEquispacedForRunge()
    {
        static double Runge(double x) => 1 / (1 + 25 * x * x);
        int n = 21;
        var cheb = Interpolation.ChebyshevNodes(n);
        var equi = Enumerable.Range(0, n).Select(k => -1 + 2.0 * k / (n - 1)).ToArray();
        var pc = Interpolation.Lagrange(cheb, cheb.Select(Runge).ToArray());
        var pe = Interpolation.Lagrange(equi, equi.Select(Runge).ToArray());
        double errC = 0, errE = 0;
        for (int i = 0; i <= 200; i++)
        {
            double x = -1 + 2.0 * i / 200;
            errC = Math.Max(errC, Math.Abs(pc.Evaluate(x) - Runge(x)));
            errE = Math.Max(errE, Math.Abs(pe.Evaluate(x) - Runge(x)));
        }
        Assert.True(errC < 0.02, $"Chebyshev error {errC}");
        Assert.True(errE > 10, $"Equispaced error {errE}");
    }

    [Fact]
    public void ChebyshevNodeValues()
    {
        var nodes = Interpolation.ChebyshevNodes(3);
        Approx.Equal(new[] { -Math.Sqrt(3) / 2, 0, Math.Sqrt(3) / 2 }, nodes, 1e-15, 1e-16);
        var mapped = Interpolation.ChebyshevNodes(2, 0, 4);
        Approx.Equal(2 - 2 * Math.Sqrt(0.5), mapped[0], 1e-15);
    }

    [Fact]
    public void NaturalSplineOfSine()
    {
        var xs = Enumerable.Range(0, 11).Select(k => Math.PI * k / 10).ToArray();
        var s = Interpolation.NaturalCubicSpline(xs, xs.Select(Math.Sin).ToArray());
        for (int i = 0; i <= 100; i++)
        {
            double x = Math.PI * i / 100;
            Assert.True(Math.Abs(s.Evaluate(x) - Math.Sin(x)) < 1e-3);
        }
        Assert.Equal(0, s.SecondDerivative(0), 12);
        Assert.Equal(0, s.SecondDerivative(Math.PI), 12);
        Approx.Equal(1, s.Derivative(0), 1e-2);
    }

    [Fact]
    public void ClampedSplineReproducesCubic()
    {
        double[] xs = [-1, 0, 0.5, 2, 3];
        Func<double, double> f = x => x * x * x - x + 1;
        var s = Interpolation.ClampedCubicSpline(xs, xs.Select(f).ToArray(), 3 * 1 - 1, 3 * 9 - 1);
        foreach (double x in new[] { -0.7, 0.2, 1.1, 2.9 })
        {
            Approx.Equal(f(x), s.Evaluate(x), 1e-13);
            Approx.Equal(3 * x * x - 1, s.Derivative(x), 1e-12);
        }
    }

    [Fact]
    public void NaturalSplineReproducesLine()
    {
        var s = Interpolation.NaturalCubicSpline([0, 1, 3, 4], [1, 3, 7, 9]);
        Approx.Equal(5, s.Evaluate(2), 1e-14);
        Approx.Equal(11, s.Evaluate(5), 1e-14);
        Assert.Equal(4, s.Knots.Count);
    }

    [Fact]
    public void TwoPointSpline()
    {
        var s = Interpolation.NaturalCubicSpline([0, 2], [1, 5]);
        Approx.Equal(3, s.Evaluate(1), 1e-15);
    }

    [Fact]
    public void InvalidInputThrows()
    {
        Assert.Throws<ArgumentException>(() => Interpolation.Linear([1, 1], [1, 2]));
        Assert.Throws<ArgumentException>(() => Interpolation.NaturalCubicSpline([1], [1]));
        Assert.Throws<ArgumentException>(() => Interpolation.Linear([1, 2], [1]));
    }
}

public class FourierTests
{
    private static Complex[] NaiveDft(Complex[] x)
    {
        int n = x.Length;
        var r = new Complex[n];
        for (int k = 0; k < n; k++)
        {
            Complex s = Complex.Zero;
            for (int j = 0; j < n; j++) s += x[j] * Complex.FromPolar(1, -2 * Math.PI * ((long)k * j % n) / n);
            r[k] = s;
        }
        return r;
    }

    private static Complex[] RandomSignal(int n, int seed)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, n).Select(_ => new Complex(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5)).ToArray();
    }

    [Fact]
    public void ImpulseHasFlatSpectrum()
    {
        var X = Fourier.Forward([1, 0, 0, 0]);
        Assert.All(X, z => Approx.Equal(Complex.One, z, 1e-15));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(100)]
    [InlineData(97)]
    public void MatchesNaiveDft(int n)
    {
        var x = RandomSignal(n, n);
        var fast = Fourier.Forward(x);
        var slow = NaiveDft(x);
        for (int k = 0; k < n; k++) Approx.Equal(slow[k], fast[k], 1e-12);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(1000)]
    [InlineData(1)]
    public void InverseRoundTrip(int n)
    {
        var x = RandomSignal(n, 3);
        var y = Fourier.Inverse(Fourier.Forward(x));
        for (int k = 0; k < n; k++) Approx.Equal(x[k], y[k], 1e-13);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(10)]
    [InlineData(7)]
    public void RealInputHelper(int n)
    {
        var rng = new Random(n);
        var x = Enumerable.Range(0, n).Select(_ => rng.NextDouble()).ToArray();
        var a = Fourier.ForwardReal(x);
        var b = Fourier.Forward(x.Select(v => new Complex(v, 0)).ToArray());
        for (int k = 0; k < n; k++) Approx.Equal(b[k], a[k], 1e-13);
        Approx.Equal(x, Fourier.InverseReal(a), 1e-13, 1e-15);
    }

    [Fact]
    public void CosinePeaks()
    {
        int n = 16;
        var x = Enumerable.Range(0, n).Select(j => Math.Cos(2 * Math.PI * 3 * j / n)).ToArray();
        var mags = Fourier.Magnitudes(Fourier.ForwardReal(x));
        for (int k = 0; k < n; k++)
            Approx.Equal(k == 3 || k == 13 ? 8 : 0, mags[k], 1e-13, 1e-13);
    }

    [Fact]
    public void Parseval()
    {
        var x = RandomSignal(50, 8);
        var X = Fourier.Forward(x);
        double ex = x.Sum(z => z.Re * z.Re + z.Im * z.Im);
        double eX = X.Sum(z => z.Re * z.Re + z.Im * z.Im) / 50;
        Approx.Equal(ex, eX, 1e-13);
    }

    [Fact]
    public void Convolution()
    {
        Approx.Equal(new[] { 0.0, 1, 2.5, 4, 1.5 }, Fourier.Convolve([1, 2, 3], [0, 1, 0.5]), 1e-15);
        var rng = new Random(2);
        var a = Enumerable.Range(0, 300).Select(_ => rng.NextDouble()).ToArray();
        var b = Enumerable.Range(0, 200).Select(_ => rng.NextDouble()).ToArray();
        var fast = Fourier.Convolve(a, b);
        Assert.Equal(499, fast.Length);
        for (int k = 0; k < 499; k += 37)
        {
            double s = 0;
            for (int i = Math.Max(0, k - 199); i <= Math.Min(k, 299); i++) s += a[i] * b[k - i];
            Approx.Equal(s, fast[k], 1e-12);
        }
    }

    [Fact]
    public void Windows()
    {
        Approx.Equal(new[] { 0, 0.5, 1, 0.5, 0 }, Fourier.Window(WindowType.Hann, 5), 1e-15, 1e-16);
        Approx.Equal(0.08, Fourier.Window(WindowType.Hamming, 5)[0], 1e-14);
        Assert.All(Fourier.Window(WindowType.Rectangular, 4), v => Assert.Equal(1, v));
    }
}

public class SeriesTests
{
    [Fact]
    public void BaselProblem()
    {
        var r = Series.SumInfinite(k => 1 / (k * k));
        Assert.True(r.Converged, r.Message);
        Approx.Equal(Math.PI * Math.PI / 6, r.Value, 1e-10);
    }

    [Fact]
    public void AlternatingHarmonic()
    {
        var r = Series.SumInfinite(k => (k % 2 == 1 ? 1 : -1) / k);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(Math.Log(2), r.Value, 1e-12);
    }

    [Fact]
    public void LeibnizSeries()
    {
        var r = Series.SumInfinite(k => (k % 2 == 0 ? 1 : -1) / (2 * k + 1), 0);
        Assert.True(r.Converged, r.Message);
        Approx.Equal(Math.PI / 4, r.Value, 1e-12);
    }

    [Fact]
    public void ExponentialSeriesConvergesDirectly()
    {
        var r = Series.SumInfinite(k =>
        {
            double f = 1;
            for (int i = 2; i <= k; i++) f *= i;
            return 1 / f;
        }, 0);
        Assert.True(r.Converged);
        Assert.Equal("Direct", r.Method);
        Approx.Equal(Math.E, r.Value, 1e-15);
    }

    [Fact]
    public void GeometricSeries()
    {
        var r = Series.SumInfinite(k => Math.Pow(0.5, k), 0);
        Approx.Equal(2, r.Value, 1e-15);
        var s = Series.SumInfinite(k => Math.Pow(0.999, k), 0);
        Approx.Equal(1000, s.Value, 1e-9);
    }

    [Fact]
    public void ZetaThree()
    {
        var r = Series.SumInfinite(k => 1 / (k * k * k));
        Assert.True(r.Converged, r.Message);
        Approx.Equal(1.2020569031595942, r.Value, 1e-11);
    }

    [Fact]
    public void ZetaThreeHalves()
    {
        var r = Series.SumInfinite(k => Math.Pow(k, -1.5), 1, 1e-8);
        Approx.Equal(2.6123753486854883, r.Value, 1e-8);
    }

    [Fact]
    public void Telescoping()
    {
        var r = Series.SumInfinite(k => 1 / (k * (k + 1)));
        Approx.Equal(1, r.Value, 1e-11);
    }

    [Fact]
    public void HarmonicSeriesDiverges()
    {
        var r = Series.SumInfinite(k => 1 / k);
        Assert.False(r.Converged);
        Assert.Contains("diverge", r.Message);
    }

    [Fact]
    public void NonVanishingTermsDiverge()
    {
        var r = Series.SumInfinite(k => 1);
        Assert.False(r.Converged);
        var s = Series.SumInfinite(k => k % 2 == 0 ? 1 : -1);
        Assert.False(s.Converged);
    }

    [Fact]
    public void FiniteSumsAndProducts()
    {
        Assert.Equal(5050, Series.Sum(k => k, 1, 100));
        Assert.Equal(3628800, Series.Product(k => k, 1, 10));
        Approx.Equal(1.6439345666815615, Series.Sum(k => 1 / (k * k), 1, 1000), 1e-14);
    }
}
