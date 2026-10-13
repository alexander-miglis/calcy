using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Calcy.Python.Runtime;

/// <summary>Python int: a long fast path with a BigInteger fallback.</summary>
internal class PyInt : PyObject
{
    internal readonly long V;
    internal readonly BigInteger Big; // non zero only when the value does not fit in a long

    private static readonly PyInt[] Cache = CreateCache();

    private static PyInt[] CreateCache()
    {
        var c = new PyInt[262];
        for (int i = 0; i < c.Length; i++) c[i] = new PyInt(i - 5);
        return c;
    }

    private protected PyInt(long v) { V = v; }

    private protected PyInt(BigInteger b) { Big = b; }

    internal override PyType Type => PyTypes.Int;

    public bool IsBig
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !Big.IsZero;
    }

    public static readonly PyInt Zero = From(0);
    public static readonly PyInt One = From(1);
    public static readonly PyInt MinusOne = From(-1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PyInt From(long v) => (ulong)(v + 5) < 262UL ? Cache[v + 5] : new PyInt(v);

    public static PyInt From(int v) => From((long)v);

    public static PyInt From(BigInteger b)
    {
        if (b >= long.MinValue && b <= long.MaxValue) return From((long)b);
        return new PyInt(b);
    }

    public BigInteger ToBig() => IsBig ? Big : new BigInteger(V);

    public int Sign => IsBig ? Big.Sign : Math.Sign(V);

    public long BitLength => IsBig ? (long)BigInteger.Abs(Big).GetBitLength() : (V == 0 ? 0 : 64 - BitOperations.LeadingZeroCount(V < 0 ? (ulong)(-(V + 1)) + 1 : (ulong)V));

    public override string ToString() => IsBig ? Big.ToString(CultureInfo.InvariantCulture) : V.ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns the value as a long or throws OverflowError.</summary>
    public long ToLong(string? what = null)
    {
        if (IsBig) throw PyErr.OverflowError(what ?? "Python int too large to convert to C ssize_t");
        return V;
    }

    public bool FitsInt => !IsBig && V >= int.MinValue && V <= int.MaxValue;
}

/// <summary>Python bool, a subclass of int.</summary>
internal sealed class PyBool : PyInt
{
    public static readonly PyBool True = new(1);
    public static readonly PyBool False = new(0);

    private PyBool(long v) : base(v) { }

    internal override PyType Type => PyTypes.Bool;

    public bool Value => V != 0;

    public override string ToString() => V != 0 ? "True" : "False";
}

internal class PyFloat : PyObject
{
    internal readonly double V;

    public PyFloat(double v) { V = v; }

    internal override PyType Type => PyTypes.Float;

    public static readonly PyFloat Zero = new(0.0);
    public static readonly PyFloat One = new(1.0);
}

internal sealed class PyComplex : PyObject
{
    internal readonly double Real;
    internal readonly double Imag;

    public PyComplex(double re, double im)
    {
        Real = re;
        Imag = im;
    }

    internal override PyType Type => PyTypes.Complex;
}

/// <summary>Integer arithmetic with Python semantics.</summary>
internal static class IntMath
{
    public static PyInt Add(PyInt a, PyInt b)
    {
        if (!a.IsBig && !b.IsBig)
        {
            long x = a.V, y = b.V, r = x + y;
            if (((x ^ r) & (y ^ r)) >= 0) return PyInt.From(r);
            return PyInt.From((BigInteger)x + y);
        }
        return PyInt.From(a.ToBig() + b.ToBig());
    }

    public static PyInt Sub(PyInt a, PyInt b)
    {
        if (!a.IsBig && !b.IsBig)
        {
            long x = a.V, y = b.V, r = x - y;
            if (((x ^ y) & (x ^ r)) >= 0) return PyInt.From(r);
            return PyInt.From((BigInteger)x - y);
        }
        return PyInt.From(a.ToBig() - b.ToBig());
    }

    public static PyInt Mul(PyInt a, PyInt b)
    {
        if (!a.IsBig && !b.IsBig)
        {
            long x = a.V, y = b.V;
            long hi = Math.BigMul(x, y, out long lo);
            if ((hi == 0 && lo >= 0) || (hi == -1 && lo < 0)) return PyInt.From(lo);
            return PyInt.From((BigInteger)x * y);
        }
        long bits = a.BitLength + b.BitLength;
        if (bits > Limits.MaxIntBits) Limits.CheckIntBits(bits);
        return PyInt.From(a.ToBig() * b.ToBig());
    }

    public static PyInt Neg(PyInt a)
    {
        if (!a.IsBig && a.V != long.MinValue) return PyInt.From(-a.V);
        return PyInt.From(-a.ToBig());
    }

    public static PyInt Abs(PyInt a) => a.Sign < 0 ? Neg(a) : a.GetType() == typeof(PyInt) ? a : a.IsBig ? PyInt.From(a.Big) : PyInt.From(a.V);

    public static PyInt FloorDiv(PyInt a, PyInt b)
    {
        if (!a.IsBig && !b.IsBig)
        {
            long x = a.V, y = b.V;
            if (y == 0) throw PyErr.ZeroDivisionError("integer division or modulo by zero");
            if (!(x == long.MinValue && y == -1))
            {
                long q = x / y;
                if ((x % y != 0) && ((x ^ y) < 0)) q--;
                return PyInt.From(q);
            }
        }
        var B = b.ToBig();
        if (B.IsZero) throw PyErr.ZeroDivisionError("integer division or modulo by zero");
        var Q = BigInteger.DivRem(a.ToBig(), B, out var R);
        if (!R.IsZero && (R.Sign != B.Sign)) Q -= 1;
        return PyInt.From(Q);
    }

    public static PyInt Mod(PyInt a, PyInt b)
    {
        if (!a.IsBig && !b.IsBig)
        {
            long x = a.V, y = b.V;
            if (y == 0) throw PyErr.ZeroDivisionError("integer modulo by zero");
            if (y == -1) return PyInt.Zero;
            long r = x % y;
            if (r != 0 && ((r ^ y) < 0)) r += y;
            return PyInt.From(r);
        }
        var B = b.ToBig();
        if (B.IsZero) throw PyErr.ZeroDivisionError("integer modulo by zero");
        var R2 = BigInteger.Remainder(a.ToBig(), B);
        if (!R2.IsZero && R2.Sign != B.Sign) R2 += B;
        return PyInt.From(R2);
    }

    public static (PyInt q, PyInt r) DivMod(PyInt a, PyInt b)
    {
        if (b.Sign == 0) throw PyErr.ZeroDivisionError("integer division or modulo by zero");
        return (FloorDiv(a, b), Mod(a, b));
    }

    public static PyObject TrueDiv(PyInt a, PyInt b)
    {
        if (b.Sign == 0) throw PyErr.ZeroDivisionError("division by zero");
        const long Lim = 1L << 53;
        if (!a.IsBig && !b.IsBig && a.V > -Lim && a.V < Lim && b.V > -Lim && b.V < Lim)
            return new PyFloat((double)a.V / b.V);
        return new PyFloat(BigTrueDiv(a.ToBig(), b.ToBig()));
    }

    /// <summary>Correctly rounded division of two big integers.</summary>
    public static double BigTrueDiv(BigInteger a, BigInteger b)
    {
        bool neg = (a.Sign < 0) != (b.Sign < 0);
        a = BigInteger.Abs(a);
        b = BigInteger.Abs(b);
        if (a.IsZero) return neg ? -0.0 : 0.0;
        long shift = (long)a.GetBitLength() - (long)b.GetBitLength();
        long k = shift - 55;
        BigInteger q, r;
        if (k >= 0) q = BigInteger.DivRem(a, b << (int)Math.Min(k, int.MaxValue), out r);
        else q = BigInteger.DivRem(a << (int)Math.Min(-k, int.MaxValue), b, out r);
        if (!r.IsZero) q |= 1;
        // q has about 55 or 56 bits
        double d = (double)(ulong)q;
        if (k > 2000) throw PyErr.OverflowError("integer division result too large for a float");
        double res = Math.ScaleB(d, (int)Math.Max(k, -3000));
        if (double.IsInfinity(res)) throw PyErr.OverflowError("integer division result too large for a float");
        return neg ? -res : res;
    }

    public static PyObject Pow(PyInt a, PyInt b)
    {
        if (b.Sign < 0)
        {
            double fa = ToDouble(a);
            if (fa == 0) throw PyErr.ZeroDivisionError("0.0 cannot be raised to a negative power");
            return FloatMath.Pow(fa, ToDouble(b));
        }
        if (!a.IsBig)
        {
            if (a.V == 0) return b.Sign == 0 ? PyInt.One : PyInt.Zero;
            if (a.V == 1) return PyInt.One;
            if (a.V == -1) return (b.IsBig ? !b.Big.IsEven : (b.V & 1) != 0) ? PyInt.MinusOne : PyInt.One;
        }
        if (b.IsBig) Limits.CheckIntBits(long.MaxValue);
        long e = b.V;
        double estBits = e * Math.Log2(Math.Max(2.0, Math.Abs(ToDoubleNoThrow(a))));
        if (a.IsBig) estBits = e * (double)(a.BitLength - 1);
        if (estBits > Limits.MaxIntBits) Limits.CheckIntBits((long)Math.Min(estBits, long.MaxValue));
        if (!a.IsBig && e < 64)
        {
            long result = 1, bas = a.V;
            long ee = e;
            bool overflow = false;
            while (true)
            {
                if ((ee & 1) != 0)
                {
                    long hi = Math.BigMul(result, bas, out long lo);
                    if (!((hi == 0 && lo >= 0) || (hi == -1 && lo < 0))) { overflow = true; break; }
                    result = lo;
                }
                ee >>= 1;
                if (ee == 0) break;
                long h2 = Math.BigMul(bas, bas, out long l2);
                if (!((h2 == 0 && l2 >= 0) || (h2 == -1 && l2 < 0))) { overflow = true; break; }
                bas = l2;
            }
            if (!overflow) return PyInt.From(result);
        }
        if (e > int.MaxValue) Limits.CheckIntBits(long.MaxValue);
        return PyInt.From(BigInteger.Pow(a.ToBig(), (int)e));
    }

    public static PyInt PowMod(PyInt a, PyInt b, PyInt m)
    {
        var M = m.ToBig();
        if (M.IsZero) throw PyErr.ValueError("pow() 3rd argument cannot be 0");
        var A = a.ToBig();
        var B = b.ToBig();
        bool negMod = M.Sign < 0;
        var absM = BigInteger.Abs(M);
        if (B.Sign < 0)
        {
            A = ModInverse(A, absM);
            B = -B;
        }
        var r = BigInteger.ModPow(A, B, absM);
        if (r.Sign < 0) r += absM;
        if (negMod && !r.IsZero) r -= absM;
        return PyInt.From(r);
    }

    public static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        a %= m;
        if (a.Sign < 0) a += m;
        BigInteger g = m, x = 0, x1 = 1, a1 = a;
        while (!a1.IsZero)
        {
            var q = g / a1;
            (g, a1) = (a1, g - q * a1);
            (x, x1) = (x1, x - q * x1);
        }
        if (g != 1) throw PyErr.ValueError("base is not invertible for the given modulus");
        x %= m;
        if (x.Sign < 0) x += m;
        return x;
    }

    public static PyInt LShift(PyInt a, PyInt b)
    {
        if (b.Sign < 0) throw PyErr.ValueError("negative shift count");
        if (a.Sign == 0) return PyInt.Zero;
        if (b.IsBig) Limits.CheckIntBits(long.MaxValue);
        long n = b.V;
        long bits = a.BitLength + n;
        if (bits > Limits.MaxIntBits) Limits.CheckIntBits(bits);
        if (!a.IsBig && bits < 63) return PyInt.From(a.V << (int)n);
        return PyInt.From(a.ToBig() << (int)n);
    }

    public static PyInt RShift(PyInt a, PyInt b)
    {
        if (b.Sign < 0) throw PyErr.ValueError("negative shift count");
        if (b.IsBig || b.V >= a.BitLength + 1) return a.Sign < 0 ? PyInt.MinusOne : PyInt.Zero;
        if (!a.IsBig) return PyInt.From(b.V >= 64 ? (a.V < 0 ? -1 : 0) : a.V >> (int)b.V);
        return PyInt.From(a.Big >> (int)b.V);
    }

    public static PyInt And(PyInt a, PyInt b) => (!a.IsBig && !b.IsBig) ? PyInt.From(a.V & b.V) : PyInt.From(a.ToBig() & b.ToBig());
    public static PyInt Or(PyInt a, PyInt b) => (!a.IsBig && !b.IsBig) ? PyInt.From(a.V | b.V) : PyInt.From(a.ToBig() | b.ToBig());
    public static PyInt Xor(PyInt a, PyInt b) => (!a.IsBig && !b.IsBig) ? PyInt.From(a.V ^ b.V) : PyInt.From(a.ToBig() ^ b.ToBig());
    public static PyInt Invert(PyInt a) => (!a.IsBig) ? PyInt.From(~a.V) : PyInt.From(-a.Big - 1);

    public static int Compare(PyInt a, PyInt b)
    {
        if (!a.IsBig && !b.IsBig) return a.V.CompareTo(b.V);
        return a.ToBig().CompareTo(b.ToBig());
    }

    public static bool Equal(PyInt a, PyInt b)
    {
        if (!a.IsBig && !b.IsBig) return a.V == b.V;
        return a.IsBig && b.IsBig && a.Big == b.Big;
    }

    /// <summary>Correctly rounded conversion to double; throws OverflowError when too large.</summary>
    public static double ToDouble(PyInt a)
    {
        if (!a.IsBig) return a.V;
        double d = BigToDouble(a.Big);
        if (double.IsInfinity(d)) throw PyErr.OverflowError("int too large to convert to float");
        return d;
    }

    private static double ToDoubleNoThrow(PyInt a) => a.IsBig ? BigToDouble(a.Big) : a.V;

    public static double BigToDouble(BigInteger v)
    {
        if (v >= long.MinValue && v <= long.MaxValue) return (long)v;
        bool neg = v.Sign < 0;
        var m = BigInteger.Abs(v);
        long bl = (long)m.GetBitLength();
        if (bl > 1100) return neg ? double.NegativeInfinity : double.PositiveInfinity;
        int shift = (int)(bl - 64);
        var top = m >> shift;
        ulong t = (ulong)top;
        if (!(m & ((BigInteger.One << shift) - 1)).IsZero) t |= 1;
        double d = Math.ScaleB((double)t, shift);
        return neg ? -d : d;
    }

    public static long Hash(PyInt a)
    {
        const ulong P = (1UL << 61) - 1;
        long r;
        if (!a.IsBig)
        {
            long v = a.V;
            ulong abs = v < 0 ? (ulong)(-(v + 1)) + 1 : (ulong)v;
            ulong m = abs % P;
            r = v < 0 ? -(long)m : (long)m;
        }
        else
        {
            var m = (long)(ulong)(BigInteger.Abs(a.Big) % P);
            r = a.Big.Sign < 0 ? -m : m;
        }
        return r == -1 ? -2 : r;
    }

    public static BigInteger ISqrt(BigInteger n)
    {
        if (n.Sign < 0) throw PyErr.ValueError("isqrt() argument must be nonnegative");
        if (n < 2) return n;
        if (n < (BigInteger.One << 52))
        {
            long v = (long)n;
            long s = (long)Math.Sqrt(v);
            while (s * s > v) s--;
            while ((s + 1) * (s + 1) <= v) s++;
            return s;
        }
        int bits = (int)n.GetBitLength();
        var x = BigInteger.One << ((bits + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x) return x;
            x = y;
        }
    }

    /// <summary>Parses an integer string with an optional sign and base prefix as int() does.</summary>
    public static PyInt? Parse(string text, int radix)
    {
        var s = text.Trim(StrType.WhitespaceChars);
        if (s.Length == 0) return null;
        bool neg = false;
        int i = 0;
        if (s[0] == '+' || s[0] == '-')
        {
            neg = s[0] == '-';
            i = 1;
        }
        if (i >= s.Length) return null;
        if (radix == 0)
        {
            if (s.Length > i + 1 && s[i] == '0')
            {
                char p = char.ToLowerInvariant(s[i + 1]);
                if (p == 'x') { radix = 16; i += 2; }
                else if (p == 'o') { radix = 8; i += 2; }
                else if (p == 'b') { radix = 2; i += 2; }
                else
                {
                    radix = 10;
                    for (int k = i; k < s.Length; k++)
                        if (s[k] != '0' && s[k] != '_') return null;
                }
            }
            else radix = 10;
        }
        else if (s.Length > i + 1 && s[i] == '0')
        {
            char p = char.ToLowerInvariant(s[i + 1]);
            if ((radix == 16 && p == 'x') || (radix == 8 && p == 'o') || (radix == 2 && p == 'b')) i += 2;
        }
        if (i >= s.Length) return null;
        if (s[i] == '_' && (i == 0 || !(i >= 2 && s[i - 2] == '0'))) return null;
        BigInteger acc = BigInteger.Zero;
        long small = 0;
        bool useBig = false;
        int digits = 0;
        char prev = '\0';
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '_')
            {
                if (prev == '_' || i == s.Length - 1) return null;
                prev = c;
                continue;
            }
            int d = DigitValue(c);
            if (d < 0 || d >= radix) return null;
            digits++;
            if (!useBig)
            {
                if (small > (long.MaxValue - d) / radix)
                {
                    useBig = true;
                    acc = small;
                    acc = acc * radix + d;
                }
                else small = small * radix + d;
            }
            else acc = acc * radix + d;
            prev = c;
        }
        if (digits == 0) return null;
        if (useBig)
        {
            Limits.CheckBig(acc);
            return PyInt.From(neg ? -acc : acc);
        }
        return PyInt.From(neg ? -small : small);
    }

    public static int DigitValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'z') return c - 'a' + 10;
        if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
        if (char.IsDigit(c)) return (int)char.GetNumericValue(c);
        return -1;
    }

    public static string ToRadix(BigInteger v, int radix)
    {
        if (v.IsZero) return "0";
        bool neg = v.Sign < 0;
        v = BigInteger.Abs(v);
        const string digitChars = "0123456789abcdefghijklmnopqrstuvwxyz";
        var sb = new System.Text.StringBuilder();
        if (radix == 16 || radix == 8 || radix == 2)
        {
            int bitsPer = radix == 16 ? 4 : radix == 8 ? 3 : 1;
            int mask = radix - 1;
            while (!v.IsZero)
            {
                int d = (int)(v & mask);
                sb.Append(digitChars[d]);
                v >>= bitsPer;
            }
        }
        else
        {
            while (!v.IsZero)
            {
                v = BigInteger.DivRem(v, radix, out var r);
                sb.Append(digitChars[(int)r]);
            }
        }
        if (neg) sb.Append('-');
        var arr = sb.ToString().ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }
}

/// <summary>Float arithmetic with Python semantics.</summary>
internal static class FloatMath
{
    public static PyFloat Div(double a, double b)
    {
        if (b == 0) throw PyErr.ZeroDivisionError("float division by zero");
        return new PyFloat(a / b);
    }

    public static double Mod(double vx, double wx)
    {
        if (wx == 0) throw PyErr.ZeroDivisionError("float modulo");
        double mod = vx % wx;
        if (mod != 0)
        {
            if ((wx < 0) != (mod < 0)) mod += wx;
        }
        else mod = Math.CopySign(0.0, wx);
        return mod;
    }

    public static (double div, double mod) DivMod(double vx, double wx, bool floorDivMessage = false)
    {
        if (wx == 0) throw PyErr.ZeroDivisionError(floorDivMessage ? "float floor division by zero" : "float divmod()");
        double mod = vx % wx;
        double div = (vx - mod) / wx;
        if (mod != 0)
        {
            if ((wx < 0) != (mod < 0))
            {
                mod += wx;
                div -= 1.0;
            }
        }
        else mod = Math.CopySign(0.0, wx);
        double floordiv;
        if (div != 0)
        {
            floordiv = Math.Floor(div);
            if (div - floordiv > 0.5) floordiv += 1.0;
        }
        else floordiv = Math.CopySign(0.0, vx / wx);
        return (floordiv, mod);
    }

    public static double FloorDiv(double a, double b) => DivMod(a, b, true).div;

    public static PyObject Pow(double iv, double iw)
    {
        if (iw == 0) return PyFloat.One;
        if (double.IsNaN(iv)) return new PyFloat(iv == 1.0 ? 1.0 : double.NaN);
        if (double.IsNaN(iw)) return new PyFloat(iv == 1.0 ? 1.0 : double.NaN);
        if (double.IsInfinity(iw))
        {
            iv = Math.Abs(iv);
            if (iv == 1.0) return PyFloat.One;
            if ((iw > 0.0) == (iv > 1.0)) return new PyFloat(Math.Abs(iw));
            return PyFloat.Zero;
        }
        if (double.IsInfinity(iv))
        {
            bool iwOdd = Math.Abs(iw) % 2.0 == 1.0;
            if (iw > 0) return new PyFloat(iwOdd ? iv : Math.Abs(iv));
            return new PyFloat(iwOdd ? Math.CopySign(0.0, iv) : 0.0);
        }
        if (iv == 0.0)
        {
            bool iwOdd = Math.Abs(iw) % 2.0 == 1.0;
            if (iw < 0.0) throw PyErr.ZeroDivisionError("0.0 cannot be raised to a negative power");
            return new PyFloat(iwOdd ? iv : 0.0);
        }
        bool negate = false;
        if (iv < 0.0)
        {
            if (Math.Floor(iw) != iw)
            {
                // Python 3 returns a complex number
                return ComplexMath.Pow(iv, 0, iw, 0);
            }
            iv = -iv;
            negate = Math.Abs(iw) % 2.0 == 1.0;
        }
        if (iv == 1.0) return new PyFloat(negate ? -1.0 : 1.0);
        double r = Math.Pow(iv, iw);
        if (double.IsInfinity(r)) throw PyErr.OverflowError("(34, 'Numerical result out of range')");
        return new PyFloat(negate ? -r : r);
    }

    public static long Hash(double v)
    {
        const int Bits = 61;
        const ulong Modulus = (1UL << Bits) - 1;
        if (double.IsInfinity(v)) return v > 0 ? 314159 : -314159;
        if (double.IsNaN(v)) return 0;
        double m = Frexp(v, out int e);
        int sign = 1;
        if (m < 0)
        {
            sign = -1;
            m = -m;
        }
        ulong x = 0;
        while (m != 0)
        {
            x = ((x << 28) & Modulus) | x >> (Bits - 28);
            m *= 268435456.0;
            e -= 28;
            ulong y = (ulong)m;
            m -= y;
            x += y;
            if (x >= Modulus) x -= Modulus;
        }
        e = e >= 0 ? e % Bits : Bits - 1 - ((-1 - e) % Bits);
        x = ((x << e) & Modulus) | x >> (Bits - e);
        x = (ulong)((long)x * sign);
        if (x == ulong.MaxValue) x = unchecked((ulong)-2L);
        return (long)x;
    }

    public static double Frexp(double d, out int e)
    {
        if (d == 0 || double.IsNaN(d) || double.IsInfinity(d))
        {
            e = 0;
            return d;
        }
        long bits = BitConverter.DoubleToInt64Bits(d);
        int exp = (int)((bits >> 52) & 0x7FF);
        int adjust = 0;
        if (exp == 0)
        {
            d = Math.ScaleB(d, 54);
            bits = BitConverter.DoubleToInt64Bits(d);
            exp = (int)((bits >> 52) & 0x7FF);
            adjust = 54;
        }
        e = exp - 1022 - adjust;
        bits = (bits & ~(0x7FFL << 52)) | (1022L << 52);
        return BitConverter.Int64BitsToDouble(bits);
    }

    public static bool IsInteger(double d) => !double.IsInfinity(d) && !double.IsNaN(d) && Math.Floor(d) == d;

    /// <summary>Exact comparison of an int with a float. NaN must be handled by the caller.</summary>
    public static int CompareIntFloat(PyInt a, double b)
    {
        if (double.IsPositiveInfinity(b)) return -1;
        if (double.IsNegativeInfinity(b)) return 1;
        if (!a.IsBig && a.V > -(1L << 53) && a.V < (1L << 53)) return ((double)a.V).CompareTo(b);
        double fl = Math.Floor(b);
        var bf = new BigInteger(fl);
        int c = a.ToBig().CompareTo(bf);
        if (c != 0) return c;
        return fl == b ? 0 : -1;
    }

    public static PyInt ToInt(double d)
    {
        if (double.IsNaN(d)) throw PyErr.ValueError("cannot convert float NaN to integer");
        if (double.IsInfinity(d)) throw PyErr.OverflowError("cannot convert float infinity to integer");
        double t = Math.Truncate(d);
        if (t >= -9.2e18 && t <= 9.2e18) return PyInt.From((long)t);
        return PyInt.From(new BigInteger(t));
    }

    public static PyInt Floor(double d) => ToInt(Math.Floor(d));
    public static PyInt Ceil(double d) => ToInt(Math.Ceiling(d));

    /// <summary>Parses a float literal as float() does. Returns false when invalid.</summary>
    public static bool TryParse(string text, out double value)
    {
        value = 0;
        var s = text.Trim(StrType.WhitespaceChars);
        if (s.Length == 0) return false;
        string body = s;
        int sign = 1;
        if (body[0] == '+' || body[0] == '-')
        {
            sign = body[0] == '-' ? -1 : 1;
            body = body.Substring(1);
        }
        var lower = body.ToLowerInvariant();
        if (lower == "inf" || lower == "infinity")
        {
            value = sign * double.PositiveInfinity;
            return true;
        }
        if (lower == "nan")
        {
            value = double.NaN;
            return true;
        }
        if (body.Length == 0) return false;
        // validate characters and underscores
        var sb = new System.Text.StringBuilder(body.Length);
        bool seenDigit = false, seenDot = false, seenExp = false;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c >= '0' && c <= '9')
            {
                sb.Append(c);
                seenDigit = true;
            }
            else if (c == '_')
            {
                if (i == 0 || i == body.Length - 1) return false;
                if (!char.IsAsciiDigit(body[i - 1]) || !char.IsAsciiDigit(body[i + 1])) return false;
            }
            else if (c == '.')
            {
                if (seenDot || seenExp) return false;
                seenDot = true;
                sb.Append(c);
            }
            else if (c == 'e' || c == 'E')
            {
                if (seenExp || !seenDigit) return false;
                seenExp = true;
                sb.Append('e');
                if (i + 1 < body.Length && (body[i + 1] == '+' || body[i + 1] == '-'))
                {
                    sb.Append(body[i + 1]);
                    i++;
                }
                if (i + 1 >= body.Length) return false;
            }
            else if (char.IsDigit(c))
            {
                sb.Append((char)('0' + (int)char.GetNumericValue(c)));
                seenDigit = true;
            }
            else return false;
        }
        if (!seenDigit) return false;
        if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
        value = sign * v;
        return true;
    }
}

/// <summary>Complex arithmetic ported from CPython.</summary>
internal static class ComplexMath
{
    public static PyComplex Div(double ar, double ai, double br, double bi)
    {
        double absBr = Math.Abs(br), absBi = Math.Abs(bi);
        double rr, ri;
        if (absBr >= absBi)
        {
            if (absBr == 0.0) throw PyErr.ZeroDivisionError("complex division by zero");
            double ratio = bi / br;
            double denom = br + bi * ratio;
            rr = (ar + ai * ratio) / denom;
            ri = (ai - ar * ratio) / denom;
        }
        else if (absBi >= absBr)
        {
            double ratio = br / bi;
            double denom = br * ratio + bi;
            rr = (ar * ratio + ai) / denom;
            ri = (ai * ratio - ar) / denom;
        }
        else
        {
            rr = ri = double.NaN;
        }
        return new PyComplex(rr, ri);
    }

    public static PyComplex Pow(double ar, double ai, double br, double bi)
    {
        if (bi == 0 && br == Math.Floor(br) && Math.Abs(br) <= 100)
        {
            int n = (int)br;
            return PowInt(ar, ai, n);
        }
        if (br == 0 && bi == 0) return new PyComplex(1, 0);
        if (ar == 0 && ai == 0)
        {
            if (bi != 0 || br < 0) throw PyErr.ZeroDivisionError("0.0 to a negative or complex power");
            return new PyComplex(0, 0);
        }
        double vabs = HypotExact(ar, ai);
        double len = Math.Pow(vabs, br);
        double at = Math.Atan2(ai, ar);
        double phase = at * br;
        if (bi != 0.0)
        {
            len /= Math.Exp(at * bi);
            phase += bi * Math.Log(vabs);
        }
        var r = new PyComplex(len * Math.Cos(phase), len * Math.Sin(phase));
        if (double.IsInfinity(r.Real) || double.IsInfinity(r.Imag))
            throw PyErr.OverflowError("complex exponentiation");
        return r;
    }

    private static PyComplex PowInt(double ar, double ai, int n)
    {
        if (n > 0) return PowU(ar, ai, n);
        if (n == 0) return new PyComplex(1, 0);
        var p = PowU(ar, ai, -n);
        return Div(1, 0, p.Real, p.Imag);
    }

    private static PyComplex PowU(double xr, double xi, int n)
    {
        double rr = 1, ri = 0, pr = xr, pi = xi;
        int mask = 1;
        while (mask > 0 && n >= mask)
        {
            if ((n & mask) != 0)
            {
                double t = rr * pr - ri * pi;
                ri = rr * pi + ri * pr;
                rr = t;
            }
            mask <<= 1;
            double t2 = pr * pr - pi * pi;
            pi = pr * pi + pi * pr;
            pr = t2;
        }
        return new PyComplex(rr, ri);
    }

    public static double Hypot(double a, double b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        if (a < b) (a, b) = (b, a);
        if (a == 0) return 0;
        double r = b / a;
        return a * Math.Sqrt(1 + r * r);
    }

    public static double Abs(double re, double im)
    {
        if (double.IsInfinity(re) || double.IsInfinity(im)) return double.PositiveInfinity;
        if (double.IsNaN(re) || double.IsNaN(im)) return double.NaN;
        double r = HypotExact(re, im);
        if (double.IsInfinity(r)) throw PyErr.OverflowError("absolute value too large");
        return r;
    }

    public static double HypotExact(double x, double y)
    {
        // A careful hypot: scale to avoid overflow, then refine.
        x = Math.Abs(x);
        y = Math.Abs(y);
        if (x < y) (x, y) = (y, x);
        if (x == 0) return 0;
        if (y == 0) return x;
        int e = Math.ILogB(x);
        double xs = Math.ScaleB(x, -e), ys = Math.ScaleB(y, -e);
        double h = Math.Sqrt(xs * xs + ys * ys);
        // one Newton style correction using fused multiply add
        double hh = h * h;
        double err = Math.FusedMultiplyAdd(-h, h, hh) + (Math.FusedMultiplyAdd(xs, xs, -hh) + ys * ys);
        h += err / (2 * h);
        return Math.ScaleB(h, e);
    }
}
