using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Calcy.Python.Runtime;

namespace Calcy.Python.Modules;

/// <summary>The cmath module.</summary>
internal static class CMathModule
{
    private static (double re, double im) Z(PyObject o)
    {
        if (o is PyComplex c) return (c.Real, c.Imag);
        var m = o.Type.Lookup("__complex__");
        if (m != null && o is PyInstance)
        {
            var r = Ops.CallBound(m, o, Array.Empty<PyObject>());
            if (r is PyComplex rc) return (rc.Real, rc.Imag);
            throw PyErr.TypeError("__complex__ should return a complex object");
        }
        return (Ops.ToDouble(o), 0.0);
    }

    private static PyComplex C(Complex c) => new(c.Real, c.Imaginary);

    private static PyObject Wrap(PyObject[] a, KwArgs? kw, string name, Func<Complex, Complex> fn)
    {
        Arg.Exact(a, kw, 1, name);
        var (re, im) = Z(a[0]);
        var r = fn(new Complex(re, im));
        if ((double.IsNaN(r.Real) || double.IsNaN(r.Imaginary)) && !double.IsNaN(re) && !double.IsNaN(im) && double.IsFinite(re) && double.IsFinite(im))
            throw PyErr.ValueError("math domain error");
        if ((double.IsInfinity(r.Real) || double.IsInfinity(r.Imaginary)) && double.IsFinite(re) && double.IsFinite(im))
            throw PyErr.OverflowError("math range error");
        return C(r);
    }

    public static (double, double) Sqrt(double re, double im)
    {
        if (re == 0 && im == 0) return (0.0, im);
        if (double.IsInfinity(im)) return (double.PositiveInfinity, im);
        if (double.IsNaN(re) || double.IsNaN(im)) return (double.NaN, double.NaN);
        if (double.IsInfinity(re))
            return re > 0 ? (re, Math.CopySign(0.0, im)) : (0.0, Math.CopySign(double.PositiveInfinity, im));
        double ax = Math.Abs(re), ay = Math.Abs(im), s;
        if (ax < double.Epsilon * 4503599627370496.0 && ay < double.Epsilon * 4503599627370496.0)
        {
            ax = Math.ScaleB(ax, 53);
            s = Math.ScaleB(Math.Sqrt(ax + ComplexMath.HypotExact(ax, Math.ScaleB(ay, 53))), -27);
        }
        else
        {
            ax /= 8.0;
            s = 2.0 * Math.Sqrt(ax + ComplexMath.HypotExact(ax, ay / 8.0));
        }
        double d = ay / (2.0 * s);
        return re >= 0 ? (s, Math.CopySign(d, im)) : (d, Math.CopySign(s, im));
    }

    public static PyModule Create()
    {
        var m = new PyModule("cmath", new Namespace(), true);
        m.Add("pi", new PyFloat(Math.PI));
        m.Add("e", new PyFloat(Math.E));
        m.Add("tau", new PyFloat(Math.Tau));
        m.Add("inf", new PyFloat(double.PositiveInfinity));
        m.Add("nan", new PyFloat(double.NaN));
        m.Add("infj", new PyComplex(0, double.PositiveInfinity));
        m.Add("nanj", new PyComplex(0, double.NaN));
        m.AddFunction("phase", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "phase");
            var (re, im) = Z(a[0]);
            return new PyFloat(Math.Atan2(im, re));
        });
        m.AddFunction("polar", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "polar");
            var (re, im) = Z(a[0]);
            return new PyTuple(new PyObject[] { new PyFloat(ComplexMath.Abs(re, im)), new PyFloat(Math.Atan2(im, re)) });
        });
        m.AddFunction("rect", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "rect");
            double r = Ops.ToDouble(a[0]), phi = Ops.ToDouble(a[1]);
            if (phi == 0) return new PyComplex(r, r * phi);
            return new PyComplex(r * Math.Cos(phi), r * Math.Sin(phi));
        });
        m.AddFunction("sqrt", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "sqrt");
            var (re, im) = Z(a[0]);
            var (sr, si) = Sqrt(re, im);
            return new PyComplex(sr, si);
        });
        m.AddFunction("exp", (a, kw) => Wrap(a, kw, "exp", z =>
        {
            if (z.Imaginary == 0) return new Complex(Math.Exp(z.Real), z.Imaginary);
            double l = Math.Exp(z.Real);
            return new Complex(l * Math.Cos(z.Imaginary), l * Math.Sin(z.Imaginary));
        }));
        m.AddFunction("log", (a, kw) =>
        {
            Arg.NoKw(kw, "log");
            Arg.Count(a, 1, 2, "log");
            var (re, im) = Z(a[0]);
            if (re == 0 && im == 0) throw PyErr.ValueError("math domain error");
            var l = new Complex(Math.Log(ComplexMath.Abs(re, im)), Math.Atan2(im, re));
            if (a.Length == 2)
            {
                var (br, bi) = Z(a[1]);
                if (br == 0 && bi == 0) throw PyErr.ValueError("math domain error");
                var lb = new Complex(Math.Log(ComplexMath.Abs(br, bi)), Math.Atan2(bi, br));
                return ComplexMath.Div(l.Real, l.Imaginary, lb.Real, lb.Imaginary);
            }
            return C(l);
        });
        m.AddFunction("log10", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "log10");
            var (re, im) = Z(a[0]);
            if (re == 0 && im == 0) throw PyErr.ValueError("math domain error");
            return new PyComplex(Math.Log10(ComplexMath.Abs(re, im)), Math.Atan2(im, re) / Math.Log(10));
        });
        m.AddFunction("sin", (a, kw) => Wrap(a, kw, "sin", Complex.Sin));
        m.AddFunction("cos", (a, kw) => Wrap(a, kw, "cos", Complex.Cos));
        m.AddFunction("tan", (a, kw) => Wrap(a, kw, "tan", Complex.Tan));
        m.AddFunction("asin", (a, kw) => Wrap(a, kw, "asin", Complex.Asin));
        m.AddFunction("acos", (a, kw) => Wrap(a, kw, "acos", Complex.Acos));
        m.AddFunction("atan", (a, kw) => Wrap(a, kw, "atan", Complex.Atan));
        m.AddFunction("sinh", (a, kw) => Wrap(a, kw, "sinh", Complex.Sinh));
        m.AddFunction("cosh", (a, kw) => Wrap(a, kw, "cosh", Complex.Cosh));
        m.AddFunction("tanh", (a, kw) => Wrap(a, kw, "tanh", Complex.Tanh));
        m.AddFunction("asinh", (a, kw) => Wrap(a, kw, "asinh", z => Complex.Log(z + Complex.Sqrt(z * z + 1))));
        m.AddFunction("acosh", (a, kw) => Wrap(a, kw, "acosh", z => 2 * Complex.Log(Complex.Sqrt((z + 1) / 2) + Complex.Sqrt((z - 1) / 2))));
        m.AddFunction("atanh", (a, kw) => Wrap(a, kw, "atanh", z =>
        {
            if (z == Complex.One || z == -Complex.One) return new Complex(double.NaN, double.NaN);
            return 0.5 * (Complex.Log(1 + z) - Complex.Log(1 - z));
        }));
        m.AddFunction("isfinite", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "isfinite");
            var (re, im) = Z(a[0]);
            return Py.Bool(double.IsFinite(re) && double.IsFinite(im));
        });
        m.AddFunction("isinf", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "isinf");
            var (re, im) = Z(a[0]);
            return Py.Bool(double.IsInfinity(re) || double.IsInfinity(im));
        });
        m.AddFunction("isnan", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "isnan");
            var (re, im) = Z(a[0]);
            return Py.Bool(double.IsNaN(re) || double.IsNaN(im));
        });
        m.AddFunction("isclose", (a, kw) =>
        {
            var b = new Sig("isclose", 2, 2, 2, "a", "b", "rel_tol", "abs_tol").Bind(a, kw);
            var (ar, ai) = Z(b[0]!);
            var (br, bi) = Z(b[1]!);
            double rel = b[2] == null ? 1e-09 : Ops.ToDouble(b[2]!);
            double abs = b[3] == null ? 0.0 : Ops.ToDouble(b[3]!);
            if (rel < 0 || abs < 0) throw PyErr.ValueError("tolerances must be non-negative");
            if (ar == br && ai == bi) return PyBool.True;
            if (double.IsInfinity(ar) || double.IsInfinity(ai) || double.IsInfinity(br) || double.IsInfinity(bi)) return PyBool.False;
            double diff = ComplexMath.Abs(br - ar, bi - ai);
            return Py.Bool(diff <= rel * ComplexMath.Abs(br, bi) || diff <= rel * ComplexMath.Abs(ar, ai) || diff <= abs);
        });
        return m;
    }
}

/// <summary>The time module. Sleeping goes through the host hook and respects cancellation.</summary>
internal static class TimeModule
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static PyModule Create(PythonEngine engine)
    {
        var m = new PyModule("time", new Namespace(), true);
        m.AddFunction("time", (a, kw) => new PyFloat((DateTime.UtcNow - Epoch).Ticks / 1e7));
        m.AddFunction("time_ns", (a, kw) => PyInt.From((DateTime.UtcNow - Epoch).Ticks * 100));
        m.AddFunction("monotonic", (a, kw) => new PyFloat(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency));
        m.AddFunction("monotonic_ns", (a, kw) => PyInt.From((long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency))));
        m.AddFunction("perf_counter", (a, kw) => new PyFloat(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency));
        m.AddFunction("perf_counter_ns", (a, kw) => PyInt.From((long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency))));
        m.AddFunction("process_time", (a, kw) => new PyFloat(Interp.Current.Elapsed.TotalSeconds));
        m.AddFunction("sleep", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "sleep");
            double secs = Ops.ToDouble(a[0]);
            if (double.IsNaN(secs)) throw PyErr.ValueError("Invalid value NaN (not a number)");
            if (secs < 0) throw PyErr.ValueError("sleep length must be non-negative");
            var ctx = Interp.Current;
            var remaining = ctx.RemainingTime;
            var wanted = TimeSpan.FromSeconds(Math.Min(secs, 3.0e7));
            bool truncated = false;
            if (remaining.HasValue && remaining.Value < wanted)
            {
                wanted = remaining.Value < TimeSpan.Zero ? TimeSpan.Zero : remaining.Value;
                truncated = true;
            }
            engine.DoSleep(wanted, ctx.Ct);
            if (truncated) ctx.Steps = Math.Max(ctx.Steps, ctx.NextCheck);
            ctx.Check();
            return Py.None;
        });
        m.AddFunction("ctime", (a, kw) =>
        {
            var t = a.Length > 0 && a[0] != Py.None ? FromSeconds(Ops.ToDouble(a[0])) : DateTime.Now;
            return PyStr.New(t.ToString("ddd MMM ", CultureInfo.InvariantCulture) + t.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2) +
                             t.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture));
        });
        m.AddFunction("localtime", (a, kw) => StructTime(a.Length > 0 && a[0] != Py.None ? FromSeconds(Ops.ToDouble(a[0])) : DateTime.Now));
        m.AddFunction("gmtime", (a, kw) => StructTime(a.Length > 0 && a[0] != Py.None ? FromSeconds(Ops.ToDouble(a[0])).ToUniversalTime() : DateTime.UtcNow));
        m.AddFunction("strftime", (a, kw) =>
        {
            Arg.Count(a, 1, 2, "strftime");
            var fmt = Arg.Str(a[0], "strftime() argument 1");
            DateTime t = DateTime.Now;
            if (a.Length > 1)
            {
                var parts = Ops.ToArray(a[1]);
                if (parts.Length < 6) throw PyErr.TypeError("time tuple must have at least 6 elements");
                t = new DateTime((int)Ops.IndexLong(parts[0]), (int)Ops.IndexLong(parts[1]), (int)Ops.IndexLong(parts[2]),
                    (int)Ops.IndexLong(parts[3]), (int)Ops.IndexLong(parts[4]), (int)Math.Min(59, Ops.IndexLong(parts[5])));
            }
            return PyStr.New(StrFTime(fmt, t));
        });
        return m;
    }

    private static DateTime FromSeconds(double s) => Epoch.AddTicks((long)(s * 1e7)).ToLocalTime();

    private static PyTuple StructTime(DateTime t)
    {
        int wday = ((int)t.DayOfWeek + 6) % 7;
        return new PyTuple(new PyObject[]
        {
            PyInt.From(t.Year), PyInt.From(t.Month), PyInt.From(t.Day), PyInt.From(t.Hour), PyInt.From(t.Minute),
            PyInt.From(t.Second), PyInt.From(wday), PyInt.From(t.DayOfYear), PyInt.From(-1),
        });
    }

    private static string StrFTime(string fmt, DateTime t)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        for (int i = 0; i < fmt.Length; i++)
        {
            char c = fmt[i];
            if (c != '%' || i + 1 >= fmt.Length)
            {
                sb.Append(c);
                continue;
            }
            char d = fmt[++i];
            switch (d)
            {
                case 'Y': sb.Append(t.Year.ToString("0000", inv)); break;
                case 'y': sb.Append((t.Year % 100).ToString("00", inv)); break;
                case 'm': sb.Append(t.Month.ToString("00", inv)); break;
                case 'd': sb.Append(t.Day.ToString("00", inv)); break;
                case 'H': sb.Append(t.Hour.ToString("00", inv)); break;
                case 'I': sb.Append((t.Hour % 12 == 0 ? 12 : t.Hour % 12).ToString("00", inv)); break;
                case 'M': sb.Append(t.Minute.ToString("00", inv)); break;
                case 'S': sb.Append(t.Second.ToString("00", inv)); break;
                case 'p': sb.Append(t.Hour < 12 ? "AM" : "PM"); break;
                case 'A': sb.Append(t.ToString("dddd", inv)); break;
                case 'a': sb.Append(t.ToString("ddd", inv)); break;
                case 'B': sb.Append(t.ToString("MMMM", inv)); break;
                case 'b': sb.Append(t.ToString("MMM", inv)); break;
                case 'j': sb.Append(t.DayOfYear.ToString("000", inv)); break;
                case 'w': sb.Append(((int)t.DayOfWeek).ToString(inv)); break;
                case '%': sb.Append('%'); break;
                default: sb.Append('%').Append(d); break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>The string module.</summary>
internal static class StringModule
{
    public static PyModule Create()
    {
        var m = new PyModule("string", new Namespace(), true);
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string digits = "0123456789";
        const string punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
        const string whitespace = " \t\n\r\x0b\x0c";
        m.Add("ascii_lowercase", PyStr.New(lower));
        m.Add("ascii_uppercase", PyStr.New(upper));
        m.Add("ascii_letters", PyStr.New(lower + upper));
        m.Add("digits", PyStr.New(digits));
        m.Add("hexdigits", PyStr.New("0123456789abcdefABCDEF"));
        m.Add("octdigits", PyStr.New("01234567"));
        m.Add("punctuation", PyStr.New(punctuation));
        m.Add("whitespace", PyStr.New(whitespace));
        m.Add("printable", PyStr.New(digits + lower + upper + punctuation + whitespace));
        m.AddFunction("capwords", (a, kw) =>
        {
            Arg.Count(a, 1, 2, "capwords");
            var s = Arg.Str(a[0], "capwords() argument 1");
            if (a.Length > 1 && a[1] != Py.None)
            {
                var sep = Arg.Str(a[1], "capwords() argument 2");
                var parts = StrType.Split(s, sep, -1);
                return PyStr.New(string.Join(sep, parts.ToArray().Select(p => Capitalize(((PyStr)p).V))));
            }
            var words = StrType.SplitWhitespace(s, -1);
            return PyStr.New(string.Join(" ", words.ToArray().Select(p => Capitalize(((PyStr)p).V))));
        });
        return m;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
}

/// <summary>The operator module (function forms of the operators).</summary>
internal static class OperatorModule
{
    public static PyModule Create()
    {
        var m = new PyModule("operator", new Namespace(), true);
        void Bin(string name, Func<PyObject, PyObject, PyObject> fn) => m.AddFunction(name, (a, kw) =>
        {
            Arg.Exact(a, kw, 2, name);
            return fn(a[0], a[1]);
        });
        void Un(string name, Func<PyObject, PyObject> fn) => m.AddFunction(name, (a, kw) =>
        {
            Arg.Exact(a, kw, 1, name);
            return fn(a[0]);
        });
        Bin("add", Arith.Add);
        Bin("sub", Arith.Sub);
        Bin("mul", Arith.Mul);
        Bin("truediv", Arith.TrueDiv);
        Bin("floordiv", Arith.FloorDiv);
        Bin("mod", Arith.Mod);
        Bin("pow", Arith.Pow);
        Bin("lshift", Arith.LShift);
        Bin("rshift", Arith.RShift);
        Bin("and_", Arith.BitAnd);
        Bin("or_", Arith.BitOr);
        Bin("xor", Arith.BitXor);
        Bin("concat", (x, y) => Arith.Add(x, y));
        Bin("eq", Ops.Eq);
        Bin("ne", Ops.Ne);
        Bin("lt", (x, y) => Ops.RichCompare(x, y, CompareOp.Lt));
        Bin("le", (x, y) => Ops.RichCompare(x, y, CompareOp.Le));
        Bin("gt", (x, y) => Ops.RichCompare(x, y, CompareOp.Gt));
        Bin("ge", (x, y) => Ops.RichCompare(x, y, CompareOp.Ge));
        Bin("is_", (x, y) => Py.Bool(ReferenceEquals(x, y)));
        Bin("is_not", (x, y) => Py.Bool(!ReferenceEquals(x, y)));
        Bin("contains", (x, y) => Py.Bool(Ops.Contains(x, y)));
        Bin("getitem", Ops.GetItem);
        Bin("countOf", (x, y) => PyInt.From(Ops.Iterate(x).Count(v => Ops.EqBool(v, y))));
        Bin("indexOf", (x, y) =>
        {
            long i = 0;
            foreach (var v in Ops.Iterate(x))
            {
                if (Ops.EqBool(v, y)) return PyInt.From(i);
                i++;
            }
            throw PyErr.ValueError("sequence.index(x): x not in sequence");
        });
        Un("neg", Arith.Neg);
        Un("pos", Arith.Pos);
        Un("abs", Arith.Abs);
        Un("invert", Arith.Invert);
        Un("inv", Arith.Invert);
        Un("not_", x => Py.Bool(!Ops.Truthy(x)));
        Un("truth", x => Py.Bool(Ops.Truthy(x)));
        Un("index", x => Ops.Index(x));
        m.AddFunction("setitem", (a, kw) =>
        {
            Arg.Exact(a, kw, 3, "setitem");
            Ops.SetItem(a[0], a[1], a[2]);
            return Py.None;
        });
        m.AddFunction("delitem", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "delitem");
            Ops.DelItem(a[0], a[1]);
            return Py.None;
        });
        m.AddFunction("itemgetter", (a, kw) =>
        {
            Arg.NoKw(kw, "itemgetter");
            if (a.Length == 0) throw PyErr.TypeError("itemgetter expected 1 argument, got 0");
            var items = (PyObject[])a.Clone();
            return new PyBuiltinFunction("itemgetter", (b, kw2) =>
            {
                Arg.Exact(b, kw2, 1, "itemgetter");
                if (items.Length == 1) return Ops.GetItem(b[0], items[0]);
                return new PyTuple(items.Select(i => Ops.GetItem(b[0], i)).ToArray());
            }, "operator");
        });
        m.AddFunction("attrgetter", (a, kw) =>
        {
            Arg.NoKw(kw, "attrgetter");
            if (a.Length == 0) throw PyErr.TypeError("attrgetter expected 1 argument, got 0");
            var names = a.Select(x => Arg.Str(x, "attribute name").Split('.')).ToArray();
            PyObject Get(PyObject o, string[] path)
            {
                foreach (var p in path) o = Ops.GetAttr(o, p);
                return o;
            }
            return new PyBuiltinFunction("attrgetter", (b, kw2) =>
            {
                Arg.Exact(b, kw2, 1, "attrgetter");
                if (names.Length == 1) return Get(b[0], names[0]);
                return new PyTuple(names.Select(n => Get(b[0], n)).ToArray());
            }, "operator");
        });
        m.AddFunction("methodcaller", (a, kw) =>
        {
            if (a.Length == 0) throw PyErr.TypeError("methodcaller needs at least one argument, the method name");
            var name = Arg.Str(a[0], "method name");
            var rest = a.Skip(1).ToArray();
            return new PyBuiltinFunction("methodcaller", (b, kw2) =>
            {
                Arg.Exact(b, kw2, 1, "methodcaller");
                return Ops.GetAttr(b[0], name).Call(rest, kw);
            }, "operator");
        });
        return m;
    }
}

/// <summary>A tiny, safe sys module.</summary>
internal static class SysModule
{
    /// <summary>sys.stdout and sys.stderr: objects with write() and flush() that go to the host output.</summary>
    internal sealed class StdStream : PyObject
    {
        public readonly bool IsError;
        public StdStream(bool isError) { IsError = isError; }
        internal override PyType Type => StreamType;
    }

    private static readonly PyType StreamType = CreateStreamType();

    private static PyType CreateStreamType()
    {
        var t = new PyType("TextIOWrapper", PyTypes.Object, "io", final: true);
        t.Method1("write", (self, s) =>
        {
            var text = Arg.Str(s, "write() argument");
            var engine = Interp.Current.Engine;
            if (((StdStream)self).IsError) engine.WriteError(text);
            else engine.WriteOutput(text);
            return PyInt.From(text.Length);
        });
        t.Method0("flush", self => Py.None);
        return t;
    }

    public static PyModule Create(PythonEngine engine)
    {
        var m = new PyModule("sys", new Namespace(), true);
        m.Add("version", PyStr.New("3.12.0 (Calcy sandboxed Python)"));
        m.Add("version_info", new PyTuple(new PyObject[] { PyInt.From(3), PyInt.From(12), PyInt.From(0), PyStr.New("final"), PyInt.From(0) }));
        m.Add("hexversion", PyInt.From(0x030C00F0));
        m.Add("platform", PyStr.New("calcy"));
        m.Add("implementation_name", PyStr.New("calcy"));
        m.Add("maxsize", PyInt.From(long.MaxValue));
        m.Add("maxunicode", PyInt.From(0x10FFFF));
        m.Add("byteorder", PyStr.New("little"));
        m.Add("argv", new PyList());
        m.Add("stdout", new StdStream(false));
        m.Add("stderr", new StdStream(true));
        m.Add("float_info", new PyTuple(new PyObject[]
        {
            new PyFloat(double.MaxValue), PyInt.From(1024), PyInt.From(308), new PyFloat(2.2250738585072014e-308), PyInt.From(-1021),
            PyInt.From(-307), PyInt.From(15), PyInt.From(53), new PyFloat(2.220446049250313e-16), PyInt.From(2), PyInt.From(1),
        }));
        m.AddFunction("exit", (a, kw) =>
        {
            var args = a.Length == 0 ? PyTuple.Empty : new PyTuple(new[] { a[0] });
            throw new PythonException(new PyBaseException(Exc.SystemExit, args));
        });
        m.AddFunction("getrecursionlimit", (a, kw) => PyInt.From(Interp.Current.MaxDepth));
        m.AddFunction("setrecursionlimit", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "setrecursionlimit");
            long n = Ops.IndexLong(a[0]);
            if (n < 1) throw PyErr.ValueError("recursion limit must be greater or equal than 1");
            var ctx = Interp.Current;
            ctx.MaxDepth = (int)Math.Min(n, Math.Max(10, engine.Options.MaxRecursionDepth));
            return Py.None;
        });
        m.AddFunction("getsizeof", (a, kw) =>
        {
            Arg.Count(a, 1, 2, "getsizeof");
            long size = a[0] switch
            {
                PyList l => 56 + 8L * l.Count,
                PyTuple t => 40 + 8L * t.Count,
                PyStr s => 49 + s.V.Length,
                PyDict d => 64 + 32L * d.Count,
                PyInt i => 28 + 4 * (i.BitLength / 30),
                _ => 48,
            };
            return PyInt.From(size);
        });
        return m;
    }
}
