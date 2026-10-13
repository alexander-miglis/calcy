namespace Calcy.Python.Runtime;

/// <summary>A Python function defined with def or lambda.</summary>
internal sealed class PyFunction : PyObject
{
    public readonly Code Code;
    public readonly Namespace Globals;
    public PyObject[] Defaults;
    public Dictionary<string, PyObject>? KwDefaults;
    public readonly Cell[] Closure;
    public string Name;
    public string QualName;
    public string Module = "__main__";
    public Dictionary<string, PyObject>? Dict;
    public PyObject Doc;

    public PyFunction(Code code, Namespace globals, PyObject[] defaults, Dictionary<string, PyObject>? kwDefaults, Cell[] closure)
    {
        Code = code;
        Globals = globals;
        Defaults = defaults;
        KwDefaults = kwDefaults;
        Closure = closure;
        Name = code.Name;
        QualName = code.QualName;
        Doc = code.Doc;
    }

    internal override PyType Type => PyTypes.Function;

    internal override PyObject Call(PyObject[] args, KwArgs? kw)
    {
        var ctx = Interp.Current;
        var frame = MakeFrame(ctx, args, kw);
        if (Code.IsGenerator) return new PyGenerator(frame, Name, QualName);
        return ctx.RunFrame(frame);
    }

    /// <summary>Fast path used by call nodes: the caller already filled the parameter slots.</summary>
    public PyObject CallWithLocals(Interp ctx, PyObject?[] locals)
    {
        var frame = new Frame(ctx, Code, Globals, locals, MakeCells(locals)) { Function = this };
        if (Code.IsGenerator) return new PyGenerator(frame, Name, QualName);
        return ctx.RunFrame(frame);
    }

    public Frame MakeFrame(Interp ctx, PyObject[] args, KwArgs? kw)
    {
        var code = Code;
        var locals = new PyObject?[code.NumLocals];
        if (code.FastArgs && kw == null && args.Length == code.ArgCount)
            Array.Copy(args, locals, args.Length);
        else
            BindSlow(locals, args, kw);
        return new Frame(ctx, code, Globals, locals, MakeCells(locals)) { Function = this };
    }

    private Cell[]? MakeCells(PyObject?[] locals)
    {
        var code = Code;
        int ncell = code.CellNames.Length, nfree = code.FreeNames.Length;
        if (ncell + nfree == 0) return null;
        var cells = new Cell[ncell + nfree];
        for (int i = 0; i < ncell; i++) cells[i] = new Cell();
        foreach (var (slot, cell) in code.CellParams) cells[cell].Value = locals[slot];
        for (int i = 0; i < nfree; i++) cells[ncell + i] = Closure[i];
        return cells;
    }

    private void BindSlow(PyObject?[] locals, PyObject[] args, KwArgs? kw)
    {
        var code = Code;
        int argc = code.ArgCount;
        int nargs = args.Length;
        int npos = Math.Min(nargs, argc);
        Array.Copy(args, locals, npos);
        if (nargs > argc)
        {
            if (!code.HasVarArgs) throw TooManyPositional(nargs, kw);
            var extra = new PyObject[nargs - argc];
            Array.Copy(args, argc, extra, 0, extra.Length);
            locals[code.VarArgsSlot] = new PyTuple(extra);
        }
        else if (code.HasVarArgs) locals[code.VarArgsSlot] = PyTuple.Empty;
        PyDict? kwdict = null;
        if (code.HasKwArgs)
        {
            kwdict = new PyDict();
            locals[code.KwArgsSlot] = kwdict;
        }
        var pn = code.ParamNames;
        if (kw != null)
        {
            List<string>? posOnlyAsKw = null;
            for (int k = 0; k < kw.Count; k++)
            {
                string name = kw.Names[k];
                int idx = -1;
                for (int p = code.PosOnlyCount; p < argc + code.KwOnlyCount; p++)
                    if (pn[p] == name)
                    {
                        idx = p;
                        break;
                    }
                if (idx < 0)
                {
                    if (kwdict != null)
                    {
                        kwdict.SetItem(PyStr.New(name), kw.Values[k]);
                        continue;
                    }
                    for (int p = 0; p < code.PosOnlyCount; p++)
                        if (pn[p] == name)
                        {
                            (posOnlyAsKw ??= new List<string>()).Add(name);
                            idx = p;
                            break;
                        }
                    if (idx >= 0) continue;
                    throw PyErr.TypeError($"{QualName}() got an unexpected keyword argument '{name}'");
                }
                if (locals[idx] != null)
                    throw PyErr.TypeError($"{QualName}() got multiple values for argument '{name}'");
                locals[idx] = kw.Values[k];
            }
            if (posOnlyAsKw != null)
                throw PyErr.TypeError($"{QualName}() got some positional-only arguments passed as keyword arguments: '{string.Join(", ", posOnlyAsKw)}'");
        }
        if (nargs < argc)
        {
            int ndef = Defaults.Length;
            int firstDefault = argc - ndef;
            List<string>? missing = null;
            for (int p = nargs; p < argc; p++)
            {
                if (locals[p] != null) continue;
                if (p >= firstDefault) locals[p] = Defaults[p - firstDefault];
                else (missing ??= new List<string>()).Add(pn[p]);
            }
            if (missing != null) throw MissingArgs(missing, "positional");
        }
        if (code.KwOnlyCount > 0)
        {
            List<string>? missing = null;
            for (int p = argc; p < argc + code.KwOnlyCount; p++)
            {
                if (locals[p] != null) continue;
                if (KwDefaults != null && KwDefaults.TryGetValue(pn[p], out var dv)) locals[p] = dv;
                else (missing ??= new List<string>()).Add(pn[p]);
            }
            if (missing != null) throw MissingArgs(missing, "keyword-only");
        }
    }

    private PythonException TooManyPositional(int given, KwArgs? kw)
    {
        int argc = Code.ArgCount;
        int ndef = Defaults.Length;
        string takes = ndef > 0
            ? $"from {argc - ndef} to {argc} positional arguments"
            : $"{argc} positional argument{(argc == 1 ? "" : "s")}";
        return PyErr.TypeError($"{QualName}() takes {takes} but {given} {(given == 1 ? "was" : "were")} given");
    }

    private PythonException MissingArgs(List<string> names, string kind)
    {
        string list;
        var quoted = names.Select(n => "'" + n + "'").ToList();
        if (quoted.Count == 1) list = quoted[0];
        else if (quoted.Count == 2) list = quoted[0] + " and " + quoted[1];
        else list = string.Join(", ", quoted.Take(quoted.Count - 1)) + ", and " + quoted[^1];
        return PyErr.TypeError($"{QualName}() missing {names.Count} required {kind} argument{(names.Count == 1 ? "" : "s")}: {list}");
    }
}

internal sealed class PyBoundMethod : PyObject
{
    public readonly PyObject Func;
    public readonly PyObject Self;

    public PyBoundMethod(PyObject func, PyObject self)
    {
        Func = func;
        Self = self;
    }

    internal override PyType Type => PyTypes.Method;

    internal override PyObject Call(PyObject[] args, KwArgs? kw)
    {
        var n = new PyObject[args.Length + 1];
        n[0] = Self;
        Array.Copy(args, 0, n, 1, args.Length);
        return Func.Call(n, kw);
    }
}

/// <summary>A function implemented in C#.</summary>
internal sealed class PyBuiltinFunction : PyObject
{
    public readonly string Name;
    public readonly Func<PyObject[], KwArgs?, PyObject> Fn;
    public string? Module;
    public PyObject Doc = Py.None;

    public PyBuiltinFunction(string name, Func<PyObject[], KwArgs?, PyObject> fn, string? module = null)
    {
        Name = name;
        Fn = fn;
        Module = module;
    }

    internal override PyType Type => PyTypes.BuiltinFunction;

    internal override PyObject Call(PyObject[] args, KwArgs? kw) => Fn(args, kw);
}

/// <summary>A method of a builtin type, implemented in C#.</summary>
internal sealed class PyBuiltinMethod : PyObject
{
    public readonly string Name;
    public readonly PyType Owner;
    public readonly Func<PyObject, PyObject[], KwArgs?, PyObject> Fn;

    public PyBuiltinMethod(string name, PyType owner, Func<PyObject, PyObject[], KwArgs?, PyObject> fn)
    {
        Name = name;
        Owner = owner;
        Fn = fn;
    }

    internal override PyType Type => PyTypes.MethodDescriptor;

    internal override PyObject Call(PyObject[] args, KwArgs? kw)
    {
        if (args.Length == 0)
            throw PyErr.TypeError($"unbound method {Owner.Name}.{Name}() needs an argument");
        if (!args[0].Type.IsSubtypeOf(Owner))
            throw PyErr.TypeError($"descriptor '{Name}' for '{Owner.Name}' objects doesn't apply to a '{args[0].Type.Name}' object");
        var rest = new PyObject[args.Length - 1];
        Array.Copy(args, 1, rest, 0, rest.Length);
        return Fn(args[0], rest, kw);
    }
}

internal sealed class PyBuiltinBoundMethod : PyObject
{
    public readonly PyBuiltinMethod Method;
    public readonly PyObject Self;

    public PyBuiltinBoundMethod(PyBuiltinMethod method, PyObject self)
    {
        Method = method;
        Self = self;
    }

    internal override PyType Type => PyTypes.BuiltinFunction;

    internal override PyObject Call(PyObject[] args, KwArgs? kw) => Method.Fn(Self, args, kw);
}

internal sealed class PyBuiltinClassMethod : PyObject
{
    public readonly string Name;
    public readonly PyType Owner;
    public readonly Func<PyType, PyObject[], KwArgs?, PyObject> Fn;

    public PyBuiltinClassMethod(string name, PyType owner, Func<PyType, PyObject[], KwArgs?, PyObject> fn)
    {
        Name = name;
        Owner = owner;
        Fn = fn;
    }

    internal override PyType Type => PyTypes.ClassMethodDescriptor;

    public PyObject BindTo(PyType t) => new PyBuiltinFunction(Name, (a, kw) => Fn(t, a, kw));
}

internal sealed class PyStaticMethod : PyObject
{
    public readonly PyObject Func;
    public PyStaticMethod(PyObject func) { Func = func; }
    internal override PyType Type => PyTypes.StaticMethod;
    internal override PyObject Call(PyObject[] args, KwArgs? kw) => Func.Call(args, kw);
}

internal sealed class PyClassMethod : PyObject
{
    public readonly PyObject Func;
    public PyClassMethod(PyObject func) { Func = func; }
    internal override PyType Type => PyTypes.ClassMethod;
}

internal sealed class PyProperty : PyObject
{
    public readonly PyObject FGet, FSet, FDel;
    public readonly PyObject Doc;

    public PyProperty(PyObject fget, PyObject fset, PyObject fdel, PyObject doc)
    {
        FGet = fget;
        FSet = fset;
        FDel = fdel;
        Doc = doc;
    }

    internal override PyType Type => PyTypes.Property;

    private string PropName => FGet is PyFunction f ? f.Name : FSet is PyFunction g ? g.Name : "?";

    public PyObject Get(PyObject obj)
    {
        if (FGet == Py.None)
            throw PyErr.AttributeError($"property '{PropName}' of '{obj.Type.Name}' object has no getter");
        return Ops.Call(FGet, new[] { obj }, null);
    }

    public void Set(PyObject obj, PyObject value)
    {
        if (FSet == Py.None)
            throw PyErr.AttributeError($"property '{PropName}' of '{obj.Type.Name}' object has no setter");
        Ops.Call(FSet, new[] { obj, value }, null);
    }

    public void Delete(PyObject obj)
    {
        if (FDel == Py.None)
            throw PyErr.AttributeError($"property '{PropName}' of '{obj.Type.Name}' object has no deleter");
        Ops.Call(FDel, new[] { obj }, null);
    }
}

/// <summary>A computed attribute of a builtin type.</summary>
internal sealed class PyGetSet : PyObject
{
    public readonly string Name;
    public readonly Func<PyObject, PyObject> Getter;
    public readonly Action<PyObject, PyObject>? Setter;

    public PyGetSet(string name, Func<PyObject, PyObject> getter, Action<PyObject, PyObject>? setter = null)
    {
        Name = name;
        Getter = getter;
        Setter = setter;
    }

    internal override PyType Type => PyTypes.Property;
}

internal sealed class PySuper : PyObject
{
    public readonly PyType StartType;
    public readonly PyObject Obj;
    public readonly PyType ObjType;

    public PySuper(PyType startType, PyObject obj, PyType objType)
    {
        StartType = startType;
        Obj = obj;
        ObjType = objType;
    }

    internal override PyType Type => PyTypes.Super;

    public PyObject? Lookup(string name)
    {
        var mro = ObjType.Mro;
        int i = Array.IndexOf(mro, StartType);
        for (int k = i + 1; k < mro.Length; k++)
        {
            if (mro[k].Dict.TryGetValue(name, out var d))
            {
                if (ReferenceEquals(Obj, ObjType))
                {
                    // super() inside a classmethod: bind to the class
                    return d switch
                    {
                        PyClassMethod cm => new PyBoundMethod(cm.Func, ObjType),
                        PyBuiltinClassMethod bcm => bcm.BindTo(ObjType),
                        PyStaticMethod sm => sm.Func,
                        _ => d,
                    };
                }
                return Ops.BindDescriptor(d, Obj, ObjType);
            }
        }
        return null;
    }
}

internal sealed class PyModule : PyObject
{
    public readonly string Name;
    public readonly Namespace Ns;
    public readonly bool IsBuiltin;

    public PyModule(string name, Namespace ns, bool isBuiltin)
    {
        Name = name;
        Ns = ns;
        IsBuiltin = isBuiltin;
        ns.Set("__name__", PyStr.New(name));
    }

    internal override PyType Type => PyTypes.Module;

    public void Add(string name, PyObject value) => Ns.Set(name, value);

    public void AddFunction(string name, Func<PyObject[], KwArgs?, PyObject> fn) =>
        Ns.Set(name, new PyBuiltinFunction(name, fn, Name));
}

/// <summary>An instance of a user defined class.</summary>
internal sealed class PyInstance : PyObject
{
    public readonly PyType Cls;
    public readonly PyDict Dict = new();

    public PyInstance(PyType cls)
    {
        Cls = cls;
    }

    internal override PyType Type => Cls;
}

/// <summary>An opaque object owned by the host application.</summary>
internal sealed class PyHostObject : PyObject
{
    public readonly object Value;

    public PyHostObject(object value)
    {
        Value = value;
    }

    internal override PyType Type => PyTypes.HostObject;
}

/// <summary>A generator object backed by a resumable statement enumerator.</summary>
internal sealed class PyGenerator : PyIterator
{
    private readonly Frame _frame;
    private IEnumerator<PyObject>? _body;
    private bool _started, _finished, _running;
    public readonly string Name;
    public readonly string QualName;

    public PyGenerator(Frame frame, string name, string qualName)
    {
        _frame = frame;
        Name = name;
        QualName = qualName;
    }

    internal override PyType Type => PyTypes.Generator;

    public bool IsFinished => _finished;

    public bool IsRunning => _running;

    public PyObject ReturnValue => _frame.ReturnValue ?? Py.None;

    public override bool MoveNext(out PyObject value) => Resume(Py.None, null, out value);

    public bool Resume(PyObject send, PyBaseException? throwIn, out PyObject value)
    {
        value = null!;
        if (_running) throw PyErr.ValueError("generator already executing");
        if (_finished)
        {
            if (throwIn != null) throw new PythonException(throwIn);
            return false;
        }
        if (!_started)
        {
            if (throwIn != null)
            {
                _finished = true;
                throw new PythonException(throwIn);
            }
            if (send != Py.None) throw PyErr.TypeError("can't send non-None value to a just-started generator");
            _started = true;
            _body = _frame.Code.Body.ExecGen(_frame).GetEnumerator();
        }
        var ctx = _frame.Ctx;
        _frame.Sent = send;
        _frame.ThrowIn = throwIn;
        if (++ctx.Depth > ctx.MaxDepth)
        {
            ctx.Depth--;
            throw PyErr.RecursionError("maximum recursion depth exceeded");
        }
        Interp.CheckStack();
        _frame.Back = ctx.CurrentFrame;
        ctx.CurrentFrame = _frame;
        _running = true;
        try
        {
            if (_body!.MoveNext())
            {
                value = _body.Current;
                return true;
            }
            _finished = true;
            return false;
        }
        catch (PythonException e) when (Traceback.Record(e, _frame))
        {
            throw;
        }
        catch (PythonException e)
        {
            _finished = true;
            if (e.Value.Type.IsSubtypeOf(Exc.StopIteration))
            {
                var re = PyErr.RuntimeError("generator raised StopIteration");
                re.Value.Cause = e.Value;
                re.Value.SuppressContext = true;
                throw re;
            }
            throw;
        }
        catch
        {
            _finished = true;
            throw;
        }
        finally
        {
            _running = false;
            ctx.CurrentFrame = _frame.Back;
            ctx.Depth--;
        }
    }

    public PyObject Send(PyObject v)
    {
        if (Resume(v, null, out var r)) return r;
        throw PyErr.StopIteration(ReturnValue);
    }

    public PyObject Throw(PyBaseException e)
    {
        if (Resume(Py.None, e, out var r)) return r;
        throw PyErr.StopIteration(ReturnValue);
    }

    public void Close()
    {
        if (!_started || _finished)
        {
            _finished = true;
            return;
        }
        try
        {
            if (Resume(Py.None, PyErr.Make(Exc.GeneratorExit), out _))
                throw PyErr.RuntimeError("generator ignored GeneratorExit");
        }
        catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.GeneratorExit) || e.Value.Type.IsSubtypeOf(Exc.StopIteration))
        {
        }
    }
}
