using System.Numerics;

namespace Calcy.Modes.Programmer;

/// <summary>Supplies variable values (ans, names defined with x = 5) and earlier card results (#3) to the evaluator.</summary>
public interface IVariableScope
{
    /// <summary>Looks up a variable by name (case-insensitive).</summary>
    bool TryGetVariable(string name, out BigInteger value);

    /// <summary>Looks up the result of card number <paramref name="number"/>.</summary>
    bool TryGetCard(int number, out BigInteger value);
}

/// <summary>A simple dictionary-backed scope.</summary>
public sealed class VariableScope : IVariableScope
{
    private readonly Dictionary<string, BigInteger> _vars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, BigInteger> _cards = [];

    /// <summary>Defines or replaces a variable.</summary>
    public void Set(string name, BigInteger value) => _vars[name] = value;

    /// <summary>Records a card result.</summary>
    public void SetCard(int number, BigInteger value) => _cards[number] = value;

    /// <summary>Names of the defined variables.</summary>
    public IEnumerable<string> Names => _vars.Keys;

    /// <summary>Removes every variable and card.</summary>
    public void Clear()
    {
        _vars.Clear();
        _cards.Clear();
    }

    /// <inheritdoc />
    public bool TryGetVariable(string name, out BigInteger value) => _vars.TryGetValue(name, out value);

    /// <inheritdoc />
    public bool TryGetCard(int number, out BigInteger value) => _cards.TryGetValue(number, out value);
}

/// <summary>The outcome of evaluating an expression.</summary>
/// <param name="Value">Canonical value in <paramref name="Format"/>.</param>
/// <param name="Format">The word the expression was evaluated in.</param>
/// <param name="AssignedName">The variable name when the input was an assignment such as mask = 0xF0.</param>
/// <param name="Notes">Things worth telling the user, such as "overflow wrapped".</param>
public sealed record EvaluationResult(BigInteger Value, IntegerFormat Format, string? AssignedName, IReadOnlyList<string> Notes);

/// <summary>
/// Parses and evaluates C-style integer expressions in a given word (<see cref="IntegerFormat"/>).
/// Operators and precedence follow C (from loosest to tightest): ?:, ||, &amp;&amp;, | (NOR), ^ (XNOR), &amp; (NAND),
/// == !=, &lt; &lt;= &gt; &gt;=, shifts and rotates (&lt;&lt; &gt;&gt; &gt;&gt;&gt; SAR ROL ROR), + -, * / %, then unary - + ~ !.
/// Every intermediate result lives in the word, so overflow is handled as the word's <see cref="OverflowMode"/> says.
/// </summary>
public static class ExpressionEvaluator
{
    private sealed record Fn(int Min, int Max, string Usage, Func<Run, BigInteger[], BigInteger> Body);

    private static readonly Dictionary<string, Fn> Functions = BuildFunctions();

    /// <summary>Names of the built-in functions (for help and completion).</summary>
    public static IEnumerable<string> FunctionNames => Functions.Keys;

    /// <summary>Usage text of every built-in function, such as "rol(x, n)".</summary>
    public static IEnumerable<string> FunctionUsages => Functions.Values.Select(f => f.Usage).Distinct();

    private static readonly string[] Constants = ["int_min", "int_max", "uint_max", "bits"];

    /// <summary>True when the name is reserved (function, constant, keyword or ans) and cannot be assigned.</summary>
    public static bool IsReserved(string name) =>
        Functions.ContainsKey(name.ToLowerInvariant()) || Constants.Contains(name.ToLowerInvariant()) ||
        ExpressionLexer.IsKeyword(name) || name.Equals("ans", StringComparison.OrdinalIgnoreCase);

    /// <summary>Evaluates the expression (or assignment) in the word.</summary>
    /// <exception cref="ProgrammerException">On a syntax error, unknown name, division by zero or (flag mode) overflow.</exception>
    public static EvaluationResult Evaluate(string text, IntegerFormat format, NumberBase inputBase, IVariableScope? scope = null)
    {
        var tokens = ExpressionLexer.Tokenize(text, inputBase);
        if (tokens.Count == 1) throw new ProgrammerException("Type an expression, for example 0xFF & (ans << 3)", 0, 0);
        var node = new Parser(tokens, text).ParseStatement();
        var run = new Run(format, scope);
        string? assigned = null;
        BigInteger value;
        if (node is AssignNode a)
        {
            if (IsReserved(a.Name)) throw new ProgrammerException($"'{a.Name}' is a built-in name; pick another variable name", a.Start, a.Name.Length);
            value = run.Eval(a.Value);
            assigned = a.Name;
        }
        else
        {
            value = run.Eval(node);
        }
        return new EvaluationResult(value, format, assigned, [.. run.Notes]);
    }

    /// <summary>Evaluates and returns only the value (convenience for tests and simple callers).</summary>
    public static BigInteger EvaluateValue(string text, IntegerFormat format, NumberBase inputBase = NumberBase.Dec, IVariableScope? scope = null) =>
        Evaluate(text, format, inputBase, scope).Value;

    // ---- syntax tree ----------------------------------------------------------------------------------------

    private abstract record Node(int Start, int End);
    private sealed record NumberNode(BigInteger Value, bool IsPattern, int Start, int End) : Node(Start, End);
    private sealed record NameNode(string Name, int Start, int End) : Node(Start, End);
    private sealed record CardNode(int Number, int Start, int End) : Node(Start, End);
    private sealed record UnaryNode(string Op, Node Operand, int Start, int End) : Node(Start, End);
    private sealed record BinaryNode(string Op, Node Left, Node Right, int Start, int End) : Node(Start, End);
    private sealed record ConditionalNode(Node Condition, Node Then, Node Else, int Start, int End) : Node(Start, End);
    private sealed record CallNode(string Name, IReadOnlyList<Node> Args, int Start, int End) : Node(Start, End);
    private sealed record AssignNode(string Name, Node Value, int Start, int End) : Node(Start, End);

    // ---- parser ---------------------------------------------------------------------------------------------

    private sealed class Parser(List<Token> tokens, string text)
    {
        private static readonly string[][] Levels =
        [
            ["||"],
            ["&&"],
            ["|", "nor"],
            ["^", "xnor"],
            ["&", "nand"],
            ["==", "!="],
            ["<", "<=", ">", ">="],
            ["<<", ">>", ">>>", "sar", "rol", "ror"],
            ["+", "-"],
            ["*", "/", "%"],
        ];

        private int _pos;

        private Token Current => tokens[_pos];

        private Token Take() => tokens[_pos++];

        public Node ParseStatement()
        {
            Node node;
            if (Current.Kind == TokenKind.Name && tokens[_pos + 1].Kind == TokenKind.Assign)
            {
                var name = Take();
                Take();
                var value = ParseConditional();
                node = new AssignNode(name.Text, value, name.Start, value.End);
            }
            else
            {
                node = ParseConditional();
            }
            if (Current.Kind != TokenKind.End) throw Unexpected(Current);
            return node;
        }

        private Node ParseConditional()
        {
            var cond = ParseBinary(0);
            if (Current.Kind != TokenKind.Question) return cond;
            Take();
            var then = ParseConditional();
            if (Current.Kind != TokenKind.Colon) throw new ProgrammerException("The ? needs a matching : (as in a > b ? a : b)", Current.Start, Current.Length);
            Take();
            var otherwise = ParseConditional();
            return new ConditionalNode(cond, then, otherwise, cond.Start, otherwise.End);
        }

        private Node ParseBinary(int level)
        {
            if (level == Levels.Length) return ParseUnary();
            var left = ParseBinary(level + 1);
            while (Current.Kind == TokenKind.Operator && Levels[level].Contains(Current.Op))
            {
                var op = Take();
                if (Current.Kind == TokenKind.End) throw new ProgrammerException($"Missing a number after '{op.Text}'", op.Start, op.Length);
                var right = ParseBinary(level + 1);
                left = new BinaryNode(op.Op, left, right, left.Start, right.End);
            }
            return left;
        }

        private Node ParseUnary()
        {
            if (Current.Kind == TokenKind.Operator && Current.Op is "-" or "+" or "~" or "!")
            {
                var op = Take();
                if (Current.Kind == TokenKind.End) throw new ProgrammerException($"Missing a number after '{op.Text}'", op.Start, op.Length);
                var operand = ParseUnary();
                // -128 is one literal, so it fits an 8-bit signed word without overflowing on the way.
                if (op.Op == "-" && operand is NumberNode n) return new NumberNode(-n.Value, false, op.Start, n.End);
                if (op.Op == "+") return operand;
                return new UnaryNode(op.Op, operand, op.Start, operand.End);
            }
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            var t = Current;
            switch (t.Kind)
            {
                case TokenKind.Number:
                    Take();
                    CheckNoJuxtaposition(t);
                    return new NumberNode(t.Value, t.Base != NumberBase.Dec, t.Start, t.End);
                case TokenKind.CardRef:
                    Take();
                    if (t.Value > int.MaxValue) throw new ProgrammerException($"There is no card {t.Text}", t.Start, t.Length);
                    return new CardNode((int)t.Value, t.Start, t.End);
                case TokenKind.Name:
                    Take();
                    if (Current.Kind == TokenKind.LeftParen) return ParseCall(t);
                    CheckNoJuxtaposition(t);
                    return new NameNode(t.Text, t.Start, t.End);
                case TokenKind.LeftParen:
                    {
                        Take();
                        if (Current.Kind == TokenKind.RightParen) throw new ProgrammerException("Empty parentheses: put an expression inside", t.Start, 2);
                        var inner = ParseConditional();
                        if (Current.Kind != TokenKind.RightParen) throw new ProgrammerException("Missing ')' to close the '('", t.Start, 1);
                        var close = Take();
                        return inner with { Start = t.Start, End = close.End };
                    }
                case TokenKind.Operator when Functions.ContainsKey(t.Text) && tokens[_pos + 1].Kind == TokenKind.LeftParen:
                    // Word operators double as functions: rol(x, 3), nand(a, b).
                    Take();
                    return ParseCall(t);
                case TokenKind.End:
                    throw new ProgrammerException("The expression ends too early: a number or name is missing", t.Start, 0);
                default:
                    throw Unexpected(t);
            }
        }

        private Node ParseCall(Token name)
        {
            Take(); // (
            var args = new List<Node>();
            if (Current.Kind != TokenKind.RightParen)
            {
                while (true)
                {
                    args.Add(ParseConditional());
                    if (Current.Kind == TokenKind.Comma) { Take(); continue; }
                    break;
                }
            }
            if (Current.Kind != TokenKind.RightParen) throw new ProgrammerException($"Missing ')' after the arguments of {name.Text}", Current.Start, Current.Length);
            var close = Take();
            return new CallNode(name.Text.ToLowerInvariant(), args, name.Start, close.End);
        }

        private void CheckNoJuxtaposition(Token previous)
        {
            var t = Current;
            if (t.Kind is TokenKind.Number or TokenKind.Name or TokenKind.CardRef or TokenKind.LeftParen)
                throw new ProgrammerException($"Missing an operator between '{previous.Text}' and '{text.Substring(t.Start, t.Length)}'", t.Start, t.Length);
        }

        private static ProgrammerException Unexpected(Token t) => t.Kind switch
        {
            TokenKind.RightParen => new ProgrammerException("This ')' has no matching '('", t.Start, 1),
            TokenKind.Assign => new ProgrammerException("Use == to compare; = only defines a variable at the start, as in mask = 0xF0", t.Start, 1),
            TokenKind.Colon => new ProgrammerException("This ':' has no matching '?'", t.Start, 1),
            TokenKind.Comma => new ProgrammerException("Commas only separate function arguments, as in rol(x, 3)", t.Start, 1),
            TokenKind.Operator when t.Op is not ("-" or "+" or "~" or "!") => new ProgrammerException($"'{t.Text}' needs a number on its left", t.Start, t.Length),
            _ => new ProgrammerException($"Unexpected '{t.Text}'", t.Start, t.Length),
        };
    }

    // ---- evaluation -----------------------------------------------------------------------------------------

    private sealed class Run(IntegerFormat f, IVariableScope? scope)
    {
        public IntegerFormat F { get; } = f;
        public SortedSet<string> Notes { get; } = new(StringComparer.Ordinal);

        public BigInteger Fit(BigInteger exact)
        {
            var v = IntegerOps.Fit(F, exact, out bool overflow);
            if (overflow) Notes.Add(F.Overflow == OverflowMode.Saturate ? "overflow saturated" : "overflow wrapped");
            return v;
        }

        private BigInteger Bool(bool b) => b ? F.FromPattern(BigInteger.One) : BigInteger.Zero;

        public BigInteger Eval(Node node)
        {
            try
            {
                return EvalCore(node);
            }
            catch (ProgrammerException ex) when (ex.Position < 0)
            {
                throw new ProgrammerException(ex.Message, node.Start, node.End - node.Start);
            }
        }

        private BigInteger EvalCore(Node node)
        {
            switch (node)
            {
                case NumberNode n:
                    {
                        var v = IntegerOps.Literal(F, n.Value, n.IsPattern, out bool overflow);
                        if (overflow) Notes.Add(F.Overflow == OverflowMode.Saturate ? "overflow saturated" : "overflow wrapped");
                        return v;
                    }
                case NameNode name:
                    return Lookup(name);
                case CardNode card:
                    if (scope is not null && scope.TryGetCard(card.Number, out var cv)) return IntegerOps.Convert(F, cv);
                    throw new ProgrammerException($"There is no card #{card.Number}", card.Start, card.End - card.Start);
                case UnaryNode u:
                    {
                        var x = Eval(u.Operand);
                        return u.Op switch
                        {
                            "-" => Fit(-x),
                            "~" => IntegerOps.Not(F, x),
                            _ => Bool(x.IsZero),
                        };
                    }
                case BinaryNode b:
                    return Binary(b);
                case ConditionalNode c:
                    return !Eval(c.Condition).IsZero ? Eval(c.Then) : Eval(c.Else);
                case CallNode call:
                    return Call(call);
                default:
                    throw new ProgrammerException("Cannot evaluate this", node.Start, node.End - node.Start);
            }
        }

        private BigInteger Lookup(NameNode n)
        {
            var key = n.Name.ToLowerInvariant();
            switch (key)
            {
                case "int_min": return F.FromPattern(BigInteger.One << (F.Bits - 1));
                case "int_max": return F.FromPattern((BigInteger.One << (F.Bits - 1)) - 1);
                case "uint_max": return F.FromPattern(F.Mask);
                case "bits": return Fit(F.Bits);
            }
            if (scope is not null && scope.TryGetVariable(n.Name, out var v)) return IntegerOps.Convert(F, v);
            if (Functions.ContainsKey(key)) throw new ProgrammerException($"{n.Name} is a function: write {Functions[key].Usage}", n.Start, n.End - n.Start);
            if (key == "ans") return BigInteger.Zero;
            throw new ProgrammerException($"Unknown name '{n.Name}'. Define it first, for example {n.Name} = 5", n.Start, n.End - n.Start);
        }

        private BigInteger Binary(BinaryNode b)
        {
            if (b.Op == "&&") return Bool(!Eval(b.Left).IsZero && !Eval(b.Right).IsZero);
            if (b.Op == "||") return Bool(!Eval(b.Left).IsZero || !Eval(b.Right).IsZero);
            var x = Eval(b.Left);
            var y = Eval(b.Right);
            try
            {
                return b.Op switch
                {
                    "+" => Fit(x + y),
                    "-" => Fit(x - y),
                    "*" => Fit(x * y),
                    "/" => y.IsZero ? throw new ProgrammerException("Division by zero") : Fit(BigInteger.Divide(x, y)),
                    "%" => IntegerOps.Remainder(F, x, y),
                    "&" => IntegerOps.And(F, x, y),
                    "|" => IntegerOps.Or(F, x, y),
                    "^" => IntegerOps.Xor(F, x, y),
                    "nand" => IntegerOps.Nand(F, x, y),
                    "nor" => IntegerOps.Nor(F, x, y),
                    "xnor" => IntegerOps.Xnor(F, x, y),
                    "==" => Bool(x == y),
                    "!=" => Bool(x != y),
                    "<" => Bool(x < y),
                    "<=" => Bool(x <= y),
                    ">" => Bool(x > y),
                    ">=" => Bool(x >= y),
                    "<<" => IntegerOps.ShiftLeft(F, x, y),
                    ">>" => IntegerOps.ShiftRight(F, x, y),
                    ">>>" => IntegerOps.ShiftRightLogical(F, x, y),
                    "sar" => IntegerOps.ShiftRightArithmetic(F, x, y),
                    "rol" => IntegerOps.RotateLeft(F, x, y),
                    "ror" => IntegerOps.RotateRight(F, x, y),
                    _ => throw new ProgrammerException($"Unknown operator {b.Op}"),
                };
            }
            catch (ProgrammerException ex) when (ex.Position < 0)
            {
                throw new ProgrammerException(ex.Message, b.Start, b.End - b.Start);
            }
        }

        private BigInteger Call(CallNode call)
        {
            if (!Functions.TryGetValue(call.Name, out var fn))
                throw new ProgrammerException($"Unknown function '{call.Name}'", call.Start, call.Name.Length);
            if (call.Args.Count < fn.Min || call.Args.Count > fn.Max)
            {
                string count = fn.Min == fn.Max ? $"{fn.Min}" : fn.Max == int.MaxValue ? $"{fn.Min} or more" : $"{fn.Min} to {fn.Max}";
                throw new ProgrammerException($"{call.Name} takes {count} argument{(fn.Max == 1 ? "" : "s")}: {fn.Usage}", call.Start, call.End - call.Start);
            }
            var args = call.Args.Select(Eval).ToArray();
            try
            {
                return fn.Body(this, args);
            }
            catch (ProgrammerException ex) when (ex.Position < 0)
            {
                throw new ProgrammerException($"{call.Name}: {ex.Message}", call.Start, call.End - call.Start);
            }
        }
    }

    private static Dictionary<string, Fn> BuildFunctions()
    {
        var d = new Dictionary<string, Fn>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, int min, int max, string usage, Func<Run, BigInteger[], BigInteger> body) => d[name] = new Fn(min, max, usage, body);
        static BigInteger Arg(BigInteger[] a, int i, BigInteger fallback) => i < a.Length ? a[i] : fallback;

        Add("popcount", 1, 1, "popcount(x)", (r, a) => r.Fit(IntegerOps.PopCount(r.F, a[0])));
        Add("clz", 1, 1, "clz(x)", (r, a) => r.Fit(IntegerOps.LeadingZeros(r.F, a[0])));
        Add("ctz", 1, 1, "ctz(x)", (r, a) => r.Fit(IntegerOps.TrailingZeros(r.F, a[0])));
        Add("parity", 1, 1, "parity(x)", (r, a) => r.Fit(IntegerOps.Parity(r.F, a[0])));
        Add("bitrev", 1, 1, "bitrev(x)", (r, a) => IntegerOps.BitReverse(r.F, a[0]));
        Add("bswap", 1, 1, "bswap(x)", (r, a) => IntegerOps.ByteSwap(r.F, a[0]));
        Add("sext", 2, 2, "sext(x, bits)", (r, a) => IntegerOps.SignExtend(r.F, a[0], a[1]));
        Add("zext", 2, 2, "zext(x, bits)", (r, a) => IntegerOps.ZeroExtend(r.F, a[0], a[1]));
        Add("bits", 3, 3, "bits(x, hi, lo)", (r, a) => IntegerOps.ExtractField(r.F, a[0], a[1], a[2]));
        Add("insert", 4, 4, "insert(x, hi, lo, v)", (r, a) => IntegerOps.InsertField(r.F, a[0], a[1], a[2], a[3]));
        Add("mask", 1, 2, "mask(n) or mask(hi, lo)", (r, a) => a.Length == 1 ? IntegerOps.Mask(r.F, a[0]) : IntegerOps.RangeMask(r.F, a[0], a[1]));
        Add("bit", 1, 1, "bit(n)", (r, a) => a[0].Sign < 0 || a[0] >= r.F.Bits
            ? throw new ProgrammerException($"Bit {a[0]} is outside the word (bits 0 to {r.F.Bits - 1})")
            : IntegerOps.SetBit(r.F, BigInteger.Zero, (int)a[0], true));
        Add("rol", 1, 2, "rol(x, n)", (r, a) => IntegerOps.RotateLeft(r.F, a[0], Arg(a, 1, 1)));
        Add("ror", 1, 2, "ror(x, n)", (r, a) => IntegerOps.RotateRight(r.F, a[0], Arg(a, 1, 1)));
        Add("rcl", 1, 3, "rcl(x, n, carry)", (r, a) => { bool c = !Arg(a, 2, 0).IsZero; return IntegerOps.RotateLeftThroughCarry(r.F, a[0], Arg(a, 1, 1), ref c); });
        Add("rcr", 1, 3, "rcr(x, n, carry)", (r, a) => { bool c = !Arg(a, 2, 0).IsZero; return IntegerOps.RotateRightThroughCarry(r.F, a[0], Arg(a, 1, 1), ref c); });
        Add("and", 2, int.MaxValue, "and(a, b, ...)", (r, a) => a.Skip(1).Aggregate(a[0], (x, y) => IntegerOps.And(r.F, x, y)));
        Add("or", 2, int.MaxValue, "or(a, b, ...)", (r, a) => a.Skip(1).Aggregate(a[0], (x, y) => IntegerOps.Or(r.F, x, y)));
        Add("xor", 2, int.MaxValue, "xor(a, b, ...)", (r, a) => a.Skip(1).Aggregate(a[0], (x, y) => IntegerOps.Xor(r.F, x, y)));
        Add("nand", 2, 2, "nand(a, b)", (r, a) => IntegerOps.Nand(r.F, a[0], a[1]));
        Add("nor", 2, 2, "nor(a, b)", (r, a) => IntegerOps.Nor(r.F, a[0], a[1]));
        Add("xnor", 2, 2, "xnor(a, b)", (r, a) => IntegerOps.Xnor(r.F, a[0], a[1]));
        Add("shl", 2, 2, "shl(x, n)", (r, a) => IntegerOps.ShiftLeft(r.F, a[0], a[1]));
        Add("shr", 2, 2, "shr(x, n)", (r, a) => IntegerOps.ShiftRightLogical(r.F, a[0], a[1]));
        Add("sar", 2, 2, "sar(x, n)", (r, a) => IntegerOps.ShiftRightArithmetic(r.F, a[0], a[1]));
        Add("abs", 1, 1, "abs(x)", (r, a) => r.Fit(BigInteger.Abs(a[0])));
        Add("min", 1, int.MaxValue, "min(a, b, ...)", (_, a) => a.Min());
        Add("max", 1, int.MaxValue, "max(a, b, ...)", (_, a) => a.Max());
        foreach (int w in IntegerFormat.Presets)
        {
            int width = w;
            Add($"i{w}", 1, 1, $"i{w}(x)", (r, a) => r.Fit(new IntegerFormat(width, true).Wrap(a[0])));
            Add($"u{w}", 1, 1, $"u{w}(x)", (r, a) => r.Fit(new IntegerFormat(width, false).Wrap(a[0])));
        }
        return d;
    }
}
