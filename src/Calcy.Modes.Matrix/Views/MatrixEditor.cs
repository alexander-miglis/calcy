using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Modes.Matrix.Logic;

namespace Calcy.Modes.Matrix.Views;

/// <summary>
/// The grid of cells for one matrix, drawn between brackets like the design. A cell shows what was typed while it is being
/// edited and the parsed value (sqrt(8) becomes 2√2) once you leave it; cells that do not parse get the error border.
/// Tab, Shift+Tab, Enter and the arrow keys move between cells; pasting a table fills the grid.
/// </summary>
public sealed class MatrixEditor : Border
{
    private readonly Grid _grid = new() { RowSpacing = 8, ColumnSpacing = 8, Margin = new Thickness(10, 8) };
    private readonly Border _left = new() { Width = 12, BorderThickness = new Thickness(4, 4, 0, 4) };
    private readonly Border _right = new() { Width = 12, BorderThickness = new Thickness(0, 4, 4, 4) };
    private TextBox[] _boxes = [];
    private string[] _texts = [];
    private string?[] _display = [];
    private bool[] _errors = [];
    private int _rows, _cols;
    private string _name = "";
    private bool _suppress;

    /// <summary>A cell's text changed: row, column and the new text.</summary>
    public event Action<int, int, string>? CellEdited;

    /// <summary>A cell got keyboard focus.</summary>
    public event Action<int, int>? CellFocused;

    /// <summary>Text with several cells was pasted into a cell: the row and column where it starts, and the text.</summary>
    public event Action<int, int, string>? TablePasted;

    /// <summary>Creates an empty editor.</summary>
    public MatrixEditor()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        _left.Bind(BorderBrushProperty, _left.GetResourceObservable("Calcy.Ink"));
        _right.Bind(BorderBrushProperty, _right.GetResourceObservable("Calcy.Ink"));
        var dock = new DockPanel();
        DockPanel.SetDock(_left, Dock.Left);
        DockPanel.SetDock(_right, Dock.Right);
        dock.Children.Add(_left);
        dock.Children.Add(_right);
        dock.Children.Add(_grid);
        Child = dock;
    }

    /// <summary>Rows shown.</summary>
    public int Rows => _rows;

    /// <summary>Columns shown.</summary>
    public int Columns => _cols;

    /// <summary>The focused cell, if any.</summary>
    public (int Row, int Col)? FocusedCell { get; private set; }

    /// <summary>The text box of a cell (row and column are zero-based).</summary>
    public TextBox Cell(int row, int col) => _boxes[row * _cols + col];

    /// <summary>Shows a matrix: its name (for accessible cell names), size, typed texts, display texts and error flags.</summary>
    public void Load(string name, int rows, int cols, IReadOnlyList<string> texts, IReadOnlyList<string?> display, IReadOnlyList<bool> errors)
    {
        bool rebuild = rows != _rows || cols != _cols || name != _name;
        _name = name;
        _rows = rows;
        _cols = cols;
        _texts = [.. texts];
        _display = [.. display];
        _errors = [.. errors];
        if (rebuild) Build();
        for (int i = 0; i < _boxes.Length; i++) Refresh(i);
    }

    /// <summary>Updates one cell's error flag and display text after it was re-evaluated.</summary>
    public void UpdateCell(int row, int col, string? display, bool error)
    {
        int i = row * _cols + col;
        if (i < 0 || i >= _boxes.Length) return;
        _display[i] = display;
        _errors[i] = error;
        _boxes[i].Classes.Set("err", error);
        if (!_boxes[i].IsFocused) Refresh(i);
    }

    /// <summary>Moves keyboard focus to a cell.</summary>
    public bool FocusCell(int row, int col)
    {
        if (row < 0 || col < 0 || row >= _rows || col >= _cols) return false;
        return Cell(row, col).Focus(NavigationMethod.Tab);
    }

    private void Build()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        for (int r = 0; r < _rows; r++) _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int c = 0; c < _cols; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        // Big matrices get smaller cells and gaps so more of a 12 × 12 grid fits without scrolling.
        bool wide = _cols > 6, tall = _rows > 6;
        double width = _cols <= 6 ? 84 : _cols <= 9 ? 66 : 56;
        double font = _cols <= 6 && _rows <= 6 ? 20 : _cols <= 9 ? 16 : 14;
        double height = _rows <= 6 ? 48 : _rows <= 9 ? 38 : 34;
        _grid.ColumnSpacing = wide ? 5 : 8;
        _grid.RowSpacing = tall ? 5 : 8;
        _boxes = new TextBox[_rows * _cols];
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                int index = r * _cols + c;
                var box = new TextBox
                {
                    Width = width,
                    Height = height,
                    MinHeight = height,
                    MinWidth = 0,
                    FontSize = font,
                    Padding = new Thickness(4, 0),
                    TextAlignment = TextAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    AcceptsReturn = false,
                    Classes = { "matrix-cell" },
                };
                AutomationProperties.SetName(box, $"{_name} row {r + 1} column {c + 1}");
                box.GotFocus += (_, _) => OnGotFocus(index);
                box.LostFocus += (_, _) => OnLostFocus(index);
                // TextChanged is raised later (posted); the property notification is immediate, which keeps the
                // swap between typed text and displayed value from being mistaken for an edit.
                box.PropertyChanged += (_, e) =>
                {
                    if (e.Property == TextBox.TextProperty) OnTextChanged(index);
                };
                box.AddHandler(KeyDownEvent, (_, e) => OnKeyDown(index, e), RoutingStrategies.Tunnel);
                box.PastingFromClipboard += (_, e) => OnPaste(index, e);
                Grid.SetRow(box, r);
                Grid.SetColumn(box, c);
                _grid.Children.Add(box);
                _boxes[index] = box;
            }
        }
    }

    private void Refresh(int i)
    {
        var box = _boxes[i];
        box.Classes.Set("err", _errors[i]);
        _suppress = true;
        try
        {
            box.Text = box.IsFocused ? _texts[i] : _display[i] ?? _texts[i];
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnGotFocus(int i)
    {
        FocusedCell = (i / _cols, i % _cols);
        var box = _boxes[i];
        _suppress = true;
        try { box.Text = _texts[i]; }
        finally { _suppress = false; }
        box.SelectAll();
        CellFocused?.Invoke(i / _cols, i % _cols);
    }

    private void OnLostFocus(int i)
    {
        if (i >= _boxes.Length) return;
        Refresh(i);
    }

    private void OnTextChanged(int i)
    {
        if (_suppress || i >= _boxes.Length) return;
        var text = _boxes[i].Text ?? "";
        if (text == _texts[i]) return;
        _texts[i] = text;
        CellEdited?.Invoke(i / _cols, i % _cols, text);
    }

    private void OnKeyDown(int i, KeyEventArgs e)
    {
        int r = i / _cols, c = i % _cols;
        var box = _boxes[i];
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool atStart = box.SelectionStart == box.SelectionEnd && box.CaretIndex == 0;
        bool atEnd = box.SelectionStart == box.SelectionEnd && box.CaretIndex >= (box.Text?.Length ?? 0);
        int target = e.Key switch
        {
            Key.Tab when !shift => i + 1 < _boxes.Length ? i + 1 : -1,
            Key.Tab when shift => i - 1,
            Key.Enter => i + 1 < _boxes.Length ? i + 1 : i,
            Key.Up => r > 0 ? i - _cols : -1,
            Key.Down => r + 1 < _rows ? i + _cols : -1,
            Key.Left when atStart || box.SelectionStart != box.SelectionEnd && box.SelectedText == box.Text => c > 0 ? i - 1 : -1,
            Key.Right when atEnd || box.SelectionStart != box.SelectionEnd && box.SelectedText == box.Text => c + 1 < _cols ? i + 1 : -1,
            _ => -2,
        };
        if (target == -2 || target < 0 || target >= _boxes.Length) return;
        e.Handled = true;
        if (target != i) _boxes[target].Focus(NavigationMethod.Tab);
        else box.SelectAll();
    }

    private async void OnPaste(int i, RoutedEventArgs e)
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
        if (string.IsNullOrEmpty(text) || i >= _boxes.Length) return;
        if (PasteParser.IsTable(text))
        {
            TablePasted?.Invoke(i / _cols, i % _cols, text);
            return;
        }
        // A single value: insert it at the caret like a normal paste.
        var box = _boxes[i];
        var current = box.Text ?? "";
        int a = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        int b = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), a, current.Length);
        var single = text.Trim();
        box.Text = current[..a] + single + current[b..];
        box.CaretIndex = a + single.Length;
    }
}
