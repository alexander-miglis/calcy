using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Formulas;
using Calcy.Modes.Formulas.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Formulas.Views;

/// <summary>The left column: the search box, the FORMULAS and CONSTANTS lists, categories that open into their formulas.</summary>
public sealed partial class FormulasView
{
    private TextBox _search = null!;
    private List<PixelToggle> _tabs = null!;
    private ListBox _categoryList = null!;
    private ListBox _resultList = null!;
    private ScrollViewer _constantScroll = null!;
    private StackPanel _constantList = null!;
    private string? _openCategory;
    private bool _syncingList;

    /// <summary>The category whose formulas are listed (null when all are closed).</summary>
    internal string? OpenCategory => _openCategory;

    /// <summary>The search results on screen (empty when the search box is empty).</summary>
    internal IReadOnlyList<Formula> SearchResults =>
        [.. _resultList.Items.OfType<ListBoxItem>().Select(i => i.Tag).OfType<FormulaListEntry>().Select(e => e.Formula)];

    /// <summary>The constants on screen in the CONSTANTS list.</summary>
    internal IReadOnlyList<FormulaConstant> ListedConstants => [.. _constantList.Children.OfType<Control>().Select(c => c.Tag).OfType<FormulaConstant>()];

    private PixelPanel BuildNav()
    {
        _search = new TextBox { Name = "FormulaSearch" };
        _search.Classes.Add("search");
        AutomationProperties.SetName(_search, "Find a formula (Ctrl+F)");
        Ui.OnText(_search, OnSearchChanged);
        Ui.OnKeys(_search, OnSearchKeyDown);

        var (group, tabs) = Ui.Segmented("Formula lists", [("Formulas", "Formulas list"), ("Constants", "Constants list")]);
        _tabs = tabs;
        tabs[0].Click += (_, _) => ShowTab(FormulasTab.Formulas);
        tabs[1].Click += (_, _) => ShowTab(FormulasTab.Constants);
        ToolTip.SetTip(tabs[0], $"Categories and formulas ({_library.All.Count})");
        ToolTip.SetTip(tabs[1], $"Physical and mathematical constants ({_library.Constants.Count})");
        group.Margin = new Thickness(0, 8, 0, 0);

        var top = new Border { Child = new StackPanel { Children = { _search, group } } };
        top.Classes.Add("searchbar");
        DockPanel.SetDock(top, Dock.Top);

        _categoryList = NewList("Categories and formulas", "CategoryList");
        foreach (var c in _browser.Categories) _categoryList.Items.Add(CategoryRow(c));
        _categoryList.SelectionChanged += (_, _) => OnListSelection(_categoryList);
        Ui.OnKeys(_categoryList, OnCategoryKeyDown);

        _resultList = NewList("Matching formulas", "SearchResults");
        _resultList.IsVisible = false;
        _resultList.SelectionChanged += (_, _) => OnListSelection(_resultList);
        Ui.OnKeys(_resultList, OnResultKeyDown);

        _constantList = new StackPanel { Name = "ConstantList" };
        _constantScroll = new ScrollViewer
        {
            Content = _constantList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsVisible = false,
        };
        AutomationProperties.SetName(_constantScroll, "Constants");

        var panel = new PixelPanel
        {
            Header = "Formulas",
            Margin = new Thickness(0, 0, 6, 6),
            Content = new DockPanel { Children = { top, new Panel { Children = { _categoryList, _resultList, _constantScroll } } } },
        };
        AutomationProperties.SetName(panel, "Find formulas and constants");
        return panel;
    }

    private static ListBox NewList(string automationName, string name)
    {
        var list = new ListBox
        {
            Name = name,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Single,
        };
        AutomationProperties.SetName(list, automationName);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        return list;
    }

    // ------------------------------------------------------------------ rows

    private ListBoxItem CategoryRow(FormulaCategory category)
    {
        var icon = new PixelIcon { Kind = "forward", Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        icon.Token(PixelIcon.ForegroundProperty, "Calcy.Ink");
        var name = new TextBlock { Text = category.Name, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var count = Ui.Text(_browser.CountIn(category.Id).ToString(System.Globalization.CultureInfo.InvariantCulture), "context");
        count.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(count, Dock.Right);
        var content = new DockPanel { Children = { icon, count, name } };
        var item = new ListBoxItem { Theme = _rowTheme, Content = content, Tag = category };
        item.Classes.Add("category");
        AutomationProperties.SetName(item, $"{category.Name}, {_browser.CountIn(category.Id)} formulas");
        ToolTip.SetTip(item, category.Description);
        item.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) ToggleCategory(category.Id);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        return item;
    }

    private ListBoxItem FormulaRow(FormulaListEntry entry)
    {
        var f = entry.Formula;
        var name = Ui.Text(f.Name, "listname");
        var tag = Ui.Tag(entry.LevelTag, f.Level == FormulaLevel.University ? "uni" : "school", FormulaBrowser.LevelName(f.Level));
        tag.Margin = new Thickness(0, 0, 6, 0);
        var context = Ui.Text(entry.Context, "context");
        DockPanel.SetDock(tag, Dock.Left);
        var line2 = new DockPanel { Children = { tag, context } };
        var content = new StackPanel { Spacing = 4, Children = { name, line2 } };
        var item = new ListBoxItem { Theme = _rowTheme, Content = content, Tag = entry };
        item.Classes.Add("formula");
        AutomationProperties.SetName(item, $"{f.Name}, {entry.Context}, {FormulaBrowser.LevelName(f.Level)}");
        ToolTip.SetTip(item, f.Equation);
        return item;
    }

    private Control ConstantRow(FormulaConstant c)
    {
        var symbol = Ui.Symbol(c.Symbol, 16);
        var use = Ui.Small("Use", $"Use {c.Name}", "Put the value into the box you were typing in, or send it to Advanced");
        use.Click += (_, _) => UseConstant(c);
        use.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(use, Dock.Right);
        var head = new DockPanel { Children = { use, symbol } };

        var name = Ui.Text(c.Name, "listname");
        var value = Ui.Text(ConstantText.Value(c), "chipmath");
        var precision = new DockPanel();
        if (ConstantText.Tag(c) is { } tagText)
        {
            var tag = Ui.Tag(tagText, tagText == "EXACT" ? "exact" : "typical",
                tagText == "EXACT" ? "Exact by definition" : "A typical or conventional value, not a measured constant");
            tag.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(tag, Dock.Left);
            precision.Children.Add(tag);
        }
        else precision.Children.Add(Ui.Text(ConstantText.Precision(c), "meaning"));

        var row = new Border
        {
            Padding = new Thickness(12, 8, 10, 9),
            Child = new StackPanel { Spacing = 4, Children = { head, name, value, precision } },
            Tag = c,
            BorderThickness = new Thickness(0, 0, 0, 2),
        }.Token(Border.BorderBrushProperty, "Calcy.Key");
        AutomationProperties.SetName(row, ConstantText.Describe(c));
        ToolTip.SetTip(row, c.Description + (c.Source is { } s ? $" Source: {s}." : ""));
        return row;
    }

    // ------------------------------------------------------------------ tabs

    /// <summary>Shows the FORMULAS or the CONSTANTS list.</summary>
    internal void ShowTab(FormulasTab tab)
    {
        ApplyTab(tab);
        SaveSoon();
    }

    private void ApplyTab(FormulasTab tab)
    {
        _state.Tab = tab;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool on = i == (int)tab;
            _tabs[i].IsChecked = on;
            AutomationProperties.SetItemStatus(_tabs[i], on ? "selected" : "");
        }
        _search.PlaceholderText = tab == FormulasTab.Formulas ? "Find a formula" : "Find a constant";
        ToolTip.SetTip(_search, tab == FormulasTab.Formulas
            ? "Find a formula by name, topic or equation (Ctrl+F). Small typos are fine. Enter opens the top match, Escape clears."
            : "Find a constant by name or symbol (Ctrl+F). Escape clears.");
        AutomationProperties.SetName(_search, tab == FormulasTab.Formulas ? "Find a formula (Ctrl+F)" : "Find a constant (Ctrl+F)");
        RefreshLists();
    }

    private void RefreshLists()
    {
        var query = _search.Text?.Trim() ?? "";
        bool constants = _state.Tab == FormulasTab.Constants;
        _constantScroll.IsVisible = constants;
        _categoryList.IsVisible = !constants && query.Length == 0;
        _resultList.IsVisible = !constants && query.Length > 0;
        if (constants) FillConstants(query);
        else if (query.Length > 0) FillResults(query);
    }

    // ------------------------------------------------------------------ search

    private void OnSearchChanged() => RefreshLists();

    private void FillResults(string query)
    {
        _syncingList = true;
        _resultList.Items.Clear();
        foreach (var entry in _browser.Search(query)) _resultList.Items.Add(FormulaRow(entry));
        if (_resultList.Items.Count == 0)
        {
            var none = new ListBoxItem
            {
                Theme = _rowTheme,
                Content = Ui.Text("No formula found. Try another word, a topic or an equation such as E=mc^2.", "hint"),
                IsEnabled = false,
                Focusable = false,
            };
            _resultList.Items.Add(none);
        }
        _syncingList = false;
    }

    private void FillConstants(string query)
    {
        _constantList.Children.Clear();
        foreach (var c in _browser.FindConstants(query)) _constantList.Children.Add(ConstantRow(c));
        if (_constantList.Children.Count == 0)
            _constantList.Children.Add(new Border { Padding = new Thickness(12, 10), Child = Ui.Text("No constant found.", "hint") });
        _constantScroll.Offset = default;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (!string.IsNullOrEmpty(_search.Text))
                {
                    ClearSearch();
                    e.Handled = true;
                }
                break;
            case Key.Enter when _state.Tab == FormulasTab.Formulas && !string.IsNullOrWhiteSpace(_search.Text):
                OpenTopHit();
                e.Handled = true;
                break;
            case Key.Down when _resultList.IsVisible && _resultList.Items.Count > 0 && _resultList.Items[0] is ListBoxItem { IsEnabled: true }:
                _resultList.SelectedIndex = 0;
                (_resultList.ContainerFromIndex(0) as Control)?.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Down when _categoryList.IsVisible:
                FocusListSelection();
                e.Handled = true;
                break;
        }
    }

    private void OnResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _resultList.SelectedItem is ListBoxItem { Tag: FormulaListEntry entry })
        {
            ClearSearch();
            OpenFormula(entry.Formula, focusInput: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearSearch();
            FocusFirst();
            e.Handled = true;
        }
    }

    /// <summary>Opens the best match of the search, clears the search and puts the cursor in the first empty box.</summary>
    internal void OpenTopHit()
    {
        var top = _browser.TopHit(_search.Text);
        if (top is null)
        {
            _services.ShowStatus("No formula matches that search");
            return;
        }
        ClearSearch();
        OpenFormula(top, focusInput: true);
    }

    private void ClearSearch()
    {
        if (string.IsNullOrEmpty(_search.Text)) return;
        _search.Text = "";
    }

    // ------------------------------------------------------------------ categories

    private void OnListSelection(ListBox list)
    {
        if (_syncingList) return;
        if (list.SelectedItem is ListBoxItem { Tag: FormulaListEntry entry } && !ReferenceEquals(entry.Formula, _formula))
            OpenFormula(entry.Formula, fromList: true);
    }

    private void OnCategoryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (_categoryList.SelectedItem)
        {
            case ListBoxItem { Tag: FormulaCategory c } when e.Key is Key.Enter or Key.Space or Key.Right:
                if (e.Key == Key.Right || _openCategory != c.Id) ExpandCategory(c.Id);
                else CollapseCategory();
                e.Handled = true;
                break;
            case ListBoxItem { Tag: FormulaCategory c } when e.Key == Key.Left && _openCategory == c.Id:
                CollapseCategory();
                e.Handled = true;
                break;
            case ListBoxItem { Tag: FormulaListEntry entry } when e.Key == Key.Left:
                var category = entry.Formula.Category;
                CollapseCategory();
                SelectRow(_categoryList, i => i.Tag is FormulaCategory fc && fc.Id == category, focus: true);
                e.Handled = true;
                break;
            case ListBoxItem { Tag: FormulaListEntry } when e.Key == Key.Enter:
                FocusFirstInput();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Opens a category (or closes it when it is already open).</summary>
    internal void ToggleCategory(string categoryId)
    {
        if (_openCategory == categoryId) CollapseCategory();
        else ExpandCategory(categoryId);
    }

    /// <summary>Lists the formulas of a category under its row, closing the one that was open.</summary>
    internal void ExpandCategory(string categoryId)
    {
        if (_openCategory == categoryId) return;
        if (_library.GetCategory(categoryId) is null) return;
        _syncingList = true;
        RemoveFormulaRows();
        _openCategory = categoryId;
        int at = IndexOfCategory(categoryId);
        if (at >= 0)
        {
            var rows = _browser.InCategory(categoryId);
            for (int i = 0; i < rows.Count; i++) _categoryList.Items.Insert(at + 1 + i, FormulaRow(rows[i]));
            MarkCategory(at, open: true);
            if (_formula is not null && _formula.Category == categoryId)
                SelectRow(_categoryList, item => item.Tag is FormulaListEntry fe && ReferenceEquals(fe.Formula, _formula), focus: false);
        }
        _syncingList = false;
    }

    /// <summary>Closes the open category.</summary>
    internal void CollapseCategory()
    {
        _syncingList = true;
        RemoveFormulaRows();
        _openCategory = null;
        _syncingList = false;
    }

    private void RemoveFormulaRows()
    {
        if (_openCategory is { } open)
        {
            int at = IndexOfCategory(open);
            if (at >= 0) MarkCategory(at, open: false);
        }
        for (int i = _categoryList.Items.Count - 1; i >= 0; i--)
            if (_categoryList.Items[i] is ListBoxItem { Tag: FormulaListEntry }) _categoryList.Items.RemoveAt(i);
    }

    private int IndexOfCategory(string categoryId)
    {
        for (int i = 0; i < _categoryList.Items.Count; i++)
            if (_categoryList.Items[i] is ListBoxItem { Tag: FormulaCategory c } && c.Id == categoryId) return i;
        return -1;
    }

    private void MarkCategory(int index, bool open)
    {
        if (_categoryList.Items[index] is not ListBoxItem item) return;
        item.Classes.Set("open", open);
        if (item.Content is DockPanel panel && panel.Children.Count > 0 && panel.Children[0] is PixelIcon icon) icon.Kind = open ? "chevron" : "forward";
        AutomationProperties.SetItemStatus(item, open ? "open" : "");
    }

    /// <summary>Shows the open formula in the list: its category open, its row selected and scrolled into view.</summary>
    private void SyncListSelection()
    {
        if (_openCategory != _formula.Category) ExpandCategory(_formula.Category);
        _syncingList = true;
        SelectRow(_categoryList, item => item.Tag is FormulaListEntry fe && ReferenceEquals(fe.Formula, _formula), focus: false);
        _syncingList = false;
    }

    private void SelectRow(ListBox list, Func<ListBoxItem, bool> match, bool focus)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is not ListBoxItem item || !match(item)) continue;
            list.SelectedIndex = i;
            list.ScrollIntoView(i);
            if (focus) Dispatcher.UIThread.Post(() => item.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
            return;
        }
    }

    private void FocusListSelection()
    {
        int index = Math.Max(0, _categoryList.SelectedIndex);
        if (_categoryList.SelectedIndex < 0) _categoryList.SelectedIndex = 0;
        _categoryList.ScrollIntoView(index);
        (_categoryList.ContainerFromIndex(index) as Control)?.Focus(NavigationMethod.Directional);
    }
}
