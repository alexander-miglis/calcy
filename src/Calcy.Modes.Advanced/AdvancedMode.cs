using Avalonia.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Advanced;

/// <summary>Advanced mode: scientific and algebra calculations, each result on its own card.</summary>
public sealed class AdvancedMode : ICalcyMode
{
    public string Id => "advanced";
    public string Title => "Advanced";
    public string Description => "Scientific and algebra with result cards";
    public string Shortcut => "Ctrl+2";
    public string AccentToken => "Sel";
    public string Icon => "advanced";

    public Control CreateView(AppServices services) => new AdvancedView(services);
}
