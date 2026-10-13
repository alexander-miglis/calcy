namespace Calcy.Graphing;

/// <summary>Colours used to export a graph, as CSS colour strings (for example "#0058F8"). Pass the current scheme's tokens.</summary>
/// <param name="Background">Plot background (scheme token Card).</param>
/// <param name="Grid">Grid lines (Grid).</param>
/// <param name="Axis">Axes and point labels (Ink).</param>
/// <param name="Label">Tick labels (Muted).</param>
/// <param name="Line">Outlines of points and markers (Line).</param>
/// <param name="Inks">Graph inks G1 to G5.</param>
/// <param name="Marker">Fill of points of interest (Key).</param>
/// <param name="Selection">Highlight colour (Sel).</param>
public sealed record GraphPalette(string Background, string Grid, string Axis, string Label, string Line, IReadOnlyList<string> Inks, string Marker, string Selection)
{
    /// <summary>The NES Classic scheme, the default.</summary>
    public static GraphPalette Nes { get; } = new("#FFFFFF", "#E4E4E4", "#000000", "#595959", "#000000",
        ["#0058F8", "#D82800", "#00A800", "#6844FC", "#AC7C00"], "#BCBCBC", "#F8B800");

    /// <summary>The ink for a colour slot (wraps around).</summary>
    public string Ink(int index) => Inks.Count == 0 ? Axis : Inks[((index % Inks.Count) + Inks.Count) % Inks.Count];
}

/// <summary>A graph ready to draw or export: the viewport, pixel size, sampled items, ticks and overlays.</summary>
public sealed class GraphScene
{
    /// <summary>The visible region.</summary>
    public required Viewport Viewport { get; init; }

    /// <summary>Width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Tick spacing style.</summary>
    public TickStyle TickStyle { get; init; } = TickStyle.Decimal;

    /// <summary>Sampled geometry per visible item, in drawing order.</summary>
    public IReadOnlyList<ItemGeometry> Items { get; init; } = [];

    /// <summary>Points of interest to mark.</summary>
    public IReadOnlyList<PointOfInterest> PointsOfInterest { get; init; } = [];

    /// <summary>Analysis overlays (shaded integrals, tangent lines).</summary>
    public IReadOnlyList<GraphAnnotation> Annotations { get; init; } = [];

    /// <summary>The pixel mapping.</summary>
    public ViewTransform Transform => new(Viewport, Width, Height);

    /// <summary>x-axis ticks.</summary>
    public IReadOnlyList<Tick> XTicks => Ticks.Generate(Viewport.XMin, Viewport.XMax, Width, TickStyle);

    /// <summary>y-axis ticks (always decimal).</summary>
    public IReadOnlyList<Tick> YTicks => Ticks.Generate(Viewport.YMin, Viewport.YMax, Height, TickStyle.Decimal, 44);

    /// <summary>Samples the visible plottable items for a viewport and pixel size.</summary>
    public static GraphScene Build(IReadOnlyList<GraphItem> items, ParameterSource parameters, Viewport viewport, int width, int height,
        TickStyle tickStyle = TickStyle.Decimal, SamplingOptions? options = null, IReadOnlyList<PointOfInterest>? points = null,
        IReadOnlyList<GraphAnnotation>? annotations = null, CancellationToken ct = default)
    {
        var view = new ViewTransform(viewport, width, height);
        var geos = new List<ItemGeometry>();
        foreach (var item in items)
        {
            if (!item.IsVisible || !item.IsPlottable) continue;
            geos.Add(GraphSampler.Sample(item, parameters(item), view, options, ct));
        }
        return new GraphScene
        {
            Viewport = viewport,
            Width = width,
            Height = height,
            TickStyle = tickStyle,
            Items = geos,
            PointsOfInterest = points ?? [],
            Annotations = annotations ?? [],
        };
    }
}
