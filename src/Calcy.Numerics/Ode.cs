namespace Calcy.Numerics;

/// <summary>Which zero crossings of an event function are reported.</summary>
public enum EventDirection
{
    /// <summary>Both directions.</summary>
    Both,

    /// <summary>Only crossings from negative to positive.</summary>
    Rising,

    /// <summary>Only crossings from positive to negative.</summary>
    Falling,
}

/// <summary>Rosenbrock scheme used by <see cref="Ode.Rosenbrock"/>.</summary>
public enum RosenbrockScheme
{
    /// <summary>Rodas3: order 3, stiffly accurate and L-stable (default, most robust).</summary>
    Rodas3,

    /// <summary>Kaps-Rentrop with Shampine's parameters: order 4, A-stable.</summary>
    KapsRentrop4,
}

/// <summary>An event: the solver locates the times where Condition(t, y) crosses zero.</summary>
/// <param name="Condition">Event function g(t, y).</param>
/// <param name="Direction">Crossing direction to detect.</param>
/// <param name="Terminal">If true, integration stops at the first such event.</param>
public sealed record OdeEvent(Func<double, double[], double> Condition, EventDirection Direction = EventDirection.Both, bool Terminal = false);

/// <summary>A detected event.</summary>
/// <param name="EventIndex">Index into <see cref="OdeOptions.Events"/>.</param>
/// <param name="T">Event time.</param>
/// <param name="Y">State at the event.</param>
public sealed record OdeEventHit(int EventIndex, double T, double[] Y);

/// <summary>Options for the adaptive ODE solvers.</summary>
public sealed record OdeOptions
{
    /// <summary>Relative tolerance per component.</summary>
    public double RelativeTolerance { get; init; } = 1e-6;

    /// <summary>Absolute tolerance per component.</summary>
    public double AbsoluteTolerance { get; init; } = 1e-9;

    /// <summary>Initial step size (chosen automatically when null).</summary>
    public double? InitialStep { get; init; }

    /// <summary>Maximum step size (unbounded when null).</summary>
    public double? MaxStep { get; init; }

    /// <summary>Maximum number of steps (accepted plus rejected).</summary>
    public int MaxSteps { get; init; } = 200_000;

    /// <summary>
    /// Times at which to report the solution (obtained by dense output). When null every
    /// accepted step is reported.
    /// </summary>
    public IReadOnlyList<double>? OutputTimes { get; init; }

    /// <summary>Events to detect.</summary>
    public IReadOnlyList<OdeEvent>? Events { get; init; }
}

/// <summary>Solution of an initial value problem.</summary>
public sealed class OdeSolution
{
    private readonly List<IDenseSegment> _segments;

    internal OdeSolution(List<double> t, List<double[]> y, List<IDenseSegment> segments, bool success, string? message,
        int evaluations, int accepted, int rejected, int jacobians, List<OdeEventHit> events)
    {
        T = t;
        Y = y;
        _segments = segments;
        Success = success;
        Message = message;
        FunctionEvaluations = evaluations;
        AcceptedSteps = accepted;
        RejectedSteps = rejected;
        JacobianEvaluations = jacobians;
        Events = events;
    }

    /// <summary>Output times.</summary>
    public IReadOnlyList<double> T { get; }

    /// <summary>States at the output times.</summary>
    public IReadOnlyList<double[]> Y { get; }

    /// <summary>True if the integration reached the final time (or a terminal event).</summary>
    public bool Success { get; }

    /// <summary>Explanation when unsuccessful.</summary>
    public string? Message { get; }

    /// <summary>Number of right-hand side evaluations.</summary>
    public int FunctionEvaluations { get; }

    /// <summary>Accepted steps.</summary>
    public int AcceptedSteps { get; }

    /// <summary>Rejected steps.</summary>
    public int RejectedSteps { get; }

    /// <summary>Jacobian evaluations (stiff solver).</summary>
    public int JacobianEvaluations { get; }

    /// <summary>Detected events in chronological order.</summary>
    public IReadOnlyList<OdeEventHit> Events { get; }

    /// <summary>Final time reached.</summary>
    public double FinalTime => T.Count > 0 ? T[^1] : double.NaN;

    /// <summary>Final state.</summary>
    public double[] FinalState => Y.Count > 0 ? (double[])Y[^1].Clone() : [];

    /// <summary>
    /// Evaluates the solution at any time within the integrated range using the solver's
    /// continuous extension (4th order for Dormand-Prince, cubic Hermite otherwise).
    /// </summary>
    public double[] Evaluate(double t)
    {
        if (_segments.Count == 0)
        {
            if (Y.Count > 0) return (double[])Y[0].Clone();
            throw new InvalidOperationException("The solution is empty.");
        }
        double t0 = _segments[0].T0, tEnd = _segments[^1].T1;
        bool forward = tEnd >= t0;
        double lo = Math.Min(t0, tEnd), hi = Math.Max(t0, tEnd);
        double tol = 1e-12 * Math.Max(1.0, Math.Max(Math.Abs(lo), Math.Abs(hi)));
        if (t < lo - tol || t > hi + tol)
            throw new ArgumentOutOfRangeException(nameof(t), $"t = {t} is outside the integrated range [{lo}, {hi}].");
        // Binary search for the segment.
        int a = 0, b = _segments.Count - 1;
        while (a < b)
        {
            int m = (a + b) / 2;
            var s = _segments[m];
            bool beforeEnd = forward ? t <= s.T1 : t >= s.T1;
            if (beforeEnd) b = m; else a = m + 1;
        }
        return _segments[a].Evaluate(t);
    }
}

internal interface IDenseSegment
{
    double T0 { get; }
    double T1 { get; }
    double[] Evaluate(double t);
}

internal sealed class HermiteSegment(double t0, double t1, double[] y0, double[] y1, double[] f0, double[] f1) : IDenseSegment
{
    public double T0 { get; } = t0;
    public double T1 { get; } = t1;

    public double[] Evaluate(double t)
    {
        double h = T1 - T0;
        int n = y0.Length;
        var r = new double[n];
        if (h == 0) { Array.Copy(y0, r, n); return r; }
        double s = (t - T0) / h;
        double h00 = (1 + 2 * s) * (1 - s) * (1 - s), h10 = s * (1 - s) * (1 - s);
        double h01 = s * s * (3 - 2 * s), h11 = s * s * (s - 1);
        for (int i = 0; i < n; i++) r[i] = h00 * y0[i] + h10 * h * f0[i] + h01 * y1[i] + h11 * h * f1[i];
        return r;
    }
}

internal sealed class DopriSegment(double t0, double t1, double[] r1, double[] r2, double[] r3, double[] r4, double[] r5) : IDenseSegment
{
    public double T0 { get; } = t0;
    public double T1 { get; } = t1;

    public double[] Evaluate(double t)
    {
        double h = T1 - T0;
        double s = h == 0 ? 0 : (t - T0) / h, s1 = 1 - s;
        var r = new double[r1.Length];
        for (int i = 0; i < r.Length; i++) r[i] = r1[i] + s * (r2[i] + s1 * (r3[i] + s * (r4[i] + s1 * r5[i])));
        return r;
    }
}

/// <summary>
/// Ordinary differential equation solvers for systems y' = f(t, y): fixed-step Euler, Heun and
/// RK4 for teaching, adaptive Dormand-Prince 5(4) with dense output and events, a stiff
/// Rosenbrock solver (Kaps-Rentrop, Shampine parameters, order 4 with embedded order 3 and a
/// numerical Jacobian), and a shooting method for two-point boundary value problems.
/// </summary>
public static class Ode
{
    private const double Eps = NumericsCore.Eps;

    // ------------------------------------------------------------------ fixed step

    /// <summary>Explicit Euler method with a fixed number of steps.</summary>
    public static OdeSolution Euler(Func<double, double[], double[]> f, double t0, double[] y0, double t1, int steps, CancellationToken cancellationToken = default)
        => FixedStep(f, t0, y0, t1, steps, 1, cancellationToken);

    /// <summary>Heun's method (explicit trapezoid, order 2) with a fixed number of steps.</summary>
    public static OdeSolution Heun(Func<double, double[], double[]> f, double t0, double[] y0, double t1, int steps, CancellationToken cancellationToken = default)
        => FixedStep(f, t0, y0, t1, steps, 2, cancellationToken);

    /// <summary>Classical fourth-order Runge-Kutta method with a fixed number of steps.</summary>
    public static OdeSolution RungeKutta4(Func<double, double[], double[]> f, double t0, double[] y0, double t1, int steps, CancellationToken cancellationToken = default)
        => FixedStep(f, t0, y0, t1, steps, 4, cancellationToken);

    private static OdeSolution FixedStep(Func<double, double[], double[]> f, double t0, double[] y0, double t1, int steps, int method, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(y0);
        if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps), "The number of steps must be at least 1.");
        int n = y0.Length;
        double h = (t1 - t0) / steps;
        var ts = new List<double> { t0 };
        var ys = new List<double[]> { NumericsCore.Copy(y0) };
        var segs = new List<IDenseSegment>();
        double[] y = NumericsCore.Copy(y0);
        double t = t0;
        double[] k1 = f(t, y);
        int evals = 1;
        for (int s = 0; s < steps; s++)
        {
            ct.ThrowIfCancellationRequested();
            double tn = s == steps - 1 ? t1 : t0 + (s + 1) * h;
            double hs = tn - t;
            var yn = new double[n];
            switch (method)
            {
                case 1:
                    for (int i = 0; i < n; i++) yn[i] = y[i] + hs * k1[i];
                    break;
                case 2:
                {
                    var yp = new double[n];
                    for (int i = 0; i < n; i++) yp[i] = y[i] + hs * k1[i];
                    var k2 = f(tn, yp); evals++;
                    for (int i = 0; i < n; i++) yn[i] = y[i] + 0.5 * hs * (k1[i] + k2[i]);
                    break;
                }
                default:
                {
                    var tmp = new double[n];
                    for (int i = 0; i < n; i++) tmp[i] = y[i] + 0.5 * hs * k1[i];
                    var k2 = f(t + 0.5 * hs, tmp);
                    for (int i = 0; i < n; i++) tmp[i] = y[i] + 0.5 * hs * k2[i];
                    var k3 = f(t + 0.5 * hs, tmp);
                    for (int i = 0; i < n; i++) tmp[i] = y[i] + hs * k3[i];
                    var k4 = f(tn, tmp);
                    evals += 3;
                    for (int i = 0; i < n; i++) yn[i] = y[i] + hs / 6 * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]);
                    break;
                }
            }
            if (!NumericsCore.AllFinite(yn))
                return new OdeSolution(ts, ys, segs, false, $"The solution became non-finite at t = {tn}.", evals, s, 0, 0, []);
            double[] kn = f(tn, yn); evals++;
            segs.Add(new HermiteSegment(t, tn, y, yn, k1, kn));
            ts.Add(tn);
            ys.Add(yn);
            y = yn; t = tn; k1 = kn;
        }
        return new OdeSolution(ts, ys, segs, true, null, evals, steps, 0, 0, []);
    }

    // ------------------------------------------------------------------ Dormand-Prince

    private const double C2 = 1.0 / 5, C3 = 3.0 / 10, C4 = 4.0 / 5, C5 = 8.0 / 9;
    private const double A21 = 1.0 / 5;
    private const double A31 = 3.0 / 40, A32 = 9.0 / 40;
    private const double A41 = 44.0 / 45, A42 = -56.0 / 15, A43 = 32.0 / 9;
    private const double A51 = 19372.0 / 6561, A52 = -25360.0 / 2187, A53 = 64448.0 / 6561, A54 = -212.0 / 729;
    private const double A61 = 9017.0 / 3168, A62 = -355.0 / 33, A63 = 46732.0 / 5247, A64 = 49.0 / 176, A65 = -5103.0 / 18656;
    private const double A71 = 35.0 / 384, A73 = 500.0 / 1113, A74 = 125.0 / 192, A75 = -2187.0 / 6784, A76 = 11.0 / 84;
    private const double E1 = 71.0 / 57600, E3 = -71.0 / 16695, E4 = 71.0 / 1920, E5 = -17253.0 / 339200, E6 = 22.0 / 525, E7 = -1.0 / 40;
    private const double D1 = -12715105075.0 / 11282082432.0, D3 = 87487479700.0 / 32700410799.0, D4 = -10690763975.0 / 1880347072.0,
        D5 = 701980252875.0 / 199316789632.0, D6 = -1453857185.0 / 822651844.0, D7 = 69997945.0 / 29380423.0;

    /// <summary>
    /// Adaptive Dormand-Prince 5(4) integration from t0 to t1 with PI step-size control, dense
    /// output (<see cref="OdeSolution.Evaluate"/>) and event detection. Integration backwards
    /// in time (t1 &lt; t0) is supported.
    /// </summary>
    public static OdeSolution DormandPrince(Func<double, double[], double[]> f, double t0, double[] y0, double t1, OdeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(y0);
        options ??= new OdeOptions();
        int n = y0.Length;
        var run = new Run(options, t0, t1, y0);
        if (t0 == t1) return run.Finish(true, null);
        double dir = Math.Sign(t1 - t0);
        double rtol = options.RelativeTolerance, atol = options.AbsoluteTolerance;
        double hmax = Math.Abs(options.MaxStep ?? Math.Abs(t1 - t0));

        double t = t0;
        double[] y = NumericsCore.Copy(y0);
        double[] k1 = f(t, y);
        run.Evals++;
        if (!NumericsCore.AllFinite(k1)) return run.Finish(false, "The right-hand side is not finite at the initial point.");
        double h = options.InitialStep is double h0 ? Math.Abs(h0) : InitialStep(f, t, y, k1, dir, 5, rtol, atol, hmax, ref run.Evals);
        h = Math.Min(h, hmax);
        double facold = 1e-4;
        const double beta = 0.04, expo1 = 0.2 - beta * 0.75, safe = 0.9, facc1 = 1.0 / 0.2, facc2 = 1.0 / 10;
        bool lastRejected = false;
        var k2 = new double[n]; var k3 = new double[n]; var k4 = new double[n]; var k5 = new double[n]; var k6 = new double[n];
        var tmp = new double[n];

        while (dir * (t1 - t) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (run.Accepted + run.Rejected >= options.MaxSteps) return run.Finish(false, $"Maximum number of steps reached at t = {t}.");
            if (h < 16 * Eps * Math.Max(Math.Abs(t), 1e-300)) return run.Finish(false, $"Step size underflow at t = {t}; the problem may be stiff or singular.");
            bool last = false;
            if (dir * (t + dir * h - t1) >= 0) { h = Math.Abs(t1 - t); last = true; }
            double hs = dir * h;

            for (int i = 0; i < n; i++) tmp[i] = y[i] + hs * A21 * k1[i];
            k2 = f(t + C2 * hs, tmp);
            for (int i = 0; i < n; i++) tmp[i] = y[i] + hs * (A31 * k1[i] + A32 * k2[i]);
            k3 = f(t + C3 * hs, tmp);
            for (int i = 0; i < n; i++) tmp[i] = y[i] + hs * (A41 * k1[i] + A42 * k2[i] + A43 * k3[i]);
            k4 = f(t + C4 * hs, tmp);
            for (int i = 0; i < n; i++) tmp[i] = y[i] + hs * (A51 * k1[i] + A52 * k2[i] + A53 * k3[i] + A54 * k4[i]);
            k5 = f(t + C5 * hs, tmp);
            for (int i = 0; i < n; i++) tmp[i] = y[i] + hs * (A61 * k1[i] + A62 * k2[i] + A63 * k3[i] + A64 * k4[i] + A65 * k5[i]);
            k6 = f(t + hs, tmp);
            var yn = new double[n];
            for (int i = 0; i < n; i++) yn[i] = y[i] + hs * (A71 * k1[i] + A73 * k3[i] + A74 * k4[i] + A75 * k5[i] + A76 * k6[i]);
            double tn = last ? t1 : t + hs;
            var k7 = f(tn, yn);
            run.Evals += 6;

            double err = 0;
            bool finite = NumericsCore.AllFinite(yn) && NumericsCore.AllFinite(k7);
            if (finite)
            {
                for (int i = 0; i < n; i++)
                {
                    double ei = hs * (E1 * k1[i] + E3 * k3[i] + E4 * k4[i] + E5 * k5[i] + E6 * k6[i] + E7 * k7[i]);
                    double sc = atol + rtol * Math.Max(Math.Abs(y[i]), Math.Abs(yn[i]));
                    double q = ei / sc;
                    err += q * q;
                }
                err = Math.Sqrt(err / Math.Max(1, n));
            }
            if (!finite || !double.IsFinite(err))
            {
                run.Rejected++;
                h *= 0.25;
                lastRejected = true;
                continue;
            }

            double fac11 = Math.Pow(err, expo1);
            double fac = fac11 / Math.Pow(facold, beta);
            fac = Math.Max(facc2, Math.Min(facc1, fac / safe));
            double hnew = h / fac;
            if (err <= 1)
            {
                facold = Math.Max(err, 1e-4);
                // Dense output coefficients.
                var r1 = NumericsCore.Copy(y);
                var r2 = new double[n]; var r3 = new double[n]; var r4 = new double[n]; var r5 = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double dy = yn[i] - y[i];
                    double bspl = hs * k1[i] - dy;
                    r2[i] = dy;
                    r3[i] = bspl;
                    r4[i] = dy - hs * k7[i] - bspl;
                    r5[i] = hs * (D1 * k1[i] + D3 * k3[i] + D4 * k4[i] + D5 * k5[i] + D6 * k6[i] + D7 * k7[i]);
                }
                var seg = new DopriSegment(t, tn, r1, r2, r3, r4, r5);
                run.Accepted++;
                bool stop = run.AddStep(seg, tn, yn);
                if (stop) return run.Finish(true, null);
                t = tn; y = yn; k1 = k7;
                if (Math.Abs(hnew) > hmax) hnew = hmax;
                if (lastRejected) hnew = Math.Min(hnew, h);
                lastRejected = false;
                h = hnew;
            }
            else
            {
                hnew = h / Math.Min(facc1, fac11 / safe);
                run.Rejected++;
                lastRejected = true;
                h = hnew;
            }
        }
        return run.Finish(true, null);
    }

    private static double InitialStep(Func<double, double[], double[]> f, double t, double[] y, double[] f0, double dir, int order, double rtol, double atol, double hmax, ref int evals)
    {
        int n = y.Length;
        double d0 = 0, d1 = 0;
        for (int i = 0; i < n; i++)
        {
            double sc = atol + rtol * Math.Abs(y[i]);
            d0 += (y[i] / sc) * (y[i] / sc);
            d1 += (f0[i] / sc) * (f0[i] / sc);
        }
        d0 = Math.Sqrt(d0 / Math.Max(1, n));
        d1 = Math.Sqrt(d1 / Math.Max(1, n));
        double h0 = d0 < 1e-5 || d1 < 1e-5 ? 1e-6 : 0.01 * d0 / d1;
        h0 = Math.Min(h0, hmax);
        var y1 = new double[n];
        for (int i = 0; i < n; i++) y1[i] = y[i] + dir * h0 * f0[i];
        double[] f1 = f(t + dir * h0, y1);
        evals++;
        double d2 = 0;
        for (int i = 0; i < n; i++)
        {
            double sc = atol + rtol * Math.Abs(y[i]);
            double v = (f1[i] - f0[i]) / sc;
            d2 += v * v;
        }
        d2 = Math.Sqrt(d2 / Math.Max(1, n)) / h0;
        if (!double.IsFinite(d2)) return h0 * 1e-3;
        double dmax = Math.Max(d1, d2);
        double h1 = dmax <= 1e-15 ? Math.Max(1e-6, h0 * 1e-3) : Math.Pow(0.01 / dmax, 1.0 / (order + 1));
        return Math.Min(Math.Min(100 * h0, h1), hmax);
    }

    // ------------------------------------------------------------------ Rosenbrock (stiff)

    /// <summary>
    /// Coefficients of a Rosenbrock method in the form
    /// (I/(gamma h) - J) K_i = f(t + alpha_i h, y + sum_j A_ij K_j) + sum_j (C_ij / h) K_j + h gammaT_i df/dt,
    /// y_new = y + sum M_i K_i, error = sum E_i K_i.
    /// </summary>
    private sealed class RosenbrockTableau
    {
        public required double Gamma { get; init; }
        public required double[][] A { get; init; }
        public required double[][] C { get; init; }
        public required double[] M { get; init; }
        public required double[] E { get; init; }
        public required double[] Alpha { get; init; }
        public required double[] GammaT { get; init; }
        public required bool[] NewF { get; init; }
        public required double ErrorOrder { get; init; }
        public int Stages => M.Length;
    }

    // Rodas3 (Sandu et al. 1997): 4 stages, order 3, stiffly accurate and L-stable, embedded order 2.
    private static readonly RosenbrockTableau Rodas3 = new()
    {
        Gamma = 0.5,
        A = [[], [0.0], [2.0, 0.0], [2.0, 0.0, 1.0]],
        C = [[], [4.0], [1.0, -1.0], [1.0, -1.0, -8.0 / 3.0]],
        M = [2.0, 0.0, 1.0, 1.0],
        E = [0.0, 0.0, 0.0, 1.0],
        Alpha = [0.0, 0.0, 1.0, 1.0],
        GammaT = [0.5, 1.5, 0.0, 0.0],
        NewF = [true, false, true, true],
        ErrorOrder = 3,
    };

    // Kaps-Rentrop with Shampine's parameters: 4 stages, order 4, A-stable, embedded order 3.
    private static readonly RosenbrockTableau KapsRentrop = new()
    {
        Gamma = 0.5,
        A = [[], [2.0], [48.0 / 25, 6.0 / 25], [48.0 / 25, 6.0 / 25, 0.0]],
        C = [[], [-8.0], [372.0 / 25, 12.0 / 5], [-112.0 / 125, -54.0 / 125, -2.0 / 5]],
        M = [19.0 / 9, 1.0 / 2, 25.0 / 108, 125.0 / 108],
        E = [17.0 / 54, 7.0 / 36, 0.0, 125.0 / 108],
        Alpha = [0.0, 1.0, 3.0 / 5, 3.0 / 5],
        GammaT = [1.0 / 2, -3.0 / 2, 121.0 / 50, 29.0 / 250],
        NewF = [true, true, true, false],
        ErrorOrder = 4,
    };

    /// <summary>
    /// Stiff solver: linearly implicit Rosenbrock method with adaptive steps. The default scheme
    /// is Rodas3 (order 3, stiffly accurate and L-stable, very robust on stiff problems such as
    /// Robertson's chemical kinetics or the van der Pol oscillator with large mu);
    /// <see cref="RosenbrockScheme.KapsRentrop4"/> (order 4, A-stable) is more efficient on mildly
    /// stiff smooth problems. The Jacobian df/dy is computed by forward differences unless
    /// supplied, and df/dt numerically. Dense output is cubic Hermite.
    /// </summary>
    public static OdeSolution Rosenbrock(Func<double, double[], double[]> f, double t0, double[] y0, double t1, OdeOptions? options = null, Func<double, double[], Matrix>? jacobian = null, RosenbrockScheme scheme = RosenbrockScheme.Rodas3, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(y0);
        options ??= new OdeOptions();
        var tab = scheme == RosenbrockScheme.KapsRentrop4 ? KapsRentrop : Rodas3;
        int n = y0.Length, S = tab.Stages;
        var run = new Run(options, t0, t1, y0);
        if (t0 == t1) return run.Finish(true, null);
        double dir = Math.Sign(t1 - t0);
        double rtol = options.RelativeTolerance, atol = options.AbsoluteTolerance;
        double hmax = Math.Abs(options.MaxStep ?? Math.Abs(t1 - t0));
        double t = t0;
        double[] y = NumericsCore.Copy(y0);
        double[] fy = f(t, y);
        run.Evals++;
        if (!NumericsCore.AllFinite(fy)) return run.Finish(false, "The right-hand side is not finite at the initial point.");
        double h = options.InitialStep is double h0 ? Math.Abs(h0) : InitialStep(f, t, y, fy, dir, (int)tab.ErrorOrder, rtol, atol, hmax, ref run.Evals);
        h = Math.Min(h, hmax);
        const double facMin = 0.2, facMax = 6, facSafe = 0.9, facRej = 0.1;
        bool rejectLast = false, rejectMore = false;
        int jacs = 0;
        var K = new double[S][];

        while (dir * (t1 - t) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Matrix J;
            if (jacobian != null) J = jacobian(t, y);
            else { J = NumericJacobianOde(f, t, y, fy, atol); run.Evals += n; }
            jacs++;
            double dt = NumericsCore.SqrtEps * Math.Max(Math.Abs(t), Math.Max(h, 1e-8));
            double[] ft = f(t + dt, y);
            run.Evals++;
            var dfdt = new double[n];
            for (int i = 0; i < n; i++) dfdt[i] = (ft[i] - fy[i]) / dt;
            if (!NumericsCore.AllFinite(dfdt)) Array.Clear(dfdt);

            bool accepted = false;
            for (int attempt = 0; attempt < 60 && !accepted; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (run.Accepted + run.Rejected >= options.MaxSteps) return run.Finish(false, $"Maximum number of steps reached at t = {t}.", jacs);
                if (h < 16 * Eps * Math.Max(Math.Abs(t), 1e-300)) return run.Finish(false, $"Step size underflow at t = {t}.", jacs);
                bool last = false;
                if (dir * (t + dir * h - t1) >= 0) { h = Math.Abs(t1 - t); last = true; }
                double hs = dir * h;

                var G = new Matrix(n, n);
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) G[i, j] = -J[i, j];
                for (int i = 0; i < n; i++) G[i, i] += 1 / (tab.Gamma * hs);
                var lu = new LuDecomposition(G);
                if (lu.IsSingular || !G.IsFinite())
                {
                    run.Rejected++;
                    h *= 0.5;
                    continue;
                }

                double[] F = fy;
                bool finite = true;
                for (int s = 0; s < S && finite; s++)
                {
                    if (s > 0 && tab.NewF[s])
                    {
                        var yt = NumericsCore.Copy(y);
                        for (int j = 0; j < s; j++)
                        {
                            double a = tab.A[s][j];
                            if (a != 0) for (int i = 0; i < n; i++) yt[i] += a * K[j][i];
                        }
                        F = f(t + tab.Alpha[s] * hs, yt);
                        run.Evals++;
                        if (!NumericsCore.AllFinite(F)) { finite = false; break; }
                    }
                    var rhs = NumericsCore.Copy(F);
                    for (int j = 0; j < s; j++)
                    {
                        double c = tab.C[s][j] / hs;
                        if (c != 0) for (int i = 0; i < n; i++) rhs[i] += c * K[j][i];
                    }
                    double gt = tab.GammaT[s] * hs;
                    if (gt != 0) for (int i = 0; i < n; i++) rhs[i] += gt * dfdt[i];
                    K[s] = lu.Solve(rhs);
                }

                var yn = new double[n];
                double err = 0;
                if (finite)
                {
                    for (int i = 0; i < n; i++)
                    {
                        double v = y[i], e = 0;
                        for (int s = 0; s < S; s++)
                        {
                            v += tab.M[s] * K[s][i];
                            e += tab.E[s] * K[s][i];
                        }
                        yn[i] = v;
                        double sc = atol + rtol * Math.Max(Math.Abs(y[i]), Math.Abs(v));
                        err += (e / sc) * (e / sc);
                    }
                    err = Math.Sqrt(err / Math.Max(1, n));
                }
                if (!finite || !NumericsCore.AllFinite(yn) || !double.IsFinite(err))
                {
                    run.Rejected++;
                    h *= 0.25;
                    rejectLast = true;
                    continue;
                }

                double fac = Math.Min(facMax, Math.Max(facMin, facSafe / Math.Pow(Math.Max(err, 1e-300), 1.0 / tab.ErrorOrder)));
                double hnew = h * fac;
                if (err <= 1)
                {
                    double tn = last ? t1 : t + hs;
                    double[] fn = f(tn, yn);
                    run.Evals++;
                    if (!NumericsCore.AllFinite(fn))
                    {
                        run.Rejected++;
                        h *= 0.25;
                        rejectLast = true;
                        continue;
                    }
                    accepted = true;
                    run.Accepted++;
                    var seg = new HermiteSegment(t, tn, y, yn, fy, fn);
                    if (run.AddStep(seg, tn, yn)) return run.Finish(true, null, jacs);
                    t = tn; y = yn; fy = fn;
                    if (rejectLast) hnew = Math.Min(hnew, h);
                    rejectLast = false;
                    rejectMore = false;
                    h = Math.Min(hnew, hmax);
                }
                else
                {
                    run.Rejected++;
                    if (rejectMore) hnew = h * facRej;
                    rejectMore = rejectLast;
                    rejectLast = true;
                    h = hnew;
                }
            }
            if (!accepted) return run.Finish(false, $"Too many step rejections at t = {t}.", jacs);
        }
        return run.Finish(true, null, jacs);
    }

    private static Matrix NumericJacobianOde(Func<double, double[], double[]> f, double t, double[] y, double[] fy, double atol)
    {
        int n = y.Length;
        var J = new Matrix(n, n);
        double ynorm = NumericsCore.NormInf(y);
        double floor = Math.Max(1e-3 * ynorm, Math.Max(atol, 1e-10));
        var yp = NumericsCore.Copy(y);
        for (int j = 0; j < n; j++)
        {
            double delta = NumericsCore.SqrtEps * Math.Max(Math.Abs(y[j]), floor);
            yp[j] = y[j] + delta;
            double d = yp[j] - y[j];
            double[] fp = f(t, yp);
            yp[j] = y[j];
            for (int i = 0; i < n; i++) J[i, j] = (fp[i] - fy[i]) / d;
        }
        return J;
    }

    // ------------------------------------------------------------------ shared output handling

    private sealed class Run
    {
        private readonly OdeOptions _options;
        private readonly double _t0, _t1, _dir;
        private readonly List<double> _ts = [];
        private readonly List<double[]> _ys = [];
        private readonly List<IDenseSegment> _segments = [];
        private readonly List<OdeEventHit> _events = [];
        private readonly double[]? _eventValues;
        private readonly List<double>? _outputTimes;
        private int _nextOutput;
        public int Evals;
        public int Accepted;
        public int Rejected;

        public Run(OdeOptions options, double t0, double t1, double[] y0)
        {
            _options = options;
            _t0 = t0;
            _t1 = t1;
            _dir = Math.Sign(t1 - t0);
            if (options.OutputTimes != null)
            {
                _outputTimes = options.OutputTimes
                    .Where(t => _dir >= 0 ? t >= Math.Min(t0, t1) && t <= Math.Max(t0, t1) : t <= Math.Max(t0, t1) && t >= Math.Min(t0, t1))
                    .OrderBy(t => _dir * t).ToList();
                while (_nextOutput < _outputTimes.Count && _outputTimes[_nextOutput] == t0)
                {
                    _ts.Add(t0); _ys.Add(NumericsCore.Copy(y0)); _nextOutput++;
                }
            }
            else
            {
                _ts.Add(t0);
                _ys.Add(NumericsCore.Copy(y0));
            }
            if (options.Events is { Count: > 0 } evs)
            {
                _eventValues = new double[evs.Count];
                for (int i = 0; i < evs.Count; i++) _eventValues[i] = evs[i].Condition(t0, y0);
            }
        }

        /// <summary>Records an accepted step; returns true if a terminal event stopped the integration.</summary>
        public bool AddStep(IDenseSegment seg, double tn, double[] yn)
        {
            double tEnd = tn;
            double[] yEnd = yn;
            bool terminal = false;
            if (_eventValues != null)
            {
                var evs = _options.Events!;
                double tFirst = double.NaN;
                var hits = new List<OdeEventHit>();
                var newValues = new double[evs.Count];
                for (int i = 0; i < evs.Count; i++)
                {
                    double g0 = _eventValues[i];
                    double g1 = evs[i].Condition(tn, yn);
                    newValues[i] = g1;
                    if (!double.IsFinite(g0) || !double.IsFinite(g1)) continue;
                    bool rising = g0 < 0 && g1 >= 0, falling = g0 > 0 && g1 <= 0;
                    bool wanted = evs[i].Direction switch
                    {
                        EventDirection.Rising => rising,
                        EventDirection.Falling => falling,
                        _ => rising || falling,
                    };
                    if (!wanted) continue;
                    int idx = i;
                    Func<double, double> g = tt => evs[idx].Condition(tt, seg.Evaluate(tt));
                    double te;
                    if (g1 == 0) te = tn;
                    else
                    {
                        try
                        {
                            var r = RootFinding.Brent(g, seg.T0, tn, 1e-14 * Math.Max(1.0, Math.Abs(tn)), 200);
                            te = r.Root;
                        }
                        catch (ArgumentException)
                        {
                            te = tn;
                        }
                    }
                    hits.Add(new OdeEventHit(i, te, seg.Evaluate(te)));
                    if (evs[i].Terminal && (double.IsNaN(tFirst) || _dir * (te - tFirst) < 0)) tFirst = te;
                }
                hits.Sort((p, q) => (_dir * p.T).CompareTo(_dir * q.T));
                foreach (var hit in hits)
                {
                    if (!double.IsNaN(tFirst) && _dir * (hit.T - tFirst) > 0) continue;
                    _events.Add(hit);
                }
                Array.Copy(newValues, _eventValues, newValues.Length);
                if (!double.IsNaN(tFirst))
                {
                    terminal = true;
                    tEnd = tFirst;
                    yEnd = seg.Evaluate(tFirst);
                }
            }

            IDenseSegment stored = terminal ? new TruncatedSegment(seg, tEnd) : seg;
            _segments.Add(stored);
            if (_outputTimes != null)
            {
                while (_nextOutput < _outputTimes.Count && _dir * (_outputTimes[_nextOutput] - tEnd) <= 0)
                {
                    double to = _outputTimes[_nextOutput++];
                    _ts.Add(to);
                    _ys.Add(to == tEnd ? NumericsCore.Copy(yEnd) : seg.Evaluate(to));
                }
                if (terminal && (_ts.Count == 0 || _ts[^1] != tEnd)) { _ts.Add(tEnd); _ys.Add(NumericsCore.Copy(yEnd)); }
            }
            else
            {
                _ts.Add(tEnd);
                _ys.Add(NumericsCore.Copy(yEnd));
            }
            return terminal;
        }

        public OdeSolution Finish(bool success, string? message, int jacobians = 0) =>
            new(_ts, _ys, _segments, success, message, Evals, Accepted, Rejected, jacobians, _events);
    }

    private sealed class TruncatedSegment(IDenseSegment inner, double t1) : IDenseSegment
    {
        public double T0 => inner.T0;
        public double T1 { get; } = t1;
        public double[] Evaluate(double t) => inner.Evaluate(t);
    }

    // ------------------------------------------------------------------ boundary value problems

    /// <summary>
    /// Shooting method for the boundary value problem y'' = f(x, y, y'), y(a) = ya, y(b) = yb.
    /// The unknown initial slope is found by bracketing and Brent's method (secant fallback);
    /// each trial integrates with Dormand-Prince at tight tolerances.
    /// </summary>
    public static BvpResult Shooting(Func<double, double, double, double> f, double a, double ya, double b, double yb, double? slopeGuess = null, double tolerance = 1e-9, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        var opts = new OdeOptions { RelativeTolerance = 1e-11, AbsoluteTolerance = 1e-12 };
        Func<double, double[], double[]> sys = (x, y) => [y[1], f(x, y[0], y[1])];
        OdeSolution Solve(double s) => DormandPrince(sys, a, [ya, s], b, opts, cancellationToken);
        double Miss(double s)
        {
            var sol = Solve(s);
            if (!sol.Success) return double.NaN;
            return sol.FinalState[0] - yb;
        }

        double s0 = slopeGuess ?? (yb - ya) / (b - a);
        double m0 = Miss(s0);
        if (m0 == 0) return Finish(s0);
        // Search for a bracket around the guess with geometrically growing offsets.
        double step = Math.Max(1.0, Math.Abs(s0));
        double? lo = null, hi = null, mlo = null, mhi = null;
        for (int k = 0; k < 40 && lo == null; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (double sgn in new[] { 1.0, -1.0 })
            {
                double s1 = s0 + sgn * step * Math.Pow(1.6, k) * 0.25;
                double m1 = Miss(s1);
                if (double.IsFinite(m0) && double.IsFinite(m1) && (m0 > 0) != (m1 > 0))
                {
                    lo = Math.Min(s0, s1); hi = Math.Max(s0, s1);
                    mlo = s1 < s0 ? m1 : m0; mhi = s1 < s0 ? m0 : m1;
                    break;
                }
            }
        }
        if (lo != null)
        {
            var r = RootFinding.BrentCore(Miss, lo.Value, hi!.Value, mlo!.Value, mhi!.Value, tolerance, 200, cancellationToken);
            return Finish(r.Root);
        }
        var sec = RootFinding.Secant(Miss, s0, s0 + 1, tolerance, 100, cancellationToken);
        return Finish(sec.Root);

        BvpResult Finish(double slope)
        {
            var sol = Solve(slope);
            double miss = sol.Success ? sol.FinalState[0] - yb : double.NaN;
            bool ok = sol.Success && Math.Abs(miss) <= Math.Max(1e-7, 1e3 * tolerance) * Math.Max(1.0, Math.Abs(yb));
            return new BvpResult(slope, sol, ok, miss);
        }
    }
}

/// <summary>Result of the shooting method.</summary>
/// <param name="InitialSlope">The initial slope y'(a) that satisfies the boundary condition.</param>
/// <param name="Solution">The solution with state [y, y'].</param>
/// <param name="Converged">True if the far boundary condition is met.</param>
/// <param name="BoundaryError">y(b) - yb for the final slope.</param>
public sealed record BvpResult(double InitialSlope, OdeSolution Solution, bool Converged, double BoundaryError);
