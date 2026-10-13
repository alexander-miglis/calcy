namespace Calcy.Graphing;

/// <summary>Quality and budget settings for sampling. Distances are in pixels.</summary>
public sealed record SamplingOptions
{
    /// <summary>Spacing of the first, uniform samples.</summary>
    public double InitialSpacing { get; init; } = 4;

    /// <summary>Longest segment drawn as a straight line.</summary>
    public double MaxSegment { get; init; } = 4;

    /// <summary>Largest allowed distance between a curve's midpoint and the straight segment drawn for it.</summary>
    public double Flatness { get; init; } = 0.25;

    /// <summary>How many times an interval may be halved (near poles, jumps and domain edges).</summary>
    public int MaxDepth { get; init; } = 14;

    /// <summary>Most points per curve.</summary>
    public int MaxPoints { get; init; } = 60_000;

    /// <summary>A jump at least this large that does not shrink when the interval is halved is a discontinuity.</summary>
    public double JumpPixels { get; init; } = 3;

    /// <summary>Extra fraction of the viewport sampled on each side, so panning shows curves at once.</summary>
    public double Margin { get; init; } = 0.25;

    /// <summary>Extra fraction for implicit curves and regions (they cost more).</summary>
    public double ImplicitMargin { get; init; } = 0.12;

    /// <summary>Size of the smallest cells of implicit curves and regions.</summary>
    public double ImplicitCell { get; init; } = 2;

    /// <summary>Most leaf cells of one implicit plot (the cell size grows beyond this).</summary>
    public int ImplicitMaxCells { get; init; } = 600_000;

    /// <summary>Distance between slope field ticks.</summary>
    public double SlopeSpacing { get; init; } = 34;

    /// <summary>The default settings.</summary>
    public static readonly SamplingOptions Default = new();
}
