using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Controls;

namespace Calcy.Modes.Notebook.Views;

/// <summary>
/// The frame of one cell (design D5: every cell is its own card): a header strip with the cell kind and caption, the
/// state chip, the run summary and the cell actions (shown on the selected or hovered cell), and the body. Text cells
/// have no card: they read as part of the document and show a dashed outline when selected.
/// </summary>
internal abstract class CellView : Border
{
    private readonly PixelText? _title;
    private readonly Border? _header;
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _chipHost = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _meta;
    private readonly PixelButton? _collapse;
    private readonly DashedFrame? _outline;
    private string _titleText = "";
    private bool _selected;
    private bool _hover;

    protected CellView(NotebookView owner, NotebookCell cell)
    {
        Owner = owner;
        Cell = cell;
        Focusable = true;
        ClipToBounds = false;
        _meta = Ui.Label("", 10);
        _meta.Margin = new Thickness(6, 0, 4, 0);
        Body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };

        if (cell.Kind == CellKind.Text)
        {
            _outline = new DashedFrame { Outline = true, Padding = new Thickness(10, 8), ShowStroke = false };
            _outline.Res(DashedFrame.StrokeProperty, "Calcy.Muted");
            var grid = new Grid();
            grid.Children.Add(Body);
            var bar = new Border { Child = _actions, BorderThickness = new Thickness(2), Padding = new Thickness(4, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -22, -4, 0) };
            bar.Res(BackgroundProperty, "Calcy.Paper").Res(BorderBrushProperty, "Calcy.Line");
            bar.Bind(IsVisibleProperty, _actions.GetObservable(IsVisibleProperty));
            grid.Children.Add(bar);
            _outline.Child = grid;
            Child = _outline;
            Margin = new Thickness(-10, 0);
        }
        else
        {
            BorderThickness = new Thickness(3);
            this.Res(BackgroundProperty, "Calcy.Card");
            _title = new PixelText { PixelSize = 1.5, LetterSpacing = 1, VerticalAlignment = VerticalAlignment.Center };
            _title.Res(PixelText.ForegroundProperty, "Calcy.Ink");
            _collapse = Ui.IconButton("chevron", "Hide or show the output");
            _collapse.Click += (_, _) => Cell.Collapsed = !Cell.Collapsed;
            _collapse.Margin = new Thickness(0, 0, 6, 0);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _chipHost, _meta, _actions } };
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_collapse, Dock.Left);
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(_collapse);
            dock.Children.Add(right);
            var titleHost = new Border { Child = _title, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
            dock.Children.Add(titleHost);
            titleHost.PropertyChanged += (_, e) =>
            {
                if (e.Property == BoundsProperty) FitTitle(titleHost.Bounds.Width);
            };
            _header = new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(6, 2, 4, 2), MinHeight = 30 };
            _header.Res(Border.BackgroundProperty, HeaderToken).Res(Border.BorderBrushProperty, "Calcy.Line");
            var root = new DockPanel();
            DockPanel.SetDock(_header, Dock.Top);
            root.Children.Add(_header);
            root.Children.Add(Body);
            Child = root;
        }

        BuildActions();
        _actions.IsVisible = false;
        PointerEntered += (_, _) => { _hover = true; UpdateChrome(); };
        PointerExited += (_, _) => { _hover = false; UpdateChrome(); };
        AddHandler(PointerPressedEvent, (_, _) => Owner.Select(this, focus: false), RoutingStrategies.Tunnel, handledEventsToo: true);
        cell.MetaChanged += OnMetaChanged;
        cell.ResultsChanged += OnResultsChanged;
        Avalonia.Automation.AutomationProperties.SetName(this, KindTitle);
    }

    /// <summary>The notebook screen.</summary>
    protected NotebookView Owner { get; }

    /// <summary>The cell shown.</summary>
    public NotebookCell Cell { get; }

    /// <summary>Holds the cell's content.</summary>
    protected ContentControl Body { get; }

    /// <summary>The header colour token.</summary>
    protected virtual string HeaderToken => "Calcy.Key";

    /// <summary>"Maths cell", "Code cell (Python, sandboxed)" ...</summary>
    protected abstract string KindTitle { get; }

    /// <summary>True when the cell has output that can be folded away.</summary>
    protected virtual bool CanCollapse => true;

    /// <summary>Adds a header action before the standard ones (shown with them on the selected or hovered cell).</summary>
    protected void AddAction(Control action) => _actions.Children.Insert(0, action);

    /// <summary>Adds a control to the header that is always visible (RUN for code cells).</summary>
    protected void AddHeaderControl(Control control) => ((StackPanel)_chipHost.Parent!).Children.Add(control);

    /// <summary>True for the selected cell.</summary>
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            UpdateChrome();
            OnSelectionChanged();
        }
    }

    /// <summary>Called when the cell is selected or deselected.</summary>
    protected virtual void OnSelectionChanged() { }

    /// <summary>Puts the keyboard focus into the cell's first input. Returns false when it has none.</summary>
    public abstract bool FocusInput(bool atEnd = false);

    /// <summary>Shift+Enter: run or commit the cell. Returns true when focus should move to the next cell.</summary>
    public virtual bool Run() => true;

    /// <summary>Reads the cell's results and state again.</summary>
    public virtual void Refresh() => UpdateChrome();

    /// <summary>Re-reads maths inputs after the notebook's defined names changed.</summary>
    public virtual void ReparseInputs() { }

    /// <summary>Text shown at the right of the header (for code cells the run summary).</summary>
    protected virtual string MetaText => "";

    /// <summary>The header label with the caption.</summary>
    protected string FullTitle => Cell.Caption is { } c ? $"{KindTitle}: {c}" : KindTitle;

    private void FitTitle(double width)
    {
        if (_title is null) return;
        var text = FullTitle.ToUpperInvariant();
        double advance = (PixelFont.Width + _title.LetterSpacing) * _title.PixelSize;
        int max = Math.Max(4, (int)Math.Floor((width + _title.LetterSpacing * _title.PixelSize) / advance));
        if (text.Length > max) text = text[..Math.Max(1, max - 3)].TrimEnd() + "...";
        if (text != _titleText)
        {
            _titleText = text;
            _title.Text = text;
        }
    }

    private void OnMetaChanged(object? sender, EventArgs e)
    {
        UpdateChrome();
        OnCollapsedChanged();
    }

    /// <summary>Called when the cell is folded or unfolded.</summary>
    protected virtual void OnCollapsedChanged() { }

    private void OnResultsChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Updates the header, chip, frame and actions.</summary>
    protected void UpdateChrome()
    {
        bool showActions = _selected || _hover;
        _actions.IsVisible = showActions;
        if (_outline is not null) _outline.ShowStroke = showActions;
        if (_title is not null && _title.Parent is Border host) FitTitle(host.Bounds.Width > 0 ? host.Bounds.Width : 400);
        if (_collapse is not null)
        {
            _collapse.IsVisible = CanCollapse;
            if (_collapse.Content is Control icon) icon.RenderTransform = Cell.Collapsed ? new RotateTransform(-90) : null;
            ToolTip.SetTip(_collapse, Cell.Collapsed ? "Show the output" : "Hide the output");
        }
        _meta.Text = MetaText;
        _meta.IsVisible = MetaText.Length > 0;
        _chipHost.Child = StateChip();
        if (Cell.Kind != CellKind.Text)
        {
            this.Res(BorderBrushProperty, Cell.State == CellState.Error ? "Calcy.Err" : "Calcy.Line");
            this.Res(BoxShadowProperty, _selected ? "Calcy.Shadow" : "Calcy.ShadowNone");
        }
    }

    private Control? StateChip()
    {
        Border? chip = Cell.State switch
        {
            CellState.Error => Ui.Chip("ERROR", "Calcy.Paper", error: true),
            CellState.Stale => Ui.Chip("STALE", "Calcy.Mem"),
            CellState.NotRun => Ui.Chip("NOT RUN", "Calcy.Paper"),
            CellState.Running => Ui.Chip("RUNNING", "Calcy.Sel"),
            _ => null,
        };
        if (chip is null) return null;
        chip.Margin = new Thickness(4, 0);
        if (Cell.StateMessage is { } msg) ToolTip.SetTip(chip, msg);
        return chip;
    }

    // ------------------------------------------------------------------ actions

    private void BuildActions()
    {
        var up = Ui.IconButton("chevron", "Move up (Alt+Up)", 180);
        up.Click += (_, _) => Owner.MoveCell(Cell, -1);
        var down = Ui.IconButton("chevron", "Move down (Alt+Down)");
        down.Click += (_, _) => Owner.MoveCell(Cell, 1);
        var copy = Ui.IconButton("copy", "Duplicate");
        copy.Click += (_, _) => Owner.DuplicateCell(Cell);
        var delete = Ui.IconButton("trash", "Delete (undo with Ctrl+Z)");
        delete.Click += (_, _) => Owner.DeleteCell(Cell);
        var more = new PixelButton { Content = Ui.Label("...", 11), Classes = { "small", "paper", "flat" }, Width = 30, MinWidth = 30, MinHeight = 26, Height = 26, Padding = new Thickness(0) };
        ToolTip.SetTip(more, "More: insert cells, caption, output");
        Avalonia.Automation.AutomationProperties.SetName(more, "More cell actions");
        more.Click += (_, _) => ShowMenu(more);
        foreach (var b in new[] { up, down, copy, delete, more }) _actions.Children.Add(b);
    }

    private void ShowMenu(Control anchor)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
        MenuItem Insert(string header, bool below)
        {
            var parent = new MenuItem { Header = header };
            foreach (var kind in Enum.GetValues<CellKind>())
            {
                var k = kind;
                parent.Items.Add(Item(NotebookCell.KindName(k), () => Owner.InsertCell(k, Cell, below)));
            }
            return parent;
        }
        menu.Items.Add(Insert("Insert above", false));
        menu.Items.Add(Insert("Insert below", true));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Cell.Caption is null ? "Add caption" : "Edit caption", () => Owner.EditCaption(this)));
        if (CanCollapse) menu.Items.Add(Item(Cell.Collapsed ? "Show output" : "Hide output", () => Cell.Collapsed = !Cell.Collapsed));
        foreach (var (header, action) in MenuExtras()) menu.Items.Add(Item(header, action));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Move up", () => Owner.MoveCell(Cell, -1)));
        menu.Items.Add(Item("Move down", () => Owner.MoveCell(Cell, 1)));
        menu.Items.Add(Item("Duplicate", () => Owner.DuplicateCell(Cell)));
        menu.Items.Add(Item("Delete", () => Owner.DeleteCell(Cell)));
        menu.ShowAt(anchor);
    }

    /// <summary>Kind specific menu entries.</summary>
    protected virtual IEnumerable<(string Header, Action Action)> MenuExtras() => [];

    /// <summary>Stops listening to the cell.</summary>
    public virtual void Detach()
    {
        Cell.MetaChanged -= OnMetaChanged;
        Cell.ResultsChanged -= OnResultsChanged;
    }

    /// <summary>The header label (for tests).</summary>
    public string HeaderText => FullTitle;
}
