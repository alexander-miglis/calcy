using System.Runtime.CompilerServices;

namespace Calcy.Python.Runtime;

internal class PyList : PyObject
{
    internal PyObject[] Items;
    internal int Count;

    public PyList()
    {
        Items = Array.Empty<PyObject>();
    }

    public PyList(int capacity)
    {
        if (capacity > 0) Limits.CheckSize(capacity);
        Items = capacity <= 0 ? Array.Empty<PyObject>() : new PyObject[capacity];
    }

    public PyList(List<PyObject> items)
    {
        Limits.CheckSize(items.Count);
        Items = items.ToArray();
        Count = Items.Length;
    }

    public PyList(IEnumerable<PyObject> items) : this(new List<PyObject>(items)) { }

    /// <summary>Wraps an array without copying.</summary>
    public static PyList Wrap(PyObject[] arr)
    {
        Limits.CheckSize(arr.Length);
        var l = new PyList { Items = arr, Count = arr.Length };
        return l;
    }

    internal override PyType Type => PyTypes.List;

    public PyObject this[int i]
    {
        get => Items[i];
        set => Items[i] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(PyObject v)
    {
        if (Count == Items.Length) Grow(Count + 1);
        Items[Count++] = v;
    }

    private void Grow(long min)
    {
        Limits.CheckSize(min);
        long cap = Math.Max(Math.Max(4, (long)Items.Length * 2), min);
        int max = Limits.MaxCollection;
        if (cap > max) cap = Math.Max(min, max);
        if (cap > Array.MaxLength) cap = Array.MaxLength;
        var n = new PyObject[cap];
        Array.Copy(Items, n, Count);
        Items = n;
    }

    public void EnsureCapacity(long n)
    {
        if (n > Items.Length) Grow(n);
    }

    public void Insert(long index, PyObject v)
    {
        if (index < 0)
        {
            index += Count;
            if (index < 0) index = 0;
        }
        if (index > Count) index = Count;
        if (Count == Items.Length) Grow(Count + 1);
        int i = (int)index;
        Array.Copy(Items, i, Items, i + 1, Count - i);
        Items[i] = v;
        Count++;
    }

    public void RemoveAt(int i)
    {
        Count--;
        if (i < Count) Array.Copy(Items, i + 1, Items, i, Count - i);
        Items[Count] = null!;
    }

    public void Clear()
    {
        Items = Array.Empty<PyObject>();
        Count = 0;
    }

    public PyObject[] ToArray()
    {
        var a = new PyObject[Count];
        Array.Copy(Items, a, Count);
        return a;
    }

    public void Extend(PyObject iterable)
    {
        if (iterable.GetType() == typeof(PyList))
        {
            var l = (PyList)iterable;
            int n = l.Count;
            EnsureCapacity((long)Count + n);
            Array.Copy(l.Items, 0, Items, Count, n);
            Count += n;
            return;
        }
        if (iterable is PyTuple t && !t.IsBuiltinSubclass)
        {
            EnsureCapacity((long)Count + t.Items.Length);
            Array.Copy(t.Items, 0, Items, Count, t.Items.Length);
            Count += t.Items.Length;
            return;
        }
        foreach (var v in Ops.Iterate(iterable)) Append(v);
    }

    public void Reverse() => Array.Reverse(Items, 0, Count);

    public long NormIndex(PyObject idx, string what = "list index out of range")
    {
        long i = Ops.IndexLong(idx);
        if (i < 0) i += Count;
        if (i < 0 || i >= Count) throw PyErr.IndexError(what);
        return i;
    }

    public PyList Slice(PySlice s)
    {
        s.Indices(Count, out long start, out _, out long step, out long n);
        if (n <= 0) return new PyList();
        var arr = new PyObject[n];
        if (step == 1) Array.Copy(Items, start, arr, 0, n);
        else
        {
            long j = start;
            for (long i = 0; i < n; i++, j += step) arr[i] = Items[j];
        }
        return Wrap(arr);
    }

    public void SetSlice(PySlice s, PyObject value)
    {
        var src = value == this ? ToArray() : Ops.ToArray(value);
        s.Indices(Count, out long start, out long stop, out long step, out long n);
        if (step == 1)
        {
            if (stop < start) stop = start;
            long newCount = Count - (stop - start) + src.Length;
            Limits.CheckSize(newCount);
            var arr = new PyObject[Math.Max(newCount, 4)];
            Array.Copy(Items, 0, arr, 0, start);
            Array.Copy(src, 0, arr, start, src.Length);
            Array.Copy(Items, stop, arr, start + src.Length, Count - stop);
            Items = arr;
            Count = (int)newCount;
            return;
        }
        if (src.Length != n)
            throw PyErr.ValueError($"attempt to assign sequence of size {src.Length} to extended slice of size {n}");
        long j = start;
        for (long i = 0; i < n; i++, j += step) Items[j] = src[i];
    }

    public void DelSlice(PySlice s)
    {
        s.Indices(Count, out long start, out long stop, out long step, out long n);
        if (n <= 0) return;
        if (step < 0)
        {
            start = start + step * (n - 1);
            step = -step;
        }
        var keep = new List<PyObject>(Count - (int)n);
        long next = start;
        long removed = 0;
        for (int i = 0; i < Count; i++)
        {
            if (removed < n && i == next)
            {
                removed++;
                next += step;
                continue;
            }
            keep.Add(Items[i]);
        }
        Items = keep.ToArray();
        Count = Items.Length;
    }

    public void Sort(PyObject? key, bool reverse)
    {
        var arr = ToArray();
        int n = arr.Length;
        if (n > 1)
        {
            if (reverse) Array.Reverse(arr);
            if (key == null || key == Py.None) Sorting.Sort(arr);
            else
            {
                var keys = new PyObject[n];
                for (int i = 0; i < n; i++) keys[i] = Ops.Call(key, new[] { arr[i] }, null);
                Sorting.SortByKeys(keys, arr);
            }
            if (reverse) Array.Reverse(arr);
        }
        Items = arr;
        Count = n;
    }
}

/// <summary>Stable sorting with Python comparison semantics.</summary>
internal static class Sorting
{
    public static void Sort(PyObject[] arr)
    {
        if (TryFastSort(arr, null)) return;
        MergeSort(arr, null, Ops.LtBool);
    }

    public static void SortByKeys(PyObject[] keys, PyObject[] values)
    {
        if (TryFastSort(keys, values)) return;
        MergeSort(keys, values, Ops.LtBool);
    }

    private static bool TryFastSort(PyObject[] keys, PyObject[]? values)
    {
        int n = keys.Length;
        bool allInt = true, allFloat = true, allStr = true;
        for (int i = 0; i < n; i++)
        {
            var k = keys[i];
            var kt = k.GetType();
            if (kt != typeof(PyInt) || ((PyInt)k).IsBig) allInt = false;
            if (kt != typeof(PyFloat) || double.IsNaN(((PyFloat)k).V)) allFloat = false;
            if (kt != typeof(PyStr)) allStr = false;
            if (!allInt && !allFloat && !allStr) return false;
        }
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        if (allInt)
        {
            var lk = new long[n];
            for (int i = 0; i < n; i++) lk[i] = ((PyInt)keys[i]).V;
            Array.Sort(idx, (a, b) =>
            {
                int c = lk[a].CompareTo(lk[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
        }
        else if (allFloat)
        {
            var dk = new double[n];
            for (int i = 0; i < n; i++) dk[i] = ((PyFloat)keys[i]).V;
            Array.Sort(idx, (a, b) =>
            {
                int c = dk[a] < dk[b] ? -1 : dk[a] > dk[b] ? 1 : 0;
                return c != 0 ? c : a.CompareTo(b);
            });
        }
        else
        {
            var sk = new string[n];
            for (int i = 0; i < n; i++) sk[i] = ((PyStr)keys[i]).V;
            Array.Sort(idx, (a, b) =>
            {
                int c = string.CompareOrdinal(sk[a], sk[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
        }
        var k2 = new PyObject[n];
        for (int i = 0; i < n; i++) k2[i] = keys[idx[i]];
        Array.Copy(k2, keys, n);
        if (values != null)
        {
            var v2 = new PyObject[n];
            for (int i = 0; i < n; i++) v2[i] = values[idx[i]];
            Array.Copy(v2, values, n);
        }
        return true;
    }

    /// <summary>Stable merge sort using only a strict less than predicate.</summary>
    public static void MergeSort(PyObject[] keys, PyObject[]? values, Func<PyObject, PyObject, bool> less)
    {
        int n = keys.Length;
        if (n < 2) return;
        var tk = new PyObject[n];
        var tv = values == null ? null : new PyObject[n];
        const int Run = 16;
        for (int lo = 0; lo < n; lo += Run)
        {
            int hi = Math.Min(lo + Run, n);
            for (int i = lo + 1; i < hi; i++)
            {
                var k = keys[i];
                var v = values?[i];
                int j = i - 1;
                while (j >= lo && less(k, keys[j]))
                {
                    keys[j + 1] = keys[j];
                    if (values != null) values[j + 1] = values[j];
                    j--;
                }
                keys[j + 1] = k;
                if (values != null) values[j + 1] = v!;
            }
        }
        var srcK = keys;
        var srcV = values;
        var dstK = tk;
        var dstV = tv;
        for (int width = Run; width < n; width *= 2)
        {
            Interp.CurrentOrNull?.Tick();
            for (int lo = 0; lo < n; lo += 2 * width)
            {
                int mid = Math.Min(lo + width, n), hi = Math.Min(lo + 2 * width, n);
                int i = lo, j = mid, o = lo;
                while (i < mid && j < hi)
                {
                    if (less(srcK[j], srcK[i]))
                    {
                        dstK[o] = srcK[j];
                        if (srcV != null) dstV![o] = srcV[j];
                        j++;
                    }
                    else
                    {
                        dstK[o] = srcK[i];
                        if (srcV != null) dstV![o] = srcV[i];
                        i++;
                    }
                    o++;
                }
                while (i < mid)
                {
                    dstK[o] = srcK[i];
                    if (srcV != null) dstV![o] = srcV[i];
                    i++;
                    o++;
                }
                while (j < hi)
                {
                    dstK[o] = srcK[j];
                    if (srcV != null) dstV![o] = srcV[j];
                    j++;
                    o++;
                }
            }
            (srcK, dstK) = (dstK, srcK);
            (srcV, dstV) = (dstV, srcV);
        }
        if (!ReferenceEquals(srcK, keys))
        {
            Array.Copy(srcK, keys, n);
            if (values != null) Array.Copy(srcV!, values, n);
        }
    }
}

internal class PyTuple : PyObject
{
    internal readonly PyObject[] Items;

    public static readonly PyTuple Empty = new(Array.Empty<PyObject>());

    public PyTuple(PyObject[] items)
    {
        Items = items;
    }

    public static PyTuple Create(params PyObject[] items) => items.Length == 0 ? Empty : new PyTuple(items);

    public static PyTuple FromList(List<PyObject> items) => items.Count == 0 ? Empty : new PyTuple(items.ToArray());

    internal override PyType Type => PyTypes.Tuple;

    public int Count => Items.Length;

    public PyTuple Slice(PySlice s)
    {
        s.Indices(Items.Length, out long start, out _, out long step, out long n);
        if (n <= 0) return Empty;
        if (step == 1 && start == 0 && n == Items.Length && GetType() == typeof(PyTuple)) return this;
        var arr = new PyObject[n];
        long j = start;
        for (long i = 0; i < n; i++, j += step) arr[i] = Items[j];
        return new PyTuple(arr);
    }
}

/// <summary>Insertion ordered hash map with Python key semantics.</summary>
internal class PyDict : PyObject
{
    private struct Entry
    {
        public long Hash;
        public PyObject? Key;
        public PyObject? Value;
        public int Next;
    }

    private Entry[] _entries = Array.Empty<Entry>();
    private int[] _buckets = Array.Empty<int>();
    private int _used;
    private int _count;
    internal int Version;

    public PyDict() { }

    public PyDict(int capacity)
    {
        if (capacity > 0)
        {
            int cap = 8;
            while (cap < capacity) cap <<= 1;
            Rebuild(cap);
        }
    }

    internal override PyType Type => PyTypes.Dict;

    public int Count => _count;

    /// <summary>Called when a key is missing in d[key]; returns null to raise KeyError.</summary>
    internal virtual PyObject? Missing(PyObject key) => null;

    private int Find(PyObject key, long hash)
    {
        var buckets = _buckets;
        if (buckets.Length == 0) return -1;
        var entries = _entries;
        int i = buckets[(int)((ulong)hash & (ulong)(buckets.Length - 1))] - 1;
        while (i >= 0)
        {
            ref Entry e = ref entries[i];
            if (e.Hash == hash && e.Key != null && (ReferenceEquals(e.Key, key) || Ops.EqBool(e.Key, key))) return i;
            i = e.Next;
        }
        return -1;
    }

    public PyObject? GetOrNull(PyObject key)
    {
        if (_count == 0) return null;
        int i = Find(key, Ops.Hash(key));
        return i >= 0 ? _entries[i].Value : null;
    }

    public PyObject? GetOrNull(string key) => GetOrNull(PyStr.New(key));

    private int FindStr(string key, long hash)
    {
        var buckets = _buckets;
        if (buckets.Length == 0) return -1;
        var entries = _entries;
        int i = buckets[(int)((ulong)hash & (ulong)(buckets.Length - 1))] - 1;
        while (i >= 0)
        {
            ref Entry e = ref entries[i];
            if (e.Hash == hash && e.Key is PyStr s && s.V == key) return i;
            i = e.Next;
        }
        return -1;
    }

    /// <summary>Looks up a str key without allocating (used for instance attributes).</summary>
    public PyObject? GetStr(string key)
    {
        if (_count == 0) return null;
        int i = FindStr(key, PyStr.HashOf(key));
        return i >= 0 ? _entries[i].Value : null;
    }

    public bool ContainsStr(string key) => _count > 0 && FindStr(key, PyStr.HashOf(key)) >= 0;

    public void SetStr(string key, PyObject value)
    {
        long h = PyStr.HashOf(key);
        int i = FindStr(key, h);
        if (i >= 0)
        {
            _entries[i].Value = value;
            return;
        }
        Insert(PyStr.New(key), h, value);
    }

    public bool RemoveStr(string key) => _count > 0 && Remove(PyStr.New(key));

    public bool ContainsKey(PyObject key) => _count > 0 && Find(key, Ops.Hash(key)) >= 0;

    public void SetItem(PyObject key, PyObject value)
    {
        long h = Ops.Hash(key);
        int i = Find(key, h);
        if (i >= 0)
        {
            _entries[i].Value = value;
            return;
        }
        Insert(key, h, value);
    }

    public void SetItem(string key, PyObject value) => SetItem(PyStr.New(key), value);

    /// <summary>Returns the existing value or inserts the given default.</summary>
    public PyObject SetDefault(PyObject key, PyObject value)
    {
        long h = Ops.Hash(key);
        int i = Find(key, h);
        if (i >= 0) return _entries[i].Value!;
        Insert(key, h, value);
        return value;
    }

    private void Insert(PyObject key, long hash, PyObject value)
    {
        if (_used == _entries.Length) Grow();
        int b = (int)((ulong)hash & (ulong)(_buckets.Length - 1));
        _entries[_used] = new Entry { Hash = hash, Key = key, Value = value, Next = _buckets[b] - 1 };
        _buckets[b] = _used + 1;
        _used++;
        _count++;
        Version++;
    }

    private void Grow()
    {
        int cap = _entries.Length;
        if (cap == 0) cap = 8;
        else if (_count > _used / 2)
        {
            Limits.CheckSize(_count + 1);
            cap = (int)Math.Min((long)cap * 2, Math.Max(_count + 1L, Limits.MaxCollection));
        }
        Rebuild(cap);
    }

    private void Rebuild(int cap)
    {
        var ne = new Entry[cap];
        int j = 0;
        for (int i = 0; i < _used; i++)
            if (_entries[i].Key != null) ne[j++] = _entries[i];
        int nbuckets = 8;
        while (nbuckets < cap) nbuckets <<= 1;
        var nb = new int[nbuckets];
        for (int i = 0; i < j; i++)
        {
            int b = (int)((ulong)ne[i].Hash & (ulong)(nbuckets - 1));
            ne[i].Next = nb[b] - 1;
            nb[b] = i + 1;
        }
        _entries = ne;
        _buckets = nb;
        _used = j;
    }

    public bool Remove(PyObject key, out PyObject? value)
    {
        value = null;
        if (_count == 0) return false;
        long hash = Ops.Hash(key);
        int b = (int)((ulong)hash & (ulong)(_buckets.Length - 1));
        int i = _buckets[b] - 1, prev = -1;
        while (i >= 0)
        {
            ref Entry e = ref _entries[i];
            if (e.Hash == hash && e.Key != null && (ReferenceEquals(e.Key, key) || Ops.EqBool(e.Key, key)))
            {
                value = e.Value;
                RemoveAt(b, i, prev);
                return true;
            }
            prev = i;
            i = e.Next;
        }
        return false;
    }

    private void RemoveAt(int bucket, int i, int prev)
    {
        ref Entry e = ref _entries[i];
        if (prev < 0) _buckets[bucket] = e.Next + 1;
        else _entries[prev].Next = e.Next;
        e.Key = null;
        e.Value = null;
        e.Next = -1;
        _count--;
        Version++;
        while (_used > 0 && _entries[_used - 1].Key == null) _used--;
    }

    public bool Remove(PyObject key) => Remove(key, out _);

    public (PyObject key, PyObject value) PopLast()
    {
        if (_count == 0) throw PyErr.KeyError("popitem(): dictionary is empty");
        int i = _used - 1;
        while (_entries[i].Key == null) i--;
        var k = _entries[i].Key!;
        var v = _entries[i].Value!;
        Remove(k, out _);
        return (k, v);
    }

    public void Clear()
    {
        _entries = Array.Empty<Entry>();
        _buckets = Array.Empty<int>();
        _used = 0;
        _count = 0;
        Version++;
    }

    internal int UsedSlots => _used;

    internal bool EntryAt(int i, out PyObject key, out PyObject value)
    {
        var e = _entries[i];
        if (e.Key == null)
        {
            key = null!;
            value = null!;
            return false;
        }
        key = e.Key;
        value = e.Value!;
        return true;
    }

    public IEnumerable<(PyObject Key, PyObject Value)> Pairs()
    {
        int version = Version;
        for (int i = 0; i < _used; i++)
        {
            if (version != Version) throw PyErr.RuntimeError("dictionary changed size during iteration");
            var e = _entries[i];
            if (e.Key != null) yield return (e.Key, e.Value!);
        }
    }

    public List<PyObject> KeyList()
    {
        var l = new List<PyObject>(_count);
        for (int i = 0; i < _used; i++)
            if (_entries[i].Key != null) l.Add(_entries[i].Key!);
        return l;
    }

    public void CopyTo(PyDict target)
    {
        for (int i = 0; i < _used; i++)
        {
            var e = _entries[i];
            if (e.Key != null) target.SetItem(e.Key, e.Value!);
        }
    }

    public PyDict Copy()
    {
        var d = new PyDict(_count);
        CopyTo(d);
        return d;
    }

    /// <summary>dict.update semantics for a mapping or an iterable of pairs.</summary>
    public void Update(PyObject other)
    {
        if (other is PyDict od)
        {
            foreach (var (k, v) in od.Pairs().ToList()) SetItem(k, v);
            return;
        }
        if (Ops.HasAttr(other, "keys"))
        {
            foreach (var k in Ops.Iterate(Ops.CallMethod(other, "keys")))
                SetItem(k, Ops.GetItem(other, k));
            return;
        }
        int idx = 0;
        foreach (var item in Ops.Iterate(other))
        {
            var pair = Ops.ToArray(item);
            if (pair.Length != 2)
                throw PyErr.ValueError($"dictionary update sequence element #{idx} has length {pair.Length}; 2 is required");
            SetItem(pair[0], pair[1]);
            idx++;
        }
    }
}

/// <summary>Hash table with CPython's set probing so that iteration order matches CPython for ints.</summary>
internal sealed class SetTable
{
    private struct Slot
    {
        public long Hash;
        public PyObject? Key;
    }

    private sealed class DummyKey : PyObject
    {
        internal override PyType Type => PyTypes.Object;
    }

    private static readonly PyObject Dummy = new DummyKey();
    private const int LinearProbes = 9;
    private const int PerturbShift = 5;
    private const int MinSize = 8;

    private Slot[] _table = new Slot[MinSize];
    private int _mask = MinSize - 1;
    private int _fill;
    private int _used;
    internal int Version;

    public int Count => _used;

    public bool Add(PyObject key) => Add(key, Ops.Hash(key));

    public bool Add(PyObject key, long hash)
    {
    restart:
        var table = _table;
        int mask = _mask;
        ulong perturb = (ulong)hash;
        int i = (int)((ulong)hash & (ulong)mask);
        int freeslot = -1;
        int found;
        while (true)
        {
            int probes = (i + LinearProbes <= mask) ? LinearProbes : 0;
            int j = i;
            while (true)
            {
                ref Slot e = ref table[j];
                if (e.Key == null)
                {
                    found = j;
                    goto foundUnused;
                }
                if (e.Hash == hash && !ReferenceEquals(e.Key, Dummy))
                {
                    var startKey = e.Key;
                    if (ReferenceEquals(startKey, key)) return false;
                    if (Ops.EqBool(startKey, key)) return false;
                    if (!ReferenceEquals(table, _table)) goto restart;
                }
                else if (ReferenceEquals(e.Key, Dummy) && freeslot < 0)
                {
                    freeslot = j;
                }
                if (probes-- == 0) break;
                j++;
            }
            perturb >>= PerturbShift;
            i = (int)(((ulong)i * 5 + 1 + perturb) & (ulong)mask);
        }
    foundUnused:
        if (freeslot >= 0)
        {
            _table[freeslot].Key = key;
            _table[freeslot].Hash = hash;
            _used++;
            Version++;
            return true;
        }
        _table[found].Key = key;
        _table[found].Hash = hash;
        _fill++;
        _used++;
        Version++;
        if ((long)_fill * 5 >= (long)mask * 3)
            Resize(_used > 50000 ? _used * 2 : _used * 4);
        return true;
    }

    private void Resize(long minused)
    {
        long newsize = MinSize;
        while (newsize <= minused) newsize <<= 1;
        Limits.CheckSize(_used);
        if (newsize > Array.MaxLength) throw PyErr.MemoryError("set too large");
        var old = _table;
        _table = new Slot[newsize];
        _mask = (int)newsize - 1;
        _fill = _used;
        foreach (var s in old)
            if (s.Key != null && !ReferenceEquals(s.Key, Dummy))
                InsertClean(s.Key, s.Hash);
    }

    private void InsertClean(PyObject key, long hash)
    {
        var table = _table;
        int mask = _mask;
        ulong perturb = (ulong)hash;
        int i = (int)((ulong)hash & (ulong)mask);
        while (true)
        {
            if (table[i].Key == null)
            {
                table[i].Key = key;
                table[i].Hash = hash;
                return;
            }
            if (i + LinearProbes <= mask)
            {
                for (int j = 1; j <= LinearProbes; j++)
                {
                    if (table[i + j].Key == null)
                    {
                        table[i + j].Key = key;
                        table[i + j].Hash = hash;
                        return;
                    }
                }
            }
            perturb >>= PerturbShift;
            i = (int)(((ulong)i * 5 + 1 + perturb) & (ulong)mask);
        }
    }

    private int Lookup(PyObject key, long hash)
    {
        var table = _table;
        int mask = _mask;
        ulong perturb = (ulong)hash;
        int i = (int)((ulong)hash & (ulong)mask);
        while (true)
        {
            int probes = (i + LinearProbes <= mask) ? LinearProbes : 0;
            int j = i;
            while (true)
            {
                ref Slot e = ref table[j];
                if (e.Key == null) return -1;
                if (e.Hash == hash && !ReferenceEquals(e.Key, Dummy))
                {
                    if (ReferenceEquals(e.Key, key) || Ops.EqBool(e.Key, key)) return j;
                }
                if (probes-- == 0) break;
                j++;
            }
            perturb >>= PerturbShift;
            i = (int)(((ulong)i * 5 + 1 + perturb) & (ulong)mask);
        }
    }

    public bool Contains(PyObject key) => _used > 0 && Lookup(key, Ops.Hash(key)) >= 0;

    public bool Contains(PyObject key, long hash) => _used > 0 && Lookup(key, hash) >= 0;

    public bool Discard(PyObject key)
    {
        if (_used == 0) return false;
        int i = Lookup(key, Ops.Hash(key));
        if (i < 0) return false;
        _table[i].Key = Dummy;
        _table[i].Hash = -1;
        _used--;
        Version++;
        return true;
    }

    public PyObject Pop()
    {
        if (_used == 0) throw PyErr.KeyError("pop from an empty set");
        for (int i = 0; i < _table.Length; i++)
        {
            var k = _table[i].Key;
            if (k != null && !ReferenceEquals(k, Dummy))
            {
                _table[i].Key = Dummy;
                _table[i].Hash = -1;
                _used--;
                Version++;
                return k;
            }
        }
        throw PyErr.KeyError("pop from an empty set");
    }

    public void Clear()
    {
        _table = new Slot[MinSize];
        _mask = MinSize - 1;
        _fill = 0;
        _used = 0;
        Version++;
    }

    internal int SlotCount => _table.Length;

    internal bool SlotAt(int i, out PyObject key, out long hash)
    {
        var s = _table[i];
        if (s.Key == null || ReferenceEquals(s.Key, Dummy))
        {
            key = null!;
            hash = 0;
            return false;
        }
        key = s.Key;
        hash = s.Hash;
        return true;
    }

    public IEnumerable<PyObject> Keys()
    {
        int version = Version;
        for (int i = 0; i < _table.Length; i++)
        {
            if (version != Version) throw PyErr.RuntimeError("Set changed size during iteration");
            var s = _table[i];
            if (s.Key != null && !ReferenceEquals(s.Key, Dummy)) yield return s.Key;
        }
    }

    public List<(PyObject, long)> Entries()
    {
        var l = new List<(PyObject, long)>(_used);
        foreach (var s in _table)
            if (s.Key != null && !ReferenceEquals(s.Key, Dummy)) l.Add((s.Key, s.Hash));
        return l;
    }

    /// <summary>Port of CPython set_merge.</summary>
    public void Merge(SetTable other)
    {
        if (ReferenceEquals(other, this) || other._used == 0) return;
        if ((long)(_fill + other._used) * 5 >= (long)_mask * 3)
            Resize((long)(_used + other._used) * 2);
        if (_fill == 0 && _mask == other._mask && other._fill == other._used)
        {
            for (int i = 0; i <= other._mask; i++)
            {
                var k = other._table[i].Key;
                if (k != null)
                {
                    _table[i].Key = k;
                    _table[i].Hash = other._table[i].Hash;
                }
            }
            _fill = other._fill;
            _used = other._used;
            Version++;
            return;
        }
        if (_fill == 0)
        {
            _fill = other._used;
            _used = other._used;
            for (int i = 0; i <= other._mask; i++)
            {
                var k = other._table[i].Key;
                if (k != null && !ReferenceEquals(k, Dummy)) InsertClean(k, other._table[i].Hash);
            }
            Version++;
            return;
        }
        for (int i = 0; i <= other._mask; i++)
        {
            var k = other._table[i].Key;
            if (k != null && !ReferenceEquals(k, Dummy)) Add(k, other._table[i].Hash);
        }
    }

    /// <summary>Port of CPython set_update_internal.</summary>
    public void Update(PyObject iterable)
    {
        if (iterable is PySetBase sb)
        {
            Merge(sb.Table);
            return;
        }
        if (iterable is PyDict d)
        {
            long dictsize = d.Count;
            if ((_fill + dictsize) * 5 >= (long)_mask * 3) Resize((_used + dictsize) * 2);
            foreach (var (k, _) in d.Pairs()) Add(k);
            return;
        }
        foreach (var item in Ops.Iterate(iterable))
        {
            Add(item);
            if (_used > Limits.MaxCollection) Limits.CheckSize(_used);
        }
    }

    public SetTable Copy()
    {
        var t = new SetTable();
        t.Merge(this);
        return t;
    }
}

internal abstract class PySetBase : PyObject
{
    internal SetTable Table;

    protected PySetBase(SetTable table)
    {
        Table = table;
    }

    public int Count => Table.Count;

    public abstract PySetBase NewSame(SetTable table);
}

internal sealed class PySet : PySetBase
{
    public PySet() : base(new SetTable()) { }
    public PySet(SetTable t) : base(t) { }

    public static PySet From(PyObject iterable)
    {
        var s = new PySet();
        s.Table.Update(iterable);
        return s;
    }

    internal override PyType Type => PyTypes.Set;

    public override PySetBase NewSame(SetTable table) => new PySet(table);
}

internal sealed class PyFrozenSet : PySetBase
{
    public static readonly PyFrozenSet Empty = new(new SetTable());

    public PyFrozenSet(SetTable t) : base(t) { }

    internal override PyType Type => PyTypes.FrozenSet;

    public override PySetBase NewSame(SetTable table) => new PyFrozenSet(table);
}

internal sealed class PyRange : PyObject
{
    internal readonly long Start, Stop, Step;
    internal readonly long Length;

    public PyRange(long start, long stop, long step)
    {
        if (step == 0) throw PyErr.ValueError("range() arg 3 must not be zero");
        Start = start;
        Stop = stop;
        Step = step;
        Int128 len = 0;
        if (step > 0 && start < stop) len = ((Int128)stop - start - 1) / step + 1;
        else if (step < 0 && start > stop) len = ((Int128)start - stop - 1) / -(Int128)step + 1;
        if (len > long.MaxValue) throw PyErr.OverflowError("Python int too large to convert to C ssize_t");
        Length = (long)len;
    }

    internal override PyType Type => PyTypes.Range;

    public long At(long i) => Start + i * Step;

    public bool ContainsValue(long v)
    {
        if (Step > 0 ? (v < Start || v >= Stop) : (v > Start || v <= Stop)) return false;
        return ((Int128)v - Start) % Step == 0;
    }
}

internal sealed class PySlice : PyObject
{
    internal readonly PyObject Start, Stop, Step;

    public PySlice(PyObject start, PyObject stop, PyObject step)
    {
        Start = start;
        Stop = stop;
        Step = step;
    }

    internal override PyType Type => PyTypes.Slice;

    private static long Clamp(PyObject o)
    {
        var i = Ops.Index(o);
        if (i.IsBig) return i.Big.Sign < 0 ? long.MinValue / 2 : long.MaxValue / 2;
        return Math.Clamp(i.V, long.MinValue / 2, long.MaxValue / 2);
    }

    /// <summary>Computes start, stop, step and length for a sequence of the given length.</summary>
    public void Indices(long length, out long start, out long stop, out long step, out long slicelength)
    {
        step = Step == Py.None ? 1 : Clamp(Step);
        if (step == 0) throw PyErr.ValueError("slice step cannot be zero");
        const long Max = long.MaxValue / 2, Min = long.MinValue / 2;
        start = Start == Py.None ? (step < 0 ? Max : 0) : Clamp(Start);
        stop = Stop == Py.None ? (step < 0 ? Min : Max) : Clamp(Stop);
        if (start < 0)
        {
            start += length;
            if (start < 0) start = step < 0 ? -1 : 0;
        }
        else if (start >= length) start = step < 0 ? length - 1 : length;
        if (stop < 0)
        {
            stop += length;
            if (stop < 0) stop = step < 0 ? -1 : 0;
        }
        else if (stop >= length) stop = step < 0 ? length - 1 : length;
        if (step < 0) slicelength = stop < start ? (start - stop - 1) / -step + 1 : 0;
        else slicelength = start < stop ? (stop - start - 1) / step + 1 : 0;
    }
}

internal enum DictViewKind
{
    Keys,
    Values,
    Items,
}

internal sealed class PyDictView : PyObject
{
    internal readonly PyDict Dict;
    internal readonly DictViewKind Kind;

    public PyDictView(PyDict d, DictViewKind kind)
    {
        Dict = d;
        Kind = kind;
    }

    internal override PyType Type => Kind switch
    {
        DictViewKind.Keys => PyTypes.DictKeys,
        DictViewKind.Values => PyTypes.DictValues,
        _ => PyTypes.DictItems,
    };
}
