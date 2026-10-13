namespace Calcy.Numerics.Tests;

public class IntervalTests
{
    private static Interval I(double lo, double hi) => new(lo, hi);

    private static void AssertContains(Interval r, double x)
    {
        Assert.False(r.IsEmpty, $"Result is empty but should contain {x:R}.");
        Assert.True(r.Lo <= x && x <= r.Hi, $"{r} does not contain {x:R}.");
    }

    [Fact]
    public void BasicArithmetic()
    {
        var s = I(1, 2) + I(3, 4);
        Assert.Equal(4, s.Lo);
        Assert.Equal(6, s.Hi);
        var d = I(1, 2) - I(3, 4);
        Assert.Equal(-3, d.Lo);
        Assert.Equal(-1, d.Hi);
        var p = I(1, 2) * I(-3, 4);
        Assert.Equal(-6, p.Lo);
        Assert.Equal(8, p.Hi);
        var q = I(1, 2) / I(-2, -1);
        Assert.Equal(-2, q.Lo);
        Assert.Equal(-0.5, q.Hi);
    }

    [Fact]
    public void ArithmeticIsOutwardRoundedAndTight()
    {
        // Exact rational checks on many operand pairs.
        var rng = new Random(7);
        for (int k = 0; k < 2000; k++)
        {
            double a = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-20, 20));
            double b = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-20, 20));
            if (b == 0) continue;
            var ra = Rational.FromDouble(a);
            var rb = Rational.FromDouble(b);
            Check(Interval.Point(a) + Interval.Point(b), ra + rb);
            Check(Interval.Point(a) - Interval.Point(b), ra - rb);
            Check(Interval.Point(a) * Interval.Point(b), ra * rb);
            Check(Interval.Point(a) / Interval.Point(b), ra / rb);
        }

        static void Check(Interval r, Rational exact)
        {
            Assert.True(Rational.FromDouble(r.Lo) <= exact && exact <= Rational.FromDouble(r.Hi), $"{r} misses the exact value.");
            // At most one ulp wide for a single correctly rounded operation.
            Assert.True(r.Hi == r.Lo || Math.BitIncrement(r.Lo) == r.Hi, $"{r} is wider than one ulp.");
        }
    }

    [Fact]
    public void DecimalLiteralEnclosure()
    {
        var x = Interval.Widened(0.1) + Interval.Widened(0.2);
        AssertContains(x, 0.3);
        AssertContains(Interval.Pi, Math.PI);
        Assert.True(Interval.Pi.Hi > Math.PI);
    }

    [Fact]
    public void DivisionByIntervalsContainingZero()
    {
        Assert.True((Interval.Point(1) / I(-1, 1)).IsEntire);
        Assert.True((Interval.Point(1) / Interval.Point(0)).IsEmpty);
        var half = Interval.Point(1) / I(0, 2);
        Assert.Equal(0.5, half.Lo);
        Assert.Equal(double.PositiveInfinity, half.Hi);
        var neg = I(1, 2) / I(-4, 0);
        Assert.Equal(double.NegativeInfinity, neg.Lo);
        Assert.Equal(-0.25, neg.Hi);
        var zero = Interval.Point(0) / I(-1, 1);
        Assert.Equal(0, zero.Lo);
        Assert.Equal(0, zero.Hi);
        Assert.True((I(-1, 2) / I(0, 1)).IsEntire);
    }

    [Fact]
    public void InfiniteBounds()
    {
        var p = I(-1, 1) * I(0, double.PositiveInfinity);
        Assert.True(p.IsEntire);
        var q = I(0, 2) * I(0, double.PositiveInfinity);
        Assert.Equal(0, q.Lo);
        Assert.Equal(double.PositiveInfinity, q.Hi);
        var r = I(1, 2) / I(1, double.PositiveInfinity);
        Assert.Equal(0, r.Lo);
        Assert.Equal(2, r.Hi);
        var s = Interval.Entire + Interval.Point(1);
        Assert.True(s.IsEntire);
    }

    [Fact]
    public void EmptyPropagates()
    {
        Assert.True((Interval.Empty + I(1, 2)).IsEmpty);
        Assert.True(Interval.Sin(Interval.Empty).IsEmpty);
        Assert.True(Interval.Intersect(I(0, 1), I(2, 3)).IsEmpty);
        Assert.Equal(I(0, 3), Interval.Hull(I(0, 1), I(2, 3)));
        Assert.Equal("Empty", Interval.Empty.ToString());
        Assert.Throws<ArgumentException>(() => new Interval(2, 1));
    }

    [Fact]
    public void SqrtAndLogClipTheDomain()
    {
        var s = Interval.Sqrt(I(-4, 4));
        Assert.Equal(0, s.Lo);
        Assert.Equal(2, s.Hi);
        Assert.True(Interval.Sqrt(I(-4, -1)).IsEmpty);
        var exact = Interval.Sqrt(I(4, 9));
        Assert.Equal(2, exact.Lo);
        Assert.Equal(3, exact.Hi);
        var l = Interval.Log(I(-1, Math.E));
        Assert.Equal(double.NegativeInfinity, l.Lo);
        AssertContains(l, 1);
        Assert.True(Interval.Log(I(-2, -1)).IsEmpty);
        Assert.True(Interval.Log(I(-2, 0)).IsEmpty);
        AssertContains(Interval.Log10(I(1, 1000)), 3);
    }

    [Fact]
    public void IntegerPowers()
    {
        var a = Interval.Pow(I(-2, 3), 2);
        Assert.Equal(0, a.Lo);
        Assert.Equal(9, a.Hi);
        var b = Interval.Pow(I(-2, 3), 3);
        Assert.Equal(-8, b.Lo);
        Assert.Equal(27, b.Hi);
        var c = Interval.Pow(I(-3, -2), 2);
        Assert.Equal(4, c.Lo);
        Assert.Equal(9, c.Hi);
        var d = Interval.Pow(I(2, 4), -1);
        Assert.Equal(0.25, d.Lo);
        Assert.Equal(0.5, d.Hi);
        var e = Interval.Pow(I(-1, 1), -2);
        Assert.Equal(1, e.Lo);
        Assert.Equal(double.PositiveInfinity, e.Hi);
        Assert.Equal(I(1, 1), Interval.Pow(I(-5, 5), 0));
        AssertContains(Interval.Pow(I(1.1, 1.2), 50), Math.Pow(1.15, 50));
    }

    [Fact]
    public void RealPowers()
    {
        var a = Interval.Pow(I(0, 4), 0.5);
        Assert.Equal(0, a.Lo);
        AssertContains(a, 2);
        var b = Interval.Pow(I(-4, 4), 0.5);
        Assert.Equal(0, b.Lo);
        Assert.True(Interval.Pow(I(-4, -1), 0.5).IsEmpty);
        var c = Interval.Pow(I(0, 4), -0.5);
        Assert.Equal(double.PositiveInfinity, c.Hi);
        AssertContains(c, 0.5);
        AssertContains(Interval.Pow(I(2, 3), I(1, 2)), 2);
        AssertContains(Interval.Pow(I(2, 3), I(1, 2)), 9);
    }

    [Fact]
    public void Trigonometry()
    {
        var s = Interval.Sin(I(0, Math.PI));
        Assert.Equal(1, s.Hi);
        Assert.True(s.Lo <= 0 && s.Lo > -1e-15);
        var t = Interval.Sin(I(0.1, 0.2));
        AssertContains(t, Math.Sin(0.1));
        AssertContains(t, Math.Sin(0.2));
        Assert.True(t.Width < 0.1);
        Assert.Equal(I(-1, 1), Interval.Sin(I(0, 10)));
        var c = Interval.Cos(I(0, Math.PI));
        Assert.Equal(-1, c.Lo);
        Assert.Equal(1, c.Hi);
        Assert.Equal(1, Interval.Cos(I(-0.5, 0.5)).Hi);
        Assert.True(Interval.Cos(I(-0.5, 0.5)).Lo > 0.87);
    }

    [Fact]
    public void TangentAcrossPoleIsEntire()
    {
        Assert.True(Interval.Tan(I(1.5, 1.6)).IsEntire);
        Assert.True(Interval.Tan(I(4.7, 4.8)).IsEntire);
        var t = Interval.Tan(I(0, 1));
        AssertContains(t, 0);
        AssertContains(t, Math.Tan(1));
        Assert.False(Interval.Tan(I(-0.5, 0.5)).IsEntire);
    }

    [Fact]
    public void InverseAndHyperbolic()
    {
        var a = Interval.Asin(I(-2, 2));
        AssertContains(a, -Math.PI / 2);
        AssertContains(a, Math.PI / 2);
        var b = Interval.Acos(I(0.5, 2));
        AssertContains(b, 0);
        AssertContains(b, Math.PI / 3);
        Assert.True(Interval.Asin(I(1.5, 2)).IsEmpty);
        var c = Interval.Atan(Interval.Entire);
        Assert.True(c.Lo <= -Math.PI / 2 && c.Hi >= Math.PI / 2);
        var d = Interval.Cosh(I(-1, 2));
        Assert.Equal(1, d.Lo);
        AssertContains(d, Math.Cosh(2));
        var e = Interval.Tanh(I(-100, 100));
        Assert.True(e.Lo >= -1 && e.Hi <= 1);
        AssertContains(Interval.Sinh(I(-1, 1)), Math.Sinh(-1));
    }

    [Fact]
    public void PiecewiseFunctions()
    {
        Assert.Equal(I(0, 3), Interval.Abs(I(-3, 2)));
        Assert.Equal(I(2, 3), Interval.Abs(I(-3, -2)));
        Assert.Equal(I(1, 2), Interval.Floor(I(1.5, 2.5)));
        Assert.Equal(I(2, 3), Interval.Ceiling(I(1.5, 2.5)));
        Assert.Equal(I(0, 2), Interval.Min(I(0, 5), I(1, 2)));
        Assert.Equal(I(1, 5), Interval.Max(I(0, 5), I(1, 2)));
    }

    [Fact]
    public void FunctionsEncloseSampledValues()
    {
        var rng = new Random(11);
        var funcs = new (Func<Interval, Interval> F, Func<double, double> f)[]
        {
            (Interval.Sin, Math.Sin), (Interval.Cos, Math.Cos), (Interval.Tan, Math.Tan),
            (Interval.Exp, Math.Exp), (Interval.Log, Math.Log), (Interval.Sqrt, Math.Sqrt),
            (Interval.Atan, Math.Atan), (Interval.Sinh, Math.Sinh), (Interval.Cosh, Math.Cosh),
            (Interval.Tanh, Math.Tanh), (Interval.Asin, Math.Asin), (Interval.Acos, Math.Acos),
            (x => Interval.Pow(x, 3), x => x * x * x), (x => Interval.Pow(x, 2.5), x => Math.Pow(x, 2.5)),
            (x => Interval.Pow(x, -2), x => 1 / (x * x)), (Interval.Abs, Math.Abs),
        };
        for (int k = 0; k < 400; k++)
        {
            double c = (rng.NextDouble() - 0.5) * 20;
            double w = rng.NextDouble() * Math.Pow(10, rng.Next(-8, 1));
            var box = I(c - w, c + w);
            foreach (var (F, f) in funcs)
            {
                var r = F(box);
                for (int j = 0; j <= 10; j++)
                {
                    double x = box.Lo + (box.Hi - box.Lo) * j / 10;
                    double v = f(x);
                    if (!double.IsFinite(v)) continue;
                    // The reference value itself carries up to about one ulp of rounding error.
                    double slack = Math.BitIncrement(Math.Abs(v)) - Math.Abs(v);
                    Assert.False(r.IsEmpty);
                    Assert.True(r.Lo - slack <= v && v <= r.Hi + slack, $"{r} does not contain {v:R} = f({x:R}).");
                }
            }
        }
    }

    [Fact]
    public void ImplicitCurveExclusionTest()
    {
        // F(x, y) = x^2 + y^2 - 1: boxes on the circle contain 0, boxes away from it do not.
        static Interval F(Interval x, Interval y) => Interval.Sqr(x) + Interval.Sqr(y) - 1;
        Assert.True(F(I(0.5, 0.6), I(0.8, 0.9)).ContainsZero);
        Assert.False(F(I(0, 0.1), I(0, 0.1)).ContainsZero);
        Assert.False(F(I(2, 3), I(-1, 1)).ContainsZero);
        // y = 1/x has no point with x in [-1, 1] mapping into y in [-0.5, 0.5].
        var g = Interval.Point(1) / I(-1, 1) - I(-0.5, 0.5);
        Assert.True(g.IsEntire);
    }
}
