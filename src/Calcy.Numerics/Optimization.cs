namespace Calcy.Numerics;

/// <summary>Result of a one-dimensional minimization.</summary>
/// <param name="X">Location of the minimum.</param>
/// <param name="Value">f(X).</param>
/// <param name="Iterations">Iterations performed.</param>
/// <param name="Converged">True if the tolerance was met.</param>
public sealed record MinimizationResult(double X, double Value, int Iterations, bool Converged);

/// <summary>Kind of a local extremum.</summary>
public enum ExtremumKind
{
    /// <summary>Local minimum.</summary>
    Minimum,

    /// <summary>Local maximum.</summary>
    Maximum,
}

/// <summary>A local extremum.</summary>
/// <param name="X">Location.</param>
/// <param name="Value">Function value.</param>
/// <param name="Kind">Minimum or maximum.</param>
public sealed record Extremum(double X, double Value, ExtremumKind Kind);

/// <summary>Result of a global extremum search on an interval.</summary>
/// <param name="X">Location of the global extremum (or of the singularity when unbounded).</param>
/// <param name="Value">Extreme value (Infinity with the appropriate sign when unbounded).</param>
/// <param name="AtEndpoint">True if the extremum is attained at an end of the interval.</param>
/// <param name="IsUnbounded">True if the function appears unbounded (a pole) in the interval.</param>
/// <param name="LocalExtrema">All interior local minima and maxima found, sorted by X.</param>
public sealed record GlobalExtremumResult(double X, double Value, bool AtEndpoint, bool IsUnbounded, IReadOnlyList<Extremum> LocalExtrema);

/// <summary>Result of a multidimensional minimization.</summary>
/// <param name="X">Location of the minimum.</param>
/// <param name="Value">f(X).</param>
/// <param name="Iterations">Iterations performed.</param>
/// <param name="Evaluations">Function evaluations performed.</param>
/// <param name="Converged">True if the convergence criteria were met.</param>
/// <param name="Message">Explanation when not converged.</param>
public sealed record MultiMinimizationResult(double[] X, double Value, int Iterations, int Evaluations, bool Converged, string? Message = null);

/// <summary>Result of a nonlinear least-squares fit.</summary>
/// <param name="Parameters">Fitted parameters.</param>
/// <param name="Residuals">Residual vector at the solution.</param>
/// <param name="SumOfSquares">Sum of squared residuals.</param>
/// <param name="Iterations">Iterations performed.</param>
/// <param name="Converged">True if a convergence criterion was met.</param>
/// <param name="Covariance">Estimated parameter covariance s^2 (J^T J)^-1, or null if unavailable.</param>
/// <param name="Message">Explanation when not converged.</param>
public sealed record LeastSquaresResult(double[] Parameters, double[] Residuals, double SumOfSquares, int Iterations, bool Converged, Matrix? Covariance, string? Message = null);

/// <summary>Minimization and maximization algorithms.</summary>
public static class Optimization
{
    private const double Eps = NumericsCore.Eps;
    private const double CGold = 0.3819660112501051;

    // ------------------------------------------------------------------ Brent 1D

    /// <summary>
    /// Brent's method (golden section with parabolic interpolation) for a local minimum on [a, b].
    /// NaN values are treated as +Infinity. The location is found to about
    /// relativeTolerance * |x| (values below sqrt(eps) rarely help for smooth functions).
    /// </summary>
    public static MinimizationResult BrentMinimize(Func<double, double> f, double a, double b, double relativeTolerance = 1e-10, int maxIterations = 500, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentException("The interval must be finite.");
        return BrentMinimizeCore(SafeF(f), a, b, relativeTolerance, 1e-15 * Math.Abs(b - a) + 1e-300, maxIterations, cancellationToken);
    }

    private static Func<double, double> SafeF(Func<double, double> f) => x =>
    {
        double v = f(x);
        return double.IsNaN(v) ? double.PositiveInfinity : v;
    };

    internal static MinimizationResult BrentMinimizeCore(Func<double, double> f, double a, double b, double tol, double absTol, int maxIterations, CancellationToken ct)
    {
        if (a > b) (a, b) = (b, a);
        double x = a + CGold * (b - a), w = x, v = x;
        double fx = f(x), fw = fx, fv = fx;
        double d = 0, e = 0;
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();
            double xm = 0.5 * (a + b);
            double tol1 = tol * Math.Abs(x) + absTol, tol2 = 2 * tol1;
            if (Math.Abs(x - xm) <= tol2 - 0.5 * (b - a))
                return new MinimizationResult(x, fx, iter, true);
            bool golden = true;
            if (Math.Abs(e) > tol1)
            {
                double r = (x - w) * (fx - fv);
                double q = (x - v) * (fx - fw);
                double p = (x - v) * q - (x - w) * r;
                q = 2 * (q - r);
                if (q > 0) p = -p;
                q = Math.Abs(q);
                double etemp = e;
                if (double.IsFinite(p) && double.IsFinite(q) && q != 0 &&
                    Math.Abs(p) < Math.Abs(0.5 * q * etemp) && p > q * (a - x) && p < q * (b - x))
                {
                    e = d;
                    d = p / q;
                    double u0 = x + d;
                    if (u0 - a < tol2 || b - u0 < tol2) d = Math.CopySign(tol1, xm - x);
                    golden = false;
                }
            }
            if (golden)
            {
                e = x >= xm ? a - x : b - x;
                d = CGold * e;
            }
            double u = Math.Abs(d) >= tol1 ? x + d : x + Math.CopySign(tol1, d);
            double fu = f(u);
            if (fu <= fx)
            {
                if (u >= x) a = x; else b = x;
                v = w; fv = fw;
                w = x; fw = fx;
                x = u; fx = fu;
            }
            else
            {
                if (u < x) a = u; else b = u;
                if (fu <= fw || w == x)
                {
                    v = w; fv = fw;
                    w = u; fw = fu;
                }
                else if (fu <= fv || v == x || v == w)
                {
                    v = u; fv = fu;
                }
            }
        }
        return new MinimizationResult(x, fx, maxIterations, false);
    }

    // ------------------------------------------------------------------ global 1D

    /// <summary>
    /// Global minimum of f on [a, b] (endpoints included) by dense sampling and Brent refinement
    /// of every sampled local extremum. Also returns all interior local extrema found. Poles are
    /// detected and reported through <see cref="GlobalExtremumResult.IsUnbounded"/>.
    /// </summary>
    public static GlobalExtremumResult FindGlobalMinimum(Func<double, double> f, double a, double b, int samples = 1000, CancellationToken cancellationToken = default)
        => FindGlobal(f, a, b, samples, false, cancellationToken);

    /// <summary>Global maximum of f on [a, b]; see <see cref="FindGlobalMinimum"/>.</summary>
    public static GlobalExtremumResult FindGlobalMaximum(Func<double, double> f, double a, double b, int samples = 1000, CancellationToken cancellationToken = default)
        => FindGlobal(f, a, b, samples, true, cancellationToken);

    private static GlobalExtremumResult FindGlobal(Func<double, double> f, double a, double b, int samples, bool maximize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentException("The interval must be finite.");
        if (a > b) (a, b) = (b, a);
        double sgn = maximize ? -1 : 1;
        int n = Math.Max(16, samples);
        double h = (b - a) / n;
        var xs = new double[n + 1];
        var fs = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            if ((i & 63) == 0) ct.ThrowIfCancellationRequested();
            xs[i] = i == n ? b : a + i * h;
            double v = f(xs[i]);
            fs[i] = double.IsFinite(v) ? v : double.NaN;
        }

        var extrema = new List<Extremum>();
        bool unbounded = false;
        double unboundedX = double.NaN;
        double bestX = double.NaN, bestG = double.PositiveInfinity;
        bool bestAtEnd = false;

        void Consider(double x, double g, bool atEnd)
        {
            if (double.IsFinite(g) && g < bestG) { bestG = g; bestX = x; bestAtEnd = atEnd; }
        }

        if (a == b)
        {
            double v = f(a);
            return new GlobalExtremumResult(a, v, true, false, extrema);
        }

        for (int i = 0; i <= n; i++)
            if (!double.IsNaN(fs[i])) Consider(xs[i], sgn * fs[i], i == 0 || i == n);

        double typical = 0;
        {
            var mags = new List<double>();
            foreach (double v in fs) if (!double.IsNaN(v)) mags.Add(Math.Abs(v));
            if (mags.Count > 0) { mags.Sort(); typical = mags[mags.Count / 2]; }
        }

        for (int i = 1; i < n; i++)
        {
            double f0 = fs[i - 1], f1 = fs[i], f2 = fs[i + 1];
            if (double.IsNaN(f0) || double.IsNaN(f1) || double.IsNaN(f2)) continue;
            for (int kindIndex = 0; kindIndex < 2; kindIndex++)
            {
                double s = kindIndex == 0 ? 1 : -1; // s = 1 looks for minima of f, s = -1 for maxima
                double g0 = s * f0, g1 = s * f1, g2 = s * f2;
                if (!(g1 <= g0 && g1 <= g2 && (g1 < g0 || g1 < g2))) continue;
                ct.ThrowIfCancellationRequested();
                Func<double, double> g = x =>
                {
                    double v = f(x);
                    return double.IsFinite(v) ? s * v : double.PositiveInfinity;
                };
                var r = BrentMinimizeCore(g, xs[i - 1], xs[i + 1], 1e-10, 1e-14 * (xs[i + 1] - xs[i - 1]) + 1e-300, 300, ct);
                double xr = r.X, gr = r.Value;
                if (!(gr <= g1)) { xr = xs[i]; gr = g1; }
                if (LooksLikePole(g, xs[i - 1], xs[i], xs[i + 1], g0, g1, g2, xr, gr, typical))
                {
                    // A pole for this kind means the function is unbounded in that direction.
                    bool relevant = (s == 1) != maximize;
                    if (relevant && !unbounded) { unbounded = true; unboundedX = xr; }
                    continue;
                }
                var kind = s == 1 ? ExtremumKind.Minimum : ExtremumKind.Maximum;
                extrema.Add(new Extremum(xr, s * gr, kind));
                Consider(xr, sgn * s * gr, false);
            }
        }

        extrema.Sort((p, q) => p.X.CompareTo(q.X));
        // Remove duplicates produced by neighbouring candidates.
        var unique = new List<Extremum>();
        foreach (var e in extrema)
        {
            if (unique.Count > 0 && unique[^1].Kind == e.Kind && Math.Abs(unique[^1].X - e.X) <= 1e-7 * h + 1e-12 * Math.Abs(e.X))
            {
                if ((e.Kind == ExtremumKind.Minimum && e.Value < unique[^1].Value) || (e.Kind == ExtremumKind.Maximum && e.Value > unique[^1].Value))
                    unique[^1] = e;
                continue;
            }
            unique.Add(e);
        }

        if (unbounded)
            return new GlobalExtremumResult(unboundedX, maximize ? double.PositiveInfinity : double.NegativeInfinity, false, true, unique);
        if (double.IsNaN(bestX))
            return new GlobalExtremumResult(double.NaN, double.NaN, false, false, unique);
        return new GlobalExtremumResult(bestX, sgn * bestG, bestAtEnd, false, unique);
    }

    private static bool LooksLikePole(Func<double, double> g, double x0, double x1, double x2, double g0, double g1, double g2, double xr, double gr, double typical)
    {
        // Value predicted by the parabola through the three samples.
        double d1 = (g1 - g0) / (x1 - x0), d2 = (g2 - g1) / (x2 - x1);
        double c2 = (d2 - d1) / (x2 - x0);
        double predicted = g1;
        if (c2 > 0)
        {
            double xv = 0.5 * (x0 + x1) - d1 / (2 * c2);
            xv = Math.Clamp(xv, x0, x2);
            predicted = Math.Min(g1, g1 + d1 * (xv - x1) + c2 * (xv - x1) * (xv - x0));
        }
        double spread = Math.Max(Math.Abs(g0 - g1), Math.Abs(g2 - g1)) + 1e-12 * typical + 1e-300;
        if (gr >= predicted - 10 * spread) return false;
        // Large anomaly: a genuine narrow minimum stays deep a short distance away, a pole does not.
        double delta = 1e-6 * (x2 - x0);
        double half = gr + 0.5 * (predicted - gr);
        double a = g(xr - delta), b = g(xr + delta);
        bool aHigh = !double.IsFinite(a) || a > half;
        bool bHigh = !double.IsFinite(b) || b > half;
        return aHigh && bHigh;
    }

    // ------------------------------------------------------------------ Nelder-Mead

    /// <summary>
    /// Nelder-Mead simplex minimization with adaptive coefficients (Gao and Han) and automatic
    /// restarts. NaN values are treated as +Infinity. Converges when both the spread of function
    /// values and the simplex diameter fall below the tolerance (relative).
    /// </summary>
    /// <param name="f">Objective.</param>
    /// <param name="x0">Starting point.</param>
    /// <param name="initialStep">Initial simplex edge; 0 selects 5% of each coordinate (0.00025 for zeros).</param>
    /// <param name="tolerance">Relative tolerance for values and simplex size.</param>
    /// <param name="maxEvaluations">Function evaluation cap.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static MultiMinimizationResult NelderMead(Func<double[], double> f, double[] x0, double initialStep = 0, double tolerance = 1e-10, int maxEvaluations = 50_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(x0);
        int n = x0.Length;
        if (n == 0) throw new ArgumentException("The starting point must have at least one coordinate.");
        int evals = 0;
        double F(double[] x)
        {
            evals++;
            double v = f(x);
            return double.IsNaN(v) ? double.PositiveInfinity : v;
        }

        double alpha = 1, gamma = n >= 2 ? 1 + 2.0 / n : 2, rho = n >= 2 ? 0.75 - 1.0 / (2 * n) : 0.5, sigma = n >= 2 ? 1 - 1.0 / n : 0.5;
        double[] best = NumericsCore.Copy(x0);
        double bestValue = F(best);
        int iterations = 0;
        bool converged = false;

        for (int restart = 0; restart < 4; restart++)
        {
            var sx = new double[n + 1][];
            var sf = new double[n + 1];
            sx[0] = NumericsCore.Copy(best);
            sf[0] = bestValue;
            for (int i = 0; i < n; i++)
            {
                var p = NumericsCore.Copy(best);
                double step = initialStep > 0 ? initialStep : (p[i] != 0 ? 0.05 * Math.Abs(p[i]) : 0.00025);
                if (restart > 0) step = Math.Max(step * 0.1, 1e-6 * Math.Max(1.0, Math.Abs(p[i])));
                p[i] += step;
                sx[i + 1] = p;
                sf[i + 1] = F(p);
            }
            converged = false;
            var order = new int[n + 1];
            while (evals < maxEvaluations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                iterations++;
                for (int i = 0; i <= n; i++) order[i] = i;
                Array.Sort(order, (p, q) => sf[p].CompareTo(sf[q]));
                var nx = new double[n + 1][];
                var nf = new double[n + 1];
                for (int i = 0; i <= n; i++) { nx[i] = sx[order[i]]; nf[i] = sf[order[i]]; }
                sx = nx; sf = nf;

                double fSpread = Math.Abs(sf[n] - sf[0]);
                double size = 0;
                for (int i = 1; i <= n; i++)
                    for (int j = 0; j < n; j++)
                        size = Math.Max(size, Math.Abs(sx[i][j] - sx[0][j]) / Math.Max(1.0, Math.Abs(sx[0][j])));
                if (double.IsFinite(sf[0]) && fSpread <= tolerance * Math.Max(1.0, Math.Abs(sf[0])) && size <= Math.Max(tolerance, 1e-15))
                {
                    converged = true;
                    break;
                }

                var c = new double[n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) c[j] += sx[i][j] / n;

                double[] Along(double t)
                {
                    var p = new double[n];
                    for (int j = 0; j < n; j++) p[j] = c[j] + t * (c[j] - sx[n][j]);
                    return p;
                }

                var xr = Along(alpha);
                double fr = F(xr);
                if (fr < sf[0])
                {
                    var xe = Along(alpha * gamma);
                    double fe = F(xe);
                    if (fe < fr) { sx[n] = xe; sf[n] = fe; }
                    else { sx[n] = xr; sf[n] = fr; }
                }
                else if (fr < sf[n - 1])
                {
                    sx[n] = xr; sf[n] = fr;
                }
                else
                {
                    bool shrink;
                    if (fr < sf[n])
                    {
                        var xoc = Along(alpha * rho);
                        double foc = F(xoc);
                        shrink = !(foc <= fr);
                        if (!shrink) { sx[n] = xoc; sf[n] = foc; }
                    }
                    else
                    {
                        var xic = Along(-rho);
                        double fic = F(xic);
                        shrink = !(fic < sf[n]);
                        if (!shrink) { sx[n] = xic; sf[n] = fic; }
                    }
                    if (shrink)
                    {
                        for (int i = 1; i <= n; i++)
                        {
                            for (int j = 0; j < n; j++) sx[i][j] = sx[0][j] + sigma * (sx[i][j] - sx[0][j]);
                            sf[i] = F(sx[i]);
                        }
                    }
                }
            }
            int bi = 0;
            for (int i = 1; i <= n; i++) if (sf[i] < sf[bi]) bi = i;
            double improvement = bestValue - sf[bi];
            bool improvedMuch = improvement > tolerance * Math.Max(1e-20, Math.Abs(sf[bi])) + 1e-300;
            if (sf[bi] <= bestValue) { best = sx[bi]; bestValue = sf[bi]; }
            if (!converged || evals >= maxEvaluations) break;
            if (restart > 0 && !improvedMuch) break;
        }
        return new MultiMinimizationResult(best, bestValue, iterations, evals, converged,
            converged ? null : "Evaluation limit reached before convergence.");
    }

    // ------------------------------------------------------------------ BFGS

    /// <summary>
    /// BFGS quasi-Newton minimization with a backtracking (Armijo) line search. If
    /// <paramref name="gradient"/> is null a fourth-order central-difference gradient is used.
    /// Converges when the gradient max-norm is below gradientTolerance * max(1, |f|).
    /// </summary>
    public static MultiMinimizationResult Bfgs(Func<double[], double> f, double[] x0, Func<double[], double[]>? gradient = null, double gradientTolerance = 1e-8, int maxIterations = 2000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(x0);
        int n = x0.Length;
        int evals = 0;
        double F(double[] x) { evals++; return f(x); }
        double[] Grad(double[] x)
        {
            if (gradient != null) return gradient(x);
            var g = NumericGradient(f, x);
            evals += 4 * n;
            return g;
        }

        double[] x = NumericsCore.Copy(x0);
        double fx = F(x);
        if (!double.IsFinite(fx))
            return new MultiMinimizationResult(x, fx, 0, evals, false, "The objective is not finite at the starting point.");
        double[] g = Grad(x);
        var H = Matrix.Identity(n);
        bool firstStep = true;
        int stall = 0;
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double gnorm = NumericsCore.NormInf(g);
            if (!double.IsFinite(gnorm))
                return new MultiMinimizationResult(x, fx, iter, evals, false, "The gradient is not finite.");
            if (gnorm <= gradientTolerance * Math.Max(1.0, Math.Abs(fx)))
                return new MultiMinimizationResult(x, fx, iter - 1, evals, true);

            double[] p = H.Multiply(g);
            for (int i = 0; i < n; i++) p[i] = -p[i];
            double slope = NumericsCore.Dot(g, p);
            if (!(slope < 0))
            {
                H = Matrix.Identity(n);
                for (int i = 0; i < n; i++) p[i] = -g[i];
                slope = -NumericsCore.Dot(g, g);
                firstStep = true;
            }
            if (firstStep)
            {
                // Scale the first step to a modest length.
                double pn = NumericsCore.NormInf(p);
                double maxLen = Math.Max(1.0, NumericsCore.NormInf(x));
                if (pn > maxLen) { for (int i = 0; i < n; i++) p[i] *= maxLen / pn; slope *= maxLen / pn; }
            }

            // Backtracking line search with quadratic/cubic interpolation.
            double t = 1, ft = double.NaN, tPrev = 0, fPrev = fx;
            double[] xt = x;
            bool ok = false;
            for (int k = 0; k < 60; k++)
            {
                xt = NumericsCore.Add(x, p, t);
                ft = F(xt);
                if (double.IsFinite(ft) && ft <= fx + 1e-4 * t * slope) { ok = true; break; }
                double tNew;
                if (!double.IsFinite(ft)) tNew = 0.25 * t;
                else if (k == 0) tNew = -slope * t * t / (2 * (ft - fx - slope * t));
                else
                {
                    double r1 = ft - fx - t * slope, r2 = fPrev - fx - tPrev * slope;
                    double aa = (r1 / (t * t) - r2 / (tPrev * tPrev)) / (t - tPrev);
                    double bb = (-tPrev * r1 / (t * t) + t * r2 / (tPrev * tPrev)) / (t - tPrev);
                    if (aa == 0) tNew = -slope / (2 * bb);
                    else
                    {
                        double disc = bb * bb - 3 * aa * slope;
                        tNew = disc < 0 ? 0.5 * t : (-bb + Math.Sqrt(disc)) / (3 * aa);
                    }
                }
                if (!double.IsFinite(tNew)) tNew = 0.5 * t;
                tPrev = t; fPrev = ft;
                t = Math.Clamp(tNew, 0.1 * t, 0.5 * t);
            }
            if (!ok)
            {
                bool small = gnorm <= Math.Sqrt(gradientTolerance) * Math.Max(1.0, Math.Abs(fx));
                return new MultiMinimizationResult(x, fx, iter, evals, small, small ? null : "Line search failed to decrease the objective.");
            }

            double[] s = new double[n];
            for (int i = 0; i < n; i++) s[i] = xt[i] - x[i];
            double[] gNew = Grad(xt);
            double[] y = new double[n];
            for (int i = 0; i < n; i++) y[i] = gNew[i] - g[i];
            double sy = NumericsCore.Dot(s, y);
            double df = fx - ft;
            x = xt; fx = ft; g = gNew;

            if (sy > 1e-12 * NumericsCore.Norm2(s) * NumericsCore.Norm2(y))
            {
                if (firstStep)
                {
                    double scale = sy / NumericsCore.Dot(y, y);
                    H = Matrix.Identity(n) * scale;
                    firstStep = false;
                }
                // H = (I - rho s y^T) H (I - rho y s^T) + rho s s^T
                double rho = 1 / sy;
                double[] Hy = H.Multiply(y);
                double yHy = NumericsCore.Dot(y, Hy);
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        H[i, j] += -rho * (Hy[i] * s[j] + s[i] * Hy[j]) + (rho * rho * yHy + rho) * s[i] * s[j];
            }

            double xs = NumericsCore.NormInf(s) / Math.Max(1.0, NumericsCore.NormInf(x));
            if (xs <= 1e-15 && Math.Abs(df) <= 1e-15 * Math.Max(1.0, Math.Abs(fx)))
            {
                if (++stall >= 3)
                    return new MultiMinimizationResult(x, fx, iter, evals, true);
            }
            else stall = 0;
        }
        return new MultiMinimizationResult(x, fx, maxIterations, evals, false, "Iteration limit reached.");
    }

    /// <summary>Fourth-order central-difference gradient.</summary>
    internal static double[] NumericGradient(Func<double[], double> f, double[] x)
    {
        int n = x.Length;
        var g = new double[n];
        var xp = NumericsCore.Copy(x);
        for (int i = 0; i < n; i++)
        {
            double h = 7.4e-4 * Math.Max(1.0, Math.Abs(x[i]));
            double xi = x[i];
            xp[i] = xi + h; double h1 = xp[i] - xi; double f1 = f(xp);
            xp[i] = xi - h; double f2 = f(xp);
            xp[i] = xi + 2 * h; double f3 = f(xp);
            xp[i] = xi - 2 * h; double f4 = f(xp);
            xp[i] = xi;
            g[i] = (8 * (f1 - f2) - (f3 - f4)) / (12 * h1);
        }
        return g;
    }

    // ------------------------------------------------------------------ Levenberg-Marquardt

    /// <summary>
    /// Levenberg-Marquardt nonlinear least squares: minimizes the sum of squares of the residual
    /// vector r(p). Uses Marquardt diagonal scaling and Nielsen's damping update. If
    /// <paramref name="jacobian"/> is null, central differences are used.
    /// </summary>
    public static LeastSquaresResult LevenbergMarquardt(Func<double[], double[]> residuals, double[] p0, Func<double[], Matrix>? jacobian = null, double tolerance = 1e-12, int maxIterations = 500, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(residuals);
        ArgumentNullException.ThrowIfNull(p0);
        int n = p0.Length;
        double[] p = NumericsCore.Copy(p0);
        double[] r = residuals(p);
        int m = r.Length;
        if (!NumericsCore.AllFinite(r))
            return new LeastSquaresResult(p, r, double.NaN, 0, false, null, "Residuals are not finite at the starting point.");
        double S = NumericsCore.Dot(r, r);

        Matrix J = jacobian?.Invoke(p) ?? CentralJacobian(residuals, p);
        Matrix JtJ = J.Transpose() * J;
        double[] g = J.Transpose().Multiply(r);
        double mu = 0;
        for (int i = 0; i < n; i++) mu = Math.Max(mu, JtJ[i, i]);
        mu = 1e-3 * (mu > 0 ? mu : 1);
        double nu = 2;
        bool converged = false;
        string? message = null;
        int iter;
        for (iter = 1; iter <= maxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NumericsCore.NormInf(g) <= tolerance * Math.Max(1.0, S) || S == 0) { converged = true; break; }

            var A = JtJ.Clone();
            var D = new double[n];
            for (int i = 0; i < n; i++)
            {
                D[i] = Math.Max(JtJ[i, i], 1e-12 * Math.Max(1e-300, mu));
                A[i, i] += mu * D[i];
            }
            double[] delta;
            var rhs = new double[n];
            for (int i = 0; i < n; i++) rhs[i] = -g[i];
            try
            {
                delta = new CholeskyDecomposition(A).Solve(rhs);
            }
            catch (InvalidOperationException)
            {
                mu *= nu; nu *= 2;
                continue;
            }
            double dn = NumericsCore.Norm2(delta), pn = NumericsCore.Norm2(p);
            if (dn <= tolerance * (pn + tolerance)) { converged = true; break; }

            double[] pNew = NumericsCore.Add(p, delta);
            double[] rNew = residuals(pNew);
            double SNew = NumericsCore.AllFinite(rNew) ? NumericsCore.Dot(rNew, rNew) : double.PositiveInfinity;
            double predicted = 0;
            for (int i = 0; i < n; i++) predicted += delta[i] * (mu * D[i] * delta[i] - g[i]);
            double ratio = predicted > 0 ? (S - SNew) / predicted : -1;
            if (ratio > 0 && double.IsFinite(SNew))
            {
                double relDrop = (S - SNew) / Math.Max(S, 1e-300);
                p = pNew; r = rNew; S = SNew;
                J = jacobian?.Invoke(p) ?? CentralJacobian(residuals, p);
                JtJ = J.Transpose() * J;
                g = J.Transpose().Multiply(r);
                mu *= Math.Max(1.0 / 3, 1 - Math.Pow(2 * ratio - 1, 3));
                nu = 2;
                if (relDrop <= tolerance && dn <= Math.Sqrt(tolerance) * (pn + Math.Sqrt(tolerance))) { converged = true; break; }
            }
            else
            {
                mu *= nu;
                nu *= 2;
                if (!double.IsFinite(mu) || mu > 1e300) { message = "Damping parameter overflow; no further progress possible."; converged = NumericsCore.NormInf(g) <= Math.Sqrt(tolerance) * Math.Max(1.0, S); break; }
            }
        }
        if (iter > maxIterations) message = "Iteration limit reached.";

        Matrix? cov = null;
        if (m > n)
        {
            try
            {
                var inv = new CholeskyDecomposition(JtJ).Inverse();
                cov = inv * (S / (m - n));
            }
            catch (InvalidOperationException)
            {
                cov = null;
            }
        }
        return new LeastSquaresResult(p, r, S, Math.Min(iter, maxIterations), converged, cov, converged ? null : message);
    }

    internal static Matrix CentralJacobian(Func<double[], double[]> F, double[] x)
    {
        int n = x.Length;
        double[] f0 = F(x);
        int m = f0.Length;
        var J = new Matrix(m, n);
        var xp = NumericsCore.Copy(x);
        for (int j = 0; j < n; j++)
        {
            double h = NumericsCore.CbrtEps * Math.Max(1.0, Math.Abs(x[j]));
            double xj = x[j];
            xp[j] = xj + h; double hp = xp[j] - xj; double[] fp = F(xp);
            xp[j] = xj - h; double hm = xj - xp[j]; double[] fm = F(xp);
            xp[j] = xj;
            for (int i = 0; i < m; i++) J[i, j] = (fp[i] - fm[i]) / (hp + hm);
        }
        return J;
    }
}
