using System.Globalization;
using System.Numerics;
using System.Text;
using Calcy.Python.Runtime;

namespace Calcy.Python.Parsing;

internal enum TokenKind : byte
{
    EndMarker,
    Newline,
    Indent,
    Dedent,
    Name,
    Number,
    String,
    Op,
}

internal sealed class Token
{
    public TokenKind Kind;
    public string Text = "";
    public object? Value;
    public int Line;
    public int Col;
    public int EndLine;
    public int EndCol;

    public override string ToString() => $"{Kind} '{Text}' at {Line}:{Col}";
}

/// <summary>A decoded string literal: either a plain value or f-string parts.</summary>
internal sealed class StringLit
{
    public string? Value;
    public List<FPart>? FParts;
}

/// <summary>A piece of an f-string: a literal or a replacement field.</summary>
internal sealed class FPart
{
    public string? Literal;
    public string? Expr;
    public int ExprLine;
    public int ExprCol;
    public char Conversion;
    public List<FPart>? Spec;
    public string? Debug;
}

/// <summary>Syntax error raised by the lexer and parser.</summary>
internal sealed class ParseError : Exception
{
    public readonly int Line;
    public readonly int Col;
    public readonly bool Incomplete;
    public readonly bool Indentation;

    public ParseError(string message, int line, int col, bool incomplete = false, bool indentation = false) : base(message)
    {
        Line = line;
        Col = col;
        Incomplete = incomplete;
        Indentation = indentation;
    }
}

/// <summary>Tokenizer producing INDENT, DEDENT and NEWLINE tokens like CPython's tokenizer.</summary>
internal sealed class Lexer
{
    private readonly string _src;
    private int _pos;
    private int _line;
    private int _lineStart;
    private readonly int _colOffset;
    private readonly List<int> _indents = new() { 0 };
    private readonly Stack<(char ch, int line, int col)> _parens = new();
    private bool _atLineStart = true;
    private readonly List<Token> _tokens = new();

    private static readonly string[] Ops3 = { "**=", "//=", ">>=", "<<=", "..." };
    private static readonly string[] Ops2 = { "**", "//", ">>", "<<", "<=", ">=", "==", "!=", "->", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "@=", ":=" };
    private const string Ops1 = "+-*/%@&|^~<>()[]{},:.;=";

    private readonly int _firstLine;

    private Lexer(string src, int line, int colOffset)
    {
        _src = src;
        _line = line;
        _firstLine = line;
        _colOffset = colOffset;
    }

    public static List<Token> Tokenize(string src, int firstLine = 1, int colOffset = 0)
    {
        src = src.Replace("\r\n", "\n").Replace('\r', '\n');
        if (src.Length > 0 && src[0] == (char)0xFEFF) src = src.Substring(1);
        var lx = new Lexer(src, firstLine, colOffset);
        lx.Run();
        return lx._tokens;
    }

    private int Col => _pos - _lineStart + (_line == _firstLine ? _colOffset : 0);

    private char Peek(int k = 0) => _pos + k < _src.Length ? _src[_pos + k] : '\0';

    private ParseError Error(string msg, bool incomplete = false) => new(msg, _line, Col, incomplete);

    private void Add(TokenKind kind, string text, object? value, int line, int col)
    {
        _tokens.Add(new Token { Kind = kind, Text = text, Value = value, Line = line, Col = col, EndLine = _line, EndCol = Col });
    }

    private void Run()
    {
        int n = _src.Length;
        while (true)
        {
            if (_atLineStart && _parens.Count == 0)
            {
                int col = 0;
                while (_pos < n)
                {
                    char c = _src[_pos];
                    if (c == ' ') col++;
                    else if (c == '\t') col = (col / 8 + 1) * 8;
                    else if (c == '\f') col = 0;
                    else break;
                    _pos++;
                }
                if (_pos >= n) break;
                char ch = _src[_pos];
                if (ch == '#' || ch == '\n')
                {
                    while (_pos < n && _src[_pos] != '\n') _pos++;
                    if (_pos < n)
                    {
                        _pos++;
                        _line++;
                        _lineStart = _pos;
                    }
                    continue;
                }
                if (ch == '\\' && Peek(1) == '\n')
                {
                    // a continuation line starting a logical line: treat as part of it
                    _atLineStart = false;
                }
                else
                {
                    int top = _indents[^1];
                    if (col > top)
                    {
                        if (_indents.Count >= 100)
                            throw new ParseError("too many levels of indentation", _line, col, indentation: true);
                        _indents.Add(col);
                        Add(TokenKind.Indent, "", null, _line, 0);
                    }
                    else if (col < top)
                    {
                        while (col < _indents[^1])
                        {
                            _indents.RemoveAt(_indents.Count - 1);
                            Add(TokenKind.Dedent, "", null, _line, col);
                        }
                        if (col != _indents[^1])
                            throw new ParseError("unindent does not match any outer indentation level", _line, col, indentation: true);
                    }
                    _atLineStart = false;
                }
            }

            while (_pos < n && (_src[_pos] == ' ' || _src[_pos] == '\t' || _src[_pos] == '\f')) _pos++;
            if (_pos >= n) break;
            char c0 = _src[_pos];
            if (c0 == '#')
            {
                while (_pos < n && _src[_pos] != '\n') _pos++;
                continue;
            }
            if (c0 == '\n')
            {
                if (_parens.Count == 0)
                {
                    Add(TokenKind.Newline, "\n", null, _line, Col);
                    _atLineStart = true;
                }
                _pos++;
                _line++;
                _lineStart = _pos;
                continue;
            }
            if (c0 == '\\')
            {
                if (Peek(1) == '\n')
                {
                    _pos += 2;
                    _line++;
                    _lineStart = _pos;
                    if (_pos >= n) throw Error("unexpected EOF while parsing", incomplete: true);
                    continue;
                }
                if (_pos + 1 >= n) throw Error("unexpected EOF while parsing", incomplete: true);
                throw Error("unexpected character after line continuation character");
            }
            if (IsIdStart(c0))
            {
                int start = _pos, col = Col;
                while (_pos < n && IsIdChar(_src[_pos])) _pos++;
                string word = _src.Substring(start, _pos - start);
                if (_pos < n && (_src[_pos] == '\'' || _src[_pos] == '"') && IsStringPrefix(word))
                {
                    ReadString(word.ToLowerInvariant(), start, col);
                    continue;
                }
                Add(TokenKind.Name, word.Normalize(NormalizationForm.FormKC), null, _line, col);
                continue;
            }
            if (char.IsAsciiDigit(c0) || (c0 == '.' && char.IsAsciiDigit(Peek(1))))
            {
                ReadNumber();
                continue;
            }
            if (c0 == '\'' || c0 == '"')
            {
                ReadString("", _pos, Col);
                continue;
            }
            ReadOperator();
        }

        ParseError? eofError = null;
        if (_parens.Count > 0)
        {
            // reported by the parser only if it actually reaches the end of the input
            var (ch, line, col) = _parens.Peek();
            eofError = new ParseError($"'{ch}' was never closed", line, col, incomplete: true);
        }
        if (_tokens.Count > 0 && eofError == null)
        {
            var last = _tokens[^1].Kind;
            if (last != TokenKind.Newline && last != TokenKind.Dedent && last != TokenKind.Indent)
                Add(TokenKind.Newline, "", null, _line, Col);
        }
        while (_indents.Count > 1)
        {
            _indents.RemoveAt(_indents.Count - 1);
            Add(TokenKind.Dedent, "", null, _line, 0);
        }
        Add(TokenKind.EndMarker, "", eofError, _line, Col);
    }

    private static bool IsIdStart(char c) => c == '_' || char.IsAsciiLetter(c) || (c > 127 && (char.IsLetter(c) || char.GetUnicodeCategory(c) == UnicodeCategory.LetterNumber));

    private static bool IsIdChar(char c) =>
        c == '_' || char.IsAsciiLetterOrDigit(c) || (c > 127 && (char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.LetterNumber));

    private static bool IsStringPrefix(string w)
    {
        switch (w.ToLowerInvariant())
        {
            case "r":
            case "u":
            case "f":
            case "b":
            case "br":
            case "rb":
            case "fr":
            case "rf":
                return true;
        }
        return false;
    }

    private void ReadOperator()
    {
        int line = _line, col = Col;
        string? op = null;
        foreach (var o in Ops3)
            if (string.CompareOrdinal(_src, _pos, o, 0, 3) == 0)
            {
                op = o;
                break;
            }
        if (op == null)
            foreach (var o in Ops2)
                if (string.CompareOrdinal(_src, _pos, o, 0, 2) == 0)
                {
                    op = o;
                    break;
                }
        if (op == null)
        {
            char c = _src[_pos];
            if (Ops1.IndexOf(c) < 0)
            {
                if (c == '!') throw Error("invalid syntax");
                if (c == '$' || c == '?' || c == '`') throw Error("invalid syntax");
                throw Error($"invalid character '{c}' (U+{(int)c:X4})");
            }
            op = c.ToString();
        }
        _pos += op.Length;
        switch (op)
        {
            case "(":
            case "[":
            case "{":
                _parens.Push((op[0], line, col));
                break;
            case ")":
            case "]":
            case "}":
                if (_parens.Count == 0) throw new ParseError($"unmatched '{op}'", line, col);
                var (open, _, _) = _parens.Pop();
                char expected = open == '(' ? ')' : open == '[' ? ']' : '}';
                if (expected != op[0])
                    throw new ParseError($"closing parenthesis '{op}' does not match opening parenthesis '{open}'", line, col);
                break;
        }
        Add(TokenKind.Op, op, null, line, col);
    }

    private void ReadNumber()
    {
        int start = _pos, line = _line, col = Col;
        int n = _src.Length;
        char c = _src[_pos];
        if (c == '0' && _pos + 1 < n && "xXoObB".IndexOf(_src[_pos + 1]) >= 0)
        {
            char kind = char.ToLowerInvariant(_src[_pos + 1]);
            int radix = kind == 'x' ? 16 : kind == 'o' ? 8 : 2;
            _pos += 2;
            int ds = _pos;
            while (_pos < n && (IsIdChar(_src[_pos]))) _pos++;
            string digits = _src.Substring(ds, _pos - ds);
            var v = ParseRadix(digits, radix);
            if (v == null)
            {
                string what = radix == 16 ? "hexadecimal" : radix == 8 ? "octal" : "binary";
                throw new ParseError($"invalid {what} literal", line, col);
            }
            Add(TokenKind.Number, _src.Substring(start, _pos - start), v, line, col);
            return;
        }
        ReadDigits();
        bool isFloat = false;
        if (Peek() == '.' && !(Peek(1) == '.' && Peek(2) == '.'))
        {
            isFloat = true;
            _pos++;
            if (char.IsAsciiDigit(Peek())) ReadDigits();
        }
        if ((Peek() == 'e' || Peek() == 'E') &&
            (char.IsAsciiDigit(Peek(1)) || ((Peek(1) == '+' || Peek(1) == '-') && char.IsAsciiDigit(Peek(2)))))
        {
            isFloat = true;
            _pos += 2;
            ReadDigits();
        }
        bool imag = false;
        if (Peek() == 'j' || Peek() == 'J')
        {
            imag = true;
            _pos++;
        }
        string text = _src.Substring(start, _pos - start);
        string clean = text.Replace("_", "");
        if (imag) clean = clean.Substring(0, clean.Length - 1);
        if (text.Contains("__") || text.EndsWith("_") || text.Contains("_.") || text.Contains("._") || text.Contains("_e") || text.Contains("e_") || text.Contains("_E") || text.Contains("E_"))
            throw new ParseError("invalid decimal literal", line, col);
        if (_pos < n && IsIdStart(_src[_pos]) && !IsKeywordStart())
            throw new ParseError("invalid decimal literal", line, col);
        PyObject value;
        if (isFloat || imag)
        {
            double d = double.Parse(clean, NumberStyles.Float, CultureInfo.InvariantCulture);
            value = imag ? new PyComplex(0, d) : new PyFloat(d);
        }
        else
        {
            if (clean.Length > 1 && clean[0] == '0' && clean.Any(ch => ch != '0'))
                throw new ParseError("leading zeros in decimal integer literals are not permitted; use an 0o prefix for octal integers", line, col);
            if (clean.Length < 18) value = PyInt.From(long.Parse(clean, CultureInfo.InvariantCulture));
            else
            {
                if (clean.Length > 400000) throw new ParseError("integer literal too large", line, col);
                value = PyInt.From(BigInteger.Parse(clean, CultureInfo.InvariantCulture));
            }
        }
        Add(TokenKind.Number, text, value, line, col);
    }

    private bool IsKeywordStart()
    {
        // allow constructs such as "1if x else 2" and "0in y" (accepted by CPython with a warning)
        foreach (var kw in new[] { "if", "else", "in", "is", "and", "or", "not", "for" })
            if (string.CompareOrdinal(_src, _pos, kw, 0, kw.Length) == 0) return true;
        return false;
    }

    private void ReadDigits()
    {
        while (_pos < _src.Length && (char.IsAsciiDigit(_src[_pos]) || (_src[_pos] == '_' && _pos + 1 < _src.Length && char.IsAsciiDigit(_src[_pos + 1])))) _pos++;
        if (_pos < _src.Length && _src[_pos] == '_') throw Error("invalid decimal literal");
    }

    private static PyInt? ParseRadix(string digits, int radix)
    {
        if (digits.Length == 0 || digits.EndsWith("_") || digits.Contains("__")) return null;
        BigInteger acc = 0;
        int count = 0;
        foreach (char ch in digits)
        {
            if (ch == '_') continue;
            int d = IntMath.DigitValue(ch);
            if (d < 0 || d >= radix) return null;
            acc = acc * radix + d;
            count++;
        }
        return count == 0 ? null : PyInt.From(acc);
    }

    private void ReadString(string prefix, int start, int col)
    {
        int line = _line;
        bool raw = prefix.Contains('r');
        bool isF = prefix.Contains('f');
        if (prefix.Contains('b')) throw new ParseError("bytes literals are not supported in this interpreter", line, col);
        char q = _src[_pos];
        bool triple = Peek(1) == q && Peek(2) == q;
        _pos += triple ? 3 : 1;
        _contentStart = _pos;
        var lit = new StringLit();
        if (isF) lit.FParts = ScanFStringBody(raw, q, triple, false, line, col);
        else lit.Value = ScanPlain(raw, q, triple, line, col);
        Add(TokenKind.String, _src.Substring(start, _pos - start), lit, line, col);
    }

    private bool AtClose(char q, bool triple)
    {
        if (_src[_pos] != q) return false;
        return !triple || (Peek(1) == q && Peek(2) == q);
    }

    private ParseError Unterminated(bool triple, int line, int col) =>
        triple
            ? new ParseError("unterminated triple-quoted string literal (detected at line " + _line + ")", line, col, incomplete: true)
            : new ParseError("unterminated string literal (detected at line " + _line + ")", line, col);

    private string ScanPlain(bool raw, char q, bool triple, int line, int col)
    {
        var sb = new StringBuilder();
        int n = _src.Length;
        while (true)
        {
            if (_pos >= n) throw Unterminated(triple, line, col);
            char c = _src[_pos];
            if (AtClose(q, triple))
            {
                _pos += triple ? 3 : 1;
                return sb.ToString();
            }
            if (c == '\n')
            {
                if (!triple) throw Unterminated(false, line, col);
                sb.Append('\n');
                _pos++;
                _line++;
                _lineStart = _pos;
                continue;
            }
            if (c == '\\')
            {
                if (raw)
                {
                    sb.Append('\\');
                    _pos++;
                    if (_pos < n)
                    {
                        char nx = _src[_pos];
                        if (nx == '\n')
                        {
                            sb.Append('\n');
                            _pos++;
                            _line++;
                            _lineStart = _pos;
                        }
                        else if (nx == q || nx == '\\')
                        {
                            sb.Append(nx);
                            _pos++;
                        }
                    }
                    continue;
                }
                Escape(sb);
                continue;
            }
            sb.Append(c);
            _pos++;
        }
    }

    private int _contentStart;

    private void Escape(StringBuilder sb)
    {
        int n = _src.Length;
        int bs = _pos;
        _pos++;
        if (_pos >= n) return;
        char e = _src[_pos];
        _pos++;
        switch (e)
        {
            case '\n':
                _line++;
                _lineStart = _pos;
                return;
            case '\\': sb.Append('\\'); return;
            case '\'': sb.Append('\''); return;
            case '"': sb.Append('"'); return;
            case 'a': sb.Append('\a'); return;
            case 'b': sb.Append('\b'); return;
            case 'f': sb.Append('\f'); return;
            case 'n': sb.Append('\n'); return;
            case 'r': sb.Append('\r'); return;
            case 't': sb.Append('\t'); return;
            case 'v': sb.Append('\v'); return;
            case 'x':
                sb.Append((char)HexDigits(2, "\\xXX", bs));
                return;
            case 'u':
                sb.Append((char)HexDigits(4, "\\uXXXX", bs));
                return;
            case 'U':
            {
                int cp = HexDigits(8, "\\UXXXXXXXX", bs);
                if (cp > 0x10FFFF) throw Error("(unicode error) 'unicodeescape' codec can't decode bytes: illegal Unicode character");
                if (cp < 0x10000) sb.Append((char)cp);
                else sb.Append(char.ConvertFromUtf32(cp));
                return;
            }
            case 'N':
                throw Error("(unicode error) \\N{...} escapes are not supported in this interpreter");
        }
        if (e >= '0' && e <= '7')
        {
            int v = e - '0';
            for (int k = 0; k < 2 && _pos < n && _src[_pos] >= '0' && _src[_pos] <= '7'; k++)
            {
                v = v * 8 + (_src[_pos] - '0');
                _pos++;
            }
            sb.Append((char)v);
            return;
        }
        sb.Append('\\').Append(e);
    }

    private int HexDigits(int count, string what, int bs)
    {
        int v = 0;
        for (int k = 0; k < count; k++)
        {
            if (_pos >= _src.Length || !char.IsAsciiHexDigit(_src[_pos]))
                throw Error($"(unicode error) 'unicodeescape' codec can't decode bytes in position {bs - _contentStart}-{_pos - 1 - _contentStart}: truncated {what} escape");
            v = v * 16 + IntMath.DigitValue(_src[_pos]);
            _pos++;
        }
        return v;
    }

    private List<FPart> ScanFStringBody(bool raw, char q, bool triple, bool inSpec, int line, int col)
    {
        var parts = new List<FPart>();
        var sb = new StringBuilder();
        int n = _src.Length;
        void Flush()
        {
            if (sb.Length > 0)
            {
                parts.Add(new FPart { Literal = sb.ToString() });
                sb.Clear();
            }
        }
        while (true)
        {
            if (_pos >= n) throw Unterminated(triple, line, col);
            char c = _src[_pos];
            if (AtClose(q, triple))
            {
                if (inSpec) throw Error("f-string: expecting '}'");
                _pos += triple ? 3 : 1;
                Flush();
                return parts;
            }
            if (inSpec && c == '}')
            {
                Flush();
                return parts;
            }
            if (c == '\n')
            {
                if (!triple) throw Unterminated(false, line, col);
                sb.Append('\n');
                _pos++;
                _line++;
                _lineStart = _pos;
                continue;
            }
            if (c == '{')
            {
                if (!inSpec && Peek(1) == '{')
                {
                    sb.Append('{');
                    _pos += 2;
                    continue;
                }
                Flush();
                _pos++;
                parts.Add(ScanField(raw, q, triple, line, col));
                continue;
            }
            if (c == '}')
            {
                if (!inSpec && Peek(1) == '}')
                {
                    sb.Append('}');
                    _pos += 2;
                    continue;
                }
                throw Error("f-string: single '}' is not allowed");
            }
            if (c == '\\')
            {
                if (raw)
                {
                    sb.Append('\\');
                    _pos++;
                    if (_pos < n && (_src[_pos] == q || _src[_pos] == '\\'))
                    {
                        sb.Append(_src[_pos]);
                        _pos++;
                    }
                    continue;
                }
                Escape(sb);
                continue;
            }
            sb.Append(c);
            _pos++;
        }
    }

    private FPart ScanField(bool raw, char q, bool triple, int line, int col)
    {
        int n = _src.Length;
        int exprStart = _pos;
        int exprLine = _line, exprCol = Col;
        int depth = 0;
        while (true)
        {
            if (_pos >= n) throw Error("f-string: expecting '}'", incomplete: triple);
            char c = _src[_pos];
            if (c == '\n')
            {
                if (!triple) throw Unterminated(false, line, col);
                _pos++;
                _line++;
                _lineStart = _pos;
                continue;
            }
            if (c == '\'' || c == '"')
            {
                SkipNestedString();
                continue;
            }
            if (c == '\\') throw Error("f-string expression part cannot include a backslash");
            if (c == '#') throw Error("f-string expression part cannot include '#'");
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == '}')
            {
                if (depth == 0) break;
                depth--;
            }
            else if (depth == 0)
            {
                if (c == '!' && Peek(1) != '=') break;
                if (c == ':') break;
                if (c == '=' && Peek(1) != '=' && _pos > exprStart && "=!<>".IndexOf(_src[_pos - 1]) < 0)
                {
                    int k = _pos + 1;
                    while (k < n && _src[k] == ' ') k++;
                    if (k < n && (_src[k] == '}' || _src[k] == '!' || _src[k] == ':')) break;
                }
            }
            _pos++;
        }
        var part = new FPart { Expr = _src.Substring(exprStart, _pos - exprStart), ExprLine = exprLine, ExprCol = exprCol };
        if (part.Expr.Trim().Length == 0) throw Error("f-string: valid expression required before '}'");
        if (_src[_pos] == '=')
        {
            _pos++;
            while (_pos < n && _src[_pos] == ' ') _pos++;
            part.Debug = _src.Substring(exprStart, _pos - exprStart);
        }
        if (_pos < n && _src[_pos] == '!')
        {
            char conv = Peek(1);
            if (conv != 'r' && conv != 's' && conv != 'a') throw Error("f-string: invalid conversion character: expected 's', 'r', or 'a'");
            part.Conversion = conv;
            _pos += 2;
        }
        if (_pos < n && _src[_pos] == ':')
        {
            _pos++;
            part.Spec = ScanFStringBody(raw, q, triple, true, line, col);
        }
        if (_pos >= n || _src[_pos] != '}') throw Error("f-string: expecting '}'");
        _pos++;
        if (part.Debug != null && part.Conversion == '\0' && part.Spec == null) part.Conversion = 'r';
        return part;
    }

    private void SkipNestedString()
    {
        int n = _src.Length;
        char q = _src[_pos];
        bool triple = Peek(1) == q && Peek(2) == q;
        int startLine = _line, startCol = Col;
        _pos += triple ? 3 : 1;
        while (true)
        {
            if (_pos >= n) throw Unterminated(triple, startLine, startCol);
            char c = _src[_pos];
            if (c == '\\')
            {
                _pos += 2;
                continue;
            }
            if (c == '\n')
            {
                if (!triple) throw Unterminated(false, startLine, startCol);
                _line++;
                _lineStart = _pos + 1;
            }
            if (AtClose(q, triple))
            {
                _pos += triple ? 3 : 1;
                return;
            }
            _pos++;
        }
    }
}
