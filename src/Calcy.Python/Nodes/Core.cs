using System.Runtime.CompilerServices;
using Calcy.Python.Runtime;

namespace Calcy.Python.Nodes;

/// <summary>A compiled expression.</summary>
internal abstract class Expr
{
    public int Line;
    public abstract PyObject Eval(Frame f);
}

/// <summary>A compiled statement. Statements containing yield also implement ExecGen.</summary>
internal abstract class Stmt
{
    public int Line;
    public bool HasYield;
    public abstract Flow Exec(Frame f);

    public virtual IEnumerable<PyObject> ExecGen(Frame f) => throw new InvalidOperationException("statement is not resumable");

    /// <summary>Runs a child statement in generator mode, or normally when it contains no yield.</summary>
    protected static IEnumerable<PyObject> Run(Stmt s, Frame f)
    {
        if (s.HasYield) return s.ExecGen(f);
        f.GenFlow = s.Exec(f);
        return Array.Empty<PyObject>();
    }
}

internal sealed class Block : Stmt
{
    public readonly Stmt[] Body;

    public Block(Stmt[] body)
    {
        Body = body;
        foreach (var s in body)
            if (s.HasYield) HasYield = true;
        Line = body.Length > 0 ? body[0].Line : 0;
    }

    public override Flow Exec(Frame f)
    {
        var ctx = f.Ctx;
        var body = Body;
        for (int i = 0; i < body.Length; i++)
        {
            var s = body[i];
            f.Line = s.Line;
            if (++ctx.Steps >= ctx.NextCheck) ctx.Check();
            var r = s.Exec(f);
            if (r != Flow.Normal) return r;
        }
        return Flow.Normal;
    }

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var ctx = f.Ctx;
        foreach (var s in Body)
        {
            f.Line = s.Line;
            ctx.Tick();
            if (s.HasYield)
            {
                foreach (var v in s.ExecGen(f)) yield return v;
                if (f.GenFlow != Flow.Normal) yield break;
            }
            else
            {
                var r = s.Exec(f);
                if (r != Flow.Normal)
                {
                    f.GenFlow = r;
                    yield break;
                }
            }
        }
        f.GenFlow = Flow.Normal;
    }
}

// ---------------------------------------------------------------------- names

internal sealed class ConstExpr : Expr
{
    public readonly PyObject Value;
    public ConstExpr(PyObject v) { Value = v; }
    public override PyObject Eval(Frame f) => Value;
}

internal sealed class LocalExpr : Expr
{
    public readonly int Slot;
    public readonly string Name;

    public LocalExpr(int slot, string name)
    {
        Slot = slot;
        Name = name;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override PyObject Eval(Frame f) => f.Locals[Slot] ?? throw Unbound(Name);

    public static PythonException Unbound(string name) =>
        PyErr.UnboundLocalError($"cannot access local variable '{name}' where it is not associated with a value");
}

internal sealed class CellExpr : Expr
{
    public readonly int Index;
    public readonly string Name;
    public readonly bool Free;

    public CellExpr(int index, string name, bool free)
    {
        Index = index;
        Name = name;
        Free = free;
    }

    public override PyObject Eval(Frame f) => f.Cells![Index].Value ?? throw Unbound(Name, Free);

    public static PythonException Unbound(string name, bool free) => free
        ? PyErr.NameError($"cannot access free variable '{name}' where it is not associated with a value in enclosing scope")
        : LocalExpr.Unbound(name);
}

internal sealed class GlobalExpr : Expr
{
    public readonly GlobalCell Cell;
    public readonly PyObject? Builtin;

    public GlobalExpr(GlobalCell cell, PyObject? builtin)
    {
        Cell = cell;
        Builtin = builtin;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override PyObject Eval(Frame f) => Cell.Value ?? Builtin ?? throw NotDefined(Cell.Name);

    public static PythonException NotDefined(string name) => PyErr.NameError($"name '{name}' is not defined");
}

/// <summary>A name read in a class body: class namespace, then globals, then builtins.</summary>
internal sealed class ClassNameExpr : Expr
{
    public readonly string Name;
    public readonly GlobalCell Cell;
    public readonly PyObject? Builtin;

    public ClassNameExpr(string name, GlobalCell cell, PyObject? builtin)
    {
        Name = name;
        Cell = cell;
        Builtin = builtin;
    }

    public override PyObject Eval(Frame f)
    {
        if (f.ClassNs != null && f.ClassNs.TryGetValue(Name, out var v)) return v;
        return Cell.Value ?? Builtin ?? throw GlobalExpr.NotDefined(Name);
    }
}

/// <summary>A free variable read in a class body: class namespace first, then the closure cell.</summary>
internal sealed class ClassFreeExpr : Expr
{
    public readonly string Name;
    public readonly int Index;

    public ClassFreeExpr(string name, int index)
    {
        Name = name;
        Index = index;
    }

    public override PyObject Eval(Frame f)
    {
        if (f.ClassNs != null && f.ClassNs.TryGetValue(Name, out var v)) return v;
        return f.Cells![Index].Value ?? throw CellExpr.Unbound(Name, true);
    }
}

/// <summary>A name in eval/exec code: explicit locals mapping, then globals, then builtins.</summary>
internal sealed class DynNameExpr : Expr
{
    public readonly string Name;
    public readonly GlobalCell Cell;
    public readonly PyObject? Builtin;
    private readonly PyStr _key;

    public DynNameExpr(string name, GlobalCell cell, PyObject? builtin)
    {
        Name = name;
        Cell = cell;
        Builtin = builtin;
        _key = PyStr.New(name);
    }

    public override PyObject Eval(Frame f)
    {
        if (f.DynLocals != null)
        {
            var v = f.DynLocals.GetOrNull(_key);
            if (v != null) return v;
        }
        return Cell.Value ?? Builtin ?? throw GlobalExpr.NotDefined(Name);
    }
}

// ---------------------------------------------------------------------- targets

internal abstract class Target
{
    public abstract void Assign(Frame f, PyObject v);
    public abstract void Delete(Frame f);

    /// <summary>Unbinds a name without raising (used after except handlers).</summary>
    public virtual void DeleteQuiet(Frame f)
    {
        try
        {
            Delete(f);
        }
        catch (PythonException)
        {
        }
    }
}

internal sealed class LocalTarget : Target
{
    public readonly int Slot;
    public readonly string Name;

    public LocalTarget(int slot, string name)
    {
        Slot = slot;
        Name = name;
    }

    public override void Assign(Frame f, PyObject v) => f.Locals[Slot] = v;

    public override void Delete(Frame f)
    {
        if (f.Locals[Slot] == null) throw LocalExpr.Unbound(Name);
        f.Locals[Slot] = null;
    }

    public override void DeleteQuiet(Frame f) => f.Locals[Slot] = null;
}

internal sealed class CellTarget : Target
{
    public readonly int Index;
    public readonly string Name;
    public readonly bool Free;

    public CellTarget(int index, string name, bool free)
    {
        Index = index;
        Name = name;
        Free = free;
    }

    public override void Assign(Frame f, PyObject v) => f.Cells![Index].Value = v;

    public override void Delete(Frame f)
    {
        var c = f.Cells![Index];
        if (c.Value == null) throw CellExpr.Unbound(Name, Free);
        c.Value = null;
    }
}

internal sealed class GlobalTarget : Target
{
    public readonly GlobalCell Cell;

    public GlobalTarget(GlobalCell cell)
    {
        Cell = cell;
    }

    public override void Assign(Frame f, PyObject v) => Cell.Value = v;

    public override void Delete(Frame f)
    {
        if (Cell.Value == null) throw GlobalExpr.NotDefined(Cell.Name);
        Cell.Value = null;
    }
}

internal sealed class ClassNsTarget : Target
{
    public readonly string Name;

    public ClassNsTarget(string name)
    {
        Name = name;
    }

    public override void Assign(Frame f, PyObject v) => f.ClassNs![Name] = v;

    public override void Delete(Frame f)
    {
        if (!f.ClassNs!.Remove(Name)) throw GlobalExpr.NotDefined(Name);
    }
}

internal sealed class DynTarget : Target
{
    public readonly string Name;
    public readonly GlobalCell Cell;
    private readonly PyStr _key;

    public DynTarget(string name, GlobalCell cell)
    {
        Name = name;
        Cell = cell;
        _key = PyStr.New(name);
    }

    public override void Assign(Frame f, PyObject v)
    {
        if (f.DynLocals != null) f.DynLocals.SetItem(_key, v);
        else Cell.Value = v;
    }

    public override void Delete(Frame f)
    {
        if (f.DynLocals != null)
        {
            if (!f.DynLocals.Remove(_key)) throw GlobalExpr.NotDefined(Name);
            return;
        }
        if (Cell.Value == null) throw GlobalExpr.NotDefined(Name);
        Cell.Value = null;
    }
}

internal sealed class AttrTarget : Target
{
    public readonly Expr Obj;
    public readonly string Name;

    public AttrTarget(Expr obj, string name)
    {
        Obj = obj;
        Name = name;
    }

    public override void Assign(Frame f, PyObject v) => Ops.SetAttr(Obj.Eval(f), Name, v);

    public override void Delete(Frame f) => Ops.DelAttr(Obj.Eval(f), Name);
}

internal sealed class SubscrTarget : Target
{
    public readonly Expr Obj;
    public readonly Expr Index;

    public SubscrTarget(Expr obj, Expr index)
    {
        Obj = obj;
        Index = index;
    }

    public override void Assign(Frame f, PyObject v)
    {
        var o = Obj.Eval(f);
        var k = Index.Eval(f);
        Ops.SetItem(o, k, v);
    }

    public override void Delete(Frame f)
    {
        var o = Obj.Eval(f);
        var k = Index.Eval(f);
        Ops.DelItem(o, k);
    }
}

internal sealed class UnpackTarget : Target
{
    public readonly Target[] Items;
    public readonly int Star;

    public UnpackTarget(Target[] items, int star)
    {
        Items = items;
        Star = star;
    }

    public override void Assign(Frame f, PyObject v)
    {
        int n = Items.Length;
        if (Star < 0)
        {
            if (v is PyTuple t && t.Items.Length == n)
            {
                for (int i = 0; i < n; i++) Items[i].Assign(f, t.Items[i]);
                return;
            }
            if (v is PyList l && l.Count == n)
            {
                var arr = l.ToArray();
                for (int i = 0; i < n; i++) Items[i].Assign(f, arr[i]);
                return;
            }
            if (!Ops.IsIterable(v)) throw PyErr.TypeError($"cannot unpack non-iterable {v.Type.Name} object");
            var it = Ops.GetIterator(v);
            var vals = new PyObject[n];
            for (int i = 0; i < n; i++)
            {
                if (!it.MoveNext(out vals[i]))
                    throw PyErr.ValueError($"not enough values to unpack (expected {n}, got {i})");
            }
            if (it.MoveNext(out _))
                throw PyErr.ValueError($"too many values to unpack (expected {n})");
            for (int i = 0; i < n; i++) Items[i].Assign(f, vals[i]);
            return;
        }
        if (!Ops.IsIterable(v)) throw PyErr.TypeError($"cannot unpack non-iterable {v.Type.Name} object");
        var all = Ops.ToArray(v);
        int min = n - 1;
        if (all.Length < min)
            throw PyErr.ValueError($"not enough values to unpack (expected at least {min}, got {all.Length})");
        int after = n - Star - 1;
        for (int i = 0; i < Star; i++) Items[i].Assign(f, all[i]);
        int starLen = all.Length - min;
        var starItems = new PyObject[starLen];
        Array.Copy(all, Star, starItems, 0, starLen);
        Items[Star].Assign(f, PyList.Wrap(starItems));
        for (int i = 0; i < after; i++) Items[Star + 1 + i].Assign(f, all[Star + starLen + i]);
    }

    public override void Delete(Frame f)
    {
        foreach (var t in Items) t.Delete(f);
    }
}
