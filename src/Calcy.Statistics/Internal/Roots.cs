namespace Calcy.Statistics.Internal;

/// <summary>Bracketing root finders used internally.</summary>
internal static class Roots
{
    /// <summary>
    /// Brent's method (zeroin). Requires f(a) and f(b) to have opposite signs (or one of them zero).
    /// Converges to a root within an absolute tolerance of about 4 eps |x| + tolAbs.
    /// </summary>
    public static double Brent(Func<double, double> f, double a, double b, double fa, double fb, double tolAbs = 0.0, int maxIterations = 300)
    {
        if (fa == 0) return a;
        if (fb == 0) return b;
        if (double.IsNaN(fa) || double.IsNaN(fb) || Math.Sign(fa) == Math.Sign(fb))
            throw new ArgumentException("the root is not bracketed");

        double c = a, fc = fa, d = b - a, e = d;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            if (Math.Sign(fb) == Math.Sign(fc))
            {
                c = a; fc = fa; d = b - a; e = d;
            }
            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b; b = c; c = a;
                fa = fb; fb = fc; fc = fa;
            }
            double tol = 2.0 * Num.Eps * Math.Abs(b) + 0.5 * tolAbs;
            double m = 0.5 * (c - b);
            if (Math.Abs(m) <= tol || fb == 0) return b;
            if (Math.Abs(e) >= tol && Math.Abs(fa) > Math.Abs(fb))
            {
                double s = fb / fa, p, q;
                if (a == c)
                {
                    p = 2.0 * m * s;
                    q = 1.0 - s;
                }
                else
                {
                    double qq = fa / fc, r = fb / fc;
                    p = s * (2.0 * m * qq * (qq - r) - (b - a) * (r - 1.0));
                    q = (qq - 1.0) * (r - 1.0) * (s - 1.0);
                }
                if (p > 0) q = -q; else p = -p;
                if (2.0 * p < Math.Min(3.0 * m * q - Math.Abs(tol * q), Math.Abs(e * q)))
                {
                    e = d;
                    d = p / q;
                }
                else
                {
                    d = m; e = m;
                }
            }
            else
            {
                d = m; e = m;
            }
            a = b; fa = fb;
            b += Math.Abs(d) > tol ? d : (m > 0 ? tol : -tol);
            fb = f(b);
            if (double.IsNaN(fb)) throw new ArgumentException("the function could not be evaluated during root finding");
        }
        return b;
    }

    /// <summary>Brent's method evaluating the end points itself.</summary>
    public static double Brent(Func<double, double> f, double a, double b, double tolAbs = 0.0)
        => Brent(f, a, b, f(a), f(b), tolAbs);

    /// <summary>
    /// Scans [lo, hi] on a uniform grid and returns every sign change bracket refined with Brent.
    /// </summary>
    public static List<double> AllRoots(Func<double, double> f, double lo, double hi, int gridPoints)
    {
        var roots = new List<double>();
        double prevX = lo, prevF = f(lo);
        if (prevF == 0) roots.Add(lo);
        for (int i = 1; i <= gridPoints; i++)
        {
            double x = lo + (hi - lo) * i / gridPoints;
            double fx = f(x);
            if (double.IsFinite(fx) && double.IsFinite(prevF))
            {
                if (fx == 0) roots.Add(x);
                else if (prevF != 0 && Math.Sign(fx) != Math.Sign(prevF))
                    roots.Add(Brent(f, prevX, x, prevF, fx));
            }
            prevX = x; prevF = fx;
        }
        return roots;
    }
}
