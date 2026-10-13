using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Calcy.Graphing;

namespace Calcy.UI.Graph;

public partial class GraphCanvas
{
    private const double CurveWidth = 4;
    private const double BoundaryWidth = 3;
    private const double PointSize = 12;
    private const double PoiSize = 10;

    /// <summary>Opacity of the hatched shading of inequalities.</summary>
    public const double ShadeOpacity = 0.6;

    private readonly Dictionary<string, object?> _resources = new();
    private readonly Dictionary<(IBrush, double, bool), Pen> _pens = new();
    private readonly Dictionary<IBrush, IBrush> _hatches = new();

    private object? Resource(string key)
    {
        if (_resources.TryGetValue(key, out var v)) return v;
        this.TryFindResource(key, ActualThemeVariant, out v);
        _resources[key] = v;
        return v;
    }

    /// <summary>A scheme brush ("Ink", "G1", ...).</summary>
    private IBrush Token(string token) => Resource("Calcy." + token) as IBrush ?? Fallback(token);

    private static IBrush Fallback(string token) => token switch
    {
        "Card" => Brushes.White,
        "Grid" => Brushes.Gainsboro,
        "Muted" => Brushes.Gray,
        "Sel" => Brushes.Gold,
        "Key" => Brushes.Silver,
        "G1" => Brushes.Blue,
        "G2" => Brushes.Red,
        "G3" => Brushes.Green,
        "G4" => Brushes.Purple,
        "G5" => Brushes.Olive,
        "Err" => Brushes.Red,
        _ => Brushes.Black,
    };

    /// <summary>The ink of a colour slot (0 to 4 map to Calcy.G1 to Calcy.G5).</summary>
    public IBrush InkBrush(int colorIndex) => Token("G" + (((colorIndex % 5) + 5) % 5 + 1));

    private FontFamily FontFamilyOf(string key) => Resource(key) as FontFamily ?? FontFamily.Default;

    private Typeface LabelFace => new(FontFamilyOf("Calcy.Font.Label"));
    private Typeface MathFace(bool bold = false) => new(FontFamilyOf("Calcy.Font.Math"), FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal);

    private Pen PenOf(IBrush brush, double width, bool dashed = false)
    {
        if (_pens.TryGetValue((brush, width, dashed), out var pen)) return pen;
        pen = new Pen(brush, width, dashed ? new DashStyle([10 / width, 6 / width], 0) : null,
            dashed ? PenLineCap.Flat : PenLineCap.Round, PenLineJoin.Round);
        _pens[(brush, width, dashed)] = pen;
        return pen;
    }

    /// <summary>The 8-bit hatch used for shading: 2 px steps on an 8 px diagonal, in the item's ink.</summary>
    private IBrush Hatch(IBrush ink)
    {
        if (_hatches.TryGetValue(ink, out var h)) return h;
        h = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            SourceRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            Opacity = ShadeOpacity,
            Drawing = new GeometryDrawing
            {
                Brush = ink,
                Geometry = Geometry.Parse("M0 6h2v2H0z M2 4h2v2H2z M4 2h2v2H4z M6 0h2v2H6z"),
            },
        };
        _hatches[ink] = h;
        return h;
    }

    private FormattedText Text(string s, Typeface face, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush);

    /// <inheritdoc />
    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return;
        if (_cache is null && !_sampling && Items is { Count: > 0 })
        {
            // First frame: sample synchronously so the graph never appears empty.
            _cache = Compute(Snapshot(), CancellationToken.None);
            SchedulePoints();
        }
        var view = new ViewTransform(Viewport, size.Width, size.Height);
        double w = size.Width, h = size.Height;
        using var clip = ctx.PushClip(new Rect(size));
        ctx.FillRectangle(Token("Card"), new Rect(size));

        var xTicks = Ticks.Generate(Viewport.XMin, Viewport.XMax, w, TickStyle);
        var yTicks = Ticks.Generate(Viewport.YMin, Viewport.YMax, h, Graphing.TickStyle.Decimal, 44);
        if (ShowGrid)
        {
            var grid = PenOf(Token("Grid"), 1);
            foreach (var x in MinorLines(Viewport.XMin, Viewport.XMax, w, TickStyle, Ticks.DefaultMinPixels))
            {
                double px = Math.Round(view.ToPixelX(x)) + 0.5;
                ctx.DrawLine(grid, new Point(px, 0), new Point(px, h));
            }
            foreach (var y in MinorLines(Viewport.YMin, Viewport.YMax, h, Graphing.TickStyle.Decimal, 44))
            {
                double py = Math.Round(view.ToPixelY(y)) + 0.5;
                ctx.DrawLine(grid, new Point(0, py), new Point(w, py));
            }
            foreach (var t in xTicks)
            {
                double x = Math.Round(view.ToPixelX(t.Value)) + 0.5;
                ctx.DrawLine(grid, new Point(x, 0), new Point(x, h));
            }
            foreach (var t in yTicks)
            {
                double y = Math.Round(view.ToPixelY(t.Value)) + 0.5;
                ctx.DrawLine(grid, new Point(0, y), new Point(w, y));
            }
        }

        var geos = _cache?.Geometry ?? [];
        // Shading first, so curves and axes stay on top.
        foreach (var g in geos) DrawFills(ctx, g, view);
        DrawAnnotations(ctx, view, shadesOnly: true);

        double ax = view.ToPixelX(0), ay = view.ToPixelY(0);
        var axis = PenOf(Token("Ink"), 2);
        if (ay >= -1 && ay <= h + 1) ctx.DrawLine(axis, new Point(0, Math.Round(ay)), new Point(w, Math.Round(ay)));
        if (ax >= -1 && ax <= w + 1) ctx.DrawLine(axis, new Point(Math.Round(ax), 0), new Point(Math.Round(ax), h));
        if (ShowGrid) DrawTickLabels(ctx, view, xTicks, yTicks, ax, ay);

        foreach (var g in geos) DrawCurves(ctx, g, view);
        foreach (var g in geos) DrawPoints(ctx, g, view);
        DrawAnnotations(ctx, view, shadesOnly: false);
        DrawPointsOfInterest(ctx, view);
        DrawTrace(ctx, view);
        DrawTooltip(ctx, view);
        DrawBoxZoom(ctx);
        if (MayBeIncomplete)
        {
            var warn = Text("Some curves may be incomplete", LabelFace, 11, Token("Err"));
            ctx.DrawText(warn, new Point(8, h - warn.Height - 6));
        }
        if (IsFocused && _keyboardFocus)
        {
            var pen = PenOf(Token("Ink"), 2, dashed: true);
            ctx.DrawRectangle(null, pen, new Rect(2, 2, w - 4, h - 4));
        }
    }

    /// <summary>Grid lines between labelled ticks: halves of 2-steps and fifths of 5-steps (whole units), halves of π steps.</summary>
    private static IEnumerable<double> MinorLines(double min, double max, double pixels, TickStyle style, double minPixels)
    {
        double step = Ticks.StepFor(min, max, pixels, style, minPixels);
        double minor;
        if (style == Graphing.TickStyle.Pi) minor = step / 2;
        else
        {
            double mantissa = step / Math.Pow(10, Math.Floor(Math.Log10(step) + 1e-9));
            minor = Math.Abs(mantissa - 2) < 1e-6 ? step / 2 : Math.Abs(mantissa - 5) < 1e-6 ? step / 5 : 0;
        }
        if (minor <= 0 || minor * pixels / (max - min) < 10) yield break;
        double first = Math.Ceiling(min / minor - 1e-9), last = Math.Floor(max / minor + 1e-9);
        if (last - first > 1000) yield break;
        for (double k = first; k <= last; k++)
        {
            double v = k * minor;
            double r = v / step;
            if (Math.Abs(r - Math.Round(r)) < 1e-6) continue;
            yield return v;
        }
    }

    private void DrawTickLabels(DrawingContext ctx, ViewTransform view, IReadOnlyList<Tick> xTicks, IReadOnlyList<Tick> yTicks, double ax, double ay)
    {
        var brush = Token("Muted");
        var face = LabelFace;
        double w = view.PixelWidth, h = view.PixelHeight;
        double labelTop = Math.Clamp(ay + 6, 4, h - 20);
        double lastRight = double.NegativeInfinity;
        foreach (var t in xTicks)
        {
            if (t.Value == 0) continue;
            var ft = Text(t.Label, face, 12, brush);
            double x = view.ToPixelX(t.Value) - ft.Width / 2;
            if (x < 2 || x + ft.Width > w - 2 || x < lastRight + 6) continue;
            if (ax >= 0 && ax <= w && Math.Abs(view.ToPixelX(t.Value) - ax) < 4) continue;
            ctx.DrawText(ft, new Point(x, labelTop));
            lastRight = x + ft.Width;
        }
        double labelRight = Math.Clamp(ax - 7, 34, w - 4);
        foreach (var t in yTicks)
        {
            if (t.Value == 0) continue;
            var ft = Text(t.Label, face, 12, brush);
            double y = view.ToPixelY(t.Value) - ft.Height / 2;
            if (y < 2 || y + ft.Height > h - 2) continue;
            if (Math.Abs(view.ToPixelY(t.Value) - ay) < 12) continue;
            double x = labelRight - ft.Width;
            ctx.DrawText(ft, new Point(Math.Max(2, x), y));
        }
    }

    private static (double, double) Px(ViewTransform v, Point2 p) => (v.ToPixelX(p.X), v.ToPixelY(p.Y));

    private void DrawFills(DrawingContext ctx, ItemGeometry g, ViewTransform view)
    {
        if (g.FillCells.Count == 0 && g.Fills.Count == 0) return;
        var hatch = Hatch(InkBrush(g.Item.ColorIndex));
        double m = 4 * Math.Max(view.PixelWidth, view.PixelHeight);
        if (g.FillCells.Count > 0)
        {
            var geo = new StreamGeometry();
            using (var sg = geo.Open())
            {
                foreach (var c in g.FillCells)
                {
                    double x0 = view.ToPixelX(c.X0), x1 = view.ToPixelX(c.X1), y0 = view.ToPixelY(c.Y1), y1 = view.ToPixelY(c.Y0);
                    if (x1 < 0 || x0 > view.PixelWidth || y1 < 0 || y0 > view.PixelHeight) continue;
                    // Snap to whole pixels so neighbouring cells meet without seams.
                    x0 = Math.Round(x0);
                    x1 = Math.Round(x1);
                    y0 = Math.Round(y0);
                    y1 = Math.Round(y1);
                    sg.BeginFigure(new Point(x0, y0), true);
                    sg.LineTo(new Point(x1, y0));
                    sg.LineTo(new Point(x1, y1));
                    sg.LineTo(new Point(x0, y1));
                    sg.EndFigure(true);
                }
            }
            ctx.DrawGeometry(hatch, null, geo);
        }
        foreach (var poly in g.Fills)
        {
            if (poly.Points.Length < 3) continue;
            var geo = new StreamGeometry();
            using (var sg = geo.Open())
            {
                var (x0, y0) = Px(view, poly.Points[0]);
                sg.BeginFigure(new Point(Math.Clamp(x0, -m, m), Math.Clamp(y0, -m, m)), true);
                for (int i = 1; i < poly.Points.Length; i++)
                {
                    var (x, y) = Px(view, poly.Points[i]);
                    sg.LineTo(new Point(Math.Clamp(x, -m, m), Math.Clamp(y, -m, m)));
                }
                sg.EndFigure(true);
            }
            ctx.DrawGeometry(hatch, null, geo);
        }
    }

    private void DrawCurves(DrawingContext ctx, ItemGeometry g, ViewTransform view)
    {
        var ink = InkBrush(g.Item.ColorIndex);
        bool thick = g.Item.Kind is GraphKind.Function or GraphKind.FunctionOfY or GraphKind.Polar or GraphKind.Parametric or GraphKind.VerticalLine;
        double width = thick ? CurveWidth : BoundaryWidth;
        if (g.Curves.Count > 0)
        {
            var solid = new StreamGeometry();
            var dashed = new StreamGeometry();
            bool anySolid = false, anyDashed = false;
            using (var s1 = solid.Open())
            using (var s2 = dashed.Open())
            {
                foreach (var line in g.Curves)
                {
                    var px = new List<(double X, double Y)>(line.Points.Length);
                    foreach (var p in line.Points) px.Add(Px(view, p));
                    foreach (var run in PolylineClipper.Clip(px, -20, -20, view.PixelWidth + 20, view.PixelHeight + 20, line.Closed))
                    {
                        if (run.Count < 2) continue;
                        var target = line.Dashed ? s2 : s1;
                        if (line.Dashed) anyDashed = true; else anySolid = true;
                        target.BeginFigure(new Point(run[0].X, run[0].Y), false);
                        for (int i = 1; i < run.Count; i++) target.LineTo(new Point(run[i].X, run[i].Y));
                        target.EndFigure(false);
                    }
                }
            }
            if (anySolid) ctx.DrawGeometry(null, PenOf(ink, width), solid);
            if (anyDashed) ctx.DrawGeometry(null, PenOf(ink, BoundaryWidth, dashed: true), dashed);
        }
        if (g.Segments.Count > 0)
        {
            var geo = new StreamGeometry();
            using (var sg = geo.Open())
            {
                foreach (var s in g.Segments)
                {
                    var (x0, y0) = Px(view, s.A);
                    var (x1, y1) = Px(view, s.B);
                    if (Math.Max(x0, x1) < 0 || Math.Min(x0, x1) > view.PixelWidth || Math.Max(y0, y1) < 0 || Math.Min(y0, y1) > view.PixelHeight) continue;
                    sg.BeginFigure(new Point(x0, y0), false);
                    sg.LineTo(new Point(x1, y1));
                    sg.EndFigure(false);
                }
            }
            ctx.DrawGeometry(null, new Pen(ink, 2, lineCap: PenLineCap.Square), geo);
        }
    }

    private void DrawPoints(DrawingContext ctx, ItemGeometry g, ViewTransform view)
    {
        if (g.Points.Count == 0) return;
        var ink = InkBrush(g.Item.ColorIndex);
        var outline = PenOf(Token("Line"), 2);
        bool isolated = g.Item.Kind is GraphKind.Implicit;
        foreach (var p in g.Points)
        {
            var (x, y) = Px(view, p.Position);
            if (x < -10 || x > view.PixelWidth + 10 || y < -10 || y > view.PixelHeight + 10) continue;
            double s = isolated ? 6 : PointSize;
            ctx.DrawRectangle(ink, isolated ? null : outline, new Rect(Math.Round(x - s / 2), Math.Round(y - s / 2), s, s));
            if (p.Label is not null)
            {
                var ft = Text(p.Label, LabelFace, 13, Token("Ink"));
                double lx = x + 12, ly = y - 10 - ft.Height + 4;
                if (lx + ft.Width > view.PixelWidth - 2) lx = x - 12 - ft.Width;
                ctx.DrawText(ft, new Point(lx, Math.Max(2, ly)));
            }
        }
    }

    private void DrawAnnotations(DrawingContext ctx, ViewTransform view, bool shadesOnly)
    {
        if (Annotations is not { Count: > 0 } list) return;
        double w = view.PixelWidth, h = view.PixelHeight, m = 4 * Math.Max(w, h);
        foreach (var a in list)
        {
            var ink = InkBrush(a.ColorIndex);
            if (shadesOnly != (a.Kind == AnnotationKind.Shade)) continue;
            switch (a.Kind)
            {
                case AnnotationKind.Shade when a.Points.Length >= 3:
                {
                    var geo = new StreamGeometry();
                    using (var sg = geo.Open())
                    {
                        var (x0, y0) = Px(view, a.Points[0]);
                        sg.BeginFigure(new Point(Math.Clamp(x0, -m, m), Math.Clamp(y0, -m, m)), true);
                        foreach (var p in a.Points.Skip(1))
                        {
                            var (x, y) = Px(view, p);
                            sg.LineTo(new Point(Math.Clamp(x, -m, m), Math.Clamp(y, -m, m)));
                        }
                        sg.EndFigure(true);
                    }
                    using (ctx.PushOpacity(0.35)) ctx.DrawGeometry(ink, null, geo);
                    break;
                }
                case AnnotationKind.Line when a.Points.Length >= 2:
                {
                    var (x0, y0) = Px(view, a.Points[0]);
                    var (x1, y1) = Px(view, a.Points[1]);
                    double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len == 0 || !double.IsFinite(len)) break;
                    var p0 = (x0 - dx / len * 4 * (w + h), y0 - dy / len * 4 * (w + h));
                    var p1 = (x0 + dx / len * 4 * (w + h), y0 + dy / len * 4 * (w + h));
                    if (PolylineClipper.ClipSegment(ref p0, ref p1, -10, -10, w + 10, h + 10, out _, out _))
                        ctx.DrawLine(PenOf(Token("Ink"), 2, dashed: true), new Point(p0.Item1, p0.Item2), new Point(p1.Item1, p1.Item2));
                    break;
                }
                case AnnotationKind.Path when a.Points.Length >= 2:
                {
                    var px = a.Points.Select(p => Px(view, p)).ToList();
                    foreach (var run in PolylineClipper.Clip(px, -10, -10, w + 10, h + 10))
                        for (int i = 1; i < run.Count; i++)
                            ctx.DrawLine(PenOf(Token("Ink"), 2, dashed: true), new Point(run[i - 1].X, run[i - 1].Y), new Point(run[i].X, run[i].Y));
                    break;
                }
                case AnnotationKind.Point:
                    foreach (var p in a.Points)
                    {
                        var (x, y) = Px(view, p);
                        ctx.DrawRectangle(Token("Sel"), PenOf(Token("Line"), 2), new Rect(Math.Round(x - 6), Math.Round(y - 6), 12, 12));
                    }
                    break;
            }
        }
    }

    /// <summary>The points of interest that are drawn (those of the focused item, or all).</summary>
    public IEnumerable<PointOfInterest> VisiblePointsOfInterest =>
        !ShowPointsOfInterest ? [] : FocusItem is null ? _pois : _pois.Where(p => p.Item == FocusItem || p.Other == FocusItem);

    private void DrawPointsOfInterest(DrawingContext ctx, ViewTransform view)
    {
        var outline = PenOf(Token("Line"), 2);
        foreach (var p in VisiblePointsOfInterest)
        {
            var (x, y) = (view.ToPixelX(p.X), view.ToPixelY(p.Y));
            if (x < -6 || x > view.PixelWidth + 6 || y < -6 || y > view.PixelHeight + 6) continue;
            bool on = (SelectedPoint is { } s && s.Id == p.Id) || (_hoverPoi is { } hp && hp.Id == p.Id);
            ctx.DrawRectangle(on ? Token("Sel") : Token("Key"), outline, new Rect(Math.Round(x - PoiSize / 2), Math.Round(y - PoiSize / 2), PoiSize, PoiSize));
        }
    }

    private void DrawTooltip(DrawingContext ctx, ViewTransform view)
    {
        var p = _hoverPoi ?? SelectedPoint;
        if (p is null || !ShowPointsOfInterest) return;
        double x = view.ToPixelX(p.X), y = view.ToPixelY(p.Y);
        if (x < 0 || x > view.PixelWidth || y < 0 || y > view.PixelHeight) return;
        DrawBox(ctx, view, x, y, p.Title, p.Text);
    }

    /// <summary>A yellow (Calcy.Sel) box with a label-font title and a bold maths line, placed next to (x, y) inside the canvas.</summary>
    private void DrawBox(DrawingContext ctx, ViewTransform view, double x, double y, string title, string text)
    {
        var ink = Token("Ink");
        var t1 = Text(title, LabelFace, 12, ink);
        var t2 = Text(text, MathFace(bold: true), 15, ink);
        double bw = Math.Max(196, Math.Max(t1.Width, t2.Width) + 22), bh = t1.Height + t2.Height + 16;
        double bx = Math.Clamp(x + 14, 4, view.PixelWidth - bw - 4);
        double by = Math.Clamp(y - bh - 14, 4, view.PixelHeight - bh - 4);
        if (bx < x + 6 && bx + bw > x - 6 && by < y + 6 && by + bh > y - 6) by = Math.Clamp(y + 14, 4, view.PixelHeight - bh - 4);
        var rect = new Rect(Math.Round(bx) + 1.5, Math.Round(by) + 1.5, Math.Round(bw), Math.Round(bh));
        ctx.DrawRectangle(Token("Sel"), PenOf(Token("Line"), 3), rect);
        ctx.DrawText(t1, new Point(rect.X + 10, rect.Y + 6));
        ctx.DrawText(t2, new Point(rect.X + 10, rect.Y + 8 + t1.Height));
    }
}
