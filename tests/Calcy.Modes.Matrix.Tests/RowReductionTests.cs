using Calcy.Core.Expressions;
using Calcy.Modes.Matrix.Logic;
using static Calcy.Modes.Matrix.Tests.TestUtil;

namespace Calcy.Modes.Matrix.Tests;

public class RowReductionTests
{
    [Theory]
    [InlineData("[[2, 1, -1], [-3, -1, 2], [-2, 1, 2]]", "-1")]
    [InlineData("[[1, 2], [3, 4]]", "-2")]
    [InlineData("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]", "0")]
    [InlineData("[[1/2, 1/3], [1/4, 1/5]]", "1/60")]
    [InlineData("[[a, b], [c, d]]", "a*d - b*c")]
    public void Determinants_are_exact(string matrix, string expected)
    {
        Assert.Equal(expected, In(MatrixOps.Determinant(M(matrix))));
    }

    [Theory]
    [InlineData("[[1, 2], [3, 4]]", "[[-2, 1], [3/2, -1/2]]")]
    [InlineData("[[1/2, 1/3], [1/4, 1/5]]", "[[12, -20], [-15, 30]]")]
    [InlineData("[[2, 1, -1], [-3, -1, 2], [-2, 1, 2]]", "[[4, 3, -1], [-2, -2, 1], [5, 4, -1]]")]
    [InlineData("[[2, 0, 0], [0, 3, 0], [0, 0, 4]]", "[[1/2, 0, 0], [0, 1/3, 0], [0, 0, 1/4]]")]
    public void Inverses_keep_fractions(string matrix, string expected)
    {
        var a = M(matrix);
        var inv = MatrixOps.Inverse(a);
        Assert.Equal(expected, In(inv));
        Assert.Equal(In(MatrixOps.Identity(a.Rows)), In(MatrixOps.Multiply(a, inv)));
    }

    [Fact]
    public void Rref_of_a_unique_system_lists_every_step()
    {
        // 2x + y - z = 8, -3x - y + 2z = -11, -2x + y + 2z = -3 has the solution (2, 3, -1).
        var aug = M("[[2, 1, -1, 8], [-3, -1, 2, -11], [-2, 1, 2, -3]]");
        var r = RowReduction.Rref(aug);
        Assert.Equal("[[1, 0, 0, 2], [0, 1, 0, 3], [0, 0, 1, -1]]", In(r.Result));
        Assert.Equal([0, 1, 2], r.PivotColumns);
        Assert.Equal(In(MatrixOps.Rref(aug).Rref), In(r.Result));
        AssertStepsReplay(r);
        Assert.Contains(r.Steps, s => s.Phase == EliminationPhase.Backward);
        // The first step divides row 1 by its pivot 2, the second adds 3 times row 1 to row 2.
        Assert.Equal("R₁ → R₁ ÷ 2", r.Steps[0].Operation.Describe());
        Assert.Equal("R₂ → R₂ + 3·R₁", r.Steps[1].Operation.Describe());
        Assert.Equal("R₃ → R₃ + 2·R₁", r.Steps[2].Operation.Describe());
    }

    [Fact]
    public void Rref_of_a_singular_matrix_finds_rank_two()
    {
        var a = M("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]");
        var r = RowReduction.Rref(a);
        Assert.Equal("[[1, 0, -1], [0, 1, 2], [0, 0, 0]]", In(r.Result));
        Assert.Equal(2, r.Rank);
        Assert.Equal([0, 1], r.PivotColumns);
        AssertStepsReplay(r);
        Assert.Equal("R₂ → R₂ − 4·R₁", r.Steps[0].Operation.Describe());
    }

    [Fact]
    public void Rref_of_an_inconsistent_system_puts_a_pivot_in_the_last_column()
    {
        // x + y = 1, 2x + 2y = 3: no solution.
        var aug = M("[[1, 1, 1], [2, 2, 3]]");
        var r = RowReduction.Rref(aug);
        Assert.Equal("[[1, 1, 0], [0, 0, 1]]", In(r.Result));
        Assert.Equal([0, 2], r.PivotColumns);
        AssertStepsReplay(r);
    }

    [Fact]
    public void Rref_swaps_rows_when_the_pivot_is_zero()
    {
        var a = M("[[0, 1, 2], [3, 4, 5]]");
        var r = RowReduction.Rref(a);
        Assert.Equal(RowOperationKind.Swap, r.Steps[0].Operation.Kind);
        Assert.Equal("R₁ ↔ R₂", r.Steps[0].Operation.Describe());
        Assert.Equal(In(MatrixOps.Rref(a).Rref), In(r.Result));
        AssertStepsReplay(r);
    }

    [Theory]
    [InlineData("[[2, 1, -1], [-3, -1, 2], [-2, 1, 2]]")]
    [InlineData("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]")]
    [InlineData("[[0, 0, 1], [0, 2, 4], [3, 6, 9]]")]
    [InlineData("[[1/2, 1/3, 1], [1/4, 1/5, 2]]")]
    [InlineData("[[sqrt(2), 1], [1, sqrt(2)]]")]
    public void Ref_and_rref_match_the_engine(string matrix)
    {
        var a = M(matrix);
        var re = RowReduction.Ref(a);
        var rr = RowReduction.Rref(a);
        Assert.Equal(In(MatrixOps.Ref(a)), In(re.Result));
        Assert.Equal(In(MatrixOps.Rref(a).Rref), In(rr.Result));
        Assert.All(re.Steps, s => Assert.Equal(EliminationPhase.Forward, s.Phase));
        AssertStepsReplay(re);
        AssertStepsReplay(rr);
    }

    [Fact]
    public void Scaling_by_a_fraction_reads_as_a_multiplication()
    {
        var r = RowReduction.Ref(M("[[2/3, 1], [1, 1]]"));
        Assert.Equal("R₁ → (3/2)·R₁", r.Steps[0].Operation.Describe());
        Assert.Equal("rowmul(A, 3/2, 1)", r.Steps[0].Operation.ToCalcyInput("A"));
    }

    [Fact]
    public void Row_operations_print_as_calcy_calls()
    {
        Assert.Equal("rowswap(A, 1, 2)", new RowOperation(RowOperationKind.Swap, 0, 1, Expr.One).ToCalcyInput("A"));
        Assert.Equal("rowmuladd(A, -3, 1, 2)", new RowOperation(RowOperationKind.AddMultiple, 1, 0, Expr.Int(-3)).ToCalcyInput("A"));
        Assert.Equal("R₁₂", RowOperation.RowName(11));
    }
}
