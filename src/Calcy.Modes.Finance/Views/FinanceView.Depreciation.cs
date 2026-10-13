using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Finance.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>The Depreciation tab: straight line, sum of the years' digits and declining balance schedules.</summary>
public sealed partial class FinanceView
{
    private List<PixelToggle> _method = null!;
    private TextBox _costBox = null!;
    private TextBox _salvageBox = null!;
    private TextBox _lifeBox = null!;
    private TextBox _factorBox = null!;
    private Control _factorCell = null!;
    private PixelToggle _switchSl = null!;
    private TextBlock _methodHint = null!;
    private TextBlock _depErrors = null!;
    private TextBlock _depFirst = null!;
    private TextBlock _depTotal = null!;
    private TextBlock _depBook = null!;
    private ScheduleTable _depTable = null!;
    private BalanceChart _bookChart = null!;
    private Answer<DepreciationReport>? _depreciation;

    /// <summary>The depreciation table (for tests).</summary>
    internal ScheduleTable DepreciationTable => _depTable;

    private Control BuildDepreciationPage()
    {
        var d = _state.Depreciation;
        var title = Title("Depreciation");
        var (methods, items) = Ui.Segmented("Depreciation method",
            [("SL", "Straight line"), ("SYD", "Sum of the years' digits"), ("DB", "Declining balance")], 40);
        _method = items;
        methods.Width = 260;
        methods.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(items[0], "Straight line: the same amount every year");
        ToolTip.SetTip(items[1], "Sum of the years' digits: more in the early years, falling by the same step each year");
        ToolTip.SetTip(items[2], "Declining balance: a fixed share of the remaining book value each year");
        for (int k = 0; k < items.Count; k++)
        {
            int index = k;
            items[k].Click += (_, _) =>
            {
                Ui.Check(_method, index);
                _state.Depreciation.Method = (DepreciationMethod)index;
                RecomputeDepreciation();
                SaveSoon();
            };
        }
        Ui.Check(_method, (int)d.Method);
        _methodHint = Ui.Text("", "hint");
        _methodHint.VerticalAlignment = VerticalAlignment.Center;
        _methodHint.Margin = new Thickness(16, 0, 0, 0);
        var methodRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(methods, Dock.Left);
        methodRow.Children.Add(methods);
        methodRow.Children.Add(_methodHint);

        _costBox = Ui.Input("Cost", "What the asset cost", 140);
        _salvageBox = Ui.Input("Salvage value", "What it is worth at the end of its life (0 if nothing)", 140);
        _lifeBox = Ui.Input("Life in years", "Useful life in whole years", 90);
        _factorBox = Ui.Input("Declining balance rate in percent", "Percent of the straight line rate: 200 is double declining, 150 is 1.5 times", 90);
        _costBox.Text = d.Cost;
        _salvageBox.Text = d.Salvage;
        _lifeBox.Text = d.Life;
        _factorBox.Text = d.Factor;
        _switchSl = new PixelToggle { Content = Ui.Pixel("Switch to SL", 2), MinHeight = 44, Padding = new Thickness(12, 4), BorderThickness = new Thickness(3), IsChecked = d.SwitchToStraightLine };
        _switchSl.Classes.Add("shadow");
        AutomationProperties.SetName(_switchSl, "Switch to straight line when it gives more");
        ToolTip.SetTip(_switchSl, "Switch to straight line in the year it gives more, so the asset ends exactly at its salvage value");
        _switchSl.Click += (_, _) =>
        {
            _state.Depreciation.SwitchToStraightLine = _switchSl.IsChecked == true;
            RecomputeDepreciation();
            SaveSoon();
        };
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, box) in new[] { ("Cost", _costBox), ("Salvage", _salvageBox), ("Life (years)", _lifeBox) })
        {
            var l = Ui.Labelled(label, box);
            l.Margin = new Thickness(0, 0, 14, 8);
            fields.Children.Add(l);
            Field(box, AddDepreciationCard, FinanceTab.Depreciation);
            Ui.OnText(box, () => OnDepreciationEdited());
        }
        var factor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { Ui.Labelled("Rate %", _factorBox), _switchSl } };
        _switchSl.VerticalAlignment = VerticalAlignment.Bottom;
        _factorCell = factor;
        factor.Margin = new Thickness(0, 0, 0, 8);
        fields.Children.Add(factor);
        Field(_factorBox, AddDepreciationCard, FinanceTab.Depreciation);
        Ui.OnText(_factorBox, () => OnDepreciationEdited());

        _depErrors = Ui.Text("", "error");
        _depErrors.IsVisible = false;
        var (firstTile, first, _) = Ui.Tile("Year 1", selected: true);
        var (totalTile, total, _) = Ui.Tile("Total");
        var (bookTile, book, _) = Ui.Tile("Book value at the end");
        (_depFirst, _depTotal, _depBook) = (first, total, book);
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*"), Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(totalTile, 2);
        Grid.SetColumn(bookTile, 4);
        tiles.Children.Add(firstTile);
        tiles.Children.Add(totalTile);
        tiles.Children.Add(bookTile);

        var tools = Toolbar(ExportButton("the depreciation schedule", () => _depreciation?.Value?.ToCsv()), AddCardButton("depreciation", AddDepreciationCard));
        tools.Margin = new Thickness(0, 14, 0, 10);

        _depTable = new ScheduleTable([new("Year", 0, 80), new("Depreciation"), new("Accumulated"), new("Book value")], "Depreciation schedule");
        _bookChart = new BalanceChart { Height = 128 };
        AutomationProperties.SetName(_bookChart, "Book value at the start and after each year");
        var chartLabel = Ui.Pixel("Book value", 1.5, "Calcy.Muted");
        chartLabel.Margin = new Thickness(12, 8, 0, 0);
        chartLabel.VerticalAlignment = VerticalAlignment.Top;
        chartLabel.HorizontalAlignment = HorizontalAlignment.Left;
        var chart = new Border { Child = new Panel { Children = { _bookChart, chartLabel } }, VerticalAlignment = VerticalAlignment.Bottom };
        chart.Classes.Add("chart");
        var inputs = new StackPanel { Children = { fields, _depErrors, tiles } };
        FlowColumns.SetMinColumnWidth(inputs, 470);
        FlowColumns.SetWeight(inputs, 2);
        FlowColumns.SetMinColumnWidth(chart, 220);
        var columns = new FlowColumns { Gap = 20, Children = { inputs, chart } };
        var head = new StackPanel { Children = { title, methodRow, columns, tools } };
        return FillPage(head, _depTable);
    }

    private void OnDepreciationEdited()
    {
        if (_loading || _factorBox is null) return;
        var d = _state.Depreciation;
        d.Cost = _costBox.Text ?? "";
        d.Salvage = _salvageBox.Text ?? "";
        d.Life = _lifeBox.Text ?? "";
        d.Factor = _factorBox.Text ?? "";
        RecomputeDepreciation();
        SaveSoon();
    }

    private void RecomputeDepreciation()
    {
        if (_loading || _depTable is null) return;
        var d = _state.Depreciation;
        _factorCell.IsVisible = d.Method == DepreciationMethod.DecliningBalance;
        _methodHint.Text = d.Method switch
        {
            DepreciationMethod.StraightLine => "The same amount every year: (cost - salvage) / life.",
            DepreciationMethod.SumOfYearsDigits => "More in the early years: year 1 gets life / (1 + 2 + ... + life) of (cost - salvage).",
            _ => "A fixed share of the book value each year, never below the salvage value.",
        };
        _depreciation = DepreciationReport.Evaluate(d, _reader);
        var e = _depreciation.FieldErrors;
        Ui.SetError(_costBox, e.GetValueOrDefault("cost"), "What the asset cost");
        Ui.SetError(_salvageBox, e.GetValueOrDefault("salvage"), "What it is worth at the end of its life");
        Ui.SetError(_lifeBox, e.GetValueOrDefault("life"), "Useful life in whole years");
        Ui.SetError(_factorBox, e.GetValueOrDefault("factor"), "200 is double declining");
        Ui.Show(_depErrors, e.Count > 0 ? string.Join(" ", e.Values) : _depreciation.Problem);
        if (_depreciation.Value is not { } r)
        {
            _depFirst.Text = _depTotal.Text = _depBook.Text = "?";
            _depTable.SetRows([], null, "Fix the fields above to see the schedule.");
            _bookChart.Values = [];
            return;
        }
        _depFirst.Text = Money.Format(r.Rows.Count > 0 ? r.Rows[0].Depreciation : 0);
        _depTotal.Text = Money.Format(r.Total);
        _depBook.Text = Money.Format(r.FinalBookValue);
        _bookChart.Values = [r.Plan.Cost, .. r.Rows.Select(x => x.BookValue)];
        var rows = r.Rows.Select(x => new TableRowData(
            [Periods.Whole(x.Year), Money.Plain(x.Depreciation), Money.Plain(x.AccumulatedDepreciation), Money.Plain(x.BookValue)],
            Name: $"Year {x.Year}: depreciation {Money.Plain(x.Depreciation)}, accumulated {Money.Plain(x.AccumulatedDepreciation)}, book value {Money.Plain(x.BookValue)}")).ToList();
        _depTable.SetRows(rows, new TableRowData(["TOTAL", Money.Plain(r.Total), "", Money.Plain(r.FinalBookValue)], TableRowKind.Total,
            Name: $"Total depreciation {Money.Plain(r.Total)}, book value at the end {Money.Plain(r.FinalBookValue)}"));
    }

    private void AddDepreciationCard()
    {
        if (_depreciation?.Value is { } r) AddCard(FinanceCards.Depreciation(r));
        else _services.ShowStatus(_depErrors.Text ?? "Fix the fields first");
    }
}
