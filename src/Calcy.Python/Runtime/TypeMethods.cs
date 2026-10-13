using Calcy.Python.Modules;
using System.Globalization;
using System.Numerics;

namespace Calcy.Python.Runtime;

internal static class ObjectType
{
    public static PyBuiltinMethod InitMethod = null!;

    public static void Register(PyType t)
    {
        t.Ctor = (type, a, kw) =>
        {
            if (a.Length > 0 || (kw != null && kw.Count > 0)) throw PyErr.TypeError("object() takes no arguments");
            return new PyInstance(PyTypes.Object);
        };
        InitMethod = t.Method("__init__", (self, a, kw) =>
        {
            if (a.Length > 0 || (kw != null && kw.Count > 0))
                throw PyErr.TypeError("object.__init__() takes exactly one argument (the instance to initialize)");
            return Py.None;
        });
        t.Dict["__new__"] = new PyBuiltinMethod("__new__", PyTypes.Type, (cls, a, kw) =>
        {
            if (cls is not PyType c) throw PyErr.TypeError($"object.__new__(X): X is not a type object ({cls.Type.Name})");
            var root = c.BuiltinBase ?? (c.IsBuiltin && c != PyTypes.Object && c.Layout != LayoutKind.Exception ? c : null);
            if (root != null) throw PyErr.TypeError($"object.__new__({c.Name}) is not safe, use {(c.Lookup("__new__") is PyBuiltinMethod ? c.Name : root.Name)}.__new__()");
            return PyType.CreateRaw(c, a, kw);
        });
        t.Method0("__repr__", self => PyStr.New(Ops.DefaultRepr(self)));
        t.Method0("__str__", self => PyStr.New(Ops.Repr(self)));
        t.Method1("__eq__", (self, o) => ReferenceEquals(self, o) ? PyBool.True : Py.NotImplemented);
        t.Method1("__ne__", (self, o) =>
        {
            var r = Ops.Eq(self, o);
            return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r));
        });
        t.Method0("__hash__", self => PyInt.From(Ops.Id(self) >> 4));
        t.Method1("__format__", (self, spec) =>
        {
            if (Arg.Str(spec, "format spec").Length > 0)
                throw PyErr.TypeError($"unsupported format string passed to {self.Type.Name}.__format__");
            return PyStr.New(Ops.Str(self));
        });
        t.Method2("__setattr__", (self, n, v) =>
        {
            Ops.GenericSetAttr(self, Arg.Str(n, "attribute name"), v);
            return Py.None;
        });
        t.Method1("__getattribute__", (self, n) => Ops.GetAttr(self, Arg.Str(n, "attribute name")));
        t.Method1("__delattr__", (self, n) =>
        {
            var name = Arg.Str(n, "attribute name");
            if (self is PyInstance inst)
            {
                if (!inst.Dict.RemoveStr(name)) throw Ops.NoAttribute(self, name);
            }
            else Ops.DelAttr(self, name);
            return Py.None;
        });
        t.Method0("__dir__", self => Builtins.Dir(self));
        t.Dict["__init_subclass__"] = new PyBuiltinClassMethod("__init_subclass__", t, (cls, a, kw) => Py.None);
        t.Dict["__doc__"] = PyStr.New("The base class of the class hierarchy.");
    }
}

internal static class TypeType
{
    public static void Register(PyType t)
    {
        t.Ctor = (type, a, kw) =>
        {
            if (a.Length == 1 && (kw == null || kw.Count == 0)) return a[0].Type;
            if (a.Length != 3) throw PyErr.TypeError("type() takes 1 or 3 arguments");
            var name = Arg.Str(a[0], "type.__new__() argument 1");
            if (a[1] is not PyTuple bt) throw PyErr.TypeError("type.__new__() argument 2 must be tuple");
            if (a[2] is not PyDict nd) throw PyErr.TypeError("type.__new__() argument 3 must be dict");
            var bases = bt.Items.Select(b => b as PyType ?? throw PyErr.TypeError("bases must be types")).ToArray();
            var ns = new Dictionary<string, PyObject>(StringComparer.Ordinal);
            foreach (var (k, v) in nd.Pairs()) ns[Arg.Str(k, "attribute name")] = v;
            return PyType.CreateClass(name, name, "__main__", bases, ns);
        };
        t.Method0("mro", self => new PyList(((PyType)self).Mro.Cast<PyObject>()));
    }
}

internal static class IntType
{
    private static readonly Sig IntSig = new("int", 0, 1, 2, "x", "base");

    public static void Register()
    {
        var t = PyTypes.Int;
        t.Ctor = (type, a, kw) =>
        {
            var b = IntSig.Bind(a, kw);
            if (b[0] == null)
            {
                if (b[1] != null) throw PyErr.TypeError("int() missing string argument");
                return PyInt.Zero;
            }
            var x = b[0]!;
            if (b[1] != null)
            {
                long radix = Arg.Index(b[1]!);
                if (radix != 0 && (radix < 2 || radix > 36)) throw PyErr.ValueError("int() base must be >= 2 and <= 36, or 0");
                if (x is not PyStr xs) throw PyErr.TypeError("int() can't convert non-string with explicit base");
                return IntMath.Parse(xs.V, (int)radix) ?? throw PyErr.ValueError($"invalid literal for int() with base {radix}: {PyStr.Repr(xs.V)}");
            }
            if (x is PyStr s && !(s.IsBuiltinSubclass && Ops.UserIntConversion(s.Type, out _) != null))
                return IntMath.Parse(s.V, 10) ?? throw PyErr.ValueError($"invalid literal for int() with base 10: {PyStr.Repr(s.V)}");
            return Ops.ToInt(x);
        };
        t.Method0("bit_length", self => PyInt.From(((PyInt)self).BitLength));
        t.Method0("bit_count", self =>
        {
            var v = BigInteger.Abs(((PyInt)self).ToBig());
            long c = 0;
            foreach (var by in v.ToByteArray()) c += BitOperations.PopCount(by);
            return PyInt.From(c);
        });
        t.Method0("conjugate", self => Plain(self));
        t.Method0("as_integer_ratio", self => new PyTuple(new PyObject[] { Plain(self), PyInt.One }));
        t.Method0("is_integer", self => PyBool.True);
        t.Method0("__index__", self => Plain(self));
        t.Method0("__int__", self => Plain(self));
        t.Method0("__float__", self => new PyFloat(IntMath.ToDouble((PyInt)self)));
        t.Method0("__abs__", self => IntMath.Abs((PyInt)self));
        t.Method0("__neg__", self => IntMath.Neg((PyInt)self));
        t.Method0("__hash__", self => PyInt.From(Ops.HashBase(self)));
        t.Method0("__repr__", self => PyStr.New(Ops.ReprBase(self)));
        t.Method1("__format__", (self, spec) => PyStr.New(Formatter.FormatBase(self, Arg.Str(spec, "format spec"))));
        t.Method("to_bytes", (self, a, kw) => throw PyErr.NotImplementedError("bytes are not supported in this interpreter"));
        t.Dict["real"] = new PyGetSet("real", Plain);
        t.Dict["imag"] = new PyGetSet("imag", _ => PyInt.Zero);
        t.Dict["numerator"] = new PyGetSet("numerator", Plain);
        t.Dict["denominator"] = new PyGetSet("denominator", _ => PyInt.One);

        var b = PyTypes.Bool;
        b.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "bool");
            Arg.Count(a, 0, 1, "bool");
            return a.Length == 0 ? PyBool.False : Py.Bool(Ops.Truthy(a[0]));
        };
    }

    private static PyObject Plain(PyObject self) => self is PyBool pb ? PyInt.From(pb.V) : Subclassing.Plain(self);
}

internal static class FloatType
{
    public static void Register()
    {
        var t = PyTypes.Float;
        t.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "float");
            Arg.Count(a, 0, 1, "float");
            if (a.Length == 0) return PyFloat.Zero;
            var x = a[0];
            if (x.GetType() == typeof(PyFloat)) return x;
            if (x.IsBuiltinSubclass && Ops.UserMethodOf(x.Type, "__float__") is { } uf)
            {
                var r = Ops.CallBound(uf, x, Array.Empty<PyObject>());
                if (r is PyFloat rf) return new PyFloat(rf.V);
                throw PyErr.TypeError($"{x.Type.Name}.__float__ returned non-float (type {r.Type.Name})");
            }
            if (x is PyFloat xf) return new PyFloat(xf.V);
            if (x is PyStr s)
            {
                if (FloatMath.TryParse(s.V, out var d)) return new PyFloat(d);
                throw PyErr.ValueError($"could not convert string to float: {PyStr.Repr(s.V)}");
            }
            if (x is not (PyInt or PyFloat) && x.Type.Lookup("__float__") == null && x.Type.Lookup("__index__") == null)
                throw PyErr.TypeError($"float() argument must be a string or a real number, not '{x.Type.Name}'");
            return new PyFloat(Ops.ToDouble(x));
        };
        t.Method0("is_integer", self => Py.Bool(FloatMath.IsInteger(((PyFloat)self).V)));
        t.Method0("as_integer_ratio", self => AsIntegerRatio(((PyFloat)self).V));
        t.Method0("conjugate", self => Subclassing.Plain(self));
        t.Method0("__int__", self => FloatMath.ToInt(((PyFloat)self).V));
        t.Method0("__trunc__", self => FloatMath.ToInt(((PyFloat)self).V));
        t.Method0("__floor__", self => FloatMath.Floor(((PyFloat)self).V));
        t.Method0("__ceil__", self => FloatMath.Ceil(((PyFloat)self).V));
        t.Method0("__float__", self => Subclassing.Plain(self));
        t.Method0("__abs__", self => new PyFloat(Math.Abs(((PyFloat)self).V)));
        t.Method0("__neg__", self => new PyFloat(-((PyFloat)self).V));
        t.Method0("__hash__", self => PyInt.From(Ops.HashBase(self)));
        t.Method0("__repr__", self => PyStr.New(Ops.ReprBase(self)));
        t.Method1("__format__", (self, spec) => PyStr.New(Formatter.FormatBase(self, Arg.Str(spec, "format spec"))));
        t.Method0("hex", self => PyStr.New(Hex(((PyFloat)self).V)));
        t.Dict["real"] = new PyGetSet("real", Subclassing.Plain);
        t.Dict["imag"] = new PyGetSet("imag", _ => PyFloat.Zero);
    }

    public static PyObject AsIntegerRatio(double d)
    {
        if (double.IsInfinity(d)) throw PyErr.OverflowError("cannot convert Infinity to integer ratio");
        if (double.IsNaN(d)) throw PyErr.ValueError("cannot convert NaN to integer ratio");
        double m = FloatMath.Frexp(d, out int e);
        for (int i = 0; i < 300 && m != Math.Floor(m); i++)
        {
            m *= 2.0;
            e--;
        }
        var num = new BigInteger(m);
        var den = BigInteger.One;
        if (e > 0) num <<= e;
        else den <<= -e;
        return new PyTuple(new PyObject[] { PyInt.From(num), PyInt.From(den) });
    }

    private static string Hex(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        if (d == 0) return double.IsNegative(d) ? "-0x0.0p+0" : "0x0.0p+0";
        string sign = d < 0 ? "-" : "";
        d = Math.Abs(d);
        long bits = BitConverter.DoubleToInt64Bits(d);
        int exp = (int)((bits >> 52) & 0x7FF);
        long mant = bits & 0xFFFFFFFFFFFFFL;
        int lead = 1;
        if (exp == 0)
        {
            lead = 0;
            exp = -1022;
        }
        else exp -= 1023;
        return $"{sign}0x{lead}.{mant:x13}p{(exp >= 0 ? "+" : "")}{exp}";
    }
}

internal static class ComplexType
{
    private static readonly Sig ComplexSig = new("complex", 0, 0, 2, "real", "imag");

    public static void Register()
    {
        var t = PyTypes.Complex;
        t.Ctor = (type, a, kw) =>
        {
            var b = ComplexSig.Bind(a, kw);
            if (b[0] == null && b[1] == null) return new PyComplex(0, 0);
            if (b[0] is PyStr s)
            {
                if (b[1] != null) throw PyErr.TypeError("complex() can't take second arg if first is a string");
                return Parse(s.V) ?? throw PyErr.ValueError("complex() arg is a malformed string");
            }
            double re = 0, im = 0;
            if (b[0] != null)
            {
                if (b[0] is PyComplex c)
                {
                    re = c.Real;
                    im = c.Imag;
                }
                else re = Ops.ToDouble(b[0]!);
            }
            if (b[1] != null)
            {
                if (b[1] is PyComplex c2)
                {
                    re -= c2.Imag;
                    im += c2.Real;
                }
                else im += Ops.ToDouble(b[1]!);
            }
            return new PyComplex(re, im);
        };
        t.Method0("conjugate", self => new PyComplex(((PyComplex)self).Real, -((PyComplex)self).Imag));
        t.Method0("__abs__", self => new PyFloat(ComplexMath.Abs(((PyComplex)self).Real, ((PyComplex)self).Imag)));
        t.Method0("__hash__", self => PyInt.From(Ops.HashBase(self)));
        t.Method1("__format__", (self, spec) => PyStr.New(Formatter.FormatBase(self, Arg.Str(spec, "format spec"))));
        t.Dict["real"] = new PyGetSet("real", s => new PyFloat(((PyComplex)s).Real));
        t.Dict["imag"] = new PyGetSet("imag", s => new PyFloat(((PyComplex)s).Imag));
    }

    public static PyComplex? Parse(string text)
    {
        var s = text.Trim(StrType.WhitespaceChars);
        if (s.StartsWith('(') && s.EndsWith(')')) s = s.Substring(1, s.Length - 2).Trim();
        if (s.Length == 0) return null;
        if (FloatMath.TryParse(s, out var only)) return new PyComplex(only, 0);
        char last = s[^1];
        if (last != 'j' && last != 'J') return null;
        var body = s.Substring(0, s.Length - 1);
        int split = -1;
        for (int i = body.Length - 1; i > 0; i--)
        {
            if ((body[i] == '+' || body[i] == '-') && body[i - 1] != 'e' && body[i - 1] != 'E')
            {
                split = i;
                break;
            }
        }
        double re = 0, im;
        string imPart = body;
        if (split > 0)
        {
            if (!FloatMath.TryParse(body.Substring(0, split), out re)) return null;
            imPart = body.Substring(split);
        }
        if (imPart is "" or "+") im = 1;
        else if (imPart == "-") im = -1;
        else if (!FloatMath.TryParse(imPart, out im)) return null;
        return new PyComplex(re, im);
    }
}

internal static class ListType
{
    private static readonly Sig SortSig = new("sort", 0, 0, 0, "key", "reverse");

    private static PyList L(PyObject o) => (PyList)o;

    public static void Register()
    {
        var t = PyTypes.List;
        t.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "list");
            Arg.Count(a, 0, 1, "list");
            var l = new PyList();
            if (a.Length == 1) l.Extend(a[0]);
            return l;
        };
        t.Method1("append", (self, x) =>
        {
            L(self).Append(x);
            return Py.None;
        });
        t.Method1("extend", (self, x) =>
        {
            L(self).Extend(x);
            return Py.None;
        });
        t.Method2("insert", (self, i, x) =>
        {
            L(self).Insert(Arg.Index(i), x);
            return Py.None;
        });
        t.Method("pop", (self, a, kw) =>
        {
            Arg.NoKw(kw, "pop");
            Arg.Count(a, 0, 1, "pop");
            var l = L(self);
            if (l.Count == 0) throw PyErr.IndexError("pop from empty list");
            long i = a.Length == 0 ? l.Count - 1 : Arg.Index(a[0]);
            if (i < 0) i += l.Count;
            if (i < 0 || i >= l.Count) throw PyErr.IndexError("pop index out of range");
            var v = l.Items[i];
            l.RemoveAt((int)i);
            return v;
        });
        t.Method1("remove", (self, x) =>
        {
            var l = L(self);
            for (int i = 0; i < l.Count; i++)
            {
                if (Ops.EqBool(l.Items[i], x))
                {
                    l.RemoveAt(i);
                    return Py.None;
                }
            }
            throw PyErr.ValueError("list.remove(x): x not in list");
        });
        t.Method("index", (self, a, kw) =>
        {
            Arg.NoKw(kw, "index");
            Arg.Count(a, 1, 3, "index");
            var l = L(self);
            StrType.StartEnd(a, 1, l.Count, out int start, out int end);
            for (int i = start; i < Math.Min(end, l.Count); i++)
                if (Ops.EqBool(l.Items[i], a[0])) return PyInt.From(i);
            throw PyErr.ValueError($"{Ops.Repr(a[0])} is not in list");
        });
        t.Method1("count", (self, x) =>
        {
            var l = L(self);
            long c = 0;
            for (int i = 0; i < l.Count; i++)
                if (Ops.EqBool(l.Items[i], x)) c++;
            return PyInt.From(c);
        });
        t.Method("sort", (self, a, kw) =>
        {
            var b = SortSig.Bind(a, kw);
            L(self).Sort(b[0], b[1] != null && Ops.Truthy(b[1]!));
            return Py.None;
        });
        t.Method0("reverse", self =>
        {
            L(self).Reverse();
            return Py.None;
        });
        t.Method0("copy", self => PyList.Wrap(L(self).ToArray()));
        t.Method0("clear", self =>
        {
            L(self).Clear();
            return Py.None;
        });
        t.Method0("__len__", self => PyInt.From(L(self).Count));
        t.Method1("__getitem__", (self, k) => Ops.GetItemBase(self, k));
        t.Method2("__setitem__", (self, k, v) =>
        {
            Ops.SetItemBase(self, k, v);
            return Py.None;
        });
        t.Method1("__delitem__", (self, k) =>
        {
            Ops.DelItemBase(self, k);
            return Py.None;
        });
        t.Method1("__contains__", (self, x) => Py.Bool(Ops.ContainsBase(self, x)));
        t.Method0("__iter__", self => new ListIterator(L(self)));
        t.Method0("__reversed__", self => new ListReverseIterator(L(self)));
        t.Method0("__repr__", self => PyStr.New(Ops.ReprBase(self)));
        t.Method("__init__", (self, a, kw) =>
        {
            Arg.NoKw(kw, "list");
            Arg.Count(a, 0, 1, "list");
            var l = L(self);
            var items = a.Length == 1 ? Ops.ToArray(a[0]) : Array.Empty<PyObject>();
            l.Clear();
            foreach (var v in items) l.Append(v);
            return Py.None;
        });
        t.Dict["__hash__"] = Py.None;
    }
}

internal static class TupleType
{
    public static void Register()
    {
        var t = PyTypes.Tuple;
        t.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "tuple");
            Arg.Count(a, 0, 1, "tuple");
            if (a.Length == 0) return PyTuple.Empty;
            if (a[0].GetType() == typeof(PyTuple)) return a[0];
            var arr = Ops.ToArray(a[0]);
            return arr.Length == 0 ? PyTuple.Empty : new PyTuple(arr);
        };
        t.Method1("count", (self, x) =>
        {
            long c = 0;
            foreach (var v in ((PyTuple)self).Items)
                if (Ops.EqBool(v, x)) c++;
            return PyInt.From(c);
        });
        t.Method("index", (self, a, kw) =>
        {
            Arg.NoKw(kw, "index");
            Arg.Count(a, 1, 3, "index");
            var items = ((PyTuple)self).Items;
            StrType.StartEnd(a, 1, items.Length, out int start, out int end);
            for (int i = start; i < Math.Min(end, items.Length); i++)
                if (Ops.EqBool(items[i], a[0])) return PyInt.From(i);
            throw PyErr.ValueError("tuple.index(x): x not in tuple");
        });
        t.Method0("__len__", self => PyInt.From(((PyTuple)self).Count));
        t.Method1("__getitem__", (self, k) => Ops.GetItemBase(self, k));
        t.Method1("__contains__", (self, x) => Py.Bool(Ops.ContainsBase(self, x)));
        t.Method0("__iter__", self => new ArrayIterator(((PyTuple)self).Items, PyTypes.TupleIterator));
        t.Method0("__hash__", self => PyInt.From(Ops.HashBase(self)));
        t.Method0("__repr__", self => PyStr.New(Ops.ReprBase(self)));
    }
}

internal static class DictType
{
    private static PyDict D(PyObject o) => (PyDict)o;

    public static void Register()
    {
        var t = PyTypes.Dict;
        t.Ctor = (type, a, kw) =>
        {
            Arg.Count(a, 0, 1, "dict");
            var d = new PyDict();
            if (a.Length == 1) d.Update(a[0]);
            if (kw != null)
                for (int i = 0; i < kw.Count; i++) d.SetItem(PyStr.New(kw.Names[i]), kw.Values[i]);
            return d;
        };
        RegisterMethods(t);
        t.ClassMethod("fromkeys", (cls, a, kw) =>
        {
            Arg.NoKw(kw, "fromkeys");
            Arg.Count(a, 1, 2, "fromkeys");
            var value = a.Length > 1 ? a[1] : Py.None;
            var d = (PyDict)cls.Call(Array.Empty<PyObject>(), null);
            foreach (var k in Ops.Iterate(a[0])) d.SetItem(k, value);
            return d;
        });

        foreach (var vt in new[] { PyTypes.DictKeys, PyTypes.DictValues, PyTypes.DictItems })
        {
            vt.Method0("__len__", self => PyInt.From(((PyDictView)self).Dict.Count));
            vt.Method0("__iter__", self => new DictIterator(((PyDictView)self).Dict, ((PyDictView)self).Kind));
            vt.Method0("__reversed__", self => new DictIterator(((PyDictView)self).Dict, ((PyDictView)self).Kind, reverse: true));
            vt.Method1("__contains__", (self, x) => Py.Bool(Ops.Contains(self, x)));
        }
        foreach (var vt in new[] { PyTypes.DictKeys, PyTypes.DictItems })
            vt.Method1("isdisjoint", (self, other) =>
            {
                foreach (var x in Ops.Iterate(other))
                    if (Ops.Contains(self, x)) return PyBool.False;
                return PyBool.True;
            });
    }

    /// <summary>Registers the dict methods; also used for dict subclasses implemented in C#.</summary>
    public static void RegisterMethods(PyType t)
    {
        t.Method0("keys", self => new PyDictView(D(self), DictViewKind.Keys));
        t.Method0("values", self => new PyDictView(D(self), DictViewKind.Values));
        t.Method0("items", self => new PyDictView(D(self), DictViewKind.Items));
        t.Method("get", (self, a, kw) =>
        {
            Arg.NoKw(kw, "get");
            Arg.Count(a, 1, 2, "get");
            return D(self).GetOrNull(a[0]) ?? (a.Length > 1 ? a[1] : Py.None);
        });
        t.Method("pop", (self, a, kw) =>
        {
            Arg.NoKw(kw, "pop");
            Arg.Count(a, 1, 2, "pop");
            if (D(self).Remove(a[0], out var v)) return v!;
            if (a.Length > 1) return a[1];
            throw PyErr.KeyError(a[0]);
        });
        t.Method0("popitem", self =>
        {
            var (k, v) = D(self).PopLast();
            return new PyTuple(new[] { k, v });
        });
        t.Method("update", (self, a, kw) =>
        {
            Arg.Count(a, 0, 1, "update");
            var d = D(self);
            if (a.Length == 1) d.Update(a[0]);
            if (kw != null)
                for (int i = 0; i < kw.Count; i++) d.SetItem(PyStr.New(kw.Names[i]), kw.Values[i]);
            return Py.None;
        });
        t.Method("setdefault", (self, a, kw) =>
        {
            Arg.NoKw(kw, "setdefault");
            Arg.Count(a, 1, 2, "setdefault");
            return D(self).SetDefault(a[0], a.Length > 1 ? a[1] : Py.None);
        });
        t.Method0("copy", self =>
        {
            var src = D(self);
            if (src is PyCounter) return CollectionsModule.CopyCounter((PyCounter)src);
            if (src is PyDefaultDict dd) return CollectionsModule.CopyDefaultDict(dd);
            return src.Copy();
        });
        t.Method0("clear", self =>
        {
            D(self).Clear();
            return Py.None;
        });
        t.Method0("__len__", self => PyInt.From(D(self).Count));
        t.Method1("__getitem__", (self, k) => Ops.GetItemBase(self, k));
        t.Method("__init__", (self, a, kw) =>
        {
            Arg.Count(a, 0, 1, "dict");
            var d = D(self);
            if (a.Length == 1) d.Update(a[0]);
            if (kw != null)
                for (int i = 0; i < kw.Count; i++) d.SetItem(PyStr.New(kw.Names[i]), kw.Values[i]);
            return Py.None;
        });
        t.Method0("__repr__", self => PyStr.New(Ops.DictRepr(D(self), false)));
        t.Method2("__setitem__", (self, k, v) =>
        {
            D(self).SetItem(k, v);
            return Py.None;
        });
        t.Method1("__delitem__", (self, k) =>
        {
            if (!D(self).Remove(k)) throw PyErr.KeyError(k);
            return Py.None;
        });
        t.Method1("__contains__", (self, k) => Py.Bool(D(self).ContainsKey(k)));
        t.Method0("__iter__", self => new DictIterator(D(self), DictViewKind.Keys));
        t.Method0("__reversed__", self => new DictIterator(D(self), DictViewKind.Keys, reverse: true));
        t.Dict["__hash__"] = Py.None;
    }
}

internal static class SetType
{
    private static SetTable ToTable(PyObject o)
    {
        if (o is PySetBase s) return s.Table;
        var t = new SetTable();
        t.Update(o);
        return t;
    }

    public static void Register()
    {
        var s = PyTypes.Set;
        s.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "set");
            Arg.Count(a, 0, 1, "set");
            var r = new PySet();
            if (a.Length == 1) r.Table.Update(a[0]);
            return r;
        };
        var f = PyTypes.FrozenSet;
        f.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "frozenset");
            Arg.Count(a, 0, 1, "frozenset");
            if (a.Length == 0) return PyFrozenSet.Empty;
            if (a[0] is PyFrozenSet) return a[0];
            var t = new SetTable();
            t.Update(a[0]);
            return new PyFrozenSet(t);
        };
        foreach (var t in new[] { s, f })
        {
            t.Method0("copy", self => self is PyFrozenSet ? self : new PySet(((PySetBase)self).Table.Copy()));
            t.Method("union", (self, a, kw) =>
            {
                var r = ((PySetBase)self).NewSame(((PySetBase)self).Table.Copy());
                foreach (var o in a) r.Table.Update(o);
                return r;
            });
            t.Method("intersection", (self, a, kw) =>
            {
                PyObject r = ((PySetBase)self).NewSame(((PySetBase)self).Table.Copy());
                foreach (var o in a) r = Arith.SetIntersection((PySetBase)r, new PySet(ToTable(o)));
                return r;
            });
            t.Method("difference", (self, a, kw) =>
            {
                var r = ((PySetBase)self).NewSame(((PySetBase)self).Table.Copy());
                foreach (var o in a)
                    foreach (var k in Ops.Iterate(o).ToList()) r.Table.Discard(k);
                return r;
            });
            t.Method1("symmetric_difference", (self, o) => Arith.SetSymDiff((PySetBase)self, new PySet(ToTable(o))));
            t.Method1("issubset", (self, o) => Py.Bool(Ops.IsSubset(((PySetBase)self).Table, ToTable(o))));
            t.Method1("issuperset", (self, o) => Py.Bool(Ops.IsSubset(ToTable(o), ((PySetBase)self).Table)));
            t.Method1("isdisjoint", (self, o) =>
            {
                var st = ((PySetBase)self).Table;
                foreach (var x in Ops.Iterate(o))
                    if (st.Contains(x)) return PyBool.False;
                return PyBool.True;
            });
            t.Method0("__len__", self => PyInt.From(((PySetBase)self).Count));
            t.Method1("__contains__", (self, x) => Py.Bool(((PySetBase)self).Table.Contains(x)));
            t.Method0("__iter__", self => new SetIterator(((PySetBase)self).Table));
        }
        s.Method1("add", (self, x) =>
        {
            var st = ((PySet)self).Table;
            st.Add(x);
            if (st.Count > Limits.MaxCollection) Limits.CheckSize(st.Count);
            return Py.None;
        });
        s.Method1("remove", (self, x) =>
        {
            if (!((PySet)self).Table.Discard(x)) throw PyErr.KeyError(x);
            return Py.None;
        });
        s.Method1("discard", (self, x) =>
        {
            ((PySet)self).Table.Discard(x);
            return Py.None;
        });
        s.Method0("pop", self => ((PySet)self).Table.Pop());
        s.Method0("clear", self =>
        {
            ((PySet)self).Table.Clear();
            return Py.None;
        });
        s.Method("update", (self, a, kw) =>
        {
            foreach (var o in a) ((PySet)self).Table.Update(o);
            return Py.None;
        });
        s.Method("intersection_update", (self, a, kw) =>
        {
            var st = (PySet)self;
            foreach (var o in a) st.Table = ((PySetBase)Arith.SetIntersection(st, new PySet(ToTable(o)))).Table;
            return Py.None;
        });
        s.Method("difference_update", (self, a, kw) =>
        {
            var st = (PySet)self;
            foreach (var o in a)
                foreach (var k in Ops.Iterate(o).ToList()) st.Table.Discard(k);
            return Py.None;
        });
        s.Method1("symmetric_difference_update", (self, o) =>
        {
            var st = (PySet)self;
            foreach (var k in new PySet(ToTable(o)).Table.Keys().ToList())
                if (!st.Table.Discard(k)) st.Table.Add(k);
            return Py.None;
        });
        s.Dict["__hash__"] = Py.None;
        f.Method0("__hash__", self => PyInt.From(Ops.Hash(self)));
    }
}

internal static class RangeType
{
    public static void Register()
    {
        var t = PyTypes.Range;
        t.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "range");
            if (a.Length == 0) throw PyErr.TypeError("range expected at least 1 argument, got 0");
            if (a.Length > 3) throw PyErr.TypeError($"range expected at most 3 arguments, got {a.Length}");
            long start = 0, stop, step = 1;
            if (a.Length == 1) stop = ToLong(a[0]);
            else
            {
                start = ToLong(a[0]);
                stop = ToLong(a[1]);
                if (a.Length == 3) step = ToLong(a[2]);
            }
            return new PyRange(start, stop, step);
        };
        t.Method1("count", (self, x) => PyInt.From(Ops.Contains(self, x) ? 1 : 0));
        t.Method1("index", (self, x) =>
        {
            var r = (PyRange)self;
            if (x is PyInt xi && !xi.IsBig && r.ContainsValue(xi.V)) return PyInt.From((xi.V - r.Start) / r.Step);
            throw PyErr.ValueError($"{Ops.Repr(x)} is not in range");
        });
        t.Dict["start"] = new PyGetSet("start", s => PyInt.From(((PyRange)s).Start));
        t.Dict["stop"] = new PyGetSet("stop", s => PyInt.From(((PyRange)s).Stop));
        t.Dict["step"] = new PyGetSet("step", s => PyInt.From(((PyRange)s).Step));
        t.Method0("__len__", self => PyInt.From(((PyRange)self).Length));
        t.Method0("__iter__", self => new RangeIterator((PyRange)self));
        t.Method0("__reversed__", self =>
        {
            var r = (PyRange)self;
            return new RangeIterator(r.Start + (r.Length - 1) * r.Step, r.Length, -r.Step);
        });
        t.Method1("__contains__", (self, x) => Py.Bool(Ops.Contains(self, x)));
        t.Method1("__getitem__", (self, k) => Ops.GetItem(self, k));
    }

    private static long ToLong(PyObject o)
    {
        var i = Ops.Index(o);
        if (i.IsBig) throw PyErr.OverflowError("Python int too large to convert to C ssize_t");
        return i.V;
    }
}

internal static class MiscTypes
{
    private static readonly Sig PropertySig = new("property", 0, 0, 4, "fget", "fset", "fdel", "doc");

    public static void Register()
    {
        var sl = PyTypes.Slice;
        sl.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "slice");
            Arg.Count(a, 1, 3, "slice");
            if (a.Length == 1) return new PySlice(Py.None, a[0], Py.None);
            return new PySlice(a[0], a[1], a.Length > 2 ? a[2] : Py.None);
        };
        sl.Dict["start"] = new PyGetSet("start", s => ((PySlice)s).Start);
        sl.Dict["stop"] = new PyGetSet("stop", s => ((PySlice)s).Stop);
        sl.Dict["step"] = new PyGetSet("step", s => ((PySlice)s).Step);
        sl.Method1("indices", (self, len) =>
        {
            ((PySlice)self).Indices(Arg.Index(len), out long st, out long sp, out long stp, out _);
            return new PyTuple(new PyObject[] { PyInt.From(st), PyInt.From(sp), PyInt.From(stp) });
        });

        var p = PyTypes.Property;
        p.Ctor = (type, a, kw) =>
        {
            var b = PropertySig.Bind(a, kw);
            var fget = b[0] ?? Py.None;
            var doc = b[3] ?? (fget is PyFunction ff ? ff.Doc : Py.None);
            return new PyProperty(fget, b[1] ?? Py.None, b[2] ?? Py.None, doc);
        };
        p.Method1("getter", (self, fn) => self is PyProperty pr ? new PyProperty(fn, pr.FSet, pr.FDel, pr.Doc) : self);
        p.Method1("setter", (self, fn) => self is PyProperty pr ? new PyProperty(pr.FGet, fn, pr.FDel, pr.Doc) : self);
        p.Method1("deleter", (self, fn) => self is PyProperty pr ? new PyProperty(pr.FGet, pr.FSet, fn, pr.Doc) : self);
        p.Dict["fget"] = new PyGetSet("fget", s => s is PyProperty pr ? pr.FGet : Py.None);
        p.Dict["fset"] = new PyGetSet("fset", s => s is PyProperty pr ? pr.FSet : Py.None);
        p.Dict["fdel"] = new PyGetSet("fdel", s => s is PyProperty pr ? pr.FDel : Py.None);

        PyTypes.StaticMethod.Ctor = (type, a, kw) =>
        {
            Arg.Exact(a, kw, 1, "staticmethod");
            return new PyStaticMethod(a[0]);
        };
        PyTypes.ClassMethod.Ctor = (type, a, kw) =>
        {
            Arg.Exact(a, kw, 1, "classmethod");
            return new PyClassMethod(a[0]);
        };
        PyTypes.Super.Ctor = (type, a, kw) => Builtins.Super(a);
        PyTypes.NoneType.Ctor = (type, a, kw) => Py.None;

        var g = PyTypes.Generator;
        g.Method1("send", (self, v) => ((PyGenerator)self).Send(v));
        g.Method("throw", (self, a, kw) =>
        {
            Arg.Count(a, 1, 3, "throw");
            var exc = Builtins.MakeException(a[0], a.Length > 1 ? a[1] : null);
            return ((PyGenerator)self).Throw(exc);
        });
        g.Method0("close", self =>
        {
            ((PyGenerator)self).Close();
            return Py.None;
        });

        PyTypes.Map.Ctor = (type, a, kw) =>
        {
            Arg.NoKw(kw, "map");
            if (a.Length < 2) throw PyErr.TypeError("map() must have at least two arguments.");
            var its = new PyIterator[a.Length - 1];
            for (int i = 1; i < a.Length; i++) its[i - 1] = Ops.GetIterator(a[i]);
            return new MapIterator(a[0], its);
        };
        PyTypes.Filter.Ctor = (type, a, kw) =>
        {
            Arg.Exact(a, kw, 2, "filter");
            return new FilterIterator(a[0], Ops.GetIterator(a[1]));
        };
        PyTypes.Zip.Ctor = (type, a, kw) =>
        {
            bool strict = false;
            if (kw != null)
            {
                foreach (var n in kw.Names)
                    if (n != "strict") throw PyErr.TypeError($"zip() got an unexpected keyword argument '{n}'");
                strict = Ops.Truthy(kw.Get("strict")!);
            }
            var its = a.Select(Ops.GetIterator).ToArray();
            return new ZipIterator(its, strict);
        };
        PyTypes.Enumerate.Ctor = (type, a, kw) =>
        {
            var b = new Sig("enumerate", 1, 0, 2, "iterable", "start").Bind(a, kw);
            return new EnumerateIterator(Ops.GetIterator(b[0]!), b[1] == null ? PyInt.Zero : Ops.Index(b[1]!));
        };
        PyTypes.Reversed.Ctor = (type, a, kw) =>
        {
            Arg.Exact(a, kw, 1, "reversed");
            return Builtins.Reversed(a[0]);
        };
    }
}

/// <summary>
/// Special methods of the subclassable builtin types (str, int, float, list, tuple, dict) so that Python subclasses can
/// call them through super() or explicitly (str.__add__(a, b)). They compute the builtin result only; the generic
/// dispatch never calls them, so there is no recursion when a subclass overrides an operator.
/// </summary>
internal static class BuiltinDunders
{
    private static readonly BinOp[] BinaryOps =
    {
        BinOp.Add, BinOp.Sub, BinOp.Mul, BinOp.TrueDiv, BinOp.FloorDiv, BinOp.Mod, BinOp.Pow,
        BinOp.LShift, BinOp.RShift, BinOp.And, BinOp.Or, BinOp.Xor,
    };

    private static readonly (string Name, CompareOp Op)[] Comparisons =
    {
        ("__lt__", CompareOp.Lt), ("__le__", CompareOp.Le), ("__eq__", CompareOp.Eq),
        ("__ne__", CompareOp.Ne), ("__gt__", CompareOp.Gt), ("__ge__", CompareOp.Ge),
    };

    public static void Register()
    {
        foreach (var t in new[] { PyTypes.Str, PyTypes.Int, PyTypes.Float, PyTypes.List, PyTypes.Tuple, PyTypes.Dict })
        {
            var type = t;
            foreach (var op in BinaryOps)
            {
                var o = op;
                if (!type.Dict.ContainsKey(Arith.DunderName(o, false)))
                    type.Method1(Arith.DunderName(o, false), (self, x) => Arith.BinaryCore(o, self, x) ?? Py.NotImplemented);
                if (!type.Dict.ContainsKey(Arith.DunderName(o, true)))
                    type.Method1(Arith.DunderName(o, true), (self, x) => Arith.BinaryCore(o, x, self) ?? Py.NotImplemented);
            }
            foreach (var (name, cop) in Comparisons)
            {
                var c = cop;
                type.Method1(name, (self, x) => Ops.CompareCore(self, x, c) ?? Py.NotImplemented);
            }
            if (type != PyTypes.List && type != PyTypes.Dict)
                type.Method0("__hash__", self => PyInt.From(Ops.HashBase(self)));
            type.Method0("__bool__", self => Py.Bool(Ops.Truthy(Subclassing.Plain(self))));
            type.Dict["__new__"] = new PyBuiltinMethod("__new__", PyTypes.Type, (cls, a, kw) =>
            {
                if (cls is not PyType c || !c.IsSubtypeOf(type))
                    throw PyErr.TypeError($"{type.Name}.__new__({(cls as PyType)?.Name ?? cls.Type.Name}): not a subtype of {type.Name}");
                return c.IsBuiltin ? type.Ctor!(type, a, kw) : PyType.CreateRaw(c, a, kw);
            });
        }
        foreach (var t in new[] { PyTypes.Int, PyTypes.Float })
        {
            var type = t;
            type.Method0("__pos__", self => Arith.Pos(Subclassing.Plain(self)));
            type.Method0("__neg__", self => Arith.Neg(Subclassing.Plain(self)));
            type.Method0("__abs__", self => Arith.Abs(Subclassing.Plain(self)));
            type.Method0("__int__", self => Ops.ToInt(Subclassing.Plain(self)));
            type.Method0("__float__", self => new PyFloat(Ops.ToDouble(Subclassing.Plain(self))));
            type.Method1("__divmod__", (self, x) => Subclassing.Plain(self) is var p && x is PyInt or PyFloat ? Arith.DivMod(p, Subclassing.Plain(x)) : Py.NotImplemented);
        }
        PyTypes.Int.Method0("__invert__", self => Arith.Invert(Subclassing.Plain(self)));
        PyTypes.Str.Method1("__contains__", (self, x) => Py.Bool(Ops.ContainsBase(self, x)));
        PyTypes.Str.Method1("__format__", (self, spec) => PyStr.New(Formatter.FormatBase(self, Arg.Str(spec, "format spec"))));
    }
}