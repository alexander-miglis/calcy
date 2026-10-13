using Avalonia;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Layout;

/// <summary>
/// A laid-out piece of maths (like a TeX box): a width, a height above the baseline (ascent) and a depth below it
/// (descent). Children are positioned relative to the parent's origin, which is the left end of its baseline.
/// </summary>
public abstract class MathBox
{
    /// <summary>Advance width.</summary>
    public double Width { get; internal set; }

    /// <summary>Height above the baseline.</summary>
    public double Ascent { get; internal set; }

    /// <summary>Depth below the baseline.</summary>
    public double Descent { get; internal set; }

    /// <summary>Total height.</summary>
    public double Height => Ascent + Descent;

    /// <summary>Horizontal offset of this box's origin from the parent's origin.</summary>
    public double OffsetX { get; internal set; }

    /// <summary>Vertical offset of this box's baseline from the parent's baseline (positive is down).</summary>
    public double OffsetY { get; internal set; }

    /// <summary>Absolute origin (left end of the baseline), set by <see cref="MathLayout.Arrange"/>.</summary>
    public Point Origin { get; internal set; }

    /// <summary>Absolute bounds, valid after <see cref="MathLayout.Arrange"/>.</summary>
    public Rect Bounds => new(Origin.X, Origin.Y - Ascent, System.Math.Max(0, Width), System.Math.Max(0, Height));

    /// <summary>Colour role for drawing.</summary>
    public MathColorRole Color { get; internal set; }

    /// <summary>Child boxes.</summary>
    public virtual IReadOnlyList<MathBox> Children => [];
}

/// <summary>A box holding children at arbitrary offsets.</summary>
public class MathContainerBox : MathBox
{
    private readonly List<MathBox> _children = [];

    /// <inheritdoc />
    public override IReadOnlyList<MathBox> Children => _children;

    /// <summary>Adds a child with its origin at (<paramref name="x"/>, <paramref name="y"/>) and grows the extents.</summary>
    public void Add(MathBox child, double x, double y)
    {
        child.OffsetX = x;
        child.OffsetY = y;
        _children.Add(child);
        Width = System.Math.Max(Width, x + child.Width);
        Ascent = System.Math.Max(Ascent, child.Ascent - y);
        Descent = System.Math.Max(Descent, child.Descent + y);
    }
}

/// <summary>The layout of one model row (the root or a template slot), with the caret stops.</summary>
public sealed class MathRowBox : MathContainerBox
{
    internal MathRowBox(MathRow row, double fontSize, int level, MathFontMetrics metrics)
    {
        Row = row;
        FontSize = fontSize;
        Level = level;
        Metrics = metrics;
    }

    /// <summary>The model row.</summary>
    public MathRow Row { get; }

    /// <summary>Font size of the row's text.</summary>
    public double FontSize { get; }

    /// <summary>Style level: 0 display, 1 text, 2 script, 3 script-script.</summary>
    public int Level { get; }

    /// <summary>Font metrics at <see cref="FontSize"/>.</summary>
    public MathFontMetrics Metrics { get; }

    /// <summary>Caret x positions relative to this box's origin, one per gap (Count + 1 entries).</summary>
    public double[] CaretX { get; internal set; } = [0];

    /// <summary>The box of each item in the row (null for items without a box).</summary>
    public MathBox?[] ItemBoxes { get; internal set; } = [];
}

/// <summary>A run of text drawn with the maths font.</summary>
public sealed class MathGlyphBox : MathBox
{
    internal MathGlyphBox(string text, double fontSize, bool italic, MathColorRole color)
    {
        Text = text;
        FontSize = fontSize;
        Italic = italic;
        Color = color;
    }

    /// <summary>The text to draw.</summary>
    public string Text { get; }

    /// <summary>Font size.</summary>
    public double FontSize { get; }

    /// <summary>Italic style.</summary>
    public bool Italic { get; }
}

/// <summary>A filled rectangle (fraction bar, radical overbar): it spans the box's ascent and descent.</summary>
public sealed class MathRuleBox : MathBox
{
    internal MathRuleBox(double width, double thickness)
    {
        Width = width;
        Ascent = thickness;
    }
}

/// <summary>Stroked shapes drawn to fill their box.</summary>
public enum MathShape
{
    /// <summary>The radical sign of a root (the overbar is a separate rule).</summary>
    Radical,

    /// <summary>Opening round bracket.</summary>
    ParenLeft,

    /// <summary>Closing round bracket.</summary>
    ParenRight,

    /// <summary>Opening square bracket.</summary>
    BracketLeft,

    /// <summary>Closing square bracket.</summary>
    BracketRight,

    /// <summary>Opening curly brace.</summary>
    BraceLeft,

    /// <summary>Closing curly brace.</summary>
    BraceRight,

    /// <summary>Vertical bar.</summary>
    Bar,

    /// <summary>Summation sign Σ.</summary>
    Sigma,

    /// <summary>Product sign Π.</summary>
    Pi,

    /// <summary>Integral sign ∫.</summary>
    Integral,
}

/// <summary>A stroked shape (delimiters, radical sign, big operators) that grows with its box.</summary>
public sealed class MathShapeBox : MathBox
{
    internal MathShapeBox(MathShape shape, double width, double ascent, double descent, double thickness, MathColorRole color)
    {
        Shape = shape;
        Width = width;
        Ascent = ascent;
        Descent = descent;
        Thickness = thickness;
        Color = color;
    }

    /// <summary>The shape.</summary>
    public MathShape Shape { get; }

    /// <summary>Stroke thickness.</summary>
    public double Thickness { get; }
}

/// <summary>An empty slot, drawn as a small dashed box (optionally with faint placeholder text).</summary>
public sealed class MathSlotBox : MathBox
{
    internal MathSlotBox(MathRow row, string? placeholder, double fontSize)
    {
        Row = row;
        Placeholder = placeholder;
        FontSize = fontSize;
        Color = MathColorRole.Muted;
    }

    /// <summary>The empty row.</summary>
    public MathRow Row { get; }

    /// <summary>Faint text shown inside, such as "otherwise".</summary>
    public string? Placeholder { get; }

    /// <summary>Font size of the slot.</summary>
    public double FontSize { get; }
}

/// <summary>Horizontal space.</summary>
public sealed class MathSpaceBox : MathBox
{
    internal MathSpaceBox(double width) => Width = width;
}
