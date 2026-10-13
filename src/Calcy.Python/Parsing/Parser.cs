using System.Runtime.CompilerServices;
using Calcy.Python.Runtime;

namespace Calcy.Python.Parsing;

/// <summary>Recursive descent parser for the supported Python 3 subset.</summary>
internal sealed class Parser
{
    private readonly List<Token> _t;
    private int _p;
    private int _depth;

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
        "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
        "pass", "raise", "return", "try", "while", "with", "yield",
    };

    private static readonly Dictionary<string, BinOp> AugOps = new(StringComparer.Ordinal)
    {
        ["+="] = BinOp.Add, ["-="] = BinOp.Sub, ["*="] = BinOp.Mul, ["@="] = BinOp.MatMul, ["/="] = BinOp.TrueDiv,
        ["//="] = BinOp.FloorDiv, ["%="] = BinOp.Mod, ["**="] = BinOp.Pow, ["<<="] = BinOp.LShift, [">>="] = BinOp.RShift,
        ["&="] = BinOp.And, ["|="] = BinOp.Or, ["^="] = BinOp.Xor,
    };

    private Parser(List<Token> tokens)
    {
        _t = tokens;
    }

    public static ModuleAst ParseModule(string source)
    {
        var p = new Parser(Lexer.Tokenize(source));
        return p.ParseFile();
    }

    public static ExprAst ParseEval(string source)
    {
        var p = new Parser(Lexer.Tokenize(source.Trim(' ', '\t', '\n', '\r')));
        while (p.Cur.Kind == TokenKind.Newline) p._p++;
        if (p.Cur.Kind == TokenKind.Indent) throw p.Err("unexpected indent");
        var e = p.ParseTestListStarExpr();
        while (p.Cur.Kind == TokenKind.Newline) p._p++;
        if (p.Cur.Kind != TokenKind.EndMarker) throw p.Err("invalid syntax");
        return e;
    }

    // ------------------------------------------------------------------ token helpers

    private Token Cur => _t[_p];

    private Token PeekTok(int k = 1) => _t[Math.Min(_p + k, _t.Count - 1)];

    private bool IsOp(string op) => Cur.Kind == TokenKind.Op && Cur.Text == op;

    private bool IsKw(string kw) => Cur.Kind == TokenKind.Name && Cur.Text == kw;

    private Token Next()
    {
        var t = _t[_p];
        if (_p < _t.Count - 1) _p++;
        return t;
    }

    private bool AcceptOp(string op)
    {
        if (!IsOp(op)) return false;
        Next();
        return true;
    }

    private bool AcceptKw(string kw)
    {
        if (!IsKw(kw)) return false;
        Next();
        return true;
    }

    private Token ExpectOp(string op, string? msg = null)
    {
        if (!IsOp(op)) throw Err(msg ?? (op == ":" ? "expected ':'" : "invalid syntax"));
        return Next();
    }

    private void ExpectKw(string kw, string? msg = null)
    {
        if (!IsKw(kw)) throw Err(msg ?? "invalid syntax");
        Next();
    }

    private string ExpectName()
    {
        if (Cur.Kind != TokenKind.Name || Keywords.Contains(Cur.Text)) throw Err("invalid syntax");
        return Next().Text;
    }

    private ParseError Err(string msg, Token? at = null, bool indentation = false)
    {
        at ??= Cur;
        if (at.Kind == TokenKind.EndMarker && at.Value is ParseError pending) return pending;
        return new ParseError(msg, at.Line, at.Col, at.Kind == TokenKind.EndMarker, indentation);
    }

    private T At<T>(T node, Token tok) where T : AstNode
    {
        node.Line = tok.Line;
        node.Col = tok.Col;
        return node;
    }

    private void Enter()
    {
        if (++_depth > 300 || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw Err("too many nested parentheses");
    }

    private void Leave() => _depth--;

    // ------------------------------------------------------------------ statements

    private ModuleAst ParseFile()
    {
        var m = new ModuleAst();
        while (Cur.Kind != TokenKind.EndMarker)
        {
            if (Cur.Kind == TokenKind.Newline)
            {
                Next();
                continue;
            }
            if (Cur.Kind == TokenKind.Indent) throw Err("unexpected indent", indentation: true);
            if (Cur.Kind == TokenKind.Dedent)
            {
                Next();
                continue;
            }
            m.Body.AddRange(ParseStatement());
        }
        return m;
    }

    private List<StmtAst> ParseStatement()
    {
        var tok = Cur;
        if (tok.Kind == TokenKind.Name)
        {
            switch (tok.Text)
            {
                case "if": return new List<StmtAst> { ParseIf() };
                case "while": return new List<StmtAst> { ParseWhile() };
                case "for": return new List<StmtAst> { ParseFor() };
                case "try": return new List<StmtAst> { ParseTry() };
                case "with": return new List<StmtAst> { ParseWith() };
                case "def": return new List<StmtAst> { ParseFuncDef(new List<ExprAst>()) };
                case "class": return new List<StmtAst> { ParseClassDef(new List<ExprAst>()) };
                case "async": throw Err("async functions are not supported in this interpreter");
                case "match":
                    if (LooksLikeMatch()) return new List<StmtAst> { ParseMatch() };
                    break;
                case "elif":
                case "else":
                case "except":
                case "finally":
                    throw Err("invalid syntax");
            }
        }
        else if (tok.Kind == TokenKind.Op && tok.Text == "@") return new List<StmtAst> { ParseDecorated() };
        return ParseSimpleStatements();
    }

    private List<StmtAst> ParseBlock(string construct, int line)
    {
        ExpectOp(":", "expected ':'");
        if (Cur.Kind == TokenKind.Newline)
        {
            Next();
            if (Cur.Kind != TokenKind.Indent)
                throw new ParseError($"expected an indented block after {construct} on line {line}", Cur.Line, Cur.Col,
                    Cur.Kind == TokenKind.EndMarker, indentation: true);
            Next();
            var body = new List<StmtAst>();
            while (Cur.Kind != TokenKind.Dedent && Cur.Kind != TokenKind.EndMarker)
            {
                if (Cur.Kind == TokenKind.Newline)
                {
                    Next();
                    continue;
                }
                if (Cur.Kind == TokenKind.Indent) throw Err("unexpected indent", indentation: true);
                body.AddRange(ParseStatement());
            }
            if (Cur.Kind == TokenKind.Dedent) Next();
            return body;
        }
        if (Cur.Kind == TokenKind.EndMarker)
            throw new ParseError($"expected an indented block after {construct} on line {line}", Cur.Line, Cur.Col, true, true);
        return ParseSimpleStatements();
    }

    private IfAst ParseIf()
    {
        var tok = Next();
        var node = At(new IfAst(), tok);
        node.Test = ParseNamedExprTest();
        node.Body = ParseBlock($"'{tok.Text}' statement", tok.Line);
        if (IsKw("elif")) node.OrElse = new List<StmtAst> { ParseIf() };
        else if (IsKw("else"))
        {
            var e = Next();
            node.OrElse = ParseBlock("'else' statement", e.Line);
        }
        return node;
    }

    private WhileAst ParseWhile()
    {
        var tok = Next();
        var node = At(new WhileAst(), tok);
        node.Test = ParseNamedExprTest();
        node.Body = ParseBlock("'while' statement", tok.Line);
        if (IsKw("else"))
        {
            var e = Next();
            node.OrElse = ParseBlock("'else' statement", e.Line);
        }
        return node;
    }

    private ForAst ParseFor()
    {
        var tok = Next();
        var node = At(new ForAst(), tok);
        node.Target = ParseTargetList();
        CheckTarget(node.Target, false);
        ExpectKw("in", "invalid syntax");
        node.Iter = ParseTestListStarExpr();
        node.Body = ParseBlock("'for' statement", tok.Line);
        if (IsKw("else"))
        {
            var e = Next();
            node.OrElse = ParseBlock("'else' statement", e.Line);
        }
        return node;
    }

    private TryAst ParseTry()
    {
        var tok = Next();
        var node = At(new TryAst(), tok);
        node.Body = ParseBlock("'try' statement", tok.Line);
        while (IsKw("except"))
        {
            var et = Next();
            if (IsOp("*")) throw Err("except* is not supported in this interpreter");
            var h = At(new HandlerAst(), et);
            if (!IsOp(":"))
            {
                h.Type = ParseTest();
                if (IsOp(",")) throw Err("multiple exception types must be parenthesized");
                if (AcceptKw("as")) h.Name = ExpectName();
            }
            h.Body = ParseBlock("'except' statement", et.Line);
            node.Handlers.Add(h);
        }
        if (IsKw("else"))
        {
            if (node.Handlers.Count == 0) throw Err("expected 'except' or 'finally' block");
            var e = Next();
            node.OrElse = ParseBlock("'else' statement", e.Line);
        }
        if (IsKw("finally"))
        {
            var f = Next();
            node.FinalBody = ParseBlock("'finally' statement", f.Line);
        }
        if (node.Handlers.Count == 0 && node.FinalBody.Count == 0) throw Err("expected 'except' or 'finally' block");
        return node;
    }

    private WithAst ParseWith()
    {
        var tok = Next();
        var node = At(new WithAst(), tok);
        while (true)
        {
            var ctx = ParseTest();
            ExprAst? vars = null;
            if (AcceptKw("as"))
            {
                vars = ParseTargetAtom();
                CheckTarget(vars, false);
            }
            node.Items.Add((ctx, vars));
            if (!AcceptOp(",")) break;
        }
        node.Body = ParseBlock("'with' statement", tok.Line);
        return node;
    }

    private ExprAst ParseTargetAtom()
    {
        var e = ParseExpr();
        return e;
    }

    private StmtAst ParseDecorated()
    {
        var decorators = new List<ExprAst>();
        while (IsOp("@"))
        {
            Next();
            decorators.Add(ParseNamedExprTest());
            if (Cur.Kind != TokenKind.Newline) throw Err("invalid syntax");
            Next();
        }
        if (IsKw("def")) return ParseFuncDef(decorators);
        if (IsKw("class")) return ParseClassDef(decorators);
        if (IsKw("async")) throw Err("async functions are not supported in this interpreter");
        throw Err("invalid syntax");
    }

    private FunctionDefAst ParseFuncDef(List<ExprAst> decorators)
    {
        var tok = Next();
        var node = At(new FunctionDefAst(), tok);
        node.Decorators = decorators;
        node.Name = ExpectName();
        ExpectOp("(", "expected '('");
        node.Args = ParseParams(true, ")");
        ExpectOp(")");
        if (AcceptOp("->")) node.Returns = ParseTest();
        node.Body = ParseBlock("function definition", tok.Line);
        return node;
    }

    private ClassDefAst ParseClassDef(List<ExprAst> decorators)
    {
        var tok = Next();
        var node = At(new ClassDefAst(), tok);
        node.Decorators = decorators;
        node.Name = ExpectName();
        if (AcceptOp("("))
        {
            var (args, kws) = ParseArgList();
            node.Bases = args;
            node.Keywords = kws;
        }
        node.Body = ParseBlock("class definition", tok.Line);
        return node;
    }

    private ArgumentsAst ParseParams(bool annotations, string closer)
    {
        var a = new ArgumentsAst();
        bool seenStar = false, seenDefault = false;
        var positional = new List<ArgAst>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        void AddName(ArgAst p)
        {
            if (!names.Add(p.Name)) throw new ParseError($"duplicate argument '{p.Name}' in function definition", p.Line, p.Col);
        }
        while (!IsOp(closer))
        {
            if (IsOp("/"))
            {
                Next();
                if (seenStar || a.PosOnly.Count > 0 || positional.Count == 0) throw Err("invalid syntax");
                a.PosOnly.AddRange(positional);
                positional.Clear();
            }
            else if (IsOp("**"))
            {
                Next();
                a.KwArg = ParseParam(annotations);
                AddName(a.KwArg);
                AcceptOp(",");
                if (!IsOp(closer)) throw Err("arguments cannot follow var-keyword argument");
                break;
            }
            else if (IsOp("*"))
            {
                var st = Next();
                if (seenStar) throw Err("* argument may appear only once");
                seenStar = true;
                if (!IsOp(",") && !IsOp(closer))
                {
                    a.VarArg = ParseParam(annotations);
                    AddName(a.VarArg);
                }
                else if (IsOp(closer) || (IsOp(",") && PeekTok().Kind == TokenKind.Op && (PeekTok().Text == closer || PeekTok().Text == "**")))
                {
                    throw Err("named arguments must follow bare *", st);
                }
            }
            else
            {
                var p = ParseParam(annotations);
                AddName(p);
                ExprAst? def = null;
                if (AcceptOp("=")) def = ParseTest();
                if (seenStar)
                {
                    a.KwOnly.Add(p);
                    a.KwDefaults.Add(def);
                }
                else
                {
                    positional.Add(p);
                    if (def != null)
                    {
                        a.Defaults.Add(def);
                        seenDefault = true;
                    }
                    else if (seenDefault) throw new ParseError("parameter without a default follows parameter with a default", p.Line, p.Col);
                }
            }
            if (!AcceptOp(",")) break;
        }
        a.Args = positional;
        return a;
    }

    private ArgAst ParseParam(bool annotations)
    {
        var tok = Cur;
        var p = At(new ArgAst(), tok);
        p.Name = ExpectName();
        if (annotations && AcceptOp(":")) p.Annotation = ParseTest();
        return p;
    }

    private List<StmtAst> ParseSimpleStatements()
    {
        var list = new List<StmtAst>();
        while (true)
        {
            list.Add(ParseSmallStatement());
            if (AcceptOp(";"))
            {
                if (Cur.Kind == TokenKind.Newline || Cur.Kind == TokenKind.EndMarker) break;
                continue;
            }
            break;
        }
        if (Cur.Kind == TokenKind.Newline) Next();
        else if (Cur.Kind != TokenKind.EndMarker && Cur.Kind != TokenKind.Dedent)
        {
            if (Cur.Kind == TokenKind.Indent) throw Err("unexpected indent", indentation: true);
            throw Err(IsOp("=") ? "invalid syntax. Maybe you meant '==' or ':=' instead of '='?" : "invalid syntax");
        }
        return list;
    }

    private StmtAst ParseSmallStatement()
    {
        var tok = Cur;
        if (tok.Kind == TokenKind.Name)
        {
            switch (tok.Text)
            {
                case "pass":
                    Next();
                    return At(new PassAst(), tok);
                case "break":
                    Next();
                    return At(new BreakAst(), tok);
                case "continue":
                    Next();
                    return At(new ContinueAst(), tok);
                case "return":
                {
                    Next();
                    var r = At(new ReturnAst(), tok);
                    if (!AtStatementEnd()) r.Value = ParseTestListStarExpr();
                    return r;
                }
                case "raise":
                {
                    Next();
                    var r = At(new RaiseAst(), tok);
                    if (!AtStatementEnd())
                    {
                        r.Exc = ParseTest();
                        if (AcceptKw("from")) r.Cause = ParseTest();
                    }
                    return r;
                }
                case "global":
                case "nonlocal":
                {
                    Next();
                    var names = new List<string> { ExpectName() };
                    while (AcceptOp(",")) names.Add(ExpectName());
                    if (tok.Text == "global") return At(new GlobalAst { Names = names }, tok);
                    return At(new NonlocalAst { Names = names }, tok);
                }
                case "del":
                {
                    Next();
                    var d = At(new DeleteAst(), tok);
                    var e = ParseTargetList();
                    if (e is TupleAst tu && !tu.Parenthesized) d.Targets.AddRange(tu.Elts);
                    else d.Targets.Add(e);
                    foreach (var t in d.Targets) CheckDelTarget(t);
                    return d;
                }
                case "assert":
                {
                    Next();
                    var a = At(new AssertAst(), tok);
                    a.Test = ParseTest();
                    if (AcceptOp(",")) a.Msg = ParseTest();
                    return a;
                }
                case "import":
                {
                    Next();
                    var im = At(new ImportAst(), tok);
                    do
                    {
                        var name = ParseDottedName();
                        string? asName = AcceptKw("as") ? ExpectName() : null;
                        im.Names.Add((name, asName));
                    } while (AcceptOp(","));
                    return im;
                }
                case "from":
                {
                    Next();
                    var im = At(new ImportFromAst(), tok);
                    while (IsOp(".") || IsOp("..."))
                    {
                        im.Level += Cur.Text.Length;
                        Next();
                    }
                    if (!IsKw("import")) im.Module = ParseDottedName();
                    ExpectKw("import");
                    if (AcceptOp("*")) im.Names.Add(("*", null));
                    else
                    {
                        bool paren = AcceptOp("(");
                        do
                        {
                            if (paren && IsOp(")")) break;
                            var name = ExpectName();
                            string? asName = AcceptKw("as") ? ExpectName() : null;
                            im.Names.Add((name, asName));
                        } while (AcceptOp(","));
                        if (paren) ExpectOp(")");
                    }
                    return im;
                }
            }
        }
        return ParseExprStatement();
    }

    private string ParseDottedName()
    {
        var name = ExpectName();
        while (AcceptOp(".")) name += "." + ExpectName();
        return name;
    }

    private bool AtStatementEnd() =>
        Cur.Kind is TokenKind.Newline or TokenKind.EndMarker or TokenKind.Dedent || IsOp(";");

    private bool AtExprEnd()
    {
        if (Cur.Kind is TokenKind.Newline or TokenKind.EndMarker or TokenKind.Dedent or TokenKind.Indent) return true;
        if (Cur.Kind == TokenKind.Op)
        {
            switch (Cur.Text)
            {
                case ")":
                case "]":
                case "}":
                case "=":
                case ";":
                case ":":
                    return true;
            }
            if (AugOps.ContainsKey(Cur.Text)) return true;
        }
        return false;
    }

    private StmtAst ParseExprStatement()
    {
        var start = Cur;
        ExprAst first = IsKw("yield") ? ParseYieldExpr() : ParseTestListStarExpr();
        if (IsOp("="))
        {
            var node = At(new AssignAst(), start);
            node.Targets.Add(first);
            while (true)
            {
                Next();
                var v = IsKw("yield") ? ParseYieldExpr() : ParseTestListStarExpr();
                if (IsOp("="))
                {
                    node.Targets.Add(v);
                    continue;
                }
                node.Value = v;
                break;
            }
            foreach (var t in node.Targets) CheckTarget(t, true);
            return node;
        }
        if (Cur.Kind == TokenKind.Op && AugOps.TryGetValue(Cur.Text, out var op))
        {
            if (first is not (NameAst or AttributeAst or SubscriptAst))
                throw new ParseError($"'{Describe(first)}' is an illegal expression for augmented assignment", first.Line, first.Col);
            Next();
            var node = At(new AugAssignAst { Target = first, Op = op }, start);
            node.Value = IsKw("yield") ? ParseYieldExpr() : ParseTestListStarExpr();
            return node;
        }
        if (IsOp(":") && first is NameAst or AttributeAst or SubscriptAst)
        {
            Next();
            var node = At(new AnnAssignAst { Target = first }, start);
            node.Annotation = ParseTest();
            if (AcceptOp("=")) node.Value = IsKw("yield") ? ParseYieldExpr() : ParseTestListStarExpr();
            return node;
        }
        if (first is NameAst { Id: "print" or "exec" } pn && !first.Parenthesized &&
            (Cur.Kind is TokenKind.String or TokenKind.Number || (Cur.Kind == TokenKind.Name && !Keywords.Contains(Cur.Text))))
            throw new ParseError($"Missing parentheses in call to '{pn.Id}'. Did you mean {pn.Id}(...)?", start.Line, start.Col);
        if (first is StarredAst) throw new ParseError("can't use starred expression here", first.Line, first.Col);
        return At(new ExprStmtAst { Value = first }, start);
    }

    private static string Describe(ExprAst e) => e switch
    {
        ConstantAst c when c.Value == Py.None => "None",
        ConstantAst c when c.Value == PyBool.True => "True",
        ConstantAst c when c.Value == PyBool.False => "False",
        ConstantAst c when c.Value == Py.Ellipsis => "ellipsis",
        ConstantAst => "literal",
        JoinedStrAst => "f-string expression",
        CallAst => "function call",
        CompareAst => "comparison",
        LambdaAst => "lambda",
        IfExpAst => "conditional expression",
        BoolOpAst or BinOpAst or UnaryOpAst => "expression",
        ComprehensionAst { Kind: ComprehensionKind.Generator } => "generator expression",
        ComprehensionAst { Kind: ComprehensionKind.List } => "list comprehension",
        ComprehensionAst { Kind: ComprehensionKind.Set } => "set comprehension",
        ComprehensionAst => "dict comprehension",
        DictAst => "dict literal",
        SetAst => "set display",
        NamedExprAst => "named expression",
        YieldAst or YieldFromAst => "yield expression",
        _ => "expression",
    };

    private void CheckTarget(ExprAst e, bool assign)
    {
        switch (e)
        {
            case NameAst n:
                if (n.Id == "__debug__") throw new ParseError("cannot assign to __debug__", e.Line, e.Col);
                return;
            case AttributeAst:
            case SubscriptAst:
                return;
            case TupleAst t:
                CheckSeqTarget(t.Elts, assign);
                return;
            case ListAst l:
                CheckSeqTarget(l.Elts, assign);
                return;
            case StarredAst:
                throw new ParseError("starred assignment target must be in a list or tuple", e.Line, e.Col);
        }
        string desc = Describe(e);
        if (desc is "None" or "True" or "False" or "__debug__")
            throw new ParseError($"cannot assign to {desc}", e.Line, e.Col);
        throw new ParseError(assign ? $"cannot assign to {desc} here. Maybe you meant '==' instead of '='?" : $"cannot assign to {desc}", e.Line, e.Col);
    }

    private void CheckSeqTarget(List<ExprAst> elts, bool assign)
    {
        int stars = 0;
        foreach (var x in elts)
        {
            if (x is StarredAst s)
            {
                if (++stars > 1) throw new ParseError("multiple starred expressions in assignment", x.Line, x.Col);
                CheckTarget(s.Value, assign);
            }
            else CheckTarget(x, assign);
        }
    }

    private void CheckDelTarget(ExprAst e)
    {
        switch (e)
        {
            case NameAst:
            case AttributeAst:
            case SubscriptAst:
                return;
            case TupleAst t:
                foreach (var x in t.Elts) CheckDelTarget(x);
                return;
            case ListAst l:
                foreach (var x in l.Elts) CheckDelTarget(x);
                return;
        }
        throw new ParseError($"cannot delete {Describe(e)}", e.Line, e.Col);
    }

    // ------------------------------------------------------------------ expressions

    private ExprAst ParseTestListStarExpr()
    {
        var start = Cur;
        var first = ParseTestOrStar();
        if (!IsOp(",")) return first;
        var elts = new List<ExprAst> { first };
        while (AcceptOp(","))
        {
            if (AtExprEnd() || IsKw("in")) break;
            elts.Add(ParseTestOrStar());
        }
        return At(new TupleAst { Elts = elts }, start);
    }

    private ExprAst ParseTestOrStar()
    {
        if (IsOp("*"))
        {
            var tok = Next();
            return At(new StarredAst { Value = ParseExpr() }, tok);
        }
        return ParseNamedExprTest();
    }

    private ExprAst ParseTargetList()
    {
        var start = Cur;
        ExprAst ParseOne()
        {
            if (IsOp("*"))
            {
                var tok = Next();
                return At(new StarredAst { Value = ParseExpr() }, tok);
            }
            return ParseExpr();
        }
        var first = ParseOne();
        if (!IsOp(",")) return first;
        var elts = new List<ExprAst> { first };
        while (AcceptOp(","))
        {
            if (IsKw("in") || IsOp("=") || AtExprEnd()) break;
            elts.Add(ParseOne());
        }
        return At(new TupleAst { Elts = elts }, start);
    }

    private ExprAst ParseNamedExprTest()
    {
        var start = Cur;
        var t = ParseTest();
        if (IsOp(":="))
        {
            if (t is not NameAst n) throw new ParseError($"cannot use assignment expressions with {Describe(t)}", t.Line, t.Col);
            Next();
            var value = ParseTest();
            return At(new NamedExprAst { Target = n, Value = value }, start);
        }
        return t;
    }

    private ExprAst ParseTest()
    {
        Enter();
        try
        {
            if (IsKw("lambda")) return ParseLambda(false);
            var start = Cur;
            var e = ParseOrTest();
            if (IsKw("if"))
            {
                Next();
                var cond = ParseOrTest();
                if (!IsKw("else")) throw Err("expected 'else' after 'if' expression");
                Next();
                var orelse = ParseTest();
                return At(new IfExpAst { Test = cond, Body = e, OrElse = orelse }, start);
            }
            return e;
        }
        finally
        {
            Leave();
        }
    }

    private ExprAst ParseTestNoCond()
    {
        if (IsKw("lambda")) return ParseLambda(true);
        return ParseOrTest();
    }

    private ExprAst ParseLambda(bool noCond)
    {
        var tok = Next();
        var node = At(new LambdaAst(), tok);
        node.Args = ParseParams(false, ":");
        ExpectOp(":");
        node.Body = noCond ? ParseTestNoCond() : ParseTest();
        return node;
    }

    private ExprAst ParseOrTest()
    {
        var start = Cur;
        var e = ParseAndTest();
        if (!IsKw("or")) return e;
        var node = At(new BoolOpAst { IsAnd = false }, start);
        node.Values.Add(e);
        while (AcceptKw("or")) node.Values.Add(ParseAndTest());
        return node;
    }

    private ExprAst ParseAndTest()
    {
        var start = Cur;
        var e = ParseNotTest();
        if (!IsKw("and")) return e;
        var node = At(new BoolOpAst { IsAnd = true }, start);
        node.Values.Add(e);
        while (AcceptKw("and")) node.Values.Add(ParseNotTest());
        return node;
    }

    private ExprAst ParseNotTest()
    {
        if (IsKw("not"))
        {
            var tok = Next();
            Enter();
            try
            {
                return At(new UnaryOpAst { Op = UnaryKind.Not, Operand = ParseNotTest() }, tok);
            }
            finally
            {
                Leave();
            }
        }
        return ParseComparison();
    }

    private ExprAst ParseComparison()
    {
        var start = Cur;
        var left = ParseExpr();
        CompareAst? node = null;
        while (true)
        {
            CmpKind op;
            if (Cur.Kind == TokenKind.Op)
            {
                switch (Cur.Text)
                {
                    case "<": op = CmpKind.Lt; break;
                    case ">": op = CmpKind.Gt; break;
                    case "==": op = CmpKind.Eq; break;
                    case ">=": op = CmpKind.Ge; break;
                    case "<=": op = CmpKind.Le; break;
                    case "!=": op = CmpKind.Ne; break;
                    default: goto done;
                }
                Next();
            }
            else if (IsKw("in"))
            {
                op = CmpKind.In;
                Next();
            }
            else if (IsKw("not") && PeekTok().Kind == TokenKind.Name && PeekTok().Text == "in")
            {
                op = CmpKind.NotIn;
                Next();
                Next();
            }
            else if (IsKw("is"))
            {
                Next();
                op = AcceptKw("not") ? CmpKind.IsNot : CmpKind.Is;
            }
            else break;
            node ??= At(new CompareAst { Left = left }, start);
            node.Ops.Add(op);
            node.Comparators.Add(ParseExpr());
        }
    done:
        return node ?? left;
    }

    private ExprAst ParseExpr() => ParseBinary(0);

    private static readonly string[][] Levels =
    {
        new[] { "|" },
        new[] { "^" },
        new[] { "&" },
        new[] { "<<", ">>" },
        new[] { "+", "-" },
        new[] { "*", "/", "//", "%", "@" },
    };

    private static BinOp OpOf(string s) => s switch
    {
        "|" => BinOp.Or,
        "^" => BinOp.Xor,
        "&" => BinOp.And,
        "<<" => BinOp.LShift,
        ">>" => BinOp.RShift,
        "+" => BinOp.Add,
        "-" => BinOp.Sub,
        "*" => BinOp.Mul,
        "/" => BinOp.TrueDiv,
        "//" => BinOp.FloorDiv,
        "%" => BinOp.Mod,
        "@" => BinOp.MatMul,
        _ => throw new InvalidOperationException(s),
    };

    private ExprAst ParseBinary(int level)
    {
        if (level == Levels.Length) return ParseFactor();
        var left = ParseBinary(level + 1);
        while (Cur.Kind == TokenKind.Op && Array.IndexOf(Levels[level], Cur.Text) >= 0)
        {
            var tok = Next();
            var right = ParseBinary(level + 1);
            left = At(new BinOpAst { Op = OpOf(tok.Text), Left = left, Right = right }, tok);
            left.Line = right.Line < left.Line ? right.Line : left.Line;
        }
        return left;
    }

    private ExprAst ParseFactor()
    {
        if (Cur.Kind == TokenKind.Op && (Cur.Text == "-" || Cur.Text == "+" || Cur.Text == "~"))
        {
            var tok = Next();
            Enter();
            try
            {
                var operand = ParseFactor();
                var kind = tok.Text == "-" ? UnaryKind.Neg : tok.Text == "+" ? UnaryKind.Pos : UnaryKind.Invert;
                return At(new UnaryOpAst { Op = kind, Operand = operand }, tok);
            }
            finally
            {
                Leave();
            }
        }
        return ParsePower();
    }

    private ExprAst ParsePower()
    {
        var start = Cur;
        var e = ParseAtomExpr();
        if (IsOp("**"))
        {
            Next();
            Enter();
            try
            {
                var right = ParseFactor();
                return At(new BinOpAst { Op = BinOp.Pow, Left = e, Right = right }, start);
            }
            finally
            {
                Leave();
            }
        }
        return e;
    }

    private ExprAst ParseAtomExpr()
    {
        if (IsKw("await")) throw Err("'await' is not supported in this interpreter");
        var e = ParseAtom();
        while (true)
        {
            if (IsOp("("))
            {
                var tok = Next();
                var call = At(new CallAst { Func = e }, tok);
                call.Line = e.Line;
                call.Col = e.Col;
                var (args, kws) = ParseArgList();
                call.Args = args;
                call.Keywords = kws;
                e = call;
            }
            else if (IsOp("["))
            {
                var tok = Next();
                var idx = ParseSubscriptList();
                ExpectOp("]");
                e = new SubscriptAst { Value = e, Index = idx, Line = e.Line, Col = e.Col };
            }
            else if (IsOp("."))
            {
                Next();
                var nameTok = Cur;
                var name = ExpectName();
                e = new AttributeAst { Value = e, Attr = name, Line = e.Line, Col = e.Col };
            }
            else break;
        }
        return e;
    }

    /// <summary>Parses call arguments after the opening parenthesis, consuming the closing one.</summary>
    private (List<ExprAst>, List<KeywordAst>) ParseArgList()
    {
        var args = new List<ExprAst>();
        var kws = new List<KeywordAst>();
        bool sawKw = false, sawKwUnpack = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!IsOp(")"))
        {
            var tok = Cur;
            if (AcceptOp("*"))
            {
                if (sawKwUnpack) throw Err("iterable argument unpacking follows keyword argument unpacking", tok);
                args.Add(At(new StarredAst { Value = ParseTest() }, tok));
            }
            else if (AcceptOp("**"))
            {
                kws.Add(At(new KeywordAst { Name = null, Value = ParseTest() }, tok));
                sawKwUnpack = true;
            }
            else
            {
                var e = ParseNamedExprTest();
                if (IsOp("="))
                {
                    if (e is not NameAst n || e.Parenthesized)
                        throw new ParseError("expression cannot contain assignment, perhaps you meant \"==\"?", e.Line, e.Col);
                    Next();
                    if (!seen.Add(n.Id)) throw new ParseError($"keyword argument repeated: {n.Id}", e.Line, e.Col);
                    kws.Add(At(new KeywordAst { Name = n.Id, Value = ParseTest() }, tok));
                    sawKw = true;
                }
                else if (IsKw("for"))
                {
                    var gens = ParseCompFor();
                    var g = At(new ComprehensionAst { Kind = ComprehensionKind.Generator, Elt = e, Generators = gens }, tok);
                    if (args.Count > 0 || kws.Count > 0 || IsOp(","))
                    {
                        if (!(IsOp(")") || (IsOp(",") && PeekTok().Kind == TokenKind.Op && PeekTok().Text == ")" && args.Count == 0 && kws.Count == 0)))
                            throw new ParseError("Generator expression must be parenthesized", e.Line, e.Col);
                    }
                    args.Add(g);
                }
                else
                {
                    if (sawKwUnpack) throw new ParseError("positional argument follows keyword argument unpacking", e.Line, e.Col);
                    if (sawKw) throw new ParseError("positional argument follows keyword argument", e.Line, e.Col);
                    args.Add(e);
                }
            }
            if (!AcceptOp(",")) break;
        }
        ExpectOp(")", Cur.Kind == TokenKind.EndMarker ? "invalid syntax" : "invalid syntax. Perhaps you forgot a comma?");
        return (args, kws);
    }

    private ExprAst ParseSubscriptList()
    {
        var start = Cur;
        var first = ParseSubscript();
        if (!IsOp(",")) return first;
        var elts = new List<ExprAst> { first };
        while (AcceptOp(","))
        {
            if (IsOp("]")) break;
            elts.Add(ParseSubscript());
        }
        return At(new TupleAst { Elts = elts }, start);
    }

    private ExprAst ParseSubscript()
    {
        var start = Cur;
        ExprAst? lower = null;
        if (!IsOp(":"))
        {
            lower = IsOp("*") ? ParseTestOrStar() : ParseNamedExprTest();
            if (!IsOp(":")) return lower;
        }
        Next();
        ExprAst? upper = null, step = null;
        if (!(IsOp("]") || IsOp(",") || IsOp(":"))) upper = ParseTest();
        if (AcceptOp(":"))
        {
            if (!(IsOp("]") || IsOp(","))) step = ParseTest();
        }
        return At(new SliceAst { Lower = lower, Upper = upper, Step = step }, start);
    }

    private List<ComprehensionFor> ParseCompFor()
    {
        var gens = new List<ComprehensionFor>();
        while (IsKw("for") || IsKw("async"))
        {
            if (IsKw("async")) throw Err("asynchronous comprehensions are not supported in this interpreter");
            var tok = Next();
            var g = At(new ComprehensionFor(), tok);
            g.Target = ParseTargetList();
            CheckTarget(g.Target, false);
            ExpectKw("in");
            g.Iter = ParseOrTest();
            while (IsKw("if"))
            {
                Next();
                g.Ifs.Add(ParseTestNoCond());
            }
            gens.Add(g);
        }
        return gens;
    }

    private ExprAst ParseYieldExpr()
    {
        var tok = Next();
        if (AcceptKw("from")) return At(new YieldFromAst { Value = ParseTest() }, tok);
        var y = At(new YieldAst(), tok);
        if (!AtExprEnd()) y.Value = ParseTestListStarExpr();
        return y;
    }

    private ExprAst ParseAtom()
    {
        var tok = Cur;
        switch (tok.Kind)
        {
            case TokenKind.Name:
                switch (tok.Text)
                {
                    case "None":
                        Next();
                        return At(new ConstantAst { Value = Py.None }, tok);
                    case "True":
                        Next();
                        return At(new ConstantAst { Value = PyBool.True }, tok);
                    case "False":
                        Next();
                        return At(new ConstantAst { Value = PyBool.False }, tok);
                }
                if (Keywords.Contains(tok.Text)) throw Err("invalid syntax");
                Next();
                return At(new NameAst { Id = tok.Text }, tok);
            case TokenKind.Number:
                Next();
                return At(new ConstantAst { Value = (PyObject)tok.Value! }, tok);
            case TokenKind.String:
                return ParseStrings();
            case TokenKind.Op:
                switch (tok.Text)
                {
                    case "(":
                        return ParseParen();
                    case "[":
                        return ParseListDisplay();
                    case "{":
                        return ParseDictOrSet();
                    case "...":
                        Next();
                        return At(new ConstantAst { Value = Py.Ellipsis }, tok);
                }
                break;
            case TokenKind.Indent:
                throw Err("unexpected indent", indentation: true);
        }
        throw Err("invalid syntax");
    }

    private ExprAst ParseParen()
    {
        var open = Next();
        Enter();
        try
        {
            if (AcceptOp(")")) return At(new TupleAst { Parenthesized = true }, open);
            if (IsKw("yield"))
            {
                var y = ParseYieldExpr();
                ExpectOp(")");
                y.Parenthesized = true;
                return y;
            }
            var first = ParseTestOrStar();
            if (IsKw("for"))
            {
                var gens = ParseCompFor();
                ExpectOp(")");
                return At(new ComprehensionAst { Kind = ComprehensionKind.Generator, Elt = first, Generators = gens, Parenthesized = true }, open);
            }
            if (IsOp(","))
            {
                var elts = new List<ExprAst> { first };
                while (AcceptOp(","))
                {
                    if (IsOp(")")) break;
                    elts.Add(ParseTestOrStar());
                }
                ExpectOp(")", Cur.Kind == TokenKind.EndMarker ? "invalid syntax" : "invalid syntax. Perhaps you forgot a comma?");
                return At(new TupleAst { Elts = elts, Parenthesized = true }, open);
            }
            ExpectOp(")", Cur.Kind == TokenKind.EndMarker ? "invalid syntax" : "invalid syntax. Perhaps you forgot a comma?");
            if (first is StarredAst) throw new ParseError("cannot use starred expression here", first.Line, first.Col);
            first.Parenthesized = true;
            return first;
        }
        finally
        {
            Leave();
        }
    }

    private ExprAst ParseListDisplay()
    {
        var open = Next();
        Enter();
        try
        {
            var node = At(new ListAst(), open);
            if (AcceptOp("]")) return node;
            var first = ParseTestOrStar();
            if (IsKw("for"))
            {
                var gens = ParseCompFor();
                ExpectOp("]");
                return At(new ComprehensionAst { Kind = ComprehensionKind.List, Elt = first, Generators = gens }, open);
            }
            node.Elts.Add(first);
            while (AcceptOp(","))
            {
                if (IsOp("]")) break;
                node.Elts.Add(ParseTestOrStar());
            }
            ExpectOp("]", Cur.Kind == TokenKind.EndMarker ? "invalid syntax" : "invalid syntax. Perhaps you forgot a comma?");
            return node;
        }
        finally
        {
            Leave();
        }
    }

    private ExprAst ParseDictOrSet()
    {
        var open = Next();
        Enter();
        try
        {
            if (AcceptOp("}")) return At(new DictAst(), open);
            if (IsOp("**"))
            {
                Next();
                var d = At(new DictAst(), open);
                d.Keys.Add(null);
                d.Values.Add(ParseExpr());
                return FinishDict(d);
            }
            var first = ParseTestOrStar();
            if (AcceptOp(":"))
            {
                var value = ParseTest();
                if (IsKw("for"))
                {
                    var gens = ParseCompFor();
                    ExpectOp("}");
                    return At(new ComprehensionAst { Kind = ComprehensionKind.Dict, Elt = first, Value = value, Generators = gens }, open);
                }
                var d = At(new DictAst(), open);
                d.Keys.Add(first);
                d.Values.Add(value);
                return FinishDict(d);
            }
            if (IsKw("for"))
            {
                var gens = ParseCompFor();
                ExpectOp("}");
                return At(new ComprehensionAst { Kind = ComprehensionKind.Set, Elt = first, Generators = gens }, open);
            }
            var s = At(new SetAst(), open);
            s.Elts.Add(first);
            while (AcceptOp(","))
            {
                if (IsOp("}")) break;
                s.Elts.Add(ParseTestOrStar());
            }
            ExpectOp("}", Cur.Kind == TokenKind.EndMarker ? "invalid syntax" : "invalid syntax. Perhaps you forgot a comma?");
            return s;
        }
        finally
        {
            Leave();
        }
    }

    private DictAst FinishDict(DictAst d)
    {
        while (AcceptOp(","))
        {
            if (IsOp("}")) break;
            if (AcceptOp("**"))
            {
                d.Keys.Add(null);
                d.Values.Add(ParseExpr());
                continue;
            }
            var k = ParseTest();
            ExpectOp(":", "':' expected after dictionary key");
            d.Keys.Add(k);
            d.Values.Add(ParseTest());
        }
        ExpectOp("}", Cur.Kind == TokenKind.EndMarker ? "invalid syntax" : "invalid syntax. Perhaps you forgot a comma?");
        return d;
    }

    private ExprAst ParseStrings()
    {
        var first = Cur;
        var lits = new List<StringLit>();
        while (Cur.Kind == TokenKind.String) lits.Add((StringLit)Next().Value!);
        if (lits.All(l => l.FParts == null))
        {
            var s = string.Concat(lits.Select(l => l.Value));
            return At(new ConstantAst { Value = PyStr.New(s) }, first);
        }
        var js = At(new JoinedStrAst(), first);
        var sb = new System.Text.StringBuilder();
        void FlushLiteral()
        {
            if (sb.Length > 0)
            {
                js.Parts.Add(At(new ConstantAst { Value = PyStr.New(sb.ToString()) }, first));
                sb.Clear();
            }
        }
        foreach (var lit in lits)
        {
            if (lit.FParts == null)
            {
                sb.Append(lit.Value);
                continue;
            }
            foreach (var part in lit.FParts)
            {
                if (part.Literal != null)
                {
                    sb.Append(part.Literal);
                    continue;
                }
                if (part.Debug != null) sb.Append(part.Debug);
                FlushLiteral();
                js.Parts.Add(MakeFormatted(part, first));
            }
        }
        FlushLiteral();
        return js;
    }

    private FormattedValueAst MakeFormatted(FPart part, Token at)
    {
        var fv = At(new FormattedValueAst(), at);
        fv.Value = ParseFStringExpr(part);
        fv.Conversion = part.Conversion;
        if (part.Spec != null)
        {
            var spec = At(new JoinedStrAst(), at);
            foreach (var sp in part.Spec)
            {
                if (sp.Literal != null) spec.Parts.Add(At(new ConstantAst { Value = PyStr.New(sp.Literal) }, at));
                else spec.Parts.Add(MakeFormatted(sp, at));
            }
            fv.Spec = spec;
        }
        return fv;
    }

    private ExprAst ParseFStringExpr(FPart part)
    {
        var tokens = Lexer.Tokenize("(" + part.Expr + ")", part.ExprLine, Math.Max(0, part.ExprCol - 1));
        var sub = new Parser(tokens) { _depth = _depth };
        var e = sub.ParseTestListStarExpr();
        while (sub.Cur.Kind == TokenKind.Newline) sub.Next();
        if (sub.Cur.Kind != TokenKind.EndMarker) throw sub.Err("f-string: invalid syntax");
        return e;
    }

    // ------------------------------------------------------------------ match statement

    private bool LooksLikeMatch()
    {
        var next = PeekTok();
        if (next.Kind == TokenKind.Newline || next.Kind == TokenKind.EndMarker) return false;
        if (next.Kind == TokenKind.Op && next.Text is "=" or "." or ")" or "]" or "," or ";" or ":" || (next.Kind == TokenKind.Op && AugOps.ContainsKey(next.Text)))
            return false;
        int depth = 0;
        for (int i = _p + 1; i < _t.Count; i++)
        {
            var t = _t[i];
            if (t.Kind == TokenKind.Op)
            {
                if (t.Text is "(" or "[" or "{") depth++;
                else if (t.Text is ")" or "]" or "}") depth--;
            }
            if (t.Kind == TokenKind.Newline || t.Kind == TokenKind.EndMarker)
            {
                var prev = _t[i - 1];
                if (!(prev.Kind == TokenKind.Op && prev.Text == ":")) return false;
                var after = i + 2 < _t.Count ? _t[i + 2] : null;
                return i + 1 < _t.Count && _t[i + 1].Kind == TokenKind.Indent && after != null && after.Kind == TokenKind.Name && after.Text == "case";
            }
        }
        return false;
    }

    private MatchAst ParseMatch()
    {
        var tok = Next();
        var node = At(new MatchAst(), tok);
        node.Subject = ParseTestListStarExpr();
        ExpectOp(":");
        if (Cur.Kind != TokenKind.Newline) throw Err("invalid syntax");
        Next();
        if (Cur.Kind != TokenKind.Indent) throw Err($"expected an indented block after 'match' statement on line {tok.Line}", indentation: true);
        Next();
        while (IsKw("case"))
        {
            var ct = Next();
            var c = At(new MatchCaseAst(), ct);
            c.Pattern = ParsePatterns();
            if (AcceptKw("if")) c.Guard = ParseNamedExprTest();
            c.Body = ParseBlock("'case' statement", ct.Line);
            node.Cases.Add(c);
        }
        if (Cur.Kind == TokenKind.Dedent) Next();
        else if (Cur.Kind != TokenKind.EndMarker) throw Err("invalid syntax");
        if (node.Cases.Count == 0) throw Err("invalid syntax");
        return node;
    }

    private PatternAst ParsePatterns()
    {
        var start = Cur;
        var p = ParseMaybeStarPattern();
        if (!IsOp(","))
        {
            if (p is MatchStarPattern) throw Err("invalid syntax", start);
            return p;
        }
        var seq = At(new MatchSequencePattern(), start);
        seq.Patterns.Add(p);
        while (AcceptOp(","))
        {
            if (IsOp(":") || IsKw("if")) break;
            seq.Patterns.Add(ParseMaybeStarPattern());
        }
        return seq;
    }

    private PatternAst ParseMaybeStarPattern()
    {
        if (IsOp("*"))
        {
            var tok = Next();
            var name = ExpectName();
            return At(new MatchStarPattern { Name = name == "_" ? null : name }, tok);
        }
        return ParsePattern();
    }

    private PatternAst ParsePattern()
    {
        var start = Cur;
        var p = ParseOrPattern();
        if (AcceptKw("as"))
        {
            var name = ExpectName();
            if (name == "_") throw Err("cannot use '_' as a target");
            return At(new MatchAsPattern { Pattern = p, Name = name }, start);
        }
        return p;
    }

    private PatternAst ParseOrPattern()
    {
        var start = Cur;
        var first = ParseClosedPattern();
        if (!IsOp("|")) return first;
        var or = At(new MatchOrPattern(), start);
        or.Patterns.Add(first);
        while (AcceptOp("|")) or.Patterns.Add(ParseClosedPattern());
        return or;
    }

    private PatternAst ParseClosedPattern()
    {
        Enter();
        try
        {
            var tok = Cur;
            if (tok.Kind == TokenKind.Number || tok.Kind == TokenKind.String || IsOp("-"))
                return At(new MatchValuePattern { Value = ParseLiteralPatternValue() }, tok);
            if (tok.Kind == TokenKind.Name)
            {
                switch (tok.Text)
                {
                    case "None":
                        Next();
                        return At(new MatchSingletonPattern { Value = Py.None }, tok);
                    case "True":
                        Next();
                        return At(new MatchSingletonPattern { Value = PyBool.True }, tok);
                    case "False":
                        Next();
                        return At(new MatchSingletonPattern { Value = PyBool.False }, tok);
                }
                var name = ExpectName();
                ExprAst e = At(new NameAst { Id = name }, tok);
                bool dotted = false;
                while (AcceptOp("."))
                {
                    dotted = true;
                    e = At(new AttributeAst { Value = e, Attr = ExpectName() }, tok);
                }
                if (IsOp("(")) return ParseClassPattern(e, tok);
                if (dotted) return At(new MatchValuePattern { Value = e }, tok);
                if (name == "_") return At(new MatchAsPattern(), tok);
                return At(new MatchAsPattern { Name = name }, tok);
            }
            if (IsOp("("))
            {
                Next();
                if (AcceptOp(")")) return At(new MatchSequencePattern(), tok);
                var p = ParseMaybeStarPattern();
                if (IsOp(","))
                {
                    var seq = At(new MatchSequencePattern(), tok);
                    seq.Patterns.Add(p);
                    while (AcceptOp(","))
                    {
                        if (IsOp(")")) break;
                        seq.Patterns.Add(ParseMaybeStarPattern());
                    }
                    ExpectOp(")");
                    return seq;
                }
                ExpectOp(")");
                return p;
            }
            if (IsOp("["))
            {
                Next();
                var seq = At(new MatchSequencePattern(), tok);
                while (!IsOp("]"))
                {
                    seq.Patterns.Add(ParseMaybeStarPattern());
                    if (!AcceptOp(",")) break;
                }
                ExpectOp("]");
                return seq;
            }
            if (IsOp("{"))
            {
                Next();
                var map = At(new MatchMappingPattern(), tok);
                while (!IsOp("}"))
                {
                    if (AcceptOp("**"))
                    {
                        map.Rest = ExpectName();
                        AcceptOp(",");
                        break;
                    }
                    ExprAst key;
                    if (Cur.Kind == TokenKind.Name && !(Cur.Text is "None" or "True" or "False"))
                    {
                        var kt = Cur;
                        key = At(new NameAst { Id = ExpectName() }, kt);
                        while (AcceptOp(".")) key = At(new AttributeAst { Value = key, Attr = ExpectName() }, kt);
                        if (key is NameAst) throw Err("mapping pattern keys may only match literals and attribute lookups", kt);
                    }
                    else if (Cur.Kind == TokenKind.Name)
                    {
                        var kt = Next();
                        key = At(new ConstantAst { Value = kt.Text == "None" ? Py.None : kt.Text == "True" ? PyBool.True : PyBool.False }, kt);
                    }
                    else key = ParseLiteralPatternValue();
                    ExpectOp(":");
                    map.Keys.Add(key);
                    map.Patterns.Add(ParsePattern());
                    if (!AcceptOp(",")) break;
                }
                ExpectOp("}");
                return map;
            }
            throw Err("invalid syntax");
        }
        finally
        {
            Leave();
        }
    }

    private PatternAst ParseClassPattern(ExprAst cls, Token tok)
    {
        Next();
        var cp = At(new MatchClassPattern { Cls = cls }, tok);
        while (!IsOp(")"))
        {
            if (Cur.Kind == TokenKind.Name && PeekTok().Kind == TokenKind.Op && PeekTok().Text == "=")
            {
                var name = ExpectName();
                Next();
                cp.KwdAttrs.Add(name);
                cp.KwdPatterns.Add(ParsePattern());
            }
            else
            {
                if (cp.KwdAttrs.Count > 0) throw Err("positional patterns follow keyword patterns");
                cp.Patterns.Add(ParsePattern());
            }
            if (!AcceptOp(",")) break;
        }
        ExpectOp(")");
        return cp;
    }

    private ExprAst ParseLiteralPatternValue()
    {
        var tok = Cur;
        if (tok.Kind == TokenKind.String) return ParseStrings();
        bool neg = AcceptOp("-");
        if (Cur.Kind != TokenKind.Number) throw Err("invalid syntax");
        var v = (PyObject)Next().Value!;
        if (neg) v = Arith.Neg(v);
        if (IsOp("+") || IsOp("-"))
        {
            bool plus = Next().Text == "+";
            if (Cur.Kind != TokenKind.Number || Cur.Value is not PyComplex) throw Err("imaginary number required in complex literal");
            var im = (PyComplex)Next().Value!;
            double re = Ops.ToDouble(v);
            v = new PyComplex(re, plus ? im.Imag : -im.Imag);
        }
        return At(new ConstantAst { Value = v }, tok);
    }
}
