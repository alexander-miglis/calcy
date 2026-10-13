using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Calcy.UI.Theming;

/// <summary>The Calcy 8-bit style sheet (control themes for panels, cards, buttons and toggles; tweaks to standard controls).</summary>
public class CalcyTheme : Styles
{
    public CalcyTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
