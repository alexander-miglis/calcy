using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Calcy.UI.Controls;

using Calcy.Units;

namespace Calcy.Modes.Convert;

internal sealed partial class ConvertView
{
    private static readonly string[] StepNames = ["days", "weeks", "months", "years"];

    private TextBox _d1Box = null!;
    private TextBox _d2Box = null!;
    private TextBlock _daysValue = null!;
    private TextBlock _weeksValue = null!;
    private TextBlock _weekdaysValue = null!;
    private TextBlock _ymdValue = null!;
    private TextBlock _d1Info = null!;
    private TextBlock _d2Info = null!;
    private TextBlock _addBase = null!;
    private PixelToggle _addPlus = null!;
    private PixelToggle _addMinus = null!;
    private TextBox _addAmount = null!;
    private ComboBox _addStep = null!;
    private TextBlock _addResult = null!;
    private TextBox _dur1 = null!;
    private TextBox _dur2 = null!;
    private PixelToggle _durPlus = null!;
    private PixelToggle _durMinus = null!;
    private TextBlock _durResult = null!;
    private TextBlock _durWords = null!;
    private PixelButton _saveDays = null!;
    private PixelButton _saveAdd = null!;
    private PixelButton _saveDuration = null!;

    private Control BuildDatesPage()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var christmas = new DateOnly(today.Year, 12, 25);
        if (christmas <= today) christmas = christmas.AddYears(1);

        // Days between dates
        _d1Box = DateBox("DateFrom", "Start date", DateTools.Format(today));
        _d2Box = DateBox("DateTo", "End date", DateTools.Format(christmas));
        OnKeys(_d1Box, (_, e) => OnDateKey(_d1Box, e, SaveDaysCard));
        OnKeys(_d2Box, (_, e) => OnDateKey(_d2Box, e, SaveDaysCard));
        var todayButton = new PixelButton { Content = "TODAY", Classes = { "card" }, MinHeight = 48, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8), Name = "Today" };
        ToolTip.SetTip(todayButton, "Start from today");
        todayButton.Click += (_, _) => _d1Box.Text = DateTools.Format(DateOnly.FromDateTime(DateTime.Today));
        var dates = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Field("From", _d1Box),
                Field("To", _d2Box),
                todayButton,
            },
        };

        _daysValue = new TextBlock { Classes = { "tilevalue" }, FontSize = 26, Name = "Days" };
        _weeksValue = new TextBlock { Classes = { "tilevalue" }, Name = "WeeksDays" };
        _weekdaysValue = new TextBlock { Classes = { "tilevalue" }, Name = "Weekdays" };
        _ymdValue = new TextBlock { Classes = { "tilevalue" }, Name = "YearsMonthsDays" };
        _d1Info = new TextBlock { Classes = { "tilevalue" }, FontSize = 17 };
        _d2Info = new TextBlock { Classes = { "tilevalue" }, FontSize = 17 };
        var tiles = Tiles(Tile("Days", _daysValue, selected: true), Tile("Weeks + days", _weeksValue), Tile("Weekdays", _weekdaysValue));
        var tiles2 = Tiles(Tile("Years, months, days", _ymdValue), Tile("From falls on", _d1Info), Tile("To falls on", _d2Info));

        _saveDays = SaveButton("SaveDays", SaveDaysCard);

        // Add or subtract
        _addBase = new TextBlock { Classes = { "mathline" } };
        (_addPlus, _addMinus) = SignToggles("Add", "Subtract", RecalculateDates);
        _addAmount = new TextBox { Classes = { "date" }, Text = "100", Width = 96, TextAlignment = Avalonia.Media.TextAlignment.Right, Name = "AddAmount" };
        AutomationProperties.SetName(_addAmount, "Amount to add");
        _addAmount.TextChanged += (_, _) => RecalculateDates();
        OnKeys(_addAmount, (_, e) => OnEnter(e, SaveAddCard));
        _addStep = new ComboBox { Classes = { "unit" }, ItemsSource = StepNames, SelectedIndex = 0, MinWidth = 120, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Left, Name = "AddStep" };
        AutomationProperties.SetName(_addStep, "Days, weeks, months or years");
        _addStep.SelectionChanged += (_, _) => RecalculateDates();
        _addResult = new TextBlock { Classes = { "mathline" }, FontWeight = Avalonia.Media.FontWeight.Bold, Name = "AddResult" };
        var addRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 10,
            LineSpacing = 10,
            Children =
            {
                Centered(_addBase),
                Pair(_addPlus, _addMinus),
                _addAmount,
                _addStep,
                Centered(new TextBlock { Text = "=", Classes = { "mathline" } }),
                new Border { Classes = { "chip" }, Child = _addResult },
            },
        };
        _saveAdd = SaveButton("SaveAdd", SaveAddCard);

        // Durations
        _dur1 = new TextBox { Classes = { "date" }, Text = "1:30:00", Width = 150, Name = "Duration1" };
        _dur2 = new TextBox { Classes = { "date" }, Text = "0:45:30", Width = 150, Name = "Duration2" };
        AutomationProperties.SetName(_dur1, "First duration");
        AutomationProperties.SetName(_dur2, "Second duration");
        foreach (var box in new[] { _dur1, _dur2 })
        {
            box.TextChanged += (_, _) => RecalculateDates();
            OnKeys(box, (_, e) => OnEnter(e, SaveDurationCard));
        }
        (_durPlus, _durMinus) = SignToggles("Add durations", "Subtract durations", RecalculateDates);
        _durResult = new TextBlock { Classes = { "mathline" }, FontWeight = Avalonia.Media.FontWeight.Bold, Name = "DurationResult" };
        _durWords = new TextBlock { Classes = { "formula" }, VerticalAlignment = VerticalAlignment.Center };
        var durRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 10,
            LineSpacing = 10,
            Children =
            {
                _dur1,
                Pair(_durPlus, _durMinus),
                _dur2,
                Centered(new TextBlock { Text = "=", Classes = { "mathline" } }),
                new Border { Classes = { "chip" }, Child = _durResult },
                Centered(_durWords),
            },
        };
        _saveDuration = SaveButton("SaveDuration", SaveDurationCard);

        _d1Box.TextChanged += (_, _) => RecalculateDates();
        _d2Box.TextChanged += (_, _) => RecalculateDates();

        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Section("Days between dates", _saveDays),
                dates,
                tiles,
                tiles2,
                Rule(),
                Section("Add or subtract", _saveAdd),
                addRow,
                Rule(),
                Section("Time durations", _saveDuration),
                durRow,
                new TextBlock
                {
                    Classes = { "hint" },
                    Text = "Dates are year-month-day (2026-10-13). Up and Down change the day, Page Up and Page Down the month. " +
                           "Durations are h:mm:ss or units such as 1h 30m. Enter saves a card.",
                },
            },
        };
        return new Border { Classes = { "panel" }, Padding = new Thickness(18), Child = body, VerticalAlignment = VerticalAlignment.Top };
    }

    private static TextBox DateBox(string name, string label, string text)
    {
        var box = new TextBox { Classes = { "date" }, Text = text, Width = 200, Name = name };
        AutomationProperties.SetName(box, label);
        ToolTip.SetTip(box, "yyyy-mm-dd, or today. Up and Down change the day.");
        return box;
    }

    private static Control Field(string label, Control input) => new StackPanel
    {
        Spacing = 4,
        Margin = new Thickness(0, 0, 14, 8),
        Children = { new TextBlock { Text = label.ToUpperInvariant(), Classes = { "label", "ink" }, FontSize = 11 }, input },
    };

    private static Border Tile(string label, TextBlock value, bool selected = false)
    {
        var tile = new Border
        {
            Classes = { "tile" },
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { new TextBlock { Text = label.ToUpperInvariant(), Classes = { "label", "ink" }, FontSize = 11 }, value },
            },
        };
        if (selected) tile.Classes.Add("sel");
        return tile;
    }

    private static Grid Tiles(params Control[] tiles)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
        for (int i = 0; i < tiles.Length; i++)
        {
            Grid.SetColumn(tiles[i], i);
            grid.Children.Add(tiles[i]);
        }
        return grid;
    }

    private static Control Section(string title, Control action)
    {
        DockPanel.SetDock(action, Dock.Right);
        return new DockPanel
        {
            Children =
            {
                action,
                new TextBlock { Text = title.ToUpperInvariant(), Classes = { "label", "ink" }, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }

    private Border Rule()
    {
        var rule = new Border { Height = 2, Margin = new Thickness(0, 4) };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Calcy.Key"));
        return rule;
    }

    private static Control Centered(Control c)
    {
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    private static Control Pair(Control a, Control b) => new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { a, b } };

    private static (PixelToggle Plus, PixelToggle Minus) SignToggles(string plusName, string minusName, Action changed)
    {
        var plus = new PixelToggle { Content = "+", IsChecked = true, MinWidth = 44, MinHeight = 48, FontSize = 18, Margin = new Thickness(0, 0, -2, 0) };
        var minus = new PixelToggle { Content = "-", MinWidth = 44, MinHeight = 48, FontSize = 18 };
        AutomationProperties.SetName(plus, plusName);
        AutomationProperties.SetName(minus, minusName);
        plus.Click += (_, _) => { plus.IsChecked = true; minus.IsChecked = false; changed(); };
        minus.Click += (_, _) => { minus.IsChecked = true; plus.IsChecked = false; changed(); };
        return (plus, minus);
    }

    private static PixelButton SaveButton(string name, Action save)
    {
        var b = new PixelButton { Content = "SAVE AS CARD", Classes = { "primary", "small" }, Name = name };
        ToolTip.SetTip(b, "Keep this result as a card (Enter)");
        b.Click += (_, _) => save();
        return b;
    }

    private static void OnEnter(KeyEventArgs e, Action action)
    {
        if (e.Key != Key.Enter) return;
        action();
        e.Handled = true;
    }

    /// <summary>Enter saves; Up and Down move the date by a day, Page Up and Page Down by a month.</summary>
    private static void OnDateKey(TextBox box, KeyEventArgs e, Action save)
    {
        if (e.Key == Key.Enter)
        {
            save();
            e.Handled = true;
            return;
        }
        var (amount, step) = e.Key switch
        {
            Key.Up => (1, DateStep.Days),
            Key.Down => (-1, DateStep.Days),
            Key.PageUp => (1, DateStep.Months),
            Key.PageDown => (-1, DateStep.Months),
            _ => (0, DateStep.Days),
        };
        if (amount == 0 || !DateTools.TryParseDate(box.Text, out var d) || !DateTools.TryAdd(d, amount, step, out var moved)) return;
        box.Text = DateTools.Format(moved);
        box.CaretIndex = box.Text.Length;
        e.Handled = true;
    }

    // ---- Calculation ----

    private bool TryDates(out DateOnly from, out DateOnly to)
    {
        bool ok1 = DateTools.TryParseDate(_d1Box.Text, out from);
        bool ok2 = DateTools.TryParseDate(_d2Box.Text, out to);
        _d1Box.Classes.Set("error", !ok1);
        _d2Box.Classes.Set("error", !ok2);
        return ok1 && ok2;
    }

    private bool TryAddRequest(out DateOnly start, out int amount, out DateStep step, out DateOnly result)
    {
        step = (DateStep)Math.Max(0, _addStep.SelectedIndex);
        result = default;
        amount = 0;
        bool okDate = DateTools.TryParseDate(_d1Box.Text, out start);
        bool okAmount = int.TryParse(_addAmount.Text?.Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out amount);
        _addAmount.Classes.Set("error", !okAmount);
        if (!okDate || !okAmount) return false;
        if (_addMinus.IsChecked == true) amount = -amount;
        return DateTools.TryAdd(start, amount, step, out result);
    }

    private bool TryDurations(out TimeSpan a, out TimeSpan b, out TimeSpan result)
    {
        bool ok1 = DateTools.TryParseDuration(_dur1.Text, out a);
        bool ok2 = DateTools.TryParseDuration(_dur2.Text, out b);
        _dur1.Classes.Set("error", !ok1);
        _dur2.Classes.Set("error", !ok2);
        result = ok1 && ok2 ? DateTools.Combine(a, b, _durMinus.IsChecked == true) : TimeSpan.Zero;
        return ok1 && ok2;
    }

    /// <summary>Refreshes every date tool from its inputs.</summary>
    internal void RecalculateDates()
    {
        if (_d1Box is null || _addStep is null || _durMinus is null) return;
        if (TryDates(out var from, out var to))
        {
            int days = DateTools.DaysBetween(from, to);
            var (w, d) = DateTools.WeeksAndDays(days);
            _daysValue.Text = days.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _weeksValue.Text = days < 0 ? $"-{-w} w {-d} d" : $"{w} w {d} d";
            _weekdaysValue.Text = DateTools.Weekdays(from, to).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ymdValue.Text = DateTools.YearsMonthsDays(from, to).ToString();
            _d1Info.Text = Describe(from);
            _d2Info.Text = Describe(to);
            _saveDays.IsEnabled = true;
        }
        else
        {
            foreach (var t in new[] { _daysValue, _weeksValue, _weekdaysValue, _ymdValue, _d1Info, _d2Info }) t.Text = "?";
            _saveDays.IsEnabled = false;
        }

        _addBase.Text = DateTools.TryParseDate(_d1Box.Text, out var start) ? DateTools.Format(start) : "?";
        if (TryAddRequest(out _, out _, out _, out var added))
        {
            _addResult.Text = $"{DateTools.Format(added)} ({DateTools.DayName(added)})";
            _saveAdd.IsEnabled = true;
        }
        else
        {
            _addResult.Text = "?";
            _saveAdd.IsEnabled = false;
        }

        if (TryDurations(out _, out _, out var sum))
        {
            _durResult.Text = DateTools.FormatDuration(sum);
            _durWords.Text = DateTools.DescribeDuration(sum);
            _saveDuration.IsEnabled = true;
        }
        else
        {
            _durResult.Text = "?";
            _durWords.Text = "h:mm:ss, or 1h 30m";
            _saveDuration.IsEnabled = false;
        }
    }

    private static string Describe(DateOnly date) => $"{DateTools.DayName(date)}, W{DateTools.IsoWeek(date).Week:D2}";

    // ---- Requests and cards ----

    private void ApplyDateRequest(DateRequest request)
    {
        switch (request)
        {
            case DateRequest.Between b:
                _d1Box.Text = DateTools.Format(b.From);
                _d2Box.Text = DateTools.Format(b.To);
                break;
            case DateRequest.Shift s:
                _d1Box.Text = DateTools.Format(s.Date);
                _addAmount.Text = Math.Abs(s.Amount).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _addPlus.IsChecked = s.Amount >= 0;
                _addMinus.IsChecked = s.Amount < 0;
                _addStep.SelectedIndex = (int)s.Step;
                break;
            case DateRequest.Durations d:
                _dur1.Text = DateTools.FormatDuration(d.First);
                _dur2.Text = DateTools.FormatDuration(d.Second);
                _durPlus.IsChecked = !d.Subtract;
                _durMinus.IsChecked = d.Subtract;
                break;
        }
        RecalculateDates();
    }

    private void SaveDateCard(DateRequest request)
    {
        switch (request)
        {
            case DateRequest.Between: SaveDaysCard(); break;
            case DateRequest.Shift: SaveAddCard(); break;
            case DateRequest.Durations: SaveDurationCard(); break;
        }
    }

    /// <summary>Saves the days between the two dates as a card.</summary>
    internal void SaveDaysCard()
    {
        if (!TryDates(out var from, out var to)) return;
        int days = DateTools.DaysBetween(from, to);
        var (w, d) = DateTools.WeeksAndDays(Math.Abs(days));
        var span = $"{DateTools.Format(from)} to {DateTools.Format(to)}";
        var unit = Math.Abs(days) == 1 ? "day" : "days";
        AddCard(new ConvertCard("Dates", $"{span} = {days} {unit} ({(days < 0 ? "-" : "")}{w} w {d} d)", span));
    }

    /// <summary>Saves the add or subtract result as a card.</summary>
    internal void SaveAddCard()
    {
        if (!TryAddRequest(out var start, out int amount, out var step, out var result)) return;
        var name = StepNames[(int)step];
        if (Math.Abs(amount) == 1) name = name[..^1];
        var request = $"{DateTools.Format(start)} {(amount < 0 ? "-" : "+")} {Math.Abs(amount)} {name}";
        AddCard(new ConvertCard("Dates", $"{request} = {DateTools.Format(result)} ({DateTools.DayName(result)})", request));
    }

    /// <summary>Saves the duration sum or difference as a card.</summary>
    internal void SaveDurationCard()
    {
        if (!TryDurations(out var a, out var b, out var result)) return;
        var request = $"{DateTools.FormatDuration(a)} {(_durMinus.IsChecked == true ? "-" : "+")} {DateTools.FormatDuration(b)}";
        AddCard(new ConvertCard("Time", $"{request} = {DateTools.FormatDuration(result)}", request));
    }
}
