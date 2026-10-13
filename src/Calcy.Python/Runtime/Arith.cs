using System.Runtime.CompilerServices;

namespace Calcy.Python.Runtime;

internal enum BinOp : byte
{
    Add,
    Sub,
    Mul,
    MatMul,
    TrueDiv,
    FloorDiv,
    Mod,
    Pow,
    LShift,
    RShift,
    And,
    Or,
    Xor,
}

/// <summary>Arithmetic operators with Python semantics and user dunder dispatch.</summary>
internal static class Arith
{
    private static readonly string[] Names =
        { "__add__", "__sub__", "__mul__", "__matmul__", "__truediv__", "__floordiv__", "__mod__", "__pow__", "__lshift__", "__rshift__", "__and__", "__or__", "__xor__" };

    private static readonly string[] RNames =
        { "__radd__", "__rsub__", "__rmul__", "__rmatmul__", "__rtruediv__", "__rfloordiv__", "__rmod__", "__rpow__", "__rlshift__", "__rrshift__", "__rand__", "__ror__", "__rxor__" };

    private static readonly string[] INames =
        { "__iadd__", "__isub__", "__imul__", "__imatmul__", "__itruediv__", "__ifloordiv__", "__imod__", "__ipow__", "__ilshift__", "__irshift__", "__iand__", "__ior__", "__ixor__" };

    public static readonly string[] Symbols = { "+", "-", "*", "@", "/", "//", "%", "**", "<<", ">>", "&", "|", "^" };

    public static PyObject Binary(BinOp op, PyObject a, PyObject b) => op switch
    {
        BinOp.Add => Add(a, b),
        BinOp.Sub => Sub(a, b),
        BinOp.Mul => Mul(a, b),
        BinOp.TrueDiv => TrueDiv(a, b),
        BinOp.FloorDiv => FloorDiv(a, b),
        BinOp.Mod => Mod(a, b),
        BinOp.Pow => Pow(a, b),
        BinOp.LShift => LShift(a, b),
        BinOp.RShift => RShift(a, b),
        BinOp.And => BitAnd(a, b),
        BinOp.Or => BitOr(a, b),
        BinOp.Xor => BitXor(a, b),
        _ => Fallback(op, a, b, false),
    };

    /// <summary>The builtin result of a binary operator, or null when the builtin types do not support it.</summary>
    public static PyObject? BinaryCore(BinOp op, PyObject a, PyObject b) => op switch
    {
        BinOp.Add => AddCore(a, b),
        BinOp.Sub => SubCore(a, b),
        BinOp.Mul => MulCore(a, b),
        BinOp.TrueDiv => TrueDivCore(a, b),
        BinOp.FloorDiv => FloorDivCore(a, b),
        BinOp.Mod => ModCore(a, b),
        BinOp.Pow => PowCore(a, b),
        BinOp.LShift => LShiftCore(a, b),
        BinOp.RShift => RShiftCore(a, b),
        BinOp.And => BitAndCore(a, b),
        BinOp.Or => BitOrCore(a, b),
        BinOp.Xor => BitXorCore(a, b),
        _ => null,
    };

    public static string DunderName(BinOp op, bool reflected) => reflected ? RNames[(int)op] : Names[(int)op];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryFloats(PyObject a, PyObject b, out double x, out double y)
    {
        if (a is PyFloat fa)
        {
            x = fa.V;
            if (b is PyFloat fb)
            {
                y = fb.V;
                return true;
            }
            if (b is PyInt ib)
            {
                y = IntMath.ToDouble(ib);
                return true;
            }
        }
        else if (b is PyFloat fb2 && a is PyInt ia)
        {
            x = IntMath.ToDouble(ia);
            y = fb2.V;
            return true;
        }
        x = y = 0;
        return false;
    }

    private static bool IsNumber(PyObject o) => o is PyInt or PyFloat or PyComplex;

    private static (double re, double im) AsComplex(PyObject o) => o switch
    {
        PyComplex c => (c.Real, c.Imag),
        PyFloat f => (f.V, 0),
        PyInt i => (IntMath.ToDouble(i), 0),
        _ => throw new InvalidOperationException(),
    };

    private static PyObject? ComplexOp(BinOp op, PyObject a, PyObject b)
    {
        if (!(a is PyComplex || b is PyComplex) || !IsNumber(a) || !IsNumber(b)) return null;
        var (ar, ai) = AsComplex(a);
        var (br, bi) = AsComplex(b);
        switch (op)
        {
            case BinOp.Add: return new PyComplex(ar + br, ai + bi);
            case BinOp.Sub: return new PyComplex(ar - br, ai - bi);
            case BinOp.Mul: return new PyComplex(ar * br - ai * bi, ar * bi + ai * br);
            case BinOp.TrueDiv: return ComplexMath.Div(ar, ai, br, bi);
            case BinOp.Pow: return ComplexMath.Pow(ar, ai, br, bi);
        }
        return null;
    }

    public static PyObject Add(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.Add, a, b) is { } u) return u;
        return AddCore(a, b) ?? Fallback(BinOp.Add, a, b, false);
    }

    internal static PyObject? AddCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.Add(ia, ib);
        if (TryFloats(a, b, out var x, out var y)) return new PyFloat(x + y);
        if (a is PyStr sa && b is PyStr sb)
        {
            if (sa.V.Length == 0) return Subclassing.Plain(sb);
            if (sb.V.Length == 0) return Subclassing.Plain(sa);
            long n = (long)sa.V.Length + sb.V.Length;
            if (n > Limits.MaxCollection) Limits.CheckStringLength(n);
            return PyStr.New(string.Concat(sa.V, sb.V));
        }
        if (a is PyList la && b is PyList lb)
        {
            long n = (long)la.Count + lb.Count;
            Limits.CheckSize(n);
            var arr = new PyObject[n];
            Array.Copy(la.Items, arr, la.Count);
            Array.Copy(lb.Items, 0, arr, la.Count, lb.Count);
            return PyList.Wrap(arr);
        }
        if (a is PyTuple ta && b is PyTuple tb)
        {
            if (tb.Items.Length == 0) return Subclassing.Plain(ta);
            if (ta.Items.Length == 0) return Subclassing.Plain(tb);
            long n = (long)ta.Items.Length + tb.Items.Length;
            Limits.CheckSize(n);
            var arr = new PyObject[n];
            Array.Copy(ta.Items, arr, ta.Items.Length);
            Array.Copy(tb.Items, 0, arr, ta.Items.Length, tb.Items.Length);
            return new PyTuple(arr);
        }
        return ComplexOp(BinOp.Add, a, b);
    }

    public static PyObject Sub(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.Sub, a, b) is { } u) return u;
        return SubCore(a, b) ?? Fallback(BinOp.Sub, a, b, false);
    }

    internal static PyObject? SubCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.Sub(ia, ib);
        if (TryFloats(a, b, out var x, out var y)) return new PyFloat(x - y);
        if (a is PySetBase sa && b is PySetBase sb) return SetDifference(sa, sb);
        return ComplexOp(BinOp.Sub, a, b);
    }

    public static PyObject Mul(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.Mul, a, b) is { } u) return u;
        return MulCore(a, b) ?? Fallback(BinOp.Mul, a, b, false);
    }

    internal static PyObject? MulCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.Mul(ia, ib);
        if (TryFloats(a, b, out var x, out var y)) return new PyFloat(x * y);
        if (b is PyInt n)
        {
            switch (a)
            {
                case PyStr s: return StrType.Repeat(s, RepeatCount(n));
                case PyList l: return RepeatList(l, RepeatCount(n));
                case PyTuple t: return RepeatTuple(t, RepeatCount(n));
            }
        }
        else if (a is PyInt n2)
        {
            switch (b)
            {
                case PyStr s: return StrType.Repeat(s, RepeatCount(n2));
                case PyList l: return RepeatList(l, RepeatCount(n2));
                case PyTuple t: return RepeatTuple(t, RepeatCount(n2));
            }
        }
        return ComplexOp(BinOp.Mul, a, b);
    }

    private static long RepeatCount(PyInt n)
    {
        if (n.IsBig)
        {
            if (n.Big.Sign < 0) return 0;
            throw PyErr.OverflowError("cannot fit 'int' into an index-sized integer");
        }
        return n.V;
    }

    public static PyList RepeatList(PyList l, long n)
    {
        if (n <= 0 || l.Count == 0) return new PyList();
        long total = l.Count * n;
        if (n > int.MaxValue || total > int.MaxValue) Limits.CheckSize(long.MaxValue);
        Limits.CheckSize(total);
        var arr = new PyObject[total];
        for (long i = 0; i < n; i++) Array.Copy(l.Items, 0, arr, i * l.Count, l.Count);
        return PyList.Wrap(arr);
    }

    private static PyTuple RepeatTuple(PyTuple t, long n)
    {
        if (n <= 0 || t.Items.Length == 0) return PyTuple.Empty;
        if (n == 1) return (PyTuple)Subclassing.Plain(t);
        long total = t.Items.Length * n;
        if (n > int.MaxValue || total > int.MaxValue) Limits.CheckSize(long.MaxValue);
        Limits.CheckSize(total);
        var arr = new PyObject[total];
        for (long i = 0; i < n; i++) Array.Copy(t.Items, 0, arr, i * t.Items.Length, t.Items.Length);
        return new PyTuple(arr);
    }

    public static PyObject TrueDiv(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.TrueDiv, a, b) is { } u) return u;
        return TrueDivCore(a, b) ?? Fallback(BinOp.TrueDiv, a, b, false);
    }

    internal static PyObject? TrueDivCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.TrueDiv(ia, ib);
        if (TryFloats(a, b, out var x, out var y)) return FloatMath.Div(x, y);
        return ComplexOp(BinOp.TrueDiv, a, b);
    }

    public static PyObject FloorDiv(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.FloorDiv, a, b) is { } u) return u;
        return FloorDivCore(a, b) ?? Fallback(BinOp.FloorDiv, a, b, false);
    }

    internal static PyObject? FloorDivCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.FloorDiv(ia, ib);
        if (TryFloats(a, b, out var x, out var y)) return new PyFloat(FloatMath.FloorDiv(x, y));
        return ComplexOp(BinOp.FloorDiv, a, b);
    }

    public static PyObject Mod(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.Mod, a, b) is { } u) return u;
        return ModCore(a, b) ?? Fallback(BinOp.Mod, a, b, false);
    }

    internal static PyObject? ModCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.Mod(ia, ib);
        if (TryFloats(a, b, out var x, out var y)) return new PyFloat(FloatMath.Mod(x, y));
        if (a is PyStr s)
        {
            if (b is PyInstance && UserOp(b.Type, "__rmod__") != null) return null;
            return PyStr.New(PercentFormat.Format(s.V, b));
        }
        return ComplexOp(BinOp.Mod, a, b);
    }

    public static PyObject Pow(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.Pow, a, b) is { } u) return u;
        return PowCore(a, b) ?? Fallback(BinOp.Pow, a, b, false);
    }

    internal static PyObject? PowCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.Pow(ia, ib);
        if (TryFloats(a, b, out var x, out var y)) return FloatMath.Pow(x, y);
        return ComplexOp(BinOp.Pow, a, b);
    }

    public static PyObject LShift(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.LShift, a, b) is { } u) return u;
        return LShiftCore(a, b) ?? Fallback(BinOp.LShift, a, b, false);
    }

    internal static PyObject? LShiftCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.LShift(ia, ib);
        return ComplexOp(BinOp.LShift, a, b);
    }

    public static PyObject RShift(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.RShift, a, b) is { } u) return u;
        return RShiftCore(a, b) ?? Fallback(BinOp.RShift, a, b, false);
    }

    internal static PyObject? RShiftCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib) return IntMath.RShift(ia, ib);
        return ComplexOp(BinOp.RShift, a, b);
    }

    public static PyObject BitAnd(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.And, a, b) is { } u) return u;
        return BitAndCore(a, b) ?? Fallback(BinOp.And, a, b, false);
    }

    internal static PyObject? BitAndCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib)
        {
            if (a is PyBool ba && b is PyBool bb) return Py.Bool(ba.Value && bb.Value);
            return IntMath.And(ia, ib);
        }
        if (a is PySetBase sa && b is PySetBase sb) return SetIntersection(sa, sb);
        return ComplexOp(BinOp.And, a, b);
    }

    public static PyObject BitOr(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.Or, a, b) is { } u) return u;
        return BitOrCore(a, b) ?? Fallback(BinOp.Or, a, b, false);
    }

    internal static PyObject? BitOrCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib)
        {
            if (a is PyBool ba && b is PyBool bb) return Py.Bool(ba.Value || bb.Value);
            return IntMath.Or(ia, ib);
        }
        if (a is PySetBase sa && b is PySetBase sb) return SetUnion(sa, sb);
        if (a is PyDict da && b is PyDict db && (!da.Type.IsBuiltin || da.Type == PyTypes.Dict) && (!db.Type.IsBuiltin || db.Type == PyTypes.Dict))
        {
            var r = da.Copy();
            db.CopyTo(r);
            return r;
        }
        return ComplexOp(BinOp.Or, a, b);
    }

    public static PyObject BitXor(PyObject a, PyObject b)
    {
        if ((a.IsBuiltinSubclass || b.IsBuiltinSubclass) && UserBinary(BinOp.Xor, a, b) is { } u) return u;
        return BitXorCore(a, b) ?? Fallback(BinOp.Xor, a, b, false);
    }

    internal static PyObject? BitXorCore(PyObject a, PyObject b)
    {
        if (a is PyInt ia && b is PyInt ib)
        {
            if (a is PyBool ba && b is PyBool bb) return Py.Bool(ba.Value != bb.Value);
            return IntMath.Xor(ia, ib);
        }
        if (a is PySetBase sa && b is PySetBase sb) return SetSymDiff(sa, sb);
        return ComplexOp(BinOp.Xor, a, b);
    }

    private static PyObject? UserOp(PyType t, string name)
    {
        var m = t.Lookup(name);
        if (m == null || m == Py.None) return null;
        if (m is PyBuiltinMethod bm && (bm.Owner == PyTypes.Object || bm.Owner.Layout == LayoutKind.Exception || IsCore(bm.Owner))) return null;
        return m;
    }

    private static bool IsCore(PyType t) =>
        t == PyTypes.Str || t == PyTypes.Int || t == PyTypes.Float || t == PyTypes.List || t == PyTypes.Tuple || t == PyTypes.Dict ||
        t == PyTypes.Set || t == PyTypes.FrozenSet || t == PyTypes.Complex || t == PyTypes.Bool || t == PyTypes.Range;

    private static PyObject? UserBinary(BinOp op, PyObject a, PyObject b)
    {
        int k = (int)op;
        var ta = a.Type;
        var tb = b.Type;
        var fa = UserOp(ta, Names[k]);
        var fb = ReferenceEquals(ta, tb) ? null : UserOp(tb, RNames[k]);
        if (fb != null && tb.IsSubtypeOf(ta))
        {
            var r = Ops.CallBound(fb, b, new[] { a });
            if (r != Py.NotImplemented) return r;
            fb = null;
        }
        if (fa != null)
        {
            var r = Ops.CallBound(fa, a, new[] { b });
            if (r != Py.NotImplemented) return r;
        }
        if (fb != null)
        {
            var r = Ops.CallBound(fb, b, new[] { a });
            if (r != Py.NotImplemented) return r;
        }
        return null;
    }

    private static PyObject Fallback(BinOp op, PyObject a, PyObject b, bool inplace)
    {
        var c = ComplexOp(op, a, b);
        if (c != null) return c;
        if ((a is PyDictView va && va.Kind != DictViewKind.Values) || (b is PyDictView vb && vb.Kind != DictViewKind.Values))
        {
            if (op is BinOp.And or BinOp.Or or BinOp.Sub or BinOp.Xor)
            {
                var sa = PySet.From(a);
                var sb = PySet.From(b);
                return Binary(op, sa, sb);
            }
        }
        var u = UserBinary(op, a, b);
        if (u != null) return u;
        throw OpError(op, a, b, inplace);
    }

    private static PythonException OpError(BinOp op, PyObject a, PyObject b, bool inplace)
    {
        string sym = Symbols[(int)op] + (inplace ? "=" : "");
        if (op == BinOp.Add && !inplace)
        {
            if (a is PyStr) return PyErr.TypeError($"can only concatenate str (not \"{b.Type.Name}\") to str");
            if (a is PyList) return PyErr.TypeError($"can only concatenate list (not \"{b.Type.Name}\") to list");
            if (a is PyTuple) return PyErr.TypeError($"can only concatenate tuple (not \"{b.Type.Name}\") to tuple");
        }
        if (op == BinOp.Mul && (a is PyStr or PyList or PyTuple || b is PyStr or PyList or PyTuple))
        {
            var other = a is PyStr or PyList or PyTuple ? b : a;
            return PyErr.TypeError($"can't multiply sequence by non-int of type '{other.Type.Name}'");
        }
        if (op == BinOp.Pow) sym = inplace ? "**=" : "** or pow()";
        return PyErr.TypeError($"unsupported operand type(s) for {sym}: '{a.Type.Name}' and '{b.Type.Name}'");
    }

    public static PyObject Inplace(BinOp op, PyObject a, PyObject b)
    {
        if (a.IsBuiltinSubclass && UserOp(a.Type, INames[(int)op]) is { } im)
        {
            var r = Ops.CallBound(im, a, new[] { b });
            if (r != Py.NotImplemented) return r;
            return Binary(op, a, b);
        }
        switch (a)
        {
            case PyInt when b is PyInt:
            case PyFloat:
            case PyStr:
            case PyTuple:
                return Binary(op, a, b);
            case PyList l:
                if (op == BinOp.Add)
                {
                    if (b is not (PyList or PyTuple) && b.Type.Lookup("__iter__") == null && b.Type.Lookup("__getitem__") == null)
                        throw PyErr.TypeError($"'{b.Type.Name}' object is not iterable");
                    l.Extend(b);
                    return l;
                }
                if (op == BinOp.Mul && b is PyInt n)
                {
                    var r = RepeatList(l, RepeatCount(n));
                    l.Items = r.Items;
                    l.Count = r.Count;
                    return l;
                }
                break;
            case PySet s when b is PySetBase sb:
                switch (op)
                {
                    case BinOp.Or:
                        s.Table.Merge(sb.Table);
                        return s;
                    case BinOp.And:
                        s.Table = ((PySetBase)SetIntersection(s, sb)).Table;
                        return s;
                    case BinOp.Sub:
                        foreach (var k in sb.Table.Keys().ToList()) s.Table.Discard(k);
                        return s;
                    case BinOp.Xor:
                        foreach (var k in sb.Table.Keys().ToList())
                            if (!s.Table.Discard(k)) s.Table.Add(k);
                        return s;
                }
                break;
            case PyDict d when op == BinOp.Or && (d.Type == PyTypes.Dict || d is PyDictSub):
                d.Update(b);
                return d;
        }
        var m = UserOp(a.Type, INames[(int)op]);
        if (m != null)
        {
            var r = Ops.CallBound(m, a, new[] { b });
            if (r != Py.NotImplemented) return r;
        }
        try
        {
            return Binary(op, a, b);
        }
        catch (PythonException e) when (e.Value.Type == Exc.TypeError && e.Value.Args.Count == 1 &&
                                        e.Value.Args.Items[0] is PyStr ms && ms.V.StartsWith("unsupported operand type(s) for ", StringComparison.Ordinal))
        {
            throw OpError(op, a, b, true);
        }
    }

    // ------------------------------------------------------------------ unary

    public static PyObject Neg(PyObject a)
    {
        if (a.IsBuiltinSubclass && UserOp(a.Type, "__neg__") is { } um) return Ops.CallBound(um, a, Array.Empty<PyObject>());
        switch (a)
        {
            case PyInt i: return IntMath.Neg(i);
            case PyFloat f: return new PyFloat(-f.V);
            case PyComplex c: return new PyComplex(-c.Real, -c.Imag);
        }
        var m = UserOp(a.Type, "__neg__");
        if (m != null) return Ops.CallBound(m, a, Array.Empty<PyObject>());
        throw PyErr.TypeError($"bad operand type for unary -: '{a.Type.Name}'");
    }

    public static PyObject Pos(PyObject a)
    {
        if (a.IsBuiltinSubclass && UserOp(a.Type, "__pos__") is { } um) return Ops.CallBound(um, a, Array.Empty<PyObject>());
        switch (a)
        {
            case PyBool b: return PyInt.From(b.V);
            case PyInt:
            case PyFloat:
            case PyComplex:
                return Subclassing.Plain(a);
        }
        var m = UserOp(a.Type, "__pos__");
        if (m != null) return Ops.CallBound(m, a, Array.Empty<PyObject>());
        throw PyErr.TypeError($"bad operand type for unary +: '{a.Type.Name}'");
    }

    public static PyObject Invert(PyObject a)
    {
        if (a.IsBuiltinSubclass && UserOp(a.Type, "__invert__") is { } um) return Ops.CallBound(um, a, Array.Empty<PyObject>());
        if (a is PyInt i) return IntMath.Invert(i);
        var m = UserOp(a.Type, "__invert__");
        if (m != null) return Ops.CallBound(m, a, Array.Empty<PyObject>());
        throw PyErr.TypeError($"bad operand type for unary ~: '{a.Type.Name}'");
    }

    public static PyObject Abs(PyObject a)
    {
        if (a.IsBuiltinSubclass && UserOp(a.Type, "__abs__") is { } um) return Ops.CallBound(um, a, Array.Empty<PyObject>());
        switch (a)
        {
            case PyInt i: return IntMath.Abs(i);
            case PyFloat f: return new PyFloat(Math.Abs(f.V));
            case PyComplex cx: return new PyFloat(ComplexMath.Abs(cx.Real, cx.Imag));
        }
        var m = UserOp(a.Type, "__abs__");
        if (m != null) return Ops.CallBound(m, a, Array.Empty<PyObject>());
        throw PyErr.TypeError($"bad operand type for abs(): '{a.Type.Name}'");
    }

    public static PyObject DivMod(PyObject a, PyObject b)
    {
        if (a.IsBuiltinSubclass || b.IsBuiltinSubclass)
        {
            if (UserOp(a.Type, "__divmod__") is { } da && Ops.CallBound(da, a, new[] { b }) is var dr && dr != Py.NotImplemented) return dr;
            if (UserOp(b.Type, "__rdivmod__") is { } db && Ops.CallBound(db, b, new[] { a }) is var rr && rr != Py.NotImplemented) return rr;
        }
        if (a is PyInt ia && b is PyInt ib)
        {
            var (q, r) = IntMath.DivMod(ia, ib);
            return new PyTuple(new PyObject[] { q, r });
        }
        if (TryFloats(a, b, out var x, out var y))
        {
            var (d, m) = FloatMath.DivMod(x, y);
            return new PyTuple(new PyObject[] { new PyFloat(d), new PyFloat(m) });
        }
        var fa = UserOp(a.Type, "__divmod__");
        if (fa != null)
        {
            var r = Ops.CallBound(fa, a, new[] { b });
            if (r != Py.NotImplemented) return r;
        }
        var fb = UserOp(b.Type, "__rdivmod__");
        if (fb != null)
        {
            var r = Ops.CallBound(fb, b, new[] { a });
            if (r != Py.NotImplemented) return r;
        }
        throw PyErr.TypeError($"unsupported operand type(s) for divmod(): '{a.Type.Name}' and '{b.Type.Name}'");
    }

    // ------------------------------------------------------------------ sets

    public static PyObject SetUnion(PySetBase a, PySetBase b)
    {
        var r = a.NewSame(a.Table.Copy());
        r.Table.Merge(b.Table);
        return r;
    }

    public static PyObject SetIntersection(PySetBase a, PySetBase b)
    {
        var result = a.NewSame(new SetTable());
        PySetBase big = a, small = b;
        if (b.Count > a.Count)
        {
            big = b;
            small = a;
        }
        foreach (var (k, h) in small.Table.Entries())
            if (big.Table.Contains(k, h)) result.Table.Add(k, h);
        return result;
    }

    public static PyObject SetDifference(PySetBase a, PySetBase b)
    {
        if ((a.Count >> 2) > b.Count)
        {
            var r = a.NewSame(a.Table.Copy());
            foreach (var k in b.Table.Keys().ToList()) r.Table.Discard(k);
            return r;
        }
        var result = a.NewSame(new SetTable());
        foreach (var (k, h) in a.Table.Entries())
            if (!b.Table.Contains(k, h)) result.Table.Add(k, h);
        return result;
    }

    public static PyObject SetSymDiff(PySetBase a, PySetBase b)
    {
        var result = a.NewSame(b.Table.Copy());
        foreach (var (k, h) in a.Table.Entries())
        {
            if (result.Table.Contains(k, h)) result.Table.Discard(k);
            else result.Table.Add(k, h);
        }
        return result;
    }
}
