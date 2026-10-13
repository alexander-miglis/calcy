using System.Collections.Immutable;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Parsing;

/// <summary>What the parser needs to know about names (functions, defined variables) and implied multiplication.</summary>
public interface IParseContext
{
    bool IsFunction(string name);
    bool IsKnownName(string name);
    ImplicitMultiplication Implicit { get; }
    bool SplitUnknownNames { get; }
}

/// <summary>A parse context that knows nothing beyond a fixed set of function names.</summary>
public sealed class SimpleParseContext(IEnumerable<string>? functions = null, ImplicitMultiplication implicitMode = ImplicitMultiplication.BindsTighter, bool split = true) : IParseContext
{
    private readonly HashSet<string> _functions = functions is null ? [] : [.. functions];
    public HashSet<string> KnownNames { get; } = [];
    public bool IsFunction(string name) => _functions.Contains(name);
    public bool IsKnownName(string name) => KnownNames.Contains(name);
    public ImplicitMultiplication Implicit { get; } = implicitMode;
    public bool SplitUnknownNames { get; } = split;
}

public abstract record Statement(string Source);

public sealed record ExprStatement(Expr Expression, string Source) : Statement(Source);

public sealed record AssignStatement(string Name, Expr Value, string Source) : Statement(Source);

public sealed record FunctionDefStatement(string Name, ImmutableArray<string> Parameters, Expr Body, string Source) : Statement(Source);

/// <summary>Recursive-descent parser for calculator input.</summary>
public sealed class Parser
{
    private static readonly Dictionary<string, string> Greek = new(StringComparer.Ordinal)
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["zeta"] = "ζ",
        ["eta"] = "η", ["theta"] = "θ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
        ["nu"] = "ν", ["xi"] = "ξ", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ",
        ["phi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Sigma"] = "Σ",
        ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
    };

    private static readonly Dictionary<string, string> Inverse = new(StringComparer.Ordinal)
    {
        ["sin"] = "asin", ["cos"] = "acos", ["tan"] = "atan", ["sec"] = "asec", ["csc"] = "acsc", ["cot"] = "acot",
        ["sinh"] = "asinh", ["cosh"] = "acosh", ["tanh"] = "atanh",
    };

    private static readonly HashSet<string> Keywords = ["and", "or", "not", "xor", "mod"];

    private readonly List<Token> _tokens;
    private readonly IParseContext _ctx;
    private readonly string _text;
    private int _pos;
    private int _absDepth;
    private int _braceDepth;
    private readonly HashSet<string> _extraFunctions = [];

    private bool IsFunctionName(string name) => _extraFunctions.Contains(name) || _ctx.IsFunction(name);

    private Parser(string text, IParseContext ctx)
    {
        _text = text;
        _ctx = ctx;
        _tokens = Lexer.Tokenize(text);
    }

    /// <summary>Typed Greek names ("theta") and the letters they become ("θ"), for editors and symbol pickers.</summary>
    public static IReadOnlyDictionary<string, string> GreekNames => Greek;

    /// <summary>Maps a typed name such as "theta" to the symbol used internally ("θ").</summary>
    public static string CanonicalName(string name) => Greek.TryGetValue(name, out var g) ? g : name;

    public static Expr ParseExpression(string text, IParseContext ctx)
    {
        var p = new Parser(text, ctx);
        if (p.Peek.Kind == TokenKind.End) throw new ParseException("The input is empty", 0, 0);
        var e = p.ParseExpr();
        p.ExpectEnd();
        return e;
    }

    public static Statement ParseStatement(string text, IParseContext ctx)
    {
        var p = new Parser(text, ctx);
        if (p.Peek.Kind == TokenKind.End) throw new ParseException("The input is empty", 0, 0);
        return p.ParseStatementCore();
    }

    private Statement ParseStatementCore()
    {
        // f(x, y) = body  or  f(x) := body
        if (Peek.Kind == TokenKind.Identifier && PeekAt(1).Kind == TokenKind.LParen && !Keywords.Contains(Peek.Text))
        {
            int j = 2;
            var parameters = new List<string>();
            bool ok = true;
            while (true)
            {
                var t = PeekAt(j);
                if (t.Kind == TokenKind.Identifier && !Keywords.Contains(t.Text))
                {
                    parameters.Add(CanonicalName(t.Text));
                    j++;
                    if (PeekAt(j).Kind == TokenKind.Comma) { j++; continue; }
                    if (PeekAt(j).Kind == TokenKind.RParen) break;
                }
                ok = false;
                break;
            }
            if (ok && parameters.Count > 0 && parameters.Distinct().Count() == parameters.Count)
            {
                var after = PeekAt(j + 1);
                if (after.Kind == TokenKind.Assign || (after.Kind == TokenKind.Equal && after.Text == "="))
                {
                    var name = CanonicalName(Peek.Text);
                    if (IsReserved(name)) throw new ParseException($"'{name}' is a built-in name and cannot be redefined", Peek.Position, Peek.Length);
                    _pos += j + 2;
                    _extraFunctions.Add(name);
                    var body = ParseExpr();
                    ExpectEnd();
                    return new FunctionDefStatement(name, [.. parameters], body, _text);
                }
            }
        }
        // name = value  or  name := value
        if (Peek.Kind == TokenKind.Identifier && !Keywords.Contains(Peek.Text))
        {
            var next = PeekAt(1);
            if (next.Kind == TokenKind.Assign || (next.Kind == TokenKind.Equal && next.Text == "="))
            {
                var name = CanonicalName(Peek.Text);
                // Single letters such as C, P or N are shorthand for built-ins, but people use them as variables far more often.
                if (!IsReserved(name) && (!_ctx.IsFunction(name) || name.Length == 1))
                {
                    _pos += 2;
                    var value = ParseExpr();
                    ExpectEnd();
                    return new AssignStatement(name, value, _text);
                }
            }
        }
        var e = ParseExpr();
        // value -> name (TI store arrow)
        if (Peek.Kind == TokenKind.Arrow)
        {
            Next();
            var target = Expect(TokenKind.Identifier, "Expected a variable name after the store arrow");
            ExpectEnd();
            var name = CanonicalName(target.Text);
            if (IsReserved(name)) throw new ParseException($"'{name}' is a built-in name and cannot be redefined", target.Position, target.Length);
            return new AssignStatement(name, e, _text);
        }
        ExpectEnd();
        return new ExprStatement(e, _text);
    }

    private static bool IsReserved(string name) => name is "pi" or "π" or "e" or "i" or "inf" or "infinity" or "true" or "false" or "ans";

    // ------------------------------------------------------------------ token helpers

    private Token Peek => _tokens[_pos];
    private Token PeekAt(int k) => _tokens[Math.Min(_pos + k, _tokens.Count - 1)];
    private Token Next() => _tokens[_pos++];

    private bool PeekIdent(string word) => Peek.Kind == TokenKind.Identifier && Peek.Text == word;

    private Token Expect(TokenKind kind, string message)
    {
        if (Peek.Kind != kind) throw Error(message);
        return Next();
    }

    private void ExpectEnd()
    {
        if (Peek.Kind == TokenKind.End) return;
        var t = Peek;
        string msg = t.Kind switch
        {
            TokenKind.RParen => "There is a ')' without a matching '('",
            TokenKind.RBracket => "There is a ']' without a matching '['",
            TokenKind.RBrace => "There is a '}' without a matching '{'",
            TokenKind.Equal or TokenKind.Assign => "Only a name (like a or f(x)) can be on the left of a definition",
            _ => $"Unexpected '{t.Text}'",
        };
        throw new ParseException(msg, t.Position, Math.Max(1, t.Length));
    }

    private ParseException Error(string message)
    {
        var t = Peek;
        if (t.Kind == TokenKind.End) return new ParseException(message + " (the input ended too early)", t.Position, 0) { };
        return new ParseException(message, t.Position, Math.Max(1, t.Length));
    }

    // ------------------------------------------------------------------ grammar

    private Expr ParseExpr()
    {
        // x => body (single-parameter lambda)
        if (Peek.Kind == TokenKind.Identifier && PeekAt(1).Kind == TokenKind.FatArrow)
        {
            var name = CanonicalName(Next().Text);
            Next();
            return new LambdaExpr([name], ParseExpr());
        }
        return ParseOr();
    }

    private Expr ParseOr()
    {
        var left = ParseAnd();
        while (PeekIdent("or") || PeekIdent("xor"))
        {
            var op = Next().Text == "or" ? LogicOp.Or : LogicOp.Xor;
            var right = ParseAnd();
            left = new LogicExpr(op, [left, right]);
        }
        return left;
    }

    private Expr ParseAnd()
    {
        var left = ParseNot();
        while (PeekIdent("and"))
        {
            Next();
            var right = ParseNot();
            left = new LogicExpr(LogicOp.And, [left, right]);
        }
        return left;
    }

    private Expr ParseNot()
    {
        if (PeekIdent("not"))
        {
            Next();
            return new LogicExpr(LogicOp.Not, [ParseNot()]);
        }
        return ParseRelation();
    }

    private static RelOp? RelOf(TokenKind k) => k switch
    {
        TokenKind.Equal => RelOp.Equal,
        TokenKind.NotEqual => RelOp.NotEqual,
        TokenKind.Less => RelOp.Less,
        TokenKind.LessEqual => RelOp.LessEqual,
        TokenKind.Greater => RelOp.Greater,
        TokenKind.GreaterEqual => RelOp.GreaterEqual,
        _ => null,
    };

    private Expr ParseRelation()
    {
        var first = ParseRange();
        var ops = new List<RelOp>();
        var operands = new List<Expr> { first };
        while (RelOf(Peek.Kind) is { } op)
        {
            Next();
            ops.Add(op);
            operands.Add(ParseRange());
        }
        if (ops.Count == 0) return first;
        if (ops.Count == 1) return new RelationExpr(ops[0], operands[0], operands[1]);
        var parts = new List<Expr>();
        for (int k = 0; k < ops.Count; k++) parts.Add(new RelationExpr(ops[k], operands[k], operands[k + 1]));
        return new LogicExpr(LogicOp.And, [.. parts]);
    }

    private Expr ParseRange()
    {
        var left = ParseAdditive();
        if (Peek.Kind == TokenKind.Range)
        {
            Next();
            var right = ParseAdditive();
            return CallExpr.Of("range", left, right);
        }
        return left;
    }

    private Expr ParseAdditive()
    {
        var left = ParseTerm();
        while (Peek.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            bool plus = Next().Kind == TokenKind.Plus;
            var right = ParseTerm();
            left = plus ? Raw.Add(left, right) : Raw.Add(left, Raw.Neg(right));
        }
        // Domain restriction: x^2 {0 < x < 5}
        if (Peek.Kind == TokenKind.LBrace && IsRestrictionAhead())
        {
            Next();
            var cond = ParseExpr();
            while (Peek.Kind == TokenKind.Comma)
            {
                Next();
                cond = new LogicExpr(LogicOp.And, [cond, ParseExpr()]);
            }
            Expect(TokenKind.RBrace, "Expected '}' to close the restriction");
            left = new PiecewiseExpr([(cond, left)], null);
        }
        return left;
    }

    private Expr ParseTerm()
    {
        var left = ParseImplicit();
        while (true)
        {
            if (Peek.Kind == TokenKind.Star)
            {
                Next();
                left = Raw.Mul(left, ParseImplicit());
            }
            else if (Peek.Kind == TokenKind.Slash)
            {
                Next();
                left = Raw.Div(left, ParseImplicit());
            }
            else if (PeekIdent("mod"))
            {
                Next();
                left = CallExpr.Of("mod", left, ParseImplicit());
            }
            else if (_ctx.Implicit == ImplicitMultiplication.LeftToRight && CanStartJuxtaposed(left))
            {
                left = Raw.Mul(left, ParsePower());
            }
            else break;
        }
        return left;
    }

    /// <summary>Juxtaposed factors (2x, 3sin(x), (a)(b)). In Casio mode they bind tighter than * and /.</summary>
    private Expr ParseImplicit()
    {
        var left = ParseUnary();
        if (_ctx.Implicit == ImplicitMultiplication.LeftToRight) return left;
        while (CanStartJuxtaposed(left))
        {
            var right = ParsePower();
            left = Raw.Mul(left, right);
        }
        return left;
    }

    private bool CanStartJuxtaposed(Expr previous)
    {
        var t = Peek;
        switch (t.Kind)
        {
            case TokenKind.Number:
                if (EndsWithNumber(previous))
                    throw new ParseException("Two numbers are next to each other: add an operator between them", t.Position, t.Length);
                return true;
            case TokenKind.Identifier:
                return !Keywords.Contains(t.Text);
            case TokenKind.LParen or TokenKind.Sqrt or TokenKind.Cbrt or TokenKind.Fourth or TokenKind.Infinity:
                return true;
            case TokenKind.Bar:
                return _absDepth == 0;
            case TokenKind.LBracket:
                return t.SpaceBefore || previous is NumberExpr;
            case TokenKind.LBrace:
                return !IsRestrictionAhead();
            default:
                return false;
        }
    }

    private static bool EndsWithNumber(Expr e) => e switch
    {
        NumberExpr => true,
        MulExpr m => EndsWithNumber(m.Factors[^1]),
        _ => false,
    };

    private Expr ParseUnary()
    {
        switch (Peek.Kind)
        {
            case TokenKind.Minus:
                Next();
                return Raw.Neg(ParseUnary());
            case TokenKind.Plus:
                Next();
                return ParseUnary();
            case TokenKind.Sqrt:
                Next();
                return CallExpr.Of("sqrt", ParseUnary());
            case TokenKind.Cbrt:
                Next();
                return CallExpr.Of("cbrt", ParseUnary());
            case TokenKind.Fourth:
                Next();
                return CallExpr.Of("root", ParseUnary(), Expr.Int(4));
            default:
                return ParsePower();
        }
    }

    private Expr ParsePower()
    {
        var b = ParsePostfix();
        if (Peek.Kind == TokenKind.Caret)
        {
            int caretAt = _pos;
            Next();
            var e = ParseUnary();
            // A split name like πr or ab directly followed by ^: the power belongs to the last letter only
            // (πr^2 is π·r², as in Desmos). (ab)^2 still squares the whole bracket.
            if (_lastSplit is { } split && ReferenceEquals(split.Product, b) && split.NextToken == caretAt)
            {
                var f = split.Factors;
                Expr product = new PowExpr(f[^1], e);
                if (f.Count > 1)
                {
                    Expr head = f[0];
                    for (int k = 1; k < f.Count - 1; k++) head = Raw.Mul(head, f[k]);
                    product = Raw.Mul(head, product);
                }
                return product;
            }
            return new PowExpr(b, e);
        }
        return b;
    }

    private Expr ParsePostfix()
    {
        var e = ParsePrimary();
        while (true)
        {
            switch (Peek.Kind)
            {
                case TokenKind.Bang:
                    e = PostfixOp(e, "factorial");
                    continue;
                case TokenKind.DoubleBang:
                    e = PostfixOp(e, "doublefactorial");
                    continue;
                case TokenKind.Percent:
                    e = PostfixOp(e, "percent");
                    continue;
                case TokenKind.Degree:
                    e = PostfixOp(e, "deg");
                    continue;
                case TokenKind.Transpose:
                    e = PostfixOp(e, "transpose");
                    continue;
                case TokenKind.LBracket when !Peek.SpaceBefore && e is SymbolExpr or IndexExpr or CallExpr or ListExpr or MatrixExpr:
                {
                    Next();
                    var idx = new List<Expr> { ParseExpr() };
                    while (Peek.Kind == TokenKind.Comma)
                    {
                        Next();
                        idx.Add(ParseExpr());
                    }
                    Expect(TokenKind.RBracket, "Expected ']' to close the index");
                    e = new IndexExpr(e, [.. idx]);
                    continue;
                }
            }
            return e;
        }
    }

    /// <summary>
    /// Applies a postfix sign (!, %, °, ᵀ). Right after a split name it belongs to the last letter only, like a power:
    /// ABᵀ is A·Bᵀ and n·k! is n·(k!).
    /// </summary>
    private Expr PostfixOp(Expr e, string function)
    {
        int at = _pos;
        Next();
        if (_lastSplit is { } split && ReferenceEquals(split.Product, e) && split.NextToken == at)
        {
            var factors = new List<Expr>(split.Factors);
            factors[^1] = CallExpr.Of(function, factors[^1]);
            Expr product = factors[0];
            for (int k = 1; k < factors.Count; k++) product = Raw.Mul(product, factors[k]);
            _lastSplit = (product, factors, _pos);
            return product;
        }
        return CallExpr.Of(function, e);
    }
    private Expr ParsePrimary()
    {
        var t = Peek;
        switch (t.Kind)
        {
            case TokenKind.Number:
                Next();
                return new NumberExpr(Number.Exact(t.Value!.Value));
            case TokenKind.String:
                Next();
                return new StringExpr(t.Text);
            case TokenKind.Infinity:
                Next();
                return ConstantExpr.Infinity;
            case TokenKind.LParen:
                return ParseParen();
            case TokenKind.LBracket:
                return ParseBracket();
            case TokenKind.LBrace:
                return ParseBrace();
            case TokenKind.Bar:
            {
                Next();
                _absDepth++;
                var inner = ParseExpr();
                _absDepth--;
                Expect(TokenKind.Bar, "Expected '|' to close the absolute value");
                return CallExpr.Of("abs", inner);
            }
            case TokenKind.Identifier:
                return ParseIdentifier();
            case TokenKind.End:
                throw Error("Something is missing here");
            case TokenKind.RParen:
                throw Error("Something is missing before ')'");
            default:
                throw Error($"'{t.Text}' cannot start an expression here");
        }
    }

    private Expr ParseParen()
    {
        var open = Next();
        if (Peek.Kind == TokenKind.RParen) throw Error("Empty brackets: put something between ( and )");
        var items = new List<Expr> { ParseExpr() };
        while (Peek.Kind == TokenKind.Comma)
        {
            Next();
            items.Add(ParseExpr());
        }
        if (Peek.Kind != TokenKind.RParen)
            throw new ParseException("A bracket is still open: add ')'", open.Position, 1) { Hint = _text + ")" };
        Next();
        if (Peek.Kind == TokenKind.FatArrow && items.All(i => i is SymbolExpr))
        {
            Next();
            var body = ParseExpr();
            return new LambdaExpr([.. items.Cast<SymbolExpr>().Select(s => s.Name)], body);
        }
        return items.Count == 1 ? items[0] : new TupleExpr([.. items]);
    }

    private Expr ParseBracket()
    {
        var open = Next();
        if (Peek.Kind == TokenKind.RBracket)
        {
            Next();
            return ListExpr.Empty;
        }
        var rows = new List<List<Expr>> { new() };
        bool sawSemicolon = false;
        while (true)
        {
            rows[^1].Add(ParseExpr());
            if (Peek.Kind == TokenKind.Comma) { Next(); continue; }
            if (Peek.Kind == TokenKind.Semicolon) { Next(); sawSemicolon = true; rows.Add([]); continue; }
            break;
        }
        if (Peek.Kind != TokenKind.RBracket) throw new ParseException("A '[' is still open: add ']'", open.Position, 1);
        Next();
        if (sawSemicolon)
        {
            if (rows.Any(r => r.Count != rows[0].Count)) throw new ParseException("Every matrix row needs the same number of entries", open.Position, 1);
            return MatrixExpr.FromRows(rows);
        }
        var items = rows[0];
        if (items.Count == 1 && items[0] is CallExpr { Name: "range" } range) return range;
        // [[1, 2], [3, 4]] is a matrix when every item is a list of the same length.
        if (items.Count > 0 && items.All(i => i is ListExpr l && l.Count > 0) && items.Select(i => ((ListExpr)i).Count).Distinct().Count() == 1)
        {
            return MatrixExpr.FromRows([.. items.Select(i => (IReadOnlyList<Expr>)((ListExpr)i).Items)]);
        }
        return new ListExpr([.. items]);
    }

    private Expr ParseBrace()
    {
        var open = Next();
        _braceDepth++;
        try
        {
            if (Peek.Kind == TokenKind.RBrace)
            {
                Next();
                return ListExpr.Empty;
            }
            var first = ParseExpr();
            if (Peek.Kind == TokenKind.Colon)
            {
                // Piecewise: {cond: value, cond: value, otherwise}
                var cases = ImmutableArray.CreateBuilder<(Expr, Expr)>();
                Expr? otherwise = null;
                var cond = first;
                while (true)
                {
                    Expect(TokenKind.Colon, "Expected ':' after the condition");
                    var value = ParseExpr();
                    cases.Add((cond, value));
                    if (Peek.Kind != TokenKind.Comma) break;
                    Next();
                    var nextItem = ParseExpr();
                    if (Peek.Kind == TokenKind.Colon)
                    {
                        cond = nextItem;
                        continue;
                    }
                    otherwise = nextItem;
                    break;
                }
                if (Peek.Kind != TokenKind.RBrace) throw new ParseException("A '{' is still open: add '}'", open.Position, 1);
                Next();
                return new PiecewiseExpr(cases.ToImmutable(), otherwise);
            }
            var items = new List<Expr> { first };
            while (Peek.Kind == TokenKind.Comma)
            {
                Next();
                items.Add(ParseExpr());
            }
            if (Peek.Kind != TokenKind.RBrace) throw new ParseException("A '{' is still open: add '}'", open.Position, 1);
            Next();
            if (items.Count == 1 && items[0] is CallExpr { Name: "range" } range) return range;
            return new ListExpr([.. items]);
        }
        finally
        {
            _braceDepth--;
        }
    }

    /// <summary>True when the brace at the cursor holds only a condition (a domain restriction like {x &gt; 0}).</summary>
    private bool IsRestrictionAhead()
    {
        if (Peek.Kind != TokenKind.LBrace) return false;
        int save = _pos;
        int abs = _absDepth;
        try
        {
            Next();
            if (Peek.Kind == TokenKind.RBrace) return false;
            var e = ParseExpr();
            while (Peek.Kind == TokenKind.Comma)
            {
                Next();
                var more = ParseExpr();
                if (!IsCondition(more)) return false;
            }
            return Peek.Kind == TokenKind.RBrace && IsCondition(e);
        }
        catch (ParseException)
        {
            return false;
        }
        finally
        {
            _pos = save;
            _absDepth = abs;
        }
    }

    private static bool IsCondition(Expr e) => e is RelationExpr || e is LogicExpr;

    private Expr ParseIdentifier()
    {
        var tok = Next();
        var name = tok.Text;
        switch (name)
        {
            case "true": return BoolExpr.True;
            case "false": return BoolExpr.False;
            case "pi" or "π": return ConstantExpr.Pi;
            case "inf" or "infinity" or "oo": return ConstantExpr.Infinity;
            case "undefined": return ConstantExpr.Undefined;
        }
        if (name == "mod" && Peek.Kind == TokenKind.LParen) return ParseFunctionCall(name, tok);
        if (Keywords.Contains(name)) throw new ParseException($"'{name}' needs something on its left", tok.Position, tok.Length);
        // gamma(0.5) and beta(2, 3) are functions; gamma on its own is the Greek letter.
        if (Greek.ContainsKey(name) && Peek.Kind == TokenKind.LParen && IsFunctionName(name)) return ParseFunctionCall(name, tok);
        name = CanonicalName(name);

        if (IsFunctionName(name)) return ParseFunctionCall(name, tok);

        if (_ctx.IsKnownName(name) || name.Length == 1 || !_ctx.SplitUnknownNames || name.Any(c => char.IsDigit(c) || c == '_') || IsReserved(name))
            return new SymbolExpr(name);

        // A longer unknown name right before a bracket is almost always a misspelled or missing function, not a product:
        // "intgrate(x)" should say so instead of multiplying i, n, t, g, ... together.
        if (name.Length >= 4 && Peek.Kind == TokenKind.LParen && !Peek.SpaceBefore)
        {
            var suggestion = SuggestFunction(name);
            throw new ParseException(suggestion is null
                    ? $"There is no function called '{name}'"
                    : $"There is no function called '{name}'. Did you mean {suggestion}?", tok.Position, tok.Length)
            {
                Hint = suggestion is null ? null : _text[..tok.Position] + suggestion + _text[(tok.Position + tok.Length)..],
            };
        }

        // Unknown multi-letter name: ab means a times b (Desmos style). Greek names were mapped above.
        var factors = name.Select(c => (Expr)new SymbolExpr(c.ToString())).ToList();
        Expr product = factors[0];
        for (int k = 1; k < factors.Count; k++) product = Raw.Mul(product, factors[k]);
        _lastSplit = (product, factors, _pos);
        return product;
    }

    /// <summary>The closest known function name (at most two edits away), when the context can list them.</summary>
    private string? SuggestFunction(string name)
    {
        if (_ctx is not CalcSession session) return null;
        string? best = null;
        int bestDistance = 3;
        foreach (var f in session.Functions.All)
        {
            if (f.Hidden) continue;
            foreach (var candidate in f.Aliases.Prepend(f.Name))
            {
                if (Math.Abs(candidate.Length - name.Length) >= bestDistance) continue;
                int d = EditDistance(name.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }
        }
        return best;
    }

    private static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>The most recent split name, so a following ^ can attach to its last letter.</summary>
    private (Expr Product, List<Expr> Factors, int NextToken)? _lastSplit;

    private Expr ParseFunctionCall(string name, Token tok)
    {
        int primes = 0;
        while (Peek.Kind == TokenKind.Prime)
        {
            Next();
            primes++;
        }
        // sin^2 x, sin²(x), sin^-1(x)
        Expr? power = null;
        if (Peek.Kind == TokenKind.Caret && (PeekAt(1).Kind == TokenKind.Number || (PeekAt(1).Kind == TokenKind.Minus && PeekAt(2).Kind == TokenKind.Number)))
        {
            Next();
            bool negative = false;
            if (Peek.Kind == TokenKind.Minus) { Next(); negative = true; }
            var n = Next();
            var value = n.Value!.Value;
            if (negative) value = -value;
            if (value == BigRational.MinusOne && Inverse.TryGetValue(name, out var inv) && primes == 0) name = inv;
            else power = new NumberExpr(Number.Exact(value));
        }

        List<Expr> args;
        if (Peek.Kind == TokenKind.LParen)
        {
            var open = Next();
            args = [];
            if (Peek.Kind != TokenKind.RParen)
            {
                args.Add(ParseExpr());
                while (Peek.Kind == TokenKind.Comma)
                {
                    Next();
                    args.Add(ParseExpr());
                }
            }
            if (Peek.Kind != TokenKind.RParen)
                throw new ParseException($"The bracket after {name} is still open: add ')'", open.Position, 1) { Hint = _text + ")" };
            Next();
        }
        else if (StartsArgument())
        {
            args = [ParseFunctionArgument()];
        }
        else
        {
            // A bare function name (for example map(sin, L)).
            if (primes > 0 || power is not null) throw new ParseException($"{name} needs an argument", tok.Position, tok.Length);
            return new SymbolExpr(name);
        }

        Expr call = primes > 0
            ? new CallExpr("fderiv", [new SymbolExpr(name), Expr.Int(primes), .. args])
            : new CallExpr(name, [.. args]);
        return power is null ? call : new PowExpr(call, power);
    }

    private bool StartsArgument()
    {
        var t = Peek;
        return t.Kind switch
        {
            TokenKind.Number or TokenKind.LBracket or TokenKind.LBrace or TokenKind.Sqrt or TokenKind.Cbrt or TokenKind.Infinity => true,
            TokenKind.Minus => true,
            TokenKind.Bar => _absDepth == 0,
            TokenKind.Identifier => !Keywords.Contains(t.Text),
            _ => false,
        };
    }

    /// <summary>Argument of a function written without brackets: sin 2x is sin(2x), sin x cos x is sin(x)·cos(x).</summary>
    private Expr ParseFunctionArgument()
    {
        var arg = ParseUnary();
        while (Peek.Kind is TokenKind.Number or TokenKind.Identifier or TokenKind.LParen or TokenKind.Sqrt or TokenKind.Infinity)
        {
            if (Peek.Kind == TokenKind.Identifier && (Keywords.Contains(Peek.Text) || IsFunctionName(CanonicalName(Peek.Text)))) break;
            if (Peek.Kind == TokenKind.Number && EndsWithNumber(arg)) break;
            arg = Raw.Mul(arg, ParsePower());
        }
        return arg;
    }
}

/// <summary>Builds unsimplified nodes; the evaluator simplifies them.</summary>
internal static class Raw
{
    public static Expr Add(Expr a, Expr b)
    {
        var items = new List<Expr>();
        if (a is AddExpr aa) items.AddRange(aa.Terms); else items.Add(a);
        items.Add(b);
        return new AddExpr([.. items]);
    }

    public static Expr Mul(Expr a, Expr b)
    {
        var items = new List<Expr>();
        if (a is MulExpr ma) items.AddRange(ma.Factors); else items.Add(a);
        items.Add(b);
        return new MulExpr([.. items]);
    }

    public static Expr Neg(Expr a) => a is NumberExpr n ? new NumberExpr(-n.Value) : new MulExpr([Expr.MinusOne, a]);

    public static Expr Div(Expr a, Expr b) => Mul(a, new PowExpr(b, Expr.MinusOne));
}
