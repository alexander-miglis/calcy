using System.Text;

namespace Calcy.Numerics;

/// <summary>A dense complex matrix stored in row-major order.</summary>
public sealed class ComplexMatrix
{
    private readonly Complex[] _a;

    /// <summary>Number of rows.</summary>
    public int Rows { get; }

    /// <summary>Number of columns.</summary>
    public int Columns { get; }

    /// <summary>Creates a zero matrix.</summary>
    public ComplexMatrix(int rows, int columns)
    {
        if (rows < 0 || columns < 0) throw new ArgumentOutOfRangeException(nameof(rows), "Matrix dimensions must be non-negative.");
        Rows = rows;
        Columns = columns;
        _a = new Complex[rows * columns];
    }

    /// <summary>Creates a matrix from a two-dimensional array (copied).</summary>
    public ComplexMatrix(Complex[,] values)
    {
        Rows = values.GetLength(0);
        Columns = values.GetLength(1);
        _a = new Complex[Rows * Columns];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Columns; j++) _a[i * Columns + j] = values[i, j];
    }

    /// <summary>Creates a complex matrix with the same entries as a real matrix.</summary>
    public static ComplexMatrix FromReal(Matrix m)
    {
        var c = new ComplexMatrix(m.Rows, m.Columns);
        for (int i = 0; i < m.Rows; i++)
            for (int j = 0; j < m.Columns; j++) c._a[i * m.Columns + j] = m[i, j];
        return c;
    }

    /// <summary>The identity matrix.</summary>
    public static ComplexMatrix Identity(int n)
    {
        var c = new ComplexMatrix(n, n);
        for (int i = 0; i < n; i++) c._a[i * n + i] = Complex.One;
        return c;
    }

    /// <summary>Element access (zero-based).</summary>
    public Complex this[int row, int column]
    {
        get
        {
            if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns) throw new IndexOutOfRangeException("Matrix index out of range.");
            return _a[row * Columns + column];
        }
        set
        {
            if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns) throw new IndexOutOfRangeException("Matrix index out of range.");
            _a[row * Columns + column] = value;
        }
    }

    /// <summary>True if square.</summary>
    public bool IsSquare => Rows == Columns;

    /// <summary>Deep copy.</summary>
    public ComplexMatrix Clone()
    {
        var c = new ComplexMatrix(Rows, Columns);
        Array.Copy(_a, c._a, _a.Length);
        return c;
    }

    /// <summary>Copy of column j.</summary>
    public Complex[] GetColumn(int j)
    {
        var c = new Complex[Rows];
        for (int i = 0; i < Rows; i++) c[i] = _a[i * Columns + j];
        return c;
    }

    /// <summary>Overwrites column j.</summary>
    public void SetColumn(int j, Complex[] values)
    {
        for (int i = 0; i < Rows; i++) _a[i * Columns + j] = values[i];
    }

    /// <summary>Transpose (no conjugation).</summary>
    public ComplexMatrix Transpose()
    {
        var t = new ComplexMatrix(Columns, Rows);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Columns; j++) t._a[j * Rows + i] = _a[i * Columns + j];
        return t;
    }

    /// <summary>Conjugate (Hermitian) transpose.</summary>
    public ComplexMatrix ConjugateTranspose()
    {
        var t = new ComplexMatrix(Columns, Rows);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Columns; j++) t._a[j * Rows + i] = Complex.Conjugate(_a[i * Columns + j]);
        return t;
    }

    /// <summary>Sum.</summary>
    public static ComplexMatrix operator +(ComplexMatrix a, ComplexMatrix b)
    {
        Check(a, b);
        var r = new ComplexMatrix(a.Rows, a.Columns);
        for (int i = 0; i < r._a.Length; i++) r._a[i] = a._a[i] + b._a[i];
        return r;
    }

    /// <summary>Difference.</summary>
    public static ComplexMatrix operator -(ComplexMatrix a, ComplexMatrix b)
    {
        Check(a, b);
        var r = new ComplexMatrix(a.Rows, a.Columns);
        for (int i = 0; i < r._a.Length; i++) r._a[i] = a._a[i] - b._a[i];
        return r;
    }

    /// <summary>Product.</summary>
    public static ComplexMatrix operator *(ComplexMatrix a, ComplexMatrix b)
    {
        if (a.Columns != b.Rows) throw new ArgumentException($"Cannot multiply a {a.Rows}x{a.Columns} matrix by a {b.Rows}x{b.Columns} matrix.");
        var r = new ComplexMatrix(a.Rows, b.Columns);
        for (int i = 0; i < a.Rows; i++)
            for (int k = 0; k < a.Columns; k++)
            {
                Complex aik = a._a[i * a.Columns + k];
                if (aik.IsZero) continue;
                for (int j = 0; j < b.Columns; j++) r._a[i * b.Columns + j] += aik * b._a[k * b.Columns + j];
            }
        return r;
    }

    /// <summary>Scalar product.</summary>
    public static ComplexMatrix operator *(Complex s, ComplexMatrix a)
    {
        var r = new ComplexMatrix(a.Rows, a.Columns);
        for (int i = 0; i < r._a.Length; i++) r._a[i] = s * a._a[i];
        return r;
    }

    /// <summary>Matrix-vector product.</summary>
    public Complex[] Multiply(Complex[] v)
    {
        if (v.Length != Columns) throw new ArgumentException("Vector length does not match the matrix.");
        var r = new Complex[Rows];
        for (int i = 0; i < Rows; i++)
        {
            Complex s = Complex.Zero;
            for (int j = 0; j < Columns; j++) s += _a[i * Columns + j] * v[j];
            r[i] = s;
        }
        return r;
    }

    private static void Check(ComplexMatrix a, ComplexMatrix b)
    {
        if (a.Rows != b.Rows || a.Columns != b.Columns) throw new ArgumentException("Matrix sizes differ.");
    }

    private double MaxAbs()
    {
        double m = 0;
        foreach (var z in _a) m = Math.Max(m, Complex.Abs(z));
        return m;
    }

    // ------------------------------------------------------------------ LU

    /// <summary>
    /// In-place LU with partial pivoting on a copy. When <paramref name="replaceZeroPivots"/> is
    /// set, zero pivots are replaced by a tiny value (used by inverse iteration).
    /// </summary>
    internal (Complex[] lu, int[] piv, int sign, bool singular) Factor(bool replaceZeroPivots)
    {
        if (!IsSquare) throw new ArgumentException("The matrix must be square.");
        int n = Rows;
        var lu = (Complex[])_a.Clone();
        var piv = new int[n];
        for (int i = 0; i < n; i++) piv[i] = i;
        int sign = 1;
        double maxAbs = MaxAbs();
        double tiny = NumericsCore.Eps * Math.Max(maxAbs, 1e-300);
        bool singular = false;
        for (int k = 0; k < n; k++)
        {
            int p = k;
            double best = Complex.Abs(lu[k * n + k]);
            for (int i = k + 1; i < n; i++)
            {
                double v = Complex.Abs(lu[i * n + k]);
                if (v > best) { best = v; p = i; }
            }
            if (p != k)
            {
                for (int j = 0; j < n; j++) (lu[p * n + j], lu[k * n + j]) = (lu[k * n + j], lu[p * n + j]);
                (piv[p], piv[k]) = (piv[k], piv[p]);
                sign = -sign;
            }
            if (!(Complex.Abs(lu[k * n + k]) > n * NumericsCore.Eps * maxAbs)) singular = true;
            if (lu[k * n + k].IsZero)
            {
                if (!replaceZeroPivots) continue;
                lu[k * n + k] = tiny;
            }
            Complex pivot = lu[k * n + k];
            for (int i = k + 1; i < n; i++)
            {
                Complex l = lu[i * n + k] / pivot;
                lu[i * n + k] = l;
                if (l.IsZero) continue;
                for (int j = k + 1; j < n; j++) lu[i * n + j] -= l * lu[k * n + j];
            }
        }
        if (maxAbs == 0 && n > 0) singular = true;
        return (lu, piv, sign, singular);
    }

    internal static Complex[] LuSolve(Complex[] lu, int[] piv, Complex[] b)
    {
        int n = piv.Length;
        var x = new Complex[n];
        for (int i = 0; i < n; i++) x[i] = b[piv[i]];
        for (int i = 0; i < n; i++)
        {
            Complex s = x[i];
            for (int j = 0; j < i; j++) s -= lu[i * n + j] * x[j];
            x[i] = s;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            Complex s = x[i];
            for (int j = i + 1; j < n; j++) s -= lu[i * n + j] * x[j];
            x[i] = s / lu[i * n + i];
        }
        return x;
    }

    /// <summary>Determinant (0 when singular to working precision).</summary>
    public Complex Determinant()
    {
        var (lu, _, sign, singular) = Factor(false);
        if (singular) return Complex.Zero;
        Complex d = sign;
        for (int i = 0; i < Rows; i++) d *= lu[i * Rows + i];
        return d;
    }

    /// <summary>Solves A x = b.</summary>
    /// <exception cref="InvalidOperationException">The matrix is singular.</exception>
    public Complex[] Solve(Complex[] b)
    {
        if (b.Length != Rows) throw new ArgumentException("Right-hand side length does not match the matrix.");
        var (lu, piv, _, singular) = Factor(false);
        if (singular) throw new InvalidOperationException("The matrix is singular; the system has no unique solution.");
        return LuSolve(lu, piv, b);
    }

    /// <summary>Solves A X = B.</summary>
    public ComplexMatrix Solve(ComplexMatrix b)
    {
        if (b.Rows != Rows) throw new ArgumentException("Right-hand side rows do not match the matrix.");
        var (lu, piv, _, singular) = Factor(false);
        if (singular) throw new InvalidOperationException("The matrix is singular; the system has no unique solution.");
        var x = new ComplexMatrix(Rows, b.Columns);
        for (int j = 0; j < b.Columns; j++) x.SetColumn(j, LuSolve(lu, piv, b.GetColumn(j)));
        return x;
    }

    /// <summary>Inverse matrix.</summary>
    public ComplexMatrix Inverse() => Solve(Identity(Rows));

    /// <summary>True if every element differs by at most tolerance in magnitude.</summary>
    public bool ApproximatelyEquals(ComplexMatrix other, double tolerance)
    {
        if (other.Rows != Rows || other.Columns != Columns) return false;
        for (int i = 0; i < _a.Length; i++) if (!(Complex.Abs(_a[i] - other._a[i]) <= tolerance)) return false;
        return true;
    }

    /// <summary>Formats as [[a, b], [c, d]].</summary>
    public override string ToString()
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < Rows; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('[');
            for (int j = 0; j < Columns; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append(_a[i * Columns + j].ToString(12));
            }
            sb.Append(']');
        }
        return sb.Append(']').ToString();
    }
}
