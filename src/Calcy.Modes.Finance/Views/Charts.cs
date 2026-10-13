using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Calcy.Modes.Finance.Views;

/// <summary>Shared drawing for the pixel bar charts: crisp rectangles with hard outlines in scheme colours.</summary>
public abstract class PixelChart : Control
{
    /// <summary>Outline of the bars.</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<PixelChart, IBrush?>(nameof(LineBrush));

    /// <summary>The axis line.</summary>
    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<PixelChart, IBrush?>(nameof(AxisBrush));

    static PixelChart()
    {
        AffectsRender<PixelChart>(LineBrushProperty, AxisBrushProperty);
    }

    /// <summary>Creates the chart with its outline and axis colours bound to the scheme.</summary>
    protected PixelChart()
    {
        this.Token(LineBrushProperty, "Calcy.Line");
        this.Token(AxisBrushProperty, "Calcy.Ink");
        ClipToBounds = true;
    }

    /// <summary>Outline of the bars.</summary>
    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>The axis line.</summary>
    public IBrush? AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    /// <summary>A filled box with a hard outline of the given thickness, snapped to whole pixels.</summary>
    protected void Box(DrawingContext ctx, double x, double y, double w, double h, IBrush? fill, double outline)
    {
        var r = Snap(x, y, w, h);
        if (r.Width <= 0 || r.Height <= 0) return;
        if (fill is not null) ctx.FillRectangle(fill, r);
        if (outline <= 0 || LineBrush is null) return;
        double t = Math.Min(outline, Math.Min(r.Width, r.Height) / 2);
        ctx.FillRectangle(LineBrush, new Rect(r.X, r.Y, r.Width, t));
        ctx.FillRectangle(LineBrush, new Rect(r.X, r.Bottom - t, r.Width, t));
        ctx.FillRectangle(LineBrush, new Rect(r.X, r.Y, t, r.Height));
        ctx.FillRectangle(LineBrush, new Rect(r.Right - t, r.Y, t, r.Height));
    }

    /// <summary>A rectangle with whole-pixel edges.</summary>
    protected static Rect Snap(double x, double y, double w, double h)
    {
        double l = Math.Round(x), t = Math.Round(y), r = Math.Round(x + w), b = Math.Round(y + h);
        return new Rect(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }

    /// <summary>A 4 px checkerboard of two colours (the 8-bit dither), used to tell series apart without relying on colour.</summary>
    protected static IBrush Dither(IBrush ink, IBrush? back) => new DrawingBrush
    {
        TileMode = TileMode.Tile,
        DestinationRect = new RelativeRect(0, 0, 4, 4, RelativeUnit.Absolute),
        SourceRect = new RelativeRect(0, 0, 4, 4, RelativeUnit.Absolute),
        Drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = back ?? Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0, 0, 4, 4)) },
                new GeometryDrawing { Brush = ink, Geometry = Geometry.Parse("M0 0h2v2H0z M2 2h2v2H2z") },
            },
        },
    };
}

/// <summary>Bars of the remaining balance over the life of a loan (or the growing balance of savings).</summary>
public sealed class BalanceChart : PixelChart
{
    /// <summary>Bar fill.</summary>
    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<BalanceChart, IBrush?>(nameof(BarBrush));

    private IReadOnlyList<double> _values = [];

    static BalanceChart() => AffectsRender<BalanceChart>(BarBrushProperty);

    /// <summary>Creates the chart in the design's head colour.</summary>
    public BalanceChart() => this.Token(BarBrushProperty, "Calcy.Head");

    /// <summary>Bar fill.</summary>
    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>The balances to draw (their size is used, not their sign).</summary>
    public IReadOnlyList<double> Values
    {
        get => _values;
        set
        {
            _values = value ?? [];
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        const double left = 12, right = 12, top = 26, bottom = 12;
        double baseY = h - bottom;
        if (_values.Count > 0)
        {
            double max = _values.Max(Math.Abs);
            double slot = (w - left - right) / _values.Count;
            double barW = Math.Max(2, Math.Floor(slot * 0.78));
            for (int i = 0; i < _values.Count; i++)
            {
                double v = max > 0 ? Math.Abs(_values[i]) / max : 0;
                double bh = Math.Round(v * (baseY - top));
                if (bh < 1) continue;
                Box(context, left + i * slot + (slot - barW) / 2, baseY - bh, barW, bh, BarBrush, 1);
            }
        }
        if (AxisBrush is not null) context.FillRectangle(AxisBrush, Snap(left - 2, baseY, w - left - right + 4, 2));
    }
}

/// <summary>Stacked yearly bars: principal (solid) below interest (dithered).</summary>
public sealed class InterestPrincipalChart : PixelChart
{
    /// <summary>Principal fill.</summary>
    public static readonly StyledProperty<IBrush?> PrincipalBrushProperty =
        AvaloniaProperty.Register<InterestPrincipalChart, IBrush?>(nameof(PrincipalBrush));

    /// <summary>Interest colour (drawn dithered over the background).</summary>
    public static readonly StyledProperty<IBrush?> InterestBrushProperty =
        AvaloniaProperty.Register<InterestPrincipalChart, IBrush?>(nameof(InterestBrush));

    /// <summary>Background showing through the dither.</summary>
    public static readonly StyledProperty<IBrush?> BackBrushProperty =
        AvaloniaProperty.Register<InterestPrincipalChart, IBrush?>(nameof(BackBrush));

    private IReadOnlyList<(double Principal, double Interest)> _values = [];

    static InterestPrincipalChart() => AffectsRender<InterestPrincipalChart>(PrincipalBrushProperty, InterestBrushProperty, BackBrushProperty);

    /// <summary>Creates the chart with the first two graph colours.</summary>
    public InterestPrincipalChart()
    {
        this.Token(PrincipalBrushProperty, "Calcy.G1");
        this.Token(InterestBrushProperty, "Calcy.G2");
        this.Token(BackBrushProperty, "Calcy.Card");
    }

    /// <summary>Principal fill.</summary>
    public IBrush? PrincipalBrush
    {
        get => GetValue(PrincipalBrushProperty);
        set => SetValue(PrincipalBrushProperty, value);
    }

    /// <summary>Interest colour.</summary>
    public IBrush? InterestBrush
    {
        get => GetValue(InterestBrushProperty);
        set => SetValue(InterestBrushProperty, value);
    }

    /// <summary>Background showing through the dither.</summary>
    public IBrush? BackBrush
    {
        get => GetValue(BackBrushProperty);
        set => SetValue(BackBrushProperty, value);
    }

    /// <summary>Yearly amounts (sizes are used, not signs).</summary>
    public IReadOnlyList<(double Principal, double Interest)> Values
    {
        get => _values;
        set
        {
            _values = value ?? [];
            InvalidateVisual();
        }
    }

    /// <summary>The dithered interest fill, also used by the legend.</summary>
    public static IBrush InterestFill(IBrush ink, IBrush? back) => Dither(ink, back);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        const double left = 12, right = 12, top = 12, bottom = 12;
        double baseY = h - bottom;
        if (_values.Count > 0)
        {
            double max = _values.Max(v => Math.Abs(v.Principal) + Math.Abs(v.Interest));
            double slot = (w - left - right) / _values.Count;
            double barW = Math.Max(2, Math.Min(28, Math.Floor(slot * 0.72)));
            var interest = InterestBrush is null ? null : Dither(InterestBrush, BackBrush);
            for (int i = 0; i < _values.Count; i++)
            {
                if (max <= 0) break;
                double x = left + i * slot + (slot - barW) / 2;
                double ph = Math.Round(Math.Abs(_values[i].Principal) / max * (baseY - top));
                double ih = Math.Round(Math.Abs(_values[i].Interest) / max * (baseY - top));
                double outline = barW >= 8 ? 2 : 1;
                if (ih >= 1) Box(context, x, baseY - ph - ih, barW, ih + (ph >= 1 ? outline : 0), interest, outline);
                if (ph >= 1) Box(context, x, baseY - ph, barW, ph, PrincipalBrush, outline);
            }
        }
        if (AxisBrush is not null) context.FillRectangle(AxisBrush, Snap(left - 2, baseY, w - left - right + 4, 2));
    }
}

/// <summary>Cash flow bars around a zero line: money in above (ok colour), money out below (memory colour).</summary>
public sealed class CashFlowChart : PixelChart
{
    /// <summary>Fill for money in.</summary>
    public static readonly StyledProperty<IBrush?> InBrushProperty =
        AvaloniaProperty.Register<CashFlowChart, IBrush?>(nameof(InBrush));

    /// <summary>Fill for money out.</summary>
    public static readonly StyledProperty<IBrush?> OutBrushProperty =
        AvaloniaProperty.Register<CashFlowChart, IBrush?>(nameof(OutBrush));

    private IReadOnlyList<double> _values = [];

    static CashFlowChart() => AffectsRender<CashFlowChart>(InBrushProperty, OutBrushProperty);

    /// <summary>Creates the chart in the design's colours.</summary>
    public CashFlowChart()
    {
        this.Token(InBrushProperty, "Calcy.Ok");
        this.Token(OutBrushProperty, "Calcy.Mem");
    }

    /// <summary>Fill for money in.</summary>
    public IBrush? InBrush
    {
        get => GetValue(InBrushProperty);
        set => SetValue(InBrushProperty, value);
    }

    /// <summary>Fill for money out.</summary>
    public IBrush? OutBrush
    {
        get => GetValue(OutBrushProperty);
        set => SetValue(OutBrushProperty, value);
    }

    /// <summary>The flows in time order (CF0 first).</summary>
    public IReadOnlyList<double> Values
    {
        get => _values;
        set
        {
            _values = value ?? [];
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        const double left = 16, right = 16, top = 10, bottom = 10;
        double maxIn = _values.Count == 0 ? 0 : Math.Max(0, _values.Max());
        double maxOut = _values.Count == 0 ? 0 : Math.Max(0, -_values.Min());
        double span = maxIn + maxOut;
        double usable = h - top - bottom;
        double zero = span > 0 ? Math.Round(top + usable * maxIn / span) : Math.Round(h / 2);
        if (span > 0)
        {
            double slot = (w - left - right) / _values.Count;
            double barW = Math.Max(2, Math.Min(60, Math.Floor(slot * 0.6)));
            double outline = barW >= 8 ? 2 : 1;
            for (int i = 0; i < _values.Count; i++)
            {
                double v = _values[i];
                double bh = Math.Round(Math.Abs(v) / span * usable);
                if (bh < 1) continue;
                double x = left + i * slot + (slot - barW) / 2;
                if (v > 0) Box(context, x, zero - bh, barW, bh + 1, InBrush, outline);
                else Box(context, x, zero, barW, bh, OutBrush, outline);
            }
        }
        if (AxisBrush is not null) context.FillRectangle(AxisBrush, Snap(left - 4, zero - 1, w - left - right + 8, 2));
    }
}
