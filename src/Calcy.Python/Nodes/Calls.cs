using Calcy.Python.Runtime;

namespace Calcy.Python.Nodes;

/// <summary>A call with positional arguments only.</summary>
internal sealed class CallExpr : Expr
{
    public readonly Expr Fn;
    public readonly Expr[] Args;

    public CallExpr(Expr fn, Expr[] args)
    {
        Fn = fn;
        Args = args;
    }

    public override PyObject Eval(Frame f)
    {
        var fn = Fn.Eval(f);
        var args = Args;
        int n = args.Length;
        if (fn is PyFunction pf)
        {
            var code = pf.Code;
            if (code.FastArgs && code.ArgCount == n)
            {
                var locals = new PyObject?[code.NumLocals];
                for (int i = 0; i < n; i++) locals[i] = args[i].Eval(f);
                return pf.CallWithLocals(f.Ctx, locals);
            }
        }
        var a = n == 0 ? Array.Empty<PyObject>() : new PyObject[n];
        for (int i = 0; i < n; i++) a[i] = args[i].Eval(f);
        return fn.Call(a, null);
    }
}

/// <summary>obj.name(args) with positional arguments, avoiding bound method allocation.</summary>
internal sealed class CallMethodExpr : Expr
{
    public readonly Expr Obj;
    public readonly string Name;
    public readonly Expr[] Args;
    private PyType? _cachedType;
    private PyObject? _cachedDescr;

    public CallMethodExpr(Expr obj, string name, Expr[] args)
    {
        Obj = obj;
        Name = name;
        Args = args;
    }

    private PyObject[] EvalArgs(Frame f)
    {
        int n = Args.Length;
        if (n == 0) return Array.Empty<PyObject>();
        var a = new PyObject[n];
        for (int i = 0; i < n; i++) a[i] = Args[i].Eval(f);
        return a;
    }

    public override PyObject Eval(Frame f)
    {
        var o = Obj.Eval(f);
        if (o is PyInstance inst)
        {
            if (!inst.Dict.ContainsStr(Name))
            {
                var d = inst.Cls.Lookup(Name);
                if (d is PyFunction pf)
                {
                    var code = pf.Code;
                    int n = Args.Length;
                    if (code.FastArgs && code.ArgCount == n + 1)
                    {
                        var locals = new PyObject?[code.NumLocals];
                        locals[0] = o;
                        for (int i = 0; i < n; i++) locals[i + 1] = Args[i].Eval(f);
                        return pf.CallWithLocals(f.Ctx, locals);
                    }
                    var a = new PyObject[n + 1];
                    a[0] = o;
                    for (int i = 0; i < n; i++) a[i + 1] = Args[i].Eval(f);
                    return pf.Call(a, null);
                }
            }
        }
        else if (o.Type.IsBuiltin && o is not (PyType or PyModule or PySuper))
        {
            var t = o.Type;
            PyObject? d;
            if (ReferenceEquals(t, _cachedType)) d = _cachedDescr;
            else
            {
                d = t.Lookup(Name);
                _cachedType = t;
                _cachedDescr = d;
            }
            if (d is PyBuiltinMethod bm) return bm.Fn(o, EvalArgs(f), null);
        }
        var m = Ops.GetAttr(o, Name);
        return m.Call(EvalArgs(f), null);
    }
}

/// <summary>A call with keyword arguments and/or star unpacking.</summary>
internal sealed class CallGeneralExpr : Expr
{
    public readonly Expr Fn;
    public readonly Elt[] Args;
    public readonly string?[] KwNames;
    public readonly Expr[] KwValues;
    private readonly bool _simplePositional;
    private readonly bool _simpleKw;
    private readonly string[]? _staticNames;

    public CallGeneralExpr(Expr fn, Elt[] args, string?[] kwNames, Expr[] kwValues)
    {
        Fn = fn;
        Args = args;
        KwNames = kwNames;
        KwValues = kwValues;
        _simplePositional = args.All(a => !a.Star);
        _simpleKw = kwNames.All(n => n != null);
        if (_simpleKw) _staticNames = kwNames.Select(n => n!).ToArray();
    }

    public override PyObject Eval(Frame f)
    {
        var fn = Fn.Eval(f);
        PyObject[] args;
        if (_simplePositional)
        {
            args = Args.Length == 0 ? Array.Empty<PyObject>() : new PyObject[Args.Length];
            for (int i = 0; i < args.Length; i++) args[i] = Args[i].Expr.Eval(f);
        }
        else args = ListExpr.EvalItems(Args, f, CallName(fn));
        var kw = EvalKw(f, fn);
        return fn.Call(args, kw);
    }

    private KwArgs? EvalKw(Frame f, PyObject fn)
    {
        if (KwNames.Length == 0) return null;
        if (_simpleKw)
        {
            var vals = new PyObject[KwValues.Length];
            for (int i = 0; i < vals.Length; i++) vals[i] = KwValues[i].Eval(f);
            return new KwArgs(_staticNames!, vals);
        }
        var names = new List<string>();
        var values = new List<PyObject>();
        for (int i = 0; i < KwNames.Length; i++)
        {
            if (KwNames[i] != null)
            {
                AddKw(names, values, KwNames[i]!, KwValues[i].Eval(f), fn);
                continue;
            }
            var m = KwValues[i].Eval(f);
            if (m is PyDict d)
            {
                foreach (var (k, v) in d.Pairs())
                {
                    if (k is not PyStr ks) throw PyErr.TypeError("keywords must be strings");
                    AddKw(names, values, ks.V, v, fn);
                }
            }
            else
            {
                if (!Ops.HasAttr(m, "keys"))
                    throw PyErr.TypeError($"{CallName(fn)}() argument after ** must be a mapping, not {m.Type.Name}");
                foreach (var k in Ops.Iterate(Ops.CallMethod(m, "keys")))
                {
                    if (k is not PyStr ks) throw PyErr.TypeError("keywords must be strings");
                    AddKw(names, values, ks.V, Ops.GetItem(m, k), fn);
                }
            }
        }
        return names.Count == 0 ? null : new KwArgs(names.ToArray(), values.ToArray());
    }

private static string CallName(PyObject fn) => fn switch
    {
        PyFunction pf => pf.QualName,
        PyBuiltinFunction bf => bf.Name,
        PyBuiltinBoundMethod bm => bm.Method.Owner.Name + "." + bm.Method.Name,
        PyBoundMethod m when m.Func is PyFunction mf => mf.QualName,
        PyType t => t.Name,
        _ => fn.Type.Name + " object",
    };

    private static void AddKw(List<string> names, List<PyObject> values, string name, PyObject value, PyObject fn)
    {
        if (names.Contains(name))
        {
            string fname = fn is PyFunction pf ? pf.QualName : fn is PyBuiltinFunction bf ? bf.Name : fn.Type.Name;
            throw PyErr.TypeError($"{fname}() got multiple values for keyword argument '{name}'");
        }
        names.Add(name);
        values.Add(value);
    }
}
