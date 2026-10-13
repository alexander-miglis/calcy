using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Calcy.Formulas;
using Calcy.Modes.Formulas.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Formulas.Views;

/// <summary>
/// The Formulas screen: search, categories and constants on the left; the open formula in the middle (equation,
/// explanation, the variables table that solves for any variable, the worked example and related formulas);
/// the answer cards on the right. The maths lives in the Logic classes; this view shows it and keeps the state.
/// </summary>
public sealed partial class FormulasView : UserControl, IReceivesPayload, IFocusFirst, IBusy
{
    private const string ModeId = "formulas";

    private readonly AppServices _services;
    private readonly FormulaLibrary _library;
    private readonly FormulaBrowser _browser;
    private readonly FormulaCalculator _calculator;
    private readonly FormulasState _state;
    private readonly FormulasStyles _styles = new();
    private readonly ControlTheme _rowTheme;
    private readonly Grid _root;
    private readonly PixelPanel _nav;
    private readonly PixelPanel _page;
    private readonly PixelPanel _cardsPanel;
    private Formula _formula = null!;
    private SavedInputs _saved = null!;
    private TextBox? _lastInput;

    /// <summary>Creates the screen on the shared services (session, preferences, navigation).</summary>
    public FormulasView(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _library = FormulaLibrary.Default;
        _browser = new FormulaBrowser(_library);
        _calculator = new FormulaCalculator(services.Session, _library);
        _state = (services.Prefs.GetModeState<FormulasState>(ModeId) ?? new FormulasState()).Normalize(_library);
        Styles.Add(_styles);
        _rowTheme = (ControlTheme)_styles.Resources["FormulaListItem"]!;
        this.Token(ForegroundProperty, "Calcy.Ink");
        this.Token(FontFamilyProperty, "Calcy.Font.Body");

        _nav = BuildNav();
        _page = BuildPage();
        _cardsPanel = BuildCardsPanel();

        _root = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,290"), ColumnSpacing = 16, Margin = new Thickness(16) };
        Grid.SetColumn(_nav, 0);
        Grid.SetColumn(_page, 1);
        Grid.SetColumn(_cardsPanel, 2);
        _root.Children.Add(_nav);
        _root.Children.Add(_page);
        _root.Children.Add(_cardsPanel);
        Content = _root;

        SizeChanged += (_, e) => FitColumns(e.NewSize.Width);
        Ui.OnKeys(this, OnViewKeyDown);
        AddHandler(GotFocusEvent, (_, e) => OnAnyGotFocus(e.Source), RoutingStrategies.Bubble);
        services.SettingsChanged += (_, _) => Recalculate();

        RenderCards();
        ApplyTab(_state.Tab);
        OpenFormula(_library.Get(_state.FormulaId)!, save: false);
    }

    /// <summary>The formula on screen.</summary>
    public Formula CurrentFormula => _formula;

    /// <summary>The variable being solved for (empty for identities).</summary>
    public string Unknown => _unknown;

    /// <summary>The list shown in the left column.</summary>
    public FormulasTab CurrentTab => _state.Tab;

    /// <summary>The remembered state (for tests).</summary>
    internal FormulasState State => _state;

    /// <summary>The last text copied to the clipboard (for tests).</summary>
    internal string? LastCopied { get; private set; }

    // ------------------------------------------------------------------ IFocusFirst, IReceivesPayload

    /// <inheritdoc />
    public void FocusFirst()
    {
        _search.Focus(NavigationMethod.Tab);
        _search.SelectAll();
    }

    /// <summary>
    /// Accepts a <see cref="FormulaRequest"/> (opens that formula), or an <see cref="InputRequest"/> or string: the text
    /// goes into the search box (a formula id opens the formula). With <see cref="InputRequest.Evaluate"/> the top
    /// match is opened straight away.
    /// </summary>
    public void Receive(object payload)
    {
        switch (payload)
        {
            case FormulaRequest r when _library.Get(r.FormulaId) is { } byId:
                ShowTab(FormulasTab.Formulas);
                ClearSearch();
                OpenFormula(byId);
                return;
            case InputRequest r:
                Search(r.Text, r.Evaluate);
                return;
            case string s:
                Search(s, false);
                return;
        }
    }

    private void Search(string? text, bool openTopHit)
    {
        var query = text?.Trim() ?? "";
        if (query.Length == 0) return;
        if (_library.Get(query) is { } exact)
        {
            ShowTab(FormulasTab.Formulas);
            ClearSearch();
            OpenFormula(exact);
            return;
        }
        if (_state.Tab != FormulasTab.Formulas) ShowTab(FormulasTab.Formulas);
        _search.Text = query;
        _search.CaretIndex = query.Length;
        if (openTopHit) OpenTopHit();
        else FocusFirst();
    }

    // ------------------------------------------------------------------ layout and keys

    private void FitColumns(double width)
    {
        var (nav, cards) = width switch
        {
            < 1000 => (214.0, 240.0),
            < 1200 => (230.0, 260.0),
            < 1500 => (260.0, 290.0),
            _ => (290.0, 330.0),
        };
        _root.ColumnDefinitions[0].Width = new GridLength(nav);
        _root.ColumnDefinitions[2].Width = new GridLength(cards);
        bool compact = cards < 260;
        if (compact != _compactCards)
        {
            _compactCards = compact;
            RenderCards();
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (ctrl && e.Key == Key.F)
        {
            FocusFirst();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Remembers the last value box that had the focus, so a constant's USE knows where to go, even after the search
    /// box was used to find the constant. Opening another formula forgets it.
    /// </summary>
    private void OnAnyGotFocus(object? source)
    {
        if (source is not Visual v) return;
        var box = v as TextBox ?? v.FindAncestorOfType<TextBox>();
        if (box is not null && _rows.Any(r => ReferenceEquals(r.Input, box))) _lastInput = box;
    }

    // ------------------------------------------------------------------ state and clipboard

    /// <summary>Keeps the state in the preferences (written to disk when the app saves them, for example on exit).</summary>
    private void SaveSoon() => _services.Prefs.SetModeState(ModeId, _state);

    /// <summary>Keeps the state and writes the preferences to disk now (after a formula or the cards change).</summary>
    private void SaveNow()
    {
        _services.Prefs.SetModeState(ModeId, _state);
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

    /// <summary>Sends an expression to Advanced mode without evaluating it, so it can be checked or extended first.</summary>
    private void SendToAdvanced(string expression, string what)
    {
        _services.Navigate("advanced", new InputRequest(expression, Evaluate: false));
        Dispatcher.UIThread.Post(() => _services.ShowStatus($"Sent {what} to Advanced: press Enter there to calculate"));
    }
}
