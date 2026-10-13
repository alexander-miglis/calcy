using System.Text;
using Calcy.Core.Parsing;

namespace Calcy.UI.Math.Model;

/// <summary>
/// Turns the structured model into linear Calcy syntax that the Calcy.Core parser reads back.
/// </summary>
/// <remarks>
/// Rules: fractions become N/D, with brackets around N unless it is a product such as 2x or a·b, and around D
/// unless it is a single operand (number, name, call, bracket group, possibly with a power), so 1/(2x) never
/// reads as x/2. A fraction followed by a power or a postfix operator is wrapped as (N/D). A multiplication
/// sign is inserted after a fraction when a factor follows, and wherever two tokens would otherwise merge
/// (x then π gives x*pi). Templates use call syntax: sqrt(a), root(a, n), abs(a), log(x, b),
/// integrate(f, x, a, b), integrate(f, x), diff(f, x), sum(f, k, a, b), product(f, k, a, b), limit(f, x, a);
/// matrices are [[a, b], [c, d]] and piecewise definitions {c1: v1, c2: v2, otherwise}.
/// </remarks>
public static class MathSerializer
{
    /// <summary>Serializes a row. The context decides which names are functions (for minimal brackets).</summary>
    public static string Serialize(MathRow row, IParseContext? context = null) => Serialize(row.Items, context);

    /// <summary>Serializes a sequence of nodes (for example a selection).</summary>
    public static string Serialize(IReadOnlyList<MathNode> items, IParseContext? context = null)
    {
        var w = new Writer(context ?? MathParsing.DefaultContext);
        w.WriteItems(items);
        return w.ToString();
    }

    private sealed class Writer(IParseContext context)
    {
        private readonly StringBuilder _sb = new();
        private bool _lastWasToken;
        private bool _starBeforeFactor;

        public override string ToString() => _sb.ToString().Trim();

        private string Sub(MathRow row)
        {
            var w = new Writer(context);
            w.WriteItems(row.Items);
            return w.ToString();
        }

        public void WriteItems(IReadOnlyList<MathNode> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                switch (items[i])
                {
                    case MathChar c:
                        WriteChar(c, items, i);
                        break;
                    case MathTemplate t:
                        WriteTemplate(t, items, i);
                        break;
                }
            }
        }

        private void WriteChar(MathChar c, IReadOnlyList<MathNode> items, int i)
        {
            if (MathCharInfo.IsWord(c.Text))
            {
                if (_sb.Length > 0 && !char.IsWhiteSpace(_sb[^1])) _sb.Append(' ');
                _sb.Append(c.Text).Append(' ');
                _lastWasToken = false;
                _starBeforeFactor = false;
                return;
            }
            string text = MathCharInfo.LinearText(c.Text);
            if (text == " " && (_sb.Length == 0 || char.IsWhiteSpace(_sb[^1]))) return;
            bool token = MathCharInfo.IsStandaloneToken(c.Text);
            Append(text, token);
            if (text == "," && !(i + 1 < items.Count && items[i + 1] is MathChar { Text: " " })) _sb.Append(' ');
        }

        /// <summary>Appends a piece, adding '*' where the lexer would otherwise merge it with what came before.</summary>
        private void Append(string text, bool isToken)
        {
            if (text.Length == 0) return;
            if (_starBeforeFactor && StartsFactor(text)) _sb.Append('*');
            else if ((isToken || _lastWasToken) && WouldMerge(text)) _sb.Append('*');
            _starBeforeFactor = false;
            _sb.Append(text);
            _lastWasToken = isToken;
        }

        private static bool StartsFactor(string text)
        {
            char c = text[0];
            return char.IsLetterOrDigit(c) || c is '_' or '.' or '(' or '[' or '{' or '|' or '"' or '√' or '∞';
        }

        /// <summary>True when appending <paramref name="next"/> would join the last token (x then pi gives xpi).</summary>
        private bool WouldMerge(string next)
        {
            if (_sb.Length == 0) return false;
            char last = _sb[^1];
            char first = next[0];
            bool firstIdent = char.IsLetterOrDigit(first) || first == '_';
            if (!firstIdent && first != '.') return false;
            if (char.IsLetter(last) || last == '_') return firstIdent;
            if (char.IsAsciiDigit(last) || last == '.')
            {
                // Is the trailing run of digits part of a name (x2) or a number (12)?
                int k = _sb.Length - 1;
                while (k >= 0 && (char.IsAsciiDigit(_sb[k]) || _sb[k] == '.')) k--;
                bool inName = k >= 0 && (char.IsLetter(_sb[k]) || _sb[k] == '_');
                if (inName) return firstIdent;
                return char.IsAsciiDigit(first) || first == '.';
            }
            return false;
        }

        private bool EndsWithFactor()
        {
            for (int k = _sb.Length - 1; k >= 0; k--)
            {
                char c = _sb[k];
                if (char.IsWhiteSpace(c)) continue;
                return char.IsLetterOrDigit(c) || c is '_' or '.' or ')' or ']' or '}' or '!' or '%' or '°' or '\'' or '"';
            }
            return false;
        }

        private void WriteTemplate(MathTemplate t, IReadOnlyList<MathNode> items, int i)
        {
            switch (t.Kind)
            {
                case MathTemplateKind.Fraction:
                    WriteFraction(t, items, i);
                    return;
                case MathTemplateKind.Power:
                {
                    var row = t[0];
                    string e = Sub(row);
                    string piece = IsAtom(row) ? "^" + e : "^(" + e + ")";
                    _starBeforeFactor = false;
                    _sb.Append(piece);
                    _lastWasToken = true;
                    return;
                }
                case MathTemplateKind.Subscript:
                {
                    string s = Sub(t[0]).Replace(" ", "");
                    if (s.Length == 0) return;
                    _starBeforeFactor = false;
                    _sb.Append('_').Append(s);
                    _lastWasToken = true;
                    return;
                }
                case MathTemplateKind.Parens:
                    Append("(" + Sub(t[0]) + ")", true);
                    return;
                case MathTemplateKind.Brackets:
                    Append("[" + Sub(t[0]) + "]", true);
                    return;
                case MathTemplateKind.Braces:
                    Append("{" + Sub(t[0]) + "}", true);
                    return;
                case MathTemplateKind.Sqrt:
                    Call("sqrt", Sub(t[0]));
                    return;
                case MathTemplateKind.NthRoot:
                {
                    string index = Sub(t[0]);
                    string rad = Sub(t[1]);
                    if (index.Length == 0 || index == "2") Call("sqrt", rad);
                    else if (index == "3" && t.FunctionName == "cbrt") Call("cbrt", rad);
                    else Call("root", rad, index);
                    return;
                }
                case MathTemplateKind.Abs:
                    Call("abs", Sub(t[0]));
                    return;
                case MathTemplateKind.LogBase:
                {
                    string b = Sub(t[0]);
                    string arg = Sub(t[1]);
                    if (b.Length == 0) Call("log", arg);
                    else Call("log", arg, b);
                    return;
                }
                case MathTemplateKind.Integral:
                    Call(t.FunctionName ?? "integrate", Sub(t[0]), Sub(t[1]));
                    return;
                case MathTemplateKind.DefiniteIntegral:
                {
                    string lo = Sub(t[0]), hi = Sub(t[1]);
                    if (lo.Length == 0 && hi.Length == 0) Call(t.FunctionName ?? "integrate", Sub(t[2]), Sub(t[3]));
                    else Call(t.FunctionName ?? "integrate", Sub(t[2]), Sub(t[3]), lo, hi);
                    return;
                }
                case MathTemplateKind.Derivative:
                    Call(t.FunctionName ?? "diff", Sub(t[1]), Sub(t[0]));
                    return;
                case MathTemplateKind.Sum or MathTemplateKind.Product:
                {
                    string name = t.FunctionName ?? (t.Kind == MathTemplateKind.Sum ? "sum" : "product");
                    string v = Sub(t[0]), from = Sub(t[1]), to = Sub(t[2]), body = Sub(t[3]);
                    if (v.Length == 0 && from.Length == 0 && to.Length == 0) Call(name, body);
                    else Call(name, body, v, from, to);
                    return;
                }
                case MathTemplateKind.Limit:
                    Call(t.FunctionName ?? "limit", Sub(t[2]), Sub(t[0]), Sub(t[1]));
                    return;
                case MathTemplateKind.Matrix:
                {
                    var sb = new StringBuilder("[");
                    for (int r = 0; r < t.Rows; r++)
                    {
                        if (r > 0) sb.Append(", ");
                        sb.Append('[');
                        for (int c = 0; c < t.Columns; c++)
                        {
                            if (c > 0) sb.Append(", ");
                            sb.Append(Sub(t.Cell(r, c)));
                        }
                        sb.Append(']');
                    }
                    sb.Append(']');
                    Append(sb.ToString(), true);
                    return;
                }
                case MathTemplateKind.Piecewise:
                {
                    var sb = new StringBuilder("{");
                    for (int r = 0; r < t.Rows; r++)
                    {
                        if (r > 0) sb.Append(", ");
                        string value = Sub(t.Cell(r, 0));
                        string cond = Sub(t.Cell(r, 1));
                        if (cond.Length == 0 && r == t.Rows - 1 && r > 0) sb.Append(value);
                        else sb.Append(cond).Append(": ").Append(value);
                    }
                    sb.Append('}');
                    Append(sb.ToString(), true);
                    return;
                }
            }
        }

        private void Call(string name, params string[] args) => Append(name + "(" + string.Join(", ", args) + ")", true);

        private void WriteFraction(MathTemplate t, IReadOnlyList<MathNode> items, int i)
        {
            var next = i + 1 < items.Count ? items[i + 1] : null;
            var prev = i > 0 ? items[i - 1] : null;
            bool wrap = next is MathTemplate { Kind: MathTemplateKind.Power or MathTemplateKind.Subscript }
                        || next is MathChar nc && MathCharInfo.Classify(nc.Text) == MathCharClass.Postfix
                        || prev is MathChar pc && pc.Text is "÷" or "/";
            string num = Sub(t[0]);
            string den = Sub(t[1]);
            string numText = IsProductChain(t[0]) ? num : "(" + num + ")";
            string denText = IsAtom(t[1]) ? den : "(" + den + ")";
            string body = numText + "/" + denText;
            if (EndsWithFactor()) _sb.Append('*');
            _starBeforeFactor = false;
            if (wrap)
            {
                _sb.Append('(').Append(body).Append(')');
                _lastWasToken = true;
            }
            else
            {
                _sb.Append(body);
                _lastWasToken = true;
                _starBeforeFactor = true;
            }
        }

        // ------------------------------------------------------------ operand analysis

        /// <summary>True when the row reads as one operand in every implicit multiplication mode (2, x, v_0, sin(x), (a+b), √2, x², n!).</summary>
        private bool IsAtom(MathRow row)
        {
            var items = row.Items;
            int k = 0;
            if (!ReadOperand(items, ref k, allowFraction: false)) return false;
            return k == items.Count;
        }

        /// <summary>
        /// True when the row is a product of operands (2x, a·b, sin(x)cos(x), -x·y), so N/D can be written without
        /// brackets around N at the start of a term.
        /// </summary>
        private bool IsProductChain(MathRow row)
        {
            var items = row.Items;
            int k = 0;
            if (items.Count == 0) return false;
            if (items[0] is MathChar { Text: "-" or "−" or "+" }) k++;
            if (!ReadOperand(items, ref k, allowFraction: true)) return false;
            while (k < items.Count)
            {
                if (items[k] is MathChar { Text: "·" or "×" or "*" or "∙" or "⋅" or " " })
                {
                    k++;
                    if (k >= items.Count) return false;
                }
                if (!ReadOperand(items, ref k, allowFraction: true)) return false;
            }
            return true;
        }

        private bool ReadOperand(IReadOnlyList<MathNode> items, ref int k, bool allowFraction)
        {
            if (k >= items.Count) return false;
            switch (items[k])
            {
                case MathChar c when char.IsAsciiDigit(c.Text[0]) || (c.Text == "." && k + 1 < items.Count && items[k + 1] is MathChar d && MathCharInfo.IsDigit(d.Text)):
                {
                    bool dot = false;
                    while (k < items.Count && items[k] is MathChar dc && (MathCharInfo.IsDigit(dc.Text) || (dc.Text == "." && !dot)))
                    {
                        if (dc.Text == ".") dot = true;
                        k++;
                    }
                    break;
                }
                case MathChar c when MathCharInfo.IsLetter(c.Text) || c.Text == "_":
                {
                    var name = new StringBuilder();
                    while (k < items.Count && items[k] is MathChar lc && lc.Text.Length == 1 && (MathCharInfo.IsLetter(lc.Text[0]) || char.IsAsciiDigit(lc.Text[0]) || lc.Text[0] == '_'))
                    {
                        name.Append(lc.Text);
                        k++;
                    }
                    while (k < items.Count && items[k] is MathTemplate { Kind: MathTemplateKind.Subscript } st)
                    {
                        name.Append('_').Append(Sub(st[0]));
                        k++;
                    }
                    int primes = k;
                    while (primes < items.Count && items[primes] is MathChar { Text: "'" or "′" }) primes++;
                    if (primes < items.Count && items[primes] is MathTemplate { Kind: MathTemplateKind.Parens })
                    {
                        // A call is one operand only when the name is a known function; otherwise f(x) may be f·x.
                        string n = Calcy.Core.Parsing.Parser.CanonicalName(name.ToString());
                        if (!MathParsing.IsFunction(context, n)) return false;
                        k = primes + 1;
                    }
                    else if (primes != k) return false;
                    break;
                }
                case MathChar c when MathCharInfo.IsStandaloneToken(c.Text) && !MathCharInfo.IsWord(c.Text):
                    k++;
                    break;
                case MathTemplate { Kind: MathTemplateKind.Power or MathTemplateKind.Subscript }:
                    return false;
                case MathTemplate { Kind: MathTemplateKind.Fraction }:
                    if (!allowFraction) return false;
                    k++;
                    // A fraction must be followed by a separator, not juxtaposed (handled by the writer).
                    return true;
                case MathTemplate:
                    k++;
                    break;
                default:
                    return false;
            }
            // Postfix operators and powers keep it one operand.
            while (k < items.Count)
            {
                if (items[k] is MathChar p && MathCharInfo.Classify(p.Text) == MathCharClass.Postfix && p.Text is not "'" and not "′") k++;
                else if (items[k] is MathTemplate { Kind: MathTemplateKind.Power }) k++;
                else break;
            }
            return true;
        }
    }
}
