namespace Calcy.Numerics;

/// <summary>Eigen-decomposition of a symmetric matrix: A = V diag(Values) V^T.</summary>
/// <param name="Values">Eigenvalues in descending order.</param>
/// <param name="Vectors">Orthonormal eigenvectors as columns, in the same order.</param>
public sealed record SymmetricEigenResult(double[] Values, Matrix Vectors);

/// <summary>Eigenvalues and eigenvectors of a general real matrix.</summary>
/// <param name="Values">Eigenvalues sorted by descending real part, then descending imaginary part.</param>
/// <param name="Vectors">Unit eigenvectors as columns (largest component real and positive).</param>
/// <param name="IsDiagonalizable">False if the matrix appears defective (repeated eigenvector columns).</param>
public sealed record EigenResult(Complex[] Values, ComplexMatrix Vectors, bool IsDiagonalizable);

/// <summary>Reduced row echelon form with the pivot columns.</summary>
/// <param name="Reduced">The reduced matrix.</param>
/// <param name="PivotColumns">Zero-based indices of pivot columns.</param>
public sealed record RrefResult(Matrix Reduced, int[] PivotColumns);

/// <summary>Dense linear algebra on <see cref="Matrix"/>.</summary>
public static class LinearAlgebra
{
    private const double Eps = NumericsCore.Eps;

    private static void RequireSquare(Matrix a, string operation)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (!a.IsSquare) throw new ArgumentException($"{operation} requires a square matrix (got {a.Rows}x{a.Columns}).");
    }

    // ------------------------------------------------------------------ basics

    /// <summary>Sum of the diagonal.</summary>
    public static double Trace(Matrix a)
    {
        RequireSquare(a, "Trace");
        double t = 0;
        for (int i = 0; i < a.Rows; i++) t += a[i, i];
        return t;
    }

    /// <summary>Determinant via LU with partial pivoting (0 for numerically singular matrices).</summary>
    public static double Determinant(Matrix a)
    {
        RequireSquare(a, "Determinant");
        if (a.Rows == 0) return 1;
        return new LuDecomposition(a).Determinant;
    }

    /// <summary>Inverse via LU.</summary>
    /// <exception cref="InvalidOperationException">The matrix is singular.</exception>
    public static Matrix Inverse(Matrix a)
    {
        RequireSquare(a, "Inverse");
        var lu = new LuDecomposition(a);
        if (lu.IsSingular) throw new InvalidOperationException("The matrix is singular and has no inverse.");
        return lu.Inverse();
    }

    /// <summary>
    /// Solves A x = b. Square systems use LU (throws if singular); rectangular systems are solved
    /// in the least-squares sense (minimum norm when underdetermined).
    /// </summary>
    public static double[] Solve(Matrix a, double[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.IsSquare)
        {
            var lu = new LuDecomposition(a);
            if (lu.IsSingular) throw new InvalidOperationException("The matrix is singular; the system has no unique solution.");
            return lu.Solve(b);
        }
        return LeastSquares(a, b);
    }

    /// <summary>Solves A X = B (square A via LU, otherwise least squares column by column).</summary>
    public static Matrix Solve(Matrix a, Matrix b)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.IsSquare)
        {
            var lu = new LuDecomposition(a);
            if (lu.IsSingular) throw new InvalidOperationException("The matrix is singular; the system has no unique solution.");
            return lu.Solve(b);
        }
        var x = new Matrix(a.Columns, b.Columns);
        for (int j = 0; j < b.Columns; j++) x.SetColumn(j, LeastSquares(a, b.GetColumn(j)));
        return x;
    }

    /// <summary>
    /// Least-squares solution minimizing |A x - b|. Uses Householder QR for full-rank
    /// overdetermined systems and the SVD minimum-norm solution otherwise.
    /// </summary>
    public static double[] LeastSquares(Matrix a, double[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (b.Length != a.Rows) throw new ArgumentException("Right-hand side length does not match the matrix rows.");
        if (a.Rows >= a.Columns)
        {
            var qr = new QrDecomposition(a);
            if (qr.IsFullRank) return qr.Solve(b);
        }
        return new SvdDecomposition(a).Solve(b);
    }

    /// <summary>Moore-Penrose pseudo-inverse.</summary>
    public static Matrix PseudoInverse(Matrix a, double? tolerance = null) => new SvdDecomposition(a).PseudoInverse(tolerance);

    /// <summary>Numerical rank from the SVD (default tolerance max(m, n) eps sigma_max).</summary>
    public static int Rank(Matrix a, double? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.Rows == 0 || a.Columns == 0) return 0;
        return new SvdDecomposition(a).Rank(tolerance);
    }

    /// <summary>Reduced row echelon form (Gauss-Jordan with partial pivoting); entries below tolerance become 0.</summary>
    public static Matrix Rref(Matrix a, double? tolerance = null) => RrefWithPivots(a, tolerance).Reduced;

    /// <summary>Reduced row echelon form and pivot columns.</summary>
    public static RrefResult RrefWithPivots(Matrix a, double? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        var r = a.Clone();
        int m = r.Rows, n = r.Columns;
        double tol = tolerance ?? Math.Max(m, n) * Eps * Math.Max(NormInf(a), 0);
        var pivots = new List<int>();
        int row = 0;
        for (int col = 0; col < n && row < m; col++)
        {
            int p = row;
            double best = Math.Abs(r[row, col]);
            for (int i = row + 1; i < m; i++)
            {
                double v = Math.Abs(r[i, col]);
                if (v > best) { best = v; p = i; }
            }
            if (!(best > tol))
            {
                for (int i = row; i < m; i++) r[i, col] = 0;
                continue;
            }
            if (p != row)
                for (int j = 0; j < n; j++) (r[p, j], r[row, j]) = (r[row, j], r[p, j]);
            double pv = r[row, col];
            for (int j = col; j < n; j++) r[row, j] /= pv;
            r[row, col] = 1;
            for (int i = 0; i < m; i++)
            {
                if (i == row) continue;
                double f = r[i, col];
                if (f == 0) continue;
                for (int j = col; j < n; j++) r[i, j] -= f * r[row, j];
                r[i, col] = 0;
            }
            pivots.Add(col);
            row++;
        }
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
                if (Math.Abs(r[i, j]) <= tol) r[i, j] = 0;
        return new RrefResult(r, pivots.ToArray());
    }

    /// <summary>Orthonormal basis of the null space (columns), from the SVD.</summary>
    public static Matrix NullSpace(Matrix a, double? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        int m = a.Rows, n = a.Columns;
        if (n == 0) return new Matrix(0, 0);
        Matrix w = m >= n ? a : a.Stack(new Matrix(n - m, n));
        var svd = new SvdDecomposition(w);
        double tol = tolerance ?? Math.Max(m, n) * Eps * (svd.S.Length > 0 ? svd.S[0] : 0);
        var cols = new List<double[]>();
        for (int j = 0; j < n; j++)
            if (!(svd.S[j] > tol)) cols.Add(svd.V.GetColumn(j));
        if (cols.Count == 0) return new Matrix(n, 0);
        return Matrix.FromColumns(cols.ToArray());
    }

    /// <summary>
    /// Null space basis read off the reduced row echelon form: one vector per free variable with
    /// that variable equal to 1 (the textbook basis, not orthonormal).
    /// </summary>
    public static Matrix NullSpaceFromRref(Matrix a, double? tolerance = null)
    {
        var rr = RrefWithPivots(a, tolerance);
        int n = a.Columns;
        var isPivot = new bool[n];
        foreach (int p in rr.PivotColumns) isPivot[p] = true;
        var cols = new List<double[]>();
        for (int f = 0; f < n; f++)
        {
            if (isPivot[f]) continue;
            var v = new double[n];
            v[f] = 1;
            for (int i = 0; i < rr.PivotColumns.Length; i++)
            {
                double c = -rr.Reduced[i, f];
                v[rr.PivotColumns[i]] = c == 0 ? 0 : c;
            }
            cols.Add(v);
        }
        if (cols.Count == 0) return new Matrix(n, 0);
        return Matrix.FromColumns(cols.ToArray());
    }

    /// <summary>Orthonormal basis of the column space (range), from the SVD.</summary>
    public static Matrix ColumnSpace(Matrix a, double? tolerance = null)
    {
        var svd = new SvdDecomposition(a);
        int r = svd.Rank(tolerance);
        return svd.U.SubMatrix(0, 0, a.Rows, r);
    }

    // ------------------------------------------------------------------ norms

    /// <summary>Maximum absolute column sum.</summary>
    public static double Norm1(Matrix a)
    {
        double best = 0;
        for (int j = 0; j < a.Columns; j++)
        {
            double s = 0;
            for (int i = 0; i < a.Rows; i++) s += Math.Abs(a[i, j]);
            best = Math.Max(best, s);
        }
        return best;
    }

    /// <summary>Maximum absolute row sum.</summary>
    public static double NormInf(Matrix a)
    {
        double best = 0;
        for (int i = 0; i < a.Rows; i++)
        {
            double s = 0;
            for (int j = 0; j < a.Columns; j++) s += Math.Abs(a[i, j]);
            best = Math.Max(best, s);
        }
        return best;
    }

    /// <summary>Frobenius norm (computed without overflow).</summary>
    public static double NormFrobenius(Matrix a) => NumericsCore.Norm2(a.Data);

    /// <summary>Spectral norm (largest singular value).</summary>
    public static double Norm2(Matrix a)
    {
        if (a.Rows == 0 || a.Columns == 0) return 0;
        return new SvdDecomposition(a).Norm2;
    }

    /// <summary>2-norm condition number sigma_max / sigma_min (Infinity if singular).</summary>
    public static double ConditionNumber(Matrix a) => new SvdDecomposition(a).ConditionNumber;

    // ------------------------------------------------------------------ functions of matrices

    /// <summary>Integer matrix power by repeated squaring; negative powers use the inverse.</summary>
    public static Matrix MatrixPower(Matrix a, int n)
    {
        RequireSquare(a, "Matrix power");
        Matrix b = n < 0 ? Inverse(a) : a;
        long e = Math.Abs((long)n);
        Matrix result = Matrix.Identity(a.Rows);
        while (e > 0)
        {
            if ((e & 1) != 0) result = result * b;
            e >>= 1;
            if (e > 0) b = b * b;
        }
        return result;
    }

    /// <summary>Matrix exponential by scaling and squaring with a degree 13 Pade approximant (Higham 2005).</summary>
    public static Matrix MatrixExponential(Matrix a)
    {
        RequireSquare(a, "Matrix exponential");
        int n = a.Rows;
        if (n == 0) return new Matrix(0, 0);
        if (!a.IsFinite()) throw new ArgumentException("The matrix contains NaN or infinite values.");
        double[] b =
        [
            64764752532480000, 32382376266240000, 7771770303897600, 1187353796428800, 129060195264000,
            10559470521600, 670442572800, 33522128640, 1323241920, 40840800, 960960, 16380, 182, 1,
        ];
        const double theta13 = 5.371920351148152;
        double norm = Norm1(a);
        int s = norm > theta13 ? (int)Math.Ceiling(Math.Log2(norm / theta13)) : 0;
        Matrix A = s > 0 ? a * Math.Pow(2, -s) : a;
        var I = Matrix.Identity(n);
        var A2 = A * A;
        var A4 = A2 * A2;
        var A6 = A4 * A2;
        var U = A * (A6 * (b[13] * A6 + b[11] * A4 + b[9] * A2) + b[7] * A6 + b[5] * A4 + b[3] * A2 + b[1] * I);
        var V = A6 * (b[12] * A6 + b[10] * A4 + b[8] * A2) + b[6] * A6 + b[4] * A4 + b[2] * A2 + b[0] * I;
        var lu = new LuDecomposition(V - U);
        if (lu.IsSingular) throw new InvalidOperationException("The Pade denominator is singular; the matrix exponential could not be computed.");
        var R = lu.Solve(V + U);
        for (int k = 0; k < s; k++) R = R * R;
        return R;
    }

    /// <summary>
    /// Orthonormalizes the columns of A by modified Gram-Schmidt with reorthogonalization.
    /// Columns that are linearly dependent on earlier ones (within tolerance) are dropped.
    /// </summary>
    public static Matrix GramSchmidt(Matrix a, double tolerance = 1e-10)
    {
        ArgumentNullException.ThrowIfNull(a);
        int m = a.Rows;
        var basis = new List<double[]>();
        for (int j = 0; j < a.Columns; j++)
        {
            double[] v = a.GetColumn(j);
            double original = NumericsCore.Norm2(v);
            if (original == 0) continue;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var q in basis)
                {
                    double d = NumericsCore.Dot(q, v);
                    for (int i = 0; i < m; i++) v[i] -= d * q[i];
                }
            }
            double nv = NumericsCore.Norm2(v);
            if (nv <= tolerance * original) continue;
            for (int i = 0; i < m; i++) v[i] /= nv;
            basis.Add(v);
        }
        if (basis.Count == 0) return new Matrix(m, 0);
        return Matrix.FromColumns(basis.ToArray());
    }

    /// <summary>
    /// Coefficients (ascending powers) of the characteristic polynomial det(lambda I - A),
    /// computed with the division-free Berkowitz algorithm (exact for integer matrices of
    /// moderate size).
    /// </summary>
    public static double[] CharacteristicPolynomial(Matrix a)
    {
        RequireSquare(a, "Characteristic polynomial");
        int n = a.Rows;
        if (n == 0) return [1];
        double[] p = [1, -a[n - 1, n - 1]];
        for (int k = n - 2; k >= 0; k--)
        {
            int m = n - k;
            var t = new double[m + 1];
            t[0] = 1;
            t[1] = -a[k, k];
            var v = new double[m - 1];
            for (int i = 0; i < m - 1; i++) v[i] = a[k + 1 + i, k];
            for (int j = 2; j <= m; j++)
            {
                double s = 0;
                for (int i = 0; i < m - 1; i++) s += a[k, k + 1 + i] * v[i];
                t[j] = -s;
                if (j < m)
                {
                    var nv = new double[m - 1];
                    for (int r = 0; r < m - 1; r++)
                    {
                        double acc = 0;
                        for (int c = 0; c < m - 1; c++) acc += a[k + 1 + r, k + 1 + c] * v[c];
                        nv[r] = acc;
                    }
                    v = nv;
                }
            }
            var np = new double[m + 1];
            for (int i = 0; i <= m; i++)
            {
                double s = 0;
                for (int j = 0; j <= Math.Min(i, m - 1); j++) s += t[i - j] * p[j];
                np[i] = s;
            }
            p = np;
        }
        Array.Reverse(p);
        return p;
    }

    // ------------------------------------------------------------------ eigenvalues

    /// <summary>
    /// Eigenvalues of a real square matrix. Symmetric matrices use the Jacobi method; general
    /// matrices use balancing, Householder reduction to Hessenberg form and the Francis
    /// double-shift QR algorithm. Sorted by descending real part, then descending imaginary part.
    /// </summary>
    public static Complex[] Eigenvalues(Matrix a)
    {
        RequireSquare(a, "Eigenvalues");
        int n = a.Rows;
        if (n == 0) return [];
        if (!a.IsFinite()) throw new ArgumentException("The matrix contains NaN or infinite values.");
        if (a.IsSymmetric(8 * Eps))
        {
            var sym = SymmetricEigen(a);
            return sym.Values.Select(v => new Complex(v, 0)).ToArray();
        }
        double[] h = (double[])a.Data.Clone();
        Balance(h, n);
        Hessenberg(h, n);
        var values = HessenbergQr(h, n);
        SortEigenvalues(values);
        return values;
    }

    private static void SortEigenvalues(Complex[] values)
    {
        Array.Sort(values, (p, q) =>
        {
            int c = q.Re.CompareTo(p.Re);
            return c != 0 ? c : q.Im.CompareTo(p.Im);
        });
    }

    /// <summary>Eigenvalues (descending) and orthonormal eigenvectors of a symmetric matrix (cyclic Jacobi).</summary>
    /// <exception cref="ArgumentException">The matrix is not symmetric.</exception>
    public static SymmetricEigenResult SymmetricEigen(Matrix a)
    {
        RequireSquare(a, "Symmetric eigen-decomposition");
        if (!a.IsSymmetric(1e-10)) throw new ArgumentException("The matrix is not symmetric.");
        int n = a.Rows;
        var A = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) A[i, j] = 0.5 * (a[i, j] + a[j, i]);
        var V = new double[n, n];
        for (int i = 0; i < n; i++) V[i, i] = 1;
        var d = new double[n];
        var bArr = new double[n];
        var z = new double[n];
        for (int i = 0; i < n; i++) { d[i] = A[i, i]; bArr[i] = d[i]; }

        bool converged = n <= 1;
        for (int sweep = 1; sweep <= 100 && !converged; sweep++)
        {
            double sm = 0;
            for (int p = 0; p < n - 1; p++)
                for (int q = p + 1; q < n; q++) sm += Math.Abs(A[p, q]);
            if (sm == 0) { converged = true; break; }
            double tresh = sweep < 4 ? 0.2 * sm / (n * n) : 0;
            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double g = 100 * Math.Abs(A[p, q]);
                    if (sweep > 4 && Math.Abs(d[p]) + g == Math.Abs(d[p]) && Math.Abs(d[q]) + g == Math.Abs(d[q]))
                    {
                        A[p, q] = 0;
                    }
                    else if (Math.Abs(A[p, q]) > tresh)
                    {
                        double hh = d[q] - d[p];
                        double t;
                        if (Math.Abs(hh) + g == Math.Abs(hh))
                        {
                            t = A[p, q] / hh;
                        }
                        else
                        {
                            double theta = 0.5 * hh / A[p, q];
                            t = 1 / (Math.Abs(theta) + Math.Sqrt(1 + theta * theta));
                            if (theta < 0) t = -t;
                        }
                        double c = 1 / Math.Sqrt(1 + t * t), s = t * c, tau = s / (1 + c);
                        hh = t * A[p, q];
                        z[p] -= hh; z[q] += hh;
                        d[p] -= hh; d[q] += hh;
                        A[p, q] = 0;
                        for (int j = 0; j < p; j++) Rotate(A, j, p, j, q, s, tau);
                        for (int j = p + 1; j < q; j++) Rotate(A, p, j, j, q, s, tau);
                        for (int j = q + 1; j < n; j++) Rotate(A, p, j, q, j, s, tau);
                        for (int j = 0; j < n; j++) Rotate(V, j, p, j, q, s, tau);
                    }
                }
            }
            for (int p = 0; p < n; p++)
            {
                bArr[p] += z[p];
                d[p] = bArr[p];
                z[p] = 0;
            }
        }
        if (!converged) throw new ConvergenceException("The Jacobi eigenvalue iteration did not converge.");

        var order = Enumerable.Range(0, n).ToArray();
        Array.Sort(order, (p, q) => d[q].CompareTo(d[p]));
        var values = new double[n];
        var vectors = new Matrix(n, n);
        for (int k = 0; k < n; k++)
        {
            int j = order[k];
            values[k] = d[j];
            // Sign convention: largest component positive.
            int big = 0;
            for (int i = 1; i < n; i++) if (Math.Abs(V[i, j]) > Math.Abs(V[big, j])) big = i;
            double sign = V[big, j] < 0 ? -1 : 1;
            for (int i = 0; i < n; i++) vectors[i, k] = sign * V[i, j];
        }
        return new SymmetricEigenResult(values, vectors);
    }

    private static void Rotate(double[,] a, int i, int j, int k, int l, double s, double tau)
    {
        double g = a[i, j], h = a[k, l];
        a[i, j] = g - s * (h + g * tau);
        a[k, l] = h + s * (g - h * tau);
    }

    /// <summary>Parlett-Reinsch balancing by powers of two (similarity transform, exact).</summary>
    private static void Balance(double[] a, int n)
    {
        const double radix = 2, sqrdx = 4;
        bool done = false;
        int guard = 0;
        while (!done && guard++ < 1000)
        {
            done = true;
            for (int i = 0; i < n; i++)
            {
                double r = 0, c = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    c += Math.Abs(a[j * n + i]);
                    r += Math.Abs(a[i * n + j]);
                }
                if (c == 0 || r == 0) continue;
                double g = r / radix, f = 1, s = c + r;
                while (c < g) { f *= radix; c *= sqrdx; }
                g = r * radix;
                while (c > g) { f /= radix; c /= sqrdx; }
                if ((c + r) / f < 0.95 * s)
                {
                    done = false;
                    double gi = 1 / f;
                    for (int j = 0; j < n; j++) a[i * n + j] *= gi;
                    for (int j = 0; j < n; j++) a[j * n + i] *= f;
                }
            }
        }
    }

    /// <summary>Householder reduction to upper Hessenberg form (in place, eigenvalues preserved).</summary>
    private static void Hessenberg(double[] h, int n)
    {
        var v = new double[n];
        for (int k = 0; k < n - 2; k++)
        {
            double scale = 0;
            for (int i = k + 1; i < n; i++) scale += Math.Abs(h[i * n + k]);
            if (scale == 0) continue;
            double sigma = 0;
            for (int i = k + 1; i < n; i++)
            {
                v[i] = h[i * n + k] / scale;
                sigma += v[i] * v[i];
            }
            double norm = Math.Sqrt(sigma);
            double alpha = v[k + 1] > 0 ? -norm : norm;
            v[k + 1] -= alpha;
            double vtv = 0;
            for (int i = k + 1; i < n; i++) vtv += v[i] * v[i];
            if (vtv == 0) continue;
            double beta = 2 / vtv;
            // Left: H = (I - beta v v^T) H on rows k+1..n-1.
            for (int j = k; j < n; j++)
            {
                double s = 0;
                for (int i = k + 1; i < n; i++) s += v[i] * h[i * n + j];
                s *= beta;
                for (int i = k + 1; i < n; i++) h[i * n + j] -= s * v[i];
            }
            // Right: H = H (I - beta v v^T) on columns k+1..n-1.
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int j = k + 1; j < n; j++) s += h[i * n + j] * v[j];
                s *= beta;
                for (int j = k + 1; j < n; j++) h[i * n + j] -= s * v[j];
            }
            h[(k + 1) * n + k] = alpha * scale;
            for (int i = k + 2; i < n; i++) h[i * n + k] = 0;
        }
    }

    /// <summary>Eigenvalues of an upper Hessenberg matrix by the Francis double-shift QR algorithm.</summary>
    private static Complex[] HessenbergQr(double[] h, int n)
    {
        var eig = new Complex[n];
        double norm = 0;
        for (int i = 0; i < n * n; i++) norm += Math.Abs(h[i]);
        if (norm == 0) return eig;
        int hi = n - 1, iter = 0, total = 0;
        while (hi >= 0)
        {
            int l = hi;
            while (l > 0)
            {
                double s = Math.Abs(h[(l - 1) * n + l - 1]) + Math.Abs(h[l * n + l]);
                if (s == 0) s = norm;
                if (Math.Abs(h[l * n + l - 1]) <= Eps * s)
                {
                    h[l * n + l - 1] = 0;
                    break;
                }
                l--;
            }
            if (l == hi)
            {
                eig[hi] = h[hi * n + hi];
                hi--;
                iter = 0;
                continue;
            }
            if (l == hi - 1)
            {
                var (e1, e2) = Eigen2x2(h[(hi - 1) * n + hi - 1], h[(hi - 1) * n + hi], h[hi * n + hi - 1], h[hi * n + hi]);
                eig[hi - 1] = e1;
                eig[hi] = e2;
                hi -= 2;
                iter = 0;
                continue;
            }
            if (++total > 100 * n) throw new ConvergenceException("The QR eigenvalue iteration did not converge.");
            iter++;

            double sh, th;
            if (iter % 10 == 0)
            {
                // Exceptional shift to break cycles.
                double x0 = Math.Abs(h[hi * n + hi - 1]) + Math.Abs(h[(hi - 1) * n + hi - 2]);
                double sigma = h[hi * n + hi] + 0.75 * x0;
                sh = 2 * sigma;
                th = sigma * sigma;
            }
            else
            {
                double a11 = h[(hi - 1) * n + hi - 1], a12 = h[(hi - 1) * n + hi], a21 = h[hi * n + hi - 1], a22 = h[hi * n + hi];
                sh = a11 + a22;
                th = a11 * a22 - a12 * a21;
            }

            int m = l;
            double x = h[m * n + m] * h[m * n + m] + h[m * n + m + 1] * h[(m + 1) * n + m] - sh * h[m * n + m] + th;
            double y = h[(m + 1) * n + m] * (h[m * n + m] + h[(m + 1) * n + m + 1] - sh);
            double z = h[(m + 1) * n + m] * h[(m + 2) * n + m + 1];
            for (int k = m; k <= hi - 2; k++)
            {
                double scale = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
                if (scale != 0)
                {
                    double v0 = x / scale, v1 = y / scale, v2 = z / scale;
                    double nv = Math.Sqrt(v0 * v0 + v1 * v1 + v2 * v2);
                    double alpha = v0 > 0 ? -nv : nv;
                    double u0 = v0 - alpha;
                    double utu = u0 * u0 + v1 * v1 + v2 * v2;
                    if (utu != 0)
                    {
                        double beta = 2 / utu;
                        int c0 = Math.Max(m, k - 1);
                        for (int j = c0; j < n && j <= hi; j++)
                        {
                            double p = beta * (u0 * h[k * n + j] + v1 * h[(k + 1) * n + j] + v2 * h[(k + 2) * n + j]);
                            h[k * n + j] -= p * u0;
                            h[(k + 1) * n + j] -= p * v1;
                            h[(k + 2) * n + j] -= p * v2;
                        }
                        int rmax = Math.Min(k + 3, hi);
                        for (int i = m; i <= rmax; i++)
                        {
                            double p = beta * (h[i * n + k] * u0 + h[i * n + k + 1] * v1 + h[i * n + k + 2] * v2);
                            h[i * n + k] -= p * u0;
                            h[i * n + k + 1] -= p * v1;
                            h[i * n + k + 2] -= p * v2;
                        }
                        if (k > m)
                        {
                            h[(k + 1) * n + k - 1] = 0;
                            h[(k + 2) * n + k - 1] = 0;
                        }
                    }
                }
                x = h[(k + 1) * n + k];
                y = h[(k + 2) * n + k];
                if (k < hi - 2) z = h[(k + 3) * n + k];
            }
            {
                int k = hi - 1;
                double scale = Math.Abs(x) + Math.Abs(y);
                if (scale != 0)
                {
                    double v0 = x / scale, v1 = y / scale;
                    double nv = Math.Sqrt(v0 * v0 + v1 * v1);
                    double alpha = v0 > 0 ? -nv : nv;
                    double u0 = v0 - alpha;
                    double utu = u0 * u0 + v1 * v1;
                    if (utu != 0)
                    {
                        double beta = 2 / utu;
                        for (int j = Math.Max(m, k - 1); j <= hi; j++)
                        {
                            double p = beta * (u0 * h[k * n + j] + v1 * h[(k + 1) * n + j]);
                            h[k * n + j] -= p * u0;
                            h[(k + 1) * n + j] -= p * v1;
                        }
                        for (int i = m; i <= hi; i++)
                        {
                            double p = beta * (h[i * n + k] * u0 + h[i * n + k + 1] * v1);
                            h[i * n + k] -= p * u0;
                            h[i * n + k + 1] -= p * v1;
                        }
                        if (k > m) h[(k + 1) * n + k - 1] = 0;
                    }
                }
            }
        }
        return eig;
    }

    /// <summary>Eigenvalues of [[a, b], [c, d]] computed without overflow or cancellation.</summary>
    internal static (Complex, Complex) Eigen2x2(double a, double b, double c, double d)
    {
        double p = 0.5 * (a - d);
        double bcmax = Math.Max(Math.Abs(b), Math.Abs(c));
        double bcmis = Math.Min(Math.Abs(b), Math.Abs(c)) * Math.Sign(b) * Math.Sign(c);
        double scale = Math.Max(Math.Abs(p), bcmax);
        if (scale == 0) return (d, d);
        double z = p / scale * p + bcmax / scale * bcmis;
        if (z >= 0)
        {
            double mu = p + (p >= 0 ? 1 : -1) * Math.Sqrt(scale) * Math.Sqrt(z);
            if (mu == 0) return (d, d);
            double l1 = d + mu;
            double l2 = d - bcmax / mu * bcmis;
            return l1 >= l2 ? (l1, l2) : (l2, l1);
        }
        double re = d + p;
        double im = Math.Sqrt(scale) * Math.Sqrt(-z);
        return (new Complex(re, im), new Complex(re, -im));
    }

    // ------------------------------------------------------------------ eigenvectors

    /// <summary>
    /// Eigenvalues and eigenvectors of a real square matrix. Symmetric matrices use the Jacobi
    /// method (real orthonormal vectors). General matrices compute eigenvectors by inverse
    /// iteration in complex arithmetic; repeated eigenvalues get independent vectors when they
    /// exist, and <see cref="EigenResult.IsDiagonalizable"/> is false for defective matrices.
    /// </summary>
    public static EigenResult Eigen(Matrix a, CancellationToken cancellationToken = default)
    {
        RequireSquare(a, "Eigen-decomposition");
        int n = a.Rows;
        if (!a.IsFinite()) throw new ArgumentException("The matrix contains NaN or infinite values.");
        if (a.IsSymmetric(8 * Eps))
        {
            var sym = SymmetricEigen(a);
            return new EigenResult(sym.Values.Select(v => new Complex(v, 0)).ToArray(), ComplexMatrix.FromReal(sym.Vectors), true);
        }
        Complex[] values = Eigenvalues(a);
        var vectors = new ComplexMatrix(n, n);
        double normA = Math.Max(NormInf(a), 1e-300);
        double clusterTol = 1e-6 * Math.Max(1.0, normA);
        bool diagonalizable = true;
        var found = new List<(Complex Value, Complex[] Vector)>();
        var usedPartners = new HashSet<int>();
        for (int k = 0; k < n; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Complex lambda = values[k];
            if (lambda.Im < 0)
            {
                int partner = -1;
                for (int f = 0; f < found.Count; f++)
                {
                    var fv = found[f].Value;
                    if (usedPartners.Contains(f) || fv.Im <= 0) continue;
                    if (Math.Abs(fv.Re - lambda.Re) <= 1e-12 * Math.Max(1, Math.Abs(lambda.Re)) &&
                        Math.Abs(fv.Im + lambda.Im) <= 1e-12 * Math.Max(1, Math.Abs(lambda.Im)))
                    {
                        partner = f;
                        break;
                    }
                }
                if (partner >= 0)
                {
                    usedPartners.Add(partner);
                    var conj = found[partner].Vector.Select(Complex.Conjugate).ToArray();
                    found.Add((lambda, conj));
                    vectors.SetColumn(k, conj);
                    continue;
                }
            }
            var cluster = found.Where(f => Complex.Abs(f.Value - lambda) <= clusterTol).Select(f => f.Vector).ToList();
            Complex[] v = InverseIteration(a, lambda, cluster, k);
            double res = EigenResidual(a, lambda, v, normA);
            if (cluster.Count > 0 && !(res <= 1e-6))
            {
                v = InverseIteration(a, lambda, [], k);
                diagonalizable = false;
            }
            found.Add((lambda, v));
            vectors.SetColumn(k, v);
        }
        return new EigenResult(values, vectors, diagonalizable);
    }

    private static double EigenResidual(Matrix a, Complex lambda, Complex[] v, double normA)
    {
        int n = a.Rows;
        double r = 0, vn = 0;
        for (int i = 0; i < n; i++)
        {
            Complex s = Complex.Zero;
            for (int j = 0; j < n; j++) s += a[i, j] * v[j];
            s -= lambda * v[i];
            r = Math.Max(r, Complex.Abs(s));
            vn = Math.Max(vn, Complex.Abs(v[i]));
        }
        return r / (normA * Math.Max(vn, 1e-300));
    }

    private static Complex[] InverseIteration(Matrix a, Complex lambda, List<Complex[]> orthogonalTo, int seedIndex)
    {
        int n = a.Rows;
        var B = ComplexMatrix.FromReal(a);
        for (int i = 0; i < n; i++) B[i, i] -= lambda;
        var (lu, piv, _, _) = B.Factor(true);
        var rng = new SplitMix64((ulong)(seedIndex + 1) * 7919UL);
        var x = new Complex[n];
        for (int i = 0; i < n; i++) x[i] = 0.5 + rng.NextDouble();
        for (int it = 0; it < 4; it++)
        {
            Orthogonalize(x, orthogonalTo);
            NormalizeMax(x);
            var y = ComplexMatrix.LuSolve(lu, piv, x);
            if (!y.All(z => z.IsFinite)) break;
            x = y;
            NormalizeMax(x);
        }
        Orthogonalize(x, orthogonalTo);
        // Unit 2-norm with the largest component real and positive.
        double norm = 0;
        int big = 0;
        for (int i = 0; i < n; i++)
        {
            double m = Complex.Abs(x[i]);
            norm = Complex.Hypot(norm, m);
            if (m > Complex.Abs(x[big])) big = i;
        }
        if (norm == 0) { x[0] = 1; return x; }
        Complex phase = Complex.Conjugate(x[big]) / Complex.Abs(x[big]);
        for (int i = 0; i < n; i++)
        {
            x[i] = x[i] * phase / norm;
            if (lambda.Im == 0) x[i] = new Complex(x[i].Re, 0);
        }
        return x;
    }

    private static void Orthogonalize(Complex[] x, List<Complex[]> basis)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var q in basis)
            {
                Complex d = Complex.Zero;
                double qq = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    d += Complex.Conjugate(q[i]) * x[i];
                    qq += q[i].Re * q[i].Re + q[i].Im * q[i].Im;
                }
                if (qq == 0) continue;
                d /= qq;
                for (int i = 0; i < x.Length; i++) x[i] -= d * q[i];
            }
        }
    }

    private static void NormalizeMax(Complex[] x)
    {
        double m = 0;
        foreach (var z in x) m = Math.Max(m, Complex.Abs(z));
        if (m == 0 || !double.IsFinite(m)) return;
        for (int i = 0; i < x.Length; i++) x[i] /= m;
    }

    // ------------------------------------------------------------------ vectors

    /// <summary>Dot product.</summary>
    public static double Dot(double[] a, double[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vectors must have the same length.");
        var s = new NumericsCore.CompensatedSum();
        for (int i = 0; i < a.Length; i++) s.Add(a[i] * b[i]);
        return s.Value;
    }

    /// <summary>Cross product of two 3-vectors.</summary>
    public static double[] Cross(double[] a, double[] b)
    {
        if (a.Length != 3 || b.Length != 3) throw new ArgumentException("The cross product requires 3-vectors.");
        return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    }

    /// <summary>Euclidean norm (overflow-safe).</summary>
    public static double VectorNorm(double[] a) => NumericsCore.Norm2(a);

    /// <summary>Angle between two vectors in radians.</summary>
    public static double AngleBetween(double[] a, double[] b)
    {
        double na = VectorNorm(a), nb = VectorNorm(b);
        if (na == 0 || nb == 0) throw new ArgumentException("The angle with a zero vector is undefined.");
        var ua = a.Select(v => v / na).ToArray();
        var ub = b.Select(v => v / nb).ToArray();
        // atan2 form is accurate for nearly parallel vectors.
        double s = 0;
        var diff = new double[ua.Length];
        var sum = new double[ua.Length];
        for (int i = 0; i < ua.Length; i++) { diff[i] = ua[i] - ub[i]; sum[i] = ua[i] + ub[i]; }
        s = 2 * Math.Atan2(VectorNorm(diff), VectorNorm(sum));
        return s;
    }

    /// <summary>Projection of a onto b.</summary>
    public static double[] Projection(double[] a, double[] b)
    {
        double bb = Dot(b, b);
        if (bb == 0) throw new ArgumentException("Cannot project onto a zero vector.");
        double f = Dot(a, b) / bb;
        return b.Select(v => f * v).ToArray();
    }
}
