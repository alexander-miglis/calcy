using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Modes.Matrix.Logic;
using static Calcy.Modes.Matrix.Tests.TestUtil;

namespace Calcy.Modes.Matrix.Tests;

public class SubspaceAndSystemTests
{
    private static string Vec(Expr[] v) => In(new MatrixExpr(v.Length, 1, [.. v]));

    [Fact]
    public void Null_space_of_a_rank_deficient_matrix()
    {
        var a = M("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]");
        var basis = Subspaces.NullSpace(a, out var rref);
        Assert.Equal(2, rref.Rank);
        var v = Assert.Single(basis);
        Assert.Equal("[[1], [-2], [1]]", Vec(v));
        Assert.Equal("[[0], [0], [0]]", In(MatrixOps.Multiply(a, new MatrixExpr(3, 1, [.. v]))));
    }

    [Fact]
    public void Null_space_with_two_free_variables_is_scaled_to_whole_numbers()
    {
        var a = M("[[2, 4, 6], [1, 2, 3]]");
        var basis = Subspaces.NullSpace(a, out _);
        Assert.Equal(2, basis.Count);
        Assert.Equal("[[-2], [1], [0]]", Vec(basis[0]));
        Assert.Equal("[[-3], [0], [1]]", Vec(basis[1]));
        var b = M("[[2, 3, 0], [1, 0, 2]]");
        var single = Assert.Single(Subspaces.NullSpace(b, out _));
        // The textbook vector (-2, 4/3, 1) becomes (-6, 4, 3).
        Assert.Equal("[[-6], [4], [3]]", Vec(single));
    }

    [Fact]
    public void Null_space_of_an_invertible_matrix_is_trivial()
    {
        Assert.Empty(Subspaces.NullSpace(M("[[1, 2], [3, 4]]"), out _));
    }

    [Fact]
    public void Column_space_uses_the_pivot_columns_of_the_original_matrix()
    {
        var basis = Subspaces.ColumnSpace(M("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]"), out var rref);
        Assert.Equal([0, 1], rref.PivotColumns);
        Assert.Equal(2, basis.Count);
        Assert.Equal("[[1], [4], [7]]", Vec(basis[0]));
        Assert.Equal("[[2], [5], [8]]", Vec(basis[1]));
    }

    [Fact]
    public void Unique_solution_of_the_design_system()
    {
        var s = LinearSystem.Solve(M("[[2, 1, -1], [-3, -1, 2], [-2, 1, 2]]"), M("[[8], [-11], [-3]]"));
        Assert.Equal(SolutionKind.Unique, s.Kind);
        Assert.Equal("[[2], [3], [-1]]", In(s.Particular!));
        Assert.Equal(["x", "y", "z"], s.Variables);
        AssertStepsReplay(s.Elimination);
    }

    [Fact]
    public void Inconsistent_system_has_no_solution()
    {
        var s = LinearSystem.Solve(M("[[1, 1], [1, 1]]"), M("[[1], [2]]"));
        Assert.Equal(SolutionKind.None, s.Kind);
        Assert.Equal(1, s.ContradictionRow);
        Assert.Equal("1", In(s.Elimination.Result[1, 2]));
        Assert.Null(s.Particular);
    }

    [Fact]
    public void Underdetermined_system_has_a_parametric_solution()
    {
        // x + y + z = 6, y + 2z = 3: z is free, so x = 3 + t, y = 3 - 2t, z = t.
        var a = M("[[1, 1, 1], [0, 1, 2]]");
        var b = M("[[6], [3]]");
        var s = LinearSystem.Solve(a, b);
        Assert.Equal(SolutionKind.Infinite, s.Kind);
        Assert.Equal([2], s.FreeVariables);
        Assert.Equal(["t"], s.Parameters);
        Assert.Equal("[[3], [3], [0]]", In(s.Particular!));
        Assert.Equal("[[1], [-2], [1]]", Vec(s.Directions[0]));
        Assert.Equal(["t + 3", "-2*t + 3", "t"], s.Components.Select(In));
        // Every member of the family solves the system: A·(p + 5d) = b.
        var p = s.Particular!;
        var d = new MatrixExpr(3, 1, [.. s.Directions[0]]);
        var x = (MatrixExpr)Algebra.Add(p, Algebra.Mul(Expr.Int(5), d));
        Assert.Equal(In(b), In(MatrixOps.Multiply(a, x)));
    }

    [Fact]
    public void Two_free_variables_get_numbered_parameters()
    {
        var s = LinearSystem.Solve(M("[[1, 2, 3]]"), M("[[6]]"));
        Assert.Equal(SolutionKind.Infinite, s.Kind);
        Assert.Equal(["t₁", "t₂"], s.Parameters);
        Assert.Equal(2, s.Directions.Count);
    }

    [Fact]
    public void Singular_square_system_can_have_infinitely_many_solutions()
    {
        var s = LinearSystem.Solve(M("[[1, 2], [2, 4]]"), M("[[3], [6]]"));
        Assert.Equal(SolutionKind.Infinite, s.Kind);
        Assert.Equal(["-2*t + 3", "t"], s.Components.Select(In));
    }

    [Fact]
    public void Several_right_hand_sides_are_solved_together()
    {
        var s = LinearSystem.Solve(M("[[1, 2], [3, 4]]"), M("[[1, 0], [0, 1]]"));
        Assert.Equal(SolutionKind.Unique, s.Kind);
        Assert.Equal("[[-2, 1], [3/2, -1/2]]", In(s.Particular!));
    }

    [Fact]
    public void Mismatched_sizes_are_explained()
    {
        var ex = Assert.Throws<CalcException>(() => LinearSystem.Solve(M("[[1, 2], [3, 4]]"), M("[[1], [2], [3]]")));
        Assert.Contains("2 rows", ex.Message);
    }

    [Fact]
    public void Many_unknowns_use_subscripts()
    {
        Assert.Equal(["x₁", "x₂", "x₃", "x₄"], LinearSystem.VariableNames(4));
    }
}
