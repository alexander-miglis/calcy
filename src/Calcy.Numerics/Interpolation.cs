namespace Calcy.Numerics;

/// <summary>An interpolating function built from data points.</summary>
public interface IInterpolant
{
    /// <summary>Value of the interpolant at x.</summary>
    double Evaluate(double x);
}

/// <summary>Piecewise linear interpolation (linear extrapolation beyond the data).</summary>
public sealed class LinearInterpolant : IInterpolant
{
    private readonly double[] _x, _y;

    internal LinearInterpolant(double[] x, double[] y) { _x = x; _y = y; }

    /// <inheritdoc/>
    public double Evaluate(double x)
    {
        if (_x.Length == 1) return _y[0];
        int i = Interpolation.Segment(_x, x);
        double t = (x - _x[i]) / (_x[i + 1] - _x[i]);
        return _y[i] + t * (_y[i + 1] - _y[i]);
    }

    /// <summary>The interpolant as a delegate.</summary>
    public Func<double, double> AsFunc() => Evaluate;
}

/// <summary>Polynomial interpolation in barycentric form (numerically stable Lagrange interpolation).</summary>
public sealed class PolynomialInterpolant : IInterpolant
{
    private readonly double[] _x, _y, _w;

    internal PolynomialInterpolant(double[] x, double[] y)
    {
        _x = x; _y = y;
        int n = x.Length;
        _w = new double[n];
        // Weights scaled by the interval length to avoid overflow or underflow.
        double scale = n > 1 ? 4 / (x[^1] - x[0]) : 1;
        for (int j = 0; j < n; j++)
        {
            double w = 1;
            for (int k = 0; k < n; k++) if (k != j) w *= scale * (x[j] - x[k]);
            _w[j] = 1 / w;
        }
    }

    /// <inheritdoc/>
    public double Evaluate(double x)
    {
        double num = 0, den = 0;
        for (int j = 0; j < _x.Length; j++)
        {
            double d = x - _x[j];
            if (d == 0) return _y[j];
            double t = _w[j] / d;
            num += t * _y[j];
            den += t;
        }
        return num / den;
    }

    /// <summary>The interpolant as a delegate.</summary>
    public Func<double, double> AsFunc() => Evaluate;
}

/// <summary>A cubic spline. Beyond the data the end cubic pieces are extended.</summary>
public sealed class CubicSpline : IInterpolant
{
    private readonly double[] _x, _y, _m; // _m: second derivatives at the knots

    internal CubicSpline(double[] x, double[] y, double[] secondDerivatives) { _x = x; _y = y; _m = secondDerivatives; }

    /// <summary>Knot abscissas (sorted).</summary>
    public IReadOnlyList<double> Knots => _x;

    /// <inheritdoc/>
    public double Evaluate(double x)
    {
        int i = Interpolation.Segment(_x, x);
        double h = _x[i + 1] - _x[i];
        double a = (_x[i + 1] - x) / h, b = (x - _x[i]) / h;
        return a * _y[i] + b * _y[i + 1] + ((a * a * a - a) * _m[i] + (b * b * b - b) * _m[i + 1]) * h * h / 6;
    }

    /// <summary>First derivative of the spline.</summary>
    public double Derivative(double x)
    {
        int i = Interpolation.Segment(_x, x);
        double h = _x[i + 1] - _x[i];
        double a = (_x[i + 1] - x) / h, b = (x - _x[i]) / h;
        return (_y[i + 1] - _y[i]) / h - (3 * a * a - 1) / 6 * h * _m[i] + (3 * b * b - 1) / 6 * h * _m[i + 1];
    }

    /// <summary>Second derivative of the spline.</summary>
    public double SecondDerivative(double x)
    {
        int i = Interpolation.Segment(_x, x);
        double h = _x[i + 1] - _x[i];
        double a = (_x[i + 1] - x) / h, b = (x - _x[i]) / h;
        return a * _m[i] + b * _m[i + 1];
    }

    /// <summary>The spline as a delegate.</summary>
    public Func<double, double> AsFunc() => Evaluate;
}

/// <summary>Interpolation factories and helpers.</summary>
public static class Interpolation
{
    /// <summary>Piecewise linear interpolant through the points (sorted internally; x values must be distinct).</summary>
    public static LinearInterpolant Linear(double[] xs, double[] ys)
    {
        var (x, y) = Prepare(xs, ys, 1);
        return new LinearInterpolant(x, y);
    }

    /// <summary>Interpolating polynomial through the points (barycentric Lagrange form).</summary>
    public static PolynomialInterpolant Lagrange(double[] xs, double[] ys)
    {
        var (x, y) = Prepare(xs, ys, 1);
        return new PolynomialInterpolant(x, y);
    }

    /// <summary>Evaluates the Lagrange interpolating polynomial at a single point.</summary>
    public static double Lagrange(double[] xs, double[] ys, double x) => Lagrange(xs, ys).Evaluate(x);

    /// <summary>Newton divided-difference coefficients c_k = f[x_0, ..., x_k] (points in the given order).</summary>
    public static double[] NewtonDividedDifferences(double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (xs.Length != ys.Length || xs.Length == 0) throw new ArgumentException("xs and ys must be non-empty and of equal length.");
        int n = xs.Length;
        var c = (double[])ys.Clone();
        for (int j = 1; j < n; j++)
            for (int i = n - 1; i >= j; i--)
            {
                double d = xs[i] - xs[i - j];
                if (d == 0) throw new ArgumentException("Interpolation nodes must be distinct.");
                c[i] = (c[i] - c[i - 1]) / d;
            }
        return c;
    }

    /// <summary>Evaluates the Newton form sum c_k prod_{j&lt;k} (x - x_j).</summary>
    public static double EvaluateNewton(double[] xs, double[] coefficients, double x)
    {
        int n = coefficients.Length;
        double r = coefficients[n - 1];
        for (int k = n - 2; k >= 0; k--) r = r * (x - xs[k]) + coefficients[k];
        return r;
    }

    /// <summary>Monomial coefficients (ascending powers) of the interpolating polynomial through the points.</summary>
    public static double[] InterpolatingPolynomial(double[] xs, double[] ys)
    {
        var c = NewtonDividedDifferences(xs, ys);
        int n = c.Length;
        double[] p = [c[n - 1]];
        for (int k = n - 2; k >= 0; k--)
        {
            p = Polynomial.Multiply(p, [-xs[k], 1]);
            p[0] += c[k];
        }
        return p;
    }

    /// <summary>Natural cubic spline (zero second derivative at both ends).</summary>
    public static CubicSpline NaturalCubicSpline(double[] xs, double[] ys)
    {
        var (x, y) = Prepare(xs, ys, 2);
        return new CubicSpline(x, y, SplineSecondDerivatives(x, y, null, null));
    }

    /// <summary>Clamped cubic spline with prescribed end slopes.</summary>
    public static CubicSpline ClampedCubicSpline(double[] xs, double[] ys, double leftSlope, double rightSlope)
    {
        var (x, y) = Prepare(xs, ys, 2);
        return new CubicSpline(x, y, SplineSecondDerivatives(x, y, leftSlope, rightSlope));
    }

    /// <summary>
    /// Chebyshev nodes of the first kind on [a, b]: (a+b)/2 + (b-a)/2 cos((2k+1) pi / (2n)),
    /// returned in ascending order.
    /// </summary>
    public static double[] ChebyshevNodes(int n, double a = -1, double b = 1)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "At least one node is required.");
        var r = new double[n];
        for (int k = 0; k < n; k++)
            r[n - 1 - k] = 0.5 * (a + b) + 0.5 * (b - a) * Math.Cos((2 * k + 1) * Math.PI / (2 * n));
        return r;
    }

    private static double[] SplineSecondDerivatives(double[] x, double[] y, double? leftSlope, double? rightSlope)
    {
        int n = x.Length;
        var m = new double[n];
        // Tridiagonal system: sub[i] m[i-1] + diag[i] m[i] + sup[i] m[i+1] = rhs[i].
        var sub = new double[n];
        var diag = new double[n];
        var sup = new double[n];
        var rhs = new double[n];
        for (int i = 1; i < n - 1; i++)
        {
            double h0 = x[i] - x[i - 1], h1 = x[i + 1] - x[i];
            sub[i] = h0 / 6;
            diag[i] = (h0 + h1) / 3;
            sup[i] = h1 / 6;
            rhs[i] = (y[i + 1] - y[i]) / h1 - (y[i] - y[i - 1]) / h0;
        }
        if (leftSlope is double ls)
        {
            double h = x[1] - x[0];
            diag[0] = h / 3; sup[0] = h / 6; rhs[0] = (y[1] - y[0]) / h - ls;
        }
        else { diag[0] = 1; sup[0] = 0; rhs[0] = 0; }
        if (rightSlope is double rs)
        {
            double h = x[n - 1] - x[n - 2];
            sub[n - 1] = h / 6; diag[n - 1] = h / 3; rhs[n - 1] = rs - (y[n - 1] - y[n - 2]) / h;
        }
        else { sub[n - 1] = 0; diag[n - 1] = 1; rhs[n - 1] = 0; }
        // Thomas algorithm (the system is diagonally dominant).
        var cp = new double[n];
        var dp = new double[n];
        cp[0] = sup[0] / diag[0];
        dp[0] = rhs[0] / diag[0];
        for (int i = 1; i < n; i++)
        {
            double den = diag[i] - sub[i] * cp[i - 1];
            cp[i] = i < n - 1 ? sup[i] / den : 0;
            dp[i] = (rhs[i] - sub[i] * dp[i - 1]) / den;
        }
        m[n - 1] = dp[n - 1];
        for (int i = n - 2; i >= 0; i--) m[i] = dp[i] - cp[i] * m[i + 1];
        return m;
    }

    private static (double[] X, double[] Y) Prepare(double[] xs, double[] ys, int minPoints)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (xs.Length != ys.Length) throw new ArgumentException("xs and ys must have the same length.");
        if (xs.Length < minPoints) throw new ArgumentException($"At least {minPoints} data point(s) are required.");
        var idx = Enumerable.Range(0, xs.Length).OrderBy(i => xs[i]).ToArray();
        var x = idx.Select(i => xs[i]).ToArray();
        var y = idx.Select(i => ys[i]).ToArray();
        for (int i = 0; i < x.Length; i++)
        {
            if (!double.IsFinite(x[i]) || !double.IsFinite(y[i])) throw new ArgumentException("Data points must be finite.");
            if (i > 0 && x[i] == x[i - 1]) throw new ArgumentException($"Duplicate x value {x[i]}.");
        }
        return (x, y);
    }

    /// <summary>Index i of the segment [x_i, x_{i+1}] used for x (end segments for extrapolation).</summary>
    internal static int Segment(double[] x, double v)
    {
        int n = x.Length;
        if (v <= x[0]) return 0;
        if (v >= x[n - 1]) return n - 2;
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (x[mid] <= v) lo = mid; else hi = mid;
        }
        return lo;
    }
}
