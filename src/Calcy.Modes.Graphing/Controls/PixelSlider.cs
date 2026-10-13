using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Calcy.Modes.Graphing.Controls;

/// <summary>
/// An 8-bit slider: a thick outlined track filled in the accent colour up to a square thumb.
/// Drag or click to set, arrows step, Page Up/Down take ten steps, Home and End jump to the ends.
/// </summary>
public sealed class PixelSlider : RangeBase
{
    /// <summary>Fill colour of the track up to the thumb.</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<PixelSlider, IBrush?>(nameof(Accent));

    /// <summary>Snap step (0 for none).</summary>
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<PixelSlider, double>(nameof(Step), 0.1);

    private bool _dragging;

    static PixelSlider()
    {
        FocusableProperty.OverrideDefaultValue<PixelSlider>(true);
        AffectsRender<PixelSlider>(AccentProperty, ValueProperty, MinimumProperty, MaximumProperty);
        HeightProperty.OverrideDefaultValue<PixelSlider>(30);
    }

    /// <summary>Creates a slider.</summary>
    public PixelSlider()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "Slider");
    }

    /// <inheritdoc cref="AccentProperty"/>
    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <inheritdoc cref="StepProperty"/>
    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    private const double ThumbWidth = 14;

    private double TrackLeft => ThumbWidth / 2 + 1;
    private double TrackRight => Math.Max(TrackLeft + 1, Bounds.Width - ThumbWidth / 2 - 1);

    private double Ratio => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    private IBrush Res(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    /// <inheritdoc />
    public override void Render(DrawingContext ctx)
    {
        double h = Bounds.Height, cy = Math.Round(h / 2);
        var line = Res("Calcy.Line", Brushes.Black);
        var track = new Rect(Math.Round(TrackLeft), cy - 4, Math.Round(TrackRight - TrackLeft), 8);
        ctx.FillRectangle(Res("Calcy.Card", Brushes.White), track);
        double tx = Math.Round(TrackLeft + Ratio * (TrackRight - TrackLeft));
        ctx.FillRectangle(Accent ?? Res("Calcy.G2", Brushes.Red), new Rect(track.X, track.Y, tx - track.X, track.Height));
        ctx.DrawRectangle(null, new Pen(line, 2), track.Deflate(1));
        var thumb = new Rect(tx - ThumbWidth / 2, cy - 11, ThumbWidth, 22);
        ctx.FillRectangle(Res("Calcy.Paper", Brushes.White), thumb);
        ctx.DrawRectangle(null, new Pen(line, 2), thumb.Deflate(1));
        ctx.FillRectangle(line, new Rect(tx - 1, cy - 5, 2, 10));
        if (IsFocused)
            ctx.DrawRectangle(null, new Pen(Res("Calcy.Ink", Brushes.Black), 1, new DashStyle([2, 2], 0)), new Rect(0.5, 0.5, Bounds.Width - 1, h - 1));
    }

    private void SetFromX(double x)
    {
        double r = Math.Clamp((x - TrackLeft) / (TrackRight - TrackLeft), 0, 1);
        Value = Snap(Minimum + r * (Maximum - Minimum));
    }

    private double Snap(double v)
    {
        if (Step > 0)
        {
            v = Minimum + Math.Round((v - Minimum) / Step) * Step;
            int decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(Step)) + 1, 0, 12);
            v = Math.Round(v, decimals);
        }
        return Math.Clamp(v, Minimum, Maximum);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFromX(e.GetPosition(this).X);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Step > 0 ? Step : (Maximum - Minimum) / 100;
        switch (e.Key)
        {
            case Key.Left or Key.Down: Value = Snap(Value - step); break;
            case Key.Right or Key.Up: Value = Snap(Value + step); break;
            case Key.PageDown: Value = Snap(Value - 10 * step); break;
            case Key.PageUp: Value = Snap(Value + 10 * step); break;
            case Key.Home: Value = Minimum; break;
            case Key.End: Value = Maximum; break;
            default: return;
        }
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }
}
