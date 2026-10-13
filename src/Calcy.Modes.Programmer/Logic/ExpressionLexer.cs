using System.Numerics;

namespace Calcy.Modes.Programmer;

/// <summary>Kinds of tokens in a C-style integer expression.</summary>
public enum TokenKind
{
    /// <summary>An integer literal.</summary>
    Number,

    /// <summary>A variable, constant or function name.</summary>
    Name,

    /// <summary>A reference to an earlier card, written #3.</summary>
    CardRef,

    /// <summary>An operator; <see cref="Token.Op"/> holds its normalized spelling.</summary>
    Operator,

    /// <summary>"(".</summary>
    LeftParen,

    /// <summary>")".</summary>
    RightParen,

    /// <summary>",".</summary>
    Comma,

    /// <summary>"?" of the conditional operator.</summary>
    Question,

    /// <summary>":" of the conditional operator.</summary>
    Colon,

    /// <summary>"=" (assignment).</summary>
    Assign,

    /// <summary>End of input.</summary>
    End,
}

/// <summary>One token with its position in the source text.</summary>
/// <param name="Kind">What the token is.</param>
/// <param name="Start">Index of the first character.</param>
/// <param name="Length">Number of characters.</param>
/// <param name="Text">The source text of the token.</param>
/// <param name="Op">Normalized operator ("&amp;" for AND, "nand", "rol", ...) or the lower-case name.</param>
/// <param name="Value">Literal value (numbers) or card number (card references).</param>
/// <param name="Base">Base the literal was written in.</param>
/// <param name="HasPrefix">True when the literal had a 0x, 0o or 0b prefix.</param>
public readonly record struct Token(TokenKind Kind, int Start, int Length, string Text, string Op, BigInteger Value, NumberBase Base, bool HasPrefix)
{
    /// <summary>Index just past the token.</summary>
    public int End => Start + Length;
}

/// <summary>
/// Splits a C-style integer expression into tokens. Literals without a prefix are read in the input base, so in HEX
/// "B5" is a number. Prefixes 0x, 0o and 0b always switch the base for one literal. Word operators AND, OR, XOR, NOT,
/// NAND, NOR, XNOR, MOD, SHL, SHR, SAR, ROL and ROR are accepted in any case.
/// </summary>
public static class ExpressionLexer
{
    private static readonly Dictionary<string, string> WordOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["and"] = "&",
        ["or"] = "|",
        ["xor"] = "^",
        ["not"] = "~",
        ["nand"] = "nand",
        ["nor"] = "nor",
        ["xnor"] = "xnor",
        ["mod"] = "%",
        ["shl"] = "<<",
        ["shr"] = ">>>",
        ["sar"] = "sar",
        ["rol"] = "rol",
        ["ror"] = "ror",
    };

    /// <summary>True when <paramref name="word"/> is a word operator such as AND or ROL.</summary>
    public static bool IsKeyword(string word) => WordOperators.ContainsKey(word);

    /// <summary>Tokenizes the text; the last token is always <see cref="TokenKind.End"/>.</summary>
    /// <exception cref="ProgrammerException">On a character or literal that cannot be read.</exception>
    public static List<Token> Tokenize(string text, NumberBase inputBase)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            int start = i;
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(Word(text, start, i - start, inputBase));
                continue;
            }
            if (c == '#')
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                if (i == start + 1) throw new ProgrammerException("# must be followed by a card number, as in #3", start, 1);
                var n = BigInteger.Parse(text.AsSpan(start + 1, i - start - 1), provider: System.Globalization.CultureInfo.InvariantCulture);
                tokens.Add(new Token(TokenKind.CardRef, start, i - start, text[start..i], "#", n, NumberBase.Dec, false));
                continue;
            }
            var (kind, op, len) = Symbol(text, i);
            if (len == 0) throw new ProgrammerException($"'{c}' cannot be used here", start, 1);
            i += len;
            tokens.Add(new Token(kind, start, len, text.Substring(start, len), op, BigInteger.Zero, inputBase, false));
        }
        tokens.Add(new Token(TokenKind.End, text.Length, 0, "", "", BigInteger.Zero, inputBase, false));
        return tokens;
    }

    private static (TokenKind Kind, string Op, int Length) Symbol(string s, int i)
    {
        bool Next(string t) => string.CompareOrdinal(s, i, t, 0, t.Length) == 0;
        if (Next(">>>")) return (TokenKind.Operator, ">>>", 3);
        foreach (var two in new[] { "<<", ">>", "<=", ">=", "==", "!=", "&&", "||" })
            if (Next(two)) return (TokenKind.Operator, two, 2);
        return s[i] switch
        {
            '+' => (TokenKind.Operator, "+", 1),
            '-' or '−' => (TokenKind.Operator, "-", 1),
            '*' or '×' or '·' => (TokenKind.Operator, "*", 1),
            '/' or '÷' => (TokenKind.Operator, "/", 1),
            '%' => (TokenKind.Operator, "%", 1),
            '&' => (TokenKind.Operator, "&", 1),
            '|' => (TokenKind.Operator, "|", 1),
            '^' => (TokenKind.Operator, "^", 1),
            '~' or '¬' => (TokenKind.Operator, "~", 1),
            '!' => (TokenKind.Operator, "!", 1),
            '<' => (TokenKind.Operator, "<", 1),
            '>' => (TokenKind.Operator, ">", 1),
            '≤' => (TokenKind.Operator, "<=", 1),
            '≥' => (TokenKind.Operator, ">=", 1),
            '≠' => (TokenKind.Operator, "!=", 1),
            '(' => (TokenKind.LeftParen, "(", 1),
            ')' => (TokenKind.RightParen, ")", 1),
            ',' => (TokenKind.Comma, ",", 1),
            '?' => (TokenKind.Question, "?", 1),
            ':' => (TokenKind.Colon, ":", 1),
            '=' => (TokenKind.Assign, "=", 1),
            _ => (TokenKind.End, "", 0),
        };
    }

    private static Token Word(string text, int start, int length, NumberBase inputBase)
    {
        var word = text.Substring(start, length);
        if (char.IsAsciiDigit(word[0])) return Number(word, start, inputBase);

        // In HEX, a word made only of hex digits (FF, BEEF, a) is a number; any other letter makes it a name.
        if (inputBase == NumberBase.Hex && word[0] != '_' && BaseFormatter.AllDigits(word, NumberBase.Hex))
            return new Token(TokenKind.Number, start, length, word, "", BaseFormatter.ParseDigits(word, NumberBase.Hex), NumberBase.Hex, false);

        if (WordOperators.TryGetValue(word, out var op)) return new Token(TokenKind.Operator, start, length, word, op, BigInteger.Zero, inputBase, false);
        return new Token(TokenKind.Name, start, length, word, word.ToLowerInvariant(), BigInteger.Zero, inputBase, false);
    }

    private static Token Number(string word, int start, NumberBase inputBase)
    {
        NumberBase? prefixed = word.Length >= 2 && word[0] == '0'
            ? char.ToLowerInvariant(word[1]) switch
            {
                'x' => NumberBase.Hex,
                'o' => NumberBase.Oct,
                'b' => NumberBase.Bin,
                _ => null,
            }
            : null;
        if (prefixed is { } pb)
        {
            var digits = StripSuffix(word[2..], pb);
            if (BaseFormatter.AllDigits(digits, pb))
                return new Token(TokenKind.Number, start, word.Length, word, "", BaseFormatter.ParseDigits(digits, pb), pb, true);
        }
        var plain = StripSuffix(word, inputBase);
        if (BaseFormatter.AllDigits(plain, inputBase))
            return new Token(TokenKind.Number, start, word.Length, word, "", BaseFormatter.ParseDigits(plain, inputBase), inputBase, false);

        if (prefixed is { } p)
        {
            var prefix = word[..2];
            if (word.Length == 2) throw new ProgrammerException($"{prefix} must be followed by {BaseFormatter.DigitWord(p)} digits", start, 2);
            for (int k = 2; k < word.Length; k++)
            {
                if (word[k] == '_' || BaseFormatter.IsDigit(word[k], p)) continue;
                throw new ProgrammerException($"'{word[k]}' is not a {BaseFormatter.DigitWord(p)} digit in {word}", start + k, 1);
            }
            throw new ProgrammerException("Underscores must sit between digits, as in 1_000", start, word.Length);
        }

        // Explain the first offending character.
        for (int k = 0; k < word.Length; k++)
        {
            char ch = word[k];
            if (ch == '_' || BaseFormatter.IsDigit(ch, inputBase)) continue;
            if (char.IsAsciiLetter(ch) && !BaseFormatter.IsDigit(ch, NumberBase.Hex))
                throw new ProgrammerException($"Missing an operator in '{word}': a number cannot run into a name", start + k, 1);
            throw new ProgrammerException($"'{ch}' is not a {BaseFormatter.DigitWord(inputBase)} digit (input base is {BaseFormatter.Name(inputBase)}; pick another row or write 0x, 0o, 0b)", start + k, 1);
        }
        throw new ProgrammerException("Underscores must sit between digits, as in 1_000", start, word.Length);
    }

    /// <summary>Drops C integer suffixes (u, l, ul, ull) unless they are digits of the base.</summary>
    private static string StripSuffix(string digits, NumberBase b)
    {
        int end = digits.Length;
        while (end > 1 && char.ToLowerInvariant(digits[end - 1]) is 'u' or 'l' && !BaseFormatter.IsDigit(digits[end - 1], b)) end--;
        return end - digits.Length < -3 ? digits : digits[..end];
    }
}
