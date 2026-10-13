using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Core;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Math;

namespace Calcy.Modes.Solver.Views;

public sealed partial class SolverView
{
    private MathEditor _equationEditor = null!;
    private readonly WrapPanel _equationChips = Ui.Row(6);
    private readonly TextBox _rangeBox = new() { Classes = { "range" }, Width = 150, PlaceholderText = "all real x" };
    private PixelToggle _stepsToggle = null!;
    private PixelToggle _complexToggle = null!;

    /// <summary>The equation editor (for tests).</summary>
    public MathEditor EquationEditor => _equationEditor;

    /// <summary>The range box of the equation tool (for tests).</summary>
    public TextBox RangeBox => _rangeBox;

    private Control BuildEquationPage()
    {
        var page = Page();
        _equationEditor = new MathEditor
        {
            FontSize = 22,
            MinHeight = 52,
            Session = _s.Session,
            PlaceholderText = "Type an equation: cos(x) = x/3,  x^2 = 2,  e^x = 5",
        };
        AutomationProperties.SetName(_equationEditor, "Equation");
        _equationEditor.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _state.Equation.Equation = _equationEditor.Text ?? "";
            RefreshUnknowns();
            SaveState();
        };
        _equationEditor.Submitted += (_, _) => _ = SolveAsync(SolverTool.Equation);

        AutomationProperties.SetName(_rangeBox, "Search range");
        ToolTip.SetTip(_rangeBox, "Leave empty to solve exactly. A range such as [-10, 10] searches it numerically and lists every solution in it.");
        Ui.OnText(_rangeBox, text =>
        {
            if (_loading) return;
            _state.Equation.Range = text;
            _rangeBox.Classes.Set("error", false);
            SaveState();
        });
        Ui.OnEnter(_rangeBox, () => _ = SolveAsync(SolverTool.Equation));

        var solve = Ui.Button("Solve", "Solve the equation", "Solve (Enter)", "primary");
        solve.MinHeight = 48;
        solve.Click += (_, _) => _ = SolveAsync(SolverTool.Equation);
        var side = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { Ui.Caption("Solve for"), _equationChips, Ui.Caption("in"), _rangeBox, solve },
        };
        page.Children.Add(new InputRow(_equationEditor, side));

        _stepsToggle = Ui.Toggle("Show steps", "Show the steps", minHeight: 36, tip: "Explain how the equation is solved");
        _stepsToggle.Click += (_, _) => SetShowSteps(_stepsToggle.IsChecked == true);
        _complexToggle = Ui.Toggle("Complex", "Include complex solutions", minHeight: 36, tip: "Also list solutions with i");
        _complexToggle.Click += (_, _) => SetComplex(_complexToggle.IsChecked == true);
        var options = Ui.Row(10);
        options.Children.Add(_stepsToggle);
        options.Children.Add(_complexToggle);
        var hint = Ui.Text("/ makes a fraction, ^ a power, sqrt a root. Leave the range empty for exact solutions.", "hint");
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(6, 0, 0, 0);
        options.Children.Add(hint);
        page.Children.Add(options);

        ResultArea(SolverTool.Equation, out var host);
        page.Children.Add(host);
        LoadEquation();
        return page;
    }

    /// <summary>Shows or hides the step-by-step explanation (solving again when the steps are not known yet).</summary>
    public void SetShowSteps(bool show)
    {
        _state.Equation.ShowSteps = show;
        _stepsToggle.IsChecked = show;
        SaveState();
        var last = _outcomes.GetValueOrDefault(SolverTool.Equation) as EquationOutcome;
        if (show && last is { Steps.Count: 0, RangeText: null, IsError: false }) _ = SolveAsync(SolverTool.Equation, addCard: false);
        else if (last is not null) ShowEquation(last);
    }

    /// <summary>Includes complex solutions or not, and refreshes the result.</summary>
    public void SetComplex(bool complex)
    {
        _state.Equation.Complex = complex;
        _complexToggle.IsChecked = complex;
        SaveState();
        _ = SolveAsync(SolverTool.Equation, addCard: false);
    }

    private void LoadEquation()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            var r = _state.Equation;
            if (_equationEditor.Text != r.Equation) _equationEditor.Text = r.Equation;
            Ui.SetText(_rangeBox, r.Range);
            _stepsToggle.IsChecked = r.ShowSteps;
            _complexToggle.IsChecked = r.Complex;
        }
        finally
        {
            _loading = was;
        }
        RefreshUnknowns();
    }

    /// <summary>Updates the SOLVE FOR chips of the equation and inequality tools from what is typed.</summary>
    private void RefreshUnknowns()
    {
        RefreshChips(_equationChips, _state.Equation.Equation, _state.Equation.Unknown, n =>
        {
            _state.Equation.Unknown = n;
            SaveState();
            RefreshUnknowns();
        });
        RefreshChips(_inequalityChips, _state.Inequality.Inequality, _state.Inequality.Unknown, n =>
        {
            _state.Inequality.Unknown = n;
            SaveState();
            RefreshUnknowns();
        });
    }

    private void RefreshChips(WrapPanel chips, string text, string? chosen, Action<string> pick)
    {
        IReadOnlyList<string> free;
        try
        {
            free = string.IsNullOrWhiteSpace(text) ? [] : _s.Session.ParseExpression(MathText.Ascii(text)).FreeSymbols();
        }
        catch (CalcException)
        {
            if (chips.Children.Count > 0) return;
            free = [];
        }
        var candidates = UnknownPicker.Candidates(free, _engine.IsStored);
        FillChips(chips, candidates, UnknownPicker.Pick(free, _engine.IsStored, chosen), pick);
    }

    private void ShowEquation(EquationOutcome o)
    {
        HideWorking(SolverTool.Equation);
        var results = _results[SolverTool.Equation];
        results.Children.Clear();
        _rangeBox.Classes.Set("error", false);
        if (o.Error is { } error)
        {
            if (error.Field == "range") _rangeBox.Classes.Set("error", true);
            results.Children.Add(ProblemBox(SolverTool.Equation, error));
            return;
        }

        var x = o.Unknown;
        string? detail = o.Status == EquationStatus.Solutions
            ? (o.RangeText is not null ? "numeric search" : o.Numeric ? "found numerically" : o.Parameters.Count > 0 ? "general solution" : "exact")
            : null;
        results.Children.Add(Headline(o.Summary, detail));

        if (o.Solutions.Count > 0)
            results.Children.Add(ValueGrid([.. o.Solutions.Select(v => (x, v))], PageWidth, 3, 19));
        else if (o.Status is EquationStatus.NoRealSolution or EquationStatus.NoSolution or EquationStatus.NotFound or EquationStatus.AllValues)
        {
            var box = new Border { Child = Ui.Text(o.Notes.FirstOrDefault() ?? o.Summary, "words") };
            box.Classes.Add("box");
            results.Children.Add(box);
        }

        var notes = o.Status is EquationStatus.Solutions ? o.Notes : o.Notes.Skip(1).ToList();
        if (notes.Count > 0) results.Children.Add(Notes(notes));

        if (o.Status == EquationStatus.NoRealSolution && o.ComplexSolutions.Count > 0)
        {
            var show = Ui.Button("Show complex solutions", "Show complex solutions", "List the solutions with i", "mem");
            show.HorizontalAlignment = HorizontalAlignment.Left;
            show.Click += (_, _) =>
            {
                _state.Equation.Complex = true;
                _complexToggle.IsChecked = true;
                SaveState();
                _ = SolveAsync(SolverTool.Equation);
            };
            results.Children.Add(show);
        }
        if (_state.Equation.ShowSteps)
        {
            if (o.Steps.Count > 0) results.Children.Add(StepsBox(o.Steps));
            else if (o.RangeText is not null) results.Children.Add(Ui.Text("Steps are shown for exact solving; clear the range to see them.", "note"));
        }
    }
}
