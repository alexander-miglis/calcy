namespace Calcy.Python.Runtime;

/// <summary>How instances of a type are represented in C#.</summary>
internal enum LayoutKind
{
    Object,
    Exception,
    Builtin,
}

/// <summary>A Python type object: a builtin type or a user defined class.</summary>
internal sealed class PyType : PyObject
{
    public string Name;
    public string Module;
    public string QualName;
    public PyType[] Bases;
    public PyType[] Mro;
    public readonly Dictionary<string, PyObject> Dict = new(StringComparer.Ordinal);
    public readonly bool IsBuiltin;
    public bool Final;
    public LayoutKind Layout;
    public Func<PyType, PyObject[], KwArgs?, PyObject>? Ctor;
    public PyObject? Doc;

    /// <summary>For user classes derived from str, int, float, list, tuple or dict: that builtin type.</summary>
    public PyType? BuiltinBase;

    internal override PyType Type => PyTypes.Type;

    /// <summary>Creates a builtin type.</summary>
    public PyType(string name, PyType? baseType, string module = "builtins", bool final = false, LayoutKind layout = LayoutKind.Builtin)
    {
        Name = name;
        QualName = name;
        Module = module;
        IsBuiltin = true;
        Final = final;
        Layout = layout;
        if (baseType == null)
        {
            Bases = Array.Empty<PyType>();
            Mro = new[] { this };
        }
        else
        {
            Bases = new[] { baseType };
            var mro = new PyType[baseType.Mro.Length + 1];
            mro[0] = this;
            Array.Copy(baseType.Mro, 0, mro, 1, baseType.Mro.Length);
            Mro = mro;
            if (baseType.Layout == LayoutKind.Exception) Layout = LayoutKind.Exception;
        }
    }

    private PyType(string name, string qualName, string module, PyType[] bases)
    {
        Name = name;
        QualName = qualName;
        Module = module;
        Bases = bases;
        Mro = Array.Empty<PyType>();
        IsBuiltin = false;
    }

    public bool IsException => Layout == LayoutKind.Exception;

    public string FullName => Module == "builtins" ? QualName : Module + "." + QualName;

    public PyObject? Lookup(string name)
    {
        var mro = Mro;
        for (int i = 0; i < mro.Length; i++)
            if (mro[i].Dict.TryGetValue(name, out var v)) return v;
        return null;
    }

    public bool IsSubtypeOf(PyType other)
    {
        if (ReferenceEquals(this, other)) return true;
        var mro = Mro;
        for (int i = 1; i < mro.Length; i++)
            if (ReferenceEquals(mro[i], other)) return true;
        return false;
    }

    /// <summary>Creates a user defined class (the three argument form of type()).</summary>
    public static PyType CreateClass(string name, string qualName, string module, PyType[] bases, Dictionary<string, PyObject> ns)
    {
        if (bases.Length == 0) bases = new[] { PyTypes.Object };
        var layout = LayoutKind.Object;
        PyType? builtinBase = null;
        foreach (var b in bases)
        {
            if (b.Final)
                throw PyErr.TypeError($"type '{b.Name}' is not an acceptable base type");
            PyType? root = null;
            if (b.IsBuiltin && b.Layout == LayoutKind.Builtin && b != PyTypes.Object)
            {
                if (!Subclassing.IsSubclassable(b))
                    throw PyErr.TypeError($"subclassing the builtin type '{b.Name}' is not supported in this interpreter");
                root = b;
            }
            else if (!b.IsBuiltin && b.BuiltinBase != null) root = b.BuiltinBase;
            if (root != null)
            {
                if ((builtinBase != null && builtinBase != root) || layout == LayoutKind.Exception)
                    throw PyErr.TypeError("multiple bases have instance lay-out conflict");
                builtinBase = root;
            }
            if (b.Layout == LayoutKind.Exception)
            {
                if (builtinBase != null) throw PyErr.TypeError("multiple bases have instance lay-out conflict");
                layout = LayoutKind.Exception;
            }
        }
        if (builtinBase != null) layout = LayoutKind.Builtin;
        for (int i = 0; i < bases.Length; i++)
            for (int j = i + 1; j < bases.Length; j++)
                if (ReferenceEquals(bases[i], bases[j]))
                    throw PyErr.TypeError($"duplicate base class {bases[i].Name}");

        var t = new PyType(name, qualName, module, bases) { Layout = layout, BuiltinBase = builtinBase };
        t.Mro = ComputeMro(t, bases);
        foreach (var kv in ns)
        {
            var v = kv.Value;
            if (kv.Key == "__new__" && v is PyFunction) v = new PyStaticMethod(v);
            if ((kv.Key == "__init_subclass__" || kv.Key == "__class_getitem__") && v is PyFunction) v = new PyClassMethod(v);
            t.Dict[kv.Key] = v;
        }
        if (t.Dict.ContainsKey("__eq__") && !t.Dict.ContainsKey("__hash__"))
            t.Dict["__hash__"] = Py.None;
        if (t.Dict.TryGetValue("__doc__", out var doc)) t.Doc = doc;
        return t;
    }

    private static PyType[] ComputeMro(PyType cls, PyType[] bases)
    {
        var seqs = new List<List<PyType>>();
        foreach (var b in bases) seqs.Add(new List<PyType>(b.Mro));
        seqs.Add(new List<PyType>(bases));
        var result = new List<PyType> { cls };
        while (true)
        {
            seqs.RemoveAll(s => s.Count == 0);
            if (seqs.Count == 0) break;
            PyType? candidate = null;
            foreach (var s in seqs)
            {
                var head = s[0];
                bool inTail = false;
                foreach (var s2 in seqs)
                {
                    for (int i = 1; i < s2.Count; i++)
                        if (ReferenceEquals(s2[i], head)) { inTail = true; break; }
                    if (inTail) break;
                }
                if (!inTail) { candidate = head; break; }
            }
            if (candidate == null)
                throw PyErr.TypeError("Cannot create a consistent method resolution order (MRO) for bases " +
                                      string.Join(", ", bases.Select(b => b.Name)));
            result.Add(candidate);
            foreach (var s in seqs)
                if (ReferenceEquals(s[0], candidate)) s.RemoveAt(0);
        }
        return result.ToArray();
    }

    internal override PyObject Call(PyObject[] args, KwArgs? kw)
    {
        if (IsBuiltin)
        {
            if (Ctor == null) throw PyErr.TypeError($"cannot create '{Name}' instances");
            return Ctor(this, args, kw);
        }
        return Instantiate(args, kw);
    }

    private PyObject Instantiate(PyObject[] args, KwArgs? kw)
    {
        PyObject obj;
        var newFn = Lookup("__new__");
        bool customNew = newFn is not PyBuiltinMethod;
        if (customNew && newFn != null)
        {
            var target = newFn is PyStaticMethod sm ? sm.Func : newFn;
            var nargs = new PyObject[args.Length + 1];
            nargs[0] = this;
            Array.Copy(args, 0, nargs, 1, args.Length);
            obj = Ops.Call(target, nargs, kw);
            if (!obj.Type.IsSubtypeOf(this)) return obj;
        }
        else
        {
            obj = CreateRaw(this, args, kw);
        }

        var init = Lookup("__init__");
        if (init is PyFunction f)
        {
            var nargs = new PyObject[args.Length + 1];
            nargs[0] = obj;
            Array.Copy(args, 0, nargs, 1, args.Length);
            var r = f.Call(nargs, kw);
            if (r != Py.None)
                throw PyErr.TypeError($"__init__() should return None, not '{r.Type.Name}'");
        }
        else if (init is PyBuiltinMethod bm && ReferenceEquals(bm, ObjectType.InitMethod))
        {
            if (!customNew && BuiltinBase == null && (args.Length > 0 || kw != null))
                throw PyErr.TypeError($"{Name}() takes no arguments");
        }
        else if (init != null)
        {
            var nargs = new PyObject[args.Length + 1];
            nargs[0] = obj;
            Array.Copy(args, 0, nargs, 1, args.Length);
            Ops.Call(init, nargs, kw);
        }
        return obj;
    }

    /// <summary>Creates an uninitialised instance of a class according to its layout.</summary>
    public static PyObject CreateRaw(PyType cls, PyObject[] args, KwArgs? kw = null)
    {
        if (cls.IsBuiltin)
        {
            if (cls.Layout == LayoutKind.Exception) return new PyBaseException(cls, new PyTuple((PyObject[])args.Clone()));
            if (cls == PyTypes.Object) return new PyInstance(cls);
            if (cls.Ctor == null) throw PyErr.TypeError($"cannot create '{cls.Name}' instances");
            return cls.Ctor(cls, args, kw);
        }
        if (cls.BuiltinBase != null) return Subclassing.Create(cls, cls.BuiltinBase, args, kw);
        return cls.Layout == LayoutKind.Exception
            ? new PyBaseException(cls, new PyTuple((PyObject[])args.Clone()))
            : new PyInstance(cls);
    }
}

/// <summary>Helpers to populate builtin type dictionaries.</summary>
internal static class TypeBuilder
{
    public static PyBuiltinMethod Method(this PyType t, string name, Func<PyObject, PyObject[], KwArgs?, PyObject> fn)
    {
        var m = new PyBuiltinMethod(name, t, fn);
        t.Dict[name] = m;
        return m;
    }

    public static void Method0(this PyType t, string name, Func<PyObject, PyObject> fn)
    {
        t.Method(name, (self, a, kw) =>
        {
            if (a.Length != 0 || kw != null)
                throw PyErr.TypeError($"{t.Name}.{name}() takes no arguments ({a.Length} given)");
            return fn(self);
        });
    }

    public static void Method1(this PyType t, string name, Func<PyObject, PyObject, PyObject> fn)
    {
        t.Method(name, (self, a, kw) =>
        {
            if (a.Length != 1 || kw != null)
                throw PyErr.TypeError($"{t.Name}.{name}() takes exactly one argument ({a.Length} given)");
            return fn(self, a[0]);
        });
    }

    public static void Method2(this PyType t, string name, Func<PyObject, PyObject, PyObject, PyObject> fn)
    {
        t.Method(name, (self, a, kw) =>
        {
            if (a.Length != 2 || kw != null)
                throw PyErr.TypeError($"{name} expected 2 arguments, got {a.Length}");
            return fn(self, a[0], a[1]);
        });
    }

    public static void ClassMethod(this PyType t, string name, Func<PyType, PyObject[], KwArgs?, PyObject> fn)
    {
        t.Dict[name] = new PyBuiltinClassMethod(name, t, fn);
    }

    public static void StaticMethod(this PyType t, string name, Func<PyObject[], KwArgs?, PyObject> fn)
    {
        t.Dict[name] = new PyStaticMethod(new PyBuiltinFunction(name, fn));
    }
}

/// <summary>The builtin type objects.</summary>
internal static class PyTypes
{
    public static readonly PyType Object;
    public static readonly PyType Type;
    public static readonly PyType Int;
    public static readonly PyType Bool;
    public static readonly PyType Float;
    public static readonly PyType Complex;
    public static readonly PyType Str;
    public static readonly PyType List;
    public static readonly PyType Tuple;
    public static readonly PyType Dict;
    public static readonly PyType Set;
    public static readonly PyType FrozenSet;
    public static readonly PyType Range;
    public static readonly PyType Slice;
    public static readonly PyType NoneType;
    public static readonly PyType NotImplementedType;
    public static readonly PyType EllipsisType;
    public static readonly PyType Function;
    public static readonly PyType BuiltinFunction;
    public static readonly PyType Method;
    public static readonly PyType MethodDescriptor;
    public static readonly PyType ClassMethodDescriptor;
    public static readonly PyType Module;
    public static readonly PyType Generator;
    public static readonly PyType Property;
    public static readonly PyType StaticMethod;
    public static readonly PyType ClassMethod;
    public static readonly PyType Super;
    public static readonly PyType DictKeys;
    public static readonly PyType DictValues;
    public static readonly PyType DictItems;
    public static readonly PyType HostObject;
    public static readonly PyType Map;
    public static readonly PyType Filter;
    public static readonly PyType Zip;
    public static readonly PyType Enumerate;
    public static readonly PyType Reversed;
    public static readonly PyType ListIterator;
    public static readonly PyType ListReverseIterator;
    public static readonly PyType TupleIterator;
    public static readonly PyType StrIterator;
    public static readonly PyType RangeIterator;
    public static readonly PyType DictKeyIterator;
    public static readonly PyType DictValueIterator;
    public static readonly PyType DictItemIterator;
    public static readonly PyType SetIterator;
    public static readonly PyType SeqIterator;
    public static readonly PyType CallableIterator;
    public static readonly PyType NativeIterator;

    static PyTypes()
    {
        Object = new PyType("object", null, layout: LayoutKind.Object);
        Type = new PyType("type", Object);
        Int = new PyType("int", Object);
        Bool = new PyType("bool", Int, final: true);
        Float = new PyType("float", Object);
        Complex = new PyType("complex", Object);
        Str = new PyType("str", Object);
        List = new PyType("list", Object);
        Tuple = new PyType("tuple", Object);
        Dict = new PyType("dict", Object);
        Set = new PyType("set", Object);
        FrozenSet = new PyType("frozenset", Object);
        Range = new PyType("range", Object, final: true);
        Slice = new PyType("slice", Object, final: true);
        NoneType = new PyType("NoneType", Object, final: true);
        NotImplementedType = new PyType("NotImplementedType", Object, final: true);
        EllipsisType = new PyType("ellipsis", Object, final: true);
        Function = new PyType("function", Object, final: true);
        BuiltinFunction = new PyType("builtin_function_or_method", Object, final: true);
        Method = new PyType("method", Object, final: true);
        MethodDescriptor = new PyType("method_descriptor", Object, final: true);
        ClassMethodDescriptor = new PyType("classmethod_descriptor", Object, final: true);
        Module = new PyType("module", Object, final: true);
        Generator = new PyType("generator", Object, final: true);
        Property = new PyType("property", Object, final: true);
        StaticMethod = new PyType("staticmethod", Object, final: true);
        ClassMethod = new PyType("classmethod", Object, final: true);
        Super = new PyType("super", Object, final: true);
        DictKeys = new PyType("dict_keys", Object, final: true);
        DictValues = new PyType("dict_values", Object, final: true);
        DictItems = new PyType("dict_items", Object, final: true);
        HostObject = new PyType("HostObject", Object, final: true);
        Map = new PyType("map", Object, final: true);
        Filter = new PyType("filter", Object, final: true);
        Zip = new PyType("zip", Object, final: true);
        Enumerate = new PyType("enumerate", Object, final: true);
        Reversed = new PyType("reversed", Object, final: true);
        ListIterator = new PyType("list_iterator", Object, final: true);
        ListReverseIterator = new PyType("list_reverseiterator", Object, final: true);
        TupleIterator = new PyType("tuple_iterator", Object, final: true);
        StrIterator = new PyType("str_ascii_iterator", Object, final: true);
        RangeIterator = new PyType("range_iterator", Object, final: true);
        DictKeyIterator = new PyType("dict_keyiterator", Object, final: true);
        DictValueIterator = new PyType("dict_valueiterator", Object, final: true);
        DictItemIterator = new PyType("dict_itemiterator", Object, final: true);
        SetIterator = new PyType("set_iterator", Object, final: true);
        SeqIterator = new PyType("iterator", Object, final: true);
        CallableIterator = new PyType("callable_iterator", Object, final: true);
        NativeIterator = new PyType("iterator", Object, final: true);

        foreach (var it in new[]
                 {
                     Map, Filter, Zip, Enumerate, Reversed, ListIterator, ListReverseIterator, TupleIterator, StrIterator,
                     RangeIterator, DictKeyIterator, DictValueIterator, DictItemIterator, SetIterator, SeqIterator,
                     CallableIterator, NativeIterator, Generator,
                 })
            IteratorMethods.Register(it);

        ObjectType.Register(Object);
        TypeType.Register(Type);
        IntType.Register();
        FloatType.Register();
        ComplexType.Register();
        StrType.Register();
        ListType.Register();
        TupleType.Register();
        DictType.Register();
        SetType.Register();
        RangeType.Register();
        MiscTypes.Register();
        BuiltinDunders.Register();
    }
}
