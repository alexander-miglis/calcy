using System.Runtime.CompilerServices;
using Calcy.Python.Nodes;
using Calcy.Python.Parsing;
using Calcy.Python.Runtime;

namespace Calcy.Python.Compilation;

/// <summary>Translates the AST into a tree of executable nodes with resolved variable slots.</summary>
internal sealed class Compiler
{
    private readonly SymbolTable _st;
    private readonly string _file;
    private readonly Namespace _globals;
    private readonly string _module;
    private readonly bool _interactive;

    private Scope _scope;
    private Dictionary<string, int> _slots = new(StringComparer.Ordinal);
    private int _loopDepth;
    private string _qualPrefix = "";
    private int _tempCounter;
    private int _exprDepth;

    private Compiler(SymbolTable st, string file, Namespace globals, string module, bool interactive)
    {
        _st = st;
        _file = file;
        _globals = globals;
        _module = module;
        _interactive = interactive;
        _scope = st.Root;
    }

    public static Code CompileModule(ModuleAst m, string file, Namespace globals, string module, bool interactive, bool dynamicLocals = false)
    {
        var st = SymbolTable.Build(m.Body, dynamicLocals ? ScopeKind.Eval : ScopeKind.Module);
        var c = new Compiler(st, file, globals, module, interactive);
        var body = c.CompileBlock(m.Body, 1);
        return new Code
        {
            Name = "<module>",
            QualName = "<module>",
            FileName = file,
            Kind = dynamicLocals ? CodeKind.Eval : CodeKind.Module,
            FirstLine = 1,
            Body = body,
            NumLocals = c._slots.Count,
            Doc = Docstring(m.Body) ?? Py.None,
        };
    }

    public static Code CompileExpression(ExprAst e, string file, Namespace globals, string module, bool dynamicLocals)
    {
        var st = SymbolTable.BuildExpr(e, dynamicLocals ? ScopeKind.Eval : ScopeKind.Module);
        var c = new Compiler(st, file, globals, module, false);
        var expr = c.CompileExpr(e);
        var ret = new ReturnStmt(expr) { Line = e.Line };
        return new Code
        {
            Name = "<module>",
            QualName = "<module>",
            FileName = file,
            Kind = dynamicLocals ? CodeKind.Eval : CodeKind.Module,
            FirstLine = e.Line,
            Body = new Block(new Stmt[] { ret }),
            NumLocals = c._slots.Count,
        };
    }

    private static ParseError Error(string msg, AstNode at) => new(msg, at.Line, at.Col);

    private static PyObject? Docstring(List<StmtAst> body) =>
        body.Count > 0 && body[0] is ExprStmtAst { Value: ConstantAst { Value: PyStr s } } ? s : null;

    // ------------------------------------------------------------------ names

    private SymKind Sym(string name) => _st.Resolve(_scope, name);

    private int CellIndex(string name)
    {
        int i = _scope.CellVars.IndexOf(name);
        if (i >= 0) return i;
        int j = _scope.FreeVars.IndexOf(name);
        if (j >= 0) return _scope.CellVars.Count + j;
        throw new InvalidOperationException("cell not found: " + name);
    }

    private int Slot(string name)
    {
        if (!_slots.TryGetValue(name, out int s))
        {
            s = _slots.Count;
            _slots[name] = s;
        }
        return s;
    }

    private static PyObject? BuiltinOf(string name) => Builtins.Table.TryGetValue(name, out var v) ? v : null;

    private Expr LoadName(string name, int line)
    {
        var k = Sym(name);
        Expr e;
        switch (_scope.Kind)
        {
            case ScopeKind.Module:
                e = new GlobalExpr(_globals.Cell(name), BuiltinOf(name));
                break;
            case ScopeKind.Eval:
                e = k == SymKind.Global && _scope.Globals.Contains(name)
                    ? new GlobalExpr(_globals.Cell(name), BuiltinOf(name))
                    : new DynNameExpr(name, _globals.Cell(name), BuiltinOf(name));
                break;
            case ScopeKind.Class:
                e = k switch
                {
                    SymKind.Global => new GlobalExpr(_globals.Cell(name), BuiltinOf(name)),
                    SymKind.Free => new ClassFreeExpr(name, CellIndex(name)),
                    _ => new ClassNameExpr(name, _globals.Cell(name), BuiltinOf(name)),
                };
                break;
            default:
                switch (k)
                {
                    case SymKind.Local:
                        e = _scope.CellVars.Contains(name) ? new CellExpr(CellIndex(name), name, false) : new LocalExpr(Slot(name), name);
                        break;
                    case SymKind.Free:
                        e = new CellExpr(CellIndex(name), name, true);
                        break;
                    default:
                        e = new GlobalExpr(_globals.Cell(name), BuiltinOf(name));
                        break;
                }
                break;
        }
        e.Line = line;
        return e;
    }

    private Target StoreName(string name)
    {
        var k = Sym(name);
        switch (_scope.Kind)
        {
            case ScopeKind.Module:
                return new GlobalTarget(_globals.Cell(name));
            case ScopeKind.Eval:
                return k == SymKind.Global && _scope.Globals.Contains(name)
                    ? new GlobalTarget(_globals.Cell(name))
                    : new DynTarget(name, _globals.Cell(name));
            case ScopeKind.Class:
                return k switch
                {
                    SymKind.Global => new GlobalTarget(_globals.Cell(name)),
                    SymKind.Free => new CellTarget(CellIndex(name), name, true),
                    _ => new ClassNsTarget(name),
                };
            default:
                switch (k)
                {
                    case SymKind.Local:
                        return _scope.CellVars.Contains(name) ? new CellTarget(CellIndex(name), name, false) : new LocalTarget(Slot(name), name);
                    case SymKind.Free:
                        return new CellTarget(CellIndex(name), name, true);
                    default:
                        return new GlobalTarget(_globals.Cell(name));
                }
        }
    }

    private int[] ClosureSource(Scope child)
    {
        var src = new int[child.FreeVars.Count];
        for (int i = 0; i < src.Length; i++) src[i] = CellIndex(child.FreeVars[i]);
        return src;
    }

    // ------------------------------------------------------------------ statements

    private Block CompileBlock(List<StmtAst> body, int line)
    {
        var list = new List<Stmt>(body.Count);
        foreach (var s in body) list.Add(CompileStmt(s));
        var b = new Block(list.ToArray());
        if (body.Count == 0) b.Line = line;
        return b;
    }

    private static Block Wrap(Stmt s) => s as Block ?? new Block(new[] { s });

    private Stmt CompileStmt(StmtAst s)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw CompileRecursion();
        var r = CompileStmtInner(s);
        if (r.Line == 0) r.Line = s.Line;
        return r;
    }

    private void RequireGenerator(AstNode at)
    {
        if (!_scope.IsFunctionLike || !_scope.HasYield) throw Error("'yield' outside function", at);
    }

    private Stmt CompileStmtInner(StmtAst s)
    {
        switch (s)
        {
            case ExprStmtAst es:
                if (es.Value is YieldAst y)
                {
                    RequireGenerator(y);
                    return new YieldStmt(y.Value == null ? null : CompileExpr(y.Value), null) { Line = s.Line };
                }
                if (es.Value is YieldFromAst yf)
                {
                    RequireGenerator(yf);
                    return new YieldFromStmt(CompileExpr(yf.Value), null) { Line = s.Line };
                }
                if (_interactive && (_scope.Kind == ScopeKind.Module)) return new PrintExprStmt(CompileExpr(es.Value)) { Line = s.Line };
                return new ExprStmt(CompileExpr(es.Value)) { Line = s.Line };

            case AssignAst a:
            {
                if (a.Value is YieldAst or YieldFromAst)
                {
                    RequireGenerator(a.Value);
                    var targets = a.Targets.Select(CompileTarget).ToArray();
                    var t = targets.Length == 1 ? targets[0] : new MultiTarget(targets);
                    if (a.Value is YieldAst ya) return new YieldStmt(ya.Value == null ? null : CompileExpr(ya.Value), t) { Line = s.Line };
                    return new YieldFromStmt(CompileExpr(((YieldFromAst)a.Value).Value), t) { Line = s.Line };
                }
                var v = CompileExpr(a.Value);
                if (a.Targets.Count == 1)
                {
                    var t = CompileTarget(a.Targets[0]);
                    return t switch
                    {
                        LocalTarget lt => new LocalAssignStmt(lt.Slot, v),
                        GlobalTarget gt => new GlobalAssignStmt(gt.Cell, v),
                        _ => new AssignStmt(t, v),
                    };
                }
                return new MultiAssignStmt(a.Targets.Select(CompileTarget).ToArray(), v);
            }

            case AugAssignAst aa:
            {
                if (aa.Value is YieldAst or YieldFromAst)
                {
                    RequireGenerator(aa.Value);
                    string tmp = ".yield" + (_tempCounter++);
                    int slot = Slot(tmp);
                    var after = CompileAug(aa.Target, aa.Op, new LocalExpr(slot, tmp));
                    after.Line = s.Line;
                    if (aa.Value is YieldAst ya)
                        return new YieldStmt(ya.Value == null ? null : CompileExpr(ya.Value), new LocalTarget(slot, tmp), after) { Line = s.Line };
                    return new YieldFromStmt(CompileExpr(((YieldFromAst)aa.Value).Value), new LocalTarget(slot, tmp), after) { Line = s.Line };
                }
                return CompileAug(aa.Target, aa.Op, CompileExpr(aa.Value));
            }

            case AnnAssignAst an:
                if (an.Value == null) return new PassStmt();
                return new AssignStmt(CompileTarget(an.Target), CompileExpr(an.Value));

            case IfAst i:
                return new IfStmt(CompileExpr(i.Test), CompileBlock(i.Body, i.Line), i.OrElse.Count > 0 ? CompileBlock(i.OrElse, i.Line) : null);

            case WhileAst w:
            {
                var test = CompileExpr(w.Test);
                _loopDepth++;
                var body = CompileBlock(w.Body, w.Line);
                _loopDepth--;
                var orelse = w.OrElse.Count > 0 ? CompileBlock(w.OrElse, w.Line) : null;
                return new WhileStmt(test, body, orelse) { Line = w.Line };
            }

            case ForAst fo:
            {
                var iter = CompileExpr(fo.Iter);
                var target = CompileTarget(fo.Target);
                _loopDepth++;
                var body = CompileBlock(fo.Body, fo.Line);
                _loopDepth--;
                var orelse = fo.OrElse.Count > 0 ? CompileBlock(fo.OrElse, fo.Line) : null;
                return new ForStmt(target, iter, body, orelse) { Line = fo.Line };
            }

            case BreakAst:
                if (_loopDepth == 0) throw Error("'break' outside loop", s);
                return new BreakStmt();

            case ContinueAst:
                if (_loopDepth == 0) throw Error("'continue' not properly in loop", s);
                return new ContinueStmt();

            case PassAst:
                return new PassStmt();

            case ReturnAst r:
                if (!_scope.IsFunctionLike) throw Error("'return' outside function", r);
                if (r.Value is YieldAst or YieldFromAst) throw Error("'return (yield ...)' is not supported in this interpreter; assign the yield result first", r);
                return new ReturnStmt(r.Value == null ? null : CompileExpr(r.Value));

            case FunctionDefAst fd:
            {
                var decorators = fd.Decorators.Select(CompileExpr).ToArray();
                var make = MakeFunction(fd, fd.Args, fd.Body, null, fd.Name, fd.Line);
                return new FunctionDefStmt(make, decorators, StoreName(fd.Name));
            }

            case ClassDefAst cd:
                return CompileClass(cd);

            case GlobalAst:
            case NonlocalAst:
                return new PassStmt();

            case DeleteAst d:
                return new DeleteStmt(d.Targets.Select(CompileTarget).ToArray());

            case TryAst t:
            {
                Stmt inner;
                if (t.Handlers.Count > 0)
                {
                    var handlers = t.Handlers.Select(h => new Handler
                    {
                        Type = h.Type == null ? null : CompileExpr(h.Type),
                        Name = h.Name == null ? null : StoreName(h.Name),
                        Body = CompileBlock(h.Body, h.Line),
                    }).ToArray();
                    for (int i = 0; i < t.Handlers.Count - 1; i++)
                        if (t.Handlers[i].Type == null) throw Error("default 'except:' must be last", t.Handlers[i]);
                    inner = new TryExceptStmt(CompileBlock(t.Body, t.Line), handlers, t.OrElse.Count > 0 ? CompileBlock(t.OrElse, t.Line) : null) { Line = t.Line };
                }
                else inner = CompileBlock(t.Body, t.Line);
                if (t.FinalBody.Count > 0)
                {
                    var fin = CompileBlock(t.FinalBody, t.Line);
                    inner = new TryFinallyStmt(Wrap(inner), fin) { Line = t.Line };
                }
                return inner;
            }

            case RaiseAst r:
                return new RaiseStmt(r.Exc == null ? null : CompileExpr(r.Exc), r.Cause == null ? null : CompileExpr(r.Cause));

            case AssertAst a:
                return new AssertStmt(CompileExpr(a.Test), a.Msg == null ? null : CompileExpr(a.Msg));

            case ImportAst im:
                return new ImportStmt(im.Names.Select(n =>
                {
                    if (n.Name.Contains('.') && n.AsName == null)
                        return (n.Name.Split('.')[0], StoreName(n.Name.Split('.')[0]));
                    return (n.Name, StoreName(n.AsName ?? n.Name));
                }).ToArray());

            case ImportFromAst imf:
            {
                string mod = new string('.', imf.Level) + imf.Module;
                bool star = imf.Names.Count == 1 && imf.Names[0].Name == "*";
                var names = star
                    ? Array.Empty<(string, Target)>()
                    : imf.Names.Select(n => (n.Name, StoreName(n.AsName ?? n.Name))).ToArray();
                return new ImportFromStmt(mod, names, star);
            }

            case WithAst w:
            {
                var body = CompileBlock(w.Body, w.Line);
                Stmt result = body;
                for (int i = w.Items.Count - 1; i >= 0; i--)
                {
                    var (ctx, vars) = w.Items[i];
                    var ws = new WithStmt(CompileExpr(ctx), vars == null ? null : CompileTarget(vars), Wrap(result)) { Line = w.Line };
                    result = ws;
                }
                return result;
            }

            case MatchAst m:
                return CompileMatch(m);
        }
        throw Error("unsupported statement", s);
    }

    private Stmt CompileAug(ExprAst target, BinOp op, Expr value)
    {
        switch (target)
        {
            case NameAst n:
            {
                var store = StoreName(n.Id);
                if (store is LocalTarget lt) return new AugLocalStmt(lt.Slot, n.Id, op, value);
                if (store is GlobalTarget gt && _scope.Kind != ScopeKind.Class) return new AugGlobalStmt(gt.Cell, BuiltinOf(n.Id), op, value);
                return new AugNameStmt(LoadName(n.Id, n.Line), store, op, value);
            }
            case AttributeAst a:
                return new AugAttrStmt(CompileExpr(a.Value), a.Attr, op, value);
            case SubscriptAst sub:
                return new AugSubscrStmt(CompileExpr(sub.Value), CompileExpr(sub.Index), op, value);
        }
        throw Error("illegal expression for augmented assignment", target);
    }

    private Target CompileTarget(ExprAst e)
    {
        switch (e)
        {
            case NameAst n:
                return StoreName(n.Id);
            case AttributeAst a:
                return new AttrTarget(CompileExpr(a.Value), a.Attr);
            case SubscriptAst s:
                return new SubscrTarget(CompileExpr(s.Value), CompileExpr(s.Index));
            case TupleAst t:
                return Unpack(t.Elts);
            case ListAst l:
                return Unpack(l.Elts);
            case StarredAst st:
                throw Error("starred assignment target must be in a list or tuple", st);
        }
        throw Error("cannot assign to expression", e);
    }

    private Target Unpack(List<ExprAst> elts)
    {
        int star = -1;
        var items = new Target[elts.Count];
        for (int i = 0; i < elts.Count; i++)
        {
            if (elts[i] is StarredAst s)
            {
                star = i;
                items[i] = CompileTarget(s.Value);
            }
            else items[i] = CompileTarget(elts[i]);
        }
        return new UnpackTarget(items, star);
    }

    // ------------------------------------------------------------------ functions and classes

    private MakeFunctionExpr MakeFunction(AstNode node, ArgumentsAst args, List<StmtAst>? body, ExprAst? lambdaBody, string name, int line)
    {
        var fs = _st.Map[node];
        var defaults = args.Defaults.Select(CompileExpr).ToArray();
        var kwdefs = new List<(string, Expr)>();
        for (int i = 0; i < args.KwOnly.Count; i++)
            if (args.KwDefaults[i] != null) kwdefs.Add((args.KwOnly[i].Name, CompileExpr(args.KwDefaults[i]!)));
        string qual = _qualPrefix + name;
        var closure = ClosureSource(fs);

        var savedScope = _scope;
        var savedSlots = _slots;
        var savedLoop = _loopDepth;
        var savedPrefix = _qualPrefix;
        _scope = fs;
        _slots = new Dictionary<string, int>(StringComparer.Ordinal);
        _loopDepth = 0;
        _qualPrefix = qual + ".<locals>.";
        try
        {
            var code = new Code
            {
                Name = name,
                QualName = qual,
                FileName = _file,
                Kind = lambdaBody != null ? CodeKind.Lambda : CodeKind.Function,
                FirstLine = line,
                IsGenerator = fs.HasYield,
            };
            var paramNames = new List<string>();
            foreach (var a in args.PosOnly) paramNames.Add(a.Name);
            foreach (var a in args.Args) paramNames.Add(a.Name);
            code.PosOnlyCount = args.PosOnly.Count;
            code.ArgCount = args.PosOnly.Count + args.Args.Count;
            foreach (var a in args.KwOnly) paramNames.Add(a.Name);
            code.KwOnlyCount = args.KwOnly.Count;
            foreach (var p in paramNames) Slot(p);
            if (args.VarArg != null)
            {
                code.HasVarArgs = true;
                code.VarArgsSlot = Slot(args.VarArg.Name);
                paramNames.Add(args.VarArg.Name);
            }
            if (args.KwArg != null)
            {
                code.HasKwArgs = true;
                code.KwArgsSlot = Slot(args.KwArg.Name);
                paramNames.Add(args.KwArg.Name);
            }
            code.ParamNames = paramNames.ToArray();
            foreach (var n in fs.AssignOrder)
                if (fs.Locals.Contains(n) && !fs.CellVars.Contains(n)) Slot(n);
            code.CellNames = fs.CellVars.ToArray();
            code.FreeNames = fs.FreeVars.ToArray();
            var cellParams = new List<(int, int)>();
            foreach (var p in paramNames)
            {
                int ci = fs.CellVars.IndexOf(p);
                if (ci >= 0) cellParams.Add((_slots[p], ci));
            }
            code.CellParams = cellParams.ToArray();
            if (lambdaBody != null)
            {
                code.Body = new Block(new Stmt[] { new ReturnStmt(CompileExpr(lambdaBody)) { Line = line } });
            }
            else
            {
                code.Doc = Docstring(body!) ?? Py.None;
                code.Body = CompileBlock(body!, line);
            }
            code.NumLocals = _slots.Count;
            code.LocalNames = _slots.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();
            code.FastArgs = !code.HasVarArgs && !code.HasKwArgs && code.KwOnlyCount == 0;
            var mf = new MakeFunctionExpr(code, defaults, kwdefs.ToArray(), closure, _module) { Line = line };
            return mf;
        }
        finally
        {
            _scope = savedScope;
            _slots = savedSlots;
            _loopDepth = savedLoop;
            _qualPrefix = savedPrefix;
        }
    }

    private Stmt CompileClass(ClassDefAst cd)
    {
        var cs = _st.Map[cd];
        var decorators = cd.Decorators.Select(CompileExpr).ToArray();
        foreach (var b in cd.Bases)
            if (b is StarredAst) throw Error("star unpacking of class bases is not supported", b);
        var bases = cd.Bases.Select(CompileExpr).ToArray();
        var keywords = new List<(string, Expr)>();
        foreach (var k in cd.Keywords)
        {
            if (k.Name == null) throw Error("**kwargs in class definitions is not supported", k);
            keywords.Add((k.Name, CompileExpr(k.Value)));
        }
        string qual = _qualPrefix + cd.Name;
        var closure = ClosureSource(cs);
        var target = StoreName(cd.Name);

        var savedScope = _scope;
        var savedSlots = _slots;
        var savedLoop = _loopDepth;
        var savedPrefix = _qualPrefix;
        _scope = cs;
        _slots = new Dictionary<string, int>(StringComparer.Ordinal);
        _loopDepth = 0;
        _qualPrefix = qual + ".";
        try
        {
            var code = new Code
            {
                Name = cd.Name,
                QualName = qual,
                FileName = _file,
                Kind = CodeKind.Class,
                FirstLine = cd.Line,
                CellNames = cs.CellVars.ToArray(),
                FreeNames = cs.FreeVars.ToArray(),
            };
            code.ClassCellIndex = cs.CellVars.IndexOf("__class__");
            var stmts = new List<Stmt>();
            var body = cd.Body;
            var doc = Docstring(body);
            int start = 0;
            if (doc != null)
            {
                stmts.Add(new AssignStmt(new ClassNsTarget("__doc__"), new ConstExpr(doc)) { Line = body[0].Line });
                start = 1;
            }
            for (int i = start; i < body.Count; i++) stmts.Add(CompileStmt(body[i]));
            code.Body = new Block(stmts.ToArray());
            if (stmts.Count == 0) code.Body.Line = cd.Line;
            code.NumLocals = _slots.Count;
            return new ClassDefStmt(cd.Name, qual, _module, code, bases, keywords.ToArray(), decorators, closure, target) { Line = cd.Line };
        }
        finally
        {
            _scope = savedScope;
            _slots = savedSlots;
            _loopDepth = savedLoop;
            _qualPrefix = savedPrefix;
        }
    }

    private Expr CompileComprehension(ComprehensionAst comp)
    {
        var cs = _st.Map[comp];
        var outer = CompileExpr(comp.Generators[0].Iter);
        var closure = ClosureSource(cs);
        string name = cs.Name;
        string qual = _qualPrefix + name;

        var savedScope = _scope;
        var savedSlots = _slots;
        var savedLoop = _loopDepth;
        var savedPrefix = _qualPrefix;
        _scope = cs;
        _slots = new Dictionary<string, int>(StringComparer.Ordinal);
        _loopDepth = 0;
        _qualPrefix = qual + ".<locals>.";
        try
        {
            int iterSlot = Slot(".0");
            int resultSlot = comp.Kind == ComprehensionKind.Generator ? -1 : Slot(".result");
            int line = comp.Line;
            Stmt body = comp.Kind switch
            {
                ComprehensionKind.List => new ListAppendStmt(resultSlot, CompileExpr(comp.Elt)),
                ComprehensionKind.Set => new SetAddStmt(resultSlot, CompileExpr(comp.Elt)),
                ComprehensionKind.Dict => new DictSetStmt(resultSlot, CompileExpr(comp.Elt), CompileExpr(comp.Value!)),
                _ => new YieldStmt(CompileExpr(comp.Elt), null),
            };
            body.Line = line;
            for (int gi = comp.Generators.Count - 1; gi >= 0; gi--)
            {
                var g = comp.Generators[gi];
                for (int k = g.Ifs.Count - 1; k >= 0; k--)
                    body = new IfStmt(CompileExpr(g.Ifs[k]), new Block(new[] { body }), null) { Line = line };
                var iterExpr = gi == 0 ? new LocalExpr(iterSlot, ".0") : CompileExpr(g.Iter);
                body = new ForStmt(CompileTarget(g.Target), iterExpr, new Block(new[] { body }), null) { Line = line };
            }
            var code = new Code
            {
                Name = name,
                QualName = qual,
                FileName = _file,
                Kind = CodeKind.Comprehension,
                FirstLine = line,
                ArgCount = 1,
                ParamNames = new[] { ".0" },
                IsGenerator = comp.Kind == ComprehensionKind.Generator,
                CellNames = cs.CellVars.ToArray(),
                FreeNames = cs.FreeVars.ToArray(),
                Body = new Block(new[] { body }),
            };
            code.NumLocals = _slots.Count;
            int kind = comp.Kind switch
            {
                ComprehensionKind.List => 0,
                ComprehensionKind.Set => 1,
                ComprehensionKind.Dict => 2,
                _ => 3,
            };
            return new ComprehensionExpr(code, outer, closure, kind, resultSlot) { Line = line };
        }
        finally
        {
            _scope = savedScope;
            _slots = savedSlots;
            _loopDepth = savedLoop;
            _qualPrefix = savedPrefix;
        }
    }

    // ------------------------------------------------------------------ expressions

    private Expr CompileExpr(ExprAst e)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw CompileRecursion();
        _exprDepth++;
        try
        {
            var r = CompileExprInner(e);
            if (r.Line == 0) r.Line = e.Line;
            // Long operator chains nest deeply; every few levels the evaluator checks that the native stack has room.
            if ((_exprDepth & 31) == 0) r = new StackGuardExpr(r);
            return r;
        }
        finally
        {
            _exprDepth--;
        }
    }

    internal static PythonException CompileRecursion() => PyErr.RecursionError("maximum recursion depth exceeded during compilation");

    private Elt[] Elts(List<ExprAst> elts) =>
        elts.Select(x => x is StarredAst s ? new Elt(CompileExpr(s.Value), true) : new Elt(CompileExpr(x), false)).ToArray();

    private Expr CompileExprInner(ExprAst e)
    {
        switch (e)
        {
            case ConstantAst c:
                return new ConstExpr(c.Value);
            case NameAst n:
                return LoadName(n.Id, n.Line);
            case BinOpAst b:
            {
                var l = CompileExpr(b.Left);
                var r = CompileExpr(b.Right);
                return b.Op switch
                {
                    BinOp.Add => new AddExpr(l, r),
                    BinOp.Sub => new SubExpr(l, r),
                    BinOp.Mul => new MulExpr(l, r),
                    _ => new BinExpr(b.Op, l, r),
                };
            }
            case UnaryOpAst u:
            {
                if (u.Op == UnaryKind.Not) return new NotExpr(CompileExpr(u.Operand));
                if (u.Operand is ConstantAst { Value: PyInt or PyFloat or PyComplex } c && c.Value is not PyBool)
                {
                    return u.Op switch
                    {
                        UnaryKind.Neg => new ConstExpr(Arith.Neg(c.Value)),
                        UnaryKind.Pos => new ConstExpr(Arith.Pos(c.Value)),
                        _ => new UnaryExpr(2, CompileExpr(u.Operand)),
                    };
                }
                return new UnaryExpr(u.Op == UnaryKind.Neg ? 0 : u.Op == UnaryKind.Pos ? 1 : 2, CompileExpr(u.Operand));
            }
            case BoolOpAst bo:
            {
                var vals = bo.Values.Select(CompileExpr).ToArray();
                return bo.IsAnd ? new AndExpr(vals) : new OrExpr(vals);
            }
            case CompareAst c:
            {
                var left = CompileExpr(c.Left);
                var ops = c.Ops.Select(o => (CmpOp)(byte)o).ToArray();
                var rights = c.Comparators.Select(CompileExpr).ToArray();
                if (ops.Length == 1) return new Compare1Expr(ops[0], left, rights[0]);
                return new CompareExpr(left, ops, rights);
            }
            case CallAst call:
                return CompileCall(call);
            case AttributeAst a:
                return new AttrExpr(CompileExpr(a.Value), a.Attr);
            case SubscriptAst s:
                return new SubscrExpr(CompileExpr(s.Value), CompileExpr(s.Index));
            case SliceAst sl:
                return new SliceExpr(sl.Lower == null ? null : CompileExpr(sl.Lower), sl.Upper == null ? null : CompileExpr(sl.Upper),
                    sl.Step == null ? null : CompileExpr(sl.Step));
            case ListAst l:
                return new ListExpr(Elts(l.Elts));
            case TupleAst t:
                if (t.Elts.All(x => x is ConstantAst) && t.Elts.Count > 0)
                    return new ConstExpr(new PyTuple(t.Elts.Select(x => ((ConstantAst)x).Value).ToArray()));
                return new TupleExpr(Elts(t.Elts));
            case SetAst st:
                if (st.Elts.Count >= 3 && st.Elts.All(x => x is ConstantAst))
                {
                    // CPython builds constant set displays from a frozenset constant; this reproduces its iteration order
                    var table = new SetTable();
                    foreach (var x in st.Elts) table.Add(((ConstantAst)x).Value);
                    return new ConstSetExpr(table);
                }
                return new SetExpr(Elts(st.Elts));
            case DictAst d:
                return new DictExpr(d.Keys.Select(k => k == null ? null : CompileExpr(k)).ToArray(), d.Values.Select(CompileExpr).ToArray());
            case ComprehensionAst comp:
                return CompileComprehension(comp);
            case LambdaAst lam:
                return MakeFunction(lam, lam.Args, null, lam.Body, "<lambda>", lam.Line);
            case IfExpAst ie:
                return new IfExpr(CompileExpr(ie.Test), CompileExpr(ie.Body), CompileExpr(ie.OrElse));
            case StarredAst st2:
                throw Error("can't use starred expression here", st2);
            case YieldAst:
            case YieldFromAst:
                throw Error("'yield' is only supported as a statement or as the value of an assignment in this interpreter", e);
            case JoinedStrAst js:
                return new JoinedStrExpr(js.Parts.Select(CompileExpr).ToArray());
            case FormattedValueAst fv:
                return new FormattedExpr(CompileExpr(fv.Value), fv.Conversion, fv.Spec == null ? null : CompileExpr(fv.Spec));
            case NamedExprAst ne:
                return new NamedExpr(StoreName(ne.Target.Id), CompileExpr(ne.Value));
        }
        throw Error("unsupported expression", e);
    }

    private Expr CompileCall(CallAst call)
    {
        bool simple = call.Keywords.Count == 0 && call.Args.All(a => a is not StarredAst);
        if (simple)
        {
            var args = call.Args.Select(CompileExpr).ToArray();
            if (call.Func is AttributeAst a) return new CallMethodExpr(CompileExpr(a.Value), a.Attr, args);
            return new CallExpr(CompileExpr(call.Func), args);
        }
        var fn = CompileExpr(call.Func);
        var elts = Elts(call.Args);
        var names = call.Keywords.Select(k => k.Name).ToArray();
        var values = call.Keywords.Select(k => CompileExpr(k.Value)).ToArray();
        return new CallGeneralExpr(fn, elts, names, values);
    }

    // ------------------------------------------------------------------ match

    private Stmt CompileMatch(MatchAst m)
    {
        var subject = CompileExpr(m.Subject);
        var cases = new List<MatchCase>();
        foreach (var c in m.Cases)
        {
            cases.Add(new MatchCase
            {
                Pattern = CompilePattern(c.Pattern),
                Guard = c.Guard == null ? null : CompileExpr(c.Guard),
                Body = CompileBlock(c.Body, c.Line),
            });
        }
        return new MatchStmt(subject, cases.ToArray()) { Line = m.Line };
    }

    private Pat CompilePattern(PatternAst p)
    {
        switch (p)
        {
            case MatchValuePattern v:
                return new ValuePat(CompileExpr(v.Value));
            case MatchSingletonPattern s:
                return new SingletonPat(s.Value);
            case MatchAsPattern a:
                return new CapturePat(a.Pattern == null ? null : CompilePattern(a.Pattern), a.Name == null ? null : StoreName(a.Name));
            case MatchOrPattern o:
                return new OrPat(o.Patterns.Select(CompilePattern).ToArray());
            case MatchSequencePattern seq:
            {
                int star = -1;
                var items = new Pat[seq.Patterns.Count];
                for (int i = 0; i < items.Length; i++)
                {
                    if (seq.Patterns[i] is MatchStarPattern sp)
                    {
                        if (star >= 0) throw Error("multiple starred names in sequence pattern", sp);
                        star = i;
                        items[i] = new CapturePat(null, sp.Name == null ? null : StoreName(sp.Name));
                    }
                    else items[i] = CompilePattern(seq.Patterns[i]);
                }
                return new SeqPat(items, star);
            }
            case MatchMappingPattern mp:
                return new MappingPat(mp.Keys.Select(CompileExpr).ToArray(), mp.Patterns.Select(CompilePattern).ToArray(),
                    mp.Rest == null ? null : StoreName(mp.Rest));
            case MatchClassPattern cp:
                return new ClassPat(CompileExpr(cp.Cls), cp.Patterns.Select(CompilePattern).ToArray(), cp.KwdAttrs.ToArray(),
                    cp.KwdPatterns.Select(CompilePattern).ToArray());
            case MatchStarPattern st:
                throw Error("can't use starred name here", st);
        }
        throw Error("unsupported pattern", p);
    }
}

/// <summary>Assigns the same value to several targets (used for chained yield assignments).</summary>
internal sealed class MultiTarget : Target
{
    private readonly Target[] _targets;
    public MultiTarget(Target[] targets) { _targets = targets; }

    public override void Assign(Frame f, PyObject v)
    {
        foreach (var t in _targets) t.Assign(f, v);
    }

    public override void Delete(Frame f)
    {
        foreach (var t in _targets) t.Delete(f);
    }
}
