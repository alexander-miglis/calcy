namespace Calcy.Formulas.Tests.Evaluation;

/// <summary>
/// A simple, robust numeric root finder: scans a very wide logarithmic grid on both sides of zero
/// (from 1e-40 to 1e40 in magnitude), brackets every sign change and refines it by bisection.
/// Sign changes caused by poles (where the function blows up instead of crossing zero) are rejected.
/// </summary>
public static class Solver
{
    public static IReadOnlyList<double> FindRoots(Func<double, double> f, int pointsPerDecade = 30, int minExponent = -40, int maxExponent = 40)
    {
        var grid = new List<double> { 0 };
        int steps = (maxExponent - minExponent) * pointsPerDecade;
        for (int k = 0; k <= steps; k++)
        {
            double x = Math.Pow(10, minExponent + (double)k / pointsPerDecade);
            grid.Add(x);
            grid.Add(-x);
        }
        grid.Sort();

        var values = grid.Select(x => Safe(f, x)).ToArray();
        var roots = new List<double>();
        for (int i = 0; i < grid.Count; i++)
        {
            if (values[i] == 0) { roots.Add(grid[i]); continue; }
            if (i + 1 >= grid.Count) break;
            double fa = values[i], fb = values[i + 1];
            if (!double.IsFinite(fa) || !double.IsFinite(fb) || fb == 0) continue;
            if (Math.Sign(fa) == Math.Sign(fb)) continue;
            if (Bisect(f, grid[i], grid[i + 1], fa, fb) is { } root) roots.Add(root);
        }

        // Merge duplicates that come from neighbouring brackets.
        roots.Sort();
        var merged = new List<double>();
        foreach (double r in roots)
        {
            if (merged.Count > 0 && Math.Abs(r - merged[^1]) <= 1e-12 * Math.Max(1e-300, Math.Abs(r))) continue;
            merged.Add(r);
        }
        return merged;
    }

    /// <summary>Bisection on a bracket with a sign change; returns null when the bracket holds a pole.</summary>
    private static double? Bisect(Func<double, double> f, double a, double b, double fa, double fb)
    {
        double fa0 = Math.Abs(fa), fb0 = Math.Abs(fb);
        for (int iter = 0; iter < 400; iter++)
        {
            double m = a + (b - a) / 2;
            if (m <= Math.Min(a, b) || m >= Math.Max(a, b)) break;
            double fm = Safe(f, m);
            if (fm == 0) return m;
            if (!double.IsFinite(fm)) return null;
            if (Math.Sign(fm) == Math.Sign(fa)) { a = m; fa = fm; }
            else { b = m; fb = fm; }
        }
        double root = Math.Abs(fa) <= Math.Abs(fb) ? a : b;
        double residual = Math.Min(Math.Abs(fa), Math.Abs(fb));
        // A genuine root shrinks the residual far below the values at the original bracket ends; a pole does not.
        return residual <= 1e-3 * Math.Max(fa0, fb0) ? root : null;
    }

    private static double Safe(Func<double, double> f, double x)
    {
        try
        {
            return f(x);
        }
        catch (ArithmeticException)
        {
            return double.NaN;
        }
    }
}
