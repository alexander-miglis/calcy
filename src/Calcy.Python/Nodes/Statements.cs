using System.Runtime.CompilerServices;
using Calcy.Python.Runtime;

namespace Calcy.Python.Nodes;

internal sealed class ExprStmt : Stmt
{
    public readonly Expr Value;
    public ExprStmt(Expr value) { Value = value; }

    public override Flow Exec(Frame f)
    {
        Value.Eval(f);
        return Flow.Normal;
    }
}

/// <summary>Interactive mode expression statement: prints the repr of non-None results.</summary>
internal sealed class PrintExprStmt : Stmt
{
    public readonly Expr Value;
    public PrintExprStmt(Expr value) { Value = value; }

    public override Flow Exec(Frame f)
    {
        var v = Value.Eval(f);
        if (v != Py.None)
        {
            f.Ctx.Engine.WriteOutput(Ops.Repr(v) + "\n");
            f.Ctx.Engine.LastInteractiveValue = v;
            f.Globals.Set("_", v);
        }
        return Flow.Normal;
    }
}

internal sealed class PassStmt : Stmt
{
    public override Flow Exec(Frame f) => Flow.Normal;
}

internal sealed class BreakStmt : Stmt
{
    public override Flow Exec(Frame f) => Flow.Break;
}

internal sealed class ContinueStmt : Stmt
{
    public override Flow Exec(Frame f) => Flow.Continue;
}

internal sealed class ReturnStmt : Stmt
{
    public readonly Expr? Value;
    public ReturnStmt(Expr? value) { Value = value; }

    public override Flow Exec(Frame f)
    {
        f.ReturnValue = Value == null ? Py.None : Value.Eval(f);
        return Flow.Return;
    }
}

internal sealed class AssignStmt : Stmt
{
    public readonly Target Target;
    public readonly Expr Value;

    public AssignStmt(Target target, Expr value)
    {
        Target = target;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        Target.Assign(f, Value.Eval(f));
        return Flow.Normal;
    }
}

internal sealed class LocalAssignStmt : Stmt
{
    public readonly int Slot;
    public readonly Expr Value;

    public LocalAssignStmt(int slot, Expr value)
    {
        Slot = slot;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        f.Locals[Slot] = Value.Eval(f);
        return Flow.Normal;
    }
}

internal sealed class GlobalAssignStmt : Stmt
{
    public readonly GlobalCell Cell;
    public readonly Expr Value;

    public GlobalAssignStmt(GlobalCell cell, Expr value)
    {
        Cell = cell;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        Cell.Value = Value.Eval(f);
        return Flow.Normal;
    }
}

internal sealed class MultiAssignStmt : Stmt
{
    public readonly Target[] Targets;
    public readonly Expr Value;

    public MultiAssignStmt(Target[] targets, Expr value)
    {
        Targets = targets;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var v = Value.Eval(f);
        foreach (var t in Targets) t.Assign(f, v);
        return Flow.Normal;
    }
}

/// <summary>x op= value for names (local, cell, global, class or dynamic).</summary>
internal sealed class AugNameStmt : Stmt
{
    public readonly Expr Load;
    public readonly Target Store;
    public readonly BinOp Op;
    public readonly Expr Value;

    public AugNameStmt(Expr load, Target store, BinOp op, Expr value)
    {
        Load = load;
        Store = store;
        Op = op;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var cur = Load.Eval(f);
        var v = Value.Eval(f);
        Store.Assign(f, AugOps.Apply(Op, cur, v));
        return Flow.Normal;
    }
}

internal static class AugOps
{
    public static PyObject Apply(BinOp op, PyObject cur, PyObject v)
    {
        if (cur.GetType() == typeof(PyInt) && v.GetType() == typeof(PyInt) && Unsafe.As<PyInt>(cur) is var a && Unsafe.As<PyInt>(v) is var b && !a.IsBig && !b.IsBig)
        {
            long x = a.V, y = b.V;
            switch (op)
            {
                case BinOp.Add:
                {
                    long r = x + y;
                    if (((x ^ r) & (y ^ r)) >= 0) return PyInt.From(r);
                    break;
                }
                case BinOp.Sub:
                {
                    long r = x - y;
                    if (((x ^ y) & (x ^ r)) >= 0) return PyInt.From(r);
                    break;
                }
                case BinOp.Mul:
                {
                    long hi = Math.BigMul(x, y, out long lo);
                    if ((hi == 0 && lo >= 0) || (hi == -1 && lo < 0)) return PyInt.From(lo);
                    break;
                }
            }
        }
        else if (cur.GetType() == typeof(PyFloat) && v.GetType() == typeof(PyFloat) && Unsafe.As<PyFloat>(cur) is var fa && Unsafe.As<PyFloat>(v) is var fb)
        {
            switch (op)
            {
                case BinOp.Add: return new PyFloat(fa.V + fb.V);
                case BinOp.Sub: return new PyFloat(fa.V - fb.V);
                case BinOp.Mul: return new PyFloat(fa.V * fb.V);
            }
        }
        return Arith.Inplace(op, cur, v);
    }
}

internal sealed class AugLocalStmt : Stmt
{
    public readonly int Slot;
    public readonly string Name;
    public readonly BinOp Op;
    public readonly Expr Value;

    public AugLocalStmt(int slot, string name, BinOp op, Expr value)
    {
        Slot = slot;
        Name = name;
        Op = op;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var cur = f.Locals[Slot] ?? throw LocalExpr.Unbound(Name);
        var v = Value.Eval(f);
        f.Locals[Slot] = AugOps.Apply(Op, cur, v);
        return Flow.Normal;
    }
}

internal sealed class AugGlobalStmt : Stmt
{
    public readonly GlobalCell Cell;
    public readonly PyObject? Builtin;
    public readonly BinOp Op;
    public readonly Expr Value;

    public AugGlobalStmt(GlobalCell cell, PyObject? builtin, BinOp op, Expr value)
    {
        Cell = cell;
        Builtin = builtin;
        Op = op;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var cur = Cell.Value ?? Builtin ?? throw GlobalExpr.NotDefined(Cell.Name);
        var v = Value.Eval(f);
        Cell.Value = AugOps.Apply(Op, cur, v);
        return Flow.Normal;
    }
}

internal sealed class AugAttrStmt : Stmt
{
    public readonly Expr Obj;
    public readonly string Name;
    public readonly BinOp Op;
    public readonly Expr Value;

    public AugAttrStmt(Expr obj, string name, BinOp op, Expr value)
    {
        Obj = obj;
        Name = name;
        Op = op;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var o = Obj.Eval(f);
        var cur = Ops.GetAttr(o, Name);
        var v = Value.Eval(f);
        Ops.SetAttr(o, Name, AugOps.Apply(Op, cur, v));
        return Flow.Normal;
    }
}

internal sealed class AugSubscrStmt : Stmt
{
    public readonly Expr Obj;
    public readonly Expr Index;
    public readonly BinOp Op;
    public readonly Expr Value;

    public AugSubscrStmt(Expr obj, Expr index, BinOp op, Expr value)
    {
        Obj = obj;
        Index = index;
        Op = op;
        Value = value;
    }

    public override Flow Exec(Frame f)
    {
        var o = Obj.Eval(f);
        var k = Index.Eval(f);
        var cur = Ops.GetItem(o, k);
        var v = Value.Eval(f);
        Ops.SetItem(o, k, AugOps.Apply(Op, cur, v));
        return Flow.Normal;
    }
}

internal sealed class DeleteStmt : Stmt
{
    public readonly Target[] Targets;
    public DeleteStmt(Target[] targets) { Targets = targets; }

    public override Flow Exec(Frame f)
    {
        foreach (var t in Targets) t.Delete(f);
        return Flow.Normal;
    }
}

internal sealed class IfStmt : Stmt
{
    public readonly Expr Test;
    public readonly Block Body;
    public readonly Block? OrElse;

    public IfStmt(Expr test, Block body, Block? orElse)
    {
        Test = test;
        Body = body;
        OrElse = orElse;
        HasYield = body.HasYield || (orElse?.HasYield ?? false);
    }

    public override Flow Exec(Frame f)
    {
        if (Ops.Truthy(Test.Eval(f))) return Body.Exec(f);
        return OrElse == null ? Flow.Normal : OrElse.Exec(f);
    }

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var branch = Ops.Truthy(Test.Eval(f)) ? Body : OrElse;
        if (branch == null)
        {
            f.GenFlow = Flow.Normal;
            return Array.Empty<PyObject>();
        }
        return Run(branch, f);
    }
}

internal sealed class WhileStmt : Stmt
{
    public readonly Expr Test;
    public readonly Block Body;
    public readonly Block? OrElse;

    public WhileStmt(Expr test, Block body, Block? orElse)
    {
        Test = test;
        Body = body;
        OrElse = orElse;
        HasYield = body.HasYield || (orElse?.HasYield ?? false);
    }

    public override Flow Exec(Frame f)
    {
        var ctx = f.Ctx;
        while (true)
        {
            f.Line = Line;
            if (++ctx.Steps >= ctx.NextCheck) ctx.Check();
            if (!Ops.Truthy(Test.Eval(f))) break;
            var r = Body.Exec(f);
            if (r == Flow.Break) return Flow.Normal;
            if (r == Flow.Return) return r;
        }
        return OrElse == null ? Flow.Normal : OrElse.Exec(f);
    }

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var ctx = f.Ctx;
        while (true)
        {
            f.Line = Line;
            ctx.Tick();
            if (!Ops.Truthy(Test.Eval(f))) break;
            foreach (var v in Run(Body, f)) yield return v;
            var r = f.GenFlow;
            if (r == Flow.Break)
            {
                f.GenFlow = Flow.Normal;
                yield break;
            }
            if (r == Flow.Return) yield break;
        }
        if (OrElse != null)
        {
            foreach (var v in Run(OrElse, f)) yield return v;
            yield break;
        }
        f.GenFlow = Flow.Normal;
    }
}

internal sealed class ForStmt : Stmt
{
    public readonly Target Target;
    public readonly Expr Iter;
    public readonly Block Body;
    public readonly Block? OrElse;

    private readonly bool _iterIsTemporary;

    public ForStmt(Target target, Expr iter, Block body, Block? orElse)
    {
        Target = target;
        Iter = iter;
        Body = body;
        OrElse = orElse;
        _iterIsTemporary = iter is CallExpr or CallMethodExpr or CallGeneralExpr or ComprehensionExpr;
        HasYield = body.HasYield || (orElse?.HasYield ?? false);
    }

    public override Flow Exec(Frame f)
    {
        var iterable = Iter.Eval(f);
        var target = Target;
        var body = Body;
        if (iterable is PyRange r)
        {
            long cur = r.Start, step = r.Step, n = r.Length;
            if (target is LocalTarget lt)
            {
                var locals = f.Locals;
                int slot = lt.Slot;
                for (long i = 0; i < n; i++, cur += step)
                {
                    locals[slot] = PyInt.From(cur);
                    var fl = body.Exec(f);
                    if (fl == Flow.Break) return Flow.Normal;
                    if (fl == Flow.Return) return fl;
                }
            }
            else
            {
                for (long i = 0; i < n; i++, cur += step)
                {
                    target.Assign(f, PyInt.From(cur));
                    var fl = body.Exec(f);
                    if (fl == Flow.Break) return Flow.Normal;
                    if (fl == Flow.Return) return fl;
                }
            }
        }
        else if (iterable.GetType() == typeof(PyList))
        {
            var list = Unsafe.As<PyList>(iterable);
            for (int i = 0; i < list.Count; i++)
            {
                target.Assign(f, list.Items[i]);
                var fl = body.Exec(f);
                if (fl == Flow.Break) return Flow.Normal;
                if (fl == Flow.Return) return fl;
            }
        }
        else
        {
            var it = Ops.GetIterator(iterable);
            while (true)
            {
                f.Line = Line;
                if (!it.MoveNext(out var v)) break;
                target.Assign(f, v);
                var fl = body.Exec(f);
                if (fl == Flow.Break)
                {
                    CloseTemporary(it);
                    return Flow.Normal;
                }
                if (fl == Flow.Return)
                {
                    CloseTemporary(it);
                    return fl;
                }
            }
        }
        return OrElse == null ? Flow.Normal : OrElse.Exec(f);
    }

    /// <summary>
    /// Closes a generator created by the loop header itself when the loop is left early, mirroring CPython where the
    /// generator is finalized as soon as the loop drops its only reference.
    /// </summary>
    private void CloseTemporary(PyIterator it)
    {
        if (!_iterIsTemporary || it is not PyGenerator g) return;
        try
        {
            g.Close();
        }
        catch (PythonException)
        {
        }
    }

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var it = Ops.GetIterator(Iter.Eval(f));
        while (true)
        {
            f.Line = Line;
            if (!it.MoveNext(out var item)) break;
            Target.Assign(f, item);
            foreach (var v in Run(Body, f)) yield return v;
            var r = f.GenFlow;
            if (r == Flow.Break)
            {
                CloseTemporary(it);
                f.GenFlow = Flow.Normal;
                yield break;
            }
            if (r == Flow.Return)
            {
                CloseTemporary(it);
                yield break;
            }
        }
        if (OrElse != null)
        {
            foreach (var v in Run(OrElse, f)) yield return v;
            yield break;
        }
        f.GenFlow = Flow.Normal;
    }
}

internal sealed class Handler
{
    public Expr? Type;
    public Target? Name;
    public Block Body = null!;
}

internal static class ExcHelpers
{
    public static bool Matches(PyObject t, PyBaseException exc)
    {
        if (t is PyType pt)
        {
            if (!pt.IsException) throw PyErr.TypeError("catching classes that do not inherit from BaseException is not allowed");
            return exc.Type.IsSubtypeOf(pt);
        }
        if (t is PyTuple tup)
        {
            foreach (var x in tup.Items)
                if (Matches(x, exc)) return true;
            return false;
        }
        throw PyErr.TypeError("catching classes that do not inherit from BaseException is not allowed");
    }

    /// <summary>Converts unexpected CLR exceptions into Python exceptions; returns null for terminations.</summary>
    public static PythonException? Convert(Exception e)
    {
        switch (e)
        {
            case PythonException pe:
                return pe;
            case TerminationException:
                return null;
            case OutOfMemoryException:
                return PyErr.MemoryError("out of memory");
            case InsufficientExecutionStackException:
                return PyErr.RecursionError("maximum recursion depth exceeded");
            case PythonRaiseException hr:
                return HostBridge.ToPython(hr);
            case ThreadInterruptedException:
            case OperationCanceledException:
                return null;
            default:
                return PyErr.New(Exc.SystemError, "internal error: " + e.GetType().Name + ": " + e.Message);
        }
    }
}

internal sealed class TryExceptStmt : Stmt
{
    public readonly Block Body;
    public readonly Handler[] Handlers;
    public readonly Block? OrElse;

    public TryExceptStmt(Block body, Handler[] handlers, Block? orElse)
    {
        Body = body;
        Handlers = handlers;
        OrElse = orElse;
        HasYield = body.HasYield || handlers.Any(h => h.Body.HasYield) || (orElse?.HasYield ?? false);
    }

    public override Flow Exec(Frame f)
    {
        Flow flow;
        try
        {
            flow = Body.Exec(f);
        }
        catch (PythonException ex)
        {
            return Handle(f, ex);
        }
        catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
        {
            return Handle(f, pe);
        }
        if (flow == Flow.Normal && OrElse != null) return OrElse.Exec(f);
        return flow;
    }

    private Handler? Find(Frame f, PythonException ex)
    {
        Traceback.Record(ex, f);
        var exc = ex.Value;
        foreach (var h in Handlers)
            if (h.Type == null || ExcHelpers.Matches(h.Type.Eval(f), exc)) return h;
        return null;
    }

    private Flow Handle(Frame f, PythonException ex)
    {
        var h = Find(f, ex);
        if (h == null) throw ex;
        var exc = ex.Value;
        var saved = f.Handled;
        f.Handled = exc;
        try
        {
            h.Name?.Assign(f, exc);
            try
            {
                return h.Body.Exec(f);
            }
            catch (PythonException inner) when (Traceback.AttachContextFilter(inner, exc))
            {
                throw;
            }
        }
        finally
        {
            f.Handled = saved;
            h.Name?.DeleteQuiet(f);
        }
    }

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        PythonException? pending = null;
        Flow flow = Flow.Normal;
        if (Body.HasYield)
        {
            var e = Body.ExecGen(f).GetEnumerator();
            while (true)
            {
                bool moved;
                try
                {
                    moved = e.MoveNext();
                }
                catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
                {
                    pending = pe;
                    break;
                }
                if (!moved)
                {
                    flow = f.GenFlow;
                    break;
                }
                yield return e.Current;
            }
        }
        else
        {
            try
            {
                flow = Body.Exec(f);
            }
            catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
            {
                pending = pe;
            }
        }
        if (pending == null)
        {
            if (flow == Flow.Normal && OrElse != null)
            {
                foreach (var v in Run(OrElse, f)) yield return v;
                yield break;
            }
            f.GenFlow = flow;
            yield break;
        }
        var h = Find(f, pending);
        if (h == null) throw pending;
        var exc = pending.Value;
        var saved = f.Handled;
        f.Handled = exc;
        h.Name?.Assign(f, exc);
        try
        {
            if (h.Body.HasYield)
            {
                var he = h.Body.ExecGen(f).GetEnumerator();
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = he.MoveNext();
                    }
                    catch (PythonException inner)
                    {
                        Traceback.AttachContext(inner.Value, exc);
                        throw;
                    }
                    if (!moved) break;
                    yield return he.Current;
                }
            }
            else
            {
                try
                {
                    f.GenFlow = h.Body.Exec(f);
                }
                catch (PythonException inner) when (Traceback.AttachContextFilter(inner, exc))
                {
                    throw;
                }
            }
        }
        finally
        {
            f.Handled = saved;
            h.Name?.DeleteQuiet(f);
        }
    }
}

internal sealed class TryFinallyStmt : Stmt
{
    public readonly Block Body;
    public readonly Block Final;

    public TryFinallyStmt(Block body, Block final)
    {
        Body = body;
        Final = final;
        HasYield = body.HasYield || final.HasYield;
    }

    public override Flow Exec(Frame f)
    {
        Flow flow;
        try
        {
            flow = Body.Exec(f);
        }
        catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
        {
            Traceback.Record(pe, f);
            var savedHandled = f.Handled;
            f.Handled = pe.Value;
            Flow ff;
            try
            {
                ff = Final.Exec(f);
            }
            finally
            {
                f.Handled = savedHandled;
            }
            if (ff != Flow.Normal) return ff;
            if (ReferenceEquals(pe, ex)) throw;
            throw pe;
        }
        var savedRet = f.ReturnValue;
        var ff2 = Final.Exec(f);
        if (ff2 != Flow.Normal) return ff2;
        f.ReturnValue = savedRet;
        return flow;
    }

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        PythonException? pending = null;
        Flow flow = Flow.Normal;
        if (Body.HasYield)
        {
            var e = Body.ExecGen(f).GetEnumerator();
            while (true)
            {
                bool moved;
                try
                {
                    moved = e.MoveNext();
                }
                catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
                {
                    pending = pe;
                    break;
                }
                if (!moved)
                {
                    flow = f.GenFlow;
                    break;
                }
                yield return e.Current;
            }
        }
        else
        {
            try
            {
                flow = Body.Exec(f);
            }
            catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
            {
                pending = pe;
            }
        }
        if (pending != null) Traceback.Record(pending, f);
        var savedHandled = f.Handled;
        if (pending != null) f.Handled = pending.Value;
        var savedRet = f.ReturnValue;
        Flow ff;
        try
        {
            if (Final.HasYield)
            {
                foreach (var v in Final.ExecGen(f)) yield return v;
                ff = f.GenFlow;
            }
            else ff = Final.Exec(f);
        }
        finally
        {
            f.Handled = savedHandled;
        }
        if (ff != Flow.Normal)
        {
            f.GenFlow = ff;
            yield break;
        }
        if (pending != null) throw pending;
        f.ReturnValue = savedRet;
        f.GenFlow = flow;
    }
}

internal sealed class RaiseStmt : Stmt
{
    public readonly Expr? Exc;
    public readonly Expr? Cause;

    public RaiseStmt(Expr? exc, Expr? cause)
    {
        Exc = exc;
        Cause = cause;
    }

    public override Flow Exec(Frame f)
    {
        if (Exc == null)
        {
            var h = f.CurrentHandled() ?? throw PyErr.RuntimeError("No active exception to reraise");
            throw new PythonException(h);
        }
        var e = Builtins.MakeException(Exc.Eval(f), null);
        if (Cause != null)
        {
            var c = Cause.Eval(f);
            e.Cause = c == Py.None ? null : Builtins.MakeException(c, null);
            e.SuppressContext = true;
        }
        Traceback.AttachContext(e, f.CurrentHandled());
        throw new PythonException(e);
    }
}

internal sealed class AssertStmt : Stmt
{
    public readonly Expr Test;
    public readonly Expr? Msg;

    public AssertStmt(Expr test, Expr? msg)
    {
        Test = test;
        Msg = msg;
    }

    public override Flow Exec(Frame f)
    {
        if (Ops.Truthy(Test.Eval(f))) return Flow.Normal;
        var args = Msg == null ? PyTuple.Empty : new PyTuple(new[] { Msg.Eval(f) });
        throw new PythonException(new PyBaseException(Runtime.Exc.AssertionError, args));
    }
}

internal sealed class WithStmt : Stmt
{
    public readonly Expr Context;
    public readonly Target? Vars;
    public readonly Block Body;

    public WithStmt(Expr context, Target? vars, Block body)
    {
        Context = context;
        Vars = vars;
        Body = body;
        HasYield = body.HasYield;
    }

    private static (PyObject enter, PyObject exit) Protocol(PyObject mgr)
    {
        var t = mgr.Type;
        var enter = t.Lookup("__enter__");
        var exit = t.Lookup("__exit__");
        if (enter == null || exit == null)
            throw PyErr.TypeError($"'{t.Name}' object does not support the context manager protocol");
        return (enter, exit);
    }

    public override Flow Exec(Frame f)
    {
        var mgr = Context.Eval(f);
        var (enter, exit) = Protocol(mgr);
        var val = Ops.CallBound(enter, mgr, Array.Empty<PyObject>());
        Vars?.Assign(f, val);
        Flow flow;
        try
        {
            flow = Body.Exec(f);
        }
        catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
        {
            Traceback.Record(pe, f);
            var r = Ops.CallBound(exit, mgr, new PyObject[] { pe.Value.Type, pe.Value, Py.None });
            if (Ops.Truthy(r)) return Flow.Normal;
            if (ReferenceEquals(pe, ex)) throw;
            throw pe;
        }
        Ops.CallBound(exit, mgr, new[] { Py.None, Py.None, Py.None });
        return flow;
    }

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var mgr = Context.Eval(f);
        var (enter, exit) = Protocol(mgr);
        var val = Ops.CallBound(enter, mgr, Array.Empty<PyObject>());
        Vars?.Assign(f, val);
        PythonException? pending = null;
        var e = Body.ExecGen(f).GetEnumerator();
        while (true)
        {
            bool moved;
            try
            {
                moved = e.MoveNext();
            }
            catch (Exception ex) when (ex is not TerminationException && ExcHelpers.Convert(ex) is { } pe)
            {
                pending = pe;
                break;
            }
            if (!moved) break;
            yield return e.Current;
        }
        if (pending != null)
        {
            Traceback.Record(pending, f);
            var r = Ops.CallBound(exit, mgr, new PyObject[] { pending.Value.Type, pending.Value, Py.None });
            if (Ops.Truthy(r))
            {
                f.GenFlow = Flow.Normal;
                yield break;
            }
            throw pending;
        }
        var flow = f.GenFlow;
        Ops.CallBound(exit, mgr, new[] { Py.None, Py.None, Py.None });
        f.GenFlow = flow;
    }
}

internal sealed class FunctionDefStmt : Stmt
{
    public readonly MakeFunctionExpr Make;
    public readonly Expr[] Decorators;
    public readonly Target Target;

    public FunctionDefStmt(MakeFunctionExpr make, Expr[] decorators, Target target)
    {
        Make = make;
        Decorators = decorators;
        Target = target;
    }

    public override Flow Exec(Frame f)
    {
        var decs = new PyObject[Decorators.Length];
        for (int i = 0; i < decs.Length; i++) decs[i] = Decorators[i].Eval(f);
        PyObject fn = Make.Eval(f);
        for (int i = decs.Length - 1; i >= 0; i--) fn = Ops.Call(decs[i], new[] { fn }, null);
        Target.Assign(f, fn);
        return Flow.Normal;
    }
}

internal sealed class ClassDefStmt : Stmt
{
    public readonly string Name;
    public readonly string QualName;
    public readonly string Module;
    public readonly Code Code;
    public readonly Expr[] Bases;
    public readonly (string Name, Expr Value)[] Keywords;
    public readonly Expr[] Decorators;
    public readonly int[] ClosureSource;
    public readonly Target Target;

    public ClassDefStmt(string name, string qualName, string module, Code code, Expr[] bases, (string, Expr)[] keywords,
        Expr[] decorators, int[] closureSource, Target target)
    {
        Name = name;
        QualName = qualName;
        Module = module;
        Code = code;
        Bases = bases;
        Keywords = keywords;
        Decorators = decorators;
        ClosureSource = closureSource;
        Target = target;
    }

    public override Flow Exec(Frame f)
    {
        var decs = new PyObject[Decorators.Length];
        for (int i = 0; i < decs.Length; i++) decs[i] = Decorators[i].Eval(f);
        var bases = new PyType[Bases.Length];
        for (int i = 0; i < bases.Length; i++)
        {
            var b = Bases[i].Eval(f);
            bases[i] = b as PyType ?? throw PyErr.TypeError($"bases must be types, not {b.Type.Name}");
        }
        var kwargs = new List<(string, PyObject)>();
        foreach (var (n, e) in Keywords)
        {
            var v = e.Eval(f);
            if (n == "metaclass")
            {
                if (v != PyTypes.Type) throw PyErr.TypeError("custom metaclasses are not supported in this interpreter");
                continue;
            }
            kwargs.Add((n, v));
        }
        var ns = new Dictionary<string, PyObject>(StringComparer.Ordinal)
        {
            ["__module__"] = PyStr.New(Module),
            ["__qualname__"] = PyStr.New(QualName),
        };
        var code = Code;
        int ncell = code.CellNames.Length, nfree = code.FreeNames.Length;
        Cell[]? cells = null;
        if (ncell + nfree > 0)
        {
            cells = new Cell[ncell + nfree];
            for (int i = 0; i < ncell; i++) cells[i] = new Cell();
            for (int i = 0; i < nfree; i++) cells[ncell + i] = f.Cells![ClosureSource[i]];
        }
        var frame = new Frame(f.Ctx, code, f.Globals, new PyObject?[code.NumLocals], cells) { ClassNs = ns };
        f.Ctx.RunFrame(frame);
        var cls = PyType.CreateClass(Name, QualName, Module, bases, ns);
        if (code.ClassCellIndex >= 0) cells![code.ClassCellIndex].Value = cls;
        // __init_subclass__ of the closest base defining it
        for (int i = 1; i < cls.Mro.Length; i++)
        {
            if (cls.Mro[i].Dict.TryGetValue("__init_subclass__", out var isc))
            {
                if (isc is PyClassMethod cm)
                {
                    var kw = kwargs.Count == 0 ? null : new KwArgs(kwargs.Select(k => k.Item1).ToArray(), kwargs.Select(k => k.Item2).ToArray());
                    cm.Func.Call(new PyObject[] { cls }, kw);
                }
                else if (kwargs.Count > 0)
                    throw PyErr.TypeError($"{Name}.__init_subclass__() takes no keyword arguments");
                break;
            }
        }
        PyObject result = cls;
        for (int i = decs.Length - 1; i >= 0; i--) result = Ops.Call(decs[i], new[] { result }, null);
        Target.Assign(f, result);
        return Flow.Normal;
    }
}

internal sealed class ImportStmt : Stmt
{
    public readonly (string Module, Target Target)[] Names;
    public ImportStmt((string, Target)[] names) { Names = names; }

    public override Flow Exec(Frame f)
    {
        foreach (var (m, t) in Names) t.Assign(f, f.Ctx.Engine.Import(m));
        return Flow.Normal;
    }
}

internal sealed class ImportFromStmt : Stmt
{
    public readonly string Module;
    public readonly (string Name, Target Target)[] Names;
    public readonly bool Star;

    public ImportFromStmt(string module, (string, Target)[] names, bool star)
    {
        Module = module;
        Names = names;
        Star = star;
    }

    public override Flow Exec(Frame f)
    {
        var mod = f.Ctx.Engine.Import(Module);
        if (Star)
        {
            var all = mod.Ns.Get("__all__");
            IEnumerable<string> names = all != null
                ? Ops.Iterate(all).Select(x => Arg.Str(x, "__all__ item")).ToList()
                : mod.Ns.Items.Select(kv => kv.Key).Where(k => !k.StartsWith('_')).ToList();
            foreach (var n in names)
            {
                var v = mod.Ns.Get(n) ?? throw PyErr.AttributeError($"module '{Module}' has no attribute '{n}'");
                if (f.DynLocals != null) f.DynLocals.SetItem(PyStr.New(n), v);
                else f.Globals.Set(n, v);
            }
            return Flow.Normal;
        }
        foreach (var (n, t) in Names)
        {
            var v = mod.Ns.Get(n) ?? throw PyErr.ImportError($"cannot import name '{n}' from '{Module}' (unknown location)");
            t.Assign(f, v);
        }
        return Flow.Normal;
    }
}

// ---------------------------------------------------------------------- yield statements (generator bodies only)

internal static class GenHelpers
{
    public static void Resume(Frame f, int line)
    {
        var t = f.ThrowIn;
        if (t != null)
        {
            f.ThrowIn = null;
            f.Line = line;
            throw new PythonException(t);
        }
    }
}

/// <summary>A yield used as a statement, as an assignment value (target set) or as an augmented assignment value.</summary>
internal sealed class YieldStmt : Stmt
{
    public readonly Expr? Value;
    public readonly Target? Target;
    public readonly Stmt? After;

    public YieldStmt(Expr? value, Target? target, Stmt? after = null)
    {
        Value = value;
        Target = target;
        After = after;
        HasYield = true;
    }

    public override Flow Exec(Frame f) => throw new InvalidOperationException("yield outside generator");

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var v = Value == null ? Py.None : Value.Eval(f);
        yield return v;
        GenHelpers.Resume(f, Line);
        Target?.Assign(f, f.Sent ?? Py.None);
        f.GenFlow = After == null ? Flow.Normal : After.Exec(f);
    }
}

internal sealed class YieldFromStmt : Stmt
{
    public readonly Expr Source;
    public readonly Target? Target;
    public readonly Stmt? After;

    public YieldFromStmt(Expr source, Target? target, Stmt? after = null)
    {
        Source = source;
        Target = target;
        After = after;
        HasYield = true;
    }

    public override Flow Exec(Frame f) => throw new InvalidOperationException("yield outside generator");

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var src = Source.Eval(f);
        PyObject result = Py.None;
        if (src is PyGenerator g)
        {
            PyObject send = Py.None;
            PyBaseException? thrown = null;
            while (true)
            {
                bool ok;
                PyObject v;
                if (thrown != null)
                {
                    var t = thrown;
                    thrown = null;
                    if (t.Type.IsSubtypeOf(Runtime.Exc.GeneratorExit))
                    {
                        g.Close();
                        throw new PythonException(t);
                    }
                    ok = g.Resume(Py.None, t, out v);
                }
                else ok = g.Resume(send, null, out v);
                if (!ok)
                {
                    result = g.ReturnValue;
                    break;
                }
                yield return v;
                send = f.Sent ?? Py.None;
                if (f.ThrowIn != null)
                {
                    thrown = f.ThrowIn;
                    f.ThrowIn = null;
                }
            }
        }
        else
        {
            var it = Ops.GetIterator(src);
            while (it.MoveNext(out var v))
            {
                yield return v;
                GenHelpers.Resume(f, Line);
            }
        }
        Target?.Assign(f, result);
        f.GenFlow = After == null ? Flow.Normal : After.Exec(f);
    }
}
