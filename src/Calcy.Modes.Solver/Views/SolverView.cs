using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Solver.Views;

/// <summary>
/// The Solver screen (design board Solver.dc.html): a panel with one tab per tool (polynomial, linear system,
/// nonlinear system, any equation, inequality) and the result of the last solve, and a Cards panel where every solve
/// becomes a card (design decision D5). Solving runs off the UI thread; a WORKING card appears after 250 ms and
/// Escape stops it.
/// </summary>
public sealed partial class SolverView : UserControl, IReceivesPayload, IFocusFirst, IBusy
{
    /// <summary>The key of the saved state in the preferences.</summary>
    public const string ModeId = "solver";

    private readonly AppServices _s;
    private readonly SolverEngine _engine;
    private readonly SolverState _state;
    private readonly Dictionary<SolverTool, PixelToggle> _tabs = [];
    private readonly Dictionary<SolverTool, Control> _pages = [];
    private readonly Dictionary<SolverTool, SolveOutcome> _outcomes = [];
    private readonly ScrollViewer _pageScroll;
    private readonly ColumnDefinition _asideColumn = new(340, GridUnitType.Pixel);
    private CancellationTokenSource? _running;
    private bool _runningAddsCard;
    private TaskCompletionSource? _runningDone;
    private bool _loading;

    /// <summary>Builds the screen from the saved state (or the design's examples on first run).</summary>
    public SolverView(AppServices services)
    {
        _s = services;
        _engine = new SolverEngine(services.Session);
        _state = (services.Prefs.GetModeState<SolverState>(ModeId) ?? new SolverState()).Normalize();
        Styles.Add(new SolverStyles());
        _loading = true;

        foreach (var tool in SolverTools.All)
        {
            _pages[tool] = tool switch
            {
                SolverTool.Polynomial => BuildPolynomialPage(),
                SolverTool.LinearSystem => BuildLinearPage(),
                SolverTool.NonlinearSystem => BuildNonlinearPage(),
                SolverTool.Equation => BuildEquationPage(),
                _ => BuildInequalityPage(),
            };
            _pages[tool].IsVisible = false;
        }
        var pageHost = new Panel();
        foreach (var page in _pages.Values) pageHost.Children.Add(page);
        _pageScroll = new ScrollViewer { Content = pageHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var main = new DockPanel();
        var strip = TabStrip();
        DockPanel.SetDock(strip, Dock.Top);
        main.Children.Add(strip);
        main.Children.Add(_pageScroll);
        var section = new PixelPanel { Content = main, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(section, "Solver");

        var aside = BuildCardsPanel();
        var root = new Grid { Margin = new Thickness(16), ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(16, GridUnitType.Pixel), _asideColumn } };
        Grid.SetColumn(section, 0);
        Grid.SetColumn(aside, 2);
        root.Children.Add(section);
        root.Children.Add(aside);
        Content = root;
        root.PropertyChanged += (_, e) =>
        {
            // The design's cards column is 340 px wide and gives way on narrow windows.
            if (e.Property == BoundsProperty) _asideColumn.Width = new GridLength(root.Bounds.Width >= 1180 ? 340 : 290);
        };

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _s.Session.Env.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshUnknowns);
        _loading = false;
        RenderCards();
        SelectTool(_state.Tool, preview: false);
        // Fill the open tab's result area without making a card, so the screen never starts empty.
        Dispatcher.UIThread.Post(() => _ = SolveAsync(_state.Tool, addCard: false), DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ public surface (also used by tests)

    /// <summary>The open tool.</summary>
    public SolverTool CurrentTool => _state.Tool;

    /// <summary>The cards, oldest first.</summary>
    public IReadOnlyList<SolverCard> Cards => _state.Cards;

    /// <summary>The last result of each tool that has one.</summary>
    public IReadOnlyDictionary<SolverTool, SolveOutcome> Outcomes => _outcomes;

    /// <summary>The engine link (for tests and tools).</summary>
    public SolverEngine Engine => _engine;

    /// <inheritdoc />
    public bool IsBusy => _running is not null;

    /// <inheritdoc />
    public void FocusFirst()
    {
        Control? target = _state.Tool switch
        {
            SolverTool.Polynomial => _coefBoxes[_state.Polynomial.Degree],
            SolverTool.LinearSystem => _state.Linear.Typed ? _linearEditors[0] : _gridBoxes[0, 0],
            SolverTool.NonlinearSystem => _nonlinearEditors[0],
            SolverTool.Equation => _equationEditor,
            _ => _inequalityEditor,
        };
        target?.Focus();
    }

    /// <summary>
    /// Accepts an <see cref="InputRequest"/> (or a string): an inequality goes to INEQUALITY, a list of equations to
    /// NONLINEAR SYSTEM and anything else into the equation editor. solve(eq, x) is unwrapped. With Evaluate it is solved.
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
        var route = _engine.Route(text);
        switch (route.Tool)
        {
            case SolverTool.Inequality:
                _state.Inequality.Unknown = route.Unknown;
                _inequalityEditor.Text = route.Lines[0];
                break;
            case SolverTool.NonlinearSystem:
                var lines = route.Lines.Take(NonlinearSystemRequest.Max).ToList();
                _state.Nonlinear.Count = Math.Max(1, lines.Count);
                for (int i = 0; i < NonlinearSystemRequest.Max; i++) _state.Nonlinear.Lines[i] = i < lines.Count ? lines[i] : "";
                _state.Nonlinear.Guesses.Clear();
                LoadNonlinear();
                break;
            default:
                _state.Equation.Unknown = route.Unknown;
                _equationEditor.Text = route.Lines[0];
                break;
        }
        SelectTool(route.Tool, preview: false);
        FocusFirst();
        SaveState();
        // Evaluate makes a card; otherwise the result area is refreshed so it matches the new input.
        _ = SolveAsync(route.Tool, addCard: evaluate);
    }

    /// <summary>Opens a tool's tab.</summary>
    public void SelectTool(SolverTool tool) => SelectTool(tool, preview: true);

    private void SelectTool(SolverTool tool, bool preview)
    {
        _state.Tool = tool;
        foreach (var (t, tab) in _tabs) tab.IsChecked = t == tool;
        foreach (var (t, page) in _pages) page.IsVisible = t == tool;
        _pageScroll.Offset = default;
        SaveState();
        if (preview && !_outcomes.ContainsKey(tool)) _ = SolveAsync(tool, addCard: false);
    }

    /// <summary>Solves the open tool and adds a card.</summary>
    public Task SolveAsync() => SolveAsync(_state.Tool);

    /// <summary>
    /// Solves a tool off the UI thread and shows the result; with <paramref name="addCard"/> the result also becomes a
    /// card. A WORKING card appears after 250 ms; Escape (or 30 seconds) stops the work.
    /// </summary>
    public async Task SolveAsync(SolverTool tool, bool addCard = true)
    {
        if (_running is not null)
        {
            // A solve the user asked for is never interrupted; a background refresh gives way to one.
            if (_runningAddsCard || !addCard) return;
            var previous = _runningDone?.Task;
            _running.Cancel();
            if (previous is not null) await previous;
            if (_running is not null) return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runningDone = done;
        try
        {
            await RunJob(tool, addCard);
        }
        finally
        {
            if (ReferenceEquals(_runningDone, done)) _runningDone = null;
            done.TrySetResult();
        }
    }

    private async Task RunJob(SolverTool tool, bool addCard)
    {
        object request = tool switch
        {
            SolverTool.Polynomial => _state.Polynomial.Clone(),
            SolverTool.LinearSystem => _state.Linear.Clone(),
            SolverTool.NonlinearSystem => _state.Nonlinear.Clone(),
            SolverTool.Equation => _state.Equation.Clone(),
            _ => _state.Inequality.Clone(),
        };
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _running = cts;
        _runningAddsCard = addCard;
        Card? working = null;
        var timer = DispatcherTimer.RunOnce(() =>
        {
            ShowWorking(tool);
            if (!addCard) return;
            working = WorkingCard(tool);
            _cardList.Children.Insert(0, working);
            _cardScroll.Offset = default;
        }, TimeSpan.FromMilliseconds(250));

        SolveOutcome? outcome;
        var token = cts.Token;
        var task = Task.Run(() => Solve(tool, request, token), token);
        try
        {
            outcome = await task;
        }
        catch (OperationCanceledException)
        {
            outcome = null;
        }
        finally
        {
            // A result that arrives after Escape is not shown: the user asked to stop.
            if (cts.IsCancellationRequested) outcome = null;
            timer.Dispose();
            cts.Dispose();
            _running = null;
        }
        if (working is not null) _cardList.Children.Remove(working);

        if (outcome is null)
        {
            HideWorking(tool);
            if (addCard)
            {
                _s.ShowStatus("Stopped: the solve took too long or was cancelled");
                AddCard(new SolverCard
                {
                    Tool = tool,
                    Title = "STOPPED",
                    Input = SolverTools.Name(tool),
                    Lines = ["Stopped before an answer was found."],
                    IsError = true,
                    Request = Snapshot(tool, request),
                });
            }
            return;
        }
        _outcomes[tool] = outcome;
        ShowOutcome(tool, outcome);
        if (addCard)
        {
            AddCard(outcome.Card);
            _s.ShowStatus(outcome.IsError ? outcome.Error!.Message : $"Solved the {SolverTools.Name(tool)}: {outcome.Summary.ToLowerInvariant()}");
        }
    }

    /// <summary>Replaces the engine call (tests use it to simulate slow work); null in the app.</summary>
    internal Func<SolverTool, object, CancellationToken, SolveOutcome>? SolveHook { get; set; }

    private SolveOutcome Solve(SolverTool tool, object request, CancellationToken ct)
    {
        try
        {
            if (SolveHook is { } hook) return hook(tool, request, ct);
            return tool switch
            {
                SolverTool.Polynomial => _engine.SolvePolynomial((PolynomialRequest)request, ct),
                SolverTool.LinearSystem => _engine.SolveLinearSystem((LinearSystemRequest)request, ct),
                SolverTool.NonlinearSystem => _engine.SolveNonlinearSystem((NonlinearSystemRequest)request, ct),
                SolverTool.Equation => _engine.SolveEquation((EquationRequest)request, ct),
                _ => _engine.SolveInequality((InequalityRequest)request, ct),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An engine failure must never crash the screen; it becomes an error card.
            var error = new SolverException("This could not be solved: " + ex.Message);
            return new EquationOutcome
            {
                Request = new EquationRequest(),
                Error = error,
                Card = new SolverCard { Tool = tool, Title = "NEEDS A FIX", Input = SolverTools.Name(tool), Lines = [error.Message], IsError = true, Request = Snapshot(tool, request) },
            };
        }
    }

    private static SolverSnapshot Snapshot(SolverTool tool, object request) => tool switch
    {
        SolverTool.Polynomial => new SolverSnapshot { Polynomial = (PolynomialRequest)request },
        SolverTool.LinearSystem => new SolverSnapshot { Linear = (LinearSystemRequest)request },
        SolverTool.NonlinearSystem => new SolverSnapshot { Nonlinear = (NonlinearSystemRequest)request },
        SolverTool.Equation => new SolverSnapshot { Equation = (EquationRequest)request },
        _ => new SolverSnapshot { Inequality = (InequalityRequest)request },
    };

    private void ShowOutcome(SolverTool tool, SolveOutcome outcome)
    {
        switch (outcome)
        {
            case PolynomialOutcome p: ShowPolynomial(p); break;
            case LinearSystemOutcome l: ShowLinear(l); break;
            case NonlinearSystemOutcome n: ShowNonlinear(n); break;
            case InequalityOutcome i: ShowInequality(i); break;
            case EquationOutcome e when tool == SolverTool.Equation: ShowEquation(e); break;
            default: ShowFailure(tool, outcome); break;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _running is not null)
        {
            e.Handled = true;
            _running.Cancel();
        }
    }

    // ------------------------------------------------------------------ layout pieces

    private Border TabStrip()
    {
        var row = Ui.Row(6);
        foreach (var tool in SolverTools.All)
        {
            var tab = Ui.Toggle(SolverTools.Label(tool), $"{SolverTools.Name(tool)} tool", minHeight: 40);
            tab.Classes.Add("shadow");
            tab.Click += (_, _) =>
            {
                SelectTool(tool);
                FocusFirst();
            };
            _tabs[tool] = tab;
            row.Children.Add(tab);
        }
        var strip = new Border { Child = row };
        strip.Classes.Add("tabstrip");
        AutomationProperties.SetName(strip, "Solver type");
        return strip;
    }

    /// <summary>A page: the inputs on top, the result area below.</summary>
    private static StackPanel Page() => new() { Spacing = 14, Margin = new Thickness(16) };

    // ------------------------------------------------------------------ state

    /// <summary>Stores the inputs and cards in the preferences (and on disk when <paramref name="toDisk"/>).</summary>
    public void SaveState(bool toDisk = false)
    {
        if (_loading) return;
        _s.Prefs.SetModeState(ModeId, _state);
        if (toDisk) _s.Prefs.Save();
    }
}
