using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Calcy.Core.Printing;
using Calcy.UI.Controls;
using Calcy.UI.Services;

using Calcy.Units;

namespace Calcy.Modes.Convert;

/// <summary>
/// The Convert screen: the category list (with a unit search) on the left, the converter or the date tools in the middle,
/// and the saved conversion cards on the right.
/// </summary>
internal sealed partial class ConvertView : UserControl, IReceivesPayload, IFocusFirst
{
    /// <summary>Id of the Dates entry in the category list.</summary>
    internal const string DatesId = "dates";

    private const string ModeId = "convert";

    private readonly AppServices _services;
    private readonly UnitCatalog _catalog = UnitCatalog.Default;
    private readonly ConvertState _state;
    private readonly ConvertStyles _styles = new();
    private readonly ControlTheme _rowTheme;
    private readonly ControlTheme _cellTheme;

    private readonly Grid _root;
    private readonly PixelPanel _nav;
    private readonly TextBox _search;
    private readonly ListBox _categoryList;
    private readonly ListBox _searchList;
    private readonly Control _unitsPage;
    private readonly Control _datesPage;
    private readonly PixelPanel _cardsPanel;
    private readonly List<string> _categoryIds;
    private string _categoryId = "";
    private bool _selectingCategory;

    public ConvertView(AppServices services)
    {
        _services = services;
        _state = services.Prefs.GetModeState<ConvertState>(ModeId) ?? new ConvertState();
        Styles.Add(_styles);
        _rowTheme = (ControlTheme)_styles.Resources["ConvertListItem"]!;
        _cellTheme = (ControlTheme)_styles.Resources["ConvertCell"]!;
        this.Bind(ForegroundProperty, this.GetResourceObservable("Calcy.Ink"));
        this.Bind(FontFamilyProperty, this.GetResourceObservable("Calcy.Font.Body"));

        _categoryIds = [.. _catalog.Categories.Select(c => c.Id), DatesId];

        // Left: search and categories.
        _search = new TextBox { Classes = { "search" }, PlaceholderText = "Find a unit", Margin = new Thickness(10, 10, 10, 8), Name = "UnitSearch" };
        AutomationProperties.SetName(_search, "Find a unit (Ctrl+F)");
        ToolTip.SetTip(_search, "Find a unit by symbol or name (Ctrl+F)");
        _search.TextChanged += (_, _) => OnSearchChanged();
        OnKeys(_search, OnSearchKeyDown);

        _categoryList = NewList("Categories");
        foreach (var id in _categoryIds)
            _categoryList.Items.Add(Row(new TextBlock { Text = id == DatesId ? "Dates" : _catalog.GetCategory(id)!.Name }, id));
        _categoryList.SelectionChanged += (_, _) =>
        {
            if (_selectingCategory || _categoryList.SelectedItem is not ListBoxItem { Tag: string id }) return;
            ShowCategory(id);
        };

        _searchList = NewList("Matching units");
        _searchList.IsVisible = false;
        OnKeys(_searchList, OnSearchListKeyDown);
        _searchList.AddHandler(PointerReleasedEvent, (_, _) => PickSearchResult(), handledEventsToo: true);

        var lists = new Panel { Children = { _categoryList, _searchList } };
        var searchBorder = new Border { Child = _search, BorderThickness = new Thickness(0, 0, 0, 2) };
        searchBorder.Bind(Border.BorderBrushProperty, searchBorder.GetResourceObservable("Calcy.Key"));
        DockPanel.SetDock(searchBorder, Dock.Top);
        _nav = new PixelPanel
        {
            Header = "Convert",
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 6, 6),
            Content = new DockPanel { Children = { searchBorder, lists } },
        };

        // Middle: the converter or the date tools.
        _unitsPage = BuildUnitsPage();
        _datesPage = BuildDatesPage();
        _datesPage.IsVisible = false;
        var center = new ScrollViewer
        {
            Content = new Panel { Children = { _unitsPage, _datesPage }, Margin = new Thickness(0, 0, 8, 8) },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        // Right: cards.
        _cardsPanel = BuildCardsPanel();

        _root = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,300"), ColumnSpacing = 16, Margin = new Thickness(16) };
        Grid.SetColumn(_nav, 0);
        Grid.SetColumn(center, 1);
        Grid.SetColumn(_cardsPanel, 2);
        _root.Children.Add(_nav);
        _root.Children.Add(center);
        _root.Children.Add(_cardsPanel);
        Content = _root;

        SizeChanged += (_, e) => FitColumns(e.NewSize.Width);
        OnKeys(this, OnViewKeyDown);
        services.SettingsChanged += (_, _) =>
        {
            Recalculate();
            RecalculateDates();
        };

        RebuildCards();
        ShowCategory(_categoryIds.Contains(_state.Category) ? _state.Category : "length");
    }

    private NumberFormatOptions Options => NumberFormatOptions.From(_services.Settings);

    /// <summary>The id of the category on screen ("length", ..., or "dates").</summary>
    internal string CurrentCategoryId => _categoryId;

    // ---- IFocusFirst, IReceivesPayload ----

    /// <inheritdoc />
    public void FocusFirst()
    {
        var box = _categoryId == DatesId ? _d1Box : _valueBox;
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
    }

    /// <summary>
    /// Accepts an <see cref="InputRequest"/> (or a string) such as "5 km to mi", "100 °C in °F", "2026-10-13 + 100 days":
    /// fills the screen and converts. With <see cref="InputRequest.Evaluate"/> the result is also saved as a card.
    /// </summary>
    public void Receive(object payload)
    {
        var (text, evaluate) = payload switch
        {
            InputRequest r => (r.Text, r.Evaluate),
            string s => (s, false),
            _ => (null, false),
        };
        if (string.IsNullOrWhiteSpace(text)) return;

        if (DateTools.TryParseRequest(text, out var request))
        {
            ShowCategory(DatesId);
            ApplyDateRequest(request);
            if (evaluate) SaveDateCard(request);
            return;
        }

        if (_catalog.TryParse(text, out var query, out _))
        {
            ApplyQuery(query);
            if (evaluate) SaveCard();
            return;
        }

        if (_categoryId == DatesId) ShowCategory(_category?.Id ?? "length");
        SetValueText(text.Trim());
        Recalculate();
        if (evaluate) SaveCard();
    }

    // ---- Categories ----

    /// <summary>Shows a unit category (by id) or the date tools ("dates").</summary>
    internal void ShowCategory(string id)
    {
        if (id != DatesId && _catalog.GetCategory(id) is null) id = "length";
        _categoryId = id;
        _selectingCategory = true;
        _categoryList.SelectedIndex = _categoryIds.IndexOf(id);
        _selectingCategory = false;

        bool dates = id == DatesId;
        _unitsPage.IsVisible = !dates;
        _datesPage.IsVisible = dates;
        if (dates)
        {
            RecalculateDates();
        }
        else
        {
            var category = _catalog.GetCategory(id)!;
            if (_category != category)
            {
                NormalizeValueText();
                SetCategory(category);
            }
            Recalculate();
        }
        _state.Category = id;
        SaveState();
    }

    private ListBox NewList(string name)
    {
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Single,
        };
        AutomationProperties.SetName(list, name);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        return list;
    }

    private ListBoxItem Row(Control content, object tag) => new() { Theme = _rowTheme, Content = content, Tag = tag };

    private void FitColumns(double width)
    {
        var (nav, cards) = width switch
        {
            < 1100 => (180, 230),
            < 1280 => (200, 260),
            _ => (220, 300),
        };
        _root.ColumnDefinitions[0].Width = new GridLength(nav);
        _root.ColumnDefinitions[2].Width = new GridLength(cards);
    }

    // ---- Unit search ----

    private void OnSearchChanged()
    {
        var q = _search.Text?.Trim() ?? "";
        _searchList.Items.Clear();
        if (q.Length == 0)
        {
            _searchList.IsVisible = false;
            _categoryList.IsVisible = true;
            return;
        }
        foreach (var unit in _catalog.Search(q, 14))
        {
            var category = _catalog.CategoryOf(unit);
            var symbol = new TextBlock { Text = unit.Symbol, FontWeight = FontWeight.Bold, Classes = { "math" }, FontSize = 15 };
            var name = new TextBlock { Text = unit.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            var where = new TextBlock { Text = category?.Name ?? "", Classes = { "hint" }, FontSize = 11 };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            Grid.SetColumn(name, 1);
            name.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(symbol);
            line.Children.Add(name);
            var content = new StackPanel { Margin = new Thickness(0, 4), Children = { line, where } };
            AutomationProperties.SetName(content, $"{unit.Symbol}, {unit.Name}, {category?.Name}");
            _searchList.Items.Add(Row(content, unit));
        }
        if (_searchList.Items.Count == 0)
            _searchList.Items.Add(new ListBoxItem { Theme = _rowTheme, Content = new TextBlock { Text = "No unit found", Classes = { "hint" } }, IsEnabled = false });
        _searchList.IsVisible = true;
        _categoryList.IsVisible = false;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ClearSearch();
                FocusFirst();
                e.Handled = true;
                break;
            case Key.Enter when _searchList.IsVisible && _searchList.Items.Count > 0:
                _searchList.SelectedIndex = 0;
                PickSearchResult();
                e.Handled = true;
                break;
            case Key.Down when _searchList.IsVisible && _searchList.Items.Count > 0:
                _searchList.SelectedIndex = 0;
                (_searchList.ContainerFromIndex(0) as Control)?.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
        }
    }

    private void OnSearchListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PickSearchResult();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearSearch();
            FocusFirst();
            e.Handled = true;
        }
    }

    private void ClearSearch() => _search.Text = "";

    /// <summary>Uses the selected search result: the target unit when it fits the current category, otherwise the new category's From unit.</summary>
    private void PickSearchResult()
    {
        if (_searchList.SelectedItem is not ListBoxItem { Tag: Unit unit }) return;
        PickUnit(unit);
        ClearSearch();
        Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Background);
    }

    /// <summary>Makes <paramref name="unit"/> part of the conversion (see <see cref="PickSearchResult"/>).</summary>
    internal void PickUnit(Unit unit)
    {
        var category = _catalog.CategoryOf(unit);
        if (category is null) return;
        if (_categoryId != DatesId && _category == category && _from.IsCompatibleWith(unit) && !_from.Equals(unit))
        {
            NormalizeValueText();
            EnsureListed(unit);
            _to = unit;
            RefreshUnitBoxes();
            Recalculate();
            return;
        }
        if (_categoryId == DatesId || _category != category) ShowCategory(category.Id);
        NormalizeValueText();
        EnsureListed(unit);
        _from = unit;
        if (!_to.IsCompatibleWith(unit) || _to.Equals(unit)) _to = DefaultTarget(unit);
        RefreshUnitBoxes();
        Recalculate();
    }

    // ---- Keyboard ----

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _search.Focus(NavigationMethod.Tab);
            _search.SelectAll();
            e.Handled = true;
        }
    }

    // ---- Shared helpers ----

    /// <summary>Handles keys before the control itself (a TextBox would otherwise consume Up, Down and Enter).</summary>
    private static void OnKeys(Control control, EventHandler<KeyEventArgs> handler) =>
        control.AddHandler(KeyDownEvent, handler, Avalonia.Interactivity.RoutingStrategies.Tunnel);

    private static TextBlock Label(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "label", "ink" } };

    private void SaveState()
    {
        _services.Prefs.SetModeState(ModeId, _state);
        _services.Prefs.Save();
    }
}
