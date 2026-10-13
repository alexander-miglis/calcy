using Calcy.Modes.Solver.Logic;
using static Calcy.Modes.Solver.Tests.TestSupport;

namespace Calcy.Modes.Solver.Tests;

public class PolynomialTests
{
    private static PolynomialRequest Poly(params string[] highestFirst)
    {
        var r = new PolynomialRequest { Degree = highestFirst.Length - 1 };
        for (int k = 0; k < r.Coefficients.Count; k++) r.Coefficients[k] = "0";
        for (int i = 0; i < highestFirst.Length; i++) r[highestFirst.Length - 1 - i] = highestFirst[i];
        return r;
    }

    [Fact]
    public void Quadratic_with_radicals_is_exact_with_discriminant_and_vertex()
    {
        var o = E().SolvePolynomial(Poly("1", "-5", "3"), default);
        Assert.False(o.IsError, o.Error?.Message);
        Assert.Equal(2, o.Degree);
        Assert.Equal("x² - 5x + 3 = 0", A(o.EquationText));
        Assert.Equal(2, o.RealCount);
        Assert.All(o.Roots, r => Assert.False(r.IsNumeric));
        Assert.Equal("(5 - √13)/2", A(o.Roots[0].Text));
        Assert.Equal("(5 + √13)/2", A(o.Roots[1].Text));
        Close((5 - Math.Sqrt(13)) / 2, o.Roots[0].Re);
        Close((5 + Math.Sqrt(13)) / 2, o.Roots[1].Re);
        Assert.Equal("0.6972243623", o.Roots[0].ApproxText);
        Assert.Equal("13", A(E().Show(o.Discriminant!)));
        Assert.Contains(o.Notes, n => n.Contains("two real roots"));
        Assert.NotNull(o.Vertex);
        Assert.Equal("5/2", o.Vertex!.X.Text);
        Assert.Equal("-13/4", A(o.Vertex.Y.Text));
        Assert.False(o.Vertex.IsMaximum);
        // The design's card: x = (5 ± √13)/2.
        Assert.Equal(["x = (5 ± √13)/2"], o.Card.Lines.Select(A));
        Assert.Equal("POLYNOMIAL", o.Card.Title);
        Assert.Equal(["y = x^2 - 5*x + 3"], o.PlotTexts);
        Assert.Equal("{(5 - sqrt(13))/2, (5 + sqrt(13))/2}", o.Card.UseText);
        Assert.Contains(@"\sqrt{13}", o.Card.LatexText);
    }

    [Fact]
    public void Quadratic_with_negative_discriminant_gives_complex_roots_as_a_plus_bi()
    {
        var o = E().SolvePolynomial(Poly("1", "1", "1"), default);
        Assert.Equal(0, o.RealCount);
        Assert.Equal(2, o.ComplexCount);
        Assert.Equal("NO REAL ROOTS, 2 COMPLEX", o.Summary);
        Assert.Equal("-1/2 - (√3/2)i", A(o.Roots[0].Text));
        Assert.Equal("-1/2 + (√3/2)i", A(o.Roots[1].Text));
        Assert.Equal("-1/2 - (sqrt(3)/2)i", o.Roots[0].Input);
        // The a + bi input reads back as the same number.
        var e = E();
        var back = e.Numeric(e.Evaluate(e.Session.ParseExpression(o.Roots[0].Input), default), default)!.Value;
        Close(-0.5, back.Re);
        Close(-Math.Sqrt(3) / 2, back.Im);
        Assert.Equal("{-1/2 - (sqrt(3)/2)i, -1/2 + (sqrt(3)/2)i}", o.Card.UseText);
        Close(-0.5, o.Roots[0].Re);
        Close(-Math.Sqrt(3) / 2, o.Roots[0].Im);
        Assert.Contains(o.Notes, n => n.Contains("complex conjugate"));
        Assert.Equal(["x = -1/2 ± (√3/2)i"], o.Card.Lines.Select(A));
    }

    [Fact]
    public void Double_root_has_multiplicity_two()
    {
        var o = E().SolvePolynomial(Poly("1", "-2", "1"), default);
        var root = Assert.Single(o.Roots);
        Assert.Equal("1", root.Text);
        Assert.Equal(2, root.Multiplicity);
        Assert.Equal("1 REAL ROOT", o.Summary);
        Assert.Contains(o.Notes, n => n.Contains("double root"));
        Assert.Equal(["x₁ = 1  (double)"], o.Card.Lines);
    }

    [Fact]
    public void Cubic_with_three_rational_roots_and_turning_points()
    {
        var o = E().SolvePolynomial(Poly("1", "-6", "11", "-6"), default);
        Assert.False(o.IsError, o.Error?.Message);
        Assert.Equal(["1", "2", "3"], o.Roots.Select(r => r.Text));
        Assert.All(o.Roots, r => Assert.True(r.IsReal && !r.IsNumeric && r.Multiplicity == 1));
        Assert.Equal("3 REAL ROOTS", o.Summary);
        Assert.Null(o.Discriminant);
        Assert.Equal(2, o.TurningPoints.Count);
        Assert.True(o.TurningPoints[0].IsMaximum);
        Assert.False(o.TurningPoints[1].IsMaximum);
        Close(2 - 1 / Math.Sqrt(3), o.TurningPoints[0].X.Re);
        Close(2 + 1 / Math.Sqrt(3), o.TurningPoints[1].X.Re);
        // The values are multiplied out so the radicals combine: p(2 − 1/√3) = 2√3/9.
        Close(2 * Math.Sqrt(3) / 9, o.TurningPoints[0].Y.Re);
        Assert.Equal("2√3/9", o.TurningPoints[0].Y.Text);
        Assert.Equal("-2√3/9", A(o.TurningPoints[1].Y.Text));
        Assert.Equal(["x₁ = 1", "x₂ = 2", "x₃ = 3"], o.Card.Lines);
        Assert.Equal("{1, 2, 3}", o.Card.UseText);
    }

    [Fact]
    public void Quartic_with_only_complex_roots()
    {
        var o = E().SolvePolynomial(Poly("1", "0", "5", "0", "4"), default);
        Assert.Equal(0, o.RealCount);
        Assert.Equal(4, o.ComplexCount);
        Assert.Equal("NO REAL ROOTS, 4 COMPLEX", o.Summary);
        var ims = o.Roots.Select(r => r.Im).OrderBy(v => v).ToList();
        Close(-2, ims[0]);
        Close(-1, ims[1]);
        Close(1, ims[2]);
        Close(2, ims[3]);
        Assert.All(o.Roots, r => Close(0, r.Re));
        Assert.Contains(o.Roots, r => A(r.Text) == "-2i");
        Assert.Contains(o.Roots, r => A(r.Text) == "i");
    }

    [Fact]
    public void Quartic_with_real_and_complex_roots()
    {
        var o = E().SolvePolynomial(Poly("1", "0", "0", "0", "-1"), default);
        Assert.Equal(2, o.RealCount);
        Assert.Equal(2, o.ComplexCount);
        // Real roots first, left to right, then the complex pair.
        Assert.Equal(["-1", "1", "-i", "i"], o.Roots.Select(r => A(r.Text)));
        Assert.Equal("2 REAL ROOTS, 2 COMPLEX", o.Summary);
    }

    [Fact]
    public void Irreducible_quintic_falls_back_to_numeric_roots()
    {
        var o = E().SolvePolynomial(Poly("1", "0", "0", "0", "-1", "-1"), default);
        Assert.Equal(1, o.RealCount);
        Assert.Equal(4, o.ComplexCount);
        Assert.All(o.Roots, r => Assert.True(r.IsNumeric));
        Close(1.1673039782614187, o.Roots[0].Re);
        Assert.Contains(o.Notes, n => n.Contains("decimals"));
        Assert.Equal("x₁ ≈ 1.167303978", o.Card.Lines[0]);
    }

    [Fact]
    public void Fraction_and_root_coefficients()
    {
        var o = E().SolvePolynomial(Poly("2", "sqrt(2)", "-1/2"), default);
        Assert.False(o.IsError, o.Error?.Message);
        Assert.Equal(2, o.RealCount);
        foreach (var r in o.Roots) Close(0, 2 * r.Re * r.Re + Math.Sqrt(2) * r.Re - 0.5, 1e-9);
    }

    [Fact]
    public void Leading_zero_lowers_the_degree()
    {
        var o = E().SolvePolynomial(Poly("0", "2", "-4"), default);
        Assert.Equal(1, o.Degree);
        Assert.Equal("2", Assert.Single(o.Roots).Text);
        Assert.Contains(o.Notes, n => n.Contains("degree 1"));
    }

    [Fact]
    public void All_zero_and_constant_polynomials()
    {
        Assert.True(E().SolvePolynomial(Poly("0", "0", "0"), default).AllValues);
        var constant = E().SolvePolynomial(Poly("0", "0", "5"), default);
        Assert.Equal("NO SOLUTION", constant.Summary);
        Assert.Empty(constant.Roots);
    }

    [Fact]
    public void Bad_coefficient_is_an_error_naming_the_box()
    {
        var o = E().SolvePolynomial(Poly("1", "q", "3"), default);
        Assert.True(o.IsError);
        Assert.Equal("coefficient of x", o.Error!.Field);
        Assert.True(o.Card.IsError);
        var parse = E().SolvePolynomial(Poly("1", "2+", "3"), default);
        Assert.True(parse.IsError);
        Assert.Contains("coefficient of x", parse.Error!.Message);
    }

    [Fact]
    public void Multiplicity_helper()
    {
        // (x - 1)^3 (x + 2) = x^4 - x^3 - 3x^2 + 5x - 2
        double[] asc = [-2, 5, -3, -1, 1];
        Assert.Equal(3, PolynomialMath.Multiplicity(asc, 1, 0));
        Assert.Equal(1, PolynomialMath.Multiplicity(asc, -2, 0));
        Close(0, PolynomialMath.Evaluate(asc, 1));
        Assert.Equal([5, -6, -3, 4], PolynomialMath.Derivative(asc));
    }
}
