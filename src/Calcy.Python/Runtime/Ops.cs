using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Calcy.Python.Runtime;

internal enum CompareOp : byte
{
    Lt,
    Le,
    Eq,
    Ne,
    Gt,
    Ge,
}

/// <summary>Core object protocol operations with Python semantics.</summary>
internal static partial class Ops
{
    private static readonly ConditionalWeakTable<PyObject, object> Ids = new();
    private static long _nextId = 0x7f3a00001000;

    public static long Id(PyObject o)
    {
        if (Ids.TryGetValue(o, out var boxed)) return (long)boxed;
        lock (Ids)
        {
            if (Ids.TryGetValue(o, out boxed)) return (long)boxed;
            long id = Interlocked.Add(ref _nextId, 48);
            Ids.Add(o, id);
            return id;
        }
    }

    private static string Addr(PyObject o) => "0x" + Id(o).ToString("x12", CultureInfo.InvariantCulture);

    /// <summary>True when a looked up special method is the default implementation inherited from object.</summary>
    public static bool IsObjectDefault(PyObject m) => m is PyBuiltinMethod bm && bm.Owner == PyTypes.Object;

    // ------------------------------------------------------------------ truthiness

    public static bool Truthy(PyObject o)
    {
        if (ReferenceEquals(o, PyBool.True)) return true;
        if (ReferenceEquals(o, PyBool.False) || ReferenceEquals(o, Py.None)) return false;
        if (o.IsBuiltinSubclass)
        {
            var ub = UserMethod(o.Type, "__bool__");
            if (ub != null)
            {
                var r = CallBound(ub, o, Array.Empty<PyObject>());
                if (r is not PyBool rb) throw PyErr.TypeError($"__bool__ should return bool, returned {r.Type.Name}");
                return rb.Value;
            }
            if (UserMethod(o.Type, "__len__") != null) return Len(o) != 0;
        }
        switch (o)
        {
            case PyInt i: return i.IsBig || i.V != 0;
            case PyFloat f: return f.V != 0;
            case PyStr s: return s.V.Length != 0;
            case PyList l: return l.Count != 0;
            case PyTuple t: return t.Items.Length != 0;
            case PyDict d: return d.Count != 0;
            case PySetBase st: return st.Count != 0;
            case PyRange r: return r.Length != 0;
            case PyComplex c: return c.Real != 0 || c.Imag != 0;
            case PyDictView v: return v.Dict.Count != 0;
            case PyFunction:
            case PyBuiltinFunction:
            case PyType:
            case PyModule:
                return true;
        }
        var t2 = o.Type;
        var b = t2.Lookup("__bool__");
        if (b != null && b != Py.None)
        {
            var r = CallBound(b, o, Array.Empty<PyObject>());
            if (r is not PyBool rb) throw PyErr.TypeError($"__bool__ should return bool, returned {r.Type.Name}");
            return rb.Value;
        }
        var len = t2.Lookup("__len__");
        if (len != null && len != Py.None)
        {
            var r = CallBound(len, o, Array.Empty<PyObject>());
            var ri = r as PyInt ?? throw PyErr.TypeError($"'{r.Type.Name}' object cannot be interpreted as an integer");
            if (ri.Sign < 0) throw PyErr.ValueError("__len__() should return >= 0");
            return ri.Sign != 0;
        }
        return true;
    }

    // ------------------------------------------------------------------ repr and str

    public static string SafeRepr(PyObject o) => ReprImpl(o, true);

    public static string SafeStr(PyObject o)
    {
        if (o is PyStr s) return s.V;
        if (o is PyBaseException e) return e.BuiltinStr(true);
        return ReprImpl(o, true);
    }

    public static string Repr(PyObject o) => ReprImpl(o, false);

    public static PyStr ReprObj(PyObject o) => PyStr.New(Repr(o));

    public static string Str(PyObject o)
    {
        if (o.IsBuiltinSubclass)
        {
            var us = UserMethod(o.Type, "__str__");
            if (us == null && o is not PyStr) us = UserMethod(o.Type, "__repr__");
            if (us != null)
            {
                var r = CallBound(us, o, Array.Empty<PyObject>());
                if (r is not PyStr rs) throw PyErr.TypeError($"__str__ returned non-string (type {r.Type.Name})");
                return rs.V;
            }
        }
        switch (o)
        {
            case PyStr s:
                return s.V;
            case PyInt or PyFloat or PyComplex or PyList or PyTuple or PyDict or PyNone:
                return ReprImpl(o, false);
            case PyInstance inst:
            {
                var m = inst.Cls.Lookup("__str__");
                if (m != null && !IsObjectDefault(m))
                {
                    var r = CallBound(m, o, Array.Empty<PyObject>());
                    if (r is not PyStr rs) throw PyErr.TypeError($"__str__ returned non-string (type {r.Type.Name})");
                    return rs.V;
                }
                return Repr(o);
            }
            case PyBaseException e:
            {
                var m = e.Type.Lookup("__str__");
                if (m != null && m is not PyBuiltinMethod)
                {
                    var r = CallBound(m, o, Array.Empty<PyObject>());
                    if (r is not PyStr rs) throw PyErr.TypeError($"__str__ returned non-string (type {r.Type.Name})");
                    return rs.V;
                }
                return e.BuiltinStr(false);
            }
        }
        var sm = o.Type.Lookup("__str__");
        if (sm is PyBuiltinMethod bsm && bsm.Owner != PyTypes.Object && bsm.Owner != PyTypes.Str)
        {
            var r = bsm.Fn(o, Array.Empty<PyObject>(), null);
            if (r is PyStr rs) return rs.V;
        }
        return Repr(o);
    }

    public static string Ascii(PyObject o)
    {
        var r = Repr(o);
        var sb = new StringBuilder();
        foreach (int cp in PyStr.ToCodePoints(r))
        {
            if (cp < 128) sb.Append((char)cp);
            else if (cp <= 0xff) sb.Append("\\x").Append(cp.ToString("x2"));
            else if (cp <= 0xffff) sb.Append("\\u").Append(cp.ToString("x4"));
            else sb.Append("\\U").Append(cp.ToString("x8"));
        }
        return sb.ToString();
    }

    /// <summary>repr() of the builtin base value, ignoring __repr__ overrides of subclasses.</summary>
    public static string ReprBase(PyObject o) => ReprImpl(o, false, true);

    private static string ReprImpl(PyObject o, bool safe, bool skipSub = false)
    {
        if (!safe && !skipSub && o.IsBuiltinSubclass)
        {
            var ur = UserMethod(o.Type, "__repr__");
            if (ur != null)
            {
                Interp.CheckStack();
                var r = CallBound(ur, o, Array.Empty<PyObject>());
                if (r is not PyStr rs) throw PyErr.TypeError($"__repr__ returned non-string (type {r.Type.Name})");
                return rs.V;
            }
        }
        switch (o)
        {
            case PyStr s:
                return PyStr.Repr(s.V);
            case PyBool b:
                return b.Value ? "True" : "False";
            case PyInt i:
                return i.ToString();
            case PyFloat f:
                return FloatFormat.Repr(f.V);
            case PyComplex c:
                return FloatFormat.ComplexRepr(c.Real, c.Imag);
            case PyNone:
                return "None";
            case PyEllipsis:
                return "Ellipsis";
            case PyNotImplemented:
                return "NotImplemented";
            case PyList l:
                return SeqRepr(o, l.Items, l.Count, "[", "]", safe);
            case PyTuple t:
                if (t.Items.Length == 1) return SeqRepr(o, t.Items, 1, "(", ",)", safe);
                return SeqRepr(o, t.Items, t.Items.Length, "(", ")", safe);
            case PyDict d when d.Type == PyTypes.Dict || d is PyDictSub:
                return DictRepr(d, safe);
            case PySet st:
                return st.Count == 0 ? "set()" : SeqRepr(o, st.Table.Keys().ToArray(), st.Count, "{", "}", safe);
            case PyFrozenSet fs:
                return fs.Count == 0 ? "frozenset()" : SeqRepr(o, fs.Table.Keys().ToArray(), fs.Count, "frozenset({", "})", safe);
            case PyRange r:
                return r.Step == 1 ? $"range({r.Start}, {r.Stop})" : $"range({r.Start}, {r.Stop}, {r.Step})";
            case PySlice sl:
                return $"slice({ReprImpl(sl.Start, safe)}, {ReprImpl(sl.Stop, safe)}, {ReprImpl(sl.Step, safe)})";
            case PyDictView v:
            {
                var items = new List<PyObject>();
                var it = new DictIterator(v.Dict, v.Kind);
                while (it.MoveNext(out var x)) items.Add(x);
                return o.Type.Name + SeqRepr(o, items.ToArray(), items.Count, "([", "])", safe);
            }
            case PyType t2:
                return $"<class '{t2.FullName}'>";
            case PyFunction fn:
                return $"<function {fn.QualName} at {Addr(o)}>";
            case PyBoundMethod bm:
            {
                string fname = bm.Func is PyFunction f2 ? f2.QualName : bm.Func is PyBuiltinFunction bf ? bf.Name : "?";
                string selfRepr;
                try { selfRepr = ReprImpl(bm.Self, safe); }
                catch (PythonException) { selfRepr = "?"; }
                return $"<bound method {fname} of {selfRepr}>";
            }
            case PyBuiltinFunction bf2:
                return $"<built-in function {bf2.Name}>";
            case PyBuiltinBoundMethod bbm:
                return $"<built-in method {bbm.Method.Name} of {bbm.Self.Type.Name} object at {Addr(bbm.Self)}>";
            case PyBuiltinMethod bmd:
                return $"<method '{bmd.Name}' of '{bmd.Owner.Name}' objects>";
            case PyModule m:
                return m.IsBuiltin ? $"<module '{m.Name}' (built-in)>" : $"<module '{m.Name}'>";
            case PyGenerator g:
                return $"<generator object {g.QualName} at {Addr(o)}>";
            case PyHostObject h:
                return $"<host object '{h.Value.GetType().Name}'>";
            case PyCellObject:
                return $"<cell at {Addr(o)}>";
        }
        if (o is PyInstance || o is PyBaseException || o is PyDict)
        {
            var m = o.Type.Lookup("__repr__");
            if (m != null && (m is not PyBuiltinMethod || ((PyBuiltinMethod)m).Owner != PyTypes.Object))
            {
                if (safe && m is not PyBuiltinMethod) return DefaultRepr(o);
                if (!safe) Interp.CheckStack();
                var r = CallBound(m, o, Array.Empty<PyObject>());
                if (r is not PyStr rs) throw PyErr.TypeError($"__repr__ returned non-string (type {r.Type.Name})");
                return rs.V;
            }
            return DefaultRepr(o);
        }
        var nm = o.Type.Lookup("__repr__");
        if (nm is PyBuiltinMethod nbm && nbm.Owner != PyTypes.Object)
        {
            var r = nbm.Fn(o, Array.Empty<PyObject>(), null);
            if (r is PyStr rs) return rs.V;
        }
        return DefaultRepr(o);
    }

    public static string DefaultRepr(PyObject o)
    {
        var t = o.Type;
        string name = t.IsBuiltin ? t.Name : t.FullName;
        return $"<{name} object at {Addr(o)}>";
    }

    private static string SeqRepr(PyObject self, PyObject[] items, int count, string open, string close, bool safe)
    {
        if (count == 0) return open + close;
        var ctx = Interp.CurrentOrNull;
        if (ctx != null && !ctx.ReprEnter(self))
            return open.EndsWith("(") || open.EndsWith("[") || open.EndsWith("{") ? open + "..." + close : "...";
        try
        {
            Interp.CheckStack();
            var sb = new StringBuilder();
            sb.Append(open);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ReprImpl(items[i], safe));
                if (sb.Length > Limits.MaxCollection) Limits.CheckStringLength(sb.Length);
            }
            sb.Append(close);
            return sb.ToString();
        }
        finally
        {
            ctx?.ReprLeave(self);
        }
    }

    public static string DictRepr(PyDict d, bool safe)
    {
        if (d.Count == 0) return "{}";
        var ctx = Interp.CurrentOrNull;
        if (ctx != null && !ctx.ReprEnter(d)) return "{...}";
        try
        {
            Interp.CheckStack();
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var (k, v) in d.Pairs())
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(ReprImpl(k, safe)).Append(": ").Append(ReprImpl(v, safe));
                if (sb.Length > Limits.MaxCollection) Limits.CheckStringLength(sb.Length);
            }
            sb.Append('}');
            return sb.ToString();
        }
        finally
        {
            ctx?.ReprLeave(d);
        }
    }

    // ------------------------------------------------------------------ hashing

    public static long Hash(PyObject o)
    {
        if (o.IsBuiltinSubclass)
        {
            var hm = o.Type.Lookup("__hash__");
            if (hm == Py.None) throw PyErr.TypeError($"unhashable type: '{o.Type.Name}'");
            var uh = UserMethod(o.Type, "__hash__");
            if (uh != null)
            {
                var r = CallBound(uh, o, Array.Empty<PyObject>());
                if (r is not PyInt ri) throw PyErr.TypeError("__hash__ method should return an integer");
                long h = ri.IsBig ? IntMath.Hash(ri) : ri.V;
                return h == -1 ? -2 : h;
            }
        }
        return HashBase(o);
    }

    /// <summary>hash() of the builtin base value, ignoring __hash__ overrides of subclasses.</summary>
    public static long HashBase(PyObject o)
    {
        switch (o)
        {
            case PyStr s:
                return s.Hash;
            case PyInt i:
                if (!i.IsBig && i.V >= 0 && i.V < (1L << 61) - 1) return i.V;
                return IntMath.Hash(i);
            case PyFloat f:
                if (double.IsNaN(f.V)) return Id(o) >> 4;
                return FloatMath.Hash(f.V);
            case PyTuple t:
                return HashTuple(t.Items);
            case PyNone:
                return 0x5f3759df;
            case PyComplex c:
            {
                ulong hr = (ulong)FloatMath.Hash(c.Real), hi = (ulong)FloatMath.Hash(c.Imag);
                ulong comb = hr + 1000003UL * hi;
                long r = (long)comb;
                return r == -1 ? -2 : r;
            }
            case PyFrozenSet fs:
            {
                ulong h = 0;
                foreach (var (k, kh) in fs.Table.Entries())
                {
                    ulong x = (ulong)kh;
                    h ^= ((x ^ 89869747UL) ^ (x << 16)) * 3644798167UL;
                }
                h ^= ((ulong)fs.Count + 1) * 1927868237UL;
                long r = (long)h;
                return r == -1 ? 590923713 : r;
            }
            case PyRange r:
                return HashTuple(new PyObject[] { PyInt.From(r.Length), PyInt.From(r.Start), PyInt.From(r.Step) });
            case PyList:
            case PyDict:
            case PySet:
            case PyDictView:
                throw PyErr.TypeError($"unhashable type: '{o.Type.Name}'");
            case PyInstance or PyBaseException:
            {
                var m = o.Type.Lookup("__hash__");
                if (m == Py.None) throw PyErr.TypeError($"unhashable type: '{o.Type.Name}'");
                if (m != null && !IsObjectDefault(m))
                {
                    var r = CallBound(m, o, Array.Empty<PyObject>());
                    if (r is not PyInt ri) throw PyErr.TypeError("__hash__ method should return an integer");
                    long h = ri.IsBig ? IntMath.Hash(ri) : ri.V;
                    return h == -1 ? -2 : h;
                }
                return Id(o) >> 4;
            }
        }
        var hm = o.Type.Lookup("__hash__");
        if (hm == Py.None) throw PyErr.TypeError($"unhashable type: '{o.Type.Name}'");
        return Id(o) >> 4;
    }

    public static long HashTuple(PyObject[] items)
    {
        Interp.CheckStack();
        const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P5 = 2870177450012600261UL;
        ulong acc = P5;
        foreach (var item in items)
        {
            ulong lane = (ulong)Hash(item);
            acc += lane * P2;
            acc = (acc << 31) | (acc >> 33);
            acc *= P1;
        }
        acc += (ulong)items.Length ^ (P5 ^ 3527539UL);
        if (acc == ulong.MaxValue) return 1546275796;
        return (long)acc;
    }

    // ------------------------------------------------------------------ equality and comparison

    /// <summary>Equality used by containers: identity implies equality.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EqBool(PyObject a, PyObject b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.IsBuiltinSubclass || b.IsBuiltinSubclass) return EqSlow(a, b);
        if (a is PyStr sa && b is PyStr sb) return sa.V == sb.V;
        if (a is PyInt ia && b is PyInt ib) return IntMath.Equal(ia, ib);
        return EqSlow(a, b);
    }

    private static bool EqSlow(PyObject a, PyObject b)
    {
        var r = Eq(a, b);
        return r is PyBool rb ? rb.Value : Truthy(r);
    }

    public static PyObject Eq(PyObject a, PyObject b)
    {
        if (a.IsBuiltinSubclass || b.IsBuiltinSubclass)
        {
            var u = RichUser(a, b, CompareOp.Eq);
            if (u != null) return u;
        }
        var r = BuiltinEq(a, b);
        if (r != null) return r;
        return RichUser(a, b, CompareOp.Eq) ?? Py.Bool(ReferenceEquals(a, b));
    }

    public static PyObject Ne(PyObject a, PyObject b)
    {
        if (a.IsBuiltinSubclass || b.IsBuiltinSubclass)
        {
            var su = RichUser(a, b, CompareOp.Ne);
            if (su != null) return su;
        }
        var r = BuiltinEq(a, b);
        if (r != null) return Py.Bool(!((PyBool)r).Value);
        var u = RichUser(a, b, CompareOp.Ne);
        if (u != null) return u;
        return Py.Bool(!ReferenceEquals(a, b));
    }

    /// <summary>Equality between builtin values; null when user dispatch is needed.</summary>
    private static PyBool? BuiltinEq(PyObject a, PyObject b)
    {
        switch (a)
        {
            case PyInt ia:
                switch (b)
                {
                    case PyInt ib: return Py.Bool(IntMath.Equal(ia, ib));
                    case PyFloat fb: return Py.Bool(!double.IsNaN(fb.V) && FloatMath.CompareIntFloat(ia, fb.V) == 0);
                    case PyComplex cb: return Py.Bool(cb.Imag == 0 && !double.IsNaN(cb.Real) && FloatMath.CompareIntFloat(ia, cb.Real) == 0);
                    case PyInstance or PyBaseException: return null;
                    default: return b.Type.IsBuiltin && b.Type.Lookup("__eq__") is null or PyBuiltinMethod ? PyBool.False : null;
                }
            case PyFloat fa:
                switch (b)
                {
                    case PyFloat fb: return Py.Bool(fa.V == fb.V);
                    case PyInt ib: return Py.Bool(!double.IsNaN(fa.V) && FloatMath.CompareIntFloat(ib, fa.V) == 0);
                    case PyComplex cb: return Py.Bool(cb.Imag == 0 && cb.Real == fa.V);
                    case PyInstance or PyBaseException: return null;
                    default: return PyBool.False;
                }
            case PyComplex ca:
                switch (b)
                {
                    case PyComplex cb: return Py.Bool(ca.Real == cb.Real && ca.Imag == cb.Imag);
                    case PyFloat fb: return Py.Bool(ca.Imag == 0 && ca.Real == fb.V);
                    case PyInt ib: return Py.Bool(ca.Imag == 0 && !double.IsNaN(ca.Real) && FloatMath.CompareIntFloat(ib, ca.Real) == 0);
                    case PyInstance or PyBaseException: return null;
                    default: return PyBool.False;
                }
            case PyStr sa:
                if (b is PyStr sb) return Py.Bool(sa.V == sb.V);
                return b is PyInstance or PyBaseException ? null : PyBool.False;
            case PyList la:
                if (b is PyList lb) return Py.Bool(SeqEq(la.Items, la.Count, lb.Items, lb.Count));
                return b is PyInstance or PyBaseException ? null : PyBool.False;
            case PyTuple ta:
                if (b is PyTuple tb) return Py.Bool(SeqEq(ta.Items, ta.Items.Length, tb.Items, tb.Items.Length));
                return b is PyInstance or PyBaseException ? null : PyBool.False;
            case PyDict da when b is PyDict db:
                return Py.Bool(DictEq(da, db));
            case PySetBase sa2 when b is PySetBase sb2:
                return Py.Bool(sa2.Count == sb2.Count && IsSubset(sa2.Table, sb2.Table));
            case PyRange ra when b is PyRange rb:
                return Py.Bool(RangeEq(ra, rb));
            case PyDictView va when b is PySetBase || b is PyDictView:
                if (va.Kind == DictViewKind.Values) return Py.Bool(ReferenceEquals(a, b));
                var sa3 = PySet.From(a);
                var sb3 = PySet.From(b);
                return Py.Bool(sa3.Count == sb3.Count && IsSubset(sa3.Table, sb3.Table));
            case PySetBase when b is PyDictView vb && vb.Kind != DictViewKind.Values:
                return BuiltinEq(b, a);
            case PyNone:
                return b is PyInstance ? null : Py.Bool(ReferenceEquals(a, b));
            case PyType or PyFunction or PyBuiltinFunction or PyModule:
                return b is PyInstance ? null : Py.Bool(ReferenceEquals(a, b));
            case PyBoundMethod bma when b is PyBoundMethod bmb:
                return Py.Bool(ReferenceEquals(bma.Func, bmb.Func) && ReferenceEquals(bma.Self, bmb.Self));
        }
        return null;
    }

    private static bool RangeEq(PyRange a, PyRange b)
    {
        if (a.Length != b.Length) return false;
        if (a.Length == 0) return true;
        if (a.Start != b.Start) return false;
        if (a.Length == 1) return true;
        return a.Step == b.Step;
    }

    private static bool SeqEq(PyObject[] a, int na, PyObject[] b, int nb)
    {
        if (na != nb) return false;
        Interp.CheckStack();
        for (int i = 0; i < na; i++)
            if (!EqBool(a[i], b[i])) return false;
        return true;
    }

    private static bool DictEq(PyDict a, PyDict b)
    {
        if (a.Count != b.Count) return false;
        Interp.CheckStack();
        foreach (var (k, v) in a.Pairs())
        {
            var bv = b.GetOrNull(k);
            if (bv == null || !EqBool(v, bv)) return false;
        }
        return true;
    }

    public static bool IsSubset(SetTable a, SetTable b)
    {
        if (a.Count > b.Count) return false;
        foreach (var (k, h) in a.Entries())
            if (!b.Contains(k, h)) return false;
        return true;
    }

    private static readonly string[] CmpNames = { "__lt__", "__le__", "__eq__", "__ne__", "__gt__", "__ge__" };
    private static readonly CompareOp[] Swapped = { CompareOp.Gt, CompareOp.Ge, CompareOp.Eq, CompareOp.Ne, CompareOp.Lt, CompareOp.Le };
    private static readonly string[] CmpSymbols = { "<", "<=", "==", "!=", ">", ">=" };

    /// <summary>User level rich comparison dispatch; returns null when both sides return NotImplemented.</summary>
    private static PyObject? RichUser(PyObject a, PyObject b, CompareOp op)
    {
        var ta = a.Type;
        var tb = b.Type;
        var rop = Swapped[(int)op];
        string name = CmpNames[(int)op];
        string rname = CmpNames[(int)rop];
        bool checkedReflected = false;
        if (!ReferenceEquals(ta, tb) && tb.IsSubtypeOf(ta))
        {
            checkedReflected = true;
            if (CompareSide(b, a, rop, rname) is { } r1) return r1;
        }
        if (CompareSide(a, b, op, name) is { } r2) return r2;
        if (!checkedReflected && CompareSide(b, a, rop, rname) is { } r3) return r3;
        return null;
    }

    /// <summary>
    /// One side of a rich comparison as CPython's slot would run it: a Python method, the builtin comparison of a
    /// builtin base (for subclass instances), or object.__ne__ inverting __eq__. Null means NotImplemented.
    /// Exact builtin values return null because their builtin comparison already ran.
    /// </summary>
    private static PyObject? CompareSide(PyObject self, PyObject other, CompareOp op, string name)
    {
        var m = self.Type.Lookup(name);
        if (m == Py.None) return null;
        if (m == null || m is PyBuiltinMethod)
        {
            var owner = (m as PyBuiltinMethod)?.Owner;
            if (owner == null || owner == PyTypes.Object || owner.Layout == LayoutKind.Exception)
                return op == CompareOp.Ne ? InvertedEq(self, other) : null;
            if (IsCoreType(owner)) return self.IsBuiltinSubclass ? CompareCore(self, other, op) : null;
        }
        var r = CallBound(m!, self, new[] { other });
        return r == Py.NotImplemented ? null : r;
    }

    private static PyObject? InvertedEq(PyObject x, PyObject y)
    {
        var m = UserMethod(x.Type, "__eq__");
        if (m == null) return null;
        var r = CallBound(m, x, new[] { y });
        return r == Py.NotImplemented ? null : Py.Bool(!Truthy(r));
    }

    /// <summary>A special method defined in Python (not a builtin default) for the type, or null.</summary>
    public static PyObject? UserMethodOf(PyType t, string name) => UserMethod(t, name);

    /// <summary>The builtin comparison of two values, or null when the builtin types do not support it.</summary>
    public static PyObject? CompareCore(PyObject a, PyObject b, CompareOp op)
    {
        if (op == CompareOp.Eq) return BuiltinEq(a, b);
        if (op == CompareOp.Ne)
        {
            var e = BuiltinEq(a, b);
            return e == null ? null : Py.Bool(!e.Value);
        }
        int? c = BuiltinOrder(a, b, op, out var direct);
        if (direct != null) return direct;
        if (c == null) return null;
        int v = c.Value;
        return Py.Bool(op switch
        {
            CompareOp.Lt => v < 0,
            CompareOp.Le => v <= 0,
            CompareOp.Gt => v > 0,
            _ => v >= 0,
        });
    }

    private static PyObject? UserMethod(PyType t, string name)
    {
        var m = t.Lookup(name);
        if (m == null || m == Py.None) return null;
        if (m is PyBuiltinMethod bm && (bm.Owner == PyTypes.Object || bm.Owner.Layout == LayoutKind.Exception || IsCoreType(bm.Owner))) return null;
        return m;
    }

    private static bool IsCoreType(PyType t) =>
        t == PyTypes.Str || t == PyTypes.Int || t == PyTypes.Float || t == PyTypes.List || t == PyTypes.Tuple || t == PyTypes.Dict ||
        t == PyTypes.Set || t == PyTypes.FrozenSet || t == PyTypes.Complex || t == PyTypes.Bool;

    public static PyObject RichCompare(PyObject a, PyObject b, CompareOp op)
    {
        if (op is not (CompareOp.Eq or CompareOp.Ne) && (a.IsBuiltinSubclass || b.IsBuiltinSubclass))
        {
            var su = RichUser(a, b, op);
            if (su != null) return su;
        }
        switch (op)
        {
            case CompareOp.Eq:
                return Eq(a, b);
            case CompareOp.Ne:
                return Ne(a, b);
        }
        int? c = BuiltinOrder(a, b, op, out var direct);
        if (direct != null) return direct;
        if (c.HasValue)
        {
            int v = c.Value;
            return Py.Bool(op switch
            {
                CompareOp.Lt => v < 0,
                CompareOp.Le => v <= 0,
                CompareOp.Gt => v > 0,
                _ => v >= 0,
            });
        }
        var u = RichUser(a, b, op);
        if (u != null) return u;
        throw PyErr.TypeError($"'{CmpSymbols[(int)op]}' not supported between instances of '{a.Type.Name}' and '{b.Type.Name}'");
    }

    public static bool CompareBool(PyObject a, PyObject b, CompareOp op)
    {
        var r = RichCompare(a, b, op);
        return r is PyBool rb ? rb.Value : Truthy(r);
    }

    public static bool LtBool(PyObject a, PyObject b)
    {
        if (a.IsBuiltinSubclass || b.IsBuiltinSubclass) return CompareBool(a, b, CompareOp.Lt);
        if (a is PyInt ia && b is PyInt ib) return IntMath.Compare(ia, ib) < 0;
        if (a is PyFloat fa && b is PyFloat fb) return fa.V < fb.V;
        if (a is PyStr sa && b is PyStr sb) return string.CompareOrdinal(sa.V, sb.V) < 0;
        return CompareBool(a, b, CompareOp.Lt);
    }

    /// <summary>
    /// Ordering between builtin values. Returns a three way result, or sets direct for results that are not
    /// a total order (NaN, sets). Returns null with direct null when user dispatch is needed.
    /// </summary>
    private static int? BuiltinOrder(PyObject a, PyObject b, CompareOp op, out PyObject? direct)
    {
        direct = null;
        switch (a)
        {
            case PyInt ia:
                if (b is PyInt ib) return IntMath.Compare(ia, ib);
                if (b is PyFloat fb)
                {
                    if (double.IsNaN(fb.V))
                    {
                        direct = PyBool.False;
                        return null;
                    }
                    return FloatMath.CompareIntFloat(ia, fb.V);
                }
                break;
            case PyFloat fa:
                if (b is PyFloat fb2)
                {
                    if (double.IsNaN(fa.V) || double.IsNaN(fb2.V))
                    {
                        direct = PyBool.False;
                        return null;
                    }
                    return fa.V.CompareTo(fb2.V);
                }
                if (b is PyInt ib2)
                {
                    if (double.IsNaN(fa.V))
                    {
                        direct = PyBool.False;
                        return null;
                    }
                    return -FloatMath.CompareIntFloat(ib2, fa.V);
                }
                break;
            case PyStr sa:
                if (b is PyStr sb) return Math.Sign(string.CompareOrdinal(sa.V, sb.V));
                break;
            case PyList la:
                if (b is PyList lb) return SeqOrder(la.Items, la.Count, lb.Items, lb.Count, op, out direct);
                break;
            case PyTuple ta:
                if (b is PyTuple tb) return SeqOrder(ta.Items, ta.Items.Length, tb.Items, tb.Items.Length, op, out direct);
                break;
            case PySetBase s1:
                if (b is PySetBase s2)
                {
                    direct = Py.Bool(op switch
                    {
                        CompareOp.Le => IsSubset(s1.Table, s2.Table),
                        CompareOp.Lt => s1.Count < s2.Count && IsSubset(s1.Table, s2.Table),
                        CompareOp.Ge => IsSubset(s2.Table, s1.Table),
                        _ => s1.Count > s2.Count && IsSubset(s2.Table, s1.Table),
                    });
                    return null;
                }
                break;
        }
        return null;
    }

    private static int? SeqOrder(PyObject[] a, int na, PyObject[] b, int nb, CompareOp op, out PyObject? direct)
    {
        direct = null;
        Interp.CheckStack();
        int n = Math.Min(na, nb);
        for (int i = 0; i < n; i++)
        {
            if (!EqBool(a[i], b[i]))
            {
                direct = RichCompare(a[i], b[i], op);
                return null;
            }
        }
        return na.CompareTo(nb);
    }

    // ------------------------------------------------------------------ attributes

    public static PyObject BindDescriptor(PyObject d, PyObject obj, PyType type)
    {
        switch (d)
        {
            case PyFunction:
                return new PyBoundMethod(d, obj);
            case PyBuiltinMethod bm:
                return new PyBuiltinBoundMethod(bm, obj);
            case PyStaticMethod sm:
                return sm.Func;
            case PyClassMethod cm:
                return new PyBoundMethod(cm.Func, type);
            case PyBuiltinClassMethod bcm:
                return bcm.BindTo(type);
            case PyProperty p:
                return p.Get(obj);
            case PyGetSet gs:
                return gs.Getter(obj);
            case PyInstance inst:
            {
                var get = inst.Cls.Lookup("__get__");
                if (get != null) return CallBound(get, d, new[] { obj, type });
                return d;
            }
            default:
                if (d.Type.Lookup("__get__") is PyBuiltinMethod g && g.Owner != PyTypes.Object)
                    return g.Fn(d, new[] { obj, type }, null);
                return d;
        }
    }

    public static PyObject GetAttr(PyObject o, string name)
    {
        var r = TryGetAttr(o, name);
        if (r != null) return r;
        throw NoAttribute(o, name);
    }

    public static PythonException NoAttribute(PyObject o, string name)
    {
        return o switch
        {
            PyType t => PyErr.AttributeError($"type object '{t.Name}' has no attribute '{name}'"),
            PyModule m => PyErr.AttributeError($"module '{m.Name}' has no attribute '{name}'"),
            _ => PyErr.AttributeError($"'{o.Type.Name}' object has no attribute '{name}'"),
        };
    }

    public static bool HasAttr(PyObject o, string name)
    {
        try
        {
            return TryGetAttr(o, name) != null;
        }
        catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.AttributeError))
        {
            return false;
        }
    }

    /// <summary>Attribute lookup; returns null when the attribute does not exist.</summary>
    public static PyObject? TryGetAttr(PyObject o, string name)
    {
        if (o is ISubclassInstance si) return SubclassGetAttr(o, si, name);
        switch (o)
        {
            case PyInstance inst:
            {
                var cls = inst.Cls;
                var d = cls.Lookup(name);
                if (d is PyProperty p) return p.Get(inst);
                var v = inst.Dict.GetStr(name);
                if (v != null) return v;
                if (d != null) return BindDescriptor(d, o, cls);
                if (name == "__class__") return cls;
                if (name == "__dict__") return inst.Dict;
                var ga = cls.Lookup("__getattr__");
                if (ga != null && !IsObjectDefault(ga))
                {
                    try
                    {
                        return CallBound(ga, o, new PyObject[] { PyStr.New(name) });
                    }
                    catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.AttributeError))
                    {
                        throw;
                    }
                }
                return null;
            }
            case PyType t:
                return TypeGetAttr(t, name);
            case PyModule m:
            {
                var v = m.Ns.Get(name);
                if (v != null) return v;
                if (name == "__dict__") return m.Ns.ToDict();
                if (name == "__class__") return PyTypes.Module;
                if (name == "__doc__") return Py.None;
                var ga = m.Ns.Get("__getattr__");
                if (ga != null) return Call(ga, new PyObject[] { PyStr.New(name) }, null);
                return null;
            }
            case PyBaseException e:
                return ExceptionGetAttr(e, name);
            case PyFunction f:
                switch (name)
                {
                    case "__name__": return PyStr.New(f.Name);
                    case "__qualname__": return PyStr.New(f.QualName);
                    case "__doc__": return f.Doc;
                    case "__module__": return PyStr.New(f.Module);
                    case "__dict__": return DictFromAttrs(f.Dict ??= new Dictionary<string, PyObject>());
                    case "__defaults__": return f.Defaults.Length == 0 ? Py.None : new PyTuple(f.Defaults);
                    case "__class__": return PyTypes.Function;
                }
                if (f.Dict != null && f.Dict.TryGetValue(name, out var fv)) return fv;
                break;
            case PyBoundMethod bm:
                switch (name)
                {
                    case "__self__": return bm.Self;
                    case "__func__": return bm.Func;
                    case "__name__": return TryGetAttr(bm.Func, "__name__");
                    case "__qualname__": return TryGetAttr(bm.Func, "__qualname__");
                    case "__doc__": return TryGetAttr(bm.Func, "__doc__");
                }
                break;
            case PyBuiltinFunction bf:
                switch (name)
                {
                    case "__name__":
                    case "__qualname__": return PyStr.New(bf.Name);
                    case "__doc__": return bf.Doc;
                    case "__module__": return bf.Module == null ? Py.None : PyStr.New(bf.Module);
                }
                break;
            case PyBuiltinMethod bmd:
                switch (name)
                {
                    case "__name__": return PyStr.New(bmd.Name);
                    case "__qualname__": return PyStr.New(bmd.Owner.Name + "." + bmd.Name);
                    case "__objclass__": return bmd.Owner;
                }
                break;
            case PyBuiltinBoundMethod bbm:
                switch (name)
                {
                    case "__name__": return PyStr.New(bbm.Method.Name);
                    case "__qualname__": return PyStr.New(bbm.Method.Owner.Name + "." + bbm.Method.Name);
                    case "__self__": return bbm.Self;
                }
                break;
            case PySuper sup:
            {
                var r = sup.Lookup(name);
                if (r != null) return r;
                if (name == "__class__") return PyTypes.Super;
                return null;
            }
            case PyStaticMethod sm when name == "__func__":
                return sm.Func;
            case PyClassMethod cm when name == "__func__":
                return cm.Func;
            case PyGenerator g:
                if (name == "__name__") return PyStr.New(g.Name);
                if (name == "__qualname__") return PyStr.New(g.QualName);
                if (name == "gi_running") return Py.Bool(g.IsRunning);
                break;
        }
        if (name == "__class__") return o.Type;
        var desc = o.Type.Lookup(name);
        if (desc != null) return BindDescriptor(desc, o, o.Type);
        if (name == "__doc__") return Py.None;
        return null;
    }

    private static PyObject? SubclassGetAttr(PyObject o, ISubclassInstance si, string name)
    {
        var cls = si.Cls;
        var d = cls.Lookup(name);
        if (d is PyProperty p) return p.Get(o);
        if (d is PyGetSet gs) return gs.Getter(o);
        var v = si.Dict.GetStr(name);
        if (v != null) return v;
        if (d != null) return BindDescriptor(d, o, cls);
        if (name == "__class__") return cls;
        if (name == "__dict__") return si.Dict;
        var ga = UserMethod(cls, "__getattr__");
        if (ga != null) return CallBound(ga, o, new PyObject[] { PyStr.New(name) });
        return null;
    }

    private static PyDict DictFromAttrs(Dictionary<string, PyObject> attrs)
    {
        var d = new PyDict(attrs.Count);
        foreach (var kv in attrs) d.SetItem(PyStr.New(kv.Key), kv.Value);
        return d;
    }

    private static PyObject? TypeGetAttr(PyType t, string name)
    {
        switch (name)
        {
            case "__name__": return PyStr.New(t.Name);
            case "__qualname__": return PyStr.New(t.QualName);
            case "__module__": return PyStr.New(t.Module);
            case "__bases__": return new PyTuple(t.Bases.Cast<PyObject>().ToArray());
            case "__base__": return t.Bases.Length > 0 ? t.Bases[0] : Py.None;
            case "__mro__": return new PyTuple(t.Mro.Cast<PyObject>().ToArray());
            case "__dict__": return DictFromAttrs(t.Dict);
            case "__doc__": return t.Doc ?? (t.Dict.TryGetValue("__doc__", out var doc) ? doc : Py.None);
            case "__class__": return PyTypes.Type;
        }
        var d = t.Lookup(name);
        if (d != null)
        {
            switch (d)
            {
                case PyClassMethod cm:
                    return new PyBoundMethod(cm.Func, t);
                case PyBuiltinClassMethod bcm:
                    return bcm.BindTo(t);
                case PyStaticMethod sm:
                    return sm.Func;
                case PyInstance inst:
                {
                    var get = inst.Cls.Lookup("__get__");
                    if (get != null) return CallBound(get, d, new PyObject[] { Py.None, t });
                    return d;
                }
                default:
                    return d;
            }
        }
        var meta = PyTypes.Type.Lookup(name);
        if (meta != null) return BindDescriptor(meta, t, PyTypes.Type);
        return null;
    }

    private static PyObject? ExceptionGetAttr(PyBaseException e, string name)
    {
        switch (name)
        {
            case "args": return e.Args;
            case "__cause__": return (PyObject?)e.Cause ?? Py.None;
            case "__context__": return (PyObject?)e.Context ?? Py.None;
            case "__suppress_context__": return Py.Bool(e.SuppressContext);
            case "__traceback__": return Py.None;
            case "__class__": return e.Type;
            case "__dict__": return DictFromAttrs(e.Dict ??= new Dictionary<string, PyObject>());
        }
        var extra = e.GetExtra(name);
        if (extra != null) return extra;
        var t = e.Type;
        if (name == "value" && t.IsSubtypeOf(Exc.StopIteration)) return e.Args.Count > 0 ? e.Args.Items[0] : Py.None;
        if (name == "code" && t.IsSubtypeOf(Exc.SystemExit)) return e.Args.Count == 0 ? Py.None : e.Args.Count == 1 ? e.Args.Items[0] : e.Args;
        if (name == "name" && (t.IsSubtypeOf(Exc.ImportError) || t.IsSubtypeOf(Exc.NameError) || t.IsSubtypeOf(Exc.AttributeError))) return Py.None;
        var d = t.Lookup(name);
        if (d is PyProperty p) return p.Get(e);
        if (d != null) return BindDescriptor(d, e, t);
        var ga = t.Lookup("__getattr__");
        if (ga != null && ga is not PyBuiltinMethod) return CallBound(ga, e, new PyObject[] { PyStr.New(name) });
        return null;
    }

    public static void SetAttr(PyObject o, string name, PyObject value)
    {
        if (o is ISubclassInstance)
        {
            var usa = UserMethod(o.Type, "__setattr__");
            if (usa != null) CallBound(usa, o, new PyObject[] { PyStr.New(name), value });
            else GenericSetAttr(o, name, value);
            return;
        }
        switch (o)
        {
            case PyInstance inst:
            {
                var cls = inst.Cls;
                var sa = cls.Lookup("__setattr__");
                if (sa != null && !IsObjectDefault(sa))
                {
                    CallBound(sa, o, new PyObject[] { PyStr.New(name), value });
                    return;
                }
                GenericSetAttr(inst, name, value);
                return;
            }
            case PyType t:
                if (t.IsBuiltin) throw PyErr.TypeError($"cannot set '{name}' attribute of immutable type '{t.Name}'");
                if (name == "__name__" && value is PyStr ns) t.Name = ns.V;
                if (name == "__doc__") t.Doc = value;
                t.Dict[name] = value;
                return;
            case PyModule m:
                m.Ns.Set(name, value);
                return;
            case PyFunction f:
                switch (name)
                {
                    case "__name__":
                        f.Name = Arg.Str(value, "__name__");
                        return;
                    case "__qualname__":
                        f.QualName = Arg.Str(value, "__qualname__");
                        return;
                    case "__doc__":
                        f.Doc = value;
                        return;
                    case "__module__":
                        f.Module = Arg.Str(value, "__module__");
                        return;
                }
                (f.Dict ??= new Dictionary<string, PyObject>(StringComparer.Ordinal))[name] = value;
                return;
            case PyBaseException e:
                switch (name)
                {
                    case "args":
                        e.Args = new PyTuple(ToArray(value));
                        return;
                    case "__cause__":
                        e.Cause = value as PyBaseException;
                        e.SuppressContext = true;
                        return;
                    case "__context__":
                        e.Context = value as PyBaseException;
                        return;
                    case "__suppress_context__":
                        e.SuppressContext = Truthy(value);
                        return;
                    case "__traceback__":
                        return;
                }
                var ed = e.Type.Lookup(name);
                if (ed is PyProperty ep)
                {
                    ep.Set(e, value);
                    return;
                }
                var esa = e.Type.Lookup("__setattr__");
                if (esa != null && esa is not PyBuiltinMethod)
                {
                    CallBound(esa, o, new PyObject[] { PyStr.New(name), value });
                    return;
                }
                e.SetExtra(name, value);
                return;
        }
        var d = o.Type.Lookup(name);
        if (d is PyGetSet gs && gs.Setter != null)
        {
            gs.Setter(o, value);
            return;
        }
        if (d != null)
            throw PyErr.AttributeError($"'{o.Type.Name}' object attribute '{name}' is read-only");
        throw PyErr.AttributeError($"'{o.Type.Name}' object has no attribute '{name}'");
    }

    public static void GenericSetAttr(PyObject o, string name, PyObject value)
    {
        if (o is PyInstance inst)
        {
            var d = inst.Cls.Lookup(name);
            if (d is PyProperty p)
            {
                p.Set(inst, value);
                return;
            }
            if (name == "__class__") throw PyErr.TypeError("__class__ assignment is not supported");
            inst.Dict.SetStr(name, value);
            return;
        }
        if (o is PyBaseException e)
        {
            e.SetExtra(name, value);
            return;
        }
        if (o is ISubclassInstance si)
        {
            var d = si.Cls.Lookup(name);
            if (d is PyProperty p)
            {
                p.Set(o, value);
                return;
            }
            if (d is PyGetSet gs)
            {
                if (gs.Setter == null) throw PyErr.AttributeError($"attribute '{name}' of '{o.Type.Name}' objects is not writable");
                gs.Setter(o, value);
                return;
            }
            if (name == "__class__") throw PyErr.TypeError("__class__ assignment is not supported");
            si.Dict.SetStr(name, value);
            return;
        }
        SetAttr(o, name, value);
    }

    public static void DelAttr(PyObject o, string name)
    {
        if (o is ISubclassInstance si)
        {
            var uda = UserMethod(o.Type, "__delattr__");
            if (uda != null)
            {
                CallBound(uda, o, new PyObject[] { PyStr.New(name) });
                return;
            }
            if (si.Cls.Lookup(name) is PyProperty sp)
            {
                sp.Delete(o);
                return;
            }
            if (!si.Dict.RemoveStr(name)) throw NoAttribute(o, name);
            return;
        }
        switch (o)
        {
            case PyInstance inst:
            {
                var da = inst.Cls.Lookup("__delattr__");
                if (da != null && !IsObjectDefault(da))
                {
                    CallBound(da, o, new PyObject[] { PyStr.New(name) });
                    return;
                }
                var d = inst.Cls.Lookup(name);
                if (d is PyProperty p)
                {
                    p.Delete(inst);
                    return;
                }
                if (!inst.Dict.RemoveStr(name)) throw NoAttribute(o, name);
                return;
            }
            case PyType t:
                if (t.IsBuiltin) throw PyErr.TypeError($"cannot delete '{name}' attribute of immutable type '{t.Name}'");
                if (!t.Dict.Remove(name)) throw NoAttribute(o, name);
                return;
            case PyModule m:
                if (!m.Ns.Remove(name)) throw NoAttribute(o, name);
                return;
            case PyFunction f:
                if (f.Dict == null || !f.Dict.Remove(name)) throw NoAttribute(o, name);
                return;
            case PyBaseException e:
                if (e.Dict == null || !e.Dict.Remove(name)) throw NoAttribute(o, name);
                return;
        }
        throw NoAttribute(o, name);
    }

    // ------------------------------------------------------------------ calls

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PyObject Call(PyObject callable, PyObject[] args, KwArgs? kw) => callable.Call(args, kw);

    public static PyObject Call(PyObject callable, params PyObject[] args) => callable.Call(args, null);

    /// <summary>Default call implementation: looks up __call__ on the type.</summary>
    public static PyObject CallSlow(PyObject o, PyObject[] args, KwArgs? kw)
    {
        var m = o.Type.Lookup("__call__");
        if (m != null && m != Py.None)
        {
            Interp.CheckStack();
            return CallBound(m, o, args, kw);
        }
        throw PyErr.TypeError($"'{o.Type.Name}' object is not callable");
    }

    /// <summary>Calls a descriptor found on the type with self prepended, without allocating a bound method.</summary>
    public static PyObject CallBound(PyObject descr, PyObject self, PyObject[] args, KwArgs? kw = null)
    {
        Interp.CheckStack();
        switch (descr)
        {
            case PyFunction f:
            {
                var n = new PyObject[args.Length + 1];
                n[0] = self;
                Array.Copy(args, 0, n, 1, args.Length);
                return f.Call(n, kw);
            }
            case PyBuiltinMethod bm:
                return bm.Fn(self, args, kw);
            case PyStaticMethod sm:
                return sm.Func.Call(args, kw);
            case PyClassMethod cm:
            {
                var n = new PyObject[args.Length + 1];
                n[0] = self.Type;
                Array.Copy(args, 0, n, 1, args.Length);
                return cm.Func.Call(n, kw);
            }
            default:
                return BindDescriptor(descr, self, self.Type).Call(args, kw);
        }
    }

    public static PyObject CallMethod(PyObject o, string name, params PyObject[] args) => GetAttr(o, name).Call(args, null);

    public static bool IsCallable(PyObject o) =>
        o is PyFunction or PyBuiltinFunction or PyBoundMethod or PyBuiltinBoundMethod or PyType or PyBuiltinMethod or PyStaticMethod
        || (o.Type.Lookup("__call__") is { } c && c != Py.None);

    // ------------------------------------------------------------------ items

    private static PythonException BadIndex(PyObject container, PyObject idx)
    {
        string kind = container is PyStr ? "string" : container.Type.Name;
        if (container is PyStr) return PyErr.TypeError($"string indices must be integers, not '{idx.Type.Name}'");
        return PyErr.TypeError($"{kind} indices must be integers or slices, not {idx.Type.Name}");
    }

    private static bool TryIndex(PyObject idx, out long i)
    {
        if (idx is PyInt pi)
        {
            if (pi.IsBig)
            {
                i = pi.Big.Sign < 0 ? long.MinValue : long.MaxValue;
                return true;
            }
            i = pi.V;
            return true;
        }
        if (idx is PyInstance && idx.Type.Lookup("__index__") is { } m)
        {
            i = ((PyInt)CallBound(m, idx, Array.Empty<PyObject>())).V;
            return true;
        }
        i = 0;
        return false;
    }

    public static PyObject GetItem(PyObject o, PyObject key)
    {
        if (o.IsBuiltinSubclass && UserMethod(o.Type, "__getitem__") is { } ug) return CallBound(ug, o, new[] { key });
        return GetItemBase(o, key);
    }

    /// <summary>Indexing of the builtin base value, ignoring __getitem__ overrides of subclasses.</summary>
    public static PyObject GetItemBase(PyObject o, PyObject key)
    {
        switch (o)
        {
            case PyList l:
                if (TryIndex(key, out long li))
                {
                    if (li < 0) li += l.Count;
                    if ((ulong)li >= (ulong)l.Count) throw PyErr.IndexError("list index out of range");
                    return l.Items[li];
                }
                if (key is PySlice ls) return l.Slice(ls);
                throw BadIndex(o, key);
            case PyTuple t:
                if (TryIndex(key, out long ti))
                {
                    if (ti < 0) ti += t.Items.Length;
                    if ((ulong)ti >= (ulong)t.Items.Length) throw PyErr.IndexError("tuple index out of range");
                    return t.Items[ti];
                }
                if (key is PySlice ts) return t.Slice(ts);
                throw BadIndex(o, key);
            case PyStr s:
                if (TryIndex(key, out long si))
                {
                    int len = s.Length;
                    if (si < 0) si += len;
                    if ((ulong)si >= (ulong)len) throw PyErr.IndexError("string index out of range");
                    return s.CharAt((int)si);
                }
                if (key is PySlice ss) return StrType.Slice(s, ss);
                throw BadIndex(o, key);
            case PyDict d:
            {
                var v = d.GetOrNull(key);
                if (v != null) return v;
                var miss = d.Missing(key);
                if (miss != null) return miss;
                throw PyErr.KeyError(key);
            }
            case PyRange r:
                if (TryIndex(key, out long ri))
                {
                    if (ri < 0) ri += r.Length;
                    if ((ulong)ri >= (ulong)r.Length) throw PyErr.IndexError("range object index out of range");
                    return PyInt.From(r.At(ri));
                }
                if (key is PySlice rs)
                {
                    rs.Indices(r.Length, out long start, out long stop, out long step, out long n);
                    long ns = r.At(start);
                    long nstep = r.Step * step;
                    return new PyRange(ns, ns + n * nstep, nstep);
                }
                throw PyErr.TypeError($"range indices must be integers or slices, not {key.Type.Name}");
            case PyType t2:
            {
                var cg = t2.Lookup("__class_getitem__");
                if (cg != null) return BindDescriptor(cg, t2, t2).Call(new[] { key }, null);
                if (t2.IsBuiltin) return t2;
                throw PyErr.TypeError($"type '{t2.Name}' is not subscriptable");
            }
        }
        var m = o.Type.Lookup("__getitem__");
        if (m != null && m != Py.None) return CallBound(m, o, new[] { key });
        throw PyErr.TypeError($"'{o.Type.Name}' object is not subscriptable");
    }

    public static void SetItem(PyObject o, PyObject key, PyObject value)
    {
        if (o.IsBuiltinSubclass && UserMethod(o.Type, "__setitem__") is { } us)
        {
            CallBound(us, o, new[] { key, value });
            return;
        }
        SetItemBase(o, key, value);
    }

    public static void SetItemBase(PyObject o, PyObject key, PyObject value)
    {
        switch (o)
        {
            case PyList l:
                if (TryIndex(key, out long li))
                {
                    if (li < 0) li += l.Count;
                    if ((ulong)li >= (ulong)l.Count) throw PyErr.IndexError("list assignment index out of range");
                    l.Items[li] = value;
                    return;
                }
                if (key is PySlice ls)
                {
                    l.SetSlice(ls, value);
                    return;
                }
                throw BadIndex(o, key);
            case PyDict d:
                d.SetItem(key, value);
                return;
        }
        var m = o.Type.Lookup("__setitem__");
        if (m != null && m != Py.None)
        {
            CallBound(m, o, new[] { key, value });
            return;
        }
        throw PyErr.TypeError($"'{o.Type.Name}' object does not support item assignment");
    }

    public static void DelItem(PyObject o, PyObject key)
    {
        if (o.IsBuiltinSubclass && UserMethod(o.Type, "__delitem__") is { } ud)
        {
            CallBound(ud, o, new[] { key });
            return;
        }
        DelItemBase(o, key);
    }

    public static void DelItemBase(PyObject o, PyObject key)
    {
        switch (o)
        {
            case PyList l:
                if (TryIndex(key, out long li))
                {
                    if (li < 0) li += l.Count;
                    if ((ulong)li >= (ulong)l.Count) throw PyErr.IndexError("list assignment index out of range");
                    l.RemoveAt((int)li);
                    return;
                }
                if (key is PySlice ls)
                {
                    l.DelSlice(ls);
                    return;
                }
                throw BadIndex(o, key);
            case PyDict d:
                if (!d.Remove(key)) throw PyErr.KeyError(key);
                return;
        }
        var m = o.Type.Lookup("__delitem__");
        if (m != null && m != Py.None)
        {
            CallBound(m, o, new[] { key });
            return;
        }
        throw PyErr.TypeError($"'{o.Type.Name}' object doesn't support item deletion");
    }

    // ------------------------------------------------------------------ iteration

    public static PyIterator GetIterator(PyObject o)
    {
        if (o.IsBuiltinSubclass && UserMethod(o.Type, "__iter__") is { } ui)
        {
            var r = CallBound(ui, o, Array.Empty<PyObject>());
            if (r is PyIterator rpi) return rpi;
            var rnext = r.Type.Lookup("__next__");
            if (rnext == null) throw PyErr.TypeError($"iter() returned non-iterator of type '{r.Type.Name}'");
            return new UserIterator(r, rnext);
        }
        switch (o)
        {
            case PyList l: return new ListIterator(l);
            case PyTuple t: return new ArrayIterator(t.Items, PyTypes.TupleIterator);
            case PyStr s: return new StrIterator(s);
            case PyRange r: return new RangeIterator(r);
            case PyDict d when d.Type == PyTypes.Dict || d.Type.Lookup("__iter__") is null or PyBuiltinMethod: return new DictIterator(d, DictViewKind.Keys);
            case PySetBase st: return new SetIterator(st.Table);
            case PyDictView v: return new DictIterator(v.Dict, v.Kind);
            case PyIterator it: return it;
        }
        var m = o.Type.Lookup("__iter__");
        if (m != null && m != Py.None)
        {
            var r = CallBound(m, o, Array.Empty<PyObject>());
            if (r is PyIterator pi) return pi;
            var next = r.Type.Lookup("__next__");
            if (next == null) throw PyErr.TypeError($"iter() returned non-iterator of type '{r.Type.Name}'");
            return new UserIterator(r, next);
        }
        if (o.Type.Lookup("__getitem__") != null) return new SeqIterator(o);
        throw PyErr.TypeError($"'{o.Type.Name}' object is not iterable");
    }

    public static bool IsIterable(PyObject o) =>
        o is PyList or PyTuple or PyStr or PyRange or PyDict or PySetBase or PyDictView or PyIterator ||
        o.Type.Lookup("__iter__") is { } i && i != Py.None || o.Type.Lookup("__getitem__") != null;

    /// <summary>Python level iter(): returns user iterators unwrapped.</summary>
    public static PyObject Iter(PyObject o)
    {
        var it = GetIterator(o);
        return it is UserIterator ui ? ui.Inner : it;
    }

    /// <summary>Python level next(); returns null when the iterator is exhausted.</summary>
    public static PyObject? Next(PyObject it)
    {
        if (it is PyIterator pi)
        {
            if (pi.MoveNext(out var v)) return v;
            if (pi is PyGenerator g && g.ReturnValue != Py.None) throw PyErr.StopIteration(g.ReturnValue);
            return null;
        }
        var m = it.Type.Lookup("__next__");
        if (m == null) throw PyErr.TypeError($"'{it.Type.Name}' object is not an iterator");
        try
        {
            return CallBound(m, it, Array.Empty<PyObject>());
        }
        catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.StopIteration) && e.Value.Args.Count == 0)
        {
            return null;
        }
    }

    public static IEnumerable<PyObject> Iterate(PyObject o)
    {
        switch (o)
        {
            case PyList l when !l.IsBuiltinSubclass:
                for (int i = 0; i < l.Count; i++) yield return l.Items[i];
                yield break;
            case PyTuple t when !t.IsBuiltinSubclass:
                foreach (var x in t.Items) yield return x;
                yield break;
        }
        var it = GetIterator(o);
        var ctx = Interp.CurrentOrNull;
        while (it.MoveNext(out var v))
        {
            ctx?.Tick();
            yield return v;
        }
    }

    public static PyObject[] ToArray(PyObject o)
    {
        switch (o)
        {
            case PyTuple t when !t.IsBuiltinSubclass:
                return t.Items;
            case PyList l when !l.IsBuiltinSubclass:
                return l.ToArray();
        }
        var list = new List<PyObject>();
        foreach (var v in Iterate(o))
        {
            list.Add(v);
            if (list.Count > Limits.MaxCollection) Limits.CheckSize(list.Count);
        }
        return list.ToArray();
    }

    public static PyList ToList(PyObject o)
    {
        var l = new PyList();
        l.Extend(o);
        return l;
    }

    // ------------------------------------------------------------------ len and contains

    public static long Len(PyObject o)
    {
        if (o.IsBuiltinSubclass && UserMethod(o.Type, "__len__") is { } ul)
        {
            var r = CallBound(ul, o, Array.Empty<PyObject>());
            var ri = r as PyInt ?? throw PyErr.TypeError($"'{r.Type.Name}' object cannot be interpreted as an integer");
            if (ri.Sign < 0) throw PyErr.ValueError("__len__() should return >= 0");
            return ri.ToLong("cannot fit 'int' into an index-sized integer");
        }
        return LenBase(o);
    }

    public static long LenBase(PyObject o)
    {
        switch (o)
        {
            case PyStr s: return s.Length;
            case PyList l: return l.Count;
            case PyTuple t: return t.Items.Length;
            case PyDict d: return d.Count;
            case PySetBase st: return st.Count;
            case PyRange r: return r.Length;
            case PyDictView v: return v.Dict.Count;
        }
        var m = o.Type.Lookup("__len__");
        if (m != null && m != Py.None)
        {
            var r = CallBound(m, o, Array.Empty<PyObject>());
            var ri = r as PyInt ?? throw PyErr.TypeError($"'{r.Type.Name}' object cannot be interpreted as an integer");
            if (ri.Sign < 0) throw PyErr.ValueError("__len__() should return >= 0");
            return ri.ToLong("cannot fit 'int' into an index-sized integer");
        }
        throw PyErr.TypeError($"object of type '{o.Type.Name}' has no len()");
    }

    public static bool Contains(PyObject container, PyObject item)
    {
        if (container.IsBuiltinSubclass && UserMethod(container.Type, "__contains__") is { } uc)
            return Truthy(CallBound(uc, container, new[] { item }));
        return ContainsBase(container, item);
    }

    public static bool ContainsBase(PyObject container, PyObject item)
    {
        switch (container)
        {
            case PyList l:
                for (int i = 0; i < l.Count; i++)
                    if (EqBool(l.Items[i], item)) return true;
                return false;
            case PyTuple t:
                foreach (var x in t.Items)
                    if (EqBool(x, item)) return true;
                return false;
            case PyStr s:
                if (item is not PyStr sub) throw PyErr.TypeError($"'in <string>' requires string as left operand, not {item.Type.Name}");
                return s.V.Contains(sub.V, StringComparison.Ordinal);
            case PyDict d when d.Type == PyTypes.Dict || d.Type.Lookup("__contains__") is null or PyBuiltinMethod:
                return d.ContainsKey(item);
            case PySetBase st:
                return st.Table.Contains(item);
            case PyRange r:
                if (item is PyInt ri && !ri.IsBig && item is not PyBool) return r.ContainsValue(ri.V);
                foreach (var x in Iterate(r))
                    if (EqBool(x, item)) return true;
                return false;
            case PyDictView v:
                switch (v.Kind)
                {
                    case DictViewKind.Keys:
                        return v.Dict.ContainsKey(item);
                    case DictViewKind.Items:
                        if (item is not PyTuple pt || pt.Count != 2) return false;
                        var val = v.Dict.GetOrNull(pt.Items[0]);
                        return val != null && EqBool(val, pt.Items[1]);
                    default:
                        foreach (var (_, dv) in v.Dict.Pairs())
                            if (EqBool(dv, item)) return true;
                        return false;
                }
        }
        var m = container.Type.Lookup("__contains__");
        if (m != null && m != Py.None) return Truthy(CallBound(m, container, new[] { item }));
        foreach (var x in Iterate(container))
            if (EqBool(x, item)) return true;
        return false;
    }

    // ------------------------------------------------------------------ numeric conversions

    /// <summary>operator.index(): int or an object with __index__.</summary>
    public static PyInt Index(PyObject o)
    {
        if (o is PyInt i) return i.IsBuiltinSubclass ? (PyInt)Subclassing.Plain(i) : i;
        var m = o.Type.Lookup("__index__");
        if (m != null && m != Py.None)
        {
            var r = CallBound(m, o, Array.Empty<PyObject>());
            if (r is PyInt ri) return ri;
            throw PyErr.TypeError($"__index__ returned non-int (type {r.Type.Name})");
        }
        throw PyErr.TypeError($"'{o.Type.Name}' object cannot be interpreted as an integer");
    }

    public static long IndexLong(PyObject o)
    {
        var i = Index(o);
        if (i.IsBig) throw PyErr.IndexError("cannot fit 'int' into an index-sized integer");
        return i.V;
    }

    public static double ToDouble(PyObject o)
    {
        switch (o)
        {
            case PyFloat f: return f.V;
            case PyInt i: return IntMath.ToDouble(i);
        }
        var m = o.Type.Lookup("__float__");
        if (m != null && m != Py.None)
        {
            var r = CallBound(m, o, Array.Empty<PyObject>());
            if (r is PyFloat rf) return rf.V;
            throw PyErr.TypeError($"{o.Type.Name}.__float__ returned non-float (type {r.Type.Name})");
        }
        var idx = o.Type.Lookup("__index__");
        if (idx != null && idx != Py.None) return IntMath.ToDouble(Index(o));
        throw PyErr.TypeError($"must be real number, not {o.Type.Name}");
    }

    /// <summary>int(x) for a non string argument.</summary>
    public static PyInt ToInt(PyObject o)
    {
        if (o.IsBuiltinSubclass)
        {
            var conv = UserIntConversion(o.Type, out var convName);
            if (conv != null) return CallIntConversion(conv, o, convName);
            if (o is PyInt si) return (PyInt)Subclassing.Plain(si);
        }
        switch (o)
        {
            case PyBool b: return PyInt.From(b.V);
            case PyInt i: return i;
            case PyFloat f: return FloatMath.ToInt(f.V);
        }
        foreach (var name in new[] { "__int__", "__index__", "__trunc__" })
        {
            var m = o.Type.Lookup(name);
            if (m != null && m != Py.None)
            {
                var r = CallBound(m, o, Array.Empty<PyObject>());
                if (r is PyInt ri) return ri is PyBool ? PyInt.From(ri.V) : ri;
                throw PyErr.TypeError($"{name} returned non-int (type {r.Type.Name})");
            }
        }
        throw PyErr.TypeError($"int() argument must be a string, a bytes-like object or a real number, not '{o.Type.Name}'");
    }

    /// <summary>An __int__, __index__ or __trunc__ defined in Python for a builtin subclass, or null.</summary>
    public static PyObject? UserIntConversion(PyType t, out string name)
    {
        foreach (var n in new[] { "__int__", "__index__", "__trunc__" })
        {
            var m = UserMethod(t, n);
            if (m != null)
            {
                name = n;
                return m;
            }
        }
        name = "";
        return null;
    }

    private static PyInt CallIntConversion(PyObject m, PyObject o, string name)
    {
        var r = CallBound(m, o, Array.Empty<PyObject>());
        if (r is PyBool rb) return PyInt.From(rb.V);
        if (r is PyInt ri) return ri.IsBuiltinSubclass ? (PyInt)Subclassing.Plain(ri) : ri;
        throw PyErr.TypeError($"{name} returned non-int (type {r.Type.Name})");
    }

    public static BigInteger ToBig(PyObject o) => Index(o).ToBig();

    // ------------------------------------------------------------------ isinstance

    public static bool IsInstance(PyObject o, PyObject cls)
    {
        if (cls is PyType t) return o.Type.IsSubtypeOf(t);
        if (cls is PyTuple tup)
        {
            foreach (var c in tup.Items)
                if (IsInstance(o, c)) return true;
            return false;
        }
        var m = cls.Type.Lookup("__instancecheck__");
        if (m != null) return Truthy(CallBound(m, cls, new[] { o }));
        throw PyErr.TypeError("isinstance() arg 2 must be a type, a tuple of types, or a union");
    }

    public static bool IsSubclass(PyObject sub, PyObject cls)
    {
        if (sub is not PyType st) throw PyErr.TypeError("issubclass() arg 1 must be a class");
        if (cls is PyType t) return st.IsSubtypeOf(t);
        if (cls is PyTuple tup)
        {
            foreach (var c in tup.Items)
                if (IsSubclass(sub, c)) return true;
            return false;
        }
        throw PyErr.TypeError("issubclass() arg 2 must be a class, a tuple of classes, or a union");
    }
}

/// <summary>Exposes a closure cell object (rarely needed; for completeness of repr).</summary>
internal sealed class PyCellObject : PyObject
{
    internal override PyType Type => PyTypes.Object;
}
