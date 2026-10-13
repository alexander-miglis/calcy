using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Calcy.Modes.Finance.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Finance.Views;

/// <summary>The cards panel (design decision D5): every answer kept as a card with COPY, USE and EXPORT CSV.</summary>
public sealed partial class FinanceView
{
    private readonly StackPanel _cardList = new() { Spacing = 12, Margin = new Thickness(8, 12, 10, 14) };
    private ScrollViewer _cardScroll = null!;
    private Control _cardsEmpty = null!;
    private PixelButton _clearCards = null!;

    /// <summary>The card controls on screen, newest first (for tests).</summary>
    internal IReadOnlyList<Card> CardControls => [.. _cardList.Children.OfType<Card>()];

    private PixelPanel BuildCardsPanel()
    {
        _cardScroll = new ScrollViewer
        {
            Content = _cardList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var title = Ui.Pixel("No cards yet", 2);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Ui.Text("Press SOLVE or ADD CARD and the answer lands here. COPY it, or USE it in Advanced.", "hint");
        text.TextAlignment = Avalonia.Media.TextAlignment.Center;
        _cardsEmpty = new StackPanel { Spacing = 10, Margin = new Thickness(18, 28), Children = { title, text } };

        _clearCards = Ui.Small("Clear", "Clear all cards", "Remove every card", "paper");
        _clearCards.Click += (_, _) =>
        {
            _state.Cards.Clear();
            RenderCards();
            SaveNow();
            _services.ShowStatus("Cards cleared");
        };
        var panel = new PixelPanel
        {
            Header = "Cards",
            HeaderContent = _clearCards,
            Margin = new Thickness(0, 0, 6, 6),
            Content = new Panel { Children = { _cardScroll, _cardsEmpty } },
        };
        panel.Token(PixelPanel.HeaderBrushProperty, "Calcy.Ok");
        AutomationProperties.SetName(panel, "Finance cards");
        return panel;
    }

    /// <summary>Adds a card at the top of the list and remembers it.</summary>
    private void AddCard(FinanceCard card)
    {
        _state.AddCard(card);
        RenderCards();
        _cardScroll.Offset = default;
        SaveNow();
        _services.ShowStatus($"Card #{card.Number}: {card.Result.Split('\n')[0]}");
    }

    private void RenderCards()
    {
        _cardList.Children.Clear();
        foreach (var c in _state.Cards) _cardList.Children.Add(CardControl(c));
        bool empty = _state.Cards.Count == 0;
        _cardsEmpty.IsVisible = empty;
        _cardScroll.IsVisible = !empty;
        _clearCards.IsEnabled = !empty;
    }

    private Control CardControl(FinanceCard c)
    {
        var body = new StackPanel { Spacing = 6 };
        if (c.Input.Length > 0) body.Children.Add(Ui.Text(c.Input, "cardinput"));
        body.Children.Add(Ui.Text(c.Result, "cardresult"));
        if (!string.IsNullOrEmpty(c.Note)) body.Children.Add(Ui.Text(c.Note, "meaning"));

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        var copy = Ui.Small("Copy", $"Copy card {c.Number}", "Copy this card as text");
        copy.Click += async (_, _) => await CopyAsync(c.CopyText(), $"Copied card #{c.Number}");
        actions.Children.Add(copy);
        if (c.Uses.Count > 0)
        {
            var use = Ui.Small("Use", $"Use card {c.Number} in Advanced", c.Uses.Count == 1
                ? "Send " + c.Uses[0].Expression + " to Advanced"
                : "Send one of the answers to Advanced as a calculation");
            if (c.Uses.Count == 1)
            {
                var only = c.Uses[0];
                use.Click += (_, _) => Use(only);
            }
            else
            {
                var menu = new MenuFlyout();
                foreach (var u in c.Uses)
                {
                    var item = new MenuItem { Header = u.Label.ToUpperInvariant() + "    " + Shorten(u.Expression) };
                    ToolTip.SetTip(item, u.Expression);
                    var chosen = u;
                    item.Click += (_, _) => Use(chosen);
                    menu.Items.Add(item);
                }
                use.Flyout = menu;
            }
            actions.Children.Add(use);
        }
        if (c.Schedule is not null)
        {
            var csv = Ui.Small("Export CSV", $"Export card {c.Number} as CSV", "Copy the schedule to the clipboard as CSV text, ready to paste into a spreadsheet", "paper");
            csv.Click += async (_, _) =>
            {
                if (c.Csv() is not { } text)
                {
                    _services.ShowStatus("This card has no schedule to export");
                    return;
                }
                await CopyAsync(text, $"Copied the schedule of card #{c.Number} as CSV: {Periods.Count(text.Count(ch => ch == '\n'), "line")}");
            };
            actions.Children.Add(csv);
        }
        body.Children.Add(actions);

        var card = new Card
        {
            Title = $"#{c.Number}  {c.Title}",
            Meta = c.Created.ToString("HH:mm", CultureInfo.InvariantCulture),
            Content = body,
            Tag = c,
        };
        AutomationProperties.SetName(card, $"Card {c.Number}, {c.Title}: {c.Result.Replace('\n', ' ')}");
        return card;
    }

    private static string Shorten(string s) => s.Length > 42 ? s[..40] + "…" : s;

    /// <summary>Sends an expression to Advanced mode (without evaluating it, so it can be checked first).</summary>
    private void Use(CardUse use)
    {
        _services.Navigate("advanced", new InputRequest(use.Expression, Evaluate: false));
        Dispatcher.UIThread.Post(() => _services.ShowStatus($"Sent {use.Label} to Advanced: press Enter there to calculate"));
    }
}
