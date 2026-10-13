using Avalonia.Controls;
using Calcy.UI.Services;

using Calcy.Units;

namespace Calcy.Modes.Convert;

/// <summary>Convert mode: units (exact where the definitions are exact) and date tools, with saved conversion cards.</summary>
public sealed class ConvertMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "convert";

    /// <inheritdoc />
    public string Title => "Convert";

    /// <inheritdoc />
    public string Description => "Units and dates";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+9";

    /// <inheritdoc />
    public string AccentToken => "Key";

    /// <inheritdoc />
    public string Icon => "convert";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new ConvertView(services);
}
