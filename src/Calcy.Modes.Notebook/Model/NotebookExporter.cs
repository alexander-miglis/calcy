using System.Text;
using Calcy.Core;

namespace Calcy.Modes.Notebook.Model;

/// <summary>Turns a notebook into Markdown or LaTeX text (results as they were last calculated).</summary>
public static class NotebookExporter
{
    /// <summary>
    /// Markdown: text cells as written with their maths as LaTeX between dollar signs, maths lines as $$...$$ with
    /// their results, tables as Markdown tables, graphs as their expressions, code in fenced blocks with the output.
    /// </summary>
    public static string ToMarkdown(NotebookDocument doc, CalcSession session)
    {
        var parts = new List<string>();
        if (doc.Cells.FirstOrDefault() is not TextCell { Text: var first } || !first.TrimStart().StartsWith("# ", StringComparison.Ordinal))
            parts.Add("# " + doc.Name);
        foreach (var cell in doc.Cells)
        {
            var sb = new StringBuilder();
            if (cell.Kind != CellKind.Text && cell.Caption is { } caption) sb.Append("**").Append(caption).Append("**\n\n");
            switch (cell)
            {
                case TextCell t:
                    sb.Append(TextMarkdown(t, session));
                    break;
                case MathCell m:
                    AppendLines(sb, m.Lines, session);
                    break;
                case TableCell tc:
                    AppendTableMarkdown(sb, tc);
                    if (tc.Summary.Count > 0)
                    {
                        sb.Append('\n');
                        AppendLines(sb, tc.Summary, session);
                    }
                    break;
                case GraphCell g:
                    if (cell.Caption is null) sb.Append("**Graph**\n\n");
                    foreach (var e in g.Expressions.Where(e => !string.IsNullOrWhiteSpace(e)))
                        sb.Append("- $").Append(ResultForms.InputLatex(e, g.CellSession ?? session)).Append("$\n");
                    foreach (var (name, value) in g.Sliders)
                        sb.Append("- slider $").Append(name).Append(" = ").Append(Format(value)).Append("$\n");
                    break;
                case CodeCell c:
                    sb.Append("```python\n").Append(c.Code.TrimEnd('\n')).Append("\n```\n");
                    var output = c.LastRun is not null ? c.Output.PlainText() : c.PreviousOutput;
                    if (!string.IsNullOrWhiteSpace(output)) sb.Append("\n```text\n").Append(output.TrimEnd('\n')).Append("\n```\n");
                    break;
            }
            var text = sb.ToString().TrimEnd('\n');
            if (text.Length > 0) parts.Add(text);
        }
        return string.Join("\n\n", parts) + "\n";
    }

    /// <summary>A text cell with its inline maths and live values turned into LaTeX between dollar signs.</summary>
    public static string TextMarkdown(TextCell cell, CalcSession session) =>
        RewriteMath(cell.Text, (source, live, index) => "$" + MathLatex(cell, source, live, index, session) + "$");

    /// <summary>A standalone LaTeX document of the notebook.</summary>
    public static string ToLatex(NotebookDocument doc, CalcSession session)
    {
        var sb = new StringBuilder();
        sb.Append("\\documentclass{article}\n\\usepackage{amsmath}\n\\usepackage{hyperref}\n");
        sb.Append("\\title{").Append(ResultForms.LatexEscape(doc.Name)).Append("}\n\\date{}\n\\begin{document}\n");
        if (doc.Cells.FirstOrDefault() is not TextCell t0 || t0.Markdown.Blocks.FirstOrDefault()?.Kind != MdBlockKind.Heading) sb.Append("\\maketitle\n");
        foreach (var cell in doc.Cells)
        {
            sb.Append('\n');
            if (cell.Kind != CellKind.Text && cell.Caption is { } caption)
                sb.Append("\\paragraph{").Append(ResultForms.LatexEscape(caption)).Append("}\n");
            switch (cell)
            {
                case TextCell t:
                    AppendTextLatex(sb, t, session);
                    break;
                case MathCell m:
                    AppendLinesLatex(sb, m.Lines, session);
                    break;
                case TableCell tc:
                    AppendTableLatex(sb, tc);
                    AppendLinesLatex(sb, tc.Summary, session);
                    break;
                case GraphCell g:
                    sb.Append("\\begin{itemize}\n");
                    foreach (var e in g.Expressions.Where(e => !string.IsNullOrWhiteSpace(e)))
                        sb.Append("\\item $").Append(ResultForms.InputLatex(e, g.CellSession ?? session)).Append("$\n");
                    foreach (var (name, value) in g.Sliders)
                        sb.Append("\\item slider $").Append(name).Append(" = ").Append(Format(value)).Append("$\n");
                    sb.Append("\\end{itemize}\n");
                    break;
                case CodeCell c:
                    sb.Append("\\begin{verbatim}\n").Append(c.Code.TrimEnd('\n')).Append("\n\\end{verbatim}\n");
                    var output = c.LastRun is not null ? c.Output.PlainText() : c.PreviousOutput;
                    if (!string.IsNullOrWhiteSpace(output)) sb.Append("Output:\n\\begin{verbatim}\n").Append(output.TrimEnd('\n')).Append("\n\\end{verbatim}\n");
                    break;
            }
        }
        sb.Append("\n\\end{document}\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Markdown parts

    private static void AppendLines(StringBuilder sb, IReadOnlyList<MathLine> lines, CalcSession session)
    {
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)))
        {
            sb.Append("$$").Append(ResultForms.LineLatex(line.Text, line.Result, line.DecimalFirst, session)).Append("$$\n");
            if (line.Result is { IsError: true } err) sb.Append("\n> Error: ").Append(err.Result.ErrorMessage).Append('\n');
            sb.Append('\n');
        }
    }

    private static void AppendTableMarkdown(StringBuilder sb, TableCell t)
    {
        sb.Append("| |");
        for (int c = 0; c < t.Columns; c++) sb.Append(' ').Append(c + 1).Append(" |");
        sb.Append("\n|---|");
        for (int c = 0; c < t.Columns; c++) sb.Append("---|");
        sb.Append('\n');
        foreach (var r in t.Rows)
        {
            sb.Append("| ").Append(EscapeCell(r.Name)).Append(" |");
            foreach (var v in r.Values) sb.Append(' ').Append(EscapeCell(v)).Append(" |");
            sb.Append('\n');
        }
    }

    private static string EscapeCell(string s) => s.Replace("|", "\\|");

    private static string MathLatex(TextCell cell, string source, bool live, int index, CalcSession session)
    {
        if (!live) return ResultForms.InputLatex(source, session);
        if (index < cell.LiveValues.Count && cell.LiveValues[index] is { Value: not null } v)
            return ResultForms.ValueLatex(ResultForms.LiveDisplay(v), session);
        return ResultForms.TextLatex("?");
    }

    /// <summary>
    /// Replaces every $maths$ and $=live$ segment of Markdown source (outside code) with the result of
    /// <paramref name="replace"/>(source, isLive, liveIndex).
    /// </summary>
    public static string RewriteMath(string markdown, Func<string, bool, int, string> replace)
    {
        var sb = new StringBuilder(markdown.Length + 16);
        int live = 0;
        bool fenced = false;
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            var line = lines[li];
            if (li > 0) sb.Append('\n');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                sb.Append(line);
                continue;
            }
            if (fenced)
            {
                sb.Append(line);
                continue;
            }
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    sb.Append(c).Append(line[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '`')
                {
                    int end = line.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        sb.Append(line, i, end - i + 1);
                        i = end + 1;
                        continue;
                    }
                }
                if (c == '$')
                {
                    bool display = i + 1 < line.Length && line[i + 1] == '$';
                    int start = i + (display ? 2 : 1);
                    int end = start;
                    while (end < line.Length && !(line[end] == '$' && (!display || (end + 1 < line.Length && line[end + 1] == '$'))))
                        end += line[end] == '\\' ? 2 : 1;
                    if (end < line.Length && end > start)
                    {
                        var src = line[start..end].Trim();
                        bool isLive = src.StartsWith('=');
                        sb.Append(replace(isLive ? src[1..].Trim() : src, isLive, isLive ? live : -1));
                        if (isLive) live++;
                        i = end + (display ? 2 : 1);
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ LaTeX parts

    private static void AppendTextLatex(StringBuilder sb, TextCell t, CalcSession session)
    {
        string? openList = null;
        void CloseList()
        {
            if (openList is null) return;
            sb.Append("\\end{").Append(openList).Append("}\n");
            openList = null;
        }
        foreach (var b in t.Markdown.Blocks)
        {
            string? list = b.Kind switch { MdBlockKind.Bullet => "itemize", MdBlockKind.Numbered => "enumerate", _ => null };
            if (list != openList) CloseList();
            switch (b.Kind)
            {
                case MdBlockKind.Heading:
                    sb.Append(b.Level switch { 1 => "\\section*{", 2 => "\\subsection*{", _ => "\\subsubsection*{" })
                      .Append(InlinesLatex(t, b.Inlines, session)).Append("}\n");
                    break;
                case MdBlockKind.Paragraph:
                    sb.Append(InlinesLatex(t, b.Inlines, session)).Append("\n\n");
                    break;
                case MdBlockKind.Bullet or MdBlockKind.Numbered:
                    if (openList is null)
                    {
                        openList = list;
                        sb.Append("\\begin{").Append(list).Append("}\n");
                    }
                    sb.Append("\\item ").Append(InlinesLatex(t, b.Inlines, session)).Append('\n');
                    break;
                case MdBlockKind.Quote:
                    sb.Append("\\begin{quote}\n").Append(InlinesLatex(t, b.Inlines, session)).Append("\n\\end{quote}\n");
                    break;
                case MdBlockKind.Code:
                    sb.Append("\\begin{verbatim}\n").Append(b.Raw).Append("\n\\end{verbatim}\n");
                    break;
                case MdBlockKind.Rule:
                    sb.Append("\\noindent\\rule{\\linewidth}{0.4pt}\n");
                    break;
            }
        }
        CloseList();
    }

    private static string InlinesLatex(TextCell cell, IReadOnlyList<MdInline> inlines, CalcSession session)
    {
        var sb = new StringBuilder();
        foreach (var i in inlines)
        {
            string s = i.Kind switch
            {
                MdInlineKind.Text => ResultForms.LatexEscape(i.Text),
                MdInlineKind.Code => "\\texttt{" + ResultForms.LatexEscape(i.Text) + "}",
                MdInlineKind.Math => "$" + ResultForms.InputLatex(i.Text, session) + "$",
                MdInlineKind.Live => "$" + MathLatex(cell, i.Text, true, i.LiveIndex, session) + "$",
                MdInlineKind.Link => "\\href{" + i.Url + "}{" + ResultForms.LatexEscape(i.Text) + "}",
                _ => "",
            };
            if (i.Bold) s = "\\textbf{" + s + "}";
            if (i.Italic) s = "\\emph{" + s + "}";
            sb.Append(s);
        }
        return sb.ToString();
    }

    private static void AppendLinesLatex(StringBuilder sb, IReadOnlyList<MathLine> lines, CalcSession session)
    {
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)))
            sb.Append("\\[ ").Append(ResultForms.LineLatex(line.Text, line.Result, line.DecimalFirst, session)).Append(" \\]\n");
    }

    private static void AppendTableLatex(StringBuilder sb, TableCell t)
    {
        sb.Append("\\begin{tabular}{l|").Append(new string('r', t.Columns)).Append("}\n");
        sb.Append(string.Join(" & ", new[] { "" }.Concat(Enumerable.Range(1, t.Columns).Select(c => c.ToString(System.Globalization.CultureInfo.InvariantCulture))))).Append(" \\\\\n\\hline\n");
        foreach (var r in t.Rows)
            sb.Append(string.Join(" & ", new[] { "$" + r.Name + "$" }.Concat(r.Values.Select(ResultForms.LatexEscape)))).Append(" \\\\\n");
        sb.Append("\\end{tabular}\n");
    }

    private static string Format(double v) => v.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
}
