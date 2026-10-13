using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Layout;

/// <summary>Breaks a long top-level row into lines at + and − (and relations), for display wrapping.</summary>
public static class MathLineBreaker
{
    /// <summary>
    /// Splits the top-level row before + and − (and before relations) so each line fits in <paramref name="width"/>
    /// (lines after the first are indented by <paramref name="indent"/>). Returns null when there is nothing to break.
    /// The returned rows hold copies of the nodes.
    /// </summary>
    /// <param name="row">The laid-out row.</param>
    /// <param name="layout">Its layout (the caret stops give the item positions).</param>
    /// <param name="width">The available width.</param>
    /// <param name="indent">Indent of continuation lines.</param>
    public static List<MathRow>? BreakLines(MathRow row, MathLayout layout, double width, double indent)
    {
        var caret = layout.RootRow.CaretX;
        var candidates = new List<int>();
        for (int i = 1; i < row.Count; i++)
        {
            if (row[i] is not MathChar c) continue;
            var cls = MathCharInfo.Classify(c.Text);
            bool afterOperand = row[i - 1] is MathTemplate
                || (row[i - 1] is MathChar p && MathCharInfo.Classify(p.Text) is MathCharClass.Ordinary or MathCharClass.Postfix or MathCharClass.Close);
            bool plusMinus = cls == MathCharClass.Binary && c.Text is "+" or "-" or "−";
            if (afterOperand && (plusMinus || cls == MathCharClass.Relation)) candidates.Add(i);
        }
        if (candidates.Count == 0) return null;
        var breaks = new List<int>();
        int start = 0, last = -1;
        foreach (int c in candidates.Append(row.Count))
        {
            double w = caret[c] - caret[start] + (breaks.Count > 0 ? indent : 0);
            if (w > width && last > start)
            {
                breaks.Add(last);
                start = last;
            }
            last = c;
        }
        if (breaks.Count == 0) return null;
        var lines = new List<MathRow>();
        int s = 0;
        foreach (int b in breaks.Append(row.Count))
        {
            lines.Add(new MathRow(row.Items.Skip(s).Take(b - s).Select(n => n.Clone())));
            s = b;
        }
        return lines;
    }

}
