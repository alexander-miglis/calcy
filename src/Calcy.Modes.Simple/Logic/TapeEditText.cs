using System.Globalization;
using System.Text;

namespace Calcy.Modes.Simple;

/// <summary>
/// Splits the editable text of a tape line into its number slots and its fixed shape.
/// "200 + 10%" has the shape "N+N%" and the slots "200" and "10"; "#3 × 2" has the slots "#3" and "2".
/// An edit is accepted when the shape is unchanged, so only the numbers (and line references) can change.
/// </summary>
internal static class TapeEditText
{
    /// <summary>One number slot: a decimal number or a reference "#n", each with an optional leading minus sign.</summary>
    internal readonly record struct Slot(string Text, bool Negative, int? LineRef, string Number);

    internal readonly record struct Parsed(string Shape, IReadOnlyList<Slot> Slots);

    /// <summary>Tokenizes edit text. Returns false with a short, user-facing message when the text is not a calculation.</summary>
    internal static bool TryParse(string text, out Parsed parsed, out string? error)
    {
        parsed = default;
        error = null;
        var shape = new StringBuilder();
        var slots = new List<Slot>();
        bool expectNumber = true;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (expectNumber)
            {
                if (c == '√') { shape.Append('√'); i++; continue; }
                if (c == '(') { shape.Append('('); i++; continue; }
                if (c == '1' && i + 2 < text.Length && text[i + 1] == '/' && StartsOperand(text[i + 2]))
                {
                    shape.Append("1/");
                    i += 2;
                    continue;
                }
                int start = i;
                bool negative = false;
                if ((c is '-' or '−') && i + 1 < text.Length && StartsSlot(text[i + 1]))
                {
                    negative = true;
                    i++;
                }
                else if (c == '+' && i + 1 < text.Length && StartsSlot(text[i + 1]))
                {
                    i++;
                }
                if (i < text.Length && text[i] == '#')
                {
                    int j = ++i;
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    if (i == j)
                    {
                        error = "Write a line number after #, for example #2";
                        return false;
                    }
                    int n = int.Parse(text.AsSpan(j, i - j), NumberStyles.None, CultureInfo.InvariantCulture);
                    slots.Add(new Slot(text[start..i], negative, n, ""));
                }
                else if (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] is '.' or ','))
                {
                    int j = i;
                    while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] is '.' or ',')) i++;
                    if (i < text.Length && text[i] is 'e' or 'E')
                    {
                        int k = i + 1;
                        if (k < text.Length && text[k] is '+' or '-' or '−') k++;
                        if (k < text.Length && char.IsAsciiDigit(text[k]))
                        {
                            i = k;
                            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                        }
                    }
                    slots.Add(new Slot(text[start..i], negative, null, text[j..i]));
                }
                else
                {
                    error = "Expected a number here: " + Snippet(text, start);
                    return false;
                }
                shape.Append('N');
                expectNumber = false;
                continue;
            }

            switch (c)
            {
                case '²':
                    shape.Append('²');
                    i++;
                    break;
                case '^' when i + 1 < text.Length && text[i + 1] == '2':
                    shape.Append('²');
                    i += 2;
                    break;
                case '%':
                case ')':
                    shape.Append(c);
                    i++;
                    break;
                case '+':
                    shape.Append('+');
                    i++;
                    expectNumber = true;
                    break;
                case '-' or '−':
                    shape.Append('−');
                    i++;
                    expectNumber = true;
                    break;
                case '*' or '×' or 'x' or 'X':
                    shape.Append('×');
                    i++;
                    expectNumber = true;
                    break;
                case '/' or '÷':
                    shape.Append('÷');
                    i++;
                    expectNumber = true;
                    break;
                default:
                    error = "Unexpected " + Snippet(text, i);
                    return false;
            }
        }
        if (slots.Count == 0 || expectNumber)
        {
            error = "The calculation is incomplete";
            return false;
        }
        parsed = new Parsed(shape.ToString(), slots);
        return true;
    }

    private static bool StartsSlot(char c) => char.IsAsciiDigit(c) || c is '.' or '#';

    private static bool StartsOperand(char c) => StartsSlot(c) || c is '(' or '√' or '-' or '−';

    private static string Snippet(string text, int at)
    {
        var rest = text[at..].Trim();
        return rest.Length <= 8 ? $"\"{rest}\"" : $"\"{rest[..8]}...\"";
    }
}
