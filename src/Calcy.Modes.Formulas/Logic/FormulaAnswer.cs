using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>What an answer is: a solved unknown, or a check of both sides of an identity.</summary>
public enum AnswerKind
{
    /// <summary>The formula was solved for one variable.</summary>
    Solved,

    /// <summary>Both sides of an identity were evaluated.</summary>
    Checked,
}

/// <summary>One value of an answer.</summary>
/// <param name="Number">The decimal for reading ("2.5", "3.644338583×10⁻¹⁹").</param>
/// <param name="Exact">The exact form when it adds something ("5/2", "√2"), or null.</param>
/// <param name="Use">A Calcy expression for the value that Advanced mode evaluates to the same number ("5/2", "3.6443385825e-19").</param>
/// <param name="Approx">The value as a double.</param>
public sealed record AnswerValue(string Number, string? Exact, string Use, double Approx);

/// <summary>A value that went into an answer.</summary>
/// <param name="Symbol">The variable symbol.</param>
/// <param name="Text">The value as typed, or the usual value.</param>
/// <param name="Unit">The variable's unit.</param>
/// <param name="IsUsual">True when the box was empty and the usual value was used.</param>
public sealed record GivenValue(string Symbol, string Text, string Unit, bool IsUsual);

/// <summary>A labelled Calcy expression that the USE button sends to Advanced mode.</summary>
/// <param name="Label">What it is, for example "a = 2.5 m/s²".</param>
/// <param name="Expression">The expression, for example "5/2".</param>
public sealed record CardUse(string Label, string Expression);

/// <summary>
/// The answer for one formula and one set of inputs, with every text the screen needs: the headline
/// ("a = 2.5 m/s²"), the inputs line, the clipboard text and the expressions for USE.
/// </summary>
public sealed record FormulaAnswer
{
    /// <summary>The formula.</summary>
    public required Formula Formula { get; init; }

    /// <summary>Solved or checked.</summary>
    public required AnswerKind Kind { get; init; }

    /// <summary>The variable that was solved for (null for an identity check).</summary>
    public FormulaVariable? Variable { get; init; }

    /// <summary>The unit of the solved variable ("" for none).</summary>
    public string Unit { get; init; } = "";

    /// <summary>The solutions, the most likely one first (solved answers).</summary>
    public IReadOnlyList<AnswerValue> Values { get; init; } = [];

    /// <summary>The value of the left side (identity checks).</summary>
    public AnswerValue? Left { get; init; }

    /// <summary>The value of the right side (identity checks).</summary>
    public AnswerValue? Right { get; init; }

    /// <summary>True when both sides agree (identity checks).</summary>
    public bool Holds { get; init; }

    /// <summary>The values that went in, in the formula's order.</summary>
    public IReadOnlyList<GivenValue> Given { get; init; } = [];

    /// <summary>Remarks from the solver, for example "Used g = 9.81 m/s² (the usual value)."</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>The solved symbol, or an empty string.</summary>
    public string Symbol => Variable?.Symbol ?? "";

    /// <summary>One line per solution ("v = 6 m/s"), or the result of a check.</summary>
    public IReadOnlyList<string> Lines => Kind == AnswerKind.Solved
        ? [.. Values.Select(v => SymbolText.Equation(Symbol, v.Number, Unit))]
        : [CheckLine];

    /// <summary>All lines joined: "v = 6 m/s or v = -6 m/s".</summary>
    public string Headline => string.Join(" or ", Lines);

    /// <summary>The exact forms where they say more than the decimals ("a = 5/2"), or null.</summary>
    public string? ExactLine
    {
        get
        {
            if (Kind != AnswerKind.Solved) return null;
            var exact = Values.Where(v => v.Exact is not null).Select(v => $"{SymbolText.Plain(Symbol)} = {v.Exact}").ToList();
            return exact.Count == 0 ? null : string.Join(" or ", exact);
        }
    }

    /// <summary>"v = 25 m/s, u = 5 m/s, t = 8 s" (usual values are marked).</summary>
    public string GivenLine => string.Join(", ", Given.Select(g => SymbolText.Equation(g.Symbol, g.Text, g.Unit) + (g.IsUsual ? " (usual)" : "")));

    /// <summary>The text COPY puts on the clipboard, for example "a = 2.5 m/s²".</summary>
    public string CopyText => Kind == AnswerKind.Solved
        ? Headline
        : $"{Formula.Name}: {CheckLine}" + (Given.Count > 0 ? $" for {GivenLine}" : "");

    /// <summary>The expressions USE can send to Advanced mode, one per value.</summary>
    public IReadOnlyList<CardUse> Uses => Kind == AnswerKind.Solved
        ? [.. Values.Select(v => new CardUse(SymbolText.Equation(Symbol, v.Number, Unit), v.Use))]
        : Left is { } l ? [new CardUse("left side = " + l.Number, l.Use)] : [];

    private string CheckLine
    {
        get
        {
            if (Left is null || Right is null) return "";
            return Holds
                ? $"both sides = {Left.Number}"
                : $"left side = {Left.Number}, right side = {Right.Number}";
        }
    }
}
