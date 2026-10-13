using System.Text;
using Calcy.Python.Runtime;

namespace Calcy.Python.Modules;

/// <summary>collections.deque implemented as a ring buffer.</summary>
internal sealed class PyDeque : PyObject
{
    private PyObject[] _buf = new PyObject[8];
    private int _head;
    private int _count;
    public readonly long MaxLen;
    public int Version;

    public PyDeque(long maxLen)
    {
        MaxLen = maxLen;
    }

    internal override PyType Type => CollectionsModule.DequeType;

    public int Count => _count;

    private int Mask => _buf.Length - 1;

    public PyObject this[int i]
    {
        get => _buf[(_head + i) & Mask];
        set => _buf[(_head + i) & Mask] = value;
    }

    private void Grow()
    {
        Limits.CheckSize((long)_count + 1);
        var n = new PyObject[_buf.Length * 2];
        for (int i = 0; i < _count; i++) n[i] = this[i];
        _buf = n;
        _head = 0;
    }

    public void Append(PyObject v)
    {
        Version++;
        if (MaxLen >= 0 && _count >= MaxLen)
        {
            if (MaxLen == 0) return;
            PopLeft();
        }
        if (_count == _buf.Length) Grow();
        _buf[(_head + _count) & Mask] = v;
        _count++;
    }

    public void AppendLeft(PyObject v)
    {
        Version++;
        if (MaxLen >= 0 && _count >= MaxLen)
        {
            if (MaxLen == 0) return;
            Pop();
        }
        if (_count == _buf.Length) Grow();
        _head = (_head - 1) & Mask;
        _buf[_head] = v;
        _count++;
    }

    public PyObject Pop()
    {
        if (_count == 0) throw PyErr.IndexError("pop from an empty deque");
        Version++;
        int idx = (_head + _count - 1) & Mask;
        var v = _buf[idx];
        _buf[idx] = null!;
        _count--;
        return v;
    }

    public PyObject PopLeft()
    {
        if (_count == 0) throw PyErr.IndexError("pop from an empty deque");
        Version++;
        var v = _buf[_head];
        _buf[_head] = null!;
        _head = (_head + 1) & Mask;
        _count--;
        return v;
    }

    public void Clear()
    {
        Version++;
        _buf = new PyObject[8];
        _head = 0;
        _count = 0;
    }

    public PyObject[] ToArray()
    {
        var a = new PyObject[_count];
        for (int i = 0; i < _count; i++) a[i] = this[i];
        return a;
    }

    public void SetAll(IList<PyObject> items)
    {
        Clear();
        foreach (var v in items) Append(v);
    }

    public void Rotate(long n)
    {
        if (_count <= 1) return;
        n %= _count;
        if (n == 0) return;
        var a = ToArray();
        var r = new PyObject[a.Length];
        for (int i = 0; i < a.Length; i++) r[(int)(((i + n) % a.Length + a.Length) % a.Length)] = a[i];
        SetAll(r);
    }

    public int NormIndex(PyObject idx)
    {
        long i = Ops.IndexLong(idx);
        if (i < 0) i += _count;
        if (i < 0 || i >= _count) throw PyErr.IndexError("deque index out of range");
        return (int)i;
    }
}

internal sealed class DequeIterator : PyIterator
{
    private readonly PyDeque _d;
    private readonly int _version;
    private int _i;
    private readonly bool _reverse;

    public DequeIterator(PyDeque d, bool reverse)
    {
        _d = d;
        _version = d.Version;
        _reverse = reverse;
        _i = reverse ? d.Count - 1 : 0;
    }

    internal override PyType Type => CollectionsModule.DequeIterType;

    public override bool MoveNext(out PyObject value)
    {
        if (_d.Version != _version) throw PyErr.RuntimeError("deque mutated during iteration");
        if (_reverse ? _i >= 0 : _i < _d.Count)
        {
            value = _d[_i];
            _i += _reverse ? -1 : 1;
            return true;
        }
        value = null!;
        return false;
    }
}

internal sealed class PyCounter : PyDict
{
    internal override PyType Type => CollectionsModule.CounterType;
    internal override PyObject? Missing(PyObject key) => PyInt.Zero;
}

internal sealed class PyDefaultDict : PyDict
{
    public PyObject Factory = Py.None;
    internal override PyType Type => CollectionsModule.DefaultDictType;

    internal override PyObject? Missing(PyObject key)
    {
        if (Factory == Py.None) return null;
        var v = Ops.Call(Factory, Array.Empty<PyObject>(), null);
        SetItem(key, v);
        return v;
    }
}

internal sealed class PyOrderedDict : PyDict
{
    internal override PyType Type => CollectionsModule.OrderedDictType;
}

/// <summary>The collections module: deque, Counter, defaultdict, OrderedDict and namedtuple.</summary>
internal static class CollectionsModule
{
    public static readonly PyType DequeType;
    public static readonly PyType DequeIterType;
    public static readonly PyType CounterType;
    public static readonly PyType DefaultDictType;
    public static readonly PyType OrderedDictType;

    static CollectionsModule()
    {
        DequeIterType = new PyType("_deque_iterator", PyTypes.Object, "collections", final: true);
        IteratorMethods.Register(DequeIterType);
        DequeType = CreateDeque();
        CounterType = CreateCounter();
        DefaultDictType = CreateDefaultDict();
        OrderedDictType = CreateOrderedDict();
    }

    public static PyModule Create()
    {
        var m = new PyModule("collections", new Namespace(), true);
        m.Add("deque", DequeType);
        m.Add("Counter", CounterType);
        m.Add("defaultdict", DefaultDictType);
        m.Add("OrderedDict", OrderedDictType);
        m.AddFunction("namedtuple", NamedTuple);
        return m;
    }

    // ------------------------------------------------------------------ deque

    private static PyDeque Dq(PyObject o) => (PyDeque)o;

    private static PyType CreateDeque()
    {
        var t = new PyType("deque", PyTypes.Object, "collections", final: true);
        t.Ctor = (type, a, kw) =>
        {
            var b = new Sig("deque", 0, 0, 2, "iterable", "maxlen").Bind(a, kw);
            long maxlen = -1;
            if (b[1] != null && b[1] != Py.None)
            {
                maxlen = Ops.IndexLong(b[1]!);
                if (maxlen < 0) throw PyErr.ValueError("maxlen must be non-negative");
            }
            var d = new PyDeque(maxlen);
            if (b[0] != null && b[0] != Py.None)
                foreach (var v in Ops.Iterate(b[0]!)) d.Append(v);
            return d;
        };
        t.Method1("append", (self, v) =>
        {
            Dq(self).Append(v);
            return Py.None;
        });
        t.Method1("appendleft", (self, v) =>
        {
            Dq(self).AppendLeft(v);
            return Py.None;
        });
        t.Method0("pop", self => Dq(self).Pop());
        t.Method0("popleft", self => Dq(self).PopLeft());
        t.Method1("extend", (self, it) =>
        {
            var d = Dq(self);
            foreach (var v in Ops.ToArray(it)) d.Append(v);
            return Py.None;
        });
        t.Method1("extendleft", (self, it) =>
        {
            var d = Dq(self);
            foreach (var v in Ops.ToArray(it)) d.AppendLeft(v);
            return Py.None;
        });
        t.Method("rotate", (self, a, kw) =>
        {
            Arg.Count(a, 0, 1, "rotate");
            Dq(self).Rotate(a.Length == 0 ? 1 : Ops.IndexLong(a[0]));
            return Py.None;
        });
        t.Method0("clear", self =>
        {
            Dq(self).Clear();
            return Py.None;
        });
        t.Method0("copy", self =>
        {
            var src = Dq(self);
            var d = new PyDeque(src.MaxLen);
            foreach (var v in src.ToArray()) d.Append(v);
            return d;
        });
        t.Method1("count", (self, x) => PyInt.From(Dq(self).ToArray().Count(v => Ops.EqBool(v, x))));
        t.Method1("index", (self, x) =>
        {
            var arr = Dq(self).ToArray();
            for (int i = 0; i < arr.Length; i++)
                if (Ops.EqBool(arr[i], x)) return PyInt.From(i);
            throw PyErr.ValueError($"{Ops.Repr(x)} is not in deque");
        });
        t.Method2("insert", (self, i, x) =>
        {
            var d = Dq(self);
            if (d.MaxLen >= 0 && d.Count >= d.MaxLen) throw PyErr.IndexError("deque already at its maximum size");
            var l = PyList.Wrap(d.ToArray());
            l.Insert(Ops.IndexLong(i), x);
            d.SetAll(l.ToArray());
            return Py.None;
        });
        t.Method1("remove", (self, x) =>
        {
            var d = Dq(self);
            var arr = d.ToArray().ToList();
            for (int i = 0; i < arr.Count; i++)
            {
                if (Ops.EqBool(arr[i], x))
                {
                    arr.RemoveAt(i);
                    d.SetAll(arr);
                    return Py.None;
                }
            }
            throw PyErr.ValueError($"{Ops.Repr(x)} is not in deque");
        });
        t.Method0("reverse", self =>
        {
            var d = Dq(self);
            var arr = d.ToArray();
            Array.Reverse(arr);
            d.SetAll(arr);
            return Py.None;
        });
        t.Dict["maxlen"] = new PyGetSet("maxlen", s => Dq(s).MaxLen < 0 ? Py.None : PyInt.From(Dq(s).MaxLen));
        t.Method0("__len__", self => PyInt.From(Dq(self).Count));
        t.Method0("__bool__", self => Py.Bool(Dq(self).Count > 0));
        t.Method1("__getitem__", (self, k) => Dq(self)[Dq(self).NormIndex(k)]);
        t.Method2("__setitem__", (self, k, v) =>
        {
            var d = Dq(self);
            d[d.NormIndex(k)] = v;
            return Py.None;
        });
        t.Method1("__delitem__", (self, k) =>
        {
            var d = Dq(self);
            int i = d.NormIndex(k);
            var arr = d.ToArray().ToList();
            arr.RemoveAt(i);
            d.SetAll(arr);
            return Py.None;
        });
        t.Method0("__iter__", self => new DequeIterator(Dq(self), false));
        t.Method0("__reversed__", self => new DequeIterator(Dq(self), true));
        t.Method1("__contains__", (self, x) => Py.Bool(Dq(self).ToArray().Any(v => Ops.EqBool(v, x))));
        t.Method1("__eq__", (self, o) =>
        {
            if (o is not PyDeque od) return Py.NotImplemented;
            var a = Dq(self).ToArray();
            var b = od.ToArray();
            return Py.Bool(a.Length == b.Length && a.Zip(b).All(p => Ops.EqBool(p.First, p.Second)));
        });
        t.Method1("__add__", (self, o) =>
        {
            if (o is not PyDeque od) return Py.NotImplemented;
            var d = new PyDeque(Dq(self).MaxLen);
            foreach (var v in Dq(self).ToArray()) d.Append(v);
            foreach (var v in od.ToArray()) d.Append(v);
            return d;
        });
        t.Method1("__iadd__", (self, o) =>
        {
            foreach (var v in Ops.ToArray(o)) Dq(self).Append(v);
            return self;
        });
        t.Method0("__repr__", self =>
        {
            var d = Dq(self);
            var ctx = Interp.CurrentOrNull;
            if (ctx != null && !ctx.ReprEnter(self)) return PyStr.New("[...]");
            try
            {
                var inner = Ops.Repr(PyList.Wrap(d.ToArray()));
                return PyStr.New(d.MaxLen >= 0 ? $"deque({inner}, maxlen={d.MaxLen})" : $"deque({inner})");
            }
            finally
            {
                ctx?.ReprLeave(self);
            }
        });
        t.Dict["__hash__"] = Py.None;
        return t;
    }

    // ------------------------------------------------------------------ Counter

    private static void CounterUpdate(PyDict c, PyObject? src, KwArgs? kw, int sign)
    {
        if (src != null && src != Py.None)
        {
            if (src is PyDict sd)
            {
                foreach (var (k, v) in sd.Pairs().ToList())
                {
                    var cur = c.GetOrNull(k) ?? PyInt.Zero;
                    c.SetItem(k, sign > 0 ? Arith.Add(cur, v) : Arith.Sub(cur, v));
                }
            }
            else
            {
                foreach (var k in Ops.Iterate(src))
                {
                    var cur = c.GetOrNull(k) ?? PyInt.Zero;
                    c.SetItem(k, sign > 0 ? Arith.Add(cur, PyInt.One) : Arith.Sub(cur, PyInt.One));
                }
            }
        }
        if (kw != null)
        {
            for (int i = 0; i < kw.Count; i++)
            {
                var k = PyStr.New(kw.Names[i]);
                var cur = c.GetOrNull(k) ?? PyInt.Zero;
                c.SetItem(k, sign > 0 ? Arith.Add(cur, kw.Values[i]) : Arith.Sub(cur, kw.Values[i]));
            }
        }
    }

    private static List<(PyObject Key, PyObject Value)> MostCommon(PyDict c)
    {
        var items = c.Pairs().ToList();
        var keys = items.Select(p => p.Value).ToArray();
        var idx = Enumerable.Range(0, items.Count).Select(i => (PyObject)PyInt.From(i)).ToArray();
        // stable sort by count, descending
        Sorting.MergeSort(keys, idx, (x, y) => Ops.CompareBool(x, y, CompareOp.Gt));
        return idx.Select(i => items[(int)((PyInt)i).V]).ToList();
    }

    public static PyDict CopyCounter(PyCounter src)
    {
        var c = new PyCounter();
        src.CopyTo(c);
        return c;
    }

    public static PyDict CopyDefaultDict(PyDefaultDict src)
    {
        var d = new PyDefaultDict { Factory = src.Factory };
        src.CopyTo(d);
        return d;
    }

    private static PyType CreateCounter()
    {
        var t = new PyType("Counter", PyTypes.Dict, "collections", final: true);
        t.Ctor = (type, a, kw) =>
        {
            Arg.Count(a, 0, 1, "Counter");
            var c = new PyCounter();
            CounterUpdate(c, a.Length > 0 ? a[0] : null, kw, 1);
            return c;
        };
        t.Method("update", (self, a, kw) =>
        {
            Arg.Count(a, 0, 1, "update");
            CounterUpdate((PyDict)self, a.Length > 0 ? a[0] : null, kw, 1);
            return Py.None;
        });
        t.Method("subtract", (self, a, kw) =>
        {
            Arg.Count(a, 0, 1, "subtract");
            CounterUpdate((PyDict)self, a.Length > 0 ? a[0] : null, kw, -1);
            return Py.None;
        });
        t.Method("most_common", (self, a, kw) =>
        {
            var b = new Sig("most_common", 0, 0, 1, "n").Bind(a, kw);
            var items = MostCommon((PyDict)self);
            if (b[0] != null && b[0] != Py.None)
            {
                long n = Ops.IndexLong(b[0]!);
                items = items.Take((int)Math.Max(0, Math.Min(n, items.Count))).ToList();
            }
            return new PyList(items.Select(p => (PyObject)new PyTuple(new[] { p.Key, p.Value })));
        });
        t.Method0("elements", self =>
        {
            var list = new List<PyObject>();
            foreach (var (k, v) in ((PyDict)self).Pairs())
            {
                long n = v is PyInt vi ? vi.ToLong() : 0;
                for (long i = 0; i < n; i++)
                {
                    list.Add(k);
                    if (list.Count > Limits.MaxCollection) Limits.CheckSize(list.Count);
                }
            }
            return new NativeIterator(list, PyTypes.NativeIterator);
        });
        t.Method0("total", self =>
        {
            PyObject total = PyInt.Zero;
            foreach (var (_, v) in ((PyDict)self).Pairs()) total = Arith.Add(total, v);
            return total;
        });
        t.Method1("__missing__", (self, k) => PyInt.Zero);
        t.Method1("__delitem__", (self, k) =>
        {
            ((PyDict)self).Remove(k);
            return Py.None;
        });
        PyObject Combine(PyObject self, PyObject other, Func<PyObject, PyObject, PyObject> op, bool union)
        {
            if (other is not PyCounter oc) return Py.NotImplemented;
            var a = (PyCounter)self;
            var r = new PyCounter();
            foreach (var (k, v) in a.Pairs())
            {
                var ov = oc.GetOrNull(k) ?? PyInt.Zero;
                var nv = op(v, ov);
                if (Ops.CompareBool(nv, PyInt.Zero, CompareOp.Gt)) r.SetItem(k, nv);
            }
            if (union)
            {
                foreach (var (k, v) in oc.Pairs())
                {
                    if (a.ContainsKey(k)) continue;
                    var nv = op(PyInt.Zero, v);
                    if (Ops.CompareBool(nv, PyInt.Zero, CompareOp.Gt)) r.SetItem(k, nv);
                }
            }
            return r;
        }
        t.Method1("__add__", (self, o) => Combine(self, o, Arith.Add, true));
        t.Method1("__sub__", (self, o) => Combine(self, o, Arith.Sub, false));
        t.Method1("__or__", (self, o) => Combine(self, o, (x, y) => Ops.CompareBool(x, y, CompareOp.Ge) ? x : y, true));
        t.Method1("__and__", (self, o) => Combine(self, o, (x, y) => Ops.CompareBool(x, y, CompareOp.Le) ? x : y, false));
        t.Method0("__repr__", self =>
        {
            var c = (PyDict)self;
            if (c.Count == 0) return PyStr.New("Counter()");
            var d = new PyDict();
            foreach (var (k, v) in MostCommon(c)) d.SetItem(k, v);
            return PyStr.New("Counter(" + Ops.DictRepr(d, false) + ")");
        });
        t.ClassMethod("fromkeys", (cls, a, kw) => throw PyErr.NotImplementedError("Counter.fromkeys() is undefined.  Use Counter(iterable) instead."));
        return t;
    }

    // ------------------------------------------------------------------ defaultdict and OrderedDict

    private static PyType CreateDefaultDict()
    {
        var t = new PyType("defaultdict", PyTypes.Dict, "collections", final: true);
        t.Ctor = (type, a, kw) =>
        {
            var d = new PyDefaultDict();
            if (a.Length > 0)
            {
                if (a[0] != Py.None && !Ops.IsCallable(a[0])) throw PyErr.TypeError("first argument must be callable or None");
                d.Factory = a[0];
                if (a.Length > 2) throw PyErr.TypeError("defaultdict expected at most 2 arguments");
                if (a.Length == 2) d.Update(a[1]);
            }
            if (kw != null)
                for (int i = 0; i < kw.Count; i++) d.SetItem(PyStr.New(kw.Names[i]), kw.Values[i]);
            return d;
        };
        t.Dict["default_factory"] = new PyGetSet("default_factory", s => ((PyDefaultDict)s).Factory, (s, v) => ((PyDefaultDict)s).Factory = v);
        t.Method1("__missing__", (self, k) => ((PyDefaultDict)self).Missing(k) ?? throw PyErr.KeyError(k));
        t.Method0("__repr__", self =>
        {
            var d = (PyDefaultDict)self;
            string f = d.Factory == Py.None ? "None" : Ops.Repr(d.Factory);
            var plain = new PyDict();
            d.CopyTo(plain);
            return PyStr.New($"defaultdict({f}, {Ops.DictRepr(plain, false)})");
        });
        return t;
    }

    private static PyType CreateOrderedDict()
    {
        var t = new PyType("OrderedDict", PyTypes.Dict, "collections", final: true);
        t.Ctor = (type, a, kw) =>
        {
            Arg.Count(a, 0, 1, "OrderedDict");
            var d = new PyOrderedDict();
            if (a.Length == 1) d.Update(a[0]);
            if (kw != null)
                for (int i = 0; i < kw.Count; i++) d.SetItem(PyStr.New(kw.Names[i]), kw.Values[i]);
            return d;
        };
        t.Method("move_to_end", (self, a, kw) =>
        {
            var b = new Sig("move_to_end", 1, 0, 2, "key", "last").Bind(a, kw);
            var d = (PyDict)self;
            bool last = b[1] == null || Ops.Truthy(b[1]!);
            if (!d.Remove(b[0]!, out var v)) throw PyErr.KeyError(b[0]!);
            if (last) d.SetItem(b[0]!, v!);
            else
            {
                var rest = d.Pairs().ToList();
                d.Clear();
                d.SetItem(b[0]!, v!);
                foreach (var (k, val) in rest) d.SetItem(k, val);
            }
            return Py.None;
        });
        t.Method("popitem", (self, a, kw) =>
        {
            var b = new Sig("popitem", 0, 0, 1, "last").Bind(a, kw);
            var d = (PyDict)self;
            bool last = b[0] == null || Ops.Truthy(b[0]!);
            if (d.Count == 0) throw PyErr.KeyError("dictionary is empty");
            if (last)
            {
                var (k, v) = d.PopLast();
                return new PyTuple(new[] { k, v });
            }
            var first = d.Pairs().First();
            d.Remove(first.Key);
            return new PyTuple(new[] { first.Key, first.Value });
        });
        t.Method0("__repr__", self =>
        {
            var d = (PyDict)self;
            if (d.Count == 0) return PyStr.New("OrderedDict()");
            var plain = new PyDict();
            d.CopyTo(plain);
            return PyStr.New("OrderedDict(" + Ops.DictRepr(plain, false) + ")");
        });
        return t;
    }

    // ------------------------------------------------------------------ namedtuple

    private static readonly Sig NamedTupleSig = new("namedtuple", 2, 0, 2, "typename", "field_names", "rename", "defaults", "module");

    private static PyObject NamedTuple(PyObject[] a, KwArgs? kw)
    {
        var b = NamedTupleSig.Bind(a, kw);
        string typename = Arg.Str(b[0]!, "typename");
        List<string> fields;
        if (b[1] is PyStr fs) fields = fs.V.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        else fields = Ops.Iterate(b[1]!).Select(x => Arg.Str(x, "field name")).ToList();
        bool rename = b[2] != null && Ops.Truthy(b[2]!);
        var seen = new HashSet<string>();
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            bool bad = !StrType.IsIdentifier(f) || f.StartsWith('_') || IsKeyword(f) || seen.Contains(f);
            if (bad)
            {
                if (rename) fields[i] = "_" + i;
                else if (f.StartsWith('_')) throw PyErr.ValueError($"Field names cannot start with an underscore: {PyStr.Repr(f)}");
                else if (seen.Contains(f)) throw PyErr.ValueError($"Encountered duplicate field name: {PyStr.Repr(f)}");
                else throw PyErr.ValueError($"Type names and field names must be valid identifiers: {PyStr.Repr(f)}");
            }
            seen.Add(fields[i]);
        }
        if (!StrType.IsIdentifier(typename)) throw PyErr.ValueError($"Type names and field names must be valid identifiers: {PyStr.Repr(typename)}");
        PyObject[] defaults = b[3] == null || b[3] == Py.None ? Array.Empty<PyObject>() : Ops.ToArray(b[3]!);
        if (defaults.Length > fields.Count) throw PyErr.TypeError("Got more default values than field names");
        string module = b[4] is PyStr ms ? ms.V : "__main__";
        return MakeNamedTuple(typename, fields.ToArray(), defaults, module);
    }

    private static bool IsKeyword(string s) => s is "False" or "None" or "True" or "and" or "as" or "assert" or "break" or "class" or "continue" or "def"
        or "del" or "elif" or "else" or "except" or "finally" or "for" or "from" or "global" or "if" or "import" or "in" or "is" or "lambda"
        or "nonlocal" or "not" or "or" or "pass" or "raise" or "return" or "try" or "while" or "with" or "yield";

    public static PyType MakeNamedTuple(string typename, string[] fields, PyObject[] defaults, string module)
    {
        var ns = new Dictionary<string, PyObject>(StringComparer.Ordinal);
        var cls = PyType.CreateClass(typename, typename, module, new[] { PyTypes.Object }, ns);
        int n = fields.Length;
        PyObject[] Values(PyObject self)
        {
            var inst = (PyInstance)self;
            var r = new PyObject[n];
            for (int i = 0; i < n; i++) r[i] = inst.Dict.GetStr(fields[i])!;
            return r;
        }
        PyInstance Make(PyType type, PyObject[] args, KwArgs? kw)
        {
            if (args.Length > n) throw PyErr.TypeError($"{typename}.__new__() takes {n + 1} positional arguments but {args.Length + 1} were given");
            var vals = new PyObject?[n];
            for (int i = 0; i < args.Length; i++) vals[i] = args[i];
            if (kw != null)
            {
                for (int k = 0; k < kw.Count; k++)
                {
                    int idx = Array.IndexOf(fields, kw.Names[k]);
                    if (idx < 0) throw PyErr.TypeError($"{typename}.__new__() got an unexpected keyword argument '{kw.Names[k]}'");
                    if (vals[idx] != null) throw PyErr.TypeError($"{typename}.__new__() got multiple values for argument '{kw.Names[k]}'");
                    vals[idx] = kw.Values[k];
                }
            }
            var missing = new List<string>();
            for (int i = 0; i < n; i++)
            {
                if (vals[i] != null) continue;
                int di = i - (n - defaults.Length);
                if (di >= 0) vals[i] = defaults[di];
                else missing.Add("'" + fields[i] + "'");
            }
            if (missing.Count > 0)
                throw PyErr.TypeError($"{typename}.__new__() missing {missing.Count} required positional argument{(missing.Count == 1 ? "" : "s")}: {string.Join(" and ", missing)}");
            var inst = new PyInstance(type);
            for (int i = 0; i < n; i++) inst.Dict.SetStr(fields[i], vals[i]!);
            return inst;
        }
        cls.Dict["__new__"] = new PyStaticMethod(new PyBuiltinFunction("__new__", (a, kw) =>
        {
            if (a.Length == 0 || a[0] is not PyType t) throw PyErr.TypeError("__new__ requires a type argument");
            return Make(t, a.Skip(1).ToArray(), kw);
        }));
        cls.Dict["_fields"] = new PyTuple(fields.Select(f => (PyObject)PyStr.New(f)).ToArray());
        cls.Dict["__match_args__"] = cls.Dict["_fields"];
        cls.Dict["_field_defaults"] = new PyDict();
        for (int i = 0; i < defaults.Length; i++)
            ((PyDict)cls.Dict["_field_defaults"]).SetItem(PyStr.New(fields[n - defaults.Length + i]), defaults[i]);
        cls.Method0("__repr__", self =>
        {
            var vals = Values(self);
            var sb = new StringBuilder(self.Type.Name).Append('(');
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(fields[i]).Append('=').Append(Ops.Repr(vals[i]));
            }
            return PyStr.New(sb.Append(')').ToString());
        });
        cls.Method1("__getitem__", (self, k) => Ops.GetItem(new PyTuple(Values(self)), k));
        cls.Method0("__len__", self => PyInt.From(n));
        cls.Method0("__iter__", self => new ArrayIterator(Values(self), PyTypes.TupleIterator));
        cls.Method1("__contains__", (self, x) => Py.Bool(Values(self).Any(v => Ops.EqBool(v, x))));
        cls.Method0("__hash__", self => PyInt.From(Ops.HashTuple(Values(self))));
        PyObject Cmp(PyObject self, PyObject other, CompareOp op)
        {
            PyTuple? o = other is PyTuple ot ? ot : other is PyInstance oi && oi.Type.Dict.ContainsKey("_fields") && oi.Type.Lookup("_fields") is PyTuple ? new PyTuple(Ops.ToArray(other)) : null;
            if (o == null) return Py.NotImplemented;
            return Ops.RichCompare(new PyTuple(Values(self)), o, op);
        }
        cls.Method1("__eq__", (self, o) => Cmp(self, o, CompareOp.Eq));
        cls.Method1("__ne__", (self, o) => Cmp(self, o, CompareOp.Ne));
        cls.Method1("__lt__", (self, o) => Cmp(self, o, CompareOp.Lt));
        cls.Method1("__le__", (self, o) => Cmp(self, o, CompareOp.Le));
        cls.Method1("__gt__", (self, o) => Cmp(self, o, CompareOp.Gt));
        cls.Method1("__ge__", (self, o) => Cmp(self, o, CompareOp.Ge));
        cls.Method2("__setattr__", (self, name, v) => throw PyErr.AttributeError("can't set attribute"));
        cls.Method0("_asdict", self =>
        {
            var d = new PyDict();
            var vals = Values(self);
            for (int i = 0; i < n; i++) d.SetItem(PyStr.New(fields[i]), vals[i]);
            return d;
        });
        cls.Method("_replace", (self, a, kw) =>
        {
            if (a.Length > 0) throw PyErr.TypeError("_replace() takes 0 positional arguments");
            var vals = Values(self);
            var inst = new PyInstance(self.Type);
            for (int i = 0; i < n; i++) inst.Dict.SetStr(fields[i], vals[i]);
            if (kw != null)
            {
                for (int k = 0; k < kw.Count; k++)
                {
                    if (!fields.Contains(kw.Names[k])) throw PyErr.ValueError($"Got unexpected field names: ['{kw.Names[k]}']");
                    inst.Dict.SetStr(kw.Names[k], kw.Values[k]);
                }
            }
            return inst;
        });
        cls.ClassMethod("_make", (type, a, kw) =>
        {
            Arg.Exact(a, kw, 1, "_make");
            var vals = Ops.ToArray(a[0]);
            if (vals.Length != n) throw PyErr.TypeError($"Expected {n} arguments, got {vals.Length}");
            return Make(type, vals, null);
        });
        return cls;
    }
}
