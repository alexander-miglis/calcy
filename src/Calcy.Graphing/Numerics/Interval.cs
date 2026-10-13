namespace Calcy.Graphing.Numerics;

/// <summary>
/// A closed interval of reals with outward rounding, used to prove that a function cannot be zero on a box
/// (implicit plots) or that a whole box satisfies an inequality (shaded regions).
/// <see cref="MaybeUndefined"/> records that some points of the box may be outside the function's domain;
/// <see cref="IsEmpty"/> means no point of the box is in the domain.
/// </summary>
public readonly struct Interval : IEquatable<Interval>
{
    /// <summary>Lower bound (may be negative infinity).</summary>
    public double Lo { get; }

    /// <summary>Upper bound (may be positive infinity).</summary>
    public double Hi { get; }

    /// <summary>True when some points of the box may be undefined (for example sqrt of a box reaching below 0).</summary>
    public bool MaybeUndefined { get; }

    /// <summary>True when the function is undefined on the whole box.</summary>
    public bool IsEmpty { get; }

    /// <summary>Creates an interval; the bounds are swapped when given in the wrong order.</summary>
    public Interval(double lo, double hi, bool maybeUndefined = false)
    {
        if (double.IsNaN(lo) || double.IsNaN(hi))
        {
            Lo = double.NegativeInfinity;
            Hi = double.PositiveInfinity;
            MaybeUndefined = true;
            IsEmpty = false;
            return;
        }
        if (lo > hi) (lo, hi) = (hi, lo);
        Lo = lo;
        Hi = hi;
        MaybeUndefined = maybeUndefined;
        IsEmpty = false;
    }

    private Interval(bool empty)
    {
        Lo = double.NaN;
        Hi = double.NaN;
        MaybeUndefined = true;
        IsEmpty = empty;
    }

    /// <summary>The interval holding a single number.</summary>
    public static Interval Point(double v) => double.IsNaN(v) ? Empty : new Interval(v, v);

    /// <summary>All reals, possibly undefined somewhere (the result when nothing better is known).</summary>
    public static readonly Interval Entire = new(double.NegativeInfinity, double.PositiveInfinity, true);

    /// <summary>Undefined everywhere on the box.</summary>
    public static readonly Interval Empty = new(true);

    /// <summary>True when the interval may contain v.</summary>
    public bool Contains(double v) => !IsEmpty && Lo <= v && v <= Hi;

    /// <summary>True when zero may be a value of the function on the box.</summary>
    public bool ContainsZero => !IsEmpty && Lo <= 0 && Hi >= 0;

    /// <summary>Width of the interval (infinity for unbounded intervals).</summary>
    public double Width => IsEmpty ? 0 : Hi - Lo;

    /// <summary>Same bounds, flagged as possibly undefined.</summary>
    public Interval WithMaybeUndefined(bool flag = true) => IsEmpty ? this : new Interval(Lo, Hi, MaybeUndefined || flag);

    /// <summary>Smallest interval containing both (undefinedness is combined).</summary>
    public static Interval Hull(Interval a, Interval b)
    {
        if (a.IsEmpty) return b.WithMaybeUndefined();
        if (b.IsEmpty) return a.WithMaybeUndefined();
        return new Interval(Math.Min(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi), a.MaybeUndefined || b.MaybeUndefined);
    }

    private static double Down(double v) => double.IsFinite(v) ? Math.BitDecrement(v) : v;
    private static double Up(double v) => double.IsFinite(v) ? Math.BitIncrement(v) : v;

    private static Interval Rounded(double lo, double hi, bool maybe) => new(Down(lo), Up(hi), maybe);

    /// <summary>Sum of two boxes (outward rounded).</summary>
    public static Interval operator +(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        return Rounded(a.Lo + b.Lo, a.Hi + b.Hi, a.MaybeUndefined || b.MaybeUndefined);
    }

    /// <summary>Negation.</summary>
    public static Interval operator -(Interval a) => a.IsEmpty ? Empty : new Interval(-a.Hi, -a.Lo, a.MaybeUndefined);

    /// <summary>Difference of two boxes.</summary>
    public static Interval operator -(Interval a, Interval b) => a + (-b);

    /// <summary>Product of two boxes (outward rounded).</summary>
    public static Interval operator *(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        bool maybe = a.MaybeUndefined || b.MaybeUndefined;
        if (a.Lo == a.Hi && b.Lo == b.Hi) return Rounded(MulZero(a.Lo, b.Lo), MulZero(a.Lo, b.Lo), maybe);
        double p1 = MulZero(a.Lo, b.Lo), p2 = MulZero(a.Lo, b.Hi), p3 = MulZero(a.Hi, b.Lo), p4 = MulZero(a.Hi, b.Hi);
        double lo = Math.Min(Math.Min(p1, p2), Math.Min(p3, p4));
        double hi = Math.Max(Math.Max(p1, p2), Math.Max(p3, p4));
        return Rounded(lo, hi, maybe);
    }

    /// <summary>Multiplication where 0 times infinity is 0 (the bound is attained only by finite values).</summary>
    private static double MulZero(double x, double y) => x == 0 || y == 0 ? 0 : x * y;

    /// <summary>1/a. A box touching 0 gives a half-line, a box containing 0 gives every real.</summary>
    public static Interval Reciprocal(Interval a)
    {
        if (a.IsEmpty) return Empty;
        if (a.Lo == 0 && a.Hi == 0) return Empty;
        if (a.Lo > 0 || a.Hi < 0) return Rounded(1 / a.Hi, 1 / a.Lo, a.MaybeUndefined);
        if (a.Lo == 0) return new Interval(Down(1 / a.Hi), double.PositiveInfinity, true);
        if (a.Hi == 0) return new Interval(double.NegativeInfinity, Up(1 / a.Lo), true);
        return Entire;
    }

    /// <summary>Quotient; a divisor box touching 0 widens the result and marks it possibly undefined.</summary>
    public static Interval operator /(Interval a, Interval b) => a * Reciprocal(b);

    /// <summary>Integer power with tight bounds for even exponents.</summary>
    public static Interval PowInt(Interval a, int n)
    {
        if (a.IsEmpty) return Empty;
        if (n == 0) return new Interval(1, 1, a.MaybeUndefined);
        if (n < 0) return Reciprocal(PowInt(a, -n));
        if (n == 1) return a;
        double pl = Math.Pow(a.Lo, n), ph = Math.Pow(a.Hi, n);
        if ((n & 1) == 1) return Rounded(pl, ph, a.MaybeUndefined);
        if (a.Lo >= 0) return Rounded(pl, ph, a.MaybeUndefined);
        if (a.Hi <= 0) return Rounded(ph, pl, a.MaybeUndefined);
        return new Interval(0, Up(Math.Max(pl, ph)), a.MaybeUndefined);
    }

    /// <summary>Real p/q power of a box: odd q keeps negative bases real, even q needs a base at or above 0.</summary>
    public static Interval PowRational(Interval a, long p, long q)
    {
        if (a.IsEmpty) return Empty;
        if (q == 1) return PowInt(a, (int)p);
        var root = Root(a, q);
        return PowInt(root, (int)p);
    }

    /// <summary>The real q-th root (monotone increasing).</summary>
    public static Interval Root(Interval a, long q)
    {
        if (a.IsEmpty) return Empty;
        if ((q & 1) == 1)
            return Rounded(OddRoot(a.Lo, q), OddRoot(a.Hi, q), a.MaybeUndefined);
        if (a.Hi < 0) return Empty;
        bool maybe = a.MaybeUndefined || a.Lo < 0;
        double lo = Math.Max(a.Lo, 0);
        return new Interval(Math.Max(0, Down(Math.Pow(lo, 1.0 / q))), Up(Math.Pow(a.Hi, 1.0 / q)), maybe);
    }

    private static double OddRoot(double x, long q) => x >= 0 ? Math.Pow(x, 1.0 / q) : -Math.Pow(-x, 1.0 / q);

    /// <summary>a^b for any exponent box, as exp(b ln a) on the part of a above 0.</summary>
    public static Interval Pow(Interval a, Interval b)
    {
        if (a.IsEmpty || b.IsEmpty) return Empty;
        if (b.Lo == b.Hi && Math.Abs(b.Lo) <= 64 && b.Lo == Math.Floor(b.Lo)) return PowInt(a, (int)b.Lo);
        var r = Exp(b * Log(a));
        return r;
    }

    /// <summary>Square root; the part of the box below 0 is undefined.</summary>
    public static Interval Sqrt(Interval a) => Root(a, 2);

    /// <summary>Exponential (monotone).</summary>
    public static Interval Exp(Interval a) => a.IsEmpty ? Empty : new Interval(Math.Max(0, Down(Math.Exp(a.Lo))), Up(Math.Exp(a.Hi)), a.MaybeUndefined);

    /// <summary>Natural logarithm; the part of the box at or below 0 is undefined.</summary>
    public static Interval Log(Interval a)
    {
        if (a.IsEmpty || a.Hi <= 0) return Empty;
        bool maybe = a.MaybeUndefined || a.Lo <= 0;
        double lo = a.Lo <= 0 ? double.NegativeInfinity : Down(Math.Log(a.Lo));
        return new Interval(lo, Up(Math.Log(a.Hi)), maybe);
    }

    /// <summary>Applies a non-decreasing function to both bounds.</summary>
    public static Interval Monotone(Interval a, Func<double, double> f)
    {
        if (a.IsEmpty) return Empty;
        double lo = f(a.Lo), hi = f(a.Hi);
        if (double.IsNaN(lo) || double.IsNaN(hi)) return Entire;
        return Rounded(lo, hi, a.MaybeUndefined);
    }

    /// <summary>Applies a non-increasing function to both bounds.</summary>
    public static Interval Decreasing(Interval a, Func<double, double> f)
    {
        if (a.IsEmpty) return Empty;
        double lo = f(a.Hi), hi = f(a.Lo);
        if (double.IsNaN(lo) || double.IsNaN(hi)) return Entire;
        return Rounded(lo, hi, a.MaybeUndefined);
    }

    /// <summary>Restricts a box to a closed domain [lo, hi]; parts outside become "maybe undefined".</summary>
    public static Interval ClampDomain(Interval a, double lo, double hi, out bool empty)
    {
        empty = false;
        if (a.IsEmpty || a.Hi < lo || a.Lo > hi)
        {
            empty = true;
            return Empty;
        }
        bool maybe = a.MaybeUndefined || a.Lo < lo || a.Hi > hi;
        return new Interval(Math.Max(a.Lo, lo), Math.Min(a.Hi, hi), maybe);
    }

    /// <summary>Absolute value (0 is the minimum when the box contains 0).</summary>
    public static Interval Abs(Interval a)
    {
        if (a.IsEmpty) return Empty;
        if (a.Lo >= 0) return a;
        if (a.Hi <= 0) return -a;
        return new Interval(0, Math.Max(-a.Lo, a.Hi), a.MaybeUndefined);
    }

    /// <summary>Sine, as a shifted cosine.</summary>
    public static Interval Sin(Interval a) => Cos(a - new Interval(Math.PI / 2, Math.PI / 2));

    /// <summary>Cosine with the extrema inside the box taken into account.</summary>
    public static Interval Cos(Interval a)
    {
        if (a.IsEmpty) return Empty;
        if (!double.IsFinite(a.Lo) || !double.IsFinite(a.Hi) || a.Hi - a.Lo >= 2 * Math.PI)
            return new Interval(-1, 1, a.MaybeUndefined);
        double lo = Math.Min(Math.Cos(a.Lo), Math.Cos(a.Hi));
        double hi = Math.Max(Math.Cos(a.Lo), Math.Cos(a.Hi));
        // Maxima at 2kπ, minima at (2k+1)π.
        if (ContainsMultiple(a.Lo, a.Hi, 2 * Math.PI, 0)) hi = 1;
        if (ContainsMultiple(a.Lo, a.Hi, 2 * Math.PI, Math.PI)) lo = -1;
        return new Interval(Math.Max(-1, Down(lo)), Math.Min(1, Up(hi)), a.MaybeUndefined);
    }

    /// <summary>True when [lo, hi] contains offset + k·period for some integer k (slightly widened for rounding).</summary>
    private static bool ContainsMultiple(double lo, double hi, double period, double offset)
    {
        double k = Math.Ceiling((lo - offset) / period - 1e-12);
        return offset + k * period <= hi + 1e-12 * Math.Max(1, Math.Abs(hi));
    }

    /// <summary>Tangent; a box containing a pole gives every real and may be undefined.</summary>
    public static Interval Tan(Interval a)
    {
        if (a.IsEmpty) return Empty;
        if (!double.IsFinite(a.Lo) || !double.IsFinite(a.Hi) || a.Hi - a.Lo >= Math.PI) return Entire;
        if (ContainsMultiple(a.Lo, a.Hi, Math.PI, Math.PI / 2)) return Entire;
        return Rounded(Math.Tan(a.Lo), Math.Tan(a.Hi), a.MaybeUndefined);
    }

    /// <summary>Inverse sine on [-1, 1].</summary>
    public static Interval Asin(Interval a)
    {
        var d = ClampDomain(a, -1, 1, out bool empty);
        return empty ? Empty : Monotone(d, Math.Asin).WithMaybeUndefined(d.MaybeUndefined);
    }

    /// <summary>Inverse cosine on [-1, 1] (decreasing).</summary>
    public static Interval Acos(Interval a)
    {
        var d = ClampDomain(a, -1, 1, out bool empty);
        return empty ? Empty : Decreasing(d, Math.Acos).WithMaybeUndefined(d.MaybeUndefined);
    }

    /// <summary>Hyperbolic cosine (minimum 1 at 0).</summary>
    public static Interval Cosh(Interval a)
    {
        if (a.IsEmpty) return Empty;
        var abs = Abs(a);
        return Rounded(Math.Cosh(abs.Lo), Math.Cosh(abs.Hi), a.MaybeUndefined);
    }

    /// <summary>Pointwise minimum.</summary>
    public static Interval Min(Interval a, Interval b) =>
        a.IsEmpty || b.IsEmpty ? Empty : new Interval(Math.Min(a.Lo, b.Lo), Math.Min(a.Hi, b.Hi), a.MaybeUndefined || b.MaybeUndefined);

    /// <summary>Pointwise maximum.</summary>
    public static Interval Max(Interval a, Interval b) =>
        a.IsEmpty || b.IsEmpty ? Empty : new Interval(Math.Max(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi), a.MaybeUndefined || b.MaybeUndefined);

    /// <summary>Bounds for atan2(y, x) (conservative: the full range when the box touches the branch cut).</summary>
    public static Interval Atan2(Interval y, Interval x)
    {
        if (y.IsEmpty || x.IsEmpty) return Empty;
        bool maybe = y.MaybeUndefined || x.MaybeUndefined;
        if (x.Lo > 0 || y.Lo > 0 || y.Hi < 0)
        {
            double a1 = Math.Atan2(y.Lo, x.Lo), a2 = Math.Atan2(y.Lo, x.Hi), a3 = Math.Atan2(y.Hi, x.Lo), a4 = Math.Atan2(y.Hi, x.Hi);
            if (x.Lo <= 0 && x.Hi >= 0 && y.Lo > 0) return Rounded(Math.Min(a2, a4), Math.Max(a1, a3), maybe);
            if (x.Lo <= 0 && x.Hi >= 0 && y.Hi < 0) return Rounded(Math.Min(a1, a3), Math.Max(a2, a4), maybe);
            return Rounded(Math.Min(Math.Min(a1, a2), Math.Min(a3, a4)), Math.Max(Math.Max(a1, a2), Math.Max(a3, a4)), maybe);
        }
        return new Interval(-Math.PI, Math.PI, true);
    }

    /// <inheritdoc />
    public bool Equals(Interval other) =>
        IsEmpty == other.IsEmpty && (IsEmpty || (Lo.Equals(other.Lo) && Hi.Equals(other.Hi) && MaybeUndefined == other.MaybeUndefined));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Interval i && Equals(i);

    /// <inheritdoc />
    public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(Lo, Hi, MaybeUndefined);

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? "[empty]" : $"[{Lo}, {Hi}]{(MaybeUndefined ? "?" : "")}";
}

/// <summary>Three-valued truth for interval comparisons.</summary>
public enum Truth
{
    /// <summary>False on the whole box.</summary>
    False,

    /// <summary>True on the whole box.</summary>
    True,

    /// <summary>Unknown (true somewhere, false somewhere, or not provable).</summary>
    Maybe,
}
