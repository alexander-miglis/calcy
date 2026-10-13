using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Math;

namespace Calcy.Modes.Solver.Views;

public sealed partial class SolverView
{
    private MathEditor _inequalityEditor = null!;
    private readonly WrapPanel _inequalityChips = Ui.Row(6);
    private readonly NumberLine _numberLine = new();

    /// <summary>The inequality editor (for tests).</summary>
    public MathEditor InequalityEditor => _inequalityEditor;

    /// <summary>The number line strip (for tests).</summary>
    public NumberLine NumberLine => _numberLine;

    private Control BuildInequalityPage()
    {
        var page = Page();
        _inequalityEditor = new MathEditor
        {
            FontSize = 22,
            MinHeight = 52,
            Session = _s.Session,
            PlaceholderText = "Type an inequality: x^2 - x - 6 < 0,  (x - 1)/(x + 2) >= 0",
        };
        AutomationProperties.SetName(_inequalityEditor, "Inequality");
        _inequalityEditor.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _state.Inequality.Inequality = _inequalityEditor.Text ?? "";
            RefreshUnknowns();
            SaveState();
        };
        _inequalityEditor.Submitted += (_, _) => _ = SolveAsync(SolverTool.Inequality);

        var solve = Ui.Button("Solve", "Solve the inequality", "Solve (Enter)", "primary");
        solve.MinHeight = 48;
        solve.Click += (_, _) => _ = SolveAsync(SolverTool.Inequality);
        var side = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { Ui.Caption("Solve for"), _inequalityChips, solve },
        };
        page.Children.Add(new InputRow(_inequalityEditor, side));
        var hint = Ui.Text("Use <, >, <= (≤), >= (≥) or != (≠). The answer is shown as intervals and on the number line.", "hint");
        page.Children.Add(hint);

        var strip = new Border { Child = _numberLine };
        strip.Classes.Add("chart");
        AutomationProperties.SetName(strip, "Number line");
        page.Children.Add(strip);

        ResultArea(SolverTool.Inequality, out var host);
        page.Children.Add(host);
        LoadInequality();
        return page;
    }

    private void LoadInequality()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            if (_inequalityEditor.Text != _state.Inequality.Inequality) _inequalityEditor.Text = _state.Inequality.Inequality;
        }
        finally
        {
            _loading = was;
        }
        RefreshUnknowns();
    }

    private void ShowInequality(InequalityOutcome o)
    {
        HideWorking(SolverTool.Inequality);
        var results = _results[SolverTool.Inequality];
        results.Children.Clear();
        if (o.Error is { } error)
        {
            _numberLine.Show(IntervalSet.Empty, _engine.Show);
            results.Children.Add(ProblemBox(SolverTool.Inequality, error));
            return;
        }
        _numberLine.Show(o.Solution, _engine.Show, o.Range);
        results.Children.Add(Headline(o.Summary, o.Numeric ? "numeric sign chart" : null));
        var answer = Ui.Text(o.Solution.InequalityText(o.Unknown, _engine.Show), "big");
        AutomationProperties.SetName(answer, "Answer");
        results.Children.Add(answer);
        if (!o.Solution.IsEmpty && !o.Solution.IsAll)
            results.Children.Add(Ui.Text($"{o.Unknown} ∈ {o.IntervalNotation}", "medium"));
        if (o.Notes.Count > 0) results.Children.Add(Notes(o.Notes));
    }
}
