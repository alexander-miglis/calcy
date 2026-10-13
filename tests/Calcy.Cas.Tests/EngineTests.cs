using System.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Cas.Tests;

public class PolynomialEngineTests
{
    private static QPoly P(params long[] lowToHigh) => QPoly.FromInts(lowToHigh);

    private static QPoly Product(IEnumerable<QPoly> factors) => factors.Aggregate(QPoly.One, (a, b) => a * b);

    [Fact]
    public void DivisionWithRemainder()
    {
        var (q, r) = P(-1, 0, 0, 1).DivRem(P(-1, 1));
        Assert.Equal(P(1, 1, 1), q);
        Assert.True(r.IsZero);
        var (q2, r2) = P(2, 0, 0, 1).DivRem(P(-1, 1));
        Assert.Equal(P(1, 1, 1), q2);
        Assert.Equal(P(3), r2);
    }

    [Fact]
    public void GcdIsMonic()
    {
        var a = P(-1, 0, 1) * P(2, 1);
        var b = P(1, 2, 1) * P(5, 3);
        Assert.Equal(P(1, 1), QPoly.Gcd(a, b));
    }

    [Fact]
    public void SquareFreeDecomposition()
    {
        var f = P(-1, 1).Pow(3) * P(2, 1).Pow(2) * P(1, 0, 1);
        var parts = f.SquareFree();
        Assert.Equal(3, parts.Count);
        Assert.Contains(parts, p => p.Multiplicity == 3 && p.Factor.Equals(P(-1, 1)));
        Assert.Contains(parts, p => p.Multiplicity == 2 && p.Factor.Equals(P(2, 1)));
        Assert.Contains(parts, p => p.Multiplicity == 1 && p.Factor.Equals(P(1, 0, 1)));
    }

    [Theory]
    [InlineData(new long[] { 4, 0, 0, 0, 1 }, 2)]
    [InlineData(new long[] { -1, 0, 0, 0, 0, 0, 1 }, 4)]
    [InlineData(new long[] { -1, 0, 0, 0, 0, 0, 0, 0, 1 }, 4)]
    [InlineData(new long[] { -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, 6)]
    [InlineData(new long[] { 1, 0, 0, 0, 1 }, 1)]
    [InlineData(new long[] { 1, 0, 1, 0, 1 }, 2)]
    [InlineData(new long[] { 1, 1, 0, 0, 0, 1 }, 2)]
    [InlineData(new long[] { -2, 0, 0, 0, 1 }, 1)]
    public void FactorCounts(long[] coefficients, int expectedFactors)
    {
        using var scope = CasEnv.Enter(Cas.Session().Context());
        var f = P(coefficients);
        var (unit, factors) = UniFactor.FactorQ(f);
        Assert.Equal(expectedFactors, factors.Sum(t => t.Multiplicity));
        var rebuilt = factors.Aggregate(QPoly.Constant(unit), (acc, t) => acc * t.Factor.ToQ().Pow(t.Multiplicity));
        Assert.Equal(f, rebuilt);
    }

    [Fact]
    public void ZassenhausOnRandomProducts()
    {
        using var scope = CasEnv.Enter(Cas.Session().Context());
        var rng = new Random(7);
        for (int trial = 0; trial < 12; trial++)
        {
            var factors = new List<QPoly>();
            int count = rng.Next(2, 4);
            for (int i = 0; i < count; i++)
            {
                int degree = rng.Next(2, 5);
                var c = new long[degree + 1];
                for (int j = 0; j < degree; j++) c[j] = rng.Next(-9, 10);
                c[degree] = rng.Next(1, 4);
                if (c[0] == 0) c[0] = 1;
                factors.Add(P(c));
            }
            var f = Product(factors);
            var (unit, found) = UniFactor.FactorQ(f);
            var rebuilt = found.Aggregate(QPoly.Constant(unit), (acc, t) => acc * t.Factor.ToQ().Pow(t.Multiplicity));
            Assert.Equal(f, rebuilt);
            Assert.True(found.Sum(t => t.Multiplicity) >= count, $"trial {trial}: {f}");
        }
    }

    [Fact]
    public void RationalRootsAreFound()
    {
        using var scope = CasEnv.Enter(Cas.Session().Context());
        // (x + 2)(2x - 1)(x - 3) = 2x³ - 3x² - 11x + 6
        var f = new ZPoly([new BigInteger(6), new BigInteger(-11), new BigInteger(-3), new BigInteger(2)]);
        var roots = UniFactor.RationalRootFactors(f);
        Assert.Equal(3, roots.Count);
        Assert.Equal(f, roots.Aggregate(ZPoly.One, (a, b) => a * b));
    }

    [Fact]
    public void MultivariateGcd()
    {
        using var scope = CasEnv.Enter(Cas.Session().Context());
        var km = KernelMap.Build([Expr.Sym("x"), Expr.Sym("y")])!;
        var s = Cas.Session();
        var a = km.ToPoly(s.Evaluate(s.ParseExpression("(x + y)^2 (x - y)")))!;
        var b = km.ToPoly(s.Evaluate(s.ParseExpression("(x + y) (x^2 + y)")))!;
        var g = MPoly.Gcd(a, b);
        Assert.Equal("x + y", ExprPrinter.ToText(km.ToExpr(g)));
    }

    [Fact]
    public void MultivariateFactorization()
    {
        var s = Cas.Session();
        var (constant, factors) = Cas.FactorMultiset(Cas.Exact("factor(x^3 y - x y^3 + x^2 - y^2)", s), s);
        Assert.Equal("1", constant);
        Assert.Equal(3, factors.Count);
        Assert.Contains("xy + 1", factors);
    }

    [Fact]
    public void ResultantVanishesForCommonRoot()
    {
        Assert.Equal("0", Cas.Text("resultant(x^2 - 3x + 2, x^2 - 1, x)"));
        Assert.Equal("18", Cas.Text("resultant(x^2 - 3x + 2, x^2 + 2, x)"));
    }

    [Fact]
    public void PolynomialRootsAreAccurate()
    {
        using var scope = CasEnv.Enter(Cas.Session().Context());
        var p = P(-6, 11, -6, 1);
        var roots = Numerics.RootFinding.RealPolynomialRoots(p);
        Assert.Equal(3, roots.Count);
        Cas.AssertClose(1, roots[0], 1e-12);
        Cas.AssertClose(2, roots[1], 1e-12);
        Cas.AssertClose(3, roots[2], 1e-12);
        var complex = Numerics.RootFinding.PolynomialRoots(P(1, 0, 0, 0, 1));
        Assert.Equal(4, complex.Count);
        Assert.All(complex, z => Cas.AssertClose(1, (z * z * z * z).Real * -1, 1e-10));
    }

    [Fact]
    public void BrentFindsRoots()
    {
        Cas.AssertClose(Math.Sqrt(2), Numerics.RootFinding.Brent(x => x * x - 2, 0, 2), 1e-14);
        var all = Numerics.RootFinding.ScanRoots(Math.Sin, -7, 7);
        Assert.Equal(5, all.Count);
    }
}

public class RobustnessTests
{
    [Fact]
    public void CancellationStopsWork()
    {
        var s = Cas.Session();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = s.Evaluate("factor(x^20 - y^20)", cts.Token);
        Assert.True(r.IsError);
        Assert.Equal("Stopped", r.ErrorMessage);
    }

    [Fact]
    public void ApproximateModeGivesDecimals()
    {
        var s = Cas.Session();
        var r = s.Evaluate(s.ParseExpression("solve(x^2 = 2, x)"), approximate: true);
        Assert.Equal("x = -1.414213562 or x = 1.414213562", ExprPrinter.ToText(r));
        Assert.Equal("0.3333333333", ExprPrinter.ToText(s.Evaluate(s.ParseExpression("integrate(x^2, x, 0, 1)"), approximate: true)));
    }

    [Fact]
    public void DecimalsInInputAreHandledExactly()
    {
        Assert.Equal("x = 2", Cas.Text("solve(0.5x = 1, x)"));
        Assert.Equal("x²", Cas.Text("integrate(2.0x, x)"));
    }

    [Theory]
    [InlineData("diff(x^2, 3)")]
    [InlineData("limit(1/x, x, 0, \"up\")")]
    [InlineData("taylor(sin(x), x, 0, -1)")]
    [InlineData("coeffs(sin(x), x)")]
    [InlineData("integrate(x, x, 1)")]
    [InlineData("compsquare(x^3, x)")]
    public void BadInputGivesClearErrors(string input) => Assert.False(string.IsNullOrWhiteSpace(Cas.Error(input)));

    [Fact]
    public void DeepExpressionsDoNotCrash()
    {
        var s = Cas.Session();
        var input = string.Concat(Enumerable.Repeat("sin(", 60)) + "x" + new string(')', 60);
        var r = s.Evaluate($"diff({input}, x)");
        Assert.False(r.IsError, r.ErrorMessage);
    }

    [Fact]
    public void LargePolynomialWorkFinishesQuickly()
    {
        var s = Cas.Session();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = s.Evaluate("factor(expand((x + 1)^30 (x - 2)^5))");
        Assert.False(r.IsError, r.ErrorMessage);
        Assert.Contains("(x + 1)³⁰", ExprPrinter.ToText(r.Exact!));
        Assert.True(sw.ElapsedMilliseconds < 20_000);
    }

    [Fact]
    public void CatalogMetadataIsComplete()
    {
        var registry = Calcy.Core.Evaluation.FunctionRegistry.CreateDefault();
        CasModule.Register(registry);
        foreach (var name in new[] { "expand", "factor", "simplify", "together", "apart", "cancel", "collect", "coeffs", "degree", "lcoeff",
                     "polyquo", "polyrem", "polygcd", "polylcm", "discriminant", "resultant", "compsquare", "diff", "integrate", "limit",
                     "taylor", "tangent", "normal", "impdiff", "solve", "nsolve", "zeros", "symsum", "symproduct", "steps",
                     "expandlog", "expandtrig", "tcollect", "derivative", "d", "int", "integral", "antiderivative", "series",
                     "partfrac", "completesquare", "quo", "remainder", "tExpand" })
        {
            Assert.True(registry.TryGet(name, out var def), name);
            Assert.False(string.IsNullOrEmpty(def.Signature), name);
            Assert.False(string.IsNullOrEmpty(def.Description), name);
            Assert.Contains(def.Category, new[] { "Algebra", "Calculus", "Equations" });
        }
        // Core's numeric rem stays untouched.
        Assert.True(registry.TryGet("rem", out var rem));
        Assert.Equal("Number theory", rem.Category);
    }

    [Fact]
    public void NoDashesInCatalogText()
    {
        var registry = Calcy.Core.Evaluation.FunctionRegistry.CreateDefault();
        CasModule.Register(registry);
        foreach (var def in registry.All.Where(d => d.Category is "Algebra" or "Calculus" or "Equations"))
        {
            Assert.DoesNotContain((char)0x2014, def.Description);
            Assert.DoesNotContain((char)0x2013, def.Description);
        }
    }
}
