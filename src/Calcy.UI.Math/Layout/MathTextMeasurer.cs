namespace Calcy.UI.Math.Layout;

/// <summary>Size of a run of text: advance width and the ink extent above and below the baseline.</summary>
/// <param name="Width">Advance width.</param>
/// <param name="Ascent">Ink height above the baseline (0 or more).</param>
/// <param name="Descent">Ink depth below the baseline (0 or more).</param>
public readonly record struct MathTextMetrics(double Width, double Ascent, double Descent);

/// <summary>Font-wide measurements at one size, used for spacing, fraction bars and the caret.</summary>
/// <param name="FontSize">The font size.</param>
/// <param name="Ascent">Line ascent of the font.</param>
/// <param name="Descent">Line descent of the font.</param>
/// <param name="XHeight">Height of a lower-case x.</param>
/// <param name="CapHeight">Height of a capital H (also used for digits).</param>
/// <param name="AxisHeight">Height of the maths axis (the middle of a plus sign); fraction bars sit on it.</param>
public readonly record struct MathFontMetrics(double FontSize, double Ascent, double Descent, double XHeight, double CapHeight, double AxisHeight)
{
    /// <summary>Thickness of fraction bars and strokes: chunky, scaled with the size, at least 1.</summary>
    public double RuleThickness => System.Math.Max(1, System.Math.Round(FontSize * 0.08));
}

/// <summary>Measures text for the layout engine. The Avalonia implementation uses FormattedText; tests can use a fixed-width fake.</summary>
public interface IMathTextMeasurer
{
    /// <summary>Measures a run of text.</summary>
    MathTextMetrics Measure(string text, double fontSize, bool italic);

    /// <summary>Font-wide metrics at a size.</summary>
    MathFontMetrics GetFontMetrics(double fontSize);
}

/// <summary>A deterministic measurer with monospace-like proportions, for tests and for layout without a platform.</summary>
public sealed class FixedWidthTextMeasurer : IMathTextMeasurer
{
    /// <summary>Advance of one character, as a fraction of the font size.</summary>
    public double Advance { get; init; } = 0.6;

    /// <inheritdoc />
    public MathTextMetrics Measure(string text, double fontSize, bool italic)
    {
        int n = new System.Globalization.StringInfo(text).LengthInTextElements;
        double asc = 0, desc = 0;
        foreach (char c in text)
        {
            double a, d = 0;
            if (char.IsAsciiLetterLower(c) && "bdfhklt".IndexOf(c) < 0) a = 0.5;
            else if (c is '-' or '−' or '+' or '=' or '·' or '<' or '>' or '≤' or '≥' or '×') a = 0.45;
            else if (c is '.' or ',') a = 0.1;
            else if (c == ' ') a = 0;
            else a = 0.7;
            if ("gjpqy,".IndexOf(c) >= 0) d = 0.2;
            if (c is '(' or ')' or '[' or ']' or '{' or '}' or '|') { a = 0.75; d = 0.2; }
            asc = System.Math.Max(asc, a);
            desc = System.Math.Max(desc, d);
        }
        return new MathTextMetrics(n * Advance * fontSize, asc * fontSize, desc * fontSize);
    }

    /// <inheritdoc />
    public MathFontMetrics GetFontMetrics(double fontSize) =>
        new(fontSize, 0.9 * fontSize, 0.25 * fontSize, 0.5 * fontSize, 0.7 * fontSize, 0.3 * fontSize);
}
