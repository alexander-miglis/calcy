namespace Calcy.Graphing.Numerics;

/// <summary>One-dimensional root finding, minimisation and differentiation used by the graph analysis tools.</summary>
public static class Solvers
{
    private const double Eps = 2.220446049250313e-16;

    /// <summary>
    /// Brent's method (inverse quadratic interpolation with bisection safeguard). Needs f(a) and f(b) of opposite signs
    /// (or one of them zero). Returns null when there is no sign change or the function is undefined inside.
    /// </summary>
    public static double? BrentRoot(Func<double, double> f, double a, double b, double tolerance = 0, int maxIterations = 200)
    {
        double fa = f(a), fb = f(b);
        if (double.IsNaN(fa) || double.IsNaN(fb)) return null;
        if (fa == 0) return a;
        if (fb == 0) return b;
        if (Math.Sign(fa) == Math.Sign(fb)) return null;
        double c = a, fc = fa, d = b - a, e = d;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            if (Math.Sign(fb) == Math.Sign(fc))
            {
                c = a;
                fc = fa;
                d = e = b - a;
            }
            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b; b = c; c = a;
                fa = fb; fb = fc; fc = fa;
            }
            double tol = 2 * Eps * Math.Abs(b) + 0.5 * tolerance;
            double m = 0.5 * (c - b);
            if (Math.Abs(m) <= tol || fb == 0) return b;
            if (Math.Abs(e) >= tol && Math.Abs(fa) > Math.Abs(fb))
            {
                double s = fb / fa, p, q;
                if (a == c)
                {
                    p = 2 * m * s;
                    q = 1 - s;
                }
                else
                {
                    double qq = fa / fc, r = fb / fc;
                    p = s * (2 * m * qq * (qq - r) - (b - a) * (r - 1));
                    q = (qq - 1) * (r - 1) * (s - 1);
                }
                if (p > 0) q = -q; else p = -p;
                if (2 * p < Math.Min(3 * m * q - Math.Abs(tol * q), Math.Abs(e * q)))
                {
                    e = d;
                    d = p / q;
                }
                else
                {
                    d = m;
                    e = m;
                }
            }
            else
            {
                d = m;
                e = m;
            }
            a = b;
            fa = fb;
            b += Math.Abs(d) > tol ? d : (m > 0 ? tol : -tol);
            fb = f(b);
            if (double.IsNaN(fb)) return null;
        }
        return b;
    }

    /// <summary>
    /// Brent's minimisation (golden section with parabolic steps) on [a, b]. Returns the x of a local minimum and its value.
    /// The x is accurate to about the square root of machine precision; refine with <see cref="BrentRoot"/> on the derivative when more is needed.
    /// </summary>
    public static (double X, double Value) BrentMinimize(Func<double, double> f, double a, double b, double tolerance = 1e-12, int maxIterations = 200)
    {
        const double golden = 0.3819660112501051;
        if (a > b) (a, b) = (b, a);
        double x = a + golden * (b - a), w = x, v = x;
        double fx = f(x), fw = fx, fv = fx;
        double d = 0, e = 0;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            double xm = 0.5 * (a + b);
            double tol1 = tolerance * Math.Abs(x) + 1e-15, tol2 = 2 * tol1;
            if (Math.Abs(x - xm) <= tol2 - 0.5 * (b - a)) break;
            bool goldenStep = true;
            if (Math.Abs(e) > tol1)
            {
                double r = (x - w) * (fx - fv);
                double q = (x - v) * (fx - fw);
                double p = (x - v) * q - (x - w) * r;
                q = 2 * (q - r);
                if (q > 0) p = -p;
                q = Math.Abs(q);
                double etemp = e;
                e = d;
                if (!(Math.Abs(p) >= Math.Abs(0.5 * q * etemp) || p <= q * (a - x) || p >= q * (b - x)))
                {
                    d = p / q;
                    double u0 = x + d;
                    if (u0 - a < tol2 || b - u0 < tol2) d = xm - x >= 0 ? tol1 : -tol1;
                    goldenStep = false;
                }
            }
            if (goldenStep)
            {
                e = x >= xm ? a - x : b - x;
                d = golden * e;
            }
            double u = Math.Abs(d) >= tol1 ? x + d : x + (d >= 0 ? tol1 : -tol1);
            double fu = f(u);
            if (double.IsNaN(fu)) fu = double.PositiveInfinity;
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
        return (x, fx);
    }

    /// <summary>
    /// Derivative by Ridders' extrapolation of central differences. Returns the value and an error estimate
    /// (NaN when the function is undefined near x).
    /// </summary>
    public static (double Value, double Error) Derivative(Func<double, double> f, double x, double h = 0)
    {
        const int ntab = 10;
        const double con = 1.4, con2 = con * con, safe = 2.0;
        if (h <= 0) h = 0.01 * Math.Max(1, Math.Abs(x));
        var a = new double[ntab, ntab];
        double hh = h;
        a[0, 0] = (f(x + hh) - f(x - hh)) / (2 * hh);
        double err = double.MaxValue, ans = a[0, 0];
        if (double.IsNaN(ans)) return (double.NaN, double.NaN);
        for (int i = 1; i < ntab; i++)
        {
            hh /= con;
            a[0, i] = (f(x + hh) - f(x - hh)) / (2 * hh);
            double fac = con2;
            for (int j = 1; j <= i; j++)
            {
                a[j, i] = (a[j - 1, i] * fac - a[j - 1, i - 1]) / (fac - 1);
                fac *= con2;
                double errt = Math.Max(Math.Abs(a[j, i] - a[j - 1, i]), Math.Abs(a[j, i] - a[j - 1, i - 1]));
                if (errt <= err)
                {
                    err = errt;
                    ans = a[j, i];
                }
            }
            if (Math.Abs(a[i, i] - a[i - 1, i - 1]) >= safe * err) break;
        }
        return (ans, err);
    }

    /// <summary>Second derivative by a central difference with a step balanced against rounding.</summary>
    public static double SecondDerivative(Func<double, double> f, double x)
    {
        double h = 1e-4 * Math.Max(1, Math.Abs(x));
        double fp = f(x + h), f0 = f(x), fm = f(x - h);
        double coarse = (fp - 2 * f0 + fm) / (h * h);
        double h2 = h / 2;
        double fine = (f(x + h2) - 2 * f0 + f(x - h2)) / (h2 * h2);
        // Richardson: the error of the central second difference is O(h²).
        return (4 * fine - coarse) / 3;
    }
}
