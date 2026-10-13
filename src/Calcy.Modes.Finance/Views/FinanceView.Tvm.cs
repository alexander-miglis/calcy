using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Calcy.Modes.Finance.Logic;
using Calcy.Statistics;
using Calcy.UI.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>The TVM tab: fill four fields, solve the fifth.</summary>
public sealed partial class FinanceView
{
    private readonly Dictionary<TvmVariable, TextBox> _tvmBoxes = [];
    private readonly Dictionary<TvmVariable, TextBlock> _tvmErrors = [];
    private readonly Dictionary<TvmVariable, PixelButton> _solveButtons = [];
    private TextBox _pyBox = null!;
    private TextBox _cyBox = null!;
    private TextBlock _rateErrors = null!;
    private List<PixelToggle> _timing = null!;
    private Border _solvedBox = null!;
    private PixelText _solvedLabel = null!;
    private TextBlock _solvedValue = null!;
    private TextBlock _solvedText = null!;
    private TextBlock _totalPaid = null!;
    private TextBlock _totalInterest = null!;
    private TextBlock _interestCaption = null!;
    private BalanceChart _balanceChart = null!;
    private TextBlock _presetHint = null!;
    private bool _tvmUpdating;

    /// <summary>The current TVM answer (null when it cannot be solved yet).</summary>
    internal TvmSolution? TvmAnswer { get; private set; }

    /// <summary>The text in the solved box (for tests).</summary>
    internal string SolvedValueText => _solvedValue.Text ?? "";

    /// <summary>The TVM input for a variable (for tests).</summary>
    internal TextBox TvmBox(TvmVariable v) => _tvmBoxes[v];

    /// <summary>The SOLVE button next to a variable (for tests).</summary>
    internal PixelButton SolveButton(TvmVariable v) => _solveButtons[v];

    private Control BuildTvmPage()
    {
        // Left column: presets, the five fields, P/Y and C/Y, payment timing, the sign rule.
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(Title("Fill four, solve the fifth", "or start from an example:"));
        var presets = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -4, 0, 4) };
        foreach (var p in TvmPresets.All)
        {
            var b = Ui.Small(p.Name, "Example: " + p.Name, p.Description, "paper");
            b.Margin = new Thickness(0, 0, 6, 6);
            var preset = p;
            b.Click += (_, _) => ApplyPreset(preset);
            presets.Children.Add(b);
        }
        left.Children.Add(presets);

        foreach (var v in TvmWorksheet.Variables) left.Children.Add(TvmRow(v));

        _pyBox = Ui.Input("Payments per year (P/Y)", "P/Y: payments per year, for example 12 for monthly");
        _cyBox = Ui.Input("Compounding periods per year (C/Y)", "C/Y: how often interest is added each year; leave blank for the same as P/Y");
        _cyBox.PlaceholderText = "= P/Y";
        _pyBox.Text = _state.Tvm.PaymentsPerYear;
        _cyBox.Text = _state.Tvm.CompoundingsPerYear;
        Field(_pyBox, () => SolveTvm(null), FinanceTab.Tvm);
        Field(_cyBox, () => SolveTvm(null), FinanceTab.Tvm);
        Ui.OnText(_pyBox, () => OnTvmEdited(null));
        Ui.OnText(_cyBox, () => OnTvmEdited(null));
        var yearGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        Grid.SetColumn(_cyBox, 2);
        yearGrid.Children.Add(_pyBox);
        yearGrid.Children.Add(_cyBox);
        left.Children.Add(Row("P/Y C/Y", "per year", yearGrid, null));
        _rateErrors = Ui.Text("", "error");
        _rateErrors.IsVisible = false;
        _rateErrors.Margin = new Thickness(104, 0, 0, 0);
        left.Children.Add(_rateErrors);

        var (timing, items) = Ui.Segmented("Payment timing", [("End", "Payments at the end of each period"), ("Begin", "Payments at the start of each period")]);
        _timing = items;
        ToolTip.SetTip(items[0], "END: payments at the end of each period (most loans)");
        ToolTip.SetTip(items[1], "BEGIN: payments at the start of each period (rent, leases, many savings plans)");
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            items[i].Click += (_, _) =>
            {
                Ui.Check(_timing, index);
                _state.Tvm.Timing = index == 0 ? PaymentTiming.End : PaymentTiming.Begin;
                RecomputeTvm();
                SaveSoon();
            };
        }
        Ui.Check(_timing, _state.Tvm.Timing == PaymentTiming.End ? 0 : 1);
        left.Children.Add(Row("Payment", "when paid", timing, null));

        var sign = Ui.Text("Money you receive is positive, money you pay is negative (same as TI and Casio). A loan: PV positive, PMT negative.", "hint");
        sign.Margin = new Thickness(0, 4, 0, 0);
        left.Children.Add(sign);
        _presetHint = Ui.Text("", "hint");
        _presetHint.IsVisible = false;
        left.Children.Add(_presetHint);

        // Right column: the answer, totals and the balance chart.
        _solvedLabel = Ui.Pixel("Solved", 2);
        _solvedValue = Ui.Text("?", "big");
        _solvedText = Ui.Text("", "body");
        AutomationProperties.SetLiveSetting(_solvedValue, AutomationLiveSetting.Polite);
        _solvedBox = new Border { Child = new StackPanel { Spacing = 6, Children = { _solvedLabel, _solvedValue, _solvedText } } };
        _solvedBox.Classes.Add("solved");
        AutomationProperties.SetName(_solvedBox, "Answer");

        var (paidTile, paid, _) = Ui.Tile("Total paid");
        _totalPaid = paid;
        var (interestTile, interest, _) = Ui.Tile("Interest");
        _totalInterest = interest;
        _interestCaption = (TextBlock)((StackPanel)interestTile.Child!).Children[0];
        ToolTip.SetTip(paidTile, "All the regular payments added up: PMT × N");
        ToolTip.SetTip(interestTile, "Every amount added up (PV + PMT × N + FV): negative is interest you pay, positive is interest you earn");
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        Grid.SetColumn(interestTile, 2);
        tiles.Children.Add(paidTile);
        tiles.Children.Add(interestTile);

        _balanceChart = new BalanceChart { Height = 150 };
        AutomationProperties.SetName(_balanceChart, "Remaining balance over the term");
        var chartLabel = Ui.Pixel("Balance", 1.5, "Calcy.Muted");
        chartLabel.Margin = new Thickness(12, 8, 0, 0);
        chartLabel.VerticalAlignment = VerticalAlignment.Top;
        chartLabel.HorizontalAlignment = HorizontalAlignment.Left;
        var chart = new Border { Child = new Panel { Children = { _balanceChart, chartLabel } } };
        chart.Classes.Add("chart");

        var right = new StackPanel { Spacing = 12, Children = { _solvedBox, tiles, chart } };

        FlowColumns.SetMinColumnWidth(left, 380);
        FlowColumns.SetMinColumnWidth(right, 280);
        left.VerticalAlignment = VerticalAlignment.Top;
        right.VerticalAlignment = VerticalAlignment.Top;
        var columns = new FlowColumns { Gap = 24, Children = { left, right } };
        return Page(columns);
    }

    /// <summary>One row: label (key and meaning), the input, and its SOLVE button, with the error below.</summary>
    private Control TvmRow(TvmVariable v)
    {
        string key = TvmWorksheet.Key(v);
        var box = Ui.Input($"{key}, {TvmWorksheet.Meaning(v)}", Tip(v));
        box.Text = _state.Tvm.Get(v);
        Field(box, () => SolveTvm(null), FinanceTab.Tvm);
        Ui.OnText(box, () => OnTvmEdited(v));
        box.AddHandler(KeyDownEvent, (_, e) => OnTvmKey(v, e), RoutingStrategies.Tunnel);
        _tvmBoxes[v] = box;

        var solve = Ui.Button("Solve", "Solve for " + key, $"Work out {key} from the other fields (Ctrl+Enter in the field)", "primary");
        solve.MinHeight = 44;
        solve.HorizontalAlignment = HorizontalAlignment.Stretch;
        solve.Click += (_, _) => SolveTvm(v);
        _solveButtons[v] = solve;

        var error = Ui.Text("", "error");
        error.IsVisible = false;
        _tvmErrors[v] = error;
        return Row(key, ShortMeaning(v), box, solve, error);
    }

    private static string ShortMeaning(TvmVariable v) => v switch
    {
        TvmVariable.N => "payments",
        TvmVariable.InterestRate => "% per year",
        TvmVariable.PresentValue => "value now",
        TvmVariable.Payment => "each payment",
        _ => "value at end",
    };

    private static string Tip(TvmVariable v) => v switch
    {
        TvmVariable.N => "N: the number of payments, for example 30*12 for 30 years of monthly payments",
        TvmVariable.InterestRate => "I%: the yearly interest rate in percent, for example 6.5",
        TvmVariable.PresentValue => "PV: the amount at the start. Positive if you receive it (a loan), negative if you pay it (a deposit)",
        TvmVariable.Payment => "PMT: each regular payment. Negative if you pay it, positive if you receive it",
        _ => "FV: the amount at the end. 0 when a loan is paid off",
    };

    private static Grid Row(string key, string meaning, Control input, Control? button, TextBlock? error = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*,88"), ColumnSpacing = 8, RowDefinitions = new RowDefinitions("Auto,Auto") };
        var label = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Pixel(key, 2), Ui.Text(meaning, "meaning") } };
        grid.Children.Add(label);
        Grid.SetColumn(input, 1);
        if (button is null) Grid.SetColumnSpan(input, 2);
        grid.Children.Add(input);
        if (button is not null)
        {
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        if (error is not null)
        {
            Grid.SetRow(error, 1);
            Grid.SetColumn(error, 1);
            Grid.SetColumnSpan(error, 2);
            error.Margin = new Thickness(0, 3, 0, 0);
            grid.Children.Add(error);
        }
        return grid;
    }

    private void OnTvmKey(TvmVariable v, KeyEventArgs e)
    {
        int i = TvmWorksheet.Variables.ToList().IndexOf(v);
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                e.Handled = true;
                SolveTvm(v);
                break;
            case Key.Escape when !string.IsNullOrEmpty(_tvmBoxes[v].Text):
                e.Handled = true;
                _tvmBoxes[v].Text = "";
                break;
            case Key.Up when i > 0:
                e.Handled = true;
                FocusField(_tvmBoxes[TvmWorksheet.Variables[i - 1]]);
                break;
            case Key.Down when i < TvmWorksheet.Variables.Count - 1:
                e.Handled = true;
                FocusField(_tvmBoxes[TvmWorksheet.Variables[i + 1]]);
                break;
        }
    }

    private void ReadTvmBoxes()
    {
        foreach (var (v, box) in _tvmBoxes) _state.Tvm.Set(v, box.Text ?? "");
        _state.Tvm.PaymentsPerYear = _pyBox.Text ?? "";
        _state.Tvm.CompoundingsPerYear = _cyBox.Text ?? "";
    }

    private void OnTvmEdited(TvmVariable? v)
    {
        if (_tvmUpdating || _loading || _pyBox is null || _cyBox is null) return;
        // Typing into the solved field makes it an input again.
        if (v is not null && v == _state.Solved) _state.Solved = null;
        ReadTvmBoxes();
        _presetHint.IsVisible = false;
        RecomputeTvm();
        SaveSoon();
    }

    private void ApplyPreset(TvmPreset preset)
    {
        var inputs = TvmPresets.Fill(preset);
        _tvmUpdating = true;
        try
        {
            foreach (var v in TvmWorksheet.Variables) Ui.SetText(_tvmBoxes[v], inputs.Get(v));
            Ui.SetText(_pyBox, inputs.PaymentsPerYear);
            Ui.SetText(_cyBox, inputs.CompoundingsPerYear);
        }
        finally
        {
            _tvmUpdating = false;
        }
        _state.Tvm = inputs;
        _state.Solved = null;
        Ui.Check(_timing, 0);
        SolveTvm(null);
        Ui.Show(_presetHint, preset.Name + ": " + preset.Description);
    }

    /// <summary>Re-reads the fields and updates the answer, totals and chart (no card).</summary>
    private void RecomputeTvm()
    {
        if (_loading || _solvedBox is null) return;
        var o = TvmWorksheet.Solve(_state.Tvm, _reader, null, _state.Solved);
        TvmAnswer = o.Solution;

        foreach (var v in TvmWorksheet.Variables)
        {
            var box = _tvmBoxes[v];
            string key = TvmWorksheet.Key(v);
            o.FieldErrors.TryGetValue(key, out var err);
            Ui.SetError(box, err, Tip(v));
            Ui.Show(_tvmErrors[v], err);
            box.Classes.Set("solved", o.Target == v);
            box.PlaceholderText = o.Target == v
                ? (o.Solution is { } s ? Short(s) : "solve for this")
                : "";
        }
        o.FieldErrors.TryGetValue(TvmWorksheet.PaymentsPerYearKey, out var pyErr);
        o.FieldErrors.TryGetValue(TvmWorksheet.CompoundingsPerYearKey, out var cyErr);
        Ui.SetError(_pyBox, pyErr, "P/Y: payments per year, for example 12 for monthly");
        Ui.SetError(_cyBox, cyErr, "C/Y: how often interest is added each year; leave blank for the same as P/Y");
        Ui.Show(_rateErrors, string.Join(" ", new[] { pyErr, cyErr }.Where(x => x is not null)));

        // Keep a solved field up to date while the other fields change (unless it is being edited).
        if (o.Solution is { } sol && sol.Solved == _state.Solved && !_tvmBoxes[sol.Solved].IsFocused &&
            !string.IsNullOrWhiteSpace(_state.Tvm.Get(sol.Solved)))
        {
            var text = Money.Field(sol.Value);
            if (_tvmBoxes[sol.Solved].Text != text)
            {
                _tvmUpdating = true;
                try { Ui.SetText(_tvmBoxes[sol.Solved], text); }
                finally { _tvmUpdating = false; }
                _state.Tvm.Set(sol.Solved, text);
            }
        }

        ShowTvmAnswer(o);
        _amortDirty = true;
        if (_state.Tab == FinanceTab.Amortization) RefreshAmortization();
    }

    private static string Short(TvmSolution s) => s.Solved switch
    {
        TvmVariable.N => Money.Number(s.Value, 4),
        TvmVariable.InterestRate => Money.Number(s.Value, 4),
        _ => Money.Plain(s.Value),
    };

    private void ShowTvmAnswer(TvmOutcome o)
    {
        bool ok = o.Solution is not null;
        _solvedBox.Classes.Set("problem", !ok);
        if (o.Solution is { } s)
        {
            _solvedLabel.Text = "SOLVED: " + TvmWorksheet.SolvedLabel(s);
            _solvedValue.Text = TvmWorksheet.ValueText(s);
            _solvedText.Text = TvmWorksheet.Explain(s);
            _totalPaid.Text = Money.Format(s.TotalPayments);
            _totalInterest.Text = Money.Format(s.NetInterest);
            _interestCaption.Text = s.NetInterest > 0 ? "INTEREST EARNED" : "INTEREST PAID";
            _balanceChart.Values = BalanceSamples(s.Values);
            AutomationProperties.SetName(_solvedValue, $"{TvmWorksheet.Key(s.Solved)} = {_solvedValue.Text}");
        }
        else
        {
            _solvedLabel.Text = o.Target is { } t ? "SOLVING: " + TvmWorksheet.Key(t) : "NOTHING TO SOLVE YET";
            _solvedValue.Text = "?";
            _solvedText.Text = o.Problem ?? "";
            _totalPaid.Text = "?";
            _totalInterest.Text = "?";
            _interestCaption.Text = "INTEREST";
            _balanceChart.Values = [];
            AutomationProperties.SetName(_solvedValue, o.Problem ?? "");
        }
    }

    /// <summary>The balance at the start and after every few payments: about 20 bars, like the design.</summary>
    private static IReadOnlyList<double> BalanceSamples(TvmValues v)
    {
        if (AmortizationReport.TryBuild(v, out _) is not { } r || r.Rows.Count == 0) return [];
        int step = Math.Max(1, (int)Math.Ceiling(r.Rows.Count / 20.0));
        var list = new List<double> { v.PresentValue };
        for (int k = step; k <= r.Rows.Count && list.Count < 21; k += step) list.Add(r.Rows[k - 1].Balance);
        if (list.Count < 21 && (r.Rows.Count % step) != 0) list.Add(r.Rows[^1].Balance);
        return list;
    }

    /// <summary>Solves for a variable (or the blank one), puts the answer in its field and adds a card.</summary>
    internal void SolveTvm(TvmVariable? requested)
    {
        ReadTvmBoxes();
        var o = TvmWorksheet.Solve(_state.Tvm, _reader, requested, _state.Solved);
        if (o.Solution is not { } s)
        {
            ShowTvmAnswer(o);
            _services.ShowStatus(o.Problem ?? "Cannot solve yet");
            foreach (var v in TvmWorksheet.Variables)
            {
                if (!o.FieldErrors.ContainsKey(TvmWorksheet.Key(v))) continue;
                FocusField(_tvmBoxes[v]);
                break;
            }
            return;
        }
        _state.Solved = s.Solved;
        _tvmUpdating = true;
        try { Ui.SetText(_tvmBoxes[s.Solved], Money.Field(s.Value)); }
        finally { _tvmUpdating = false; }
        _state.Tvm.Set(s.Solved, Money.Field(s.Value));
        RecomputeTvm();
        AddCard(FinanceCards.Tvm(s));
    }
}
