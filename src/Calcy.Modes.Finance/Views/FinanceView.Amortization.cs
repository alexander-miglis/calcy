using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Finance.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>The Amortization tab: the schedule of the TVM loan, by year with the payments inside, and ΣInt / ΣPrn / bal.</summary>
public sealed partial class FinanceView
{
    private readonly HashSet<int> _openYears = [0];
    private AmortizationReport? _report;
    private bool _amortDirty = true;
    private TextBox _fromBox = null!;
    private TextBox _toBox = null!;
    private TextBlock _rangeError = null!;
    private TextBlock _sumInt = null!;
    private TextBlock _sumPrn = null!;
    private TextBlock _balAfter = null!;
    private TextBlock _balCaption = null!;
    private TextBlock _amortProblem = null!;
    private Control _amortProblemPanel = null!;
    private PixelButton _goToTvm = null!;
    private InterestPrincipalChart _yearChart = null!;
    private Border _legendInterest = null!;
    private ScheduleTable _amortTable = null!;
    private Control _amortBody = null!;
    private PixelButton _amortCsv = null!;
    private PixelButton _amortCard = null!;

    /// <summary>The schedule table (for tests).</summary>
    internal ScheduleTable AmortizationTable => _amortTable;

    /// <summary>The schedule on screen (for tests).</summary>
    internal AmortizationReport? AmortizationShown => _report;

    private Control BuildAmortizationPage()
    {
        var title = Title("Amortization", "Uses the loan from the TVM tab. Click a year to show its payments.");

        _amortProblem = Ui.Text("", "body");
        _goToTvm = Ui.Small("Go to TVM", "Go to the TVM tab", null, "sel");
        _goToTvm.HorizontalAlignment = HorizontalAlignment.Left;
        _goToTvm.Click += (_, _) => ShowTab(FinanceTab.Tvm);
        var problem = new StackPanel { Spacing = 10, Children = { _amortProblem, _goToTvm }, IsVisible = false };
        _amortProblemPanel = problem;

        // Range tool: the TI ΣInt, ΣPrn and bal.
        _fromBox = Ui.Input("First payment", "First payment of the range", 90, small: true);
        _toBox = Ui.Input("Last payment", "Last payment of the range", 90, small: true);
        _fromBox.Text = _state.RangeFrom;
        _toBox.Text = _state.RangeTo;
        Field(_fromBox, AddAmortizationCard, FinanceTab.Amortization);
        Field(_toBox, AddAmortizationCard, FinanceTab.Amortization);
        Ui.OnText(_fromBox, () => OnRangeEdited());
        Ui.OnText(_toBox, () => OnRangeEdited());
        var rangeRow = new WrapPanel { Orientation = Orientation.Horizontal };
        rangeRow.Children.Add(Words("Payments"));
        _fromBox.Margin = new Thickness(0, 0, 10, 0);
        rangeRow.Children.Add(_fromBox);
        rangeRow.Children.Add(Words("to"));
        rangeRow.Children.Add(_toBox);
        _rangeError = Ui.Text("", "error");
        _rangeError.IsVisible = false;
        var (intTile, sumInt, _) = Ui.Tile("ΣInt interest", selected: true);
        var (prnTile, sumPrn, _) = Ui.Tile("ΣPrn principal");
        var (balTile, bal, _) = Ui.Tile("Balance after");
        _sumInt = sumInt;
        _sumPrn = sumPrn;
        _balAfter = bal;
        _balCaption = (TextBlock)((StackPanel)balTile.Child!).Children[0];
        ToolTip.SetTip(intTile, "Interest paid in these payments (the TI ΣInt)");
        ToolTip.SetTip(prnTile, "Loan paid off by these payments (the TI ΣPrn)");
        ToolTip.SetTip(balTile, "What is still owed after the last payment of the range (the TI bal)");
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*") };
        Grid.SetColumn(prnTile, 2);
        Grid.SetColumn(balTile, 4);
        tiles.Children.Add(intTile);
        tiles.Children.Add(prnTile);
        tiles.Children.Add(balTile);
        var range = new StackPanel { Spacing = 8, Children = { rangeRow, _rangeError, tiles } };

        // Chart: principal and interest per year.
        _yearChart = new InterestPrincipalChart { Height = 116 };
        AutomationProperties.SetName(_yearChart, "Interest and principal paid each year");
        _legendInterest = new Border { Width = 16, Height = 16, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center }
            .Token(Border.BorderBrushProperty, "Calcy.Line");
        UpdateLegend();
        _yearChart.PropertyChanged += (_, e) =>
        {
            if (e.Property == InterestPrincipalChart.InterestBrushProperty || e.Property == InterestPrincipalChart.BackBrushProperty) UpdateLegend();
        };
        var legend = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 0),
            Children =
            {
                Ui.Pixel("Per year", 1.5, "Calcy.Muted"),
                Spacer(12), Ui.Swatch("Calcy.G1"), Spacer(5), Ui.Text("PRINCIPAL", "cap"),
                Spacer(12), _legendInterest, Spacer(5), Ui.Text("INTEREST", "cap"),
            },
        };
        foreach (var c in legend.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var chart = new Border { Child = new StackPanel { Children = { legend, _yearChart } } };
        chart.Classes.Add("chart");
        chart.VerticalAlignment = VerticalAlignment.Top;

        FlowColumns.SetMinColumnWidth(range, 330);
        FlowColumns.SetMinColumnWidth(chart, 240);
        FlowColumns.SetWeight(range, 2);
        var top = new FlowColumns { Gap = 20, Children = { range, chart } };

        // Toolbar and table.
        var openAll = Ui.Small("Open all", "Show every payment", "Show the payments of every year");
        openAll.Click += (_, _) =>
        {
            if (_report is null) return;
            for (int i = 0; i < _report.Years.Count; i++) _openYears.Add(i);
            FillAmortizationTable();
        };
        var closeAll = Ui.Small("Close all", "Show years only", "Show one line per year");
        closeAll.Click += (_, _) =>
        {
            _openYears.Clear();
            FillAmortizationTable();
        };
        _amortCsv = ExportButton("the schedule", () => _report?.ToCsv());
        _amortCard = AddCardButton("amortization", AddAmortizationCard);
        var leftTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { openAll, closeAll } };
        // Side by side when there is room; in a narrow window the right-hand buttons go on their own line.
        var rightTools = Toolbar(_amortCsv, _amortCard);
        FlowColumns.SetMinColumnWidth(leftTools, 250);
        FlowColumns.SetMinColumnWidth(rightTools, 290);
        var tools = new FlowColumns { Gap = 10, Margin = new Thickness(0, 14, 0, 10), Children = { leftTools, rightTools } };

        _amortTable = new ScheduleTable(
            [new("#", 0, 92), new("Payment"), new("Interest"), new("Principal"), new("Balance")],
            "Amortization schedule");
        _amortTable.GroupToggled += (_, year) =>
        {
            if (!_openYears.Remove(year)) _openYears.Add(year);
            FillAmortizationTable();
        };

        var head = new StackPanel { Children = { title, top, tools } };
        var body = FillPage(head, _amortTable);
        _amortBody = body;
        problem.VerticalAlignment = VerticalAlignment.Top;
        problem.Margin = new Thickness(16, 50, 16, 16);
        return new Panel { Children = { body, problem } };
    }

    private static Border Spacer(double width) => new() { Width = width };

    private static TextBlock Words(string text)
    {
        var t = Ui.Text(text, "words");
        t.Margin = new Thickness(0, 0, 10, 0);
        return t;
    }

    private void UpdateLegend()
    {
        if (_yearChart.InterestBrush is { } ink) _legendInterest.Background = InterestPrincipalChart.InterestFill(ink, _yearChart.BackBrush);
    }

    private void OnRangeEdited()
    {
        if (_loading) return;
        _state.RangeFrom = _fromBox.Text ?? "";
        _state.RangeTo = _toBox.Text ?? "";
        ShowRange();
        SaveSoon();
    }

    /// <summary>Rebuilds the schedule from the TVM answer when it changed.</summary>
    private void RefreshAmortization()
    {
        if (_amortTable is null || !_amortDirty) return;
        _amortDirty = false;
        string? problem = null;
        AmortizationReport? report = null;
        if (TvmAnswer is not { } s)
            problem = "Fill in the TVM tab first: the schedule needs N, I%, PV and PMT, so solve the loan there.";
        else
            report = AmortizationReport.TryBuild(s.Values, out problem);

        if (report is null)
        {
            _report = null;
            _amortProblem.Text = problem;
            _amortProblemPanel.IsVisible = true;
            _amortBody.IsVisible = false;
            return;
        }
        _amortProblemPanel.IsVisible = false;
        _amortBody.IsVisible = true;
        bool sameLoan = _report is not null && _report.Values == report.Values;
        _report = report;
        if (!sameLoan)
        {
            _openYears.Clear();
            _openYears.Add(0);
            _amortTable.Scroll.Offset = default;
        }
        _yearChart.Values = [.. report.Years.Select(y => (y.Principal, y.Interest))];
        FillAmortizationTable();
        ShowRange();
    }

    private void FillAmortizationTable()
    {
        if (_report is not { } r) return;
        var rows = new List<TableRowData>();
        bool grouped = r.PaymentsPerYear > 1;
        foreach (var y in r.Years)
        {
            int index = y.Year - 1;
            bool open = !grouped || _openYears.Contains(index);
            if (grouped)
            {
                rows.Add(new TableRowData(
                    ["YEAR " + y.Year, Money.Plain(y.Payment), Money.Plain(y.Interest), Money.Plain(y.Principal), Money.Plain(y.EndBalance)],
                    TableRowKind.Group, index, open,
                    $"Year {y.Year}, payments {y.FirstPeriod} to {y.LastPeriod}: paid {Money.Plain(y.Payment)}, interest {Money.Plain(y.Interest)}, principal {Money.Plain(y.Principal)}, balance {Money.Plain(y.EndBalance)}, {(open ? "open" : "closed")}"));
            }
            if (!open) continue;
            for (int k = y.FirstPeriod; k <= y.LastPeriod; k++)
            {
                var p = r.Rows[k - 1];
                rows.Add(new TableRowData(
                    [Periods.Whole(p.Period), Money.Plain(p.Payment), Money.Plain(p.Interest), Money.Plain(p.Principal), Money.Plain(p.Balance)],
                    Name: $"Payment {p.Period}: {Money.Plain(p.Payment)}, interest {Money.Plain(p.Interest)}, principal {Money.Plain(p.Principal)}, balance {Money.Plain(p.Balance)}"));
            }
        }
        var total = new TableRowData(
            ["TOTAL", Money.Plain(r.TotalPayment), Money.Plain(r.TotalInterest), Money.Plain(r.TotalPrincipal), Money.Plain(r.FinalBalance)],
            TableRowKind.Total,
            Name: $"Total of {r.Rows.Count} payments: {Money.Plain(r.TotalPayment)}, interest {Money.Plain(r.TotalInterest)}, principal {Money.Plain(r.TotalPrincipal)}, final balance {Money.Plain(r.FinalBalance)}");
        _amortTable.SetRows(rows, total);
    }

    /// <summary>Reads the range and shows ΣInt, ΣPrn and the balance; returns the range when valid.</summary>
    private AmortizationRange? ShowRange()
    {
        if (_report is not { } r) return null;
        int count = r.Rows.Count;
        var from = _reader.Read(_fromBox.Text, FieldRule.Whole("the first payment", 1, count));
        var to = _reader.Read(_toBox.Text, FieldRule.Whole("the last payment", 1, count));
        string? fromErr = from.IsBlank ? "Type the first payment, for example 1." : from.Error;
        string? toErr = to.IsBlank ? "Type the last payment, for example 12." : to.Error;
        if (fromErr is null && toErr is null && to.Value < from.Value) toErr = "The last payment must not be before the first.";
        Ui.SetError(_fromBox, fromErr, "First payment of the range");
        Ui.SetError(_toBox, toErr, "Last payment of the range");
        Ui.Show(_rangeError, string.Join(" ", new[] { fromErr, toErr }.Where(x => x is not null)));
        if (fromErr is not null || toErr is not null)
        {
            _sumInt.Text = _sumPrn.Text = _balAfter.Text = "?";
            _balCaption.Text = "BALANCE AFTER";
            return null;
        }
        var range = r.Range((int)from.Value, (int)to.Value);
        _sumInt.Text = Money.Format(range.Interest);
        _sumPrn.Text = Money.Format(range.Principal);
        _balAfter.Text = Money.Format(range.Balance);
        _balCaption.Text = "BALANCE AFTER " + Periods.Whole(range.To);
        return range;
    }

    private void AddAmortizationCard()
    {
        RefreshAmortization();
        if (_report is null)
        {
            _services.ShowStatus(_amortProblem.Text ?? "Solve the loan on the TVM tab first");
            return;
        }
        if (ShowRange() is not { } range)
        {
            _services.ShowStatus(_rangeError.Text ?? "Check the payment range");
            return;
        }
        AddCard(FinanceCards.Amortization(_report, range));
    }
}
