using System.Text;

namespace Calcy.Python.Runtime;

internal readonly record struct TracebackEntry(string FileName, int Line, string FunctionName);

/// <summary>A Python exception instance (builtin or user defined exception class).</summary>
internal sealed class PyBaseException : PyObject
{
    public readonly PyType ExcType;
    public PyTuple Args;
    public Dictionary<string, PyObject>? Dict;
    public PyBaseException? Cause;
    public PyBaseException? Context;
    public bool SuppressContext;
    public bool CauseSet;
    public readonly List<TracebackEntry> Traceback = new();
    internal Frame? LastFrame;

    public PyBaseException(PyType type, PyTuple args)
    {
        ExcType = type;
        Args = args;
    }

    internal override PyType Type => ExcType;

    public PyObject? GetExtra(string name) => Dict != null && Dict.TryGetValue(name, out var v) ? v : null;

    public void SetExtra(string name, PyObject v)
    {
        Dict ??= new Dictionary<string, PyObject>(StringComparer.Ordinal);
        Dict[name] = v;
    }

    /// <summary>str(exc) for builtin exception types, without running user code.</summary>
    public string BuiltinStr(bool safe)
    {
        if (ExcType.IsSubtypeOf(Exc.KeyError) && Args.Count == 1)
            return safe ? Ops.SafeRepr(Args.Items[0]) : Ops.Repr(Args.Items[0]);
        if (ExcType.IsSubtypeOf(Exc.SyntaxError))
        {
            var msg = GetExtra("msg");
            var line = GetExtra("lineno");
            var file = GetExtra("filename");
            if (msg is PyStr ms)
            {
                if (line is PyInt li && file is PyStr fs) return $"{ms.V} ({fs.V}, line {li.V})";
                return ms.V;
            }
        }
        if (Args.Count == 0) return "";
        if (Args.Count == 1) return safe ? Ops.SafeStr(Args.Items[0]) : Ops.Str(Args.Items[0]);
        return safe ? Ops.SafeRepr(Args) : Ops.Repr(Args);
    }
}

internal static class Traceback
{
    /// <summary>Records the frame in the exception traceback. Used as an exception filter, so always returns false.</summary>
    public static bool Record(PythonException e, Frame f)
    {
        var v = e.Value;
        if (!ReferenceEquals(v.LastFrame, f))
        {
            v.Traceback.Add(new TracebackEntry(f.Code.FileName, f.Line, f.Code.Name));
            v.LastFrame = f;
            AttachContext(v, f.CurrentHandled());
        }
        return false;
    }

    public static void AttachContext(PyBaseException v, PyBaseException? handled)
    {
        if (handled == null || ReferenceEquals(handled, v) || v.Context != null) return;
        // avoid cycles
        for (var c = handled; c != null; c = c.Context)
            if (ReferenceEquals(c, v)) return;
        v.Context = handled;
    }

    public static bool AttachContextFilter(PythonException e, PyBaseException handled)
    {
        AttachContext(e.Value, handled);
        return false;
    }

    /// <summary>Formats the exception like CPython's default exception hook.</summary>
    public static string Format(PyBaseException e, Func<string, int, string?> sourceLine)
    {
        var sb = new StringBuilder();
        FormatChain(e, sourceLine, sb, new HashSet<PyBaseException>(ReferenceEqualityComparer.Instance));
        return sb.ToString();
    }

    private static void FormatChain(PyBaseException e, Func<string, int, string?> sourceLine, StringBuilder sb, HashSet<PyBaseException> seen)
    {
        seen.Add(e);
        if (e.Cause != null && !seen.Contains(e.Cause))
        {
            FormatChain(e.Cause, sourceLine, sb, seen);
            sb.Append("\nThe above exception was the direct cause of the following exception:\n\n");
        }
        else if (e.Context != null && !e.SuppressContext && !seen.Contains(e.Context))
        {
            FormatChain(e.Context, sourceLine, sb, seen);
            sb.Append("\nDuring handling of the above exception, another exception occurred:\n\n");
        }
        FormatSingle(e, sourceLine, sb);
    }

    public static void FormatSingle(PyBaseException e, Func<string, int, string?> sourceLine, StringBuilder sb)
    {
        bool isSyntax = e.Type.IsSubtypeOf(Exc.SyntaxError);
        if (e.Traceback.Count > 0)
        {
            sb.Append("Traceback (most recent call last):\n");
            for (int i = e.Traceback.Count - 1; i >= 0; i--)
            {
                var t = e.Traceback[i];
                sb.Append("  File \"").Append(t.FileName).Append("\", line ").Append(t.Line).Append(", in ").Append(t.FunctionName).Append('\n');
                var src = sourceLine(t.FileName, t.Line);
                if (!string.IsNullOrWhiteSpace(src)) sb.Append("    ").Append(src.Trim()).Append('\n');
            }
        }
        if (isSyntax)
        {
            var file = e.GetExtra("filename") as PyStr;
            var line = e.GetExtra("lineno") as PyInt;
            var off = e.GetExtra("offset") as PyInt;
            var text = e.GetExtra("text") as PyStr;
            if (file != null && line != null)
            {
                sb.Append("  File \"").Append(file.V).Append("\", line ").Append(line.V).Append('\n');
                if (text != null && text.V.Trim().Length > 0)
                {
                    string raw = text.V.TrimEnd('\n', '\r');
                    string trimmed = raw.TrimStart();
                    int removed = raw.Length - trimmed.Length;
                    sb.Append("    ").Append(trimmed).Append('\n');
                    if (off != null && off.V > 0)
                    {
                        int caret = Math.Max(0, (int)off.V - 1 - removed);
                        sb.Append("    ").Append(' ', caret).Append("^\n");
                    }
                }
            }
            var msg = e.GetExtra("msg") as PyStr;
            sb.Append(TypeLabel(e.Type)).Append(": ").Append(msg?.V ?? e.BuiltinStr(true)).Append('\n');
            return;
        }
        string m;
        try
        {
            m = Ops.Str(e);
        }
        catch (PythonException)
        {
            m = "<exception str() failed>";
        }
        sb.Append(TypeLabel(e.Type));
        if (m.Length > 0) sb.Append(": ").Append(m);
        sb.Append('\n');
    }

    public static string TypeLabel(PyType t)
    {
        if (t.Module == "builtins" || t.Module == "__main__") return t.QualName;
        return t.Module + "." + t.QualName;
    }
}

/// <summary>Factories for Python exceptions raised from C#.</summary>
internal static class PyErr
{
    public static PyBaseException Make(PyType type, string? message = null)
    {
        var args = message == null ? PyTuple.Empty : new PyTuple(new PyObject[] { PyStr.New(message) });
        return new PyBaseException(type, args);
    }

    public static PythonException New(PyType type, string message) => new(Make(type, message));

    public static PythonException TypeError(string m) => New(Exc.TypeError, m);
    public static PythonException ValueError(string m) => New(Exc.ValueError, m);
    public static PythonException IndexError(string m) => New(Exc.IndexError, m);
    public static PythonException AttributeError(string m) => New(Exc.AttributeError, m);
    public static PythonException NameError(string m) => New(Exc.NameError, m);
    public static PythonException UnboundLocalError(string m) => New(Exc.UnboundLocalError, m);
    public static PythonException ZeroDivisionError(string m) => New(Exc.ZeroDivisionError, m);
    public static PythonException OverflowError(string m) => New(Exc.OverflowError, m);
    public static PythonException MemoryError(string m) => New(Exc.MemoryError, m);
    public static PythonException RecursionError(string m) => New(Exc.RecursionError, m);
    public static PythonException RuntimeError(string m) => New(Exc.RuntimeError, m);
    public static PythonException NotImplementedError(string m) => New(Exc.NotImplementedError, m);
    public static PythonException AssertionError(string m) => New(Exc.AssertionError, m);
    public static PythonException ImportError(string m) => New(Exc.ImportError, m);
    public static PythonException ModuleNotFoundError(string m) => New(Exc.ModuleNotFoundError, m);
    public static PythonException EOFError(string m) => New(Exc.EOFError, m);
    public static PythonException LookupError(string m) => New(Exc.LookupError, m);
    public static PythonException OSError(string m) => New(Exc.OSError, m);

    public static PythonException KeyError(PyObject key) =>
        new(new PyBaseException(Exc.KeyError, new PyTuple(new[] { key })));

    public static PythonException KeyError(string message) => New(Exc.KeyError, message);

    public static PythonException StopIteration(PyObject? value)
    {
        var args = value == null || value == Py.None ? PyTuple.Empty : new PyTuple(new[] { value });
        var e = new PyBaseException(Exc.StopIteration, args);
        return new PythonException(e);
    }

    public static PythonException SyntaxError(string msg, string file, int line, int col, string? text, PyType? type = null)
    {
        var e = new PyBaseException(type ?? Exc.SyntaxError, new PyTuple(new PyObject[] { PyStr.New(msg) }));
        e.SetExtra("msg", PyStr.New(msg));
        e.SetExtra("filename", PyStr.New(file));
        e.SetExtra("lineno", PyInt.From(line));
        e.SetExtra("offset", PyInt.From(col));
        e.SetExtra("text", text == null ? Py.None : PyStr.New(text));
        return new PythonException(e);
    }
}

/// <summary>The builtin exception hierarchy.</summary>
internal static class Exc
{
    public static readonly PyType BaseException;
    public static readonly PyType SystemExit;
    public static readonly PyType KeyboardInterrupt;
    public static readonly PyType GeneratorExit;
    public static readonly PyType Exception;
    public static readonly PyType StopIteration;
    public static readonly PyType StopAsyncIteration;
    public static readonly PyType ArithmeticError;
    public static readonly PyType FloatingPointError;
    public static readonly PyType OverflowError;
    public static readonly PyType ZeroDivisionError;
    public static readonly PyType AssertionError;
    public static readonly PyType AttributeError;
    public static readonly PyType BufferError;
    public static readonly PyType EOFError;
    public static readonly PyType ImportError;
    public static readonly PyType ModuleNotFoundError;
    public static readonly PyType LookupError;
    public static readonly PyType IndexError;
    public static readonly PyType KeyError;
    public static readonly PyType MemoryError;
    public static readonly PyType NameError;
    public static readonly PyType UnboundLocalError;
    public static readonly PyType OSError;
    public static readonly PyType TimeoutError;
    public static readonly PyType ReferenceError;
    public static readonly PyType RuntimeError;
    public static readonly PyType NotImplementedError;
    public static readonly PyType RecursionError;
    public static readonly PyType SyntaxError;
    public static readonly PyType IndentationError;
    public static readonly PyType TabError;
    public static readonly PyType SystemError;
    public static readonly PyType TypeError;
    public static readonly PyType ValueError;
    public static readonly PyType UnicodeError;
    public static readonly PyType Warning;
    public static readonly PyType UserWarning;
    public static readonly PyType DeprecationWarning;
    public static readonly PyType RuntimeWarning;
    public static readonly PyType SyntaxWarning;
    public static readonly PyType FutureWarning;

    public static readonly List<PyType> All = new();

    static Exc()
    {
        var obj = PyTypes.Object;
        BaseException = Make("BaseException", obj);
        BaseException.Layout = LayoutKind.Exception;
        SystemExit = Make("SystemExit", BaseException);
        KeyboardInterrupt = Make("KeyboardInterrupt", BaseException);
        GeneratorExit = Make("GeneratorExit", BaseException);
        Exception = Make("Exception", BaseException);
        StopIteration = Make("StopIteration", Exception);
        StopAsyncIteration = Make("StopAsyncIteration", Exception);
        ArithmeticError = Make("ArithmeticError", Exception);
        FloatingPointError = Make("FloatingPointError", ArithmeticError);
        OverflowError = Make("OverflowError", ArithmeticError);
        ZeroDivisionError = Make("ZeroDivisionError", ArithmeticError);
        AssertionError = Make("AssertionError", Exception);
        AttributeError = Make("AttributeError", Exception);
        BufferError = Make("BufferError", Exception);
        EOFError = Make("EOFError", Exception);
        ImportError = Make("ImportError", Exception);
        ModuleNotFoundError = Make("ModuleNotFoundError", ImportError);
        LookupError = Make("LookupError", Exception);
        IndexError = Make("IndexError", LookupError);
        KeyError = Make("KeyError", LookupError);
        MemoryError = Make("MemoryError", Exception);
        NameError = Make("NameError", Exception);
        UnboundLocalError = Make("UnboundLocalError", NameError);
        OSError = Make("OSError", Exception);
        TimeoutError = Make("TimeoutError", OSError);
        ReferenceError = Make("ReferenceError", Exception);
        RuntimeError = Make("RuntimeError", Exception);
        NotImplementedError = Make("NotImplementedError", RuntimeError);
        RecursionError = Make("RecursionError", RuntimeError);
        SyntaxError = Make("SyntaxError", Exception);
        IndentationError = Make("IndentationError", SyntaxError);
        TabError = Make("TabError", IndentationError);
        SystemError = Make("SystemError", Exception);
        TypeError = Make("TypeError", Exception);
        ValueError = Make("ValueError", Exception);
        UnicodeError = Make("UnicodeError", ValueError);
        Warning = Make("Warning", Exception);
        UserWarning = Make("UserWarning", Warning);
        DeprecationWarning = Make("DeprecationWarning", Warning);
        RuntimeWarning = Make("RuntimeWarning", Warning);
        SyntaxWarning = Make("SyntaxWarning", Warning);
        FutureWarning = Make("FutureWarning", Warning);
        RegisterMethods();
    }

    private static PyType Make(string name, PyType b)
    {
        var t = new PyType(name, b, layout: LayoutKind.Exception);
        t.Ctor = (type, args, kw) =>
        {
            var e = new PyBaseException(type, args.Length == 0 ? PyTuple.Empty : new PyTuple((PyObject[])args.Clone()));
            InitSpecial(e);
            return e;
        };
        All.Add(t);
        return t;
    }

    private static void InitSpecial(PyBaseException e)
    {
        var t = e.ExcType;
        if (t.IsSubtypeOf(SyntaxError) && e.Args.Count >= 1)
        {
            e.SetExtra("msg", e.Args.Items[0]);
            if (e.Args.Count >= 2 && e.Args.Items[1] is PyTuple info && info.Count >= 4)
            {
                e.SetExtra("filename", info.Items[0]);
                e.SetExtra("lineno", info.Items[1]);
                e.SetExtra("offset", info.Items[2]);
                e.SetExtra("text", info.Items[3]);
            }
        }
    }

    private static void RegisterMethods()
    {
        var b = BaseException;
        b.Dict["__new__"] = new PyBuiltinMethod("__new__", PyTypes.Type, (cls, a, kw) =>
            new PyBaseException((PyType)cls, new PyTuple(a)));
        b.Method("__init__", (self, a, kw) =>
        {
            var e = (PyBaseException)self;
            e.Args = a.Length == 0 ? PyTuple.Empty : new PyTuple((PyObject[])a.Clone());
            InitSpecial(e);
            return Py.None;
        });
        b.Method0("__str__", self => PyStr.New(((PyBaseException)self).BuiltinStr(false)));
        b.Method0("__repr__", self =>
        {
            var e = (PyBaseException)self;
            if (e.Args.Count == 1) return PyStr.New(e.Type.Name + "(" + Ops.Repr(e.Args.Items[0]) + ")");
            return PyStr.New(e.Type.Name + Ops.Repr(e.Args));
        });
        b.Method1("with_traceback", (self, tb) => self);
        b.Method1("add_note", (self, note) =>
        {
            var e = (PyBaseException)self;
            var notes = e.GetExtra("__notes__") as PyList ?? new PyList();
            notes.Append(note);
            e.SetExtra("__notes__", notes);
            return Py.None;
        });
    }
}
