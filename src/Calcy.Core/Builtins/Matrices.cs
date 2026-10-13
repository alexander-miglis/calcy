using System.Collections.Immutable;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Builtins;

public static partial class CoreBuiltins
{
    private static void RegisterMatrices(FunctionRegistry r)
    {
        const string cat = "Matrices and vectors";
        r.Add(Def("det", c => MatrixOps.Determinant(MatrixArg(c, 0)), cat, "det(A)", "Determinant of a square matrix.", "det([[1, 2], [3, 4]])", listable: false, aliases: ["determinant"]));
        r.Add(Def("inv", c => MatrixOps.Inverse(MatrixArg(c, 0)), cat, "inv(A)", "Inverse matrix (also A^-1).", "inv([[1, 2], [3, 4]])", listable: false, aliases: ["inverse"]));
        r.Add(Def("transpose", c => MatrixOps.Transpose(MatrixArg(c, 0)), cat, "transpose(A)", "Swaps rows and columns.", "transpose([[1, 2, 3]])", listable: false));
        r.Add(Def("trace", c => MatrixOps.Trace(MatrixArg(c, 0)), cat, "trace(A)", "Sum of the diagonal.", "trace([[1, 2], [3, 4]])", listable: false));
        r.Add(Def("rref", c => MatrixOps.Rref(MatrixArg(c, 0), c.Context.Cancellation).Rref, cat, "rref(A)", "Reduced row echelon form (Gauss-Jordan).", "rref([[1, 2, 3], [4, 5, 6]])", listable: false));
        r.Add(Def("ref", c => MatrixOps.Ref(MatrixArg(c, 0)), cat, "ref(A)", "Row echelon form.", "ref([[1, 2], [3, 4]])", listable: false));
        r.Add(Def("rank", c => Expr.Int(MatrixOps.Rref(MatrixArg(c, 0), c.Context.Cancellation).Rank), cat, "rank(A)", "Number of independent rows.", "rank([[1, 2], [2, 4]])", listable: false));
        r.Add(Def("identity", c => MatrixOps.Identity(SmallIntArg(c, 0, 1, 500)), cat, "identity(n)", "The n×n identity matrix.", "identity(3)", listable: false, aliases: ["eye"]));
        r.Add(Def("zeros", c => MatrixOps.Zeros(SmallIntArg(c, 0, 1, 1000), c.Count > 1 ? SmallIntArg(c, 1, 1, 1000) : SmallIntArg(c, 0, 1, 1000)), cat, "zeros(r, c)", "A matrix full of zeros.", "zeros(2, 3)", min: 1, max: 2, listable: false));
        r.Add(Def("dot", Dot, cat, "dot(u, v)", "Dot product of two vectors.", "dot({1, 2, 3}, {4, 5, 6})", min: 2, max: 2, listable: false, aliases: ["dotP"]));
        r.Add(Def("cross", Cross, cat, "cross(u, v)", "Cross product of two 3D vectors.", "cross({1, 0, 0}, {0, 1, 0})", min: 2, max: 2, listable: false, aliases: ["crossP"]));
        r.Add(Def("norm", c =>
        {
            var items = VectorItems(c, 0);
            return Algebra.Sqrt(Algebra.Add(items.Select(x => Algebra.Mul(x, x))));
        }, cat, "norm(v)", "Length (magnitude) of a vector.", "norm({3, 4})", listable: false, aliases: ["magnitude"]));
        r.Add(Def("unitv", c =>
        {
            var items = VectorItems(c, 0);
            var len = Algebra.Sqrt(Algebra.Add(items.Select(x => Algebra.Mul(x, x))));
            if (len.IsZero) throw c.Error("the zero vector has no direction");
            return new ListExpr([.. items.Select(x => Algebra.Div(x, len))]);
        }, cat, "unitv(v)", "Vector of length 1 in the same direction.", "unitv({3, 4})", listable: false, aliases: ["unitV", "normalize"]));
        r.Add(Def("augment", c =>
        {
            if (c[0] is ListExpr la && c[1] is ListExpr lb) return new ListExpr([.. la.Items, .. lb.Items]);
            var a = MatrixArg(c, 0);
            var b = c[1] is ListExpr col ? new MatrixExpr(col.Count, 1, col.Items) : MatrixArg(c, 1);
            if (a.Rows != b.Rows) throw c.Error("both matrices need the same number of rows");
            var items = ImmutableArray.CreateBuilder<Expr>(a.Rows * (a.Cols + b.Cols));
            for (int i = 0; i < a.Rows; i++)
            {
                for (int j = 0; j < a.Cols; j++) items.Add(a[i, j]);
                for (int j = 0; j < b.Cols; j++) items.Add(b[i, j]);
            }
            return new MatrixExpr(a.Rows, a.Cols + b.Cols, items.MoveToImmutable());
        }, cat, "augment(A, B)", "Puts B to the right of A (or joins two lists).", "augment([[1], [2]], [[3], [4]])", min: 2, max: 2, listable: false));
        r.Add(Def("linsolve", c => MatrixOps.Solve(MatrixArg(c, 0), c[1]), cat, "linsolve(A, b)", "Solves A·x = b exactly.", "linsolve([[2, 1], [1, 3]], {3, 5})", min: 2, max: 2, listable: false));
        r.Add(Def("rowswap", c => RowOp(c, (rows, m) => { int a = Row(c, 1, m), b = Row(c, 2, m); (rows[a], rows[b]) = (rows[b], rows[a]); }), cat,
            "rowswap(A, i, j)", "Swaps rows i and j.", "rowswap([[1, 2], [3, 4]], 1, 2)", min: 3, max: 3, listable: false, aliases: ["rowSwap"]));
        r.Add(Def("rowadd", c => RowOp(c, (rows, m) => { int a = Row(c, 1, m), b = Row(c, 2, m); rows[b] = [.. rows[b].Zip(rows[a], (x, y) => Algebra.Add(x, y))]; }), cat,
            "rowadd(A, i, j)", "Adds row i to row j.", "rowadd([[1, 2], [3, 4]], 1, 2)", min: 3, max: 3, listable: false, aliases: ["rowAdd", "row+"]));
        r.Add(Def("rowmul", c => RowOp(c, (rows, m) => { int a = Row(c, 2, m); var k = c[1]; rows[a] = [.. rows[a].Select(x => Algebra.Mul(k, x))]; }, matrixIndex: 0), cat,
            "rowmul(A, k, i)", "Multiplies row i by k.", "rowmul([[1, 2], [3, 4]], 2, 1)", min: 3, max: 3, listable: false, aliases: ["rowMul", "*row"]));
        r.Add(Def("rowmuladd", c => RowOp(c, (rows, m) => { int a = Row(c, 2, m), b = Row(c, 3, m); var k = c[1]; rows[b] = [.. rows[b].Zip(rows[a], (x, y) => Algebra.Add(x, Algebra.Mul(k, y)))]; }), cat,
            "rowmuladd(A, k, i, j)", "Adds k times row i to row j.", "rowmuladd([[1, 2], [3, 4]], -3, 1, 2)", min: 4, max: 4, listable: false, aliases: ["rowMulAdd", "*row+"]));
    }

    private static List<Expr> VectorItems(CallInfo c, int i) => c.Args[i] switch
    {
        ListExpr l => [.. l.Items],
        TupleExpr t => [.. t.Items],
        MatrixExpr m when m.Rows == 1 || m.Cols == 1 => [.. m.Items],
        _ => throw c.Error($"argument {i + 1} must be a vector like {{1, 2, 3}}"),
    };

    private static Expr? Dot(CallInfo c)
    {
        var a = VectorItems(c, 0);
        var b = VectorItems(c, 1);
        if (a.Count != b.Count) throw c.Error($"the vectors have different lengths ({a.Count} and {b.Count})");
        return Algebra.Add(a.Zip(b, (x, y) => Algebra.Mul(x, y)));
    }

    private static Expr? Cross(CallInfo c)
    {
        var a = VectorItems(c, 0);
        var b = VectorItems(c, 1);
        if (a.Count == 2 && b.Count == 2) return Algebra.Sub(Algebra.Mul(a[0], b[1]), Algebra.Mul(a[1], b[0]));
        if (a.Count != 3 || b.Count != 3) throw c.Error("needs two 3D vectors");
        return new ListExpr([
            Algebra.Sub(Algebra.Mul(a[1], b[2]), Algebra.Mul(a[2], b[1])),
            Algebra.Sub(Algebra.Mul(a[2], b[0]), Algebra.Mul(a[0], b[2])),
            Algebra.Sub(Algebra.Mul(a[0], b[1]), Algebra.Mul(a[1], b[0])),
        ]);
    }

    private static int Row(CallInfo c, int argIndex, MatrixExpr m)
    {
        int k = SmallIntArg(c, argIndex);
        if (k < 1 || k > m.Rows) throw c.Error($"row {k} does not exist (the matrix has {m.Rows} rows)");
        return k - 1;
    }

    private static Expr RowOp(CallInfo c, Action<Expr[][], MatrixExpr> op, int matrixIndex = 0)
    {
        var m = MatrixArg(c, matrixIndex);
        var rows = new Expr[m.Rows][];
        for (int i = 0; i < m.Rows; i++)
        {
            rows[i] = new Expr[m.Cols];
            for (int j = 0; j < m.Cols; j++) rows[i][j] = m[i, j];
        }
        op(rows, m);
        return new MatrixExpr(m.Rows, m.Cols, [.. rows.SelectMany(x => x)]);
    }
}
