using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Calcy.UI.Math.Layout;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Rendering;

/// <summary>Draws arranged <see cref="MathBox"/> trees with chunky, square-capped strokes.</summary>
public static class MathRenderer
{
    /// <summary>Draws a box tree. Boxes must have been arranged (<see cref="MathLayout.Arrange"/>).</summary>
    /// <param name="dc">Drawing context.</param>
    /// <param name="box">The root box.</param>
    /// <param name="font">The font used for measuring.</param>
    /// <param name="brushFor">Maps a colour role to a brush.</param>
    /// <param name="showEmptySlots">Draw empty slots as dashed boxes.</param>
    public static void Draw(DrawingContext dc, MathBox box, AvaloniaMathFont font, Func<MathColorRole, IBrush> brushFor, bool showEmptySlots = true)
    {
        switch (box)
        {
            case MathGlyphBox g:
            {
                var ft = font.GetFormattedText(g.Text, g.FontSize, g.Italic);
                ft.SetForegroundBrush(brushFor(g.Color));
                dc.DrawText(ft, new Point(g.Origin.X, g.Origin.Y - ft.Baseline));
                return;
            }
            case MathRuleBox r:
                dc.FillRectangle(brushFor(r.Color), new Rect(r.Origin.X, r.Origin.Y - r.Ascent, r.Width, r.Ascent + r.Descent), 0);
                return;
            case MathShapeBox s:
                DrawShape(dc, s, brushFor(s.Color));
                return;
            case MathSlotBox slot:
                if (showEmptySlots) DrawSlot(dc, slot, font, brushFor(MathColorRole.Muted));
                return;
        }
        foreach (var c in box.Children) Draw(dc, c, font, brushFor, showEmptySlots);
    }

    private static void DrawSlot(DrawingContext dc, MathSlotBox slot, AvaloniaMathFont font, IBrush brush)
    {
        double th = System.Math.Max(1, System.Math.Round(slot.FontSize * 0.06));
        var b = slot.Bounds;
        var rect = new Rect(b.X + th / 2, b.Y + th / 2, System.Math.Max(0, b.Width - th), System.Math.Max(0, b.Height - th));
        var pen = new ImmutablePen(ToImmutable(brush), th, new ImmutableDashStyle([2, 2], 0), PenLineCap.Flat, PenLineJoin.Miter, 10);
        dc.DrawRectangle(null, pen, rect, 0, 0, default);
        if (slot.Placeholder is { } text)
        {
            var ft = font.GetFormattedText(text, slot.FontSize * 0.8, false);
            ft.SetForegroundBrush(brush);
            dc.DrawText(ft, new Point(b.X + (b.Width - ft.WidthIncludingTrailingWhitespace) / 2, slot.Origin.Y - ft.Baseline));
        }
    }

    private static IImmutableBrush ToImmutable(IBrush brush) => brush as IImmutableBrush ?? (brush is ISolidColorBrush s ? new ImmutableSolidColorBrush(s) : new ImmutableSolidColorBrush(Colors.Black));

    private static void DrawShape(DrawingContext dc, MathShapeBox s, IBrush brush)
    {
        double th = s.Thickness;
        double l = s.Origin.X, t = s.Origin.Y - s.Ascent, w = s.Width, h = s.Height;
        double i = th / 2;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            Point P(double fx, double fy) => new(l + fx * w, t + fy * h);
            Point Q(double x, double y) => new(x, y);
            switch (s.Shape)
            {
                case MathShape.Radical:
                    g.BeginFigure(Q(l + i, t + 0.6 * h), false);
                    g.LineTo(Q(l + 0.24 * w, t + 0.5 * h), true);
                    g.LineTo(Q(l + 0.55 * w, t + h - i), true);
                    g.LineTo(Q(l + w, t + i), true);
                    g.EndFigure(false);
                    break;
                case MathShape.ParenLeft or MathShape.ParenRight:
                {
                    bool left = s.Shape == MathShape.ParenLeft;
                    double x0 = left ? l + w * 0.85 : l + w * 0.15, x1 = left ? l + w * 0.12 + i : l + w * 0.88 - i;
                    g.BeginFigure(Q(x0, t + i), false);
                    g.CubicBezierTo(Q(x1, t + 0.22 * h), Q(x1, t + 0.78 * h), Q(x0, t + h - i), true);
                    g.EndFigure(false);
                    break;
                }
                case MathShape.BracketLeft or MathShape.BracketRight:
                {
                    bool left = s.Shape == MathShape.BracketLeft;
                    double outer = left ? l + w * 0.9 : l + w * 0.1, inner = left ? l + w * 0.3 : l + w * 0.7;
                    g.BeginFigure(Q(outer, t + i), false);
                    g.LineTo(Q(inner, t + i), true);
                    g.LineTo(Q(inner, t + h - i), true);
                    g.LineTo(Q(outer, t + h - i), true);
                    g.EndFigure(false);
                    break;
                }
                case MathShape.BraceLeft or MathShape.BraceRight:
                {
                    bool left = s.Shape == MathShape.BraceLeft;
                    double X(double fx) => left ? l + fx * w : l + (1 - fx) * w;
                    double top = t + i, bottom = t + h - i, mid = t + h / 2;
                    double r = System.Math.Min(0.4 * w, 0.12 * h);
                    g.BeginFigure(Q(X(0.95), top), false);
                    g.QuadraticBezierTo(Q(X(0.5), top), Q(X(0.5), top + r), true);
                    g.LineTo(Q(X(0.5), mid - r), true);
                    g.QuadraticBezierTo(Q(X(0.5), mid), Q(X(0.05), mid), true);
                    g.QuadraticBezierTo(Q(X(0.5), mid), Q(X(0.5), mid + r), true);
                    g.LineTo(Q(X(0.5), bottom - r), true);
                    g.QuadraticBezierTo(Q(X(0.5), bottom), Q(X(0.95), bottom), true);
                    g.EndFigure(false);
                    break;
                }
                case MathShape.Bar:
                    g.BeginFigure(Q(l + w / 2, t + i), false);
                    g.LineTo(Q(l + w / 2, t + h - i), true);
                    g.EndFigure(false);
                    break;
                case MathShape.Sigma:
                    g.BeginFigure(Q(l + w - i, t + 0.2 * h), false);
                    g.LineTo(Q(l + w - i, t + i), true);
                    g.LineTo(Q(l + i, t + i), true);
                    g.LineTo(Q(l + 0.55 * w, t + 0.5 * h), true);
                    g.LineTo(Q(l + i, t + h - i), true);
                    g.LineTo(Q(l + w - i, t + h - i), true);
                    g.LineTo(Q(l + w - i, t + 0.8 * h), true);
                    g.EndFigure(false);
                    break;
                case MathShape.Pi:
                    g.BeginFigure(Q(l + i, t + i), false);
                    g.LineTo(Q(l + w - i, t + i), true);
                    g.EndFigure(false);
                    g.BeginFigure(Q(l + 0.24 * w, t + i), false);
                    g.LineTo(Q(l + 0.24 * w, t + h - i), true);
                    g.EndFigure(false);
                    g.BeginFigure(Q(l + 0.76 * w, t + i), false);
                    g.LineTo(Q(l + 0.76 * w, t + h - i), true);
                    g.EndFigure(false);
                    break;
                case MathShape.Integral:
                    g.BeginFigure(P(0.98, 0.1), false);
                    g.CubicBezierTo(P(0.9, -0.02), P(0.66, 0.0), P(0.6, 0.18), true);
                    g.LineTo(P(0.4, 0.82), true);
                    g.CubicBezierTo(P(0.34, 1.0), P(0.1, 1.02), P(0.02, 0.9), true);
                    g.EndFigure(false);
                    break;
            }
        }
        var pen = new ImmutablePen(ToImmutable(brush), th, null, PenLineCap.Square, PenLineJoin.Miter, 4);
        dc.DrawGeometry(null, pen, geo);
    }
}
