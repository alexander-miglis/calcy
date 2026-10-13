using System.Text;
using Calcy.Python.Runtime;

namespace Calcy.Python.Modules;

/// <summary>functools.partial objects.</summary>
internal sealed class PyPartial : PyObject
{
    public readonly PyObject Func;
    public readonly PyObject[] Args;
    public readonly KwArgs? Kw;

    public PyPartial(PyObject func, PyObject[] args, KwArgs? kw)
    {
        Func = func;
        Args = args;
        Kw = kw;
    }

    internal override PyType Type => FunctoolsModule.PartialType;

    internal override PyObject Call(PyObject[] args, KwArgs? kw)
    {
        var all = new PyObject[Args.Length + args.Length];
        Array.Copy(Args, all, Args.Length);
        Array.Copy(args, 0, all, Args.Length, args.Length);
        KwArgs? merged = Kw;
        if (kw != null)
        {
            if (merged == null) merged = kw;
            else
            {
                var names = new List<string>(merged.Names);
                var values = new List<PyObject>(merged.Values);
                for (int i = 0; i < kw.Count; i++)
                {
                    int idx = names.IndexOf(kw.Names[i]);
                    if (idx >= 0) values[idx] = kw.Values[i];
                    else
                    {
                        names.Add(kw.Names[i]);
                        values.Add(kw.Values[i]);
                    }
                }
                merged = new KwArgs(names.ToArray(), values.ToArray());
            }
        }
        return Func.Call(all, merged);
    }
}

/// <summary>The wrapper returned by functools.lru_cache and functools.cache.</summary>
internal sealed class PyLruCache : PyObject
{
    public readonly PyObject Func;
    public readonly long MaxSize;
    private readonly PyDict _cache = new();
    public long Hits, Misses;
    private static readonly PyObject KwMark = new PyStrMarker();

    private sealed class PyStrMarker : PyObject
    {
        internal override PyType Type => PyTypes.Object;
    }

    public PyLruCache(PyObject func, long maxSize)
    {
        Func = func;
        MaxSize = maxSize;
    }

    internal override PyType Type => FunctoolsModule.LruType;

    public int Size => _cache.Count;

    public void Clear()
    {
        _cache.Clear();
        Hits = Misses = 0;
    }

    internal override PyObject Call(PyObject[] args, KwArgs? kw)
    {
        PyObject key;
        if (kw == null && args.Length == 1 && args[0] is PyInt or PyStr) key = args[0];
        else
        {
            var parts = new List<PyObject>(args);
            if (kw != null)
            {
                parts.Add(KwMark);
                for (int i = 0; i < kw.Count; i++)
                {
                    parts.Add(PyStr.New(kw.Names[i]));
                    parts.Add(kw.Values[i]);
                }
            }
            key = new PyTuple(parts.ToArray());
        }
        var hit = _cache.GetOrNull(key);
        if (hit != null)
        {
            Hits++;
            if (MaxSize > 0)
            {
                _cache.Remove(key);
                _cache.SetItem(key, hit);
            }
            return hit;
        }
        Misses++;
        var result = Func.Call(args, kw);
        if (MaxSize != 0)
        {
            if (MaxSize > 0 && _cache.Count >= MaxSize)
            {
                var first = _cache.Pairs().First().Key;
                _cache.Remove(first);
            }
            _cache.SetItem(key, result);
            if (_cache.Count > Limits.MaxCollection) Limits.CheckSize(_cache.Count);
        }
        return result;
    }
}

/// <summary>Key objects created by functools.cmp_to_key.</summary>
internal sealed class PyCmpKey : PyObject
{
    public readonly PyObject Cmp;
    public readonly PyObject Obj;

    public PyCmpKey(PyObject cmp, PyObject obj)
    {
        Cmp = cmp;
        Obj = obj;
    }

    internal override PyType Type => FunctoolsModule.CmpKeyType;
}

internal static class FunctoolsModule
{
    public static readonly PyType PartialType = CreatePartial();
    public static readonly PyType LruType = CreateLru();
    public static readonly PyType CmpKeyType = CreateCmpKey();
    private static PyType? _cacheInfo;

    private static PyType CreatePartial()
    {
        var t = new PyType("partial", PyTypes.Object, "functools", final: true);
        t.Ctor = (type, a, kw) =>
        {
            if (a.Length == 0) throw PyErr.TypeError("type 'partial' takes at least one argument");
            if (!Ops.IsCallable(a[0])) throw PyErr.TypeError("the first argument must be callable");
            if (a[0] is PyPartial inner)
            {
                var args = inner.Args.Concat(a.Skip(1)).ToArray();
                var merged = inner.Kw;
                if (kw != null)
                    for (int i = 0; i < kw.Count; i++) merged = KwArgs.Merge(merged, kw.Names[i], kw.Values[i]);
                return new PyPartial(inner.Func, args, merged);
            }
            return new PyPartial(a[0], a.Skip(1).ToArray(), kw);
        };
        t.Dict["func"] = new PyGetSet("func", s => ((PyPartial)s).Func);
        t.Dict["args"] = new PyGetSet("args", s => new PyTuple(((PyPartial)s).Args));
        t.Dict["keywords"] = new PyGetSet("keywords", s =>
        {
            var d = new PyDict();
            var kw = ((PyPartial)s).Kw;
            if (kw != null)
                for (int i = 0; i < kw.Count; i++) d.SetItem(PyStr.New(kw.Names[i]), kw.Values[i]);
            return d;
        });
        t.Method0("__repr__", self =>
        {
            var p = (PyPartial)self;
            var sb = new StringBuilder("functools.partial(").Append(Ops.Repr(p.Func));
            foreach (var a in p.Args) sb.Append(", ").Append(Ops.Repr(a));
            if (p.Kw != null)
                for (int i = 0; i < p.Kw.Count; i++) sb.Append(", ").Append(p.Kw.Names[i]).Append('=').Append(Ops.Repr(p.Kw.Values[i]));
            return PyStr.New(sb.Append(')').ToString());
        });
        return t;
    }

    private static PyType CreateLru()
    {
        var t = new PyType("_lru_cache_wrapper", PyTypes.Object, "functools", final: true);
        t.Method0("cache_info", self =>
        {
            var c = (PyLruCache)self;
            _cacheInfo ??= CollectionsModule.MakeNamedTuple("CacheInfo", new[] { "hits", "misses", "maxsize", "currsize" }, Array.Empty<PyObject>(), "functools");
            return _cacheInfo.Call(new PyObject[]
            {
                PyInt.From(c.Hits), PyInt.From(c.Misses), c.MaxSize < 0 ? Py.None : PyInt.From(c.MaxSize), PyInt.From(c.Size),
            }, null);
        });
        t.Method0("cache_clear", self =>
        {
            ((PyLruCache)self).Clear();
            return Py.None;
        });
        t.Dict["__wrapped__"] = new PyGetSet("__wrapped__", s => ((PyLruCache)s).Func);
        t.Dict["__name__"] = new PyGetSet("__name__", s => Ops.TryGetAttr(((PyLruCache)s).Func, "__name__") ?? Py.None);
        t.Dict["__doc__"] = new PyGetSet("__doc__", s => Ops.TryGetAttr(((PyLruCache)s).Func, "__doc__") ?? Py.None);
        t.Method2("__get__", (self, obj, type) => obj == Py.None ? self : new PyBoundMethod(self, obj));
        return t;
    }

    private static PyType CreateCmpKey()
    {
        var t = new PyType("functools.KeyWrapper", PyTypes.Object, "functools", final: true);
        PyObject Cmp(PyObject self, PyObject other, Func<int, bool> pred)
        {
            if (other is not PyCmpKey ok) return Py.NotImplemented;
            var k = (PyCmpKey)self;
            var r = Ops.Call(k.Cmp, new[] { k.Obj, ok.Obj }, null);
            return Py.Bool(pred(Ops.CompareBool(r, PyInt.Zero, CompareOp.Lt) ? -1 : Ops.CompareBool(r, PyInt.Zero, CompareOp.Gt) ? 1 : 0));
        }
        t.Method1("__lt__", (s, o) => Cmp(s, o, c => c < 0));
        t.Method1("__le__", (s, o) => Cmp(s, o, c => c <= 0));
        t.Method1("__gt__", (s, o) => Cmp(s, o, c => c > 0));
        t.Method1("__ge__", (s, o) => Cmp(s, o, c => c >= 0));
        t.Method1("__eq__", (s, o) => Cmp(s, o, c => c == 0));
        t.Dict["__hash__"] = Py.None;
        t.Dict["obj"] = new PyGetSet("obj", s => ((PyCmpKey)s).Obj);
        return t;
    }

    public static PyModule Create()
    {
        var m = new PyModule("functools", new Namespace(), true);
        m.Add("partial", PartialType);
        m.AddFunction("reduce", (a, kw) =>
        {
            Arg.NoKw(kw, "reduce");
            Arg.Count(a, 2, 3, "reduce");
            var it = Ops.GetIterator(a[1]);
            PyObject? acc = a.Length == 3 ? a[2] : null;
            var ctx = Interp.CurrentOrNull;
            while (it.MoveNext(out var v))
            {
                ctx?.Tick();
                acc = acc == null ? v : Ops.Call(a[0], new[] { acc, v }, null);
            }
            return acc ?? throw PyErr.TypeError("reduce() of empty iterable with no initial value");
        });
        m.AddFunction("lru_cache", (a, kw) =>
        {
            long maxsize = 128;
            if (a.Length == 1 && Ops.IsCallable(a[0]) && a[0] is not PyInt) return new PyLruCache(a[0], maxsize);
            var b = new Sig("lru_cache", 0, 0, 2, "maxsize", "typed").Bind(a, kw);
            if (b[0] != null) maxsize = b[0] == Py.None ? -1 : Math.Max(0, Ops.IndexLong(b[0]!));
            return new PyBuiltinFunction("decorating_function", (a2, kw2) =>
            {
                Arg.Exact(a2, kw2, 1, "decorating_function");
                return new PyLruCache(a2[0], maxsize);
            }, "functools");
        });
        m.AddFunction("cache", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "cache");
            return new PyLruCache(a[0], -1);
        });
        m.AddFunction("cmp_to_key", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "cmp_to_key");
            var cmp = a[0];
            return new PyBuiltinFunction("K", (a2, kw2) =>
            {
                Arg.Exact(a2, kw2, 1, "K");
                return new PyCmpKey(cmp, a2[0]);
            }, "functools");
        });
        m.AddFunction("wraps", (a, kw) =>
        {
            if (a.Length < 1) throw PyErr.TypeError("wraps() missing required argument 'wrapped'");
            var wrapped = a[0];
            return new PyBuiltinFunction("decorator", (a2, kw2) =>
            {
                Arg.Exact(a2, kw2, 1, "decorator");
                return UpdateWrapper(a2[0], wrapped);
            }, "functools");
        });
        m.AddFunction("update_wrapper", (a, kw) =>
        {
            Arg.Count(a, 2, 4, "update_wrapper");
            return UpdateWrapper(a[0], a[1]);
        });
        m.AddFunction("total_ordering", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "total_ordering");
            if (a[0] is not PyType cls || cls.IsBuiltin) throw PyErr.TypeError("total_ordering requires a class");
            return TotalOrdering(cls);
        });
        return m;
    }

    private static PyObject UpdateWrapper(PyObject wrapper, PyObject wrapped)
    {
        foreach (var name in new[] { "__module__", "__name__", "__qualname__", "__doc__" })
        {
            var v = Ops.TryGetAttr(wrapped, name);
            if (v == null) continue;
            try
            {
                Ops.SetAttr(wrapper, name, v);
            }
            catch (PythonException)
            {
            }
        }
        try
        {
            Ops.SetAttr(wrapper, "__wrapped__", wrapped);
        }
        catch (PythonException)
        {
        }
        return wrapper;
    }

    private static PyObject TotalOrdering(PyType cls)
    {
        PyObject? Own(string name)
        {
            var v = cls.Lookup(name);
            return v == null || Ops.IsObjectDefault(v) ? null : v;
        }
        string? root = new[] { "__lt__", "__le__", "__gt__", "__ge__" }.FirstOrDefault(n => Own(n) != null);
        if (root == null) throw PyErr.ValueError("must define at least one ordering operation: < > <= >=");
        var rootFn = Own(root)!;
        PyObject Call(PyObject self, PyObject other) => Ops.CallBound(rootFn, self, new[] { other });
        bool Eq(PyObject self, PyObject other) => Ops.Truthy(Ops.Eq(self, other));
        void Add(string name, Func<PyObject, PyObject, PyObject> fn)
        {
            if (Own(name) == null) cls.Method1(name, fn);
        }
        switch (root)
        {
            case "__lt__":
                Add("__gt__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r) && !Eq(s, o)); });
                Add("__le__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(Ops.Truthy(r) || Eq(s, o)); });
                Add("__ge__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r)); });
                break;
            case "__le__":
                Add("__ge__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r) || Eq(s, o)); });
                Add("__lt__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(Ops.Truthy(r) && !Eq(s, o)); });
                Add("__gt__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r)); });
                break;
            case "__gt__":
                Add("__lt__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r) && !Eq(s, o)); });
                Add("__ge__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(Ops.Truthy(r) || Eq(s, o)); });
                Add("__le__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r)); });
                break;
            default:
                Add("__le__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r) || Eq(s, o)); });
                Add("__gt__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(Ops.Truthy(r) && !Eq(s, o)); });
                Add("__lt__", (s, o) => { var r = Call(s, o); return r == Py.NotImplemented ? r : Py.Bool(!Ops.Truthy(r)); });
                break;
        }
        return cls;
    }
}
