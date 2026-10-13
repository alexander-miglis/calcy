using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Simple;

/// <summary>
/// LCD text in the built-in 5x7 pixel font. It uses the largest whole pixel size that fits the width
/// (so long numbers shrink instead of clipping), keeps punctuation narrow like a real LCD, and when even the
/// smallest size is too wide it drops characters from the start and shows an ellipsis.
/// </summary>
public sealed class LcdText : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LcdText, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LcdText>();

    /// <summary>Largest size of one font pixel; also sets the control's height (7 rows).</summary>
    public static readonly StyledProperty<double> MaxPixelSizeProperty =
        AvaloniaProperty.Register<LcdText, double>(nameof(MaxPixelSize), 7);

    /// <summary>Smallest pixel size before the text is trimmed.</summary>
    public static readonly StyledProperty<double> MinPixelSizeProperty =
        AvaloniaProperty.Register<LcdText, double>(nameof(MinPixelSize), 2);

    /// <summary>True to right-align (numbers), false to left-align.</summary>
    public static readonly StyledProperty<bool> AlignRightProperty =
        AvaloniaProperty.Register<LcdText, bool>(nameof(AlignRight), true);

    private const int Spacing = 1;
    private const char Ellipsis = '…';

    static LcdText()
    {
        AffectsMeasure<LcdText>(TextProperty, MaxPixelSizeProperty);
        AffectsRender<LcdText>(TextProperty, ForegroundProperty, MaxPixelSizeProperty, MinPixelSizeProperty, AlignRightProperty);
        TextProperty.Changed.AddClassHandler<LcdText>((c, e) => AutomationProperties.SetName(c, e.NewValue as string ?? ""));
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double MaxPixelSize
    {
        get => GetValue(MaxPixelSizeProperty);
        set => SetValue(MaxPixelSizeProperty, value);
    }

    public double MinPixelSize
    {
        get => GetValue(MinPixelSizeProperty);
        set => SetValue(MinPixelSizeProperty, value);
    }

    public bool AlignRight
    {
        get => GetValue(AlignRightProperty);
        set => SetValue(AlignRightProperty, value);
    }

    /// <summary>The pixel size used in the last render (for tests).</summary>
    public double RenderedPixelSize { get; private set; }

    /// <summary>The text drawn in the last render, after any trimming (for tests).</summary>
    public string RenderedText { get; private set; } = "";

    protected override Size MeasureOverride(Size availableSize)
    {
        double natural = Units(Text ?? "") * MaxPixelSize;
        double width = double.IsInfinity(availableSize.Width) ? natural : Math.Min(natural, availableSize.Width);
        return new Size(width, PixelFont.Height * MaxPixelSize);
    }

    public override void Render(DrawingContext context)
    {
        var text = Text ?? "";
        RenderedText = "";
        if (text.Length == 0) return;
        double width = Bounds.Width;

        double px = Math.Max(1, Math.Floor(MaxPixelSize));
        double min = Math.Max(1, Math.Floor(MinPixelSize));
        while (px > min && Units(text) * px > width) px--;
        if (Units(text) * px > width)
        {
            // Still too wide at the smallest size: keep the end of the text (the latest input) visible.
            while (text.Length > 1 && (Units(text) + Advance(Ellipsis)) * px > width) text = text[1..];
            text = Ellipsis + text;
        }
        RenderedPixelSize = px;
        RenderedText = text;

        double textWidth = Units(text) * px;
        double x = AlignRight ? Math.Floor(width - textWidth) : 0;
        double y = Math.Floor(Bounds.Height - PixelFont.Height * px);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var ch in text)
            {
                var (left, columns) = Columns(ch);
                var rows = PixelFont.Glyph(ch);
                for (int r = 0; r < PixelFont.Height; r++)
                {
                    int c = left;
                    while (c < left + columns)
                    {
                        if (!Lit(rows[r], c)) { c++; continue; }
                        int start = c;
                        while (c < left + columns && Lit(rows[r], c)) c++;
                        double rx = x + (start - left) * px, ry = y + r * px, rw = (c - start) * px;
                        g.BeginFigure(new Point(rx, ry), true);
                        g.LineTo(new Point(rx + rw, ry));
                        g.LineTo(new Point(rx + rw, ry + px));
                        g.LineTo(new Point(rx, ry + px));
                        g.EndFigure(true);
                    }
                }
                x += Advance(ch) * px;
            }
        }
        context.DrawGeometry(Foreground ?? Brushes.Black, null, geometry);
    }

    private static bool Lit(byte bits, int column) => (bits & (1 << (PixelFont.Width - 1 - column))) != 0;

    /// <summary>Width of the text in font pixels (no spacing after the last character).</summary>
    private static double Units(string text)
    {
        if (text.Length == 0) return 0;
        double u = 0;
        foreach (var ch in text) u += Advance(ch);
        return u - Spacing;
    }

    private static int Advance(char ch) => Columns(ch).Count + Spacing;

    /// <summary>Punctuation is drawn with only its lit columns, like the narrow dot and comma cells of an LCD.</summary>
    private static (int Left, int Count) Columns(char ch)
    {
        if (ch == ' ') return (0, 3);
        if (ch is not ('.' or ',' or ':' or ';' or '\'' or '!')) return (0, PixelFont.Width);
        var rows = PixelFont.Glyph(ch);
        int left = PixelFont.Width, right = -1;
        foreach (var bits in rows)
        {
            for (int c = 0; c < PixelFont.Width; c++)
            {
                if (!Lit(bits, c)) continue;
                left = Math.Min(left, c);
                right = Math.Max(right, c);
            }
        }
        return right < 0 ? (0, PixelFont.Width) : (left, right - left + 1);
    }
}
