using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Calcy.Modes.Formulas.Views;

/// <summary>Styles for the Formulas screen: list rows, tags, the variables table, the answer box and cards. Every colour is a scheme token.</summary>
public sealed class FormulasStyles : Styles
{
    /// <summary>Loads the style sheet.</summary>
    public FormulasStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
