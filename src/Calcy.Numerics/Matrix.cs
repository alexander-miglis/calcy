using System.Globalization;
using System.Text;

namespace Calcy.Numerics;

/// <summary>
/// A dense real matrix stored in row-major order. Arithmetic operators create new matrices;
/// the indexer allows in-place element updates. Convenience methods delegate to
/// <see cref="LinearAlgebra"/> and the decomposition classes.
/// </summary>
public sealed class Matrix : IEquatable<Matrix>
{
    private readonly double[] _a;

    /// <summary>Number of rows.</summary>
    public int Rows { get; }

    /// <summary>Number of columns.</summary>
    public int Columns { get; }

    /// <summary>Creates a zero matrix of the given size.</summary>
    public Matrix(int rows, int columns)
    {
        if (rows < 0 || columns < 0) throw new ArgumentOutOfRangeException(nameof(rows), "Matrix dimensions must be non-negative.");
        Rows = rows;
        Columns = columns;
        _a = new double[(long)rows * columns];
    }

    /// <summary>Creates a matrix from a two-dimensional array (copied).</summary>
    public Matrix(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Rows = values.GetLength(0);
        Columns = values.GetLength(1);
        _a = new double[Rows * Columns];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Columns; j++)
                _a[i * Columns + j] = values[i, j];
    }

    private Matrix(int rows, int columns, double[] data)
    {
        Rows = rows;
        Columns = columns;
        _a = data;
    }

    internal double[] Data => _a;

    internal static Matrix Wrap(int rows, int columns, double[] data) => new(rows, columns, data);

    /// <summary>Creates a matrix from rows (all rows must have the same length).</summary>
    public static Matrix FromRows(params double[][] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int r = rows.Length, c = r == 0 ? 0 : rows[0].Length;
        var m = new Matrix(r, c);
        for (int i = 0; i < r; i++)
        {
            if (rows[i].Length != c) throw new ArgumentException("All rows must have the same length.");
            Array.Copy(rows[i], 0, m._a, i * c, c);
        }
        return m;
    }

    /// <summary>Creates a matrix from columns (all columns must have the same length).</summary>
    public static Matrix FromColumns(params double[][] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        int c = columns.Length, r = c == 0 ? 0 : columns[0].Length;
        var m = new Matrix(r, c);
        for (int j = 0; j < c; j++)
        {
            if (columns[j].Length != r) throw new ArgumentException("All columns must have the same length.");
            for (int i = 0; i < r; i++) m._a[i * c + j] = columns[j][i];
        }
        return m;
    }

    /// <summary>The n by n identity matrix.</summary>
    public static Matrix Identity(int n)
    {
        var m = new Matrix(n, n);
        for (int i = 0; i < n; i++) m._a[i * n + i] = 1;
        return m;
    }

    /// <summary>A zero matrix.</summary>
    public static Matrix Zeros(int rows, int columns) => new(rows, columns);

    /// <summary>A square diagonal matrix.</summary>
    public static Matrix Diagonal(params double[] diagonal)
    {
        int n = diagonal.Length;
        var m = new Matrix(n, n);
        for (int i = 0; i < n; i++) m._a[i * n + i] = diagonal[i];
        return m;
    }

    /// <summary>A column vector (n by 1).</summary>
    public static Matrix ColumnVector(params double[] values)
    {
        var m = new Matrix(values.Length, 1);
        Array.Copy(values, m._a, values.Length);
        return m;
    }

    /// <summary>A row vector (1 by n).</summary>
    public static Matrix RowVector(params double[] values)
    {
        var m = new Matrix(1, values.Length);
        Array.Copy(values, m._a, values.Length);
        return m;
    }

    /// <summary>The n by n Hilbert matrix H[i, j] = 1 / (i + j + 1).</summary>
    public static Matrix Hilbert(int n)
    {
        var m = new Matrix(n, n);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) m._a[i * n + j] = 1.0 / (i + j + 1);
        return m;
    }

    /// <summary>Element access (zero-based).</summary>
    public double this[int row, int column]
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

    /// <summary>True if the matrix is square.</summary>
    public bool IsSquare => Rows == Columns;

    /// <summary>A deep copy.</summary>
    public Matrix Clone() => new(Rows, Columns, (double[])_a.Clone());

    /// <summary>Copies the elements to a two-dimensional array.</summary>
    public double[,] ToArray()
    {
        var r = new double[Rows, Columns];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Columns; j++) r[i, j] = _a[i * Columns + j];
        return r;
    }

    /// <summary>Copies the elements to an array of rows.</summary>
    public double[][] ToRows()
    {
        var r = new double[Rows][];
        for (int i = 0; i < Rows; i++) r[i] = GetRow(i);
        return r;
    }

    /// <summary>Copy of row i.</summary>
    public double[] GetRow(int i)
    {
        var r = new double[Columns];
        Array.Copy(_a, i * Columns, r, 0, Columns);
        return r;
    }

    /// <summary>Copy of column j.</summary>
    public double[] GetColumn(int j)
    {
        var c = new double[Rows];
        for (int i = 0; i < Rows; i++) c[i] = _a[i * Columns + j];
        return c;
    }

    /// <summary>Overwrites row i.</summary>
    public void SetRow(int i, double[] values)
    {
        if (values.Length != Columns) throw new ArgumentException("Row length mismatch.");
        Array.Copy(values, 0, _a, i * Columns, Columns);
    }

    /// <summary>Overwrites column j.</summary>
    public void SetColumn(int j, double[] values)
    {
        if (values.Length != Rows) throw new ArgumentException("Column length mismatch.");
        for (int i = 0; i < Rows; i++) _a[i * Columns + j] = values[i];
    }

    /// <summary>Transpose.</summary>
    public Matrix Transpose()
    {
        var t = new Matrix(Columns, Rows);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Columns; j++) t._a[j * Rows + i] = _a[i * Columns + j];
        return t;
    }

    /// <summary>Submatrix of the given size starting at (row, column).</summary>
    public Matrix SubMatrix(int row, int column, int rows, int columns)
    {
        if (row < 0 || column < 0 || rows < 0 || columns < 0 || row + rows > Rows || column + columns > Columns)
            throw new ArgumentOutOfRangeException(nameof(row), "Submatrix exceeds the matrix bounds.");
        var s = new Matrix(rows, columns);
        for (int i = 0; i < rows; i++)
            Array.Copy(_a, (row + i) * Columns + column, s._a, i * columns, columns);
        return s;
    }

    /// <summary>Horizontal concatenation [this | right].</summary>
    public Matrix Augment(Matrix right)
    {
        if (right.Rows != Rows) throw new ArgumentException("Augment requires the same number of rows.");
        var r = new Matrix(Rows, Columns + right.Columns);
        for (int i = 0; i < Rows; i++)
        {
            Array.Copy(_a, i * Columns, r._a, i * r.Columns, Columns);
            Array.Copy(right._a, i * right.Columns, r._a, i * r.Columns + Columns, right.Columns);
        }
        return r;
    }

    /// <summary>Vertical concatenation [this ; below].</summary>
    public Matrix Stack(Matrix below)
    {
        if (below.Columns != Columns) throw new ArgumentException("Stack requires the same number of columns.");
        var r = new Matrix(Rows + below.Rows, Columns);
        Array.Copy(_a, r._a, _a.Length);
        Array.Copy(below._a, 0, r._a, _a.Length, below._a.Length);
        return r;
    }

    /// <summary>Matrix-vector product.</summary>
    public double[] Multiply(double[] v)
    {
        if (v.Length != Columns) throw new ArgumentException($"Vector length {v.Length} does not match {Columns} columns.");
        var r = new double[Rows];
        for (int i = 0; i < Rows; i++)
        {
            double s = 0;
            int o = i * Columns;
            for (int j = 0; j < Columns; j++) s += _a[o + j] * v[j];
            r[i] = s;
        }
        return r;
    }

    /// <summary>Largest absolute element.</summary>
    public double MaxAbs()
    {
        double m = 0;
        foreach (double v in _a)
        {
            double a = Math.Abs(v);
            if (double.IsNaN(a)) return double.NaN;
            if (a > m) m = a;
        }
        return m;
    }

    /// <summary>True if all elements are finite.</summary>
    public bool IsFinite()
    {
        foreach (double v in _a) if (!double.IsFinite(v)) return false;
        return true;
    }

    /// <summary>True if square and |A[i, j] - A[j, i]| &lt;= tolerance * max|A| for all i, j.</summary>
    public bool IsSymmetric(double tolerance = 0)
    {
        if (!IsSquare) return false;
        double t = tolerance * MaxAbs();
        for (int i = 0; i < Rows; i++)
            for (int j = i + 1; j < Columns; j++)
                if (Math.Abs(_a[i * Columns + j] - _a[j * Columns + i]) > t) return false;
        return true;
    }

    // ------------------------------------------------------------------ operators

    /// <summary>Matrix sum.</summary>
    public static Matrix operator +(Matrix a, Matrix b)
    {
        CheckSameSize(a, b);
        var r = new double[a._a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = a._a[i] + b._a[i];
        return new Matrix(a.Rows, a.Columns, r);
    }

    /// <summary>Matrix difference.</summary>
    public static Matrix operator -(Matrix a, Matrix b)
    {
        CheckSameSize(a, b);
        var r = new double[a._a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = a._a[i] - b._a[i];
        return new Matrix(a.Rows, a.Columns, r);
    }

    /// <summary>Negation.</summary>
    public static Matrix operator -(Matrix a)
    {
        var r = new double[a._a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = -a._a[i];
        return new Matrix(a.Rows, a.Columns, r);
    }

    /// <summary>Matrix product.</summary>
    public static Matrix operator *(Matrix a, Matrix b)
    {
        if (a.Columns != b.Rows) throw new ArgumentException($"Cannot multiply a {a.Rows}x{a.Columns} matrix by a {b.Rows}x{b.Columns} matrix.");
        int n = a.Rows, m = a.Columns, p = b.Columns;
        var r = new double[n * p];
        for (int i = 0; i < n; i++)
        {
            int ro = i * p;
            for (int k = 0; k < m; k++)
            {
                double aik = a._a[i * m + k];
                if (aik == 0) continue;
                int bo = k * p;
                for (int j = 0; j < p; j++) r[ro + j] += aik * b._a[bo + j];
            }
        }
        return new Matrix(n, p, r);
    }

    /// <summary>Scalar product.</summary>
    public static Matrix operator *(double s, Matrix a)
    {
        var r = new double[a._a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = s * a._a[i];
        return new Matrix(a.Rows, a.Columns, r);
    }

    /// <summary>Scalar product.</summary>
    public static Matrix operator *(Matrix a, double s) => s * a;

    /// <summary>Division by a scalar.</summary>
    public static Matrix operator /(Matrix a, double s)
    {
        var r = new double[a._a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = a._a[i] / s;
        return new Matrix(a.Rows, a.Columns, r);
    }

    private static void CheckSameSize(Matrix a, Matrix b)
    {
        if (a.Rows != b.Rows || a.Columns != b.Columns)
            throw new ArgumentException($"Matrix sizes differ: {a.Rows}x{a.Columns} and {b.Rows}x{b.Columns}.");
    }

    // ------------------------------------------------------------------ convenience

    /// <summary>Trace (sum of the diagonal).</summary>
    public double Trace() => LinearAlgebra.Trace(this);

    /// <summary>Determinant (LU with partial pivoting).</summary>
    public double Determinant() => LinearAlgebra.Determinant(this);

    /// <summary>Inverse (throws for singular matrices).</summary>
    public Matrix Inverse() => LinearAlgebra.Inverse(this);

    /// <summary>Solves A x = b (least squares if A is not square).</summary>
    public double[] Solve(double[] b) => LinearAlgebra.Solve(this, b);

    /// <summary>Solves A X = B.</summary>
    public Matrix Solve(Matrix b) => LinearAlgebra.Solve(this, b);

    /// <summary>Numerical rank (SVD).</summary>
    public int Rank(double? tolerance = null) => LinearAlgebra.Rank(this, tolerance);

    /// <summary>Reduced row echelon form.</summary>
    public Matrix Rref(double? tolerance = null) => LinearAlgebra.Rref(this, tolerance);

    /// <summary>Eigenvalues (complex).</summary>
    public Complex[] Eigenvalues() => LinearAlgebra.Eigenvalues(this);

    /// <summary>Integer power (negative powers use the inverse).</summary>
    public Matrix Power(int n) => LinearAlgebra.MatrixPower(this, n);

    /// <summary>Matrix exponential.</summary>
    public Matrix Exp() => LinearAlgebra.MatrixExponential(this);

    /// <summary>Maximum absolute column sum.</summary>
    public double Norm1() => LinearAlgebra.Norm1(this);

    /// <summary>Spectral norm (largest singular value).</summary>
    public double Norm2() => LinearAlgebra.Norm2(this);

    /// <summary>Maximum absolute row sum.</summary>
    public double NormInf() => LinearAlgebra.NormInf(this);

    /// <summary>Frobenius norm.</summary>
    public double NormFrobenius() => LinearAlgebra.NormFrobenius(this);

    // ------------------------------------------------------------------ equality and formatting

    /// <summary>True if both matrices have the same size and every element differs by at most tolerance.</summary>
    public bool ApproximatelyEquals(Matrix other, double tolerance)
    {
        if (other.Rows != Rows || other.Columns != Columns) return false;
        for (int i = 0; i < _a.Length; i++)
            if (!(Math.Abs(_a[i] - other._a[i]) <= tolerance)) return false;
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(Matrix? other)
    {
        if (other is null || other.Rows != Rows || other.Columns != Columns) return false;
        for (int i = 0; i < _a.Length; i++) if (!_a[i].Equals(other._a[i])) return false;
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Matrix);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Rows);
        h.Add(Columns);
        foreach (double v in _a) h.Add(v);
        return h.ToHashCode();
    }

    /// <summary>Formats as [[a, b], [c, d]] with 15 significant digits.</summary>
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
                double v = _a[i * Columns + j];
                sb.Append((v == 0 ? 0 : v).ToString("G15", CultureInfo.InvariantCulture));
            }
            sb.Append(']');
        }
        return sb.Append(']').ToString();
    }
}
