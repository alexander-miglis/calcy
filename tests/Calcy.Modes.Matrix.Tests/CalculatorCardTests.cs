using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Modes.Matrix.Logic;
using static Calcy.Modes.Matrix.Tests.TestUtil;

namespace Calcy.Modes.Matrix.Tests;

public class CalculatorCardTests
{
    private static (MatrixCalculator Calc, CalcSession Session) Make()
    {
        var s = Session();
        foreach (var e in MatrixLibrary.CreateDefault().Entries) MatrixLibrary.PushToSession(e, s);
        return (new MatrixCalculator(s), s);
    }

    private static Operands Ops(CalcSession s, string x, string? y = null, string k = "2", string n = "2") => new()
    {
        XName = x,
        X = s.Env.Variables[x] as MatrixExpr,
        YName = y,
        Y = y is null ? null : s.Env.Variables[y] as MatrixExpr,
        K = k,
        N = n,
    };

    private static IEnumerable<string> Texts(MatrixCard c) => c.Blocks.Where(b => b.Text is not null).Select(b => b.Text!);

    [Fact]
    public void Determinant_card()
    {
        var (calc, s) = Make();
        var card = calc.Run(MatrixOperation.Determinant, Ops(s, "A"));
        Assert.False(card.IsError);
        Assert.Equal("DETERMINANT", card.Title);
        Assert.Equal("det(A)", card.Input);
        Assert.Equal("= -1", card.Blocks[0].Text);
        Assert.Equal("-1", card.CopyText);
        Assert.Contains(Texts(card), t => t.Contains("invertible"));
    }

    [Fact]
    public void Inverse_card_shows_gauss_jordan_steps()
    {
        var (calc, s) = Make();
        var card = calc.Run(MatrixOperation.Inverse, Ops(s, "C"));
        Assert.False(card.IsError);
        var grid = card.Blocks.First(b => b.Kind == CardBlockKind.Matrix);
        Assert.Equal(["-2", "1"], grid.Cells![0]);
        Assert.Equal(["3/2", "-1/2"], grid.Cells![1]);
        Assert.Equal(["1.5", "-0.5"], grid.ApproxCells![1]);
        Assert.True(card.HasApprox);
        Assert.Equal("[[-2, 1], [3/2, -1/2]]", card.CopyText);
        Assert.Equal(@"\begin{bmatrix}-2 & 1 \\ \frac{3}{2} & -\frac{1}{2}\end{bmatrix}", card.Latex);
        var steps = card.Steps.Where(b => b.Kind == CardBlockKind.Step).ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, st => Assert.Equal(2, st.AugmentAt));
    }

    [Fact]
    public void Singular_inverse_is_an_error_that_keeps_the_steps()
    {
        var (calc, s) = Make();
        s.Env.SetVariable("S", M("[[1, 2], [2, 4]]", s));
        var card = calc.Run(MatrixOperation.Inverse, Ops(s, "S"));
        Assert.True(card.IsError);
        Assert.Contains("singular", card.Blocks[0].Text);
        Assert.NotEmpty(card.Steps);
        Assert.Null(card.CopyText);
    }

    [Fact]
    public void Rref_card_lists_steps_with_headings()
    {
        var (calc, s) = Make();
        var card = calc.Run(MatrixOperation.Rref, Ops(s, "A"));
        Assert.Contains(card.Steps, b => b.Kind == CardBlockKind.Heading && b.Text!.StartsWith("Forward"));
        Assert.Contains(card.Steps, b => b.Kind == CardBlockKind.Heading && b.Text!.StartsWith("Backward"));
        Assert.Equal("R₁ → R₁ ÷ 2", card.Steps.First(b => b.Kind == CardBlockKind.Step).Label);
        Assert.Contains(Texts(card), t => t.StartsWith("Rank 3"));
    }

    [Fact]
    public void Solve_cards_cover_all_three_cases()
    {
        var (calc, s) = Make();
        var unique = calc.Run(MatrixOperation.Solve, Ops(s, "A", "B"));
        Assert.Contains("x = 2,  y = 3,  z = -1", Texts(unique));
        Assert.Equal("[[2], [3], [-1]]", unique.CopyText);

        s.Env.SetVariable("P", M("[[1, 1], [1, 1]]", s));
        s.Env.SetVariable("q", M("[[1], [2]]", s));
        var none = calc.Run(MatrixOperation.Solve, Ops(s, "P", "q"));
        Assert.False(none.IsError);
        Assert.Equal("No solution", none.Blocks[0].Text);
        Assert.True(none.Blocks[0].Emphasis);
        Assert.Contains(Texts(none), t => t.Contains("0 = 1"));

        s.Env.SetVariable("R", M("[[1, 1, 1], [0, 1, 2]]", s));
        s.Env.SetVariable("r", M("[[6], [3]]", s));
        var many = calc.Run(MatrixOperation.Solve, Ops(s, "R", "r"));
        Assert.Equal("Infinitely many solutions", many.Blocks[0].Text);
        Assert.Contains("z = t", Texts(many));
        Assert.Contains("x = (3, 3, 0) + t·(1, -2, 1)", Texts(many).Select(t => t.Replace('\u00A0', ' ')));
        Assert.Equal("[[3], [3], [0]] + t*[[1], [-2], [1]]", many.CopyText);
        Assert.Equal(2, many.StoreParts.Count);
    }

    [Fact]
    public void Eigen_card_offers_p_and_d()
    {
        var (calc, s) = Make();
        s.Env.SetVariable("S", M("[[2, 1], [1, 2]]", s));
        var card = calc.Run(MatrixOperation.Eigen, Ops(s, "S"));
        Assert.False(card.IsError);
        Assert.Contains("λ₁ = 3", Texts(card));
        Assert.Contains("λ₂ = 1", Texts(card));
        Assert.Equal("{3, 1}", card.CopyText);
        Assert.Equal(2, card.StoreParts.Count);
        Assert.Equal("[[1, -1], [1, 1]]", card.StoreParts[0].Text);
        Assert.Equal("[[3, 0], [0, 1]]", card.StoreParts[1].Text);
        Assert.Contains(Texts(card), t => t.Replace('\u00A0', ' ').Contains("(λ - 3)(λ - 1)"));
    }

    [Fact]
    public void Numeric_decompositions_reconstruct_the_matrix()
    {
        var (calc, s) = Make();
        var lu = calc.Run(MatrixOperation.Lu, Ops(s, "A"));
        Assert.False(lu.IsError);
        Assert.Equal(["P", "L", "U"], lu.StoreParts.Select(p => p.Label));
        var p = Assert.IsType<MatrixExpr>(s.EvaluateExpression(lu.StoreParts[0].Text));
        var l = Assert.IsType<MatrixExpr>(s.EvaluateExpression(lu.StoreParts[1].Text));
        var u = Assert.IsType<MatrixExpr>(s.EvaluateExpression(lu.StoreParts[2].Text));
        var pa = MatrixOps.Multiply(p, (MatrixExpr)s.Env.Variables["A"]);
        var luProduct = MatrixOps.Multiply(l, u);
        for (int i = 0; i < 9; i++)
            Assert.Equal(((NumberExpr)pa.Items[i]).Value.Re, ((NumberExpr)luProduct.Items[i]).Value.Re, 9);

        Assert.False(calc.Run(MatrixOperation.Qr, Ops(s, "A")).IsError);
        var svd = calc.Run(MatrixOperation.Svd, Ops(s, "A"));
        Assert.False(svd.IsError);
        Assert.Contains(Texts(svd), t => t.StartsWith("σ = "));
        var chol = calc.Run(MatrixOperation.Cholesky, Ops(s, "A"));
        Assert.True(chol.IsError);
        Assert.Contains("symmetric", chol.Blocks[0].Text);
        s.Env.SetVariable("S", M("[[4, 2], [2, 3]]", s));
        var good = calc.Run(MatrixOperation.Cholesky, Ops(s, "S"));
        Assert.False(good.IsError);
        Assert.Equal(["2", "0"], good.Blocks.First(b => b.Kind == CardBlockKind.Matrix).Cells![0]);
    }

    [Fact]
    public void Norms_are_exact_where_possible()
    {
        var (calc, s) = Make();
        var card = calc.Run(MatrixOperation.Norms, Ops(s, "C"));
        Assert.Contains("‖C‖₁ = 6", Texts(card));
        Assert.Contains("‖C‖∞ = 7", Texts(card));
        Assert.Contains("‖C‖F = √30", Texts(card));
        Assert.Contains(Texts(card), t => t.StartsWith("‖C‖₂ ≈ 5.46498"));
        var vec = calc.Run(MatrixOperation.Norms, Ops(s, "B"));
        Assert.Contains("‖B‖₂ = √194", Texts(vec));
    }

    [Fact]
    public void Condition_number_warns_when_ill_conditioned()
    {
        var (calc, s) = Make();
        var ok = calc.Run(MatrixOperation.Condition, Ops(s, "C"));
        Assert.StartsWith("κ₂ ≈ 14.93", ok.Blocks[0].Text);
        s.Env.SetVariable("H", M("[[1, 1/2, 1/3, 1/4], [1/2, 1/3, 1/4, 1/5], [1/3, 1/4, 1/5, 1/6], [1/4, 1/5, 1/6, 1/7]]", s));
        var hilbert = calc.Run(MatrixOperation.Condition, Ops(s, "H"));
        Assert.Contains(Texts(hilbert), t => t.Contains("lose about 4"));
        s.Env.SetVariable("Z", M("[[1, 2], [2, 4]]", s));
        Assert.Equal("κ₂ = ∞", calc.Run(MatrixOperation.Condition, Ops(s, "Z")).Blocks[0].Text);
    }

    [Fact]
    public void Binary_operations_and_errors()
    {
        var (calc, s) = Make();
        Assert.Equal("[[2], [3], [-1]]", calc.Run(MatrixOperation.Solve, Ops(s, "A", "B")).CopyText);
        Assert.Equal("[[8], [-19], [-33]]", calc.Run(MatrixOperation.Multiply, Ops(s, "A", "B")).CopyText);
        Assert.True(calc.Run(MatrixOperation.MultiplyReversed, Ops(s, "A", "B")).IsError);
        var bad = calc.Run(MatrixOperation.Add, Ops(s, "A", "C"));
        Assert.True(bad.IsError);
        Assert.Contains("same size", bad.Blocks[0].Text);
        Assert.Equal("[[7, 10], [15, 22]]", calc.Run(MatrixOperation.Power, Ops(s, "C", n: "2")).CopyText);
        Assert.Equal("[[-2, 1], [3/2, -1/2]]", calc.Run(MatrixOperation.Power, Ops(s, "C", n: "-1")).CopyText);
        Assert.Equal("[[1/2, 1], [3/2, 2]]", calc.Run(MatrixOperation.Scale, Ops(s, "C", k: "1/2")).CopyText);
        Assert.True(calc.Run(MatrixOperation.Determinant, Ops(s, "B")).IsError);
        var missing = calc.Run(MatrixOperation.Determinant, new Operands { XName = "A", XError = "Cell (1, 1): oops" });
        Assert.True(missing.IsError);
    }

    [Fact]
    public void Null_space_and_column_space_cards()
    {
        var (calc, s) = Make();
        s.Env.SetVariable("N", M("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]", s));
        var ns = calc.Run(MatrixOperation.NullSpace, Ops(s, "N"));
        Assert.Equal("v₁ =", ns.Blocks[0].Label);
        Assert.Equal("[[1], [-2], [1]]", ns.CopyText);
        var cs = calc.Run(MatrixOperation.ColumnSpace, Ops(s, "N"));
        Assert.Equal("[[1, 2], [4, 5], [7, 8]]", cs.CopyText);
        Assert.Equal("= 1", calc.Run(MatrixOperation.Nullity, Ops(s, "N")).Blocks[0].Text);
        Assert.Equal("= 2", calc.Run(MatrixOperation.Rank, Ops(s, "N")).Blocks[0].Text);
    }

    [Fact]
    public void Expression_cards()
    {
        var (calc, s) = Make();
        var card = calc.Expression("A⁻¹·B", 3);
        Assert.Equal("CALCULATION", card.Title);
        Assert.Equal("[[2], [3], [-1]]", card.CopyText);
        var stored = calc.Expression("F = 2C", 3);
        Assert.Equal("STORED", stored.Title);
        Assert.True(s.Env.Variables.ContainsKey("F"));
        var err = calc.Expression("A + C", 3);
        Assert.True(err.IsError);
    }
}
