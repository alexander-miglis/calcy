using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>Result of a nonlinear least squares fit.</summary>
public sealed record NonlinearFitResult
{
    /// <summary>Fitted parameter values.</summary>
    public required IReadOnlyList<double> Parameters { get; init; }
    /// <summary>Asymptotic standard errors sqrt(diag(s^2 (J^T W J)^-1)); NaN when not determined.</summary>
    public required IReadOnlyList<double> StandardErrors { get; init; }
    /// <summary>Parameter covariance matrix s^2 (J^T W J)^-1.</summary>
    public required double[,] Covariance { get; init; }
    /// <summary>Weighted residual sum of squares.</summary>
    public required double ResidualSumOfSquares { get; init; }
    /// <summary>Residual standard error s = sqrt(RSS / (n - p)).</summary>
    public required double StandardError { get; init; }
    /// <summary>Residual degrees of freedom n - p.</summary>
    public required double DegreesOfFreedom { get; init; }
    /// <summary>1 - RSS / SST (weighted).</summary>
    public required double RSquared { get; init; }
    /// <summary>Residuals y - f(x).</summary>
    public required IReadOnlyList<double> Residuals { get; init; }
    /// <summary>Number of Levenberg-Marquardt iterations performed.</summary>
    public required int Iterations { get; init; }
    /// <summary>True when a convergence criterion was met before the iteration limit.</summary>
    public required bool Converged { get; init; }
    /// <summary>The fitted model as a function of x.</summary>
    public required Func<double, double> Predict { get; init; }
}

/// <summary>General nonlinear least squares.</summary>
public static class NonlinearFit
{
    /// <summary>
    /// Fits y = model(parameters, x) by Levenberg-Marquardt with Marquardt scaling and Nielsen's damping update.
    /// The Jacobian is taken from <paramref name="gradient"/> (d model / d parameters at x) when supplied, otherwise
    /// by central differences. Weights, when given, multiply the squared residuals.
    /// </summary>
    public static NonlinearFitResult LevenbergMarquardt(
        Func<double[], double, double> model,
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        IReadOnlyList<double> initialGuess,
        IReadOnlyList<double>? weights = null,
        int maxIterations = 500,
        double tolerance = 1e-12,
        Func<double[], double, double[]>? gradient = null)
    {
        Guard.NotNull(model, nameof(model));
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.AllFinite(initialGuess, nameof(initialGuess));
        Guard.SameLength(x.Count, y.Count, "x and y");
        if (weights is not null) Descriptive.CheckFrequencies(weights, x.Count);
        int m = initialGuess.Count;
        if (m == 0) throw new ArgumentException("at least one parameter is required", nameof(initialGuess));
        if (x.Count < m) throw new ArgumentException($"at least {m} data points are required to fit {m} parameters", nameof(x));
        if (maxIterations < 1) throw new ArgumentException("the iteration limit must be positive", nameof(maxIterations));
        return Core(model, x, y, initialGuess.ToArray(), weights, maxIterations, tolerance, gradient);
    }

    internal static NonlinearFitResult Core(
        Func<double[], double, double> model,
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        double[] p,
        IReadOnlyList<double>? weights,
        int maxIterations,
        double tolerance,
        Func<double[], double, double[]>? gradient)
    {
        int n = x.Count, m = p.Length;
        var sw = new double[n];
        for (int i = 0; i < n; i++) sw[i] = Math.Sqrt(weights is null ? 1.0 : weights[i]);

        double Rss(double[] par, double[] res)
        {
            double s = 0;
            for (int i = 0; i < n; i++)
            {
                double r = sw[i] * (y[i] - model(par, x[i]));
                res[i] = r;
                s += r * r;
            }
            return s;
        }

        var r = new double[n];
        double S = Rss(p, r);
        if (!double.IsFinite(S)) throw new ArgumentException("the model cannot be evaluated at the initial guess");

        var jac = new double[n, m];
        void Jacobian(double[] par)
        {
            var tmp = (double[])par.Clone();
            for (int i = 0; i < n; i++)
            {
                if (gradient is not null)
                {
                    var gr = gradient(par, x[i]);
                    for (int j = 0; j < m; j++) jac[i, j] = sw[i] * gr[j];
                    continue;
                }
                for (int j = 0; j < m; j++)
                {
                    double h = 6.0554544523933395e-6 * Math.Max(Math.Abs(par[j]), 1e-3);
                    tmp[j] = par[j] + h;
                    double fp = model(tmp, x[i]);
                    tmp[j] = par[j] - h;
                    double fm = model(tmp, x[i]);
                    tmp[j] = par[j];
                    jac[i, j] = sw[i] * (fp - fm) / (2 * h);
                }
            }
        }

        var A = new double[m, m];
        var g = new double[m];
        void Normal()
        {
            for (int a = 0; a < m; a++)
            {
                double gs = 0;
                for (int i = 0; i < n; i++) gs += jac[i, a] * r[i];
                g[a] = gs;
                for (int b = a; b < m; b++)
                {
                    double s = 0;
                    for (int i = 0; i < n; i++) s += jac[i, a] * jac[i, b];
                    A[a, b] = s;
                    A[b, a] = s;
                }
            }
        }

        Jacobian(p);
        Normal();
        double lambda = 1e-3, nu = 2;
        bool converged = false;
        int iter = 0;
        var rNew = new double[n];
        var M = new double[m, m];
        var d = new double[m];
        double yScale = 0;
        for (int i = 0; i < n; i++) yScale += sw[i] * sw[i] * y[i] * y[i];

        for (iter = 0; iter < maxIterations; iter++)
        {
            if (S <= 1e-30 * Math.Max(yScale, 1e-300))
            {
                converged = true;
                break;
            }
            // Orthogonality test (MINPACK gtol): the residual is orthogonal to every Jacobian column.
            double cosMax = 0;
            for (int j = 0; j < m; j++)
            {
                double denom = Math.Sqrt(A[j, j] * S);
                if (denom > 0) cosMax = Math.Max(cosMax, Math.Abs(g[j]) / denom);
            }
            if (cosMax <= 1e-14)
            {
                converged = true;
                break;
            }
            for (int a = 0; a < m; a++)
            {
                d[a] = Math.Max(A[a, a], 1e-30);
                for (int b = 0; b < m; b++) M[a, b] = A[a, b];
                M[a, a] += lambda * d[a];
            }
            if (!LinearAlgebra.TryCholeskySolve(M, g, out var delta))
            {
                lambda *= nu;
                nu *= 2;
                if (lambda > 1e20) break;
                continue;
            }
            double stepNorm = 0, pNorm = 0;
            for (int j = 0; j < m; j++)
            {
                stepNorm += delta[j] * delta[j];
                pNorm += p[j] * p[j];
            }
            stepNorm = Math.Sqrt(stepNorm);
            pNorm = Math.Sqrt(pNorm);
            if (stepNorm <= tolerance * (pNorm + tolerance))
            {
                converged = true;
                break;
            }
            var pNew = new double[m];
            for (int j = 0; j < m; j++) pNew[j] = p[j] + delta[j];
            double SNew = Rss(pNew, rNew);
            double predicted = 0;
            for (int j = 0; j < m; j++) predicted += delta[j] * (lambda * d[j] * delta[j] + g[j]);
            double rho = predicted > 0 ? (S - SNew) / predicted : -1;
            if (double.IsFinite(SNew) && SNew < S && rho > 0)
            {
                double reduction = (S - SNew) / S;
                Array.Copy(pNew, p, m);
                Array.Copy(rNew, r, n);
                S = SNew;
                Jacobian(p);
                Normal();
                double t = 2 * rho - 1;
                lambda *= Math.Max(1.0 / 3, 1 - t * t * t);
                nu = 2;
                if (reduction < 1e-15)
                {
                    converged = true;
                    iter++;
                    break;
                }
            }
            else
            {
                lambda *= nu;
                nu *= 2;
                if (lambda > 1e20)
                {
                    converged = stepNorm <= 1e-8 * (pNorm + 1e-8);
                    break;
                }
            }
        }

        // Covariance from the QR factorization of the (weighted) Jacobian at the solution.
        Jacobian(p);
        double dof = (weights is null ? n : Num.Sum(weights)) - m;
        double s2 = dof > 0 ? S / dof : double.NaN;
        var cov = new double[m, m];
        var se = new double[m];
        var ls = LinearAlgebra.LeastSquares(jac, new double[n]);
        if (ls.FullRank)
        {
            var uc = ls.UnscaledCovariance();
            for (int a = 0; a < m; a++)
                for (int b = 0; b < m; b++) cov[a, b] = uc[a, b] * s2;
            for (int a = 0; a < m; a++) se[a] = Math.Sqrt(cov[a, a]);
        }
        else
        {
            for (int a = 0; a < m; a++)
            {
                se[a] = double.NaN;
                for (int b = 0; b < m; b++) cov[a, b] = double.NaN;
            }
        }

        var residuals = new double[n];
        double wsum = 0, wy = 0;
        for (int i = 0; i < n; i++)
        {
            double w = weights is null ? 1.0 : weights[i];
            wsum += w;
            wy += w * y[i];
        }
        double ybar = wy / wsum, sst = 0;
        for (int i = 0; i < n; i++)
        {
            double w = weights is null ? 1.0 : weights[i];
            residuals[i] = y[i] - model(p, x[i]);
            sst += w * (y[i] - ybar) * (y[i] - ybar);
        }
        var final = (double[])p.Clone();
        return new NonlinearFitResult
        {
            Parameters = final,
            StandardErrors = se,
            Covariance = cov,
            ResidualSumOfSquares = S,
            StandardError = Math.Sqrt(s2),
            DegreesOfFreedom = dof,
            RSquared = sst > 0 ? 1 - S / sst : double.NaN,
            Residuals = residuals,
            Iterations = iter,
            Converged = converged,
            Predict = xv => model(final, xv),
        };
    }
}
