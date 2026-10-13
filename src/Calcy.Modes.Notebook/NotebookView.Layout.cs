using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Modes.Notebook.Model;
using Calcy.Modes.Notebook.Views;
using Calcy.UI.Controls;

namespace Calcy.Modes.Notebook;

public sealed partial class NotebookView
{
    /// <summary>Below this width the sidebar folds into the MENU button.</summary>
    public const double NarrowWidth = 860;

    private readonly StackPanel _notebookList = new();
    private readonly StackPanel _outline = new();
    private readonly List<(string Label, CellView View, int Heading)> _outlineTargets = [];
    private readonly HashSet<NotebookDocument> _dismissedTrust = [];
    private Grid _root = null!;
    private Border _sidebar = null!;
    private ScrollViewer _sidebarScroll = null!;
    private Border _docPanel = null!;
    private Border _chip = null!;
    private TextBlock _chipText = null!;
    private PixelButton _runAll = null!;
    private PixelButton _clear = null!;
    private PixelButton _menu = null!;
    private PixelButton _deleteNotebook = null!;
    private Border _trustBanner = null!;
    private TextBlock _trustText = null!;
    private Border _undoBanner = null!;
    private TextBlock _undoText = null!;
    private TextBox _renameBox = null!;
    private Control _notebookActions = null!;
    private DispatcherTimer? _deleteArm;
    private bool _narrow;

    /// <summary>True when the sidebar is folded into the MENU button.</summary>
    public bool IsNarrow => _narrow;

    private Control BuildLayout()
    {
        _sidebar = BuildSidebar();
        _sidebarScroll = new ScrollViewer
        {
            Content = _sidebar,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 8),
        };

        var toolbar = BuildToolbar();
        var banners = new StackPanel { Children = { BuildTrustBanner(), BuildUndoBanner() } };
        var dock = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(banners, Dock.Top);
        dock.Children.Add(toolbar);
        dock.Children.Add(banners);
        dock.Children.Add(_scroll);
        _docPanel = new Border { Child = dock, BorderThickness = new Thickness(4), Margin = new Thickness(0, 0, 6, 6), ClipToBounds = false };
        _docPanel.Res(Border.BackgroundProperty, "Calcy.Paper").Res(Border.BorderBrushProperty, "Calcy.Line").Res(Border.BoxShadowProperty, "Calcy.ShadowLarge");
        Avalonia.Automation.AutomationProperties.SetName(_docPanel, "Notebook");
        _docPanel.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && ((Rect)e.OldValue!).Width is var old && (old < 1000) != (_docPanel.Bounds.Width < 1000)) UpdateToolbar();
        };

        _root = new Grid { Margin = new Thickness(16), ColumnDefinitions = new ColumnDefinitions("220,16,*") };
        Grid.SetColumn(_sidebarScroll, 0);
        Grid.SetColumn(_docPanel, 2);
        _root.Children.Add(_sidebarScroll);
        _root.Children.Add(_docPanel);
        _root.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) ApplyWidth(_root.Bounds.Width);
        };
        return _root;
    }

    private void ApplyWidth(double width)
    {
        bool narrow = width < NarrowWidth - 32;
        double side = width >= 1180 ? 230 : 210;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            if (narrow)
            {
                _sidebarScroll.Content = null;
                _sidebarScroll.IsVisible = false;
            }
            else
            {
                if (_menu.Flyout is Flyout f)
                {
                    f.Hide();
                    if (f.Content is ScrollViewer holder) holder.Content = null;
                    f.Content = null;
                }
                _sidebarScroll.Content = _sidebar;
                _sidebarScroll.IsVisible = true;
            }
            _menu.IsVisible = narrow;
        }
        _root.ColumnDefinitions[0].Width = new GridLength(narrow ? 0 : side);
        _root.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 16);
    }

    // ================================================================== sidebar

    private Border BuildSidebar()
    {
        var add = Ui.Button("+ NEW", "Start a new notebook");
        add.Click += (_, _) => _library.Create();
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 8, 8) };
        var rename = Ui.Button("RENAME", "Rename this notebook");
        rename.Click += (_, _) => StartRename();
        var copy = Ui.Button("COPY", "Duplicate this notebook");
        copy.Click += (_, _) => _library.Duplicate(Document);
        _deleteNotebook = Ui.Button("DELETE", "Delete this notebook (click twice)");
        _deleteNotebook.Click += (_, _) => DeleteNotebookClicked();
        foreach (var b in new[] { rename, copy, _deleteNotebook })
        {
            b.Margin = new Thickness(0, 0, 6, 6);
            actions.Children.Add(b);
        }
        _notebookActions = actions;
        _renameBox = new TextBox { FontSize = 15, Margin = new Thickness(12, 10, 12, 10), IsVisible = false, MinHeight = 34 };
        _renameBox.Res(TextBox.FontFamilyProperty, "Calcy.Font.Body");
        Avalonia.Automation.AutomationProperties.SetName(_renameBox, "Notebook name");
        _renameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) FinishRename(true);
            else if (e.Key == Key.Escape) FinishRename(false);
        };
        _renameBox.LostFocus += (_, _) => FinishRename(true);

        var export = new StackPanel { Spacing = 8, Margin = new Thickness(14, 10, 14, 12) };
        export.Children.Add(Ui.Label("Export", 11));
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        var md = ExportButton("Markdown", "Copy the notebook as Markdown (maths as LaTeX)");
        md.Click += async (_, _) => await CopyMarkdownAsync();
        var tex = ExportButton("LaTeX", "Copy the notebook as a LaTeX document");
        tex.Click += async (_, _) => await CopyLatexAsync();
        var png = ExportButton("PNG", "Copy the selected graph (or the first one) as an image");
        png.Click += async (_, _) =>
        {
            var graph = _selected as GraphCellView ?? _views.OfType<GraphCellView>().FirstOrDefault();
            if (graph is null) _s.ShowStatus("This notebook has no graph cell");
            else await CopyGraphImageAsync(graph.Canvas);
        };
        foreach (var b in new[] { md, tex, png }) buttons.Children.Add(b);
        export.Children.Add(buttons);

        var stack = new StackPanel
        {
            Children =
            {
                SectionHeader("Notebooks", add, top: false),
                _notebookList,
                _renameBox,
                actions,
                SectionHeader("Outline", null, top: true),
                _outline,
                export,
            },
        };
        var panel = new Border { Child = stack, BorderThickness = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
        panel.Res(Border.BackgroundProperty, "Calcy.Paper").Res(Border.BorderBrushProperty, "Calcy.Line").Res(Border.BoxShadowProperty, "Calcy.ShadowLarge");
        Avalonia.Automation.AutomationProperties.SetName(panel, "Notebooks and outline");
        return panel;
    }

    private static PixelButton ExportButton(string text, string tip)
    {
        var b = Ui.Button(text, tip, "");
        b.Classes.Add("flat");
        b.MinHeight = 34;
        b.Margin = new Thickness(0, 0, 6, 6);
        return b;
    }

    private static Border SectionHeader(string title, Control? right, bool top)
    {
        var text = new PixelText { Text = title.ToUpperInvariant(), PixelSize = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Res(PixelText.ForegroundProperty, "Calcy.Ink");
        var dock = new DockPanel();
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        dock.Children.Add(text);
        var b = new Border { Child = dock, BorderThickness = new Thickness(0, top ? 4 : 0, 0, 4), Padding = new Thickness(14, 7, 8, 7), MinHeight = 40 };
        b.Res(Border.BackgroundProperty, "Calcy.Head").Res(Border.BorderBrushProperty, "Calcy.Line");
        return b;
    }

    private void RefreshSidebar()
    {
        RefreshNotebookList();
        RefreshOutline();
    }

    private void RefreshNotebookList()
    {
        _notebookList.Children.Clear();
        foreach (var doc in _library.Notebooks)
        {
            var d = doc;
            _notebookList.Children.Add(new SidebarRow(doc.Name, doc == Document, () =>
            {
                CloseMenu();
                _library.Select(d);
            }, bold: true));
        }
    }

    private void RefreshOutline()
    {
        _outline.Children.Clear();
        _outlineTargets.Clear();
        foreach (var view in _views)
        {
            if (view is TextCellView t)
            {
                int k = 0;
                foreach (var (level, text) in ((TextCell)t.Cell).Markdown.Headings)
                {
                    if (level <= 3 && text.Length > 0) _outlineTargets.Add((text, view, k));
                    k++;
                }
            }
            else _outlineTargets.Add((view.Cell.Caption ?? NotebookCell.KindName(view.Cell.Kind), view, -1));
        }
        foreach (var target in _outlineTargets)
        {
            var t = target;
            _outline.Children.Add(new SidebarRow(t.Label, false, () => GoTo(t)));
        }
        if (_outlineTargets.Count == 0)
            _outline.Children.Add(new Border { Padding = new Thickness(14, 10), Child = Ui.Body("Headings and cells appear here.", 14, "Calcy.Muted") });
    }

    private void GoTo((string Label, CellView View, int Heading) target)
    {
        CloseMenu();
        Select(target.View, focus: false);
        Control c = target.View;
        if (target.Heading >= 0 && target.View is TextCellView t && target.Heading < t.Preview.Headings.Count && t.Preview.IsVisible)
            c = t.Preview.Headings[target.Heading];
        if (c.TranslatePoint(default, _cellsPanel) is { } p)
        {
            double max = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
            _scroll.Offset = new Vector(0, Math.Clamp(p.Y + 22 - 10, 0, max));
        }
        target.View.Focus();
    }

    private void StartRename()
    {
        _renameBox.Text = Document.Name;
        _renameBox.IsVisible = true;
        _notebookActions.IsVisible = false;
        Dispatcher.UIThread.Post(() =>
        {
            _renameBox.Focus();
            _renameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void FinishRename(bool apply)
    {
        if (!_renameBox.IsVisible) return;
        _renameBox.IsVisible = false;
        _notebookActions.IsVisible = true;
        if (apply && !string.IsNullOrWhiteSpace(_renameBox.Text)) _library.Rename(Document, _renameBox.Text!);
    }

    private void DeleteNotebookClicked()
    {
        if (_deleteArm is null)
        {
            _deleteNotebook.Content = "SURE?";
            _deleteNotebook.Classes.Add("err");
            _deleteArm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _deleteArm.Tick += (_, _) => DisarmDelete();
            _deleteArm.Start();
            _s.ShowStatus($"Click SURE? to delete \"{Document.Name}\"");
            return;
        }
        DisarmDelete();
        var name = Document.Name;
        _library.Delete(Document);
        _s.ShowStatus($"Deleted \"{name}\"");
    }

    private void DisarmDelete()
    {
        _deleteArm?.Stop();
        _deleteArm = null;
        _deleteNotebook.Content = "DELETE";
        _deleteNotebook.Classes.Remove("err");
    }

    private void CloseMenu()
    {
        if (_menu.Flyout is Flyout f) f.Hide();
    }

    // ================================================================== toolbar

    private Control BuildToolbar()
    {
        var bar = new FlowBar { Spacing = 6, LineSpacing = 6 };
        _menu = Ui.Button("MENU", "Notebooks, outline and export");
        _menu.IsVisible = false;
        _menu.Flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        _menu.Click += (_, _) =>
        {
            if (_menu.Flyout is Flyout f)
            {
                _sidebarScroll.Content = null;
                if (f.Content is ScrollViewer old) old.Content = null;
                f.Content = new ScrollViewer { Content = _sidebar, MaxHeight = Math.Max(300, Bounds.Height - 80), Width = 260 };
            }
        };
        bar.Children.Add(_menu);
        var add = Ui.Label("Add", 12);
        add.Margin = new Thickness(0, 0, 4, 0);
        bar.Children.Add(add);
        foreach (var kind in Enum.GetValues<CellKind>())
        {
            var k = kind;
            var b = Ui.Button("+ " + NotebookCell.KindName(kind), $"Add a {NotebookCell.KindName(kind).ToLowerInvariant()} cell below the selected one");
            b.MinHeight = 34;
            b.Click += (_, _) => InsertCell(k, null);
            bar.Children.Add(b);
        }
        _runAll = Ui.Button("RUN ALL", "Run every code cell from the top with fresh Python variables", "primary");
        _runAll.MinHeight = 34;
        _runAll.Click += async (_, _) =>
        {
            if (_runtime.RunningCell is not null) StopCode();
            else await RunAllAsync();
        };
        _clear = Ui.Button("CLEAR", "Clear the output of every code cell");
        _clear.MinHeight = 34;
        _clear.Click += (_, _) => ClearOutputs();
        _chipText = Ui.Label("LIVE: cells update together", 11);
        _chip = new Border { Child = _chipText, BorderThickness = new Thickness(2), Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center };
        _chip.Res(Border.BorderBrushProperty, "Calcy.Line");
        foreach (var c in new Control[] { _runAll, _clear, _chip })
        {
            FlowBar.SetRight(c, true);
            bar.Children.Add(c);
        }
        var border = new Border { Child = bar, Padding = new Thickness(12, 8), BorderThickness = new Thickness(0, 0, 0, 4) };
        border.Res(Border.BackgroundProperty, "Calcy.Key").Res(Border.BorderBrushProperty, "Calcy.Line");
        return border;
    }

    private void UpdateToolbar()
    {
        if (_chip is null) return;
        bool running = _runtime.RunningCell is not null;
        _runAll.Content = running ? "STOP" : "RUN ALL";
        _runAll.Classes.Set("primary", !running);
        _runAll.Classes.Set("err", running);
        ToolTip.SetTip(_runAll, running ? "Stop the running code (Esc)" : "Run every code cell from the top with fresh Python variables");
        _clear.IsEnabled = !running;
        int stale = _runtime.StaleCodeCells.Count + _runtime.UnrunCodeCells.Count;
        string text, token;
        // On a narrow document the chip keeps to a word so the toolbar stays on one line.
        bool compact = _docPanel.Bounds.Width is > 0 and < 1000;
        if (running)
        {
            (text, token) = (compact ? "RUNNING" : "RUNNING CODE", "Calcy.Sel");
        }
        else if (stale > 0)
        {
            (text, token) = (compact ? $"LIVE: {stale} TO RUN" : stale == 1 ? "LIVE: 1 code cell to run" : $"LIVE: {stale} code cells to run", "Calcy.Mem");
        }
        else (text, token) = (compact ? "LIVE" : "LIVE: cells update together", "Calcy.Ok");
        _chipText.Text = text;
        _chip.Res(Border.BackgroundProperty, token);
        ToolTip.SetTip(_chip, stale > 0 && !running
            ? "Maths, tables, graphs and text follow every change. Code runs when you press RUN; cells marked STALE or NOT RUN are out of date."
            : "Maths, tables, graphs and text follow every change at once.");
    }

    // ================================================================== banners

    private Border Banner(TextBlock text, params Control[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons) row.Children.Add(b);
        var dock = new DockPanel();
        DockPanel.SetDock(row, Dock.Right);
        dock.Children.Add(row);
        text.Margin = new Thickness(0, 0, 10, 0);
        dock.Children.Add(text);
        var border = new Border { Child = dock, Padding = new Thickness(12, 6), BorderThickness = new Thickness(0, 0, 0, 2), IsVisible = false };
        border.Res(Border.BackgroundProperty, "Calcy.Card").Res(Border.BorderBrushProperty, "Calcy.Line");
        return border;
    }

    private Control BuildTrustBanner()
    {
        _trustText = Ui.Body("", 14);
        var run = Ui.Button("RUN ALL", "Run every code cell from the top", "ok");
        run.Click += async (_, _) => await RunAllAsync();
        var dismiss = Ui.IconButton("close", "Hide this message");
        dismiss.Click += (_, _) =>
        {
            _dismissedTrust.Add(Document);
            UpdateBanners();
        };
        _trustBanner = Banner(_trustText, run, dismiss);
        return _trustBanner;
    }

    private Control BuildUndoBanner()
    {
        _undoText = Ui.Body("", 14);
        var undo = Ui.Button("UNDO", "Bring the cell back (Ctrl+Z)", "sel");
        undo.Click += (_, _) => Undo();
        var dismiss = Ui.IconButton("close", "Hide this message");
        dismiss.Click += (_, _) => HideUndo();
        _undoBanner = Banner(_undoText, undo, dismiss);
        return _undoBanner;
    }

    private void UpdateBanners()
    {
        if (_trustBanner is null) return;
        int unrun = _runtime.UnrunCodeCells.Count;
        bool show = unrun > 0 && _runtime.RunningCell is null && !_dismissedTrust.Contains(Document) && Document.Cells.OfType<CodeCell>().All(c => c.LastRun is null);
        _trustBanner.IsVisible = show;
        if (show)
            _trustText.Text = unrun == 1
                ? "The code cell has not run. Code never runs by itself when a notebook opens: RUN ALL when you trust it."
                : $"{unrun} code cells have not run. Code never runs by itself when a notebook opens: RUN ALL when you trust it.";
    }

    private DispatcherTimer? _undoTimer;

    private void ShowUndo(string message)
    {
        _undoText.Text = message;
        _undoBanner.IsVisible = true;
        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _undoTimer.Tick += (_, _) => HideUndo();
        _undoTimer.Start();
    }

    private void HideUndo()
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        if (_undoBanner is not null) _undoBanner.IsVisible = false;
    }

    /// <summary>A row of the sidebar: the notebook list and the outline (16 px text, a 2 px rule under each row).</summary>
    private sealed class SidebarRow : Border
    {
        public SidebarRow(string text, bool selected, Action click, bool bold = false)
        {
            var label = Ui.Body(text, 16);
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            if (bold && selected) label.FontWeight = FontWeight.Bold;
            Child = label;
            Padding = new Thickness(14, 10);
            MinHeight = 44;
            BorderThickness = new Thickness(0, 0, 0, 2);
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            this.Res(BorderBrushProperty, "Calcy.Key").Res(BackgroundProperty, selected ? "Calcy.Sel" : "Calcy.Paper");
            ToolTip.SetTip(this, text);
            Avalonia.Automation.AutomationProperties.SetName(this, text);
            PointerEntered += (_, _) =>
            {
                if (!selected) this.Res(BackgroundProperty, "Calcy.Card");
            };
            PointerExited += (_, _) =>
            {
                if (!selected) this.Res(BackgroundProperty, "Calcy.Paper");
            };
            PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                click();
            };
            KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                e.Handled = true;
                click();
            };
        }
    }
}
