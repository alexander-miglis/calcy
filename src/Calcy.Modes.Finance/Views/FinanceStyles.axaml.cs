using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Calcy.Modes.Finance.Views;

/// <summary>Styles for the Finance screen: result boxes, tiles, inputs and text. Every colour is a scheme token.</summary>
public sealed class FinanceStyles : Styles
{
    /// <summary>Loads the style sheet.</summary>
    public FinanceStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
