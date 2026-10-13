namespace Calcy.Statistics.Internal;

/// <summary>Result of a dense linear least squares solve.</summary>
internal sealed class LeastSquaresSolution
{
    public required double[] Coefficients { get; init; }
    public required double[,] R { get; init; }
    public required bool FullRank { get; init; }

    /// <summary>(A^T W A)^-1, the unscaled covariance of the coefficients.</summary>
    public double[,] UnscaledCovariance()
    {
        int p = Coefficients.Length;
        var rinv = LinearAlgebra.InvertUpperTriangular(R);
        var cov = new double[p, p];
        for (int i = 0; i < p; i++)
        {
            for (int j = i; j < p; j++)
            {
                double s = 0;
                for (int k = Math.Max(i, j); k < p; k++) s += rinv[i, k] * rinv[j, k];
                cov[i, j] = s;
                cov[j, i] = s;
            }
        }
        return cov;
    }
}

/// <summary>Small dense linear algebra (Householder QR, Cholesky) implemented without external libraries.</summary>
internal static class LinearAlgebra
{
    /// <summary>
    /// Solves min || sqrt(W) (A c - y) || by Householder QR. A is given row major as a[i, j] (n rows, p columns).
    /// </summary>
    public static LeastSquaresSolution LeastSquares(double[,] a, double[] y, double[]? weights = null)
    {
        int n = a.GetLength(0), p = a.GetLength(1);
        if (n < p) throw new ArgumentException($"at least {p} data points are required");
        var m = (double[,])a.Clone();
        var b = (double[])y.Clone();
        if (weights is not null)
        {
            for (int i = 0; i < n; i++)
            {
                double sw = Math.Sqrt(weights[i]);
                for (int j = 0; j < p; j++) m[i, j] *= sw;
                b[i] *= sw;
            }
        }

        var colNorm = new double[p];
        for (int j = 0; j < p; j++) colNorm[j] = ColumnNorm(m, j, 0, n);

        bool fullRank = true;
        var v = new double[n];
        for (int k = 0; k < p; k++)
        {
            double norm = ColumnNorm(m, k, k, n);
            if (norm == 0 || norm <= 1e-13 * colNorm[k])
            {
                fullRank = false;
                continue;
            }
            double alpha = m[k, k] > 0 ? -norm : norm;
            double vnorm2 = 0;
            for (int i = k; i < n; i++)
            {
                v[i] = m[i, k];
                if (i == k) v[i] -= alpha;
                vnorm2 += v[i] * v[i];
            }
            if (vnorm2 == 0) continue;
            for (int j = k + 1; j < p; j++)
            {
                double s = 0;
                for (int i = k; i < n; i++) s += v[i] * m[i, j];
                double f = 2.0 * s / vnorm2;
                for (int i = k; i < n; i++) m[i, j] -= f * v[i];
            }
            {
                double s = 0;
                for (int i = k; i < n; i++) s += v[i] * b[i];
                double f = 2.0 * s / vnorm2;
                for (int i = k; i < n; i++) b[i] -= f * v[i];
            }
            m[k, k] = alpha;
            for (int i = k + 1; i < n; i++) m[i, k] = 0;
        }

        var r = new double[p, p];
        for (int i = 0; i < p; i++)
            for (int j = i; j < p; j++) r[i, j] = m[i, j];

        var c = new double[p];
        if (fullRank)
        {
            for (int k = p - 1; k >= 0; k--)
            {
                double s = b[k];
                for (int j = k + 1; j < p; j++) s -= r[k, j] * c[j];
                c[k] = s / r[k, k];
            }
        }
        else
        {
            for (int k = 0; k < p; k++) c[k] = double.NaN;
        }
        return new LeastSquaresSolution { Coefficients = c, R = r, FullRank = fullRank };
    }

    private static double ColumnNorm(double[,] m, int col, int from, int to)
    {
        double scale = 0;
        for (int i = from; i < to; i++) scale = Math.Max(scale, Math.Abs(m[i, col]));
        if (scale == 0 || !double.IsFinite(scale)) return scale;
        double s = 0;
        for (int i = from; i < to; i++)
        {
            double t = m[i, col] / scale;
            s += t * t;
        }
        return scale * Math.Sqrt(s);
    }

    /// <summary>Inverse of an upper triangular matrix.</summary>
    public static double[,] InvertUpperTriangular(double[,] r)
    {
        int p = r.GetLength(0);
        var inv = new double[p, p];
        for (int j = 0; j < p; j++)
        {
            inv[j, j] = 1.0 / r[j, j];
            for (int i = j - 1; i >= 0; i--)
            {
                double s = 0;
                for (int k = i + 1; k <= j; k++) s += r[i, k] * inv[k, j];
                inv[i, j] = -s / r[i, i];
            }
        }
        return inv;
    }

    /// <summary>Solves a symmetric positive definite system by Cholesky. Returns false if the matrix is not positive definite.</summary>
    public static bool TryCholeskySolve(double[,] a, double[] rhs, out double[] x)
    {
        int n = rhs.Length;
        var l = new double[n, n];
        x = new double[n];
        for (int j = 0; j < n; j++)
        {
            double d = a[j, j];
            for (int k = 0; k < j; k++) d -= l[j, k] * l[j, k];
            if (!(d > 0) || !double.IsFinite(d)) return false;
            double ljj = Math.Sqrt(d);
            l[j, j] = ljj;
            for (int i = j + 1; i < n; i++)
            {
                double s = a[i, j];
                for (int k = 0; k < j; k++) s -= l[i, k] * l[j, k];
                l[i, j] = s / ljj;
            }
        }
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = rhs[i];
            for (int k = 0; k < i; k++) s -= l[i, k] * z[k];
            z[i] = s / l[i, i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double s = z[i];
            for (int k = i + 1; k < n; k++) s -= l[k, i] * x[k];
            x[i] = s / l[i, i];
        }
        return true;
    }
}
