using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;
using Calcy.UI.Controls;

using Calcy.Units;

namespace Calcy.Modes.Convert;

internal sealed partial class ConvertView
{
    private TextBox _valueBox = null!;
    private ComboBox _fromBox = null!;
    private ComboBox _toBox = null!;
    private PixelButton _swap = null!;
    private TextBlock _resultText = null!;
    private TextBlock _formula = null!;
    private PixelButton _save = null!;
    private TextBlock _gridHeader = null!;
    private UniformGrid _grid = null!;
    private readonly List<(Unit Unit, Button Cell, TextBlock Value)> _cells = [];

    private UnitCategory? _category;
    private readonly List<Unit> _units = [];
    private List<Unit> _targets = [];
    private Unit _from = null!;
    private Unit _to = null!;
    private ConversionQuery? _query;
    private ConversionResult? _result;
    private bool _updating;
    private bool _suppressText;

    private Control BuildUnitsPage()
    {
        _valueBox = new TextBox { Classes = { "big" }, Text = _state.Value, Name = "FromValue" };
        AutomationProperties.SetName(_valueBox, "Value to convert");
        ToolTip.SetTip(_valueBox, "A number, or a whole conversion such as 5 km to mi or 3 ft 4 in to cm. Enter saves a card.");
        _valueBox.TextChanged += (_, _) =>
        {
            if (_suppressText) return;
            Recalculate();
        };
        OnKeys(_valueBox, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SaveCard();
                e.Handled = true;
            }
        });

        _fromBox = new ComboBox { Classes = { "unit" }, Name = "FromUnit" };
        AutomationProperties.SetName(_fromBox, "From unit");
        _fromBox.SelectionChanged += (_, _) => OnFromPicked();
        var fromColumn = new StackPanel { Spacing = 6, Children = { Label("From"), _valueBox, _fromBox } };

        var swapIcon = new PixelIcon { Kind = "swap", Width = 22, Height = 22 };
        swapIcon.Bind(PixelIcon.ForegroundProperty, swapIcon.GetResourceObservable("Calcy.Ink"));
        _swap = new PixelButton
        {
            Classes = { "sel" },
            Content = swapIcon,
            Width = 52,
            Height = 52,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 50),
            Name = "Swap",
        };
        AutomationProperties.SetName(_swap, "Swap units");
        ToolTip.SetTip(_swap, "Swap units");
        _swap.Click += (_, _) => Swap();

        _resultText = new TextBlock { Classes = { "big" }, Name = "Result", TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
        // A long result shrinks to fit on one line instead of breaking inside a number.
        var resultBox = new Border { Classes = { "result" }, Child = FitWidth(_resultText) };
        AutomationProperties.SetLiveSetting(_resultText, AutomationLiveSetting.Polite);
        _toBox = new ComboBox { Classes = { "unit" }, Name = "ToUnit" };
        AutomationProperties.SetName(_toBox, "To unit");
        _toBox.SelectionChanged += (_, _) => OnToPicked();
        var toColumn = new StackPanel { Spacing = 6, Children = { Label("To"), resultBox, _toBox } };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 14 };
        Grid.SetColumn(fromColumn, 0);
        Grid.SetColumn(_swap, 1);
        Grid.SetColumn(toColumn, 2);
        row.Children.Add(fromColumn);
        row.Children.Add(_swap);
        row.Children.Add(toColumn);

        _formula = new TextBlock { Classes = { "formula" }, VerticalAlignment = VerticalAlignment.Center, Name = "Formula" };
        _save = new PixelButton { Classes = { "primary" }, Content = "SAVE AS CARD", MinHeight = 44, Padding = new Thickness(14, 0), Name = "SaveCard" };
        ToolTip.SetTip(_save, "Keep this conversion as a card (Enter)");
        _save.Click += (_, _) => SaveCard();
        DockPanel.SetDock(_save, Dock.Right);
        var bottom = new DockPanel { Children = { _save, new Border { Child = _formula, Margin = new Thickness(0, 0, 12, 0) } } };

        var converter = new Border
        {
            Classes = { "panel" },
            Padding = new Thickness(18),
            Child = new StackPanel { Spacing = 16, Children = { row, bottom } },
        };

        _gridHeader = new TextBlock { Classes = { "label", "ink" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        _grid = new UniformGrid { Columns = 3, Name = "EveryUnit" };
        KeyboardNavigation.SetTabNavigation(_grid, KeyboardNavigationMode.Once);
        var every = new Border
        {
            Classes = { "panel" },
            Child = new StackPanel { Children = { new Border { Classes = { "strip" }, Child = _gridHeader }, _grid } },
        };

        return new StackPanel { Spacing = 16, Children = { converter, every }, VerticalAlignment = VerticalAlignment.Top };
    }

    /// <summary>Scales a single line of text down (never up) so it fits the available width.</summary>
    private static Viewbox FitWidth(TextBlock text)
    {
        text.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
        return new Viewbox
        {
            Child = text,
            Stretch = Avalonia.Media.Stretch.Uniform,
            StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ---- Category and unit lists ----

    /// <summary>Switches to a category, restoring its last units (or the defaults).</summary>
    private void SetCategory(UnitCategory category)
    {
        _category = category;
        _units.Clear();
        _units.AddRange(category.Units);
        Unit? from = null, to = null;
        if (_state.Units.TryGetValue(category.Id, out var saved) && saved.Length == 2)
        {
            from = _catalog.GetById(saved[0]);
            to = _catalog.GetById(saved[1]);
        }
        from ??= category.DefaultFrom;
        if (to is null || !to.IsCompatibleWith(from)) to = from.Equals(category.DefaultFrom) ? category.DefaultTo : DefaultTarget(from);
        EnsureListed(from);
        EnsureListed(to);
        _from = from;
        _to = to;
        RefreshUnitBoxes();
    }

    /// <summary>A sensible target for a unit: the category default when compatible, else the first other compatible unit.</summary>
    private Unit DefaultTarget(Unit from)
    {
        var category = _category ?? _catalog.CategoryOf(from)!;
        if (category.DefaultTo.IsCompatibleWith(from) && !category.DefaultTo.Equals(from)) return category.DefaultTo;
        if (category.DefaultFrom.IsCompatibleWith(from) && !category.DefaultFrom.Equals(from)) return category.DefaultFrom;
        return _units.FirstOrDefault(u => u.IsCompatibleWith(from) && !u.Equals(from)) ?? from;
    }

    /// <summary>Adds a unit that is not listed in the category (a prefixed or compound unit from typed input).</summary>
    private void EnsureListed(Unit unit)
    {
        if (!_units.Contains(unit)) _units.Add(unit);
    }

    /// <summary>Refills both unit boxes and the grid from the current state.</summary>
    private void RefreshUnitBoxes()
    {
        _updating = true;
        _fromBox.ItemsSource = _units.Select(u => $"{u.Symbol}  {u.Name}").ToList();
        _fromBox.SelectedIndex = _units.IndexOf(_from);
        _targets = [.. _units.Where(u => u.IsCompatibleWith(_from))];
        _toBox.ItemsSource = _targets.Select(u => $"{u.Symbol}  {u.Name}").ToList();
        _toBox.SelectedIndex = _targets.IndexOf(_to);
        _updating = false;
        BuildGrid();
        _state.Units[_category!.Id] = [_from.Id, _to.Id];
    }

    private void BuildGrid()
    {
        _grid.Children.Clear();
        _cells.Clear();
        foreach (var unit in _targets)
        {
            var value = new TextBlock { Classes = { "cellvalue" } };
            var name = new TextBlock { Classes = { "cellname" }, Text = unit.Name };
            var cell = new Button { Theme = _cellTheme, Content = new StackPanel { Spacing = 2, Children = { FitWidth(value), name } } };
            AutomationProperties.SetName(cell, $"Convert to {unit.Name}");
            var target = unit;
            cell.Click += (_, _) => PickTarget(target);
            cell.KeyDown += OnCellKeyDown;
            _cells.Add((unit, cell, value));
            _grid.Children.Add(cell);
        }
        // Fill the last row so the grid lines stay complete.
        for (int i = _targets.Count; i % _grid.Columns != 0; i++)
        {
            var filler = new Border { BorderThickness = new Thickness(0, 0, 2, 2) };
            filler.Bind(Border.BackgroundProperty, filler.GetResourceObservable("Calcy.Card"));
            filler.Bind(Border.BorderBrushProperty, filler.GetResourceObservable("Calcy.Key"));
            _grid.Children.Add(filler);
        }
        MarkTargetCell();
    }

    private void MarkTargetCell()
    {
        foreach (var (unit, cell, _) in _cells) cell.Classes.Set("sel", unit.Equals(_to));
    }

    private void OnCellKeyDown(object? sender, KeyEventArgs e)
    {
        int index = _cells.FindIndex(c => ReferenceEquals(c.Cell, sender));
        if (index < 0) return;
        int next = e.Key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Up => index - _grid.Columns,
            Key.Down => index + _grid.Columns,
            _ => -1,
        };
        if (next >= 0 && next < _cells.Count && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            _cells[next].Cell.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }

    // ---- User actions ----

    private void OnFromPicked()
    {
        if (_updating || _fromBox.SelectedIndex < 0 || _fromBox.SelectedIndex >= _units.Count) return;
        var picked = _units[_fromBox.SelectedIndex];
        if (picked.Equals(_from) && _query is null) return;
        NormalizeValueText();
        _from = picked;
        if (!_to.IsCompatibleWith(_from)) _to = DefaultTarget(_from);
        RefreshUnitBoxes();
        Recalculate();
    }

    private void OnToPicked()
    {
        if (_updating || _toBox.SelectedIndex < 0 || _toBox.SelectedIndex >= _targets.Count) return;
        PickTarget(_targets[_toBox.SelectedIndex]);
    }

    /// <summary>Makes <paramref name="unit"/> the target (from the To box or a grid cell).</summary>
    internal void PickTarget(Unit unit)
    {
        if (!unit.IsCompatibleWith(_from)) return;
        if (_query is { Targets.Count: > 0 }) NormalizeValueText();
        _to = unit;
        _updating = true;
        _toBox.SelectedIndex = _targets.IndexOf(unit);
        _updating = false;
        _state.Units[_category!.Id] = [_from.Id, _to.Id];
        MarkTargetCell();
        Recalculate();
    }

    /// <summary>Swaps From and To, keeping the number.</summary>
    internal void Swap()
    {
        NormalizeValueText();
        (_from, _to) = (_to, _from);
        RefreshUnitBoxes();
        Recalculate();
    }

    /// <summary>
    /// When the value box holds units ("5 km to mi"), moves them into the unit boxes and leaves just the number,
    /// so a following unit change applies to the number.
    /// </summary>
    private void NormalizeValueText()
    {
        if (_query is not { } q) return;
        _from = q.Unit;
        if (q.Targets.Count > 0) _to = q.Targets[0];
        string text;
        if (q.IsSimple) text = q.NumberTexts[0];
        else
        {
            bool exact = true;
            BigRational total = BigRational.Zero;
            foreach (var t in q.Terms) total += _catalog.ConvertValue(t.Value, t.Unit, q.Unit, ref exact);
            text = PlainNumber(total);
        }
        _query = null;
        SetValueText(text);
    }

    /// <summary>A number as text that reads back exactly when possible (terminating decimal or fraction).</summary>
    private static string PlainNumber(BigRational value) =>
        value.ToTerminatingDecimal(20) ?? (value.Den < 1000 ? value.ToString() : NumberFormatter.FormatRational(value, new NumberFormatOptions { Digits = 15, Unicode = false }));

    private void SetValueText(string text)
    {
        _suppressText = true;
        _valueBox.Text = text;
        _valueBox.CaretIndex = text.Length;
        _suppressText = false;
    }

    /// <summary>Puts a parsed request on screen: its category, units and value.</summary>
    private void ApplyQuery(ConversionQuery query)
    {
        var category = _catalog.CategoryOf(query.Unit);
        if (category is null) return;
        if (_categoryId != category.Id) ShowCategory(category.Id);
        _query = null;
        EnsureListed(query.Unit);
        _from = query.Unit;
        if (query.Targets.Count > 0)
        {
            EnsureListed(query.Targets[0]);
            _to = query.Targets[0];
        }
        else if (!_to.IsCompatibleWith(_from) || _to.Equals(_from)) _to = DefaultTarget(_from);
        RefreshUnitBoxes();
        string text = query.Targets.Count > 1 ? $"{query.InputText} to {string.Join(" ", query.Targets.Select(t => t.Symbol))}"
            : query.IsSimple ? query.NumberTexts[0] : query.InputText;
        SetValueText(text);
        Recalculate();
    }

    /// <summary>While typing a whole request, follows its units without touching the text.</summary>
    private void FollowQueryUnits(ConversionQuery query)
    {
        var category = _catalog.CategoryOf(query.Unit);
        if (category is null) return;
        if (_category != category)
        {
            _category = category;
            _categoryId = category.Id;
            _selectingCategory = true;
            _categoryList.SelectedIndex = _categoryIds.IndexOf(category.Id);
            _selectingCategory = false;
            _units.Clear();
            _units.AddRange(category.Units);
            _state.Category = category.Id;
        }
        EnsureListed(query.Unit);
        _from = query.Unit;
        if (query.Targets.Count > 0)
        {
            EnsureListed(query.Targets[0]);
            _to = query.Targets[0];
        }
        else if (!_to.IsCompatibleWith(_from)) _to = DefaultTarget(_from);
        RefreshUnitBoxes();
    }

    // ---- Calculation ----

    /// <summary>Converts the value box with the current units and refreshes the result, formula and grid.</summary>
    internal void Recalculate()
    {
        if (_category is null || _from is null) return;
        var text = _valueBox.Text ?? "";
        string? error = null;
        _query = null;
        BigRational value = BigRational.Zero;
        bool haveValue = false;
        if (string.IsNullOrWhiteSpace(text)) error = "Type a number to convert";
        else if (UnitCatalog.TryParseNumber(text, out value)) haveValue = true;
        else if (_catalog.TryParse(text, out var query, out var parseError))
        {
            _query = query;
            FollowQueryUnits(query);
        }
        else if (TryArithmetic(text, out value)) haveValue = true;
        else error = parseError;

        ConversionResult? result = null;
        try
        {
            if (_query is { } q) result = _catalog.Evaluate(q, q.Targets.Count > 0 ? q.Targets : [_to]);
            else if (haveValue) result = _catalog.Convert(value, _from, _to);
        }
        catch (ConversionException ex)
        {
            error = ex.Message;
        }

        _result = result;
        var resultText = result?.Format(Options) ?? "?";
        _resultText.Text = resultText;
        _resultText.FontSize = resultText.Length <= 15 ? 28 : 24;
        _formula.Text = error ?? _catalog.Formula(_from, _to, Options);
        _formula.Classes.Set("err", error is not null && !string.IsNullOrWhiteSpace(text));
        _valueBox.Classes.Set("error", error is not null && !string.IsNullOrWhiteSpace(text));
        _save.IsEnabled = result is not null;
        UpdateGrid(result?.Input);
        _state.Value = text;
        // Kept in the preferences (written to disk when the app saves them, for example on exit).
        _services.Prefs.SetModeState(ModeId, _state);
    }

    private void UpdateGrid(Quantity? input)
    {
        if (input is not { } q)
        {
            _gridHeader.Text = "Every unit";
            foreach (var (_, _, value) in _cells) value.Text = "?";
            return;
        }
        var number = NumberFormatter.FormatRational(q.Value, Options);
        _gridHeader.Text = $"{number} {q.Unit.NameFor(q.Value)} in every unit";
        foreach (var (unit, _, value) in _cells)
        {
            try
            {
                bool exact = true;
                value.Text = unit.Format(_catalog.ConvertValue(q.Value, q.Unit, unit, ref exact), Options);
            }
            catch (ConversionException)
            {
                value.Text = unit.WithSymbol("∞");
            }
        }
    }

    /// <summary>Arithmetic in the value box (2*3, 1/3 + 1/6, 2^10), evaluated by the calculator engine.</summary>
    private bool TryArithmetic(string text, out BigRational value)
    {
        value = BigRational.Zero;
        // Letters would be read as variables (5 km is 5·k·m), so only plain arithmetic goes to the engine.
        if (text.Any(char.IsLetter) && !text.Contains('π')) return false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var expr = _services.Session.EvaluateExpression(text, ct: timeout.Token);
            if (expr is NumberExpr { Value: { IsExactRational: true } exact })
            {
                value = exact.ReQ;
                return true;
            }
            var approx = _services.Session.EvaluateExpression(text, approximate: true, ct: timeout.Token);
            if (approx is NumberExpr { Value: { IsReal: true } n } && double.IsFinite(n.Re))
            {
                value = BigRational.FromDouble(n.Re);
                return true;
            }
        }
        catch (Exception ex) when (ex is CalcException or ArithmeticException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
        }
        return false;
    }

    // ---- Cards ----

    /// <summary>Saves the current conversion as a card (does nothing when there is no result).</summary>
    internal void SaveCard()
    {
        if (_categoryId == DatesId) return;
        Recalculate();
        if (_result is not { } r || _category is null) return;
        string query = r.Query is { } q
            ? $"{q.InputText} to {string.Join(" ", r.Parts.Select(p => p.Unit.Symbol))}"
            : $"{PlainNumber(r.Input.Value)} {r.Input.Unit.Symbol} to {r.Target.Symbol}";
        AddCard(new ConvertCard(_category.Name, r.Describe(Options), query));
    }
}
