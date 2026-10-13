using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Calcy.UI.Controls;

/// <summary>
/// The chunky 8-bit button: thick border, hard shadow, presses down on click.
/// Classes pick the colour: primary, sel, head, mem, ok, paper, card, err; size: small, key; flat removes the shadow.
/// </summary>
public class PixelButton : Button;

/// <summary>A toggle (segmented control item, tab, chip) in the 8-bit style. Checked items use the selection colour.</summary>
public class PixelToggle : ToggleButton;
