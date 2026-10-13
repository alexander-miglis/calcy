using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Calcy.UI.Controls;

/// <summary>Text drawn with the built-in 5×7 pixel font. Used for the logo, panel headers, keys and the LCD.</summary>
public class PixelText : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PixelText, string?>(nameof(Text));

    /// <summary>Size of one font pixel in device-independent pixels.</summary>
    public static readonly StyledProperty<double> PixelSizeProperty =
        AvaloniaProperty.Register<PixelText, double>(nameof(PixelSize), 2);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<PixelText>();

    /// <summary>Optional hard shadow drawn one pixel cell down and right.</summary>
    public static readonly StyledProperty<IBrush?> ShadowBrushProperty =
        AvaloniaProperty.Register<PixelText, IBrush?>(nameof(ShadowBrush));

    public static readonly StyledProperty<double> LetterSpacingProperty =
        AvaloniaProperty.Register<PixelText, double>(nameof(LetterSpacing), 1);

    static PixelText()
    {
        AffectsMeasure<PixelText>(TextProperty, PixelSizeProperty, LetterSpacingProperty);
        AffectsRender<PixelText>(ForegroundProperty, ShadowBrushProperty);
    }

    public PixelText()
    {
        this.GetObservable(TextProperty).Subscribe(new Observer(t => AutomationProperties.SetName(this, t ?? "")));
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double PixelSize
    {
        get => GetValue(PixelSizeProperty);
        set => SetValue(PixelSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? ShadowBrush
    {
        get => GetValue(ShadowBrushProperty);
        set => SetValue(ShadowBrushProperty, value);
    }

    public double LetterSpacing
    {
        get => GetValue(LetterSpacingProperty);
        set => SetValue(LetterSpacingProperty, value);
    }

    private double Advance => (PixelFont.Width + LetterSpacing) * PixelSize;

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? "";
        if (text.Length == 0) return new Size(0, PixelFont.Height * PixelSize);
        double w = text.Length * Advance - LetterSpacing * PixelSize;
        double extra = ShadowBrush is null ? 0 : PixelSize;
        return new Size(w + extra, PixelFont.Height * PixelSize + extra);
    }

    public override void Render(DrawingContext context)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text)) return;
        double px = PixelSize;
        if (ShadowBrush is { } shadow) Draw(context, text, shadow, px, px);
        Draw(context, text, Foreground ?? Brushes.Black, 0, 0);
    }

    private void Draw(DrawingContext ctx, string text, IBrush brush, double dx, double dy)
    {
        double px = PixelSize;
        double x0 = dx;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var ch in text)
            {
                var rows = PixelFont.Glyph(ch);
                for (int r = 0; r < PixelFont.Height; r++)
                {
                    byte bits = rows[r];
                    int c = 0;
                    while (c < PixelFont.Width)
                    {
                        if ((bits & (1 << (PixelFont.Width - 1 - c))) == 0) { c++; continue; }
                        // Merge runs of lit pixels into one rectangle.
                        int start = c;
                        while (c < PixelFont.Width && (bits & (1 << (PixelFont.Width - 1 - c))) != 0) c++;
                        double x = x0 + start * px, y = dy + r * px;
                        double w = (c - start) * px;
                        g.BeginFigure(new Point(x, y), true);
                        g.LineTo(new Point(x + w, y));
                        g.LineTo(new Point(x + w, y + px));
                        g.LineTo(new Point(x, y + px));
                        g.EndFigure(true);
                    }
                }
                x0 += Advance;
            }
        }
        ctx.DrawGeometry(brush, null, geometry);
    }

    private sealed class Observer(Action<string?> onNext) : IObserver<string?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(string? value) => onNext(value);
    }
}
