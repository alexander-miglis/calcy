using System.Numerics;
using System.Text;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Polynomials;

/// <summary>Dense univariate polynomial with exact rational coefficients; index i holds the coefficient of x^i.</summary>
internal sealed class QPoly : IEquatable<QPoly>
{
    private readonly BigRational[] _c;

    public static readonly QPoly Zero = new([]);
    public static readonly QPoly One = new([BigRational.One]);
    public static readonly QPoly X = new([BigRational.Zero, BigRational.One]);

    public QPoly(BigRational[] coefficients)
    {
        int n = coefficients.Length;
        while (n > 0 && coefficients[n - 1].IsZero) n--;
        _c = n == coefficients.Length ? coefficients : coefficients[..n];
    }

    public static QPoly FromInts(params long[] c) => new([.. c.Select(v => new BigRational(v))]);
    public static QPoly Constant(BigRational c) => c.IsZero ? Zero : new([c]);

    /// <summary>c·x^k.</summary>
    public static QPoly Monomial(BigRational c, int k)
    {
        if (c.IsZero) return Zero;
        var a = new BigRational[k + 1];
        for (int i = 0; i < k; i++) a[i] = BigRational.Zero;
        a[k] = c;
        return new QPoly(a);
    }

    public int Degree => _c.Length - 1;
    public bool IsZero => _c.Length == 0;
    public bool IsConstant => _c.Length <= 1;
    public BigRational this[int i] => i >= 0 && i < _c.Length ? _c[i] : BigRational.Zero;
    public BigRational Lc => _c.Length == 0 ? BigRational.Zero : _c[^1];
    public IReadOnlyList<BigRational> Coefficients => _c;

    public static QPoly operator +(QPoly a, QPoly b)
    {
        int n = Math.Max(a._c.Length, b._c.Length);
        var r = new BigRational[n];
        for (int i = 0; i < n; i++) r[i] = a[i] + b[i];
        return new QPoly(r);
    }

    public static QPoly operator -(QPoly a, QPoly b)
    {
        int n = Math.Max(a._c.Length, b._c.Length);
        var r = new BigRational[n];
        for (int i = 0; i < n; i++) r[i] = a[i] - b[i];
        return new QPoly(r);
    }

    public static QPoly operator -(QPoly a) => new([.. a._c.Select(x => -x)]);

    public static QPoly operator *(QPoly a, QPoly b)
    {
        if (a.IsZero || b.IsZero) return Zero;
        var r = new BigRational[a._c.Length + b._c.Length - 1];
        for (int i = 0; i < r.Length; i++) r[i] = BigRational.Zero;
        for (int i = 0; i < a._c.Length; i++)
        {
            if (a._c[i].IsZero) continue;
            for (int j = 0; j < b._c.Length; j++)
                if (!b._c[j].IsZero) r[i + j] += a._c[i] * b._c[j];
        }
        return new QPoly(r);
    }

    public QPoly Scale(BigRational s) => s.IsZero ? Zero : new QPoly([.. _c.Select(x => x * s)]);

    public QPoly Monic() => IsZero ? this : Scale(Lc.Reciprocal());

    /// <summary>Euclidean division: this = q·b + r with deg r &lt; deg b.</summary>
    public (QPoly Quotient, QPoly Remainder) DivRem(QPoly b)
    {
        if (b.IsZero) throw new DivideByZeroException("Polynomial division by zero");
        if (Degree < b.Degree) return (Zero, this);
        var r = (BigRational[])_c.Clone();
        var q = new BigRational[Degree - b.Degree + 1];
        var inv = b.Lc.Reciprocal();
        for (int k = q.Length - 1; k >= 0; k--)
        {
            var coef = r[k + b.Degree] * inv;
            q[k] = coef;
            if (coef.IsZero) continue;
            for (int j = 0; j <= b.Degree; j++) r[k + j] -= coef * b._c[j];
        }
        return (new QPoly(q), new QPoly(r[..Math.Min(r.Length, b.Degree)]));
    }

    public QPoly Rem(QPoly b) => DivRem(b).Remainder;

    /// <summary>Monic greatest common divisor (zero when both are zero).</summary>
    public static QPoly Gcd(QPoly a, QPoly b)
    {
        while (!b.IsZero)
        {
            CasEnv.Check();
            (a, b) = (b, a.Rem(b));
        }
        return a.IsZero ? Zero : a.Monic();
    }

    /// <summary>Extended Euclid: returns (g, s, t) with s·a + t·b = g, g monic.</summary>
    public static (QPoly G, QPoly S, QPoly T) ExtendedGcd(QPoly a, QPoly b)
    {
        QPoly r0 = a, r1 = b, s0 = One, s1 = Zero, t0 = Zero, t1 = One;
        while (!r1.IsZero)
        {
            CasEnv.Check();
            var (q, r) = r0.DivRem(r1);
            (r0, r1) = (r1, r);
            (s0, s1) = (s1, s0 - q * s1);
            (t0, t1) = (t1, t0 - q * t1);
        }
        if (r0.IsZero) return (Zero, Zero, Zero);
        var inv = r0.Lc.Reciprocal();
        return (r0.Scale(inv), s0.Scale(inv), t0.Scale(inv));
    }

    public QPoly Derivative()
    {
        if (_c.Length <= 1) return Zero;
        var r = new BigRational[_c.Length - 1];
        for (int i = 1; i < _c.Length; i++) r[i - 1] = _c[i] * i;
        return new QPoly(r);
    }

    /// <summary>The antiderivative with zero constant term.</summary>
    public QPoly Integral()
    {
        if (IsZero) return Zero;
        var r = new BigRational[_c.Length + 1];
        r[0] = BigRational.Zero;
        for (int i = 0; i < _c.Length; i++) r[i + 1] = _c[i] / (i + 1);
        return new QPoly(r);
    }

    public BigRational Eval(BigRational x)
    {
        var acc = BigRational.Zero;
        for (int i = _c.Length - 1; i >= 0; i--) acc = acc * x + _c[i];
        return acc;
    }

    public double EvalDouble(double x)
    {
        double acc = 0;
        for (int i = _c.Length - 1; i >= 0; i--) acc = acc * x + _c[i].ToDouble();
        return acc;
    }

    public QPoly Pow(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        QPoly result = One, b = this;
        while (n > 0)
        {
            if ((n & 1) != 0) result *= b;
            n >>= 1;
            if (n > 0) b *= b;
        }
        return result;
    }

    /// <summary>this(g(x)).</summary>
    public QPoly Compose(QPoly g)
    {
        QPoly acc = Zero;
        for (int i = _c.Length - 1; i >= 0; i--) acc = acc * g + Constant(_c[i]);
        return acc;
    }

    /// <summary>this(x + a).</summary>
    public QPoly Shift(BigRational a) => Compose(new QPoly([a, BigRational.One]));

    /// <summary>this(x)·x^k.</summary>
    public QPoly MulXPow(int k)
    {
        if (IsZero || k == 0) return this;
        var r = new BigRational[_c.Length + k];
        for (int i = 0; i < k; i++) r[i] = BigRational.Zero;
        Array.Copy(_c, 0, r, k, _c.Length);
        return new QPoly(r);
    }

    /// <summary>Splits into a positive rational content and a primitive integer polynomial with positive leading coefficient.</summary>
    public (BigRational Content, ZPoly Primitive) ToPrimitive()
    {
        if (IsZero) return (BigRational.Zero, ZPoly.Zero);
        BigInteger lcm = BigInteger.One;
        foreach (var c in _c) lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, c.Den) * c.Den;
        var ints = new BigInteger[_c.Length];
        BigInteger g = BigInteger.Zero;
        for (int i = 0; i < _c.Length; i++)
        {
            ints[i] = _c[i].Num * (lcm / _c[i].Den);
            g = BigInteger.GreatestCommonDivisor(g, ints[i]);
        }
        if (ints[^1].Sign < 0) g = -g;
        for (int i = 0; i < ints.Length; i++) ints[i] /= g;
        return (new BigRational(g, lcm), new ZPoly(ints));
    }

    /// <summary>Square-free decomposition (Yun): this = lc · ∏ f_i^i with f_i monic, square-free and coprime.</summary>
    public List<(QPoly Factor, int Multiplicity)> SquareFree()
    {
        var result = new List<(QPoly, int)>();
        if (Degree < 1) return result;
        var f = Monic();
        var d = f.Derivative();
        var a = Gcd(f, d);
        var b = f.DivRem(a).Quotient;
        var c = d.DivRem(a).Quotient;
        var dd = c - b.Derivative();
        int i = 1;
        while (b.Degree > 0)
        {
            CasEnv.Check();
            var g = Gcd(b, dd);
            if (g.Degree > 0) result.Add((g, i));
            var nb = b.DivRem(g).Quotient;
            c = dd.DivRem(g).Quotient;
            b = nb;
            dd = c - b.Derivative();
            i++;
        }
        return result;
    }

    public bool Equals(QPoly? other)
    {
        if (other is null || other._c.Length != _c.Length) return false;
        for (int i = 0; i < _c.Length; i++)
            if (_c[i] != other._c[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is QPoly q && Equals(q);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var c in _c) h.Add(c);
        return h.ToHashCode();
    }

    public override string ToString()
    {
        if (IsZero) return "0";
        var sb = new StringBuilder();
        for (int i = _c.Length - 1; i >= 0; i--)
        {
            if (_c[i].IsZero) continue;
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append('(').Append(_c[i]).Append(')');
            if (i > 0) sb.Append("x^").Append(i);
        }
        return sb.ToString();
    }
}
