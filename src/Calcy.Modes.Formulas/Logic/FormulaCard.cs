using System.Text.Json.Serialization;
using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>
/// A kept answer (design decision D5): what was solved, with which values, the result lines, the clipboard text and
/// the expressions for USE. Stored in the preferences, so it only holds plain data.
/// </summary>
public sealed class FormulaCard
{
    /// <summary>Card number, counting up from 1.</summary>
    public int Number { get; set; }

    /// <summary>The formula id, so OPEN can bring the formula back.</summary>
    public string FormulaId { get; set; } = "";

    /// <summary>The formula name at the time.</summary>
    public string FormulaName { get; set; } = "";

    /// <summary>The category id ("mechanics").</summary>
    public string Category { get; set; } = "";

    /// <summary>True for an identity check, false for a solved unknown.</summary>
    public bool IsCheck { get; set; }

    /// <summary>The values that went in: "v = 25 m/s, u = 5 m/s, t = 8 s".</summary>
    public string Given { get; set; } = "";

    /// <summary>One line per solution: "a = 2.5 m/s²".</summary>
    public List<string> Results { get; set; } = [];

    /// <summary>The exact forms when they add something ("a = 5/2"), or null.</summary>
    public string? Exact { get; set; }

    /// <summary>Remarks such as "Used g = 9.81 m/s² (the usual value)."</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>The expressions USE can send to Advanced mode.</summary>
    public List<CardUse> Uses { get; set; } = [];

    /// <summary>What COPY puts on the clipboard.</summary>
    public string CopyText { get; set; } = "";

    /// <summary>The unknown that was solved for (null for checks).</summary>
    public string? SolveFor { get; set; }

    /// <summary>The box texts by symbol, so OPEN can restore them.</summary>
    public Dictionary<string, string> Values { get; set; } = [];

    /// <summary>When the card was made (local time).</summary>
    public DateTime Created { get; set; }

    /// <summary>The card header: "#3  MECHANICS".</summary>
    [JsonIgnore]
    public string Title => $"#{Number}  {FormulaBrowser.ShortCategory(Category)}";

    /// <summary>Makes a card from an answer and the box texts that produced it.</summary>
    public static FormulaCard From(FormulaAnswer answer, int number, IReadOnlyDictionary<string, string> values, DateTime created)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(values);
        Formula f = answer.Formula;
        return new FormulaCard
        {
            Number = number,
            FormulaId = f.Id,
            FormulaName = f.Name,
            Category = f.Category,
            IsCheck = answer.Kind == AnswerKind.Checked,
            Given = answer.GivenLine,
            Results = [.. answer.Lines],
            Exact = answer.ExactLine,
            Notes = [.. answer.Notes],
            Uses = [.. answer.Uses],
            CopyText = answer.CopyText,
            SolveFor = answer.Kind == AnswerKind.Solved ? answer.Symbol : null,
            Values = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal),
            Created = created,
        };
    }
}
