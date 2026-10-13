using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Calcy.Core;
using Calcy.Graphing;

namespace Calcy.UI.Graph;

/// <summary>Raised when the user clicks the graph (or presses Enter while tracing).</summary>
public sealed class PointPickedEventArgs(Point2 position, GraphItem? item, int member, PointOfInterest? point) : EventArgs
{
    /// <summary>The clicked position in graph coordinates (snapped to the curve when one was hit).</summary>
    public Point2 Position { get; } = position;

    /// <summary>The curve under the pointer, if any.</summary>
    public GraphItem? Item { get; } = item;

    /// <summary>Family member of <see cref="Item"/>.</summary>
    public int Member { get; } = member;

    /// <summary>The point of interest that was clicked, if any.</summary>
    public PointOfInterest? Point { get; } = point;
}

/// <summary>Raised when the visible window changes (pan, zoom, presets).</summary>
public sealed class ViewportChangedEventArgs(Viewport oldViewport, Viewport newViewport) : EventArgs
{
    /// <summary>The previous window.</summary>
    public Viewport OldViewport { get; } = oldViewport;

    /// <summary>The new window.</summary>
    public Viewport NewViewport { get; } = newViewport;
}

/// <summary>
/// An interactive 2D graph in the Calcy 8-bit style. Set <see cref="Items"/> (from <see cref="GraphParser"/> or a
/// <see cref="GraphDocument"/>), <see cref="Parameters"/> for sliders and <see cref="Viewport"/>; the control samples in the
/// background, caches geometry per window and redraws transformed geometry while panning and zooming.
/// Colours come from the current scheme (Calcy.Card, Calcy.Grid, Calcy.Ink, Calcy.Muted, Calcy.G1 to Calcy.G5, ...).
/// Mouse: drag to pan, wheel or pinch to zoom around the pointer, Shift+drag to zoom into a box, click a point of interest
/// to pin it. Keys: arrows pan (or move the trace), + and - zoom, T traces, N and Shift+N step through points of interest,
/// Home resets, Backspace goes back to the previous window, Escape clears.
/// </summary>
public partial class GraphCanvas : Control, IDisposable
{
    /// <summary>The dispatcher of the thread that created the control; background work posts back here, never to whatever UIThread is current later.</summary>
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

    /// <summary>The visible window.</summary>
    public static readonly StyledProperty<Viewport> ViewportProperty =
        AvaloniaProperty.Register<GraphCanvas, Viewport>(nameof(Viewport), Viewport.Standard, defaultBindingMode: BindingMode.TwoWay,
            coerce: (_, v) => v.Normalized());

    /// <summary>The graph items to draw (hidden items are skipped).</summary>
    public static readonly StyledProperty<IReadOnlyList<GraphItem>?> ItemsProperty =
        AvaloniaProperty.Register<GraphCanvas, IReadOnlyList<GraphItem>?>(nameof(Items));

    /// <summary>Slider values for the items' parameters (null uses the default value 1).</summary>
    public static readonly StyledProperty<GraphParameters?> ParametersProperty =
        AvaloniaProperty.Register<GraphCanvas, GraphParameters?>(nameof(Parameters));

    /// <summary>The session used to find exact forms of points of interest (optional).</summary>
    public static readonly StyledProperty<CalcSession?> SessionProperty =
        AvaloniaProperty.Register<GraphCanvas, CalcSession?>(nameof(Session));

    /// <summary>Analysis overlays (shaded integrals, tangent lines, marked points).</summary>
    public static readonly StyledProperty<IReadOnlyList<GraphAnnotation>?> AnnotationsProperty =
        AvaloniaProperty.Register<GraphCanvas, IReadOnlyList<GraphAnnotation>?>(nameof(Annotations));

    /// <summary>Decimal or π-based x ticks.</summary>
    public static readonly StyledProperty<TickStyle> TickStyleProperty =
        AvaloniaProperty.Register<GraphCanvas, TickStyle>(nameof(TickStyle));

    /// <summary>Show grid lines and tick labels.</summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<GraphCanvas, bool>(nameof(ShowGrid), true);

    /// <summary>Find and mark points of interest of function graphs.</summary>
    public static readonly StyledProperty<bool> ShowPointsOfInterestProperty =
        AvaloniaProperty.Register<GraphCanvas, bool>(nameof(ShowPointsOfInterest), true);

    /// <summary>When set, only this item's points of interest (and its intersections) are marked.</summary>
    public static readonly StyledProperty<GraphItem?> FocusItemProperty =
        AvaloniaProperty.Register<GraphCanvas, GraphItem?>(nameof(FocusItem));

    /// <summary>The pinned point of interest (shown with its tooltip).</summary>
    public static readonly StyledProperty<PointOfInterest?> SelectedPointProperty =
        AvaloniaProperty.Register<GraphCanvas, PointOfInterest?>(nameof(SelectedPoint), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Trace mode: the cursor snaps to a curve and the arrow keys move along it.</summary>
    public static readonly StyledProperty<bool> IsTracingProperty =
        AvaloniaProperty.Register<GraphCanvas, bool>(nameof(IsTracing), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Sampling quality settings.</summary>
    public static readonly StyledProperty<SamplingOptions?> SamplingOptionsProperty =
        AvaloniaProperty.Register<GraphCanvas, SamplingOptions?>(nameof(SamplingOptions));

    private readonly Stack<Viewport> _history = new();
    private readonly Debouncer _resampleTimer;
    private readonly Debouncer _poiTimer;
    private SceneCache? _cache;
    private int _version;
    private bool _sampling;
    private bool _dirty;
    private bool _poiRunning;
    private bool _poiDirty;
    private CancellationTokenSource? _poiCts;
    private GraphParameters? _subscribedParameters;
    private IReadOnlyList<PointOfInterest> _pois = [];

    static GraphCanvas()
    {
        FocusableProperty.OverrideDefaultValue<GraphCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<GraphCanvas>(true);
        AffectsRender<GraphCanvas>(AnnotationsProperty, TickStyleProperty, ShowGridProperty, FocusItemProperty, SelectedPointProperty, IsTracingProperty);
    }

    /// <summary>Creates an empty graph showing the standard window.</summary>
    public GraphCanvas()
    {
        _resampleTimer = new Debouncer(TimeSpan.FromMilliseconds(45), RequestSample);
        _poiTimer = new Debouncer(TimeSpan.FromMilliseconds(120), StartPointsOfInterest);
        AutomationProperties.SetName(this, "Graph");
        AutomationProperties.SetHelpText(this, "Drag to pan, scroll to zoom, Shift and drag to zoom into a box. Keys: arrows pan, plus and minus zoom, T traces a curve, N steps through points of interest.");
        Cursor = new Cursor(StandardCursorType.Cross);
        GestureRecognizers.Add(new PinchGestureRecognizer());
        ResourcesChanged += (_, _) => OnSchemeChanged();
        ActualThemeVariantChanged += (_, _) => OnSchemeChanged();
    }

    /// <inheritdoc cref="ViewportProperty"/>
    public Viewport Viewport
    {
        get => GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    /// <inheritdoc cref="ItemsProperty"/>
    public IReadOnlyList<GraphItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <inheritdoc cref="ParametersProperty"/>
    public GraphParameters? Parameters
    {
        get => GetValue(ParametersProperty);
        set => SetValue(ParametersProperty, value);
    }

    /// <inheritdoc cref="SessionProperty"/>
    public CalcSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <inheritdoc cref="AnnotationsProperty"/>
    public IReadOnlyList<GraphAnnotation>? Annotations
    {
        get => GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    /// <inheritdoc cref="TickStyleProperty"/>
    public TickStyle TickStyle
    {
        get => GetValue(TickStyleProperty);
        set => SetValue(TickStyleProperty, value);
    }

    /// <inheritdoc cref="ShowGridProperty"/>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <inheritdoc cref="ShowPointsOfInterestProperty"/>
    public bool ShowPointsOfInterest
    {
        get => GetValue(ShowPointsOfInterestProperty);
        set => SetValue(ShowPointsOfInterestProperty, value);
    }

    /// <inheritdoc cref="FocusItemProperty"/>
    public GraphItem? FocusItem
    {
        get => GetValue(FocusItemProperty);
        set => SetValue(FocusItemProperty, value);
    }

    /// <inheritdoc cref="SelectedPointProperty"/>
    public PointOfInterest? SelectedPoint
    {
        get => GetValue(SelectedPointProperty);
        set => SetValue(SelectedPointProperty, value);
    }

    /// <inheritdoc cref="IsTracingProperty"/>
    public bool IsTracing
    {
        get => GetValue(IsTracingProperty);
        set => SetValue(IsTracingProperty, value);
    }

    /// <inheritdoc cref="SamplingOptionsProperty"/>
    public SamplingOptions? SamplingOptions
    {
        get => GetValue(SamplingOptionsProperty);
        set => SetValue(SamplingOptionsProperty, value);
    }

    /// <summary>The points of interest found for the current window (all visible function graphs).</summary>
    public IReadOnlyList<PointOfInterest> PointsOfInterest => _pois;

    /// <summary>True while sampling or point-of-interest work is pending (tests and screenshots wait for it).</summary>
    public bool IsBusy => _sampling || _dirty || _poiRunning || _poiDirty || _resampleTimer.Pending || _poiTimer.Pending;

    /// <summary>True when some visible curve could not be guaranteed complete (shown as a warning on the graph).</summary>
    public bool MayBeIncomplete => _cache?.Geometry.Any(g => g.MayBeIncomplete) ?? false;

    /// <summary>Raised on a click (or Enter while tracing).</summary>
    public event EventHandler<PointPickedEventArgs>? PointPicked;

    /// <summary>Raised when the window changes.</summary>
    public event EventHandler<ViewportChangedEventArgs>? ViewportChanged;

    /// <summary>Raised when new points of interest have been found.</summary>
    public event EventHandler? PointsOfInterestChanged;

    /// <summary>Raised when the trace moves (the argument is the traced point, or null when tracing stops).</summary>
    public event EventHandler<PointPickedEventArgs?>? TraceMoved;

    /// <summary>Parameter values for an item from <see cref="Parameters"/>.</summary>
    public double[] ParameterValues(GraphItem item) => Parameters?.Vector(item.Parameters) ?? Enumerable.Repeat(GraphParameters.DefaultValue, item.Parameters.Count).ToArray();

    /// <summary>Sets the window, remembering the old one for <see cref="GoBack"/>.</summary>
    public void SetViewport(Viewport v, bool remember = true)
    {
        v = v.Normalized();
        if (remember && v != Viewport)
        {
            _history.Push(Viewport);
            if (_history.Count > 50) TrimHistory();
        }
        Viewport = v;
    }

    private void TrimHistory()
    {
        var keep = _history.Take(40).Reverse().ToList();
        _history.Clear();
        foreach (var k in keep) _history.Push(k);
    }

    /// <summary>Returns to the previous window (Zoom Previous). Returns false when there is none.</summary>
    public bool GoBack()
    {
        if (_history.Count == 0) return false;
        Viewport = _history.Pop();
        return true;
    }

    /// <summary>Zooms by <paramref name="factor"/> (below 1 zooms in) around a graph point (the centre by default).</summary>
    public void ZoomBy(double factor, Point2? center = null, bool remember = true)
    {
        var v = Viewport;
        var c = center ?? new Point2(v.CenterX, v.CenterY);
        SetViewport(v.Zoom(factor, c.X, c.Y), remember);
    }

    /// <summary>Zooms in by a factor of two around the centre.</summary>
    public void ZoomIn() => ZoomBy(0.5);

    /// <summary>Zooms out by a factor of two around the centre.</summary>
    public void ZoomOut() => ZoomBy(2);

    /// <summary>Equal scale on both axes (circles look round).</summary>
    public void Square()
    {
        var s = Bounds.Size;
        if (s.Width > 1 && s.Height > 1) SetViewport(Viewport.Square(s.Width, s.Height));
    }

    /// <summary>Fits the y range to the visible graphs (Zoom Fit).</summary>
    public void Fit()
    {
        if (Items is null) return;
        SetViewport(GraphFit.FitY(Items, ParameterValues, Viewport));
    }

    /// <summary>Resamples everything (call after changing an item's visibility, colour or range).</summary>
    public void Refresh()
    {
        _version++;
        RequestSample();
        InvalidateVisual();
    }

    /// <summary>Samples the current items synchronously and finds points of interest (used for export and tests).</summary>
    public void RenderNow()
    {
        var size = Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return;
        _cache = Compute(Snapshot(), CancellationToken.None);
        _dirty = false;
        if (ShowPointsOfInterest) SetPointsOfInterest(FindPoints(Snapshot(), CancellationToken.None));
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewportProperty)
        {
            var (o, n) = change.GetOldAndNewValue<Viewport>();
            InvalidateVisual();
            ScheduleResample(n);
            ViewportChanged?.Invoke(this, new ViewportChangedEventArgs(o, n));
            if (IsTracing) UpdateTraceAfterViewChange();
        }
        else if (change.Property == ItemsProperty || change.Property == SamplingOptionsProperty)
        {
            _version++;
            if (SelectedPoint is { } sp && Items is { } list && !list.Contains(sp.Item)) SelectedPoint = null;
            if (_traceItem is { } ti && Items is { } items && !items.Contains(ti))
                _traceItem = items.FirstOrDefault(i => i.Name == ti.Name && Traceable(i));
            RequestSample();
            InvalidateVisual();
        }
        else if (change.Property == ParametersProperty)
        {
            if (_subscribedParameters is not null) _subscribedParameters.ValuesChanged -= OnParameterChanged;
            _subscribedParameters = Parameters;
            if (_subscribedParameters is not null) _subscribedParameters.ValuesChanged += OnParameterChanged;
            Refresh();
        }
        else if (change.Property == IsTracingProperty)
        {
            if (IsTracing && (_traceItem is null || !(Items ?? []).Contains(_traceItem))) StartTrace(new ViewTransform(Viewport, Bounds.Width, Bounds.Height));
            else RaiseTrace();
        }
        else if (change.Property == ShowPointsOfInterestProperty || change.Property == SessionProperty)
        {
            SchedulePoints();
            InvalidateVisual();
        }
        else if (change.Property == BoundsProperty)
        {
            var (o, n) = change.GetOldAndNewValue<Rect>();
            if (o.Size != n.Size) RequestSample();
        }
    }

    private void OnParameterChanged(object? sender, GraphParameter p)
    {
        if (_dispatcher.CheckAccess()) Refresh();
        else _dispatcher.Post(Refresh);
    }

    private void OnSchemeChanged()
    {
        _resources.Clear();
        _pens.Clear();
        _hatches.Clear();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ background sampling

    private sealed record Snap(int Version, Viewport View, Size Size, IReadOnlyList<(GraphItem Item, double[] P)> Items, SamplingOptions Options, CalcSession? Session, bool Points);

    private sealed record SceneCache(int Version, Viewport View, Size Size, IReadOnlyList<ItemGeometry> Geometry);

    private Snap Snapshot()
    {
        var items = (Items ?? []).Where(i => i.IsVisible && i.IsPlottable).Select(i => (i, ParameterValues(i))).ToList();
        return new Snap(_version, Viewport, Bounds.Size, items, SamplingOptions ?? Graphing.SamplingOptions.Default, Session, ShowPointsOfInterest);
    }

    private static SceneCache Compute(Snap s, CancellationToken ct)
    {
        var view = new ViewTransform(s.View, s.Size.Width, s.Size.Height);
        var geos = new List<ItemGeometry>(s.Items.Count);
        foreach (var (item, p) in s.Items)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                geos.Add(GraphSampler.Sample(item, p, view, s.Options, ct));
            }
            catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or IndexOutOfRangeException or CalcException)
            {
                geos.Add(ItemGeometry.Empty(item, s.View));
            }
        }
        return new SceneCache(s.Version, s.View, s.Size, geos);
    }

    private void ScheduleResample(Viewport v)
    {
        var c = _cache;
        bool covered = c is not null && c.Geometry.Count > 0 && c.Geometry.All(g => g.Sampled.Covers(v)) &&
                       Math.Abs(Math.Log(c.View.Width / v.Width)) < 0.15 && Math.Abs(Math.Log(c.View.Height / v.Height)) < 0.15;
        if (!covered && c is not null && (c.Geometry.Count == 0 || !c.View.Expand(0.25).Covers(v)))
        {
            RequestSample();
            return;
        }
        // Covered: redraw the cached geometry now and refine once the movement settles.
        _resampleTimer.Trigger();
        SchedulePoints();
    }

    private void RequestSample()
    {
        _dirty = true;
        if (_sampling) return;
        var size = Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return;
        _dirty = false;
        _sampling = true;
        var snap = Snapshot();
        Task.Run(() => Compute(snap, CancellationToken.None)).ContinueWith(t =>
        {
            _dispatcher.Post(() =>
            {
                _sampling = false;
                // A slightly stale result is still shown (so fast slider moves never starve the display); it is then refreshed.
                if (t.IsCompletedSuccessfully)
                {
                    _cache = t.Result;
                    if (t.Result.Version != _version) _dirty = true;
                }
                InvalidateVisual();
                if (_dirty) RequestSample();
                else SchedulePoints();
            });
        }, TaskScheduler.Default);
    }

    private void SchedulePoints()
    {
        if (!ShowPointsOfInterest)
        {
            if (_pois.Count > 0) SetPointsOfInterest([]);
            return;
        }
        _poiTimer.Trigger();
    }

    private void StartPointsOfInterest()
    {
        if (_poiRunning)
        {
            _poiDirty = true;
            _poiCts?.Cancel();
            return;
        }
        _poiDirty = false;
        _poiRunning = true;
        var snap = Snapshot();
        var cts = new CancellationTokenSource();
        _poiCts = cts;
        Task.Run(() => FindPoints(snap, cts.Token)).ContinueWith(t =>
        {
            _dispatcher.Post(() =>
            {
                _poiRunning = false;
                if (t.IsCompletedSuccessfully && !_poiDirty) SetPointsOfInterest(t.Result);
                if (_poiDirty) StartPointsOfInterest();
            });
        }, TaskScheduler.Default);
    }

    private static IReadOnlyList<PointOfInterest> FindPoints(Snap s, CancellationToken ct)
    {
        if (!s.Points || s.Items.Count == 0) return [];
        var map = s.Items.ToDictionary(x => x.Item, x => x.P);
        int samples = (int)Math.Clamp(s.Size.Width * 2, 400, 3000);
        try
        {
            return PointsOfInterestFinder.Find([.. s.Items.Select(x => x.Item)], it => map.TryGetValue(it, out var p) ? p : [], s.View, s.Session,
                new PoiOptions { Samples = samples }, null, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ArithmeticException or CalcException or InvalidOperationException)
        {
            return [];
        }
    }

    private void SetPointsOfInterest(IReadOnlyList<PointOfInterest> pois)
    {
        _pois = pois;
        if (SelectedPoint is { } sel)
        {
            var match = pois.FirstOrDefault(p => p.Kind == sel.Kind && p.Item.Name == sel.Item.Name && p.Other?.Name == sel.Other?.Name &&
                                                 Math.Abs(p.X - sel.X) <= 1e-6 * Math.Max(1, Viewport.Width));
            if (match is not null) SelectedPoint = match;
        }
        InvalidateVisual();
        PointsOfInterestChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs an action on the UI thread once calls to <see cref="Trigger"/> have stopped for a moment.
    /// Uses Task.Delay rather than a dispatcher timer so it also works under the headless test dispatcher.
    /// </summary>
    private sealed class Debouncer(TimeSpan delay, Action action)
    {
        // Captured when the owning control creates it, on that control's UI thread.
        private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
        private int _generation;

        public bool Pending { get; private set; }

        public void Trigger()
        {
            int g = ++_generation;
            Pending = true;
            Task.Delay(delay).ContinueWith(_ => _dispatcher.Post(() =>
            {
                if (g != _generation) return;
                Pending = false;
                action();
            }), TaskScheduler.Default);
        }

        public void Cancel()
        {
            _generation++;
            Pending = false;
        }
    }

    /// <summary>Stops background work.</summary>
    public void Dispose()
    {
        _resampleTimer.Cancel();
        _poiTimer.Cancel();
        _poiCts?.Cancel();
        if (_subscribedParameters is not null) _subscribedParameters.ValuesChanged -= OnParameterChanged;
        GC.SuppressFinalize(this);
    }
}
