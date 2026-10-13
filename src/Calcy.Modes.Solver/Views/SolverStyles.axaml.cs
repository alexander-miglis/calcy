using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Calcy.Modes.Solver.Views;

/// <summary>Styles for the Solver screen: boxes, inputs and text. Every colour is a scheme token.</summary>
public sealed class SolverStyles : Styles
{
    /// <summary>Loads the style sheet.</summary>
    public SolverStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
