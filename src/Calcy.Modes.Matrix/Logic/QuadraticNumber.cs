using System.Numerics;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>
/// An exact number a + b·√d with rational a and b and a squarefree integer d (d &lt; 0 gives complex numbers,
/// so √-1 is i). Rational numbers have b = 0 and d = 0. Used for exact eigenvalues and eigenvectors.
/// </summary>
public readonly struct QuadraticNumber : IEquatable<QuadraticNumber>
{
    /// <summary>Rational part.</summary>
    public BigRational A { get; }

    /// <summary>Coefficient of √d.</summary>
    public BigRational B { get; }

    /// <summary>The squarefree radicand (0 for a rational number).</summary>
    public BigInteger D { get; }

    /// <summary>Creates a + b·√d. The radicand must already be squarefree.</summary>
    public QuadraticNumber(BigRational a, BigRational b, BigInteger d)
    {
        if (b.IsZero || d.IsZero) { b = BigRational.Zero; d = BigInteger.Zero; }
        else if (d.IsOne) { a += b; b = BigRational.Zero; d = BigInteger.Zero; }
        A = a;
        B = b;
        D = d;
    }

    /// <summary>A rational number.</summary>
    public static QuadraticNumber Rational(BigRational a) => new(a, BigRational.Zero, BigInteger.Zero);

    /// <summary>Zero.</summary>
    public static QuadraticNumber Zero => Rational(BigRational.Zero);

    /// <summary>One.</summary>
    public static QuadraticNumber One => Rational(BigRational.One);

    /// <summary>True when the value is rational.</summary>
    public bool IsRational => B.IsZero;

    /// <summary>True when the value is exactly zero.</summary>
    public bool IsZero => A.IsZero && B.IsZero;

    /// <summary>True when the value is real (rational, or a real square root).</summary>
    public bool IsReal => B.IsZero || D.Sign > 0;

    /// <summary>The conjugate a - b·√d (the complex conjugate when d &lt; 0).</summary>
    public QuadraticNumber Conjugate() => new(A, -B, D);

    private static BigInteger CommonRadicand(QuadraticNumber x, QuadraticNumber y)
    {
        if (x.B.IsZero) return y.D;
        if (y.B.IsZero || x.D == y.D) return x.D;
        throw new InvalidOperationException("Cannot combine square roots of different numbers.");
    }

    public static QuadraticNumber operator +(QuadraticNumber x, QuadraticNumber y) => new(x.A + y.A, x.B + y.B, CommonRadicand(x, y));

    public static QuadraticNumber operator -(QuadraticNumber x, QuadraticNumber y) => new(x.A - y.A, x.B - y.B, CommonRadicand(x, y));

    public static QuadraticNumber operator -(QuadraticNumber x) => new(-x.A, -x.B, x.D);

    public static QuadraticNumber operator *(QuadraticNumber x, QuadraticNumber y)
    {
        var d = CommonRadicand(x, y);
        return new QuadraticNumber(x.A * y.A + x.B * y.B * new BigRational(d), x.A * y.B + x.B * y.A, d);
    }

    public static QuadraticNumber operator /(QuadraticNumber x, QuadraticNumber y)
    {
        if (y.IsZero) throw new DivideByZeroException("Division by zero");
        if (y.B.IsZero) return new QuadraticNumber(x.A / y.A, x.B / y.A, x.D);
        // Multiply by the conjugate: (a + b√d)(a - b√d) = a² - b²d is rational and non-zero (d is not a square).
        var norm = y.A * y.A - y.B * y.B * new BigRational(y.D);
        var num = x * y.Conjugate();
        return new QuadraticNumber(num.A / norm, num.B / norm, num.D);
    }

    public static implicit operator QuadraticNumber(BigRational q) => Rational(q);

    public static implicit operator QuadraticNumber(int v) => Rational(v);

    /// <summary>An approximate complex value (real and imaginary parts).</summary>
    public (double Re, double Im) ToComplex()
    {
        double a = A.ToDouble(), b = B.ToDouble();
        if (B.IsZero) return (a, 0);
        double r = Math.Sqrt(Math.Abs((double)D));
        return D.Sign > 0 ? (a + b * r, 0) : (a, b * r);
    }

    /// <summary>The value as a simplified Calcy expression (√ for real roots, i for complex ones).</summary>
    public Expr ToExpr()
    {
        var a = Algebra.Q(A);
        if (B.IsZero) return a;
        var root = Algebra.Sqrt(new NumberExpr(Number.Exact(new BigRational(D))));
        return Algebra.Add(a, Algebra.Mul(Algebra.Q(B), root));
    }

    /// <summary>
    /// Textbook display text with the rational part first: 3/4, 2 + √2, (1 + √5)/2, √2/2, i, -i or (1 - i√3)/2.
    /// </summary>
    public string ToDisplay()
    {
        if (B.IsZero) return A.ToString();
        var q = A.Den / BigInteger.GreatestCommonDivisor(A.Den, B.Den) * B.Den;
        var a = A.Num * (q / A.Den);
        var b = B.Num * (q / B.Den);
        string root = D == BigInteger.MinusOne ? "i" : D.Sign < 0 ? "i√" + (-D).ToString(System.Globalization.CultureInfo.InvariantCulture) : "√" + D.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var magnitude = BigInteger.Abs(b);
        string radical = (magnitude.IsOne ? "" : magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture)) + root;
        if (a.IsZero)
        {
            string signed = (b.Sign < 0 ? "-" : "") + radical;
            return q.IsOne ? signed : signed + "/" + q.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        string body = a.ToString(System.Globalization.CultureInfo.InvariantCulture) + (b.Sign < 0 ? " - " : " + ") + radical;
        return q.IsOne ? body : "(" + body + ")/" + q.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool Equals(QuadraticNumber other) => A == other.A && B == other.B && (B.IsZero || D == other.D);

    public override bool Equals(object? obj) => obj is QuadraticNumber q && Equals(q);

    public override int GetHashCode() => B.IsZero ? A.GetHashCode() : HashCode.Combine(A, B, D);

    public static bool operator ==(QuadraticNumber x, QuadraticNumber y) => x.Equals(y);

    public static bool operator !=(QuadraticNumber x, QuadraticNumber y) => !x.Equals(y);

    public override string ToString() => B.IsZero ? A.ToString() : $"{A} + {B}*sqrt({D})";
}
