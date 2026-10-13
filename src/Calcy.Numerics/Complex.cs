using System.Globalization;

namespace Calcy.Numerics;

/// <summary>
/// A double-precision complex number. Elementary functions use the principal branch with the
/// conventions of C99 Annex G (Kahan): branch cuts lie on the real or imaginary axis and the sign
/// of a zero imaginary part selects the side of the cut (a real argument with Im = +0 takes the
/// limit from the upper half plane).
/// </summary>
public readonly struct Complex : IEquatable<Complex>, IFormattable
{
    private const double HalfPi = Math.PI / 2;
    private const double Ln10 = 2.302585092994045684017991454684364208;

    /// <summary>Real part.</summary>
    public double Re { get; }

    /// <summary>Imaginary part.</summary>
    public double Im { get; }

    /// <summary>Creates the complex number <paramref name="re"/> + <paramref name="im"/> i.</summary>
    public Complex(double re, double im)
    {
        Re = re;
        Im = im;
    }

    /// <summary>The number 0.</summary>
    public static Complex Zero => new(0, 0);

    /// <summary>The number 1.</summary>
    public static Complex One => new(1, 0);

    /// <summary>The imaginary unit i.</summary>
    public static Complex I => new(0, 1);

    /// <summary>Not a number.</summary>
    public static Complex NaN => new(double.NaN, double.NaN);

    /// <summary>The complex point at infinity, represented as +Infinity + 0i.</summary>
    public static Complex Infinity => new(double.PositiveInfinity, 0);

    /// <summary>Magnitude |z| (same as <see cref="Abs"/>).</summary>
    public double Magnitude => Abs(this);

    /// <summary>Argument in (-pi, pi] (same as <see cref="Arg"/>).</summary>
    public double Phase => Math.Atan2(Im, Re);

    /// <summary>True if either part is NaN.</summary>
    public bool IsNaN => double.IsNaN(Re) || double.IsNaN(Im);

    /// <summary>True if either part is infinite (and none is NaN).</summary>
    public bool IsInfinity => !IsNaN && (double.IsInfinity(Re) || double.IsInfinity(Im));

    /// <summary>True if both parts are finite.</summary>
    public bool IsFinite => double.IsFinite(Re) && double.IsFinite(Im);

    /// <summary>True if both parts are zero.</summary>
    public bool IsZero => Re == 0 && Im == 0;

    /// <summary>
    /// True if the imaginary part is negligible: |Im| &lt;= tolerance * max(1, |Re|).
    /// With the default tolerance of 0 the imaginary part must be exactly zero.
    /// </summary>
    public bool IsReal(double tolerance = 0) => !IsNaN && Math.Abs(Im) <= tolerance * Math.Max(1.0, Math.Abs(Re));

    /// <summary>Converts a real number to a complex number with zero imaginary part.</summary>
    public static implicit operator Complex(double value) => new(value, 0);

    /// <summary>Creates a complex number from polar coordinates r e^(i theta).</summary>
    public static Complex FromPolar(double magnitude, double phase)
    {
        var (s, c) = Math.SinCos(phase);
        return new Complex(magnitude * c, magnitude * s);
    }

    // ---------------------------------------------------------------- arithmetic

    /// <summary>Unary plus.</summary>
    public static Complex operator +(Complex a) => a;

    /// <summary>Negation.</summary>
    public static Complex operator -(Complex a) => new(-a.Re, -a.Im);

    /// <summary>Addition.</summary>
    public static Complex operator +(Complex a, Complex b) => new(a.Re + b.Re, a.Im + b.Im);

    /// <summary>Addition of a real number.</summary>
    public static Complex operator +(Complex a, double b) => new(a.Re + b, a.Im);

    /// <summary>Addition of a real number.</summary>
    public static Complex operator +(double a, Complex b) => new(a + b.Re, b.Im);

    /// <summary>Subtraction.</summary>
    public static Complex operator -(Complex a, Complex b) => new(a.Re - b.Re, a.Im - b.Im);

    /// <summary>Subtraction of a real number.</summary>
    public static Complex operator -(Complex a, double b) => new(a.Re - b, a.Im);

    /// <summary>Subtraction from a real number.</summary>
    public static Complex operator -(double a, Complex b) => new(a - b.Re, -b.Im);

    /// <summary>Multiplication.</summary>
    public static Complex operator *(Complex a, Complex b) =>
        new(a.Re * b.Re - a.Im * b.Im, a.Re * b.Im + a.Im * b.Re);

    /// <summary>Multiplication by a real number.</summary>
    public static Complex operator *(Complex a, double b) => new(a.Re * b, a.Im * b);

    /// <summary>Multiplication by a real number.</summary>
    public static Complex operator *(double a, Complex b) => new(a * b.Re, a * b.Im);

    /// <summary>Division by a real number.</summary>
    public static Complex operator /(Complex a, double b) => new(a.Re / b, a.Im / b);

    /// <summary>Division of a real number.</summary>
    public static Complex operator /(double a, Complex b) => new Complex(a, 0) / b;

    /// <summary>
    /// Division using Smith's algorithm with Baudin's refinement (no intermediate overflow for
    /// representable results). A nonzero number divided by zero gives <see cref="Infinity"/>;
    /// 0/0 gives NaN.
    /// </summary>
    public static Complex operator /(Complex a, Complex b)
    {
        double c = b.Re, d = b.Im;
        if (c == 0 && d == 0)
        {
            if (a.IsNaN || a.IsZero) return NaN;
            return Infinity;
        }

        if (Math.Abs(d) <= Math.Abs(c))
        {
            double r = d / c;
            double den = c + d * r;
            if (r != 0)
                return new Complex((a.Re + a.Im * r) / den, (a.Im - a.Re * r) / den);
            return new Complex((a.Re + d * (a.Im / c)) / den, (a.Im - d * (a.Re / c)) / den);
        }
        else
        {
            double r = c / d;
            double den = c * r + d;
            if (r != 0)
                return new Complex((a.Re * r + a.Im) / den, (a.Im * r - a.Re) / den);
            return new Complex((c * (a.Re / d) + a.Im) / den, (c * (a.Im / d) - a.Re) / den);
        }
    }

    /// <summary>Exact component-wise equality.</summary>
    public static bool operator ==(Complex a, Complex b) => a.Re == b.Re && a.Im == b.Im;

    /// <summary>Exact component-wise inequality.</summary>
    public static bool operator !=(Complex a, Complex b) => !(a == b);

    /// <inheritdoc/>
    public bool Equals(Complex other) => Re.Equals(other.Re) && Im.Equals(other.Im);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Complex c && Equals(c);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Re, Im);

    // ---------------------------------------------------------------- basic functions

    /// <summary>Magnitude |z| computed without intermediate overflow or underflow.</summary>
    public static double Abs(Complex z) => Hypot(z.Re, z.Im);

    /// <summary>Argument (phase) in (-pi, pi].</summary>
    public static double Arg(Complex z) => Math.Atan2(z.Im, z.Re);

    /// <summary>Complex conjugate.</summary>
    public static Complex Conjugate(Complex z) => new(z.Re, -z.Im);

    /// <summary>Reciprocal 1/z.</summary>
    public static Complex Reciprocal(Complex z) => One / z;

    /// <summary>Overflow-safe sqrt(x^2 + y^2).</summary>
    public static double Hypot(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        if (double.IsInfinity(x) || double.IsInfinity(y)) return double.PositiveInfinity;
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (x < y) (x, y) = (y, x);
        if (x == 0) return 0;
        double r = y / x;
        return x * Math.Sqrt(1 + r * r);
    }

    /// <summary>Accurate log(1 + x) for small x.</summary>
    internal static double Log1p(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (double.IsPositiveInfinity(x)) return x;
        double u = 1.0 + x;
        if (u == 1.0) return x;
        if (u == 0) return double.NegativeInfinity;
        return Math.Log(u) * x / (u - 1.0);
    }

    /// <summary>log|z| computed accurately near the unit circle and without overflow.</summary>
    private static double LogAbs(double x, double y)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y);
        if (double.IsInfinity(ax) || double.IsInfinity(ay)) return double.PositiveInfinity;
        if (double.IsNaN(ax) || double.IsNaN(ay)) return double.NaN;
        if (ax < ay) (ax, ay) = (ay, ax);
        if (ax == 0) return double.NegativeInfinity;
        if (ay == 0) return Math.Log(ax);
        if (ax > 0.5 && ax < 2.0)
            return 0.5 * Log1p((ax - 1) * (ax + 1) + ay * ay);
        double r = ay / ax;
        return Math.Log(ax) + 0.5 * Log1p(r * r);
    }

    /// <summary>Principal square root with Re(result) &gt;= 0, numerically stable.</summary>
    public static Complex Sqrt(Complex z)
    {
        double x = z.Re, y = z.Im;
        if (double.IsInfinity(y)) return new Complex(double.PositiveInfinity, y);
        if (double.IsNaN(x) || double.IsNaN(y)) return NaN;
        if (y == 0)
        {
            if (x >= 0) return new Complex(Math.Sqrt(x), y);
            return new Complex(0, Math.CopySign(Math.Sqrt(-x), y));
        }
        if (double.IsInfinity(x))
            return x > 0 ? new Complex(x, Math.CopySign(0, y)) : new Complex(0, Math.CopySign(double.PositiveInfinity, y));

        double scale = 1;
        double ax = Math.Abs(x), ay = Math.Abs(y);
        if (ax > 1e300 || ay > 1e300)
        {
            x *= 0.25; y *= 0.25; scale = 2;
        }
        else if (ax < 1e-300 && ay < 1e-300)
        {
            const double up = 8.112963841460668e31; // 2^106
            x *= up; y *= up; scale = 1.1102230246251565e-16; // 2^-53
        }

        double t = Math.Sqrt((Math.Abs(x) + Hypot(x, y)) * 0.5);
        if (x >= 0)
            return new Complex(t * scale, y / (2 * t) * scale);
        return new Complex(Math.Abs(y) / (2 * t) * scale, Math.CopySign(t, y) * scale);
    }

    /// <summary>Complex exponential.</summary>
    public static Complex Exp(Complex z)
    {
        if (z.Im == 0) return new Complex(Math.Exp(z.Re), z.Im);
        var (s, c) = Math.SinCos(z.Im);
        double e = Math.Exp(z.Re);
        if (double.IsInfinity(e) && double.IsFinite(z.Re))
        {
            double h = Math.Exp(z.Re / 2);
            return new Complex(h * c * h, h * s * h);
        }
        return new Complex(MulZ(e, c), MulZ(e, s));
    }

    /// <summary>Principal natural logarithm: Re = ln|z|, Im = Arg z in (-pi, pi].</summary>
    public static Complex Log(Complex z) => new(LogAbs(z.Re, z.Im), Math.Atan2(z.Im, z.Re));

    /// <summary>Principal base-10 logarithm.</summary>
    public static Complex Log10(Complex z)
    {
        double re = z.Im == 0 && z.Re > 0 ? Math.Log10(z.Re) : LogAbs(z.Re, z.Im) / Ln10;
        return new Complex(re, Math.Atan2(z.Im, z.Re) / Ln10);
    }

    /// <summary>Principal logarithm in an arbitrary base.</summary>
    public static Complex Log(Complex z, Complex baseValue) => Log(z) / Log(baseValue);

    /// <summary>Integer power by repeated squaring (exact for small Gaussian integers).</summary>
    public static Complex Pow(Complex z, int n)
    {
        if (n == 0) return One;
        if (z.Im == 0) return new Complex(Math.Pow(z.Re, n), 0);
        long m = Math.Abs((long)n);
        Complex result = One, b = z;
        while (m > 0)
        {
            if ((m & 1) != 0) result *= b;
            m >>= 1;
            if (m > 0) b *= b;
        }
        if (n > 0) return result;
        if (result.IsInfinity || result.IsZero)
        {
            // Overflow or underflow of z^|n|: compute (1/z)^|n| instead.
            return Pow(One / z, -(long)n);
        }
        return One / result;
    }

    private static Complex Pow(Complex z, long n)
    {
        Complex result = One, b = z;
        while (n > 0)
        {
            if ((n & 1) != 0) result *= b;
            n >>= 1;
            if (n > 0) b *= b;
        }
        return result;
    }

    /// <summary>Principal value of z^p for a real exponent.</summary>
    public static Complex Pow(Complex z, double p)
    {
        if (p == 0) return One;
        if (double.IsNaN(p) || z.IsNaN) return NaN;
        if (z.Im == 0 && z.Re >= 0) return new Complex(Math.Pow(z.Re, p), 0);
        if (p == Math.Floor(p) && Math.Abs(p) <= int.MaxValue) return Pow(z, (int)p);
        if (z.IsZero) return p > 0 ? Zero : Infinity;
        double logr = LogAbs(z.Re, z.Im);
        double r = Math.Exp(p * logr);
        double theta = p * Math.Atan2(z.Im, z.Re);
        return FromPolar(r, theta);
    }

    /// <summary>Principal value of z^w = exp(w log z). 0^w is 0 when Re w &gt; 0.</summary>
    public static Complex Pow(Complex z, Complex w)
    {
        if (w.Im == 0) return Pow(z, w.Re);
        if (z.IsZero) return w.Re > 0 ? Zero : NaN;
        return Exp(w * Log(z));
    }

    // ---------------------------------------------------------------- trigonometric

    /// <summary>Complex sine.</summary>
    public static Complex Sin(Complex z)
    {
        var (s, c) = Math.SinCos(z.Re);
        return new Complex(MulZ(s, Math.Cosh(z.Im)), MulZ(c, Math.Sinh(z.Im)));
    }

    /// <summary>Complex cosine.</summary>
    public static Complex Cos(Complex z)
    {
        var (s, c) = Math.SinCos(z.Re);
        return new Complex(MulZ(c, Math.Cosh(z.Im)), -MulZ(s, Math.Sinh(z.Im)));
    }

    /// <summary>Complex tangent, computed as -i tanh(iz) (stable for large |Im z|).</summary>
    public static Complex Tan(Complex z)
    {
        Complex w = Tanh(new Complex(-z.Im, z.Re));
        return new Complex(w.Im, -w.Re);
    }

    /// <summary>Complex hyperbolic sine.</summary>
    public static Complex Sinh(Complex z)
    {
        var (s, c) = Math.SinCos(z.Im);
        return new Complex(MulZ(Math.Sinh(z.Re), c), MulZ(Math.Cosh(z.Re), s));
    }

    /// <summary>Complex hyperbolic cosine.</summary>
    public static Complex Cosh(Complex z)
    {
        var (s, c) = Math.SinCos(z.Im);
        return new Complex(MulZ(Math.Cosh(z.Re), c), MulZ(Math.Sinh(z.Re), s));
    }

    /// <summary>Complex hyperbolic tangent (Kahan's formulation, no cancellation).</summary>
    public static Complex Tanh(Complex z)
    {
        double x = z.Re, y = z.Im;
        if (double.IsNaN(x) || double.IsNaN(y)) return NaN;
        if (Math.Abs(x) > 22)
        {
            double e = Math.Exp(-2 * Math.Abs(x));
            double im = double.IsFinite(y) ? 2 * Math.Sin(2 * y) * e : double.NaN;
            if (double.IsInfinity(x) && double.IsFinite(y)) im = Math.CopySign(0, Math.Sin(2 * y));
            return new Complex(Math.CopySign(1, x), im);
        }
        double t = Math.Tan(y);
        double beta = 1 + t * t;
        double s = Math.Sinh(x);
        double rho = Math.Sqrt(1 + s * s);
        double den = 1 + beta * s * s;
        return new Complex(beta * rho * s / den, t / den);
    }

    /// <summary>Principal arcsine. Branch cuts: real axis outside [-1, 1].</summary>
    public static Complex Asin(Complex z)
    {
        Complex s1 = Sqrt(new Complex(1 - z.Re, -z.Im));
        Complex s2 = Sqrt(new Complex(1 + z.Re, z.Im));
        double re = Math.Atan2(z.Re, s1.Re * s2.Re - s1.Im * s2.Im);
        double im = Math.Asinh(s1.Re * s2.Im - s1.Im * s2.Re);
        return new Complex(re, im);
    }

    /// <summary>Principal arccosine (real part in [0, pi]). Branch cuts: real axis outside [-1, 1].</summary>
    public static Complex Acos(Complex z)
    {
        Complex s1 = Sqrt(new Complex(1 - z.Re, -z.Im));
        Complex s2 = Sqrt(new Complex(1 + z.Re, z.Im));
        double re = 2 * Math.Atan2(s1.Re, s2.Re);
        double im = Math.Asinh(s2.Re * s1.Im - s2.Im * s1.Re);
        return new Complex(re, im);
    }

    /// <summary>Principal arctangent. Branch cuts: imaginary axis outside [-i, i].</summary>
    public static Complex Atan(Complex z)
    {
        Complex w = Atanh(new Complex(-z.Im, z.Re));
        return new Complex(w.Im, -w.Re);
    }

    /// <summary>Principal inverse hyperbolic sine. Branch cuts: imaginary axis outside [-i, i].</summary>
    public static Complex Asinh(Complex z)
    {
        Complex w = Asin(new Complex(-z.Im, z.Re));
        return new Complex(w.Im, -w.Re);
    }

    /// <summary>Principal inverse hyperbolic cosine (real part &gt;= 0). Branch cut: real axis below 1.</summary>
    public static Complex Acosh(Complex z)
    {
        Complex s1 = Sqrt(new Complex(z.Re - 1, z.Im));
        Complex s2 = Sqrt(new Complex(z.Re + 1, z.Im));
        double re = Math.Asinh(s1.Re * s2.Re + s1.Im * s2.Im);
        double im = 2 * Math.Atan2(s1.Im, s2.Re);
        return new Complex(re, im);
    }

    /// <summary>Principal inverse hyperbolic tangent. Branch cuts: real axis outside [-1, 1].</summary>
    public static Complex Atanh(Complex z)
    {
        double x = z.Re, y = z.Im;
        if (double.IsNaN(x) || double.IsNaN(y)) return NaN;
        if (y == 0 && Math.Abs(x) < 1) return new Complex(Math.Atanh(x), y);
        double ax = Math.Abs(x), ay = Math.Abs(y);
        if (ax > 1e150 || ay > 1e150)
        {
            Complex inv = One / z;
            return new Complex(inv.Re, Math.CopySign(HalfPi, y));
        }
        double oneMinus = 1 - ax;
        double den = oneMinus * oneMinus + ay * ay;
        double re = den == 0 ? double.PositiveInfinity : 0.25 * Log1p(4 * ax / den);
        double im = 0.5 * Math.Atan2(2 * y, (1 - x) * (1 + x) - y * y);
        return new Complex(Math.CopySign(re, x), im);
    }

    private static double MulZ(double a, double b) => a == 0 ? (double.IsNaN(b) ? double.NaN : a) : a * b;

    // ---------------------------------------------------------------- formatting

    /// <summary>Formats with 15 significant digits, for example "3+4i", "-2i" or "1.5".</summary>
    public override string ToString() => ToString(15);

    /// <summary>
    /// Formats with the given number of significant digits per part, for example "3+4i",
    /// "-2i", "1.5", "3-i". Uses the invariant culture.
    /// </summary>
    public string ToString(int significantDigits)
    {
        if (significantDigits < 1 || significantDigits > 17)
            throw new ArgumentOutOfRangeException(nameof(significantDigits), "Significant digits must be between 1 and 17.");
        string format = "G" + significantDigits.ToString(CultureInfo.InvariantCulture);
        return Format(v => v.ToString(format, CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        formatProvider ??= CultureInfo.InvariantCulture;
        return Format(v => v.ToString(format, formatProvider));
    }

    private string Format(Func<double, string> formatPart)
    {
        if (IsNaN) return "NaN";
        double re = Re == 0 ? 0 : Re;
        double im = Im == 0 ? 0 : Im;
        if (im == 0) return formatPart(re);
        string imagText = formatPart(Math.Abs(im));
        if (imagText == "1") imagText = "";
        string sign = im < 0 ? "-" : "+";
        if (re == 0) return (im < 0 ? "-" : "") + imagText + "i";
        return formatPart(re) + sign + imagText + "i";
    }
}
