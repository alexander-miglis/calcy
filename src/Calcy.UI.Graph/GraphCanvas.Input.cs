using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Calcy.Graphing;

namespace Calcy.UI.Graph;

public partial class GraphCanvas
{
    private enum DragMode { None, Pan, Box }

    private DragMode _drag;
    private Point _pressPoint;
    private Point _lastPoint;
    private Viewport _pressViewport;
    private bool _moved;
    private PointOfInterest? _hoverPoi;
    private bool _keyboardFocus;
    private double _pinchScale = 1;

    // Trace state: the item, its member, and the parameter (x, y, t or θ) along the curve.
    private GraphItem? _traceItem;
    private int _traceMember;
    private double _traceParam;

    /// <summary>The traced point, or null when not tracing.</summary>
    public Point2? TracePoint => IsTracing && _traceItem is not null ? Evaluate(_traceItem, _traceMember, _traceParam) : null;

    /// <summary>The traced item.</summary>
    public GraphItem? TraceItem => IsTracing ? _traceItem : null;

    // ------------------------------------------------------------------ pointer

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;
        _keyboardFocus = false;
        Focus();
        var pos = e.GetPosition(this);
        _pressPoint = _lastPoint = pos;
        _pressViewport = Viewport;
        _moved = false;
        var view = new ViewTransform(Viewport, Bounds.Width, Bounds.Height);
        var poi = HitPoint(pos, view);
        if (poi is not null && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SelectedPoint = poi;
            PointPicked?.Invoke(this, new PointPickedEventArgs(new Point2(poi.X, poi.Y), poi.Item, poi.Member, poi));
            e.Handled = true;
            return;
        }
        _drag = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? DragMode.Box : DragMode.Pan;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var view = new ViewTransform(Viewport, Bounds.Width, Bounds.Height);
        if (_drag != DragMode.None)
        {
            if (Math.Abs(pos.X - _pressPoint.X) + Math.Abs(pos.Y - _pressPoint.Y) > 3) _moved = true;
            _lastPoint = pos;
            if (_drag == DragMode.Pan && _moved)
            {
                var start = new ViewTransform(_pressViewport, Bounds.Width, Bounds.Height);
                double dx = (pos.X - _pressPoint.X) / start.ScaleX, dy = (pos.Y - _pressPoint.Y) / start.ScaleY;
                Viewport = _pressViewport.Pan(-dx, dy);
            }
            else InvalidateVisual();
            return;
        }
        var hover = HitPoint(pos, view);
        if (hover?.Id != _hoverPoi?.Id)
        {
            _hoverPoi = hover;
            InvalidateVisual();
        }
        if (IsTracing)
        {
            TraceTo(pos, view);
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var mode = _drag;
        _drag = DragMode.None;
        e.Pointer.Capture(null);
        var pos = e.GetPosition(this);
        var view = new ViewTransform(Viewport, Bounds.Width, Bounds.Height);
        if (mode == DragMode.Box)
        {
            var r = new Rect(_pressPoint, pos).Normalize();
            if (r.Width > 6 && r.Height > 6)
            {
                SetViewport(new Viewport(view.ToWorldX(r.Left), view.ToWorldX(r.Right), view.ToWorldY(r.Bottom), view.ToWorldY(r.Top)));
            }
            InvalidateVisual();
            return;
        }
        if (mode == DragMode.Pan)
        {
            if (_moved)
            {
                // Remember the window before the drag so Previous returns to it.
                _history.Push(_pressViewport);
                return;
            }
            var hit = HitCurve(pos, view);
            if (IsTracing)
            {
                // Clicking another curve moves the trace onto it.
                if (hit is { } h && Traceable(h.Item))
                {
                    _traceItem = h.Item;
                    _traceMember = h.Member;
                }
                TraceTo(pos, view);
            }
            var world = hit?.Point ?? new Point2(view.ToWorldX(pos.X), view.ToWorldY(pos.Y));
            if (hit is null && SelectedPoint is not null) SelectedPoint = null;
            PointPicked?.Invoke(this, new PointPickedEventArgs(world, hit?.Item, hit?.Member ?? 0, null));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag == DragMode.Box) InvalidateVisual();
        _drag = DragMode.None;
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverPoi is not null)
        {
            _hoverPoi = null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double delta = e.Delta.Y;
        if (delta == 0) return;
        var pos = e.GetPosition(this);
        var view = new ViewTransform(Viewport, Bounds.Width, Bounds.Height);
        double factor = Math.Pow(0.85, Math.Clamp(delta, -6, 6));
        ZoomBy(factor, new Point2(view.ToWorldX(pos.X), view.ToWorldY(pos.Y)), remember: false);
        e.Handled = true;
    }

    /// <summary>Pinch to zoom on touch screens and touchpads.</summary>
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (e.Scale <= 0) return;
        double rel = _pinchScale / e.Scale;
        _pinchScale = e.Scale;
        var view = new ViewTransform(Viewport, Bounds.Width, Bounds.Height);
        var origin = e.ScaleOrigin;
        double ox = origin.X * Bounds.Width, oy = origin.Y * Bounds.Height;
        ZoomBy(rel, new Point2(view.ToWorldX(ox), view.ToWorldY(oy)), remember: false);
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e) => _pinchScale = 1;

    private void OnMagnify(object? sender, PointerDeltaEventArgs e)
    {
        var pos = e.GetPosition(this);
        var view = new ViewTransform(Viewport, Bounds.Width, Bounds.Height);
        double factor = 1 / (1 + Math.Clamp(e.Delta.X, -0.5, 0.5));
        ZoomBy(factor, new Point2(view.ToWorldX(pos.X), view.ToWorldY(pos.Y)), remember: false);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Pinch += OnPinch;
        PinchEnded += OnPinchEnded;
        PointerTouchPadGestureMagnify += OnMagnify;
        OnSchemeChanged();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Pinch -= OnPinch;
        PinchEnded -= OnPinchEnded;
        PointerTouchPadGestureMagnify -= OnMagnify;
    }

    // ------------------------------------------------------------------ keyboard

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _keyboardFocus = e.NavigationMethod == NavigationMethod.Tab;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _keyboardFocus = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var v = Viewport;
        var view = new ViewTransform(v, Bounds.Width, Bounds.Height);
        switch (e.Key)
        {
            case Key.Left or Key.Right when IsTracing && _traceItem is not null:
                MoveTrace(e.Key == Key.Right ? 1 : -1, shift ? 10 : 1, view);
                break;
            case Key.Up or Key.Down when IsTracing:
                SwitchTrace(e.Key == Key.Down ? 1 : -1, view);
                break;
            case Key.Left:
                SetViewport(v.Pan(-v.Width * 0.1, 0), remember: false);
                break;
            case Key.Right:
                SetViewport(v.Pan(v.Width * 0.1, 0), remember: false);
                break;
            case Key.Up:
                SetViewport(v.Pan(0, v.Height * 0.1), remember: false);
                break;
            case Key.Down:
                SetViewport(v.Pan(0, -v.Height * 0.1), remember: false);
                break;
            case Key.Add or Key.OemPlus:
                ZoomBy(0.8);
                break;
            case Key.Subtract or Key.OemMinus:
                ZoomBy(1.25);
                break;
            case Key.Home:
                SetViewport(Viewport.Standard);
                break;
            case Key.Back:
                GoBack();
                break;
            case Key.T:
                IsTracing = !IsTracing;
                break;
            case Key.N:
                StepPoint(shift ? -1 : 1);
                break;
            case Key.Enter when IsTracing && TracePoint is { } tp:
                PointPicked?.Invoke(this, new PointPickedEventArgs(tp, _traceItem, _traceMember, null));
                break;
            case Key.Escape:
                if (IsTracing) IsTracing = false;
                else if (SelectedPoint is not null) SelectedPoint = null;
                else return;
                break;
            default:
                return;
        }
        _keyboardFocus = true;
        e.Handled = true;
        InvalidateVisual();
    }

    /// <summary>Selects the next (or previous) point of interest, in x order.</summary>
    public void StepPoint(int direction)
    {
        var list = VisiblePointsOfInterest.Where(p => Viewport.Contains(p.X, p.Y)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (list.Count == 0) return;
        int i = SelectedPoint is { } s ? list.FindIndex(p => p.Id == s.Id) : -1;
        i = i < 0 ? (direction > 0 ? 0 : list.Count - 1) : ((i + direction) % list.Count + list.Count) % list.Count;
        SelectedPoint = list[i];
    }

    // ------------------------------------------------------------------ trace

    private static bool Traceable(GraphItem i) => i.IsVisible && i.Kind is GraphKind.Function or GraphKind.FunctionOfY or GraphKind.Polar or GraphKind.Parametric;

    private Point2? Evaluate(GraphItem item, int member, double t)
    {
        if (member < 0 || member >= item.Members.Count) return null;
        var m = item.Members[member];
        var p = ParameterValues(item);
        Point2 q = item.Kind switch
        {
            GraphKind.Function => new Point2(t, m.Main?.Eval(t, p) ?? double.NaN),
            GraphKind.FunctionOfY => new Point2(m.Main?.Eval(t, p) ?? double.NaN, t),
            GraphKind.Polar => PolarPoint(item, m.Main?.Eval(t, p) ?? double.NaN, t),
            GraphKind.Parametric => new Point2(m.X?.Eval(t, p) ?? double.NaN, m.Y?.Eval(t, p) ?? double.NaN),
            _ => new Point2(double.NaN, double.NaN),
        };
        return q.IsFinite ? q : null;
    }

    private static Point2 PolarPoint(GraphItem item, double r, double t) =>
        new(r * Math.Cos(t * item.AngleToRadians), r * Math.Sin(t * item.AngleToRadians));

    private void StartTrace(ViewTransform view)
    {
        var items = (Items ?? []).Where(Traceable).ToList();
        if (items.Count == 0)
        {
            IsTracing = false;
            return;
        }
        _traceItem = FocusItem is { } f && Traceable(f) ? f : items[0];
        _traceMember = 0;
        _traceParam = _traceItem.Kind switch
        {
            GraphKind.Function => Viewport.CenterX,
            GraphKind.FunctionOfY => Viewport.CenterY,
            _ => 0.5 * (_traceItem.TMin + _traceItem.TMax),
        };
        RaiseTrace();
    }

    private void UpdateTraceAfterViewChange() => InvalidateVisual();

    /// <summary>Starts tracing <paramref name="item"/> at a parameter value (x for functions, y for x = g(y), t or θ otherwise).</summary>
    public void TraceAt(GraphItem item, double parameter, int member = 0)
    {
        if (!Traceable(item)) return;
        _traceItem = item;
        _traceMember = Math.Clamp(member, 0, Math.Max(0, item.Members.Count - 1));
        _traceParam = parameter;
        IsTracing = true;
        if (TracePoint is { } q && !Viewport.Contains(q.X, q.Y))
        {
            var v = Viewport;
            SetViewport(v.Pan(q.X - v.CenterX, q.Y < v.YMin || q.Y > v.YMax ? q.Y - v.CenterY : 0));
        }
        RaiseTrace();
    }

    private void MoveTrace(int direction, int steps, ViewTransform view)
    {
        if (_traceItem is null) return;
        double step = _traceItem.Kind switch
        {
            GraphKind.Function => 1 / view.ScaleX,
            GraphKind.FunctionOfY => 1 / view.ScaleY,
            _ => (_traceItem.TMax - _traceItem.TMin) / 400,
        };
        _traceParam += direction * steps * step;
        if (_traceItem.HasRange) _traceParam = Math.Clamp(_traceParam, _traceItem.TMin, _traceItem.TMax);
        // Keep the traced point in view (TI style: the window scrolls with the cursor).
        if (TracePoint is { } q && !Viewport.Contains(q.X, Math.Clamp(q.Y, Viewport.YMin, Viewport.YMax)))
        {
            var v = Viewport;
            SetViewport(v.Pan(q.X < v.XMin ? q.X - v.XMin - v.Width * 0.1 : q.X > v.XMax ? q.X - v.XMax + v.Width * 0.1 : 0, 0), remember: false);
        }
        RaiseTrace();
    }

    private void SwitchTrace(int direction, ViewTransform view)
    {
        var items = (Items ?? []).Where(Traceable).ToList();
        if (items.Count == 0) return;
        var current = TracePoint;
        int i = _traceItem is null ? -1 : items.IndexOf(_traceItem);
        if (_traceItem is not null && _traceMember + direction >= 0 && _traceMember + direction < _traceItem.Members.Count)
        {
            _traceMember += direction;
        }
        else
        {
            i = ((i + direction) % items.Count + items.Count) % items.Count;
            _traceItem = items[i];
            _traceMember = direction > 0 ? 0 : _traceItem.Members.Count - 1;
        }
        if (current is { } c && _traceItem.Kind == GraphKind.Function) _traceParam = c.X;
        else if (current is { } c2 && _traceItem.Kind == GraphKind.FunctionOfY) _traceParam = c2.Y;
        else if (_traceItem.HasRange) _traceParam = Math.Clamp(_traceParam, _traceItem.TMin, _traceItem.TMax);
        RaiseTrace();
    }

    private void TraceTo(Point pos, ViewTransform view)
    {
        if (_traceItem is null) StartTrace(view);
        if (_traceItem is null) return;
        switch (_traceItem.Kind)
        {
            case GraphKind.Function:
                _traceParam = view.ToWorldX(pos.X);
                break;
            case GraphKind.FunctionOfY:
                _traceParam = view.ToWorldY(pos.Y);
                break;
            default:
            {
                // Nearest parameter value along the curve.
                double best = double.PositiveInfinity, bestT = _traceParam;
                int n = 600;
                for (int k = 0; k <= n; k++)
                {
                    double t = _traceItem.TMin + (_traceItem.TMax - _traceItem.TMin) * k / n;
                    if (Evaluate(_traceItem, _traceMember, t) is not { } q) continue;
                    double dx = view.ToPixelX(q.X) - pos.X, dy = view.ToPixelY(q.Y) - pos.Y, d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        best = d;
                        bestT = t;
                    }
                }
                _traceParam = bestT;
                break;
            }
        }
        RaiseTrace();
    }

    private void RaiseTrace()
    {
        var q = TracePoint;
        TraceMoved?.Invoke(this, q is null ? null : new PointPickedEventArgs(q.Value, _traceItem, _traceMember, null));
        InvalidateVisual();
    }

    private void DrawTrace(DrawingContext ctx, ViewTransform view)
    {
        if (!IsTracing || _traceItem is null) return;
        var q = TracePoint;
        string v = _traceItem.Variable ?? "x";
        if (q is null)
        {
            DrawBox(ctx, view, view.PixelWidth / 2, view.PixelHeight / 2, $"TRACE {_traceItem.Name}", $"{v} = {GraphFormat.Number(_traceParam, 5, true)}: undefined");
            return;
        }
        double x = view.ToPixelX(q.Value.X), y = view.ToPixelY(q.Value.Y);
        var pen = PenOf(Token("Ink"), 1, dashed: true);
        ctx.DrawLine(pen, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, view.PixelHeight));
        ctx.DrawLine(pen, new Point(0, Math.Round(y) + 0.5), new Point(view.PixelWidth, Math.Round(y) + 0.5));
        ctx.DrawRectangle(Token("Card"), PenOf(InkBrush(_traceItem.ColorIndex), 3), new Rect(Math.Round(x) - 7, Math.Round(y) - 7, 14, 14));
        string text = _traceItem.HasRange
            ? $"{v} = {GraphFormat.Number(_traceParam, 4, true)}  {GraphFormat.Point(q.Value.X, q.Value.Y, 5, true)}"
            : $"x = {GraphFormat.Number(q.Value.X, 6, true)}  y = {GraphFormat.Number(q.Value.Y, 6, true)}";
        DrawBox(ctx, view, x, y, $"TRACE {_traceItem.Name}", text);
    }

    private void DrawBoxZoom(DrawingContext ctx)
    {
        if (_drag != DragMode.Box) return;
        var r = new Rect(_pressPoint, _lastPoint).Normalize();
        using (ctx.PushOpacity(0.25)) ctx.FillRectangle(Token("Sel"), r);
        ctx.DrawRectangle(null, PenOf(Token("Ink"), 2, dashed: true), r);
    }

    // ------------------------------------------------------------------ hit testing

    private PointOfInterest? HitPoint(Point pos, ViewTransform view)
    {
        PointOfInterest? best = null;
        double bestD = 9 * 9;
        foreach (var p in VisiblePointsOfInterest)
        {
            double dx = view.ToPixelX(p.X) - pos.X, dy = view.ToPixelY(p.Y) - pos.Y, d = dx * dx + dy * dy;
            if (d <= bestD)
            {
                bestD = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>The curve point nearest to a pixel position (within 12 pixels), or null.</summary>
    public (GraphItem Item, int Member, Point2 Point)? HitCurve(Point pos, ViewTransform view)
    {
        if (_cache is null) return null;
        (GraphItem, int, Point2)? best = null;
        double bestD = 12 * 12;
        foreach (var g in _cache.Geometry)
        {
            foreach (var line in g.Curves)
            {
                for (int i = 1; i < line.Points.Length; i++)
                {
                    var (ax, ay) = Px(view, line.Points[i - 1]);
                    var (bx, by) = Px(view, line.Points[i]);
                    double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                    double t = len2 == 0 ? 0 : Math.Clamp(((pos.X - ax) * dx + (pos.Y - ay) * dy) / len2, 0, 1);
                    double px = ax + t * dx, py = ay + t * dy;
                    double d = (px - pos.X) * (px - pos.X) + (py - pos.Y) * (py - pos.Y);
                    if (d < bestD)
                    {
                        bestD = d;
                        var a = line.Points[i - 1];
                        var b = line.Points[i];
                        best = (g.Item, line.Member, new Point2(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
                    }
                }
            }
            foreach (var pt in g.Points)
            {
                var (x, y) = Px(view, pt.Position);
                double d = (x - pos.X) * (x - pos.X) + (y - pos.Y) * (y - pos.Y);
                if (d < bestD)
                {
                    bestD = d;
                    best = (g.Item, pt.Member, pt.Position);
                }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ export

    /// <summary>The current scheme's colours as an export palette.</summary>
    public GraphPalette CurrentPalette()
    {
        static string Hex(IBrush b) => b is ISolidColorBrush s ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : "#000000";
        return new GraphPalette(Hex(Token("Card")), Hex(Token("Grid")), Hex(Token("Ink")), Hex(Token("Muted")), Hex(Token("Line")),
            [.. Enumerable.Range(0, 5).Select(i => Hex(InkBrush(i)))], Hex(Token("Key")), Hex(Token("Sel")));
    }

    /// <summary>The graph as an SVG document in the current colours (sampled at the given size, or the control's size).</summary>
    public string ExportSvg(int width = 0, int height = 0)
    {
        int w = width > 0 ? width : (int)Math.Max(1, Bounds.Width), h = height > 0 ? height : (int)Math.Max(1, Bounds.Height);
        var items = (Items ?? []).ToList();
        var shown = VisiblePointsOfInterest.ToList();
        var scene = GraphScene.Build(items, ParameterValues, Viewport, w, h, TickStyle, SamplingOptions, shown, Annotations);
        return SvgExporter.Export(scene, CurrentPalette());
    }

    /// <summary>Renders the graph to a PNG stream at <paramref name="scale"/> times the control's size.</summary>
    public void SavePng(Stream stream, double scale = 1)
    {
        var size = Bounds.Size;
        if (size.Width < 1 || size.Height < 1) throw new InvalidOperationException("The graph has no size yet");
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        using var rtb = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        rtb.Render(this);
        rtb.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
