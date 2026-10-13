using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Calcy.Modes.Matrix.Views;

/// <summary>Styles for the Matrix screen: library rows and editor cells. Every colour is a scheme token.</summary>
public sealed class MatrixStyles : Styles
{
    /// <summary>Loads the style sheet.</summary>
    public MatrixStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
