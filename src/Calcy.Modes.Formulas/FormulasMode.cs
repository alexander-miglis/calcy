using Avalonia.Controls;
using Calcy.Modes.Formulas.Views;
using Calcy.UI.Services;

namespace Calcy.Modes.Formulas;

/// <summary>
/// Formulas mode (design decision D14): the documented formula collection. Find a formula by name, topic or a typed
/// equation, read what it means, solve it for any of its variables and keep every answer as a card.
/// </summary>
public sealed class FormulasMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "formulas";

    /// <inheritdoc />
    public string Title => "Formulas";

    /// <inheritdoc />
    public string Description => "Documented formula collection";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+0";

    /// <inheritdoc />
    public string AccentToken => "G3";

    /// <inheritdoc />
    public string Icon => "formula";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new FormulasView(services);
}
