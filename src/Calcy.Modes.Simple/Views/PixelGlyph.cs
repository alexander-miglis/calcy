using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Calcy.Modes.Simple;

/// <summary>A small pixel-art glyph defined as rows of '#' and '.', drawn in square pixels with the inherited foreground.</summary>
internal sealed class PixelGlyph : Control
{
    /// <summary>Backspace: a key-cap tag pointing left with an x, 11 by 7 pixels (the 5 by 7 font glyph is too small for a key).</summary>
    public static readonly string[] Backspace =
    [
        "..#########",
        ".#........#",
        "#...#.#...#",
        "#....#....#",
        "#...#.#...#",
        ".#........#",
        "..#########",
    ];

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<PixelGlyph>();

    public static readonly StyledProperty<double> PixelSizeProperty =
        AvaloniaProperty.Register<PixelGlyph, double>(nameof(PixelSize), 3);

    private readonly string[] _rows;

    static PixelGlyph()
    {
        AffectsMeasure<PixelGlyph>(PixelSizeProperty);
        AffectsRender<PixelGlyph>(ForegroundProperty, PixelSizeProperty);
    }

    public PixelGlyph(string[] rows)
    {
        _rows = rows;
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double PixelSize
    {
        get => GetValue(PixelSizeProperty);
        set => SetValue(PixelSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(_rows.Max(r => r.Length) * PixelSize, _rows.Length * PixelSize);

    public override void Render(DrawingContext context)
    {
        double px = PixelSize;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (int r = 0; r < _rows.Length; r++)
            {
                var row = _rows[r];
                int c = 0;
                while (c < row.Length)
                {
                    if (row[c] != '#') { c++; continue; }
                    int start = c;
                    while (c < row.Length && row[c] == '#') c++;
                    g.BeginFigure(new Point(start * px, r * px), true);
                    g.LineTo(new Point(c * px, r * px));
                    g.LineTo(new Point(c * px, (r + 1) * px));
                    g.LineTo(new Point(start * px, (r + 1) * px));
                    g.EndFigure(true);
                }
            }
        }
        context.DrawGeometry(Foreground ?? Brushes.Black, null, geometry);
    }
}
