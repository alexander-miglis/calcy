using System.Globalization;
using System.Text;

namespace Calcy.Formulas.Tests.Evaluation;

/// <summary>
/// A tiny evaluator for the restricted Calcy formula syntax used by the formula data:
/// numbers, identifiers (ASCII or Greek letters with an optional underscore subscript), + - * / ^,
/// unary minus, postfix ! (factorial), parentheses, the functions sqrt, root, abs, exp, ln, log,
/// sin, cos, tan, asin, acos, atan, sinh, cosh, tanh, and the constants pi and e.
/// Implicit multiplication is deliberately not supported, so the data must use explicit <c>*</c>.
/// </summary>
public abstract class Expression
{
    /// <summary>Function names with their allowed argument counts.</summary>
    public static readonly IReadOnlyDictionary<string, int[]> Functions = new Dictionary<string, int[]>(StringComparer.Ordinal)
    {
        ["sqrt"] = [1], ["root"] = [2], ["abs"] = [1], ["exp"] = [1], ["ln"] = [1], ["log"] = [1, 2],
        ["sin"] = [1], ["cos"] = [1], ["tan"] = [1], ["asin"] = [1], ["acos"] = [1], ["atan"] = [1],
        ["sinh"] = [1], ["cosh"] = [1], ["tanh"] = [1],
    };

    /// <summary>Built-in constant names.</summary>
    public static readonly IReadOnlySet<string> BuiltInConstants = new HashSet<string>(StringComparer.Ordinal) { "pi", "e" };

    public abstract double Evaluate(IReadOnlyDictionary<string, double> values);

    /// <summary>Free identifiers (variables and constants), excluding functions and built-in constants.</summary>
    public IReadOnlySet<string> Identifiers()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Collect(set);
        return set;
    }

    protected abstract void Collect(HashSet<string> identifiers);

    public static Expression Parse(string text) => new Parser(text).ParseAll();

    /// <summary>Parses "left = right" (exactly one equals sign).</summary>
    public static (Expression Left, Expression Right) ParseEquation(string equation)
    {
        int first = equation.IndexOf('=');
        if (first < 0 || equation.IndexOf('=', first + 1) >= 0)
            throw new FormatException($"An equation needs exactly one '=': {equation}");
        return (Parse(equation[..first]), Parse(equation[(first + 1)..]));
    }

    // ---------- nodes ----------

    private sealed class Number(double value) : Expression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> values) => value;
        protected override void Collect(HashSet<string> identifiers) { }
    }

    private sealed class Identifier(string name) : Expression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> values)
        {
            if (values.TryGetValue(name, out double v)) return v;
            return name switch
            {
                "pi" => Math.PI,
                "e" => Math.E,
                _ => throw new KeyNotFoundException($"No value for '{name}'."),
            };
        }

        protected override void Collect(HashSet<string> identifiers)
        {
            if (!BuiltInConstants.Contains(name)) identifiers.Add(name);
        }
    }

    private sealed class Negate(Expression operand) : Expression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> values) => -operand.Evaluate(values);
        protected override void Collect(HashSet<string> identifiers) => operand.Collect(identifiers);
    }

    private sealed class Factorial(Expression operand) : Expression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> values) => SpecialFunctions.Factorial(operand.Evaluate(values));
        protected override void Collect(HashSet<string> identifiers) => operand.Collect(identifiers);
    }

    private sealed class Binary(char op, Expression left, Expression right) : Expression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> values)
        {
            double a = left.Evaluate(values), b = right.Evaluate(values);
            return op switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => a / b,
                '^' => SpecialFunctions.Power(a, b),
                _ => throw new InvalidOperationException($"Unknown operator {op}"),
            };
        }

        protected override void Collect(HashSet<string> identifiers)
        {
            left.Collect(identifiers);
            right.Collect(identifiers);
        }
    }

    private sealed class Call(string name, Expression[] args) : Expression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> values)
        {
            double x = args[0].Evaluate(values);
            return name switch
            {
                "sqrt" => Math.Sqrt(x),
                "root" => SpecialFunctions.Root(x, args[1].Evaluate(values)),
                "abs" => Math.Abs(x),
                "exp" => Math.Exp(x),
                "ln" => Math.Log(x),
                "log" => args.Length == 1 ? Math.Log10(x) : Math.Log(x) / Math.Log(args[1].Evaluate(values)),
                "sin" => Math.Sin(x),
                "cos" => Math.Cos(x),
                "tan" => Math.Tan(x),
                "asin" => Math.Asin(x),
                "acos" => Math.Acos(x),
                "atan" => Math.Atan(x),
                "sinh" => Math.Sinh(x),
                "cosh" => Math.Cosh(x),
                "tanh" => Math.Tanh(x),
                _ => throw new InvalidOperationException($"Unknown function {name}"),
            };
        }

        protected override void Collect(HashSet<string> identifiers)
        {
            foreach (var a in args) a.Collect(identifiers);
        }
    }

    // ---------- tokenizer and recursive descent parser ----------

    private enum TokenKind { Number, Identifier, Operator, End }

    private readonly record struct Token(TokenKind Kind, string Text, double Value, int Position);

    private sealed class Parser
    {
        private readonly string _text;
        private readonly List<Token> _tokens;
        private int _pos;

        public Parser(string text)
        {
            _text = text;
            _tokens = Tokenize(text);
        }

        public Expression ParseAll()
        {
            var e = ParseSum();
            if (Peek.Kind != TokenKind.End) throw Error($"Unexpected '{Peek.Text}'");
            return e;
        }

        private Token Peek => _tokens[_pos];

        private bool IsOp(string op) => Peek.Kind == TokenKind.Operator && Peek.Text == op;

        private FormatException Error(string message) =>
            new($"{message} at position {Peek.Position} in \"{_text}\"");

        private void Expect(string op)
        {
            if (!IsOp(op)) throw Error($"Expected '{op}'");
            _pos++;
        }

        private Expression ParseSum()
        {
            var left = ParseProduct();
            while (IsOp("+") || IsOp("-"))
            {
                char op = Peek.Text[0];
                _pos++;
                left = new Binary(op, left, ParseProduct());
            }
            return left;
        }

        private Expression ParseProduct()
        {
            var left = ParseUnary();
            while (IsOp("*") || IsOp("/"))
            {
                char op = Peek.Text[0];
                _pos++;
                left = new Binary(op, left, ParseUnary());
            }
            return left;
        }

        private Expression ParseUnary()
        {
            if (IsOp("-")) { _pos++; return new Negate(ParseUnary()); }
            if (IsOp("+")) { _pos++; return ParseUnary(); }
            return ParsePower();
        }

        private Expression ParsePower()
        {
            var basis = ParsePostfix();
            if (IsOp("^"))
            {
                _pos++;
                return new Binary('^', basis, ParseUnary()); // right associative, allows 2^-3
            }
            return basis;
        }

        private Expression ParsePostfix()
        {
            var e = ParsePrimary();
            while (IsOp("!"))
            {
                _pos++;
                e = new Factorial(e);
            }
            return e;
        }

        private Expression ParsePrimary()
        {
            var t = Peek;
            switch (t.Kind)
            {
                case TokenKind.Number:
                    _pos++;
                    return new Number(t.Value);
                case TokenKind.Identifier:
                    _pos++;
                    if (Functions.TryGetValue(t.Text, out var arities))
                    {
                        Expect("(");
                        var args = new List<Expression> { ParseSum() };
                        while (IsOp(",")) { _pos++; args.Add(ParseSum()); }
                        Expect(")");
                        if (!arities.Contains(args.Count))
                            throw Error($"Function {t.Text} does not take {args.Count} argument(s)");
                        return new Call(t.Text, args.ToArray());
                    }
                    if (IsOp("(")) throw Error($"'{t.Text}' is not a known function (use explicit * for multiplication)");
                    return new Identifier(t.Text);
                case TokenKind.Operator when t.Text == "(":
                    _pos++;
                    var inner = ParseSum();
                    Expect(")");
                    return inner;
                default:
                    throw Error(t.Kind == TokenKind.End ? "Unexpected end of expression" : $"Unexpected '{t.Text}'");
            }
        }

        private static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                if (char.IsWhiteSpace(ch)) { i++; continue; }
                int start = i;
                if (char.IsAsciiDigit(ch) || (ch == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
                {
                    while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                    if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
                    if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                    {
                        int j = i + 1;
                        if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
                        if (j < s.Length && char.IsAsciiDigit(s[j]))
                        {
                            i = j;
                            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                        }
                    }
                    string text = s[start..i];
                    tokens.Add(new Token(TokenKind.Number, text, double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), start));
                    continue;
                }
                if (char.IsLetter(ch))
                {
                    var sb = new StringBuilder();
                    while (i < s.Length && char.IsLetterOrDigit(s[i])) sb.Append(s[i++]);
                    if (i + 1 < s.Length && s[i] == '_' && char.IsLetterOrDigit(s[i + 1]))
                    {
                        sb.Append(s[i++]);
                        while (i < s.Length && char.IsLetterOrDigit(s[i])) sb.Append(s[i++]);
                    }
                    tokens.Add(new Token(TokenKind.Identifier, sb.ToString(), 0, start));
                    continue;
                }
                if ("+-*/^(),!".IndexOf(ch) >= 0)
                {
                    tokens.Add(new Token(TokenKind.Operator, ch.ToString(), 0, start));
                    i++;
                    continue;
                }
                throw new FormatException($"Unexpected character '{ch}' (U+{(int)ch:X4}) at position {i} in \"{s}\"");
            }
            tokens.Add(new Token(TokenKind.End, "", 0, s.Length));
            return tokens;
        }
    }
}

/// <summary>Numeric helpers for the evaluator.</summary>
public static class SpecialFunctions
{
    /// <summary>x! for whole numbers, Γ(x + 1) otherwise (Lanczos approximation).</summary>
    public static double Factorial(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == Math.Floor(x))
        {
            if (x < 0) return double.NaN;
            if (x > 170) return double.PositiveInfinity;
            double r = 1;
            for (int k = 2; k <= (int)x; k++) r *= k;
            return r;
        }
        return Gamma(x + 1);
    }

    public static double Gamma(double z)
    {
        if (z < 0.5) return Math.PI / (Math.Sin(Math.PI * z) * Gamma(1 - z));
        ReadOnlySpan<double> g =
        [
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
            1.5056327351493116e-7,
        ];
        z -= 1;
        double a = g[0];
        double t = z + 7.5;
        for (int k = 1; k < 9; k++) a += g[k] / (z + k);
        return Math.Sqrt(2 * Math.PI) * Math.Pow(t, z + 0.5) * Math.Exp(-t) * a;
    }

    /// <summary>Real power: a negative base is allowed when the exponent is a whole number.</summary>
    public static double Power(double a, double b) => Math.Pow(a, b);

    /// <summary>Real n-th root, including odd roots of negative numbers.</summary>
    public static double Root(double x, double n)
    {
        if (x >= 0) return Math.Pow(x, 1 / n);
        bool oddInteger = n == Math.Floor(n) && Math.Abs(n % 2) == 1;
        return oddInteger ? -Math.Pow(-x, 1 / n) : double.NaN;
    }
}
