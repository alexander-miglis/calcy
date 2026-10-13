using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Calcy.Modes.Formulas.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Formulas.Views;

/// <summary>The cards column (design decision D5): every kept answer as a card with COPY, USE and OPEN.</summary>
public sealed partial class FormulasView
{
    private readonly StackPanel _cardList = new() { Spacing = 12, Margin = new Thickness(8, 12, 10, 14), Name = "Cards" };
    private ScrollViewer _cardScroll = null!;
    private Control _cardsEmpty = null!;
    private PixelButton _clearCards = null!;
    private bool _compactCards;

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
        var title = Ui.Pixel("No answers yet", 2);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Ui.Text("Fill in the values and press SOLVE (or Enter), or TRY EXAMPLE: every answer lands here as a card. COPY it, or USE it in Advanced.", "hint");
        text.TextAlignment = Avalonia.Media.TextAlignment.Center;
        _cardsEmpty = new StackPanel { Spacing = 10, Margin = new Thickness(16, 26), Children = { title, text } };

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
        panel.Token(PixelPanel.HeaderBrushProperty, "Calcy.Key");
        AutomationProperties.SetName(panel, "Formula cards");
        return panel;
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

    private Card CardControl(FormulaCard c)
    {
        var body = new StackPanel { Spacing = 5 };
        body.Children.Add(Ui.Text(c.FormulaName, "cardname"));
        if (c.Given.Length > 0) body.Children.Add(Ui.Text(c.Given, "cardinput"));
        foreach (var line in c.Results) body.Children.Add(Ui.Text(line, "cardresult"));
        if (c.Exact is { Length: > 0 } exact) body.Children.Add(Ui.Text("exact: " + exact, "cardinput"));
        // The solver's notes stay on the answer box; the given line already marks usual values.

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 5, LineSpacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        var copy = Ui.Small("Copy", $"Copy card {c.Number}", "Copy the answer as text: " + c.CopyText);
        copy.Click += async (_, _) => await CopyAsync(c.CopyText, $"Copied: {c.CopyText}");
        actions.Children.Add(copy);
        if (c.Uses.Count > 0)
        {
            var use = Ui.Small("Use", $"Use card {c.Number} in Advanced", c.Uses.Count == 1
                ? $"Send {c.Uses[0].Expression} to Advanced"
                : "Send one of the answers to Advanced");
            if (c.Uses.Count == 1)
            {
                var only = c.Uses[0];
                use.Click += (_, _) => SendToAdvanced(only.Expression, only.Label);
            }
            else
            {
                var menu = new MenuFlyout();
                foreach (var u in c.Uses)
                {
                    var item = new MenuItem { Header = u.Label };
                    ToolTip.SetTip(item, u.Expression);
                    var chosen = u;
                    item.Click += (_, _) => SendToAdvanced(chosen.Expression, chosen.Label);
                    menu.Items.Add(item);
                }
                use.Flyout = menu;
            }
            actions.Children.Add(use);
        }
        var open = Ui.Small("Open", $"Open card {c.Number}", "Open the formula with these values again");
        open.Click += (_, _) => OpenCard(c);
        actions.Children.Add(open);
        var removeIcon = new PixelIcon { Kind = "close", Width = 12, Height = 12 };
        removeIcon.Token(PixelIcon.ForegroundProperty, "Calcy.Ink");
        var remove = new PixelButton { Content = removeIcon, MinHeight = 28, MinWidth = 28, Padding = new Thickness(5, 3), ClipToBounds = false };
        remove.Classes.Add("small");
        remove.Classes.Add("flat");
        AutomationProperties.SetName(remove, $"Remove card {c.Number}");
        ToolTip.SetTip(remove, "Remove this card");
        remove.Click += (_, _) =>
        {
            _state.Cards.Remove(c);
            RenderCards();
            SaveNow();
        };
        actions.Children.Add(remove);
        body.Children.Add(actions);

        var card = new Card
        {
            Title = c.Title,
            Meta = _compactCards ? null : c.Created.ToString("HH:mm", CultureInfo.InvariantCulture),
            Padding = new Thickness(10, 8),
            Content = body,
            Tag = c,
        };
        AutomationProperties.SetName(card, $"Card {c.Number}, {c.FormulaName}: {string.Join(" or ", c.Results)}");
        return card;
    }

    /// <summary>Opens a card's formula with the values and unknown it was made from.</summary>
    internal void OpenCard(FormulaCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (_library.Get(card.FormulaId) is not { } f)
        {
            _services.ShowStatus("That formula is no longer in the collection");
            return;
        }
        var saved = _state.InputsFor(f.Id);
        saved.Values = new Dictionary<string, string>(card.Values, StringComparer.Ordinal);
        if (card.SolveFor is not null && f.GetVariable(card.SolveFor) is not null) saved.SolveFor = card.SolveFor;
        ShowTab(FormulasTab.Formulas);
        ClearSearch();
        OpenFormula(f);
    }

    /// <summary>A constant's USE: into the value box that had the focus last, or to Advanced mode when there is none.</summary>
    internal void UseConstant(Calcy.Formulas.FormulaConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        var text = ConstantText.Use(constant);
        var box = _lastInput;
        if (box is not null && box.IsVisible && _rows.Any(r => ReferenceEquals(r.Input, box)))
        {
            var current = box.Text ?? "";
            int start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
            int end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, current.Length);
            if (start == end) start = end = Math.Clamp(box.CaretIndex, 0, current.Length);
            box.Text = current[..start] + text + current[end..];
            box.CaretIndex = start + text.Length;
            box.Focus();
            var row = _rows.First(r => ReferenceEquals(r.Input, box));
            _services.ShowStatus($"Put {SymbolText.Plain(constant.Symbol)} = {ConstantText.Value(constant)} into {SymbolText.Plain(row.Variable.Symbol)}");
            return;
        }
        SendToAdvanced(text, $"{SymbolText.Plain(constant.Symbol)} ({constant.Name})");
    }
}
