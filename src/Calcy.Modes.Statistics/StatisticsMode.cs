using Avalonia.Controls;
using Calcy.Modes.Statistics.Views;
using Calcy.UI.Services;

namespace Calcy.Modes.Statistics;

/// <summary>
/// Statistics mode: a list editor whose lists are session variables, one- and two-variable statistics, every TI
/// regression model, hypothesis tests and confidence intervals, a distribution explorer, statistical plots and result
/// cards.
/// </summary>
public sealed class StatisticsMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "statistics";

    /// <inheritdoc />
    public string Title => "Statistics";

    /// <inheritdoc />
    public string Description => "Data, regressions, tests, distributions";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+5";

    /// <inheritdoc />
    public string AccentToken => "Mem";

    /// <inheritdoc />
    public string Icon => "stats";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new StatisticsView(services);
}
