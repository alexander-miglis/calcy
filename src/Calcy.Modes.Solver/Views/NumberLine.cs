using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Calcy.Core.Expressions;
using Calcy.Modes.Solver.Logic;

namespace Calcy.Modes.Solver.Views;

/// <summary>
/// The number line strip of the INEQUALITY tool (design: an ink line, the solution as a selection-coloured band,
/// open squares for end points that are not included and filled squares for those that are, labels underneath).
/// Rays that go on forever run to the edge and end in an arrow.
/// </summary>
public sealed class NumberLine : Control
{
    /// <summary>The line and closed end points.</summary>
    public static readonly StyledProperty<IBrush?> InkBrushProperty = AvaloniaProperty.Register<NumberLine, IBrush?>(nameof(InkBrush));

    /// <summary>The solution band.</summary>
    public static readonly StyledProperty<IBrush?> BandBrushProperty = AvaloniaProperty.Register<NumberLine, IBrush?>(nameof(BandBrush));

    /// <summary>Outlines.</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<NumberLine, IBrush?>(nameof(LineBrush));

    /// <summary>The inside of open end points.</summary>
    public static readonly StyledProperty<IBrush?> FillBrushProperty = AvaloniaProperty.Register<NumberLine, IBrush?>(nameof(FillBrush));

    /// <summary>Tick marks.</summary>
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<NumberLine, IBrush?>(nameof(TickBrush));

    private IntervalSet _set = IntervalSet.Empty;
    private Func<Expr, string> _label = e => e.ToString();
    private (double Min, double Max) _range = (-5, 5);

    static NumberLine()
    {
        AffectsRender<NumberLine>(InkBrushProperty, BandBrushProperty, LineBrushProperty, FillBrushProperty, TickBrushProperty);
        HeightProperty.OverrideDefaultValue<NumberLine>(96);
    }

    /// <summary>Creates the strip with the design's colours.</summary>
    public NumberLine()
    {
        ClipToBounds = true;
        this.Token(InkBrushProperty, "Calcy.Ink");
        this.Token(BandBrushProperty, "Calcy.Sel");
        this.Token(LineBrushProperty, "Calcy.Line");
        this.Token(FillBrushProperty, "Calcy.Card");
        this.Token(TickBrushProperty, "Calcy.Muted");
    }

    /// <inheritdoc cref="InkBrushProperty"/>
    public IBrush? InkBrush { get => GetValue(InkBrushProperty); set => SetValue(InkBrushProperty, value); }

    /// <inheritdoc cref="BandBrushProperty"/>
    public IBrush? BandBrush { get => GetValue(BandBrushProperty); set => SetValue(BandBrushProperty, value); }

    /// <inheritdoc cref="LineBrushProperty"/>
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    /// <inheritdoc cref="FillBrushProperty"/>
    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    /// <inheritdoc cref="TickBrushProperty"/>
    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    /// <summary>The set shown.</summary>
    public IntervalSet Set => _set;

    /// <summary>The stretch of the real line shown.</summary>
    public (double Min, double Max) Range => _range;

    /// <summary>Shows a solution set; <paramref name="label"/> prints end points, <paramref name="searched"/> is a numeric search range.</summary>
    public void Show(IntervalSet set, Func<Expr, string> label, (double Low, double High)? searched = null)
    {
        _set = set;
        _label = label;
        _range = PlotWindow.ForNumberLine(set, searched);
        Avalonia.Automation.AutomationProperties.SetName(this, set.IsEmpty ? "Number line: no solution" : "Number line: " + set.InequalityText("x", label));
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40) return;
        const double margin = 22;
        double y = Math.Round(h * 0.42);
        double left = margin, right = w - margin;
        double X(double v) => left + (v - _range.Min) / (_range.Max - _range.Min) * (right - left);

        Ticks(context, X, y, left, right);
        if (InkBrush is { } line) context.FillRectangle(line, new Rect(4, y - 1, Math.Round(w - 8), 3));

        foreach (var iv in _set.Intervals)
        {
            if (iv.IsPoint) continue;
            double a = iv.Lower is null ? 0 : X(iv.Lower.Approx);
            double b = iv.Upper is null ? w : X(iv.Upper.Approx);
            Box(context, a, y - 9, b - a, 18, BandBrush, 2);
        }
        // The line goes on forever both ways; rays of the solution run under the arrows.
        if (InkBrush is { } ink)
        {
            Arrow(context, ink, 6, y, -1);
            Arrow(context, ink, w - 6, y, 1);
        }

        var labels = new List<(double X, string Text)>();
        foreach (var iv in _set.Intervals)
        {
            if (iv.Lower is { } lo)
            {
                End(context, X(lo.Approx), y, lo.Closed || iv.IsPoint);
                labels.Add((X(lo.Approx), Label(lo)));
            }
            if (iv.Upper is { } hi && !iv.IsPoint)
            {
                End(context, X(hi.Approx), y, hi.Closed);
                labels.Add((X(hi.Approx), Label(hi)));
            }
        }
        // 0 for orientation, unless an end point label sits there already.
        if (_range.Min < 0 && _range.Max > 0 && labels.All(l => Math.Abs(l.X - X(0)) > 24)) ZeroLabel(context, X(0), y);
        Labels(context, labels, y, h);
    }

    private void ZeroLabel(DrawingContext ctx, double x, double y)
    {
        if (TickBrush is not { } brush) return;
        var typeface = new Typeface(Ui.Font(this, "Calcy.Font.Label"), FontStyle.Normal, FontWeight.Bold);
        var ft = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 12, brush);
        ctx.DrawText(ft, new Point(Math.Round(x - ft.Width / 2), Math.Round(y + 16)));
    }

    private string Label(Bound b)
    {
        var text = _label(b.Value);
        return text.Length <= 7 ? text : MathText.Short(b.Approx, 4);
    }

    private void End(DrawingContext ctx, double x, double y, bool closed) =>
        Box(ctx, x - 8, y - 8, 16, 16, closed ? InkBrush : FillBrush, 3);

    private void Box(DrawingContext ctx, double x, double y, double width, double height, IBrush? fill, double outline)
    {
        var r = new Rect(Math.Round(x), Math.Round(y), Math.Max(0, Math.Round(width)), Math.Round(height));
        if (r.Width <= 0) return;
        if (fill is not null) ctx.FillRectangle(fill, r);
        if (LineBrush is not { } line) return;
        double t = Math.Min(outline, r.Width / 2);
        ctx.FillRectangle(line, new Rect(r.X, r.Y, r.Width, t));
        ctx.FillRectangle(line, new Rect(r.X, r.Bottom - t, r.Width, t));
        ctx.FillRectangle(line, new Rect(r.X, r.Y, t, r.Height));
        ctx.FillRectangle(line, new Rect(r.Right - t, r.Y, t, r.Height));
    }

    private static void Arrow(DrawingContext ctx, IBrush ink, double tipX, double y, int direction)
    {
        // A solid pixel triangle pointing outwards: 2 px columns that grow by 2 px above and below the line.
        for (int i = 0; i < 5; i++)
        {
            double half = 1 + 2 * i;
            double x = direction > 0 ? tipX - 2 * (i + 1) : tipX + 2 * i;
            ctx.FillRectangle(ink, new Rect(Math.Round(x), y - half, 2, 2 * half + 1));
        }
    }

    private void Ticks(DrawingContext ctx, Func<double, double> x, double y, double left, double right)
    {
        if (TickBrush is not { } tick) return;
        double span = _range.Max - _range.Min;
        double step = span <= 24 ? 1 : span <= 120 ? 5 : span <= 240 ? 10 : 0;
        if (step == 0) return;
        for (double v = Math.Ceiling(_range.Min / step) * step; v <= _range.Max; v += step)
        {
            double px = Math.Round(x(v));
            if (px < left - 4 || px > right + 4) continue;
            bool zero = Math.Abs(v) < 1e-9;
            ctx.FillRectangle(tick, new Rect(px - 1, y - (zero ? 12 : 7), 2, zero ? 24 : 14));
        }
    }

    private void Labels(DrawingContext ctx, List<(double X, string Text)> labels, double y, double h)
    {
        if (InkBrush is not { } ink) return;
        var typeface = new Typeface(Ui.Font(this, "Calcy.Font.Label"), FontStyle.Normal, FontWeight.Bold);
        double lastRight = double.NegativeInfinity;
        bool lastBelow = false;
        foreach (var (x, text) in labels.OrderBy(l => l.X))
        {
            var ft = new FormattedText(MathText.Typographic(text), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, ink);
            double lx = Math.Clamp(x - ft.Width / 2, 2, Bounds.Width - ft.Width - 2);
            // Labels that would overlap the previous one go above the line instead.
            bool below = !(lx < lastRight + 6 && lastBelow);
            double ly = below ? y + 16 : y - 16 - ft.Height;
            if (ly + ft.Height > h) ly = h - ft.Height;
            ctx.DrawText(ft, new Point(Math.Round(lx), Math.Round(ly)));
            if (below)
            {
                lastRight = lx + ft.Width;
                lastBelow = true;
            }
            else lastBelow = false;
        }
    }
}
