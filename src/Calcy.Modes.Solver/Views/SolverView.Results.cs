using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Solver.Views;

public sealed partial class SolverView
{
    private readonly Dictionary<SolverTool, StackPanel> _results = [];
    private readonly Dictionary<SolverTool, TextBlock> _working = [];

    /// <summary>The result area of a tool, with its WORKING line above it.</summary>
    private StackPanel ResultArea(SolverTool tool, out Control host)
    {
        var results = new StackPanel { Spacing = 10 };
        var working = Ui.Text("Working…  Esc stops it.", "note");
        working.IsVisible = false;
        _results[tool] = results;
        _working[tool] = working;
        host = new StackPanel { Spacing = 10, Children = { working, results } };
        return results;
    }

    private void ShowWorking(SolverTool tool)
    {
        if (_working.TryGetValue(tool, out var w)) w.IsVisible = true;
    }

    private void HideWorking(SolverTool tool)
    {
        if (_working.TryGetValue(tool, out var w)) w.IsVisible = false;
    }

    /// <summary>Shows an outcome that only has an error.</summary>
    private void ShowFailure(SolverTool tool, SolveOutcome outcome)
    {
        HideWorking(tool);
        if (!_results.TryGetValue(tool, out var results)) return;
        results.Children.Clear();
        if (outcome.Error is { } error) results.Children.Add(ProblemBox(tool, error));
    }

    /// <summary>The red-bordered box that explains an input problem (and offers FIX IT when a correction is known).</summary>
    private Control ProblemBox(SolverTool tool, SolverException error)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(Ui.Caption("Needs a fix", 2, "Calcy.Err"));
        body.Children.Add(Ui.Text(error.Message, "problem"));
        if (error.Hint is { } hint && tool is SolverTool.Equation or SolverTool.Inequality)
        {
            var fix = Ui.Button("Fix it", "Fix the input", "Use " + hint, "sel");
            fix.HorizontalAlignment = HorizontalAlignment.Left;
            fix.Click += (_, _) =>
            {
                if (tool == SolverTool.Equation) _equationEditor.Text = hint;
                else _inequalityEditor.Text = hint;
                _ = SolveAsync(tool);
            };
            body.Children.Add(fix);
        }
        var box = new Border { Child = body };
        box.Classes.Add("box");
        box.Classes.Add("problem");
        Avalonia.Automation.AutomationProperties.SetName(box, "Problem: " + error.Message);
        return box;
    }

    /// <summary>The headline of a result: the summary in the pixel font and an optional muted detail.</summary>
    private static Control Headline(string summary, string? detail = null)
    {
        var row = Ui.Row(12);
        row.Children.Add(Ui.Caption(summary));
        if (!string.IsNullOrEmpty(detail))
        {
            var d = Ui.Text(detail, "hint");
            d.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(d);
        }
        return row;
    }

    /// <summary>Explanations under a result.</summary>
    private static Control Notes(IEnumerable<string> notes)
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var n in notes) panel.Children.Add(Ui.Text(n, "note"));
        return panel;
    }

    /// <summary>
    /// "x₁ = value" in the maths font, bold, as on the design board (≈ for numeric values). Linear text keeps the
    /// textbook order of the printer: (5 − √13)/2, −1/2 − (√3/2)i.
    /// </summary>
    private static TextBlock NameValue(string name, SolutionValue v, double size = 20)
    {
        var t = Ui.Text($"{name} {(v.IsNumeric ? "≈" : "=")} {v.Text}", "name");
        t.FontSize = size;
        t.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        return t;
    }

    /// <summary>
    /// A solution box (design: thick border, card colour): the value on the left, its decimal form (or the check
    /// left − right for numeric roots, or the multiplicity) on the right.
    /// </summary>
    private static Border ValueBox(string name, SolutionValue v, double size = 20)
    {
        var side = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (v.HasApprox && !v.IsNumeric) side.Children.Add(Ui.Text("≈ " + v.ApproxText, "approx"));
        if (v.Check is { } check) side.Children.Add(Ui.Text($"check {check}", "approx"));
        if (v.Multiplicity > 1)
        {
            var tag = new Border { Child = Ui.Caption(v.Multiplicity switch { 2 => "double", 3 => "triple", var m => $"×{m}" }, 1.5) };
            tag.Classes.Add("chip");
            tag.HorizontalAlignment = HorizontalAlignment.Right;
            side.Children.Add(tag);
        }
        var value = NameValue(name, v, size);
        // The value keeps its line; the decimal form moves underneath when the box is too narrow for both.
        Control content = side.Children.Count == 0 ? value
            : new InputRow(value, side) { Gap = 12, MinMainWidth = Math.Min(420, $"{name} = {v.Text}".Length * size * 0.6 + 4) };
        var box = new Border { Child = content };
        box.Classes.Add("box");
        Avalonia.Automation.AutomationProperties.SetName(box, $"{name} = {v.Text}");
        return box;
    }

    /// <summary>About how wide a <see cref="ValueBox"/> needs to be (monospace maths font, about 0.6 em per character).</summary>
    private static double BoxWidth(string name, SolutionValue v, double size)
    {
        double main = $"{name} = {v.Text}".Length * size * 0.6;
        double side = 0;
        if (v.HasApprox && !v.IsNumeric) side = (v.ApproxText!.Length + 2) * 14 * 0.6;
        if (v.Check is { } check) side = Math.Max(side, (check.Length + 6) * 14 * 0.6);
        if (v.Multiplicity > 1) side = Math.Max(side, 64);
        return main + (side > 0 ? side + 12 : 0) + 30;
    }

    /// <summary>Lays out value boxes in as many columns (up to <paramref name="max"/>) as fit the width.</summary>
    private static Control ValueGrid(IReadOnlyList<(string Name, SolutionValue Value)> values, double available, int max, double size)
    {
        int columns = 1;
        for (int c = Math.Min(max, Math.Max(1, values.Count)); c >= 1; c--)
        {
            double each = (available - 10 * (c - 1)) / c;
            if (values.All(v => BoxWidth(v.Name, v.Value, size) <= each))
            {
                columns = c;
                break;
            }
        }
        var grid = new UniformGrid { Columns = columns, ColumnSpacing = 10, RowSpacing = 10 };
        foreach (var (name, value) in values) grid.Children.Add(ValueBox(name, value, size));
        return grid;
    }

    /// <summary>The width of the page content (for choosing columns); a typical width before the first layout.</summary>
    private double PageWidth => _pageScroll.Bounds.Width > 100 ? _pageScroll.Bounds.Width - 32 : 920;

    /// <summary>A box with a caption and text (Δ, VERTEX, MAX ...).</summary>
    private static Border InfoBox(string caption, string text)
    {
        var row = Ui.Row(10);
        row.Children.Add(Ui.Caption(caption));
        var t = Ui.Text(text, "medium");
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        var box = new Border { Child = row, MinHeight = 0 };
        box.Classes.Add("box");
        return box;
    }

    /// <summary>The numbered step-by-step explanation.</summary>
    private static Control StepsBox(IReadOnlyList<string> steps)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Ui.Caption("Steps"));
        for (int i = 0; i < steps.Count; i++)
        {
            var row = new DockPanel();
            var no = Ui.Text($"{i + 1}", "stepno");
            no.Width = 26;
            no.VerticalAlignment = VerticalAlignment.Top;
            no.Margin = new Thickness(0, 2, 0, 0);
            DockPanel.SetDock(no, Dock.Left);
            row.Children.Add(no);
            row.Children.Add(Ui.Text(MathText.Typographic(steps[i]), "step"));
            panel.Children.Add(row);
        }
        var box = new Border { Child = panel };
        box.Classes.Add("steps");
        Avalonia.Automation.AutomationProperties.SetName(box, "Steps");
        return box;
    }

    /// <summary>The SOLVE FOR chips: one toggle per candidate, the chosen one in the selection colour.</summary>
    private static void FillChips(WrapPanel chips, IReadOnlyList<string> candidates, string chosen, Action<string> pick)
    {
        chips.Children.Clear();
        var names = candidates.Count == 0 ? [chosen] : candidates;
        foreach (var name in names.Take(6))
        {
            var chip = new PixelToggle
            {
                Content = name,
                IsChecked = name == chosen,
                MinWidth = 44,
                MinHeight = 44,
                FontSize = 18,
                BorderThickness = new Thickness(3),
                Padding = new Thickness(10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            chip.Token(PixelToggle.FontFamilyProperty, "Calcy.Font.Math");
            Avalonia.Automation.AutomationProperties.SetName(chip, "Solve for " + name);
            ToolTip.SetTip(chip, "Solve for " + name);
            var n = name;
            chip.Click += (_, _) => pick(n);
            chips.Children.Add(chip);
        }
    }
}
