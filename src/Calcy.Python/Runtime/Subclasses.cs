using System.Numerics;

namespace Calcy.Python.Runtime;

/// <summary>
/// An instance of a user class that derives from a builtin type (str, int, float, list, tuple, dict).
/// The C# object is a real instance of the builtin representation, so native code treats it as the base value,
/// while the dispatch layers honour dunder methods overridden in Python.
/// </summary>
internal interface ISubclassInstance
{
    PyType Cls { get; }
    PyDict Dict { get; }
}

internal sealed class PyStrSub : PyStr, ISubclassInstance
{
    public PyStrSub(PyType cls, string value) : base(value)
    {
        Cls = cls;
    }

    public PyType Cls { get; }
    public PyDict Dict { get; } = new();
    internal override PyType Type => Cls;
    internal override bool IsBuiltinSubclass => true;
}

internal sealed class PyIntSub : PyInt, ISubclassInstance
{
    private PyIntSub(PyType cls, long v) : base(v)
    {
        Cls = cls;
    }

    private PyIntSub(PyType cls, BigInteger big) : base(big)
    {
        Cls = cls;
    }

    public static PyIntSub Create(PyType cls, PyInt value) => value.IsBig ? new PyIntSub(cls, value.Big) : new PyIntSub(cls, value.V);

    public PyType Cls { get; }
    public PyDict Dict { get; } = new();
    internal override PyType Type => Cls;
    internal override bool IsBuiltinSubclass => true;
}

internal sealed class PyFloatSub : PyFloat, ISubclassInstance
{
    public PyFloatSub(PyType cls, double v) : base(v)
    {
        Cls = cls;
    }

    public PyType Cls { get; }
    public PyDict Dict { get; } = new();
    internal override PyType Type => Cls;
    internal override bool IsBuiltinSubclass => true;
}

internal sealed class PyTupleSub : PyTuple, ISubclassInstance
{
    public PyTupleSub(PyType cls, PyObject[] items) : base(items)
    {
        Cls = cls;
    }

    public PyType Cls { get; }
    public PyDict Dict { get; } = new();
    internal override PyType Type => Cls;
    internal override bool IsBuiltinSubclass => true;
}

internal sealed class PyListSub : PyList, ISubclassInstance
{
    public PyListSub(PyType cls)
    {
        Cls = cls;
    }

    public PyType Cls { get; }
    public PyDict Dict { get; } = new();
    internal override PyType Type => Cls;
    internal override bool IsBuiltinSubclass => true;
}

internal sealed class PyDictSub : PyDict, ISubclassInstance
{
    public PyDictSub(PyType cls)
    {
        Cls = cls;
    }

    public PyType Cls { get; }
    public PyDict Dict { get; } = new();
    internal override PyType Type => Cls;
    internal override bool IsBuiltinSubclass => true;

    internal override PyObject? Missing(PyObject key)
    {
        var m = Ops.UserMethodOf(Cls, "__missing__");
        return m == null ? null : Ops.CallBound(m, this, new[] { key });
    }
}

/// <summary>Creation of builtin subclass instances.</summary>
internal static class Subclassing
{
    /// <summary>The builtin types that Python classes may derive from.</summary>
    public static bool IsSubclassable(PyType t) =>
        t == PyTypes.Str || t == PyTypes.Int || t == PyTypes.Float || t == PyTypes.List || t == PyTypes.Tuple || t == PyTypes.Dict;

    /// <summary>Creates an instance of <paramref name="cls"/> whose builtin root is <paramref name="root"/>.</summary>
    public static PyObject Create(PyType cls, PyType root, PyObject[] args, KwArgs? kw)
    {
        if (root == PyTypes.Str) return new PyStrSub(cls, ((PyStr)root.Ctor!(root, args, kw)).V);
        if (root == PyTypes.Int) return PyIntSub.Create(cls, (PyInt)root.Ctor!(root, args, kw));
        if (root == PyTypes.Float) return new PyFloatSub(cls, ((PyFloat)root.Ctor!(root, args, kw)).V);
        if (root == PyTypes.Tuple) return new PyTupleSub(cls, ((PyTuple)root.Ctor!(root, args, kw)).Items);
        if (root == PyTypes.List) return new PyListSub(cls);
        if (root == PyTypes.Dict) return new PyDictSub(cls);
        throw PyErr.TypeError($"cannot create '{cls.Name}' instances");
    }

    /// <summary>Returns the value as an exact instance of its builtin type (never a subclass instance).</summary>
    public static PyObject Plain(PyObject o) => o switch
    {
        PyStrSub s => PyStr.New(s.V),
        PyIntSub i => i.IsBig ? PyInt.From(i.Big) : PyInt.From(i.V),
        PyFloatSub f => new PyFloat(f.V),
        PyTupleSub t => PyTuple.Create(t.Items),
        _ => o,
    };
}
