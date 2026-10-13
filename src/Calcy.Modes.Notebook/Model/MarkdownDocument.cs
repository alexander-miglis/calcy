using System.Text;
using System.Text.RegularExpressions;

namespace Calcy.Modes.Notebook.Model;

/// <summary>The kinds of block in a text cell.</summary>
public enum MdBlockKind
{
    /// <summary># to ###### headings.</summary>
    Heading,

    /// <summary>A paragraph.</summary>
    Paragraph,

    /// <summary>A "- " list item.</summary>
    Bullet,

    /// <summary>A "1. " list item.</summary>
    Numbered,

    /// <summary>A "> " quote or callout.</summary>
    Quote,

    /// <summary>A fenced code block.</summary>
    Code,

    /// <summary>A horizontal rule (---).</summary>
    Rule,
}

/// <summary>The kinds of inline content.</summary>
public enum MdInlineKind
{
    /// <summary>Plain text (possibly bold or italic).</summary>
    Text,

    /// <summary>`inline code`.</summary>
    Code,

    /// <summary>$maths$ in calculator syntax, typeset.</summary>
    Math,

    /// <summary>$=expr$: the current value of an expression.</summary>
    Live,

    /// <summary>[text](url) or a bare https:// address.</summary>
    Link,
}

/// <summary>A piece of inline content.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">The text (for maths and live values the calculator source).</param>
/// <param name="Bold">Bold text.</param>
/// <param name="Italic">Italic text.</param>
/// <param name="Url">The target of a link.</param>
/// <param name="LiveIndex">The position of a live value among all live values of the cell.</param>
public sealed record MdInline(MdInlineKind Kind, string Text, bool Bold = false, bool Italic = false, string? Url = null, int LiveIndex = -1);

/// <summary>A block of a text cell.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Level">Heading level (1 to 6) or list nesting (0 based).</param>
/// <param name="Inlines">The content (empty for code blocks and rules).</param>
/// <param name="Raw">The source text of the block's content (code for code blocks).</param>
/// <param name="Number">The number of a numbered list item.</param>
public sealed record MdBlock(MdBlockKind Kind, int Level, IReadOnlyList<MdInline> Inlines, string Raw, int Number = 0)
{
    /// <summary>The block as plain text (maths and live values as written).</summary>
    public string PlainText => string.Concat(Inlines.Select(i => i.Kind == MdInlineKind.Live ? "=" + i.Text : i.Text));
}

/// <summary>
/// The Markdown subset of text cells: headings, paragraphs, bullet and numbered lists, quotes, fenced code, rules,
/// **bold**, *italic*, `code`, [links](https://...), $inline maths$ and $=live values$. Backslash escapes a character.
/// </summary>
public sealed partial class MarkdownDocument
{
    private MarkdownDocument(IReadOnlyList<MdBlock> blocks, IReadOnlyList<string> live)
    {
        Blocks = blocks;
        LiveSources = live;
    }

    /// <summary>The blocks in order.</summary>
    public IReadOnlyList<MdBlock> Blocks { get; }

    /// <summary>The sources of the live values ($=expr$) in order.</summary>
    public IReadOnlyList<string> LiveSources { get; }

    /// <summary>The headings (level and plain text) in order, for the outline.</summary>
    public IEnumerable<(int Level, string Text)> Headings =>
        Blocks.Where(b => b.Kind == MdBlockKind.Heading).Select(b => (b.Level, b.PlainText.Trim()));

    /// <summary>Parses Markdown text.</summary>
    public static MarkdownDocument Parse(string? text)
    {
        var blocks = new List<MdBlock>();
        var live = new List<string>();
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var para = new StringBuilder();
        MdBlockKind paraKind = MdBlockKind.Paragraph;
        int paraLevel = 0, paraNumber = 0;

        void Flush()
        {
            if (para.Length == 0) return;
            var raw = para.ToString();
            blocks.Add(new MdBlock(paraKind, paraLevel, ParseInlines(raw, live), raw, paraNumber));
            para.Clear();
            paraKind = MdBlockKind.Paragraph;
            paraLevel = paraNumber = 0;
        }

        void Start(MdBlockKind kind, int level, int number, string content)
        {
            Flush();
            paraKind = kind;
            paraLevel = level;
            paraNumber = number;
            para.Append(content);
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var code = new StringBuilder();
                int j = i + 1;
                for (; j < lines.Length && !lines[j].Trim().StartsWith("```", StringComparison.Ordinal); j++)
                    code.Append(code.Length > 0 ? "\n" : "").Append(lines[j]);
                blocks.Add(new MdBlock(MdBlockKind.Code, 0, [], code.ToString()));
                i = j;
                continue;
            }
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }
            Match m;
            if ((m = HeadingPattern().Match(trimmed)).Success)
            {
                Flush();
                var content = m.Groups["t"].Value.TrimEnd('#', ' ');
                blocks.Add(new MdBlock(MdBlockKind.Heading, m.Groups["h"].Length, ParseInlines(content, live), content));
                continue;
            }
            if (RulePattern().IsMatch(trimmed))
            {
                Flush();
                blocks.Add(new MdBlock(MdBlockKind.Rule, 0, [], ""));
                continue;
            }
            int indent = line.Length - line.TrimStart().Length;
            if ((m = BulletPattern().Match(line)).Success)
            {
                Start(MdBlockKind.Bullet, Math.Min(3, indent / 2), 0, m.Groups["t"].Value);
                continue;
            }
            if ((m = NumberPattern().Match(line)).Success)
            {
                Start(MdBlockKind.Numbered, Math.Min(3, indent / 2), int.TryParse(m.Groups["n"].Value, out var n) ? n : 1, m.Groups["t"].Value);
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                var content = trimmed[1..].TrimStart();
                if (paraKind == MdBlockKind.Quote && para.Length > 0) para.Append(' ').Append(content);
                else Start(MdBlockKind.Quote, 0, 0, content);
                continue;
            }
            // A continuation line joins the open paragraph, list item or quote.
            if (para.Length > 0) para.Append(' ').Append(trimmed);
            else Start(MdBlockKind.Paragraph, 0, 0, trimmed);
        }
        Flush();
        return new MarkdownDocument(blocks, live);
    }

    /// <summary>Parses inline Markdown. Live value sources are appended to <paramref name="live"/>.</summary>
    public static IReadOnlyList<MdInline> ParseInlines(string text, List<string>? live = null)
    {
        var result = new List<MdInline>();
        var buf = new StringBuilder();
        bool bold = false, italic = false;

        void Flush()
        {
            if (buf.Length == 0) return;
            AddText(result, buf.ToString(), bold, italic);
            buf.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && "\\`*_$[]()#>-+!=".Contains(text[i + 1]))
            {
                buf.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    result.Add(new MdInline(MdInlineKind.Code, text[(i + 1)..end], bold, italic));
                    i = end + 1;
                    continue;
                }
            }
            if (c == '$')
            {
                bool display = i + 1 < text.Length && text[i + 1] == '$';
                int start = i + (display ? 2 : 1);
                int end = FindClosingDollar(text, start, display);
                if (end > start)
                {
                    Flush();
                    var src = text[start..end].Trim();
                    if (src.StartsWith('='))
                    {
                        live ??= [];
                        result.Add(new MdInline(MdInlineKind.Live, src[1..].Trim(), bold, italic, LiveIndex: live.Count));
                        live.Add(src[1..].Trim());
                    }
                    else result.Add(new MdInline(MdInlineKind.Math, src, bold, italic));
                    i = end + (display ? 2 : 1);
                    continue;
                }
            }
            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c)
            {
                Flush();
                bold = !bold;
                i += 2;
                continue;
            }
            if (c == '*' || (c == '_' && IsEmphasisUnderscore(text, i)))
            {
                Flush();
                italic = !italic;
                i++;
                continue;
            }
            if (c == '[')
            {
                int close = text.IndexOf("](", i + 1, StringComparison.Ordinal);
                int end = close > i ? text.IndexOf(')', close + 2) : -1;
                if (close > i && end > close + 2)
                {
                    Flush();
                    var label = text[(i + 1)..close];
                    var url = text[(close + 2)..end].Trim();
                    result.Add(new MdInline(MdInlineKind.Link, label.Length > 0 ? label : url, bold, italic, url));
                    i = end + 1;
                    continue;
                }
            }
            if ((c == 'h' || c == 'H') && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                var m = UrlPattern().Match(text, i);
                if (m.Success && m.Index == i)
                {
                    Flush();
                    var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?');
                    result.Add(new MdInline(MdInlineKind.Link, url, bold, italic, url));
                    i += url.Length;
                    continue;
                }
            }
            buf.Append(c);
            i++;
        }
        Flush();
        return result;
    }

    private static int FindClosingDollar(string text, int start, bool display)
    {
        for (int k = start; k < text.Length; k++)
        {
            if (text[k] == '\\') { k++; continue; }
            if (text[k] != '$') continue;
            if (!display) return k;
            if (k + 1 < text.Length && text[k + 1] == '$') return k;
        }
        return -1;
    }

    private static bool IsEmphasisUnderscore(string text, int i)
    {
        bool before = i > 0 && char.IsLetterOrDigit(text[i - 1]);
        bool after = i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]);
        return !(before && after);
    }

    private static void AddText(List<MdInline> result, string text, bool bold, bool italic)
    {
        if (result.Count > 0 && result[^1] is { Kind: MdInlineKind.Text } last && last.Bold == bold && last.Italic == italic)
            result[^1] = last with { Text = last.Text + text };
        else result.Add(new MdInline(MdInlineKind.Text, text, bold, italic));
    }

    [GeneratedRegex(@"^(?<h>#{1,6})\s+(?<t>.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(\*\s*){3,}$|^(-\s*){3,}$|^(_\s*){3,}$")]
    private static partial Regex RulePattern();

    [GeneratedRegex(@"^\s*[-*+]\s+(?<t>.*)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^\s*(?<n>\d{1,6})[.)]\s+(?<t>.*)$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"https?://[^\s<>()\[\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
