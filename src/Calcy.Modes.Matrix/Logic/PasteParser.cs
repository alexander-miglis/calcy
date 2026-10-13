using System.Text;
using System.Text.RegularExpressions;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>
/// Turns pasted text into rows of cell texts. Understands spreadsheet copies (tab separated), CSV (commas or semicolons),
/// numbers separated by spaces, and Calcy matrices such as [[1, 2], [3, 4]] or [1, 2; 3, 4].
/// </summary>
public static partial class PasteParser
{
    /// <summary>
    /// Splits <paramref name="text"/> into a rectangular table (short rows are padded with "0"). Returns null when there is
    /// nothing to paste.
    /// </summary>
    public static string[][]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n', ' ', '\t');
        if (t.Length == 0) return null;

        var bracketed = TryBracketed(t.Trim());
        var rows = bracketed ?? SplitLines(t);
        if (rows.Count == 0) return null;
        int width = rows.Max(r => r.Count);
        if (width == 0) return null;
        return [.. rows.Select(r => Enumerable.Range(0, width).Select(i => i < r.Count && r[i].Length > 0 ? r[i] : "0").ToArray())];
    }

    /// <summary>True when the text holds more than one cell (so a paste into one cell should fill the grid instead).</summary>
    public static bool IsTable(string? text)
    {
        var table = Parse(text);
        return table is not null && (table.Length > 1 || table[0].Length > 1);
    }

    private static List<List<string>> SplitLines(string t)
    {
        var lines = t.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0).ToList();
        bool tabs = lines.Any(l => l.Contains('\t'));
        bool semicolons = !tabs && lines.Any(l => TopLevelSplit(l, ';').Count > 1);
        bool commas = !tabs && !semicolons && lines.Any(l => TopLevelSplit(l, ',').Count > 1);
        var rows = new List<List<string>>();
        foreach (var line in lines)
        {
            List<string> cells;
            if (tabs) cells = [.. line.Split('\t').Select(c => CleanCell(c, decimalComma: true))];
            else if (semicolons) cells = [.. TopLevelSplit(line, ';').Select(c => CleanCell(c, decimalComma: true))];
            else if (commas) cells = [.. TopLevelSplit(line, ',').Select(c => CleanCell(c, decimalComma: false))];
            else
            {
                var parts = line.Split([' ', ' '], StringSplitOptions.RemoveEmptyEntries);
                cells = parts.Length > 1 && parts.All(p => NumberToken().IsMatch(p)) ? [.. parts] : [line.Trim()];
            }
            rows.Add(cells);
        }
        return rows;
    }

    /// <summary>Calcy or MATLAB style: [[1, 2], [3, 4]], {{1, 2}, {3, 4}}, [1, 2; 3, 4] or a single row [1, 2, 3].</summary>
    private static List<List<string>>? TryBracketed(string t)
    {
        if (t.Length < 2) return null;
        char open = t[0];
        char close = open switch { '[' => ']', '{' => '}', _ => '\0' };
        if (close == '\0' || t[^1] != close || MatchingClose(t, 0) != t.Length - 1) return null;
        var inner = t[1..^1].Trim();
        if (inner.Length == 0) return null;
        var rows = new List<List<string>>();
        var semicolonRows = TopLevelSplit(inner, ';');
        if (semicolonRows.Count > 1)
        {
            foreach (var r in semicolonRows) rows.Add([.. TopLevelSplit(r, ',').Select(c => c.Trim())]);
            return rows;
        }
        var items = TopLevelSplit(inner, ',').Select(s => s.Trim()).ToList();
        bool nested = items.All(i => i.Length >= 2 && (i[0] == '[' || i[0] == '{') && MatchingClose(i, 0) == i.Length - 1);
        if (nested)
        {
            foreach (var item in items) rows.Add([.. TopLevelSplit(item[1..^1], ',').Select(c => c.Trim())]);
            return rows;
        }
        rows.Add(items);
        return rows;
    }

    private static int MatchingClose(string s, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < s.Length; i++)
        {
            if (s[i] is '(' or '[' or '{') depth++;
            else if (s[i] is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>Splits on a separator that is not inside brackets (so log(8, 2) stays one cell).</summary>
    internal static List<string> TopLevelSplit(string s, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;
        foreach (var ch in s)
        {
            if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
            if (ch == separator && depth == 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    private static string CleanCell(string cell, bool decimalComma)
    {
        var c = cell.Trim();
        if (c.Length >= 2 && c[0] == '"' && c[^1] == '"') c = c[1..^1].Replace("\"\"", "\"").Trim();
        // Spreadsheets in many countries copy 1,5 for one and a half.
        if (decimalComma && DecimalComma().IsMatch(c)) c = c.Replace(',', '.');
        return c;
    }

    [GeneratedRegex(@"^[-+−]?(\d+([.,]\d*)?|\.\d+)([eE][-+]?\d+)?(/\d+)?$")]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"^[-+]?\d+,\d+$")]
    private static partial Regex DecimalComma();
}
