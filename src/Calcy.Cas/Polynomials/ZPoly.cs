using System.Numerics;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Polynomials;

/// <summary>Dense univariate polynomial with integer coefficients; index i holds the coefficient of x^i.</summary>
internal sealed class ZPoly : IEquatable<ZPoly>
{
    private readonly BigInteger[] _c;

    public static readonly ZPoly Zero = new([]);
    public static readonly ZPoly One = new([BigInteger.One]);

    public ZPoly(BigInteger[] coefficients)
    {
        int n = coefficients.Length;
        while (n > 0 && coefficients[n - 1].IsZero) n--;
        _c = n == coefficients.Length ? coefficients : coefficients[..n];
    }

    public int Degree => _c.Length - 1;
    public bool IsZero => _c.Length == 0;
    public BigInteger this[int i] => i >= 0 && i < _c.Length ? _c[i] : BigInteger.Zero;
    public BigInteger Lc => _c.Length == 0 ? BigInteger.Zero : _c[^1];
    public IReadOnlyList<BigInteger> Coefficients => _c;

    public QPoly ToQ() => new([.. _c.Select(x => new BigRational(x))]);

    public BigInteger Content()
    {
        BigInteger g = BigInteger.Zero;
        foreach (var c in _c)
        {
            g = BigInteger.GreatestCommonDivisor(g, c);
            if (g.IsOne) break;
        }
        return g;
    }

    /// <summary>Primitive part with a positive leading coefficient.</summary>
    public ZPoly Primitive()
    {
        if (IsZero) return this;
        var g = Content();
        if (Lc.Sign < 0) g = -g;
        if (g.IsOne) return this;
        return new ZPoly([.. _c.Select(x => x / g)]);
    }

    public static ZPoly operator *(ZPoly a, ZPoly b)
    {
        if (a.IsZero || b.IsZero) return Zero;
        var r = new BigInteger[a._c.Length + b._c.Length - 1];
        for (int i = 0; i < a._c.Length; i++)
        {
            if (a._c[i].IsZero) continue;
            for (int j = 0; j < b._c.Length; j++) r[i + j] += a._c[i] * b._c[j];
        }
        return new ZPoly(r);
    }

    public static ZPoly operator +(ZPoly a, ZPoly b)
    {
        int n = Math.Max(a._c.Length, b._c.Length);
        var r = new BigInteger[n];
        for (int i = 0; i < n; i++) r[i] = a[i] + b[i];
        return new ZPoly(r);
    }

    public static ZPoly operator -(ZPoly a, ZPoly b)
    {
        int n = Math.Max(a._c.Length, b._c.Length);
        var r = new BigInteger[n];
        for (int i = 0; i < n; i++) r[i] = a[i] - b[i];
        return new ZPoly(r);
    }

    public ZPoly Scale(BigInteger s) => new([.. _c.Select(x => x * s)]);

    /// <summary>Exact division over the integers, or null when b does not divide this.</summary>
    public ZPoly? DivExact(ZPoly b)
    {
        if (b.IsZero) throw new DivideByZeroException();
        if (IsZero) return Zero;
        if (Degree < b.Degree) return null;
        var r = (BigInteger[])_c.Clone();
        var q = new BigInteger[Degree - b.Degree + 1];
        var lc = b.Lc;
        for (int k = q.Length - 1; k >= 0; k--)
        {
            var top = r[k + b.Degree];
            if (top.IsZero) continue;
            var coef = BigInteger.DivRem(top, lc, out var rem);
            if (!rem.IsZero) return null;
            q[k] = coef;
            for (int j = 0; j <= b.Degree; j++) r[k + j] -= coef * b._c[j];
        }
        for (int i = 0; i < b.Degree && i < r.Length; i++)
            if (!r[i].IsZero) return null;
        return new ZPoly(q);
    }

    public ZPoly Derivative()
    {
        if (_c.Length <= 1) return Zero;
        var r = new BigInteger[_c.Length - 1];
        for (int i = 1; i < _c.Length; i++) r[i - 1] = _c[i] * i;
        return new ZPoly(r);
    }

    public BigInteger MaxAbs()
    {
        BigInteger m = BigInteger.Zero;
        foreach (var c in _c)
        {
            var a = BigInteger.Abs(c);
            if (a > m) m = a;
        }
        return m;
    }

    /// <summary>Coefficients reduced into the symmetric range (-m/2, m/2].</summary>
    public ZPoly SymmetricMod(BigInteger m)
    {
        var half = m / 2;
        var r = new BigInteger[_c.Length];
        for (int i = 0; i < _c.Length; i++)
        {
            var v = BigInteger.Remainder(_c[i], m);
            if (v.Sign < 0) v += m;
            if (v > half) v -= m;
            r[i] = v;
        }
        return new ZPoly(r);
    }

    public ZPoly Mod(BigInteger m)
    {
        var r = new BigInteger[_c.Length];
        for (int i = 0; i < _c.Length; i++)
        {
            var v = BigInteger.Remainder(_c[i], m);
            if (v.Sign < 0) v += m;
            r[i] = v;
        }
        return new ZPoly(r);
    }

    public bool Equals(ZPoly? other)
    {
        if (other is null || other._c.Length != _c.Length) return false;
        for (int i = 0; i < _c.Length; i++)
            if (_c[i] != other._c[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is ZPoly z && Equals(z);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var c in _c) h.Add(c);
        return h.ToHashCode();
    }

    public override string ToString() => ToQ().ToString();
}

/// <summary>Polynomial arithmetic over GF(p) on coefficient arrays (low to high, trimmed), p an odd prime below 2^31.</summary>
internal static class ModP
{
    public static long[] Trim(long[] a)
    {
        int n = a.Length;
        while (n > 0 && a[n - 1] == 0) n--;
        return n == a.Length ? a : a[..n];
    }

    public static long Norm(long v, long p)
    {
        v %= p;
        return v < 0 ? v + p : v;
    }

    public static long Inverse(long a, long p)
    {
        long t = 0, newT = 1, r = p, newR = Norm(a, p);
        while (newR != 0)
        {
            long q = r / newR;
            (t, newT) = (newT, t - q * newT);
            (r, newR) = (newR, r - q * newR);
        }
        if (r != 1) throw new ArithmeticException("not invertible");
        return Norm(t, p);
    }

    public static long[] FromZ(ZPoly f, long p)
    {
        var r = new long[f.Degree + 1];
        for (int i = 0; i <= f.Degree; i++)
        {
            var v = (long)BigInteger.Remainder(f[i], p);
            r[i] = v < 0 ? v + p : v;
        }
        return Trim(r);
    }

    public static ZPoly ToZ(long[] a) => new([.. a.Select(x => new BigInteger(x))]);

    public static long[] Add(long[] a, long[] b, long p)
    {
        var r = new long[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < r.Length; i++)
        {
            long v = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
            r[i] = v >= p ? v - p : v;
        }
        return Trim(r);
    }

    public static long[] Sub(long[] a, long[] b, long p)
    {
        var r = new long[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < r.Length; i++)
        {
            long v = (i < a.Length ? a[i] : 0) - (i < b.Length ? b[i] : 0);
            r[i] = v < 0 ? v + p : v;
        }
        return Trim(r);
    }

    public static long[] Mul(long[] a, long[] b, long p)
    {
        if (a.Length == 0 || b.Length == 0) return [];
        var r = new long[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == 0) continue;
            for (int j = 0; j < b.Length; j++) r[i + j] = (r[i + j] + a[i] * b[j]) % p;
        }
        return Trim(r);
    }

    public static long[] Scale(long[] a, long s, long p)
    {
        var r = new long[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = a[i] * s % p;
        return Trim(r);
    }

    public static long[] Monic(long[] a, long p) => a.Length == 0 ? a : Scale(a, Inverse(a[^1], p), p);

    public static (long[] Q, long[] R) DivRem(long[] a, long[] b, long p)
    {
        if (b.Length == 0) throw new DivideByZeroException();
        if (a.Length < b.Length) return ([], a);
        var r = (long[])a.Clone();
        var q = new long[a.Length - b.Length + 1];
        long inv = Inverse(b[^1], p);
        int db = b.Length - 1;
        for (int k = q.Length - 1; k >= 0; k--)
        {
            long coef = r[k + db] * inv % p;
            q[k] = coef;
            if (coef == 0) continue;
            for (int j = 0; j <= db; j++)
            {
                r[k + j] = (r[k + j] - coef * b[j]) % p;
                if (r[k + j] < 0) r[k + j] += p;
            }
        }
        return (Trim(q), Trim(r[..db]));
    }

    public static long[] Rem(long[] a, long[] b, long p) => DivRem(a, b, p).R;

    public static long[] Gcd(long[] a, long[] b, long p)
    {
        while (b.Length > 0)
        {
            (a, b) = (b, Rem(a, b, p));
        }
        return Monic(a, p);
    }

    /// <summary>Returns (g, s, t) with s·a + t·b = g (monic).</summary>
    public static (long[] G, long[] S, long[] T) ExtendedGcd(long[] a, long[] b, long p)
    {
        long[] r0 = a, r1 = b, s0 = [1], s1 = [], t0 = [], t1 = [1];
        while (r1.Length > 0)
        {
            var (q, r) = DivRem(r0, r1, p);
            (r0, r1) = (r1, r);
            (s0, s1) = (s1, Sub(s0, Mul(q, s1, p), p));
            (t0, t1) = (t1, Sub(t0, Mul(q, t1, p), p));
        }
        long inv = Inverse(r0[^1], p);
        return (Scale(r0, inv, p), Scale(s0, inv, p), Scale(t0, inv, p));
    }

    public static long[] Derivative(long[] a, long p)
    {
        if (a.Length <= 1) return [];
        var r = new long[a.Length - 1];
        for (int i = 1; i < a.Length; i++) r[i - 1] = a[i] * (i % p) % p;
        return Trim(r);
    }

    /// <summary>b^e mod m over GF(p).</summary>
    public static long[] PowMod(long[] b, BigInteger e, long[] m, long p)
    {
        long[] result = [1];
        b = Rem(b, m, p);
        while (e > 0)
        {
            CasEnv.Check();
            if (!e.IsEven) result = Rem(Mul(result, b, p), m, p);
            e >>= 1;
            if (e > 0) b = Rem(Mul(b, b, p), m, p);
        }
        return result;
    }

    public static bool IsOne(long[] a) => a.Length == 1 && a[0] == 1;
}
