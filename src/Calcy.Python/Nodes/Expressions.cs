using System.Runtime.CompilerServices;
using System.Text;
using Calcy.Python.Runtime;

namespace Calcy.Python.Nodes;

internal sealed class AttrExpr : Expr
{
    public readonly Expr Obj;
    public readonly string Name;

    public AttrExpr(Expr obj, string name)
    {
        Obj = obj;
        Name = name;
    }

    public override PyObject Eval(Frame f) => Ops.GetAttr(Obj.Eval(f), Name);
}

internal sealed class SubscrExpr : Expr
{
    public readonly Expr Obj;
    public readonly Expr Index;

    public SubscrExpr(Expr obj, Expr index)
    {
        Obj = obj;
        Index = index;
    }

    public override PyObject Eval(Frame f)
    {
        var o = Obj.Eval(f);
        var k = Index.Eval(f);
        if (o.GetType() == typeof(PyList) && k.GetType() == typeof(PyInt) && Unsafe.As<PyInt>(k) is var i && !i.IsBig)
        {
            var l = Unsafe.As<PyList>(o);
            long idx = i.V;
            if (idx < 0) idx += l.Count;
            if ((ulong)idx < (ulong)l.Count) return l.Items[idx];
        }
        return Ops.GetItem(o, k);
    }
}

internal sealed class SliceExpr : Expr
{
    public readonly Expr? Lower, Upper, Step;

    public SliceExpr(Expr? lower, Expr? upper, Expr? step)
    {
        Lower = lower;
        Upper = upper;
        Step = step;
    }

    public override PyObject Eval(Frame f) =>
        new PySlice(Lower?.Eval(f) ?? Py.None, Upper?.Eval(f) ?? Py.None, Step?.Eval(f) ?? Py.None);
}

// ---------------------------------------------------------------------- arithmetic

internal sealed class BinExpr : Expr
{
    public readonly BinOp Op;
    public readonly Expr A, B;

    public BinExpr(BinOp op, Expr a, Expr b)
    {
        Op = op;
        A = a;
        B = b;
    }

    public override PyObject Eval(Frame f)
    {
        var x = A.Eval(f);
        var y = B.Eval(f);
        return Arith.Binary(Op, x, y);
    }
}

internal sealed class AddExpr : Expr
{
    public readonly Expr A, B;

    public AddExpr(Expr a, Expr b)
    {
        A = a;
        B = b;
    }

    public override PyObject Eval(Frame f)
    {
        var x = A.Eval(f);
        var y = B.Eval(f);
        if (x.GetType() == typeof(PyInt) && y.GetType() == typeof(PyInt) && Unsafe.As<PyInt>(x) is var ix && Unsafe.As<PyInt>(y) is var iy && !ix.IsBig && !iy.IsBig)
        {
            long a = ix.V, b = iy.V, r = a + b;
            if (((a ^ r) & (b ^ r)) >= 0) return PyInt.From(r);
        }
        else if (x.GetType() == typeof(PyFloat) && y.GetType() == typeof(PyFloat) && Unsafe.As<PyFloat>(x) is var fx && Unsafe.As<PyFloat>(y) is var fy) return new PyFloat(fx.V + fy.V);
        return Arith.Add(x, y);
    }
}

internal sealed class SubExpr : Expr
{
    public readonly Expr A, B;

    public SubExpr(Expr a, Expr b)
    {
        A = a;
        B = b;
    }

    public override PyObject Eval(Frame f)
    {
        var x = A.Eval(f);
        var y = B.Eval(f);
        if (x.GetType() == typeof(PyInt) && y.GetType() == typeof(PyInt) && Unsafe.As<PyInt>(x) is var ix && Unsafe.As<PyInt>(y) is var iy && !ix.IsBig && !iy.IsBig)
        {
            long a = ix.V, b = iy.V, r = a - b;
            if (((a ^ b) & (a ^ r)) >= 0) return PyInt.From(r);
        }
        else if (x.GetType() == typeof(PyFloat) && y.GetType() == typeof(PyFloat) && Unsafe.As<PyFloat>(x) is var fx && Unsafe.As<PyFloat>(y) is var fy) return new PyFloat(fx.V - fy.V);
        return Arith.Sub(x, y);
    }
}

internal sealed class MulExpr : Expr
{
    public readonly Expr A, B;

    public MulExpr(Expr a, Expr b)
    {
        A = a;
        B = b;
    }

    public override PyObject Eval(Frame f)
    {
        var x = A.Eval(f);
        var y = B.Eval(f);
        if (x.GetType() == typeof(PyInt) && y.GetType() == typeof(PyInt) && Unsafe.As<PyInt>(x) is var ix && Unsafe.As<PyInt>(y) is var iy && !ix.IsBig && !iy.IsBig)
        {
            long hi = Math.BigMul(ix.V, iy.V, out long lo);
            if ((hi == 0 && lo >= 0) || (hi == -1 && lo < 0)) return PyInt.From(lo);
        }
        else if (x.GetType() == typeof(PyFloat) && y.GetType() == typeof(PyFloat) && Unsafe.As<PyFloat>(x) is var fx && Unsafe.As<PyFloat>(y) is var fy) return new PyFloat(fx.V * fy.V);
        return Arith.Mul(x, y);
    }
}

internal sealed class UnaryExpr : Expr
{
    public readonly int Kind; // 0 neg, 1 pos, 2 invert
    public readonly Expr A;

    public UnaryExpr(int kind, Expr a)
    {
        Kind = kind;
        A = a;
    }

    public override PyObject Eval(Frame f)
    {
        var x = A.Eval(f);
        return Kind switch
        {
            0 => Arith.Neg(x),
            1 => Arith.Pos(x),
            _ => Arith.Invert(x),
        };
    }
}

internal sealed class NotExpr : Expr
{
    public readonly Expr A;
    public NotExpr(Expr a) { A = a; }
    public override PyObject Eval(Frame f) => Py.Bool(!Ops.Truthy(A.Eval(f)));
}

internal sealed class AndExpr : Expr
{
    public readonly Expr[] Values;
    public AndExpr(Expr[] values) { Values = values; }

    public override PyObject Eval(Frame f)
    {
        PyObject v = Py.None;
        for (int i = 0; i < Values.Length; i++)
        {
            v = Values[i].Eval(f);
            if (i < Values.Length - 1 && !Ops.Truthy(v)) return v;
        }
        return v;
    }
}

internal sealed class OrExpr : Expr
{
    public readonly Expr[] Values;
    public OrExpr(Expr[] values) { Values = values; }

    public override PyObject Eval(Frame f)
    {
        PyObject v = Py.None;
        for (int i = 0; i < Values.Length; i++)
        {
            v = Values[i].Eval(f);
            if (i < Values.Length - 1 && Ops.Truthy(v)) return v;
        }
        return v;
    }
}

internal sealed class IfExpr : Expr
{
    public readonly Expr Test, Body, OrElse;

    public IfExpr(Expr test, Expr body, Expr orElse)
    {
        Test = test;
        Body = body;
        OrElse = orElse;
    }

    public override PyObject Eval(Frame f) => Ops.Truthy(Test.Eval(f)) ? Body.Eval(f) : OrElse.Eval(f);
}

internal enum CmpOp : byte
{
    Lt,
    Le,
    Eq,
    Ne,
    Gt,
    Ge,
    In,
    NotIn,
    Is,
    IsNot,
}

internal sealed class CompareExpr : Expr
{
    public readonly Expr Left;
    public readonly CmpOp[] Ops_;
    public readonly Expr[] Rights;

    public CompareExpr(Expr left, CmpOp[] ops, Expr[] rights)
    {
        Left = left;
        Ops_ = ops;
        Rights = rights;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PyObject Apply(CmpOp op, PyObject a, PyObject b)
    {
        if (a.GetType() == typeof(PyInt) && b.GetType() == typeof(PyInt) && op <= CmpOp.Ge &&
            Unsafe.As<PyInt>(a) is var ia && Unsafe.As<PyInt>(b) is var ib && !ia.IsBig && !ib.IsBig)
        {
            long x = ia.V, y = ib.V;
            return Py.Bool(op switch
            {
                CmpOp.Lt => x < y,
                CmpOp.Le => x <= y,
                CmpOp.Eq => x == y,
                CmpOp.Ne => x != y,
                CmpOp.Gt => x > y,
                _ => x >= y,
            });
        }
        switch (op)
        {
            case CmpOp.Lt: return Ops.RichCompare(a, b, CompareOp.Lt);
            case CmpOp.Le: return Ops.RichCompare(a, b, CompareOp.Le);
            case CmpOp.Eq: return Ops.Eq(a, b);
            case CmpOp.Ne: return Ops.Ne(a, b);
            case CmpOp.Gt: return Ops.RichCompare(a, b, CompareOp.Gt);
            case CmpOp.Ge: return Ops.RichCompare(a, b, CompareOp.Ge);
            case CmpOp.In: return Py.Bool(Ops.Contains(b, a));
            case CmpOp.NotIn: return Py.Bool(!Ops.Contains(b, a));
            case CmpOp.Is: return Py.Bool(ReferenceEquals(a, b));
            default: return Py.Bool(!ReferenceEquals(a, b));
        }
    }

    public override PyObject Eval(Frame f)
    {
        var a = Left.Eval(f);
        if (Rights.Length == 1) return Apply(Ops_[0], a, Rights[0].Eval(f));
        PyObject r = PyBool.True;
        for (int i = 0; i < Rights.Length; i++)
        {
            var b = Rights[i].Eval(f);
            r = Apply(Ops_[i], a, b);
            if (i < Rights.Length - 1 && !Ops.Truthy(r)) return r;
            a = b;
        }
        return r;
    }
}

/// <summary>Fast single comparison for the common int case in loop conditions.</summary>
internal sealed class Compare1Expr : Expr
{
    public readonly CmpOp Op;
    public readonly Expr A, B;

    public Compare1Expr(CmpOp op, Expr a, Expr b)
    {
        Op = op;
        A = a;
        B = b;
    }

    public override PyObject Eval(Frame f)
    {
        var x = A.Eval(f);
        var y = B.Eval(f);
        return CompareExpr.Apply(Op, x, y);
    }
}

internal sealed class NamedExpr : Expr
{
    public readonly Target Target;
    public readonly Expr Value;

    public NamedExpr(Target target, Expr value)
    {
        Target = target;
        Value = value;
    }

    public override PyObject Eval(Frame f)
    {
        var v = Value.Eval(f);
        Target.Assign(f, v);
        return v;
    }
}

// ---------------------------------------------------------------------- displays

/// <summary>An element of a display or call: an expression, optionally star-unpacked.</summary>
internal readonly struct Elt
{
    public readonly Expr Expr;
    public readonly bool Star;

    public Elt(Expr expr, bool star)
    {
        Expr = expr;
        Star = star;
    }
}

internal sealed class ListExpr : Expr
{
    public readonly Elt[] Elts;
    private readonly bool _anyStar;

    public ListExpr(Elt[] elts)
    {
        Elts = elts;
        _anyStar = elts.Any(e => e.Star);
    }

    public override PyObject Eval(Frame f)
    {
        if (!_anyStar)
        {
            var arr = new PyObject[Elts.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = Elts[i].Expr.Eval(f);
            return PyList.Wrap(arr);
        }
        var l = new PyList();
        foreach (var e in Elts)
        {
            if (e.Star) l.Extend(CheckStar(e.Expr.Eval(f), null));
            else l.Append(e.Expr.Eval(f));
        }
        return l;
    }

    public static PyObject CheckStar(PyObject v, string? callName)
    {
        if (Ops.IsIterable(v)) return v;
        throw PyErr.TypeError(callName != null
            ? $"{callName}() argument after * must be an iterable, not {v.Type.Name}"
            : $"Value after * must be an iterable, not {v.Type.Name}");
    }

    public static PyObject[] EvalItems(Elt[] elts, Frame f, string? callName = null)
    {
        var list = new List<PyObject>(elts.Length);
        foreach (var e in elts)
        {
            if (e.Star)
            {
                foreach (var v in Ops.Iterate(CheckStar(e.Expr.Eval(f), callName))) list.Add(v);
                if (list.Count > Limits.MaxCollection) Limits.CheckSize(list.Count);
            }
            else list.Add(e.Expr.Eval(f));
        }
        return list.ToArray();
    }
}

internal sealed class TupleExpr : Expr
{
    public readonly Elt[] Elts;
    private readonly bool _anyStar;

    public TupleExpr(Elt[] elts)
    {
        Elts = elts;
        _anyStar = elts.Any(e => e.Star);
    }

    public override PyObject Eval(Frame f)
    {
        if (Elts.Length == 0) return PyTuple.Empty;
        if (!_anyStar)
        {
            var arr = new PyObject[Elts.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = Elts[i].Expr.Eval(f);
            return new PyTuple(arr);
        }
        return PyTuple.Create(ListExpr.EvalItems(Elts, f));
    }
}

internal sealed class SetExpr : Expr
{
    public readonly Elt[] Elts;

    public SetExpr(Elt[] elts)
    {
        Elts = elts;
    }

    public override PyObject Eval(Frame f)
    {
        var s = new PySet();
        foreach (var e in Elts)
        {
            if (e.Star) s.Table.Update(ListExpr.CheckStar(e.Expr.Eval(f), null));
            else s.Table.Add(e.Expr.Eval(f));
        }
        return s;
    }
}

internal sealed class ConstSetExpr : Expr
{
    private readonly SetTable _constant;

    public ConstSetExpr(SetTable constant)
    {
        _constant = constant;
    }

    public override PyObject Eval(Frame f)
    {
        var s = new PySet();
        s.Table.Merge(_constant);
        return s;
    }
}

internal sealed class DictExpr : Expr
{
    public readonly Expr?[] Keys;
    public readonly Expr[] Values;

    public DictExpr(Expr?[] keys, Expr[] values)
    {
        Keys = keys;
        Values = values;
    }

    public override PyObject Eval(Frame f)
    {
        var d = new PyDict(Keys.Length);
        for (int i = 0; i < Keys.Length; i++)
        {
            if (Keys[i] == null)
            {
                var m = Values[i].Eval(f);
                if (m is not PyDict && !Ops.HasAttr(m, "keys"))
                    throw PyErr.TypeError($"'{m.Type.Name}' object is not a mapping");
                d.Update(m);
            }
            else
            {
                var k = Keys[i]!.Eval(f);
                d.SetItem(k, Values[i].Eval(f));
            }
        }
        return d;
    }
}

// ---------------------------------------------------------------------- f-strings

internal sealed class JoinedStrExpr : Expr
{
    public readonly Expr[] Parts;
    public JoinedStrExpr(Expr[] parts) { Parts = parts; }

    public override PyObject Eval(Frame f)
    {
        if (Parts.Length == 1) return Parts[0].Eval(f);
        var sb = new StringBuilder();
        foreach (var p in Parts)
        {
            sb.Append(((PyStr)p.Eval(f)).V);
            if (sb.Length > Limits.MaxCollection) Limits.CheckStringLength(sb.Length);
        }
        return PyStr.New(sb.ToString());
    }
}

internal sealed class FormattedExpr : Expr
{
    public readonly Expr Value;
    public readonly char Conversion;
    public readonly Expr? Spec;

    public FormattedExpr(Expr value, char conversion, Expr? spec)
    {
        Value = value;
        Conversion = conversion;
        Spec = spec;
    }

    public override PyObject Eval(Frame f)
    {
        var v = Value.Eval(f);
        switch (Conversion)
        {
            case 'r':
                v = PyStr.New(Ops.Repr(v));
                break;
            case 's':
                v = PyStr.New(Ops.Str(v));
                break;
            case 'a':
                v = PyStr.New(Ops.Ascii(v));
                break;
        }
        string spec = Spec == null ? "" : ((PyStr)Spec.Eval(f)).V;
        if (spec.Length == 0 && v.GetType() == typeof(PyStr)) return v;
        if (spec.Length == 0 && v.GetType() == typeof(PyInt)) return PyStr.New(v.ToString()!);
        return PyStr.New(Formatter.Format(v, spec));
    }
}

// ---------------------------------------------------------------------- functions and comprehensions

internal sealed class MakeFunctionExpr : Expr
{
    public readonly Code Code;
    public readonly Expr[] Defaults;
    public readonly (string Name, Expr Value)[] KwDefaults;
    public readonly int[] ClosureSource;
    public readonly string Module;

    public MakeFunctionExpr(Code code, Expr[] defaults, (string, Expr)[] kwDefaults, int[] closureSource, string module)
    {
        Code = code;
        Defaults = defaults;
        KwDefaults = kwDefaults;
        ClosureSource = closureSource;
        Module = module;
    }

    public override PyObject Eval(Frame f)
    {
        var defs = Defaults.Length == 0 ? Array.Empty<PyObject>() : new PyObject[Defaults.Length];
        for (int i = 0; i < defs.Length; i++) defs[i] = Defaults[i].Eval(f);
        Dictionary<string, PyObject>? kwd = null;
        if (KwDefaults.Length > 0)
        {
            kwd = new Dictionary<string, PyObject>(StringComparer.Ordinal);
            foreach (var (n, e) in KwDefaults) kwd[n] = e.Eval(f);
        }
        var closure = BuildClosure(f, ClosureSource);
        return new PyFunction(Code, f.Globals, defs, kwd, closure) { Module = Module };
    }

    public static Cell[] BuildClosure(Frame f, int[] source)
    {
        if (source.Length == 0) return Array.Empty<Cell>();
        var closure = new Cell[source.Length];
        for (int i = 0; i < source.Length; i++) closure[i] = f.Cells![source[i]];
        return closure;
    }
}

internal sealed class ComprehensionExpr : Expr
{
    public readonly Code Code;
    public readonly Expr OuterIter;
    public readonly int[] ClosureSource;
    public readonly int Kind; // 0 list, 1 set, 2 dict, 3 generator
    public readonly int ResultSlot;

    public ComprehensionExpr(Code code, Expr outerIter, int[] closureSource, int kind, int resultSlot)
    {
        Code = code;
        OuterIter = outerIter;
        ClosureSource = closureSource;
        Kind = kind;
        ResultSlot = resultSlot;
    }

    public override PyObject Eval(Frame f)
    {
        var it = Ops.GetIterator(OuterIter.Eval(f));
        var code = Code;
        var locals = new PyObject?[code.NumLocals];
        locals[0] = it;
        Cell[]? cells = null;
        int ncell = code.CellNames.Length, nfree = code.FreeNames.Length;
        if (ncell + nfree > 0)
        {
            cells = new Cell[ncell + nfree];
            for (int i = 0; i < ncell; i++) cells[i] = new Cell();
            for (int i = 0; i < nfree; i++) cells[ncell + i] = f.Cells![ClosureSource[i]];
        }
        var frame = new Frame(f.Ctx, code, f.Globals, locals, cells) { ClassNs = null, Back = f, Function = f.Function };
        if (Kind == 3) return new PyGenerator(frame, "<genexpr>", code.QualName);
        PyObject result = Kind switch
        {
            0 => new PyList(),
            1 => new PySet(),
            _ => new PyDict(),
        };
        locals[ResultSlot] = result;
        if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw PyErr.RecursionError("maximum recursion depth exceeded");
        code.Body.Exec(frame);
        return result;
    }
}

internal sealed class ListAppendStmt : Stmt
{
    public readonly int Slot;
    public readonly Expr Value;

    public ListAppendStmt(int slot, Expr value)
    {
        Slot = slot;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        ((PyList)f.Locals[Slot]!).Append(Value.Eval(f));
        return Flow.Normal;
    }
}

internal sealed class SetAddStmt : Stmt
{
    public readonly int Slot;
    public readonly Expr Value;

    public SetAddStmt(int slot, Expr value)
    {
        Slot = slot;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var s = (PySet)f.Locals[Slot]!;
        s.Table.Add(Value.Eval(f));
        if (s.Count > Limits.MaxCollection) Limits.CheckSize(s.Count);
        return Flow.Normal;
    }
}

internal sealed class DictSetStmt : Stmt
{
    public readonly int Slot;
    public readonly Expr Key, Value;

    public DictSetStmt(int slot, Expr key, Expr value)
    {
        Slot = slot;
        Key = key;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var k = Key.Eval(f);
        var v = Value.Eval(f);
        ((PyDict)f.Locals[Slot]!).SetItem(k, v);
        return Flow.Normal;
    }
}

/// <summary>Checks that the native stack has room before evaluating a deeply nested subexpression.</summary>
internal sealed class StackGuardExpr : Expr
{
    private readonly Expr _inner;

    public StackGuardExpr(Expr inner)
    {
        _inner = inner;
        Line = inner.Line;
    }

    public override PyObject Eval(Frame f)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw PyErr.RecursionError("maximum recursion depth exceeded");
        return _inner.Eval(f);
    }
}
