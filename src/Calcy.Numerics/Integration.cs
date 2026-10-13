namespace Calcy.Numerics;

/// <summary>Result of a numerical integration.</summary>
/// <param name="Value">Integral estimate.</param>
/// <param name="ErrorEstimate">Estimated absolute error.</param>
/// <param name="Evaluations">Number of integrand evaluations.</param>
/// <param name="Success">True if the requested tolerance was met.</param>
/// <param name="Message">Explanation when unsuccessful.</param>
public sealed record IntegrationResult(double Value, double ErrorEstimate, int Evaluations, bool Success, string? Message = null);

/// <summary>Rule used by <see cref="Integration.RiemannSum"/>.</summary>
public enum RiemannRule
{
    /// <summary>Left endpoint of each subinterval.</summary>
    Left,

    /// <summary>Right endpoint of each subinterval.</summary>
    Right,

    /// <summary>Midpoint of each subinterval.</summary>
    Midpoint,
}

/// <summary>
/// Numerical integration: adaptive Gauss-Kronrod (G7/K15) with global error control,
/// tanh-sinh (double exponential) quadrature for endpoint singularities, infinite ranges by
/// variable transformation, Romberg, composite rules for teaching and 2D integrals.
/// </summary>
public static class Integration
{
    private const double Eps = NumericsCore.Eps;

    // Kronrod nodes (positive, descending) and weights for the 15-point rule, and the
    // weights of the embedded 7-point Gauss rule (QUADPACK qk15).
    private static readonly double[] Xgk =
    [
        0.991455371120812639206854697526329, 0.949107912342758524526189684047851,
        0.864864423359769072789712788640926, 0.741531185599394439863864773280788,
        0.586087235467691130294144845693013, 0.405845151377397166906606412076961,
        0.207784955007898467600689403773245, 0.000000000000000000000000000000000,
    ];

    private static readonly double[] Wgk =
    [
        0.022935322010529224963732008058970, 0.063092092629978553290700663189204,
        0.104790010322250183839876322541518, 0.140653259715525918745189590510238,
        0.169004726639267902826583426598550, 0.190350578064785409913256402421014,
        0.204432940075298892414161999234649, 0.209482141084727828012999174891714,
    ];

    private static readonly double[] Wg =
    [
        0.129484966168869693270611432679082, 0.279705391489276667901467771423780,
        0.381830050505118944950369775488975, 0.417959183673469387755102040816327,
    ];

    // ------------------------------------------------------------------ top level

    /// <summary>
    /// Integrates f over [a, b] (either bound may be infinite) to the tolerance
    /// max(absTol, relTol |I|). Tries adaptive Gauss-Kronrod first and falls back to tanh-sinh
    /// quadrature (endpoint singularities such as 1/sqrt(x) or ln(x) at 0). Reversed bounds
    /// give the negated integral.
    /// </summary>
    public static IntegrationResult Integrate(Func<double, double> f, double a, double b, double relTol = 1e-10, double absTol = 1e-12, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (double.IsNaN(a) || double.IsNaN(b)) throw new ArgumentException("Integration bounds cannot be NaN.");
        if (a == b) return new IntegrationResult(0, 0, 0, true);
        if (a > b)
        {
            var r = Integrate(f, b, a, relTol, absTol, cancellationToken);
            return r with { Value = -r.Value };
        }
        if (double.IsInfinity(a) || double.IsInfinity(b))
        {
            Func<double, double> g;
            if (double.IsNegativeInfinity(a) && double.IsPositiveInfinity(b))
            {
                g = t =>
                {
                    double d = 1 - t * t;
                    double x = t / d;
                    if (!double.IsFinite(x) || d == 0) return 0;
                    return f(x) * (1 + t * t) / (d * d);
                };
                return IntegrateFinite(g, -1, 1, relTol, absTol, cancellationToken);
            }
            if (double.IsPositiveInfinity(b))
            {
                g = t =>
                {
                    double d = 1 - t;
                    double x = a + t / d;
                    if (!double.IsFinite(x) || d == 0) return 0;
                    return f(x) / (d * d);
                };
                return IntegrateFinite(g, 0, 1, relTol, absTol, cancellationToken);
            }
            g = t =>
            {
                double x = b - (1 - t) / t;
                if (!double.IsFinite(x) || t == 0) return 0;
                return f(x) / (t * t);
            };
            return IntegrateFinite(g, 0, 1, relTol, absTol, cancellationToken);
        }
        return IntegrateFinite(f, a, b, relTol, absTol, cancellationToken);
    }

    private static IntegrationResult IntegrateFinite(Func<double, double> f, double a, double b, double relTol, double absTol, CancellationToken ct)
    {
        var gk = GaussKronrod(f, a, b, relTol, absTol, 400, ct);
        if (gk.Success) return gk;
        var ts = TanhSinh(f, a, b, relTol, absTol, 12, ct);
        int evals = gk.Evaluations + ts.Evaluations;
        if (ts.Success) return ts with { Evaluations = evals };
        // Neither met the tolerance: continue Gauss-Kronrod with a larger budget, then report the best.
        var gk2 = GaussKronrod(f, a, b, relTol, absTol, 5000, ct);
        evals += gk2.Evaluations;
        if (gk2.Success) return gk2 with { Evaluations = evals };
        var best = double.IsNaN(ts.ErrorEstimate) || gk2.ErrorEstimate <= ts.ErrorEstimate ? gk2 : ts;
        return best with
        {
            Evaluations = evals,
            Success = false,
            Message = best.Message ?? "The requested accuracy was not reached; the integral may diverge or the integrand may be badly behaved.",
        };
    }

    // ------------------------------------------------------------------ Gauss-Kronrod

    private readonly record struct Segment(double A, double B, double Value, double Error);

    private static Segment Qk15(Func<double, double> f, double a, double b, ref bool nonFinite, ref double badX)
    {
        double centr = 0.5 * (a + b), hlgth = 0.5 * (b - a), dhlgth = Math.Abs(hlgth);
        var fv1 = new double[7];
        var fv2 = new double[7];
        double fc = f(centr);
        if (!double.IsFinite(fc)) { nonFinite = true; badX = centr; }
        double resg = fc * Wg[3], resk = fc * Wgk[7], resabs = Math.Abs(resk);
        for (int j = 0; j < 3; j++)
        {
            int jtw = 2 * j + 1;
            double absc = hlgth * Xgk[jtw];
            double f1 = f(centr - absc), f2 = f(centr + absc);
            if (!double.IsFinite(f1)) { nonFinite = true; badX = centr - absc; }
            if (!double.IsFinite(f2)) { nonFinite = true; badX = centr + absc; }
            fv1[jtw] = f1; fv2[jtw] = f2;
            double fsum = f1 + f2;
            resg += Wg[j] * fsum;
            resk += Wgk[jtw] * fsum;
            resabs += Wgk[jtw] * (Math.Abs(f1) + Math.Abs(f2));
        }
        for (int j = 0; j < 4; j++)
        {
            int jtwm1 = 2 * j;
            double absc = hlgth * Xgk[jtwm1];
            double f1 = f(centr - absc), f2 = f(centr + absc);
            if (!double.IsFinite(f1)) { nonFinite = true; badX = centr - absc; }
            if (!double.IsFinite(f2)) { nonFinite = true; badX = centr + absc; }
            fv1[jtwm1] = f1; fv2[jtwm1] = f2;
            double fsum = f1 + f2;
            resk += Wgk[jtwm1] * fsum;
            resabs += Wgk[jtwm1] * (Math.Abs(f1) + Math.Abs(f2));
        }
        double reskh = resk * 0.5;
        double resasc = Wgk[7] * Math.Abs(fc - reskh);
        for (int j = 0; j < 7; j++) resasc += Wgk[j] * (Math.Abs(fv1[j] - reskh) + Math.Abs(fv2[j] - reskh));
        double result = resk * hlgth;
        resabs *= dhlgth;
        resasc *= dhlgth;
        double abserr = Math.Abs((resk - resg) * hlgth);
        if (resasc != 0 && abserr != 0) abserr = resasc * Math.Min(1, Math.Pow(200 * abserr / resasc, 1.5));
        if (resabs > double.Epsilon / (50 * Eps)) abserr = Math.Max(Eps * 50 * resabs, abserr);
        return new Segment(a, b, result, abserr);
    }

    /// <summary>
    /// Adaptive Gauss-Kronrod (7/15 point) quadrature with global error control: the subinterval
    /// with the largest error is bisected until the total error estimate meets
    /// max(absTol, relTol |I|) or <paramref name="maxSubintervals"/> is reached.
    /// </summary>
    public static IntegrationResult GaussKronrod(Func<double, double> f, double a, double b, double relTol = 1e-10, double absTol = 1e-12, int maxSubintervals = 2000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentException("GaussKronrod requires finite bounds; use Integrate for infinite ranges.");
        if (a == b) return new IntegrationResult(0, 0, 0, true);
        bool nonFinite = false;
        double badX = double.NaN;
        var queue = new PriorityQueue<Segment, double>();
        var first = Qk15(f, a, b, ref nonFinite, ref badX);
        int evals = 15;
        if (nonFinite)
            return new IntegrationResult(double.NaN, double.PositiveInfinity, evals, false, $"The integrand is not finite at x = {badX}.");
        queue.Enqueue(first, -first.Error);
        double total = first.Value, totalErr = first.Error;
        var finished = new List<Segment>();
        int count = 1;
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double tol = Math.Max(absTol, relTol * Math.Abs(total));
            if (totalErr <= tol) break;
            if (count >= maxSubintervals) break;
            var s = queue.Dequeue();
            double mid = 0.5 * (s.A + s.B);
            if (!(mid > s.A && mid < s.B) || (s.B - s.A) <= 100 * Eps * Math.Max(Math.Abs(s.A), Math.Abs(s.B)))
            {
                // Cannot subdivide further (roundoff limit).
                finished.Add(s);
                if (queue.Count == 0) break;
                continue;
            }
            var left = Qk15(f, s.A, mid, ref nonFinite, ref badX);
            var right = Qk15(f, mid, s.B, ref nonFinite, ref badX);
            evals += 30;
            if (nonFinite)
                return new IntegrationResult(double.NaN, double.PositiveInfinity, evals, false, $"The integrand is not finite at x = {badX}.");
            total += left.Value + right.Value - s.Value;
            totalErr += left.Error + right.Error - s.Error;
            queue.Enqueue(left, -left.Error);
            queue.Enqueue(right, -right.Error);
            count++;
        }
        // Recompute the totals exactly to avoid drift.
        var sum = new NumericsCore.CompensatedSum();
        double err = 0;
        foreach (var (seg, _) in queue.UnorderedItems) { sum.Add(seg.Value); err += seg.Error; }
        foreach (var seg in finished) { sum.Add(seg.Value); err += seg.Error; }
        double value = sum.Value;
        bool ok = err <= Math.Max(absTol, relTol * Math.Abs(value));
        return new IntegrationResult(value, err, evals, ok, ok ? null : "Maximum number of subintervals reached before the tolerance was met.");
    }

    // ------------------------------------------------------------------ tanh-sinh

    /// <summary>
    /// Tanh-sinh (double exponential) quadrature on a finite interval. Very effective for
    /// analytic integrands with algebraic or logarithmic endpoint singularities. The error
    /// estimate is the difference between successive levels (conservative).
    /// </summary>
    public static IntegrationResult TanhSinh(Func<double, double> f, double a, double b, double relTol = 1e-10, double absTol = 1e-12, int maxLevels = 12, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentException("TanhSinh requires finite bounds; use Integrate for infinite ranges.");
        if (a == b) return new IntegrationResult(0, 0, 0, true);
        if (a > b)
        {
            var r = TanhSinh(f, b, a, relTol, absTol, maxLevels, cancellationToken);
            return r with { Value = -r.Value };
        }
        double c = 0.5 * (a + b), hw = 0.5 * (b - a);
        int evals = 0;
        string? problem = null;

        // Sum of w(t) [f(left) + f(right)] over t = start, start + step, ... until negligible.
        double SumAt(double start, double step)
        {
            var acc = new NumericsCore.CompensatedSum();
            for (int k = 0; ; k++)
            {
                double t = start + k * step;
                if (t > 7) break;
                double u = 0.5 * Math.PI * Math.Sinh(t);
                double e = Math.Exp(-2 * u);
                double w = 2 * Math.PI * Math.Cosh(t) * e / ((1 + e) * (1 + e));
                double delta = hw * 2 * e / (1 + e); // distance from the endpoint
                if (delta == 0 || w == 0) break;
                double xl = a + delta, xr = b - delta;
                if (xl == a && xr == b) break;
                double contrib = 0;
                bool nearEnd = delta < 1e-6 * hw;
                if (xl != a && xl < b)
                {
                    double fl = f(xl); evals++;
                    if (double.IsFinite(fl)) contrib += fl;
                    else if (!nearEnd) problem = $"The integrand is not finite at x = {xl}.";
                }
                if (xr != b && xr > a)
                {
                    double fr = f(xr); evals++;
                    if (double.IsFinite(fr)) contrib += fr;
                    else if (!nearEnd) problem = $"The integrand is not finite at x = {xr}.";
                }
                double term = w * contrib;
                acc.Add(term);
                if (k > 3 && Math.Abs(term) <= Eps * 1e-3 * Math.Abs(acc.Value) && delta < 1e-3 * hw) break;
            }
            return acc.Value;
        }

        double f0 = f(c); evals++;
        if (!double.IsFinite(f0))
            return new IntegrationResult(double.NaN, double.PositiveInfinity, evals, false, $"The integrand is not finite at x = {c}.");
        double h = 1;
        double sum = 0.5 * Math.PI * f0 + SumAt(h, h);
        double prev = hw * h * sum;
        double err = double.PositiveInfinity;
        double value = prev;
        for (int level = 1; level <= maxLevels; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            h *= 0.5;
            sum += SumAt(h, 2 * h);
            value = hw * h * sum;
            if (problem != null)
                return new IntegrationResult(double.NaN, double.PositiveInfinity, evals, false, problem);
            err = Math.Abs(value - prev);
            if (level >= 3 && err <= Math.Max(absTol, relTol * Math.Abs(value)))
                return new IntegrationResult(value, err, evals, true);
            prev = value;
        }
        return new IntegrationResult(value, err, evals, false, "Tanh-sinh quadrature did not reach the requested accuracy.");
    }

    // ------------------------------------------------------------------ Romberg and composite rules

    /// <summary>Romberg integration (trapezoid rule with Richardson extrapolation).</summary>
    public static IntegrationResult Romberg(Func<double, double> f, double a, double b, double relTol = 1e-10, double absTol = 1e-12, int maxLevels = 20, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentException("Romberg requires finite bounds.");
        if (a == b) return new IntegrationResult(0, 0, 0, true);
        double fa = f(a), fb = f(b);
        int evals = 2;
        if (!double.IsFinite(fa) || !double.IsFinite(fb))
            return new IntegrationResult(double.NaN, double.PositiveInfinity, evals, false, "The integrand is not finite at an endpoint; use Integrate or TanhSinh.");
        var prevRow = new double[] { 0.5 * (b - a) * (fa + fb) };
        double h = b - a;
        long panels = 1;
        double err = double.PositiveInfinity;
        for (int k = 1; k <= maxLevels; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            h *= 0.5;
            var sum = new NumericsCore.CompensatedSum();
            for (long i = 0; i < panels; i++)
            {
                double fx = f(a + (2 * i + 1) * h);
                if (!double.IsFinite(fx))
                    return new IntegrationResult(double.NaN, double.PositiveInfinity, evals, false, $"The integrand is not finite at x = {a + (2 * i + 1) * h}.");
                sum.Add(fx);
            }
            evals += (int)panels;
            panels *= 2;
            var row = new double[k + 1];
            row[0] = 0.5 * prevRow[0] + h * sum.Value;
            double factor = 1;
            for (int j = 1; j <= k; j++)
            {
                factor *= 4;
                row[j] = row[j - 1] + (row[j - 1] - prevRow[j - 1]) / (factor - 1);
            }
            err = Math.Abs(row[k] - prevRow[k - 1]);
            if (k >= 4 && err <= Math.Max(absTol, relTol * Math.Abs(row[k])))
                return new IntegrationResult(row[k], err, evals, true);
            prevRow = row;
        }
        return new IntegrationResult(prevRow[^1], err, evals, false, "Romberg integration did not converge.");
    }

    /// <summary>Composite trapezoid rule with n subintervals.</summary>
    public static double Trapezoid(Func<double, double> f, double a, double b, int n)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "The number of subintervals must be at least 1.");
        double h = (b - a) / n;
        var s = new NumericsCore.CompensatedSum();
        s.Add(0.5 * (f(a) + f(b)));
        for (int i = 1; i < n; i++) s.Add(f(a + i * h));
        return s.Value * h;
    }

    /// <summary>Composite Simpson rule with n subintervals (n must be even).</summary>
    public static double Simpson(Func<double, double> f, double a, double b, int n)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (n < 2 || n % 2 != 0) throw new ArgumentOutOfRangeException(nameof(n), "Simpson's rule requires an even number of subintervals (at least 2).");
        double h = (b - a) / n;
        var s = new NumericsCore.CompensatedSum();
        s.Add(f(a) + f(b));
        for (int i = 1; i < n; i++) s.Add((i % 2 == 1 ? 4 : 2) * f(a + i * h));
        return s.Value * h / 3;
    }

    /// <summary>Riemann sum with n equal subintervals using the left, right or midpoint rule.</summary>
    public static double RiemannSum(Func<double, double> f, double a, double b, int n, RiemannRule rule)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "The number of subintervals must be at least 1.");
        double h = (b - a) / n;
        double offset = rule switch
        {
            RiemannRule.Left => 0,
            RiemannRule.Right => 1,
            _ => 0.5,
        };
        var s = new NumericsCore.CompensatedSum();
        for (int i = 0; i < n; i++) s.Add(f(a + (i + offset) * h));
        return s.Value * h;
    }

    // ------------------------------------------------------------------ 2D

    /// <summary>Double integral of f(x, y) over the rectangle [x0, x1] by [y0, y1] (iterated adaptive quadrature).</summary>
    public static IntegrationResult Integrate2D(Func<double, double, double> f, double x0, double x1, double y0, double y1, double relTol = 1e-8, double absTol = 1e-10, CancellationToken cancellationToken = default)
        => Integrate2D(f, x0, x1, _ => y0, _ => y1, relTol, absTol, cancellationToken);

    /// <summary>
    /// Double integral of f(x, y) for x in [x0, x1] and y between yLower(x) and yUpper(x)
    /// (iterated adaptive quadrature; bounds may be infinite).
    /// </summary>
    public static IntegrationResult Integrate2D(Func<double, double, double> f, double x0, double x1, Func<double, double> yLower, Func<double, double> yUpper, double relTol = 1e-8, double absTol = 1e-10, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        int evals = 0;
        bool innerOk = true;
        double innerErr = 0;
        string? message = null;
        double Inner(double x)
        {
            var r = Integrate(y => f(x, y), yLower(x), yUpper(x), relTol * 0.1, absTol * 0.1, cancellationToken);
            evals += r.Evaluations;
            if (!r.Success)
            {
                innerOk = false;
                message ??= r.Message;
            }
            innerErr = Math.Max(innerErr, double.IsFinite(r.ErrorEstimate) ? r.ErrorEstimate : double.PositiveInfinity);
            return r.Value;
        }
        var outer = Integrate(Inner, x0, x1, relTol, absTol, cancellationToken);
        double width = double.IsFinite(x1 - x0) ? Math.Abs(x1 - x0) : 1;
        double err = outer.ErrorEstimate + width * innerErr;
        bool ok = outer.Success && innerOk;
        return new IntegrationResult(outer.Value, err, evals, ok, ok ? null : (outer.Message ?? message));
    }
}
