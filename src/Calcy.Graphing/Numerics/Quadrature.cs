namespace Calcy.Graphing.Numerics;

/// <summary>The value of a definite integral with its estimated error.</summary>
/// <param name="Value">The integral (NaN when the function is undefined somewhere in the range).</param>
/// <param name="Error">Estimated absolute error.</param>
/// <param name="Converged">False when the error target was not reached (for example near a singularity).</param>
public readonly record struct QuadratureResult(double Value, double Error, bool Converged);

/// <summary>Adaptive Gauss-Kronrod (7-point Gauss, 15-point Kronrod) integration with global error control.</summary>
public static class Quadrature
{
    private static readonly double[] Xk =
    [
        0.991455371120812639206854697526329, 0.949107912342758524526189684047851, 0.864864423359769072789712788640926,
        0.741531185599394439863864773280788, 0.586087235467691130294144845693013, 0.405845151377397166906606412076961,
        0.207784955007898467600689403773245, 0.000000000000000000000000000000000,
    ];

    private static readonly double[] Wk =
    [
        0.022935322010529224963732008058970, 0.063092092629978553290700663189204, 0.104790010322250183839876322541518,
        0.140653259715525918745189590510238, 0.169004726639267902826583426598550, 0.190350578064785409913256402421014,
        0.204432940075298892414161999234649, 0.209482141084727828012999174891714,
    ];

    private static readonly double[] Wg =
    [
        0.129484966168869693270611432679082, 0.279705391489276667901467771423780, 0.381830050505118944950369775488975,
        0.417959183673469387755102040816327,
    ];

    /// <summary>Integrates f over [a, b] (a may exceed b, which flips the sign).</summary>
    public static QuadratureResult Integrate(Func<double, double> f, double a, double b, double absTolerance = 1e-12, double relTolerance = 1e-12, int maxIntervals = 4000)
    {
        if (a == b) return new QuadratureResult(0, 0, true);
        if (!double.IsFinite(a) || !double.IsFinite(b)) return new QuadratureResult(double.NaN, double.NaN, false);
        double sign = 1;
        if (a > b)
        {
            (a, b) = (b, a);
            sign = -1;
        }
        var heap = new PriorityQueue<(double A, double B, double Value, double Error), double>();
        var first = Rule(f, a, b);
        if (double.IsNaN(first.Value)) return new QuadratureResult(double.NaN, double.NaN, false);
        heap.Enqueue((a, b, first.Value, first.Error), -first.Error);
        double total = first.Value, error = first.Error;
        int count = 1;
        while (error > Math.Max(absTolerance, relTolerance * Math.Abs(total)) && count < maxIntervals)
        {
            var (ia, ib, iv, ie) = heap.Dequeue();
            double mid = 0.5 * (ia + ib);
            if (mid <= ia || mid >= ib)
            {
                // Cannot split further in double precision; keep it and stop.
                heap.Enqueue((ia, ib, iv, ie), double.PositiveInfinity);
                break;
            }
            var left = Rule(f, ia, mid);
            var right = Rule(f, mid, ib);
            if (double.IsNaN(left.Value) || double.IsNaN(right.Value)) return new QuadratureResult(double.NaN, double.NaN, false);
            total += left.Value + right.Value - iv;
            error += left.Error + right.Error - ie;
            heap.Enqueue((ia, mid, left.Value, left.Error), -left.Error);
            heap.Enqueue((mid, ib, right.Value, right.Error), -right.Error);
            count++;
        }
        // Re-sum to avoid drift from the incremental updates.
        double sum = 0, err = 0;
        foreach (var (_, _, v, e) in heap.UnorderedItems.Select(u => u.Element)) { sum += v; err += e; }
        bool converged = err <= Math.Max(absTolerance, relTolerance * Math.Abs(sum)) * 10;
        return new QuadratureResult(sign * sum, err, converged);
    }

    private static (double Value, double Error) Rule(Func<double, double> f, double a, double b)
    {
        double c = 0.5 * (a + b), h = 0.5 * (b - a);
        double fc = f(c);
        double kronrod = fc * Wk[7];
        double gauss = fc * Wg[3];
        for (int j = 0; j < 7; j++)
        {
            double dx = h * Xk[j];
            double f1 = f(c - dx), f2 = f(c + dx);
            kronrod += Wk[j] * (f1 + f2);
            if ((j & 1) == 1) gauss += Wg[j / 2] * (f1 + f2);
        }
        double value = kronrod * h;
        double error = Math.Abs((kronrod - gauss) * h);
        if (double.IsInfinity(value)) return (double.NaN, double.NaN);
        return (value, error);
    }
}
