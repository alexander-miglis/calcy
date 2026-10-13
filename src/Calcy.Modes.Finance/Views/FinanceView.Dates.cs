using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Finance.Logic;

namespace Calcy.Modes.Finance.Views;

/// <summary>The Dates tab: days between two dates (actual, 30/360 US and European) and adding days to a date.</summary>
public sealed partial class FinanceView
{
    private TextBox _startBox = null!;
    private TextBox _endBox = null!;
    private TextBox _fromDate = null!;
    private TextBox _addDays = null!;
    private TextBlock _actual = null!;
    private TextBlock _actualSub = null!;
    private TextBlock _us360 = null!;
    private TextBlock _eu360 = null!;
    private TextBlock _years365 = null!;
    private TextBlock _dateErrors = null!;
    private TextBlock _shifted = null!;
    private TextBlock _shiftErrors = null!;
    private Answer<DayCount>? _dayCount;
    private Answer<DateShift>? _shift;

    /// <summary>The actual day count on screen (for tests).</summary>
    internal string ActualDaysText => _actual.Text ?? "";

    private Control BuildDatesPage()
    {
        var d = _state.Dates;
        var page = new StackPanel();

        var between = new StackPanel { Spacing = 12 };
        between.Children.Add(Title("Days between dates", "Type dates like 2026-10-13, or today."));
        _startBox = DateBox("From date", d.Start);
        _endBox = DateBox("To date", d.End);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, box) in new[] { ("From", _startBox), ("To", _endBox) })
        {
            var l = Ui.Labelled(label, box);
            l.Margin = new Thickness(0, 0, 14, 8);
            row.Children.Add(l);
        }
        between.Children.Add(row);
        _dateErrors = Ui.Text("", "error");
        _dateErrors.IsVisible = false;
        between.Children.Add(_dateErrors);
        var (actualTile, actual, actualSub) = Ui.Tile("Actual days", selected: true);
        var (usTile, us, _) = Ui.Tile("30/360 US");
        var (euTile, eu, _) = Ui.Tile("30/360 European");
        var (yearTile, years, _) = Ui.Tile("Years (actual/365)");
        (_actual, _actualSub, _us360, _eu360, _years365) = (actual, actualSub, us, eu, years);
        ToolTip.SetTip(actualTile, "Calendar days, as the TI dbd function counts them");
        ToolTip.SetTip(usTile, "Every month counts as 30 days (US rule: the 31st becomes the 30th; an end date of the 31st only when the start is the 30th or 31st)");
        ToolTip.SetTip(euTile, "Every month counts as 30 days (European rule: any 31st becomes the 30th)");
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*,10,*") };
        int col = 0;
        foreach (var t in new[] { actualTile, usTile, euTile, yearTile })
        {
            Grid.SetColumn(t, col);
            col += 2;
            tiles.Children.Add(t);
        }
        between.Children.Add(tiles);
        between.Children.Add(Toolbar(AddCardButton("days between dates", AddDaysBetweenCard)));
        Field(_startBox, AddDaysBetweenCard, FinanceTab.Dates);
        Field(_endBox, AddDaysBetweenCard, FinanceTab.Dates);
        page.Children.Add(Section(between));

        var add = new StackPanel { Spacing = 12 };
        add.Children.Add(Title("Add days to a date", "Use a negative number to go back."));
        _fromDate = DateBox("Start date", d.From);
        _addDays = Ui.Input("Days to add", "Days to add (negative to go back); expressions like 6*30 work", 120);
        _addDays.Text = d.Days;
        _shifted = Ui.Text("?", "mathline");
        _shifted.FontWeight = Avalonia.Media.FontWeight.Bold;
        AutomationProperties.SetLiveSetting(_shifted, AutomationLiveSetting.Polite);
        var resultBox = new Border { Child = _shifted, VerticalAlignment = VerticalAlignment.Bottom };
        resultBox.Classes.Add("box");
        resultBox.Classes.Add("sel");
        // Labels sit above the inputs, so the + and = signs line up with the boxes at the bottom of the row.
        var line = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 14 };
        foreach (var c in new Control[] { Ui.Labelled("Date", _fromDate), Words("+"), Ui.Labelled("Days", _addDays), Words("="), resultBox })
        {
            c.VerticalAlignment = VerticalAlignment.Bottom;
            c.Margin = c is TextBlock ? new Thickness(0, 0, 0, 10) : default;
            line.Children.Add(c);
        }
        add.Children.Add(line);
        _shiftErrors = Ui.Text("", "error");
        _shiftErrors.IsVisible = false;
        add.Children.Add(_shiftErrors);
        add.Children.Add(Toolbar(AddCardButton("date plus days", AddShiftCard)));
        Field(_fromDate, AddShiftCard, FinanceTab.Dates);
        Field(_addDays, AddShiftCard, FinanceTab.Dates);
        Ui.OnText(_addDays, () => OnDatesEdited());
        page.Children.Add(add);
        return Page(page);
    }

    private TextBox DateBox(string name, string text)
    {
        var box = Ui.Input(name, "A date like 2026-10-13, or today", 180);
        box.Classes.Add("left");
        box.Text = text;
        Ui.OnText(box, () => OnDatesEdited());
        return box;
    }

    private void OnDatesEdited()
    {
        if (_loading || _addDays is null || _fromDate is null) return;
        var d = _state.Dates;
        d.Start = _startBox.Text ?? "";
        d.End = _endBox.Text ?? "";
        d.From = _fromDate.Text ?? "";
        d.Days = _addDays.Text ?? "";
        RecomputeDates();
        SaveSoon();
    }

    private void RecomputeDates()
    {
        if (_loading || _shifted is null) return;
        _dayCount = DateCalculator.Count(_state.Dates);
        Ui.SetError(_startBox, _dayCount.FieldErrors.GetValueOrDefault("start"), "A date like 2026-10-13, or today");
        Ui.SetError(_endBox, _dayCount.FieldErrors.GetValueOrDefault("end"), "A date like 2026-10-13, or today");
        Ui.Show(_dateErrors, _dayCount.FieldErrors.Count > 0 ? string.Join(" ", _dayCount.FieldErrors.Values) : null);
        if (_dayCount.Value is { } c)
        {
            _actual.Text = Periods.Count(c.Actual, "day");
            int weeks = Math.Abs(c.Actual) / 7, rest = Math.Abs(c.Actual) % 7;
            Ui.Show(_actualSub, weeks == 0 ? null : $"{Periods.Count(weeks, "week")}" + (rest == 0 ? "" : $" {Periods.Count(rest, "day")}"));
            _us360.Text = Periods.Count(c.Thirty360US, "day");
            _eu360.Text = Periods.Count(c.Thirty360European, "day");
            _years365.Text = Money.Number(c.Years365, 4);
        }
        else
        {
            _actual.Text = _us360.Text = _eu360.Text = _years365.Text = "?";
            Ui.Show(_actualSub, null);
        }

        _shift = DateCalculator.Add(_state.Dates, _reader);
        Ui.SetError(_fromDate, _shift.FieldErrors.GetValueOrDefault("from"), "A date like 2026-10-13, or today");
        Ui.SetError(_addDays, _shift.FieldErrors.GetValueOrDefault("days"), "Days to add (negative to go back)");
        Ui.Show(_shiftErrors, _shift.FieldErrors.Count > 0 ? string.Join(" ", _shift.FieldErrors.Values) : _shift.Problem);
        _shifted.Text = _shift.Value is { } s ? DateCalculator.Show(s.Result) : "?";
    }

    private void AddDaysBetweenCard()
    {
        if (_dayCount?.Value is { } c) AddCard(FinanceCards.DaysBetween(c));
        else _services.ShowStatus(_dateErrors.Text ?? "Fix the dates first");
    }

    private void AddShiftCard()
    {
        if (_shift?.Value is { } s) AddCard(FinanceCards.AddDays(s));
        else _services.ShowStatus(_shiftErrors.Text ?? "Fix the date first");
    }
}
