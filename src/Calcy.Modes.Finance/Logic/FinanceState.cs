using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>The tabs of the Finance screen.</summary>
public enum FinanceTab
{
    /// <summary>Time value of money solver.</summary>
    Tvm,

    /// <summary>Amortization schedule.</summary>
    Amortization,

    /// <summary>Cash flow worksheet.</summary>
    CashFlow,

    /// <summary>Rate conversion, simple and compound interest.</summary>
    Interest,

    /// <summary>Day counts and date arithmetic.</summary>
    Dates,

    /// <summary>Depreciation schedules.</summary>
    Depreciation,

    /// <summary>Bond price and yield.</summary>
    Bonds,
}

/// <summary>Everything the Finance screen remembers between sessions (stored with Preferences.SetModeState("finance", ...)).</summary>
public sealed class FinanceState
{
    /// <summary>The most cards kept.</summary>
    public const int MaxCards = 100;

    /// <summary>Format version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The tab on screen.</summary>
    public FinanceTab Tab { get; set; } = FinanceTab.Tvm;

    /// <summary>TVM fields.</summary>
    public TvmInputs Tvm { get; set; } = TvmInputs.Default();

    /// <summary>The TVM variable solved last (kept up to date when the other fields change), if any.</summary>
    public TvmVariable? Solved { get; set; }

    /// <summary>First payment of the amortization range.</summary>
    public string RangeFrom { get; set; } = "1";

    /// <summary>Last payment of the amortization range.</summary>
    public string RangeTo { get; set; } = "12";

    /// <summary>Cash flow worksheet.</summary>
    public CashFlowInputs CashFlow { get; set; } = CashFlowInputs.Default();

    /// <summary>Interest tab.</summary>
    public InterestInputs Interest { get; set; } = new();

    /// <summary>Dates tab.</summary>
    public DateInputs Dates { get; set; } = new();

    /// <summary>Depreciation tab.</summary>
    public DepreciationInputs Depreciation { get; set; } = new();

    /// <summary>Bonds tab.</summary>
    public BondInputs Bonds { get; set; } = new();

    /// <summary>The bond value solved last.</summary>
    public BondTarget? BondSolved { get; set; }

    /// <summary>Result cards, newest first.</summary>
    public List<FinanceCard> Cards { get; set; } = [];

    /// <summary>Number for the next card.</summary>
    public int NextCardNumber { get; set; } = 1;

    /// <summary>Adds a card at the top, numbering it, and drops the oldest beyond <see cref="MaxCards"/>.</summary>
    public FinanceCard AddCard(FinanceCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.Number = NextCardNumber++;
        Cards.Insert(0, card);
        if (Cards.Count > MaxCards) Cards.RemoveRange(MaxCards, Cards.Count - MaxCards);
        return card;
    }

    /// <summary>Repairs a state read from disk (missing parts become defaults).</summary>
    public FinanceState Normalize()
    {
        Tvm ??= TvmInputs.Default();
        Tvm.N ??= "";
        Tvm.InterestRate ??= "";
        Tvm.PresentValue ??= "";
        Tvm.Payment ??= "";
        Tvm.FutureValue ??= "";
        Tvm.PaymentsPerYear ??= "12";
        Tvm.CompoundingsPerYear ??= "";
        CashFlow ??= CashFlowInputs.Default();
        CashFlow.Flows ??= [];
        CashFlow.Flows.RemoveAll(f => f is null);
        Interest ??= new InterestInputs();
        Dates ??= new DateInputs();
        Depreciation ??= new DepreciationInputs();
        Bonds ??= new BondInputs();
        if (Bonds.Frequency is not (1 or 2 or 4 or 12)) Bonds.Frequency = 2;
        RangeFrom ??= "1";
        RangeTo ??= "12";
        Cards ??= [];
        Cards.RemoveAll(c => c is null);
        foreach (var c in Cards)
        {
            c.Title ??= "";
            c.Input ??= "";
            c.Result ??= "";
            c.Uses ??= [];
        }
        if (Cards.Count > MaxCards) Cards.RemoveRange(MaxCards, Cards.Count - MaxCards);
        int max = Cards.Count == 0 ? 0 : Cards.Max(c => c.Number);
        if (NextCardNumber <= max) NextCardNumber = max + 1;
        if (!Enum.IsDefined(Tab)) Tab = FinanceTab.Tvm;
        return this;
    }
}
