using System.Numerics;
using Calcy.Cas.Polynomials;

namespace Calcy.Cas.Numerics;

/// <summary>Numeric root finding: Brent's method, scanning an interval, polynomial roots (Aberth) and Newton for systems.</summary>
internal static class RootFinding
{
    /// <summary>Brent's method on a bracket [a, b] with f(a)·f(b) ≤ 0.</summary>
    public static double Brent(Func<double, double> f, double a, double b, double tol = 1e-15, int maxIter = 200)
    {
        double fa = f(a), fb = f(b);
        if (fa == 0) return a;
        if (fb == 0) return b;
        if (double.IsNaN(fa) || double.IsNaN(fb) || fa * fb > 0) return double.NaN;
        if (Math.Abs(fa) < Math.Abs(fb))
        {
            (a, b) = (b, a);
            (fa, fb) = (fb, fa);
        }
        double c = a, fc = fa, d = b - a, e = d;
        for (int i = 0; i < maxIter; i++)
        {
            if (fb * fc > 0)
            {
                c = a; fc = fa; d = b - a; e = d;
            }
            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b; b = c; c = a;
                fa = fb; fb = fc; fc = fa;
            }
            double tol1 = 2 * 2.220446049250313e-16 * Math.Abs(b) + 0.5 * tol;
            double m = 0.5 * (c - b);
            if (Math.Abs(m) <= tol1 || fb == 0) return b;
            if (Math.Abs(e) >= tol1 && Math.Abs(fa) > Math.Abs(fb))
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
                if (2 * p < Math.Min(3 * m * q - Math.Abs(tol1 * q), Math.Abs(e * q)))
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
            b += Math.Abs(d) > tol1 ? d : (m > 0 ? tol1 : -tol1);
            fb = f(b);
            if (double.IsNaN(fb)) return double.NaN;
        }
        return b;
    }

    /// <summary>
    /// All roots of f in [lo, hi] found by sampling: sign changes refined with Brent (poles rejected by checking |f| at the
    /// root) and touching roots found at small local minima of |f|.
    /// </summary>
    public static List<double> ScanRoots(Func<double, double> f, double lo, double hi, int samples = 2000)
    {
        var roots = new List<double>();
        if (!(hi > lo)) return roots;
        var xs = new double[samples + 1];
        var ys = new double[samples + 1];
        double scale = 0;
        for (int i = 0; i <= samples; i++)
        {
            xs[i] = lo + (hi - lo) * i / samples;
            ys[i] = Safe(f, xs[i]);
            if (double.IsFinite(ys[i])) scale = Math.Max(scale, Math.Abs(ys[i]));
        }
        if (scale == 0) scale = 1;
        double step = (hi - lo) / samples;
        for (int i = 0; i < samples; i++)
        {
            CasEnv.Check();
            double y0 = ys[i], y1 = ys[i + 1];
            if (y0 == 0)
            {
                Add(roots, xs[i], step);
                continue;
            }
            if (!double.IsFinite(y0) || !double.IsFinite(y1)) continue;
            if (y0 * y1 < 0)
            {
                double r = Brent(z => Safe(f, z), xs[i], xs[i + 1]);
                if (!double.IsFinite(r)) continue;
                double fr = Math.Abs(Safe(f, r));
                // A sign change through a pole leaves |f| large at the "root".
                if (fr <= 1e-6 * Math.Max(1, Math.Min(Math.Abs(y0), Math.Abs(y1)))) Add(roots, r, step);
            }
        }
        if (ys[samples] == 0) Add(roots, xs[samples], step);
        // Touching roots: local minima of |f| close to zero without a sign change.
        for (int i = 1; i < samples; i++)
        {
            double a = Math.Abs(ys[i - 1]), b = Math.Abs(ys[i]), c = Math.Abs(ys[i + 1]);
            if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c)) continue;
            if (b <= a && b <= c && (b < a || b < c) && ys[i - 1] * ys[i + 1] > 0)
            {
                double m = GoldenMin(z => Math.Abs(Safe(f, z)), xs[i - 1], xs[i + 1]);
                double fm = Math.Abs(Safe(f, m));
                if (fm < 1e-10 * Math.Max(1, Math.Max(a, c))) Add(roots, m, step);
            }
        }
        roots.Sort();
        return roots;
    }

    private static void Add(List<double> roots, double r, double step)
    {
        foreach (var x in roots)
            if (Math.Abs(x - r) < Math.Max(1e-9, step * 0.5)) return;
        roots.Add(Math.Abs(r) < 1e-13 ? 0 : r);
    }

    private static double Safe(Func<double, double> f, double x)
    {
        try { return f(x); }
        catch (ArithmeticException) { return double.NaN; }
    }

    public static double GoldenMin(Func<double, double> f, double a, double b, int iterations = 200)
    {
        const double g = 0.6180339887498949;
        double c = b - g * (b - a), d = a + g * (b - a);
        double fc = f(c), fd = f(d);
        for (int i = 0; i < iterations && Math.Abs(b - a) > 1e-15 * (1 + Math.Abs(a)); i++)
        {
            if (fc < fd)
            {
                b = d; d = c; fd = fc;
                c = b - g * (b - a); fc = f(c);
            }
            else
            {
                a = c; c = d; fc = fd;
                d = a + g * (b - a); fd = f(d);
            }
        }
        return 0.5 * (a + b);
    }

    /// <summary>Newton iteration from a starting point (with a secant fallback), or NaN when it does not converge.</summary>
    public static double Newton(Func<double, double> f, double x0, int maxIter = 100)
    {
        double x = x0;
        for (int i = 0; i < maxIter; i++)
        {
            double fx = f(x);
            if (!double.IsFinite(fx)) return double.NaN;
            if (Math.Abs(fx) < 1e-15) return x;
            double h = 1e-7 * Math.Max(1, Math.Abs(x));
            double d = (f(x + h) - f(x - h)) / (2 * h);
            if (!double.IsFinite(d) || d == 0) return double.NaN;
            double step = fx / d;
            double nx = x - step;
            // Damping: never accept a step that makes |f| much worse.
            for (int k = 0; k < 30 && Math.Abs(f(nx)) > Math.Abs(fx) * 2; k++)
            {
                step /= 2;
                nx = x - step;
            }
            if (Math.Abs(nx - x) <= 1e-15 * Math.Max(1, Math.Abs(x))) return nx;
            x = nx;
        }
        return Math.Abs(f(x)) < 1e-9 ? x : double.NaN;
    }

    // ------------------------------------------------------------------ polynomials

    /// <summary>All complex roots of a polynomial with rational coefficients (Aberth-Ehrlich iteration, then Newton polishing).</summary>
    public static List<Complex> PolynomialRoots(QPoly p)
    {
        int n = p.Degree;
        var roots = new List<Complex>();
        if (n < 1) return roots;
        var c = new Complex[n + 1];
        for (int i = 0; i <= n; i++) c[i] = p[i].ToDouble() / p.Lc.ToDouble();
        if (n == 1)
        {
            roots.Add(-c[0]);
            return roots;
        }
        // Initial guesses on a circle with the Cauchy bound radius.
        double radius = 0;
        for (int i = 0; i < n; i++) radius = Math.Max(radius, Math.Pow(Complex.Abs(c[i]), 1.0 / (n - i)));
        radius = Math.Max(radius, 1e-3);
        var z = new Complex[n];
        for (int k = 0; k < n; k++) z[k] = Complex.FromPolarCoordinates(radius, 2 * Math.PI * k / n + 0.4);
        for (int iter = 0; iter < 500; iter++)
        {
            CasEnv.Check();
            double maxStep = 0;
            for (int k = 0; k < n; k++)
            {
                var (v, d) = Horner(c, z[k]);
                if (v == Complex.Zero) continue;
                var ratio = v / d;
                Complex sum = Complex.Zero;
                for (int j = 0; j < n; j++)
                    if (j != k) sum += 1 / (z[k] - z[j]);
                var w = ratio / (1 - ratio * sum);
                if (double.IsNaN(w.Real) || double.IsNaN(w.Imaginary)) continue;
                z[k] -= w;
                maxStep = Math.Max(maxStep, Complex.Abs(w) / Math.Max(1, Complex.Abs(z[k])));
            }
            if (maxStep < 1e-15) break;
        }
        // Polish with Newton on the original polynomial.
        for (int k = 0; k < n; k++)
        {
            for (int it = 0; it < 5; it++)
            {
                var (v, d) = Horner(c, z[k]);
                if (d == Complex.Zero) break;
                var step = v / d;
                if (Complex.Abs(step) < 1e-17 * Math.Max(1, Complex.Abs(z[k]))) break;
                z[k] -= step;
            }
            if (Math.Abs(z[k].Imaginary) < 1e-12 * Math.Max(1, Math.Abs(z[k].Real))) z[k] = new Complex(z[k].Real, 0);
            roots.Add(z[k]);
        }
        return roots;
    }

    private static (Complex Value, Complex Derivative) Horner(Complex[] c, Complex x)
    {
        int n = c.Length - 1;
        Complex v = Complex.One, d = Complex.Zero;
        for (int i = n - 1; i >= 0; i--)
        {
            d = d * x + v;
            v = v * x + c[i];
        }
        return (v, d);
    }

    /// <summary>Real roots of a square-free rational polynomial, refined by bisection on sign changes.</summary>
    public static List<double> RealPolynomialRoots(QPoly p)
    {
        var result = new List<double>();
        if (p.Degree < 1) return result;
        foreach (var z in PolynomialRoots(p))
        {
            if (Math.Abs(z.Imaginary) > 1e-7 * Math.Max(1, Math.Abs(z.Real))) continue;
            double r = z.Real;
            double h = 1e-6 * Math.Max(1, Math.Abs(r));
            double a = r - h, b = r + h;
            double fa = p.EvalDouble(a), fb = p.EvalDouble(b);
            if (fa * fb < 0)
            {
                double rr = Brent(p.EvalDouble, a, b);
                if (double.IsFinite(rr)) r = rr;
            }
            if (!result.Any(x => Math.Abs(x - r) < 1e-9 * Math.Max(1, Math.Abs(r)))) result.Add(r);
        }
        result.Sort();
        return result;
    }

    // ------------------------------------------------------------------ systems

    /// <summary>Newton's method for F(x) = 0 in several variables (Jacobian by finite differences), or null.</summary>
    public static double[]? NewtonSystem(Func<double[], double>[] f, double[] start, int maxIter = 60)
    {
        int n = start.Length;
        var x = (double[])start.Clone();
        for (int iter = 0; iter < maxIter; iter++)
        {
            CasEnv.Check();
            var fx = f.Select(g => g(x)).ToArray();
            if (fx.Any(v => !double.IsFinite(v))) return null;
            double norm = fx.Sum(v => v * v);
            if (norm < 1e-26) return x;
            var J = new double[f.Length, n];
            for (int j = 0; j < n; j++)
            {
                double h = 1e-7 * Math.Max(1, Math.Abs(x[j]));
                var xp = (double[])x.Clone();
                var xm = (double[])x.Clone();
                xp[j] += h;
                xm[j] -= h;
                for (int i = 0; i < f.Length; i++) J[i, j] = (f[i](xp) - f[i](xm)) / (2 * h);
            }
            var step = SolveLeastSquares(J, fx, f.Length, n);
            if (step is null) return null;
            double lambda = 1;
            double[] next = x;
            for (int k = 0; k < 30; k++)
            {
                next = new double[n];
                for (int j = 0; j < n; j++) next[j] = x[j] - lambda * step[j];
                double nn = f.Sum(g => { var v = g(next); return v * v; });
                if (double.IsFinite(nn) && nn < norm) break;
                lambda /= 2;
            }
            if (next.Zip(x, (a, b) => Math.Abs(a - b)).Max() < 1e-15) return norm < 1e-18 ? next : null;
            x = next;
        }
        var final = f.Sum(g => { var v = g(x); return v * v; });
        return final < 1e-20 ? x : null;
    }

    private static double[]? SolveLeastSquares(double[,] J, double[] r, int m, int n)
    {
        // Normal equations (J^T J) s = J^T r with Gaussian elimination.
        var A = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double s = 0;
                for (int k = 0; k < m; k++) s += J[k, i] * J[k, j];
                A[i, j] = s;
            }
            double t = 0;
            for (int k = 0; k < m; k++) t += J[k, i] * r[k];
            A[i, n] = t;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int i = col + 1; i < n; i++)
                if (Math.Abs(A[i, col]) > Math.Abs(A[piv, col])) piv = i;
            if (Math.Abs(A[piv, col]) < 1e-300) return null;
            for (int j = 0; j <= n; j++) (A[col, j], A[piv, j]) = (A[piv, j], A[col, j]);
            for (int i = 0; i < n; i++)
            {
                if (i == col) continue;
                double factor = A[i, col] / A[col, col];
                for (int j = col; j <= n; j++) A[i, j] -= factor * A[col, j];
            }
        }
        var s2 = new double[n];
        for (int i = 0; i < n; i++) s2[i] = A[i, n] / A[i, i];
        return s2;
    }
}
