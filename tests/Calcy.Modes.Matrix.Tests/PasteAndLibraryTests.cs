using Calcy.Core.Expressions;
using Calcy.Modes.Matrix.Logic;
using static Calcy.Modes.Matrix.Tests.TestUtil;

namespace Calcy.Modes.Matrix.Tests;

public class PasteAndLibraryTests
{
    private static string Flat(string[][]? t) => t is null ? "null" : string.Join(" | ", t.Select(r => string.Join(" ; ", r)));

    [Theory]
    [InlineData("1\t2\t3\r\n4\t5\t6\r\n", "1 ; 2 ; 3 | 4 ; 5 ; 6")]
    [InlineData("1,2\n3,4", "1 ; 2 | 3 ; 4")]
    [InlineData("1;2,5\n3;4", "1 ; 2.5 | 3 ; 4")]
    [InlineData("1\t2,5\n3\t-0,75", "1 ; 2.5 | 3 ; -0.75")]
    [InlineData("1 2 3\n4 5 6", "1 ; 2 ; 3 | 4 ; 5 ; 6")]
    [InlineData("[[1, 2], [3, 4]]", "1 ; 2 | 3 ; 4")]
    [InlineData("{{1/2, sqrt(2)}, {a, 4}}", "1/2 ; sqrt(2) | a ; 4")]
    [InlineData("[1, 2; 3, 4]", "1 ; 2 | 3 ; 4")]
    [InlineData("[1, 2, 3]", "1 ; 2 ; 3")]
    [InlineData("log(8, 2), 5\n1, 2", "log(8, 2) ; 5 | 1 ; 2")]
    [InlineData("1\t2\t3\n4\t5", "1 ; 2 ; 3 | 4 ; 5 ; 0")]
    [InlineData("\"1\",\"2\"\n\"3\",\"4\"", "1 ; 2 | 3 ; 4")]
    [InlineData("x + 1", "x + 1")]
    public void Pasted_text_becomes_a_table(string text, string expected)
    {
        Assert.Equal(expected, Flat(PasteParser.Parse(text)));
    }

    [Fact]
    public void Single_values_are_not_tables()
    {
        Assert.Null(PasteParser.Parse("  \n "));
        Assert.False(PasteParser.IsTable("sqrt(2)"));
        Assert.True(PasteParser.IsTable("1\t2"));
    }

    [Fact]
    public void Cells_accept_expressions_and_report_errors()
    {
        var s = Session();
        Assert.Equal("1/2", In(MatrixLibrary.EvaluateCell("0.5", s).Value!));
        Assert.Equal("2*sqrt(2)", In(MatrixLibrary.EvaluateCell("sqrt(8)", s).Value!));
        Assert.Equal("a", In(MatrixLibrary.EvaluateCell("a", s).Value!));
        Assert.Equal("0", In(MatrixLibrary.EvaluateCell("", s).Value!));
        Assert.False(MatrixLibrary.EvaluateCell("1/", s).IsValid);
        Assert.False(MatrixLibrary.EvaluateCell("{1, 2}", s).IsValid);
        Assert.False(MatrixLibrary.EvaluateCell("1/0", s).IsValid);
    }

    [Fact]
    public void Library_matrices_become_session_variables()
    {
        var s = Session();
        var lib = MatrixLibrary.CreateDefault();
        foreach (var e in lib.Entries) Assert.True(MatrixLibrary.PushToSession(e, s));
        Assert.Equal("[[2], [3], [-1]]", In(s.EvaluateExpression("A^-1*B")));
        Assert.Equal("-1", In(s.EvaluateExpression("det(A)")));
        var a = lib.Find("A")!;
        a[0, 0] = "1/";
        Assert.False(MatrixLibrary.PushToSession(a, s));
        Assert.Equal("-1", In(s.EvaluateExpression("det(A)")));
    }

    [Fact]
    public void Resizing_keeps_existing_cells()
    {
        var e = new MatrixEntry("A", 2, 2, cells: ["1", "2", "3", "4"]);
        e.Resize(3, 3);
        Assert.Equal(["1", "2", "0", "3", "4", "0", "0", "0", "0"], e.Cells);
        e.Resize(1, 2);
        Assert.Equal(["1", "2"], e.Cells);
        e.Resize(20, 0);
        Assert.Equal(MatrixLibrary.MaxSize, e.Rows);
        Assert.Equal(1, e.Cols);
        var v = new MatrixEntry("u", 3, 4, isVector: true);
        Assert.Equal(1, v.Cols);
    }

    [Fact]
    public void Fills_and_names()
    {
        var e = new MatrixEntry("A", 3, 3);
        e.FillIdentity();
        Assert.Equal(["1", "0", "0", "0", "1", "0", "0", "0", "1"], e.Cells);
        e.FillRandom(new Random(1));
        Assert.All(e.Cells, c => Assert.InRange(int.Parse(c, System.Globalization.CultureInfo.InvariantCulture), -9, 9));
        var lib = MatrixLibrary.CreateDefault();
        Assert.Equal("D", lib.NextMatrixName());
        Assert.Equal("F", lib.NextMatrixName(n => n == "D"));
        Assert.Equal("u", lib.NextVectorName());
        var s = Session();
        Assert.True(MatrixLibrary.IsValidName("M2", s, out _));
        Assert.False(MatrixLibrary.IsValidName("I", s, out _));
        Assert.False(MatrixLibrary.IsValidName("det", s, out _));
        // Single letters that are also built-in aliases (C combinations, P permutations) can hold a matrix.
        Assert.True(MatrixLibrary.IsValidName("C", s, out _));
        Assert.True(MatrixLibrary.IsValidName("P", s, out _));
        Assert.False(MatrixLibrary.IsValidName("2A", s, out _));
        Assert.False(MatrixLibrary.IsValidName("pi", s, out _));
    }

    [Fact]
    public void State_round_trips()
    {
        var lib = MatrixLibrary.CreateDefault();
        var copy = MatrixLibrary.FromState(lib.ToState());
        Assert.Equal(lib.Entries.Select(e => e.Name + e.Dimensions + string.Join(",", e.Cells)), copy.Entries.Select(e => e.Name + e.Dimensions + string.Join(",", e.Cells)));
    }

    [Fact]
    public void Expression_line_handles_identity_and_transpose()
    {
        var s = Session();
        var lib = MatrixLibrary.CreateDefault();
        Assert.Equal(["A", "B", "C"], lib.Entries.Select(e => e.Name));
        foreach (var e in lib.Entries) MatrixLibrary.PushToSession(e, s);
        s.Env.SetVariable("F", M("[[1, 2], [3, 4]]", s));

        // C is also the combinations function, but the stored matrix wins.
        var r = ExpressionLine.Evaluate("2C^-1·F - I", s, 3);
        Assert.Null(r.Error);
        // 2·[[-2, 1], [3/2, -1/2]]·[[1, 2], [3, 4]] - I = 2I - I = I.
        Assert.Equal("[[1, 0], [0, 1]]", In(r.Value!));
        Assert.Equal("[[0, 2], [3, 3]]", In(ExpressionLine.Evaluate("C - I", s, 2).Value!));

        // ᵀ is the core's transpose operator; ^T is typed shorthand for it.
        Assert.Equal("[[2, -3, -2], [1, -1, 1], [-1, 2, 2]]", In(ExpressionLine.Evaluate("Aᵀ", s, 3).Value!));
        Assert.Equal("[[2, -3, -2], [1, -1, 1], [-1, 2, 2]]", In(ExpressionLine.Evaluate("A^T", s, 3).Value!));
        Assert.Equal("[[7, 15], [10, 22]]", In(ExpressionLine.Evaluate("(C·F)ᵀ", s, 3).Value!));
        Assert.Equal("[[1, 0, 0], [0, 1, 0], [0, 0, 1]]", In(ExpressionLine.Evaluate("A·inv(A)", s, 3).Value!));
        Assert.Equal("[[1, 0, 0], [0, 1, 0], [0, 0, 1]]", In(ExpressionLine.Evaluate("I", s, 3).Value!));
        Assert.Equal("[[8, 3, -1], [-2, 2, 1], [5, 4, 3]]", In(ExpressionLine.Evaluate("A^-1 + 4I", s, 2).Value!));
        Assert.Equal("[[194]]", In(ExpressionLine.Evaluate("Bᵀ·B", s, 3).Value!));
        Assert.Equal("[[2], [3], [-1]]", In(ExpressionLine.Evaluate("A⁻¹·B", s, 3).Value!));
    }

    [Fact]
    public void Expression_line_definitions_create_variables()
    {
        var s = Session();
        s.Env.SetVariable("D", M("[[1, 2], [3, 4]]", s));
        var r = ExpressionLine.Evaluate("E = D·D", s, 2);
        Assert.Equal("E", r.AssignedName);
        Assert.True(s.Env.TryGetVariable("E", out var e));
        Assert.Equal("[[7, 10], [15, 22]]", In(e));
        var bad = ExpressionLine.Evaluate("D·[[1, 2, 3]]", s, 2);
        Assert.True(bad.IsError);
        Assert.Contains("multiply", bad.Error);
    }

    [Theory]
    [InlineData("A^T·B", "Aᵀ·B")]
    [InlineData("(A + B)^T", "(A + B)ᵀ")]
    [InlineData("A^Tx", "A^Tx")]
    [InlineData("A^2", "A^2")]
    public void Caret_t_means_transpose(string text, string expected)
    {
        Assert.Equal(expected, ExpressionLine.CaretT(text, _ => false));
        Assert.Equal("A^T", ExpressionLine.CaretT("A^T", n => n == "T"));
    }
}
