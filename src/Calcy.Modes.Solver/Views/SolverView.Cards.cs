using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Solver.Views;

public sealed partial class SolverView
{
    private readonly StackPanel _cardList = new() { Spacing = 12, Margin = new Thickness(10, 10, 10, 12) };
    private ScrollViewer _cardScroll = null!;

    /// <summary>The card views, newest first (for tests).</summary>
    public IReadOnlyList<Card> CardViews => [.. _cardList.Children.OfType<Card>().Where(c => c.Tag is SolverCard)];

    private PixelPanel BuildCardsPanel()
    {
        _cardScroll = new ScrollViewer { Content = _cardList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var clear = Ui.Small("CLEAR", "Clear all cards", "Remove every card", "paper");
        clear.Click += (_, _) => ClearCards();
        var panel = new PixelPanel
        {
            Header = "Cards",
            HeaderContent = clear,
            Content = _cardScroll,
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.Token(PixelPanel.HeaderBrushProperty, "Calcy.Primary");
        AutomationProperties.SetName(panel, "Solver cards");
        return panel;
    }

    /// <summary>Removes every card.</summary>
    public void ClearCards()
    {
        _state.Cards.Clear();
        RenderCards();
        SaveState(toDisk: true);
        _s.ShowStatus("Cards cleared");
    }

    private void AddCard(SolverCard card)
    {
        card.Number = ++_state.Counter;
        card.Time = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        _state.Cards.Add(card);
        if (_state.Cards.Count > SolverState.MaxCards) _state.Cards.RemoveRange(0, _state.Cards.Count - SolverState.MaxCards);
        RenderCards();
        _cardScroll.Offset = default;
        SaveState(toDisk: true);
    }

    private void RenderCards()
    {
        _cardList.Children.Clear();
        if (_state.Cards.Count == 0)
        {
            _cardList.Children.Add(WelcomeCard());
            return;
        }
        // Newest first, as on the design board.
        for (int i = _state.Cards.Count - 1; i >= 0; i--) _cardList.Children.Add(CardView(_state.Cards[i]));
    }

    private static Card WelcomeCard()
    {
        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Ui.Text("Every solve becomes a card here, newest on top.", "words"),
                Ui.Text("COPY or COPY LATEX takes the answer, USE sends it to Advanced, PLOT draws it in Graphing.", "cardnote"),
            },
        };
        return new Card { Title = "CARDS", Content = body, Padding = new Thickness(8, 8) };
    }

    private Card WorkingCard(SolverTool tool)
    {
        var body = new StackPanel { Spacing = 4, Children = { Ui.Text($"Solving the {SolverTools.Name(tool)}…", "cardline"), Ui.Text("Press Esc to stop.", "cardnote") } };
        var card = new Card { Title = "WORKING", Meta = "ESC TO STOP", Content = body, Padding = new Thickness(8, 8) };
        card.Token(Card.HeaderBrushProperty, "Calcy.Mem");
        return card;
    }

    private Card CardView(SolverCard model)
    {
        var card = new Card
        {
            Title = $"#{model.Number}  {model.Title}",
            Meta = model.Time,
            IsError = model.IsError,
            Padding = new Thickness(8, 8),
            Tag = model,
        };
        AutomationProperties.SetName(card, $"Card {model.Number}: {model.Title.ToLowerInvariant()}");
        var body = new StackPanel { Spacing = 5 };
        if (model.Input.Length > 0) body.Children.Add(Ui.Text(model.Input, "cardinput"));
        foreach (var line in model.Lines) body.Children.Add(Ui.Text(line, model.IsError ? "carderror" : "cardline"));
        if (!string.IsNullOrEmpty(model.Approx)) body.Children.Add(Ui.Text(model.Approx, "cardapprox"));
        if (!string.IsNullOrEmpty(model.Note)) body.Children.Add(Ui.Text(model.Note, "cardnote"));

        var actions = Ui.Row(6);
        actions.Margin = new Thickness(0, 4, 0, 0);
        if (model.IsError)
        {
            if (model.Hint is { } hint)
            {
                var fix = Ui.Small("FIX IT", $"Fix card {model.Number}", "Use " + hint, "sel");
                fix.Click += (_, _) => FixIt(model, hint);
                actions.Children.Add(fix);
            }
        }
        else
        {
            if (model.CopyText is { } copy)
            {
                var b = Ui.Small("COPY", $"Copy card {model.Number} as text", "Copy the answer as text");
                b.Click += async (_, _) => await Copy(copy, "answer");
                actions.Children.Add(b);
            }
            if (model.LatexText is { } latex)
            {
                var b = Ui.Small("COPY LATEX", $"Copy card {model.Number} as LaTeX", "Copy the answer as LaTeX");
                b.Click += async (_, _) => await Copy(latex, "LaTeX");
                actions.Children.Add(b);
            }
            if (model.UseText is { } use)
            {
                var b = Ui.Small("USE", $"Use card {model.Number} in Advanced", "Put the answer into Advanced mode");
                b.Click += (_, _) => _s.Navigate("advanced", new InputRequest(use, Evaluate: false));
                actions.Children.Add(b);
            }
            if (model.PlotTexts.Count > 0)
            {
                var b = Ui.Small("PLOT", $"Plot card {model.Number} in Graphing", "Draw it in Graphing: " + string.Join(";  ", model.PlotTexts), "head");
                b.Click += (_, _) => _s.Navigate("graphing", new PlotRequest([.. model.PlotTexts]));
                actions.Children.Add(b);
            }
        }
        if (model.Request is not null)
        {
            var edit = Ui.Small("EDIT", $"Edit card {model.Number}", "Put these inputs back into the " + SolverTools.Name(model.Tool) + " tool", "paper");
            edit.Click += (_, _) => Restore(model);
            actions.Children.Add(edit);
        }
        if (actions.Children.Count > 0) body.Children.Add(actions);
        card.Content = body;
        return card;
    }

    private async Task Copy(string text, string what)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is not null) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clip, text);
        var shown = text.Replace(Environment.NewLine, "; ");
        _s.ShowStatus($"Copied {what}: {(shown.Length > 60 ? shown[..57] + "..." : shown)}");
    }

    /// <summary>Puts a card's inputs back into its tool and opens that tab.</summary>
    public void Restore(SolverCard card)
    {
        if (card.Request is not { } r) return;
        _loading = true;
        try
        {
            if (r.Equation is { } eq) { _state.Equation = eq.Clone(); LoadEquation(); }
            if (r.Polynomial is { } p) { _state.Polynomial = p.Clone().Normalize(); LoadPolynomial(); }
            if (r.Linear is { } l) { _state.Linear = l.Clone().Normalize(); LoadLinear(); }
            if (r.Nonlinear is { } n) { _state.Nonlinear = n.Clone().Normalize(); LoadNonlinear(); }
            if (r.Inequality is { } i) { _state.Inequality = i.Clone(); LoadInequality(); }
        }
        finally
        {
            _loading = false;
        }
        SelectTool(card.Tool, preview: false);
        SaveState();
        FocusFirst();
        _s.ShowStatus($"Card {card.Number} is back in the {SolverTools.Name(card.Tool)} tool");
        _ = SolveAsync(card.Tool, addCard: false);
    }

    private void FixIt(SolverCard card, string hint)
    {
        Restore(card);
        switch (card.Tool)
        {
            case SolverTool.Equation: _equationEditor.Text = hint; break;
            case SolverTool.Inequality: _inequalityEditor.Text = hint; break;
        }
        _ = SolveAsync(card.Tool);
    }
}
