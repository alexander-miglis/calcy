using Avalonia.Controls;
using Calcy.Modes.Finance.Views;
using Calcy.UI.Services;

namespace Calcy.Modes.Finance;

/// <summary>
/// Finance mode (section 6.9, CE-090 to CE-094): the TVM solver with presets, amortization schedules, cash flows
/// (NPV, IRR, MIRR, payback), nominal and effective rates, simple and compound interest, day counts, depreciation and
/// bonds. Every answer becomes a card that can be copied, used in Advanced or exported as CSV.
/// </summary>
public sealed class FinanceMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "finance";

    /// <inheritdoc />
    public string Title => "Finance";

    /// <inheritdoc />
    public string Description => "Loans, savings, cash flows";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+8";

    /// <inheritdoc />
    public string AccentToken => "G5";

    /// <inheritdoc />
    public string Icon => "finance";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new FinanceView(services);
}
