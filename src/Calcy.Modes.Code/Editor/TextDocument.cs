using System.Text;

namespace Calcy.Modes.Code.Editor;

/// <summary>A position in a <see cref="TextDocument"/>: zero based line and column (column counts UTF-16 characters).</summary>
/// <param name="Line">Zero based line.</param>
/// <param name="Column">Zero based column.</param>
public readonly record struct TextPos(int Line, int Column) : IComparable<TextPos>
{
    /// <inheritdoc />
    public int CompareTo(TextPos other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    /// <summary>Ordering.</summary>
    public static bool operator <(TextPos a, TextPos b) => a.CompareTo(b) < 0;

    /// <summary>Ordering.</summary>
    public static bool operator >(TextPos a, TextPos b) => a.CompareTo(b) > 0;

    /// <summary>Ordering.</summary>
    public static bool operator <=(TextPos a, TextPos b) => a.CompareTo(b) <= 0;

    /// <summary>Ordering.</summary>
    public static bool operator >=(TextPos a, TextPos b) => a.CompareTo(b) >= 0;

    /// <summary>The smaller of two positions.</summary>
    public static TextPos Min(TextPos a, TextPos b) => a <= b ? a : b;

    /// <summary>The larger of two positions.</summary>
    public static TextPos Max(TextPos a, TextPos b) => a >= b ? a : b;
}

/// <summary>
/// The text of the code editor as a list of lines (always at least one). Line breaks are normalised to "\n" and tabs
/// to four spaces, so every character is one column.
/// </summary>
public sealed class TextDocument
{
    /// <summary>Spaces per indentation level and per tab.</summary>
    public const int IndentSize = 4;

    private readonly List<string> _lines = [""];

    /// <summary>Raised after every change with the first line that changed.</summary>
    public event EventHandler<int>? Changed;

    /// <summary>Increases with every change.</summary>
    public int Version { get; private set; }

    /// <summary>Number of lines.</summary>
    public int LineCount => _lines.Count;

    /// <summary>A line without its line break.</summary>
    public string this[int line] => _lines[line];

    /// <summary>The position after the last character.</summary>
    public TextPos End => new(_lines.Count - 1, _lines[^1].Length);

    /// <summary>The whole text with "\n" line breaks.</summary>
    public string Text
    {
        get => string.Join('\n', _lines);
        set
        {
            _lines.Clear();
            _lines.AddRange(Normalize(value ?? "").Split('\n'));
            Version++;
            Changed?.Invoke(this, 0);
        }
    }

    /// <summary>Normalises line breaks and expands tabs to spaces.</summary>
    public static string Normalize(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!text.Contains('\t')) return text;
        var sb = new StringBuilder(text.Length + 16);
        int col = 0;
        foreach (var c in text)
        {
            if (c == '\t')
            {
                int spaces = IndentSize - col % IndentSize;
                sb.Append(' ', spaces);
                col += spaces;
            }
            else
            {
                sb.Append(c);
                col = c == '\n' ? 0 : col + 1;
            }
        }
        return sb.ToString();
    }

    /// <summary>Clamps a position into the document.</summary>
    public TextPos Clamp(TextPos p)
    {
        int line = Math.Clamp(p.Line, 0, _lines.Count - 1);
        return new TextPos(line, Math.Clamp(p.Column, 0, _lines[line].Length));
    }

    /// <summary>The text between two positions (in either order).</summary>
    public string GetText(TextPos a, TextPos b)
    {
        var s = Clamp(TextPos.Min(a, b));
        var e = Clamp(TextPos.Max(a, b));
        if (s.Line == e.Line) return _lines[s.Line][s.Column..e.Column];
        var sb = new StringBuilder();
        sb.Append(_lines[s.Line], s.Column, _lines[s.Line].Length - s.Column);
        for (int i = s.Line + 1; i < e.Line; i++) sb.Append('\n').Append(_lines[i]);
        sb.Append('\n').Append(_lines[e.Line], 0, e.Column);
        return sb.ToString();
    }

    /// <summary>Replaces the text between two positions and returns the position after the inserted text.</summary>
    public TextPos Replace(TextPos a, TextPos b, string text)
    {
        var s = Clamp(TextPos.Min(a, b));
        var e = Clamp(TextPos.Max(a, b));
        text = Normalize(text);
        string before = _lines[s.Line][..s.Column];
        string after = _lines[e.Line][e.Column..];
        var parts = text.Split('\n');
        _lines.RemoveRange(s.Line, e.Line - s.Line + 1);
        var insert = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++) insert[i] = parts[i];
        insert[0] = before + insert[0];
        int endColumn = insert[^1].Length;
        insert[^1] += after;
        _lines.InsertRange(s.Line, insert);
        Version++;
        Changed?.Invoke(this, s.Line);
        return new TextPos(s.Line + parts.Length - 1, endColumn);
    }

    /// <summary>The position reached by walking over text from a start position.</summary>
    public static TextPos Advance(TextPos start, string text)
    {
        int nl = text.LastIndexOf('\n');
        if (nl < 0) return new TextPos(start.Line, start.Column + text.Length);
        int lines = 0;
        foreach (var c in text)
            if (c == '\n') lines++;
        return new TextPos(start.Line + lines, text.Length - nl - 1);
    }

    /// <summary>Character offset of a position in <see cref="Text"/>.</summary>
    public int OffsetOf(TextPos p)
    {
        p = Clamp(p);
        int offset = 0;
        for (int i = 0; i < p.Line; i++) offset += _lines[i].Length + 1;
        return offset + p.Column;
    }

    /// <summary>The position of a character offset in <see cref="Text"/>.</summary>
    public TextPos PositionAt(int offset)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            if (offset <= _lines[i].Length) return new TextPos(i, Math.Max(0, offset));
            offset -= _lines[i].Length + 1;
        }
        return End;
    }

    /// <summary>Number of leading spaces of a line.</summary>
    public int IndentOf(int line)
    {
        var s = _lines[line];
        int i = 0;
        while (i < s.Length && s[i] == ' ') i++;
        return i;
    }
}
