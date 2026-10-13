using Calcy.Core;
using Calcy.Core.Expressions;
using NumMatrix = Calcy.Numerics.Matrix;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>Converts exact Calcy matrices to double-precision <see cref="Calcy.Numerics.Matrix"/> values and back.</summary>
public static class NumericBridge
{
    /// <summary>True when every entry is an exact rational number.</summary>
    public static bool IsRational(MatrixExpr m) => m.Items.All(e => e is NumberExpr { Value.IsExactRational: true });

    /// <summary>True when every entry is a constant (no free symbols).</summary>
    public static bool IsConstant(MatrixExpr m) => m.Items.All(e => e.IsConstantValue());

    /// <summary>The matrix in double precision.</summary>
    /// <exception cref="CalcException">An entry contains a symbol or is not a real number.</exception>
    public static NumMatrix ToNumeric(MatrixExpr m, CalcSession session, CancellationToken ct = default)
    {
        var result = new NumMatrix(m.Rows, m.Cols);
        for (int r = 0; r < m.Rows; r++)
        {
            for (int c = 0; c < m.Cols; c++)
            {
                ct.ThrowIfCancellationRequested();
                result[r, c] = ToDouble(m[r, c], session, r, c, ct);
            }
        }
        return result;
    }

    private static double ToDouble(Expr e, CalcSession session, int r, int c, CancellationToken ct)
    {
        if (e is NumberExpr { Value.IsReal: true } n) return n.Value.Re;
        if (!e.IsConstantValue())
        {
            var names = string.Join(", ", e.FreeSymbols());
            throw new CalcException($"Entry ({r + 1}, {c + 1}) contains {names}, so it has no decimal value. Give {names} a value first");
        }
        var approx = session.Evaluate(e, approximate: true, ct);
        if (approx is NumberExpr { Value.IsReal: true } an && double.IsFinite(an.Value.Re)) return an.Value.Re;
        throw new CalcException($"Entry ({r + 1}, {c + 1}) is not a real number; decompositions work with real entries");
    }

    /// <summary>Wraps doubles as approximate Calcy numbers (tiny round-off noise below 1e-14 · scale becomes 0).</summary>
    public static MatrixExpr FromNumeric(NumMatrix m)
    {
        double scale = Math.Max(1e-300, m.MaxAbs());
        var items = new Expr[m.Rows * m.Columns];
        for (int r = 0; r < m.Rows; r++)
            for (int c = 0; c < m.Columns; c++)
                items[r * m.Columns + c] = Approx(Clean(m[r, c], scale));
        return new MatrixExpr(m.Rows, m.Columns, [.. items]);
    }

    /// <summary>A double as an approximate Calcy number.</summary>
    public static Expr Approx(double x) => new NumberExpr(Calcy.Core.Numbers.Number.Approx(x));

    /// <summary>Sets values that are round-off noise relative to <paramref name="scale"/> to exactly 0.</summary>
    public static double Clean(double x, double scale) => Math.Abs(x) <= 1e-13 * scale ? 0 : x;
}
