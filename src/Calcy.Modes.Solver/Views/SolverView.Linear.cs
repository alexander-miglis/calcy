using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Math;

namespace Calcy.Modes.Solver.Views;

public sealed partial class SolverView
{
    private const int MaxLinear = LinearSystemRequest.Max;
    private readonly TextBox[,] _gridBoxes = new TextBox[MaxLinear, MaxLinear + 1];
    private readonly Control[,] _gridCells = new Control[MaxLinear, MaxLinear];
    private readonly TextBlock[,] _gridTerms = new TextBlock[MaxLinear, MaxLinear];
    private readonly Control[] _gridRows = new Control[MaxLinear];
    private readonly MathEditor[] _linearEditors = new MathEditor[MaxLinear];
    private readonly Control[] _linearEditorRows = new Control[MaxLinear];
    private readonly Dictionary<int, PixelToggle> _unknownButtons = [];
    private readonly StackPanel _gridPanel = new() { Spacing = 10 };
    private readonly StackPanel _typedPanel = new() { Spacing = 10 };
    private readonly PixelText _linearCaption = Ui.Caption("", 2);
    private readonly TextBlock _equationCount = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Center, MinWidth = 22, TextAlignment = Avalonia.Media.TextAlignment.Center };
    private readonly TextBlock _linearHint = Ui.Text("", "hint");
    private Control _unknownGroup = null!;
    private PixelToggle _gridMode = null!;
    private PixelToggle _typedMode = null!;

    /// <summary>The grid box for a coefficient (column 6 is the right side), for tests.</summary>
    public TextBox GridBox(int row, int column) => _gridBoxes[row, column];

    /// <summary>The typed equation editors of the linear system tool, for tests.</summary>
    public IReadOnlyList<MathEditor> LinearEditors => _linearEditors;

    private Control BuildLinearPage()
    {
        var page = Page();

        var unknownRow = Ui.Row(8);
        unknownRow.Children.Add(Ui.Caption("Unknowns"));
        for (int n = 2; n <= MaxLinear; n++)
        {
            int count = n;
            var b = Ui.Toggle(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{n} unknowns", minWidth: 44, minHeight: 40);
            b.Padding = new Thickness(0);
            b.Click += (_, _) => SetUnknownCount(count);
            _unknownButtons[n] = b;
            unknownRow.Children.Add(b);
        }
        _unknownGroup = unknownRow;

        var less = Ui.Button("−", "Fewer equations", "Remove the last equation");
        less.Padding = new Thickness(0);
        less.MinWidth = 40;
        less.Click += (_, _) => SetEquationCount(_state.Linear.Equations - 1);
        var more = Ui.Button("+", "More equations", "Add an equation");
        more.Padding = new Thickness(0);
        more.MinWidth = 40;
        more.Click += (_, _) => SetEquationCount(_state.Linear.Equations + 1);
        _equationCount.Token(TextBlock.FontFamilyProperty, "Calcy.Font.Math").Token(TextBlock.ForegroundProperty, "Calcy.Ink");
        var equationRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Caption("Equations"), less, _equationCount, more } };

        _gridMode = Ui.Toggle("Grid", "Enter coefficients in a grid", minHeight: 40);
        _typedMode = Ui.Toggle("Typed", "Type the equations", minHeight: 40);
        _gridMode.Click += (_, _) => SetLinearTyped(false);
        _typedMode.Click += (_, _) => SetLinearTyped(true);
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _gridMode, _typedMode } };

        var top = Ui.Row(28);
        top.Children.Add(unknownRow);
        top.Children.Add(equationRow);
        top.Children.Add(modeRow);
        page.Children.Add(top);
        page.Children.Add(_linearCaption);

        var names = LinearSystemRequest.GridNames(MaxLinear);
        for (int i = 0; i < MaxLinear; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            for (int j = 0; j <= MaxLinear; j++)
            {
                int r = i, c = j;
                var box = new TextBox { Classes = { "coef", "grid" } };
                AutomationProperties.SetName(box, j < MaxLinear ? $"Equation {i + 1} coefficient of {names[j]}" : $"Equation {i + 1} right side");
                Ui.OnText(box, text =>
                {
                    if (_loading) return;
                    _state.Linear.Grid[r][c] = text;
                    box.Classes.Set("error", false);
                    SaveState();
                });
                Ui.OnEnter(box, () => _ = SolveAsync(SolverTool.LinearSystem));
                _gridBoxes[i, j] = box;
                if (j < MaxLinear)
                {
                    var term = Ui.Text("", "term");
                    _gridTerms[i, j] = term;
                    var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { box, term } };
                    _gridCells[i, j] = cell;
                    row.Children.Add(cell);
                }
                else row.Children.Add(box);
            }
            _gridRows[i] = row;
            _gridPanel.Children.Add(row);

            int line = i;
            var editor = new MathEditor { FontSize = 20, MinHeight = 50, Session = _s.Session, PlaceholderText = i == 0 ? "2x + y - z = 8" : "" };
            AutomationProperties.SetName(editor, $"Linear equation {i + 1}");
            editor.TextChanged += (_, _) =>
            {
                if (_loading) return;
                _state.Linear.Lines[line] = editor.Text ?? "";
                SaveState();
            };
            editor.Submitted += (_, _) => _ = SolveAsync(SolverTool.LinearSystem);
            _linearEditors[i] = editor;
            var label = Ui.Caption((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            label.Width = 22;
            var editorRow = new DockPanel();
            DockPanel.SetDock(label, Dock.Left);
            editorRow.Children.Add(label);
            editorRow.Children.Add(editor);
            _linearEditorRows[i] = editorRow;
            _typedPanel.Children.Add(editorRow);
        }
        page.Children.Add(_gridPanel);
        page.Children.Add(_typedPanel);

        var solve = Ui.Button("Solve", "Solve the linear system", "Solve the system (Enter)", "primary");
        solve.Click += (_, _) => _ = SolveAsync(SolverTool.LinearSystem);
        _linearHint.VerticalAlignment = VerticalAlignment.Center;
        var actionRow = new DockPanel();
        DockPanel.SetDock(solve, Dock.Right);
        solve.Margin = new Thickness(12, 0, 0, 0);
        actionRow.Children.Add(solve);
        actionRow.Children.Add(_linearHint);
        page.Children.Add(actionRow);

        ResultArea(SolverTool.LinearSystem, out var host);
        page.Children.Add(host);
        LoadLinear();
        return page;
    }

    /// <summary>Sets the number of unknowns of the grid (2 to 6).</summary>
    public void SetUnknownCount(int count)
    {
        _state.Linear.Unknowns = Math.Clamp(count, 2, MaxLinear);
        if (_state.Linear.Equations < 2) _state.Linear.Equations = Math.Min(_state.Linear.Unknowns, MaxLinear);
        LoadLinear();
        SaveState();
    }

    /// <summary>Sets the number of equations (1 to 6).</summary>
    public void SetEquationCount(int count)
    {
        _state.Linear.Equations = Math.Clamp(count, 1, MaxLinear);
        LoadLinear();
        SaveState();
    }

    /// <summary>Switches between the coefficient grid and typed equations, carrying the system across.</summary>
    public void SetLinearTyped(bool typed)
    {
        var r = _state.Linear;
        if (r.Typed == typed)
        {
            LoadLinear();
            return;
        }
        if (typed)
        {
            var lines = SolverEngine.GridToLines(r);
            for (int i = 0; i < MaxLinear; i++) r.Lines[i] = lines[i];
            r.Typed = true;
        }
        else
        {
            var grid = _engine.LinesToGrid(r);
            if (grid is null)
                _s.ShowStatus("These equations do not fit a coefficient grid (they must be linear with number coefficients); the grid keeps its numbers");
            else
            {
                r.Grid = grid.Grid;
                r.Unknowns = grid.Unknowns;
                r.Equations = grid.Equations;
            }
            r.Typed = false;
        }
        LoadLinear();
        SaveState();
    }

    private void LoadLinear()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            var r = _state.Linear;
            int n = r.Unknowns, m = r.Equations;
            var names = LinearSystemRequest.GridNames(MaxLinear);
            for (int i = 0; i < MaxLinear; i++)
            {
                _gridRows[i].IsVisible = i < m;
                for (int j = 0; j < MaxLinear; j++)
                {
                    _gridCells[i, j].IsVisible = j < n;
                    _gridTerms[i, j].Text = j < n - 1 ? names[j] + " +" : names[j] + " =";
                    Ui.SetText(_gridBoxes[i, j], r.Cell(i, j));
                }
                Ui.SetText(_gridBoxes[i, MaxLinear], r.Rhs(i));
                _linearEditorRows[i].IsVisible = i < m;
                if (_linearEditors[i].Text != r.Lines[i]) _linearEditors[i].Text = r.Lines[i];
            }
            foreach (var (count, b) in _unknownButtons) b.IsChecked = count == n;
            _equationCount.Text = m.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _gridMode.IsChecked = !r.Typed;
            _typedMode.IsChecked = r.Typed;
            _gridPanel.IsVisible = !r.Typed;
            _typedPanel.IsVisible = r.Typed;
            _unknownGroup.IsVisible = !r.Typed;
            _linearHint.Text = r.Typed
                ? "One equation per line, like 2x + y - z = 8. The unknowns are the letters you use. Enter solves."
                : "Empty boxes count as 0. Coefficients can be fractions or roots. Tab moves to the next box, Enter solves.";
            _linearCaption.Text = r.Typed
                ? $"{m} EQUATION{(m == 1 ? "" : "S")}, UNKNOWNS FOUND AUTOMATICALLY"
                : $"{m} EQUATION{(m == 1 ? "" : "S")}, {n} UNKNOWNS";
        }
        finally
        {
            _loading = was;
        }
    }

    private void ShowLinear(LinearSystemOutcome o)
    {
        HideWorking(SolverTool.LinearSystem);
        var results = _results[SolverTool.LinearSystem];
        results.Children.Clear();
        foreach (var box in _gridBoxes) box.Classes.Set("error", false);
        if (o.Error is { } error)
        {
            MarkGridError(error.Field);
            results.Children.Add(ProblemBox(SolverTool.LinearSystem, error));
            return;
        }
        results.Children.Add(Headline(o.Summary));
        var body = o.Kind == LinearKind.None ? Ui.Text("No solution: the equations contradict each other.", "big")
            : o.Values.Count == 0 ? Ui.Text("Every choice of the unknowns is a solution.", "big")
            : Ui.Text(string.Join(",  ", o.Values.Select(v => $"{v.Name} = {v.Value.Text}")), "big");
        var answer = new Border { Child = body };
        answer.Classes.Add("answer");
        AutomationProperties.SetName(answer, "Answer: " + (o.Card.Lines.FirstOrDefault() ?? ""));
        results.Children.Add(answer);
        if (o.Card.Approx is { } approx) results.Children.Add(Ui.Text(approx, "medium"));
        if (o.Notes.Count > 0) results.Children.Add(Notes(o.Notes));
    }

    private void MarkGridError(string? field)
    {
        if (field is null || _state.Linear.Typed) return;
        var names = LinearSystemRequest.GridNames(MaxLinear);
        for (int i = 0; i < MaxLinear; i++)
        {
            for (int j = 0; j < MaxLinear; j++)
                if (field == $"coefficient of {names[j]} in equation {i + 1}") _gridBoxes[i, j].Classes.Set("error", true);
            if (field == $"right side of equation {i + 1}") _gridBoxes[i, MaxLinear].Classes.Set("error", true);
        }
    }
}
