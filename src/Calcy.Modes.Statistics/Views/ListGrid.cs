using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Modes.Statistics.Logic;

namespace Calcy.Modes.Statistics.Views;

/// <summary>What the list editor shows for one list.</summary>
/// <param name="Name">The list name.</param>
/// <param name="Header">Header text ("L1 hours").</param>
/// <param name="Cells">Cell texts.</param>
/// <param name="Kinds">What each cell holds (numbers are right aligned, text left aligned).</param>
/// <param name="IsFormula">True for a live formula list (read in a muted colour).</param>
/// <param name="Tip">Tooltip of the header (the formula, or its error).</param>
/// <param name="HasError">True when the formula failed.</param>
public sealed record GridColumn(string Name, string Header, IReadOnlyList<string> Cells, IReadOnlyList<CellKind> Kinds, bool IsFormula, string? Tip, bool HasError);

/// <summary>
/// The spreadsheet-like list editor of the design: a numbered row column, one column per list with its header in the
/// selection colour, and an edit box that sits on the active cell. Only the visible rows are drawn, so lists with many
/// thousands of values scroll smoothly. Keys: arrows, Tab and Enter move (typing replaces the cell like a spreadsheet),
/// Escape undoes the edit, Page Up and Page Down jump, and pasting a block from a spreadsheet fills the lists.
/// </summary>
public sealed class ListGrid : DockPanel
{
    /// <summary>Height of a row.</summary>
    public const double RowHeight = 34;

    /// <summary>Height of the header row.</summary>
    public const double HeaderHeight = 30;

    /// <summary>Width of the row number column.</summary>
    public const double NumberWidth = 34;

    private readonly GridHeader _header;
    private readonly GridBody _body;
    private readonly ScrollViewer _scroll;
    private readonly TextBox _editor;
    private IReadOnlyList<GridColumn> _columns = [];
    private int _col, _row;
    private string _original = "";
    private bool _suppress;
    private double _columnWidth = 120;
    private int _minRows = 10;

    /// <summary>A cell was edited: list name, row and the new text.</summary>
    public event Action<string, int, string>? CellEdited;

    /// <summary>The active cell moved: column index and row.</summary>
    public event Action<int, int>? ActiveCellChanged;

    /// <summary>Text with several cells was pasted: column index and row of the top-left cell, and the text.</summary>
    public event Action<int, int, string>? TablePasted;

    /// <summary>A column header was double-clicked (rename).</summary>
    public event Action<int>? HeaderDoubleClicked;

    /// <summary>A column header was right-clicked: the column index and the header control (to open a menu on).</summary>
    public event Action<int, Control>? HeaderMenuRequested;

    /// <summary>Creates an empty editor.</summary>
    public ListGrid()
    {
        _editor = new TextBox
        {
            FontSize = 15,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(6, 0),
            BorderThickness = new Thickness(3),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            AcceptsReturn = false,
            Classes = { "list-cell" },
        };
        _editor.Token(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        _editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) OnEditorText();
        };
        _editor.AddHandler(KeyDownEvent, OnEditorKey, RoutingStrategies.Tunnel);
        _editor.PastingFromClipboard += OnPaste;
        _body = new GridBody(this) { Children = { _editor } };
        _scroll = new ScrollViewer
        {
            Content = _body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroll.ScrollChanged += (_, _) =>
        {
            _header?.InvalidateVisual();
            _body.Redraw();
        };
        _scroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) Relayout();
        };
        _header = new GridHeader(this) { Height = HeaderHeight };
        SetDock(_header, Dock.Top);
        Children.Add(_header);
        Children.Add(_scroll);
        AutomationProperties.SetName(this, "List editor");
        UpdateEditorName();
    }

    /// <summary>The lists shown.</summary>
    public IReadOnlyList<GridColumn> Columns => _columns;

    /// <summary>The active cell (column index, row).</summary>
    public (int Column, int Row) Active => (_col, _row);

    /// <summary>The name of the active list, or null.</summary>
    public string? ActiveName => _col >= 0 && _col < _columns.Count ? _columns[_col].Name : null;

    /// <summary>The edit box on the active cell.</summary>
    public TextBox Editor => _editor;

    /// <summary>The width of a list column.</summary>
    public double ColumnWidth => _columnWidth;

    /// <summary>Rows drawn (the longest list plus one empty row, at least enough to fill the view).</summary>
    public int RowCount => Math.Max(Math.Max(_columns.Select(c => c.Cells.Count).DefaultIfEmpty(0).Max() + 1, _row + 1), _minRows);

    internal ScrollViewer Scroll => _scroll;

    /// <summary>Shows the lists. The text being typed in the active cell is left alone.</summary>
    public void Load(IReadOnlyList<GridColumn> columns)
    {
        _columns = columns;
        if (_col >= _columns.Count) _col = Math.Max(0, _columns.Count - 1);
        Relayout();
        // While typing, the table already holds what the edit box shows; anything else (a paste, a formula, a sort)
        // replaces the edit box text.
        var current = CellText(_col, _row);
        var typed = _editor.Text ?? "";
        if (typed != current && !(_editor.IsFocused && typed.Trim() == current.Trim())) SetEditorText(current, selectAll: true);
        _editor.IsVisible = _columns.Count > 0;
        UpdateEditorStyle();
        UpdateEditorName();
        _header.InvalidateVisual();
        _body.InvalidateMeasure();
        _body.Redraw();
    }

    /// <summary>Moves the edit box to a cell (and keyboard focus with it when <paramref name="focus"/> is set).</summary>
    public void Activate(int column, int row, bool focus = true)
    {
        if (_columns.Count == 0) return;
        column = Math.Clamp(column, 0, _columns.Count - 1);
        row = Math.Clamp(row, 0, StatTable.MaxRows - 1);
        bool moved = column != _col || row != _row;
        _col = column;
        _row = row;
        Relayout();
        SetEditorText(CellText(column, row), selectAll: true);
        UpdateEditorStyle();
        UpdateEditorName();
        _body.InvalidateMeasure();
        _body.InvalidateArrange();
        _header.InvalidateVisual();
        _body.Redraw();
        if (focus) _editor.Focus(NavigationMethod.Tab);
        EnsureVisible(column, row);
        if (moved) ActiveCellChanged?.Invoke(column, row);
    }

    /// <summary>Scrolls so the cell is in view (worked out directly, so it also works before the next layout pass).</summary>
    private void EnsureVisible(int col, int row)
    {
        var r = CellRect(col, row);
        var view = _scroll.Viewport;
        if (view.Width <= 0 || view.Height <= 0) return;
        double x = _scroll.Offset.X, y = _scroll.Offset.Y;
        if (r.Y < y) y = r.Y;
        else if (r.Bottom + 2 > y + view.Height) y = r.Bottom + 2 - view.Height;
        if (r.X < x) x = Math.Max(0, r.X - NumberWidth);
        else if (r.Right + 2 > x + view.Width) x = r.Right + 2 - view.Width;
        if (x != _scroll.Offset.X || y != _scroll.Offset.Y)
        {
            // The body may have to grow first (a row beyond the end), or the offset would be clamped to the old size.
            _body.InvalidateMeasure();
            _scroll.UpdateLayout();
            _scroll.Offset = new Vector(Math.Max(0, x), Math.Max(0, y));
        }
    }

    /// <summary>Puts keyboard focus on the active cell.</summary>
    public void FocusEditor() => Activate(_col, _row, focus: true);

    private string CellText(int col, int row) =>
        col >= 0 && col < _columns.Count && row >= 0 && row < _columns[col].Cells.Count ? _columns[col].Cells[row] : "";

    private void SetEditorText(string text, bool selectAll)
    {
        _suppress = true;
        try
        {
            _editor.Text = text;
            _original = text;
            if (selectAll) _editor.SelectAll();
            else _editor.CaretIndex = text.Length;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnEditorText()
    {
        if (_suppress || ActiveName is not { } name) return;
        var text = _editor.Text ?? "";
        if (text == CellText(_col, _row)) return;
        CellEdited?.Invoke(name, _row, text);
    }

    private void UpdateEditorName()
    {
        var name = ActiveName ?? "List";
        AutomationProperties.SetName(_editor, $"{name} row {(_row + 1).ToString(CultureInfo.InvariantCulture)}");
    }

    private void UpdateEditorStyle()
    {
        bool text = _col < _columns.Count && _row < _columns[_col].Kinds.Count && _columns[_col].Kinds[_row] == CellKind.Text;
        _editor.TextAlignment = text ? TextAlignment.Left : TextAlignment.Right;
        _editor.HorizontalContentAlignment = text ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void Relayout()
    {
        double viewport = _scroll.Bounds.Width;
        int n = Math.Max(1, _columns.Count);
        // Fill the view with rows, but never so many that a scroll bar appears for data that fits.
        int fitting = Math.Max(4, (int)Math.Floor((_scroll.Bounds.Height - 2) / RowHeight));
        _minRows = fitting;
        int needed = Math.Max(_columns.Select(c => c.Cells.Count).DefaultIfEmpty(0).Max() + 1, _row + 1);
        double scrollBar = needed > fitting ? 12 : 0;
        double available = Math.Max(0, viewport - NumberWidth - 2 - scrollBar);
        _columnWidth = Math.Clamp(Math.Floor(available / n), 76, 150);
        _body.InvalidateMeasure();
        _header.InvalidateVisual();
    }

    /// <summary>The rectangle of a cell in body coordinates.</summary>
    internal Rect CellRect(int col, int row) => new(NumberWidth + col * _columnWidth, row * RowHeight, _columnWidth, RowHeight);

    /// <summary>The cell at a point of the body, or null.</summary>
    internal (int Col, int Row)? HitCell(Point p)
    {
        if (p.X < NumberWidth || _columns.Count == 0) return null;
        int col = (int)((p.X - NumberWidth) / _columnWidth);
        int row = (int)(p.Y / RowHeight);
        if (col < 0 || col >= _columns.Count || row < 0) return null;
        return (col, row);
    }

    /// <summary>The column under a header x position (header coordinates), or -1.</summary>
    internal int HitHeader(double x)
    {
        double bx = x + _scroll.Offset.X - NumberWidth;
        if (bx < 0) return -1;
        int col = (int)(bx / _columnWidth);
        return col < _columns.Count ? col : -1;
    }

    internal void OnHeaderPressed(int col, PointerPressedEventArgs e, Control source)
    {
        if (col < 0) return;
        var point = e.GetCurrentPoint(source);
        if (point.Properties.IsRightButtonPressed)
        {
            Activate(col, _row, focus: true);
            HeaderMenuRequested?.Invoke(col, source);
            e.Handled = true;
            return;
        }
        if (e.ClickCount >= 2)
        {
            HeaderDoubleClicked?.Invoke(col);
            e.Handled = true;
            return;
        }
        Activate(col, _row, focus: true);
        e.Handled = true;
    }

    internal void OnBodyPressed(Point p, PointerPressedEventArgs e)
    {
        if (HitCell(p) is not { } cell) return;
        Activate(cell.Col, cell.Row, focus: true);
        e.Handled = true;
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        if (_columns.Count == 0) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var text = _editor.Text ?? "";
        bool all = _editor.SelectionStart != _editor.SelectionEnd && Math.Abs(_editor.SelectionEnd - _editor.SelectionStart) == text.Length;
        bool atStart = _editor.SelectionStart == _editor.SelectionEnd && _editor.CaretIndex == 0;
        bool atEnd = _editor.SelectionStart == _editor.SelectionEnd && _editor.CaretIndex >= text.Length;
        (int c, int r)? target = e.Key switch
        {
            Key.Enter => (_col, shift ? _row - 1 : _row + 1),
            Key.Tab when !shift => _col + 1 < _columns.Count ? (_col + 1, _row) : (0, _row + 1),
            Key.Tab => _col > 0 ? (_col - 1, _row) : (_columns.Count - 1, _row - 1),
            Key.Up => (_col, _row - 1),
            Key.Down => (_col, _row + 1),
            Key.PageUp => (_col, _row - 10),
            Key.PageDown => (_col, _row + 10),
            Key.Home when ctrl => (0, 0),
            Key.End when ctrl => (_col, Math.Max(0, _columns[_col].Cells.Count)),
            Key.Left when atStart || all => (_col - 1, _row),
            Key.Right when atEnd || all => (_col + 1, _row),
            _ => null,
        };
        if (e.Key == Key.Escape)
        {
            if (text != _original)
            {
                _editor.Text = _original;
                _editor.SelectAll();
                e.Handled = true;
            }
            return;
        }
        if (target is not { } t) return;
        if (t.c < 0 || t.c >= _columns.Count || t.r < 0)
        {
            e.Handled = e.Key is Key.Tab or Key.Enter or Key.Up;
            return;
        }
        e.Handled = true;
        Activate(t.c, t.r, focus: true);
    }

    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        string? text;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            return;
        }
        if (string.IsNullOrEmpty(text)) return;
        if (TablePaste.IsTable(text))
        {
            TablePasted?.Invoke(_col, _row, text);
            return;
        }
        var current = _editor.Text ?? "";
        int a = Math.Clamp(Math.Min(_editor.SelectionStart, _editor.SelectionEnd), 0, current.Length);
        int b = Math.Clamp(Math.Max(_editor.SelectionStart, _editor.SelectionEnd), a, current.Length);
        var single = text.Trim();
        _editor.Text = current[..a] + single + current[b..];
        _editor.CaretIndex = a + single.Length;
    }

    /// <summary>Draws the column headers (scrolled with the body).</summary>
    private sealed class GridHeader(ListGrid grid) : Control
    {
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            grid.OnHeaderPressed(grid.HitHeader(e.GetPosition(this).X), e, this);
        }

        public override void Render(DrawingContext ctx)
        {
            var size = Bounds.Size;
            var line = Find("Calcy.Line");
            ctx.FillRectangle(Find("Calcy.Paper"), new Rect(size));
            double ox = -grid._scroll.Offset.X;
            using var clip = ctx.PushClip(new Rect(size));
            var face = new Typeface(FindFont("Calcy.Font.Label"), FontStyle.Normal, FontWeight.Bold);
            var ink = Find("Calcy.Ink");
            void Cell(double x, double w, IBrush fill, string text, IBrush fg, double fontSize)
            {
                var r = new Rect(Math.Round(x), 0, Math.Round(w), size.Height);
                ctx.FillRectangle(fill, r);
                ctx.FillRectangle(line, new Rect(r.X, 0, r.Width + 2, 2));
                ctx.FillRectangle(line, new Rect(r.X, size.Height - 2, r.Width + 2, 2));
                ctx.FillRectangle(line, new Rect(r.X, 0, 2, size.Height));
                ctx.FillRectangle(line, new Rect(r.Right, 0, 2, size.Height));
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, fontSize, fg)
                {
                    MaxTextWidth = Math.Max(10, r.Width - 10),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                };
                ctx.DrawText(ft, new Point(r.X + 5, (size.Height - ft.Height) / 2 + 1));
            }
            Cell(ox, NumberWidth, Find("Calcy.Key"), "#", ink, 10);
            for (int c = 0; c < grid._columns.Count; c++)
            {
                var col = grid._columns[c];
                var fill = col.HasError ? Find("Calcy.Paper") : c == grid._col ? Find("Calcy.Head") : col.IsFormula ? Find("Calcy.Key") : Find("Calcy.Sel");
                var fg = col.HasError ? Find("Calcy.Err") : ink;
                Cell(ox + NumberWidth + c * grid._columnWidth, grid._columnWidth, fill, (col.IsFormula ? "= " : "") + col.Header, fg, 11);
            }
        }

        private IBrush Find(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

        private FontFamily FindFont(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is FontFamily f ? f : FontFamily.Default;
    }

    /// <summary>Draws the visible rows and hosts the edit box.</summary>
    private sealed class GridBody : Panel
    {
        private readonly ListGrid _grid;
        private readonly GridSurface _surface;

        public GridBody(ListGrid grid)
        {
            _grid = grid;
            ClipToBounds = true;
            Background = Brushes.Transparent;
            _surface = new GridSurface(grid);
            Children.Add(_surface);
        }

        public void Redraw() => _surface.InvalidateVisual();

        protected override Size MeasureOverride(Size availableSize)
        {
            var size = new Size(NumberWidth + _grid._columns.Count * _grid._columnWidth + 2, _grid.RowCount * RowHeight + 2);
            _surface.Measure(size);
            _grid._editor.Measure(new Size(_grid._columnWidth + 2, RowHeight + 2));
            return size;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _surface.Arrange(new Rect(finalSize));
            var r = _grid.CellRect(_grid._col, _grid._row);
            _grid._editor.Arrange(new Rect(r.X, r.Y, r.Width + 2, r.Height + 2));
            return finalSize;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _grid.OnBodyPressed(e.GetPosition(this), e);
        }
    }

    /// <summary>Draws the visible rows of the body.</summary>
    private sealed class GridSurface(ListGrid grid) : Control
    {
        private readonly ListGrid _grid = grid;

        public override void Render(DrawingContext ctx)
        {
            var size = Bounds.Size;
            var paper = Find("Calcy.Paper");
            var card = Find("Calcy.Card");
            var line = Find("Calcy.Line");
            var ink = Find("Calcy.Ink");
            var muted = Find("Calcy.Muted");
            ctx.FillRectangle(paper, new Rect(size));
            var scroll = _grid._scroll;
            double top = scroll.Offset.Y, height = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : size.Height;
            int first = Math.Max(0, (int)(top / RowHeight) - 1);
            int last = Math.Min(_grid.RowCount - 1, (int)((top + height) / RowHeight) + 1);
            double w = _grid._columnWidth;
            int cols = _grid._columns.Count;
            double right = NumberWidth + cols * w;
            var math = new Typeface(FindFont("Calcy.Font.Math"));
            var label = new Typeface(FindFont("Calcy.Font.Label"));
            for (int r = first; r <= last; r++)
            {
                double y = r * RowHeight;
                ctx.FillRectangle(card, new Rect(NumberWidth, y, cols * w, RowHeight));
                var num = new FormattedText((r + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, label, 12, muted);
                ctx.DrawText(num, new Point((NumberWidth - num.Width) / 2 + 1, y + (RowHeight - num.Height) / 2));
                for (int c = 0; c < cols; c++)
                {
                    var col = _grid._columns[c];
                    if (r >= col.Cells.Count || col.Cells[r].Length == 0) continue;
                    bool isText = r < col.Kinds.Count && col.Kinds[r] == CellKind.Text;
                    var ft = new FormattedText(col.Cells[r].Trim(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, math, 15, col.IsFormula ? muted : ink)
                    {
                        MaxTextWidth = Math.Max(10, w - 14),
                        MaxLineCount = 1,
                        Trimming = TextTrimming.CharacterEllipsis,
                        TextAlignment = isText ? TextAlignment.Left : TextAlignment.Right,
                    };
                    ctx.DrawText(ft, new Point(NumberWidth + c * w + 8, y + (RowHeight - ft.Height) / 2));
                }
                ctx.FillRectangle(line, new Rect(0, y, right + 2, 2));
            }
            ctx.FillRectangle(line, new Rect(0, (last + 1) * RowHeight, right + 2, 2));
            double yTop = first * RowHeight, yBottom = (last + 1) * RowHeight + 2;
            ctx.FillRectangle(line, new Rect(0, yTop, 2, yBottom - yTop));
            for (int c = 0; c <= cols; c++) ctx.FillRectangle(line, new Rect(NumberWidth + c * w, yTop, 2, yBottom - yTop));
        }

        private IBrush Find(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

        private FontFamily FindFont(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is FontFamily f ? f : FontFamily.Default;
    }
}
