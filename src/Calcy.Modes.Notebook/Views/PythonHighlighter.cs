namespace Calcy.Modes.Notebook.Views;

/// <summary>What a piece of Python source is, for colouring.</summary>
internal enum PyTokenKind
{
    Plain,
    Keyword,
    Builtin,
    String,
    Comment,
    Number,
}

/// <summary>A small Python tokenizer for colouring code cells (keywords and builtins, strings, comments, numbers).</summary>
internal static class PythonHighlighter
{
    private static readonly HashSet<string> Keywords =
    [
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
        "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
        "pass", "raise", "return", "try", "while", "with", "yield", "match", "case",
    ];

    private static readonly HashSet<string> Builtins =
    [
        "print", "input", "range", "len", "int", "float", "str", "list", "dict", "set", "tuple", "abs", "round", "min", "max",
        "sum", "sorted", "enumerate", "zip", "map", "filter", "isinstance", "type", "bool", "any", "all", "open", "repr", "divmod", "pow",
    ];

    /// <summary>Splits source into coloured pieces; concatenating the texts gives the source back.</summary>
    public static List<(string Text, PyTokenKind Kind)> Tokenize(string source)
    {
        var result = new List<(string, PyTokenKind)>();
        int i = 0, n = source.Length;
        void Add(int start, int end, PyTokenKind kind)
        {
            if (end <= start) return;
            var text = source[start..end];
            if (result.Count > 0 && result[^1].Item2 == kind) result[^1] = (result[^1].Item1 + text, kind);
            else result.Add((text, kind));
        }
        while (i < n)
        {
            char c = source[i];
            if (c == '#')
            {
                int e = source.IndexOf('\n', i);
                if (e < 0) e = n;
                Add(i, e, PyTokenKind.Comment);
                i = e;
                continue;
            }
            if (c is '"' or '\'' || ((c is 'f' or 'r' or 'b' or 'F' or 'R' or 'B') && i + 1 < n && source[i + 1] is '"' or '\'' && (i == 0 || !IsIdent(source[i - 1]))))
            {
                int start = i;
                if (c is not ('"' or '\'')) i++;
                char q = source[i];
                bool triple = i + 2 < n && source[i + 1] == q && source[i + 2] == q;
                i += triple ? 3 : 1;
                while (i < n)
                {
                    if (source[i] == '\\') { i += 2; continue; }
                    if (triple)
                    {
                        if (i + 2 < n && source[i] == q && source[i + 1] == q && source[i + 2] == q) { i += 3; break; }
                    }
                    else if (source[i] == q) { i++; break; }
                    else if (source[i] == '\n') break;
                    i++;
                }
                Add(start, Math.Min(i, n), PyTokenKind.String);
                continue;
            }
            if (char.IsDigit(c) && (i == 0 || !IsIdent(source[i - 1])))
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(source[i]) || source[i] is '.' or '_')) i++;
                Add(start, i, PyTokenKind.Number);
                continue;
            }
            if (IsIdentStart(c))
            {
                int start = i;
                while (i < n && IsIdent(source[i])) i++;
                var word = source[start..i];
                bool attribute = start > 0 && source[start - 1] == '.';
                var kind = Keywords.Contains(word) ? PyTokenKind.Keyword : !attribute && Builtins.Contains(word) ? PyTokenKind.Builtin : PyTokenKind.Plain;
                Add(start, i, kind);
                continue;
            }
            Add(i, i + 1, PyTokenKind.Plain);
            i++;
        }
        return result;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';
}
