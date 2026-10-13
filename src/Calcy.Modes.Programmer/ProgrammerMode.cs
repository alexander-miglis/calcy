using Avalonia.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Programmer;

/// <summary>
/// Programmer mode (MO-030 to MO-038, CE-070, CE-071): hex, decimal, octal and binary side by side, word sizes and
/// signedness, a clickable bit grid, bitwise operators and C-style expressions, an IEEE 754 inspector, fixed point,
/// and characters with their encodings.
/// </summary>
public sealed class ProgrammerMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "programmer";

    /// <inheritdoc />
    public string Title => "Programmer";

    /// <inheritdoc />
    public string Description => "Hex, binary, bits, word sizes";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+4";

    /// <inheritdoc />
    public string AccentToken => "Ok";

    /// <inheritdoc />
    public string Icon => "programmer";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new ProgrammerView(services);
}
