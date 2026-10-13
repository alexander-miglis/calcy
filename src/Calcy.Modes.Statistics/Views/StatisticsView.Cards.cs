using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Calcy.Core;
using Calcy.Modes.Statistics.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Statistics.Views;

public sealed partial class StatisticsView
{
    private readonly StackPanel _cardList = new() { Spacing = 10, Margin = new Thickness(10, 10, 12, 12) };
    private ScrollViewer _cardScroll = null!;
    private Card? _welcome;
    private PixelButton _clearCards = null!;

    private PixelPanel BuildCardsPanel()
    {
        _cardScroll = new ScrollViewer
        {
            Content = _cardList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _clearCards = Ui.Small("CLEAR", "Remove every card", ClearCards, "paper");
        var panel = new PixelPanel { Header = "Cards", HeaderContent = _clearCards, Content = _cardScroll };
        panel.Token(PixelPanel.HeaderBrushProperty, "Calcy.Mem");
        AutomationProperties.SetName(panel, "Statistics cards");
        return panel;
    }

    /// <summary>Keeps the current result of the open tab as a card (waits for a running analysis first).</summary>
    public async Task AddCardAsync()
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (_pending > 0 && DateTime.UtcNow < until) await Task.Delay(10);
        var card = Outcome(_state.Tab) switch
        {
            OneVarOutcome o => o.Card,
            TwoVarOutcome o => o.Card,
            RegressionOutcome o => o.Card,
            TestOutcome o => o.Card,
            DistributionOutcome o => o.Card,
            PlotOutcome o => o.Card,
            _ => null,
        };
        if (card is null)
        {
            _s.ShowStatus("Nothing to keep yet");
            return;
        }
        AddCard(card.Copy());
    }

    /// <summary>Adds a card at the top of the list.</summary>
    private void AddCard(StatCard card)
    {
        card.Number = _state.NextCardNumber++;
        card.Time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        _state.Cards.Insert(0, card);
        _cardList.Children.Insert(_welcome is null ? 0 : 0, new StatCardView(card, this));
        while (_state.Cards.Count > StatsModeState.MaxCards)
        {
            var last = _state.Cards[^1];
            _state.Cards.RemoveAt(_state.Cards.Count - 1);
            var view = _cardList.Children.OfType<StatCardView>().FirstOrDefault(v => v.Model == last);
            if (view is not null) _cardList.Children.Remove(view);
        }
        UpdateWelcome();
        _cardScroll.Offset = default;
        ScheduleSave();
        _s.ShowStatus($"Card #{card.Number}: {card.Title}");
    }

    private void ClearCards()
    {
        _state.Cards.Clear();
        _cardList.Children.Clear();
        _welcome = null;
        UpdateWelcome();
        ScheduleSave();
        _s.ShowStatus("Cards cleared");
    }

    private void UpdateWelcome()
    {
        bool empty = _state.Cards.Count == 0;
        _clearCards.IsEnabled = !empty;
        if (!empty)
        {
            if (_welcome is not null) _cardList.Children.Remove(_welcome);
            _welcome = null;
            return;
        }
        if (_welcome is not null) return;
        var text = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Ui.Body("Press ADD CARD on any tab and the result lands here, newest on top.", 13),
                Ui.Body("COPY gives the text, COPY LATEX the LaTeX, USE sends the calculator expression (linreg(L1, L2), ttest(65, L2)) to Advanced and SHOW PLOT reopens the analysis.", 12, muted: true),
            },
        };
        _welcome = new Card { Title = "WELCOME", Content = text, Padding = new Thickness(10, 8) };
        _cardList.Children.Insert(0, _welcome);
    }

    void IStatCardHost.Copy(string text, string what)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) _ = clipboard.SetTextAsync(text);
        var shown = text.Replace('\n', ' ');
        _s.ShowStatus($"Copied {what}: {(shown.Length > 60 ? shown[..58] + "…" : shown)}");
    }

    void IStatCardHost.Use(string text)
    {
        _s.Navigate("advanced", new InputRequest(text, Evaluate: false));
        _ui.Post(() => _s.ShowStatus($"Sent {text} to Advanced: press Enter there to calculate"));
    }

    void IStatCardHost.ShowPlot(StatRecipe recipe) => ShowRecipe(recipe);

    CalcSession IStatCardHost.Session => _session;

    /// <summary>Reopens the analysis a card came from.</summary>
    public void ShowRecipe(StatRecipe recipe)
    {
        switch (recipe.Tab)
        {
            case StatTab.OneVar when recipe.OneVar is { } s: _state.OneVar = s.Clone(); break;
            case StatTab.TwoVar when recipe.TwoVar is { } s: _state.TwoVar = s.Clone(); break;
            case StatTab.Regression when recipe.Regression is { } s: _state.Regression = s.Clone(); break;
            case StatTab.Tests when recipe.Test is { } s: _state.Test = s.Clone(); break;
            case StatTab.Distribution when recipe.Distribution is { } s: _state.Distribution = s.Clone(); break;
            case StatTab.Plots when recipe.Plot is { } s: _state.Plot = s.Clone(); break;
        }
        RebuildTabs();
        ShowTab(recipe.Tab);
    }

    /// <summary>Rebuilds the tab pages from the settings (after SHOW PLOT replaced them).</summary>
    private void RebuildTabs()
    {
        _pickerRefreshers.Clear();
        _errors.Clear();
        // The graphs, charts and tiles are kept; take them out of the old pages first.
        Control[] kept = [_testGraph, _distGraph, _plotGraph, _oneChart, _distChart, _plotChart];
        foreach (var c in kept)
            if (c.Parent is Border b) b.Child = null;
        _tabHost.Content = null;
        foreach (var page in _tabContent.Values)
            if (page is Panel p) p.Children.Clear();
        _tabContent[StatTab.OneVar] = BuildOneVarTab();
        _tabContent[StatTab.TwoVar] = BuildTwoVarTab();
        _tabContent[StatTab.Regression] = BuildRegressionTab();
        _tabContent[StatTab.Tests] = BuildTestsTab();
        _tabContent[StatTab.Distribution] = BuildDistributionTab();
        _tabContent[StatTab.Plots] = BuildPlotsTab();
        _predicted = null;
    }
}
