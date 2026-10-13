using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Calcy.UI.Controls;
using Calcy.UI.Services;

using Calcy.Units;

namespace Calcy.Modes.Convert;

internal sealed partial class ConvertView
{
    private const int MaxCards = 200;

    private StackPanel _cardList = null!;

    private PixelPanel BuildCardsPanel()
    {
        _cardList = new StackPanel { Spacing = 12, Name = "Cards" };
        var hint = new TextBlock
        {
            Classes = { "hint" },
            Text = "Enter or Save as card keeps a conversion here. The From box also takes whole requests: 5 km to mi, 3 ft 4 in to cm, 170 cm to ft in.",
        };
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(10, 10, 14, 14), Children = { _cardList, hint } };
        var panel = new PixelPanel
        {
            Header = "Cards",
            Margin = new Thickness(0, 0, 6, 6),
            VerticalAlignment = VerticalAlignment.Top,
            Content = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
        };
        panel.Bind(PixelPanel.HeaderBrushProperty, panel.GetResourceObservable("Calcy.Key"));
        return panel;
    }

    /// <summary>Adds a card at the top of the list and remembers it.</summary>
    private void AddCard(ConvertCard card)
    {
        _state.Cards.Insert(0, card);
        if (_state.Cards.Count > MaxCards) _state.Cards.RemoveRange(MaxCards, _state.Cards.Count - MaxCards);
        SaveState();
        RebuildCards();
        _services.ShowStatus($"Saved: {card.Text}");
    }

    /// <summary>The saved cards, newest first.</summary>
    internal IReadOnlyList<ConvertCard> Cards => _state.Cards;

    private void RebuildCards()
    {
        _cardList.Children.Clear();
        int n = _state.Cards.Count;
        foreach (var card in _state.Cards)
        {
            _cardList.Children.Add(CardView(card, n));
            n--;
        }
    }

    private Card CardView(ConvertCard card, int number)
    {
        var text = new TextBlock { Text = card.Text, Classes = { "math" }, FontSize = 15, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        if (card.Query is { } query)
        {
            var use = SmallButton("USE", $"Use card {number} again");
            use.Click += (_, _) =>
            {
                Receive(new InputRequest(query));
                FocusFirst();
            };
            actions.Children.Add(use);
        }
        var copy = SmallButton("COPY", $"Copy card {number}");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(card.Text);
                _services.ShowStatus("Copied to the clipboard");
            }
        };
        actions.Children.Add(copy);
        var removeIcon = new PixelIcon { Kind = "close", Width = 12, Height = 12 };
        removeIcon.Bind(PixelIcon.ForegroundProperty, removeIcon.GetResourceObservable("Calcy.Ink"));
        var remove = SmallButton(removeIcon, $"Remove card {number}");
        remove.Click += (_, _) =>
        {
            _state.Cards.Remove(card);
            SaveState();
            RebuildCards();
        };
        actions.Children.Add(remove);

        return new Card
        {
            Title = $"#{number}  {card.Category.ToUpperInvariant()}",
            Padding = new Thickness(10, 8),
            Content = new StackPanel { Spacing = 8, Children = { text, actions } },
        };
    }

    private static PixelButton SmallButton(object content, string name)
    {
        var b = new PixelButton { Content = content, Classes = { "small", "flat" } };
        AutomationProperties.SetName(b, name);
        ToolTip.SetTip(b, name);
        return b;
    }
}
