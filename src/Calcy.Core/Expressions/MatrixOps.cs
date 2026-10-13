using System.Collections.Immutable;

namespace Calcy.Core.Expressions;

/// <summary>Exact matrix algebra on expression entries (fractions and symbols stay exact).</summary>
public static class MatrixOps
{
    public static MatrixExpr Identity(int n)
    {
        var b = ImmutableArray.CreateBuilder<Expr>(n * n);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                b.Add(r == c ? Expr.One : Expr.Zero);
        return new MatrixExpr(n, n, b.MoveToImmutable());
    }

    public static MatrixExpr Zeros(int rows, int cols) =>
        new(rows, cols, [.. Enumerable.Repeat<Expr>(Expr.Zero, rows * cols)]);

    public static MatrixExpr Multiply(MatrixExpr a, MatrixExpr b)
    {
        if (a.Cols != b.Rows)
            throw new CalcException($"Cannot multiply a {a.Rows}×{a.Cols} matrix by a {b.Rows}×{b.Cols} matrix (columns of the first must equal rows of the second)");
        var items = ImmutableArray.CreateBuilder<Expr>(a.Rows * b.Cols);
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < b.Cols; c++)
            {
                var terms = new Expr[a.Cols];
                for (int k = 0; k < a.Cols; k++) terms[k] = Algebra.Mul(a[r, k], b[k, c]);
                items.Add(Algebra.Add(terms));
            }
        }
        return new MatrixExpr(a.Rows, b.Cols, items.MoveToImmutable());
    }

    public static MatrixExpr Transpose(MatrixExpr m)
    {
        var items = ImmutableArray.CreateBuilder<Expr>(m.Rows * m.Cols);
        for (int c = 0; c < m.Cols; c++)
            for (int r = 0; r < m.Rows; r++)
                items.Add(m[r, c]);
        return new MatrixExpr(m.Cols, m.Rows, items.MoveToImmutable());
    }

    private static Expr[][] ToRows(MatrixExpr m)
    {
        var rows = new Expr[m.Rows][];
        for (int r = 0; r < m.Rows; r++)
        {
            rows[r] = new Expr[m.Cols];
            for (int c = 0; c < m.Cols; c++) rows[r][c] = m[r, c];
        }
        return rows;
    }

    private static MatrixExpr FromRows(Expr[][] rows, int cols) =>
        new(rows.Length, cols, [.. rows.SelectMany(r => r)]);

    private static bool IsZero(Expr e) => e is NumberExpr n && n.Value.IsZero;

    /// <summary>Picks a pivot row: prefers a nonzero number, then any expression that is not zero.</summary>
    private static int FindPivot(Expr[][] rows, int col, int startRow)
    {
        int candidate = -1;
        for (int r = startRow; r < rows.Length; r++)
        {
            var e = rows[r][col];
            if (IsZero(e)) continue;
            if (e is NumberExpr) return r;
            if (candidate < 0) candidate = r;
        }
        return candidate;
    }

    /// <summary>Reduced row echelon form and the rank.</summary>
    public static (MatrixExpr Rref, int Rank, List<int> PivotColumns) Rref(MatrixExpr m, CancellationToken ct = default)
    {
        var rows = ToRows(m);
        int rank = 0;
        var pivots = new List<int>();
        for (int col = 0; col < m.Cols && rank < m.Rows; col++)
        {
            ct.ThrowIfCancellationRequested();
            int p = FindPivot(rows, col, rank);
            if (p < 0) continue;
            (rows[p], rows[rank]) = (rows[rank], rows[p]);
            var pivot = rows[rank][col];
            var inv = Algebra.Pow(pivot, Expr.MinusOne);
            for (int c = 0; c < m.Cols; c++) rows[rank][c] = Algebra.Mul(rows[rank][c], inv);
            for (int r = 0; r < m.Rows; r++)
            {
                if (r == rank || IsZero(rows[r][col])) continue;
                var f = rows[r][col];
                for (int c = 0; c < m.Cols; c++)
                    rows[r][c] = Algebra.Sub(rows[r][c], Algebra.Mul(f, rows[rank][c]));
            }
            pivots.Add(col);
            rank++;
        }
        return (FromRows(rows, m.Cols), rank, pivots);
    }

    /// <summary>Row echelon form (pivots scaled to 1, zeros below pivots only).</summary>
    public static MatrixExpr Ref(MatrixExpr m)
    {
        var rows = ToRows(m);
        int rank = 0;
        for (int col = 0; col < m.Cols && rank < m.Rows; col++)
        {
            int p = FindPivot(rows, col, rank);
            if (p < 0) continue;
            (rows[p], rows[rank]) = (rows[rank], rows[p]);
            var inv = Algebra.Pow(rows[rank][col], Expr.MinusOne);
            for (int c = 0; c < m.Cols; c++) rows[rank][c] = Algebra.Mul(rows[rank][c], inv);
            for (int r = rank + 1; r < m.Rows; r++)
            {
                if (IsZero(rows[r][col])) continue;
                var f = rows[r][col];
                for (int c = 0; c < m.Cols; c++)
                    rows[r][c] = Algebra.Sub(rows[r][c], Algebra.Mul(f, rows[rank][c]));
            }
            rank++;
        }
        return FromRows(rows, m.Cols);
    }

    public static Expr Determinant(MatrixExpr m)
    {
        if (m.Rows != m.Cols) throw new CalcException($"The determinant needs a square matrix, not {m.Rows}×{m.Cols}");
        int n = m.Rows;
        if (n == 0) return Expr.One;
        if (n == 1) return m[0, 0];
        if (n == 2) return Algebra.Sub(Algebra.Mul(m[0, 0], m[1, 1]), Algebra.Mul(m[0, 1], m[1, 0]));
        bool numeric = m.Items.All(e => e is NumberExpr);
        if (!numeric && n <= 4) return Laplace(m);
        var rows = ToRows(m);
        Expr det = Expr.One;
        for (int col = 0; col < n; col++)
        {
            int p = FindPivot(rows, col, col);
            if (p < 0) return Expr.Zero;
            if (p != col)
            {
                (rows[p], rows[col]) = (rows[col], rows[p]);
                det = Algebra.Neg(det);
            }
            var pivot = rows[col][col];
            det = Algebra.Mul(det, pivot);
            var inv = Algebra.Pow(pivot, Expr.MinusOne);
            for (int r = col + 1; r < n; r++)
            {
                if (IsZero(rows[r][col])) continue;
                var f = Algebra.Mul(rows[r][col], inv);
                for (int c = col; c < n; c++)
                    rows[r][c] = Algebra.Sub(rows[r][c], Algebra.Mul(f, rows[col][c]));
            }
        }
        return det;
    }

    /// <summary>Cofactor expansion, which keeps symbolic determinants polynomial (no divisions).</summary>
    private static Expr Laplace(MatrixExpr m)
    {
        int n = m.Rows;
        if (n == 1) return m[0, 0];
        if (n == 2) return Algebra.Sub(Algebra.Mul(m[0, 0], m[1, 1]), Algebra.Mul(m[0, 1], m[1, 0]));
        var terms = new List<Expr>();
        for (int c = 0; c < n; c++)
        {
            if (IsZero(m[0, c])) continue;
            var minor = Minor(m, 0, c);
            var t = Algebra.Mul(m[0, c], Laplace(minor));
            terms.Add(c % 2 == 0 ? t : Algebra.Neg(t));
        }
        return Algebra.Add(terms);
    }

    public static MatrixExpr Minor(MatrixExpr m, int row, int col)
    {
        var items = ImmutableArray.CreateBuilder<Expr>((m.Rows - 1) * (m.Cols - 1));
        for (int r = 0; r < m.Rows; r++)
        {
            if (r == row) continue;
            for (int c = 0; c < m.Cols; c++)
            {
                if (c == col) continue;
                items.Add(m[r, c]);
            }
        }
        return new MatrixExpr(m.Rows - 1, m.Cols - 1, items.MoveToImmutable());
    }

    public static MatrixExpr Inverse(MatrixExpr m)
    {
        if (m.Rows != m.Cols) throw new CalcException($"Only square matrices have an inverse, this one is {m.Rows}×{m.Cols}");
        int n = m.Rows;
        if (n == 2 && m.Items.Any(e => e is not NumberExpr))
        {
            var det = Determinant(m);
            if (IsZero(det)) throw new CalcException("The matrix is singular (its determinant is 0), so it has no inverse");
            var inv = Algebra.Pow(det, Expr.MinusOne);
            return new MatrixExpr(2, 2, [
                Algebra.Mul(inv, m[1, 1]), Algebra.Mul(inv, Algebra.Neg(m[0, 1])),
                Algebra.Mul(inv, Algebra.Neg(m[1, 0])), Algebra.Mul(inv, m[0, 0])]);
        }
        var aug = ImmutableArray.CreateBuilder<Expr>(n * 2 * n);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++) aug.Add(m[r, c]);
            for (int c = 0; c < n; c++) aug.Add(r == c ? Expr.One : Expr.Zero);
        }
        var (rref, _, pivots) = Rref(new MatrixExpr(n, 2 * n, aug.MoveToImmutable()));
        if (pivots.Count < n || pivots.Take(n).Where((p, i) => p != i).Any())
            throw new CalcException("The matrix is singular (its determinant is 0), so it has no inverse");
        var items = ImmutableArray.CreateBuilder<Expr>(n * n);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                items.Add(rref[r, n + c]);
        return new MatrixExpr(n, n, items.MoveToImmutable());
    }

    public static Expr Trace(MatrixExpr m)
    {
        if (m.Rows != m.Cols) throw new CalcException("The trace needs a square matrix");
        return Algebra.Add(Enumerable.Range(0, m.Rows).Select(i => m[i, i]));
    }

    /// <summary>Solves A x = b for a square, non-singular A (b may be a column matrix or a list).</summary>
    public static Expr Solve(MatrixExpr a, Expr b)
    {
        var inv = Inverse(a);
        var bm = b switch
        {
            MatrixExpr mb => mb,
            ListExpr lb => new MatrixExpr(lb.Count, 1, lb.Items),
            _ => throw new CalcException("The right-hand side must be a list or a column matrix"),
        };
        var x = Multiply(inv, bm);
        return b is ListExpr ? new ListExpr(x.Items) : x;
    }
}
