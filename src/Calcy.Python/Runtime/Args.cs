using System.Numerics;

namespace Calcy.Python.Runtime;

/// <summary>Argument extraction helpers for builtin functions and methods.</summary>
internal static class Arg
{
    public static void Count(PyObject[] a, int min, int max, string fname)
    {
        if (a.Length >= min && a.Length <= max) return;
        if (min == max)
        {
            if (min == 0) throw PyErr.TypeError($"{fname}() takes no arguments ({a.Length} given)");
            if (min == 1) throw PyErr.TypeError($"{fname}() takes exactly one argument ({a.Length} given)");
            throw PyErr.TypeError($"{fname} expected {min} arguments, got {a.Length}");
        }
        if (a.Length < min)
            throw PyErr.TypeError($"{fname} expected at least {min} argument{(min == 1 ? "" : "s")}, got {a.Length}");
        throw PyErr.TypeError($"{fname} expected at most {max} argument{(max == 1 ? "" : "s")}, got {a.Length}");
    }

    public static void NoKw(KwArgs? kw, string fname)
    {
        if (kw != null && kw.Count > 0)
            throw PyErr.TypeError($"{fname}() takes no keyword arguments");
    }

    public static void Exact(PyObject[] a, KwArgs? kw, int n, string fname)
    {
        NoKw(kw, fname);
        Count(a, n, n, fname);
    }

    public static string Str(PyObject o, string what)
    {
        if (o is PyStr s) return s.V;
        throw PyErr.TypeError($"{what} must be str, not {o.Type.Name}");
    }

    public static PyStr PStr(PyObject o, string what)
    {
        if (o is PyStr s) return s;
        throw PyErr.TypeError($"{what} must be str, not {o.Type.Name}");
    }

    public static long Index(PyObject o) => Ops.IndexLong(o);

    public static int Int(PyObject o)
    {
        long v = Ops.IndexLong(o);
        if (v > int.MaxValue || v < int.MinValue)
            throw PyErr.OverflowError("Python int too large to convert to C int");
        return (int)v;
    }

    public static double Double(PyObject o) => Ops.ToDouble(o);

    public static BigInteger Big(PyObject o) => Ops.Index(o).ToBig();

    public static bool IsNone(PyObject? o) => o == null || o == Py.None;
}

/// <summary>A builtin signature used to bind positional and keyword arguments by name.</summary>
internal sealed class Sig
{
    private readonly string _fname;
    private readonly string[] _names;
    private readonly int _required;
    private readonly int _posOnly;
    private readonly int _maxPositional;

    /// <param name="fname">Function name used in error messages.</param>
    /// <param name="required">Number of leading required parameters.</param>
    /// <param name="posOnly">Number of leading positional only parameters.</param>
    /// <param name="maxPositional">Number of parameters that can be passed positionally (the rest are keyword only).</param>
    /// <param name="names">Parameter names.</param>
    public Sig(string fname, int required, int posOnly, int maxPositional, params string[] names)
    {
        _fname = fname;
        _names = names;
        _required = required;
        _posOnly = posOnly;
        _maxPositional = maxPositional < 0 ? names.Length : maxPositional;
    }

    public PyObject?[] Bind(PyObject[] args, KwArgs? kw)
    {
        var r = new PyObject?[_names.Length];
        if (args.Length > _maxPositional)
            throw PyErr.TypeError($"{_fname}() takes at most {_maxPositional} {(_maxPositional == _names.Length ? "" : "positional ")}argument{(_maxPositional == 1 ? "" : "s")} ({args.Length} given)");
        for (int i = 0; i < args.Length; i++) r[i] = args[i];
        if (kw != null)
        {
            for (int k = 0; k < kw.Count; k++)
            {
                int idx = Array.IndexOf(_names, kw.Names[k]);
                if (idx < 0 || idx < _posOnly)
                    throw PyErr.TypeError($"{_fname}() got an unexpected keyword argument '{kw.Names[k]}'");
                if (r[idx] != null)
                    throw PyErr.TypeError($"{_fname}() got multiple values for argument '{kw.Names[k]}'");
                r[idx] = kw.Values[k];
            }
        }
        for (int i = 0; i < _required; i++)
            if (r[i] == null)
                throw PyErr.TypeError($"{_fname}() missing required argument '{_names[i]}' (pos {i + 1})");
        return r;
    }
}
