using System.Text;
using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>A Calcy expression a card can send to Advanced mode, with the name shown when there is a choice.</summary>
/// <param name="Label">Short name, for example "NPV" or "ΣInt".</param>
/// <param name="Expression">The expression, for example tvm_pmt(360, 8, 100000, 0, 12, 12).</param>
public sealed record CardUse(string Label, string Expression);

/// <summary>The kind of schedule a card can export as CSV.</summary>
public enum ScheduleKind
{
    /// <summary>A loan or savings amortization schedule.</summary>
    Amortization,

    /// <summary>A depreciation schedule.</summary>
    Depreciation,

    /// <summary>A cash flow table.</summary>
    CashFlow,
}

/// <summary>The numbers a schedule is built from, kept on the card so the CSV can be rebuilt at any time.</summary>
public sealed class ScheduleSource
{
    /// <summary>Which schedule.</summary>
    public ScheduleKind Kind { get; set; }

    /// <summary>Numbers in a fixed order per kind (see <see cref="FinanceCards"/>).</summary>
    public List<double> Numbers { get; set; } = [];

    /// <summary>Second list of numbers (cash flow amounts and counts are interleaved here).</summary>
    public List<double> Flows { get; set; } = [];
}

/// <summary>One result card (design decision D5): a title, the inputs, the answer and what can be done with it.</summary>
public sealed class FinanceCard
{
    /// <summary>Card number shown as "#12".</summary>
    public int Number { get; set; }

    /// <summary>Title, for example "TVM: PMT".</summary>
    public string Title { get; set; } = "";

    /// <summary>The inputs, one or more lines.</summary>
    public string Input { get; set; } = "";

    /// <summary>The answer, one or more lines.</summary>
    public string Result { get; set; } = "";

    /// <summary>A short explanation in plain words.</summary>
    public string? Note { get; set; }

    /// <summary>Expressions for the USE button (none, one, or a choice).</summary>
    public List<CardUse> Uses { get; set; } = [];

    /// <summary>The schedule behind the card, for EXPORT CSV.</summary>
    public ScheduleSource? Schedule { get; set; }

    /// <summary>When the card was made (local time).</summary>
    public DateTime Created { get; set; }

    /// <summary>The card as plain text (what COPY puts on the clipboard).</summary>
    public string CopyText()
    {
        var sb = new StringBuilder();
        sb.Append(Title).Append('\n');
        if (Input.Length > 0) sb.Append(Input).Append('\n');
        sb.Append(Result);
        if (!string.IsNullOrEmpty(Note)) sb.Append('\n').Append(Note);
        return sb.ToString();
    }

    /// <summary>The schedule as CSV, or null when the card has no schedule.</summary>
    public string? Csv() => Schedule is null ? null : FinanceCards.Csv(Schedule);
}
