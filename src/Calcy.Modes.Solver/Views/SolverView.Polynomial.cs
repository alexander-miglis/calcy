using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Solver.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Solver.Views;

public sealed partial class SolverView
{
    private readonly TextBox[] _coefBoxes = new TextBox[PolynomialRequest.MaxDegree + 1];
    private readonly Control[] _coefCells = new Control[PolynomialRequest.MaxDegree + 1];
    private readonly Dictionary<int, PixelToggle> _degreeButtons = [];
    private readonly PolynomialGraph _polyGraph = new() { Height = 250 };

    /// <summary>The coefficient box of x^power (for tests).</summary>
    public TextBox CoefficientBox(int power) => _coefBoxes[power];

    /// <summary>The polynomial graph (for tests).</summary>
    public PolynomialGraph PolynomialGraph => _polyGraph;

    private Control BuildPolynomialPage()
    {
        var page = Page();

        var degreeRow = Ui.Row(10);
        degreeRow.Children.Add(Ui.Caption("Degree"));
        for (int d = PolynomialRequest.MinDegree; d <= PolynomialRequest.MaxDegree; d++)
        {
            var deg = d;
            var b = Ui.Toggle(d.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Degree {d}", minWidth: 44, minHeight: 40, tip: $"A polynomial of degree {d}");
            b.Padding = new Thickness(0);
            b.Click += (_, _) => SetDegree(deg);
            _degreeButtons[d] = b;
            degreeRow.Children.Add(b);
        }
        var hint = Ui.Text("Fractions and roots work: 1/2, sqrt(2). Enter solves.", "hint");
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(6, 0, 0, 0);
        degreeRow.Children.Add(hint);
        // SOLVE sits at the end of the degree row so six coefficients always fit on one line.
        var solve = Ui.Button("Solve", "Solve the polynomial", "Find every root (Enter)", "primary");
        solve.Click += (_, _) => _ = SolveAsync(SolverTool.Polynomial);
        solve.Margin = new Thickness(12, 0, 0, 0);
        var topRow = new DockPanel();
        DockPanel.SetDock(solve, Dock.Right);
        topRow.Children.Add(solve);
        topRow.Children.Add(degreeRow);
        page.Children.Add(topRow);

        var coefRow = Ui.Row(8);
        for (int k = PolynomialRequest.MaxDegree; k >= 0; k--)
        {
            int power = k;
            var box = new TextBox { Classes = { "coef" } };
            AutomationProperties.SetName(box, "Coefficient of x to the power " + k);
            ToolTip.SetTip(box, SolverEngine.CoefficientName(k));
            Ui.OnText(box, text =>
            {
                if (_loading) return;
                _state.Polynomial[power] = text;
                box.Classes.Set("error", false);
                SaveState();
            });
            Ui.OnEnter(box, () => _ = SolveAsync(SolverTool.Polynomial));
            _coefBoxes[k] = box;
            var term = Ui.Text(k switch { 0 => "", 1 => "x +", _ => "x" + SolverEngine.Superscript(k) + " +" }, "term");
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { box } };
            if (k > 0) cell.Children.Add(term);
            _coefCells[k] = cell;
            coefRow.Children.Add(cell);
        }
        var equalsZero = Ui.Text("= 0", "term");
        equalsZero.Margin = new Thickness(2, 0, 0, 0);
        coefRow.Children.Add(equalsZero);
        page.Children.Add(coefRow);

        var chart = new Border { Child = _polyGraph };
        chart.Classes.Add("chart");
        chart.VerticalAlignment = VerticalAlignment.Top;
        ResultArea(SolverTool.Polynomial, out var resultHost);
        page.Children.Add(new SideBySide(chart, resultHost));

        LoadPolynomial();
        return page;
    }

    /// <summary>Changes the degree; a new leading coefficient starts at 1.</summary>
    public void SetDegree(int degree)
    {
        degree = Math.Clamp(degree, PolynomialRequest.MinDegree, PolynomialRequest.MaxDegree);
        var p = _state.Polynomial;
        p.Degree = degree;
        if (string.IsNullOrWhiteSpace(p[degree]) || p[degree].Trim() == "0") p[degree] = "1";
        LoadPolynomial();
        SaveState();
        _ = SolveAsync(SolverTool.Polynomial, addCard: false);
    }

    private void LoadPolynomial()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            var p = _state.Polynomial;
            for (int k = 0; k <= PolynomialRequest.MaxDegree; k++)
            {
                Ui.SetText(_coefBoxes[k], p[k]);
                _coefCells[k].IsVisible = k <= p.Degree;
            }
            foreach (var (d, b) in _degreeButtons) b.IsChecked = d == p.Degree;
        }
        finally
        {
            _loading = was;
        }
    }

    private void ShowPolynomial(PolynomialOutcome o)
    {
        HideWorking(SolverTool.Polynomial);
        var results = _results[SolverTool.Polynomial];
        results.Children.Clear();
        foreach (var box in _coefBoxes) box.Classes.Set("error", false);
        if (o.Error is { } error)
        {
            _polyGraph.Clear();
            if (error.Field is { } field)
                for (int k = 0; k <= PolynomialRequest.MaxDegree; k++)
                    if (SolverEngine.CoefficientName(k) == field) _coefBoxes[k].Classes.Set("error", true);
            results.Children.Add(ProblemBox(SolverTool.Polynomial, error));
            return;
        }

        List<(double X, double Y)> turning = o.Vertex is { } v ? [(v.X.Re, v.Y.Re)] : [.. o.TurningPoints.Select(t => (t.X.Re, t.Y.Re))];
        _polyGraph.Show(o.Ascending, [.. o.Roots.Where(r => r.IsReal && double.IsFinite(r.Re)).Select(r => r.Re)], turning);

        results.Children.Add(Headline("Roots", o.Summary.ToLowerInvariant()));
        if (o.AllValues || o.Degree <= 0)
        {
            var box = new Border { Child = Ui.Text(o.AllValues ? "Every x is a root (0 = 0)." : "No x is a root.", "words") };
            box.Classes.Add("box");
            results.Children.Add(box);
        }
        if (o.Roots.Count > 0)
        {
            double width = results.Bounds.Width > 100 ? results.Bounds.Width : (PageWidth - 16) / 2.1;
            results.Children.Add(ValueGrid([.. o.Roots.Select((r, i) => (MathText.Subscript("x", i + 1), r))], width, o.Roots.Count > 3 ? 2 : 1, 20));
        }
        if (o.Discriminant is { } disc) results.Children.Add(InfoBox("Discriminant", "Δ = " + _engine.Show(disc)));
        if (o.Vertex is { } vx) results.Children.Add(InfoBox("Vertex", $"({vx.X.Text}, {vx.Y.Text})  {(vx.IsMaximum ? "maximum" : "minimum")}"));
        if (o.TurningPoints.Count > 0)
        {
            var lines = new StackPanel { Spacing = 4 };
            lines.Children.Add(Ui.Caption("Turning points"));
            foreach (var t in o.TurningPoints)
                lines.Children.Add(Ui.Text($"{(t.IsMaximum ? "max" : "min")}  ({t.X.Text}, {t.Y.Text})", "medium"));
            var box = new Border { Child = lines };
            box.Classes.Add("box");
            results.Children.Add(box);
        }
        if (o.Notes.Count > 0) results.Children.Add(Notes(o.Notes));
    }
}
