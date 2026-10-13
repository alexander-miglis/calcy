namespace Calcy.Numerics.Tests;

public class PolynomialTests
{
    private static Complex C(double re, double im = 0) => new(re, im);

    private static void AssertRootsMatch(Complex[] expected, Complex[] actual, double tol)
    {
        Assert.Equal(expected.Length, actual.Length);
        var remaining = actual.ToList();
        foreach (var e in expected)
        {
            int best = 0;
            for (int i = 1; i < remaining.Count; i++)
                if (Complex.Abs(remaining[i] - e) < Complex.Abs(remaining[best] - e)) best = i;
            Assert.True(Complex.Abs(remaining[best] - e) <= tol * Math.Max(1, Complex.Abs(e)),
                $"No root near {e.ToString(15)}; closest {remaining[best].ToString(15)}.");
            remaining.RemoveAt(best);
        }
    }

    [Fact]
    public void EvaluateAndDerivative()
    {
        Assert.Equal(17, Polynomial.Evaluate([1, 2, 3], 2));
        Assert.Equal(new double[] { 2, 6 }, Polynomial.Derivative([1, 2, 3]));
        Approx.Equal(C(-2, 2), Polynomial.Evaluate([1, 2, 3], C(0, 1)) + C(0, 0));
        Assert.Equal(new double[] { 5, 1, 1, 1 }, Polynomial.Integral([1, 2, 3], 5));
    }

    [Fact]
    public void Arithmetic()
    {
        Assert.Equal(new double[] { 1, 0, -1 }, Polynomial.Multiply([1, 1], [1, -1]));
        Assert.Equal(new double[] { 2, 3, 1 }, Polynomial.Add([1, 2], [1, 1, 1]));
        Assert.Equal(new double[] { 0, 1, -1 }, Polynomial.Subtract([1, 2], [1, 1, 1]));
        Assert.Equal(2, Polynomial.Degree([1, 0, 3, 0, 0]));
    }

    [Fact]
    public void DivisionExact()
    {
        var (q, r) = Polynomial.DivideWithRemainder([-1, 0, 0, 1], [-1, 1]);
        Assert.Equal(new double[] { 1, 1, 1 }, q);
        Assert.Equal(new double[] { 0 }, r);
    }

    [Fact]
    public void DivisionWithRemainder()
    {
        var (q, r) = Polynomial.DivideWithRemainder([1, 0, 1], [1, 1]);
        Assert.Equal(new double[] { -1, 1 }, q);
        Assert.Equal(new double[] { 2 }, r);
        Assert.Throws<DivideByZeroException>(() => Polynomial.DivideWithRemainder([1, 2], [0, 0]));
        var (cq, cr) = Polynomial.DivideWithRemainder([C(1), C(0), C(1)], [C(0, -1), C(1)]);
        Approx.Equal(C(0, 1), cq[0]);
        Approx.Equal(C(0), cr[0]);
    }

    [Fact]
    public void FromRoots()
    {
        Assert.Equal(new double[] { -6, 11, -6, 1 }, Polynomial.FromRoots(1, 2, 3));
        var c = Polynomial.FromRoots(C(0, 1), C(0, -1));
        Approx.Equal(C(1), c[0]);
        Approx.Equal(C(0), c[1]);
        Approx.Equal(C(1), c[2]);
    }

    [Theory]
    [InlineData(2, -3, 1, 1, 2)]
    [InlineData(1, -2, 1, 1, 1)]
    [InlineData(-4, 0, 1, -2, 2)]
    [InlineData(0, 0, 1, 0, 0)]
    public void RealQuadratics(double c0, double c1, double c2, double r1, double r2)
    {
        var roots = Polynomial.AllRoots([c0, c1, c2]);
        Assert.Equal(2, roots.Length);
        Approx.Equal(C(r1), roots[0], 1e-15);
        Approx.Equal(C(r2), roots[1], 1e-15);
        Assert.True(roots.All(z => z.Im == 0));
    }

    [Fact]
    public void ComplexQuadratic()
    {
        var roots = Polynomial.AllRoots([1, 0, 1]);
        Assert.Equal(C(0, -1), roots[0]);
        Assert.Equal(C(0, 1), roots[1]);
        var roots2 = Polynomial.AllRoots([5, 2, 1]);
        Approx.Equal(C(-1, -2), roots2[0], 1e-15);
        Approx.Equal(C(-1, 2), roots2[1], 1e-15);
    }

    [Fact]
    public void QuadraticWithoutCancellation()
    {
        var roots = Polynomial.AllRoots([1, -1e8, 1]);
        Approx.Equal(1e-8, roots[0].Re, 1e-15);
        Approx.Equal(1e8, roots[1].Re, 1e-15);
    }

    [Fact]
    public void Cubic()
    {
        var roots = Polynomial.AllRoots([-6, 11, -6, 1]);
        Approx.Equal(new[] { 1.0, 2.0, 3.0 }, roots.Select(z => z.Re).ToArray(), 1e-14);
        Assert.True(roots.All(z => z.Im == 0));
    }

    [Fact]
    public void CubeRootOfTwoHasExactlyRealRoot()
    {
        var roots = Polynomial.AllRoots([-2, 0, 0, 1]);
        var real = Assert.Single(roots, z => z.Im == 0);
        Approx.Equal(Math.Cbrt(2), real.Re, 1e-15);
        var complexRoots = roots.Where(z => z.Im != 0).ToArray();
        Assert.Equal(2, complexRoots.Length);
        Assert.Equal(complexRoots[0].Re, complexRoots[1].Re);
        Assert.Equal(-complexRoots[0].Im, complexRoots[1].Im);
    }

    [Fact]
    public void TripleRoot()
    {
        var roots = Polynomial.AllRoots(Polynomial.FromRoots(1, 1, 1));
        Assert.Equal(3, roots.Length);
        foreach (var z in roots)
        {
            Approx.Equal(1, z.Re, 1e-10);
            Assert.Equal(0, z.Im);
        }
    }

    [Fact]
    public void MixedMultiplicities()
    {
        var p = Polynomial.FromRoots(1, 1, -2, -2, -2, 3);
        var roots = Polynomial.AllRoots(p);
        AssertRootsMatch([C(-2), C(-2), C(-2), C(1), C(1), C(3)], roots, 1e-8);
        Assert.True(roots.All(z => z.Im == 0));
    }

    [Fact]
    public void FifthOrderRoot()
    {
        var roots = Polynomial.AllRoots(Polynomial.FromRoots(2, 2, 2, 2, 2));
        foreach (var z in roots) Approx.Equal(C(2), z, 1e-8);
    }

    [Fact]
    public void ComplexDoubleRoots()
    {
        // (x^2 + 1)^2 has double roots at i and -i.
        var roots = Polynomial.AllRoots([1, 0, 2, 0, 1]);
        AssertRootsMatch([C(0, 1), C(0, 1), C(0, -1), C(0, -1)], roots, 1e-8);
    }

    [Fact]
    public void RootsOfUnityDegree50()
    {
        var c = new double[51];
        c[0] = -1;
        c[50] = 1;
        var roots = Polynomial.AllRoots(c);
        Assert.Equal(50, roots.Length);
        var expected = Enumerable.Range(0, 50).Select(k => Complex.FromPolar(1, 2 * Math.PI * k / 50)).ToArray();
        AssertRootsMatch(expected, roots, 1e-13);
        Assert.Equal(2, roots.Count(z => z.Im == 0));
    }

    [Fact]
    public void Degree50RandomRoots()
    {
        var rng = new Random(5);
        var expected = new Complex[50];
        for (int k = 0; k < 25; k++)
        {
            double r = k % 2 == 0 ? 0.6 : 1.3;
            var z = Complex.FromPolar(r, Math.PI * rng.NextDouble());
            expected[2 * k] = z;
            expected[2 * k + 1] = Complex.Conjugate(z);
        }
        var p = Polynomial.FromRoots(expected).Select(z => z.Re).ToArray();
        var roots = Polynomial.AllRoots(p);
        // Accuracy is limited by the conditioning of the monomial coefficients.
        AssertRootsMatch(expected, roots, 1e-5);
        // Backward check: every computed root has a tiny relative residual.
        foreach (var z in roots)
        {
            double scale = 0, az = Complex.Abs(z);
            for (int i = p.Length - 1; i >= 0; i--) scale = scale * az + Math.Abs(p[i]);
            Assert.True(Complex.Abs(Polynomial.Evaluate(p, z)) <= 1e-12 * scale);
        }
    }

    [Fact]
    public void Wilkinson12IsAccurate()
    {
        var p = Polynomial.FromRoots(Enumerable.Range(1, 12).Select(k => (double)k).ToArray());
        var roots = Polynomial.RealRoots(p);
        Assert.Equal(12, roots.Length);
        for (int k = 0; k < 12; k++) Approx.Equal(k + 1, roots[k], 1e-8);
    }

    [Fact]
    public void Wilkinson20()
    {
        // The double-rounded coefficients define a slightly different polynomial; its roots are
        // still close to 1..20 and each one has a backward-stable residual.
        var p = Polynomial.FromRoots(Enumerable.Range(1, 20).Select(k => (double)k).ToArray());
        var roots = Polynomial.AllRoots(p);
        Assert.Equal(20, roots.Length);
        for (int k = 0; k < 20; k++)
        {
            Assert.True(Math.Abs(roots[k].Re - (k + 1)) < 0.02, $"root {k + 1}: {roots[k].ToString(10)}");
            Assert.Equal(0, roots[k].Im);
            double scale = 0, az = Complex.Abs(roots[k]);
            for (int i = p.Length - 1; i >= 0; i--) scale = scale * az + Math.Abs(p[i]);
            Assert.True(Complex.Abs(Polynomial.Evaluate(p, roots[k])) <= 1e-15 * scale, $"residual at root {k + 1}");
        }
        for (int k = 0; k < 3; k++) Approx.Equal(k + 1, roots[k].Re, 1e-9);
    }

    [Fact]
    public void ChebyshevT10()
    {
        // T10(x) = 512x^10 - 1280x^8 + 1120x^6 - 400x^4 + 50x^2 - 1.
        var roots = Polynomial.RealRoots([-1, 0, 50, 0, -400, 0, 1120, 0, -1280, 0, 512]);
        Assert.Equal(10, roots.Length);
        var expected = Enumerable.Range(1, 10).Select(k => Math.Cos((2 * k - 1) * Math.PI / 20)).OrderBy(v => v).ToArray();
        Approx.Equal(expected, roots, 1e-12, 1e-13);
    }

    [Fact]
    public void ComplexCoefficients()
    {
        var p = Polynomial.FromRoots(C(0, 1), C(2), C(-1, -3));
        var roots = Polynomial.AllRoots(p);
        AssertRootsMatch([C(0, 1), C(2), C(-1, -3)], roots, 1e-13);
    }

    [Fact]
    public void LeadingAndTrailingZeros()
    {
        var roots = Polynomial.AllRoots([2, 3, 1, 0, 0]);
        Assert.Equal(2, roots.Length);
        Approx.Equal(C(-2), roots[0], 1e-15);
        Approx.Equal(C(-1), roots[1], 1e-15);
        var zeros = Polynomial.AllRoots([0, 0, 0, 1]);
        Assert.All(zeros, z => Assert.Equal(Complex.Zero, z));
        Assert.Equal(new[] { 3.0 }, Polynomial.RealRoots([3, -1, 3, -1]).Select(x => Math.Round(x, 12)).ToArray());
    }

    [Fact]
    public void LinearAndErrors()
    {
        Assert.Equal(C(-2), Assert.Single(Polynomial.AllRoots([4, 2])));
        Assert.Throws<ArgumentException>(() => Polynomial.AllRoots([0, 0]));
        Assert.Empty(Polynomial.AllRoots([5]));
    }

    [Fact]
    public void WidelyScaledRoots()
    {
        var p = Polynomial.FromRoots(1e-6, 1, 1e6);
        var roots = Polynomial.RealRoots(p);
        Approx.Equal(1e-6, roots[0], 1e-10);
        Approx.Equal(1, roots[1], 1e-12);
        Approx.Equal(1e6, roots[2], 1e-12);
    }
}
