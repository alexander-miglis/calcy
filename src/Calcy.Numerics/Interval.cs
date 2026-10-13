using System.Globalization;

namespace Calcy.Numerics;

/// <summary>
/// A closed real interval [Lo, Hi] with outward-rounded arithmetic, suitable for guaranteed
/// (enclosure-correct) evaluation such as implicit-curve plotting. Every operation returns an
/// interval that contains all real results of the operation applied to points of the inputs.
/// Parts of the input outside a function's domain are clipped (sqrt, log, asin, acos, real powers),
/// so the result encloses the function on the defined part only; a fully undefined input gives
/// <see cref="Empty"/>. Bounds may be infinite. The empty interval is represented by NaN bounds.
/// </summary>
public readonly struct Interval : IEquatable<Interval>
{
    private const double TwoPi = 2 * Math.PI;

    /// <summary>Lower bound (NaN for the empty interval).</summary>
    public double Lo { get; }

    /// <summary>Upper bound (NaN for the empty interval).</summary>
    public double Hi { get; }

    private Interval(double lo, double hi, bool _)
    {
        Lo = lo;
        Hi = hi;
    }

    /// <summary>Creates [lo, hi]. Requires lo &lt;= hi, no NaN, lo != +Infinity and hi != -Infinity.</summary>
    public Interval(double lo, double hi)
    {
        if (double.IsNaN(lo) || double.IsNaN(hi))
            throw new ArgumentException("Interval bounds cannot be NaN; use Interval.Empty for the empty set.");
        if (lo > hi)
            throw new ArgumentException($"Invalid interval: lower bound {lo} exceeds upper bound {hi}.");
        if (double.IsPositiveInfinity(lo) || double.IsNegativeInfinity(hi))
            throw new ArgumentException("Interval lower bound cannot be +Infinity and upper bound cannot be -Infinity.");
        Lo = lo;
        Hi = hi;
    }

    /// <summary>The empty set.</summary>
    public static Interval Empty => new(double.NaN, double.NaN, true);

    /// <summary>The whole real line [-Infinity, +Infinity].</summary>
    public static Interval Entire => new(double.NegativeInfinity, double.PositiveInfinity, true);

    /// <summary>A guaranteed enclosure of pi.</summary>
    public static Interval Pi => new(Math.PI, Math.BitIncrement(Math.PI), true);

    /// <summary>A guaranteed enclosure of e.</summary>
    public static Interval E => new(Math.E, Math.BitIncrement(Math.E), true);

    /// <summary>The degenerate interval [x, x]. NaN gives Empty; infinities give a half-line beyond MaxValue.</summary>
    public static Interval Point(double x)
    {
        if (double.IsNaN(x)) return Empty;
        if (double.IsPositiveInfinity(x)) return new Interval(double.MaxValue, double.PositiveInfinity, true);
        if (double.IsNegativeInfinity(x)) return new Interval(double.NegativeInfinity, -double.MaxValue, true);
        return new Interval(x, x, true);
    }

    /// <summary>
    /// [x - 1 ulp, x + 1 ulp]: encloses the real number that x approximates when x is the
    /// correctly rounded value of a decimal literal or constant.
    /// </summary>
    public static Interval Widened(double x)
    {
        if (double.IsNaN(x)) return Empty;
        return Normalize(Math.BitDecrement(x), Math.BitIncrement(x));
    }

    /// <summary>Converts a double to a point interval.</summary>
    public static implicit operator Interval(double x) => Point(x);

    /// <summary>True for the empty set.</summary>
    public bool IsEmpty => double.IsNaN(Lo);

    /// <summary>True for [-Infinity, +Infinity].</summary>
    public bool IsEntire => Lo == double.NegativeInfinity && Hi == double.PositiveInfinity;

    /// <summary>True if Lo == Hi.</summary>
    public bool IsPoint => !IsEmpty && Lo == Hi;

    /// <summary>Hi - Lo rounded up (NaN for empty).</summary>
    public double Width => IsEmpty ? double.NaN : AddUp(Hi, -Lo);

    /// <summary>Midpoint (finite when both bounds are finite).</summary>
    public double Midpoint
    {
        get
        {
            if (IsEmpty) return double.NaN;
            if (IsEntire) return 0;
            if (double.IsNegativeInfinity(Lo)) return -double.MaxValue;
            if (double.IsPositiveInfinity(Hi)) return double.MaxValue;
            double m = 0.5 * Lo + 0.5 * Hi;
            return Math.Clamp(m, Lo, Hi);
        }
    }

    /// <summary>True if x lies in the interval.</summary>
    public bool Contains(double x) => !IsEmpty && Lo <= x && x <= Hi;

    /// <summary>True if 0 lies in the interval.</summary>
    public bool ContainsZero => Contains(0);

    /// <summary>True if every element is &gt; 0.</summary>
    public bool IsStrictlyPositive => !IsEmpty && Lo > 0;

    /// <summary>True if every element is &lt; 0.</summary>
    public bool IsStrictlyNegative => !IsEmpty && Hi < 0;

    /// <summary>Intersection of two intervals.</summary>
    public static Interval Intersect(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        double lo = Math.Max(a.Lo, b.Lo), hi = Math.Min(a.Hi, b.Hi);
        return lo <= hi ? new Interval(lo, hi, true) : Empty;
    }

    /// <summary>Smallest interval containing both inputs (interval hull).</summary>
    public static Interval Hull(Interval a, Interval b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return new Interval(Math.Min(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi), true);
    }

    // ---------------------------------------------------------------- directed rounding helpers

    private static Interval Normalize(double lo, double hi)
    {
        if (double.IsNaN(lo)) lo = double.NegativeInfinity;
        if (double.IsNaN(hi)) hi = double.PositiveInfinity;
        if (double.IsPositiveInfinity(lo)) lo = double.MaxValue;
        if (double.IsNegativeInfinity(hi)) hi = -double.MaxValue;
        if (lo > hi) return Empty;
        return new Interval(lo, hi, true);
    }

    private const double UnderflowGuard = 1e-290;

    internal static double AddDown(double a, double b)
    {
        double s = a + b;
        if (!double.IsFinite(s))
        {
            if (double.IsNaN(s)) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(s) && double.IsFinite(a) && double.IsFinite(b)) return double.MaxValue;
            return s;
        }
        double bb = s - a;
        double err = (a - (s - bb)) + (b - bb);
        return err < 0 ? Math.BitDecrement(s) : s;
    }

    internal static double AddUp(double a, double b)
    {
        double s = a + b;
        if (!double.IsFinite(s))
        {
            if (double.IsNaN(s)) return double.PositiveInfinity;
            if (double.IsNegativeInfinity(s) && double.IsFinite(a) && double.IsFinite(b)) return -double.MaxValue;
            return s;
        }
        double bb = s - a;
        double err = (a - (s - bb)) + (b - bb);
        return err > 0 ? Math.BitIncrement(s) : s;
    }

    internal static double MulDown(double a, double b)
    {
        if (a == 0 || b == 0) return 0;
        double p = a * b;
        if (double.IsNaN(p)) return double.NegativeInfinity;
        if (double.IsInfinity(p))
            return double.IsPositiveInfinity(p) && double.IsFinite(a) && double.IsFinite(b) ? double.MaxValue : p;
        if (Math.Abs(p) < UnderflowGuard) return Math.BitDecrement(p);
        double e = Math.FusedMultiplyAdd(a, b, -p);
        return e < 0 ? Math.BitDecrement(p) : p;
    }

    internal static double MulUp(double a, double b)
    {
        if (a == 0 || b == 0) return 0;
        double p = a * b;
        if (double.IsNaN(p)) return double.PositiveInfinity;
        if (double.IsInfinity(p))
            return double.IsNegativeInfinity(p) && double.IsFinite(a) && double.IsFinite(b) ? -double.MaxValue : p;
        if (Math.Abs(p) < UnderflowGuard) return Math.BitIncrement(p);
        double e = Math.FusedMultiplyAdd(a, b, -p);
        return e > 0 ? Math.BitIncrement(p) : p;
    }

    internal static double DivDown(double a, double b)
    {
        // b != 0 is guaranteed by callers.
        if (double.IsInfinity(b))
        {
            if (double.IsInfinity(a)) return Math.Sign(a) * Math.Sign(b) > 0 ? 0 : double.NegativeInfinity;
            return 0;
        }
        if (double.IsInfinity(a)) return a / b;
        if (a == 0) return 0;
        double q = a / b;
        if (double.IsInfinity(q)) return q > 0 ? double.MaxValue : double.NegativeInfinity;
        if (Math.Abs(q) < UnderflowGuard || Math.Abs(a) < UnderflowGuard) return Math.BitDecrement(q);
        double r = Math.FusedMultiplyAdd(-q, b, a); // exact remainder a - q b
        if (r == 0) return q;
        bool trueBelow = (r < 0) != (b < 0); // sign(r / b) < 0
        return trueBelow ? Math.BitDecrement(q) : q;
    }

    internal static double DivUp(double a, double b)
    {
        if (double.IsInfinity(b))
        {
            if (double.IsInfinity(a)) return Math.Sign(a) * Math.Sign(b) > 0 ? double.PositiveInfinity : 0;
            return 0;
        }
        if (double.IsInfinity(a)) return a / b;
        if (a == 0) return 0;
        double q = a / b;
        if (double.IsInfinity(q)) return q < 0 ? -double.MaxValue : double.PositiveInfinity;
        if (Math.Abs(q) < UnderflowGuard || Math.Abs(a) < UnderflowGuard) return Math.BitIncrement(q);
        double r = Math.FusedMultiplyAdd(-q, b, a);
        if (r == 0) return q;
        bool trueAbove = (r > 0) == (b > 0); // sign(r / b) > 0
        return trueAbove ? Math.BitIncrement(q) : q;
    }

    private static double SqrtDown(double x)
    {
        if (x <= 0) return 0;
        double s = Math.Sqrt(x);
        if (double.IsInfinity(s)) return double.MaxValue;
        if (x < UnderflowGuard) return Math.Max(0, Math.BitDecrement(s));
        double r = Math.FusedMultiplyAdd(-s, s, x);
        return r < 0 ? Math.BitDecrement(s) : s;
    }

    private static double SqrtUp(double x)
    {
        if (x <= 0) return 0;
        double s = Math.Sqrt(x);
        if (double.IsInfinity(s)) return s;
        if (x < UnderflowGuard) return Math.BitIncrement(s);
        double r = Math.FusedMultiplyAdd(-s, s, x);
        return r > 0 ? Math.BitIncrement(s) : s;
    }

    /// <summary>Widens a library function result downward by two ulps (covers libm error).</summary>
    private static double Dn(double x)
    {
        if (double.IsNaN(x)) return double.NegativeInfinity;
        return Math.BitDecrement(Math.BitDecrement(x));
    }

    /// <summary>Widens a library function result upward by two ulps.</summary>
    private static double Up(double x)
    {
        if (double.IsNaN(x)) return double.PositiveInfinity;
        return Math.BitIncrement(Math.BitIncrement(x));
    }

    // ---------------------------------------------------------------- arithmetic

    /// <summary>Unary plus.</summary>
    public static Interval operator +(Interval a) => a;

    /// <summary>Negation (exact).</summary>
    public static Interval operator -(Interval a) => a.IsEmpty ? Empty : new Interval(-a.Hi, -a.Lo, true);

    /// <summary>Addition with outward rounding.</summary>
    public static Interval operator +(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        return Normalize(AddDown(a.Lo, b.Lo), AddUp(a.Hi, b.Hi));
    }

    /// <summary>Subtraction with outward rounding.</summary>
    public static Interval operator -(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        return Normalize(AddDown(a.Lo, -b.Hi), AddUp(a.Hi, -b.Lo));
    }

    /// <summary>Multiplication with outward rounding (0 times Infinity is taken as 0).</summary>
    public static Interval operator *(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        double lo = Math.Min(Math.Min(MulDown(a.Lo, b.Lo), MulDown(a.Lo, b.Hi)), Math.Min(MulDown(a.Hi, b.Lo), MulDown(a.Hi, b.Hi)));
        double hi = Math.Max(Math.Max(MulUp(a.Lo, b.Lo), MulUp(a.Lo, b.Hi)), Math.Max(MulUp(a.Hi, b.Lo), MulUp(a.Hi, b.Hi)));
        return Normalize(lo, hi);
    }

    /// <summary>
    /// Division with outward rounding. A divisor containing 0 in its interior gives
    /// <see cref="Entire"/>; a divisor of exactly [0, 0] gives <see cref="Empty"/>; a divisor with
    /// 0 as an endpoint gives a half-line.
    /// </summary>
    public static Interval operator /(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        if (b.Lo > 0 || b.Hi < 0)
        {
            double lo = Math.Min(Math.Min(DivDown(a.Lo, b.Lo), DivDown(a.Lo, b.Hi)), Math.Min(DivDown(a.Hi, b.Lo), DivDown(a.Hi, b.Hi)));
            double hi = Math.Max(Math.Max(DivUp(a.Lo, b.Lo), DivUp(a.Lo, b.Hi)), Math.Max(DivUp(a.Hi, b.Lo), DivUp(a.Hi, b.Hi)));
            return Normalize(lo, hi);
        }
        if (b.Lo == 0 && b.Hi == 0) return Empty;
        if (a.Lo == 0 && a.Hi == 0) return new Interval(0, 0, true);
        if (b.Lo < 0 && b.Hi > 0) return Entire;
        return a * Reciprocal(b);
    }

    /// <summary>Reciprocal 1/x with the same zero conventions as division.</summary>
    public static Interval Reciprocal(Interval x)
    {
        if (x.IsEmpty) return Empty;
        if (x.Lo > 0 || x.Hi < 0) return Normalize(DivDown(1, x.Hi), DivUp(1, x.Lo));
        if (x.Lo == 0 && x.Hi == 0) return Empty;
        if (x.Lo < 0 && x.Hi > 0) return Entire;
        if (x.Lo == 0) return new Interval(DivDown(1, x.Hi), double.PositiveInfinity, true);
        return new Interval(double.NegativeInfinity, DivUp(1, x.Lo), true);
    }

    // ---------------------------------------------------------------- powers and roots

    /// <summary>Square root; the negative part of the input is clipped.</summary>
    public static Interval Sqrt(Interval x)
    {
        if (x.IsEmpty || x.Hi < 0) return Empty;
        return Normalize(SqrtDown(Math.Max(x.Lo, 0)), SqrtUp(x.Hi));
    }

    /// <summary>x^2 (tighter than x * x).</summary>
    public static Interval Sqr(Interval x) => Pow(x, 2);

    /// <summary>Integer power (exact interval semantics, even powers are non-negative).</summary>
    public static Interval Pow(Interval x, int n)
    {
        if (x.IsEmpty) return Empty;
        if (n == 0) return new Interval(1, 1, true);
        if (n == 1) return x;
        if (n < 0)
        {
            Interval p = PowLong(x, -(long)n);
            return Reciprocal(p);
        }
        return PowLong(x, n);
    }

    private static Interval PowLong(Interval x, long n)
    {
        if (n == 1) return x;
        bool even = (n & 1) == 0;
        if (x.Lo >= 0) return Normalize(PowDownScalar(x.Lo, n), PowUpScalar(x.Hi, n));
        if (x.Hi <= 0)
        {
            double lo = PowDownScalar(-x.Hi, n), hi = PowUpScalar(-x.Lo, n);
            return even ? Normalize(lo, hi) : Normalize(-hi, -lo);
        }
        if (even) return Normalize(0, PowUpScalar(Math.Max(-x.Lo, x.Hi), n));
        return Normalize(-PowUpScalar(-x.Lo, n), PowUpScalar(x.Hi, n));
    }

    private static double PowDownScalar(double a, long n)
    {
        // a >= 0, n >= 1; lower bound of a^n by repeated squaring with downward rounding.
        double result = 1, b = a;
        bool first = true;
        while (n > 0)
        {
            if ((n & 1) != 0) result = first ? b : MulDown(result, b);
            if ((n & 1) != 0) first = false;
            n >>= 1;
            if (n > 0) b = MulDown(b, b);
        }
        return result;
    }

    private static double PowUpScalar(double a, long n)
    {
        double result = 1, b = a;
        bool first = true;
        while (n > 0)
        {
            if ((n & 1) != 0) result = first ? b : MulUp(result, b);
            if ((n & 1) != 0) first = false;
            n >>= 1;
            if (n > 0) b = MulUp(b, b);
        }
        return result;
    }

    /// <summary>
    /// Real power x^p. Integer p uses <see cref="Pow(Interval, int)"/>; non-integer p is defined
    /// for x &gt;= 0 only (the negative part is clipped).
    /// </summary>
    public static Interval Pow(Interval x, double p)
    {
        if (x.IsEmpty || double.IsNaN(p)) return Empty;
        if (p == Math.Floor(p) && Math.Abs(p) <= int.MaxValue) return Pow(x, (int)p);
        Interval d = Intersect(x, new Interval(0, double.PositiveInfinity, true));
        if (d.IsEmpty) return Empty;
        if (p > 0)
        {
            double lo = d.Lo == 0 ? 0 : Math.Max(0, Dn(Dn(Math.Pow(d.Lo, p))));
            double hi = Up(Up(Math.Pow(d.Hi, p)));
            return Normalize(lo, hi);
        }
        else
        {
            if (d.Hi == 0) return Empty;
            double lo = Math.Max(0, Dn(Dn(Math.Pow(d.Hi, p))));
            double hi = d.Lo == 0 ? double.PositiveInfinity : Up(Up(Math.Pow(d.Lo, p)));
            return Normalize(lo, hi);
        }
    }

    /// <summary>
    /// General power x^y. A point exponent uses the real-power rules; otherwise x is clipped to
    /// x &gt;= 0 and the result is exp(y log x).
    /// </summary>
    public static Interval Pow(Interval x, Interval y)
    {
        if (x.IsEmpty || y.IsEmpty) return Empty;
        if (y.IsPoint) return Pow(x, y.Lo);
        Interval d = Intersect(x, new Interval(0, double.PositiveInfinity, true));
        if (d.IsEmpty) return Empty;
        if (d.Hi == 0)
        {
            // 0^y: 0 for y > 0, 1 for y = 0, undefined (infinite) for y < 0.
            Interval r = Empty;
            if (y.Hi > 0) r = Hull(r, new Interval(0, 0, true));
            if (y.Contains(0)) r = Hull(r, new Interval(1, 1, true));
            if (y.Lo < 0) r = Hull(r, new Interval(double.MaxValue, double.PositiveInfinity, true));
            return r;
        }
        return Exp(y * Log(d));
    }

    // ---------------------------------------------------------------- exponential and logarithm

    /// <summary>Exponential.</summary>
    public static Interval Exp(Interval x)
    {
        if (x.IsEmpty) return Empty;
        double lo = Math.Max(0, Dn(Math.Exp(x.Lo)));
        double hi = Up(Math.Exp(x.Hi));
        return Normalize(lo, hi);
    }

    /// <summary>Natural logarithm; the non-positive part of the input is clipped.</summary>
    public static Interval Log(Interval x)
    {
        if (x.IsEmpty || x.Hi <= 0) return Empty;
        double lo = x.Lo <= 0 ? double.NegativeInfinity : Dn(Math.Log(x.Lo));
        double hi = Up(Math.Log(x.Hi));
        return Normalize(lo, hi);
    }

    /// <summary>Base-10 logarithm; the non-positive part of the input is clipped.</summary>
    public static Interval Log10(Interval x)
    {
        if (x.IsEmpty || x.Hi <= 0) return Empty;
        double lo = x.Lo <= 0 ? double.NegativeInfinity : Dn(Math.Log10(x.Lo));
        double hi = Up(Math.Log10(x.Hi));
        return Normalize(lo, hi);
    }

    // ---------------------------------------------------------------- trigonometric

    /// <summary>
    /// Conservative test whether some point offset + k * period (k integer) may lie in [a, b].
    /// False negatives are impossible; false positives only widen results.
    /// </summary>
    private static bool MayContainCritical(double a, double b, double offset, double period)
    {
        double ta = (a - offset) / period;
        double tb = (b - offset) / period;
        double slack = 1e-12 * Math.Max(1.0, Math.Max(Math.Abs(ta), Math.Abs(tb)));
        return Math.Floor(tb + slack) >= Math.Ceiling(ta - slack);
    }

    private static Interval Clamp(double lo, double hi, double min, double max) =>
        Normalize(Math.Max(lo, min), Math.Min(hi, max));

    /// <summary>Sine.</summary>
    public static Interval Sin(Interval x)
    {
        if (x.IsEmpty) return Empty;
        if (!double.IsFinite(x.Lo) || !double.IsFinite(x.Hi) || x.Hi - x.Lo >= TwoPi) return new Interval(-1, 1, true);
        double s1 = Math.Sin(x.Lo), s2 = Math.Sin(x.Hi);
        double lo = Dn(Math.Min(s1, s2)), hi = Up(Math.Max(s1, s2));
        if (MayContainCritical(x.Lo, x.Hi, Math.PI / 2, TwoPi)) hi = 1;
        if (MayContainCritical(x.Lo, x.Hi, -Math.PI / 2, TwoPi)) lo = -1;
        return Clamp(lo, hi, -1, 1);
    }

    /// <summary>Cosine.</summary>
    public static Interval Cos(Interval x)
    {
        if (x.IsEmpty) return Empty;
        if (!double.IsFinite(x.Lo) || !double.IsFinite(x.Hi) || x.Hi - x.Lo >= TwoPi) return new Interval(-1, 1, true);
        double c1 = Math.Cos(x.Lo), c2 = Math.Cos(x.Hi);
        double lo = Dn(Math.Min(c1, c2)), hi = Up(Math.Max(c1, c2));
        if (MayContainCritical(x.Lo, x.Hi, 0, TwoPi)) hi = 1;
        if (MayContainCritical(x.Lo, x.Hi, Math.PI, TwoPi)) lo = -1;
        return Clamp(lo, hi, -1, 1);
    }

    /// <summary>Tangent; an input that may contain a pole gives <see cref="Entire"/>.</summary>
    public static Interval Tan(Interval x)
    {
        if (x.IsEmpty) return Empty;
        if (!double.IsFinite(x.Lo) || !double.IsFinite(x.Hi) || x.Hi - x.Lo >= Math.PI) return Entire;
        if (MayContainCritical(x.Lo, x.Hi, Math.PI / 2, Math.PI)) return Entire;
        return Normalize(Dn(Math.Tan(x.Lo)), Up(Math.Tan(x.Hi)));
    }

    /// <summary>Arcsine; the input is clipped to [-1, 1].</summary>
    public static Interval Asin(Interval x)
    {
        if (x.IsEmpty || x.Hi < -1 || x.Lo > 1) return Empty;
        double a = Math.Max(x.Lo, -1), b = Math.Min(x.Hi, 1);
        return Clamp(Dn(Math.Asin(a)), Up(Math.Asin(b)), Dn(-Math.PI / 2), Up(Math.PI / 2));
    }

    /// <summary>Arccosine; the input is clipped to [-1, 1].</summary>
    public static Interval Acos(Interval x)
    {
        if (x.IsEmpty || x.Hi < -1 || x.Lo > 1) return Empty;
        double a = Math.Max(x.Lo, -1), b = Math.Min(x.Hi, 1);
        return Clamp(Dn(Math.Acos(b)), Up(Math.Acos(a)), 0, Up(Math.PI));
    }

    /// <summary>Arctangent.</summary>
    public static Interval Atan(Interval x)
    {
        if (x.IsEmpty) return Empty;
        return Clamp(Dn(Math.Atan(x.Lo)), Up(Math.Atan(x.Hi)), Dn(-Math.PI / 2), Up(Math.PI / 2));
    }

    // ---------------------------------------------------------------- hyperbolic

    /// <summary>Hyperbolic sine.</summary>
    public static Interval Sinh(Interval x)
    {
        if (x.IsEmpty) return Empty;
        return Normalize(Dn(Math.Sinh(x.Lo)), Up(Math.Sinh(x.Hi)));
    }

    /// <summary>Hyperbolic cosine.</summary>
    public static Interval Cosh(Interval x)
    {
        if (x.IsEmpty) return Empty;
        if (x.Lo <= 0 && x.Hi >= 0)
            return Normalize(1, Up(Math.Max(Math.Cosh(x.Lo), Math.Cosh(x.Hi))));
        if (x.Lo > 0) return Normalize(Math.Max(1, Dn(Math.Cosh(x.Lo))), Up(Math.Cosh(x.Hi)));
        return Normalize(Math.Max(1, Dn(Math.Cosh(x.Hi))), Up(Math.Cosh(x.Lo)));
    }

    /// <summary>Hyperbolic tangent.</summary>
    public static Interval Tanh(Interval x)
    {
        if (x.IsEmpty) return Empty;
        return Clamp(Dn(Math.Tanh(x.Lo)), Up(Math.Tanh(x.Hi)), -1, 1);
    }

    // ---------------------------------------------------------------- piecewise

    /// <summary>Absolute value.</summary>
    public static Interval Abs(Interval x)
    {
        if (x.IsEmpty) return Empty;
        if (x.Lo >= 0) return x;
        if (x.Hi <= 0) return -x;
        return new Interval(0, Math.Max(-x.Lo, x.Hi), true);
    }

    /// <summary>Pointwise minimum.</summary>
    public static Interval Min(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        return new Interval(Math.Min(a.Lo, b.Lo), Math.Min(a.Hi, b.Hi), true);
    }

    /// <summary>Pointwise maximum.</summary>
    public static Interval Max(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        return new Interval(Math.Max(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi), true);
    }

    /// <summary>Floor (exact).</summary>
    public static Interval Floor(Interval x) =>
        x.IsEmpty ? Empty : Normalize(Math.Floor(x.Lo), Math.Floor(x.Hi));

    /// <summary>Ceiling (exact).</summary>
    public static Interval Ceiling(Interval x) =>
        x.IsEmpty ? Empty : Normalize(Math.Ceiling(x.Lo), Math.Ceiling(x.Hi));

    /// <summary>Sign function enclosure.</summary>
    public static Interval Sign(Interval x)
    {
        if (x.IsEmpty) return Empty;
        return new Interval(Math.Sign(x.Lo), Math.Sign(x.Hi), true);
    }

    // ---------------------------------------------------------------- equality and formatting

    /// <inheritdoc/>
    public bool Equals(Interval other) =>
        (IsEmpty && other.IsEmpty) || (Lo.Equals(other.Lo) && Hi.Equals(other.Hi));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Interval i && Equals(i);

    /// <inheritdoc/>
    public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(Lo, Hi);

    /// <summary>Equality (all empty intervals are equal).</summary>
    public static bool operator ==(Interval a, Interval b) => a.Equals(b);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Interval a, Interval b) => !a.Equals(b);

    /// <summary>Formats as "[lo, hi]" or "Empty".</summary>
    public override string ToString() =>
        IsEmpty ? "Empty" : string.Create(CultureInfo.InvariantCulture, $"[{Lo:R}, {Hi:R}]");
}
