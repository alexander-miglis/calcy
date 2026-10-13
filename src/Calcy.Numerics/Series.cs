namespace Calcy.Numerics;

/// <summary>Result of summing an infinite series.</summary>
/// <param name="Value">Estimated sum.</param>
/// <param name="ErrorEstimate">Estimated absolute error.</param>
/// <param name="TermsUsed">Number of terms evaluated.</param>
/// <param name="Converged">True if the estimate meets the tolerance.</param>
/// <param name="Method">Method that produced the estimate.</param>
/// <param name="Message">Explanation when not converged (for example a divergent series).</param>
public sealed record SeriesResult(double Value, double ErrorEstimate, long TermsUsed, bool Converged, string Method, string? Message = null);

/// <summary>
/// Summation of series: compensated finite sums and infinite sums sum_{k=start}^inf f(k)
/// with convergence acceleration (Wynn epsilon, Levin u-transform, Richardson extrapolation
/// on geometrically spaced partial sums for logarithmic convergence).
/// </summary>
public static class Series
{
    private const double Eps = NumericsCore.Eps;

    /// <summary>Finite sum sum_{k=from}^{to} f(k) with compensated (Neumaier) summation.</summary>
    public static double Sum(Func<double, double> term, long from, long to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(term);
        var s = new NumericsCore.CompensatedSum();
        for (long k = from; k <= to; k++)
        {
            if ((k & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            s.Add(term(k));
        }
        return s.Value;
    }

    /// <summary>Finite product prod_{k=from}^{to} f(k).</summary>
    public static double Product(Func<double, double> term, long from, long to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(term);
        double p = 1;
        for (long k = from; k <= to; k++)
        {
            if ((k & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            p *= term(k);
        }
        return p;
    }

    /// <summary>
    /// Infinite sum sum_{k=start}^inf f(k). Fast convergence is detected by direct summation;
    /// otherwise the Wynn epsilon algorithm, the Levin u-transform and (for positive monotone
    /// terms) Richardson extrapolation are applied and the most reliable estimate is returned.
    /// Divergence is reported when the terms do not tend to zero or decay no faster than 1/k.
    /// </summary>
    public static SeriesResult SumInfinite(Func<double, double> term, long start = 1, double relTol = 1e-10, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(term);
        long evals = 0;
        var cache = new Dictionary<long, double>();
        double A(long k)
        {
            if (cache.TryGetValue(k, out double v)) return v;
            v = term(k);
            evals++;
            if (cache.Count < 1_000_000) cache[k] = v;
            return v;
        }

        // 1. Direct summation with detection of fast convergence.
        const int directLimit = 4000;
        var partial = new List<double>(directLimit + 1);
        var sum = new NumericsCore.CompensatedSum();
        int smallRun = 0;
        for (int i = 0; i < directLimit; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            double a = A(start + i);
            if (!double.IsFinite(a))
                return new SeriesResult(double.NaN, double.PositiveInfinity, evals, false, "Direct", $"The term at k = {start + i} is not finite.");
            sum.Add(a);
            partial.Add(sum.Value);
            double s = sum.Value;
            if (Math.Abs(a) <= 0.25 * Eps * Math.Abs(s) || (a == 0 && i > 10)) smallRun++;
            else smallRun = 0;
            if (smallRun >= 8 && i >= 16)
                return new SeriesResult(s, 4 * Eps * Math.Abs(s), evals, true, "Direct");
        }

        // 2. Divergence checks.
        double aN = A(start + directLimit - 1);
        double tailMax = 0;
        for (int i = directLimit - 50; i < directLimit; i++) tailMax = Math.Max(tailMax, Math.Abs(A(start + i)));
        double headMax = 0;
        for (int i = 0; i < 50; i++) headMax = Math.Max(headMax, Math.Abs(A(start + i)));
        bool alternating = true, positive = true, negative = true;
        for (int i = directLimit - 40; i < directLimit; i++)
        {
            double a0 = A(start + i), a1 = A(start + i + 1);
            if (!(a0 * a1 < 0)) alternating = false;
            if (!(a0 > 0)) positive = false;
            if (!(a0 < 0)) negative = false;
        }
        if (tailMax > 0.5 * headMax && headMax > 0)
            return new SeriesResult(sum.Value, double.PositiveInfinity, evals, false, "Direct", "The series diverges: its terms do not tend to zero.");

        bool monotone = positive || negative;
        double p = double.NaN;
        if (monotone)
        {
            // Estimate the decay exponent from a(n) ~ C n^-p.
            long n1 = 1L << 14, n2 = 1L << 15;
            double a1 = Math.Abs(A(start + n1)), a2 = Math.Abs(A(start + n2));
            if (a1 > 0 && a2 > 0)
            {
                p = Math.Log(a1 / a2) / Math.Log(2.0);
                if (p <= 1 + 1e-3 && Math.Abs(a2) > 1e-300)
                    return new SeriesResult(sum.Value, double.PositiveInfinity, evals, false, "Direct", "The series appears to diverge: the terms decay no faster than 1/k.");
            }
        }

        var candidates = new List<SeriesResult>();
        // 3. Wynn epsilon on partial sums (alternating and linearly convergent series).
        {
            int count = Math.Min(partial.Count, 60);
            var seq = partial.GetRange(partial.Count - count, count).ToArray();
            var (value, err) = WynnEpsilon(alternating || !monotone ? partial.GetRange(0, Math.Min(partial.Count, 60)).ToArray() : seq);
            if (double.IsFinite(value)) candidates.Add(new SeriesResult(value, err, evals, false, "Wynn epsilon"));
        }
        // 4. Levin u-transform.
        {
            var (value, err) = LevinU(k => A(start + k), partial, 0, 22);
            if (double.IsFinite(value)) candidates.Add(new SeriesResult(value, err, evals, false, "Levin u-transform"));
        }
        // 5. Richardson extrapolation of S(n) ~ S + c1 n^-(p-1) + c2 n^-p + ... (monotone terms).
        if (monotone && double.IsFinite(p))
        {
            var (value, err) = Richardson(A, start, p, cancellationToken);
            if (double.IsFinite(value)) candidates.Add(new SeriesResult(value, err, evals, false, "Richardson extrapolation"));
        }

        if (candidates.Count == 0)
            return new SeriesResult(sum.Value, double.PositiveInfinity, evals, false, "Direct", "No acceleration method produced a finite estimate.");
        var best = candidates.OrderBy(c => c.ErrorEstimate).First();
        // Cross-check against another method when available: a large disagreement signals trouble.
        double errEst = best.ErrorEstimate;
        var second = candidates.Where(c => !ReferenceEquals(c, best)).OrderBy(c => c.ErrorEstimate).FirstOrDefault();
        if (second != null && double.IsFinite(second.ErrorEstimate))
        {
            double diff = Math.Abs(second.Value - best.Value);
            if (diff > 10 * (best.ErrorEstimate + second.ErrorEstimate)) errEst = Math.Max(errEst, diff);
        }
        errEst = Math.Max(errEst, 4 * Eps * Math.Abs(best.Value));
        bool ok = errEst <= relTol * Math.Max(Math.Abs(best.Value), 1e-300) || errEst <= 1e-300;
        return best with
        {
            ErrorEstimate = errEst,
            TermsUsed = evals,
            Converged = ok,
            Message = ok ? null : "The requested accuracy was not reached.",
        };
    }

    /// <summary>Wynn's epsilon algorithm applied to a sequence of partial sums.</summary>
    internal static (double Value, double Error) WynnEpsilon(double[] s)
    {
        int n = s.Length;
        if (n < 3) return (n > 0 ? s[^1] : double.NaN, double.PositiveInfinity);
        // e[k][j]: column k of the epsilon table; even columns hold estimates.
        var prev = new double[n + 1]; // column -1 (zeros)
        var cur = (double[])s.Clone(); // column 0
        double best = s[^1], bestErr = Math.Abs(s[^1] - s[^2]);
        double[] lastEven = cur;
        for (int k = 1; k < n; k++)
        {
            int len = n - k;
            var next = new double[len];
            bool ok = true;
            for (int j = 0; j < len; j++)
            {
                double d = cur[j + 1] - cur[j];
                if (d == 0 || !double.IsFinite(d)) { ok = false; break; }
                next[j] = prev[j + 1] + 1 / d;
            }
            if (!ok) break;
            prev = cur;
            cur = next;
            if (k % 2 == 0)
            {
                if (len >= 2)
                {
                    double est = cur[^1];
                    double err = Math.Abs(cur[^1] - cur[^2]) + Math.Abs(est - lastEven[^1]);
                    if (double.IsFinite(est) && err < bestErr)
                    {
                        best = est;
                        bestErr = err;
                    }
                }
                lastEven = cur;
            }
        }
        return (best, bestErr);
    }

    /// <summary>Levin u-transform with remainder estimates omega_n = (n + 1) a_n.</summary>
    private static (double Value, double Error) LevinU(Func<long, double> a, List<double> partial, int n0, int maxK)
    {
        double best = double.NaN, bestErr = double.PositiveInfinity, prevEst = double.NaN;
        for (int k = 2; k <= maxK && n0 + k < partial.Count; k++)
        {
            double num = 0, den = 0;
            bool ok = true;
            double binom = 1;
            for (int j = 0; j <= k; j++)
            {
                if (j > 0) binom = binom * (k - j + 1) / j;
                long nj = n0 + j;
                double an = a(nj);
                if (an == 0 || !double.IsFinite(an)) { ok = false; break; }
                double omega = (nj + 1) * an;
                double ratio = Math.Pow((double)(n0 + j + 1) / (n0 + k + 1), k - 1);
                double c = (j % 2 == 0 ? 1 : -1) * binom * ratio / omega;
                num += c * partial[(int)nj];
                den += c;
            }
            if (!ok || den == 0) break;
            double est = num / den;
            if (!double.IsFinite(est)) break;
            if (double.IsFinite(prevEst))
            {
                double err = Math.Abs(est - prevEst);
                if (err < bestErr) { bestErr = err; best = est; }
            }
            prevEst = est;
        }
        return (best, bestErr);
    }

    /// <summary>Richardson extrapolation on S(n_j) with n_j = 16 * 2^j, assuming error exponents p-1, p, p+1, ...</summary>
    private static (double Value, double Error) Richardson(Func<long, double> a, long start, double p, CancellationToken ct)
    {
        const int levels = 11;
        var S = new double[levels];
        var sum = new NumericsCore.CompensatedSum();
        long n = 0;
        for (int j = 0; j < levels; j++)
        {
            long target = 16L << j;
            for (; n < target; n++)
            {
                if ((n & 4095) == 0) ct.ThrowIfCancellationRequested();
                sum.Add(a(start + n));
            }
            S[j] = sum.Value;
        }
        // Round p to a nearby half-integer when it is clearly one (common for p-series).
        double pr = Math.Round(2 * p) / 2;
        if (Math.Abs(pr - p) < 2e-3) p = pr;
        var T = (double[])S.Clone();
        double best = S[^1], bestErr = Math.Abs(S[^1] - S[^2]);
        for (int k = 1; k < levels; k++)
        {
            double e = p - 1 + (k - 1);
            double f = Math.Pow(2, e);
            var next = new double[levels - k];
            for (int j = 0; j < next.Length; j++) next[j] = (f * T[j + 1] - T[j]) / (f - 1);
            T = next;
            if (T.Length >= 2)
            {
                double err = Math.Abs(T[^1] - T[^2]);
                if (err < bestErr) { bestErr = err; best = T[^1]; }
            }
        }
        return (best, bestErr);
    }
}
