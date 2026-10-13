using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Calcy.Core;
using Calcy.Graphing;
using Calcy.Modes.Statistics.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Graph;
using Calcy.UI.Services;

namespace Calcy.Modes.Statistics.Views;

/// <summary>
/// The Statistics screen (docs/design/Statistics.dc.html): the list editor on the left, the analysis tabs (1-Var, 2-Var,
/// Regression, Tests, Distribution, Plots) in the middle and the result cards on the right. Every list is a session
/// variable; analyses run in the background and update as the data changes; every result can become a card.
/// </summary>
public sealed partial class StatisticsView : UserControl, IReceivesPayload, IFocusFirst, IBusy, IStatCardHost
{
    private const string StateKey = "statistics";

    private readonly AppServices _s;
    private readonly CalcSession _session;
    // Background work posts back through the dispatcher captured here, never through the static Dispatcher.UIThread
    // getter (which a pool thread must not be the first to touch).
    private readonly Dispatcher _ui = Dispatcher.UIThread;
    private readonly StatTable _table;
    private StatsModeState _state;
    private readonly Grid _root;
    private readonly PixelPanel _dataPanel;
    private readonly PixelPanel _analysisPanel;
    private readonly PixelPanel _cardsPanel;
    private readonly Dictionary<StatTab, object> _last = [];
    private CancellationTokenSource? _cts;
    private int _generation;
    private int _pending;
    private int _saveGeneration;
    private bool _savePending;
    private bool _restoring;

    /// <summary>Builds the screen and restores the saved lists, settings and cards.</summary>
    public StatisticsView(AppServices services)
    {
        _s = services;
        _session = services.Session;
        _table = new StatTable(_session);
        _state = services.Prefs.GetModeState<StatsModeState>(StateKey) ?? StatsModeState.CreateDefault();
        Normalize(_state);
        _restoring = true;
        RestoreTable();

        _twoVarGraph = NewCanvas(_session, "Scatter plot", 230);
        _regGraph = NewCanvas(_session, "Scatter plot with the fitted curve", 290);
        _testGraph = NewCanvas(_session, "Distribution of the test statistic with the rejection region shaded", 200);
        _distGraph = NewCanvas(_session, "Density curve with the shaded area", 220);
        _plotGraph = NewCanvas(_session, "Scatter plot", 300);
        _twoVarHost = new PlotHost(_twoVarGraph, 230);
        _regHost = new PlotHost(_regGraph, 290);
        _dataPanel = BuildDataPanel();
        _analysisPanel = BuildAnalysisPanel();
        _cardsPanel = BuildCardsPanel();
        _root = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*,300"), ColumnSpacing = 16, Margin = new Thickness(16) };
        Grid.SetColumn(_dataPanel, 0);
        Grid.SetColumn(_analysisPanel, 1);
        Grid.SetColumn(_cardsPanel, 2);
        _root.Children.Add(_dataPanel);
        _root.Children.Add(_analysisPanel);
        _root.Children.Add(_cardsPanel);
        Content = _root;

        SizeChanged += (_, e) => Adapt(e.NewSize.Width);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _pending > 0)
            {
                _cts?.Cancel();
                e.Handled = true;
            }
        };
        _session.Env.Changed += (_, _) => _ui.Post(SyncFromSession);
        _s.SettingsChanged += (_, _) => ScheduleAnalysis();
        Restore();
    }

    // ------------------------------------------------------------------ public surface

    /// <summary>The lists.</summary>
    public StatTable Table => _table;

    /// <summary>The list editor.</summary>
    public ListGrid ListEditor => _grid;

    /// <summary>The open analysis tab.</summary>
    public StatTab Tab => _state.Tab;

    /// <summary>The cards, newest first.</summary>
    public IReadOnlyList<StatCard> Cards => _state.Cards;

    /// <summary>The card controls, newest first.</summary>
    public IEnumerable<StatCardView> CardViews => _cardList.Children.OfType<StatCardView>();

    /// <summary>The saved state (settings of every tab).</summary>
    public StatsModeState State => _state;

    /// <summary>The latest result of a tab (OneVarOutcome, RegressionOutcome, ...), or null.</summary>
    public object? Outcome(StatTab tab) => _last.TryGetValue(tab, out var o) ? o : null;

    /// <summary>True while an analysis runs in the background or a graph is still being drawn.</summary>
    public bool IsBusy => _pending > 0 || VisibleCanvases().Any(c => c.IsBusy);

    /// <summary>Puts keyboard focus in the active cell of the list editor.</summary>
    public void FocusFirst() => _grid.FocusEditor();

    /// <summary>
    /// Accepts a column formula or text for the formula box (InputRequest; Evaluate applies it) and numeric data
    /// (DataRequest) as new lists, opening an analysis on them.
    /// </summary>
    public void Receive(object payload)
    {
        switch (payload)
        {
            case InputRequest r:
                _formula.Text = r.Text;
                _formula.CaretIndex = r.Text.Length;
                _formula.Focus();
                if (r.Evaluate) ApplyFormula();
                break;
            case DataRequest d when d.Columns.Count > 0:
                ImportData(d);
                break;
        }
    }

    /// <summary>Opens an analysis tab.</summary>
    public void ShowTab(StatTab tab)
    {
        _state.Tab = tab;
        _tabs.Select(tab);
        // Settings may have been changed from outside (a card, data from another mode): show them.
        RefreshListPickers();
        ShowTabContent();
        ScheduleAnalysis(0);
        ScheduleSave();
    }

    // ------------------------------------------------------------------ layout

    private void Adapt(double width)
    {
        bool narrow = width < 1240;
        _root.ColumnDefinitions[0].Width = new GridLength(narrow ? 270 : 300);
        _root.ColumnDefinitions[2].Width = new GridLength(narrow ? 260 : 300);
    }

    private IEnumerable<GraphCanvas> VisibleCanvases()
    {
        // Graphs on hidden tabs are not in the visual tree and never sample, so they must not count as busy.
        foreach (var c in AllCanvases())
            if (c.IsEffectivelyVisible && TopLevel.GetTopLevel(c) is not null && c.Bounds.Width > 2 && c.Bounds.Height > 2) yield return c;
    }

    private IEnumerable<GraphCanvas> AllCanvases() => [_twoVarGraph, _regGraph, _testGraph, _distGraph, _plotGraph];

    // ------------------------------------------------------------------ background analysis

    /// <summary>One analysis computed in the background, with the graph items it draws.</summary>
    private sealed record Job(StatTab Tab, object Outcome, IReadOnlyList<GraphItem>? Items);

    /// <summary>Recomputes the open tab in the background (after a short pause so fast typing does not queue work).</summary>
    private void ScheduleAnalysis(int delayMs = 40)
    {
        if (_restoring) return;
        _cts?.Cancel();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _cts = cts;
        int generation = ++_generation;
        var tab = _state.Tab;
        var work = Snapshot(tab);
        _pending++;
        var token = cts.Token;
        Task.Run(async () =>
        {
            if (delayMs > 0) await Task.Delay(delayMs, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return work(token);
        }, token).ContinueWith(t => _ui.Post(() =>
        {
            _pending--;
            if (generation != _generation) return;
            if (t.IsCanceled)
            {
                ShowAnalysisError(tab, "Stopped: the calculation took too long or was cancelled. Change the data or a setting to try again.");
                return;
            }
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException();
                ShowAnalysisError(tab, ex is OperationCanceledException ? "Stopped: the calculation took too long" : "Something went wrong: " + ex?.Message);
                return;
            }
            _last[tab] = t.Result.Outcome;
            if (tab != _state.Tab) return;
            try
            {
                Render(t.Result);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException or IndexOutOfRangeException)
            {
                // A result that cannot be shown must not take the app down; say what happened instead.
                ShowAnalysisError(tab, "This result could not be shown: " + ex.Message);
            }
        }), TaskScheduler.Default);
        // Slow work (a big list, a stubborn nonlinear fit) says so after a moment, with the way out.
        Task.Delay(400).ContinueWith(_ => _ui.Post(() =>
        {
            if (generation == _generation && _pending > 0 && tab == _state.Tab) ShowWorking(tab);
        }), TaskScheduler.Default);
    }

    /// <summary>Takes what a tab needs from the screen (a data snapshot and a copy of its settings) for the background.</summary>
    private Func<CancellationToken, Job> Snapshot(StatTab tab)
    {
        var data = DataSet.From(_table);
        var session = _session;
        switch (tab)
        {
            case StatTab.OneVar:
            {
                var s = _state.OneVar.Clone();
                return _ => new Job(tab, Descriptives.OneVar(data, s), null);
            }
            case StatTab.TwoVar:
            {
                var s = _state.TwoVar.Clone();
                return _ =>
                {
                    var o = Descriptives.TwoVar(data, s);
                    return new Job(tab, o, Items(o.Graph, session));
                };
            }
            case StatTab.Regression:
            {
                var s = _state.Regression.Clone();
                return ct =>
                {
                    var o = Regressions.Fit(data, s, session, ct);
                    return new Job(tab, o, Items(o.Graph, session));
                };
            }
            case StatTab.Tests:
            {
                var s = _state.Test.Clone();
                return _ =>
                {
                    var o = TestRunner.Run(s, data, session);
                    return new Job(tab, o, Items(o.Graph, session));
                };
            }
            case StatTab.Distribution:
            {
                var s = _state.Distribution.Clone();
                return _ =>
                {
                    var o = Distributions.Compute(s, session);
                    return new Job(tab, o, Items(o.Graph, session));
                };
            }
            default:
            {
                var s = _state.Plot.Clone();
                var method = _state.OneVar.Method;
                return _ =>
                {
                    var o = Plots.Build(data, s, method, session);
                    return new Job(tab, o, Items(o.Graph, session));
                };
            }
        }
    }

    private static IReadOnlyList<GraphItem>? Items(GraphSpec? spec, CalcSession session)
    {
        if (spec is null) return null;
        var items = new List<GraphItem>();
        for (int i = 0; i < spec.Lines.Count; i++)
        {
            var item = GraphParser.Parse(spec.Lines[i].Text, session, defineFunctions: false);
            item.ColorIndex = spec.Lines[i].ColorIndex;
            item.Name = "s" + (i + 1).ToString(CultureInfo.InvariantCulture);
            items.Add(item);
        }
        return items;
    }

    private static void ShowGraph(GraphCanvas canvas, GraphSpec? spec, IReadOnlyList<GraphItem>? items)
    {
        if (spec is null || items is null)
        {
            canvas.Items = [];
            canvas.Annotations = [];
            return;
        }
        canvas.Viewport = spec.Viewport;
        canvas.Annotations = spec.Annotations;
        canvas.Items = items;
    }

    private static GraphCanvas NewCanvas(CalcSession session, string name, double height)
    {
        var c = new GraphCanvas { Session = session, ShowPointsOfInterest = false, Height = height, Viewport = new Viewport(-1, 10, -1, 10) };
        AutomationProperties.SetName(c, name);
        return c;
    }

    // ------------------------------------------------------------------ persistence

    private static void Normalize(StatsModeState s)
    {
        s.OneVar ??= new OneVarSettings();
        s.TwoVar ??= new TwoVarSettings();
        s.Regression ??= new RegressionSettings();
        s.Test ??= new TestSettings();
        s.Test.Fields ??= [];
        s.Distribution ??= new DistributionSettings();
        s.Distribution.Parameters ??= [];
        s.Plot ??= new PlotSettings();
        s.Cards ??= [];
        s.Columns ??= [];
        s.Formula ??= "";
        if (s.Cards.Count > StatsModeState.MaxCards) s.Cards.RemoveRange(StatsModeState.MaxCards, s.Cards.Count - StatsModeState.MaxCards);
        s.NextCardNumber = Math.Max(s.NextCardNumber, s.Cards.Count == 0 ? 1 : s.Cards.Max(c => c.Number) + 1);
    }

    private void RestoreTable()
    {
        _table.Restore(_state.Columns);
        if (_table.Columns.Count == 0)
            foreach (var n in new[] { "L1", "L2" }) _table.AddColumn(n);
        // Lists already in this session (typed in Advanced, say) are newer than the saved copies.
        _table.PullFromSession();
        _table.PublishAll();
    }

    private void Restore()
    {
        _restoring = true;
        _formula.Text = _state.Formula;
        foreach (var card in _state.Cards) _cardList.Children.Add(new StatCardView(card, this));
        UpdateWelcome();
        _tabs.Select(_state.Tab);
        RefreshGrid();
        RefreshListPickers();
        ShowTabContent();
        _restoring = false;
        ScheduleAnalysis(0);
    }

    /// <summary>Saves the lists, settings and cards into the preferences now.</summary>
    public void Save()
    {
        _state.Columns = _table.ToState();
        _state.Formula = _formula.Text ?? "";
        _s.Prefs.SetModeState(StateKey, _state);
        _s.Prefs.Save();
    }

    private void ScheduleSave()
    {
        if (_restoring) return;
        int g = ++_saveGeneration;
        _savePending = true;
        Task.Delay(500).ContinueWith(_ => _ui.Post(() =>
        {
            if (g != _saveGeneration) return;
            _savePending = false;
            Save();
        }), TaskScheduler.Default);
    }

    /// <summary>True while a save is waiting (for tests).</summary>
    public bool SavePending => _savePending;
}
