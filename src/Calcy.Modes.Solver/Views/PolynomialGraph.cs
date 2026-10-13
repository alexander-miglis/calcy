using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Calcy.Modes.Solver.Logic;

namespace Calcy.Modes.Solver.Views;

/// <summary>
/// The small graph beside the roots (design: axes in ink, the curve in the first graph colour, square dots at the real
/// roots). Turning points are drawn as hollow squares. Every colour is a scheme token.
/// </summary>
public sealed class PolynomialGraph : Control
{
    /// <summary>The curve.</summary>
    public static readonly StyledProperty<IBrush?> CurveBrushProperty = AvaloniaProperty.Register<PolynomialGraph, IBrush?>(nameof(CurveBrush));

    /// <summary>The axes.</summary>
    public static readonly StyledProperty<IBrush?> AxisBrushProperty = AvaloniaProperty.Register<PolynomialGraph, IBrush?>(nameof(AxisBrush));

    /// <summary>The root dots.</summary>
    public static readonly StyledProperty<IBrush?> DotBrushProperty = AvaloniaProperty.Register<PolynomialGraph, IBrush?>(nameof(DotBrush));

    /// <summary>Outlines.</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<PolynomialGraph, IBrush?>(nameof(LineBrush));

    /// <summary>The fill of hollow markers.</summary>
    public static readonly StyledProperty<IBrush?> FillBrushProperty = AvaloniaProperty.Register<PolynomialGraph, IBrush?>(nameof(FillBrush));

    /// <summary>Axis labels.</summary>
    public static readonly StyledProperty<IBrush?> LabelBrushProperty = AvaloniaProperty.Register<PolynomialGraph, IBrush?>(nameof(LabelBrush));

    private IReadOnlyList<double> _ascending = [];
    private IReadOnlyList<double> _roots = [];
    private IReadOnlyList<(double X, double Y)> _turning = [];
    private PlotWindow _window = PlotWindow.Default;

    static PolynomialGraph()
    {
        AffectsRender<PolynomialGraph>(CurveBrushProperty, AxisBrushProperty, DotBrushProperty, LineBrushProperty, FillBrushProperty, LabelBrushProperty);
    }

    /// <summary>Creates the graph with the design's colours.</summary>
    public PolynomialGraph()
    {
        ClipToBounds = true;
        this.Token(CurveBrushProperty, "Calcy.G1");
        this.Token(AxisBrushProperty, "Calcy.Ink");
        this.Token(DotBrushProperty, "Calcy.Sel");
        this.Token(LineBrushProperty, "Calcy.Line");
        this.Token(FillBrushProperty, "Calcy.Card");
        this.Token(LabelBrushProperty, "Calcy.Muted");
        Avalonia.Automation.AutomationProperties.SetName(this, "Graph of the polynomial with its real roots");
    }

    /// <inheritdoc cref="CurveBrushProperty"/>
    public IBrush? CurveBrush { get => GetValue(CurveBrushProperty); set => SetValue(CurveBrushProperty, value); }

    /// <inheritdoc cref="AxisBrushProperty"/>
    public IBrush? AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    /// <inheritdoc cref="DotBrushProperty"/>
    public IBrush? DotBrush { get => GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }

    /// <inheritdoc cref="LineBrushProperty"/>
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    /// <inheritdoc cref="FillBrushProperty"/>
    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    /// <inheritdoc cref="LabelBrushProperty"/>
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    /// <summary>The window shown.</summary>
    public PlotWindow Window => _window;

    /// <summary>Shows a polynomial (coefficients constant term first) with its real roots and turning points.</summary>
    public void Show(IReadOnlyList<double> ascending, IReadOnlyList<double> roots, IReadOnlyList<(double X, double Y)> turning)
    {
        _ascending = ascending;
        _roots = roots;
        _turning = turning;
        _window = PlotWindow.ForPolynomial(ascending, roots.Concat(turning.Select(t => t.X)), turning.Select(t => t.Y));
        InvalidateVisual();
    }

    /// <summary>Clears the graph (axes only).</summary>
    public void Clear() => Show([], [], []);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 4) return;
        var win = _window;
        double Px(double x) => (x - win.XMin) / (win.XMax - win.XMin) * w;
        double Py(double y) => h - (y - win.YMin) / (win.YMax - win.YMin) * h;

        if (AxisBrush is { } axis)
        {
            if (win.YMin <= 0 && win.YMax >= 0) context.FillRectangle(axis, new Rect(0, Math.Round(Py(0)) - 1, w, 2));
            if (win.XMin <= 0 && win.XMax >= 0) context.FillRectangle(axis, new Rect(Math.Round(Px(0)) - 1, 0, 2, h));
        }
        if (_ascending.Count > 0) Curve(context, w, h, Px, Py);
        Labels(context, w, h);
    }

    private void Curve(DrawingContext context, double w, double h, Func<double, double> px, Func<double, double> py)
    {
        var win = _window;
        if (CurveBrush is { } curve)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                int n = Math.Max(60, (int)(w / 2));
                for (int i = 0; i <= n; i++)
                {
                    double x = win.XMin + (win.XMax - win.XMin) * i / n;
                    double y = Math.Clamp(py(PolynomialMath.Evaluate(_ascending, x)), -2 * h, 3 * h);
                    if (i == 0) g.BeginFigure(new Point(px(x), y), false);
                    else g.LineTo(new Point(px(x), y));
                }
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(curve, 3, lineJoin: PenLineJoin.Bevel), geometry);
        }

        if (win.YMin <= 0 && win.YMax >= 0)
        {
            foreach (var r in _roots) Square(context, px(r), py(0), 14, DotBrush, 2);
        }
        foreach (var (x, y) in _turning) Square(context, px(x), py(y), 10, FillBrush, 2);
    }

    private void Square(DrawingContext ctx, double cx, double cy, double size, IBrush? fill, double outline)
    {
        var r = new Rect(Math.Round(cx - size / 2), Math.Round(cy - size / 2), size, size);
        if (fill is not null) ctx.FillRectangle(fill, r);
        if (LineBrush is { } line)
        {
            ctx.FillRectangle(line, new Rect(r.X, r.Y, r.Width, outline));
            ctx.FillRectangle(line, new Rect(r.X, r.Bottom - outline, r.Width, outline));
            ctx.FillRectangle(line, new Rect(r.X, r.Y, outline, r.Height));
            ctx.FillRectangle(line, new Rect(r.Right - outline, r.Y, outline, r.Height));
        }
    }

    private void Labels(DrawingContext ctx, double w, double h)
    {
        if (LabelBrush is not { } brush) return;
        var typeface = new Typeface(Ui.Font(this, "Calcy.Font.Label"), FontStyle.Normal, FontWeight.Bold);
        FormattedText Text(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, brush);
        var left = Text(MathText.Short(_window.XMin, 3));
        var right = Text(MathText.Short(_window.XMax, 3));
        // The window's left and right edges, on a patch of card colour so the axes never run through them.
        void Put(FormattedText t, double x)
        {
            var at = new Point(Math.Round(x), Math.Round(h - t.Height - 4));
            if (FillBrush is { } fill) ctx.FillRectangle(fill, new Rect(at.X - 3, at.Y - 1, t.Width + 6, t.Height + 2));
            ctx.DrawText(t, at);
        }
        Put(left, 6);
        Put(right, w - right.Width - 6);
    }
}
