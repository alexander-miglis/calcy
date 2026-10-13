namespace Calcy.Modes.Code.Editor;

/// <summary>The kind of a highlighted piece of Python source.</summary>
public enum TokenKind
{
    /// <summary>Names, operators and everything else.</summary>
    Text,

    /// <summary>Keywords (def, for, if, True, None, ...).</summary>
    Keyword,

    /// <summary>Builtin functions and types (print, range, len, ...).</summary>
    Builtin,

    /// <summary>String literals, including triple quoted strings that span lines.</summary>
    String,

    /// <summary>Number literals.</summary>
    Number,

    /// <summary>Comments.</summary>
    Comment,

    /// <summary>A decorator such as @calc.function.</summary>
    Decorator,

    /// <summary>The name after def or class.</summary>
    Definition,

    /// <summary>Brackets ( ) [ ] { }.</summary>
    Bracket,
}

/// <summary>A highlighted span of one line.</summary>
/// <param name="Start">Index of the first character in the line.</param>
/// <param name="Length">Number of characters.</param>
/// <param name="Kind">What the span is.</param>
public readonly record struct Token(int Start, int Length, TokenKind Kind);

/// <summary>Tokenizer state carried from one line to the next: an open triple quoted string.</summary>
/// <param name="OpenQuote">The open triple quote ("\"\"\"" or "'''"), or null.</param>
public readonly record struct LexState(string? OpenQuote)
{
    /// <summary>The state at the start of a file.</summary>
    public static LexState Start => default;
}

/// <summary>Splits Python source lines into highlighted tokens, one line at a time, so large files highlight incrementally.</summary>
public static class PythonHighlighter
{
    /// <summary>Python keywords (soft keywords included).</summary>
    public static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del",
        "elif", "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal",
        "not", "or", "pass", "raise", "return", "try", "while", "with", "yield", "match", "case",
    };

    /// <summary>Builtin functions and types that are highlighted.</summary>
    public static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "print", "input", "range", "len", "int", "float", "str", "bool", "list", "dict", "set", "tuple", "frozenset",
        "complex", "abs", "min", "max", "sum", "round", "sorted", "reversed", "enumerate", "zip", "map", "filter", "any",
        "all", "isinstance", "issubclass", "type", "object", "super", "property", "staticmethod", "classmethod", "iter",
        "next", "divmod", "pow", "hex", "oct", "bin", "chr", "ord", "repr", "format", "hash", "id", "callable",
        "getattr", "setattr", "hasattr", "delattr", "globals", "locals", "vars", "dir", "help", "open", "exit", "quit",
        "self", "Exception", "ValueError", "TypeError", "KeyError", "IndexError", "ZeroDivisionError", "NameError",
        "RuntimeError", "StopIteration", "AttributeError", "ArithmeticError", "OverflowError", "KeyboardInterrupt",
    };

    /// <summary>Tokenizes one line given the state at its start; returns the tokens and the state at its end.</summary>
    public static (List<Token> Tokens, LexState End) Tokenize(string line, LexState state)
    {
        var tokens = new List<Token>();
        int i = 0, n = line.Length;
        if (state.OpenQuote is { } open)
        {
            int close = FindClose(line, 0, open);
            if (close < 0)
            {
                if (n > 0) tokens.Add(new Token(0, n, TokenKind.String));
                return (tokens, state);
            }
            tokens.Add(new Token(0, close, TokenKind.String));
            i = close;
            state = LexState.Start;
        }
        bool expectName = false;
        while (i < n)
        {
            char c = line[i];
            if (c == ' ' || c == '\t')
            {
                i++;
                continue;
            }
            if (c == '#')
            {
                tokens.Add(new Token(i, n - i, TokenKind.Comment));
                break;
            }
            int prefix = StringPrefixLength(line, i);
            if (prefix >= 0)
            {
                int q = i + prefix;
                char quote = line[q];
                bool triple = q + 2 < n && line[q + 1] == quote && line[q + 2] == quote;
                if (triple)
                {
                    string delim = new(quote, 3);
                    int close = FindClose(line, q + 3, delim);
                    if (close < 0)
                    {
                        tokens.Add(new Token(i, n - i, TokenKind.String));
                        return (tokens, new LexState(delim));
                    }
                    tokens.Add(new Token(i, close - i, TokenKind.String));
                    i = close;
                }
                else
                {
                    int j = q + 1;
                    while (j < n && line[j] != quote)
                    {
                        if (line[j] == '\\') j++;
                        j++;
                    }
                    j = Math.Min(n, j + 1);
                    tokens.Add(new Token(i, j - i, TokenKind.String));
                    i = j;
                }
                expectName = false;
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(line[i + 1])))
            {
                int j = ScanNumber(line, i);
                tokens.Add(new Token(i, j - i, TokenKind.Number));
                i = j;
                expectName = false;
                continue;
            }
            if (char.IsLetter(c) || c == '_' || c > 127 && char.IsLetterOrDigit(c))
            {
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(line[j]) || line[j] == '_')) j++;
                var word = line.Substring(i, j - i);
                TokenKind kind;
                if (expectName) kind = TokenKind.Definition;
                else if (Keywords.Contains(word)) kind = TokenKind.Keyword;
                else if (Builtins.Contains(word) && !(i > 0 && line[i - 1] == '.')) kind = TokenKind.Builtin;
                else kind = TokenKind.Text;
                tokens.Add(new Token(i, j - i, kind));
                expectName = word is "def" or "class";
                i = j;
                continue;
            }
            if (c == '@' && IsLineStart(line, i))
            {
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(line[j]) || line[j] is '_' or '.')) j++;
                tokens.Add(new Token(i, j - i, TokenKind.Decorator));
                i = j;
                continue;
            }
            if (c is '(' or ')' or '[' or ']' or '{' or '}')
            {
                tokens.Add(new Token(i, 1, TokenKind.Bracket));
                i++;
                expectName = false;
                continue;
            }
            // Operators and other punctuation stay plain text; merge runs to keep the token list short.
            int k = i + 1;
            while (k < n && !char.IsLetterOrDigit(line[k]) && line[k] is not (' ' or '\t' or '#' or '"' or '\'' or '_' or '(' or ')' or '[' or ']' or '{' or '}' or '@')) k++;
            tokens.Add(new Token(i, k - i, TokenKind.Text));
            i = k;
            expectName = false;
        }
        return (tokens, state);
    }

    private static bool IsLineStart(string line, int i)
    {
        for (int k = 0; k < i; k++)
            if (line[k] is not (' ' or '\t')) return false;
        return true;
    }

    /// <summary>Length of a string prefix (r, b, f, u, rb, ...) before a quote at i, 0 for a bare quote, -1 when no string starts here.</summary>
    private static int StringPrefixLength(string line, int i)
    {
        int j = i;
        while (j < line.Length && j - i < 2 && "rRbBfFuU".IndexOf(line[j]) >= 0) j++;
        if (j < line.Length && line[j] is '"' or '\'')
        {
            if (j > i && i > 0 && (char.IsLetterOrDigit(line[i - 1]) || line[i - 1] == '_')) return -1;
            return j - i;
        }
        return -1;
    }

    private static int FindClose(string line, int from, string delim)
    {
        int i = from;
        while (i <= line.Length - delim.Length)
        {
            if (line[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (string.CompareOrdinal(line, i, delim, 0, delim.Length) == 0) return i + delim.Length;
            i++;
        }
        return -1;
    }

    private static int ScanNumber(string line, int i)
    {
        int n = line.Length, j = i;
        if (line[j] == '0' && j + 1 < n && line[j + 1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O')
        {
            j += 2;
            while (j < n && (char.IsAsciiHexDigit(line[j]) || line[j] == '_')) j++;
            return j;
        }
        while (j < n && (char.IsDigit(line[j]) || line[j] is '_' or '.')) j++;
        if (j < n && line[j] is 'e' or 'E')
        {
            int k = j + 1;
            if (k < n && line[k] is '+' or '-') k++;
            if (k < n && char.IsDigit(line[k]))
            {
                j = k;
                while (j < n && char.IsDigit(line[j])) j++;
            }
        }
        if (j < n && line[j] is 'j' or 'J') j++;
        return j;
    }
}
