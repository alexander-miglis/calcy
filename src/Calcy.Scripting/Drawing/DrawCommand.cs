using System.Globalization;
using System.Text;

namespace Calcy.Scripting.Drawing;

/// <summary>What a <see cref="DrawCommand"/> does.</summary>
public enum DrawKind
{
    /// <summary>Fills the whole canvas with <see cref="DrawCommand.Color"/>.</summary>
    Clear,

    /// <summary>Changes the background colour: pixels that showed the old background now show the new one.</summary>
    Background,

    /// <summary>The program changed its current colour (recorded for completeness; shapes carry their own colour).</summary>
    Color,

    /// <summary>One pixel at (X, Y).</summary>
    Pixel,

    /// <summary>A line from (X, Y) to (X2, Y2), <see cref="DrawCommand.Width"/> pixels wide.</summary>
    Line,

    /// <summary>A rectangle with its top left corner at (X, Y) and size (X2, Y2); filled when <see cref="DrawCommand.Fill"/>.</summary>
    Rect,

    /// <summary>A circle centred on (X, Y) with radius <see cref="DrawCommand.Size"/>.</summary>
    Circle,

    /// <summary>An elliptical arc in the box (X, Y, X2 wide, Y2 high) from angle Start over Extent degrees (counterclockwise).</summary>
    Arc,

    /// <summary>A polygon through <see cref="DrawCommand.Points"/> (x0, y0, x1, y1, ...).</summary>
    Polygon,

    /// <summary>Text with its top left corner at (X, Y), scaled by <see cref="DrawCommand.Size"/>.</summary>
    Text,

    /// <summary>Shows what was drawn so far (the end of a frame when the canvas is double buffered).</summary>
    Show,
}

/// <summary>
/// One drawing operation in canvas pixel coordinates ((0, 0) is the top left pixel, y grows downwards). The compatibility
/// modules (turtle, ti_plotlib, ti_draw, casioplot, calcydraw) translate their own coordinate systems into these.
/// </summary>
public sealed record DrawCommand
{
    /// <summary>The operation.</summary>
    public required DrawKind Kind { get; init; }

    /// <summary>The colour of the shape, the new current colour, or the clear or background colour.</summary>
    public DrawColor Color { get; init; } = DrawColor.Ink;

    /// <summary>First x coordinate.</summary>
    public double X { get; init; }

    /// <summary>First y coordinate.</summary>
    public double Y { get; init; }

    /// <summary>Second x coordinate, or the width of a rectangle or arc box.</summary>
    public double X2 { get; init; }

    /// <summary>Second y coordinate, or the height of a rectangle or arc box.</summary>
    public double Y2 { get; init; }

    /// <summary>Circle radius or text scale.</summary>
    public double Size { get; init; } = 1;

    /// <summary>Pen width in pixels for outlines and lines.</summary>
    public double Width { get; init; } = 1;

    /// <summary>True for filled shapes.</summary>
    public bool Fill { get; init; }

    /// <summary>Arc start angle in degrees (0 is east, counterclockwise).</summary>
    public double Start { get; init; }

    /// <summary>Arc extent in degrees.</summary>
    public double Extent { get; init; }

    /// <summary>The text of a <see cref="DrawKind.Text"/> command.</summary>
    public string? Text { get; init; }

    /// <summary>Polygon vertices as x0, y0, x1, y1, ...</summary>
    public IReadOnlyList<double>? Points { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        static string N(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder(Kind.ToString());
        switch (Kind)
        {
            case DrawKind.Clear or DrawKind.Background or DrawKind.Color:
                sb.Append(' ').Append(Color);
                break;
            case DrawKind.Pixel:
                sb.Append(' ').Append(N(X)).Append(',').Append(N(Y)).Append(' ').Append(Color);
                break;
            case DrawKind.Line:
                sb.Append(' ').Append(N(X)).Append(',').Append(N(Y)).Append(' ').Append(N(X2)).Append(',').Append(N(Y2)).Append(' ').Append(Color);
                if (Width != 1) sb.Append(" w").Append(N(Width));
                break;
            case DrawKind.Rect:
                sb.Append(Fill ? " fill " : " ").Append(N(X)).Append(',').Append(N(Y)).Append(' ').Append(N(X2)).Append('x').Append(N(Y2)).Append(' ').Append(Color);
                break;
            case DrawKind.Circle:
                sb.Append(Fill ? " fill " : " ").Append(N(X)).Append(',').Append(N(Y)).Append(" r").Append(N(Size)).Append(' ').Append(Color);
                break;
            case DrawKind.Arc:
                sb.Append(Fill ? " fill " : " ").Append(N(X)).Append(',').Append(N(Y)).Append(' ').Append(N(X2)).Append('x').Append(N(Y2))
                    .Append(' ').Append(N(Start)).Append('+').Append(N(Extent)).Append(' ').Append(Color);
                break;
            case DrawKind.Polygon:
                sb.Append(Fill ? " fill" : "").Append(" [");
                for (int i = 0; Points is not null && i + 1 < Points.Count; i += 2)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(N(Points[i])).Append(',').Append(N(Points[i + 1]));
                }
                sb.Append("] ").Append(Color);
                break;
            case DrawKind.Text:
                sb.Append(' ').Append(N(X)).Append(',').Append(N(Y)).Append(" \"").Append(Text).Append("\" ").Append(Color);
                break;
        }
        return sb.ToString();
    }
}

/// <summary>The turtle cursor drawn over the canvas (not part of the picture).</summary>
/// <param name="X">Canvas x of the turtle.</param>
/// <param name="Y">Canvas y of the turtle.</param>
/// <param name="Heading">Heading in degrees, 0 east and counterclockwise.</param>
/// <param name="Color">Pen colour.</param>
public sealed record DrawSprite(double X, double Y, double Heading, DrawColor Color);
