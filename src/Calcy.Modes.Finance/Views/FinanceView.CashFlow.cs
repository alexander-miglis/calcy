using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Finance.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>The Cash flow tab: CF0 and CFj with frequencies, NPV at a rate, IRR, MIRR and payback.</summary>
public sealed partial class FinanceView
{
    private readonly List<(TextBox Amount, TextBox Count)> _flowBoxes = [];
    private WrapPanel _flowPanel = null!;
    private TextBox _cf0Box = null!;
    private TextBox _rateBox = null!;
    private TextBox _financeBox = null!;
    private TextBox _reinvestBox = null!;
    private TextBlock _flowErrors = null!;
    private TextBlock _npv = null!;
    private TextBlock _npvSub = null!;
    private TextBlock _irr = null!;
    private TextBlock _irrSub = null!;
    private TextBlock _mirr = null!;
    private TextBlock _mirrSub = null!;
    private TextBlock _payback = null!;
    private TextBlock _paybackSub = null!;
    private CashFlowChart _flowChart = null!;
    private PixelButton _addFlow = null!;
    private PixelButton _removeFlow = null!;

    /// <summary>The current cash flow answers (null when they cannot be worked out).</summary>
    internal CashFlowResult? CashFlowAnswer { get; private set; }

    /// <summary>The amount box of CF0 (index 0) or CFj (for tests).</summary>
    internal TextBox FlowBox(int j) => j == 0 ? _cf0Box : _flowBoxes[j - 1].Amount;

    private Control BuildCashFlowPage()
    {
        var page = new StackPanel { Spacing = 12 };
        page.Children.Add(Title("Cash flows", "Money out is negative, money in is positive. ×N repeats a flow N periods in a row."));

        _flowPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        _cf0Box = Ui.Input("CF0, the starting amount", "CF0: the amount at the start, usually the investment (negative)", 112);
        _cf0Box.Text = _state.CashFlow.Initial;
        Field(_cf0Box, AddCashFlowCard, FinanceTab.CashFlow);
        Ui.OnText(_cf0Box, () => OnFlowsEdited());
        _addFlow = Ui.Small("+", "Add a cash flow", "Add a cash flow at the end");
        _addFlow.Width = 36;
        _addFlow.Click += (_, _) => AddFlow(focus: true);
        _removeFlow = Ui.Small("-", "Remove the last cash flow", "Remove the last cash flow");
        _removeFlow.Width = 36;
        _removeFlow.Click += (_, _) => RemoveFlow();
        foreach (var f in _state.CashFlow.Flows) AddFlowBoxes(f);
        RebuildFlowPanel();
        page.Children.Add(_flowPanel);

        _rateBox = Ui.Input("Discount rate in percent per period", "The rate per period in percent, used for NPV and the discounted payback", 96);
        _financeBox = Ui.Input("Finance rate for MIRR in percent", "MIRR: the rate paid on money borrowed (blank: the discount rate)", 96);
        _reinvestBox = Ui.Input("Reinvestment rate for MIRR in percent", "MIRR: the rate earned on money reinvested (blank: the discount rate)", 96);
        _financeBox.PlaceholderText = "= rate";
        _reinvestBox.PlaceholderText = "= rate";
        _rateBox.Text = _state.CashFlow.Rate;
        _financeBox.Text = _state.CashFlow.FinanceRate;
        _reinvestBox.Text = _state.CashFlow.ReinvestRate;
        foreach (var b in new[] { _rateBox, _financeBox, _reinvestBox })
        {
            Field(b, AddCashFlowCard, FinanceTab.CashFlow);
            Ui.OnText(b, () => OnFlowsEdited());
        }
        var rates = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, box) in new[] { ("Rate %", _rateBox), ("Finance %", _financeBox), ("Reinvest %", _reinvestBox) })
        {
            var l = Ui.Labelled(label, box);
            l.Margin = new Thickness(0, 0, 14, 0);
            rates.Children.Add(l);
        }
        page.Children.Add(rates);
        _flowErrors = Ui.Text("", "error");
        _flowErrors.IsVisible = false;
        page.Children.Add(_flowErrors);

        var (npvTile, npv, npvSub) = Ui.Tile("NPV", selected: true);
        var (irrTile, irr, irrSub) = Ui.Tile("IRR");
        var (mirrTile, mirr, mirrSub) = Ui.Tile("MIRR");
        var (pbTile, pb, pbSub) = Ui.Tile("Payback");
        (_npv, _npvSub, _irr, _irrSub, _mirr, _mirrSub, _payback, _paybackSub) = (npv, npvSub, irr, irrSub, mirr, mirrSub, pb, pbSub);
        ToolTip.SetTip(npvTile, "Net present value: every flow brought back to today at the rate. Positive means the investment beats the rate.");
        ToolTip.SetTip(irrTile, "Internal rate of return: the rate at which the NPV is zero.");
        ToolTip.SetTip(mirrTile, "Modified IRR: money out is financed at the finance rate and money in reinvested at the reinvestment rate.");
        ToolTip.SetTip(pbTile, "Payback: how many periods until the flows add up to zero (discounted: at the rate).");
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*,10,*") };
        int col = 0;
        foreach (var t in new[] { npvTile, irrTile, mirrTile, pbTile })
        {
            Grid.SetColumn(t, col);
            col += 2;
            tiles.Children.Add(t);
        }
        page.Children.Add(tiles);

        _flowChart = new CashFlowChart { Height = 150 };
        AutomationProperties.SetName(_flowChart, "Cash flow bars: money in above the line, money out below");
        var chart = new Border { Child = _flowChart };
        chart.Classes.Add("chart");
        page.Children.Add(chart);

        page.Children.Add(Toolbar(ExportButton("the cash flows", () => CashFlowAnswer is { } r ? CashFlowWorksheet.ToCsv(r) : null),
            AddCardButton("cash flows", AddCashFlowCard)));
        return Page(page);
    }

    private void AddFlowBoxes(CashFlowEntry entry)
    {
        int j = _flowBoxes.Count + 1;
        string name = "CF" + j.ToString(CultureInfo.InvariantCulture);
        var amount = Ui.Input(name + " amount", name + ": the amount in period " + j, 112);
        var count = Ui.Input(name + " repeats", $"How many periods in a row {name} repeats (1 if it happens once)", 76, small: true);
        count.PlaceholderText = "1";
        amount.Text = entry.Amount;
        count.Text = entry.Count == "1" ? "" : entry.Count;
        Field(amount, AddCashFlowCard, FinanceTab.CashFlow);
        Field(count, AddCashFlowCard, FinanceTab.CashFlow);
        Ui.OnText(amount, () => OnFlowsEdited());
        Ui.OnText(count, () => OnFlowsEdited());
        _flowBoxes.Add((amount, count));
    }

    private void RebuildFlowPanel()
    {
        _flowPanel.Children.Clear();
        _flowPanel.Children.Add(FlowCell("CF0", _cf0Box, null));
        for (int j = 0; j < _flowBoxes.Count; j++)
            _flowPanel.Children.Add(FlowCell("CF" + (j + 1).ToString(CultureInfo.InvariantCulture), _flowBoxes[j].Amount, _flowBoxes[j].Count));
        var buttons = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 17, 0, 0), Children = { _addFlow, _removeFlow } };
        _flowPanel.Children.Add(buttons);
        _addFlow.IsEnabled = _flowBoxes.Count < CashFlowWorksheet.MaxFlows;
        _removeFlow.IsEnabled = _flowBoxes.Count > 1;
    }

    private static Control FlowCell(string label, TextBox amount, TextBox? count)
    {
        var cell = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 10, 10), Children = { Ui.Text(label, "cap"), amount } };
        if (count is not null)
        {
            var times = Ui.Text("×", "mathline");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { times, count } };
            cell.Children.Add(row);
        }
        return cell;
    }

    private void AddFlow(bool focus)
    {
        if (_flowBoxes.Count >= CashFlowWorksheet.MaxFlows) return;
        AddFlowBoxes(new CashFlowEntry());
        RebuildFlowPanel();
        OnFlowsEdited();
        if (focus) FocusField(_flowBoxes[^1].Amount);
    }

    private void RemoveFlow()
    {
        if (_flowBoxes.Count <= 1) return;
        var (amount, count) = _flowBoxes[^1];
        _primaryActions.Remove(amount);
        _primaryActions.Remove(count);
        if (_lastField == amount || _lastField == count) _lastField = null;
        _flowBoxes.RemoveAt(_flowBoxes.Count - 1);
        RebuildFlowPanel();
        OnFlowsEdited();
    }

    private void OnFlowsEdited()
    {
        if (_loading || _rateBox is null || _reinvestBox is null || _financeBox is null) return;
        _state.CashFlow.Initial = _cf0Box.Text ?? "";
        _state.CashFlow.Flows = [.. _flowBoxes.Select(b => new CashFlowEntry { Amount = b.Amount.Text ?? "", Count = string.IsNullOrWhiteSpace(b.Count.Text) ? "1" : b.Count.Text! })];
        _state.CashFlow.Rate = _rateBox.Text ?? "";
        _state.CashFlow.FinanceRate = _financeBox.Text ?? "";
        _state.CashFlow.ReinvestRate = _reinvestBox.Text ?? "";
        RecomputeCashFlow();
        SaveSoon();
    }

    private void RecomputeCashFlow()
    {
        if (_loading || _npv is null) return;
        var o = CashFlowWorksheet.Evaluate(_state.CashFlow, _reader);
        CashFlowAnswer = o.Result;
        var e = o.FieldErrors;
        Ui.SetError(_cf0Box, e.GetValueOrDefault(CashFlowWorksheet.InitialKey), "CF0: the amount at the start, usually the investment (negative)");
        for (int j = 0; j < _flowBoxes.Count; j++)
        {
            Ui.SetError(_flowBoxes[j].Amount, e.GetValueOrDefault(CashFlowWorksheet.AmountKey(j + 1)), $"CF{j + 1}: the amount in period {j + 1}");
            Ui.SetError(_flowBoxes[j].Count, e.GetValueOrDefault(CashFlowWorksheet.CountKey(j + 1)), "How many periods in a row it repeats");
        }
        Ui.SetError(_rateBox, e.GetValueOrDefault(CashFlowWorksheet.RateKey), "The rate per period in percent");
        Ui.SetError(_financeBox, e.GetValueOrDefault(CashFlowWorksheet.FinanceKey), "MIRR finance rate (blank: the discount rate)");
        Ui.SetError(_reinvestBox, e.GetValueOrDefault(CashFlowWorksheet.ReinvestKey), "MIRR reinvestment rate (blank: the discount rate)");
        Ui.Show(_flowErrors, e.Count > 0 ? string.Join(" ", e.Values) : o.Problem);

        if (o.Result is not { } r)
        {
            _npv.Text = _irr.Text = _mirr.Text = _payback.Text = "?";
            foreach (var s in new[] { _npvSub, _irrSub, _mirrSub, _paybackSub }) Ui.Show(s, null);
            _flowChart.Values = [];
            return;
        }
        _npv.Text = Money.Format(r.Npv);
        Ui.Show(_npvSub, "at " + Money.Percent(r.Rate, 2) + (r.Npv >= 0 ? ": worth it" : ": below the rate"));
        _irr.Text = r.Irr is { } irr ? Money.Percent(irr, 2) : "none";
        Ui.Show(_irrSub, r.IrrNote);
        _mirr.Text = r.Mirr is { } mirr ? Money.Percent(mirr, 2) : "none";
        Ui.Show(_mirrSub, r.MirrNote ?? $"finance {Money.Number(r.FinanceRate, 2)} %, reinvest {Money.Number(r.ReinvestRate, 2)} %");
        _payback.Text = r.Payback is { } pb ? Money.Number(pb, 2) : "never";
        Ui.Show(_paybackSub, r.Payback is null
            ? "the flows never add up to zero"
            : "periods; discounted " + (r.DiscountedPayback is { } d ? Money.Number(d, 2) : "never"));
        _flowChart.Values = r.Expanded;
    }

    private void AddCashFlowCard()
    {
        if (CashFlowAnswer is not { } r)
        {
            _services.ShowStatus(_flowErrors.Text ?? "Fix the cash flows first");
            return;
        }
        AddCard(FinanceCards.CashFlow(r));
    }
}
