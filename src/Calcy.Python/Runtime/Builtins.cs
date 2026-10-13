using Calcy.Python.Modules;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Calcy.Python.Runtime;

/// <summary>The builtins namespace (shared, immutable).</summary>
internal static class Builtins
{
    public static readonly Dictionary<string, PyObject> Table = new(StringComparer.Ordinal);

    private static readonly Sig SortedSig = new("sorted", 1, 1, 1, "iterable", "key", "reverse");
    private static readonly Sig SumSig = new("sum", 1, 1, 2, "iterable", "start");
    private static readonly Sig RoundSig = new("round", 1, 0, 2, "number", "ndigits");
    private static readonly Sig PowSig = new("pow", 2, 0, 3, "base", "exp", "mod");
    private static readonly Sig EnumerateSig = new("enumerate", 1, 0, 2, "iterable", "start");

    static Builtins()
    {
        void T(string name, PyType t) => Table[name] = t;
        void F(string name, Func<PyObject[], KwArgs?, PyObject> fn) => Table[name] = new PyBuiltinFunction(name, fn, "builtins");

        T("object", PyTypes.Object);
        T("type", PyTypes.Type);
        T("int", PyTypes.Int);
        T("bool", PyTypes.Bool);
        T("float", PyTypes.Float);
        T("complex", PyTypes.Complex);
        T("str", PyTypes.Str);
        T("list", PyTypes.List);
        T("tuple", PyTypes.Tuple);
        T("dict", PyTypes.Dict);
        T("set", PyTypes.Set);
        T("frozenset", PyTypes.FrozenSet);
        T("range", PyTypes.Range);
        T("slice", PyTypes.Slice);
        T("property", PyTypes.Property);
        T("staticmethod", PyTypes.StaticMethod);
        T("classmethod", PyTypes.ClassMethod);
        T("super", PyTypes.Super);
        T("map", PyTypes.Map);
        T("filter", PyTypes.Filter);
        T("zip", PyTypes.Zip);
        T("enumerate", PyTypes.Enumerate);
        T("reversed", PyTypes.Reversed);
        foreach (var e in Exc.All) T(e.Name, e);
        Table["EnvironmentError"] = Exc.OSError;
        Table["IOError"] = Exc.OSError;
        Table["None"] = Py.None;
        Table["True"] = PyBool.True;
        Table["False"] = PyBool.False;
        Table["NotImplemented"] = Py.NotImplemented;
        Table["Ellipsis"] = Py.Ellipsis;
        Table["__debug__"] = PyBool.True;

        F("print", Print);
        F("input", Input);
        F("len", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "len");
            return PyInt.From(Ops.Len(a[0]));
        });
        F("repr", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "repr");
            return PyStr.New(Ops.Repr(a[0]));
        });
        F("ascii", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "ascii");
            return PyStr.New(Ops.Ascii(a[0]));
        });
        F("abs", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "abs");
            return Arith.Abs(a[0]);
        });
        F("min", (a, kw) => MinMax(a, kw, "min", CompareOp.Lt));
        F("max", (a, kw) => MinMax(a, kw, "max", CompareOp.Gt));
        F("sum", Sum);
        F("round", Round);
        F("sorted", (a, kw) =>
        {
            var b = SortedSig.Bind(a, kw);
            var l = Ops.ToList(b[0]!);
            l.Sort(b[1], b[2] != null && Ops.Truthy(b[2]!));
            return l;
        });
        F("any", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "any");
            foreach (var x in Ops.Iterate(a[0]))
                if (Ops.Truthy(x)) return PyBool.True;
            return PyBool.False;
        });
        F("all", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "all");
            foreach (var x in Ops.Iterate(a[0]))
                if (!Ops.Truthy(x)) return PyBool.False;
            return PyBool.True;
        });
        F("isinstance", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "isinstance");
            return Py.Bool(Ops.IsInstance(a[0], a[1]));
        });
        F("issubclass", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "issubclass");
            return Py.Bool(Ops.IsSubclass(a[0], a[1]));
        });
        F("chr", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "chr");
            var i = Ops.Index(a[0]);
            if (i.IsBig || i.V < 0 || i.V > 0x10FFFF) throw PyErr.ValueError("chr() arg not in range(0x110000)");
            return PyStr.FromCodePoint((int)i.V);
        });
        F("ord", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "ord");
            if (a[0] is not PyStr s) throw PyErr.TypeError($"ord() expected string of length 1, but {a[0].Type.Name} found");
            if (s.Length != 1) throw PyErr.TypeError($"ord() expected a character, but string of length {s.Length} found");
            return PyInt.From(s.HasSurrogates ? s.CodePoints[0] : s.V[0]);
        });
        F("hex", (a, kw) => RadixStr(a, kw, "hex", 16, "0x"));
        F("oct", (a, kw) => RadixStr(a, kw, "oct", 8, "0o"));
        F("bin", (a, kw) => RadixStr(a, kw, "bin", 2, "0b"));
        F("divmod", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "divmod");
            return Arith.DivMod(a[0], a[1]);
        });
        F("pow", Pow);
        F("iter", (a, kw) =>
        {
            Arg.NoKw(kw, "iter");
            Arg.Count(a, 1, 2, "iter");
            if (a.Length == 2)
            {
                if (!Ops.IsCallable(a[0])) throw PyErr.TypeError("iter(v, w): v must be callable");
                return new CallableIterator(a[0], a[1]);
            }
            return Ops.Iter(a[0]);
        });
        F("next", (a, kw) =>
        {
            Arg.NoKw(kw, "next");
            Arg.Count(a, 1, 2, "next");
            var it = a[0];
            if (it is not PyIterator && it.Type.Lookup("__next__") == null)
                throw PyErr.TypeError($"'{it.Type.Name}' object is not an iterator");
            PyObject? v;
            try
            {
                v = Ops.Next(it);
            }
            catch (PythonException e) when (a.Length == 2 && e.Value.Type.IsSubtypeOf(Exc.StopIteration))
            {
                return a[1];
            }
            if (v != null) return v;
            if (a.Length == 2) return a[1];
            throw PyErr.StopIteration(null);
        });
        F("format", (a, kw) =>
        {
            Arg.NoKw(kw, "format");
            Arg.Count(a, 1, 2, "format");
            return PyStr.New(Formatter.Format(a[0], a.Length > 1 ? Arg.Str(a[1], "format() argument 2") : ""));
        });
        F("hasattr", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "hasattr");
            return Py.Bool(Ops.HasAttr(a[0], Arg.Str(a[1], "attribute name")));
        });
        F("getattr", (a, kw) =>
        {
            Arg.NoKw(kw, "getattr");
            Arg.Count(a, 2, 3, "getattr");
            var name = Arg.Str(a[1], "attribute name");
            if (a.Length == 2) return Ops.GetAttr(a[0], name);
            try
            {
                return Ops.TryGetAttr(a[0], name) ?? a[2];
            }
            catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.AttributeError))
            {
                return a[2];
            }
        });
        F("setattr", (a, kw) =>
        {
            Arg.Exact(a, kw, 3, "setattr");
            Ops.SetAttr(a[0], Arg.Str(a[1], "attribute name"), a[2]);
            return Py.None;
        });
        F("delattr", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "delattr");
            Ops.DelAttr(a[0], Arg.Str(a[1], "attribute name"));
            return Py.None;
        });
        F("callable", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "callable");
            return Py.Bool(Ops.IsCallable(a[0]));
        });
        F("id", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "id");
            return PyInt.From(Ops.Id(a[0]));
        });
        F("hash", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "hash");
            return PyInt.From(Ops.Hash(a[0]));
        });
        F("eval", (a, kw) => EvalExec(a, kw, false));
        F("exec", (a, kw) => EvalExec(a, kw, true));
        F("globals", (a, kw) =>
        {
            Arg.Exact(a, kw, 0, "globals");
            var f = Interp.Current.CurrentFrame;
            return f == null ? new PyDict() : f.Globals.ToDict();
        });
        F("locals", (a, kw) =>
        {
            Arg.Exact(a, kw, 0, "locals");
            var f = Interp.Current.CurrentFrame;
            return f == null ? new PyDict() : LocalsDict(f);
        });
        F("vars", (a, kw) =>
        {
            Arg.NoKw(kw, "vars");
            Arg.Count(a, 0, 1, "vars");
            if (a.Length == 0)
            {
                var f = Interp.Current.CurrentFrame;
                return f == null ? new PyDict() : LocalsDict(f);
            }
            return Ops.TryGetAttr(a[0], "__dict__") ?? throw PyErr.TypeError("vars() argument must have __dict__ attribute");
        });
        F("dir", (a, kw) =>
        {
            Arg.NoKw(kw, "dir");
            Arg.Count(a, 0, 1, "dir");
            if (a.Length == 0)
            {
                var f = Interp.Current.CurrentFrame;
                var d = f == null ? new PyDict() : LocalsDict(f);
                var keys = d.KeyList().Select(k => ((PyStr)k).V).OrderBy(k => k, StringComparer.Ordinal).Select(k => (PyObject)PyStr.New(k));
                return new PyList(keys);
            }
            return Dir(a[0]);
        });
        F("exit", Exit);
        F("quit", Exit);
        F("__import__", (a, kw) =>
        {
            if (a.Length < 1) throw PyErr.TypeError("__import__() missing required argument 'name' (pos 1)");
            return Interp.Current.Engine.Import(Arg.Str(a[0], "__import__() argument 1"));
        });
        F("open", (a, kw) => throw PyErr.OSError("file system access is not available in this sandbox"));
        F("help", (a, kw) =>
        {
            var engine = Interp.Current.Engine;
            if (a.Length == 0)
            {
                engine.WriteOutput("Type help(object) to see the documentation of an object.\n");
                return Py.None;
            }
            var doc = Ops.TryGetAttr(a[0], "__doc__");
            engine.WriteOutput((doc is PyStr ds ? ds.V : "No documentation available.") + "\n");
            return Py.None;
        });
    }

    // ------------------------------------------------------------------ print and input

    private static PyObject Print(PyObject[] a, KwArgs? kw)
    {
        string sep = " ", end = "\n";
        PyObject? file = null;
        if (kw != null)
        {
            for (int i = 0; i < kw.Count; i++)
            {
                var v = kw.Values[i];
                switch (kw.Names[i])
                {
                    case "sep":
                        sep = v == Py.None ? " " : v is PyStr s ? s.V : throw PyErr.TypeError($"sep must be None or a string, not {v.Type.Name}");
                        break;
                    case "end":
                        end = v == Py.None ? "\n" : v is PyStr e ? e.V : throw PyErr.TypeError($"end must be None or a string, not {v.Type.Name}");
                        break;
                    case "file":
                        file = v == Py.None ? null : v;
                        break;
                    case "flush":
                        break;
                    default:
                        throw PyErr.TypeError($"'{kw.Names[i]}' is an invalid keyword argument for print()");
                }
            }
        }
        var sb = new StringBuilder();
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0) sb.Append(sep);
            sb.Append(Ops.Str(a[i]));
            if (sb.Length > Limits.MaxCollection) Limits.CheckStringLength(sb.Length);
        }
        sb.Append(end);
        var text = sb.ToString();
        if (file is SysModule.StdStream { IsError: true })
        {
            Interp.Current.Engine.WriteError(text);
            return Py.None;
        }
        if (file != null && file is not SysModule.StdStream)
        {
            Ops.CallMethod(file, "write", PyStr.New(text));
            return Py.None;
        }
        Interp.Current.Engine.WriteOutput(text);
        return Py.None;
    }

    private static PyObject Input(PyObject[] a, KwArgs? kw)
    {
        Arg.NoKw(kw, "input");
        Arg.Count(a, 0, 1, "input");
        var ctx = Interp.Current;
        string prompt = a.Length > 0 ? Ops.Str(a[0]) : "";
        var engine = ctx.Engine;
        if (prompt.Length > 0 && engine.Options.EchoInputPrompt) engine.WriteOutput(prompt);
        var line = ctx.Paused(() => engine.ReadInput(prompt));
        ctx.Check();
        if (line == null) throw PyErr.EOFError("EOF when reading a line");
        if (line.EndsWith('\n')) line = line.TrimEnd('\n', '\r');
        if (engine.Options.EchoInput) engine.WriteOutput(line + "\n");
        return PyStr.New(line);
    }

    private static PyObject Exit(PyObject[] a, KwArgs? kw)
    {
        var args = a.Length == 0 ? PyTuple.Empty : new PyTuple(new[] { a[0] });
        throw new PythonException(new PyBaseException(Exc.SystemExit, args));
    }

    // ------------------------------------------------------------------ numeric helpers

    private static PyObject RadixStr(PyObject[] a, KwArgs? kw, string name, int radix, string prefix)
    {
        Arg.Exact(a, kw, 1, name);
        var v = Ops.Index(a[0]).ToBig();
        string s = IntMath.ToRadix(BigInteger.Abs(v), radix);
        return PyStr.New((v.Sign < 0 ? "-" : "") + prefix + s);
    }

    private static PyObject Pow(PyObject[] a, KwArgs? kw)
    {
        var b = PowSig.Bind(a, kw);
        if (b[2] == null || b[2] == Py.None) return Arith.Pow(b[0]!, b[1]!);
        if (b[0] is PyInt x && b[1] is PyInt y && b[2] is PyInt m) return IntMath.PowMod(x, y, m);
        var um = b[0]!.Type.Lookup("__pow__");
        if (b[0] is PyInstance && um != null) return Ops.CallBound(um, b[0]!, new[] { b[1]!, b[2]! });
        throw PyErr.TypeError("pow() 3rd argument not allowed unless all arguments are integers");
    }

    private static PyObject MinMax(PyObject[] a, KwArgs? kw, string name, CompareOp op)
    {
        PyObject? key = null, dflt = null;
        if (kw != null)
        {
            for (int i = 0; i < kw.Count; i++)
            {
                if (kw.Names[i] == "key") key = kw.Values[i] == Py.None ? null : kw.Values[i];
                else if (kw.Names[i] == "default") dflt = kw.Values[i];
                else throw PyErr.TypeError($"{name}() got an unexpected keyword argument '{kw.Names[i]}'");
            }
        }
        if (a.Length == 0) throw PyErr.TypeError($"{name} expected at least 1 argument, got 0");
        IEnumerable<PyObject> items;
        if (a.Length == 1) items = Ops.Iterate(a[0]);
        else
        {
            if (dflt != null) throw PyErr.TypeError($"Cannot specify a default for {name}() with multiple positional arguments");
            items = a;
        }
        PyObject? best = null, bestKey = null;
        foreach (var item in items)
        {
            var k = key == null ? item : Ops.Call(key, new[] { item }, null);
            if (best == null)
            {
                best = item;
                bestKey = k;
                continue;
            }
            if (Ops.CompareBool(k, bestKey!, op))
            {
                best = item;
                bestKey = k;
            }
        }
        if (best != null) return best;
        if (dflt != null) return dflt;
        throw PyErr.ValueError($"{name}() iterable argument is empty");
    }

    private static PyObject Sum(PyObject[] a, KwArgs? kw)
    {
        var b = SumSig.Bind(a, kw);
        PyObject result = b[1] ?? PyInt.Zero;
        if (result is PyStr) throw PyErr.TypeError("sum() can't sum strings [use ''.join(seq) instead]");
        var it = Ops.GetIterator(b[0]!);
        var ctx = Interp.CurrentOrNull;
        if (result is PyInt ri && result is not PyBool && !ri.IsBig)
        {
            long acc = ri.V;
            PyObject? pending = null;
            while (it.MoveNext(out var item))
            {
                ctx?.Tick();
                if (item is PyInt ii && !ii.IsBig)
                {
                    long r = acc + ii.V;
                    if (((acc ^ r) & (ii.V ^ r)) >= 0)
                    {
                        acc = r;
                        continue;
                    }
                }
                pending = item;
                break;
            }
            result = PyInt.From(acc);
            if (pending == null) return result;
            result = Arith.Add(result, pending);
        }
        if (result is PyFloat rf)
        {
            double f = rf.V, c = 0.0;
            PyObject? pending = null;
            while (it.MoveNext(out var item))
            {
                ctx?.Tick();
                if (item is PyFloat xf)
                {
                    double x = xf.V;
                    double t = f + x;
                    if (Math.Abs(f) >= Math.Abs(x)) c += (f - t) + x;
                    else c += (x - t) + f;
                    f = t;
                    continue;
                }
                if (item is PyInt xi && !xi.IsBig)
                {
                    f += xi.V;
                    continue;
                }
                pending = item;
                break;
            }
            if (c != 0 && double.IsFinite(c)) f += c;
            result = new PyFloat(f);
            if (pending == null) return result;
            result = Arith.Add(result, pending);
        }
        while (it.MoveNext(out var item))
        {
            ctx?.Tick();
            result = Arith.Add(result, item);
        }
        return result;
    }

    private static PyObject Round(PyObject[] a, KwArgs? kw)
    {
        var b = RoundSig.Bind(a, kw);
        var x = b[0]!;
        var nd = b[1] == null || b[1] == Py.None ? null : b[1];
        if (x.IsBuiltinSubclass)
        {
            if (Ops.UserMethodOf(x.Type, "__round__") is { } ur)
                return Ops.CallBound(ur, x, nd == null ? Array.Empty<PyObject>() : new[] { nd });
            x = Subclassing.Plain(x);
        }
        switch (x)
        {
            case PyFloat f:
                if (nd == null)
                {
                    double r = Math.Round(f.V, MidpointRounding.ToEven);
                    return FloatMath.ToInt(r);
                }
                return new PyFloat(RoundFloat(f.V, Ops.Index(nd)));
            case PyInt i:
            {
                if (nd == null) return i is PyBool ? PyInt.From(i.V) : i;
                var n = Ops.Index(nd);
                if (n.Sign >= 0) return i is PyBool ? PyInt.From(i.V) : i;
                if (n.IsBig || n.V < -100000) return PyInt.Zero;
                var pow = BigInteger.Pow(10, (int)-n.V);
                var v = i.ToBig();
                var q = BigInteger.DivRem(v, pow, out var rem);
                if (rem.Sign < 0)
                {
                    q -= 1;
                    rem += pow;
                }
                var twice = rem * 2;
                int c = twice.CompareTo(pow);
                if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
                return PyInt.From(q * pow);
            }
        }
        var m = x.Type.Lookup("__round__");
        if (m != null && m != Py.None)
            return Ops.CallBound(m, x, nd == null ? Array.Empty<PyObject>() : new[] { nd });
        throw PyErr.TypeError($"type {x.Type.Name} doesn't define __round__ method");
    }

    public static double RoundFloat(double x, PyInt ndigits)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0) return x;
        if (ndigits.IsBig) return ndigits.Sign > 0 ? x : 0.0 * Math.Sign(x);
        long n = ndigits.V;
        if (n > 330) return x;
        if (n < -330) return Math.CopySign(0.0, x);
        var s = FloatFormat.Format(x, 'f', (int)Math.Max(n, 0), false, false, false);
        if (n >= 0) return double.Parse(s, CultureInfo.InvariantCulture);
        // negative ndigits: round to a multiple of 10^-n
        var scaled = FloatFormat.ScaleRoundSigned(x, (int)n);
        var str = scaled.ToString(CultureInfo.InvariantCulture) + "e" + (-n).ToString(CultureInfo.InvariantCulture);
        double r = double.Parse(str, CultureInfo.InvariantCulture);
        if (double.IsInfinity(r)) throw PyErr.OverflowError("rounded value too large to represent");
        return r == 0 ? Math.CopySign(0.0, x) : r;
    }

    // ------------------------------------------------------------------ misc helpers used by other files

    public static PyObject Reversed(PyObject seq)
    {
        switch (seq)
        {
            case PyList l:
                return new ListReverseIterator(l);
            case PyRange r:
                return new RangeIterator(r.Start + (r.Length - 1) * r.Step, r.Length, -r.Step);
            case PyDict d when d.Type == PyTypes.Dict:
                return new DictIterator(d, DictViewKind.Keys, reverse: true);
            case PyDictView v:
                return new DictIterator(v.Dict, v.Kind, reverse: true);
        }
        var m = seq.Type.Lookup("__reversed__");
        if (m != null && m != Py.None) return Ops.CallBound(m, seq, Array.Empty<PyObject>());
        if (seq is PyTuple or PyStr || (seq.Type.Lookup("__len__") != null && seq.Type.Lookup("__getitem__") != null && seq is not PyDict))
            return new ReversedSeqIterator(seq, Ops.Len(seq));
        throw PyErr.TypeError($"'{seq.Type.Name}' object is not reversible");
    }

    public static PyBaseException MakeException(PyObject o, PyObject? value)
    {
        if (o is PyType t)
        {
            if (!t.IsException) throw PyErr.TypeError("exceptions must derive from BaseException");
            PyObject inst = value == null || value == Py.None
                ? t.Call(Array.Empty<PyObject>(), null)
                : value is PyBaseException ve && ve.Type.IsSubtypeOf(t) ? ve : t.Call(new[] { value }, null);
            if (inst is not PyBaseException be) throw PyErr.TypeError($"calling {t.Name} should have returned an instance of BaseException, not {inst.Type.Name}");
            return be;
        }
        if (o is PyBaseException e) return e;
        throw PyErr.TypeError("exceptions must derive from BaseException");
    }

    public static PyObject Super(PyObject[] a)
    {
        if (a.Length == 0)
        {
            var f = Interp.Current.CurrentFrame ?? throw PyErr.RuntimeError("super(): no current frame");
            var code = f.Code;
            int idx = Array.IndexOf(code.FreeNames, "__class__");
            if (idx < 0) throw PyErr.RuntimeError("super(): __class__ cell not found");
            var cls = f.Cells![code.CellNames.Length + idx].Value as PyType ?? throw PyErr.RuntimeError("super(): empty __class__ cell");
            if (code.ArgCount == 0 && !code.HasVarArgs) throw PyErr.RuntimeError("super(): no arguments");
            PyObject? self = null;
            foreach (var (slot, cell) in code.CellParams)
                if (slot == 0) self = f.Cells[cell].Value;
            self ??= f.Locals.Length > 0 ? f.Locals[0] : null;
            if (code.ArgCount == 0 && code.HasVarArgs && f.Locals[code.VarArgsSlot] is PyTuple vt && vt.Count > 0) self = vt.Items[0];
            if (self == null) throw PyErr.RuntimeError("super(): arg[0] deleted");
            return MakeSuper(cls, self);
        }
        if (a[0] is not PyType st) throw PyErr.TypeError($"super() argument 1 must be a type, not {a[0].Type.Name}");
        if (a.Length == 1) throw PyErr.TypeError("unbound super objects are not supported");
        return MakeSuper(st, a[1]);
    }

    private static PyObject MakeSuper(PyType cls, PyObject self)
    {
        if (self is PyType ot && ot.IsSubtypeOf(cls)) return new PySuper(cls, ot, ot);
        if (self.Type.IsSubtypeOf(cls)) return new PySuper(cls, self, self.Type);
        throw PyErr.TypeError("super(type, obj): obj must be an instance or subtype of type");
    }

    public static PyList Dir(PyObject o)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        switch (o)
        {
            case PyModule m:
                foreach (var kv in m.Ns.Items) names.Add(kv.Key);
                break;
            case PyType t:
                foreach (var mt in t.Mro)
                    foreach (var k in mt.Dict.Keys) names.Add(k);
                break;
            default:
                if (o is PyInstance inst)
                    foreach (var k in inst.Dict.KeyList()) if (k is PyStr ks) names.Add(ks.V);
                if (o is ISubclassInstance si)
                    foreach (var k in si.Dict.KeyList())
                        if (k is PyStr ks2) names.Add(ks2.V);
                if (o is PyBaseException e && e.Dict != null)
                    foreach (var k in e.Dict.Keys) names.Add(k);
                foreach (var mt in o.Type.Mro)
                    foreach (var k in mt.Dict.Keys) names.Add(k);
                break;
        }
        return new PyList(names.OrderBy(n => n, StringComparer.Ordinal).Select(n => (PyObject)PyStr.New(n)));
    }

    public static PyDict LocalsDict(Frame f)
    {
        if (f.ClassNs != null)
        {
            var d = new PyDict();
            foreach (var kv in f.ClassNs) d.SetItem(PyStr.New(kv.Key), kv.Value);
            return d;
        }
        if (f.DynLocals != null) return f.DynLocals;
        if (f.Code.Kind is CodeKind.Module or CodeKind.Eval) return f.Globals.ToDict();
        var r = new PyDict();
        var code = f.Code;
        for (int i = 0; i < code.LocalNames.Length && i < f.Locals.Length; i++)
        {
            var n = code.LocalNames[i];
            if (n.StartsWith('.')) continue;
            if (code.CellNames.Contains(n)) continue;
            var v = f.Locals[i];
            if (v != null) r.SetItem(PyStr.New(n), v);
        }
        if (f.Cells != null)
        {
            for (int i = 0; i < code.CellNames.Length; i++)
            {
                var v = f.Cells[i].Value;
                if (v != null && code.CellNames[i] != "__class__") r.SetItem(PyStr.New(code.CellNames[i]), v);
            }
            for (int i = 0; i < code.FreeNames.Length; i++)
            {
                var v = f.Cells[code.CellNames.Length + i].Value;
                if (v != null && code.FreeNames[i] != "__class__") r.SetItem(PyStr.New(code.FreeNames[i]), v);
            }
        }
        return r;
    }

    private static PyObject EvalExec(PyObject[] a, KwArgs? kw, bool isExec)
    {
        string name = isExec ? "exec" : "eval";
        var b = new Sig(name, 1, 1, 3, "source", "globals", "locals").Bind(a, kw);
        if (b[0] is not PyStr src) throw PyErr.TypeError($"{name}() arg 1 must be a string");
        var ctx = Interp.Current;
        var engine = ctx.Engine;
        var caller = ctx.CurrentFrame;
        PyObject? g = b[1] == Py.None ? null : b[1];
        PyObject? l = b[2] == Py.None ? null : b[2];
        if (g != null && g is not PyDict) throw PyErr.TypeError($"{name}() globals must be a dict, not {g.Type.Name}");
        if (l != null && l is not PyDict) throw PyErr.TypeError($"{name}() locals must be a mapping");

        Namespace ns;
        PyDict? globalsDict = g as PyDict;
        PyDict? dynLocals = null;
        Dictionary<string, PyObject>? classNs = null;
        if (globalsDict != null)
        {
            ns = new Namespace();
            foreach (var (k, v) in globalsDict.Pairs())
                if (k is PyStr ks) ns.Set(ks.V, v);
            dynLocals = l as PyDict;
        }
        else
        {
            ns = caller?.Globals ?? engine.Globals;
            if (l != null) dynLocals = (PyDict)l;
            else if (caller != null)
            {
                if (caller.ClassNs != null)
                {
                    classNs = caller.ClassNs;
                    dynLocals = LocalsDict(caller);
                }
                else if (caller.DynLocals != null) dynLocals = caller.DynLocals;
                else if (caller.Code.Kind is not (CodeKind.Module or CodeKind.Eval)) dynLocals = LocalsDict(caller);
            }
        }
        var code = engine.CompileForEval(src.V, ns, isExec, dynLocals != null);
        var frame = new Frame(ctx, code, ns, new PyObject?[code.NumLocals], null) { DynLocals = dynLocals };
        var result = ctx.RunFrame(frame);
        if (globalsDict != null)
            foreach (var kv in ns.Items) globalsDict.SetItem(PyStr.New(kv.Key), kv.Value);
        if (classNs != null && dynLocals != null)
            foreach (var (k, v) in dynLocals.Pairs())
                if (k is PyStr ks) classNs[ks.V] = v;
        return isExec ? Py.None : result;
    }
}
