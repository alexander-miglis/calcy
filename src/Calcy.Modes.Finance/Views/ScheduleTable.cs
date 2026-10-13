using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Calcy.Modes.Finance.Views;

/// <summary>A table column: header text, share of the spare width and smallest width.</summary>
public sealed record TableColumn(string Header, double Weight = 1, double MinWidth = 96);

/// <summary>How a table row looks.</summary>
public enum TableRowKind
{
    /// <summary>A plain row (one payment, one year of depreciation).</summary>
    Normal,

    /// <summary>A group summary row that can be opened and closed (one year of payments).</summary>
    Group,

    /// <summary>The totals row.</summary>
    Total,

    /// <summary>The column headers.</summary>
    Header,
}

/// <summary>The content of one table row.</summary>
/// <param name="Cells">Cell texts, one per column.</param>
/// <param name="Kind">How the row looks.</param>
/// <param name="Group">For group rows: the group number passed back when the row is opened or closed.</param>
/// <param name="Expanded">For group rows: whether the group is open.</param>
/// <param name="Name">What a screen reader says for the row.</param>
public sealed record TableRowData(IReadOnlyList<string> Cells, TableRowKind Kind = TableRowKind.Normal, int Group = -1, bool Expanded = false, string? Name = null);

/// <summary>
/// A money table in the 8-bit style: header strip, rows drawn with hard cell lines and numbers right aligned in the
/// monospace font (so the decimal points line up), group rows that open and close, and a totals row that stays visible.
/// </summary>
public sealed class ScheduleTable : Border
{
    /// <summary>Plain row background.</summary>
    public static readonly StyledProperty<IBrush?> RowBrushProperty = AvaloniaProperty.Register<ScheduleTable, IBrush?>(nameof(RowBrush));

    /// <summary>Group row background.</summary>
    public static readonly StyledProperty<IBrush?> GroupBrushProperty = AvaloniaProperty.Register<ScheduleTable, IBrush?>(nameof(GroupBrush));

    /// <summary>Totals row background.</summary>
    public static readonly StyledProperty<IBrush?> TotalBrushProperty = AvaloniaProperty.Register<ScheduleTable, IBrush?>(nameof(TotalBrush));

    /// <summary>Header background.</summary>
    public static readonly StyledProperty<IBrush?> HeadBrushProperty = AvaloniaProperty.Register<ScheduleTable, IBrush?>(nameof(HeadBrush));

    /// <summary>Cell lines.</summary>
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<ScheduleTable, IBrush?>(nameof(GridBrush));

    /// <summary>Text colour.</summary>
    public static readonly StyledProperty<IBrush?> InkBrushProperty = AvaloniaProperty.Register<ScheduleTable, IBrush?>(nameof(InkBrush));

    /// <summary>Font for numbers.</summary>
    public static readonly StyledProperty<FontFamily?> MathFontProperty = AvaloniaProperty.Register<ScheduleTable, FontFamily?>(nameof(MathFont));

    /// <summary>Font for headers.</summary>
    public static readonly StyledProperty<FontFamily?> LabelFontProperty = AvaloniaProperty.Register<ScheduleTable, FontFamily?>(nameof(LabelFont));

    /// <summary>Text size of the cells when there is room.</summary>
    internal const double CellTextSize = 15;

    /// <summary>Smallest text size before cells are clipped.</summary>
    internal const double MinCellTextSize = 10;

    /// <summary>Space on each side of a cell's text.</summary>
    internal const double CellPadding = 8;

    /// <summary>Room for the open and close arrow of group rows.</summary>
    internal const double ArrowWidth = 14;

    private readonly IReadOnlyList<TableColumn> _columns;
    private int[] _maxChars;
    private bool _hasGroups;
    private (double Width, double Size) _sizeCache = (-1, CellTextSize);
    private readonly StackPanel _body = new();
    private readonly ScrollViewer _scroll;
    private readonly TableRow _header;
    private readonly TableRow _total;
    private readonly TextBlock _empty;

    /// <summary>Creates a table with the given columns.</summary>
    public ScheduleTable(IReadOnlyList<TableColumn> columns, string automationName)
    {
        _columns = columns;
        _maxChars = new int[columns.Count];
        BorderThickness = new Thickness(3);
        VerticalAlignment = VerticalAlignment.Top;
        this.Token(BorderBrushProperty, "Calcy.Line");
        this.Token(BackgroundProperty, "Calcy.Card");
        this.Token(RowBrushProperty, "Calcy.Card");
        this.Token(GroupBrushProperty, "Calcy.Key");
        this.Token(TotalBrushProperty, "Calcy.Sel");
        this.Token(HeadBrushProperty, "Calcy.Head");
        this.Token(GridBrushProperty, "Calcy.Line");
        this.Token(InkBrushProperty, "Calcy.Ink");
        this.Token(MathFontProperty, "Calcy.Font.Math");
        this.Token(LabelFontProperty, "Calcy.Font.Label");
        AutomationProperties.SetName(this, automationName);

        _header = new TableRow(this, new TableRowData([.. columns.Select(c => c.Header.ToUpperInvariant())], TableRowKind.Header, Name: string.Join(", ", columns.Select(c => c.Header))));
        _total = new TableRow(this, new TableRowData([], TableRowKind.Total)) { IsVisible = false };
        _scroll = new ScrollViewer
        {
            Content = _body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _empty = Ui.Text("", "hint");
        _empty.Margin = new Thickness(12);
        _empty.IsVisible = false;
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_total, Dock.Bottom);
        Child = new DockPanel { Children = { _header, _total, new Panel { Children = { _scroll, _empty } } } };
    }

    /// <summary>Raised with the group number when a group row is opened or closed.</summary>
    public event EventHandler<int>? GroupToggled;

    /// <summary>Plain row background.</summary>
    public IBrush? RowBrush { get => GetValue(RowBrushProperty); set => SetValue(RowBrushProperty, value); }

    /// <summary>Group row background.</summary>
    public IBrush? GroupBrush { get => GetValue(GroupBrushProperty); set => SetValue(GroupBrushProperty, value); }

    /// <summary>Totals row background.</summary>
    public IBrush? TotalBrush { get => GetValue(TotalBrushProperty); set => SetValue(TotalBrushProperty, value); }

    /// <summary>Header background.</summary>
    public IBrush? HeadBrush { get => GetValue(HeadBrushProperty); set => SetValue(HeadBrushProperty, value); }

    /// <summary>Cell lines.</summary>
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    /// <summary>Text colour.</summary>
    public IBrush? InkBrush { get => GetValue(InkBrushProperty); set => SetValue(InkBrushProperty, value); }

    /// <summary>Font for numbers.</summary>
    public FontFamily? MathFont { get => GetValue(MathFontProperty); set => SetValue(MathFontProperty, value); }

    /// <summary>Font for headers.</summary>
    public FontFamily? LabelFont { get => GetValue(LabelFontProperty); set => SetValue(LabelFontProperty, value); }

    /// <summary>The rows on screen (for tests and screen readers).</summary>
    public IReadOnlyList<TableRowData> Rows => [.. _body.Children.OfType<TableRow>().Select(r => r.Data)];

    /// <summary>The totals row, if shown.</summary>
    public TableRowData? Total => _total.IsVisible ? _total.Data : null;

    /// <summary>The scroll viewer around the rows.</summary>
    public ScrollViewer Scroll => _scroll;

    /// <summary>Replaces the rows (and the totals row; null hides it).</summary>
    public void SetRows(IReadOnlyList<TableRowData> rows, TableRowData? total, string? emptyText = null)
    {
        // Reuse row controls where possible so opening a year does not rebuild hundreds of rows.
        var existing = _body.Children.OfType<TableRow>().ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i < existing.Count) existing[i].Data = rows[i];
            else _body.Children.Add(new TableRow(this, rows[i]));
        }
        if (existing.Count > rows.Count) _body.Children.RemoveRange(rows.Count, existing.Count - rows.Count);
        _total.IsVisible = total is not null;
        if (total is not null) _total.Data = total;
        Ui.Show(_empty, rows.Count == 0 ? emptyText : null);

        // The widest text of each column decides how small the text gets in a narrow table.
        _maxChars = new int[_columns.Count];
        _hasGroups = rows.Any(r => r.Kind == TableRowKind.Group);
        foreach (var r in total is null ? rows : rows.Append(total))
            for (int c = 0; c < r.Cells.Count && c < _maxChars.Length; c++)
                _maxChars[c] = Math.Max(_maxChars[c], r.Cells[c].Length);
        _sizeCache = (-1, CellTextSize);
        _total.InvalidateRow();
        foreach (var r in _body.Children.OfType<TableRow>()) r.InvalidateRow();
    }

    /// <summary>
    /// The cell text size for a row of the given width: the normal size when every column fits, otherwise smaller (down
    /// to <see cref="MinCellTextSize"/>) so the numbers are not cut off. Monospace digits are about 0.6 em wide.
    /// </summary>
    internal double TextSize(double width)
    {
        if (Math.Abs(_sizeCache.Width - width) < 0.5) return _sizeCache.Size;
        var widths = Widths(width);
        double size = CellTextSize;
        for (int c = 0; c < widths.Length; c++)
        {
            if (_maxChars[c] == 0) continue;
            double room = widths[c] - 2 * CellPadding - (c == 0 && _hasGroups ? ArrowWidth : 0);
            size = Math.Min(size, room / (_maxChars[c] * 0.6));
        }
        size = Math.Clamp(Math.Floor(size * 2) / 2, MinCellTextSize, CellTextSize);
        _sizeCache = (width, size);
        return size;
    }

    /// <summary>Column widths for a row of the given width: the first column fixed, the others sharing the rest.</summary>
    internal double[] Widths(double total)
    {
        var w = _columns.Select(c => c.MinWidth).ToArray();
        double spare = total - w.Sum();
        double weights = _columns.Sum(c => c.Weight);
        if (spare > 0 && weights > 0)
            for (int i = 0; i < w.Length; i++) w[i] += Math.Floor(spare * _columns[i].Weight / weights);
        // Give the rounding remainder to the last column so the lines meet the right edge.
        w[^1] += total - w.Sum();
        return w;
    }

    internal void Toggle(int group) => GroupToggled?.Invoke(this, group);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowBrushProperty || change.Property == GroupBrushProperty || change.Property == TotalBrushProperty ||
            change.Property == HeadBrushProperty || change.Property == GridBrushProperty || change.Property == InkBrushProperty ||
            change.Property == MathFontProperty || change.Property == LabelFontProperty)
        {
            _header?.InvalidateRow();
            _total?.InvalidateRow();
            foreach (var r in _body.Children.OfType<TableRow>()) r.InvalidateRow();
        }
    }
}

/// <summary>One drawn row of a <see cref="ScheduleTable"/>.</summary>
internal sealed class TableRow : Control
{
    private const double Pad = ScheduleTable.CellPadding;
    private readonly ScheduleTable _table;
    private TableRowData _data;

    public TableRow(ScheduleTable table, TableRowData data)
    {
        _table = table;
        _data = data;
        Apply();
    }

    public TableRowData Data
    {
        get => _data;
        set
        {
            if (Equals(_data, value)) return;
            _data = value;
            Apply();
            InvalidateRow();
        }
    }

    private void Apply()
    {
        Height = _data.Kind == TableRowKind.Header ? 32 : 30;
        bool group = _data.Kind == TableRowKind.Group;
        Focusable = group;
        Cursor = group ? new Cursor(StandardCursorType.Hand) : null;
        AutomationProperties.SetName(this, _data.Name ?? string.Join(", ", _data.Cells));
        if (group) ToolTip.SetTip(this, _data.Expanded ? "Click to hide the payments of this year" : "Click to show the payments of this year");
        else ToolTip.SetTip(this, null);
    }

    public void InvalidateRow() => InvalidateVisual();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_data.Kind != TableRowKind.Group || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        Focus();
        _table.Toggle(_data.Group);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_data.Kind == TableRowKind.Group && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            _table.Toggle(_data.Group);
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var back = _data.Kind switch
        {
            TableRowKind.Group => _table.GroupBrush,
            TableRowKind.Total => _table.TotalBrush,
            TableRowKind.Header => _table.HeadBrush,
            _ => _table.RowBrush,
        };
        if (back is not null) ctx.FillRectangle(back, new Rect(0, 0, w, h));
        var line = _table.GridBrush;
        var ink = _table.InkBrush ?? Brushes.Black;
        bool header = _data.Kind == TableRowKind.Header;
        var font = (header ? _table.LabelFont : _table.MathFont) ?? FontFamily.Default;
        var weight = header || _data.Kind is TableRowKind.Group or TableRowKind.Total ? FontWeight.Bold : FontWeight.Normal;
        var typeface = new Typeface(font, FontStyle.Normal, weight);
        double size = header ? 11 : _table.TextSize(w);

        var widths = _table.Widths(w);
        double x = 0;
        for (int c = 0; c < widths.Length; c++)
        {
            double cw = widths[c];
            if (c < _data.Cells.Count && _data.Cells[c].Length > 0)
            {
                var text = _data.Cells[c];
                double left = x + Pad;
                if (c == 0 && _data.Kind == TableRowKind.Group)
                {
                    DrawArrow(ctx, ink, left, h / 2, _data.Expanded);
                    left += ScheduleTable.ArrowWidth;
                }
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, ink);
                double room = cw - Pad - (left - x);
                double tx = header || (c == 0 && _data.Kind != TableRowKind.Normal)
                    ? (header ? x + (cw - ft.Width) / 2 : left)
                    : x + cw - Pad - ft.Width;
                if (ft.Width > room && !header) tx = left;
                using (ctx.PushClip(new Rect(x, 0, cw, h)))
                    ctx.DrawText(ft, new Point(Math.Round(tx), Math.Round((h - ft.Height) / 2)));
            }
            x += cw;
            if (line is not null && c < widths.Length - 1) ctx.FillRectangle(line, new Rect(Math.Round(x) - 1, 0, 2, h));
        }
        if (line is not null)
        {
            double t = header || _data.Kind == TableRowKind.Total ? 3 : 2;
            if (_data.Kind == TableRowKind.Total) ctx.FillRectangle(line, new Rect(0, 0, w, t));
            else ctx.FillRectangle(line, new Rect(0, h - t, w, t));
        }
        if (IsFocused) DrawFocus(ctx, ink, w, h);
    }

    /// <summary>A pixel triangle: pointing right when closed, down when open.</summary>
    private static void DrawArrow(DrawingContext ctx, IBrush ink, double x, double cy, bool open)
    {
        x = Math.Round(x);
        double y = Math.Round(cy) - 5;
        if (open)
        {
            for (int r = 0; r < 4; r++) ctx.FillRectangle(ink, new Rect(x + r, y + 2 + r * 2, 10 - 2 * r, 2));
        }
        else
        {
            for (int c = 0; c < 4; c++) ctx.FillRectangle(ink, new Rect(x + 1 + c * 2, y + c, 2, 10 - 2 * c));
        }
    }

    private static void DrawFocus(DrawingContext ctx, IBrush ink, double w, double h)
    {
        ctx.FillRectangle(ink, new Rect(2, 2, w - 4, 2));
        ctx.FillRectangle(ink, new Rect(2, h - 6, w - 4, 2));
        ctx.FillRectangle(ink, new Rect(2, 2, 2, h - 6));
        ctx.FillRectangle(ink, new Rect(w - 4, 2, 2, h - 6));
    }
}
