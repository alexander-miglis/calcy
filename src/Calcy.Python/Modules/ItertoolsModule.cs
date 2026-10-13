using Calcy.Python.Runtime;

namespace Calcy.Python.Modules;

/// <summary>The itertools module. Every iterator is lazy and ticks the step counter.</summary>
internal static class ItertoolsModule
{
    private static PyType IterType(PyModule m, string name, Func<PyObject[], KwArgs?, IEnumerable<PyObject>> make)
    {
        var t = new PyType(name, PyTypes.Object, "itertools", final: true);
        IteratorMethods.Register(t);
        t.Ctor = (type, a, kw) => new NativeIterator(Ticking(make(a, kw)), type);
        m.Add(name, t);
        return t;
    }

    private static IEnumerable<PyObject> Ticking(IEnumerable<PyObject> src)
    {
        var ctx = Interp.CurrentOrNull;
        foreach (var v in src)
        {
            ctx?.Tick();
            yield return v;
        }
    }

    private static void NoKw(KwArgs? kw, string name) => Arg.NoKw(kw, name);

    public static PyModule Create()
    {
        var m = new PyModule("itertools", new Namespace(), true);

        IterType(m, "count", (a, kw) =>
        {
            var b = new Sig("count", 0, 0, 2, "start", "step").Bind(a, kw);
            return Count(b[0] ?? PyInt.Zero, b[1] ?? PyInt.One);
        });
        IterType(m, "cycle", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "cycle");
            return Cycle(Ops.GetIterator(a[0]));
        });
        IterType(m, "repeat", (a, kw) =>
        {
            var b = new Sig("repeat", 1, 0, 2, "object", "times").Bind(a, kw);
            long times = b[1] == null ? -1 : Math.Max(0, Ops.IndexLong(b[1]!));
            return Repeat(b[0]!, times);
        });
        var chain = IterType(m, "chain", (a, kw) =>
        {
            NoKw(kw, "chain");
            return Chain(a);
        });
        chain.ClassMethod("from_iterable", (cls, a, kw) =>
        {
            Arg.Exact(a, kw, 1, "from_iterable");
            return new NativeIterator(Ticking(ChainFrom(Ops.GetIterator(a[0]))), cls);
        });
        IterType(m, "islice", (a, kw) =>
        {
            NoKw(kw, "islice");
            Arg.Count(a, 2, 4, "islice");
            long start = 0, stop = -1, step = 1;
            static long Idx(PyObject o, string msg)
            {
                if (o == Py.None) return -1;
                if (o is not PyInt) throw PyErr.ValueError(msg);
                long v = Ops.IndexLong(o);
                if (v < 0) throw PyErr.ValueError(msg);
                return v;
            }
            const string msg1 = "Stop argument for islice() must be None or an integer: 0 <= x <= sys.maxsize.";
            const string msg2 = "Indices for islice() must be None or an integer: 0 <= x <= sys.maxsize.";
            if (a.Length == 2) stop = Idx(a[1], msg1);
            else
            {
                start = Math.Max(0, Idx(a[1], msg2));
                stop = Idx(a[2], msg2);
                if (a.Length == 4)
                {
                    if (a[3] != Py.None)
                    {
                        if (a[3] is not PyInt || Ops.IndexLong(a[3]) <= 0) throw PyErr.ValueError("Step for islice() must be a positive integer or None.");
                        step = Ops.IndexLong(a[3]);
                    }
                }
            }
            return ISlice(Ops.GetIterator(a[0]), start, stop, step);
        });
        IterType(m, "product", (a, kw) =>
        {
            long repeat = 1;
            if (kw != null)
            {
                foreach (var n in kw.Names)
                    if (n != "repeat") throw PyErr.TypeError($"product() got an unexpected keyword argument '{n}'");
                repeat = Ops.IndexLong(kw.Get("repeat")!);
                if (repeat < 0) throw PyErr.ValueError("repeat argument cannot be negative");
            }
            var pools = new List<PyObject[]>();
            var basePools = a.Select(Ops.ToArray).ToList();
            for (long r = 0; r < repeat; r++) pools.AddRange(basePools);
            return Product(pools);
        });
        IterType(m, "permutations", (a, kw) =>
        {
            var b = new Sig("permutations", 1, 0, 2, "iterable", "r").Bind(a, kw);
            var pool = Ops.ToArray(b[0]!);
            int r = b[1] == null || b[1] == Py.None ? pool.Length : (int)Ops.IndexLong(b[1]!);
            if (r < 0) throw PyErr.ValueError("r must be non-negative");
            return Permutations(pool, r);
        });
        IterType(m, "combinations", (a, kw) =>
        {
            var b = new Sig("combinations", 2, 0, 2, "iterable", "r").Bind(a, kw);
            var pool = Ops.ToArray(b[0]!);
            int r = (int)Ops.IndexLong(b[1]!);
            if (r < 0) throw PyErr.ValueError("r must be non-negative");
            return Combinations(pool, r);
        });
        IterType(m, "combinations_with_replacement", (a, kw) =>
        {
            var b = new Sig("combinations_with_replacement", 2, 0, 2, "iterable", "r").Bind(a, kw);
            var pool = Ops.ToArray(b[0]!);
            int r = (int)Ops.IndexLong(b[1]!);
            if (r < 0) throw PyErr.ValueError("r must be non-negative");
            return CombinationsWithReplacement(pool, r);
        });
        IterType(m, "accumulate", (a, kw) =>
        {
            var b = new Sig("accumulate", 1, 0, 2, "iterable", "func", "initial").Bind(a, kw);
            var fn = b[1] == null || b[1] == Py.None ? null : b[1];
            return Accumulate(Ops.GetIterator(b[0]!), fn, b[2] == Py.None ? null : b[2]);
        });
        IterType(m, "zip_longest", (a, kw) =>
        {
            PyObject fill = Py.None;
            if (kw != null)
            {
                foreach (var n in kw.Names)
                    if (n != "fillvalue") throw PyErr.TypeError($"zip_longest() got an unexpected keyword argument '{n}'");
                fill = kw.Get("fillvalue")!;
            }
            return ZipLongest(a.Select(Ops.GetIterator).ToArray(), fill);
        });
        IterType(m, "starmap", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "starmap");
            return StarMap(a[0], Ops.GetIterator(a[1]));
        });
        IterType(m, "takewhile", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "takewhile");
            return TakeWhile(a[0], Ops.GetIterator(a[1]));
        });
        IterType(m, "dropwhile", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "dropwhile");
            return DropWhile(a[0], Ops.GetIterator(a[1]));
        });
        IterType(m, "filterfalse", (a, kw) =>
        {
            Arg.Exact(a, kw, 2, "filterfalse");
            return FilterFalse(a[0], Ops.GetIterator(a[1]));
        });
        IterType(m, "compress", (a, kw) =>
        {
            var b = new Sig("compress", 2, 0, 2, "data", "selectors").Bind(a, kw);
            return Compress(Ops.GetIterator(b[0]!), Ops.GetIterator(b[1]!));
        });
        IterType(m, "groupby", (a, kw) =>
        {
            var b = new Sig("groupby", 1, 0, 2, "iterable", "key").Bind(a, kw);
            return GroupBy(Ops.GetIterator(b[0]!), b[1] == null || b[1] == Py.None ? null : b[1]);
        });
        IterType(m, "pairwise", (a, kw) =>
        {
            Arg.Exact(a, kw, 1, "pairwise");
            return Pairwise(Ops.GetIterator(a[0]));
        });
        IterType(m, "batched", (a, kw) =>
        {
            var b = new Sig("batched", 2, 0, 2, "iterable", "n").Bind(a, kw);
            long n = Ops.IndexLong(b[1]!);
            if (n < 1) throw PyErr.ValueError("n must be at least one");
            return Batched(Ops.GetIterator(b[0]!), n);
        });
        m.AddFunction("tee", (a, kw) =>
        {
            NoKw(kw, "tee");
            Arg.Count(a, 1, 2, "tee");
            long n = a.Length > 1 ? Ops.IndexLong(a[1]) : 2;
            if (n < 0) throw PyErr.ValueError("n must be >= 0");
            var src = Ops.GetIterator(a[0]);
            var buffer = new List<PyObject>();
            bool done = false;
            bool Fetch(int idx)
            {
                while (buffer.Count <= idx && !done)
                {
                    if (src.MoveNext(out var v))
                    {
                        buffer.Add(v);
                        Limits.CheckSize(buffer.Count);
                    }
                    else done = true;
                }
                return idx < buffer.Count;
            }
            IEnumerable<PyObject> Reader()
            {
                for (int i = 0; Fetch(i); i++) yield return buffer[i];
            }
            var arr = new PyObject[n];
            for (int i = 0; i < n; i++) arr[i] = new NativeIterator(Reader());
            return new PyTuple(arr);
        });
        return m;
    }

    private static IEnumerable<PyObject> Count(PyObject start, PyObject step)
    {
        var cur = start;
        while (true)
        {
            yield return cur;
            cur = Arith.Add(cur, step);
        }
    }

    private static IEnumerable<PyObject> Cycle(PyIterator it)
    {
        var saved = new List<PyObject>();
        while (it.MoveNext(out var v))
        {
            saved.Add(v);
            Limits.CheckSize(saved.Count);
            yield return v;
        }
        if (saved.Count == 0) yield break;
        while (true)
            foreach (var v in saved) yield return v;
    }

    private static IEnumerable<PyObject> Repeat(PyObject obj, long times)
    {
        if (times < 0)
        {
            while (true) yield return obj;
        }
        for (long i = 0; i < times; i++) yield return obj;
    }

    private static IEnumerable<PyObject> Chain(PyObject[] iterables)
    {
        foreach (var x in iterables)
        {
            var it = Ops.GetIterator(x);
            while (it.MoveNext(out var v)) yield return v;
        }
    }

    private static IEnumerable<PyObject> ChainFrom(PyIterator outer)
    {
        while (outer.MoveNext(out var x))
        {
            var it = Ops.GetIterator(x);
            while (it.MoveNext(out var v)) yield return v;
        }
    }

    private static IEnumerable<PyObject> ISlice(PyIterator it, long start, long stop, long step)
    {
        long i = 0;
        long next = start;
        while (stop < 0 || next < stop)
        {
            if (!it.MoveNext(out var v)) yield break;
            if (i == next)
            {
                yield return v;
                next += step;
            }
            i++;
        }
        // consume up to stop like CPython
        while (i < stop)
        {
            if (!it.MoveNext(out _)) yield break;
            i++;
        }
    }

    private static IEnumerable<PyObject> Product(List<PyObject[]> pools)
    {
        int n = pools.Count;
        foreach (var p in pools)
            if (p.Length == 0) yield break;
        var idx = new int[n];
        while (true)
        {
            var t = new PyObject[n];
            for (int i = 0; i < n; i++) t[i] = pools[i][idx[i]];
            yield return new PyTuple(t);
            int k = n - 1;
            while (k >= 0)
            {
                idx[k]++;
                if (idx[k] < pools[k].Length) break;
                idx[k] = 0;
                k--;
            }
            if (k < 0) yield break;
        }
    }

    private static PyTuple Pick(PyObject[] pool, int[] indices, int r)
    {
        var t = new PyObject[r];
        for (int i = 0; i < r; i++) t[i] = pool[indices[i]];
        return new PyTuple(t);
    }

    private static IEnumerable<PyObject> Permutations(PyObject[] pool, int r)
    {
        int n = pool.Length;
        if (r > n) yield break;
        var indices = Enumerable.Range(0, n).ToArray();
        var cycles = Enumerable.Range(0, r).Select(i => n - i).ToArray();
        yield return Pick(pool, indices, r);
        if (n == 0) yield break;
        while (true)
        {
            bool found = false;
            for (int i = r - 1; i >= 0; i--)
            {
                cycles[i]--;
                if (cycles[i] == 0)
                {
                    int first = indices[i];
                    Array.Copy(indices, i + 1, indices, i, n - i - 1);
                    indices[n - 1] = first;
                    cycles[i] = n - i;
                }
                else
                {
                    int j = cycles[i];
                    (indices[i], indices[n - j]) = (indices[n - j], indices[i]);
                    found = true;
                    break;
                }
            }
            if (!found) yield break;
            yield return Pick(pool, indices, r);
        }
    }

    private static IEnumerable<PyObject> Combinations(PyObject[] pool, int r)
    {
        int n = pool.Length;
        if (r > n) yield break;
        var indices = Enumerable.Range(0, r).ToArray();
        yield return Pick(pool, indices, r);
        while (true)
        {
            int i = r - 1;
            while (i >= 0 && indices[i] == i + n - r) i--;
            if (i < 0) yield break;
            indices[i]++;
            for (int j = i + 1; j < r; j++) indices[j] = indices[j - 1] + 1;
            yield return Pick(pool, indices, r);
        }
    }

    private static IEnumerable<PyObject> CombinationsWithReplacement(PyObject[] pool, int r)
    {
        int n = pool.Length;
        if (n == 0 && r > 0) yield break;
        var indices = new int[r];
        yield return Pick(pool, indices, r);
        while (true)
        {
            int i = r - 1;
            while (i >= 0 && indices[i] == n - 1) i--;
            if (i < 0) yield break;
            int v = indices[i] + 1;
            for (int j = i; j < r; j++) indices[j] = v;
            yield return Pick(pool, indices, r);
        }
    }

    private static IEnumerable<PyObject> Accumulate(PyIterator it, PyObject? fn, PyObject? initial)
    {
        PyObject? total = initial;
        if (total != null) yield return total;
        while (it.MoveNext(out var v))
        {
            total = total == null ? v : fn == null ? Arith.Add(total, v) : Ops.Call(fn, new[] { total, v }, null);
            yield return total;
        }
    }

    private static IEnumerable<PyObject> ZipLongest(PyIterator[] its, PyObject fill)
    {
        int n = its.Length;
        if (n == 0) yield break;
        var active = new bool[n];
        Array.Fill(active, true);
        int remaining = n;
        while (true)
        {
            var t = new PyObject[n];
            for (int i = 0; i < n; i++)
            {
                if (active[i] && its[i].MoveNext(out var v)) t[i] = v;
                else
                {
                    if (active[i])
                    {
                        active[i] = false;
                        remaining--;
                    }
                    t[i] = fill;
                }
            }
            if (remaining == 0) yield break;
            yield return new PyTuple(t);
        }
    }

    private static IEnumerable<PyObject> StarMap(PyObject fn, PyIterator it)
    {
        while (it.MoveNext(out var v)) yield return Ops.Call(fn, Ops.ToArray(v), null);
    }

    private static IEnumerable<PyObject> TakeWhile(PyObject pred, PyIterator it)
    {
        while (it.MoveNext(out var v))
        {
            if (!Ops.Truthy(Ops.Call(pred, new[] { v }, null))) yield break;
            yield return v;
        }
    }

    private static IEnumerable<PyObject> DropWhile(PyObject pred, PyIterator it)
    {
        bool dropping = true;
        while (it.MoveNext(out var v))
        {
            if (dropping && Ops.Truthy(Ops.Call(pred, new[] { v }, null))) continue;
            dropping = false;
            yield return v;
        }
    }

    private static IEnumerable<PyObject> FilterFalse(PyObject pred, PyIterator it)
    {
        while (it.MoveNext(out var v))
        {
            bool t = pred == Py.None ? Ops.Truthy(v) : Ops.Truthy(Ops.Call(pred, new[] { v }, null));
            if (!t) yield return v;
        }
    }

    private static IEnumerable<PyObject> Compress(PyIterator data, PyIterator sel)
    {
        while (data.MoveNext(out var d) && sel.MoveNext(out var s))
            if (Ops.Truthy(s)) yield return d;
    }

    private static IEnumerable<PyObject> GroupBy(PyIterator it, PyObject? key)
    {
        if (!it.MoveNext(out var first)) yield break;
        var cur = first;
        var curKey = key == null ? cur : Ops.Call(key, new[] { cur }, null);
        while (true)
        {
            var group = new List<PyObject> { cur };
            bool more = false;
            PyObject? nextKey = null;
            while (it.MoveNext(out var v))
            {
                var k = key == null ? v : Ops.Call(key, new[] { v }, null);
                if (Ops.EqBool(k, curKey))
                {
                    group.Add(v);
                    continue;
                }
                cur = v;
                nextKey = k;
                more = true;
                break;
            }
            yield return new PyTuple(new PyObject[] { curKey, new NativeIterator(group) });
            if (!more) yield break;
            curKey = nextKey!;
        }
    }

    private static IEnumerable<PyObject> Pairwise(PyIterator it)
    {
        if (!it.MoveNext(out var prev)) yield break;
        while (it.MoveNext(out var v))
        {
            yield return new PyTuple(new[] { prev, v });
            prev = v;
        }
    }

    private static IEnumerable<PyObject> Batched(PyIterator it, long n)
    {
        while (true)
        {
            var batch = new List<PyObject>();
            while (batch.Count < n && it.MoveNext(out var v)) batch.Add(v);
            if (batch.Count == 0) yield break;
            yield return PyTuple.FromList(batch);
            if (batch.Count < n) yield break;
        }
    }
}
