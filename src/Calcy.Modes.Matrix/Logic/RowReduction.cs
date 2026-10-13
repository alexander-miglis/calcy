using System.Text;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>The three elementary row operations.</summary>
public enum RowOperationKind
{
    /// <summary>Swap two rows.</summary>
    Swap,

    /// <summary>Multiply a row by a non-zero number.</summary>
    Scale,

    /// <summary>Add a multiple of one row to another.</summary>
    AddMultiple,
}

/// <summary>Which part of Gauss-Jordan elimination a step belongs to.</summary>
public enum EliminationPhase
{
    /// <summary>Making zeros below each pivot (row echelon form).</summary>
    Forward,

    /// <summary>Making zeros above each pivot (reduced row echelon form).</summary>
    Backward,
}

/// <summary>
/// One elementary row operation with zero-based row indices. For <see cref="RowOperationKind.Scale"/> the target row is
/// multiplied by <see cref="Factor"/>; for <see cref="RowOperationKind.AddMultiple"/> the target row becomes
/// target + Factor · source.
/// </summary>
public sealed record RowOperation(RowOperationKind Kind, int Target, int Source, Expr Factor)
{
    /// <summary>Readable form, for example "R₂ → R₂ − 3·R₁", "R₁ → R₁ ÷ 2" or "R₁ ↔ R₃".</summary>
    public string Describe(PrintOptions? options = null)
    {
        var o = options ?? PrintOptions.Display;
        string t = RowName(Target), s = RowName(Source);
        switch (Kind)
        {
            case RowOperationKind.Swap:
                return $"{t} ↔ {s}";
            case RowOperationKind.Scale:
            {
                // Dividing by the pivot reads better than multiplying by its reciprocal (R₁ ÷ 2 rather than (1/2)·R₁).
                var pivot = Algebra.Pow(Factor, Expr.MinusOne);
                bool pivotIsFraction = pivot is NumberExpr { Value.IsExactRational: true } pn && !pn.Value.ReQ.IsInteger;
                if (!pivotIsFraction)
                {
                    if (pivot.IsOne) return $"{t} → {t}";
                    if (IsMinusOne(pivot)) return $"{t} → −{t}";
                    return $"{t} → {t} ÷ {Wrap(ExprPrinter.Print(pivot, o), allowLeadingMinus: false)}";
                }
                return $"{t} → {Coefficient(Factor, o)}{t}";
            }
            default:
            {
                bool negative = IsNegative(Factor);
                var magnitude = negative ? Algebra.Neg(Factor) : Factor;
                return $"{t} → {t} {(negative ? "−" : "+")} {Coefficient(magnitude, o)}{s}";
            }
        }
    }

    /// <summary>The same operation as a Calcy call on a matrix called <paramref name="matrixName"/> (1-based rows).</summary>
    public string ToCalcyInput(string matrixName) => Kind switch
    {
        RowOperationKind.Swap => $"rowswap({matrixName}, {Target + 1}, {Source + 1})",
        RowOperationKind.Scale => $"rowmul({matrixName}, {ExprPrinter.ToInput(Factor)}, {Target + 1})",
        _ => $"rowmuladd({matrixName}, {ExprPrinter.ToInput(Factor)}, {Source + 1}, {Target + 1})",
    };

    /// <summary>R with a subscript row number (1-based).</summary>
    public static string RowName(int index)
    {
        var sb = new StringBuilder("R");
        foreach (var c in (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) sb.Append((char)('₀' + (c - '0')));
        return sb.ToString();
    }

    private static string Coefficient(Expr k, PrintOptions o)
    {
        if (k.IsOne) return "";
        return Wrap(ExprPrinter.Print(k, o), allowLeadingMinus: true) + "·";
    }

    private static string Wrap(string text, bool allowLeadingMinus)
    {
        bool simple = text.All(c => char.IsLetterOrDigit(c) || c is '√' or 'π' or '.' or '²' or '³') ||
                      (allowLeadingMinus && text.Length > 1 && text[0] == '-' && text[1..].All(char.IsLetterOrDigit));
        return simple ? text : "(" + text + ")";
    }

    private static bool IsMinusOne(Expr e) => e is NumberExpr { Value.IsMinusOne: true };

    private static bool IsNegative(Expr e) => e switch
    {
        NumberExpr n => n.Value.IsNegativeReal,
        MulExpr m => m.Factors[0] is NumberExpr { Value.IsNegativeReal: true },
        _ => false,
    };
}

/// <summary>A row operation together with the matrix it produced.</summary>
public sealed record EliminationStep(RowOperation Operation, MatrixExpr After, EliminationPhase Phase);

/// <summary>The outcome of row reduction: the final matrix, every step and the pivot columns.</summary>
public sealed record EliminationResult(MatrixExpr Original, MatrixExpr Result, IReadOnlyList<EliminationStep> Steps, IReadOnlyList<int> PivotColumns)
{
    /// <summary>The number of pivots (the rank of the matrix).</summary>
    public int Rank => PivotColumns.Count;
}

/// <summary>
/// Exact Gaussian and Gauss-Jordan elimination that records every elementary row operation, so students can follow it.
/// The pivot rules match <see cref="MatrixOps"/>, so the results equal Calcy's ref() and rref().
/// </summary>
public static class RowReduction
{
    /// <summary>Row echelon form with leading 1s (zeros below each pivot).</summary>
    public static EliminationResult Ref(MatrixExpr m, CancellationToken ct = default) => Run(m, reduced: false, ct);

    /// <summary>Reduced row echelon form (zeros above and below each leading 1).</summary>
    public static EliminationResult Rref(MatrixExpr m, CancellationToken ct = default) => Run(m, reduced: true, ct);

    /// <summary>True when the entry is exactly zero.</summary>
    public static bool IsZero(Expr e) => e is NumberExpr { Value.IsZero: true };

    private static EliminationResult Run(MatrixExpr m, bool reduced, CancellationToken ct)
    {
        int rowCount = m.Rows, colCount = m.Cols;
        var rows = new Expr[rowCount][];
        for (int r = 0; r < rowCount; r++)
        {
            rows[r] = new Expr[colCount];
            for (int c = 0; c < colCount; c++) rows[r][c] = m[r, c];
        }
        var steps = new List<EliminationStep>();
        var pivots = new List<int>();
        void Record(RowOperation op, EliminationPhase phase) => steps.Add(new EliminationStep(op, Snapshot(rows, colCount), phase));

        int rank = 0;
        for (int col = 0; col < colCount && rank < rowCount; col++)
        {
            ct.ThrowIfCancellationRequested();
            int p = FindPivot(rows, col, rank);
            if (p < 0) continue;
            if (p != rank)
            {
                (rows[p], rows[rank]) = (rows[rank], rows[p]);
                Record(new RowOperation(RowOperationKind.Swap, rank, p, Expr.One), EliminationPhase.Forward);
            }
            var pivot = rows[rank][col];
            if (!pivot.IsOne)
            {
                var inv = Algebra.Pow(pivot, Expr.MinusOne);
                for (int c = 0; c < colCount; c++) rows[rank][c] = c == col ? Expr.One : Algebra.Mul(inv, rows[rank][c]);
                Record(new RowOperation(RowOperationKind.Scale, rank, rank, inv), EliminationPhase.Forward);
            }
            for (int r = rank + 1; r < rowCount; r++)
            {
                if (IsZero(rows[r][col])) continue;
                AddMultiple(rows, r, rank, col, colCount, out var factor);
                Record(new RowOperation(RowOperationKind.AddMultiple, r, rank, factor), EliminationPhase.Forward);
            }
            pivots.Add(col);
            rank++;
        }

        if (reduced)
        {
            for (int k = pivots.Count - 1; k >= 0; k--)
            {
                ct.ThrowIfCancellationRequested();
                int col = pivots[k];
                for (int r = 0; r < k; r++)
                {
                    if (IsZero(rows[r][col])) continue;
                    AddMultiple(rows, r, k, col, colCount, out var factor);
                    Record(new RowOperation(RowOperationKind.AddMultiple, r, k, factor), EliminationPhase.Backward);
                }
            }
        }
        return new EliminationResult(m, Snapshot(rows, colCount), steps, pivots);
    }

    /// <summary>row[target] += factor · row[source] with factor = -row[target][col], so the pivot column becomes exactly 0.</summary>
    private static void AddMultiple(Expr[][] rows, int target, int source, int col, int colCount, out Expr factor)
    {
        factor = Algebra.Neg(rows[target][col]);
        for (int c = 0; c < colCount; c++)
        {
            if (c == col) { rows[target][c] = Expr.Zero; continue; }
            if (IsZero(rows[source][c])) continue;
            rows[target][c] = Algebra.Add(rows[target][c], Algebra.Mul(factor, rows[source][c]));
        }
    }

    /// <summary>Same rule as Calcy's rref: the first non-zero number from the current row down, else the first non-zero expression.</summary>
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

    private static MatrixExpr Snapshot(Expr[][] rows, int cols) => new(rows.Length, cols, [.. rows.SelectMany(r => r)]);
}
