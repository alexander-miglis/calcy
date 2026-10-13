namespace Calcy.UI.Math.Model;

/// <summary>Spacing class of a character, like TeX's atom types.</summary>
public enum MathCharClass
{
    /// <summary>Letters, digits, constants: no extra spacing.</summary>
    Ordinary,

    /// <summary>Binary operators (+, −, ·): medium space on both sides unless used as a sign.</summary>
    Binary,

    /// <summary>Relations (=, ≤, →): thick space on both sides.</summary>
    Relation,

    /// <summary>Comma and semicolon: space after.</summary>
    Punctuation,

    /// <summary>Keywords such as "and" or "mod": space on both sides.</summary>
    Word,

    /// <summary>Postfix operators (!, %, °, ′).</summary>
    Postfix,

    /// <summary>A typed space.</summary>
    Space,

    /// <summary>A literal opening bracket character (brackets are normally templates).</summary>
    Open,

    /// <summary>A literal closing bracket character.</summary>
    Close,
}

/// <summary>Classification and text mapping of <see cref="MathChar"/> atoms.</summary>
public static class MathCharInfo
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal) { "and", "or", "not", "xor", "mod" };

    private static readonly Dictionary<string, string> ToLinear = new(StringComparer.Ordinal)
    {
        ["·"] = "*", ["×"] = "*", ["∙"] = "*", ["⋅"] = "*", ["÷"] = "/", ["−"] = "-", ["≤"] = "<=", ["≥"] = ">=",
        ["≠"] = "!=", ["→"] = "->", ["π"] = "pi", ["∞"] = "inf", ["′"] = "'", ["⇒"] = "=>", ["≔"] = ":=",
    };

    private static readonly Dictionary<string, string> ToDisplay = new(StringComparer.Ordinal)
    {
        ["-"] = "−", ["*"] = "·", ["'"] = "′", ["<="] = "≤", [">="] = "≥", ["!="] = "≠", ["->"] = "→", ["=>"] = "⇒",
    };

    /// <summary>True for the logic and operator keywords that are written as words.</summary>
    public static bool IsWord(string text) => Words.Contains(text);

    /// <summary>True for a single letter (Latin or Greek) that can be part of a name. π and ∞ are not.</summary>
    public static bool IsLetter(string text) => text.Length == 1 && IsLetter(text[0]);

    /// <summary>True for a letter that can be part of a name.</summary>
    public static bool IsLetter(char c) => char.IsLetter(c) && c != 'π';

    /// <summary>True for a single decimal digit.</summary>
    public static bool IsDigit(string text) => text.Length == 1 && char.IsAsciiDigit(text[0]);

    /// <summary>True when the text continues a name or number in the linear syntax (letter, digit, underscore, decimal point).</summary>
    public static bool IsNameOrNumberPart(string text) => text.Length == 1 && (IsLetter(text[0]) || char.IsAsciiDigit(text[0]) || text[0] is '_' or '.');

    /// <summary>The spacing class of an atom.</summary>
    public static MathCharClass Classify(string text)
    {
        if (Words.Contains(text) || text is "otherwise" or "if") return MathCharClass.Word;
        if (text.Length > 1)
        {
            return text switch
            {
                "<=" or ">=" or "!=" or "->" or "=>" or ":=" or "==" => MathCharClass.Relation,
                "!!" => MathCharClass.Postfix,
                _ => MathCharClass.Ordinary,
            };
        }
        return text[0] switch
        {
            '+' or '-' or '−' or '·' or '×' or '*' or '÷' or '±' or '∓' or '∙' or '⋅' => MathCharClass.Binary,
            '=' or '<' or '>' or '≤' or '≥' or '≠' or '→' or '⇒' or '↦' or '≈' or '≔' or ':' => MathCharClass.Relation,
            ',' or ';' => MathCharClass.Punctuation,
            '!' or '%' or '°' or '\'' or '′' => MathCharClass.Postfix,
            ' ' => MathCharClass.Space,
            '(' or '[' or '{' => MathCharClass.Open,
            ')' or ']' or '}' => MathCharClass.Close,
            _ => MathCharClass.Ordinary,
        };
    }

    /// <summary>The text drawn for an atom (for example "-" is drawn as the minus sign "−").</summary>
    public static string DisplayText(string text) => ToDisplay.TryGetValue(text, out var d) ? d : text;

    /// <summary>The linear Calcy syntax for an atom (for example "≤" becomes "&lt;=").</summary>
    public static string LinearText(string text) => ToLinear.TryGetValue(text, out var l) ? l : text;

    /// <summary>
    /// True for atoms that are complete tokens on their own (π, ∞, words, strings): the serializer separates them
    /// from neighbouring letters and digits so they do not merge into one name.
    /// </summary>
    public static bool IsStandaloneToken(string text) =>
        text is "π" or "∞" || Words.Contains(text) || (text.Length > 1 && text[0] == '"');

    /// <summary>True for atoms that can be part of an operand (used to find the numerator when "/" is typed).</summary>
    public static bool IsOperandPart(string text)
    {
        if (text.Length > 1) return text[0] == '"' || text is "!!" || (!Words.Contains(text) && Classify(text) == MathCharClass.Ordinary);
        char c = text[0];
        if (char.IsLetterOrDigit(c) || c is '.' or '_' or 'π' or '∞') return true;
        return c is '!' or '%' or '°' or '\'' or '′';
    }
}
