using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Controls;

namespace Calcy.Modes.Notebook.Views;

/// <summary>
/// A table cell as on the design board: numbered columns, one named row per data series (each a list variable), and
/// summary lines next to the table.
/// </summary>
internal sealed class TableCellView : CellView
{
    private readonly TableCell _cell;
    private readonly Grid _grid = new();
    private readonly StackPanel _summary = new() { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _errors;
    private readonly WrapPanel _buttons = new() { Orientation = Orientation.Horizontal };
    private readonly List<MathLineView> _summaryViews = [];
    private readonly List<(TableRow Row, TextBox Name, List<TextBox> Values, Border NameCell)> _rows = [];
    private int _builtColumns = -1;

    public TableCellView(NotebookView owner, TableCell cell) : base(owner, cell)
    {
        _cell = cell;
        _errors = Ui.Body("", 14, "Calcy.Err");
        var tableFrame = new Border { Child = _grid, BorderThickness = new Thickness(2, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        tableFrame.Res(Border.BorderBrushProperty, "Calcy.Line");
        var scroll = new ScrollViewer
        {
            Content = tableFrame,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Children = { scroll, _summary } };
        foreach (var (text, tip, action) in new (string, string, Action)[]
        {
            ("+ ROW", "Add a data row (a new list variable)", AddRow),
            ("+ COLUMN", "Add a column", () => _cell.AddColumn()),
            ("- COLUMN", "Remove the last column", () => _cell.RemoveLastColumn()),
            ("+ SUMMARY", "Add a summary line such as mean(Tm)", () => FocusSummary(_cell.AddSummary())),
        })
        {
            var b = Ui.Button(text, tip);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Click += (_, _) => action();
            _buttons.Children.Add(b);
        }
        _buttons.IsVisible = false;
        Body.Content = new StackPanel { Spacing = 10, Margin = new Thickness(14, 10), Children = { wrap, _errors, _buttons } };
        cell.InputChanged += OnInput;
        Rebuild();
        Refresh();
    }

    protected override string KindTitle => "Table cell";

    /// <summary>The value boxes of a row (for tests).</summary>
    public IReadOnlyList<TextBox> ValueBoxes(int row) => _rows[row].Values;

    /// <summary>The summary line views.</summary>
    public IReadOnlyList<MathLineView> SummaryViews => _summaryViews;

    private void AddRow()
    {
        var name = "data" + (_cell.Rows.Count + 1);
        _cell.AddRow(name);
        Dispatcher.UIThread.Post(() => _rows.LastOrDefault().Name?.Focus(), DispatcherPriority.Input);
    }

    private void OnInput(object? sender, EventArgs e)
    {
        if (_builtColumns != _cell.Columns || !_rows.Select(r => r.Row).SequenceEqual(_cell.Rows)) Rebuild();
        SyncSummary();
    }

    private void Rebuild()
    {
        _builtColumns = _cell.Columns;
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _rows.Clear();
        _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int c = 0; c < _cell.Columns; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Place(HeaderCell("#"), 0, 0);
        for (int c = 0; c < _cell.Columns; c++) Place(HeaderCell((c + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), 0, c + 1);
        for (int r = 0; r < _cell.Rows.Count; r++)
        {
            var row = _cell.Rows[r];
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Box(row.Name, bold: true, width: 76);
            Avalonia.Automation.AutomationProperties.SetName(name, "Row name");
            name.LostFocus += (_, _) => _cell.SetName(row, name.Text ?? "");
            name.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) _cell.SetName(row, name.Text ?? "");
            };
            var nameCell = GridCell(name, "Calcy.Card");
            Place(nameCell, r + 1, 0);
            var values = new List<TextBox>();
            for (int c = 0; c < _cell.Columns; c++)
            {
                int col = c;
                var box = Box(row.Values[c], bold: false, width: 66);
                Avalonia.Automation.AutomationProperties.SetName(box, $"{row.Name} value {c + 1}");
                box.TextChanged += (_, _) => _cell.SetValue(row, col, box.Text ?? "");
                values.Add(box);
                Place(GridCell(box, "Calcy.Card"), r + 1, c + 1);
            }
            var remove = Ui.IconButton("close", "Remove this row");
            remove.Margin = new Thickness(6, 0, 0, 0);
            remove.Click += (_, _) => _cell.RemoveRow(row);
            remove.Bind(IsVisibleProperty, _buttons.GetObservable(IsVisibleProperty));
            Grid.SetRow(remove, r + 1);
            Grid.SetColumn(remove, _cell.Columns + 1);
            _grid.Children.Add(remove);
            _rows.Add((row, name, values, nameCell));
        }
        SyncSummary();
    }

    private void Place(Control c, int row, int col)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        _grid.Children.Add(c);
    }

    private static Border HeaderCell(string text)
    {
        var l = Ui.Label(text, 11);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Border { Child = l, BorderThickness = new Thickness(0, 0, 2, 2), Padding = new Thickness(10, 5), MinWidth = 40 };
        b.Res(BackgroundProperty, "Calcy.Head").Res(BorderBrushProperty, "Calcy.Line");
        return b;
    }

    private static Border GridCell(Control child, string background)
    {
        var b = new Border { Child = child, BorderThickness = new Thickness(0, 0, 2, 2) };
        b.Res(BackgroundProperty, background).Res(BorderBrushProperty, "Calcy.Line");
        return b;
    }

    private static TextBox Box(string text, bool bold, double width)
    {
        var t = new TextBox
        {
            Text = text,
            Width = width,
            MinHeight = 32,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 3),
            FontSize = 16,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        t.Res(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        return t;
    }

    private void SyncSummary()
    {
        if (_summaryViews.Select(v => v.Line).SequenceEqual(_cell.Summary)) return;
        foreach (var v in _summaryViews) v.Detach();
        _summaryViews.Clear();
        _summary.Children.Clear();
        foreach (var line in _cell.Summary)
        {
            var v = new MathLineView(line, Owner.Session, Owner.Settings, 18) { ShowResults = !Cell.Collapsed, IsActive = IsSelected };
            v.DeleteRequested += (_, _) => _cell.RemoveSummary(line);
            v.Submitted += (_, _) =>
            {
                int i = _cell.Summary.ToList().IndexOf(line);
                if (i == _cell.Summary.Count - 1 && !string.IsNullOrWhiteSpace(line.Text)) FocusSummary(_cell.AddSummary());
                else if (i >= 0 && i < _summaryViews.Count - 1) _summaryViews[i + 1].Editor.Focus();
            };
            _summaryViews.Add(v);
            _summary.Children.Add(v);
        }
        _summary.IsVisible = _summaryViews.Count > 0;
    }

    private void FocusSummary(MathLine line)
    {
        SyncSummary();
        Dispatcher.UIThread.Post(() => _summaryViews.FirstOrDefault(v => v.Line == line)?.Editor.Focus(), DispatcherPriority.Input);
    }

    public override bool FocusInput(bool atEnd = false)
    {
        if (_rows.Count > 0 && _rows[0].Values.Count > 0)
        {
            _rows[0].Values[0].Focus();
            return true;
        }
        if (_summaryViews.Count > 0)
        {
            _summaryViews[0].Editor.Focus();
            return true;
        }
        return false;
    }

    public override void Refresh()
    {
        base.Refresh();
        var errors = new List<string>();
        foreach (var (row, name, values, nameCell) in _rows)
        {
            if (!name.IsFocused && name.Text != row.Name) name.Text = row.Name;
            bool bad = row.Result is { IsError: true, Tainted: false };
            nameCell.Res(BorderBrushProperty, bad ? "Calcy.Err" : "Calcy.Line");
            if (bad) errors.Add($"{(row.Name.Length > 0 ? row.Name : "Row")}: {row.Result!.Result.ErrorMessage}");
            ToolTip.SetTip(nameCell, bad ? row.Result!.Result.ErrorMessage : $"{row.Name} is a list you can use below, for example mean({row.Name})");
        }
        _errors.Text = string.Join("\n", errors);
        _errors.IsVisible = errors.Count > 0;
        foreach (var v in _summaryViews) v.Refresh();
    }

    public override void ReparseInputs()
    {
        foreach (var v in _summaryViews) v.Reparse();
    }

    protected override void OnSelectionChanged()
    {
        _buttons.IsVisible = IsSelected;
        foreach (var v in _summaryViews) v.IsActive = IsSelected;
    }

    protected override void OnCollapsedChanged()
    {
        foreach (var v in _summaryViews) v.ShowResults = !Cell.Collapsed;
    }

    public override void Detach()
    {
        base.Detach();
        _cell.InputChanged -= OnInput;
        foreach (var v in _summaryViews) v.Detach();
    }
}
