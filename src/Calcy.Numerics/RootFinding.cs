namespace Calcy.Numerics;

/// <summary>Result of a scalar root search.</summary>
/// <param name="Root">Best root estimate.</param>
/// <param name="FunctionValue">f(Root).</param>
/// <param name="Iterations">Iterations performed.</param>
/// <param name="Converged">True if the tolerance was met.</param>
/// <param name="ErrorEstimate">Estimated absolute error of <paramref name="Root"/>.</param>
/// <param name="Message">Explanation when the search failed.</param>
public sealed record RootResult(double Root, double FunctionValue, int Iterations, bool Converged, double ErrorEstimate, string? Message = null);

/// <summary>How a root found by <see cref="RootFinding.FindAllRoots"/> was detected.</summary>
public enum RootKind
{
    /// <summary>The function changes sign at the root (odd multiplicity).</summary>
    SignChange,

    /// <summary>The function touches zero without changing sign (even multiplicity, or a domain boundary).</summary>
    Touching,
}

/// <summary>A root located by <see cref="RootFinding.FindAllRoots"/>.</summary>
/// <param name="X">Root location.</param>
/// <param name="FunctionValue">f(X).</param>
/// <param name="Kind">Sign change or touching root.</param>
public sealed record FoundRoot(double X, double FunctionValue, RootKind Kind);

/// <summary>Result of solving a nonlinear system F(x) = 0.</summary>
/// <param name="X">Solution estimate.</param>
/// <param name="Residual">F(X).</param>
/// <param name="ResidualNorm">Max-norm of F(X).</param>
/// <param name="Iterations">Newton iterations performed.</param>
/// <param name="Converged">True if the residual tolerance was met.</param>
/// <param name="Message">Explanation when the solve failed.</param>
public sealed record SystemSolveResult(double[] X, double[] Residual, double ResidualNorm, int Iterations, bool Converged, string? Message = null);

/// <summary>Scalar and multidimensional root finding.</summary>
public static class RootFinding
{
    private const double Eps = NumericsCore.Eps;

    // ------------------------------------------------------------------ Brent

    /// <summary>
    /// Brent's method (inverse quadratic interpolation, secant and bisection) on a bracket [a, b]
    /// where f(a) and f(b) have opposite signs. Converges to |error| &lt;= tolerance + 4 eps |root|.
    /// </summary>
    /// <exception cref="ArgumentException">The interval does not bracket a root or f is undefined at an endpoint.</exception>
    public static RootResult Brent(Func<double, double> f, double a, double b, double tolerance = 1e-15, int maxIterations = 200, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        double fa = f(a), fb = f(b);
        if (double.IsNaN(fa) || double.IsNaN(fb))
            throw new ArgumentException("The function is undefined (NaN) at an endpoint of the bracket.");
        return BrentCore(f, a, b, fa, fb, tolerance, maxIterations, cancellationToken);
    }

    internal static RootResult BrentCore(Func<double, double> f, double a, double b, double fa, double fb, double tolerance, int maxIterations, CancellationToken ct)
    {
        if (fa == 0) return new RootResult(a, 0, 0, true, 0);
        if (fb == 0) return new RootResult(b, 0, 0, true, 0);
        if ((fa > 0) == (fb > 0))
            throw new ArgumentException($"The root is not bracketed: f({a}) = {fa} and f({b}) = {fb} have the same sign.");

        double c = b, fc = fb, d = b - a, e = d;
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();
            if ((fb > 0 && fc > 0) || (fb < 0 && fc < 0))
            {
                c = a; fc = fa; d = b - a; e = d;
            }
            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b; b = c; c = a;
                fa = fb; fb = fc; fc = fa;
            }
            double tol1 = 2 * Eps * Math.Abs(b) + 0.5 * tolerance + 1e-300;
            double xm = 0.5 * (c - b);
            if (Math.Abs(xm) <= tol1 || fb == 0)
                return new RootResult(b, fb, iter, true, Math.Abs(xm));

            if (Math.Abs(e) >= tol1 && Math.Abs(fa) > Math.Abs(fb))
            {
                double s = fb / fa, p, q;
                if (a == c)
                {
                    p = 2 * xm * s;
                    q = 1 - s;
                }
                else
                {
                    q = fa / fc;
                    double r = fb / fc;
                    p = s * (2 * xm * q * (q - r) - (b - a) * (r - 1));
                    q = (q - 1) * (r - 1) * (s - 1);
                }
                if (p > 0) q = -q;
                p = Math.Abs(p);
                double min1 = 3 * xm * q - Math.Abs(tol1 * q);
                double min2 = Math.Abs(e * q);
                if (2 * p < Math.Min(min1, min2) && double.IsFinite(p / q))
                {
                    e = d;
                    d = p / q;
                }
                else
                {
                    d = xm; e = d;
                }
            }
            else
            {
                d = xm; e = d;
            }
            a = b; fa = fb;
            b += Math.Abs(d) > tol1 ? d : Math.CopySign(tol1, xm);
            fb = f(b);
            if (double.IsNaN(fb))
                return new RootResult(b, fb, iter, false, Math.Abs(c - b), "The function is undefined (NaN) inside the bracket.");
        }
        return new RootResult(b, fb, maxIterations, false, Math.Abs(c - b) / 2, "Iteration limit reached.");
    }

    // ------------------------------------------------------------------ Bisection

    /// <summary>Bisection on a bracket [a, b] with f(a) f(b) &lt; 0. Robust, linear convergence.</summary>
    public static RootResult Bisection(Func<double, double> f, double a, double b, double tolerance = 1e-15, int maxIterations = 2000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        double fa = f(a), fb = f(b);
        if (double.IsNaN(fa) || double.IsNaN(fb))
            throw new ArgumentException("The function is undefined (NaN) at an endpoint of the bracket.");
        if (fa == 0) return new RootResult(a, 0, 0, true, 0);
        if (fb == 0) return new RootResult(b, 0, 0, true, 0);
        if ((fa > 0) == (fb > 0))
            throw new ArgumentException($"The root is not bracketed: f({a}) = {fa} and f({b}) = {fb} have the same sign.");
        if (a > b) { (a, b) = (b, a); (fa, fb) = (fb, fa); }
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double m = a + 0.5 * (b - a);
            if (m <= a || m >= b || b - a <= tolerance + 2 * Eps * Math.Abs(m))
                return new RootResult(m, f(m), iter, true, 0.5 * (b - a));
            double fm = f(m);
            if (fm == 0) return new RootResult(m, 0, iter, true, 0);
            if (double.IsNaN(fm))
                return new RootResult(m, fm, iter, false, b - a, "The function is undefined (NaN) inside the bracket.");
            if ((fm > 0) == (fa > 0)) { a = m; fa = fm; }
            else { b = m; }
        }
        double mid = 0.5 * (a + b);
        return new RootResult(mid, f(mid), maxIterations, false, 0.5 * (b - a), "Iteration limit reached.");
    }

    // ------------------------------------------------------------------ Newton and secant

    /// <summary>
    /// Damped Newton iteration from x0. If <paramref name="derivative"/> is null a central
    /// difference derivative is used. Steps that increase |f| are halved (up to 30 times).
    /// Converges when the step is below tolerance * max(1, |x|) or f(x) = 0.
    /// </summary>
    public static RootResult Newton(Func<double, double> f, double x0, Func<double, double>? derivative = null, double tolerance = 1e-14, int maxIterations = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        double x = x0, fx = f(x);
        if (!double.IsFinite(fx))
            return new RootResult(x, fx, 0, false, double.PositiveInfinity, "The function is not finite at the starting point.");
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fx == 0) return new RootResult(x, 0, iter - 1, true, 0);
            double d = derivative?.Invoke(x) ?? CentralDerivative(f, x, fx);
            if (d == 0 || !double.IsFinite(d))
                return new RootResult(x, fx, iter, false, double.PositiveInfinity, "The derivative is zero or undefined; Newton's method cannot continue.");
            double step = fx / d;
            double xn = x - step, fn = f(xn);
            int halvings = 0;
            while ((!double.IsFinite(fn) || Math.Abs(fn) > Math.Abs(fx)) && halvings < 30)
            {
                step *= 0.5;
                xn = x - step;
                fn = f(xn);
                halvings++;
            }
            if (!double.IsFinite(fn))
                return new RootResult(x, fx, iter, false, Math.Abs(step), "The Newton step leaves the domain of the function.");
            if (halvings == 30)
            {
                // No decrease found: take the plain Newton step anyway.
                xn = x - fx / d;
                fn = f(xn);
                if (!double.IsFinite(fn))
                    return new RootResult(x, fx, iter, false, Math.Abs(step), "Newton's method stalled.");
            }
            double dx = Math.Abs(xn - x);
            x = xn; fx = fn;
            if (dx <= tolerance * Math.Max(1.0, Math.Abs(x)))
                return new RootResult(x, fx, iter, true, dx);
        }
        return new RootResult(x, fx, maxIterations, false, double.NaN, "Iteration limit reached.");
    }

    /// <summary>Secant method from two starting points.</summary>
    public static RootResult Secant(Func<double, double> f, double x0, double x1, double tolerance = 1e-14, int maxIterations = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        double f0 = f(x0), f1 = f(x1);
        if (!double.IsFinite(f0) || !double.IsFinite(f1))
            return new RootResult(x1, f1, 0, false, double.PositiveInfinity, "The function is not finite at a starting point.");
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (f1 == 0) return new RootResult(x1, 0, iter - 1, true, 0);
            double denom = f1 - f0;
            if (denom == 0)
                return new RootResult(x1, f1, iter, false, Math.Abs(x1 - x0), "Secant slope is zero; cannot continue.");
            double x2 = x1 - f1 * (x1 - x0) / denom;
            double f2 = f(x2);
            if (!double.IsFinite(f2))
                return new RootResult(x1, f1, iter, false, Math.Abs(x1 - x0), "The secant step leaves the domain of the function.");
            double dx = Math.Abs(x2 - x1);
            x0 = x1; f0 = f1; x1 = x2; f1 = f2;
            if (dx <= tolerance * Math.Max(1.0, Math.Abs(x1)))
                return new RootResult(x1, f1, iter, true, dx);
        }
        return new RootResult(x1, f1, maxIterations, false, Math.Abs(x1 - x0), "Iteration limit reached.");
    }

    internal static double CentralDerivative(Func<double, double> f, double x, double fx)
    {
        double h = NumericsCore.CbrtEps * Math.Max(1.0, Math.Abs(x));
        double xp = x + h, xm = x - h;
        double fp = f(xp), fm = f(xm);
        if (double.IsFinite(fp) && double.IsFinite(fm)) return (fp - fm) / (xp - xm);
        if (double.IsFinite(fp)) return (fp - fx) / (xp - x);
        if (double.IsFinite(fm)) return (fx - fm) / (x - xm);
        return double.NaN;
    }

    // ------------------------------------------------------------------ automatic solve

    /// <summary>
    /// Finds a root near the guess x0: tries damped Newton first, then searches outward for a
    /// sign change and refines it with Brent's method. A sign change across a pole or jump is
    /// rejected.
    /// </summary>
    public static RootResult FindRoot(Func<double, double> f, double x0, double tolerance = 1e-14, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        var newton = Newton(f, x0, null, tolerance, 60, cancellationToken);
        double scale0 = Math.Abs(f(x0));
        if (newton.Converged && double.IsFinite(newton.FunctionValue) &&
            (newton.FunctionValue == 0 || Math.Abs(newton.FunctionValue) <= 1e-10 * Math.Max(scale0, 1e-300) || Math.Abs(newton.FunctionValue) < 1e-12))
            return newton;

        // Outward bracket search.
        double h = 0.01 * Math.Max(1.0, Math.Abs(x0));
        double a = x0 - h, b = x0 + h;
        double fa = f(a), fb = f(b);
        for (int k = 0; k < 80; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (double.IsFinite(fa) && double.IsFinite(fb) && ((fa > 0) != (fb > 0) || fa == 0 || fb == 0))
            {
                var r = BrentCore(f, a, b, fa, fb, tolerance, 300, cancellationToken);
                if (r.Converged && IsGenuineRoot(f, a, b, fa, fb, r.Root, r.FunctionValue))
                    return r;
                // Try both halves if the bracket contained a pole.
            }
            bool expandLeft = !double.IsFinite(fb) || (double.IsFinite(fa) && Math.Abs(fa) < Math.Abs(fb));
            double w = b - a;
            if (expandLeft) { a -= 0.6 * w; fa = f(a); }
            else { b += 0.6 * w; fb = f(b); }
            if (!double.IsFinite(a) || !double.IsFinite(b)) break;
        }
        return newton with { Converged = false, Message = newton.Message ?? "No root found near the starting point." };
    }

    // ------------------------------------------------------------------ all roots in an interval

    /// <summary>
    /// Finds all real roots of f in [a, b]: dense sampling, sign changes refined by Brent's
    /// method, touching (even multiplicity) roots found at local minima of |f| and refined, and
    /// roots at the boundary of undefined (NaN) regions. Sign changes across poles or jumps are
    /// not reported. Returns sorted distinct roots. Features narrower than the sampling step
    /// (b - a) / samples can be missed, except pairs of close roots between two samples of the
    /// same sign, which the |f| minimum search separates. Touching roots are accepted when the
    /// minimum of |f| is within the local rounding noise of f.
    /// </summary>
    /// <param name="f">Function to examine.</param>
    /// <param name="a">Left end of the interval.</param>
    /// <param name="b">Right end of the interval.</param>
    /// <param name="samples">Number of sampling subintervals (at least 16).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static IReadOnlyList<FoundRoot> FindAllRoots(Func<double, double> f, double a, double b, int samples = 2000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b))
            throw new ArgumentException("The search interval must be finite.");
        if (a > b) (a, b) = (b, a);
        var roots = new List<FoundRoot>();
        if (a == b)
        {
            double fa0 = f(a);
            if (fa0 == 0) roots.Add(new FoundRoot(a, 0, RootKind.Touching));
            return roots;
        }
        int n = Math.Max(16, samples);
        double h = (b - a) / n;
        var xs = new double[n + 1];
        var fs = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            xs[i] = i == n ? b : a + i * h;
            double v = f(xs[i]);
            fs[i] = double.IsFinite(v) ? v : double.NaN;
        }

        // Absolute root tolerance relative to the sampling scale (lets Brent finish near x = 0
        // for multiple roots, where its relative tolerance would otherwise never be met).
        double rootTol = 1e-13 * h;

        // 1. Exact zeros at sample points (runs of zeros report their two ends). The intervals
        //    next to the zero are searched for a hidden second crossing.
        for (int i = 0; i <= n; i++)
        {
            if (fs[i] != 0) continue;
            int j = i;
            while (j + 1 <= n && fs[j + 1] == 0) j++;
            int leftSign = i > 0
                ? SideSign(f, xs[i - 1], fs[i - 1], xs[i], rootTol, roots, cancellationToken)
                : Sign(f(a - h));
            int rightSign = j < n
                ? SideSign(f, xs[j + 1], fs[j + 1], xs[j], rootTol, roots, cancellationToken)
                : Sign(f(b + h));
            RootKind kind = leftSign != 0 && rightSign != 0 && leftSign != rightSign ? RootKind.SignChange : RootKind.Touching;
            roots.Add(new FoundRoot(xs[i], 0, kind));
            if (j > i) roots.Add(new FoundRoot(xs[j], 0, kind));
            i = j;
        }

        // 2. Sign changes between consecutive nonzero finite samples.
        for (int i = 0; i < n; i++)
        {
            double f0 = fs[i], f1 = fs[i + 1];
            if (double.IsNaN(f0) || double.IsNaN(f1) || f0 == 0 || f1 == 0 || (f0 > 0) == (f1 > 0)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var r = BrentCore(f, xs[i], xs[i + 1], f0, f1, rootTol, 1000, cancellationToken);
            if (r.Converged && IsGenuineRoot(f, xs[i], xs[i + 1], f0, f1, r.Root, r.FunctionValue))
                roots.Add(new FoundRoot(r.Root, r.FunctionValue, RootKind.SignChange));
        }

        // 3. Touching roots: local minima of |f| among same-sign samples (endpoints included).
        for (int i = 0; i <= n; i++)
        {
            double fi = fs[i];
            if (double.IsNaN(fi) || fi == 0) continue;
            int l = Math.Max(0, i - 1), r = Math.Min(n, i + 1);
            double fl = fs[l], fr = fs[r];
            if (double.IsNaN(fl) || double.IsNaN(fr)) continue;
            if (fl == 0 || fr == 0) continue;
            if ((fl > 0) != (fi > 0) || (fr > 0) != (fi > 0)) continue;
            double ai = Math.Abs(fi);
            bool isMin = ai <= Math.Abs(fl) && ai <= Math.Abs(fr) && (ai < Math.Abs(fl) || ai < Math.Abs(fr));
            if (!isMin) continue;
            cancellationToken.ThrowIfCancellationRequested();
            double localScale = Math.Max(Math.Abs(fl), Math.Abs(fr));
            ExamineTouchingCandidate(f, xs[l], xs[r], fi > 0 ? 1.0 : -1.0, localScale, rootTol, roots, cancellationToken);
        }

        // 4. Roots at the boundary of an undefined region (for example sqrt(x) at 0).
        for (int i = 0; i < n; i++)
        {
            bool d0 = !double.IsNaN(fs[i]), d1 = !double.IsNaN(fs[i + 1]);
            if (d0 == d1) continue;
            double xDef = d0 ? xs[i] : xs[i + 1];
            double xUndef = d0 ? xs[i + 1] : xs[i];
            double fDef = d0 ? fs[i] : fs[i + 1];
            if (fDef == 0) continue;
            for (int k = 0; k < 80; k++)
            {
                double m = 0.5 * (xDef + xUndef);
                if (m == xDef || m == xUndef) break;
                double fm = f(m);
                if (double.IsFinite(fm)) { xDef = m; fDef = fm; }
                else xUndef = m;
            }
            double scale = d0 ? Math.Abs(fs[i]) : Math.Abs(fs[i + 1]);
            if (fDef == 0 || Math.Abs(fDef) <= 1e-5 * scale)
                roots.Add(new FoundRoot(xDef, fDef, RootKind.Touching));
        }

        return MergeRoots(roots, h);
    }

    private static int Sign(double v) => double.IsFinite(v) && v != 0 ? (v > 0 ? 1 : -1) : 0;

    /// <summary>
    /// Rounding noise of f near x: the spread of f over a few dozen ulps around x, where the
    /// true function is constant to working precision.
    /// </summary>
    private static double LocalNoise(Func<double, double> f, double x)
    {
        double ax = Math.Abs(x);
        double u = 16 * (Math.BitIncrement(ax) - ax);
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int k = -8; k <= 8; k++)
        {
            double v = f(x + k * u);
            if (!double.IsFinite(v)) continue;
            lo = Math.Min(lo, v);
            hi = Math.Max(hi, v);
        }
        return hi >= lo ? hi - lo : 0;
    }

    /// <summary>
    /// Sign of f immediately next to the exact zero at <paramref name="zero"/>, on the side of the
    /// neighbouring sample. If f dips to the opposite sign between them (beyond rounding noise),
    /// the hidden crossing is located and added.
    /// </summary>
    private static int SideSign(Func<double, double> f, double neighbour, double fNeighbour, double zero, double rootTol, List<FoundRoot> roots, CancellationToken ct)
    {
        int s = Sign(fNeighbour);
        if (s == 0) return 0;
        double G(double x)
        {
            double v = f(x);
            return double.IsFinite(v) ? s * v : double.PositiveInfinity;
        }
        double lo = Math.Min(neighbour, zero), hi = Math.Max(neighbour, zero);
        var min = Optimization.BrentMinimizeCore(G, lo, hi, 1e-12, 1e-14 * (hi - lo) + 1e-300, 300, ct);
        if (!double.IsFinite(min.Value) || !(min.Value < -4 * LocalNoise(f, min.X))) return s;
        double fm = f(min.X);
        var r = neighbour > zero
            ? BrentCore(f, min.X, neighbour, fm, fNeighbour, rootTol, 1000, ct)
            : BrentCore(f, neighbour, min.X, fNeighbour, fm, rootTol, 1000, ct);
        if (r.Converged && IsGenuineRoot(f, Math.Min(min.X, neighbour), Math.Max(min.X, neighbour), fm, fNeighbour, r.Root, r.FunctionValue))
            roots.Add(new FoundRoot(r.Root, r.FunctionValue, RootKind.SignChange));
        return -s;
    }

    private static void ExamineTouchingCandidate(Func<double, double> f, double xl, double xr, double sign, double localScale, double rootTol, List<FoundRoot> roots, CancellationToken ct)
    {
        double G(double x)
        {
            double v = f(x);
            return double.IsFinite(v) ? sign * v : double.PositiveInfinity;
        }
        var min = Optimization.BrentMinimizeCore(G, xl, xr, 1e-12, 1e-14 * (xr - xl) + 1e-300, 300, ct);
        double xm = min.X, gm = min.Value;
        if (!double.IsFinite(gm)) return;
        double noise = LocalNoise(f, xm);
        if (gm < -4 * noise)
        {
            // f dips through zero between the samples: two hidden sign changes.
            ExamineTouchingCandidateCrossing(f, xl, xr, xm, rootTol, roots, ct);
            return;
        }

        // Refine the location as a root of f' (a double root of f is a simple root of f').
        double xBest = xm, gBest = gm;
        double w = 0.25 * (xr - xl);
        double hd = 1e-3 * (xr - xl);
        double D(double x)
        {
            double d1 = (G(x + hd) - G(x - hd)) / (2 * hd);
            double d2 = (G(x + hd / 2) - G(x - hd / 2)) / hd;
            return (4 * d2 - d1) / 3;
        }
        double pl = Math.Max(xl, xm - w), pr = Math.Min(xr, xm + w);
        double dl = D(pl), dr = D(pr);
        if (double.IsFinite(dl) && double.IsFinite(dr) && dl < 0 && dr > 0)
        {
            var rd = BrentCore(D, pl, pr, dl, dr, 0, 200, ct);
            if (rd.Converged)
            {
                double gd = G(rd.Root);
                if (double.IsFinite(gd))
                {
                    double nd = LocalNoise(f, rd.Root);
                    if (gd < -4 * nd)
                    {
                        // The refined point dips below zero: report the two crossings.
                        ExamineTouchingCandidateCrossing(f, xl, xr, rd.Root, rootTol, roots, ct);
                        return;
                    }
                    if (gd <= gBest) { xBest = rd.Root; gBest = gd; noise = Math.Max(noise, nd); }
                }
            }
        }

        double threshold = Math.Max(1e-8 * localScale, 16 * noise);
        if (gBest <= threshold)
            roots.Add(new FoundRoot(xBest, sign * gBest, RootKind.Touching));
    }

    private static void ExamineTouchingCandidateCrossing(Func<double, double> f, double xl, double xr, double xm, double rootTol, List<FoundRoot> roots, CancellationToken ct)
    {
        double fl = f(xl), fm = f(xm), fr = f(xr);
        if (!double.IsFinite(fm) || fm == 0)
        {
            if (fm == 0) roots.Add(new FoundRoot(xm, 0, RootKind.Touching));
            return;
        }
        if (double.IsFinite(fl) && fl != 0 && (fl > 0) != (fm > 0))
        {
            var r1 = BrentCore(f, xl, xm, fl, fm, rootTol, 1000, ct);
            if (r1.Converged && IsGenuineRoot(f, xl, xm, fl, fm, r1.Root, r1.FunctionValue)) roots.Add(new FoundRoot(r1.Root, r1.FunctionValue, RootKind.SignChange));
        }
        if (double.IsFinite(fr) && fr != 0 && (fr > 0) != (fm > 0))
        {
            var r2 = BrentCore(f, xm, xr, fm, fr, rootTol, 1000, ct);
            if (r2.Converged && IsGenuineRoot(f, xm, xr, fm, fr, r2.Root, r2.FunctionValue)) roots.Add(new FoundRoot(r2.Root, r2.FunctionValue, RootKind.SignChange));
        }
    }

    /// <summary>
    /// Decides whether a converged sign-change bracket holds a genuine root rather than a pole or
    /// a jump discontinuity.
    /// </summary>
    internal static bool IsGenuineRoot(Func<double, double> f, double xl, double xr, double fl, double fr, double root, double froot)
    {
        if (!double.IsFinite(froot)) return false;
        if (froot == 0) return true;
        double m = Math.Max(Math.Abs(fl), Math.Abs(fr));
        double af = Math.Abs(froot);
        if (af <= 1e-9 * m) return true;
        if (af >= 0.1 * Math.Min(Math.Abs(fl), Math.Abs(fr))) return false;
        // Steep root (for example a cube root): |f| must keep shrinking towards the root.
        double width = Math.Abs(xr - xl);
        double previous = double.PositiveInfinity;
        for (int k = 2; k <= 8; k += 2)
        {
            double delta = width * Math.Pow(10, -k);
            double g1 = f(root - delta), g2 = f(root + delta);
            if (!double.IsFinite(g1) || !double.IsFinite(g2)) return false;
            double g = Math.Max(Math.Abs(g1), Math.Abs(g2));
            if (g >= previous) return false;
            previous = g;
        }
        return previous <= 1e-2 * m;
    }

    private static List<FoundRoot> MergeRoots(List<FoundRoot> roots, double h)
    {
        roots.Sort((p, q) => p.X.CompareTo(q.X));
        var result = new List<FoundRoot>();
        foreach (var r in roots)
        {
            if (result.Count > 0)
            {
                var last = result[^1];
                double tol = 1e-9 * h + 8 * Eps * Math.Max(Math.Abs(r.X), Math.Abs(last.X));
                if (Math.Abs(r.X - last.X) <= tol)
                {
                    // Keep the better of the two; prefer the sign-change classification.
                    var kind = last.Kind == RootKind.SignChange || r.Kind == RootKind.SignChange ? RootKind.SignChange : RootKind.Touching;
                    var best = Math.Abs(r.FunctionValue) < Math.Abs(last.FunctionValue) ? r : last;
                    result[^1] = best with { Kind = kind };
                    continue;
                }
            }
            result.Add(r);
        }
        return result;
    }

    // ------------------------------------------------------------------ systems

    /// <summary>
    /// Damped Newton's method for F(x) = 0 in n dimensions (F may return m values; m != n is
    /// solved in the least-squares sense). The Jacobian is computed by forward differences unless
    /// supplied. Each step solves J dx = -F with an SVD-based minimum-norm solve (robust for
    /// singular Jacobians) and uses a backtracking line search on ||F||^2. When the iteration
    /// fails and <paramref name="restarts"/> is positive, it is restarted from deterministic
    /// pseudo-random perturbations of x0 (growing in size) and the first success, or else the
    /// attempt with the smallest residual, is returned.
    /// </summary>
    public static SystemSolveResult NewtonSystem(Func<double[], double[]> F, double[] x0, double tolerance = 1e-12, int maxIterations = 100, Func<double[], Matrix>? jacobian = null, int restarts = 0, ulong seed = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(F);
        ArgumentNullException.ThrowIfNull(x0);
        var best = NewtonSystemOnce(F, x0, tolerance, maxIterations, jacobian, cancellationToken);
        if (best.Converged || restarts <= 0) return best;
        var rng = new SplitMix64(seed);
        for (int r = 1; r <= restarts; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double spread = Math.Pow(2, Math.Min(r, 20) / 2.0);
            var start = new double[x0.Length];
            for (int i = 0; i < start.Length; i++)
                start[i] = x0[i] + (2 * rng.NextDouble() - 1) * spread * (0.5 + Math.Abs(x0[i]));
            var attempt = NewtonSystemOnce(F, start, tolerance, maxIterations, jacobian, cancellationToken);
            if (attempt.Converged) return attempt;
            if (!(best.ResidualNorm <= attempt.ResidualNorm)) best = attempt;
        }
        return best with { Message = (best.Message ?? "Did not converge.") + $" ({restarts} restarts tried)" };
    }

    private static SystemSolveResult NewtonSystemOnce(Func<double[], double[]> F, double[] x0, double tolerance, int maxIterations, Func<double[], Matrix>? jacobian, CancellationToken cancellationToken)
    {
        double[] x = NumericsCore.Copy(x0);
        double[] fx = F(x);
        if (!NumericsCore.AllFinite(fx))
            return new SystemSolveResult(x, fx, double.NaN, 0, false, "F is not finite at the starting point.");
        double phi = 0.5 * NumericsCore.Dot(fx, fx);
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double res = NumericsCore.NormInf(fx);
            if (res <= tolerance) return new SystemSolveResult(x, fx, res, iter - 1, true);

            Matrix J = jacobian?.Invoke(x) ?? ForwardJacobian(F, x, fx);
            double[] rhs = new double[fx.Length];
            for (int i = 0; i < rhs.Length; i++) rhs[i] = -fx[i];
            double[] dx;
            try
            {
                dx = new SvdDecomposition(J).Solve(rhs);
            }
            catch (ConvergenceException)
            {
                return new SystemSolveResult(x, fx, res, iter, false, "Could not factor the Jacobian.");
            }
            if (!NumericsCore.AllFinite(dx))
                return new SystemSolveResult(x, fx, res, iter, false, "The Jacobian is not finite.");

            double t = 1;
            double[] xn = x, fn = fx;
            double phin = phi;
            bool accepted = false;
            while (t >= 1e-10)
            {
                xn = NumericsCore.Add(x, dx, t);
                fn = F(xn);
                if (NumericsCore.AllFinite(fn))
                {
                    phin = 0.5 * NumericsCore.Dot(fn, fn);
                    if (phin <= (1 - 1e-4 * t) * phi) { accepted = true; break; }
                }
                t *= 0.5;
            }
            if (!accepted)
            {
                bool stepTiny = NumericsCore.NormInf(dx) <= 1e-14 * (1 + NumericsCore.NormInf(x));
                return new SystemSolveResult(x, fx, res, iter, stepTiny && res <= Math.Sqrt(tolerance),
                    stepTiny ? null : "Line search failed: the iteration is stuck at a local minimum of |F| that is not a solution.");
            }
            double stepNorm = t * NumericsCore.NormInf(dx);
            x = xn; fx = fn; phi = phin;
            if (t == 1 && stepNorm <= 1e-15 * (1 + NumericsCore.NormInf(x)))
            {
                double r2 = NumericsCore.NormInf(fx);
                return new SystemSolveResult(x, fx, r2, iter, r2 <= Math.Max(tolerance, 1e-8));
            }
        }
        double rf = NumericsCore.NormInf(fx);
        return new SystemSolveResult(x, fx, rf, maxIterations, rf <= tolerance, rf <= tolerance ? null : "Iteration limit reached.");
    }

    internal static Matrix ForwardJacobian(Func<double[], double[]> F, double[] x, double[] fx)
    {
        int n = x.Length, m = fx.Length;
        var J = new Matrix(m, n);
        double[] xp = NumericsCore.Copy(x);
        for (int j = 0; j < n; j++)
        {
            double h = NumericsCore.SqrtEps * Math.Max(1.0, Math.Abs(x[j]));
            xp[j] = x[j] + h;
            double hh = xp[j] - x[j];
            double[] fp = F(xp);
            if (!NumericsCore.AllFinite(fp))
            {
                xp[j] = x[j] - h;
                hh = xp[j] - x[j];
                fp = F(xp);
            }
            for (int i = 0; i < m; i++) J[i, j] = (fp[i] - fx[i]) / hh;
            xp[j] = x[j];
        }
        return J;
    }

    /// <summary>
    /// Searches a box for several solutions of F(x) = 0 by running <see cref="NewtonSystem"/>
    /// from the box centre and from deterministic pseudo-random starting points. Returns the
    /// distinct converged solutions inside the box, sorted lexicographically.
    /// </summary>
    public static IReadOnlyList<SystemSolveResult> FindSystemSolutions(Func<double[], double[]> F, double[] lower, double[] upper, int starts = 64, ulong seed = 12345, double tolerance = 1e-10, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(F);
        if (lower.Length != upper.Length) throw new ArgumentException("Bounds must have the same dimension.");
        int n = lower.Length;
        var rng = new SplitMix64(seed);
        var found = new List<SystemSolveResult>();
        for (int s = 0; s < Math.Max(1, starts); s++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x0 = new double[n];
            for (int i = 0; i < n; i++)
                x0[i] = s == 0 ? 0.5 * (lower[i] + upper[i]) : lower[i] + (upper[i] - lower[i]) * rng.NextDouble();
            SystemSolveResult r;
            try { r = NewtonSystemOnce(F, x0, tolerance, 100, null, cancellationToken); }
            catch (ArgumentException) { continue; }
            if (!r.Converged) continue;
            bool inside = true;
            for (int i = 0; i < n; i++)
            {
                double slack = 1e-9 * Math.Max(1.0, upper[i] - lower[i]);
                if (r.X[i] < lower[i] - slack || r.X[i] > upper[i] + slack) { inside = false; break; }
            }
            if (!inside) continue;
            bool duplicate = false;
            foreach (var q in found)
            {
                double dist = 0;
                for (int i = 0; i < n; i++) dist = Math.Max(dist, Math.Abs(q.X[i] - r.X[i]) / Math.Max(1.0, Math.Abs(q.X[i])));
                if (dist <= 1e-6) { duplicate = true; break; }
            }
            if (!duplicate) found.Add(r);
        }
        found.Sort((p, q) =>
        {
            for (int i = 0; i < n; i++)
            {
                int c = p.X[i].CompareTo(q.X[i]);
                if (Math.Abs(p.X[i] - q.X[i]) > 1e-9 * Math.Max(1.0, Math.Abs(p.X[i])) && c != 0) return c;
            }
            return 0;
        });
        return found;
    }
}
