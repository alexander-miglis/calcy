using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Modes.Matrix.Logic;
using static Calcy.Modes.Matrix.Tests.TestUtil;

namespace Calcy.Modes.Matrix.Tests;

public class EigenTests
{
    private static readonly CalcSession S = Session();

    private static EigenAnalysis Analyse(string matrix) => Eigen.Analyse(M(matrix, S), S);

    private static string[] Values(EigenAnalysis e) =>
        [.. e.Values.SelectMany(v => Enumerable.Repeat(In(v.Exact!), v.AlgebraicMultiplicity))];

    /// <summary>A·v = λ·v, checked exactly with the engine (radicals and i included).</summary>
    private static void AssertEigenpairs(string matrix, EigenAnalysis e)
    {
        var a = M(matrix, S);
        foreach (var v in e.Values)
        {
            Assert.NotEmpty(v.Vectors);
            foreach (var vec in v.Vectors)
            {
                var col = new MatrixExpr(vec.Length, 1, [.. vec]);
                var lhs = MatrixOps.Multiply(a, col);
                var rhs = (MatrixExpr)Algebra.Mul(v.Exact!, col);
                for (int i = 0; i < vec.Length; i++)
                {
                    var diff = S.Evaluate(Algebra.Sub(lhs[i, 0], rhs[i, 0]), approximate: true);
                    var n = Assert.IsType<NumberExpr>(diff);
                    Assert.True(Math.Abs(n.Value.Re) < 1e-9 && Math.Abs(n.Value.Im) < 1e-9, $"A·v - λ·v = {diff} for λ = {v.Exact}");
                }
                Assert.Contains(vec, x => !x.IsZero);
            }
        }
    }

    [Fact]
    public void Symmetric_two_by_two_has_eigenvalues_three_and_one()
    {
        var e = Analyse("[[2, 1], [1, 2]]");
        Assert.Equal(EigenMethod.Exact, e.Method);
        Assert.Equal(["3", "1"], Values(e));
        Assert.Equal("[[1], [1]]", In(new MatrixExpr(2, 1, [.. e.Values[0].Vectors[0]])));
        Assert.Equal("[[-1], [1]]", In(new MatrixExpr(2, 1, [.. e.Values[1].Vectors[0]])));
        Assert.Equal("(λ - 3)(λ - 1)", e.Factored!.Replace('\u00A0', ' '));
        Assert.True(e.IsDiagonalizable);
        AssertEigenpairs("[[2, 1], [1, 2]]", e);
    }

    [Fact]
    public void Swapped_two_by_two_has_eigenvalues_three_and_minus_one()
    {
        var e = Analyse("[[1, 2], [2, 1]]");
        Assert.Equal(["3", "-1"], Values(e));
        Assert.Equal("λ^2 - 2*λ - 3", In(CharacteristicPolynomial.ToExpr(e.CharacteristicPolynomial)));
        AssertEigenpairs("[[1, 2], [2, 1]]", e);
    }

    [Fact]
    public void Three_by_three_with_integer_eigenvalues()
    {
        const string a = "[[1, 1, 0], [1, 0, 1], [0, 1, 1]]";
        var e = Analyse(a);
        Assert.Equal(EigenMethod.Exact, e.Method);
        Assert.Equal(["2", "1", "-1"], Values(e));
        AssertEigenpairs(a, e);
        Assert.True(e.IsDiagonalizable);
    }

    [Fact]
    public void Textbook_three_by_three_has_three_six_and_eight()
    {
        const string a = "[[6, -2, -1], [-2, 6, -1], [-1, -1, 5]]";
        var e = Analyse(a);
        Assert.Equal(["8", "6", "3"], Values(e));
        AssertEigenpairs(a, e);
    }

    [Fact]
    public void Quarter_turn_rotation_has_eigenvalues_plus_and_minus_i()
    {
        const string a = "[[0, -1], [1, 0]]";
        var e = Analyse(a);
        Assert.Equal(EigenMethod.Exact, e.Method);
        Assert.Equal(["i", "-i"], Values(e));
        Assert.Equal("λ^2 + 1", In(CharacteristicPolynomial.ToExpr(e.CharacteristicPolynomial)));
        AssertEigenpairs(a, e);
    }

    [Fact]
    public void Sixty_degree_rotation_has_complex_eigenvalues_on_the_unit_circle()
    {
        const string a = "[[1/2, -sqrt(3)/2], [sqrt(3)/2, 1/2]]";
        var e = Analyse(a);
        Assert.Equal(EigenMethod.Exact, e.Method);
        Assert.Equal(2, e.Values.Count);
        foreach (var v in e.Values)
        {
            Assert.Equal(0.5, v.Approx.Re, 12);
            Assert.Equal(Math.Sqrt(3) / 2, Math.Abs(v.Approx.Im), 12);
        }
        AssertEigenpairs(a, e);
    }

    [Fact]
    public void Tridiagonal_matrix_gets_exact_square_roots()
    {
        const string a = "[[2, -1, 0], [-1, 2, -1], [0, -1, 2]]";
        var e = Analyse(a);
        Assert.Equal(EigenMethod.Exact, e.Method);
        // Compared as expressions, so the test does not depend on how the printer orders terms.
        var expected = new[] { "2 + sqrt(2)", "2", "2 - sqrt(2)" }.Select(t => S.EvaluateExpression(t)).ToArray();
        Assert.Equal(expected, e.Values.Select(v => v.Exact!).ToArray());
        AssertEigenpairs(a, e);
    }

    [Theory]
    [InlineData("[[2, -1, 0], [-1, 2, -1], [0, -1, 2]]", "2 + √2|2|2 - √2")]
    [InlineData("[[1, 1], [1, 0]]", "(1 + √5)/2|(1 - √5)/2")]
    [InlineData("[[0, -1], [1, -1]]", "(-1 + i√3)/2|(-1 - i√3)/2")]
    [InlineData("[[0, -1], [1, 0]]", "i|-i")]
    [InlineData("[[0, 1/2], [1, 0]]", "√2/2|-√2/2")]
    public void Exact_eigenvalues_read_like_a_textbook(string matrix, string expected)
    {
        var e = Analyse(matrix);
        Assert.Equal(expected, string.Join("|", e.Values.Select(v => v.ExactText)));
        AssertEigenpairs(matrix, e);
    }

    [Fact]
    public void Four_by_four_splitting_into_two_quadratics_stays_exact()
    {
        const string a = "[[0, -1, 0, 0], [1, 0, 0, 0], [0, 0, 1, 1], [0, 0, 1, 0]]";
        var e = Analyse(a);
        Assert.Equal(EigenMethod.Exact, e.Method);
        Assert.Equal(4, e.Values.Count);
        AssertEigenpairs(a, e);
    }

    [Fact]
    public void Repeated_eigenvalue_without_enough_eigenvectors_is_defective()
    {
        var e = Analyse("[[2, 1], [0, 2]]");
        var v = Assert.Single(e.Values);
        Assert.Equal("2", In(v.Exact!));
        Assert.Equal(2, v.AlgebraicMultiplicity);
        Assert.Equal(1, v.GeometricMultiplicity);
        Assert.False(e.IsDiagonalizable);
        Assert.Equal("(λ - 2)²", e.Factored!.Replace('\u00A0', ' '));
    }

    [Fact]
    public void Identity_has_a_full_eigenspace()
    {
        var e = Analyse("[[1, 0, 0], [0, 1, 0], [0, 0, 1]]");
        var v = Assert.Single(e.Values);
        Assert.Equal(3, v.AlgebraicMultiplicity);
        Assert.Equal(3, v.GeometricMultiplicity);
        Assert.True(e.IsDiagonalizable);
    }

    [Fact]
    public void Fraction_entries_scale_correctly()
    {
        const string a = "[[1/2, 1/4], [1/4, 1/2]]";
        var e = Analyse(a);
        Assert.Equal(["3/4", "1/4"], Values(e));
        AssertEigenpairs(a, e);
    }

    [Fact]
    public void Irreducible_cubic_falls_back_to_numbers()
    {
        // The companion matrix of λ³ - 2: the eigenvalues are the three cube roots of 2.
        var e = Analyse("[[0, 0, 2], [1, 0, 0], [0, 1, 0]]");
        Assert.Equal(EigenMethod.Numeric, e.Method);
        Assert.NotNull(e.Note);
        Assert.Equal(3, e.Values.Count);
        Assert.Contains(e.Values, v => Math.Abs(v.Approx.Re - Math.Cbrt(2)) < 1e-9 && v.Approx.Im == 0);
        Assert.All(e.Values, v => Assert.Equal(Math.Cbrt(2), Calcy.Numerics.Complex.Abs(v.Approx), 9));
        Assert.Equal("λ^3 - 2", In(CharacteristicPolynomial.ToExpr(e.CharacteristicPolynomial)));
    }

    [Fact]
    public void Symbolic_two_by_two_uses_the_quadratic_formula()
    {
        var e = Analyse("[[a, 0], [0, b]]");
        Assert.Equal(EigenMethod.Exact, e.Method);
        Assert.Equal(2, e.Values.Count);
    }

    [Fact]
    public void Non_square_matrices_are_rejected()
    {
        Assert.Throws<CalcException>(() => Eigen.Analyse(M("[[1, 2, 3]]", S), S));
    }

    [Theory]
    [InlineData("[[1, 2], [3, 4]]", "λ^2 - 5*λ - 2")]
    [InlineData("[[2, 1, -1], [-3, -1, 2], [-2, 1, 2]]", "λ^3 - 3*λ^2 - λ + 1")]
    [InlineData("[[5]]", "λ - 5")]
    public void Characteristic_polynomials_are_exact(string matrix, string expected)
    {
        var c = CharacteristicPolynomial.Coefficients(M(matrix, S), S);
        Assert.Equal(expected, In(CharacteristicPolynomial.ToExpr(c)));
    }
}
