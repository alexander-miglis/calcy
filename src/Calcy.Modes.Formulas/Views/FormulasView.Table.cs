using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Calcy.Formulas;
using Calcy.Modes.Formulas.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Formulas.Views;

/// <summary>
/// The variables table (symbol, meaning, unit and a value box per variable; the unknown is highlighted and shows the
/// answer), the answer box below it, and the background solving that keeps both up to date while typing.
/// </summary>
public sealed partial class FormulasView
{
    /// <summary>Below this table width each variable takes two lines (name and unit, then the box).</summary>
    private const double NarrowTableWidth = 560;

    private static readonly TimeSpan SolveTimeout = TimeSpan.FromSeconds(8);

    private sealed class VariableRow
    {
        public required FormulaVariable Variable { get; init; }
        public required Border Back { get; init; }
        public required Control Symbol { get; init; }
        public required TextBlock Name { get; init; }
        public required TextBlock Unit { get; init; }
        public required TextBox Input { get; init; }
        public required Border Solved { get; init; }
        public required TextBlock SolvedText { get; init; }
        public PixelToggle? SolveFor { get; init; }
    }

    private readonly List<VariableRow> _rows = [];
    private readonly object _engineGate = new();
    private Grid _table = null!;
    private bool _narrow;
    private bool _settingInputs;
    private string _unknown = "";

    private Border _answerBox = null!;
    private DockPanel _answerLayout = null!;
    private TextBlock _answerCaption = null!;
    private TextBlock _answerText = null!;
    private TextBlock _answerMessage = null!;
    private TextBlock _answerExact = null!;
    private TextBlock _answerNotes = null!;
    private PixelButton _solveButton = null!;

    private CancellationTokenSource? _liveCts;
    private int _busy;
    private int _generation;
    private FormulaOutcome? _lastOutcome;

    /// <inheritdoc />
    public bool IsBusy => _busy > 0;

    /// <summary>The outcome on screen (null while the first solve runs).</summary>
    internal FormulaOutcome? LastOutcome => _lastOutcome;

    /// <summary>The value box of a variable (for tests).</summary>
    internal TextBox InputFor(string symbol) => _rows.First(r => r.Variable.Symbol == symbol).Input;

    /// <summary>The value boxes on screen, in the formula's order (for tests).</summary>
    internal IReadOnlyList<TextBox> Inputs => [.. _rows.Select(r => r.Input)];

    /// <summary>True when the table uses two lines per variable (narrow page).</summary>
    internal bool NarrowTable => _narrow;

    // ------------------------------------------------------------------ table

    private Control BuildTable(Formula f)
    {
        _rows.Clear();
        _table = new Grid { Name = "Variables" };
        foreach (var v in f.Variables)
        {
            var symbolView = Ui.Symbol(v.Symbol, 19);
            symbolView.HorizontalAlignment = HorizontalAlignment.Center;
            Control symbol;
            PixelToggle? solveFor = null;
            if (f.IsIdentity)
            {
                symbol = new Border { Child = symbolView, MinWidth = 50, MinHeight = 38, Padding = new Thickness(8, 4) };
            }
            else
            {
                var toggle = new PixelToggle { Content = symbolView, MinWidth = 50, MinHeight = 38, Padding = new Thickness(8, 4), ClipToBounds = false };
                AutomationProperties.SetName(toggle, $"Solve for {v.Symbol}, {v.Name}");
                ToolTip.SetTip(toggle, $"Solve for {SymbolText.Plain(v.Symbol)} ({v.Name})");
                var s = v.Symbol;
                toggle.Click += (_, _) => SetUnknown(s);
                symbol = toggle;

                solveFor = new PixelToggle
                {
                    Content = Ui.Pixel("Solve for", 1.5),
                    MinHeight = 34,
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    ClipToBounds = false,
                };
                AutomationProperties.SetName(solveFor, $"Solve for {v.Symbol}");
                ToolTip.SetTip(solveFor, $"Make {SymbolText.Plain(v.Symbol)} the one to find");
                solveFor.Click += (_, _) => SetUnknown(s);
            }

            var name = Ui.Text(v.Name, "rowname");
            ToolTip.SetTip(name, FormulaTexts.VariableTip(v));
            var unit = Ui.Text(v.Unit, "unit");
            var input = new TextBox { Text = _saved.Values.GetValueOrDefault(v.Symbol, ""), PlaceholderText = InputReader.Placeholder(v), VerticalAlignment = VerticalAlignment.Center };
            input.Classes.Add("value");
            AutomationProperties.SetName(input, $"{v.Symbol}, {v.Name}" + (v.Unit.Length > 0 ? $", in {v.Unit}" : ""));
            ToolTip.SetTip(input, FormulaTexts.VariableTip(v));
            var solvedText = Ui.Text("?", "solvedvalue");
            var solved = new Border { Child = solvedText, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            solved.Classes.Add("solved");
            AutomationProperties.SetLiveSetting(solvedText, AutomationLiveSetting.Polite);

            var back = new Border();
            back.Classes.Add("row");
            var row = new VariableRow
            {
                Variable = v,
                Back = back,
                Symbol = symbol,
                Name = name,
                Unit = unit,
                Input = input,
                Solved = solved,
                SolvedText = solvedText,
                SolveFor = solveFor,
            };
            Ui.OnText(input, () => OnInputChanged(row));
            Ui.OnEnter(input, () => Recalculate(keep: true));
            _rows.Add(row);
            _table.Children.Add(back);
            _table.Children.Add(symbol);
            _table.Children.Add(name);
            _table.Children.Add(unit);
            _table.Children.Add(input);
            _table.Children.Add(solved);
            if (solveFor is not null) _table.Children.Add(solveFor);
        }
        if (_rows.Count > 0) _rows[^1].Back.BorderThickness = new Thickness(0);
        _table.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < NarrowTableWidth;
            if (narrow == _narrow) return;
            _narrow = narrow;
            LayoutTable();
            LayoutAnswerBox();
        };
        LayoutTable();
        SyncRows();

        var caption = Ui.Caption("Variables");
        var hint = Ui.Text(f.IsIdentity ? "Fill in values to check that both sides agree." : "Pick the one to find, fill in the others.", "meaning");
        hint.VerticalAlignment = VerticalAlignment.Center;
        var strip = new Border { Child = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, Children = { caption, hint } } };
        strip.Classes.Add("strip");
        var box = new Border { Child = new StackPanel { Children = { strip, _table } } };
        box.Classes.Add("box");
        AutomationProperties.SetName(box, "Variables");
        return box;
    }

    private void LayoutTable()
    {
        var g = _table;
        g.RowDefinitions.Clear();
        g.ColumnDefinitions = new ColumnDefinitions(_narrow ? "Auto,*,Auto" : "Auto,*,Auto,*,Auto");
        bool identity = _formula.IsIdentity;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (!_narrow)
            {
                int row = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Place(r.Back, row, 0, 1, 5);
                Place(r.Symbol, row, 0, 1, 1, new Thickness(10, 6, 12, 6));
                Place(r.Name, row, 1, 1, 1, new Thickness(0, 6, 10, 6));
                Place(r.Unit, row, 2, 1, 1, new Thickness(0, 6, 12, 6));
                Place(r.Input, row, 3, 1, identity ? 2 : 1, new Thickness(0, 6, 10, 6));
                Place(r.Solved, row, 3, 1, 1, new Thickness(0, 6, 10, 6));
                if (r.SolveFor is not null) Place(r.SolveFor, row, 4, 1, 1, new Thickness(0, 6, 10, 6));
                r.Unit.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                int row = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Place(r.Back, row, 0, 2, 3);
                Place(r.Symbol, row, 0, 2, 1, new Thickness(10, 8, 12, 8));
                Place(r.Name, row, 1, 1, 1, new Thickness(0, 8, 8, 2));
                Place(r.Unit, row, 2, 1, 1, new Thickness(0, 8, 10, 2));
                Place(r.Input, row + 1, 1, 1, identity ? 2 : 1, new Thickness(0, 4, identity ? 10 : 8, 8));
                Place(r.Solved, row + 1, 1, 1, 1, new Thickness(0, 4, 8, 8));
                if (r.SolveFor is not null) Place(r.SolveFor, row + 1, 2, 1, 1, new Thickness(0, 4, 10, 8));
                r.Unit.HorizontalAlignment = HorizontalAlignment.Right;
            }
        }
    }

    private static void Place(Control c, int row, int column, int rowSpan, int columnSpan, Thickness? margin = null)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, column);
        Grid.SetRowSpan(c, rowSpan);
        Grid.SetColumnSpan(c, columnSpan);
        if (margin is { } m) c.Margin = m;
    }

    /// <summary>Shows which variable is the unknown: its toggles checked, the answer instead of its box.</summary>
    private void SyncRows()
    {
        foreach (var r in _rows)
        {
            bool unknown = r.Variable.Symbol == _unknown;
            if (r.Symbol is PixelToggle t) t.IsChecked = unknown;
            if (r.SolveFor is { } s)
            {
                s.IsChecked = unknown;
                AutomationProperties.SetItemStatus(s, unknown ? "solving for this" : "");
            }
            r.Input.IsVisible = !unknown;
            r.Solved.IsVisible = unknown;
        }
        UpdateCaption();
    }

    /// <summary>Makes a variable the one to solve for and solves again.</summary>
    internal void SetUnknown(string symbol)
    {
        if (_formula.IsIdentity || _formula.GetVariable(symbol) is null)
        {
            SyncRows();
            return;
        }
        bool changed = _unknown != symbol;
        _unknown = symbol;
        _saved.SolveFor = symbol;
        SyncRows();
        if (!changed) return;
        Recalculate();
        SaveSoon();
    }

    private void OnInputChanged(VariableRow row)
    {
        if (_settingInputs) return;
        Store(row);
        Recalculate();
        SaveSoon();
    }

    private void Store(VariableRow row)
    {
        var text = row.Input.Text?.Trim() ?? "";
        if (text.Length == 0) _saved.Values.Remove(row.Variable.Symbol);
        else _saved.Values[row.Variable.Symbol] = text;
    }

    /// <summary>Replaces the text of every box (missing symbols become empty) without solving once per box.</summary>
    internal void SetInputs(IReadOnlyDictionary<string, string> texts)
    {
        _settingInputs = true;
        try
        {
            // Only the text changes: moving the caret would scroll the page to the last box.
            foreach (var r in _rows)
            {
                r.Input.Text = texts.GetValueOrDefault(r.Variable.Symbol, "");
                Store(r);
            }
        }
        finally
        {
            _settingInputs = false;
        }
        SaveSoon();
    }

    private Dictionary<string, string> CurrentTexts() =>
        _rows.ToDictionary(r => r.Variable.Symbol, r => r.Input.Text ?? "", StringComparer.Ordinal);

    // ------------------------------------------------------------------ answer box

    private Control BuildAnswerBox(Formula f)
    {
        _answerCaption = Ui.Text("", "cap");
        _answerText = Ui.Text("", "big");
        _answerText.Name = "Answer";
        AutomationProperties.SetLiveSetting(_answerText, AutomationLiveSetting.Polite);
        _answerMessage = Ui.Text("", "waiting");
        _answerMessage.Name = "AnswerMessage";
        _answerExact = Ui.Text("", "exact");
        _answerNotes = Ui.Text("", "note");
        _solveButton = Ui.Button(f.IsIdentity ? "Check" : "Solve", f.IsIdentity ? "Check both sides" : "Solve and keep as a card",
            f.IsIdentity ? "Evaluate both sides with these values and keep the check as a card (Enter)" : "Keep this answer as a card (Enter)", "primary");
        _solveButton.Name = "Solve";
        _solveButton.Click += (_, _) => Recalculate(keep: true);
        var texts = new StackPanel { Spacing = 4, Children = { _answerCaption, _answerText, _answerMessage, _answerExact, _answerNotes } };
        _answerLayout = new DockPanel { Children = { _solveButton, texts } };
        _answerBox = new Border { Child = _answerLayout, Name = "AnswerBox" };
        _answerBox.Classes.Add("result");
        AutomationProperties.SetName(_answerBox, "Answer");
        LayoutAnswerBox();
        UpdateCaption();
        return _answerBox;
    }

    private void LayoutAnswerBox()
    {
        if (_solveButton is null) return;
        DockPanel.SetDock(_solveButton, _narrow ? Dock.Bottom : Dock.Right);
        _solveButton.HorizontalAlignment = HorizontalAlignment.Left;
        _solveButton.VerticalAlignment = _narrow ? VerticalAlignment.Center : VerticalAlignment.Top;
        _solveButton.Margin = _narrow ? new Thickness(0, 10, 0, 0) : new Thickness(14, 2, 0, 0);
        _answerText.FontSize = _narrow ? 22 : 26;
    }

    private void UpdateCaption()
    {
        if (_answerCaption is null) return;
        if (_formula.IsIdentity)
        {
            _answerCaption.Text = "CHECK BOTH SIDES";
            return;
        }
        var v = _formula.GetVariable(_unknown);
        _answerCaption.Text = v is null ? "SOLVE" : $"SOLVE FOR {SymbolText.Plain(v.Symbol)}  ·  {v.Name}";
    }

    private void ShowOutcome(FormulaOutcome outcome)
    {
        _lastOutcome = outcome;
        foreach (var r in _rows)
        {
            var error = outcome.InputErrors.GetValueOrDefault(r.Variable.Symbol);
            r.Input.Classes.Set("error", error is not null);
            ToolTip.SetTip(r.Input, error ?? FormulaTexts.VariableTip(r.Variable));
        }
        var answer = outcome.Answer;
        if (_rows.FirstOrDefault(r => r.Variable.Symbol == _unknown) is { } unknownRow)
        {
            unknownRow.SolvedText.Text = answer is { Kind: AnswerKind.Solved } a ? string.Join(" or ", a.Values.Select(x => x.Number)) : "?";
            ToolTip.SetTip(unknownRow.Solved, answer?.Headline ?? outcome.Message);
        }

        bool problem = outcome.Kind is OutcomeKind.Invalid or OutcomeKind.Failed or OutcomeKind.Cancelled;
        _answerBox.Classes.Set("waiting", outcome.Kind == OutcomeKind.NeedsInput);
        _answerBox.Classes.Set("problem", problem);
        _answerMessage.Classes.Set("waiting", !problem);
        _answerMessage.Classes.Set("error", problem);
        UpdateCaption();
        if (answer is null)
        {
            _answerText.IsVisible = false;
            Ui.Show(_answerMessage, outcome.Message);
            Ui.Show(_answerExact, null);
            Ui.Show(_answerNotes, null);
            return;
        }
        _answerMessage.IsVisible = false;
        _answerText.IsVisible = true;
        if (answer.Kind == AnswerKind.Solved)
        {
            _answerText.Text = answer.Headline;
            Ui.Show(_answerExact, answer.ExactLine is { } exact ? "exact: " + exact : null);
            Ui.Show(_answerNotes, answer.Notes.Count > 0 ? string.Join(Environment.NewLine, answer.Notes) : null);
        }
        else
        {
            _answerText.Text = answer.Holds ? $"both sides = {answer.Left!.Number}" : $"left {answer.Left!.Number}, right {answer.Right!.Number}";
            Ui.Show(_answerExact, answer.Holds ? "It holds, as it does for every value." : "The sides differ here: check the values are allowed.");
            Ui.Show(_answerNotes, answer.Given.Count > 0 ? "Checked with " + answer.GivenLine : null);
        }
    }

    // ------------------------------------------------------------------ solving in the background

    /// <summary>
    /// Solves (or checks) with the current boxes off the UI thread. A newer live request cancels the previous one;
    /// with <paramref name="keep"/> the answer is also kept as a card, even if more typing follows.
    /// </summary>
    internal void Recalculate(bool keep = false)
    {
        if (_formula is null || _answerBox is null) return;
        var formula = _formula;
        var texts = CurrentTexts();
        var job = new FormulaJob(formula, texts, formula.IsIdentity ? null : _unknown);
        CancellationTokenSource cts;
        if (keep) cts = new CancellationTokenSource(SolveTimeout);
        else
        {
            _liveCts?.Cancel();
            cts = new CancellationTokenSource(SolveTimeout);
            _liveCts = cts;
        }
        int generation = ++_generation;
        _busy++;
        var token = cts.Token;
        Task.Run(() =>
        {
            lock (_engineGate) return _calculator.Run(job, token);
        }, CancellationToken.None).ContinueWith(t =>
        {
            var outcome = t.IsCompletedSuccessfully
                ? t.Result
                : FormulaOutcome.Without(OutcomeKind.Failed, "Something went wrong: " + (t.Exception?.InnerException?.Message ?? "unknown error"));
            Dispatcher.UIThread.Post(() => Finish(outcome, formula, texts, generation, keep, cts));
        }, TaskScheduler.Default);
    }

    private void Finish(FormulaOutcome outcome, Formula formula, Dictionary<string, string> texts, int generation, bool keep, CancellationTokenSource cts)
    {
        _busy--;
        if (ReferenceEquals(_liveCts, cts)) _liveCts = null;
        cts.Dispose();
        bool current = generation == _generation && ReferenceEquals(formula, _formula);
        // A live request that a newer one replaced is simply dropped.
        if (current) ShowOutcome(outcome);
        if (keep) Keep(outcome, texts);
    }

    private void Keep(FormulaOutcome outcome, IReadOnlyDictionary<string, string> texts)
    {
        if (outcome.Answer is not { } answer)
        {
            _services.ShowStatus(outcome.Message.Length > 0 ? outcome.Message : "There is no answer to keep yet");
            return;
        }
        var card = _state.AddCard(answer, texts, DateTime.Now);
        RenderCards();
        _cardScroll.Offset = default;
        SaveNow();
        _services.ShowStatus($"Card #{card.Number}: {string.Join(" or ", card.Results)}");
    }
}
