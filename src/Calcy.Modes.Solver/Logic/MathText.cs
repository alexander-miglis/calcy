using System.Text;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Modes.Solver.Logic;

/// <summary>Text forms of maths for cards, labels and the clipboard: the design's typographic minus, subscripts and ± pairs.</summary>
public static class MathText
{
    /// <summary>The minus sign used on screen (U+2212), as in the design boards.</summary>
    public const char Minus = '−';

    private const string SubscriptDigits = "₀₁₂₃₄₅₆₇₈₉";

    /// <summary>Display options that follow the calculator settings (digits, fix/sci).</summary>
    public static PrintOptions Options(CalcSettings settings) => new() { Numbers = NumberFormatOptions.From(settings) };

    /// <summary>The expression as on-screen text: Unicode maths with the typographic minus.</summary>
    public static string Show(Expr e, CalcSettings settings) => Typographic(ExprPrinter.Print(e, Options(settings)));

    /// <summary>Linear Calcy input that reads back as the same expression.</summary>
    public static string Input(Expr e) => ExprPrinter.ToInput(e);

    /// <summary>Replaces the ASCII hyphen used as a minus sign with the typographic minus.</summary>
    public static string Typographic(string text) => text.Replace('-', Minus);

    /// <summary>Back to ASCII (for text that goes into an input).</summary>
    public static string Ascii(string text) => text.Replace(Minus, '-');

    /// <summary>A name with a subscript index: x₁, x₂ ...</summary>
    public static string Subscript(string name, int index)
    {
        var sb = new StringBuilder(name);
        foreach (var ch in index.ToString(System.Globalization.CultureInfo.InvariantCulture)) sb.Append(SubscriptDigits[ch - '0']);
        return sb.ToString();
    }

    /// <summary>
    /// Merges two values that differ only in one sign into a ± form: (5 − √13)/2 and (5 + √13)/2 give (5 ± √13)/2.
    /// Returns null when they differ in any other way.
    /// </summary>
    public static string? PlusMinus(string a, string b)
    {
        if (a.Length != b.Length) return null;
        int diff = -1;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (diff >= 0) return null;
            diff = i;
        }
        if (diff < 0) return null;
        bool minusPlus = IsMinus(a[diff]) && b[diff] == '+';
        bool plusMinus = a[diff] == '+' && IsMinus(b[diff]);
        if (!minusPlus && !plusMinus) return null;
        // Only a sign between two terms (a space on each side) can be merged; a leading sign belongs to the value.
        if (diff == 0 || diff + 1 >= a.Length || a[diff - 1] != ' ' || a[diff + 1] != ' ') return null;
        return a[..diff] + "±" + a[(diff + 1)..];
    }

    private static bool IsMinus(char c) => c is '-' or Minus;

    /// <summary>A short number for labels and axes (4 significant digits).</summary>
    public static string Short(double v, int digits = 4)
    {
        if (double.IsPositiveInfinity(v)) return "∞";
        if (double.IsNegativeInfinity(v)) return Minus + "∞";
        if (double.IsNaN(v)) return "?";
        return Typographic(NumberFormatter.Short(v, digits));
    }

    /// <summary>LaTeX for a value, with the a + bi form when it is given.</summary>
    public static string Latex(Expr e) => LatexPrinter.ToLatex(e);

    /// <summary>Joins LaTeX alternatives with "or".</summary>
    public static string LatexOr(IEnumerable<string> parts) => string.Join(@" \quad\text{or}\quad ", parts);

    /// <summary>Joins LaTeX statements that hold together (a solution of a system).</summary>
    public static string LatexAnd(IEnumerable<string> parts) => string.Join(@",\; ", parts);
}
