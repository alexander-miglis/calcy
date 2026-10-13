namespace Calcy.Python.Runtime;

/// <summary>Base class of native iterators. MoveNext never raises StopIteration.</summary>
internal abstract class PyIterator : PyObject
{
    public abstract bool MoveNext(out PyObject value);
}

internal static class IteratorMethods
{
    public static void Register(PyType t)
    {
        t.Method0("__iter__", self => self);
        t.Method0("__next__", self =>
        {
            if (((PyIterator)self).MoveNext(out var v)) return v;
            throw PyErr.StopIteration(self is PyGenerator g ? g.ReturnValue : null);
        });
    }
}

internal sealed class ListIterator : PyIterator
{
    private PyList? _list;
    private int _i;

    public ListIterator(PyList list) { _list = list; }

    internal override PyType Type => PyTypes.ListIterator;

    public override bool MoveNext(out PyObject value)
    {
        var l = _list;
        if (l != null && _i < l.Count)
        {
            value = l.Items[_i++];
            return true;
        }
        _list = null;
        value = null!;
        return false;
    }
}

internal sealed class ListReverseIterator : PyIterator
{
    private PyList? _list;
    private int _i;

    public ListReverseIterator(PyList list)
    {
        _list = list;
        _i = list.Count - 1;
    }

    internal override PyType Type => PyTypes.ListReverseIterator;

    public override bool MoveNext(out PyObject value)
    {
        var l = _list;
        if (l != null && _i >= 0 && _i < l.Count)
        {
            value = l.Items[_i--];
            return true;
        }
        _list = null;
        value = null!;
        return false;
    }
}

internal sealed class ArrayIterator : PyIterator
{
    private readonly PyObject[] _items;
    private int _i;
    private readonly PyType _type;

    public ArrayIterator(PyObject[] items, PyType type)
    {
        _items = items;
        _type = type;
    }

    internal override PyType Type => _type;

    public override bool MoveNext(out PyObject value)
    {
        if (_i < _items.Length)
        {
            value = _items[_i++];
            return true;
        }
        value = null!;
        return false;
    }
}

internal sealed class StrIterator : PyIterator
{
    private readonly PyStr _s;
    private int _i;

    public StrIterator(PyStr s) { _s = s; }

    internal override PyType Type => PyTypes.StrIterator;

    public override bool MoveNext(out PyObject value)
    {
        if (!_s.HasSurrogates)
        {
            if (_i < _s.V.Length)
            {
                value = PyStr.FromChar(_s.V[_i++]);
                return true;
            }
        }
        else
        {
            var cps = _s.CodePoints;
            if (_i < cps.Length)
            {
                value = PyStr.FromCodePoint(cps[_i++]);
                return true;
            }
        }
        value = null!;
        return false;
    }
}

internal sealed class RangeIterator : PyIterator
{
    private long _next;
    private long _remaining;
    private readonly long _step;

    public RangeIterator(PyRange r)
    {
        _next = r.Start;
        _remaining = r.Length;
        _step = r.Step;
    }

    public RangeIterator(long start, long count, long step)
    {
        _next = start;
        _remaining = count;
        _step = step;
    }

    internal override PyType Type => PyTypes.RangeIterator;

    public override bool MoveNext(out PyObject value)
    {
        if (_remaining > 0)
        {
            value = PyInt.From(_next);
            _next += _step;
            _remaining--;
            return true;
        }
        value = null!;
        return false;
    }
}

internal sealed class DictIterator : PyIterator
{
    private readonly PyDict _d;
    private readonly DictViewKind _kind;
    private readonly int _version;
    private int _pos;
    private readonly bool _reverse;

    public DictIterator(PyDict d, DictViewKind kind, bool reverse = false)
    {
        _d = d;
        _kind = kind;
        _version = d.Version;
        _reverse = reverse;
        _pos = reverse ? d.UsedSlots - 1 : 0;
    }

    internal override PyType Type => _kind switch
    {
        DictViewKind.Keys => PyTypes.DictKeyIterator,
        DictViewKind.Values => PyTypes.DictValueIterator,
        _ => PyTypes.DictItemIterator,
    };

    public override bool MoveNext(out PyObject value)
    {
        if (_d.Version != _version) throw PyErr.RuntimeError("dictionary changed size during iteration");
        while (_reverse ? _pos >= 0 : _pos < _d.UsedSlots)
        {
            int p = _pos;
            _pos += _reverse ? -1 : 1;
            if (_d.EntryAt(p, out var k, out var v))
            {
                value = _kind switch
                {
                    DictViewKind.Keys => k,
                    DictViewKind.Values => v,
                    _ => new PyTuple(new[] { k, v }),
                };
                return true;
            }
        }
        value = null!;
        return false;
    }
}

internal sealed class SetIterator : PyIterator
{
    private readonly SetTable _t;
    private readonly int _version;
    private int _pos;

    public SetIterator(SetTable t)
    {
        _t = t;
        _version = t.Version;
    }

    internal override PyType Type => PyTypes.SetIterator;

    public override bool MoveNext(out PyObject value)
    {
        if (_t.Version != _version) throw PyErr.RuntimeError("Set changed size during iteration");
        while (_pos < _t.SlotCount)
        {
            if (_t.SlotAt(_pos++, out var k, out _))
            {
                value = k;
                return true;
            }
        }
        value = null!;
        return false;
    }
}

/// <summary>Iterates an object through the __getitem__ sequence protocol.</summary>
internal sealed class SeqIterator : PyIterator
{
    private PyObject? _obj;
    private long _i;

    public SeqIterator(PyObject obj) { _obj = obj; }

    internal override PyType Type => PyTypes.SeqIterator;

    public override bool MoveNext(out PyObject value)
    {
        if (_obj == null)
        {
            value = null!;
            return false;
        }
        try
        {
            value = Ops.GetItem(_obj, PyInt.From(_i++));
            return true;
        }
        catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.IndexError) || e.Value.Type.IsSubtypeOf(Exc.StopIteration))
        {
            _obj = null;
            value = null!;
            return false;
        }
    }
}

/// <summary>iter(callable, sentinel).</summary>
internal sealed class CallableIterator : PyIterator
{
    private PyObject? _fn;
    private readonly PyObject _sentinel;

    public CallableIterator(PyObject fn, PyObject sentinel)
    {
        _fn = fn;
        _sentinel = sentinel;
    }

    internal override PyType Type => PyTypes.CallableIterator;

    public override bool MoveNext(out PyObject value)
    {
        if (_fn == null)
        {
            value = null!;
            return false;
        }
        PyObject v;
        try
        {
            v = Ops.Call(_fn, Array.Empty<PyObject>(), null);
        }
        catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.StopIteration))
        {
            _fn = null;
            value = null!;
            return false;
        }
        if (Ops.EqBool(v, _sentinel))
        {
            _fn = null;
            value = null!;
            return false;
        }
        value = v;
        return true;
    }
}

/// <summary>Adapts a user defined iterator (an object with __next__) to the native protocol.</summary>
internal sealed class UserIterator : PyIterator
{
    private readonly PyObject _obj;
    private readonly PyObject _next;
    private bool _done;

    public UserIterator(PyObject obj, PyObject next)
    {
        _obj = obj;
        _next = next;
    }

    internal override PyType Type => _obj.Type;

    public PyObject Inner => _obj;

    public override bool MoveNext(out PyObject value)
    {
        Interp.CheckStack();
        if (_done)
        {
            value = null!;
            return false;
        }
        try
        {
            value = Ops.CallBound(_next, _obj, Array.Empty<PyObject>());
            return true;
        }
        catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.StopIteration))
        {
            _done = true;
            value = null!;
            return false;
        }
    }
}

/// <summary>Wraps a C# enumerator as a Python iterator (used by builtins and modules).</summary>
internal sealed class NativeIterator : PyIterator
{
    private readonly IEnumerator<PyObject> _e;
    private readonly PyType _type;
    private bool _done;

    public NativeIterator(IEnumerable<PyObject> source, PyType? type = null)
    {
        _e = source.GetEnumerator();
        _type = type ?? PyTypes.NativeIterator;
    }

    internal override PyType Type => _type;

    public override bool MoveNext(out PyObject value)
    {
        Interp.CheckStack();
        if (!_done && _e.MoveNext())
        {
            value = _e.Current;
            return true;
        }
        _done = true;
        value = null!;
        return false;
    }
}

internal sealed class MapIterator : PyIterator
{
    private readonly PyObject _fn;
    private readonly PyIterator[] _its;

    public MapIterator(PyObject fn, PyIterator[] its)
    {
        _fn = fn;
        _its = its;
    }

    internal override PyType Type => PyTypes.Map;

    public override bool MoveNext(out PyObject value)
    {
        Interp.CheckStack();
        var args = new PyObject[_its.Length];
        for (int i = 0; i < _its.Length; i++)
        {
            if (!_its[i].MoveNext(out args[i]))
            {
                value = null!;
                return false;
            }
        }
        Interp.CurrentOrNull?.Tick();
        value = Ops.Call(_fn, args, null);
        return true;
    }
}

internal sealed class FilterIterator : PyIterator
{
    private readonly PyObject _fn;
    private readonly PyIterator _it;

    public FilterIterator(PyObject fn, PyIterator it)
    {
        _fn = fn;
        _it = it;
    }

    internal override PyType Type => PyTypes.Filter;

    public override bool MoveNext(out PyObject value)
    {
        Interp.CheckStack();
        var ctx = Interp.CurrentOrNull;
        while (_it.MoveNext(out var v))
        {
            ctx?.Tick();
            bool keep = _fn == Py.None ? Ops.Truthy(v) : Ops.Truthy(Ops.Call(_fn, new[] { v }, null));
            if (keep)
            {
                value = v;
                return true;
            }
        }
        value = null!;
        return false;
    }
}

internal sealed class ZipIterator : PyIterator
{
    private readonly PyIterator[] _its;
    private readonly bool _strict;
    private bool _done;

    public ZipIterator(PyIterator[] its, bool strict)
    {
        _its = its;
        _strict = strict;
    }

    internal override PyType Type => PyTypes.Zip;

    public override bool MoveNext(out PyObject value)
    {
        Interp.CheckStack();
        value = null!;
        if (_done || _its.Length == 0) return false;
        var items = new PyObject[_its.Length];
        for (int i = 0; i < _its.Length; i++)
        {
            if (!_its[i].MoveNext(out items[i]))
            {
                _done = true;
                if (_strict)
                {
                    if (i > 0) throw PyErr.ValueError($"zip() argument {i + 1} is shorter than argument{(i == 1 ? "" : "s")} 1{(i == 1 ? "" : "-" + i)}");
                    for (int j = 1; j < _its.Length; j++)
                        if (_its[j].MoveNext(out _))
                            throw PyErr.ValueError($"zip() argument {j + 1} is longer than argument{(j == 1 ? "" : "s")} 1{(j == 1 ? "" : "-" + j)}");
                }
                return false;
            }
        }
        value = new PyTuple(items);
        return true;
    }
}

internal sealed class EnumerateIterator : PyIterator
{
    private readonly PyIterator _it;
    private PyInt _i;

    public EnumerateIterator(PyIterator it, PyInt start)
    {
        _it = it;
        _i = start;
    }

    internal override PyType Type => PyTypes.Enumerate;

    public override bool MoveNext(out PyObject value)
    {
        Interp.CheckStack();
        if (_it.MoveNext(out var v))
        {
            value = new PyTuple(new PyObject[] { _i, v });
            _i = IntMath.Add(_i, PyInt.One);
            return true;
        }
        value = null!;
        return false;
    }
}

/// <summary>reversed() over a sequence with __len__ and __getitem__.</summary>
internal sealed class ReversedSeqIterator : PyIterator
{
    private readonly PyObject _seq;
    private long _i;

    public ReversedSeqIterator(PyObject seq, long len)
    {
        _seq = seq;
        _i = len - 1;
    }

    internal override PyType Type => PyTypes.Reversed;

    public override bool MoveNext(out PyObject value)
    {
        if (_i >= 0)
        {
            value = Ops.GetItem(_seq, PyInt.From(_i--));
            return true;
        }
        value = null!;
        return false;
    }
}
