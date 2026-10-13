using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Calcy.Core;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Math;

namespace Calcy.Modes.Notebook.Views;

/// <summary>
/// Renders the Markdown of a text cell as on the design board: large bold headings, 17 px body text with 1.5 line
/// height, lists, quotes, code, links, typeset inline maths and live values.
/// </summary>
internal sealed class MarkdownView : StackPanel
{
    public const double BodySize = 17;

    private readonly CalcSession _session;
    private readonly Action<string> _openLink;
    private readonly List<Control> _headings = [];

    public MarkdownView(CalcSession session, Action<string> openLink)
    {
        _session = session;
        _openLink = openLink;
        Spacing = 10;
    }

    /// <summary>The heading controls in order (for the outline).</summary>
    public IReadOnlyList<Control> Headings => _headings;

    public void Render(MarkdownDocument doc, IReadOnlyList<LiveValue> live)
    {
        Children.Clear();
        _headings.Clear();
        foreach (var b in doc.Blocks)
        {
            Control c = b.Kind switch
            {
                MdBlockKind.Heading => Heading(b, live),
                MdBlockKind.Bullet or MdBlockKind.Numbered => ListItem(b, live),
                MdBlockKind.Quote => Quote(b, live),
                MdBlockKind.Code => CodeBlock(b),
                MdBlockKind.Rule => Ui.Rule("Calcy.Muted", 2),
                _ => Paragraph(b.Inlines, live, BodySize),
            };
            if (b.Kind == MdBlockKind.Heading) _headings.Add(c);
            Children.Add(c);
        }
    }

    private Control Heading(MdBlock b, IReadOnlyList<LiveValue> live)
    {
        double size = b.Level switch { 1 => 40, 2 => 26, 3 => 20, _ => 18 };
        var t = Paragraph(b.Inlines, live, size, bold: true);
        t.LineHeight = double.NaN;
        if (b.Level == 1) t.Margin = new Thickness(0, 0, 0, 2);
        return t;
    }

    private SelectableTextBlock Paragraph(IReadOnlyList<MdInline> inlines, IReadOnlyList<LiveValue> live, double size, bool bold = false)
    {
        // Typeset maths is taller than a line of text, so lines that hold it take their natural height.
        bool tall = inlines.Any(i => i.Kind is MdInlineKind.Math or MdInlineKind.Live);
        var t = new SelectableTextBlock
        {
            FontSize = size,
            LineHeight = tall ? double.NaN : size * 1.5,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Inlines = [],
        };
        t.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Body").Res(TextBlock.ForegroundProperty, "Calcy.Ink").Res(SelectableTextBlock.SelectionBrushProperty, "Calcy.Sel");
        foreach (var i in inlines) t.Inlines!.Add(Inline(i, live, size));
        return t;
    }

    private Inline Inline(MdInline i, IReadOnlyList<LiveValue> live, double size)
    {
        switch (i.Kind)
        {
            case MdInlineKind.Code:
            {
                var r = new Run(i.Text) { FontSize = size * 0.9 };
                r.Res(TextElement.FontFamilyProperty, "Calcy.Font.Math").Res(TextElement.BackgroundProperty, "Calcy.Key");
                return r;
            }
            case MdInlineKind.Math:
            {
                var m = new MathView { Text = i.Text, Session = _session, FontSize = size * 1.05, PreserveStructure = true, Margin = new Thickness(2, 0) };
                ToolTip.SetTip(m, i.Text);
                return new InlineUIContainer(new InlineMath(m, m)) { BaselineAlignment = BaselineAlignment.Baseline };
            }
            case MdInlineKind.Live:
            {
                var (box, math) = LiveBox(i, live, size);
                return new InlineUIContainer(new InlineMath(box, math)) { BaselineAlignment = BaselineAlignment.Baseline };
            }
            case MdInlineKind.Link:
            {
                var link = new TextBlock
                {
                    Text = i.Text,
                    FontSize = size,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontWeight = i.Bold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = i.Italic ? FontStyle.Italic : FontStyle.Normal,
                    // Line spacing is inherited from the paragraph; the link is one line and must not grow with it.
                    LineSpacing = 0,
                    LineHeight = double.NaN,
                };
                link.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Body").Res(TextBlock.ForegroundProperty, "Calcy.G1");
                ToolTip.SetTip(link, i.Url);
                Avalonia.Automation.AutomationProperties.SetName(link, "Link " + i.Text);
                var url = i.Url ?? i.Text;
                link.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(link).Properties.IsLeftButtonPressed) return;
                    e.Handled = true;
                    _openLink(url);
                };
                return new InlineUIContainer(new InlineMath(link, null)) { BaselineAlignment = BaselineAlignment.Baseline };
            }
            default:
                return new Run(i.Text)
                {
                    FontWeight = i.Bold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = i.Italic ? FontStyle.Italic : FontStyle.Normal,
                };
        }
    }

    private (Control Box, MathView? Math) LiveBox(MdInline i, IReadOnlyList<LiveValue> live, double size)
    {
        var v = i.LiveIndex >= 0 && i.LiveIndex < live.Count ? live[i.LiveIndex] : null;
        Control content;
        MathView? math = null;
        if (v is { Value: not null })
        {
            math = new MathView { Expression = ResultForms.LiveDisplay(v), Session = _session, FontSize = size * 1.05 };
            if (v.Tainted) math.Res(MathView.ForegroundProperty, "Calcy.Muted");
            content = math;
        }
        else
        {
            content = Ui.Mono(v is null ? "..." : "?", size, v?.Error is null ? "Calcy.Muted" : "Calcy.Err", bold: true);
        }
        var box = new Border { Child = content, BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(2, 0), Margin = new Thickness(2, 0) };
        box.Res(Border.BorderBrushProperty, v?.Error is not null ? "Calcy.Err" : "Calcy.Head");
        ToolTip.SetTip(box, v?.Error is { } err ? $"= {i.Text}: {err}" : $"Live value of {i.Text}" + (v?.Tainted == true ? " (waiting for a code cell to run)" : ""));
        return (box, math);
    }

    /// <summary>
    /// Hosts typeset maths inside a line of text so that the maths baseline sits on the text baseline: the line puts
    /// the bottom of the box on the baseline, and the box is then drawn lower by the depth of the maths below its own baseline.
    /// </summary>
    private sealed class InlineMath : Decorator
    {
        private readonly MathView? _math;
        private double _depth = -1;

        public InlineMath(Control child, MathView? math)
        {
            Child = child;
            _math = math;
            ClipToBounds = false;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = base.ArrangeOverride(finalSize);
            double depth = _math?.CurrentLayout is { } layout ? layout.Descent * _math.ActualScale : 0;
            if (Child is Border b) depth += b.BorderThickness.Bottom + b.Padding.Bottom;
            else if (Child is TextBlock tb && tb.TextLayout is { } tl) depth = Math.Max(0, tb.Bounds.Height - tl.Baseline);
            depth = Math.Round(Math.Min(depth, 60));
            if (depth == _depth) return size;
            _depth = depth;
            RenderTransform = depth > 0.5 ? new TranslateTransform(0, depth) : null;
            // The text lays the box out above the baseline; extra line spacing keeps the lowered maths off the next line
            // (the text's own descent already leaves part of the room).
            if (this.FindAncestorOfType<TextBlock>() is { } text)
            {
                double spacing = Math.Max(0, Math.Round(depth - 0.2 * text.FontSize));
                if (spacing > text.LineSpacing + 0.5)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (spacing > text.LineSpacing + 0.5) text.LineSpacing = spacing;
                    }, DispatcherPriority.Background);
            }
            return size;
        }
    }

    private Control ListItem(MdBlock b, IReadOnlyList<LiveValue> live)
    {
        var text = Paragraph(b.Inlines, live, BodySize);
        Control marker;
        if (b.Kind == MdBlockKind.Bullet)
        {
            var square = new Border { Width = 7, Height = 7, Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            square.Res(Border.BackgroundProperty, "Calcy.Ink");
            marker = square;
        }
        else
        {
            var n = Ui.Body(b.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", BodySize);
            n.FontWeight = FontWeight.Bold;
            n.VerticalAlignment = VerticalAlignment.Top;
            n.LineHeight = BodySize * 1.5;
            marker = n;
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(6 + 20 * b.Level, -4, 0, -4) };
        marker.Margin = new Thickness(0, marker.Margin.Top, 10, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(marker);
        grid.Children.Add(text);
        return grid;
    }

    private Control Quote(MdBlock b, IReadOnlyList<LiveValue> live)
    {
        var box = new Border { Child = Paragraph(b.Inlines, live, BodySize), BorderThickness = new Thickness(4, 0, 0, 0), Padding = new Thickness(12, 6) };
        box.Res(Border.BorderBrushProperty, "Calcy.Sel").Res(Border.BackgroundProperty, "Calcy.Card");
        return box;
    }

    private static Control CodeBlock(MdBlock b)
    {
        var t = new SelectableTextBlock { Text = b.Raw, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        t.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Math").Res(TextBlock.ForegroundProperty, "Calcy.Ink");
        return Ui.Box(t, "Calcy.Card", 2, new Thickness(10, 8));
    }
}
