using System.Runtime.CompilerServices;
using Calcy.Python.Parsing;

namespace Calcy.Python.Compilation;

internal enum ScopeKind : byte
{
    Module,
    Function,
    Class,
    Comprehension,
    Eval,
}

internal enum SymKind : byte
{
    Local,
    Free,
    Global,
    GlobalImplicit,
    ClassLocal,
    ClassGlobal,
    Dynamic,
}

/// <summary>A lexical scope and the resolution of its names.</summary>
internal sealed class Scope
{
    public readonly ScopeKind Kind;
    public readonly Scope? Parent;
    public readonly string Name;
    public readonly HashSet<string> Assigned = new(StringComparer.Ordinal);
    public readonly HashSet<string> Used = new(StringComparer.Ordinal);
    public readonly HashSet<string> Globals = new(StringComparer.Ordinal);
    public readonly HashSet<string> Nonlocals = new(StringComparer.Ordinal);
    public readonly HashSet<string> IterVars = new(StringComparer.Ordinal);
    public readonly List<string> Params = new();
    public readonly List<string> AssignOrder = new();
    public readonly List<Scope> Children = new();
    public bool HasYield;
    public readonly HashSet<string> Locals = new(StringComparer.Ordinal);
    public readonly List<string> CellVars = new();
    public readonly List<string> FreeVars = new();
    public readonly Dictionary<string, SymKind> Syms = new(StringComparer.Ordinal);

    public Scope(ScopeKind kind, Scope? parent, string name)
    {
        Kind = kind;
        Parent = parent;
        Name = name;
        parent?.Children.Add(this);
    }

    public bool IsFunctionLike => Kind is ScopeKind.Function or ScopeKind.Comprehension;
}

/// <summary>Builds scopes for a module (CPython symtable equivalent).</summary>
internal sealed class SymbolTable
{
    public readonly Dictionary<object, Scope> Map = new(ReferenceEqualityComparer.Instance);
    public Scope Root = null!;
    private Scope _cur = null!;

    public static SymbolTable Build(List<StmtAst> body, ScopeKind rootKind)
    {
        var st = new SymbolTable();
        st.Root = new Scope(rootKind, null, "<module>");
        st._cur = st.Root;
        foreach (var s in body) st.VisitStmt(s);
        st.ComputeLocals(st.Root);
        st.ResolveAll(st.Root);
        return st;
    }

    public static SymbolTable BuildExpr(ExprAst e, ScopeKind rootKind)
    {
        var st = new SymbolTable();
        st.Root = new Scope(rootKind, null, "<module>");
        st._cur = st.Root;
        st.VisitExpr(e);
        st.ComputeLocals(st.Root);
        st.ResolveAll(st.Root);
        return st;
    }

    private static ParseError Error(string msg, AstNode at) => new(msg, at.Line, at.Col);

    private void Bind(string name)
    {
        if (_cur.Assigned.Add(name)) _cur.AssignOrder.Add(name);
    }

    private void VisitBody(List<StmtAst> body)
    {
        foreach (var s in body) VisitStmt(s);
    }

    private void VisitStmt(StmtAst s)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw Compiler.CompileRecursion();
        switch (s)
        {
            case FunctionDefAst fd:
            {
                foreach (var d in fd.Decorators) VisitExpr(d);
                VisitArgDefaults(fd.Args);
                Bind(fd.Name);
                var fs = new Scope(ScopeKind.Function, _cur, fd.Name);
                Map[fd] = fs;
                var saved = _cur;
                _cur = fs;
                foreach (var a in fd.Args.All())
                {
                    fs.Params.Add(a.Name);
                    Bind(a.Name);
                }
                VisitBody(fd.Body);
                _cur = saved;
                break;
            }
            case ClassDefAst cd:
            {
                foreach (var d in cd.Decorators) VisitExpr(d);
                foreach (var b in cd.Bases) VisitExpr(b);
                foreach (var k in cd.Keywords) VisitExpr(k.Value);
                Bind(cd.Name);
                var cs = new Scope(ScopeKind.Class, _cur, cd.Name);
                Map[cd] = cs;
                var saved = _cur;
                _cur = cs;
                VisitBody(cd.Body);
                _cur = saved;
                break;
            }
            case ExprStmtAst es:
                VisitExpr(es.Value);
                break;
            case AssignAst a:
                VisitExpr(a.Value);
                foreach (var t in a.Targets) VisitTarget(t);
                break;
            case AugAssignAst aa:
                if (aa.Target is NameAst an) _cur.Used.Add(an.Id);
                VisitTarget(aa.Target);
                VisitExpr(aa.Value);
                break;
            case AnnAssignAst ann:
                if (ann.Value != null) VisitExpr(ann.Value);
                if (ann.Target is NameAst || ann.Value != null) VisitTarget(ann.Target);
                break;
            case IfAst i:
                VisitExpr(i.Test);
                VisitBody(i.Body);
                VisitBody(i.OrElse);
                break;
            case WhileAst w:
                VisitExpr(w.Test);
                VisitBody(w.Body);
                VisitBody(w.OrElse);
                break;
            case ForAst fo:
                VisitExpr(fo.Iter);
                VisitTarget(fo.Target);
                VisitBody(fo.Body);
                VisitBody(fo.OrElse);
                break;
            case WithAst wi:
                foreach (var (c, v) in wi.Items)
                {
                    VisitExpr(c);
                    if (v != null) VisitTarget(v);
                }
                VisitBody(wi.Body);
                break;
            case TryAst t:
                VisitBody(t.Body);
                foreach (var h in t.Handlers)
                {
                    if (h.Type != null) VisitExpr(h.Type);
                    if (h.Name != null) Bind(h.Name);
                    VisitBody(h.Body);
                }
                VisitBody(t.OrElse);
                VisitBody(t.FinalBody);
                break;
            case RaiseAst r:
                if (r.Exc != null) VisitExpr(r.Exc);
                if (r.Cause != null) VisitExpr(r.Cause);
                break;
            case AssertAst asrt:
                VisitExpr(asrt.Test);
                if (asrt.Msg != null) VisitExpr(asrt.Msg);
                break;
            case ReturnAst ret:
                if (!_cur.IsFunctionLike) throw Error("'return' outside function", ret);
                if (ret.Value != null) VisitExpr(ret.Value);
                break;
            case DeleteAst d:
                foreach (var t in d.Targets) VisitTarget(t);
                break;
            case GlobalAst g:
                foreach (var n in g.Names)
                {
                    if (_cur.Kind == ScopeKind.Module || _cur.Kind == ScopeKind.Eval) continue;
                    if (_cur.Params.Contains(n)) throw Error($"name '{n}' is parameter and global", g);
                    if (_cur.Nonlocals.Contains(n)) throw Error($"name '{n}' is nonlocal and global", g);
                    _cur.Globals.Add(n);
                }
                break;
            case NonlocalAst nl:
                if (_cur.Kind == ScopeKind.Module || _cur.Kind == ScopeKind.Eval) throw Error("nonlocal declaration not allowed at module level", nl);
                foreach (var n in nl.Names)
                {
                    if (_cur.Params.Contains(n)) throw Error($"name '{n}' is parameter and nonlocal", nl);
                    if (_cur.Globals.Contains(n)) throw Error($"name '{n}' is nonlocal and global", nl);
                    _cur.Nonlocals.Add(n);
                }
                break;
            case ImportAst im:
                foreach (var (name, asName) in im.Names) Bind(asName ?? name.Split('.')[0]);
                break;
            case ImportFromAst imf:
                foreach (var (name, asName) in imf.Names)
                {
                    if (name == "*")
                    {
                        if (_cur.Kind != ScopeKind.Module && _cur.Kind != ScopeKind.Eval) throw Error("import * only allowed at module level", imf);
                        continue;
                    }
                    Bind(asName ?? name);
                }
                break;
            case MatchAst m:
                VisitExpr(m.Subject);
                foreach (var c in m.Cases)
                {
                    VisitPattern(c.Pattern);
                    if (c.Guard != null) VisitExpr(c.Guard);
                    VisitBody(c.Body);
                }
                break;
        }
    }

    private void VisitPattern(PatternAst p)
    {
        switch (p)
        {
            case MatchValuePattern v:
                VisitExpr(v.Value);
                break;
            case MatchSequencePattern s:
                foreach (var x in s.Patterns) VisitPattern(x);
                break;
            case MatchStarPattern st:
                if (st.Name != null) Bind(st.Name);
                break;
            case MatchMappingPattern mp:
                foreach (var k in mp.Keys) VisitExpr(k);
                foreach (var x in mp.Patterns) VisitPattern(x);
                if (mp.Rest != null) Bind(mp.Rest);
                break;
            case MatchClassPattern cp:
                VisitExpr(cp.Cls);
                foreach (var x in cp.Patterns) VisitPattern(x);
                foreach (var x in cp.KwdPatterns) VisitPattern(x);
                break;
            case MatchAsPattern a:
                if (a.Pattern != null) VisitPattern(a.Pattern);
                if (a.Name != null) Bind(a.Name);
                break;
            case MatchOrPattern o:
                foreach (var x in o.Patterns) VisitPattern(x);
                break;
        }
    }

    private void VisitArgDefaults(ArgumentsAst args)
    {
        foreach (var d in args.Defaults) VisitExpr(d);
        foreach (var d in args.KwDefaults)
            if (d != null) VisitExpr(d);
    }

    private void VisitTarget(ExprAst e)
    {
        switch (e)
        {
            case NameAst n:
                Bind(n.Id);
                break;
            case TupleAst t:
                foreach (var x in t.Elts) VisitTarget(x);
                break;
            case ListAst l:
                foreach (var x in l.Elts) VisitTarget(x);
                break;
            case StarredAst s:
                VisitTarget(s.Value);
                break;
            case AttributeAst a:
                VisitExpr(a.Value);
                break;
            case SubscriptAst sub:
                VisitExpr(sub.Value);
                VisitExpr(sub.Index);
                break;
            default:
                VisitExpr(e);
                break;
        }
    }

    private void VisitExpr(ExprAst e)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw Compiler.CompileRecursion();
        switch (e)
        {
            case NameAst n:
                _cur.Used.Add(n.Id);
                if (n.Id == "super" && _cur.IsFunctionLike) _cur.Used.Add("__class__");
                break;
            case ConstantAst:
                break;
            case BinOpAst b:
                VisitExpr(b.Left);
                VisitExpr(b.Right);
                break;
            case UnaryOpAst u:
                VisitExpr(u.Operand);
                break;
            case BoolOpAst bo:
                foreach (var v in bo.Values) VisitExpr(v);
                break;
            case CompareAst c:
                VisitExpr(c.Left);
                foreach (var x in c.Comparators) VisitExpr(x);
                break;
            case CallAst call:
                VisitExpr(call.Func);
                foreach (var a in call.Args) VisitExpr(a);
                foreach (var k in call.Keywords) VisitExpr(k.Value);
                break;
            case AttributeAst a:
                VisitExpr(a.Value);
                break;
            case SubscriptAst s:
                VisitExpr(s.Value);
                VisitExpr(s.Index);
                break;
            case SliceAst sl:
                if (sl.Lower != null) VisitExpr(sl.Lower);
                if (sl.Upper != null) VisitExpr(sl.Upper);
                if (sl.Step != null) VisitExpr(sl.Step);
                break;
            case ListAst l:
                foreach (var x in l.Elts) VisitExpr(x);
                break;
            case TupleAst t:
                foreach (var x in t.Elts) VisitExpr(x);
                break;
            case SetAst st:
                foreach (var x in st.Elts) VisitExpr(x);
                break;
            case DictAst d:
                foreach (var k in d.Keys)
                    if (k != null) VisitExpr(k);
                foreach (var v in d.Values) VisitExpr(v);
                break;
            case ComprehensionAst comp:
            {
                VisitExpr(comp.Generators[0].Iter);
                string name = comp.Kind switch
                {
                    ComprehensionKind.List => "<listcomp>",
                    ComprehensionKind.Set => "<setcomp>",
                    ComprehensionKind.Dict => "<dictcomp>",
                    _ => "<genexpr>",
                };
                var cs = new Scope(ScopeKind.Comprehension, _cur, name);
                Map[comp] = cs;
                var saved = _cur;
                _cur = cs;
                cs.Params.Add(".0");
                Bind(".0");
                for (int i = 0; i < comp.Generators.Count; i++)
                {
                    var g = comp.Generators[i];
                    if (i > 0) VisitExpr(g.Iter);
                    VisitTarget(g.Target);
                    CollectNames(g.Target, cs.IterVars);
                    foreach (var cond in g.Ifs) VisitExpr(cond);
                }
                VisitExpr(comp.Elt);
                if (comp.Value != null) VisitExpr(comp.Value);
                _cur = saved;
                break;
            }
            case LambdaAst lam:
            {
                VisitArgDefaults(lam.Args);
                var fs = new Scope(ScopeKind.Function, _cur, "<lambda>");
                Map[lam] = fs;
                var saved = _cur;
                _cur = fs;
                foreach (var a in lam.Args.All())
                {
                    fs.Params.Add(a.Name);
                    Bind(a.Name);
                }
                VisitExpr(lam.Body);
                _cur = saved;
                break;
            }
            case IfExpAst ie:
                VisitExpr(ie.Test);
                VisitExpr(ie.Body);
                VisitExpr(ie.OrElse);
                break;
            case StarredAst s:
                VisitExpr(s.Value);
                break;
            case YieldAst y:
                CheckYield(y);
                if (y.Value != null) VisitExpr(y.Value);
                break;
            case YieldFromAst yf:
                CheckYield(yf);
                VisitExpr(yf.Value);
                break;
            case JoinedStrAst js:
                foreach (var p in js.Parts) VisitExpr(p);
                break;
            case FormattedValueAst fv:
                VisitExpr(fv.Value);
                if (fv.Spec != null) VisitExpr(fv.Spec);
                break;
            case NamedExprAst ne:
            {
                VisitExpr(ne.Value);
                string name = ne.Target.Id;
                if (_cur.Kind == ScopeKind.Comprehension)
                {
                    var t = _cur;
                    var path = new List<Scope>();
                    while (t.Kind == ScopeKind.Comprehension)
                    {
                        if (t.IterVars.Contains(name))
                            throw Error($"assignment expression cannot rebind comprehension iteration variable '{name}'", ne);
                        path.Add(t);
                        t = t.Parent!;
                    }
                    if (t.Kind == ScopeKind.Class)
                        throw Error("assignment expression within a comprehension cannot be used in a class body", ne);
                    if (t.Assigned.Add(name)) t.AssignOrder.Add(name);
                    foreach (var c in path)
                    {
                        if (t.Kind == ScopeKind.Module || t.Kind == ScopeKind.Eval || t.Globals.Contains(name)) c.Globals.Add(name);
                        else c.Nonlocals.Add(name);
                    }
                    _cur.Used.Add(name);
                }
                else Bind(name);
                break;
            }
        }
    }

    private static void CollectNames(ExprAst e, HashSet<string> into)
    {
        switch (e)
        {
            case NameAst n:
                into.Add(n.Id);
                break;
            case TupleAst t:
                foreach (var x in t.Elts) CollectNames(x, into);
                break;
            case ListAst l:
                foreach (var x in l.Elts) CollectNames(x, into);
                break;
            case StarredAst s:
                CollectNames(s.Value, into);
                break;
        }
    }

    private void CheckYield(ExprAst y)
    {
        if (_cur.Kind == ScopeKind.Comprehension)
            throw Error("'yield' inside comprehension", y);
        if (!_cur.IsFunctionLike) throw Error("'yield' outside function", y);
        _cur.HasYield = true;
    }

    private void ComputeLocals(Scope s)
    {
        if (s.IsFunctionLike)
        {
            foreach (var n in s.Params)
                if (!s.Globals.Contains(n) && !s.Nonlocals.Contains(n)) s.Locals.Add(n);
            foreach (var n in s.Assigned)
                if (!s.Globals.Contains(n) && !s.Nonlocals.Contains(n)) s.Locals.Add(n);
        }
        foreach (var c in s.Children) ComputeLocals(c);
    }

    private void ResolveAll(Scope s)
    {
        var names = new List<string>();
        names.AddRange(s.Used);
        names.AddRange(s.Assigned);
        names.AddRange(s.Nonlocals);
        foreach (var n in names) Resolve(s, n);
        foreach (var c in s.Children) ResolveAll(c);
    }

    public SymKind Resolve(Scope s, string name)
    {
        if (s.Syms.TryGetValue(name, out var k)) return k;
        if (s.Globals.Contains(name)) k = SymKind.Global;
        else if (s.Kind == ScopeKind.Module) k = SymKind.Global;
        else if (s.Kind == ScopeKind.Eval) k = SymKind.Dynamic;
        else if (s.IsFunctionLike && s.Locals.Contains(name)) k = SymKind.Local;
        else if (s.Kind == ScopeKind.Class && s.Assigned.Contains(name) && !s.Nonlocals.Contains(name)) k = SymKind.ClassLocal;
        else
        {
            var found = FindBinding(s, name);
            if (found != null) k = SymKind.Free;
            else if (s.Nonlocals.Contains(name)) throw new ParseError($"no binding for nonlocal '{name}' found", 0, 0);
            else k = s.Kind == ScopeKind.Class ? SymKind.ClassGlobal : SymKind.GlobalImplicit;
        }
        s.Syms[name] = k;
        return k;
    }

    private static Scope? FindBinding(Scope s, string name)
    {
        for (var p = s.Parent; p != null; p = p.Parent)
        {
            if (p.Kind == ScopeKind.Class)
            {
                if (name == "__class__")
                {
                    if (!p.CellVars.Contains(name)) p.CellVars.Add(name);
                    MarkFree(s, p, name);
                    return p;
                }
                continue;
            }
            if (p.Kind == ScopeKind.Module || p.Kind == ScopeKind.Eval) return null;
            if (p.Globals.Contains(name)) return null;
            if (p.Locals.Contains(name))
            {
                if (!p.CellVars.Contains(name)) p.CellVars.Add(name);
                MarkFree(s, p, name);
                return p;
            }
        }
        return null;
    }

    private static void MarkFree(Scope s, Scope binder, string name)
    {
        for (var q = s; q != null && !ReferenceEquals(q, binder); q = q.Parent)
            if (!q.FreeVars.Contains(name)) q.FreeVars.Add(name);
    }
}
