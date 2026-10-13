using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Math;

namespace Calcy.Modes.Solver.Views;

public sealed partial class SolverView
{
    private readonly MathEditor[] _nonlinearEditors = new MathEditor[NonlinearSystemRequest.Max];
    private readonly Control[] _nonlinearRows = new Control[NonlinearSystemRequest.Max];
    private readonly TextBlock _nonlinearCount = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Center, MinWidth = 22, TextAlignment = Avalonia.Media.TextAlignment.Center };
    private readonly WrapPanel _guessRow = Ui.Row(12);
    private readonly Dictionary<string, TextBox> _guessBoxes = [];
    private IReadOnlyList<string> _guessNames = [];

    /// <summary>The equation editors of the nonlinear system tool, for tests.</summary>
    public IReadOnlyList<MathEditor> NonlinearEditors => _nonlinearEditors;

    /// <summary>The starting guess box of an unknown (null when that unknown is not in the equations), for tests.</summary>
    public TextBox? GuessBox(string unknown) => _guessBoxes.GetValueOrDefault(unknown);

    private Control BuildNonlinearPage()
    {
        var page = Page();
        var less = Ui.Button("−", "Fewer nonlinear equations", "Remove the last equation");
        less.Padding = new Thickness(0);
        less.MinWidth = 40;
        less.Click += (_, _) => SetNonlinearCount(_state.Nonlinear.Count - 1);
        var more = Ui.Button("+", "More nonlinear equations", "Add an equation");
        more.Padding = new Thickness(0);
        more.MinWidth = 40;
        more.Click += (_, _) => SetNonlinearCount(_state.Nonlinear.Count + 1);
        _nonlinearCount.Token(TextBlock.FontFamilyProperty, "Calcy.Font.Math").Token(TextBlock.ForegroundProperty, "Calcy.Ink");
        var top = Ui.Row(8);
        top.Children.Add(Ui.Caption("Equations"));
        top.Children.Add(less);
        top.Children.Add(_nonlinearCount);
        top.Children.Add(more);
        var hint = Ui.Text("Type each equation; the unknowns are found automatically. Every real solution is listed.", "hint");
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(14, 0, 0, 0);
        top.Children.Add(hint);
        page.Children.Add(top);

        var editors = new StackPanel { Spacing = 10 };
        for (int i = 0; i < NonlinearSystemRequest.Max; i++)
        {
            int line = i;
            var editor = new MathEditor { FontSize = 20, MinHeight = 50, Session = _s.Session, PlaceholderText = i == 0 ? "x^2 + y^2 = 25" : "" };
            AutomationProperties.SetName(editor, $"Nonlinear equation {i + 1}");
            editor.TextChanged += (_, _) =>
            {
                if (_loading) return;
                _state.Nonlinear.Lines[line] = editor.Text ?? "";
                RefreshGuesses();
                SaveState();
            };
            editor.Submitted += (_, _) => _ = SolveAsync(SolverTool.NonlinearSystem);
            _nonlinearEditors[i] = editor;
            var label = Ui.Caption((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            label.Width = 22;
            var row = new DockPanel();
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(editor);
            _nonlinearRows[i] = row;
            editors.Children.Add(row);
        }
        page.Children.Add(editors);

        var solve = Ui.Button("Solve", "Solve the nonlinear system", "Find every solution (Enter)", "primary");
        solve.Click += (_, _) => _ = SolveAsync(SolverTool.NonlinearSystem);
        solve.Margin = new Thickness(12, 0, 0, 0);
        solve.VerticalAlignment = VerticalAlignment.Top;
        var guessLine = new DockPanel();
        DockPanel.SetDock(solve, Dock.Right);
        guessLine.Children.Add(solve);
        guessLine.Children.Add(_guessRow);
        page.Children.Add(guessLine);

        ResultArea(SolverTool.NonlinearSystem, out var host);
        page.Children.Add(host);
        LoadNonlinear();
        return page;
    }

    /// <summary>Sets the number of equations shown (1 to 6).</summary>
    public void SetNonlinearCount(int count)
    {
        _state.Nonlinear.Count = Math.Clamp(count, 1, NonlinearSystemRequest.Max);
        LoadNonlinear();
        SaveState();
    }

    private void LoadNonlinear()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            var r = _state.Nonlinear;
            for (int i = 0; i < NonlinearSystemRequest.Max; i++)
            {
                _nonlinearRows[i].IsVisible = i < r.Count;
                if (_nonlinearEditors[i].Text != r.Lines[i]) _nonlinearEditors[i].Text = r.Lines[i];
            }
            _nonlinearCount.Text = r.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _loading = was;
        }
        RefreshGuesses();
    }

    /// <summary>One starting guess box per unknown found in the equations.</summary>
    private void RefreshGuesses()
    {
        var names = _engine.UnknownsOf(_state.Nonlinear.Lines.Take(_state.Nonlinear.Count));
        if (names.SequenceEqual(_guessNames) && _guessRow.Children.Count > 0) return;
        _guessNames = names;
        _guessRow.Children.Clear();
        _guessBoxes.Clear();
        _guessRow.Children.Add(Ui.Caption("Starting guesses"));
        if (names.Count == 0)
        {
            _guessRow.Children.Add(Ui.Text("(type the equations first)", "hint"));
            return;
        }
        foreach (var name in names.Take(NonlinearSystemRequest.Max))
        {
            var n = name;
            var box = new TextBox { Classes = { "guess" }, PlaceholderText = "auto" };
            box.Text = _state.Nonlinear.Guesses.GetValueOrDefault(n, "");
            AutomationProperties.SetName(box, "Starting guess for " + n);
            ToolTip.SetTip(box, $"Optional: a value of {n} near a solution");
            Ui.OnText(box, text =>
            {
                if (string.IsNullOrWhiteSpace(text)) _state.Nonlinear.Guesses.Remove(n);
                else _state.Nonlinear.Guesses[n] = text;
                SaveState();
            });
            Ui.OnEnter(box, () => _ = SolveAsync(SolverTool.NonlinearSystem));
            _guessBoxes[n] = box;
            var label = Ui.Text(n + " ≈", "term");
            label.FontSize = 18;
            _guessRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, box } });
        }
        var optional = Ui.Text("optional", "hint");
        optional.VerticalAlignment = VerticalAlignment.Center;
        _guessRow.Children.Add(optional);
    }

    private void ShowNonlinear(NonlinearSystemOutcome o)
    {
        HideWorking(SolverTool.NonlinearSystem);
        var results = _results[SolverTool.NonlinearSystem];
        results.Children.Clear();
        if (o.Error is { } error)
        {
            results.Children.Add(ProblemBox(SolverTool.NonlinearSystem, error));
            return;
        }
        results.Children.Add(Headline(o.Summary, o.Unknowns.Count > 0 ? "for " + string.Join(", ", o.Unknowns) : null));
        var grid = new UniformGrid { Columns = o.Sets.Count >= 2 && o.Unknowns.Count <= 2 ? 2 : 1, ColumnSpacing = 10, RowSpacing = 10 };
        foreach (var set in o.Sets) grid.Children.Add(SetBox(set));
        if (o.Sets.Count > 0) results.Children.Add(grid);
        if (o.Notes.Count > 0) results.Children.Add(Notes(o.Notes));
    }

    private Border SetBox(SolutionSet set)
    {
        var values = Ui.Text(set.Line, "name");
        values.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var dock = new DockPanel();
        var side = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (set.Check is { } check) side.Children.Add(Ui.Text("check " + check, "approx"));
        if (set.FromGuess)
        {
            var tag = new Border { Child = Ui.Caption("from guess", 1.5) };
            tag.Classes.Add("chip");
            side.Children.Add(tag);
        }
        if (side.Children.Count > 0)
        {
            DockPanel.SetDock(side, Dock.Right);
            dock.Children.Add(side);
        }
        dock.Children.Add(values);
        var box = new Border { Child = dock };
        box.Classes.Add("box");
        AutomationProperties.SetName(box, "Solution: " + set.Line);
        return box;
    }
}
