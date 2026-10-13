using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Calcy.Scripting.Drawing;

namespace Calcy.Modes.Notebook.Views;

/// <summary>
/// The 8-bit slider of the style board: an outlined track filled with the header colour up to a square thumb in the
/// selection colour. Drag or click to set; arrows step, Page Up and Down take ten steps, Home and End jump to the ends.
/// </summary>
internal sealed class NbSlider : RangeBase
{
    public static readonly StyledProperty<double> StepProperty = AvaloniaProperty.Register<NbSlider, double>(nameof(Step), 0.1);

    private const double ThumbWidth = 14;
    private bool _dragging;

    static NbSlider()
    {
        FocusableProperty.OverrideDefaultValue<NbSlider>(true);
        AffectsRender<NbSlider>(ValueProperty, MinimumProperty, MaximumProperty);
        HeightProperty.OverrideDefaultValue<NbSlider>(28);
    }

    public NbSlider()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "Slider");
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>Raised when the user moves the thumb (not when the value is set from code).</summary>
    public event EventHandler<double>? UserChanged;

    private double TrackLeft => ThumbWidth / 2 + 1;
    private double TrackRight => Math.Max(TrackLeft + 1, Bounds.Width - ThumbWidth / 2 - 1);
    private double Ratio => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    private IBrush Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

    public override void Render(DrawingContext ctx)
    {
        double h = Bounds.Height, cy = Math.Round(h / 2);
        var line = Res("Calcy.Line");
        var track = new Rect(Math.Round(TrackLeft), cy - 5, Math.Round(TrackRight - TrackLeft), 10);
        ctx.FillRectangle(Res("Calcy.Off"), track);
        double tx = Math.Round(TrackLeft + Ratio * (TrackRight - TrackLeft));
        ctx.FillRectangle(Res("Calcy.Head"), new Rect(track.X, track.Y, Math.Max(0, tx - track.X), track.Height));
        ctx.DrawRectangle(null, new Pen(line, 2), track.Deflate(1));
        var thumb = new Rect(tx - ThumbWidth / 2, cy - 11, ThumbWidth, 22);
        ctx.FillRectangle(Res("Calcy.Sel"), thumb);
        ctx.DrawRectangle(null, new Pen(line, 2), thumb.Deflate(1));
        if (IsFocused)
            ctx.DrawRectangle(null, new Pen(Res("Calcy.Ink"), 1, new DashStyle([2, 2], 0)), new Rect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), h - 1));
    }

    private void SetByUser(double v)
    {
        v = Snap(v);
        if (v == Value) return;
        Value = v;
        UserChanged?.Invoke(this, v);
    }

    private void SetFromX(double x) => SetByUser(Minimum + Math.Clamp((x - TrackLeft) / (TrackRight - TrackLeft), 0, 1) * (Maximum - Minimum));

    private double Snap(double v)
    {
        if (Step > 0)
        {
            v = Minimum + Math.Round((v - Minimum) / Step) * Step;
            v = Math.Round(v, Math.Clamp((int)Math.Ceiling(-Math.Log10(Step) - 1e-9), 0, 10));
        }
        return Math.Clamp(v, Minimum, Maximum);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        e.Pointer.Capture(this);
        SetFromX(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFromX(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers != KeyModifiers.None) return;
        double step = Step > 0 ? Step : (Maximum - Minimum) / 100;
        switch (e.Key)
        {
            case Key.Left or Key.Down: SetByUser(Value - step); break;
            case Key.Right or Key.Up: SetByUser(Value + step); break;
            case Key.PageDown: SetByUser(Value - 10 * step); break;
            case Key.PageUp: SetByUser(Value + 10 * step); break;
            case Key.Home: SetByUser(Minimum); break;
            case Key.End: SetByUser(Maximum); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }
}

/// <summary>
/// A toolbar layout: children flow left to right and wrap; children marked <see cref="RightProperty"/> sit at the
/// right end of the last line when they fit there, otherwise on a line of their own, still right aligned.
/// </summary>
internal sealed class FlowBar : Panel
{
    public static readonly AttachedProperty<bool> RightProperty = AvaloniaProperty.RegisterAttached<FlowBar, Control, bool>("Right");

    public static void SetRight(Control c, bool value) => c.SetValue(RightProperty, value);

    public static bool GetRight(Control c) => c.GetValue(RightProperty);

    public double Spacing { get; set; } = 6;

    public double LineSpacing { get; set; } = 6;

    private List<List<Control>> Lines(double width, out List<Control> right, out double rightWidth)
    {
        var lines = new List<List<Control>> { new() };
        double x = 0;
        foreach (var c in Children.Where(c => c.IsVisible && !GetRight(c)))
        {
            double w = c.DesiredSize.Width;
            if (x > 0 && x + w > width)
            {
                lines.Add([]);
                x = 0;
            }
            lines[^1].Add(c);
            x += w + Spacing;
        }
        right = [.. Children.Where(c => c.IsVisible && GetRight(c))];
        rightWidth = right.Sum(c => c.DesiredSize.Width) + Math.Max(0, right.Count - 1) * Spacing;
        return lines;
    }

    private double LineWidth(List<Control> line) => line.Sum(c => c.DesiredSize.Width) + Math.Max(0, line.Count - 1) * Spacing;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var c in Children) c.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : double.MaxValue;
        var lines = Lines(width, out var right, out var rw);
        double h = 0, maxW = 0;
        foreach (var line in lines)
        {
            if (line.Count == 0) continue;
            h += (h > 0 ? LineSpacing : 0) + line.Max(c => c.DesiredSize.Height);
            maxW = Math.Max(maxW, LineWidth(line));
        }
        if (right.Count > 0)
        {
            double rh = right.Max(c => c.DesiredSize.Height);
            var last = lines[^1];
            bool fits = last.Count == 0 || LineWidth(last) + Spacing * 2 + rw <= width;
            if (fits)
            {
                double lastH = last.Count == 0 ? 0 : last.Max(c => c.DesiredSize.Height);
                h += Math.Max(0, rh - lastH) + (last.Count == 0 && h > 0 ? LineSpacing : 0);
                maxW = Math.Max(maxW, (last.Count == 0 ? 0 : LineWidth(last) + Spacing * 2) + rw);
            }
            else
            {
                h += LineSpacing + rh;
                maxW = Math.Max(maxW, rw);
            }
        }
        return new Size(double.IsFinite(availableSize.Width) ? Math.Min(maxW, availableSize.Width) : maxW, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var lines = Lines(finalSize.Width, out var right, out var rw);
        double y = 0;
        double lastTop = 0, lastHeight = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Count == 0) continue;
            double lh = line.Max(c => c.DesiredSize.Height);
            if (i == lines.Count - 1 && right.Count > 0 && LineWidth(line) + Spacing * 2 + rw <= finalSize.Width)
                lh = Math.Max(lh, right.Max(c => c.DesiredSize.Height));
            double x = 0;
            foreach (var c in line)
            {
                c.Arrange(new Rect(x, y + (lh - c.DesiredSize.Height) / 2, c.DesiredSize.Width, c.DesiredSize.Height));
                x += c.DesiredSize.Width + Spacing;
            }
            lastTop = y;
            lastHeight = lh;
            y += lh + LineSpacing;
        }
        if (right.Count > 0)
        {
            var last = lines[^1];
            bool fits = last.Count == 0 || LineWidth(last) + Spacing * 2 + rw <= finalSize.Width;
            double rh = right.Max(c => c.DesiredSize.Height);
            double top = fits && last.Count > 0 ? lastTop : (last.Count == 0 && lines.Count == 1 ? 0 : y);
            double lineH = fits && last.Count > 0 ? lastHeight : rh;
            double x = Math.Max(0, finalSize.Width - rw);
            foreach (var c in right)
            {
                c.Arrange(new Rect(x, top + (lineH - c.DesiredSize.Height) / 2, c.DesiredSize.Width, c.DesiredSize.Height));
                x += c.DesiredSize.Width + Spacing;
            }
        }
        return finalSize;
    }
}

/// <summary>A decorator that draws a dashed line along its top edge, or a dashed outline (the output separator and the selected text cell).</summary>
internal sealed class DashedFrame : Decorator
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<DashedFrame, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<bool> OutlineProperty = AvaloniaProperty.Register<DashedFrame, bool>(nameof(Outline));

    public static readonly StyledProperty<bool> ShowStrokeProperty = AvaloniaProperty.Register<DashedFrame, bool>(nameof(ShowStroke), true);

    static DashedFrame() => AffectsRender<DashedFrame>(StrokeProperty, OutlineProperty, ShowStrokeProperty);

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>True for a full outline, false for a top line only.</summary>
    public bool Outline
    {
        get => GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    public bool ShowStroke
    {
        get => GetValue(ShowStrokeProperty);
        set => SetValue(ShowStrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Stroke is null || !ShowStroke) return;
        var pen = new Pen(Stroke, 2, new DashStyle([3, 2], 0), PenLineCap.Flat);
        if (Outline) context.DrawRectangle(null, pen, new Rect(1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2)));
        else context.DrawLine(pen, new Point(0, 1), new Point(Bounds.Width, 1));
    }
}

/// <summary>
/// Shows a drawing from a code cell: either a captured frame or the live canvas while the code runs (polled at 60
/// frames a second). Pixels are scaled without smoothing so drawings stay crisp; scheme tokens use the current scheme.
/// </summary>
internal sealed class FramePicture : Control
{
    private static readonly string[] PaletteTokens = ["Card", "Ink", "Muted", "Grid", "G1", "G2", "G3", "G4", "G5"];

    private readonly Color[] _palette = new Color[PaletteTokens.Length];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DrawCanvas? _live;
    private uint[]? _pixels;
    private int _version = -1;
    private int _width = DrawCanvas.DefaultWidth;
    private int _height = DrawCanvas.DefaultHeight;
    private WriteableBitmap? _bitmap;
    private bool _dirty = true;

    public FramePicture()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        for (int i = 0; i < PaletteTokens.Length; i++)
        {
            int index = i;
            this.GetResourceObservable($"Calcy.{PaletteTokens[i]}.Color").Subscribe(new Observer(v =>
            {
                if (v is Color c) _palette[index] = c;
                _dirty = true;
                InvalidateVisual();
            }));
        }
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>Shows a captured frame.</summary>
    public void Show(CanvasFrame frame)
    {
        _live = null;
        _timer.Stop();
        _pixels = frame.Pixels;
        _width = frame.Width;
        _height = frame.Height;
        _dirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Follows a canvas while code draws on it.</summary>
    public void Follow(DrawCanvas canvas)
    {
        _live = canvas;
        _version = -1;
        Poll();
        if (VisualRoot is not null) _timer.Start();
    }

    /// <summary>True when there is a picture to show.</summary>
    public bool HasPicture => _pixels is not null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_live is not null) _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void Poll()
    {
        if (_live is null) return;
        if (!_live.TryCopyFrame(ref _pixels, ref _version, out var w, out var h, out _, out _)) return;
        if (w != _width || h != _height) InvalidateMeasure();
        (_width, _height) = (w, h);
        _dirty = true;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Whole pixel scales keep drawings crisp: twice the size for small canvases, the canvas size otherwise.
        double w = Math.Max(1, _width), h = Math.Max(1, _height);
        double scale = w <= 240 && h <= 240 ? 2 : 1;
        double width = w * scale;
        if (double.IsFinite(availableSize.Width) && width > availableSize.Width) width = Math.Max(1, availableSize.Width);
        return new Size(width, width * h / w);
    }

    public override void Render(DrawingContext context)
    {
        if (_pixels is null) return;
        if (_dirty) UpdateBitmap();
        if (_bitmap is null) return;
        context.DrawImage(_bitmap, new Rect(0, 0, _width, _height), new Rect(Bounds.Size));
    }

    private void UpdateBitmap()
    {
        _dirty = false;
        if (_pixels is null || _width <= 0 || _height <= 0 || _pixels.Length < _width * _height) return;
        if (_bitmap is null || _bitmap.PixelSize.Width != _width || _bitmap.PixelSize.Height != _height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        var palette = new int[_palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            var c = _palette[i];
            palette[i] = unchecked((int)(0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
        }
        using var fb = _bitmap.Lock();
        var row = new int[_width];
        for (int y = 0; y < _height; y++)
        {
            int o = y * _width;
            for (int x = 0; x < _width; x++)
            {
                uint v = _pixels[o + x];
                row[x] = (v & 0xFF000000u) == 0 ? palette[Math.Min(v, (uint)palette.Length - 1)] : unchecked((int)v);
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, _width);
        }
    }

    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(object? value) => next(value);
    }
}
