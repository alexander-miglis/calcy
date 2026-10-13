using System.Text;
using System.Text.RegularExpressions;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>A pasted table split into columns, with an optional header row.</summary>
public sealed class PastedTable
{
    private readonly List<List<string>> _columns;

    internal PastedTable(List<List<string>> columns, IReadOnlyList<string>? header)
    {
        _columns = columns;
        Header = header;
    }

    /// <summary>The header row (column titles) when the first row was text above numbers, else null.</summary>
    public IReadOnlyList<string>? Header { get; }

    /// <summary>Number of columns.</summary>
    public int ColumnCount => _columns.Count;

    /// <summary>Number of data rows (without the header).</summary>
    public int RowCount => _columns.Count == 0 ? 0 : _columns.Max(c => c.Count);

    /// <summary>The cells of one column, top to bottom (numbers in invariant spelling, so 1,5 arrives as 1.5).</summary>
    public IReadOnlyList<string> Column(int index) => _columns[index];
}

/// <summary>
/// Reads text pasted from spreadsheets and documents: tab separated rows (Excel, Google Sheets, LibreOffice), CSV with
/// commas or semicolons and quoted cells, numbers separated by spaces, one value per line, and Calcy lists {1, 2, 3}.
/// Decimal commas (1,5 and 1.234,5) become decimal points; a text row above numbers becomes the header.
/// </summary>
public static partial class TablePaste
{
    private enum Separator { Tab, Semicolon, Comma, Space, Line }

    /// <summary>Parses pasted text, or returns null when there is nothing to paste.</summary>
    public static PastedTable? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = t.Split('\n').Select(l => l.TrimEnd(' ')).ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        if (lines.Count == 0) return null;

        if (lines.Count == 1 && TryBracedList(lines[0].Trim()) is { } items)
            return Build([.. items.Select(i => new List<string> { i })], decimalComma: false);

        var sep = DetectSeparator(lines);
        var rows = lines.Select(l => Split(l, sep)).ToList();
        bool decimalComma = sep switch
        {
            Separator.Comma => false,
            Separator.Line => lines.Any(l => DecimalCommaCell().IsMatch(l.Trim())),
            _ => !rows.SelectMany(r => r).Any(c => DotDecimalCell().IsMatch(c.Trim())),
        };
        return Build(rows, decimalComma);
    }

    /// <summary>True when the text holds more than one cell (a paste into one cell then fills a block instead).</summary>
    public static bool IsTable(string? text)
    {
        var p = Parse(text);
        return p is not null && (p.ColumnCount > 1 || p.RowCount > 1 || p.Header is not null);
    }

    private static Separator DetectSeparator(List<string> lines)
    {
        if (lines.Any(l => l.Contains('\t'))) return Separator.Tab;
        if (lines.Any(l => SplitQuoted(l, ';').Count > 1)) return Separator.Semicolon;
        if (lines.Any(l => SplitQuoted(l, ',').Count > 1))
        {
            // One number per line with decimal commas (copied from a single spreadsheet column in many countries):
            // 1,5 / 2,25 / 3 is one column, not two. When every line has a comma it reads as CSV.
            bool allSingle = lines.All(l => SingleDecimalCommaLine().IsMatch(l.Trim()));
            if (allSingle && lines.Any(l => !l.Contains(','))) return Separator.Line;
            return Separator.Comma;
        }
        if (lines.Any(l => l.Trim().Split([' ', '\u00A0'], StringSplitOptions.RemoveEmptyEntries).Length > 1) &&
            lines.All(l => l.Trim().Split([' ', '\u00A0'], StringSplitOptions.RemoveEmptyEntries).All(p => CellParser.TryPlainNumber(p, out _))))
            return Separator.Space;
        return Separator.Line;
    }

    private static List<string> Split(string line, Separator sep) => sep switch
    {
        Separator.Tab => [.. line.Split('\t')],
        Separator.Semicolon => SplitQuoted(line, ';'),
        Separator.Comma => SplitQuoted(line, ','),
        Separator.Space => [.. line.Trim().Split([' ', '\u00A0'], StringSplitOptions.RemoveEmptyEntries)],
        _ => [line],
    };

    /// <summary>Splits on a separator outside double quotes (CSV); "" inside quotes is a quote.</summary>
    private static List<string> SplitQuoted(string line, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                    continue;
                }
                quoted = !quoted;
                continue;
            }
            if (ch == separator && !quoted)
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

    private static PastedTable Build(List<List<string>> rows, bool decimalComma)
    {
        int width = rows.Max(r => r.Count);
        var cleaned = rows.Select(r => Enumerable.Range(0, width).Select(i => i < r.Count ? Clean(r[i], decimalComma) : "").ToList()).ToList();
        IReadOnlyList<string>? header = null;
        if (cleaned.Count > 1 && IsHeader(cleaned[0], cleaned.Skip(1)))
        {
            header = cleaned[0];
            cleaned.RemoveAt(0);
        }
        var columns = new List<List<string>>();
        for (int j = 0; j < width; j++)
        {
            var col = cleaned.Select(r => r[j]).ToList();
            while (col.Count > 0 && col[^1].Length == 0) col.RemoveAt(col.Count - 1);
            columns.Add(col);
        }
        // Drop columns that are empty all the way down (a trailing tab, say), unless they have a title.
        for (int j = columns.Count - 1; j >= 0; j--)
        {
            if (columns[j].Count > 0 || (header is not null && header[j].Length > 0)) break;
            columns.RemoveAt(j);
            if (header is not null) header = [.. header.Take(j)];
        }
        return new PastedTable(columns, header);
    }

    /// <summary>A first row of text above rows of numbers is a header.</summary>
    private static bool IsHeader(List<string> first, IEnumerable<List<string>> rest)
    {
        bool anyText = first.Any(c => c.Length > 0 && !CellParser.TryPlainNumber(c, out _));
        if (!anyText) return false;
        var body = rest.ToList();
        for (int j = 0; j < first.Count; j++)
        {
            if (first[j].Length == 0 || CellParser.TryPlainNumber(first[j], out _)) continue;
            var below = body.Select(r => r[j]).Where(c => c.Length > 0).ToList();
            if (below.Count > 0 && below.Count(c => CellParser.TryPlainNumber(c, out _)) * 2 >= below.Count) return true;
        }
        return false;
    }

    private static string Clean(string cell, bool decimalComma)
    {
        var c = cell.Trim();
        if (c.Length >= 2 && c[0] == '"' && c[^1] == '"') c = c[1..^1].Replace("\"\"", "\"").Trim();
        if (c.Contains('−')) c = c.Replace('−', '-');
        if (decimalComma)
        {
            if (DecimalCommaCell().IsMatch(c)) return c.TrimStart('+').Replace(',', '.');
            if (GroupedCommaDecimal().IsMatch(c)) return c.TrimStart('+').Replace(".", "").Replace(',', '.');
        }
        else
        {
            // 1,234.5 (thousands separators from an English spreadsheet).
            if (GroupedDotDecimal().IsMatch(c)) return c.Replace(",", "");
            if (DecimalCommaCell().IsMatch(c) && !ThreeDigitsAfterComma().IsMatch(c)) return c.Replace(',', '.');
        }
        return c;
    }

    /// <summary>{1, 2, 3} or [1, 2, 3]: the items (a Calcy list copied from a card).</summary>
    private static List<string>? TryBracedList(string t)
    {
        if (t.Length < 2) return null;
        if (!((t[0] == '{' && t[^1] == '}') || (t[0] == '[' && t[^1] == ']'))) return null;
        var inner = t[1..^1];
        if (inner.Contains('{') || inner.Contains('[')) return null;
        var items = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;
        foreach (var ch in inner)
        {
            if (ch == '(') depth++;
            else if (ch == ')') depth = Math.Max(0, depth - 1);
            if (ch == ',' && depth == 0)
            {
                items.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        items.Add(sb.ToString().Trim());
        return items.Count == 0 || items.All(i => i.Length == 0) ? null : items;
    }

    [GeneratedRegex(@"^[-+]?\d+,\d+$")]
    private static partial Regex DecimalCommaCell();

    [GeneratedRegex(@"^[-+]?\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex GroupedCommaDecimal();

    [GeneratedRegex(@"^[-+]?\d{1,3}(,\d{3})+\.\d+$")]
    private static partial Regex GroupedDotDecimal();

    [GeneratedRegex(@"^[-+]?\d*\.\d+$")]
    private static partial Regex DotDecimalCell();

    [GeneratedRegex(@"^[-+]?\d+(,\d+)?$")]
    private static partial Regex SingleDecimalCommaLine();

    [GeneratedRegex(@"^[-+]?\d{1,3},\d{3}$")]
    private static partial Regex ThreeDigitsAfterComma();
}
