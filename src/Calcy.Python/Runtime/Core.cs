using System.Numerics;
using System.Runtime.CompilerServices;
using Calcy.Python.Runtime;

namespace Calcy.Python
{
    /// <summary>
    /// Base class of every Python value managed by the interpreter. Host code can hold references to
    /// these values and pass them back to Python, but can only inspect them through <see cref="PyConvert"/>.
    /// </summary>
    public abstract class PyObject
    {
        private protected PyObject() { }

        internal abstract PyType Type { get; }

        /// <summary>Gets the Python type name of the value, for example <c>int</c> or <c>list</c>.</summary>
        public string TypeName => Type.Name;

        /// <summary>Returns a Python style representation computed without running any user code.</summary>
        public override string ToString() => Ops.SafeRepr(this);

        internal virtual PyObject Call(PyObject[] args, KwArgs? kw) => Ops.CallSlow(this, args, kw);

        /// <summary>True for instances of Python classes derived from str, int, float, list, tuple or dict.</summary>
        internal virtual bool IsBuiltinSubclass => false;
    }
}

namespace Calcy.Python.Runtime
{
    internal sealed class PyNone : PyObject
    {
        internal static readonly PyNone Instance = new();
        private PyNone() { }
        internal override PyType Type => PyTypes.NoneType;
    }

    internal sealed class PyNotImplemented : PyObject
    {
        internal static readonly PyNotImplemented Instance = new();
        private PyNotImplemented() { }
        internal override PyType Type => PyTypes.NotImplementedType;
    }

    internal sealed class PyEllipsis : PyObject
    {
        internal static readonly PyEllipsis Instance = new();
        private PyEllipsis() { }
        internal override PyType Type => PyTypes.EllipsisType;
    }

    /// <summary>Frequently used singletons.</summary>
    internal static class Py
    {
        public static readonly PyObject None = PyNone.Instance;
        public static readonly PyObject NotImplemented = PyNotImplemented.Instance;
        public static readonly PyObject Ellipsis = PyEllipsis.Instance;
        public static PyBool True => PyBool.True;
        public static PyBool False => PyBool.False;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyBool Bool(bool b) => b ? PyBool.True : PyBool.False;
    }

    /// <summary>Keyword arguments of a call: names and values in call order.</summary>
    internal sealed class KwArgs
    {
        public readonly string[] Names;
        public readonly PyObject[] Values;

        public KwArgs(string[] names, PyObject[] values)
        {
            Names = names;
            Values = values;
        }

        public int Count => Names.Length;

        public PyObject? Get(string name)
        {
            for (int i = 0; i < Names.Length; i++)
                if (Names[i] == name) return Values[i];
            return null;
        }

        public bool Has(string name) => Array.IndexOf(Names, name) >= 0;

        public static KwArgs? Merge(KwArgs? a, string name, PyObject value)
        {
            if (a == null) return new KwArgs(new[] { name }, new[] { value });
            var n = new string[a.Count + 1];
            var v = new PyObject[a.Count + 1];
            Array.Copy(a.Names, n, a.Count);
            Array.Copy(a.Values, v, a.Count);
            n[a.Count] = name;
            v[a.Count] = value;
            return new KwArgs(n, v);
        }
    }

    /// <summary>A closure cell holding a variable shared between nested scopes.</summary>
    internal sealed class Cell
    {
        public PyObject? Value;
        public Cell() { }
        public Cell(PyObject? v) { Value = v; }
    }

    /// <summary>A module level variable slot. Compiled code holds direct references to cells.</summary>
    internal sealed class GlobalCell
    {
        public readonly string Name;
        public PyObject? Value;
        public GlobalCell(string name) { Name = name; }
    }

    /// <summary>The global namespace of a module.</summary>
    internal sealed class Namespace
    {
        private readonly Dictionary<string, GlobalCell> _cells = new(StringComparer.Ordinal);

        public GlobalCell Cell(string name)
        {
            lock (_cells)
            {
                if (!_cells.TryGetValue(name, out var c))
                {
                    c = new GlobalCell(name);
                    _cells[name] = c;
                }
                return c;
            }
        }

        public PyObject? Get(string name)
        {
            lock (_cells) return _cells.TryGetValue(name, out var c) ? c.Value : null;
        }

        public void Set(string name, PyObject? value) => Cell(name).Value = value;

        public bool Remove(string name)
        {
            lock (_cells)
            {
                if (_cells.TryGetValue(name, out var c) && c.Value != null)
                {
                    c.Value = null;
                    return true;
                }
                return false;
            }
        }

        public bool Contains(string name)
        {
            lock (_cells) return _cells.TryGetValue(name, out var c) && c.Value != null;
        }

        /// <summary>A snapshot of the bound names in definition order.</summary>
        public List<KeyValuePair<string, PyObject>> Items
        {
            get
            {
                lock (_cells)
                {
                    var list = new List<KeyValuePair<string, PyObject>>(_cells.Count);
                    foreach (var kv in _cells)
                    {
                        var v = kv.Value.Value;
                        if (v != null) list.Add(new KeyValuePair<string, PyObject>(kv.Key, v));
                    }
                    return list;
                }
            }
        }

        public void Clear()
        {
            lock (_cells)
                foreach (var c in _cells.Values) c.Value = null;
        }

        public PyDict ToDict()
        {
            var d = new PyDict();
            foreach (var kv in Items) d.SetItem(PyStr.New(kv.Key), kv.Value);
            return d;
        }
    }

    /// <summary>A Python exception propagating through the C# stack.</summary>
    internal sealed class PythonException : PythonScriptException
    {
        public readonly PyBaseException Value;

        public PythonException(PyBaseException value)
        {
            Value = value;
        }

        public override string PythonType => Value.Type.Name;

        public override string Message => Value.Type.Name + ": " + Ops.SafeStr(Value);
    }

    internal enum TerminationReason
    {
        Cancelled,
        Timeout,
        StepLimit,
    }

    /// <summary>
    /// Terminates execution. Not a Python exception: no except clause can catch it and no
    /// Python finally block runs while it propagates.
    /// </summary>
    internal sealed class TerminationException : Exception
    {
        public readonly TerminationReason Reason;
        public readonly List<TracebackEntry> Frames;

        public TerminationException(TerminationReason reason, string message, List<TracebackEntry> frames) : base(message)
        {
            Reason = reason;
            Frames = frames;
        }
    }

    /// <summary>Runtime resource limits read from the active interpreter.</summary>
    internal static class Limits
    {
        public const int DefaultMaxCollection = 10_000_000;
        public const int DefaultMaxIntBits = 1_000_000;

        public static int MaxCollection => Interp.CurrentOrNull?.MaxCollection ?? DefaultMaxCollection;
        public static int MaxIntBits => Interp.CurrentOrNull?.MaxIntBits ?? DefaultMaxIntBits;

        public static void CheckSize(long n)
        {
            int max = MaxCollection;
            if (n > max)
                throw PyErr.MemoryError($"collection size {n} exceeds the limit of {max} elements");
        }

        public static void CheckStringLength(long n)
        {
            int max = MaxCollection;
            if (n > max)
                throw PyErr.MemoryError($"string length {n} exceeds the limit of {max} characters");
        }

        public static void CheckIntBits(long bits)
        {
            int max = MaxIntBits;
            if (bits > max)
                throw PyErr.MemoryError($"integer result would have about {bits} bits, which exceeds the limit of {max} bits");
        }

        public static void CheckBig(BigInteger v)
        {
            long bits = (long)v.GetBitLength();
            if (bits > MaxIntBits) CheckIntBits(bits);
        }
    }
}
