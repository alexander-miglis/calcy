namespace Calcy.Numerics;

/// <summary>A numerical derivative with an error estimate.</summary>
/// <param name="Value">Derivative estimate.</param>
/// <param name="ErrorEstimate">Estimated absolute error.</param>
public sealed record DerivativeResult(double Value, double ErrorEstimate);

/// <summary>
/// Numerical differentiation by Ridders' method: central-difference formulas evaluated for a
/// decreasing sequence of steps and extrapolated to zero step (Neville tableau), with an
/// error estimate. Several starting steps are tried and the most accurate result is kept.
/// </summary>
public static class Differentiation
{
    private const int TableSize = 12;
    private const double Con = 1.4, Con2 = Con * Con, Safe = 2.0;

    /// <summary>Derivative of order 1 to 4 of f at x.</summary>
    /// <param name="f">Function to differentiate.</param>
    /// <param name="x">Point.</param>
    /// <param name="order">Derivative order (1 to 4).</param>
    /// <param name="initialStep">Optional starting step; by default several are tried.</param>
    public static DerivativeResult Derivative(Func<double, double> f, double x, int order = 1, double? initialStep = null)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (order < 1 || order > 4) throw new ArgumentOutOfRangeException(nameof(order), "Derivative order must be between 1 and 4.");
        if (!double.IsFinite(x)) throw new ArgumentException("The point must be finite.");
        double fx = f(x);
        Func<double, double> estimate = h => CentralDifference(f, x, fx, order, h);

        double s = Math.Max(1.0, Math.Abs(x));
        double[] candidates = initialStep is double h0
            ? [h0]
            : [0.5 * s, 0.1 * s, 0.02 * s, 0.1, 0.01, 0.001];
        double minStep = 1e3 * NumericsCore.Eps * Math.Max(Math.Abs(x), 1e-300);
        DerivativeResult best = new(double.NaN, double.PositiveInfinity);
        foreach (double c in candidates.Distinct())
        {
            double h = c;
            // Shrink the starting step until all stencil points are defined.
            int shrink = 0;
            while (shrink < 30 && !double.IsFinite(estimate(h))) { h *= 0.25; shrink++; }
            if (h <= minStep || !double.IsFinite(estimate(h))) continue;
            var r = Ridders(estimate, h, minStep);
            if (double.IsFinite(r.Value) && r.ErrorEstimate < best.ErrorEstimate) best = r;
            if (best.ErrorEstimate == 0) break;
        }
        return best;
    }

    /// <summary>Central-difference estimate of the given order with step h (error expands in even powers of h).</summary>
    internal static double CentralDifference(Func<double, double> f, double x, double fx, int order, double h)
    {
        // Use exactly representable step offsets.
        double xp1 = x + h, xm1 = x - h;
        double hh = 0.5 * (xp1 - xm1);
        switch (order)
        {
            case 1:
                return (f(xp1) - f(xm1)) / (2 * hh);
            case 2:
                return (f(xp1) - 2 * fx + f(xm1)) / (hh * hh);
            case 3:
            {
                double xp2 = x + 2 * hh, xm2 = x - 2 * hh;
                return (f(xp2) - 2 * f(xp1) + 2 * f(xm1) - f(xm2)) / (2 * hh * hh * hh);
            }
            default:
            {
                double xp2 = x + 2 * hh, xm2 = x - 2 * hh;
                return (f(xp2) - 4 * f(xp1) + 6 * fx - 4 * f(xm1) + f(xm2)) / (hh * hh * hh * hh);
            }
        }
    }

    internal static DerivativeResult Ridders(Func<double, double> estimate, double h0, double minStep)
    {
        var a = new double[TableSize, TableSize];
        double hh = h0;
        a[0, 0] = estimate(hh);
        double ans = a[0, 0], err = double.PositiveInfinity;
        if (!double.IsFinite(ans)) return new DerivativeResult(double.NaN, double.PositiveInfinity);
        for (int i = 1; i < TableSize; i++)
        {
            hh /= Con;
            if (hh <= minStep) break;
            a[0, i] = estimate(hh);
            if (!double.IsFinite(a[0, i])) break;
            double fac = Con2;
            for (int j = 1; j <= i; j++)
            {
                a[j, i] = (a[j - 1, i] * fac - a[j - 1, i - 1]) / (fac - 1);
                fac *= Con2;
                double errt = Math.Max(Math.Abs(a[j, i] - a[j - 1, i]), Math.Abs(a[j, i] - a[j - 1, i - 1]));
                if (errt <= err)
                {
                    err = errt;
                    ans = a[j, i];
                }
            }
            if (Math.Abs(a[i, i] - a[i - 1, i - 1]) >= Safe * err) break;
        }
        return new DerivativeResult(ans, err);
    }

    private static DerivativeResult RiddersVector(Func<double, double[]> estimate, double h0, double minStep, out double[] values)
    {
        int m = estimate(h0).Length;
        var a = new double[TableSize, TableSize][];
        double hh = h0;
        a[0, 0] = estimate(hh);
        values = (double[])a[0, 0].Clone();
        double err = double.PositiveInfinity;
        for (int i = 1; i < TableSize; i++)
        {
            hh /= Con;
            if (hh <= minStep) break;
            a[0, i] = estimate(hh);
            if (!NumericsCore.AllFinite(a[0, i])) break;
            double fac = Con2;
            for (int j = 1; j <= i; j++)
            {
                var row = new double[m];
                double errt = 0;
                for (int k = 0; k < m; k++)
                {
                    row[k] = (a[j - 1, i][k] * fac - a[j - 1, i - 1][k]) / (fac - 1);
                    errt = Math.Max(errt, Math.Max(Math.Abs(row[k] - a[j - 1, i][k]), Math.Abs(row[k] - a[j - 1, i - 1][k])));
                }
                a[j, i] = row;
                fac *= Con2;
                if (errt <= err)
                {
                    err = errt;
                    values = (double[])row.Clone();
                }
            }
            double diag = 0;
            for (int k = 0; k < m; k++) diag = Math.Max(diag, Math.Abs(a[i, i][k] - a[i - 1, i - 1][k]));
            if (diag >= Safe * err) break;
        }
        return new DerivativeResult(0, err);
    }

    /// <summary>Partial derivative of f with respect to coordinate <paramref name="index"/> at x.</summary>
    public static DerivativeResult PartialDerivative(Func<double[], double> f, double[] x, int index, int order = 1)
    {
        ArgumentNullException.ThrowIfNull(f);
        var p = NumericsCore.Copy(x);
        return Derivative(t =>
        {
            p[index] = t;
            double v = f(p);
            p[index] = x[index];
            return v;
        }, x[index], order);
    }

    /// <summary>Gradient of a scalar function of several variables.</summary>
    public static double[] Gradient(Func<double[], double> f, double[] x)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(x);
        var g = new double[x.Length];
        for (int i = 0; i < x.Length; i++) g[i] = PartialDerivative(f, x, i).Value;
        return g;
    }

    /// <summary>Jacobian matrix J[i, j] = dF_i / dx_j of a vector function.</summary>
    public static Matrix Jacobian(Func<double[], double[]> F, double[] x)
    {
        ArgumentNullException.ThrowIfNull(F);
        ArgumentNullException.ThrowIfNull(x);
        int n = x.Length;
        double[] f0 = F(x);
        int m = f0.Length;
        var J = new Matrix(m, n);
        for (int j = 0; j < n; j++)
        {
            var p = NumericsCore.Copy(x);
            double xj = x[j];
            Func<double, double[]> est = h =>
            {
                p[j] = xj + h; double hp = p[j] - xj; double[] fp = F(p);
                p[j] = xj - h; double hm = xj - p[j]; double[] fm = F(p);
                p[j] = xj;
                var d = new double[m];
                for (int i = 0; i < m; i++) d[i] = (fp[i] - fm[i]) / (hp + hm);
                return d;
            };
            double s = Math.Max(1.0, Math.Abs(xj));
            double minStep = 1e3 * NumericsCore.Eps * Math.Max(Math.Abs(xj), 1e-300);
            double[]? bestValues = null;
            double bestErr = double.PositiveInfinity;
            foreach (double h0 in new[] { 0.1 * s, 0.01 * s })
            {
                double h = h0;
                int shrink = 0;
                while (shrink < 30 && !NumericsCore.AllFinite(est(h))) { h *= 0.25; shrink++; }
                if (!NumericsCore.AllFinite(est(h))) continue;
                var r = RiddersVector(est, h, minStep, out var vals);
                if (r.ErrorEstimate < bestErr || bestValues == null) { bestErr = r.ErrorEstimate; bestValues = vals; }
            }
            bestValues ??= Enumerable.Repeat(double.NaN, m).ToArray();
            for (int i = 0; i < m; i++) J[i, j] = bestValues[i];
        }
        return J;
    }

    /// <summary>Hessian matrix of second partial derivatives (symmetric).</summary>
    public static Matrix Hessian(Func<double[], double> f, double[] x)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(x);
        int n = x.Length;
        var H = new Matrix(n, n);
        for (int i = 0; i < n; i++) H[i, i] = PartialDerivative(f, x, i, 2).Value;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var p = NumericsCore.Copy(x);
                int ii = i, jj = j;
                Func<double, double> est = h =>
                {
                    double F(double di, double dj)
                    {
                        p[ii] = x[ii] + di; p[jj] = x[jj] + dj;
                        double v = f(p);
                        p[ii] = x[ii]; p[jj] = x[jj];
                        return v;
                    }
                    return (F(h, h) - F(h, -h) - F(-h, h) + F(-h, -h)) / (4 * h * h);
                };
                double s = Math.Max(1.0, Math.Max(Math.Abs(x[i]), Math.Abs(x[j])));
                double minStep = 1e3 * NumericsCore.Eps * s;
                DerivativeResult best = new(double.NaN, double.PositiveInfinity);
                foreach (double h0 in new[] { 0.1 * s, 0.01 * s, 0.001 * s })
                {
                    double h = h0;
                    int shrink = 0;
                    while (shrink < 30 && !double.IsFinite(est(h))) { h *= 0.25; shrink++; }
                    if (!double.IsFinite(est(h))) continue;
                    var r = Ridders(est, h, minStep);
                    if (r.ErrorEstimate < best.ErrorEstimate) best = r;
                }
                H[i, j] = best.Value;
                H[j, i] = best.Value;
            }
        }
        return H;
    }
}
