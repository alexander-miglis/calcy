using System.Numerics;

namespace Calcy.Core.Numbers;

/// <summary>
/// A complex scalar that is either exact (rational real and imaginary parts) or approximate (double parts).
/// Real numbers are the common case (imaginary part zero).
/// </summary>
public readonly struct Number : IEquatable<Number>
{
    private readonly BigRational _reQ, _imQ;
    private readonly double _re, _im;

    public bool IsExact { get; }

    private Number(BigRational re, BigRational im)
    {
        IsExact = true;
        _reQ = re; _imQ = im;
        _re = 0; _im = 0;
    }

    private Number(double re, double im)
    {
        IsExact = false;
        _re = re; _im = im;
        _reQ = default; _imQ = default;
    }

    public static readonly Number Zero = Exact(BigRational.Zero);
    public static readonly Number One = Exact(BigRational.One);
    public static readonly Number MinusOne = Exact(BigRational.MinusOne);
    public static readonly Number I = Exact(BigRational.Zero, BigRational.One);
    public static readonly Number NaN = Approx(double.NaN);

    public static Number Exact(BigRational re) => new(re, BigRational.Zero);
    public static Number Exact(BigRational re, BigRational im) => new(re, im);
    public static Number Approx(double re, double im = 0) => new(re == 0 ? 0 : re, im == 0 ? 0 : im);
    public static Number FromInt(long v) => Exact(new BigRational(v));

    public static implicit operator Number(int v) => Exact(v);
    public static implicit operator Number(long v) => Exact(v);
    public static implicit operator Number(BigInteger v) => Exact(v);
    public static implicit operator Number(BigRational v) => Exact(v);
    public static implicit operator Number(double v) => Approx(v);

    public BigRational ReQ => IsExact ? _reQ : throw new InvalidOperationException("not exact");
    public BigRational ImQ => IsExact ? _imQ : throw new InvalidOperationException("not exact");
    public double Re => IsExact ? _reQ.ToDouble() : _re;
    public double Im => IsExact ? _imQ.ToDouble() : _im;

    public bool IsReal => IsExact ? _imQ.IsZero : _im == 0;
    public bool IsZero => IsExact ? _reQ.IsZero && _imQ.IsZero : _re == 0 && _im == 0;
    public bool IsOne => IsExact ? _reQ.IsOne && _imQ.IsZero : _re == 1 && _im == 0;
    public bool IsMinusOne => IsExact ? _reQ == BigRational.MinusOne && _imQ.IsZero : _re == -1 && _im == 0;
    public bool IsInteger => IsExact ? _imQ.IsZero && _reQ.IsInteger : _im == 0 && Math.Floor(_re) == _re && !double.IsInfinity(_re);
    public bool IsExactInteger => IsExact && _imQ.IsZero && _reQ.IsInteger;
    public bool IsExactRational => IsExact && _imQ.IsZero;
    public bool IsNaN => !IsExact && (double.IsNaN(_re) || double.IsNaN(_im));
    public bool IsFinite => IsExact || (double.IsFinite(_re) && double.IsFinite(_im));
    public bool IsPureImaginary => IsExact ? _reQ.IsZero && !_imQ.IsZero : _re == 0 && _im != 0;

    /// <summary>Sign of a real number (-1, 0, 1). Throws for non-real numbers.</summary>
    public int RealSign
    {
        get
        {
            if (!IsReal) throw new InvalidOperationException("sign of a complex number");
            return IsExact ? _reQ.Sign : Math.Sign(_re);
        }
    }

    public bool IsNegativeReal => IsReal && (IsExact ? _reQ.Sign < 0 : _re < 0);
    public bool IsPositiveReal => IsReal && (IsExact ? _reQ.Sign > 0 : _re > 0);

    public Number ToApprox() => IsExact ? Approx(_reQ.ToDouble(), _imQ.ToDouble()) : this;

    public BigInteger ToBigInteger()
    {
        if (IsExact && _imQ.IsZero && _reQ.IsInteger) return _reQ.Num;
        if (!IsExact && _im == 0 && Math.Floor(_re) == _re && double.IsFinite(_re)) return new BigInteger(_re);
        throw new InvalidOperationException("Expected an integer");
    }

    public bool TryGetInt(out int value)
    {
        value = 0;
        if (!IsInteger) return false;
        var b = ToBigInteger();
        if (b < int.MinValue || b > int.MaxValue) return false;
        value = (int)b;
        return true;
    }

    public static Number operator +(Number a, Number b)
    {
        if (a.IsExact && b.IsExact) return Exact(a._reQ + b._reQ, a._imQ + b._imQ);
        return Approx(a.Re + b.Re, a.Im + b.Im);
    }

    public static Number operator -(Number a, Number b)
    {
        if (a.IsExact && b.IsExact) return Exact(a._reQ - b._reQ, a._imQ - b._imQ);
        return Approx(a.Re - b.Re, a.Im - b.Im);
    }

    public static Number operator -(Number a) => a.IsExact ? Exact(-a._reQ, -a._imQ) : Approx(-a._re, -a._im);

    public static Number operator *(Number a, Number b)
    {
        if (a.IsExact && b.IsExact)
        {
            if (a._imQ.IsZero && b._imQ.IsZero) return Exact(a._reQ * b._reQ);
            return Exact(a._reQ * b._reQ - a._imQ * b._imQ, a._reQ * b._imQ + a._imQ * b._reQ);
        }
        double ar = a.Re, ai = a.Im, br = b.Re, bi = b.Im;
        if (ai == 0 && bi == 0) return Approx(ar * br);
        return Approx(ar * br - ai * bi, ar * bi + ai * br);
    }

    public static Number operator /(Number a, Number b)
    {
        if (b.IsZero) throw new DivideByZeroException("Division by zero");
        if (a.IsExact && b.IsExact)
        {
            if (b._imQ.IsZero) return Exact(a._reQ / b._reQ, a._imQ / b._reQ);
            var den = b._reQ * b._reQ + b._imQ * b._imQ;
            return Exact((a._reQ * b._reQ + a._imQ * b._imQ) / den, (a._imQ * b._reQ - a._reQ * b._imQ) / den);
        }
        double ar = a.Re, ai = a.Im, br = b.Re, bi = b.Im;
        if (bi == 0) return Approx(ar / br, ai / br);
        // Smith's algorithm for numerical robustness.
        if (Math.Abs(br) >= Math.Abs(bi))
        {
            double r = bi / br, d = br + bi * r;
            return Approx((ar + ai * r) / d, (ai - ar * r) / d);
        }
        else
        {
            double r = br / bi, d = bi + br * r;
            return Approx((ar * r + ai) / d, (ai * r - ar) / d);
        }
    }

    public Number Conjugate() => IsExact ? Exact(_reQ, -_imQ) : Approx(_re, -_im);

    /// <summary>Exact |z|^2 for exact numbers.</summary>
    public Number AbsSquared() => IsExact ? Exact(_reQ * _reQ + _imQ * _imQ) : Approx(_re * _re + _im * _im);

    public double AbsDouble() => IsReal ? Math.Abs(Re) : Hypot(Re, Im);

    public double ArgDouble() => Math.Atan2(Im, Re);

    public static double Hypot(double a, double b)
    {
        a = Math.Abs(a); b = Math.Abs(b);
        if (a < b) (a, b) = (b, a);
        if (a == 0) return 0;
        if (double.IsInfinity(a)) return double.PositiveInfinity;
        double r = b / a;
        return a * Math.Sqrt(1 + r * r);
    }

    /// <summary>Exact integer power (works for exact and approximate numbers). Returns null when the result would be absurdly large.</summary>
    public Number? PowInteger(BigInteger n)
    {
        if (n.IsZero) return One;
        if (IsExact)
        {
            if (IsZero)
            {
                if (n.Sign < 0) throw new DivideByZeroException("Division by zero");
                return Zero;
            }
            if (IsOne) return One;
            var bits = Math.Max(_reQ.BitSize, _imQ.BitSize);
            if (BigInteger.Abs(n) * Math.Max(bits, 1) > 4_000_000) return null;
            if (_imQ.IsZero && _reQ == BigRational.MinusOne) return n.IsEven ? One : MinusOne;
            int e = (int)BigInteger.Abs(n);
            Number result;
            if (_imQ.IsZero) result = Exact(_reQ.Pow(e));
            else
            {
                result = One;
                Number b = this;
                int k = e;
                while (k > 0)
                {
                    if ((k & 1) != 0) result *= b;
                    b *= b;
                    k >>= 1;
                }
            }
            return n.Sign < 0 ? One / result : result;
        }
        if (IsReal) return Approx(Math.Pow(_re, (double)n));
        if (BigInteger.Abs(n) < 64)
        {
            Number result = One.ToApprox(), b = this;
            int k = (int)BigInteger.Abs(n);
            while (k > 0)
            {
                if ((k & 1) != 0) result *= b;
                b *= b;
                k >>= 1;
            }
            return n.Sign < 0 ? Approx(1) / result : result;
        }
        return ComplexMath.Pow(this, Approx((double)n));
    }

    public bool Equals(Number other)
    {
        if (IsExact != other.IsExact) return false;
        return IsExact ? _reQ == other._reQ && _imQ == other._imQ : _re.Equals(other._re) && _im.Equals(other._im);
    }

    public override bool Equals(object? obj) => obj is Number n && Equals(n);
    public override int GetHashCode() => IsExact ? HashCode.Combine(_reQ, _imQ, 1) : HashCode.Combine(_re, _im, 2);

    /// <summary>Compares two real numbers by value (exact and approximate mixed). Throws for complex.</summary>
    public static int CompareReal(Number a, Number b)
    {
        if (!a.IsReal || !b.IsReal) throw new InvalidOperationException("Cannot order complex numbers");
        if (a.IsExact && b.IsExact) return a._reQ.CompareTo(b._reQ);
        return a.Re.CompareTo(b.Re);
    }

    public override string ToString()
    {
        if (IsExact)
        {
            if (_imQ.IsZero) return _reQ.ToString();
            return $"{_reQ}+{_imQ}i";
        }
        return _im == 0 ? _re.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : $"{_re}+{_im}i";
    }
}

/// <summary>Approximate complex elementary functions on <see cref="Number"/> (always returns approximate numbers).</summary>
public static class ComplexMath
{
    private static Number A(double re, double im = 0) => Number.Approx(re, im);

    public static Number Exp(Number z)
    {
        double re = z.Re, im = z.Im;
        if (im == 0) return A(Math.Exp(re));
        double m = Math.Exp(re);
        return A(m * Math.Cos(im), m * Math.Sin(im));
    }

    public static Number Log(Number z)
    {
        double re = z.Re, im = z.Im;
        if (im == 0 && re >= 0) return A(Math.Log(re));
        return A(Math.Log(Number.Hypot(re, im)), Math.Atan2(im, re));
    }

    public static Number Sqrt(Number z)
    {
        double re = z.Re, im = z.Im;
        if (im == 0) return re >= 0 ? A(Math.Sqrt(re)) : A(0, Math.Sqrt(-re));
        double m = Number.Hypot(re, im);
        double t = Math.Sqrt((m + Math.Abs(re)) / 2);
        if (re >= 0) return A(t, im / (2 * t));
        return A(Math.Abs(im) / (2 * t), im >= 0 ? t : -t);
    }

    public static Number Pow(Number b, Number e)
    {
        if (e.IsZero) return A(1);
        if (b.IsZero)
        {
            if (e.Re > 0) return A(0);
            throw new DivideByZeroException("Division by zero");
        }
        if (b.IsReal && e.IsReal)
        {
            double br = b.Re, er = e.Re;
            if (br > 0 || Math.Floor(er) == er) return A(Math.Pow(br, er));
        }
        return Exp(Log(b) * e.ToApprox());
    }

    public static Number Sin(Number z)
    {
        double x = z.Re, y = z.Im;
        if (y == 0) return A(Math.Sin(x));
        return A(Math.Sin(x) * Math.Cosh(y), Math.Cos(x) * Math.Sinh(y));
    }

    public static Number Cos(Number z)
    {
        double x = z.Re, y = z.Im;
        if (y == 0) return A(Math.Cos(x));
        return A(Math.Cos(x) * Math.Cosh(y), -Math.Sin(x) * Math.Sinh(y));
    }

    public static Number Tan(Number z)
    {
        if (z.IsReal) return A(Math.Tan(z.Re));
        return Sin(z) / Cos(z);
    }

    public static Number Sinh(Number z)
    {
        double x = z.Re, y = z.Im;
        if (y == 0) return A(Math.Sinh(x));
        return A(Math.Sinh(x) * Math.Cos(y), Math.Cosh(x) * Math.Sin(y));
    }

    public static Number Cosh(Number z)
    {
        double x = z.Re, y = z.Im;
        if (y == 0) return A(Math.Cosh(x));
        return A(Math.Cosh(x) * Math.Cos(y), Math.Sinh(x) * Math.Sin(y));
    }

    public static Number Tanh(Number z)
    {
        if (z.IsReal) return A(Math.Tanh(z.Re));
        return Sinh(z) / Cosh(z);
    }

    private static readonly Number Iu = Number.Approx(0, 1);

    public static Number Asin(Number z)
    {
        if (z.IsReal && Math.Abs(z.Re) <= 1) return A(Math.Asin(z.Re));
        // asin z = -i ln(iz + sqrt(1 - z^2))
        var w = Log(Iu * z + Sqrt(A(1) - z * z));
        return A(w.Im, -w.Re);
    }

    public static Number Acos(Number z)
    {
        if (z.IsReal && Math.Abs(z.Re) <= 1) return A(Math.Acos(z.Re));
        var a = Asin(z);
        return A(Math.PI / 2 - a.Re, -a.Im);
    }

    public static Number Atan(Number z)
    {
        if (z.IsReal) return A(Math.Atan(z.Re));
        // atan z = (i/2) ln((i + z)/(i - z))
        var w = Log((Iu + z) / (Iu - z));
        return A(-w.Im / 2, w.Re / 2);
    }

    public static Number Asinh(Number z)
    {
        if (z.IsReal) return A(Math.Asinh(z.Re));
        return Log(z + Sqrt(z * z + A(1)));
    }

    public static Number Acosh(Number z)
    {
        if (z.IsReal && z.Re >= 1) return A(Math.Acosh(z.Re));
        return Log(z + Sqrt(z + A(1)) * Sqrt(z - A(1)));
    }

    public static Number Atanh(Number z)
    {
        if (z.IsReal && Math.Abs(z.Re) < 1) return A(Math.Atanh(z.Re));
        var w = Log((A(1) + z) / (A(1) - z));
        return A(w.Re / 2, w.Im / 2);
    }
}
