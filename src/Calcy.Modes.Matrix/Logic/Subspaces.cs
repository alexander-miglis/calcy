using System.Numerics;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>Exact bases of the null space and the column space, read off the reduced row echelon form.</summary>
public static class Subspaces
{
    /// <summary>
    /// A basis of the null space {x : A·x = 0}: one vector per free variable (the textbook basis). Rational vectors are
    /// scaled to the smallest whole numbers, so (−1/2, 1) becomes (−1, 2).
    /// </summary>
    public static IReadOnlyList<Expr[]> NullSpace(MatrixExpr a, out EliminationResult rref, CancellationToken ct = default)
    {
        rref = RowReduction.Rref(a, ct);
        return NullSpaceFromRref(rref.Result, rref.PivotColumns, a.Cols);
    }

    /// <summary>Null space basis vectors from a reduced matrix, using the first <paramref name="columns"/> columns.</summary>
    public static IReadOnlyList<Expr[]> NullSpaceFromRref(MatrixExpr reduced, IReadOnlyList<int> pivotColumns, int columns)
    {
        var pivots = pivotColumns.Where(p => p < columns).ToList();
        var basis = new List<Expr[]>();
        for (int free = 0; free < columns; free++)
        {
            if (pivots.Contains(free)) continue;
            var v = new Expr[columns];
            for (int i = 0; i < columns; i++) v[i] = Expr.Zero;
            v[free] = Expr.One;
            for (int i = 0; i < pivots.Count; i++) v[pivots[i]] = Algebra.Neg(reduced[i, free]);
            basis.Add(Primitive(v));
        }
        return basis;
    }

    /// <summary>A basis of the column space: the columns of the original matrix that hold pivots.</summary>
    public static IReadOnlyList<Expr[]> ColumnSpace(MatrixExpr a, out EliminationResult rref, CancellationToken ct = default)
    {
        rref = RowReduction.Rref(a, ct);
        var basis = new List<Expr[]>();
        foreach (int c in rref.PivotColumns)
        {
            var v = new Expr[a.Rows];
            for (int r = 0; r < a.Rows; r++) v[r] = a[r, c];
            basis.Add(v);
        }
        return basis;
    }

    /// <summary>
    /// Scales a vector of exact rationals to coprime integers (keeping its direction and sign).
    /// Vectors with other entries are returned unchanged.
    /// </summary>
    public static Expr[] Primitive(Expr[] v)
    {
        if (!v.All(e => e is NumberExpr { Value.IsExactRational: true })) return v;
        var q = v.Select(e => ((NumberExpr)e).Value.ReQ).ToArray();
        if (q.All(x => x.IsZero)) return v;
        BigInteger lcm = BigInteger.One, gcd = BigInteger.Zero;
        foreach (var x in q) lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, x.Den) * x.Den;
        var ints = q.Select(x => x.Num * (lcm / x.Den)).ToArray();
        foreach (var n in ints) gcd = BigInteger.GreatestCommonDivisor(gcd, n);
        if (gcd.IsZero) gcd = BigInteger.One;
        return [.. ints.Select(n => (Expr)new NumberExpr(Number.Exact(new BigRational(n / gcd))))];
    }
}
