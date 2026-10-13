using Avalonia.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Graphing;

/// <summary>Graphing mode: an expression list with sliders, an interactive graph with table and split views, and analysis cards.</summary>
public sealed class GraphingMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "graphing";

    /// <inheritdoc />
    public string Title => "Graphing";

    /// <inheritdoc />
    public string Description => "Plot, trace, roots, areas, sliders";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+3";

    /// <inheritdoc />
    public string AccentToken => "G2";

    /// <inheritdoc />
    public string Icon => "graph";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new GraphingView(services);
}
