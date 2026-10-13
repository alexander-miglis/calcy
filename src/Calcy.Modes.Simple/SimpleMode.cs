using Avalonia.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Simple;

/// <summary>Simple mode: everyday sums with a desk-calculator keypad, memory keys and an editable tape (MO-020 to MO-022).</summary>
public sealed class SimpleMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "simple";

    /// <inheritdoc />
    public string Title => "Simple";

    /// <inheritdoc />
    public string Description => "Everyday sums, memory keys, tape";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+1";

    /// <inheritdoc />
    public string AccentToken => "Lcd";

    /// <inheritdoc />
    public string Icon => "simple";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new SimpleView(services);
}
