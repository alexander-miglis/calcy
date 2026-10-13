using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Calcy.Core;
using Calcy.Modes.Notebook.Model;
using Calcy.Modes.Notebook.Runtime;
using Calcy.Modes.Notebook.Views;
using Calcy.UI.Graph;
using Calcy.UI.Math;
using Calcy.UI.Services;

namespace Calcy.Modes.Notebook;

/// <summary>
/// The Notebook screen (design board Notebook.dc.html): the notebook list, an outline of headings and cells and the
/// export buttons on the left; the document on the right with its Add toolbar and the LIVE chip, then every cell as
/// its own card. Maths, tables, graphs and live values recalculate in order whenever something above them changes;
/// code cells run when asked and are marked stale when what they read has changed.
/// </summary>
public sealed partial class NotebookView : UserControl, IReceivesPayload, IFocusFirst, IBusy
{
    /// <summary>The mode id (also the key of the saved state).</summary>
    public const string ModeId = "notebook";

    private readonly AppServices _s;
    private readonly NotebookLibrary _library;
    private readonly Dictionary<NotebookDocument, NotebookRuntime> _runtimes = [];
    private readonly StackPanel _cellsPanel = new() { Spacing = 16, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer _scroll;
    private readonly List<CellView> _views = [];
    private readonly Debouncer _recalcLater;
    private readonly Debouncer _autoRunLater;
    private readonly Debouncer _saveLater;
    private NotebookRuntime _runtime = null!;
    private NotebookDocument? _shown;
    private CellView? _selected;
    private int _recalcRunning;

    /// <summary>Creates the screen; notebooks are restored from the preferences (the sample notebook the first time).</summary>
    public NotebookView(AppServices services)
    {
        _s = services ?? throw new ArgumentNullException(nameof(services));
        _recalcLater = new Debouncer(TimeSpan.FromMilliseconds(70), () => _ = RecalculateAsync());
        _autoRunLater = new Debouncer(TimeSpan.FromMilliseconds(350), () => _ = AutoRunAsync());
        _saveLater = new Debouncer(TimeSpan.FromMilliseconds(700), SaveState);

        _library = NotebookLibrary.FromData(LoadState());
        _library.Changed += (_, _) => OnLibraryChanged();

        var keys = Ui.Body("Shift+Enter: run and move on   Ctrl+Enter: run   Alt+Up or Down: move the cell   Ctrl+Z: undo   Esc: stop code", 12, "Calcy.Muted");
        keys.Margin = new Thickness(0, 22, 0, 0);
        keys.MaxWidth = 860;
        keys.HorizontalAlignment = HorizontalAlignment.Left;
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel { Margin = new Thickness(28, 22, 28, 28), Children = { _cellsPanel, keys } },
        };
        Content = BuildLayout();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        _s.SettingsChanged += (_, _) =>
        {
            foreach (var rt in _runtimes.Values) rt.Invalidate();
            foreach (var v in _views) v.Refresh();
        };
        ShowNotebook(_library.Current);
    }

    // ================================================================== public surface

    /// <summary>The notebooks.</summary>
    public NotebookLibrary Library => _library;

    /// <summary>The open notebook.</summary>
    public NotebookDocument Document => _library.Current;

    /// <summary>The runtime of the open notebook (its session and interpreter).</summary>
    public NotebookRuntime Runtime => _runtime;

    /// <summary>The cell views in order.</summary>
    internal IReadOnlyList<CellView> CellViews => _views;

    /// <summary>The selected cell view.</summary>
    internal CellView? SelectedView => _selected;

    /// <summary>The selected cell.</summary>
    public NotebookCell? SelectedCell => _selected?.Cell;

    internal CalcSession Session => _runtime.Session;

    internal CalcSettings Settings => _s.Settings;

    internal AppServices Services => _s;

    /// <inheritdoc />
    public bool IsBusy => _runtime.IsBusy || _recalcRunning > 0 || _recalcLater.Pending || _autoRunLater.Pending ||
                          this.GetVisualDescendants().OfType<GraphCanvas>().Any(c => c.IsEffectivelyVisible && c.Bounds.Width >= 2 && c.IsBusy);

    /// <inheritdoc />
    public void FocusFirst()
    {
        if (_views.Count == 0) return;
        Select(_views[0], focus: false);
        if (!_views[0].FocusInput()) _views[0].Focus();
    }

    /// <inheritdoc />
    public void Receive(object payload)
    {
        switch (payload)
        {
            case InputRequest r when !string.IsNullOrWhiteSpace(r.Text):
                AppendAndShow(new MathCell([r.Text.Trim()]));
                break;
            case PlotRequest p when p.Expressions.Count > 0:
                AppendAndShow(new GraphCell(p.Expressions));
                break;
        }
    }

    private void AppendAndShow(NotebookCell cell)
    {
        Document.Add(cell);
        var view = ViewOf(cell);
        if (view is null) return;
        Select(view, focus: false);
        Dispatcher.UIThread.Post(() =>
        {
            ScrollTo(view);
            view.FocusInput(atEnd: true);
        }, DispatcherPriority.Background);
    }

    /// <summary>Copies the open notebook as Markdown to the clipboard and returns the text.</summary>
    public async Task<string> CopyMarkdownAsync()
    {
        var md = NotebookExporter.ToMarkdown(Document, Session);
        bool ok = await Ui.CopyTextAsync(this, md);
        _s.ShowStatus(ok ? "Copied the notebook as Markdown" : "There is no clipboard here");
        return md;
    }

    /// <summary>Copies the open notebook as a LaTeX document to the clipboard and returns the text.</summary>
    public async Task<string> CopyLatexAsync()
    {
        var tex = NotebookExporter.ToLatex(Document, Session);
        bool ok = await Ui.CopyTextAsync(this, tex);
        _s.ShowStatus(ok ? "Copied the notebook as LaTeX" : "There is no clipboard here");
        return tex;
    }

    // ================================================================== notebooks

    private void ShowNotebook(NotebookDocument doc)
    {
        if (_shown == doc && _views.Count == doc.Cells.Count) return;
        if (_shown is not null) _shown.Changed -= OnDocumentChanged;
        foreach (var v in _views) v.Detach();
        _views.Clear();
        _cellsPanel.Children.Clear();
        _selected = null;
        _shown = doc;
        if (!_runtimes.TryGetValue(doc, out var rt))
        {
            rt = new NotebookRuntime(doc, _s.Settings);
            rt.RecalculationNeeded += OnRecalculationNeeded;
            rt.Recalculated += OnRecalculated;
            rt.RunStateChanged += (_, _) =>
            {
                UpdateToolbar();
                _saveLater.Trigger();
            };
            _runtimes[doc] = rt;
            // The first pass runs before the cells are built, so maths inputs are read with the notebook's names.
            rt.Recalculate();
        }
        _runtime = rt;
        _names = NamesKey();
        doc.Changed += OnDocumentChanged;
        SyncCellViews();
        RefreshSidebar();
        UpdateToolbar();
        UpdateBanners();
        _scroll.Offset = default;
        _ = RecalculateAsync();
    }

    private void OnLibraryChanged()
    {
        foreach (var gone in _runtimes.Keys.Where(d => !_library.Notebooks.Contains(d)).ToList())
        {
            _runtimes[gone].Dispose();
            _runtimes.Remove(gone);
        }
        if (_library.Current != _shown) ShowNotebook(_library.Current);
        RefreshSidebar();
        _saveLater.Trigger();
    }

    private void OnDocumentChanged(object? sender, NotebookChangedEventArgs e)
    {
        if (e.Kind == NotebookChangeKind.Structure) SyncCellViews();
        if (e.Kind == NotebookChangeKind.Structure || e.Cell is TextCell || e.Kind == NotebookChangeKind.Meta) RefreshOutline();
        if (e.Kind == NotebookChangeKind.Meta && e.Cell is null) RefreshNotebookList();
        _saveLater.Trigger();
    }

    private void OnRecalculationNeeded(object? sender, EventArgs e)
    {
        if (sender == _runtime) _recalcLater.Trigger();
    }

    private void OnRecalculated(object? sender, EventArgs e)
    {
        if (sender != _runtime) return;
        var names = NamesKey();
        if (names != _names)
        {
            _names = names;
            foreach (var v in _views) v.ReparseInputs();
        }
        UpdateToolbar();
        UpdateBanners();
        RefreshOutline();
        if (_runtime.AutoRunCandidates.Count > 0) _autoRunLater.Trigger();
    }

    private string _names = "";

    /// <summary>The names the notebook defines, as one string (to notice new or removed definitions).</summary>
    private string NamesKey()
    {
        var env = _runtime.Session.Env;
        return string.Join(",", env.Variables.Keys.Where(k => k != "ans").Order(StringComparer.Ordinal)) + "|" + string.Join(",", env.Functions.Keys.Order(StringComparer.Ordinal));
    }

    private async Task RecalculateAsync()
    {
        _recalcRunning++;
        UpdateToolbar();
        try
        {
            await _runtime.RecalculateAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or ArgumentException)
        {
            _s.ShowStatus("The notebook could not be recalculated: " + ex.Message);
        }
        finally
        {
            _recalcRunning--;
            UpdateToolbar();
        }
    }

    private async Task AutoRunAsync()
    {
        if (_runtime.RunningCell is not null || _recalcLater.Pending) return;
        await _runtime.RunStaleAsync();
    }

    // ================================================================== cell views

    private void SyncCellViews()
    {
        var doc = Document;
        var map = _views.ToDictionary(v => v.Cell);
        var next = new List<CellView>(doc.Cells.Count);
        foreach (var c in doc.Cells) next.Add(map.TryGetValue(c, out var v) ? v : CreateView(c));
        foreach (var gone in _views.Where(v => !next.Contains(v))) gone.Detach();
        bool same = next.SequenceEqual(_views);
        _views.Clear();
        _views.AddRange(next);
        if (!same)
        {
            _cellsPanel.Children.Clear();
            foreach (var v in _views) _cellsPanel.Children.Add(v);
            if (_views.Count == 0) _cellsPanel.Children.Add(EmptyMessage());
        }
        if (_selected is not null && !_views.Contains(_selected)) _selected = null;
    }

    private Control EmptyMessage() => Ui.Body("This notebook is empty. Add a cell with the buttons above: text, maths, a graph, a table or code.", 16, "Calcy.Muted");

    private CellView CreateView(NotebookCell cell) => cell switch
    {
        TextCell t => new TextCellView(this, t),
        MathCell m => new MathCellView(this, m),
        TableCell t => new TableCellView(this, t),
        GraphCell g => new GraphCellView(this, g),
        CodeCell c => new CodeCellView(this, c),
        _ => throw new NotSupportedException(cell.Kind.ToString()),
    };

    private CellView? ViewOf(NotebookCell cell) => _views.FirstOrDefault(v => v.Cell == cell);

    /// <summary>Selects a cell (its actions show and it gets the hard shadow).</summary>
    internal void Select(CellView view, bool focus)
    {
        if (_selected != view)
        {
            if (_selected is not null) _selected.IsSelected = false;
            _selected = view;
            view.IsSelected = true;
        }
        if (focus) view.FocusInput();
    }

    private void OnAnyGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<CellView>(includeSelf: true) is { } cell && _views.Contains(cell)) Select(cell, focus: false);
    }

    internal void FocusNext(CellView from)
    {
        int i = _views.IndexOf(from);
        if (i < 0 || i + 1 >= _views.Count) return;
        var next = _views[i + 1];
        Select(next, focus: false);
        ScrollTo(next);
        if (next is MathCellView or TableCellView) next.FocusInput();
        else next.Focus();
    }

    internal void FocusPrevious(CellView from)
    {
        int i = _views.IndexOf(from);
        if (i <= 0) return;
        var prev = _views[i - 1];
        Select(prev, focus: false);
        ScrollTo(prev);
        if (prev is MathCellView m) m.FocusInput(atEnd: true);
        else prev.Focus();
    }

    /// <summary>Scrolls the document so the control is in view (near the top).</summary>
    internal void ScrollTo(Control target)
    {
        if (target.TranslatePoint(default, _cellsPanel) is not { } p) return;
        double y = p.Y + 22 - 12;
        double max = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
        double top = _scroll.Offset.Y, bottom = top + _scroll.Viewport.Height;
        if (y >= top && y + Math.Min(target.Bounds.Height, _scroll.Viewport.Height * 0.6) <= bottom) return;
        _scroll.Offset = new Vector(0, Math.Clamp(y, 0, max));
    }

    /// <summary>Scrolls the document by a number of pixels.</summary>
    internal void ScrollBy(double dy)
    {
        double max = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
        _scroll.Offset = new Vector(0, Math.Clamp(_scroll.Offset.Y + dy, 0, max));
    }

    // ================================================================== commands

    /// <summary>A new cell of a kind, ready to type into.</summary>
    private static NotebookCell NewCell(CellKind kind) => kind switch
    {
        CellKind.Text => new TextCell(),
        CellKind.Maths => new MathCell(),
        CellKind.Table => NewTable(),
        CellKind.Graph => new GraphCell(),
        _ => new CodeCell(),
    };

    private static TableCell NewTable()
    {
        var t = new TableCell(5);
        t.AddRow("data1");
        return t;
    }

    /// <summary>Inserts a cell above or below another (or after the selected cell, or at the end) and focuses it.</summary>
    internal NotebookCell InsertCell(CellKind kind, NotebookCell? anchor, bool below = true)
    {
        var doc = Document;
        anchor ??= _selected?.Cell;
        int index = anchor is null ? doc.Cells.Count : doc.IndexOf(anchor) + (below ? 1 : 0);
        var cell = doc.Insert(index, NewCell(kind));
        HideUndo();
        if (ViewOf(cell) is { } view)
        {
            Select(view, focus: false);
            Dispatcher.UIThread.Post(() =>
            {
                ScrollTo(view);
                if (view is TextCellView t) t.StartEditing();
                else view.FocusInput();
            }, DispatcherPriority.Background);
        }
        return cell;
    }

    internal void MoveCell(NotebookCell cell, int delta)
    {
        int i = Document.IndexOf(cell);
        if (i < 0 || !Document.Move(cell, i + delta)) return;
        if (ViewOf(cell) is { } view)
        {
            Select(view, focus: false);
            Dispatcher.UIThread.Post(() =>
            {
                ScrollTo(view);
                if (!view.IsKeyboardFocusWithin) view.Focus();
            }, DispatcherPriority.Background);
        }
    }

    internal void DuplicateCell(NotebookCell cell)
    {
        var copy = Document.Duplicate(cell);
        if (ViewOf(copy) is { } view) Select(view, focus: false);
    }

    internal void DeleteCell(NotebookCell cell)
    {
        int i = Document.IndexOf(cell);
        if (i < 0) return;
        if (cell is CodeCell { IsRunning: true }) _runtime.Stop();
        Document.Remove(cell);
        ShowUndo($"Deleted a {NotebookCell.KindName(cell.Kind).ToLowerInvariant()} cell.");
        if (_views.Count > 0)
        {
            var near = _views[Math.Clamp(i, 0, _views.Count - 1)];
            Select(near, focus: false);
            near.Focus();
        }
    }

    /// <summary>Undoes the last structural change (adding, deleting, moving or duplicating a cell).</summary>
    public bool Undo()
    {
        if (!Document.Undo()) return false;
        HideUndo();
        _s.ShowStatus("Undone");
        return true;
    }

    internal async Task RunCodeAsync(CodeCell cell)
    {
        if (_runtime.RunningCell is not null)
        {
            _s.ShowStatus("Another code cell is running; press STOP first");
            return;
        }
        _autoRunLater.Cancel();
        await _runtime.RunAsync(cell);
        UpdateToolbar();
    }

    internal void StopCode() => _runtime.Stop();

    internal async Task RunAllAsync()
    {
        _autoRunLater.Cancel();
        _dismissedTrust.Add(Document);
        UpdateBanners();
        await _runtime.RunAllAsync();
        UpdateToolbar();
        UpdateBanners();
    }

    internal void ClearOutputs()
    {
        _runtime.ClearOutputs();
        _s.ShowStatus("Cleared the output of every code cell");
    }

    /// <summary>Shift+Enter (advance) or Ctrl+Enter on the selected cell.</summary>
    internal void RunSelected(bool advance)
    {
        if (_selected is not { } view) return;
        bool move = view.Run();
        if (!advance || !move) return;
        int i = _views.IndexOf(view);
        if (i == _views.Count - 1 && view.Cell.Kind is CellKind.Maths or CellKind.Code)
        {
            InsertCell(view.Cell.Kind, view.Cell, below: true);
            return;
        }
        FocusNext(view);
    }

    internal void OpenLink(string url)
    {
        if (url.StartsWith('#'))
        {
            var key = url[1..].Trim().ToLowerInvariant();
            var target = _outlineTargets.FirstOrDefault(t => Slug(t.Label) == key || t.View.Cell.Id == key);
            if (target.View is not null) GoTo(target);
            return;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null) _ = launcher.LaunchUriAsync(uri);
            _s.ShowStatus("Opening " + uri);
            return;
        }
        _s.ShowStatus("Only web links (https://...) and #headings can be opened");
    }

    private static string Slug(string text) => string.Join("-", text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    internal async Task CopyGraphImageAsync(GraphCanvas canvas)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null)
        {
            _s.ShowStatus("There is no clipboard here");
            return;
        }
        try
        {
            using var stream = new MemoryStream();
            canvas.SavePng(stream, 2);
            stream.Position = 0;
            var bitmap = new Bitmap(stream);
            await Avalonia.Input.Platform.ClipboardExtensions.SetBitmapAsync(clip, bitmap);
            _s.ShowStatus("Copied the graph as an image");
        }
        catch (Exception ex) when (ex is NotSupportedException or NotImplementedException or PlatformNotSupportedException or InvalidOperationException or IOException or ArgumentException)
        {
            _s.ShowStatus("Images cannot be copied on this system; copy the notebook as Markdown instead");
        }
    }

    internal void EditCaption(CellView view)
    {
        var box = new TextBox { Text = view.Cell.Caption ?? "", Width = 260, FontSize = 15, PlaceholderText = "Caption, for example Model" };
        box.Res(TextBox.FontFamilyProperty, "Calcy.Font.Body");
        var ok = Ui.Button("OK", "Use this caption", "ok");
        var remove = Ui.Button("REMOVE", "Remove the caption");
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12), Children = { Ui.Label("CAPTION", 11), box, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { ok, remove } } } };
        var flyout = new Flyout { Content = Ui.Box(panel, "Calcy.Paper", 0), Placement = PlacementMode.BottomEdgeAlignedLeft };
        void Apply(string? text)
        {
            view.Cell.Caption = text;
            flyout.Hide();
        }
        ok.Click += (_, _) => Apply(box.Text);
        remove.Click += (_, _) => Apply(null);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Apply(box.Text);
        };
        flyout.ShowAt(view);
        Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Input);
    }

    // ================================================================== keys

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.Enter when shift && !ctrl && !alt:
                if (InGridTemplate(e.Source)) return;
                e.Handled = true;
                RunSelected(advance: true);
                return;
            case Key.Enter when ctrl && !shift:
                e.Handled = true;
                RunSelected(advance: false);
                return;
            case Key.Up when alt && !ctrl && _selected is { } up:
                e.Handled = true;
                MoveCell(up.Cell, -1);
                return;
            case Key.Down when alt && !ctrl && _selected is { } down:
                e.Handled = true;
                MoveCell(down.Cell, 1);
                return;
            case Key.Escape when _runtime.RunningCell is not null:
                e.Handled = true;
                StopCode();
                return;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && e.Source is TextCellView t && !t.IsEditing:
                e.Handled = true;
                t.StartEditing();
                return;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && e.Source is CellView cv && cv is not TextCellView:
                e.Handled = true;
                cv.FocusInput();
                return;
            case Key.Z when ctrl && !IsTextInput(e.Source):
                e.Handled = true;
                if (shift) Document.Redo();
                else Undo();
                return;
            case Key.Y when ctrl && !IsTextInput(e.Source):
                e.Handled = true;
                Document.Redo();
                return;
            case Key.Delete when e.Source is CellView del && e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                DeleteCell(del.Cell);
                return;
        }
    }

    private static bool IsTextInput(object? source) => source is TextBox or MathEditor;

    /// <summary>True when the caret is inside a matrix or piecewise template, where Shift+Enter adds a row.</summary>
    private static bool InGridTemplate(object? source) =>
        source is MathEditor m && m.EditorState.Caret.Row.Owner?.Kind is MathTemplateKind.Matrix or MathTemplateKind.Piecewise;

    // ================================================================== saving

    private NotebookStoreData? LoadState()
    {
        try
        {
            return _s.Prefs.GetModeState<NotebookStoreData>(ModeId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private void SaveState()
    {
        _s.Prefs.SetModeState(ModeId, _library.ToData());
        _s.Prefs.Save();
    }

    /// <summary>Saves now (tests and shutdown).</summary>
    public void SaveNow()
    {
        _saveLater.Cancel();
        SaveState();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // Switching mode or closing the window must not lose the last edits.
        if (_saveLater.Pending) SaveNow();
    }
}
