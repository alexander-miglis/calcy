using System.Text;
using Calcy.Core.Numbers;

namespace Calcy.Core.Parsing;

public enum TokenKind
{
    Number,
    Identifier,
    String,
    Plus,
    Minus,
    Star,
    Slash,
    Caret,
    LParen,
    RParen,
    LBracket,
    RBracket,
    LBrace,
    RBrace,
    Comma,
    Semicolon,
    Colon,
    Assign,      // :=
    Equal,       // = or ==
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Bang,        // !
    DoubleBang,  // !!
    Percent,
    Degree,
    /// <summary>Postfix ᵀ: the transpose of a matrix.</summary>
    Transpose,
    Prime,       // '
    Bar,         // |
    Arrow,       // -> or →
    FatArrow,    // =>
    Sqrt,        // √
    Cbrt,        // ∛
    Fourth,      // ∜
    Infinity,    // ∞
    Range,       // .. or ...
    End,
}

public readonly record struct Token(TokenKind Kind, string Text, int Position, int Length, bool SpaceBefore)
{
    /// <summary>Exact value of a number token.</summary>
    public BigRational? Value { get; init; }

    /// <summary>True when a number literal was written with a decimal point or exponent.</summary>
    public bool IsDecimal { get; init; }

    public override string ToString() => $"{Kind} '{Text}' @{Position}";
}

/// <summary>Turns calculator input into tokens. Accepts ASCII and the usual Unicode maths symbols.</summary>
public static class Lexer
{
    private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;
        bool space = false;
        while (i < text.Length)
        {
            char ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                space = true;
                i++;
                continue;
            }
            int start = i;
            void Add(TokenKind k, int len)
            {
                tokens.Add(new Token(k, text.Substring(start, len), start, len, space));
                i = start + len;
                space = false;
            }

            if (char.IsDigit(ch) || (ch == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                tokens.Add(ReadNumber(text, ref i, space));
                space = false;
                continue;
            }
            if (IsIdentStart(ch))
            {
                int j = i + 1;
                while (j < text.Length && IsIdentPart(text[j])) j++;
                // Allow subscript digits written with Unicode subscripts: v₀ becomes v_0.
                var name = NormalizeIdentifier(text[i..j]);
                tokens.Add(new Token(TokenKind.Identifier, name, i, j - i, space));
                i = j;
                space = false;
                continue;
            }
            if (Superscripts.Contains(ch) || ch == '⁻')
            {
                // x² is x^2, x⁻¹ is x^(-1).
                tokens.Add(new Token(TokenKind.Caret, "^", i, 0, space));
                space = false;
                if (ch == '⁻')
                {
                    tokens.Add(new Token(TokenKind.Minus, "-", i, 1, false));
                    i++;
                }
                int j = i;
                var digits = new StringBuilder();
                while (j < text.Length && Superscripts.Contains(text[j]))
                {
                    digits.Append((char)('0' + Superscripts.IndexOf(text[j])));
                    j++;
                }
                if (digits.Length == 0) throw new ParseException("Superscript minus must be followed by superscript digits", i);
                tokens.Add(new Token(TokenKind.Number, digits.ToString(), i, j - i, false) { Value = BigRational.ParseDecimal(digits.ToString()) });
                i = j;
                continue;
            }
            switch (ch)
            {
                case '"':
                {
                    int j = i + 1;
                    var sb = new StringBuilder();
                    while (j < text.Length && text[j] != '"')
                    {
                        if (text[j] == '\\' && j + 1 < text.Length)
                        {
                            j++;
                            sb.Append(text[j] switch { 'n' => '\n', 't' => '\t', _ => text[j] });
                        }
                        else sb.Append(text[j]);
                        j++;
                    }
                    if (j >= text.Length) throw new ParseException("This text is missing its closing quote", i, text.Length - i);
                    tokens.Add(new Token(TokenKind.String, sb.ToString(), i, j - i + 1, space));
                    i = j + 1;
                    space = false;
                    continue;
                }
                case '+': Add(TokenKind.Plus, 1); continue;
                case '-' or '−':
                    if (ch == '-' && Peek(text, i + 1) == '>') { Add(TokenKind.Arrow, 2); continue; }
                    Add(TokenKind.Minus, 1);
                    continue;
                case '*' or '×' or '·' or '∙' or '⋅':
                    if (ch == '*' && Peek(text, i + 1) == '*') { Add(TokenKind.Caret, 2); continue; }
                    Add(TokenKind.Star, 1);
                    continue;
                case '/' or '÷' or '∕': Add(TokenKind.Slash, 1); continue;
                case '^': Add(TokenKind.Caret, 1); continue;
                case '(': Add(TokenKind.LParen, 1); continue;
                case ')': Add(TokenKind.RParen, 1); continue;
                case '[': Add(TokenKind.LBracket, 1); continue;
                case ']': Add(TokenKind.RBracket, 1); continue;
                case '{': Add(TokenKind.LBrace, 1); continue;
                case '}': Add(TokenKind.RBrace, 1); continue;
                case ',': Add(TokenKind.Comma, 1); continue;
                case ';': Add(TokenKind.Semicolon, 1); continue;
                case ':':
                    if (Peek(text, i + 1) == '=') { Add(TokenKind.Assign, 2); continue; }
                    Add(TokenKind.Colon, 1);
                    continue;
                case '=':
                    if (Peek(text, i + 1) == '=') { Add(TokenKind.Equal, 2); continue; }
                    if (Peek(text, i + 1) == '>') { Add(TokenKind.FatArrow, 2); continue; }
                    Add(TokenKind.Equal, 1);
                    continue;
                case '≠': Add(TokenKind.NotEqual, 1); continue;
                case '<':
                    if (Peek(text, i + 1) == '=') { Add(TokenKind.LessEqual, 2); continue; }
                    if (Peek(text, i + 1) == '>') { Add(TokenKind.NotEqual, 2); continue; }
                    Add(TokenKind.Less, 1);
                    continue;
                case '>':
                    if (Peek(text, i + 1) == '=') { Add(TokenKind.GreaterEqual, 2); continue; }
                    Add(TokenKind.Greater, 1);
                    continue;
                case '≤': Add(TokenKind.LessEqual, 1); continue;
                case '≥': Add(TokenKind.GreaterEqual, 1); continue;
                case '!':
                    if (Peek(text, i + 1) == '=') { Add(TokenKind.NotEqual, 2); continue; }
                    if (Peek(text, i + 1) == '!') { Add(TokenKind.DoubleBang, 2); continue; }
                    Add(TokenKind.Bang, 1);
                    continue;
                case '%': Add(TokenKind.Percent, 1); continue;
                case '°': Add(TokenKind.Degree, 1); continue;
                case 'ᵀ': Add(TokenKind.Transpose, 1); continue;
                case '\'' or '′': Add(TokenKind.Prime, 1); continue;
                case '|': Add(TokenKind.Bar, 1); continue;
                case '→': Add(TokenKind.Arrow, 1); continue;
                case '√': Add(TokenKind.Sqrt, 1); continue;
                case '∛': Add(TokenKind.Cbrt, 1); continue;
                case '∜': Add(TokenKind.Fourth, 1); continue;
                case '∞': Add(TokenKind.Infinity, 1); continue;
                case '…': Add(TokenKind.Range, 1); continue;
                case '.':
                    if (Peek(text, i + 1) == '.')
                    {
                        Add(TokenKind.Range, Peek(text, i + 2) == '.' ? 3 : 2);
                        continue;
                    }
                    break;
            }
            throw new ParseException($"Unexpected character '{ch}'", i);
        }
        tokens.Add(new Token(TokenKind.End, "", text.Length, 0, space));
        return tokens;
    }

    private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';

    // ᵀ is a letter to Unicode but the transpose operator here.
    private static bool IsIdentStart(char c) => (char.IsLetter(c) && c != 'ᵀ') || c == '_';

    private static bool IsIdentPart(char c) =>
        (char.IsLetterOrDigit(c) && c != 'ᵀ') || c == '_' || (c >= '₀' && c <= '₉');

    private static string NormalizeIdentifier(string raw)
    {
        if (!raw.Any(c => c >= '₀' && c <= '₉')) return raw;
        var sb = new StringBuilder();
        bool inSub = false;
        foreach (var c in raw)
        {
            if (c >= '₀' && c <= '₉')
            {
                if (!inSub) { sb.Append('_'); inSub = true; }
                sb.Append((char)('0' + (c - '₀')));
            }
            else
            {
                inSub = false;
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static Token ReadNumber(string text, ref int i, bool space)
    {
        int start = i;
        // Base prefixes.
        if (text[i] == '0' && i + 1 < text.Length)
        {
            char p = char.ToLowerInvariant(text[i + 1]);
            int radix = p switch { 'x' => 16, 'b' => 2, 'o' => 8, _ => 0 };
            if (radix != 0)
            {
                int j = i + 2;
                while (j < text.Length && (Uri.IsHexDigit(text[j]) || text[j] == '_') && DigitValue(text[j]) < radix) j++;
                if (j > i + 2)
                {
                    var digits = text[(i + 2)..j].Replace("_", "");
                    System.Numerics.BigInteger v = 0;
                    foreach (var d in digits) v = v * radix + DigitValue(d);
                    i = j;
                    return new Token(TokenKind.Number, text[start..j], start, j - start, space) { Value = new BigRational(v) };
                }
            }
        }
        bool isDecimal = false;
        int k = i;
        while (k < text.Length && (char.IsDigit(text[k]) || (text[k] == '_' && k + 1 < text.Length && char.IsDigit(text[k + 1])))) k++;
        if (k < text.Length && text[k] == '.' && !(k + 1 < text.Length && text[k + 1] == '.'))
        {
            isDecimal = true;
            k++;
            while (k < text.Length && char.IsDigit(text[k])) k++;
        }
        // Exponent: 1e5, 2.5E-3, 3ᴇ4. Only when digits follow, so 2e stays 2 times e.
        if (k < text.Length && (text[k] == 'e' || text[k] == 'E' || text[k] == 'ᴇ'))
        {
            int m = k + 1;
            if (m < text.Length && (text[m] == '+' || text[m] == '-')) m++;
            if (m < text.Length && char.IsDigit(text[m]))
            {
                while (m < text.Length && char.IsDigit(text[m])) m++;
                isDecimal = true;
                k = m;
            }
        }
        var raw = text[start..k].Replace("_", "").Replace('ᴇ', 'e');
        i = k;
        try
        {
            return new Token(TokenKind.Number, text[start..k], start, k - start, space) { Value = BigRational.ParseDecimal(raw), IsDecimal = isDecimal };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new ParseException($"'{text[start..k]}' is not a valid number", start, k - start);
        }
    }


    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 99,
    };

}
