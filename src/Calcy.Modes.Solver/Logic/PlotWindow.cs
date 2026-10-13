namespace Calcy.Modes.Solver.Logic;

/// <summary>The part of the plane a small graph shows.</summary>
/// <param name="XMin">Left edge.</param>
/// <param name="XMax">Right edge.</param>
/// <param name="YMin">Bottom edge.</param>
/// <param name="YMax">Top edge.</param>
public readonly record struct PlotWindow(double XMin, double XMax, double YMin, double YMax)
{
    /// <summary>The window used when there is nothing to show.</summary>
    public static PlotWindow Default => new(-5, 5, -5, 5);

    /// <summary>
    /// A window for a polynomial that shows its real roots and turning points with some room around them, includes the
    /// axes when they are near, and is tall enough for the interesting part of the curve (steep ends are cut off).
    /// </summary>
    public static PlotWindow ForPolynomial(IReadOnlyList<double> ascending, IEnumerable<double> pointsX, IEnumerable<double> pointsY)
    {
        var xs = pointsX.Where(double.IsFinite).ToList();
        double lo, hi;
        if (xs.Count == 0)
        {
            lo = -4;
            hi = 4;
        }
        else
        {
            lo = xs.Min();
            hi = xs.Max();
            double span = hi - lo;
            double pad = Math.Max(1, 0.22 * span);
            if (span < 1e-9) pad = Math.Max(2, 0.5 * Math.Abs(lo));
            lo -= pad;
            hi += pad;
        }
        // Keep the y-axis in view when it is close.
        double width = hi - lo;
        if (lo > 0 && lo < 0.6 * width) lo = -0.08 * width;
        if (hi < 0 && -hi < 0.6 * width) hi = 0.08 * width;

        // Tall enough for the turning points and the axis; without turning points, for the middle of the curve.
        var turning = pointsY.Where(double.IsFinite).ToList();
        var ys = turning.Append(0).ToList();
        if (turning.Count == 0)
        {
            var samples = new List<double>();
            for (int i = 0; i <= 200; i++)
            {
                double y = PolynomialMath.Evaluate(ascending, lo + (hi - lo) * i / 200);
                if (double.IsFinite(y)) samples.Add(y);
            }
            samples.Sort();
            if (samples.Count > 0)
            {
                ys.Add(samples[(int)(0.2 * (samples.Count - 1))]);
                ys.Add(samples[(int)(0.8 * (samples.Count - 1))]);
            }
        }
        double yLo = ys.Min(), yHi = ys.Max();
        if (yHi - yLo < 1e-9)
        {
            yLo -= 1;
            yHi += 1;
        }
        double yPad = 0.25 * (yHi - yLo);
        return new PlotWindow(lo, hi, yLo - yPad, yHi + yPad);
    }

    /// <summary>
    /// The stretch of the real line a number line shows: every end point with room around it, or the searched range
    /// for numeric answers, or −5 to 5 when there are no end points.
    /// </summary>
    public static (double Min, double Max) ForNumberLine(IntervalSet set, (double Low, double High)? range = null)
    {
        var ends = set.EndPoints.Select(b => b.Approx).Where(double.IsFinite).ToList();
        if (range is { } r) ends.AddRange([r.Low, r.High]);
        if (ends.Count == 0) return (-5, 5);
        double lo = ends.Min(), hi = ends.Max();
        double span = hi - lo;
        double pad = span < 1e-9 ? Math.Max(2, 0.5 * Math.Abs(lo)) : Math.Max(0.5, 0.35 * span);
        return (lo - pad, hi + pad);
    }
}
