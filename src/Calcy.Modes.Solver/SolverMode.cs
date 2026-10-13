using Avalonia.Controls;
using Calcy.Modes.Solver.Views;
using Calcy.UI.Services;

namespace Calcy.Modes.Solver;

/// <summary>Solver mode: polynomials, linear and nonlinear systems, any equation and inequalities, each solve on its own card.</summary>
public sealed class SolverMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "solver";

    /// <inheritdoc />
    public string Title => "Solver";

    /// <inheritdoc />
    public string Description => "Polynomials, systems, any equation";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+7";

    /// <inheritdoc />
    public string AccentToken => "Primary";

    /// <inheritdoc />
    public string Icon => "solver";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new SolverView(services);
}
