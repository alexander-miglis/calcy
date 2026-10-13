namespace Calcy.Numerics;

/// <summary>
/// LU decomposition with partial pivoting, P A = L U. The matrix is flagged singular when a
/// pivot is at most n * eps * max|A| in magnitude.
/// </summary>
public sealed class LuDecomposition
{
    private readonly double[] _lu;
    private readonly int[] _piv;
    private readonly int _n;
    private readonly int _pivSign;

    /// <summary>Factors a square matrix.</summary>
    public LuDecomposition(Matrix a)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (!a.IsSquare) throw new ArgumentException("LU decomposition requires a square matrix.");
        _n = a.Rows;
        int n = _n;
        _lu = (double[])a.Data.Clone();
        _piv = new int[n];
        for (int i = 0; i < n; i++) _piv[i] = i;
        _pivSign = 1;
        double maxAbs = a.MaxAbs();
        for (int k = 0; k < n; k++)
        {
            int p = k;
            double best = Math.Abs(_lu[k * n + k]);
            for (int i = k + 1; i < n; i++)
            {
                double v = Math.Abs(_lu[i * n + k]);
                if (v > best) { best = v; p = i; }
            }
            if (p != k)
            {
                for (int j = 0; j < n; j++) (_lu[p * n + j], _lu[k * n + j]) = (_lu[k * n + j], _lu[p * n + j]);
                (_piv[p], _piv[k]) = (_piv[k], _piv[p]);
                _pivSign = -_pivSign;
            }
            double pivot = _lu[k * n + k];
            if (pivot != 0)
            {
                for (int i = k + 1; i < n; i++)
                {
                    double l = _lu[i * n + k] /= pivot;
                    if (l == 0) continue;
                    for (int j = k + 1; j < n; j++) _lu[i * n + j] -= l * _lu[k * n + j];
                }
            }
        }
        double threshold = n * NumericsCore.Eps * maxAbs;
        bool singular = !(maxAbs > 0) && n > 0;
        for (int k = 0; k < n && !singular; k++)
            if (!(Math.Abs(_lu[k * n + k]) > threshold)) singular = true;
        IsSingular = singular;
    }

    /// <summary>True if the matrix is singular to working precision.</summary>
    public bool IsSingular { get; }

    /// <summary>Row permutation: row i of P A is row Pivot[i] of A.</summary>
    public int[] Pivot => (int[])_piv.Clone();

    /// <summary>Unit lower triangular factor.</summary>
    public Matrix L
    {
        get
        {
            var l = new Matrix(_n, _n);
            for (int i = 0; i < _n; i++)
            {
                l[i, i] = 1;
                for (int j = 0; j < i; j++) l[i, j] = _lu[i * _n + j];
            }
            return l;
        }
    }

    /// <summary>Upper triangular factor.</summary>
    public Matrix U
    {
        get
        {
            var u = new Matrix(_n, _n);
            for (int i = 0; i < _n; i++)
                for (int j = i; j < _n; j++) u[i, j] = _lu[i * _n + j];
            return u;
        }
    }

    /// <summary>Permutation matrix P with P A = L U.</summary>
    public Matrix P
    {
        get
        {
            var p = new Matrix(_n, _n);
            for (int i = 0; i < _n; i++) p[i, _piv[i]] = 1;
            return p;
        }
    }

    /// <summary>Determinant (0 when the matrix is flagged singular).</summary>
    public double Determinant
    {
        get
        {
            if (IsSingular) return 0;
            double d = _pivSign;
            for (int i = 0; i < _n; i++) d *= _lu[i * _n + i];
            return d;
        }
    }

    /// <summary>Solves A x = b.</summary>
    /// <exception cref="InvalidOperationException">The matrix is singular.</exception>
    public double[] Solve(double[] b)
    {
        if (b.Length != _n) throw new ArgumentException("Right-hand side length does not match the matrix.");
        if (IsSingular) throw new InvalidOperationException("The matrix is singular; the system has no unique solution.");
        int n = _n;
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = b[_piv[i]];
        for (int i = 0; i < n; i++)
        {
            double s = x[i];
            for (int j = 0; j < i; j++) s -= _lu[i * n + j] * x[j];
            x[i] = s;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double s = x[i];
            for (int j = i + 1; j < n; j++) s -= _lu[i * n + j] * x[j];
            x[i] = s / _lu[i * n + i];
        }
        return x;
    }

    /// <summary>Solves A X = B column by column.</summary>
    public Matrix Solve(Matrix b)
    {
        if (b.Rows != _n) throw new ArgumentException("Right-hand side rows do not match the matrix.");
        var x = new Matrix(_n, b.Columns);
        for (int j = 0; j < b.Columns; j++) x.SetColumn(j, Solve(b.GetColumn(j)));
        return x;
    }

    /// <summary>Inverse matrix.</summary>
    public Matrix Inverse() => Solve(Matrix.Identity(_n));
}

/// <summary>
/// Householder QR decomposition A = Q R of an m by n matrix. Q is m by k and R is k by n with
/// k = min(m, n).
/// </summary>
public sealed class QrDecomposition
{
    private readonly double[] _qr;
    private readonly double[] _rdiag;
    private readonly int _m, _n;

    /// <summary>Factors the matrix.</summary>
    public QrDecomposition(Matrix a)
    {
        ArgumentNullException.ThrowIfNull(a);
        _m = a.Rows;
        _n = a.Columns;
        int m = _m, n = _n, k = Math.Min(m, n);
        _qr = (double[])a.Data.Clone();
        _rdiag = new double[k];
        for (int c = 0; c < k; c++)
        {
            double nrm = 0;
            for (int i = c; i < m; i++) nrm = Complex.Hypot(nrm, _qr[i * n + c]);
            if (nrm != 0)
            {
                if (_qr[c * n + c] < 0) nrm = -nrm;
                for (int i = c; i < m; i++) _qr[i * n + c] /= nrm;
                _qr[c * n + c] += 1;
                for (int j = c + 1; j < n; j++)
                {
                    double s = 0;
                    for (int i = c; i < m; i++) s += _qr[i * n + c] * _qr[i * n + j];
                    s = -s / _qr[c * n + c];
                    for (int i = c; i < m; i++) _qr[i * n + j] += s * _qr[i * n + c];
                }
            }
            _rdiag[c] = -nrm;
        }
    }

    /// <summary>True if R has no negligible diagonal entry (requires rows &gt;= columns).</summary>
    public bool IsFullRank
    {
        get
        {
            if (_m < _n) return false;
            double maxd = 0;
            foreach (double d in _rdiag) maxd = Math.Max(maxd, Math.Abs(d));
            double tol = Math.Max(_m, _n) * NumericsCore.Eps * maxd;
            foreach (double d in _rdiag) if (!(Math.Abs(d) > tol)) return false;
            return true;
        }
    }

    /// <summary>Orthonormal factor (m by min(m, n)).</summary>
    public Matrix Q
    {
        get
        {
            int m = _m, n = _n, k = Math.Min(m, n);
            var q = new Matrix(m, k);
            for (int c = k - 1; c >= 0; c--)
            {
                q[c, c] = 1;
                for (int j = c; j < k; j++)
                {
                    if (_qr[c * n + c] == 0) continue;
                    double s = 0;
                    for (int i = c; i < m; i++) s += _qr[i * n + c] * q[i, j];
                    s = -s / _qr[c * n + c];
                    for (int i = c; i < m; i++) q[i, j] += s * _qr[i * n + c];
                }
            }
            return q;
        }
    }

    /// <summary>Upper triangular factor (min(m, n) by n).</summary>
    public Matrix R
    {
        get
        {
            int k = Math.Min(_m, _n);
            var r = new Matrix(k, _n);
            for (int i = 0; i < k; i++)
                for (int j = i; j < _n; j++)
                    r[i, j] = i == j ? _rdiag[i] : _qr[i * _n + j];
            return r;
        }
    }

    /// <summary>Least-squares solution of A x = b (requires rows &gt;= columns and full rank).</summary>
    /// <exception cref="InvalidOperationException">The matrix is rank deficient.</exception>
    public double[] Solve(double[] b)
    {
        if (b.Length != _m) throw new ArgumentException("Right-hand side length does not match the matrix.");
        if (!IsFullRank) throw new InvalidOperationException("The matrix is rank deficient; use the SVD (minimum-norm) solve instead.");
        int m = _m, n = _n;
        var y = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            double s = 0;
            for (int i = c; i < m; i++) s += _qr[i * n + c] * y[i];
            s = -s / _qr[c * n + c];
            for (int i = c; i < m; i++) y[i] += s * _qr[i * n + c];
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int j = i + 1; j < n; j++) s -= _qr[i * n + j] * x[j];
            x[i] = s / _rdiag[i];
        }
        return x;
    }
}

/// <summary>Cholesky decomposition A = L L^T of a symmetric positive definite matrix.</summary>
public sealed class CholeskyDecomposition
{
    private readonly double[] _l;
    private readonly int _n;

    /// <summary>Factors the matrix.</summary>
    /// <exception cref="InvalidOperationException">The matrix is not symmetric positive definite.</exception>
    public CholeskyDecomposition(Matrix a)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (!a.IsSquare) throw new ArgumentException("Cholesky decomposition requires a square matrix.");
        if (!a.IsSymmetric(1e-12)) throw new InvalidOperationException("The matrix is not symmetric.");
        _n = a.Rows;
        int n = _n;
        _l = new double[n * n];
        var d = a.Data;
        for (int j = 0; j < n; j++)
        {
            double s = d[j * n + j];
            for (int k = 0; k < j; k++) s -= _l[j * n + k] * _l[j * n + k];
            if (!(s > 0)) throw new InvalidOperationException("The matrix is not positive definite.");
            double ljj = Math.Sqrt(s);
            _l[j * n + j] = ljj;
            for (int i = j + 1; i < n; i++)
            {
                double t = d[i * n + j];
                for (int k = 0; k < j; k++) t -= _l[i * n + k] * _l[j * n + k];
                _l[i * n + j] = t / ljj;
            }
        }
    }

    /// <summary>Lower triangular factor L.</summary>
    public Matrix L => Matrix.Wrap(_n, _n, (double[])_l.Clone());

    /// <summary>Determinant of A (product of squared diagonal of L).</summary>
    public double Determinant
    {
        get
        {
            double p = 1;
            for (int i = 0; i < _n; i++) p *= _l[i * _n + i] * _l[i * _n + i];
            return p;
        }
    }

    /// <summary>Solves A x = b.</summary>
    public double[] Solve(double[] b)
    {
        if (b.Length != _n) throw new ArgumentException("Right-hand side length does not match the matrix.");
        int n = _n;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= _l[i * n + k] * y[k];
            y[i] = s / _l[i * n + i];
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int k = i + 1; k < n; k++) s -= _l[k * n + i] * x[k];
            x[i] = s / _l[i * n + i];
        }
        return x;
    }

    /// <summary>Inverse of A.</summary>
    public Matrix Inverse()
    {
        var inv = new Matrix(_n, _n);
        for (int j = 0; j < _n; j++)
        {
            var e = new double[_n];
            e[j] = 1;
            inv.SetColumn(j, Solve(e));
        }
        return inv;
    }
}

/// <summary>
/// Thin singular value decomposition A = U diag(S) V^T computed by one-sided Jacobi rotations
/// (high relative accuracy). U is m by k, S has k entries in descending order and V is n by k,
/// with k = min(m, n).
/// </summary>
public sealed class SvdDecomposition
{
    private readonly int _m, _n;

    /// <summary>Left singular vectors (columns).</summary>
    public Matrix U { get; }

    /// <summary>Singular values in descending order.</summary>
    public double[] S { get; }

    /// <summary>Right singular vectors (columns).</summary>
    public Matrix V { get; }

    /// <summary>Computes the decomposition.</summary>
    /// <exception cref="ArgumentException">The matrix contains NaN or infinite values.</exception>
    /// <exception cref="ConvergenceException">The Jacobi sweeps did not converge.</exception>
    public SvdDecomposition(Matrix a)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (!a.IsFinite()) throw new ArgumentException("The matrix contains NaN or infinite values.");
        _m = a.Rows;
        _n = a.Columns;
        bool transposed = _m < _n;
        Matrix w = transposed ? a.Transpose() : a;
        int M = w.Rows, N = w.Columns;
        var (u, s, v) = JacobiSvd(w, M, N);
        if (transposed)
        {
            U = v; S = s; V = u;
        }
        else
        {
            U = u; S = s; V = v;
        }
    }

    private static (Matrix U, double[] S, Matrix V) JacobiSvd(Matrix w, int M, int N)
    {
        double scale = w.MaxAbs();
        var cols = new double[N][];
        var vcols = new double[N][];
        for (int j = 0; j < N; j++)
        {
            cols[j] = new double[M];
            for (int i = 0; i < M; i++) cols[j][i] = scale > 0 ? w[i, j] / scale : 0;
            vcols[j] = new double[N];
            vcols[j][j] = 1;
        }
        double tol = NumericsCore.Eps * Math.Max(M, 4);
        // Columns whose energy is below eps^2 of the total are rounding noise (rank deficiency):
        // rotating them cannot improve orthogonality, so they are left alone.
        double fro2 = 0;
        foreach (var col in cols) foreach (double v in col) fro2 += v * v;
        double negligible = NumericsCore.Eps * NumericsCore.Eps * fro2;
        bool converged = scale == 0;
        for (int sweep = 0; sweep < 80 && !converged; sweep++)
        {
            bool rotated = false;
            for (int p = 0; p < N - 1; p++)
            {
                for (int q = p + 1; q < N; q++)
                {
                    double[] cp = cols[p], cq = cols[q];
                    double alpha = 0, beta = 0, gamma = 0;
                    for (int i = 0; i < M; i++)
                    {
                        alpha += cp[i] * cp[i];
                        beta += cq[i] * cq[i];
                        gamma += cp[i] * cq[i];
                    }
                    if (gamma == 0 || alpha <= negligible || beta <= negligible || Math.Abs(gamma) <= tol * Math.Sqrt(alpha * beta)) continue;
                    rotated = true;
                    double zeta = (beta - alpha) / (2 * gamma);
                    double t = Math.Abs(zeta) > 1e150 ? 0.5 / zeta : (zeta >= 0 ? 1 : -1) / (Math.Abs(zeta) + Math.Sqrt(1 + zeta * zeta));
                    double c = 1 / Math.Sqrt(1 + t * t), s = c * t;
                    for (int i = 0; i < M; i++)
                    {
                        double up = cp[i], uq = cq[i];
                        cp[i] = c * up - s * uq;
                        cq[i] = s * up + c * uq;
                    }
                    double[] vp = vcols[p], vq = vcols[q];
                    for (int i = 0; i < N; i++)
                    {
                        double up = vp[i], uq = vq[i];
                        vp[i] = c * up - s * uq;
                        vq[i] = s * up + c * uq;
                    }
                }
            }
            if (!rotated) converged = true;
        }
        if (!converged) throw new ConvergenceException("The SVD (one-sided Jacobi) did not converge.");

        var sigma = new double[N];
        for (int j = 0; j < N; j++) sigma[j] = NumericsCore.Norm2(cols[j]);
        var order = new int[N];
        for (int j = 0; j < N; j++) order[j] = j;
        Array.Sort(order, (p, q) => sigma[q].CompareTo(sigma[p]));

        var U = new Matrix(M, N);
        var V = new Matrix(N, N);
        var S = new double[N];
        double sMax = sigma.Length > 0 ? sigma[order[0]] : 0;
        var zeroCols = new List<int>();
        for (int k = 0; k < N; k++)
        {
            int j = order[k];
            S[k] = sigma[j] * scale;
            for (int i = 0; i < N; i++) V[i, k] = vcols[j][i];
            if (sigma[j] > 1e-300 && sigma[j] > sMax * 1e-300)
            {
                for (int i = 0; i < M; i++) U[i, k] = cols[j][i] / sigma[j];
            }
            else
            {
                zeroCols.Add(k);
            }
        }
        // Complete U with orthonormal columns where the singular value is zero.
        foreach (int k in zeroCols)
        {
            for (int e = 0; e < M; e++)
            {
                var v = new double[M];
                v[e] = 1;
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int j = 0; j < N; j++)
                    {
                        if (j == k || (zeroCols.Contains(j) && j > k)) continue;
                        double d = 0;
                        for (int i = 0; i < M; i++) d += U[i, j] * v[i];
                        for (int i = 0; i < M; i++) v[i] -= d * U[i, j];
                    }
                }
                double nv = NumericsCore.Norm2(v);
                if (nv > 0.5)
                {
                    for (int i = 0; i < M; i++) U[i, k] = v[i] / nv;
                    break;
                }
            }
        }
        return (U, S, V);
    }

    /// <summary>Default rank tolerance max(m, n) * eps * S[0].</summary>
    public double DefaultTolerance => S.Length == 0 ? 0 : Math.Max(_m, _n) * NumericsCore.Eps * S[0];

    /// <summary>Number of singular values above the tolerance.</summary>
    public int Rank(double? tolerance = null)
    {
        double tol = tolerance ?? DefaultTolerance;
        int r = 0;
        foreach (double s in S) if (s > tol) r++;
        return r;
    }

    /// <summary>Largest singular value (the spectral norm).</summary>
    public double Norm2 => S.Length == 0 ? 0 : S[0];

    /// <summary>2-norm condition number S[0] / S[k-1] (Infinity if singular).</summary>
    public double ConditionNumber
    {
        get
        {
            if (S.Length == 0) return 0;
            double smin = S[^1];
            return smin == 0 ? double.PositiveInfinity : S[0] / smin;
        }
    }

    /// <summary>Minimum-norm least-squares solution of A x = b.</summary>
    public double[] Solve(double[] b, double? tolerance = null)
    {
        if (b.Length != _m) throw new ArgumentException("Right-hand side length does not match the matrix.");
        double tol = tolerance ?? DefaultTolerance;
        int k = S.Length;
        var x = new double[_n];
        for (int j = 0; j < k; j++)
        {
            if (!(S[j] > tol)) continue;
            double d = 0;
            for (int i = 0; i < _m; i++) d += U[i, j] * b[i];
            d /= S[j];
            for (int i = 0; i < _n; i++) x[i] += d * V[i, j];
        }
        return x;
    }

    /// <summary>Moore-Penrose pseudo-inverse V diag(1/S) U^T (singular values below tolerance dropped).</summary>
    public Matrix PseudoInverse(double? tolerance = null)
    {
        double tol = tolerance ?? DefaultTolerance;
        var p = new Matrix(_n, _m);
        for (int j = 0; j < S.Length; j++)
        {
            if (!(S[j] > tol)) continue;
            double inv = 1 / S[j];
            for (int r = 0; r < _n; r++)
            {
                double vr = V[r, j] * inv;
                if (vr == 0) continue;
                for (int c = 0; c < _m; c++) p[r, c] += vr * U[c, j];
            }
        }
        return p;
    }
}
