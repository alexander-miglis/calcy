using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.Modes.Matrix.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Matrix.Views;

/// <summary>
/// The Matrix screen (docs/design/Matrix.dc.html): the matrix library on the left, the grid editor, operation buttons and
/// expression line in the middle, and the result cards on the right.
/// </summary>
public sealed class MatrixModeView : UserControl, IReceivesPayload, IFocusFirst, IBusy, ICardActions
{
    private const string StateKey = "matrix";

    private static readonly (MatrixOperation Op, string Color, string Tip)[] OpSpec =
    [
        (MatrixOperation.Determinant, "head", "Determinant"),
        (MatrixOperation.Inverse, "head", "Inverse matrix, with the Gauss-Jordan steps"),
        (MatrixOperation.Transpose, "head", "Transpose: swap rows and columns"),
        (MatrixOperation.Rref, "head", "Reduced row echelon form, with every row operation"),
        (MatrixOperation.Ref, "head", "Row echelon form, with every row operation"),
        (MatrixOperation.Rank, "head", "Rank: the number of pivots"),
        (MatrixOperation.Trace, "head", "Trace: the sum of the diagonal"),
        (MatrixOperation.Nullity, "head", "Nullity: the dimension of the null space"),
        (MatrixOperation.NullSpace, "head", "Basis of the null space (the solutions of X·x = 0)"),
        (MatrixOperation.ColumnSpace, "head", "Basis of the column space"),
        (MatrixOperation.Eigen, "key", "Eigenvalues and eigenvectors, exact when possible"),
        (MatrixOperation.CharacteristicPolynomial, "key", "Characteristic polynomial det(λI − X)"),
        (MatrixOperation.Condition, "key", "Condition number: how much A·x = b amplifies errors"),
        (MatrixOperation.Norms, "key", "Matrix norms: 1, ∞, Frobenius and 2"),
        (MatrixOperation.Power, "key", "Power Xⁿ with n from the n box (negative n uses the inverse)"),
        (MatrixOperation.Lu, "key", "LU decomposition with partial pivoting (decimals)"),
        (MatrixOperation.Qr, "key", "QR decomposition (decimals)"),
        (MatrixOperation.Cholesky, "key", "Cholesky decomposition of a symmetric positive definite matrix (decimals)"),
        (MatrixOperation.Svd, "key", "Singular value decomposition (decimals)"),
        (MatrixOperation.Scale, "key", "Multiply every entry by k from the k box"),
        (MatrixOperation.Multiply, "ok", "Matrix product X·Y"),
        (MatrixOperation.MultiplyReversed, "ok", "Matrix product Y·X"),
        (MatrixOperation.Add, "ok", "Sum X + Y"),
        (MatrixOperation.Subtract, "ok", "Difference X − Y"),
        (MatrixOperation.Solve, "ok", "Solve X·x = Y: one, none or infinitely many solutions"),
    ];

    private readonly AppServices _s;
    private readonly CalcSession _session;
    private readonly MatrixCalculator _calc;
    private readonly ControlTheme _rowTheme;
    private MatrixLibrary _library;
    private MatrixEntry? _selected;
    private readonly Dictionary<string, MatrixExpr> _synced = [];
    private readonly List<MatrixCard> _cards = [];
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private int _nextCard = 1;
    private CancellationTokenSource? _running;
    private bool _restoring;

    private readonly Grid _root;
    private readonly StackPanel _libraryRows = new();
    private readonly TextBlock _libraryHint = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Classes = { "body", "muted" } };
    private readonly PixelPanel _editorPanel;
    private readonly MatrixEditor _editor = new();
    private readonly TextBlock _rowsText = new() { FontSize = 16, Classes = { "math" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 20, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _colsText = new() { FontSize = 16, Classes = { "math" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 20, TextAlignment = TextAlignment.Center };
    private readonly PixelButton _colsDown;
    private readonly PixelButton _colsUp;
    private readonly TextBlock _cellInfo = new() { FontSize = 15, Classes = { "math" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox _operand = new() { MinWidth = 76, FontSize = 16 };
    private readonly TextBlock _operandHint = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Classes = { "body", "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox _k = new() { Text = "2", Width = 64, FontSize = 16, MinHeight = 34, Height = 34, Padding = new Thickness(6, 0), TextAlignment = TextAlignment.Center, Classes = { "expression" } };
    private readonly TextBox _n = new() { Text = "2", Width = 64, FontSize = 16, MinHeight = 34, Height = 34, Padding = new Thickness(6, 0), TextAlignment = TextAlignment.Center, Classes = { "expression" } };
    private readonly Dictionary<MatrixOperation, PixelButton> _opButtons = [];
    private readonly TextBox _expression;
    private readonly StackPanel _cardList = new() { Spacing = 10, Margin = new Thickness(10) };
    private readonly ScrollViewer _cardScroll;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Control? _welcome;

    /// <summary>Builds the screen and restores the saved library and cards.</summary>
    public MatrixModeView(AppServices services)
    {
        _s = services;
        _session = services.Session;
        _calc = new MatrixCalculator(_session);
        var styles = new MatrixStyles();
        Styles.Add(styles);
        _rowTheme = (ControlTheme)styles.Resources["MatrixLibraryRow"]!;
        _library = new MatrixLibrary();

        // ---------------------------------------------------------- library (left)
        var addMatrix = SmallButton("+ MATRIX", "Add a new matrix", "card", AddMatrix);
        var addVector = SmallButton("+ VECTOR", "Add a new column vector", "card", AddVector);
        var delete = SmallButton("DELETE", "Delete the selected matrix", "paper", DeleteSelected);
        var libraryFooter = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(14, 10, 14, 12),
            Children = { new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { addMatrix, addVector, delete } }, _libraryHint },
        };
        var libraryContent = new DockPanel();
        DockPanel.SetDock(libraryFooter, Dock.Bottom);
        libraryContent.Children.Add(libraryFooter);
        libraryContent.Children.Add(new ScrollViewer { Content = _libraryRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var libraryPanel = new PixelPanel { Header = "Matrices", Content = libraryContent, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(libraryPanel, "Matrix library");

        // ---------------------------------------------------------- editor (middle)
        var rowsDown = Stepper("−", "Fewer rows", () => Resize(-1, 0));
        var rowsUp = Stepper("+", "More rows", () => Resize(1, 0));
        _colsDown = Stepper("−", "Fewer columns", () => Resize(0, -1));
        _colsUp = Stepper("+", "More columns", () => Resize(0, 1));
        var steppers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { Label("ROWS"), rowsDown, _rowsText, rowsUp, new Border { Width = 10 }, Label("COLS"), _colsDown, _colsText, _colsUp },
        };
        _editor.CellEdited += OnCellEdited;
        _editor.CellFocused += (_, _) => UpdateCellInfo();
        _editor.TablePasted += (r, c, text) => PasteTable(r, c, text);
        var fills = new WrapPanel
        {
            ItemSpacing = 6,
            LineSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SmallButton("IDENTITY", "Fill with the identity pattern", "card", () => Fill(e => e.FillIdentity())),
                SmallButton("ZERO", "Fill with zeros", "card", () => Fill(e => e.FillZero())),
                SmallButton("RANDOM", "Fill with random whole numbers from -9 to 9", "card", () => Fill(e => e.FillRandom(Random.Shared))),
                SmallButton("PASTE", "Paste a table from a spreadsheet (tab or comma separated)", "card", PasteFromClipboard),
            },
        };
        var toolbar = new DockPanel { Margin = new Thickness(14, 0, 14, 6) };
        DockPanel.SetDock(fills, Dock.Right);
        toolbar.Children.Add(fills);
        toolbar.Children.Add(_cellInfo);
        var hint = new TextBlock
        {
            Text = "Cells take fractions (2/3), decimals and expressions (√2, π/4). Results stay exact.",
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 0, 14, 12),
            Classes = { "body", "muted" },
        };
        var editorFooter = new StackPanel { Children = { toolbar, hint } };
        var editorContent = new DockPanel();
        DockPanel.SetDock(editorFooter, Dock.Bottom);
        editorContent.Children.Add(editorFooter);
        editorContent.Children.Add(new ScrollViewer
        {
            Content = _editor,
            Padding = new Thickness(20, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        _editorPanel = new PixelPanel { Header = "Edit A", HeaderContent = steppers, Content = editorContent };
        _editorPanel.Bind(PixelPanel.HeaderBrushProperty, _editorPanel.GetResourceObservable("Calcy.Sel"));
        AutomationProperties.SetName(_editorPanel, "Matrix editor");

        // ---------------------------------------------------------- operand strip and operations
        AutomationProperties.SetName(_operand, "Second matrix");
        AutomationProperties.SetName(_k, "Scalar k");
        AutomationProperties.SetName(_n, "Power n");
        _operand.SelectionChanged += (_, _) => { UpdateOpLabels(); ScheduleSave(); };
        _k.TextChanged += (_, _) => { UpdateOpLabels(); ScheduleSave(); };
        _n.TextChanged += (_, _) => { UpdateOpLabels(); ScheduleSave(); };
        var operandHint = _operandHint;
        var operandRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { Label("WITH"), _operand, new Border { Width = 6 }, Label("K ="), _k, new Border { Width = 6 }, Label("N ="), _n },
        };
        var operandDock = new DockPanel { Children = { operandRow, operandHint } };
        DockPanel.SetDock(operandRow, Dock.Left);
        operandHint.Margin = new Thickness(14, 0, 0, 0);
        var operandStrip = Boxed(operandDock, new Thickness(12, 6), large: false);

        var ops = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        for (int c = 0; c < 5; c++) ops.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int r = 0; r < (OpSpec.Length + 4) / 5; r++) ops.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < OpSpec.Length; i++)
        {
            var (op, color, tip) = OpSpec[i];
            var b = new PixelButton { Classes = { color }, MinHeight = 44, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 4) };
            b.Bind(PixelButton.FontFamilyProperty, b.GetResourceObservable("Calcy.Font.Math"));
            b.FontWeight = FontWeight.Normal;
            ToolTip.SetTip(b, tip);
            var operation = op;
            b.Click += async (_, _) => await RunOperationAsync(operation);
            Grid.SetRow(b, i / 5);
            Grid.SetColumn(b, i % 5);
            ops.Children.Add(b);
            _opButtons[op] = b;
        }

        _expression = new TextBox
        {
            PlaceholderText = "A⁻¹·B,  2A - I,  Aᵀ·A  or  D = A·B",
            FontSize = 20,
            MinHeight = 52,
            Padding = new Thickness(12, 4),
            Classes = { "expression" },
        };
        AutomationProperties.SetName(_expression, "Matrix expression");
        _expression.KeyDown += OnExpressionKeyDown;
        _expression.TextChanged += (_, _) => ScheduleSave();
        var equals = new PixelButton { Content = "=", Classes = { "primary" }, Width = 72, MinHeight = 52, FontSize = 22 };
        AutomationProperties.SetName(equals, "Calculate");
        equals.Click += async (_, _) => await EvaluateExpressionAsync();
        var exprDock = new DockPanel();
        DockPanel.SetDock(equals, Dock.Right);
        equals.Margin = new Thickness(10, 0, 0, 0);
        exprDock.Children.Add(equals);
        exprDock.Children.Add(_expression);
        var expressionPanel = Boxed(exprDock, new Thickness(10), large: true);

        var middle = new FlexStack { Spacing = 14, VerticalAlignment = VerticalAlignment.Top, Children = { _editorPanel, operandStrip, ops, expressionPanel } };

        // ---------------------------------------------------------- cards (right)
        _cardScroll = new ScrollViewer { Content = _cardList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var clear = SmallButton("CLEAR", "Remove every card", "paper", ClearCards);
        var cardsPanel = new PixelPanel { Header = "Cards", HeaderContent = clear, Content = _cardScroll };
        cardsPanel.Bind(PixelPanel.HeaderBrushProperty, cardsPanel.GetResourceObservable("Calcy.Mem"));
        AutomationProperties.SetName(cardsPanel, "Matrix cards");

        _root = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,360"), ColumnSpacing = 16, Margin = new Thickness(16) };
        Grid.SetColumn(libraryPanel, 0);
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(cardsPanel, 2);
        _root.Children.Add(libraryPanel);
        _root.Children.Add(middle);
        _root.Children.Add(cardsPanel);
        Content = _root;

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
        SizeChanged += (_, e) => Adapt(e.NewSize.Width);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _running is not null) { _running.Cancel(); e.Handled = true; }
        };
        _session.Env.Changed += (_, _) => Dispatcher.UIThread.Post(SyncFromSession);
        Restore();
    }

    // ------------------------------------------------------------------ public surface

    /// <summary>True while an operation runs in the background.</summary>
    public bool IsBusy => _running is not null;

    /// <summary>The matrix library (for tests).</summary>
    public MatrixLibrary Library => _library;

    /// <summary>The selected matrix.</summary>
    public MatrixEntry? Selected => _selected;

    /// <summary>The grid editor.</summary>
    public MatrixEditor Editor => _editor;

    /// <summary>The cards, newest first.</summary>
    public IReadOnlyList<MatrixCard> Cards => _cards;

    /// <summary>The card controls, newest first.</summary>
    public IEnumerable<CardView> CardViews => _cardList.Children.OfType<CardView>();

    /// <summary>The expression line.</summary>
    public TextBox ExpressionBox => _expression;

    /// <summary>The button for an operation.</summary>
    public PixelButton OperationButton(MatrixOperation op) => _opButtons[op];

    /// <summary>Puts keyboard focus in the first cell.</summary>
    public void FocusFirst() => _editor.FocusCell(0, 0);

    /// <summary>Accepts text for the expression line and numeric tables (DataRequest) as new matrices.</summary>
    public void Receive(object payload)
    {
        switch (payload)
        {
            case InputRequest r:
                _expression.Text = r.Text;
                _expression.CaretIndex = r.Text.Length;
                _expression.Focus();
                if (r.Evaluate) _ = EvaluateExpressionAsync();
                break;
            case DataRequest d when d.Columns.Count > 0:
                ImportData(d);
                break;
        }
    }

    /// <summary>Selects a matrix by name.</summary>
    public void Select(string name)
    {
        var entry = _library.Find(name);
        if (entry is null) return;
        _selected = entry;
        LoadEditor();
        RefreshLibrary();
        RefreshOperands();
        ScheduleSave();
    }

    /// <summary>Fills the selected matrix from pasted text (spreadsheet rows, CSV or Calcy matrix syntax).</summary>
    public void PasteText(string text) => PasteTable(0, 0, text);

    /// <summary>Runs an operation on the selected matrix and adds its card.</summary>
    public async Task RunOperationAsync(MatrixOperation op)
    {
        if (_selected is null)
        {
            _s.ShowStatus("Add a matrix first");
            return;
        }
        var operands = BuildOperands();
        await RunAsync(MatrixCalculator.InputOf(op, operands), ct => _calc.Run(op, operands, ct));
    }

    /// <summary>Evaluates the expression line and adds its card.</summary>
    public async Task EvaluateExpressionAsync()
    {
        var text = (_expression.Text ?? "").Trim();
        if (text.Length == 0) return;
        if (_history.Count == 0 || _history[^1] != text) _history.Add(text);
        _historyIndex = -1;
        int size = _selected?.Rows ?? 3;
        await RunAsync(text, ct => _calc.Expression(text, size, ct));
        _expression.SelectAll();
    }

    // ------------------------------------------------------------------ layout helpers

    private void Adapt(double width)
    {
        bool narrow = width < 1240;
        _root.ColumnDefinitions[0].Width = new GridLength(narrow ? 190 : 220);
        _root.ColumnDefinitions[2].Width = new GridLength(narrow ? 300 : 360);
    }

    private static TextBlock Label(string text) => new() { Text = text, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };

    private static PixelButton SmallButton(string text, string tip, string color, Action click)
    {
        var b = new PixelButton { Content = text, Classes = { "small" } };
        if (color.Length > 0) b.Classes.Add(color);
        ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static PixelButton Stepper(string text, string tip, Action click)
    {
        var b = new PixelButton { Content = text, Classes = { "paper", "flat" }, Width = 36, Height = 36, MinHeight = 36, Padding = new Thickness(0), FontSize = 16, BorderThickness = new Thickness(2) };
        b.Bind(PixelButton.FontFamilyProperty, b.GetResourceObservable("Calcy.Font.Math"));
        ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static Border Boxed(Control child, Thickness padding, bool large)
    {
        var b = new Border { Child = child, Padding = padding, BorderThickness = new Thickness(large ? 4 : 3) };
        b.Bind(Border.BackgroundProperty, b.GetResourceObservable("Calcy.Paper"));
        b.Bind(Border.BorderBrushProperty, b.GetResourceObservable("Calcy.Line"));
        b.Bind(Border.BoxShadowProperty, b.GetResourceObservable(large ? "Calcy.ShadowLarge" : "Calcy.ShadowSmall"));
        b.Bind(TextElement.ForegroundProperty, b.GetResourceObservable("Calcy.Ink"));
        return b;
    }

    // ------------------------------------------------------------------ library

    private void RefreshLibrary()
    {
        _libraryRows.Children.Clear();
        foreach (var entry in _library.Entries)
        {
            var name = new TextBlock { Text = entry.Name, FontSize = 26, FontWeight = FontWeight.Bold, Classes = { "math" }, VerticalAlignment = VerticalAlignment.Center };
            var dims = new TextBlock { Text = entry.Dimensions, FontSize = 12, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel();
            DockPanel.SetDock(dims, Dock.Right);
            row.Children.Add(dims);
            row.Children.Add(name);
            var button = new Button { Theme = _rowTheme, Content = row };
            if (entry == _selected) button.Classes.Add("selected");
            AutomationProperties.SetName(button, $"{entry.Name}, {entry.Rows} by {entry.Cols}{(entry.IsVector ? " vector" : "")}");
            var n = entry.Name;
            button.Click += (_, _) => Select(n);
            _libraryRows.Children.Add(button);
        }
        var names = _library.Entries.Take(2).Select(e => e.Name).ToList();
        string a = names.Count > 0 ? names[0] : "A", b = names.Count > 1 ? names[1] : "B";
        _libraryHint.Text = $"Use them anywhere: {a}⁻¹·{b}, det({a}), {a}².";
    }

    private void RefreshOperands()
    {
        var names = _library.Entries.Select(e => e.Name).ToList();
        var current = _operand.SelectedItem as string;
        _operand.ItemsSource = names;
        if (current is null || !names.Contains(current))
            current = names.FirstOrDefault(n => n != _selected?.Name) ?? names.FirstOrDefault();
        _operand.SelectedItem = current;
        UpdateOpLabels();
    }

    private bool NameTaken(string name) =>
        _library.Find(name) is not null || MatrixLibrary.IsBuiltInName(name, _session) || _session.Env.Variables.ContainsKey(name);

    private void AddMatrix()
    {
        var entry = _library.Add(new MatrixEntry(_library.NextMatrixName(NameTaken), 3, 3));
        Push(entry);
        Select(entry.Name);
        _editor.FocusCell(0, 0);
    }

    private void AddVector()
    {
        var entry = _library.Add(new MatrixEntry(_library.NextVectorName(NameTaken), 3, 1, isVector: true));
        Push(entry);
        Select(entry.Name);
        _editor.FocusCell(0, 0);
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;
        var name = _selected.Name;
        int index = _library.Entries.ToList().IndexOf(_selected);
        _library.Remove(name);
        _synced.Remove(name);
        _session.Env.Remove(name);
        _selected = _library.Entries.Count == 0 ? null : _library.Entries[Math.Clamp(index, 0, _library.Entries.Count - 1)];
        _s.ShowStatus($"Deleted {name}");
        LoadEditor();
        RefreshLibrary();
        RefreshOperands();
        ScheduleSave();
    }

    private void ImportData(DataRequest d)
    {
        int cols = Math.Min(d.Columns.Count, MatrixLibrary.MaxSize);
        int rows = Math.Min(d.Columns.Max(c => c.Count), MatrixLibrary.MaxSize);
        var cells = new string[rows * cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                cells[r * cols + c] = r < d.Columns[c].Count ? d.Columns[c][r].ToString("R", CultureInfo.InvariantCulture) : "0";
        var name = MatrixLibrary.IsValidName(d.Name, _session, out _) && _library.Find(d.Name) is null ? d.Name : _library.NextMatrixName(NameTaken);
        var entry = _library.Add(new MatrixEntry(name, rows, cols, cells: cells));
        Push(entry);
        Select(entry.Name);
        _s.ShowStatus($"Added {name} ({rows} × {cols})");
    }

    // ------------------------------------------------------------------ session sync

    /// <summary>Writes a library entry into the session when all its cells are valid.</summary>
    private void Push(MatrixEntry entry)
    {
        var (m, _) = MatrixLibrary.Evaluate(entry, _session);
        if (m is null) return;
        _synced[entry.Name] = m;
        if (!_session.Env.TryGetVariable(entry.Name, out var existing) || !existing.Equals(m))
            _session.Env.SetVariable(entry.Name, m);
    }

    /// <summary>Picks up matrices defined or changed elsewhere (Advanced, MCP) and drops ones that were deleted there.</summary>
    private void SyncFromSession()
    {
        bool libraryChanged = false, selectedChanged = false;
        var variables = _session.Env.Variables;
        foreach (var (name, value) in variables)
        {
            if (name == "ans" || value is not MatrixExpr m || m.Rows > MatrixLibrary.MaxSize || m.Cols > MatrixLibrary.MaxSize || m.Rows == 0 || m.Cols == 0) continue;
            if (_synced.TryGetValue(name, out var known) && known.Equals(m)) continue;
            var entry = _library.Find(name);
            if (entry is null)
            {
                _library.Add(new MatrixEntry(name, m.Rows, m.Cols, cells: MatrixLibrary.CellTexts(m)));
                libraryChanged = true;
            }
            else
            {
                entry.SetAll(m.Rows, entry.IsVector ? 1 : m.Cols, MatrixLibrary.CellTexts(m));
                libraryChanged = true;
                if (entry == _selected) selectedChanged = true;
            }
            _synced[name] = m;
        }
        foreach (var name in _synced.Keys.ToList())
        {
            if (variables.TryGetValue(name, out var v) && v is MatrixExpr) continue;
            _synced.Remove(name);
            if (_library.Remove(name))
            {
                libraryChanged = true;
                if (_selected?.Name == name)
                {
                    _selected = _library.Entries.FirstOrDefault();
                    selectedChanged = true;
                }
            }
        }
        if (!libraryChanged) return;
        if (selectedChanged) LoadEditor();
        RefreshLibrary();
        RefreshOperands();
        ScheduleSave();
    }

    // ------------------------------------------------------------------ editor

    private void LoadEditor()
    {
        if (_selected is null)
        {
            _editorPanel.Header = "No matrix";
            _editor.Load("", 1, 1, ["0"], [null], [false]);
            _editor.IsEnabled = false;
            _rowsText.Text = "0";
            _colsText.Text = "0";
            UpdateOpLabels();
            return;
        }
        _editor.IsEnabled = true;
        var e = _selected;
        _editorPanel.Header = $"Edit {e.Name}";
        _rowsText.Text = e.Rows.ToString(CultureInfo.InvariantCulture);
        _colsText.Text = e.Cols.ToString(CultureInfo.InvariantCulture);
        _colsDown.IsEnabled = !e.IsVector && e.Cols > 1;
        _colsUp.IsEnabled = !e.IsVector && e.Cols < MatrixLibrary.MaxSize;
        var (_, cells) = MatrixLibrary.Evaluate(e, _session);
        _editor.Load(e.Name, e.Rows, e.Cols, e.Cells, [.. cells.Select(Display)], [.. cells.Select(c => !c.IsValid)]);
        UpdateCellInfo();
        UpdateOpLabels();
    }

    private string? Display(CellValue cell)
    {
        if (!cell.IsValid) return null;
        var shown = ExprPrinter.Print(cell.Value!, new PrintOptions { Unicode = true, Numbers = NumberFormatOptions.From(_session.Settings) });
        return shown == cell.Text.Trim() ? null : shown;
    }

    private void OnCellEdited(int r, int c, string text)
    {
        if (_selected is null) return;
        _selected[r, c] = text;
        var cell = MatrixLibrary.EvaluateCell(text, _session);
        _editor.UpdateCell(r, c, Display(cell), !cell.IsValid);
        UpdateCellInfo();
        Push(_selected);
        ScheduleSave();
    }

    private void UpdateCellInfo()
    {
        if (_selected is null)
        {
            _cellInfo.Text = "";
            return;
        }
        var e = _selected;
        _cellInfo.ClearValue(TextBlock.ForegroundProperty);
        if (_editor.FocusedCell is { } f && f.Row < e.Rows && f.Col < e.Cols)
        {
            var cell = MatrixLibrary.EvaluateCell(e[f.Row, f.Col], _session);
            string where = e.Name + LinearSystem.Subscript(f.Row + 1) + "," + LinearSystem.Subscript(f.Col + 1);
            if (!cell.IsValid)
            {
                _cellInfo.Text = $"{where}: {cell.Error}";
                _cellInfo.Bind(TextBlock.ForegroundProperty, _cellInfo.GetResourceObservable("Calcy.Err"));
                return;
            }
            var exact = _calc.Formatter.Text(cell.Value!);
            var approx = _calc.Formatter.ApproxText(cell.Value!);
            _cellInfo.Text = approx is null ? $"{where} = {exact}" : $"{where} = {exact} ≈ {approx}";
            return;
        }
        var (_, cells) = MatrixLibrary.Evaluate(e, _session);
        int bad = cells.Count(c => !c.IsValid);
        _cellInfo.Text = bad == 0 ? $"{e.Name} is {e.Rows} × {e.Cols}" : $"{bad} cell{(bad == 1 ? "" : "s")} need{(bad == 1 ? "s" : "")} a fix";
        if (bad > 0) _cellInfo.Bind(TextBlock.ForegroundProperty, _cellInfo.GetResourceObservable("Calcy.Err"));
    }

    private void Resize(int dr, int dc)
    {
        if (_selected is null) return;
        _selected.Resize(_selected.Rows + dr, _selected.Cols + dc);
        Push(_selected);
        LoadEditor();
        RefreshLibrary();
        ScheduleSave();
    }

    private void Fill(Action<MatrixEntry> fill)
    {
        if (_selected is null) return;
        fill(_selected);
        Push(_selected);
        LoadEditor();
        ScheduleSave();
    }

    private async void PasteFromClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        string? text;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            _s.ShowStatus("The clipboard could not be read");
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            _s.ShowStatus("The clipboard has no text to paste");
            return;
        }
        PasteTable(0, 0, text);
    }

    private void PasteTable(int row, int col, string text)
    {
        if (_selected is null) return;
        var table = PasteParser.Parse(text);
        if (table is null) return;
        var e = _selected;
        // A vector takes a pasted row as its column.
        if (e.IsVector && table.Length == 1 && table[0].Length > 1) table = [.. table[0].Select(x => new[] { x })];
        int rows = table.Length, cols = table.Max(r => r.Length);
        bool clipped = row + rows > MatrixLibrary.MaxSize || col + cols > MatrixLibrary.MaxSize || (e.IsVector && cols > 1);
        int newRows = row == 0 && col == 0 ? row + rows : Math.Max(e.Rows, row + rows);
        int newCols = row == 0 && col == 0 ? col + cols : Math.Max(e.Cols, col + cols);
        e.Resize(newRows, newCols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (row + r < e.Rows && col + c < e.Cols) e[row + r, col + c] = table[r][c];
        Push(e);
        LoadEditor();
        RefreshLibrary();
        ScheduleSave();
        _s.ShowStatus(clipped ? $"Pasted into {e.Name}; the part beyond {e.Rows} × {e.Cols} was left out" : $"Pasted {rows} × {cols} into {e.Name}");
    }

    // ------------------------------------------------------------------ operations

    private Operands BuildOperands()
    {
        var x = _selected!;
        var (xm, xcells) = MatrixLibrary.Evaluate(x, _session);
        var yName = _operand.SelectedItem as string;
        var y = yName is null ? null : _library.Find(yName);
        MatrixExpr? ym = null;
        string? yError = null;
        if (y is not null)
        {
            var (m, ycells) = MatrixLibrary.Evaluate(y, _session);
            ym = m;
            yError = m is null ? CellError(y, ycells) : null;
        }
        return new Operands
        {
            XName = x.Name,
            X = xm,
            XError = xm is null ? CellError(x, xcells) : null,
            YName = y?.Name,
            Y = ym,
            YError = yError,
            K = (_k.Text ?? "").Trim().Length == 0 ? "2" : _k.Text!.Trim(),
            N = (_n.Text ?? "").Trim().Length == 0 ? "2" : _n.Text!.Trim(),
        };
    }

    private static string CellError(MatrixEntry e, CellValue[] cells)
    {
        int i = Array.FindIndex(cells, c => !c.IsValid);
        return i < 0 ? "" : $"Cell ({i / e.Cols + 1}, {i % e.Cols + 1}) of {e.Name} needs a fix: {cells[i].Error}";
    }

    private void UpdateOpLabels()
    {
        string x = _selected?.Name ?? "A";
        string y = _operand.SelectedItem as string ?? "B";
        string k = (_k.Text ?? "").Trim();
        string n = (_n.Text ?? "").Trim();
        string kLabel = k.Length is > 0 and <= 3 && k.All(ch => char.IsDigit(ch) || ch == '-') ? k : "k";
        string nLabel = n.Length is > 0 and <= 3 && int.TryParse(n, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int nv) ? Superscript(nv) : "ⁿ";
        _operandHint.Text = $"used by the green buttons, k{x} and {x}ⁿ";
        foreach (var (op, button) in _opButtons)
        {
            button.Content = op switch
            {
                MatrixOperation.Determinant => "det",
                MatrixOperation.Inverse => x + "⁻¹",
                MatrixOperation.Transpose => x + "ᵀ",
                MatrixOperation.Rref => "rref",
                MatrixOperation.Ref => "ref",
                MatrixOperation.Rank => "rank",
                MatrixOperation.Trace => "trace",
                MatrixOperation.Nullity => "nullity",
                MatrixOperation.NullSpace => "null " + x,
                MatrixOperation.ColumnSpace => "col " + x,
                MatrixOperation.Eigen => "eigen λ",
                MatrixOperation.CharacteristicPolynomial => "char poly",
                MatrixOperation.Condition => "cond",
                MatrixOperation.Norms => "norms",
                MatrixOperation.Power => x + nLabel,
                MatrixOperation.Lu => "LU",
                MatrixOperation.Qr => "QR",
                MatrixOperation.Cholesky => "Cholesky",
                MatrixOperation.Svd => "SVD",
                MatrixOperation.Scale => kLabel + x,
                MatrixOperation.Multiply => x + "·" + y,
                MatrixOperation.MultiplyReversed => y + "·" + x,
                MatrixOperation.Add => x + "+" + y,
                MatrixOperation.Subtract => x + "−" + y,
                MatrixOperation.Solve => x + "·x=" + y,
                _ => op.ToString(),
            };
            var tip = OpSpec.First(s => s.Op == op).Tip.Replace("X", x).Replace("Y", y);
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, tip);
            button.IsEnabled = _selected is not null;
        }
    }

    private static string Superscript(int v) =>
        new([.. v.ToString(CultureInfo.InvariantCulture).Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0'])]);

    private async Task RunAsync(string what, Func<CancellationToken, MatrixCard> work)
    {
        if (_running is not null) return;
        _running = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = _running.Token;
        Card? pending = null;
        var timer = DispatcherTimer.RunOnce(() =>
        {
            pending = new Card { Title = "WORKING", Meta = "ESC TO STOP", Content = new TextBlock { Text = what, FontSize = 15, Classes = { "math", "muted" } } };
            HideWelcome();
            _cardList.Children.Insert(0, pending);
        }, TimeSpan.FromMilliseconds(250));
        MatrixCard card;
        try
        {
            card = await Task.Run(() => work(token), token);
        }
        catch (OperationCanceledException)
        {
            card = new MatrixCard
            {
                Title = "STOPPED",
                Input = what,
                IsError = true,
                Time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                Blocks = [CardBlock.NoteText("Stopped: the calculation took too long or was cancelled.")],
            };
        }
        finally
        {
            timer.Dispose();
            _running.Dispose();
            _running = null;
        }
        if (pending is not null) _cardList.Children.Remove(pending);
        AddCard(card);
    }

    // ------------------------------------------------------------------ expression line

    private async void OnExpressionKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await EvaluateExpressionAsync();
                break;
            case Key.Up when _history.Count > 0:
                e.Handled = true;
                _historyIndex = _historyIndex < 0 ? _history.Count - 1 : Math.Max(0, _historyIndex - 1);
                _expression.Text = _history[_historyIndex];
                _expression.CaretIndex = _expression.Text.Length;
                break;
            case Key.Down when _historyIndex >= 0:
                e.Handled = true;
                _historyIndex++;
                if (_historyIndex >= _history.Count) { _historyIndex = -1; _expression.Text = ""; }
                else _expression.Text = _history[_historyIndex];
                _expression.CaretIndex = _expression.Text?.Length ?? 0;
                break;
            case Key.Escape when _running is null:
                e.Handled = true;
                _expression.Text = "";
                break;
        }
    }

    // ------------------------------------------------------------------ cards

    private void AddCard(MatrixCard card, bool restoring = false)
    {
        if (!restoring)
        {
            card.Number = _nextCard++;
            _cards.Insert(0, card);
        }
        HideWelcome();
        var view = new CardView(card, this);
        if (restoring) _cardList.Children.Add(view);
        else _cardList.Children.Insert(0, view);
        while (_cards.Count > MatrixModeState.MaxCards)
        {
            var last = _cards[^1];
            _cards.RemoveAt(_cards.Count - 1);
            var lastView = _cardList.Children.OfType<CardView>().FirstOrDefault(v => v.Model == last);
            if (lastView is not null) _cardList.Children.Remove(lastView);
        }
        if (!restoring)
        {
            _cardScroll.Offset = default;
            ScheduleSave();
        }
    }

    private void ClearCards()
    {
        _cards.Clear();
        _cardList.Children.Clear();
        ShowWelcome();
        ScheduleSave();
    }

    private void ShowWelcome()
    {
        if (_welcome is not null) return;
        var text = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Every result lands here as a card, newest on top.", Classes = { "body" }, FontSize = 14 },
                new TextBlock { Text = "COPY gives Calcy input, COPY LATEX gives LaTeX, STORE AS saves the result as a new matrix and USE sends it to Advanced.", Classes = { "body", "muted" }, FontSize = 13 },
            },
        };
        _welcome = new Card { Title = "WELCOME", Content = text };
        _cardList.Children.Insert(0, _welcome);
    }

    private void HideWelcome()
    {
        if (_welcome is null) return;
        _cardList.Children.Remove(_welcome);
        _welcome = null;
    }

    void ICardActions.Copy(string text, string what)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) _ = clipboard.SetTextAsync(text);
        _s.ShowStatus($"Copied {what}: {(text.Length > 60 ? text[..58] + "…" : text)}");
    }

    void ICardActions.Use(string text) => _s.Navigate("advanced", new InputRequest(text, Evaluate: false));

    string? ICardActions.Store(string name, StorePart part) => Store(name, part);

    /// <summary>Saves a card value under a name; matrices also join the library. Returns an error message or null.</summary>
    public string? Store(string name, StorePart part)
    {
        if (!MatrixLibrary.IsValidName(name, _session, out var error)) return error;
        Expr value;
        try
        {
            value = _session.EvaluateExpression(part.Text);
        }
        catch (CalcException ex)
        {
            return ex.Message;
        }
        bool replaced = _session.Env.Variables.ContainsKey(name);
        if (value is MatrixExpr m && m.Rows <= MatrixLibrary.MaxSize && m.Cols <= MatrixLibrary.MaxSize)
        {
            var entry = _library.Find(name);
            if (entry is null) _library.Add(new MatrixEntry(name, m.Rows, m.Cols, cells: MatrixLibrary.CellTexts(m)));
            else entry.SetAll(m.Rows, entry.IsVector ? 1 : m.Cols, MatrixLibrary.CellTexts(m));
            _synced[name] = m;
            _session.Env.SetVariable(name, m);
            if (_selected?.Name == name) LoadEditor();
            RefreshLibrary();
            RefreshOperands();
            ScheduleSave();
        }
        else _session.Env.SetVariable(name, value);
        _s.ShowStatus(replaced ? $"Replaced {name}" : $"Stored as {name}");
        return null;
    }

    string ICardActions.SuggestName(StorePart part)
    {
        if (part.IsMatrix)
        {
            var letter = part.Label.Length > 0 ? part.Label[..1] : "";
            if (letter.Length == 1 && char.IsUpper(letter[0]) && !NameTaken(letter) && MatrixLibrary.IsValidName(letter, _session, out _)) return letter;
            return _library.NextMatrixName(NameTaken);
        }
        for (int k = 1; ; k++)
        {
            var n = "r" + k.ToString(CultureInfo.InvariantCulture);
            if (!NameTaken(n)) return n;
        }
    }

    void ICardActions.Edit(string text)
    {
        _expression.Text = text;
        _expression.CaretIndex = text.Length;
        _expression.Focus();
    }

    void ICardActions.Changed() => ScheduleSave();

    bool ICardActions.PreferDecimals => _session.Settings.Results == ResultPreference.DecimalFirst;

    // ------------------------------------------------------------------ persistence

    private void Restore()
    {
        _restoring = true;
        var state = _s.Prefs.GetModeState<MatrixModeState>(StateKey);
        _library = state is { Matrices.Count: > 0 } ? MatrixLibrary.FromState(state.Matrices) : MatrixLibrary.CreateDefault();
        // Names that became functions or non-matrix variables elsewhere get a fresh name rather than overwriting them.
        foreach (var entry in _library.Entries.ToList())
        {
            bool clash = MatrixLibrary.IsBuiltInName(entry.Name, _session) ||
                         (_session.Env.TryGetVariable(entry.Name, out var v) && v is not MatrixExpr);
            if (!clash) continue;
            entry.Name = entry.IsVector ? _library.NextVectorName(NameTaken) : _library.NextMatrixName(NameTaken);
        }
        foreach (var entry in _library.Entries)
        {
            // A matrix already defined in this session (for example in Advanced) is newer than the saved copy.
            if (_session.Env.TryGetVariable(entry.Name, out var v) && v is MatrixExpr existing)
            {
                entry.SetAll(existing.Rows, entry.IsVector ? 1 : existing.Cols, MatrixLibrary.CellTexts(existing));
                _synced[entry.Name] = existing;
            }
            else Push(entry);
        }
        _selected = (state?.Selected is { } sel ? _library.Find(sel) : null) ?? _library.Entries.FirstOrDefault();
        if (state is not null)
        {
            _k.Text = state.K;
            _n.Text = state.N;
            _expression.Text = state.Expression ?? "";
            _nextCard = Math.Max(1, state.NextCardNumber);
            foreach (var card in state.Cards.Take(MatrixModeState.MaxCards))
            {
                _cards.Add(card);
                AddCard(card, restoring: true);
            }
        }
        if (_cards.Count == 0) ShowWelcome();
        RefreshLibrary();
        RefreshOperands();
        if (state?.Operand is { } operand && _library.Find(operand) is not null) _operand.SelectedItem = operand;
        LoadEditor();
        SyncFromSession();
        _restoring = false;
    }

    /// <summary>
    /// Keeps the saved preferences small: only the newest cards keep their elimination steps, and very long step lists
    /// (big matrices) are not saved at all. The results themselves are always kept.
    /// </summary>
    private static MatrixCard ForSaving(MatrixCard card, int index)
    {
        const int CardsWithSteps = 20, MaxStepCells = 4000;
        int cells = card.Steps.Sum(s => s.Cells?.Sum(r => r.Length) ?? 0);
        if (card.Steps.Count == 0 || (index < CardsWithSteps && cells <= MaxStepCells)) return card;
        return new MatrixCard
        {
            Number = card.Number,
            Title = card.Title,
            Time = card.Time,
            Input = card.Input,
            IsError = card.IsError,
            Blocks = card.Blocks,
            CopyText = card.CopyText,
            Latex = card.Latex,
            StoreParts = card.StoreParts,
            UseText = card.UseText,
            Source = card.Source,
            PreferDecimals = card.PreferDecimals,
        };
    }

    private void ScheduleSave()
    {
        if (_restoring) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Saves the library, cards and inputs into the preferences now.</summary>
    public void Save()
    {
        var state = new MatrixModeState
        {
            Matrices = _library.ToState(),
            Selected = _selected?.Name,
            Operand = _operand.SelectedItem as string,
            K = _k.Text ?? "2",
            N = _n.Text ?? "2",
            Expression = _expression.Text,
            Cards = [.. _cards.Take(MatrixModeState.MaxCards).Select((c, i) => ForSaving(c, i))],
            NextCardNumber = _nextCard,
        };
        _s.Prefs.SetModeState(StateKey, state);
        _s.Prefs.Save();
    }
}
