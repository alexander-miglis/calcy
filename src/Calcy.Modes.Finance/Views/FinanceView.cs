using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Calcy.Modes.Finance.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Finance.Views;

/// <summary>
/// The Finance screen (docs/design/Finance.dc.html): a tab strip with the TVM solver, amortization, cash flows, interest,
/// dates, depreciation and bonds on the left, and the result cards on the right. All maths lives in the Logic classes;
/// this view reads the fields, shows the answers and keeps the state.
/// </summary>
public sealed partial class FinanceView : UserControl, IReceivesPayload, IFocusFirst
{
    private const string ModeId = "finance";

    private static readonly (FinanceTab Tab, string Label)[] TabLabels =
    [
        (FinanceTab.Tvm, "TVM"),
        (FinanceTab.Amortization, "Amortization"),
        (FinanceTab.CashFlow, "Cash flow"),
        (FinanceTab.Interest, "Interest"),
        (FinanceTab.Dates, "Dates"),
        (FinanceTab.Depreciation, "Depreciation"),
        (FinanceTab.Bonds, "Bonds"),
    ];

    private readonly AppServices _services;
    private readonly FinanceState _state;
    private readonly ValueReader _reader;
    private readonly Dictionary<FinanceTab, PixelToggle> _tabButtons = [];
    private readonly Dictionary<FinanceTab, Control> _pages = [];
    private readonly Dictionary<FinanceTab, TextBox> _firstFields = [];
    private readonly Dictionary<TextBox, Action> _primaryActions = [];
    private readonly Grid _root;
    private readonly PixelPanel _cardsPanel;
    private readonly DispatcherTimer _saveTimer;
    private TextBox? _lastField;
    private bool _loading = true;

    /// <summary>Creates the screen on the shared services (session, preferences, navigation).</summary>
    public FinanceView(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _state = (services.Prefs.GetModeState<FinanceState>(ModeId) ?? new FinanceState()).Normalize();
        _reader = new ValueReader(services.Session);
        Styles.Add(new FinanceStyles());
        this.Token(ForegroundProperty, "Calcy.Ink");
        this.Token(FontFamilyProperty, "Calcy.Font.Body");

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _services.Prefs.Save();
        };

        // Left: tab strip and the page of the current tab.
        var strip = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (tab, label) in TabLabels)
        {
            var t = new PixelToggle
            {
                Content = Ui.Pixel(label, 2),
                MinHeight = 40,
                Padding = new Thickness(12, 4),
                Margin = new Thickness(0, 2, 8, 4),
                BorderThickness = new Thickness(2),
                ClipToBounds = false,
            };
            t.Classes.Add("shadow");
            AutomationProperties.SetName(t, label + " tab");
            ToolTip.SetTip(t, label + " (Ctrl+PageDown and Ctrl+PageUp move between tabs)");
            var which = tab;
            t.Click += (_, _) => ShowTab(which, focus: false);
            _tabButtons[tab] = t;
            strip.Children.Add(t);
        }
        var stripBorder = new Border { Child = strip };
        stripBorder.Classes.Add("tabstrip");
        AutomationProperties.SetName(stripBorder, "Finance tools");

        _pages[FinanceTab.Tvm] = BuildTvmPage();
        _pages[FinanceTab.Amortization] = BuildAmortizationPage();
        _pages[FinanceTab.CashFlow] = BuildCashFlowPage();
        _pages[FinanceTab.Interest] = BuildInterestPage();
        _pages[FinanceTab.Dates] = BuildDatesPage();
        _pages[FinanceTab.Depreciation] = BuildDepreciationPage();
        _pages[FinanceTab.Bonds] = BuildBondsPage();
        var host = new Panel();
        foreach (var p in _pages.Values)
        {
            p.IsVisible = false;
            host.Children.Add(p);
        }
        DockPanel.SetDock(stripBorder, Dock.Top);
        var section = new PixelPanel
        {
            Content = new DockPanel { Children = { stripBorder, host } },
            Margin = new Thickness(0, 0, 6, 6),
        };
        AutomationProperties.SetName(section, "Finance");

        // Right: cards.
        _cardsPanel = BuildCardsPanel();

        _root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,340"), Margin = new Thickness(16) };
        Grid.SetColumn(_cardsPanel, 2);
        _root.Children.Add(section);
        _root.Children.Add(_cardsPanel);
        Content = _root;
        SizeChanged += (_, e) => FitColumns(e.NewSize.Width);

        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Visual v && (v as TextBox ?? v.FindAncestorOfType<TextBox>()) is { } box && IsOurField(box)) _lastField = box;
        }, RoutingStrategies.Bubble);
        services.SettingsChanged += (_, _) => RecomputeAll();

        _loading = false;
        RenderCards();
        RecomputeAll();
        ApplyTab(_state.Tab);
    }

    /// <summary>The tab on screen.</summary>
    public FinanceTab CurrentTab => _state.Tab;

    /// <summary>The remembered state (for tests).</summary>
    internal FinanceState State => _state;

    /// <summary>The last text copied to the clipboard by COPY or EXPORT CSV (for tests).</summary>
    internal string? LastCopied { get; set; }

    // ------------------------------------------------------------------ IFocusFirst, IReceivesPayload

    /// <inheritdoc />
    public void FocusFirst()
    {
        if (_firstFields.TryGetValue(_state.Tab, out var box)) FocusField(box);
    }

    /// <summary>
    /// Accepts an <see cref="InputRequest"/> (or a string): the text goes into the field that had focus last on the
    /// current tab (or the tab's first field). With Evaluate, the tab's main action runs too (SOLVE or ADD CARD).
    /// </summary>
    public void Receive(object payload)
    {
        var (text, evaluate) = payload switch
        {
            InputRequest r => (r.Text, r.Evaluate),
            string s => (s, false),
            _ => (null, false),
        };
        if (text is null) return;
        var box = _lastField is not null && IsOnCurrentTab(_lastField) ? _lastField : _firstFields.GetValueOrDefault(_state.Tab);
        if (box is null) return;
        box.Text = text.Trim();
        box.CaretIndex = box.Text.Length;
        FocusField(box);
        if (evaluate && _primaryActions.TryGetValue(box, out var action)) action();
    }

    private bool IsOurField(TextBox box) => _primaryActions.ContainsKey(box);

    private bool IsOnCurrentTab(TextBox box) => _pages.TryGetValue(_state.Tab, out var page) && box.GetVisualAncestors().Contains(page);

    private static void FocusField(TextBox box)
    {
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
    }

    /// <summary>Registers an input: Enter runs the action, and it becomes a target for text sent from other modes.</summary>
    private TextBox Field(TextBox box, Action primary, FinanceTab tab)
    {
        _primaryActions[box] = primary;
        _firstFields.TryAdd(tab, box);
        Ui.OnEnter(box, primary);
        return box;
    }

    // ------------------------------------------------------------------ tabs and layout

    /// <summary>Shows a tab and remembers it; with <paramref name="focus"/> its first field gets the keyboard focus.</summary>
    public void ShowTab(FinanceTab tab, bool focus = true)
    {
        ApplyTab(tab);
        SaveSoon();
        if (!focus) return;
        // The page was hidden, so its fields have no template yet: focus again once it has been laid out.
        FocusFirst();
        Dispatcher.UIThread.Post(() =>
        {
            if (_state.Tab == tab && !IsKeyboardFocusWithin) FocusFirst();
        }, DispatcherPriority.Loaded);
    }

    private void ApplyTab(FinanceTab tab)
    {
        _state.Tab = tab;
        foreach (var (t, b) in _tabButtons)
        {
            b.IsChecked = t == tab;
            AutomationProperties.SetItemStatus(b, t == tab ? "selected" : "");
        }
        foreach (var (t, p) in _pages) p.IsVisible = t == tab;
        if (tab == FinanceTab.Amortization) RefreshAmortization();

    }

    private void FitColumns(double width)
    {
        double cards = width >= 1180 ? 340 : 300;
        _root.ColumnDefinitions[2].Width = new GridLength(cards);
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        int index = Array.FindIndex(TabLabels, x => x.Tab == _state.Tab);
        if (ctrl && e.Key is Key.PageDown or Key.PageUp)
        {
            int next = (index + (e.Key == Key.PageDown ? 1 : TabLabels.Length - 1)) % TabLabels.Length;
            ShowTab(TabLabels[next].Tab);
            e.Handled = true;
        }
    }

    private void RecomputeAll()
    {
        RecomputeTvm();
        RecomputeCashFlow();
        RecomputeInterest();
        RecomputeDates();
        RecomputeDepreciation();
        RecomputeBond();
    }

    // ------------------------------------------------------------------ state and clipboard

    /// <summary>Stores the state in the preferences now and writes them to disk shortly.</summary>
    private void SaveSoon()
    {
        if (_loading) return;
        _services.Prefs.SetModeState(ModeId, _state);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Stores the state and writes the preferences to disk straight away (after cards change).</summary>
    private void SaveNow()
    {
        _services.Prefs.SetModeState(ModeId, _state);
        _saveTimer.Stop();
        _services.Prefs.Save();
    }

    private async Task CopyAsync(string text, string done)
    {
        LastCopied = text;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            _services.ShowStatus(done);
            return;
        }
        try
        {
            await clipboard.SetTextAsync(text);
            _services.ShowStatus(done);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            _services.ShowStatus("Could not copy to the clipboard");
        }
    }

    // ------------------------------------------------------------------ shared building blocks

    /// <summary>A page: padded, scrolling content.</summary>
    private static ScrollViewer Page(Control content)
    {
        content.Margin = new Thickness(16, 16, 16, 20);
        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    /// <summary>
    /// A page with a header and a table that fills the rest of the height. When the window is too short for the header
    /// and a useful part of the table, the whole page scrolls instead of squeezing the table.
    /// </summary>
    private static ScrollViewer FillPage(Control head, Control fill, double minFill = 260)
    {
        const double margin = 16;
        var dock = new DockPanel { Margin = new Thickness(margin) };
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(fill);
        var scroller = new ScrollViewer
        {
            Content = dock,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        void Fit()
        {
            double viewport = scroller.Bounds.Height;
            if (viewport <= 0) return;
            head.Measure(new Size(Math.Max(0, scroller.Bounds.Width - 2 * margin), double.PositiveInfinity));
            double height = Math.Max(viewport - 2 * margin, head.DesiredSize.Height + minFill);
            if (Math.Abs(dock.Height - height) > 0.5 || double.IsNaN(dock.Height)) dock.Height = height;
        }
        scroller.SizeChanged += (_, _) => Fit();
        head.SizeChanged += (_, _) => Fit();
        return scroller;
    }

    /// <summary>A section title in the pixel font with an optional muted line next to it.</summary>
    private static Control Title(string title, string? hint = null)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var t = Ui.Pixel(title, 2);
        t.Margin = new Thickness(0, 0, 14, 4);
        panel.Children.Add(t);
        if (hint is not null)
        {
            var h = Ui.Text(hint, "hint");
            h.VerticalAlignment = VerticalAlignment.Center;
            h.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(h);
        }
        return panel;
    }

    /// <summary>A row of buttons aligned to the right.</summary>
    private static StackPanel Toolbar(params Control[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    /// <summary>The primary ADD CARD button.</summary>
    private static PixelButton AddCardButton(string what, Action action)
    {
        var b = Ui.Button("Add card", "Add card: " + what, "Keep this answer as a card (Enter)", "primary");
        b.Click += (_, _) => action();
        return b;
    }

    /// <summary>An EXPORT CSV button.</summary>
    private PixelButton ExportButton(string what, Func<string?> csv)
    {
        var b = Ui.Button("Export CSV", "Export CSV: " + what, "Copy the schedule to the clipboard as CSV text, ready to paste into a spreadsheet", "paper");
        b.Click += async (_, _) =>
        {
            if (csv() is not { } text) return;
            int lines = text.Count(c => c == '\n');
            await CopyAsync(text, $"Copied {what} as CSV: {Periods.Count(lines, "line")}");
        };
        return b;
    }
}
