using System.Numerics;
using Calcy.Python.Runtime;

namespace Calcy.Python.Modules;

/// <summary>The math module (CPython compatible behaviour and error messages).</summary>
internal static class MathModule
{
    private static PythonException Domain() => PyErr.ValueError("math domain error");
    private static PythonException Range() => PyErr.OverflowError("math range error");

    private static double D(PyObject o) => Ops.ToDouble(o);

    /// <summary>Applies a libm style function with CPython's errno style checks.</summary>
    private static PyObject Math1(PyObject[] a, KwArgs? kw, string name, Func<double, double> fn, bool canOverflow)
    {
        Arg.Exact(a, kw, 1, name);
        double x = D(a[0]);
        double r = fn(x);
        if (double.IsNaN(r) && !double.IsNaN(x)) throw Domain();
        if (double.IsInfinity(r) && double.IsFinite(x)) throw canOverflow ? Range() : Domain();
        return new PyFloat(r);
    }

    /// <summary>
    /// Cube root rounded to the nearest double on every platform. Math.Cbrt comes from the C library, and glibc's
    /// is up to 2 units in the last place off (cbrt(27) printed 3.0000000000000004 on Linux).
    /// </summary>
    internal static double Cbrt(double x)
    {
        if (x == 0 || !double.IsFinite(x)) return Math.Cbrt(x);
        double a = Math.Abs(x);
        double r = Math.Cbrt(a);
        // The double within 3 units of r whose cube is closest to a, compared exactly; r comes first so it wins ties.
        var candidates = new List<double> { r };
        double down = r, up = r;
        for (int i = 0; i < 3; i++)
        {
            down = Math.BitDecrement(down);
            up = Math.BitIncrement(up);
            candidates.Add(down);
            candidates.Add(up);
        }
        var (ma, ea) = Split(a);
        int k = ea;
        foreach (var c in candidates) k = Math.Min(k, 3 * Split(c).Exponent);
        double best = r;
        BigInteger bestError = BigInteger.MinusOne;
        foreach (var c in candidates)
        {
            var (mc, ec) = Split(c);
            var error = BigInteger.Abs((BigInteger.Pow(mc, 3) << (3 * ec - k)) - (ma << (ea - k)));
            if (bestError.Sign < 0 || error < bestError)
            {
                best = c;
                bestError = error;
            }
        }
        return Math.CopySign(best, x);
    }

    /// <summary>A positive finite double as mantissa times a power of two.</summary>
    private static (BigInteger Mantissa, int Exponent) Split(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        int exp = (int)((bits >> 52) & 0x7FF);
        long frac = bits & 0xFFFFFFFFFFFFFL;
        return exp == 0 ? (frac, -1074) : (frac | (1L << 52), exp - 1075);
    }

    public static PyModule Create()
    {
        var m = new PyModule("math", new Namespace(), true);
        m.Add("pi", new PyFloat(Math.PI));
        m.Add("e", new PyFloat(Math.E));
        m.Add("tau", new PyFloat(Math.Tau));
        m.Add("inf", new PyFloat(double.PositiveInfinity));
        m.Add("nan", new PyFloat(double.NaN));

        void F1(string name, Func<double, double> fn, bool overflow = false) =>
            m.AddFunction(name, (a, kw) => Math1(a, kw, name, fn, overflow));

        F1("sin", x => double.IsInfinity(x) ? double.NaN : Math.Sin(x));
        F1("cos", x => double.IsInfinity(x) ? double.NaN : Math.Cos(x));
        F1("tan", x => double.IsInfinity(x) ? double.NaN : Math.Tan(x));
        F1("asin", x => x < -1 || x > 1 ? double.NaN : Math.Asin(x));
        F1("acos", x => x < -1 || x > 1 ? double.NaN : Math.Acos(x));
        F1("atan", Math.Atan);
        F1("sinh", Math.Sinh, true);
        F1("cosh", Math.Cosh, true);
        F1("tanh", Math.Tanh);
        F1("asinh", Math.Asinh);
        F1("acosh", x => x < 1 ? double.NaN : Math.Acosh(x));
        F1("atanh", x => x <= -1 || x >= 1 ? double.NaN : Math.Atanh(x));
        F1("exp", Math.Exp, true);
        F1("exp2", x => Math.Pow(2, x), true);
        F1("expm1", Expm1, true);
        F1("log1p", x => x <= -1 ? (x == -1 ? double.NegativeInfinity : double.NaN) : Log1p(x));
        F1("fabs", Math.Abs);
        F1("cbrt", Cbrt);
        F1("erf", SpecialFunctions.Erf);
        F1("erfc", SpecialFunctions.Erfc);
        F1("degrees", x => x * (180.0 / Math.PI));
        F1("radians", x => x * (Math.PI / 180.0));

        m.AddFunction("sqrt", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "sqrt");
            double x = D(a[0]);
            if (x < 0) throw Domain();
            return new PyFloat(Math.Sqrt(x));
        });
        m.AddFunction("gamma", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "gamma");
            double x = D(a[0]);
            double r = SpecialFunctions.Gamma(x, out bool domain, out bool range);
            if (domain) throw Domain();
            if (range) throw Range();
            return new PyFloat(r);
        });
        m.AddFunction("lgamma", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "lgamma");
            double x = D(a[0]);
            double r = SpecialFunctions.LGamma(x, out bool domain, out bool range);
            if (domain) throw Domain();
            if (range) throw Range();
            return new PyFloat(r);
        });
        m.AddFunction("atan2", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "atan2");
            return new PyFloat(Math.Atan2(D(a[0]), D(a[1])));
        });
        m.AddFunction("pow", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "pow");
            double x = D(a[0]), y = D(a[1]);
            double r;
            if (double.IsFinite(x) && double.IsFinite(y))
            {
                if (x == 0 && y < 0) throw Domain();
                if (x < 0 && Math.Floor(y) != y) throw Domain();
                r = Math.Pow(x, y);
                if (double.IsInfinity(r)) throw Range();
            }
            else r = Math.Pow(x, y);
            if (x == 1 || y == 0) r = 1;
            return new PyFloat(r);
        });
        m.AddFunction("log", (a, kw) =>
        {
            Arg.NoKw(kw, "log");
            Arg.Count(a, 1, 2, "log");
            double num = LogHelper(a[0], Math.Log);
            if (a.Length == 1) return new PyFloat(num);
            double den = LogHelper(a[1], Math.Log);
            if (den == 0) throw PyErr.ZeroDivisionError("float division by zero");
            return new PyFloat(num / den);
        });
        m.AddFunction("log2", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "log2");
            return new PyFloat(LogHelper(a[0], Math.Log2));
        });
        m.AddFunction("log10", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "log10");
            return new PyFloat(LogHelper(a[0], Math.Log10));
        });
        // A subclass of int or float that defines the hook in Python uses it; otherwise its builtin value counts.
        static PyObject? SubclassHook(ref PyObject x, string name)
        {
            if (!x.IsBuiltinSubclass) return null;
            if (Ops.UserMethodOf(x.Type, name) is { } hook) return Ops.CallBound(hook, x, Array.Empty<PyObject>());
            x = Subclassing.Plain(x);
            return null;
        }

        m.AddFunction("ceil", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "ceil");
            var x = a[0];
            if (SubclassHook(ref x, "__ceil__") is { } hooked) return hooked;
            if (x is PyInt i) return i is PyBool ? PyInt.From(i.V) : i;
            if (x is PyFloat f) return FloatMath.Ceil(f.V);
            var mm = x.Type.Lookup("__ceil__");
            if (mm != null) return Ops.CallBound(mm, x, Array.Empty<PyObject>());
            return FloatMath.Ceil(D(x));
        });
        m.AddFunction("floor", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "floor");
            var x = a[0];
            if (SubclassHook(ref x, "__floor__") is { } hooked) return hooked;
            if (x is PyInt i) return i is PyBool ? PyInt.From(i.V) : i;
            if (x is PyFloat f) return FloatMath.Floor(f.V);
            var mm = x.Type.Lookup("__floor__");
            if (mm != null) return Ops.CallBound(mm, x, Array.Empty<PyObject>());
            return FloatMath.Floor(D(x));
        });
        m.AddFunction("trunc", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "trunc");
            var x = a[0];
            if (SubclassHook(ref x, "__trunc__") is { } hooked) return hooked;
            if (x is PyInt i) return i is PyBool ? PyInt.From(i.V) : i;
            if (x is PyFloat f) return FloatMath.ToInt(f.V);
            var mm = x.Type.Lookup("__trunc__");
            if (mm != null) return Ops.CallBound(mm, x, Array.Empty<PyObject>());
            throw PyErr.TypeError($"type {x.Type.Name} doesn't define __trunc__ method");
        });
        m.AddFunction("factorial", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "factorial");
            if (a[0] is PyFloat) throw PyErr.TypeError("'float' object cannot be interpreted as an integer");
            var n = Ops.Index(a[0]);
            if (n.Sign < 0) throw PyErr.ValueError("factorial() not defined for negative values");
            if (n.IsBig) Limits.CheckIntBits(long.MaxValue);
            return PyInt.From(Factorial(n.V));
        });
        m.AddFunction("gcd", (a, kw) =>
        {
            Arg.NoKw(kw, "gcd");
            BigInteger g = 0;
            foreach (var x in a) g = BigInteger.GreatestCommonDivisor(g, Ops.Index(x).ToBig());
            return PyInt.From(g);
        });
        m.AddFunction("lcm", (a, kw) =>
        {
            Arg.NoKw(kw, "lcm");
            BigInteger l = 1;
            foreach (var x in a)
            {
                var v = BigInteger.Abs(Ops.Index(x).ToBig());
                if (v.IsZero) return PyInt.Zero;
                l = l / BigInteger.GreatestCommonDivisor(l, v) * v;
                Limits.CheckBig(l);
            }
            return PyInt.From(l);
        });
        m.AddFunction("isqrt", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "isqrt");
            return PyInt.From(IntMath.ISqrt(Ops.Index(a[0]).ToBig()));
        });
        m.AddFunction("comb", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "comb");
            var n = Ops.Index(a[0]).ToBig();
            var k = Ops.Index(a[1]).ToBig();
            if (n.Sign < 0) throw PyErr.ValueError("n must be a non-negative integer");
            if (k.Sign < 0) throw PyErr.ValueError("k must be a non-negative integer");
            if (k > n) return PyInt.Zero;
            if (n - k < k) k = n - k;
            if (k > int.MaxValue) Limits.CheckIntBits(long.MaxValue);
            BigInteger r = 1;
            int kk = (int)k;
            for (int i = 1; i <= kk; i++)
            {
                r = r * (n - kk + i) / i;
                if ((i & 63) == 0)
                {
                    Limits.CheckBig(r);
                    Interp.CurrentOrNull?.Tick();
                }
            }
            return PyInt.From(r);
        });
        m.AddFunction("perm", (a, kw) =>
        {
            Arg.NoKw(kw, "perm");
            Arg.Count(a, 1, 2, "perm");
            var n = Ops.Index(a[0]).ToBig();
            if (n.Sign < 0) throw PyErr.ValueError("n must be a non-negative integer");
            if (a.Length == 1 || a[1] == Py.None)
            {
                if (n > int.MaxValue) Limits.CheckIntBits(long.MaxValue);
                return PyInt.From(Factorial((long)n));
            }
            var k = Ops.Index(a[1]).ToBig();
            if (k.Sign < 0) throw PyErr.ValueError("k must be a non-negative integer");
            if (k > n) return PyInt.Zero;
            if (k > int.MaxValue) Limits.CheckIntBits(long.MaxValue);
            BigInteger r = 1;
            for (long i = 0; i < (long)k; i++)
            {
                r *= n - i;
                if ((i & 63) == 0)
                {
                    Limits.CheckBig(r);
                    Interp.CurrentOrNull?.Tick();
                }
            }
            return PyInt.From(r);
        });
        m.AddFunction("prod", (a, kw) =>
        {
            var b = new Sig("prod", 1, 1, 1, "iterable", "start").Bind(a, kw);
            PyObject r = b[1] ?? PyInt.One;
            foreach (var x in Ops.Iterate(b[0]!)) r = Arith.Mul(r, x);
            return r;
        });
        m.AddFunction("fsum", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "fsum");
            return new PyFloat(FSum(Ops.Iterate(a[0]).Select(D)));
        });
        m.AddFunction("sumprod", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "sumprod");
            var p = Ops.ToArray(a[0]);
            var q = Ops.ToArray(a[1]);
            if (p.Length != q.Length) throw PyErr.ValueError("Inputs are not the same length");
            PyObject total = PyInt.Zero;
            bool allFloat = p.Concat(q).All(x => x is PyFloat or PyInt) && p.Concat(q).Any(x => x is PyFloat);
            if (allFloat) return new PyFloat(FSum(p.Zip(q).Select(t => D(t.First) * D(t.Second))));
            for (int i = 0; i < p.Length; i++) total = Arith.Add(total, Arith.Mul(p[i], q[i]));
            return total;
        });
        m.AddFunction("hypot", (a, kw) =>
        {
            Arg.NoKw(kw, "hypot");
            return new PyFloat(Hypot(a.Select(D).ToArray()));
        });
        m.AddFunction("dist", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "dist");
            var p = Ops.ToArray(a[0]).Select(D).ToArray();
            var q = Ops.ToArray(a[1]).Select(D).ToArray();
            if (p.Length != q.Length) throw PyErr.ValueError("both points must have the same number of dimensions");
            return new PyFloat(Hypot(p.Zip(q).Select(t => t.First - t.Second).ToArray()));
        });
        m.AddFunction("isclose", (a, kw) =>
        {
            var b = new Sig("isclose", 2, 2, 2, "a", "b", "rel_tol", "abs_tol").Bind(a, kw);
            double x = D(b[0]!), y = D(b[1]!);
            double rel = b[2] == null ? 1e-09 : D(b[2]!);
            double abs = b[3] == null ? 0.0 : D(b[3]!);
            if (rel < 0 || abs < 0) throw PyErr.ValueError("tolerances must be non-negative");
            if (x == y) return PyBool.True;
            if (double.IsInfinity(x) || double.IsInfinity(y)) return PyBool.False;
            double diff = Math.Abs(y - x);
            return Py.Bool(diff <= Math.Abs(rel * y) || diff <= Math.Abs(rel * x) || diff <= abs);
        });
        m.AddFunction("isfinite", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "isfinite");
            return Py.Bool(double.IsFinite(D(a[0])));
        });
        m.AddFunction("isinf", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "isinf");
            return Py.Bool(double.IsInfinity(D(a[0])));
        });
        m.AddFunction("isnan", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "isnan");
            return Py.Bool(double.IsNaN(D(a[0])));
        });
        m.AddFunction("copysign", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "copysign");
            return new PyFloat(Math.CopySign(D(a[0]), D(a[1])));
        });
        m.AddFunction("fmod", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "fmod");
            double x = D(a[0]), y = D(a[1]);
            if (double.IsInfinity(y) && double.IsFinite(x)) return new PyFloat(x);
            if (y == 0 || double.IsInfinity(x)) throw Domain();
            return new PyFloat(x % y);
        });
        m.AddFunction("remainder", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "remainder");
            double x = D(a[0]), y = D(a[1]);
            if (double.IsNaN(x) || double.IsNaN(y)) return new PyFloat(double.NaN);
            if (double.IsInfinity(x) || y == 0) throw Domain();
            if (double.IsInfinity(y)) return new PyFloat(x);
            return new PyFloat(Math.IEEERemainder(x, y));
        });
        m.AddFunction("modf", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "modf");
            double x = D(a[0]);
            if (double.IsInfinity(x)) return new PyTuple(new PyObject[] { new PyFloat(Math.CopySign(0.0, x)), new PyFloat(x) });
            double ip = Math.Truncate(x);
            return new PyTuple(new PyObject[] { new PyFloat(Math.CopySign(x - ip, x)), new PyFloat(ip) });
        });
        m.AddFunction("frexp", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "frexp");
            double x = D(a[0]);
            double mm = FloatMath.Frexp(x, out int e);
            return new PyTuple(new PyObject[] { new PyFloat(mm), PyInt.From(e) });
        });
        m.AddFunction("ldexp", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "ldexp");
            double x = D(a[0]);
            if (a[1] is not PyInt) throw PyErr.TypeError("Expected an int as second argument to ldexp.");
            var e = Ops.Index(a[1]);
            long ev = e.IsBig ? (e.Sign > 0 ? 100000 : -100000) : Math.Clamp(e.V, -100000, 100000);
            double r = Math.ScaleB(x, (int)ev);
            if (double.IsInfinity(r) && double.IsFinite(x)) throw Range();
            return new PyFloat(r);
        });
        m.AddFunction("nextafter", (a, kw) =>
        {
            var b = new Sig("nextafter", 2, 2, 2, "x", "y", "steps").Bind(a, kw);
            double x = D(b[0]!), y = D(b[1]!);
            long steps = b[2] == null || b[2] == Py.None ? 1 : Ops.IndexLong(b[2]!);
            if (steps < 0) throw PyErr.ValueError("steps must be a non-negative integer");
            for (long i = 0; i < steps && x != y; i++)
            {
                if (double.IsNaN(x) || double.IsNaN(y)) return new PyFloat(double.NaN);
                x = y > x ? Math.BitIncrement(x) : Math.BitDecrement(x);
            }
            if (steps > 0 && x == y) x = y;
            return new PyFloat(x);
        });
        m.AddFunction("ulp", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "ulp");
            double x = Math.Abs(D(a[0]));
            if (double.IsNaN(x) || double.IsInfinity(x)) return new PyFloat(x);
            if (x == double.MaxValue) return new PyFloat(x - Math.BitDecrement(x));
            return new PyFloat(Math.BitIncrement(x) - x);
        });
        return m;
    }

    private static double Expm1(double x)
    {
        if (Math.Abs(x) > 0.7) return Math.Exp(x) - 1.0;
        double u = Math.Exp(x);
        if (u == 1.0) return x;
        double um1 = u - 1.0;
        if (um1 == -1.0) return -1.0;
        return um1 * x / Math.Log(u);
    }

    private static double Log1p(double x)
    {
        // accurate log1p: log(1+x) * x / ((1+x)-1)
        double u = 1.0 + x;
        if (u == 1.0) return x;
        return Math.Log(u) * x / (u - 1.0);
    }

    private static double LogHelper(PyObject o, Func<double, double> fn)
    {
        if (o is PyInt i)
        {
            if (i.Sign <= 0) throw Domain();
            if (i.IsBig)
            {
                var big = i.Big;
                long bits = (long)big.GetBitLength();
                if (bits > 1000)
                {
                    int shift = (int)(bits - 64);
                    double mant = (double)(ulong)(big >> shift);
                    return fn(mant) + fn(2.0) * shift;
                }
            }
            return fn(IntMath.ToDouble(i));
        }
        double x = Ops.ToDouble(o);
        if (double.IsNaN(x)) return x;
        if (x <= 0) throw Domain();
        return fn(x);
    }

    public static BigInteger Factorial(long n)
    {
        if (n < 2) return BigInteger.One;
        if (n < 21)
        {
            long r = 1;
            for (long i = 2; i <= n; i++) r *= i;
            return r;
        }
        // estimate bits: log2(n!) ~ n log2 n - n log2 e
        double est = n * (Math.Log2(n) - 1.4426950408889634) + 0.5 * Math.Log2(2 * Math.PI * n);
        if (est > Limits.MaxIntBits) Limits.CheckIntBits((long)est);
        return Product(2, n);
    }

    private static BigInteger Product(long lo, long hi)
    {
        if (hi - lo < 16)
        {
            BigInteger r = BigInteger.One;
            for (long i = lo; i <= hi; i++) r *= i;
            return r;
        }
        Interp.CurrentOrNull?.Tick();
        long mid = (lo + hi) / 2;
        return Product(lo, mid) * Product(mid + 1, hi);
    }

    /// <summary>Port of CPython math.fsum (Shewchuk's exact summation).</summary>
    public static double FSum(IEnumerable<double> values)
    {
        var p = new List<double>();
        double specialSum = 0.0, infSum = 0.0;
        double lo = 0.0;
        var ctx = Interp.CurrentOrNull;
        foreach (var v in values)
        {
            ctx?.Tick();
            double x = v;
            double xsave = x;
            int i = 0;
            for (int j = 0; j < p.Count; j++)
            {
                double y = p[j];
                if (Math.Abs(x) < Math.Abs(y)) (x, y) = (y, x);
                double hi2 = x + y;
                lo = y - (hi2 - x);
                if (lo != 0.0) p[i++] = lo;
                x = hi2;
            }
            p.RemoveRange(i, p.Count - i);
            if (x != 0.0)
            {
                if (!double.IsFinite(x))
                {
                    if (double.IsFinite(xsave)) throw PyErr.OverflowError("intermediate overflow in fsum");
                    if (double.IsInfinity(xsave)) infSum += xsave;
                    specialSum += xsave;
                    p.Clear();
                }
                else p.Add(x);
            }
        }
        if (specialSum != 0.0)
        {
            if (double.IsNaN(infSum)) throw PyErr.ValueError("-inf + inf in fsum");
            return specialSum;
        }
        double hi = 0.0;
        int n = p.Count;
        if (n > 0)
        {
            hi = p[--n];
            while (n > 0)
            {
                double x = hi;
                double y = p[--n];
                hi = x + y;
                double yr = hi - x;
                lo = y - yr;
                if (lo != 0.0) break;
            }
            if (n > 0 && ((lo < 0.0 && p[n - 1] < 0.0) || (lo > 0.0 && p[n - 1] > 0.0)))
            {
                double y = lo * 2.0;
                double x = hi + y;
                double yr = x - hi;
                if (y == yr) hi = x;
            }
        }
        return hi;
    }

    public static double Hypot(double[] coords)
    {
        double max = 0;
        bool nan = false;
        foreach (var c in coords)
        {
            double ac = Math.Abs(c);
            if (double.IsInfinity(ac)) return double.PositiveInfinity;
            if (double.IsNaN(ac)) nan = true;
            else if (ac > max) max = ac;
        }
        if (nan) return double.NaN;
        if (max == 0) return 0.0;
        if (coords.Length == 2) return ComplexMath.HypotExact(coords[0], coords[1]);
        int e = Math.ILogB(max);
        double sum = 0, comp = 0;
        foreach (var c in coords)
        {
            double s = Math.ScaleB(c, -e);
            double sq = s * s;
            double err = Math.FusedMultiplyAdd(s, s, -sq);
            double t = sum + sq;
            if (Math.Abs(sum) >= Math.Abs(sq)) comp += (sum - t) + sq;
            else comp += (sq - t) + sum;
            comp += err;
            sum = t;
        }
        double total = sum + comp;
        double h = Math.Sqrt(total);
        double hh = h * h;
        double resid = (sum - hh) + comp - Math.FusedMultiplyAdd(h, h, -hh);
        h += resid / (2 * h);
        return Math.ScaleB(h, e);
    }
}

/// <summary>Special functions ported from CPython (gamma, lgamma) and fdlibm (erf, erfc).</summary>
internal static class SpecialFunctions
{
    private const int LanczosN = 13;
    private const double LanczosG = 6.024680040776729583740234375;
    private const double LanczosGMinusHalf = 5.524680040776729583740234375;
    private const double LogPi = 1.144729885849400174143427351353058711647;

    private static readonly double[] LanczosNum =
    {
        23531376880.410759688572007674451636754734846804940,
        42919803642.649098768957899047001988850926355848959,
        35711959237.355668049440185451547166705960488635843,
        17921034426.037209699919755754458931112671403265390,
        6039542586.3520280050642916443072979210699388420708,
        1439720407.3117216736632230727949123939715485786772,
        248874557.86205415651146038641322942321632125127801,
        31426415.585400194380614231628318205362874684987640,
        2876370.6289353724412254090516208496135991145378768,
        186056.26539522349504029498971604569928220784236328,
        8071.6720023658162106380029022722506138218516325024,
        210.82427775157934587250973392071336271166969580291,
        2.5066282746310002701649081771338373386264310793408,
    };

    private static readonly double[] LanczosDen =
    {
        0.0, 39916800.0, 120543840.0, 150917976.0, 105258076.0, 45995730.0,
        13339535.0, 2637558.0, 357423.0, 32670.0, 1925.0, 66.0, 1.0,
    };

    private static readonly double[] GammaIntegral =
    {
        1.0, 1.0, 2.0, 6.0, 24.0, 120.0, 720.0, 5040.0, 40320.0, 362880.0,
        3628800.0, 39916800.0, 479001600.0, 6227020800.0, 87178291200.0,
        1307674368000.0, 20922789888000.0, 355687428096000.0,
        6402373705728000.0, 121645100408832000.0, 2432902008176640000.0,
        51090942171709440000.0, 1124000727777607680000.0,
    };

    private static double LanczosSum(double x)
    {
        double num = 0.0, den = 0.0;
        if (x < 5.0)
        {
            for (int i = LanczosN; --i >= 0;)
            {
                num = num * x + LanczosNum[i];
                den = den * x + LanczosDen[i];
            }
        }
        else
        {
            for (int i = 0; i < LanczosN; i++)
            {
                num = num / x + LanczosNum[i];
                den = den / x + LanczosDen[i];
            }
        }
        return num / den;
    }

    private static double SinPi(double x)
    {
        double y = Math.Abs(x) % 2.0;
        int n = (int)Math.Round(2.0 * y, MidpointRounding.AwayFromZero);
        double r = n switch
        {
            0 => Math.Sin(Math.PI * y),
            1 => Math.Cos(Math.PI * (y - 0.5)),
            2 => Math.Sin(Math.PI * (1.0 - y)),
            3 => -Math.Cos(Math.PI * (y - 1.5)),
            _ => Math.Sin(Math.PI * (y - 2.0)),
        };
        return Math.CopySign(1.0, x) * r;
    }

    public static double Gamma(double x, out bool domain, out bool range)
    {
        domain = range = false;
        if (!double.IsFinite(x))
        {
            if (double.IsNaN(x) || x > 0.0) return x;
            domain = true;
            return double.NaN;
        }
        if (x == 0.0)
        {
            domain = true;
            return Math.CopySign(double.PositiveInfinity, x);
        }
        if (x == Math.Floor(x))
        {
            if (x < 0.0)
            {
                domain = true;
                return double.NaN;
            }
            if (x <= GammaIntegral.Length) return GammaIntegral[(int)x - 1];
        }
        double absx = Math.Abs(x);
        if (absx < 1e-20)
        {
            double r0 = 1.0 / x;
            if (double.IsInfinity(r0)) range = true;
            return r0;
        }
        if (absx > 200.0)
        {
            if (x < 0.0) return 0.0 / SinPi(x);
            range = true;
            return double.PositiveInfinity;
        }
        double y = absx + LanczosGMinusHalf;
        double z;
        if (absx > LanczosGMinusHalf)
        {
            double q = y - absx;
            z = q - LanczosGMinusHalf;
        }
        else
        {
            double q = y - LanczosGMinusHalf;
            z = q - absx;
        }
        z = z * LanczosG / y;
        double r;
        if (x < 0.0)
        {
            r = -Math.PI / SinPi(absx) / absx * Math.Exp(y) / LanczosSum(absx);
            r -= z * r;
            if (absx < 140.0) r /= Math.Pow(y, absx - 0.5);
            else
            {
                double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                r /= sqrtpow;
                r /= sqrtpow;
            }
        }
        else
        {
            r = LanczosSum(absx) / Math.Exp(y);
            r += z * r;
            if (absx < 140.0) r *= Math.Pow(y, absx - 0.5);
            else
            {
                double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                r *= sqrtpow;
                r *= sqrtpow;
            }
        }
        if (double.IsInfinity(r)) range = true;
        return r;
    }

    public static double LGamma(double x, out bool domain, out bool range)
    {
        domain = range = false;
        if (!double.IsFinite(x))
        {
            if (double.IsNaN(x)) return x;
            return double.PositiveInfinity;
        }
        if (x == Math.Floor(x) && x <= 2.0)
        {
            if (x <= 0.0)
            {
                domain = true;
                return double.PositiveInfinity;
            }
            return 0.0;
        }
        double absx = Math.Abs(x);
        if (absx < 1e-20) return -Math.Log(absx);
        double r = Math.Log(LanczosSum(absx)) - LanczosG;
        r += (absx - 0.5) * (Math.Log(absx + LanczosG - 0.5) - 1);
        if (x < 0.0) r = LogPi - Math.Log(Math.Abs(SinPi(absx))) - Math.Log(absx) - r;
        if (double.IsInfinity(r)) range = true;
        return r;
    }

    private const double Erx = 8.45062911510467529297e-01;
    private const double Efx = 1.28379167095512586316e-01;
    private const double Efx8 = 1.02703333676410069053e+00;
    private const double Pp0 = 1.28379167095512558561e-01;
    private const double Pp1 = -3.25042107247001499370e-01;
    private const double Pp2 = -2.84817495755985104766e-02;
    private const double Pp3 = -5.77027029648944159157e-03;
    private const double Pp4 = -2.37630166566501626084e-05;
    private const double Qq1 = 3.97917223959155352819e-01;
    private const double Qq2 = 6.50222499887672944485e-02;
    private const double Qq3 = 5.08130628187576562776e-03;
    private const double Qq4 = 1.32494738004321644526e-04;
    private const double Qq5 = -3.96022827877536812320e-06;
    private const double Pa0 = -2.36211856075265944077e-03;
    private const double Pa1 = 4.14856118683748331666e-01;
    private const double Pa2 = -3.72207876035701323847e-01;
    private const double Pa3 = 3.18346619901161753674e-01;
    private const double Pa4 = -1.10894694282396677476e-01;
    private const double Pa5 = 3.54783043256182359371e-02;
    private const double Pa6 = -2.16637559486879084300e-03;
    private const double Qa1 = 1.06420880400844228286e-01;
    private const double Qa2 = 5.40397917702171048937e-01;
    private const double Qa3 = 7.18286544141962662868e-02;
    private const double Qa4 = 1.26171219808761642112e-01;
    private const double Qa5 = 1.36370839120290507362e-02;
    private const double Qa6 = 1.19844998467991074170e-02;
    private const double Ra0 = -9.86494403484714822705e-03;
    private const double Ra1 = -6.93858572707181764372e-01;
    private const double Ra2 = -1.05586262253232909814e+01;
    private const double Ra3 = -6.23753324503260060396e+01;
    private const double Ra4 = -1.62396669462573470355e+02;
    private const double Ra5 = -1.84605092906711035994e+02;
    private const double Ra6 = -8.12874355063065934246e+01;
    private const double Ra7 = -9.81432934416914548592e+00;
    private const double Sa1 = 1.96512716674392571292e+01;
    private const double Sa2 = 1.37657754143519042600e+02;
    private const double Sa3 = 4.34565877475229228821e+02;
    private const double Sa4 = 6.45387271733267880336e+02;
    private const double Sa5 = 4.29008140027567833386e+02;
    private const double Sa6 = 1.08635005541779435134e+02;
    private const double Sa7 = 6.57024977031928170135e+00;
    private const double Sa8 = -6.04244152148580987438e-02;
    private const double Rb0 = -9.86494292470009928597e-03;
    private const double Rb1 = -7.99283237680523006574e-01;
    private const double Rb2 = -1.77579549177547519889e+01;
    private const double Rb3 = -1.60636384855821916062e+02;
    private const double Rb4 = -6.37566443368389627722e+02;
    private const double Rb5 = -1.02509513161107724954e+03;
    private const double Rb6 = -4.83519191608651397019e+02;
    private const double Sb1 = 3.03380607434824582924e+01;
    private const double Sb2 = 3.25792512996573918826e+02;
    private const double Sb3 = 1.53672958608443695994e+03;
    private const double Sb4 = 3.19985821950859553908e+03;
    private const double Sb5 = 2.55305040643316442583e+03;
    private const double Sb6 = 4.74528541206955367215e+02;
    private const double Sb7 = -2.24409524465858183362e+01;

    private static int High(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);

    private static double ClearLow(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) & unchecked((long)0xFFFFFFFF00000000UL));

    public static double Erf(double x)
    {
        int hx = High(x);
        int ix = hx & 0x7fffffff;
        if (ix >= 0x7ff00000)
        {
            int i = (int)(((uint)hx >> 31) << 1);
            return (1 - i) + 1.0 / x;
        }
        double s, z, r, y, P, Q, R, S;
        if (ix < 0x3feb0000)
        {
            if (ix < 0x3e300000)
            {
                if (ix < 0x00800000) return 0.125 * (8.0 * x + Efx8 * x);
                return x + Efx * x;
            }
            z = x * x;
            r = Pp0 + z * (Pp1 + z * (Pp2 + z * (Pp3 + z * Pp4)));
            s = 1.0 + z * (Qq1 + z * (Qq2 + z * (Qq3 + z * (Qq4 + z * Qq5))));
            y = r / s;
            return x + x * y;
        }
        if (ix < 0x3ff40000)
        {
            s = Math.Abs(x) - 1.0;
            P = Pa0 + s * (Pa1 + s * (Pa2 + s * (Pa3 + s * (Pa4 + s * (Pa5 + s * Pa6)))));
            Q = 1.0 + s * (Qa1 + s * (Qa2 + s * (Qa3 + s * (Qa4 + s * (Qa5 + s * Qa6)))));
            return hx >= 0 ? Erx + P / Q : -Erx - P / Q;
        }
        if (ix >= 0x40180000) return hx >= 0 ? 1.0 - 1e-300 : 1e-300 - 1.0;
        x = Math.Abs(x);
        s = 1.0 / (x * x);
        if (ix < 0x4006DB6E)
        {
            R = Ra0 + s * (Ra1 + s * (Ra2 + s * (Ra3 + s * (Ra4 + s * (Ra5 + s * (Ra6 + s * Ra7))))));
            S = 1.0 + s * (Sa1 + s * (Sa2 + s * (Sa3 + s * (Sa4 + s * (Sa5 + s * (Sa6 + s * (Sa7 + s * Sa8)))))));
        }
        else
        {
            R = Rb0 + s * (Rb1 + s * (Rb2 + s * (Rb3 + s * (Rb4 + s * (Rb5 + s * Rb6)))));
            S = 1.0 + s * (Sb1 + s * (Sb2 + s * (Sb3 + s * (Sb4 + s * (Sb5 + s * (Sb6 + s * Sb7))))));
        }
        z = ClearLow(x);
        r = Math.Exp(-z * z - 0.5625) * Math.Exp((z - x) * (z + x) + R / S);
        return hx >= 0 ? 1.0 - r / x : r / x - 1.0;
    }

    public static double Erfc(double x)
    {
        int hx = High(x);
        int ix = hx & 0x7fffffff;
        if (ix >= 0x7ff00000) return (double)(((uint)hx >> 31) << 1) + 1.0 / x;
        double s, z, r, y, P, Q, R, S;
        if (ix < 0x3feb0000)
        {
            if (ix < 0x3c700000) return 1.0 - x;
            z = x * x;
            r = Pp0 + z * (Pp1 + z * (Pp2 + z * (Pp3 + z * Pp4)));
            s = 1.0 + z * (Qq1 + z * (Qq2 + z * (Qq3 + z * (Qq4 + z * Qq5))));
            y = r / s;
            if (hx < 0x3fd00000) return 1.0 - (x + x * y);
            r = x * y;
            r += x - 0.5;
            return 0.5 - r;
        }
        if (ix < 0x3ff40000)
        {
            s = Math.Abs(x) - 1.0;
            P = Pa0 + s * (Pa1 + s * (Pa2 + s * (Pa3 + s * (Pa4 + s * (Pa5 + s * Pa6)))));
            Q = 1.0 + s * (Qa1 + s * (Qa2 + s * (Qa3 + s * (Qa4 + s * (Qa5 + s * Qa6)))));
            if (hx >= 0) return 1.0 - Erx - P / Q;
            return 1.0 + (Erx + P / Q);
        }
        if (ix < 0x403c0000)
        {
            x = Math.Abs(x);
            s = 1.0 / (x * x);
            if (ix < 0x4006DB6D)
            {
                R = Ra0 + s * (Ra1 + s * (Ra2 + s * (Ra3 + s * (Ra4 + s * (Ra5 + s * (Ra6 + s * Ra7))))));
                S = 1.0 + s * (Sa1 + s * (Sa2 + s * (Sa3 + s * (Sa4 + s * (Sa5 + s * (Sa6 + s * (Sa7 + s * Sa8)))))));
            }
            else
            {
                if (hx < 0 && ix >= 0x40180000) return 2.0 - 1e-300;
                R = Rb0 + s * (Rb1 + s * (Rb2 + s * (Rb3 + s * (Rb4 + s * (Rb5 + s * Rb6)))));
                S = 1.0 + s * (Sb1 + s * (Sb2 + s * (Sb3 + s * (Sb4 + s * (Sb5 + s * (Sb6 + s * Sb7))))));
            }
            z = ClearLow(x);
            r = Math.Exp(-z * z - 0.5625) * Math.Exp((z - x) * (z + x) + R / S);
            return hx > 0 ? r / x : 2.0 - r / x;
        }
        return hx > 0 ? 1e-300 * 1e-300 : 2.0 - 1e-300;
    }
}
