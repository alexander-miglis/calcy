using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Programmer;

/// <summary>The CARDS panel: one card per evaluated operation, newest first (design decision D5).</summary>
internal sealed class CardsPanel : UserControl
{
    private readonly ProgrammerCalculator _calc;
    private readonly StackPanel _list = new() { Spacing = 12, Margin = new Thickness(12, 12, 16, 16) };
    private readonly TextBlock _empty;
    private readonly PixelButton _clear;
    private int _shownCount = -1;
    private int _shownLast = -1;

    /// <summary>A line-break opportunity that takes no space (U+200B).</summary>
    private static readonly string ZeroWidthSpace = char.ConvertFromUtf32(0x200B);

    /// <summary>Raised when a card's USE button is pressed, with the card number.</summary>
    public event Action<int>? Use;

    /// <summary>Creates the panel for the calculator's cards.</summary>
    public CardsPanel(ProgrammerCalculator calc)
    {
        _calc = calc;
        _empty = Ui.Text("Every calculation becomes a card here. Type an expression or use the keypad, then press Enter or =.", 14, "Body", "Muted");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.Margin = new Thickness(4);
        _clear = Ui.SmallButton("CLEAR", "Remove all cards");
        _clear.Click += (_, _) => _calc.ClearCards();
        var panel = new PixelPanel
        {
            Header = "Cards",
            HeaderContent = _clear,
            Content = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
        panel.Token(PixelPanel.HeaderBrushProperty, "Ok");
        Content = panel;
        _calc.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Rebuilds the list when cards were added or removed.</summary>
    public void Refresh()
    {
        var cards = _calc.Cards;
        int last = cards.Count == 0 ? 0 : cards[^1].Number;
        if (cards.Count == _shownCount && last == _shownLast) return;
        _shownCount = cards.Count;
        _shownLast = last;
        _list.Children.Clear();
        _clear.IsVisible = cards.Count > 0;
        if (cards.Count == 0)
        {
            _list.Children.Add(_empty);
            return;
        }
        for (int i = cards.Count - 1; i >= 0; i--) _list.Children.Add(Build(cards[i]));
    }

    private Control Build(ProgrammerCard c)
    {
        var expr = Ui.Text(c.Expression, 14, "Math", "Muted");
        expr.TextWrapping = TextWrapping.Wrap;
        expr.HorizontalAlignment = HorizontalAlignment.Left;
        var body = new StackPanel { Spacing = 3, Children = { expr } };
        if (c.IsError)
        {
            var err = Ui.Text(c.Error!, 14, "Body", "Ink");
            err.TextWrapping = TextWrapping.Wrap;
            err.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(err);
        }
        else
        {
            // Long results get a smaller size and may break after the _ digit separators.
            var shown = "= " + c.Result;
            double size = shown.Length <= 16 ? 20 : shown.Length <= 24 ? 16 : 14;
            var result = Ui.Text(shown.Replace("_", "_" + ZeroWidthSpace, StringComparison.Ordinal), size, "Math", "Ink", FontWeight.Bold);
            result.TextWrapping = TextWrapping.Wrap;
            result.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(result);
            var extra = new List<string> { c.Secondary };
            if (c.AssignedName is { } name) extra.Add($"stored in {name}");
            extra.AddRange(c.Notes);
            var second = Ui.Text(string.Join(", ", extra), 13, "Math", "Muted");
            second.TextWrapping = TextWrapping.Wrap;
            second.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(second);
        }

        var card = new Card
        {
            Title = $"#{c.Number}  {(c.IsError ? "error" : c.Context)}",
            Meta = c.IsError ? null : BaseFormatter.Name(c.Base),
            IsError = c.IsError,
            Content = body,
            Padding = new Thickness(12, 8),
        };
        if (!c.IsError)
        {
            var use = Ui.SmallButton("USE", $"Put #{c.Number} into the expression");
            int n = c.Number;
            use.Click += (_, _) => Use?.Invoke(n);
            card.Actions = use;
        }
        return card;
    }
}
