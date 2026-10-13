using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Calcy.Units;

namespace Calcy.Modes.Convert;

/// <summary>Styles for the Convert screen: category rows, unit grid cells, result boxes and tiles. Every colour is a scheme token.</summary>
public sealed class ConvertStyles : Styles
{
    /// <summary>Loads the style sheet.</summary>
    public ConvertStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
