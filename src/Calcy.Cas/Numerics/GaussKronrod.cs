namespace Calcy.Cas.Numerics;

/// <summary>Adaptive Gauss-Kronrod (7-15 point) quadrature with transformations for infinite intervals.</summary>
public static class GaussKronrod
{
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

    /// <summary>
    /// The integral of f over [a, b] (bounds may be infinite), or NaN when the integrand is not finite at a sample
    /// point or the requested accuracy is not reached.
    /// </summary>
    public static double Integrate(Func<double, double> f, double a, double b) => Integrate(f, a, b, 1e-10, 2000, out _);

    /// <summary>Integrates with a relative tolerance and a limit on subintervals, reporting the error estimate.</summary>
    public static double Integrate(Func<double, double> f, double a, double b, double relTol, int maxIntervals, out double error)
    {
        error = double.NaN;
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        if (a == b) { error = 0; return 0; }
        if (a > b) return -Integrate(f, b, a, relTol, maxIntervals, out error);
        Func<double, double> g;
        double lo, hi;
        if (double.IsNegativeInfinity(a) && double.IsPositiveInfinity(b))
        {
            g = t =>
            {
                double d = 1 - t * t;
                return f(t / d) * (1 + t * t) / (d * d);
            };
            lo = -1; hi = 1;
        }
        else if (double.IsPositiveInfinity(b))
        {
            g = t =>
            {
                double d = 1 - t;
                return f(a + t / d) / (d * d);
            };
            lo = 0; hi = 1;
        }
        else if (double.IsNegativeInfinity(a))
        {
            g = t => f(b - (1 - t) / t) / (t * t);
            lo = 0; hi = 1;
        }
        else
        {
            g = f;
            lo = a; hi = b;
        }
        return Adaptive(g, lo, hi, relTol, maxIntervals, out error);
    }

    private static double Adaptive(Func<double, double> f, double a, double b, double relTol, int maxIntervals, out double error)
    {
        var intervals = new List<(double A, double B, double Value, double Err)>();
        var first = Rule(f, a, b);
        if (double.IsNaN(first.Value))
        {
            error = double.NaN;
            return double.NaN;
        }
        intervals.Add((a, b, first.Value, first.Err));
        double total = first.Value, totalErr = first.Err;
        while (true)
        {
            double tol = Math.Max(1e-14, relTol * Math.Abs(total));
            if (totalErr <= tol) break;
            if (intervals.Count >= maxIntervals) break;
            int worst = 0;
            for (int i = 1; i < intervals.Count; i++)
                if (intervals[i].Err > intervals[worst].Err) worst = i;
            var (ia, ib, iv, ie) = intervals[worst];
            double mid = 0.5 * (ia + ib);
            if (mid <= ia || mid >= ib) break;
            var left = Rule(f, ia, mid);
            var right = Rule(f, mid, ib);
            if (double.IsNaN(left.Value) || double.IsNaN(right.Value))
            {
                error = double.NaN;
                return double.NaN;
            }
            intervals[worst] = (ia, mid, left.Value, left.Err);
            intervals.Add((mid, ib, right.Value, right.Err));
            total += left.Value + right.Value - iv;
            totalErr += left.Err + right.Err - ie;
        }
        // Recompute the sums to avoid drift from the incremental updates.
        total = 0;
        totalErr = 0;
        foreach (var iv in intervals)
        {
            total += iv.Value;
            totalErr += iv.Err;
        }
        error = totalErr;
        if (!double.IsFinite(total) || totalErr > 1e-6 * Math.Max(1, Math.Abs(total))) return double.NaN;
        return total;
    }

    private static (double Value, double Err) Rule(Func<double, double> f, double a, double b)
    {
        double c = 0.5 * (a + b), h = 0.5 * (b - a);
        double fc = f(c);
        if (!double.IsFinite(fc)) return (double.NaN, double.NaN);
        double kronrod = fc * Wgk[7];
        double gauss = fc * Wg[3];
        for (int j = 0; j < 7; j++)
        {
            double dx = h * Xgk[j];
            double f1 = f(c - dx), f2 = f(c + dx);
            if (!double.IsFinite(f1) || !double.IsFinite(f2)) return (double.NaN, double.NaN);
            kronrod += Wgk[j] * (f1 + f2);
            if (j % 2 == 1) gauss += Wg[j / 2] * (f1 + f2);
        }
        double value = kronrod * h;
        double err = Math.Abs((kronrod - gauss) * h);
        return (value, err);
    }
}
