using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Calcy.Modes.Code.Editor;
using Calcy.Modes.Code.Programs;
using Calcy.Modes.Code.Views;
using Calcy.Scripting;
using Calcy.UI.Services;

namespace Calcy.Modes.Code;

/// <summary>
/// The Code screen (design board Code.dc.html): the editor with file tabs, RUN and STOP on the left; the canvas (with graph
/// and variables views) and the runs console with its REPL line on the right. Programs, the open file, editor positions and
/// console history are kept in the preferences.
/// </summary>
public sealed partial class CodeView : UserControl, IBusy, IFocusFirst, IReceivesPayload
{
    internal const string ModeId = "code";

    /// <summary>Below this width the canvas and console move under the editor.</summary>
    public const double NarrowWidth = 1000;

    private readonly AppServices _s;
    private readonly ScriptHost _host;
    private readonly ProgramStore _store = new();
    private readonly CodeEditor _editor = new();
    private readonly Debouncer _saveLater;
    private readonly Grid _root = new();
    private readonly ScrollViewer _narrowScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Dictionary<string, EditorViewState> _views = [];
    private Control _editorSection = null!;
    private Control _canvasPanel = null!;
    private Control _runsPanel = null!;
    private ProgramFile _current = null!;
    private bool _sourceDirty;
    private bool _restoring;
    private bool? _narrow;
    private int _tier = -1;

    /// <summary>Creates the screen on the shared services (session, theme, preferences).</summary>
    public CodeView(AppServices services)
    {
        _s = services;
        _host = new ScriptHost(services.Session, ScriptHostOptions.Default with { AnimateTurtle = true });
        _saveLater = new Debouncer(TimeSpan.FromMilliseconds(500), SaveState);
        AddStyles();

        _editorSection = BuildEditorSection();
        _canvasPanel = BuildSidePanel();
        _runsPanel = BuildRunsPanel();
        Content = _root;
        _root.Margin = new Thickness(16);

        _editor.TextChanged += (_, _) =>
        {
            if (_restoring) return;
            _sourceDirty = true;
            if (IsFindVisible) UpdateFindCount();
            _saveLater.Trigger();
        };
        _editor.CaretMoved += (_, _) =>
        {
            UpdateCaretStatus();
            if (!_restoring) _saveLater.Trigger();
        };
        _editor.FindRequested += (_, _) => ShowFind();
        _store.Changed += (_, _) => RebuildTabs();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) UpdateLayoutMode();
        };

        _host.Output += OnHostOutput;
        _host.InputProvider = OnInputRequested;
        _host.Graphs.Changed += (_, _) => _graphsDirty = true;
        _flush.Tick += (_, _) => Flush();

        RestoreState();
        UpdateLayoutMode();
        UpdateRunState();
        // The module reference is read from the modules' docstrings by running Python once; do it off the UI thread.
        _ = Task.Run(() => ScriptHost.Reference);
    }

    /// <summary>The code editor.</summary>
    public CodeEditor Editor => _editor;

    /// <summary>The scripting host that runs the programs.</summary>
    public ScriptHost Host => _host;

    /// <summary>The user's programs.</summary>
    public ProgramStore Store => _store;

    /// <summary>The open program.</summary>
    public ProgramFile CurrentProgram => _current;

    /// <summary>True when the canvas and console sit under the editor.</summary>
    public bool IsNarrow => _narrow == true;

    /// <inheritdoc />
    public bool IsBusy => _running || _replRunning || _saveLater.Pending || !_queue.IsEmpty;

    /// <inheritdoc />
    public void FocusFirst() => _editor.FocusEditor();

    /// <inheritdoc />
    public void Receive(object payload)
    {
        if (payload is not InputRequest r || string.IsNullOrWhiteSpace(r.Text)) return;
        SyncSource();
        var program = _store.Add("scratch.py", r.Text.Replace("\r\n", "\n"), _current);
        Open(program);
        if (r.Evaluate) _ = RunCurrentAsync();
    }

    // ------------------------------------------------------------------ layout

    /// <summary>Below this width the canvas and console stack under each other as well.</summary>
    public const double TinyWidth = 700;

    private void UpdateLayoutMode()
    {
        double width = Bounds.Width;
        int tier = width <= 0 || width >= NarrowWidth ? 0 : width >= TinyWidth ? 1 : 2;
        if (_tier == tier) return;
        _tier = tier;
        _narrow = tier > 0;
        Detach(_editorSection);
        Detach(_canvasPanel);
        Detach(_runsPanel);
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        _root.RowDefinitions.Clear();
        _narrowScroll.Content = null;
        _canvasPanel.Margin = new Thickness(0);
        _runsPanel.Margin = new Thickness(0);
        ArrangeFooter(tier == 2);
        if (tier == 0)
        {
            // The design board: editor on the left, canvas above the console on the right.
            _editorSection.Height = double.NaN;
            _runsPanel.Height = double.NaN;
            _canvasPanel.MaxWidth = 420;
            _root.ColumnDefinitions = new ColumnDefinitions("*,16,Auto");
            var side = new DockPanel { Width = 420 };
            DockPanel.SetDock(_canvasPanel, Dock.Top);
            _runsPanel.Margin = new Thickness(0, 16, 0, 0);
            side.Children.Add(_canvasPanel);
            side.Children.Add(_runsPanel);
            Grid.SetColumn(_editorSection, 0);
            Grid.SetColumn(side, 2);
            _root.Children.Add(_editorSection);
            _root.Children.Add(side);
            return;
        }
        // Narrow windows wrap like the design's flex layout: the editor on top, the rest below, and the page scrolls.
        var stack = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 14, 10) };
        _editorSection.Height = 440;
        _canvasPanel.MaxWidth = double.PositiveInfinity;
        stack.Children.Add(_editorSection);
        if (tier == 1)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
            _runsPanel.Height = double.NaN;
            _canvasPanel.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(_canvasPanel, 0);
            Grid.SetColumn(_runsPanel, 2);
            row.Children.Add(_canvasPanel);
            row.Children.Add(_runsPanel);
            row.Height = 400;
            stack.Children.Add(row);
        }
        else
        {
            _runsPanel.Height = 380;
            stack.Children.Add(_canvasPanel);
            stack.Children.Add(_runsPanel);
        }
        _narrowScroll.Content = stack;
        _root.Children.Add(_narrowScroll);
    }

    private static void Detach(Control c)
    {
        switch (c.Parent)
        {
            case Panel p:
                p.Children.Remove(c);
                break;
            case ContentControl cc when cc.Content == c:
                cc.Content = null;
                break;
            case Decorator d when d.Child == c:
                d.Child = null;
                break;
        }
    }

    // ------------------------------------------------------------------ programs

    /// <summary>Shows a program in the editor (keeping the caret position of the one that was open).</summary>
    public void Open(ProgramFile program)
    {
        if (_current is not null)
        {
            SyncSource();
            _views[_current.Name] = _editor.ViewState;
        }
        _current = program;
        _restoring = true;
        try
        {
            _editor.Text = program.Source;
            _editor.RestoreViewState(_views.GetValueOrDefault(program.Name));
        }
        finally
        {
            _restoring = false;
        }
        _sourceDirty = false;
        RebuildTabs();
        UpdateCaretStatus();
        _saveLater.Trigger();
    }

    /// <summary>Opens a program by name; false when there is none.</summary>
    public bool Open(string name)
    {
        var p = _store.Find(name);
        if (p is null) return false;
        Open(p);
        return true;
    }

    /// <summary>Creates a new empty program and opens it.</summary>
    public ProgramFile NewProgram()
    {
        SyncSource();
        var p = _store.Add("program.py", "", _current);
        Open(p);
        _editor.FocusEditor();
        return p;
    }

    /// <summary>Copies an example into a new program and opens it.</summary>
    public ProgramFile OpenExample(ExampleProgram example)
    {
        SyncSource();
        var p = _store.Add(example.FileName, example.Source.Replace("\r\n", "\n"), _current);
        Open(p);
        return p;
    }

    /// <summary>Renames a program; false when the name cannot be used.</summary>
    public bool Rename(ProgramFile program, string name)
    {
        var old = program.Name;
        if (!_store.Rename(program, name)) return false;
        if (_views.Remove(old, out var v)) _views[program.Name] = v;
        _saveLater.Trigger();
        return true;
    }

    /// <summary>Copies a program and opens the copy.</summary>
    public ProgramFile Duplicate(ProgramFile program)
    {
        SyncSource();
        var copy = _store.Duplicate(program);
        Open(copy);
        return copy;
    }

    /// <summary>Deletes a program and opens a neighbour.</summary>
    public void Delete(ProgramFile program)
    {
        SyncSource();
        _views.Remove(program.Name);
        var next = _store.Delete(program);
        if (program == _current || !_store.Programs.Contains(_current)) Open(next);
        else RebuildTabs();
        _s.ShowStatus($"Deleted {program.Name}");
        _saveLater.Trigger();
    }

    private void SyncSource()
    {
        if (_current is null || !_sourceDirty) return;
        _current.Source = _editor.Text;
        _sourceDirty = false;
    }

    // ------------------------------------------------------------------ persistence

    private void RestoreState()
    {
        var state = _s.Prefs.GetModeState<CodeState>(ModeId);
        _restoring = true;
        try
        {
            if (state is { Programs.Count: > 0 })
            {
                _store.Load(state.Programs);
                foreach (var (k, v) in state.Views) _views[k] = v;
                _history.AddRange(state.History.TakeLast(MaxHistory));
                _runNumber = Math.Max(1, state.NextRun);
                foreach (var saved in state.Runs.Take(MaxCards).Reverse()) AddCard(RunCard.FromSaved(saved, _s.Session));
                _sidePanel = state.Panel switch { "graph" => SidePanel.Graph, "vars" => SidePanel.Variables, _ => SidePanel.Canvas };
            }
            else
            {
                _store.Load(Examples.Starters.Select(n => Examples.Find(n)!).Select(e => new ProgramFile { Name = e.FileName, Source = e.Source.Replace("\r\n", "\n") }));
            }
        }
        finally
        {
            _restoring = false;
        }
        Open(_store.Find(state?.Open) ?? _store.Programs[0]);
        ShowSide(_sidePanel);
    }

    private void SaveState()
    {
        if (_restoring) return;
        SyncSource();
        if (_current is not null) _views[_current.Name] = _editor.ViewState;
        var names = _store.Programs.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var state = new CodeState
        {
            Programs = _store.Programs.Select(p => new ProgramFile { Name = p.Name, Source = p.Source }).ToList(),
            Open = _current?.Name,
            Views = _views.Where(kv => names.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            History = _history.TakeLast(MaxHistory).ToList(),
            Runs = _cards.Children.OfType<RunCard>().Where(c => !c.IsRunning).Take(MaxCards).Select(c => new SavedRun
            {
                Number = c.Number,
                File = c.IsRepl ? "REPL" : c.FileName,
                Meta = c.Meta ?? "",
                Output = c.SaveText(),
                Failed = c.IsError,
            }).ToList(),
            NextRun = _runNumber,
            Panel = _sidePanel switch { SidePanel.Graph => "graph", SidePanel.Variables => "vars", _ => "canvas" },
        };
        _s.Prefs.SetModeState(ModeId, state);
        _s.Prefs.Save();
    }

    /// <summary>Writes the state now (normally it is saved half a second after a change).</summary>
    public void SaveNow() => SaveState();

    // ------------------------------------------------------------------ styles

    private void AddStyles()
    {
        Selector Part(Selector? s) => s!.Template().OfType<Border>().Name("PART_BorderElement");
        foreach (var cls in new[] { "repl", "inline", "find" })
        {
            var thickness = cls == "repl" ? new Thickness(0) : new Thickness(0, 0, 0, 2);
            foreach (var state in new[] { "", ":pointerover", ":focus" })
            {
                object border = cls == "repl" ? Brushes.Transparent : new DynamicResourceExtension(state == "" ? "Calcy.Muted" : "Calcy.Ink");
                object background = cls == "find" && state == ":focus" ? new DynamicResourceExtension("Calcy.Card") : Brushes.Transparent;
                Styles.Add(new Style(x => Part(state == "" ? x.OfType<TextBox>().Class(cls) : x.OfType<TextBox>().Class(cls).Class(state)))
                {
                    Setters =
                    {
                        new Setter(Border.BackgroundProperty, background),
                        new Setter(Border.BorderBrushProperty, border),
                        new Setter(Border.BorderThicknessProperty, thickness),
                    },
                });
            }
        }
        Styles.Add(new Style(x => x.OfType<Button>().Class("link").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("link").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension("Calcy.Grid")) },
        });
    }

    /// <summary>Runs an action on the UI thread once calls to <see cref="Trigger"/> stop for a moment.</summary>
    private sealed class Debouncer(TimeSpan delay, Action action)
    {
        private int _generation;

        public bool Pending { get; private set; }

        public void Trigger()
        {
            int g = ++_generation;
            Pending = true;
            Task.Delay(delay).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (g != _generation) return;
                Pending = false;
                action();
            }), TaskScheduler.Default);
        }
    }
}
