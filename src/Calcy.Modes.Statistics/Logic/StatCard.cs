using System.Text;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>One labelled value on a card ("x̄", "67.5").</summary>
/// <param name="Label">The label as read (Unicode allowed).</param>
/// <param name="Value">The formatted value.</param>
public sealed record CardLine(string Label, string Value);

/// <summary>
/// A result card (design decision D5): what was computed, the headline result, the values, a plain-language note and
/// the texts for COPY (plain text), COPY LATEX, USE (the equivalent calculator expression for Advanced mode) and the
/// recipe that SHOW PLOT reopens. Plain data, saved with the mode state.
/// </summary>
public sealed class StatCard
{
    /// <summary>The card number (#1, #2, ...).</summary>
    public int Number { get; set; }

    /// <summary>The title in the header strip, for example "LINREG AX+B L1,L2" (pixel font characters only).</summary>
    public string Title { get; set; } = "";

    /// <summary>The time the card was made (HH:mm).</summary>
    public string Time { get; set; } = "";

    /// <summary>A Calcy expression shown typeset at the top (an equation or a probability), or null.</summary>
    public string? Headline { get; set; }

    /// <summary>True when the headline is a Calcy expression to typeset (an equation); false for plain text lines.</summary>
    public bool MathHeadline { get; set; }

    /// <summary>The values, label and value.</summary>
    public List<CardLine> Lines { get; set; } = [];

    /// <summary>A sentence in plain words (the decision of a test), or null.</summary>
    public string? Note { get; set; }

    /// <summary>True for a card that reports a problem.</summary>
    public bool IsError { get; set; }

    /// <summary>The text COPY puts on the clipboard.</summary>
    public string CopyText { get; set; } = "";

    /// <summary>The LaTeX COPY LATEX puts on the clipboard, or null.</summary>
    public string? Latex { get; set; }

    /// <summary>The calculator expression USE sends to Advanced (for example "ttest(65, L2)"), or null.</summary>
    public string? UseText { get; set; }

    /// <summary>What SHOW PLOT reopens, or null when the card has no plot.</summary>
    public StatRecipe? Recipe { get; set; }

    /// <summary>A copy (so the same result can be kept on several cards).</summary>
    public StatCard Copy()
    {
        var c = (StatCard)MemberwiseClone();
        c.Lines = [.. Lines];
        return c;
    }

    /// <summary>Builds the plain text form: title, headline, lines and note.</summary>
    public static string PlainText(string title, string? headline, IEnumerable<CardLine> lines, string? note)
    {
        var sb = new StringBuilder();
        sb.Append(title.Trim());
        if (headline is not null) sb.Append('\n').Append(headline);
        foreach (var l in lines) sb.Append('\n').Append(l.Label).Append(" = ").Append(l.Value);
        if (note is not null) sb.Append('\n').Append(note);
        return sb.ToString();
    }

    /// <summary>An error card.</summary>
    public static StatCard Error(string title, string message, string? input = null) => new()
    {
        Title = title,
        IsError = true,
        Note = message,
        Lines = input is null ? [] : [new CardLine("input", input)],
        CopyText = title + "\n" + message,
    };
}

/// <summary>What a card's SHOW PLOT reopens: the analysis tab and its settings at the time.</summary>
public sealed class StatRecipe
{
    /// <summary>The tab.</summary>
    public StatTab Tab { get; set; }

    /// <summary>1-Var settings (for the 1-Var tab).</summary>
    public OneVarSettings? OneVar { get; set; }

    /// <summary>2-Var settings.</summary>
    public TwoVarSettings? TwoVar { get; set; }

    /// <summary>Regression settings.</summary>
    public RegressionSettings? Regression { get; set; }

    /// <summary>Test settings.</summary>
    public TestSettings? Test { get; set; }

    /// <summary>Distribution settings.</summary>
    public DistributionSettings? Distribution { get; set; }

    /// <summary>Plot settings.</summary>
    public PlotSettings? Plot { get; set; }
}

/// <summary>Builds LaTeX for card values.</summary>
public static class CardLatex
{
    /// <summary>The LaTeX spelling of a label such as x̄, Σx², σx, r², χ², H₀.</summary>
    public static string Label(string label)
    {
        var map = new (string From, string To)[]
        {
            ("Σx²", "\\sum x^2"), ("Σy²", "\\sum y^2"), ("Σxy", "\\sum xy"), ("Σx", "\\sum x"), ("Σy", "\\sum y"),
            ("x̄₁", "\\bar{x}_1"), ("x̄₂", "\\bar{x}_2"), ("x̄", "\\bar{x}"), ("ȳ", "\\bar{y}"), ("p̂₁", "\\hat{p}_1"), ("p̂₂", "\\hat{p}_2"), ("p̂", "\\hat{p}"),
            ("σx", "\\sigma_x"), ("σy", "\\sigma_y"), ("Sx₁", "s_{x_1}"), ("Sx₂", "s_{x_2}"), ("Sx", "s_x"), ("Sy", "s_y"), ("Sxp", "s_p"),
            ("r²", "r^2"), ("R²", "R^2"), ("χ²", "\\chi^2"), ("μ₀", "\\mu_0"), ("μ", "\\mu"), ("σ", "\\sigma"), ("α", "\\alpha"),
            ("H₀", "H_0"), ("H₁", "H_1"), ("ŷ", "\\hat{y}"), ("≠", "\\neq"), ("≤", "\\le"), ("≥", "\\ge"), ("n₁", "n_1"), ("n₂", "n_2"),
            ("ρ", "\\rho"), ("τ", "\\tau"), ("λ", "\\lambda"), ("θ", "\\theta"), ("γ", "\\gamma"), ("β", "\\beta"), ("ν", "\\nu"),
        };
        var s = label;
        foreach (var (from, to) in map) s = s.Replace(from, to);
        if (s.Length > 1 && s.All(char.IsLetter) && !s.StartsWith('\\')) s = "\\text{" + s + "}";
        else if (s.Contains(' ') && !s.Contains('\\')) s = "\\text{" + s + "}";
        return s;
    }

    /// <summary>An aligned block of label = value rows.</summary>
    public static string Aligned(IEnumerable<(string Label, string Value)> rows)
    {
        var sb = new StringBuilder("\\begin{aligned}\n");
        bool first = true;
        foreach (var (label, value) in rows)
        {
            if (!first) sb.Append(" \\\\\n");
            first = false;
            sb.Append(Label(label)).Append(" &= ").Append(value);
        }
        sb.Append("\n\\end{aligned}");
        return sb.ToString();
    }
}
