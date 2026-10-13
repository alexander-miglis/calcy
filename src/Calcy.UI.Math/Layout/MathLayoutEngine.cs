using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Layout;

/// <summary>Options for <see cref="MathLayoutEngine"/>.</summary>
public sealed record MathLayoutOptions
{
    /// <summary>Font size of the top-level text.</summary>
    public double FontSize { get; init; } = 20;

    /// <summary>Draw single-letter variables in italics (off by default for a clean monospace look).</summary>
    public bool ItalicVariables { get; init; }

    /// <summary>Draw empty slots as small dashed boxes.</summary>
    public bool ShowEmptySlots { get; init; } = true;

    /// <summary>Smallest font size used for scripts.</summary>
    public double MinFontSize { get; init; } = 7;
}

/// <summary>
/// Lays out the structured model as boxes (rows, fractions, scripts, radicals, growing delimiters, big operators,
/// grids). Pure layout: it only needs an <see cref="IMathTextMeasurer"/>.
/// </summary>
public sealed class MathLayoutEngine(IMathTextMeasurer measurer, MathLayoutOptions? options = null)
{
    // Levels: 0 display, 1 text, 2 script, 3 script-script, 4 and 5 nested fraction parts (gently smaller).
    private static readonly double[] LevelScale = [1.0, 1.0, 0.72, 0.58, 0.86, 0.76];

    private readonly Dictionary<double, MathFontMetrics> _metrics = [];
    private Dictionary<MathRow, MathRowBox> _rows = new(ReferenceEqualityComparer.Instance);

    /// <summary>The options in use.</summary>
    public MathLayoutOptions Options { get; } = options ?? new MathLayoutOptions();

    /// <summary>The text measurer in use.</summary>
    public IMathTextMeasurer Measurer { get; } = measurer;

    /// <summary>Lays out a root row.</summary>
    public MathLayout Layout(MathRow root)
    {
        _rows = new Dictionary<MathRow, MathRowBox>(ReferenceEqualityComparer.Instance);
        var box = LayoutRow(root, 0, isSlot: false, placeholder: null);
        var layout = new MathLayout(box, _rows);
        layout.Arrange(new Avalonia.Point(0, box.Ascent));
        return layout;
    }

    /// <summary>Lays out several rows as lines stacked vertically (used to wrap long sums).</summary>
    public MathLayout LayoutLines(IReadOnlyList<MathRow> lines, double indent, double lineGap)
    {
        _rows = new Dictionary<MathRow, MathRowBox>(ReferenceEqualityComparer.Instance);
        var all = new MathContainerBox();
        double y = 0, prevDescent = 0;
        MathRowBox? first = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var b = LayoutRow(lines[i], 0, isSlot: false, placeholder: null);
            first ??= b;
            if (i > 0) y += prevDescent + lineGap + b.Ascent;
            all.Add(b, i == 0 ? 0 : indent, y);
            prevDescent = b.Descent;
        }
        var layout = new MathLayout(first ?? LayoutRow(new MathRow(), 0, false, null), _rows, all);
        layout.Arrange(new Avalonia.Point(0, all.Ascent));
        return layout;
    }

    // ------------------------------------------------------------------ helpers

    private double Size(int level) => System.Math.Max(Options.MinFontSize, Options.FontSize * LevelScale[System.Math.Clamp(level, 0, LevelScale.Length - 1)]);

    private static bool IsScript(int level) => level is 2 or 3;

    private static int FracChild(int level) => level switch
    {
        0 => 1,
        1 => 4,
        4 or 5 => 5,
        _ => 3,
    };

    private static int ScriptLevel(int level) => IsScript(level) ? 3 : 2;

    private MathFontMetrics Metrics(double size)
    {
        if (!_metrics.TryGetValue(size, out var m))
        {
            m = Measurer.GetFontMetrics(size);
            _metrics[size] = m;
        }
        return m;
    }

    private MathGlyphBox Glyph(string text, double size, bool italic = false, MathColorRole color = MathColorRole.Ink)
    {
        var tm = Measurer.Measure(text, size, italic);
        var g = new MathGlyphBox(text, size, italic, color)
        {
            Width = tm.Width,
            Ascent = System.Math.Max(0, tm.Ascent),
            Descent = System.Math.Max(0, tm.Descent),
        };
        return g;
    }

    // ------------------------------------------------------------------ rows

    private enum Cls { None, Ord, Bin, Rel, Punct, Word, Postfix, Space, Open, Script }

    private MathRowBox LayoutRow(MathRow row, int level, bool isSlot, string? placeholder)
    {
        double f = Size(level);
        var m = Metrics(f);
        var box = new MathRowBox(row, f, level, m);
        _rows[row] = box;
        int n = row.Count;
        var caret = new double[n + 1];
        var itemBoxes = new MathBox?[n];

        if (n == 0)
        {
            if (isSlot && Options.ShowEmptySlots)
            {
                double w = placeholder is null ? 0.6 * f : System.Math.Max(0.6 * f, Measurer.Measure(placeholder, f * 0.8, false).Width + 0.3 * f);
                var slot = new MathSlotBox(row, placeholder, f) { Width = w, Ascent = m.CapHeight + 0.06 * f, Descent = 0.12 * f };
                box.Add(slot, 0, 0);
            }
            else
            {
                box.Ascent = m.CapHeight;
            }
            box.CaretX = caret;
            box.ItemBoxes = itemBoxes;
            return box;
        }

        double x = 0;
        var prev = Cls.None;
        bool scripts = IsScript(level);
        double bin = (scripts ? 0.08 : 0.2) * f, rel = (scripts ? 0.1 : 0.28) * f, punct = (scripts ? 0.12 : 0.28) * f, word = (scripts ? 0.15 : 0.3) * f;
        // Script stacking: v_0^2 puts the superscript above the subscript.
        MathBox? baseBox = null;
        (MathTemplateKind Kind, double X, double Width, MathBox Box, MathBox Inner, double Y)? lastScript = null;

        for (int i = 0; i < n; i++)
        {
            var node = row[i];
            Cls cls;
            if (node is MathChar c)
            {
                cls = MathCharInfo.Classify(c.Text) switch
                {
                    MathCharClass.Binary => Cls.Bin,
                    MathCharClass.Relation => Cls.Rel,
                    MathCharClass.Punctuation => Cls.Punct,
                    MathCharClass.Word => Cls.Word,
                    MathCharClass.Postfix => Cls.Postfix,
                    MathCharClass.Space => Cls.Space,
                    MathCharClass.Open => Cls.Open,
                    MathCharClass.Close => Cls.Ord,
                    _ => Cls.Ord,
                };
                // A sign at the start or after another operator is unary: no spacing.
                if (cls == Cls.Bin && prev is Cls.None or Cls.Bin or Cls.Rel or Cls.Punct or Cls.Word or Cls.Open) cls = Cls.Ord;
            }
            else if (node is MathTemplate { Kind: MathTemplateKind.Power or MathTemplateKind.Subscript }) cls = Cls.Script;
            else cls = Cls.Ord;

            double space = 0;
            if (prev != Cls.None)
            {
                if (prev == Cls.Bin) space += bin;
                if (prev == Cls.Rel) space += rel;
                if (prev == Cls.Punct) space += punct;
                if (prev == Cls.Word) space += word;
                if (cls == Cls.Bin) space += bin;
                if (cls == Cls.Rel) space += rel;
                if (cls == Cls.Word) space += word;
            }
            x += space;
            caret[i] = x;

            if (node is MathTemplate { Kind: MathTemplateKind.Power or MathTemplateKind.Subscript } script)
            {
                var sl = ScriptLevel(level);
                var s = LayoutRow(script[0], sl, isSlot: true, placeholder: null);
                bool sup = script.Kind == MathTemplateKind.Power;
                var bAsc = baseBox?.Ascent ?? m.XHeight;
                var bDesc = baseBox?.Descent ?? 0;
                double shift = sup
                    ? System.Math.Max(System.Math.Max(0.42 * f, bAsc - 0.3 * f), s.Descent + 0.25 * m.XHeight)
                    : System.Math.Max(System.Math.Max(0.2 * f, s.Ascent - 0.75 * m.XHeight), bDesc - 0.15 * f);
                double y = sup ? -shift : shift;
                double kern = 0.03 * f;
                double sx = x;
                if (lastScript is { } ls && ls.Kind != script.Kind && i >= 2)
                {
                    // Stack on the previous script: same x, enough vertical gap.
                    sx = ls.X;
                    double th = m.RuleThickness;
                    double supBottom, subTop;
                    if (sup)
                    {
                        supBottom = y + s.Descent;
                        subTop = ls.Y - ls.Inner.Ascent;
                        if (subTop - supBottom < 3 * th) y -= 3 * th - (subTop - supBottom);
                    }
                    else
                    {
                        supBottom = ls.Y + ls.Inner.Descent;
                        subTop = y - s.Ascent;
                        if (subTop - supBottom < 3 * th) y += 3 * th - (subTop - supBottom);
                    }
                    caret[i] = sx;
                }
                var holder = new MathContainerBox();
                holder.Add(s, kern, y);
                holder.Width = kern + s.Width + 0.04 * f;
                box.Add(holder, sx, 0);
                itemBoxes[i] = holder;
                if (lastScript is { } prevScript && prevScript.Kind != script.Kind && i >= 2)
                {
                    x = System.Math.Max(prevScript.X + prevScript.Width, sx + holder.Width);
                    lastScript = null;
                }
                else
                {
                    x = sx + holder.Width;
                    lastScript = (script.Kind, sx, holder.Width, holder, s, y);
                }
                prev = Cls.Ord;
                continue;
            }

            lastScript = null;
            MathBox b = node switch
            {
                MathChar ch => LayoutChar(ch, f),
                MathTemplate t => LayoutTemplate(t, level),
                _ => new MathSpaceBox(0),
            };
            box.Add(b, x, 0);
            itemBoxes[i] = b;
            baseBox = b;
            x += b.Width;
            prev = cls;
        }
        // Trailing space after an operator so the caret sits where the next operand goes.
        if (prev == Cls.Bin) x += bin;
        else if (prev == Cls.Rel) x += rel;
        else if (prev == Cls.Punct) x += punct;
        else if (prev == Cls.Word) x += word;
        caret[n] = x;
        box.Width = System.Math.Max(box.Width, x);
        box.Ascent = System.Math.Max(box.Ascent, m.CapHeight);
        box.CaretX = caret;
        box.ItemBoxes = itemBoxes;
        return box;
    }

    private MathBox LayoutChar(MathChar c, double f)
    {
        if (c.Text == " ") return new MathSpaceBox(0.33 * f);
        string text = MathCharInfo.DisplayText(c.Text);
        bool italic = Options.ItalicVariables && c.Text.Length == 1 && char.IsAsciiLetter(c.Text[0]) && c.Color == MathColorRole.Ink;
        var g = Glyph(text, f, italic, c.Color);
        if (italic) g.Width += 0.04 * f;
        return g;
    }

    // ------------------------------------------------------------------ templates

    private MathBox LayoutTemplate(MathTemplate t, int level)
    {
        double f = Size(level);
        var m = Metrics(f);
        switch (t.Kind)
        {
            case MathTemplateKind.Fraction:
            {
                int cl = FracChild(level);
                var num = LayoutRow(t[0], cl, true, null);
                var den = LayoutRow(t[1], cl, true, null);
                return Fraction(num, den, f, m, level);
            }
            case MathTemplateKind.Sqrt:
                return Radical(LayoutRow(t[0], level, true, null), null, f, m);
            case MathTemplateKind.NthRoot:
                return Radical(LayoutRow(t[1], level, true, null), LayoutRow(t[0], 3, true, null), f, m);
            case MathTemplateKind.Abs:
                return Delimited(LayoutRow(t[0], level, true, null), MathShape.Bar, MathShape.Bar, f, m, false);
            case MathTemplateKind.Parens:
                return Delimited(LayoutRow(t[0], level, true, null), MathShape.ParenLeft, MathShape.ParenRight, f, m, t.IsGhostClosed);
            case MathTemplateKind.Brackets:
                return Delimited(LayoutRow(t[0], level, true, null), MathShape.BracketLeft, MathShape.BracketRight, f, m, t.IsGhostClosed);
            case MathTemplateKind.Braces:
                return Delimited(LayoutRow(t[0], level, true, null), MathShape.BraceLeft, MathShape.BraceRight, f, m, t.IsGhostClosed);
            case MathTemplateKind.LogBase:
                return LogBase(t, level, f, m);
            case MathTemplateKind.Integral:
                return Integral(t, level, f, m, definite: false);
            case MathTemplateKind.DefiniteIntegral:
                return Integral(t, level, f, m, definite: true);
            case MathTemplateKind.Derivative:
                return Derivative(t, level, f, m);
            case MathTemplateKind.Sum or MathTemplateKind.Product:
                return BigOperator(t, level, f, m);
            case MathTemplateKind.Limit:
                return Limit(t, level, f, m);
            case MathTemplateKind.Matrix:
                return Matrix(t, level, f, m);
            case MathTemplateKind.Piecewise:
                return Piecewise(t, level, f, m);
            default:
                // Power and Subscript are handled by the row; a stray one is laid out as a raised row.
                return LayoutRow(t[0], ScriptLevel(level), true, null);
        }
    }

    private static MathContainerBox Fraction(MathBox num, MathBox den, double f, MathFontMetrics m, int level)
    {
        double th = m.RuleThickness;
        double pad = 0.1 * f, margin = 0.08 * f;
        double w = System.Math.Max(num.Width, den.Width) + 2 * pad;
        double gap = IsScript(level) ? System.Math.Max(th, 0.1 * f) : System.Math.Max(1.5 * th, 0.14 * f);
        double axis = m.AxisHeight;
        double u = axis + th / 2 + gap + num.Descent;
        double v = th / 2 + gap + den.Ascent - axis;
        var box = new MathContainerBox();
        box.Add(num, margin + (w - num.Width) / 2, -u);
        box.Add(den, margin + (w - den.Width) / 2, v);
        box.Add(new MathRuleBox(w, th), margin, -axis + th / 2);
        box.Width = w + 2 * margin;
        return box;
    }

    private MathContainerBox Radical(MathRowBox radicand, MathRowBox? index, double f, MathFontMetrics m)
    {
        double th = m.RuleThickness;
        double clear = th + 0.12 * f;
        double innerAsc = System.Math.Max(radicand.Ascent, m.CapHeight) + clear;
        double innerDesc = System.Math.Max(radicand.Descent, 0.12 * f) + 0.04 * f;
        double h = innerAsc + th + innerDesc;
        double signW = System.Math.Clamp(0.5 * f + 0.08 * (h - 1.1 * f), 0.5 * f, 1.0 * f);
        double x0 = 0;
        var box = new MathContainerBox();
        if (index is not null)
        {
            double ix = signW * 0.62 - index.Width;
            if (ix < 0)
            {
                x0 = -ix;
                ix = 0;
            }
            double bottom = innerDesc - 0.52 * h;
            box.Add(index, ix, bottom - index.Descent);
        }
        var sign = new MathShapeBox(MathShape.Radical, signW, innerAsc + th, innerDesc, th, MathColorRole.Ink);
        box.Add(sign, x0, 0);
        double rx = x0 + signW + 0.08 * f;
        box.Add(radicand, rx, 0);
        double end = rx + radicand.Width + 0.1 * f;
        box.Add(new MathRuleBox(end - (x0 + signW) + th / 2, th), x0 + signW - th / 2, -innerAsc);
        box.Width = end + 0.04 * f;
        return box;
    }

    private static double DelimiterWidth(MathShape shape, double h, double f) => shape switch
    {
        MathShape.ParenLeft or MathShape.ParenRight => System.Math.Clamp(0.34 * f + 0.06 * (h - 1.25 * f), 0.34 * f, 0.7 * f),
        MathShape.BracketLeft or MathShape.BracketRight => System.Math.Clamp(0.3 * f + 0.03 * (h - 1.25 * f), 0.3 * f, 0.5 * f),
        MathShape.BraceLeft or MathShape.BraceRight => System.Math.Clamp(0.42 * f + 0.05 * (h - 1.25 * f), 0.42 * f, 0.8 * f),
        _ => 0.26 * f,
    };

    private static MathContainerBox Delimited(MathBox content, MathShape left, MathShape? right, double f, MathFontMetrics m, bool ghostRight)
    {
        double axis = m.AxisHeight;
        double th = m.RuleThickness;
        double half = System.Math.Max(content.Ascent - axis, content.Descent + axis) + 0.1 * f;
        half = System.Math.Max(half, 0.62 * f);
        double h = 2 * half;
        double lw = DelimiterWidth(left, h, f);
        double gap = 0.05 * f;
        var box = new MathContainerBox();
        box.Add(new MathShapeBox(left, lw, axis + half, half - axis, th, MathColorRole.Ink), 0, 0);
        box.Add(content, lw + gap, 0);
        double x = lw + gap + content.Width + gap;
        if (right is { } r)
        {
            double rw = DelimiterWidth(r, h, f);
            box.Add(new MathShapeBox(r, rw, axis + half, half - axis, th, ghostRight ? MathColorRole.Muted : MathColorRole.Ink), x, 0);
            x += rw;
        }
        box.Width = x;
        return box;
    }

    private MathContainerBox LogBase(MathTemplate t, int level, double f, MathFontMetrics m)
    {
        var log = Glyph("log", f);
        var b = LayoutRow(t[0], ScriptLevel(level), true, null);
        var arg = Delimited(LayoutRow(t[1], level, true, null), MathShape.ParenLeft, MathShape.ParenRight, f, m, false);
        var box = new MathContainerBox();
        box.Add(log, 0, 0);
        double shift = System.Math.Max(System.Math.Max(0.22 * f, b.Ascent - 0.75 * m.XHeight), 0);
        double bx = log.Width + 0.03 * f;
        box.Add(b, bx, shift);
        box.Add(arg, bx + b.Width + 0.06 * f, 0);
        return box;
    }

    private MathContainerBox Integral(MathTemplate t, int level, double f, MathFontMetrics m, bool definite)
    {
        double axis = m.AxisHeight;
        double th = m.RuleThickness;
        var body = LayoutRow(t[definite ? 2 : 0], level, true, null);
        var v = LayoutRow(t[definite ? 3 : 1], level, true, null);
        double minH = IsScript(level) ? 1.4 * f : 2.0 * f;
        double signH = System.Math.Max(minH, 2 * System.Math.Max(body.Ascent - axis, body.Descent + axis) + 0.3 * f);
        double signW = System.Math.Clamp(0.5 * f + 0.06 * (signH - 2 * f), 0.45 * f, 0.9 * f);
        var box = new MathContainerBox();
        double signTop = -(axis + signH / 2), signBottom = signH / 2 - axis;
        box.Add(new MathShapeBox(MathShape.Integral, signW, axis + signH / 2, signH / 2 - axis, th * 1.15, MathColorRole.Ink), 0, 0);
        double right = signW;
        if (definite)
        {
            int sl = ScriptLevel(level);
            var lo = LayoutRow(t[0], sl, true, null);
            var hi = LayoutRow(t[1], sl, true, null);
            double hiX = signW + 0.08 * f, loX = signW * 0.5;
            box.Add(hi, hiX, signTop + hi.Ascent);
            box.Add(lo, loX, signBottom - lo.Descent);
            right = System.Math.Max(hiX + hi.Width, loX + lo.Width);
        }
        double bodyX = right + 0.14 * f;
        box.Add(body, bodyX, 0);
        double dX = bodyX + body.Width + 0.18 * f;
        var d = Glyph("d", f);
        box.Add(d, dX, 0);
        box.Add(v, dX + d.Width + 0.02 * f, 0);
        box.Width = dX + d.Width + 0.02 * f + v.Width + 0.06 * f;
        return box;
    }

    private MathContainerBox Derivative(MathTemplate t, int level, double f, MathFontMetrics m)
    {
        int cl = FracChild(level);
        double fs = Size(cl);
        var num = new MathContainerBox();
        num.Add(Glyph("d", fs), 0, 0);
        var den = new MathContainerBox();
        var d = Glyph("d", fs);
        den.Add(d, 0, 0);
        var v = LayoutRow(t[0], cl, true, null);
        den.Add(v, d.Width + 0.02 * fs, 0);
        var frac = Fraction(num, den, f, m, level);
        var body = LayoutRow(t[1], level, true, null);
        var box = new MathContainerBox();
        box.Add(frac, 0, 0);
        box.Add(body, frac.Width + 0.08 * f, 0);
        return box;
    }

    private MathContainerBox BigOperator(MathTemplate t, int level, double f, MathFontMetrics m)
    {
        double axis = m.AxisHeight;
        double th = m.RuleThickness;
        double opH = IsScript(level) ? 1.0 * f : 1.35 * f;
        double opW = opH * 0.8;
        int sl = ScriptLevel(level);
        double fs = Size(sl);
        var lower = new MathContainerBox();
        var v = LayoutRow(t[0], sl, true, null);
        lower.Add(v, 0, 0);
        var eq = Glyph("=", fs);
        lower.Add(eq, v.Width + 0.05 * fs, 0);
        var from = LayoutRow(t[1], sl, true, null);
        lower.Add(from, v.Width + 0.1 * fs + eq.Width, 0);
        var upper = LayoutRow(t[2], sl, true, null);
        double col = System.Math.Max(opW, System.Math.Max(lower.Width, upper.Width));
        double gap = 0.12 * f;
        var box = new MathContainerBox();
        var shape = t.Kind == MathTemplateKind.Sum ? MathShape.Sigma : MathShape.Pi;
        box.Add(new MathShapeBox(shape, opW, axis + opH / 2, opH / 2 - axis, th * 1.2, MathColorRole.Ink), (col - opW) / 2, 0);
        box.Add(upper, (col - upper.Width) / 2, -(axis + opH / 2) - gap - upper.Descent);
        box.Add(lower, (col - lower.Width) / 2, (opH / 2 - axis) + gap + lower.Ascent);
        var body = LayoutRow(t[3], level, true, null);
        box.Add(body, col + 0.16 * f, 0);
        return box;
    }

    private MathContainerBox Limit(MathTemplate t, int level, double f, MathFontMetrics m)
    {
        var lim = Glyph("lim", f);
        int sl = ScriptLevel(level);
        double fs = Size(sl);
        var below = new MathContainerBox();
        var v = LayoutRow(t[0], sl, true, null);
        below.Add(v, 0, 0);
        var arrow = Glyph("→", fs);
        below.Add(arrow, v.Width + 0.05 * fs, 0);
        var to = LayoutRow(t[1], sl, true, null);
        below.Add(to, v.Width + 0.1 * fs + arrow.Width, 0);
        double col = System.Math.Max(lim.Width, below.Width);
        var box = new MathContainerBox();
        box.Add(lim, (col - lim.Width) / 2, 0);
        box.Add(below, (col - below.Width) / 2, lim.Descent + 0.12 * f + below.Ascent);
        var body = LayoutRow(t[2], level, true, null);
        box.Add(body, col + 0.2 * f, 0);
        return box;
    }

    private MathContainerBox Grid(MathTemplate t, int level, double f, MathFontMetrics m, double colGap, double rowGap, bool center, Func<int, int, string?> placeholder)
    {
        int rows = t.Rows, cols = t.Columns;
        var cells = new MathRowBox[rows, cols];
        var colW = new double[cols];
        var rowA = new double[rows];
        var rowD = new double[rows];
        int cl = level == 0 ? 1 : level;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var b = LayoutRow(t.Cell(r, c), cl, true, placeholder(r, c));
                cells[r, c] = b;
                colW[c] = System.Math.Max(colW[c], b.Width);
                rowA[r] = System.Math.Max(rowA[r], System.Math.Max(b.Ascent, m.CapHeight));
                rowD[r] = System.Math.Max(rowD[r], System.Math.Max(b.Descent, 0.15 * f));
            }
        }
        double total = 0;
        for (int r = 0; r < rows; r++) total += rowA[r] + rowD[r];
        total += rowGap * (rows - 1);
        double top = -(m.AxisHeight + total / 2);
        var grid = new MathContainerBox();
        double y = top;
        for (int r = 0; r < rows; r++)
        {
            double baseline = y + rowA[r];
            double x = 0;
            for (int c = 0; c < cols; c++)
            {
                var b = cells[r, c];
                double cx = center ? x + (colW[c] - b.Width) / 2 : x;
                grid.Add(b, cx, baseline);
                x += colW[c] + colGap;
            }
            y += rowA[r] + rowD[r] + rowGap;
        }
        double width = colW.Sum() + colGap * (cols - 1);
        grid.Width = width;
        grid.Ascent = System.Math.Max(grid.Ascent, -top);
        grid.Descent = System.Math.Max(grid.Descent, total + top);
        return grid;
    }

    private MathContainerBox Matrix(MathTemplate t, int level, double f, MathFontMetrics m)
    {
        var grid = Grid(t, level, f, m, 0.8 * f, 0.3 * f, center: true, (_, _) => null);
        var padded = new MathContainerBox();
        padded.Add(grid, 0.3 * f, 0);
        padded.Width = grid.Width + 0.6 * f;
        return Delimited(padded, MathShape.BracketLeft, MathShape.BracketRight, f, m, false);
    }

    private MathContainerBox Piecewise(MathTemplate t, int level, double f, MathFontMetrics m)
    {
        int last = t.Rows - 1;
        var grid = Grid(t, level, f, m, 1.0 * f, 0.25 * f, center: false,
            (r, c) => c == 1 && r == last && r > 0 ? "otherwise" : null);
        var padded = new MathContainerBox();
        padded.Add(grid, 0.16 * f, 0);
        padded.Width = grid.Width + 0.3 * f;
        return Delimited(padded, MathShape.BraceLeft, null, f, m, false);
    }
}
