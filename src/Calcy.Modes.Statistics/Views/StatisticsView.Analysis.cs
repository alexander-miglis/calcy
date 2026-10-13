using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Graphing;
using Calcy.Modes.Statistics.Logic;
using Calcy.Statistics;
using Calcy.UI.Controls;
using Calcy.UI.Graph;
using Calcy.UI.Math;
using Calcy.UI.Services;

namespace Calcy.Modes.Statistics.Views;

public sealed partial class StatisticsView
{
    private static readonly (StatTab Tab, string Label, string Tip)[] TabSpec =
    [
        (StatTab.OneVar, "1-Var", "One-variable statistics of a list, with its box plot"),
        (StatTab.TwoVar, "2-Var", "Two-variable statistics of two lists, with a scatter plot"),
        (StatTab.Regression, "Regression", "Fit a model to two lists: r, r², residuals, predictions"),
        (StatTab.Tests, "Tests", "Hypothesis tests and confidence intervals"),
        (StatTab.Distribution, "Distribution", "Probabilities, densities and inverses of distributions"),
        (StatTab.Plots, "Plots", "Histogram, box plot, scatter, normal probability, dot plot, bar chart"),
    ];

    private ChoiceRow<StatTab> _tabs = null!;
    private readonly ContentControl _tabHost = new();
    private ScrollViewer _tabScroll = null!;
    private readonly Dictionary<StatTab, Control> _tabContent = [];
    private readonly Dictionary<StatTab, TextBlock> _errors = [];
    private readonly List<Action> _pickerRefreshers = [];
    private bool _suppressPickers;

    // 1-Var
    private WrapPanel _oneLists = null!;
    private readonly TileGrid _oneTiles = new(4, 18);
    private readonly StatChart _oneChart = new() { Height = 112 };
    private TextBlock _oneNote = null!;

    // 2-Var
    private readonly TileGrid _twoTiles = new(4, 16);
    private readonly GraphCanvas _twoVarGraph;
    private readonly PlotHost _twoVarHost;
    private TextBlock _twoNote = null!;

    // Regression
    private readonly GraphCanvas _regGraph;
    private readonly PlotHost _regHost;
    private readonly TileGrid _regTiles = new(4, 20);
    private MathView _regEquation = null!;
    private TextBlock _regNote = null!;
    private PixelButton _regSend = null!;
    private PixelButton _regResiduals = null!;
    private TextBox _regPredict = null!;
    private TextBox _regStore = null!;
    private StackPanel _regDegreeRow = null!;
    private StackPanel _regCustomRow = null!;
    private PixelToggle _regResidualToggle = null!;
    private Point2? _predicted;

    // Tests
    private readonly GraphCanvas _testGraph;
    private readonly TileGrid _testTiles = new(4, 18);
    private WrapPanel _testFields = null!;
    private ChoiceRow<bool> _testInput = null!;
    private ChoiceRow<Alternative> _testAlternative = null!;
    private StackPanel _testAltRow = null!;
    private TextBlock _testAltLabel = null!;
    private StackPanel _testAlphaBox = null!;
    private StackPanel _testLevelBox = null!;
    private TextBlock _testHypotheses = null!;
    private TextBlock _testSentence = null!;
    private TextBlock _testGraphNote = null!;
    private Border _testGraphFrame = null!;

    // Distribution
    private readonly GraphCanvas _distGraph;
    private readonly StatChart _distChart = new() { Height = 210 };
    private Border _distGraphFrame = null!;
    private Border _distChartFrame = null!;
    private WrapPanel _distParams = null!;
    private ChoiceRow<DistributionMode> _distMode = null!;
    private StackPanel _distBetween = null!;
    private StackPanel _distDensity = null!;
    private StackPanel _distInverse = null!;
    private ChoiceRow<Tail> _distTail = null!;
    private TextBlock _distHeadline = null!;
    private readonly TileGrid _distTiles = new(4, 16);

    // Plots
    private ChoiceRow<PlotKind> _plotKind = null!;
    private readonly StatChart _plotChart = new() { Height = 300 };
    private readonly GraphCanvas _plotGraph;
    private Border _plotChartFrame = null!;
    private Border _plotGraphFrame = null!;
    private StackPanel _plotSecond = null!;
    private TextBlock _plotSecondLabel = null!;
    private StackPanel _plotFreq = null!;
    private StackPanel _plotBins = null!;
    private PixelToggle _plotOutliers = null!;
    private PixelToggle _plotFit = null!;
    private TextBlock _plotSummary = null!;
    private readonly TileGrid _plotTiles = new(4, 16);

    private PixelPanel BuildAnalysisPanel()
    {
        _tabs = new ChoiceRow<StatTab>(TabSpec.Select(t => (t.Tab, t.Label, t.Tip)), _state.Tab, shadow: true, fontSize: 12, minHeight: 40);
        foreach (var t in TabSpec) _tabs.Toggle(t.Tab).Padding = new Thickness(12, 0);
        _tabs.Picked += ShowTab;
        var tabBar = new Border { Child = _tabs, Padding = new Thickness(10, 8, 10, 10), BorderThickness = new Thickness(0, 0, 0, 4) };
        tabBar.Token(Border.BackgroundProperty, "Calcy.Key");
        tabBar.Token(Border.BorderBrushProperty, "Calcy.Line");
        AutomationProperties.SetName(tabBar, "Analysis type");

        _tabContent[StatTab.OneVar] = BuildOneVarTab();
        _tabContent[StatTab.TwoVar] = BuildTwoVarTab();
        _tabContent[StatTab.Regression] = BuildRegressionTab();
        _tabContent[StatTab.Tests] = BuildTestsTab();
        _tabContent[StatTab.Distribution] = BuildDistributionTab();
        _tabContent[StatTab.Plots] = BuildPlotsTab();

        _tabScroll = new ScrollViewer
        {
            Content = _tabHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var dock = new DockPanel();
        DockPanel.SetDock(tabBar, Dock.Top);
        dock.Children.Add(tabBar);
        dock.Children.Add(_tabScroll);
        var panel = new PixelPanel { Content = dock };
        AutomationProperties.SetName(panel, "Analysis");
        return panel;
    }

    private void ShowTabContent()
    {
        _tabHost.Content = _tabContent[_state.Tab];
        _tabScroll.Offset = default;
    }

    /// <summary>Settings changed: recompute and save.</summary>
    private void OnSettingsChanged()
    {
        if (_restoring) return;
        ScheduleAnalysis();
        ScheduleSave();
    }

    private StackPanel TabPage(StatTab tab, params Control[] children)
    {
        var error = Ui.Body("", 13);
        error.Token(TextBlock.ForegroundProperty, "Calcy.Err");
        error.IsVisible = false;
        _errors[tab] = error;
        var page = new StackPanel { Spacing = 12, Margin = new Thickness(14) };
        foreach (var c in children) page.Children.Add(c);
        page.Children.Insert(Math.Min(1, page.Children.Count), error);
        return page;
    }

    private static WrapPanel Row(params Control[] children)
    {
        var w = new WrapPanel { ItemSpacing = 12, LineSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) w.Children.Add(c);
        return w;
    }

    private static WrapPanel Buttons(params Control[] children)
    {
        var w = new WrapPanel { ItemSpacing = 10, LineSpacing = 10 };
        foreach (var c in children) w.Children.Add(c);
        return w;
    }

    private PixelButton AddCardButton() =>
        Ui.Action("ADD CARD", "Keep this result as a card (COPY, COPY LATEX, USE, SHOW PLOT)", () => _ = AddCardAsync(), "mem");

    private static Border GraphFrame(Control graph) => Ui.Frame(graph, 2);

    /// <summary>A combo box of the lists (with "none" first when optional) bound to a setting.</summary>
    private ComboBox ListPicker(string name, Func<string?> get, Action<string?> set, bool optional = false)
    {
        var combo = Ui.Combo(name, optional ? 90 : 84);
        void Fill()
        {
            _suppressPickers = true;
            try
            {
                var items = (optional ? new[] { "none" } : []).Concat(_table.Columns.Select(c => c.Name)).ToList();
                combo.ItemsSource = items;
                var current = get();
                string? pick = current is null ? (optional ? "none" : items.FirstOrDefault()) : items.Contains(current) ? current : optional ? "none" : items.FirstOrDefault();
                combo.SelectedItem = pick;
                var value = optional && pick == "none" ? null : pick;
                if (value != current && !(value is null && current is null)) set(value);
            }
            finally
            {
                _suppressPickers = false;
            }
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (_suppressPickers || combo.SelectedItem is not string v) return;
            set(optional && v == "none" ? null : v);
            OnSettingsChanged();
        };
        _pickerRefreshers.Add(Fill);
        Fill();
        return combo;
    }

    /// <summary>Updates every list picker after lists were added, removed or renamed.</summary>
    private void RefreshListPickers()
    {
        foreach (var r in _pickerRefreshers) r();
    }

    private void SetError(StatTab tab, string? error)
    {
        var t = _errors[tab];
        t.Token(TextBlock.ForegroundProperty, "Calcy.Err");
        t.Text = error ?? "";
        t.IsVisible = error is not null;
    }

    private void ShowWorking(StatTab tab)
    {
        if (!_errors.TryGetValue(tab, out var t)) return;
        t.Token(TextBlock.ForegroundProperty, "Calcy.Muted");
        t.Text = "Working… press Esc to stop";
        t.IsVisible = true;
    }

    private void ShowAnalysisError(StatTab tab, string message) => SetError(tab, message);

    private TextBox NumberBox(string name, Func<string> get, Action<string> set, double width = 76)
    {
        var box = Ui.Field(get(), name, width);
        box.TextChanged += (_, _) =>
        {
            if (_suppressPickers) return;
            set(box.Text ?? "");
            OnSettingsChanged();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = AddCardAsync(); }
        };
        return box;
    }

    // ------------------------------------------------------------------ 1-Var

    private Control BuildOneVarTab()
    {
        _oneLists = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _pickerRefreshers.Add(RefreshOneVarLists);
        RefreshOneVarLists();
        var freq = ListPicker("Frequency list", () => _state.OneVar.Freq, v => _state.OneVar.Freq = v, optional: true);
        var method = Ui.Combo("Quartile method", 150);
        method.ItemsSource = Descriptives.QuartileMethods.Select(q => q.Name).ToList();
        method.SelectedIndex = Math.Max(0, Descriptives.QuartileMethods.ToList().FindIndex(q => q.Method == _state.OneVar.Method));
        method.SelectionChanged += (_, _) =>
        {
            if (method.SelectedIndex < 0) return;
            _state.OneVar.Method = Descriptives.QuartileMethods[method.SelectedIndex].Method;
            OnSettingsChanged();
        };
        var top = new DockPanel();
        var right = Ui.Labelled("QUARTILES", method, "Calculators and textbooks compute quartiles differently");
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(Row(Ui.Labelled("LIST", _oneLists), Ui.Labelled("FREQ", freq)));
        _oneNote = Ui.Body("", 13, muted: true);
        var chart = GraphFrame(_oneChart);
        AutomationProperties.SetName(_oneChart, "Box plot");
        return TabPage(StatTab.OneVar, top, _oneTiles, chart, _oneNote,
            Buttons(AddCardButton(), Ui.Action("HISTOGRAM", "Open the histogram of this list in Plots", () =>
            {
                _state.Plot.Kind = PlotKind.Histogram;
                _state.Plot.List = _state.OneVar.List;
                _state.Plot.Freq = _state.OneVar.Freq;
                _plotKind.Select(PlotKind.Histogram);
                RefreshListPickers();
                UpdatePlotOptions();
                ShowTab(StatTab.Plots);
            })));
    }

    private void RefreshOneVarLists()
    {
        _oneLists.Children.Clear();
        var names = _table.Columns.Select(c => c.Name).ToList();
        if (!names.Contains(_state.OneVar.List) && names.Count > 0) _state.OneVar.List = names[0];
        foreach (var c in _table.Columns)
        {
            var t = Ui.Toggle(c.Header, $"Statistics of {c.Name}");
            t.FontSize = 12;
            t.MinHeight = 38;
            t.IsChecked = c.Name == _state.OneVar.List;
            var n = c.Name;
            t.Click += (_, _) =>
            {
                _state.OneVar.List = n;
                foreach (var other in _oneLists.Children.OfType<PixelToggle>()) other.IsChecked = ReferenceEquals(other, t);
                OnSettingsChanged();
            };
            _oneLists.Children.Add(t);
        }
    }

    // ------------------------------------------------------------------ 2-Var

    private Control BuildTwoVarTab()
    {
        var x = ListPicker("x list", () => _state.TwoVar.X, v => _state.TwoVar.X = v ?? "L1");
        var y = ListPicker("y list", () => _state.TwoVar.Y, v => _state.TwoVar.Y = v ?? "L2");
        var f = ListPicker("Frequency list", () => _state.TwoVar.Freq, v => _state.TwoVar.Freq = v, optional: true);
        _twoNote = Ui.Body("", 13, muted: true);
        return TabPage(StatTab.TwoVar, Row(Ui.Labelled("X", x), Ui.Labelled("Y", y), Ui.Labelled("FREQ", f)), _twoTiles, _twoVarHost, _twoNote,
            Buttons(AddCardButton(), Ui.Action("REGRESSION", "Fit a model to these lists", () =>
            {
                _state.Regression.X = _state.TwoVar.X;
                _state.Regression.Y = _state.TwoVar.Y;
                _state.Regression.Freq = _state.TwoVar.Freq;
                RefreshListPickers();
                ShowTab(StatTab.Regression);
            })));
    }

    // ------------------------------------------------------------------ Regression

    private Control BuildRegressionTab()
    {
        var model = Ui.Combo("Regression model", 290);
        model.ItemsSource = Regressions.Models.Select(m => $"{m.Name}  {m.Form}").ToList();
        model.SelectedIndex = Regressions.Models.ToList().FindIndex(m => m.Kind == _state.Regression.Model);
        model.SelectionChanged += (_, _) =>
        {
            if (model.SelectedIndex < 0) return;
            _state.Regression.Model = Regressions.Models[model.SelectedIndex].Kind;
            _predicted = null;
            UpdateRegressionOptions();
            OnSettingsChanged();
        };
        var x = ListPicker("x list", () => _state.Regression.X, v => _state.Regression.X = v ?? "L1");
        var y = ListPicker("y list", () => _state.Regression.Y, v => _state.Regression.Y = v ?? "L2");
        var f = ListPicker("Weights", () => _state.Regression.Freq, v => _state.Regression.Freq = v, optional: true);
        _regResidualToggle = Ui.Toggle("RESIDUALS", "Show the residual plot instead of the scatter plot");
        _regResidualToggle.IsChecked = _state.Regression.Residuals;
        _regResidualToggle.Click += (_, _) =>
        {
            _state.Regression.Residuals = _regResidualToggle.IsChecked == true;
            OnSettingsChanged();
        };
        var degree = NumberBox("Polynomial degree", () => _state.Regression.Degree.ToString(CultureInfo.InvariantCulture), v =>
        {
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)) _state.Regression.Degree = Math.Clamp(d, 1, 10);
        }, 56);
        _regDegreeRow = Ui.Labelled("DEGREE", degree, "Degree 1 to 10");
        var custom = NumberBox("Custom model", () => _state.Regression.Custom, v => _state.Regression.Custom = v, 220);
        var start = NumberBox("Start values", () => _state.Regression.CustomStart, v => _state.Regression.CustomStart = v, 150);
        start.PlaceholderText = "a = 1, b = 0.1";
        _regCustomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { Ui.Labelled("y =", custom, "Any formula in x; the other letters are fitted"), Ui.Labelled("START", start, "Start values for the fit; letters left out start at 1") },
        };
        var top = Row(Ui.Labelled("MODEL", model), Ui.Labelled("x:", x), Ui.Labelled("y:", y));
        var second = Row(Ui.Labelled("WEIGHTS", f, "Frequencies or weights (optional)"), _regResidualToggle, _regDegreeRow, _regCustomRow);

        _regEquation = new MathView { FontSize = 20, Session = _session, PreserveStructure = true, OverflowMode = MathOverflowMode.Scale, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(_regEquation, "Fitted equation");
        _regNote = Ui.Body("", 13, muted: true);

        _regSend = Ui.Action("SEND y = ax + b TO GRAPHING", "Plot the points and the fitted curve in Graphing", SendRegressionToGraphing, "primary");
        _regResiduals = Ui.Action("RESIDUALS TO L3", "Store the residuals y - f(x) as a new list", ResidualsToList);
        _regPredict = Ui.Field(_state.Regression.PredictX, "Predict at x", 64);
        _regPredict.PlaceholderText = "x";
        _regPredict.TextChanged += (_, _) => { _state.Regression.PredictX = _regPredict.Text ?? ""; ScheduleSave(); };
        _regPredict.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Predict(); } };
        var predict = Ui.Action("PREDICT AT x =", "Evaluate the fitted curve at x and keep it as a card", Predict);
        var predictRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { predict, _regPredict } };
        _regStore = Ui.Field(_state.Regression.StoreName, "Function name", 46);
        _regStore.TextChanged += (_, _) => { _state.Regression.StoreName = _regStore.Text ?? "f"; ScheduleSave(); };
        var store = Ui.Action("STORE AS", "Store the fitted equation as a function, for example f(x)", StoreEquation);
        var storeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { store, _regStore, Ui.Math("(x)", 16) } };
        UpdateRegressionOptions();
        return TabPage(StatTab.Regression, top, second, _regHost, _regEquation, _regTiles, _regNote,
            Buttons(_regSend, _regResiduals, predictRow, storeRow, AddCardButton()));
    }

    private void UpdateRegressionOptions()
    {
        _regDegreeRow.IsVisible = _state.Regression.Model == RegressionKind.Polynomial;
        _regCustomRow.IsVisible = _state.Regression.Model == RegressionKind.Custom;
        var info = Regressions.Info(_state.Regression.Model);
        var form = info.Kind switch
        {
            RegressionKind.Polynomial => "THE POLYNOMIAL",
            RegressionKind.Custom => "THE CURVE",
            _ => info.Form,
        };
        _regSend.Content = $"SEND {form} TO GRAPHING";
        _regResiduals.Content = $"RESIDUALS TO {NextFreeListName()}";
    }

    private string NextFreeListName()
    {
        int i = FirstFreeColumn();
        return i < _table.Columns.Count ? _table.Columns[i].Name : _table.NextName();
    }

    private RegressionOutcome? CurrentFit => Outcome(StatTab.Regression) as RegressionOutcome;

    private void SendRegressionToGraphing()
    {
        if (CurrentFit is not { Error: null } o)
        {
            _s.ShowStatus("Fit a model first");
            return;
        }
        var s = _state.Regression;
        var points = o.Data is { Skipped: 0 } ? $"({s.X}, {s.Y})" : GraphSpec.Points(o.Data!.X, o.Data.Y);
        _s.Navigate("graphing", new PlotRequest([points, o.Equation]));
        _s.ShowStatus($"Sent the points and {o.DisplayEquation} to Graphing");
    }

    private void ResidualsToList()
    {
        if (CurrentFit is not { Error: null, Data: { } data } o)
        {
            _s.ShowStatus("Fit a model first");
            return;
        }
        int rows = data.Rows.Length == 0 ? 0 : data.Rows.Max() + 1;
        var cells = Enumerable.Repeat("", rows).ToArray();
        for (int i = 0; i < data.Rows.Length && i < o.Residuals.Count; i++) cells[data.Rows[i]] = NumberText.Cell(o.Residuals[i]);
        int index = FirstFreeColumn();
        if (index >= _table.Columns.Count && _table.Columns.Count >= StatTable.MaxColumns)
        {
            _s.ShowStatus("There is no room for another list");
            return;
        }
        var column = index < _table.Columns.Count ? _table.Columns[index] : _table.AddColumn();
        _table.SetCells(column.Name, cells);
        column.Caption = "resid";
        DataChanged(listsChanged: true);
        UpdateRegressionOptions();
        _s.ShowStatus($"The residuals of {o.Model.Name.ToLowerInvariant()} are in {column.Name}");
    }

    /// <summary>Evaluates the fitted curve at the x in the predict box and adds the card.</summary>
    public void Predict()
    {
        if (CurrentFit is not { Error: null, Predict: { } f } o)
        {
            _s.ShowStatus("Fit a model first");
            return;
        }
        double x;
        try
        {
            x = Distributions.ReadNumber(_regPredict.Text, "x", _session);
        }
        catch (StatDataException ex)
        {
            _s.ShowStatus(ex.Message);
            return;
        }
        double y = f(x);
        _predicted = new Point2(x, y);
        string xs = NumberText.Display(x, 10), ys = NumberText.Display(y, NumberText.CardDigits);
        var use = Regex.Replace(o.Expression, @"(?<![A-Za-z0-9_])x(?![A-Za-z0-9_])", "(" + NumberText.Input(x) + ")");
        var card = new StatCard
        {
            Title = $"PREDICT {o.Model.CardName}",
            Headline = $"ŷ({xs}) = {ys}",
            Lines = [new CardLine("x", xs), new CardLine("ŷ", ys), new CardLine("model", o.DisplayEquation)],
            CopyText = $"{o.DisplayEquation}\nx = {xs}\nŷ = {NumberText.Plain(y)}",
            Latex = $"\\hat{{y}}({NumberText.Latex(x)}) = {NumberText.Latex(y)}",
            UseText = use,
            Recipe = new StatRecipe { Tab = StatTab.Regression, Regression = _state.Regression.Clone() },
        };
        AddCard(card);
        RenderRegression(o, _lastRegItems);
    }

    private void StoreEquation()
    {
        if (CurrentFit is not { Error: null } o)
        {
            _s.ShowStatus("Fit a model first");
            return;
        }
        var name = (_regStore.Text ?? "").Trim();
        if (name.Length == 0) name = "f";
        var r = _session.Evaluate($"{name}(x) = {o.Expression}");
        _s.ShowStatus(r.IsError ? $"Could not store {name}(x): {r.ErrorMessage}" : $"Stored {name}(x) = {Regressions.Readable(o.DisplayEquation[4..])}");
    }

    // ------------------------------------------------------------------ Tests

    private Control BuildTestsTab()
    {
        var test = Ui.Combo("Test", 260);
        test.ItemsSource = TestCatalog.All.Select(t => t.Name).ToList();
        test.SelectedIndex = TestCatalog.All.ToList().FindIndex(t => t.Kind == _state.Test.Kind);
        test.SelectionChanged += (_, _) =>
        {
            if (test.SelectedIndex < 0) return;
            _state.Test.Kind = TestCatalog.All[test.SelectedIndex].Kind;
            UpdateTestOptions();
            OnSettingsChanged();
        };
        _testInput = new ChoiceRow<bool>([(false, "DATA", "Use lists"), (true, "STATS", "Type summary statistics (x̄, Sx, n)")], _state.Test.Stats);
        _testInput.Picked += v =>
        {
            _state.Test.Stats = v;
            BuildTestFields();
            OnSettingsChanged();
        };
        _testFields = new WrapPanel { ItemSpacing = 14, LineSpacing = 8 };
        _pickerRefreshers.Add(BuildTestFields);
        _testAlternative = new ChoiceRow<Alternative>(
            [(Alternative.NotEqual, "≠", "Two-sided: differs"), (Alternative.Less, "<", "One-sided: less"), (Alternative.Greater, ">", "One-sided: greater")],
            _state.Test.Alternative, fontSize: 14);
        _testAlternative.Picked += v =>
        {
            _state.Test.Alternative = v;
            OnSettingsChanged();
        };
        _testAltLabel = Ui.Label("H₁:", 12);
        _testAltRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _testAltLabel, _testAlternative } };
        var alpha = NumberBox("Significance level", () => _state.Test.Alpha, v => _state.Test.Alpha = v, 64);
        _testAlphaBox = Ui.Labelled("α =", alpha, "Reject H₀ when p ≤ α");
        var level = NumberBox("Confidence level", () => _state.Test.Level, v => _state.Test.Level = v, 64);
        _testLevelBox = Ui.Labelled("C-LEVEL", level, "0.95 or 95 for a 95% interval");
        _testHypotheses = Ui.Math("", 15, bold: true);
        _testHypotheses.TextWrapping = TextWrapping.Wrap;
        _testSentence = Ui.Body("", 14);
        _testGraphNote = Ui.Body("", 12, muted: true);
        _testGraphFrame = GraphFrame(_testGraph);
        UpdateTestOptions();
        return TabPage(StatTab.Tests, Row(Ui.Labelled("TEST", test), _testInput), _testFields, Row(_testAltRow, _testAlphaBox, _testLevelBox),
            _testGraphFrame, _testHypotheses, _testTiles, _testSentence, _testGraphNote, Buttons(AddCardButton()));
    }

    private void UpdateTestOptions()
    {
        var info = TestCatalog.Info(_state.Test.Kind);
        _testInput.IsVisible = info.HasStats;
        _testAltRow.IsVisible = info.HasAlternative;
        _testAlphaBox.IsVisible = !info.IsInterval;
        _testLevelBox.IsVisible = info.IsInterval;
        BuildTestFields();
    }

    private void BuildTestFields()
    {
        if (_testFields is null) return;
        _testFields.Children.Clear();
        var s = _state.Test;
        var names = _table.Columns.Select(c => c.Name).ToList();
        foreach (var f in TestCatalog.Fields(s.Kind, s.Stats))
        {
            var key = f.Key;
            switch (f.Kind)
            {
                case FieldKind.Number:
                {
                    var box = NumberBox(f.Tip, () => TestCatalog.Value(s, f, names), v => s.Fields[key] = v, f.Key is "mu0" or "p0" or "m0" ? 70 : 76);
                    if (f.Default.Length == 0) box.PlaceholderText = "auto";
                    _testFields.Children.Add(Ui.Labelled(f.Label, box, f.Tip));
                    break;
                }
                case FieldKind.List:
                case FieldKind.Frequency:
                {
                    bool optional = f.Kind == FieldKind.Frequency;
                    var combo = Ui.Combo(f.Tip, optional ? 90 : 84);
                    var items = (optional ? new[] { "none" } : []).Concat(names).ToList();
                    combo.ItemsSource = items;
                    var current = TestCatalog.Value(s, f, names);
                    combo.SelectedItem = current.Length == 0 ? (optional ? "none" : items.FirstOrDefault()) : current;
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (combo.SelectedItem is not string v) return;
                        s.Fields[key] = optional && v == "none" ? "" : v;
                        OnSettingsChanged();
                    };
                    _testFields.Children.Add(Ui.Labelled(f.Label.ToUpperInvariant(), combo, f.Tip));
                    break;
                }
                case FieldKind.Lists:
                {
                    var chosen = TestCatalog.Value(s, f, names).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
                    foreach (var n in names)
                    {
                        var t = Ui.Toggle(n, $"Use {n}");
                        t.IsChecked = chosen.Contains(n);
                        t.Click += (_, _) =>
                        {
                            var picked = chips.Children.OfType<PixelToggle>().Where(c => c.IsChecked == true).Select(c => (string)c.Content!);
                            s.Fields[key] = string.Join(",", picked);
                            OnSettingsChanged();
                        };
                        chips.Children.Add(t);
                    }
                    _testFields.Children.Add(Ui.Labelled(f.Label.ToUpperInvariant(), chips, f.Tip));
                    break;
                }
                case FieldKind.Flag:
                {
                    var t = Ui.Toggle(f.Label.ToUpperInvariant(), f.Tip);
                    t.IsChecked = TestCatalog.Value(s, f, names) == "true";
                    t.Click += (_, _) =>
                    {
                        s.Fields[key] = t.IsChecked == true ? "true" : "false";
                        OnSettingsChanged();
                    };
                    _testFields.Children.Add(t);
                    break;
                }
            }
        }
        UpdateAlternativeLabels();
    }

    private void UpdateAlternativeLabels()
    {
        var info = TestCatalog.Info(_state.Test.Kind);
        string p = _state.Test.Kind switch
        {
            TestKind.ZTest or TestKind.TTest => "μ",
            TestKind.TwoSampleZTest or TestKind.TwoSampleTTest => "μ₁",
            TestKind.PairedTTest => "μd",
            TestKind.OnePropZTest => "p",
            TestKind.TwoPropZTest => "p₁",
            TestKind.TwoSampleFTest => "σ₁",
            TestKind.LinRegTTest => "β",
            TestKind.SignTest or TestKind.WilcoxonSignedRank => "median",
            TestKind.Spearman => "ρs",
            TestKind.Kendall => "τ",
            _ => "",
        };
        string target = _state.Test.Kind switch
        {
            TestKind.ZTest or TestKind.TTest or TestKind.PairedTTest => "μ₀",
            TestKind.TwoSampleZTest or TestKind.TwoSampleTTest => "μ₂",
            TestKind.OnePropZTest => "p₀",
            TestKind.TwoPropZTest => "p₂",
            TestKind.TwoSampleFTest => "σ₂",
            TestKind.SignTest or TestKind.WilcoxonSignedRank => "m₀",
            TestKind.MannWhitney => "",
            _ => "0",
        };
        if (!info.HasAlternative) return;
        if (_state.Test.Kind == TestKind.MannWhitney)
        {
            _testAlternative.Toggle(Alternative.NotEqual).Content = "differ";
            _testAlternative.Toggle(Alternative.Less).Content = "1 smaller";
            _testAlternative.Toggle(Alternative.Greater).Content = "1 larger";
            return;
        }
        _testAlternative.Toggle(Alternative.NotEqual).Content = $"{p} ≠ {target}";
        _testAlternative.Toggle(Alternative.Less).Content = $"{p} < {target}";
        _testAlternative.Toggle(Alternative.Greater).Content = $"{p} > {target}";
    }

    // ------------------------------------------------------------------ Distribution

    private Control BuildDistributionTab()
    {
        var dist = Ui.Combo("Distribution", 190);
        dist.ItemsSource = Distributions.All.Select(d => d.Name).ToList();
        dist.SelectedIndex = Distributions.All.ToList().FindIndex(d => d.Kind == _state.Distribution.Kind);
        _distParams = new WrapPanel { ItemSpacing = 12, LineSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _distMode = new ChoiceRow<DistributionMode>(
            [(DistributionMode.Between, "P(a < X < b)", "The probability between two bounds"),
             (DistributionMode.Density, "DENSITY", "The density f(x), or P(X = x) for counts"),
             (DistributionMode.Inverse, "INVERSE", "The x with a given area")],
            _state.Distribution.Mode);
        _distMode.Picked += m =>
        {
            _state.Distribution.Mode = m;
            UpdateDistributionOptions();
            OnSettingsChanged();
        };
        var lo = NumberBox("Lower bound", () => _state.Distribution.Lower, v => _state.Distribution.Lower = v, 74);
        var hi = NumberBox("Upper bound", () => _state.Distribution.Upper, v => _state.Distribution.Upper = v, 74);
        lo.PlaceholderText = "-∞";
        hi.PlaceholderText = "∞";
        _distBetween = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Ui.Labelled("FROM", lo, "Empty or -inf: no lower bound"), Ui.Labelled("TO", hi, "Empty or inf: no upper bound") } };
        var x = NumberBox("x", () => _state.Distribution.X, v => _state.Distribution.X = v, 74);
        _distDensity = new StackPanel { Orientation = Orientation.Horizontal, Children = { Ui.Labelled("x =", x) } };
        dist.SelectionChanged += (_, _) =>
        {
            if (dist.SelectedIndex < 0) return;
            var kind = Distributions.All[dist.SelectedIndex].Kind;
            if (kind == _state.Distribution.Kind) return;
            _state.Distribution.Kind = kind;
            _state.Distribution.Parameters.Clear();
            // Each distribution starts with bounds that make sense for it (counts for the binomial, x > 0 for χ²).
            DefaultBounds(kind);
            lo.Text = _state.Distribution.Lower;
            hi.Text = _state.Distribution.Upper;
            x.Text = _state.Distribution.X;
            BuildDistributionParameters();
            UpdateDistributionOptions();
            OnSettingsChanged();
        };
        var area = NumberBox("Area", () => _state.Distribution.Area, v => _state.Distribution.Area = v, 74);
        _distTail = new ChoiceRow<Tail>([(Tail.Left, "LEFT", "Area to the left of x"), (Tail.Right, "RIGHT", "Area to the right of x"), (Tail.Center, "CENTER", "Area in the middle")], _state.Distribution.Tail);
        _distTail.Picked += t =>
        {
            _state.Distribution.Tail = t;
            OnSettingsChanged();
        };
        _distInverse = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Ui.Labelled("AREA", area), _distTail } };
        _distHeadline = Ui.Math("", 22, bold: true);
        _distHeadline.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(_distHeadline, "Result");
        _distGraphFrame = GraphFrame(_distGraph);
        _distChartFrame = GraphFrame(_distChart);
        BuildDistributionParameters();
        UpdateDistributionOptions();
        return TabPage(StatTab.Distribution, Row(dist, _distParams), Row(_distMode, _distBetween, _distDensity, _distInverse),
            _distGraphFrame, _distChartFrame, _distHeadline, _distTiles, Buttons(AddCardButton()));
    }

    private void DefaultBounds(DistributionKind kind)
    {
        var d = _state.Distribution;
        (d.Lower, d.Upper, d.X) = kind switch
        {
            DistributionKind.Normal or DistributionKind.StudentT or DistributionKind.Cauchy => ("-1.96", "1.96", "0"),
            DistributionKind.ChiSquared => ("0", "7.815", "2"),
            DistributionKind.F => ("0", "3.326", "1"),
            DistributionKind.Uniform or DistributionKind.Beta => ("0.2", "0.6", "0.5"),
            DistributionKind.Binomial => ("3", "6", "5"),
            DistributionKind.Poisson => ("2", "4", "3"),
            DistributionKind.Geometric => ("1", "5", "3"),
            DistributionKind.Hypergeometric => ("1", "2", "1"),
            DistributionKind.NegativeBinomial => ("0", "3", "2"),
            _ => ("0.5", "2", "1"),
        };
    }

    private void BuildDistributionParameters()
    {
        _distParams.Children.Clear();
        var info = Distributions.Info(_state.Distribution.Kind);
        foreach (var p in info.Parameters)
        {
            var key = p.Key;
            var box = NumberBox(p.Tip, () => _state.Distribution.Parameter(key, p.Default), v => _state.Distribution.Parameters[key] = v, 64);
            _distParams.Children.Add(Ui.Labelled(p.Label + " =", box, p.Tip));
        }
    }

    private void UpdateDistributionOptions()
    {
        var info = Distributions.Info(_state.Distribution.Kind);
        var mode = _state.Distribution.Mode;
        _distMode.Toggle(DistributionMode.Between).Content = info.IsDiscrete ? "P(a ≤ X ≤ b)" : "P(a < X < b)";
        _distMode.Toggle(DistributionMode.Density).Content = info.IsDiscrete ? "P(X = x)" : "DENSITY f(x)";
        _distBetween.IsVisible = mode == DistributionMode.Between;
        _distDensity.IsVisible = mode == DistributionMode.Density;
        _distInverse.IsVisible = mode == DistributionMode.Inverse;
        _distTail.IsVisible = !info.IsDiscrete;
        _distGraphFrame.IsVisible = !info.IsDiscrete;
        _distChartFrame.IsVisible = info.IsDiscrete;
    }

    // ------------------------------------------------------------------ Plots

    private Control BuildPlotsTab()
    {
        _plotKind = new ChoiceRow<PlotKind>(Plots.Kinds.Select(k => (k.Kind, k.Name.ToUpperInvariant(), k.Name)), _state.Plot.Kind, fontSize: 11);
        _plotKind.Picked += k =>
        {
            _state.Plot.Kind = k;
            if (k == PlotKind.Scatter && _state.Plot.List2 is null)
                _state.Plot.List2 = _table.Columns.Select(c => c.Name).FirstOrDefault(n => n != _state.Plot.List);
            RefreshListPickers();
            UpdatePlotOptions();
            OnSettingsChanged();
        };
        var list = ListPicker("Plot list", () => _state.Plot.List, v => _state.Plot.List = v ?? "L1");
        var second = ListPicker("Second list", () => _state.Plot.List2, v => _state.Plot.List2 = v, optional: true);
        _plotSecondLabel = Ui.Label("Y", 11);
        _plotSecond = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _plotSecondLabel, second } };
        var freq = ListPicker("Frequency list", () => _state.Plot.Freq, v => _state.Plot.Freq = v, optional: true);
        _plotFreq = Ui.Labelled("FREQ", freq);
        var width = NumberBox("Bin width", () => _state.Plot.BinWidth, v => _state.Plot.BinWidth = v, 64);
        width.PlaceholderText = "auto";
        var start = NumberBox("Bin start", () => _state.Plot.BinStart, v => _state.Plot.BinStart = v, 64);
        start.PlaceholderText = "auto";
        _plotBins = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Ui.Labelled("BIN WIDTH", width, "Empty for an automatic width"), Ui.Labelled("START", start, "Left edge of the first bin; empty for automatic") } };
        _plotOutliers = Ui.Toggle("OUTLIERS", "Modified box plot: values beyond 1.5 IQR are drawn as points");
        _plotOutliers.IsChecked = _state.Plot.Outliers;
        _plotOutliers.Click += (_, _) => { _state.Plot.Outliers = _plotOutliers.IsChecked == true; OnSettingsChanged(); };
        _plotFit = Ui.Toggle("LINE", "Draw the least squares line");
        _plotFit.IsChecked = _state.Plot.FitLine;
        _plotFit.Click += (_, _) => { _state.Plot.FitLine = _plotFit.IsChecked == true; OnSettingsChanged(); };
        _plotSummary = Ui.Body("", 13);
        _plotChartFrame = GraphFrame(_plotChart);
        _plotGraphFrame = GraphFrame(_plotGraph);
        AutomationProperties.SetName(_plotChart, "Plot");
        UpdatePlotOptions();
        return TabPage(StatTab.Plots, _plotKind, Row(Ui.Labelled("LIST", list), _plotSecond, _plotFreq, _plotBins, _plotOutliers, _plotFit),
            _plotChartFrame, _plotGraphFrame, _plotSummary, _plotTiles, Buttons(AddCardButton()));
    }

    private void UpdatePlotOptions()
    {
        var k = _state.Plot.Kind;
        _plotSecond.IsVisible = k is PlotKind.Scatter or PlotKind.BoxPlot;
        _plotSecondLabel.Text = k == PlotKind.Scatter ? "Y" : "AND";
        _plotFreq.IsVisible = k != PlotKind.Scatter;
        _plotBins.IsVisible = k == PlotKind.Histogram;
        _plotOutliers.IsVisible = k == PlotKind.BoxPlot;
        _plotFit.IsVisible = k == PlotKind.Scatter;
        _plotGraphFrame.IsVisible = k == PlotKind.Scatter;
        _plotChartFrame.IsVisible = k != PlotKind.Scatter;
        _plotChart.Height = k == PlotKind.BoxPlot && _state.Plot.List2 is not null && _state.Plot.List2 != _state.Plot.List ? 220 : k == PlotKind.BoxPlot ? 160 : 300;
    }

    // ------------------------------------------------------------------ rendering results

    private IReadOnlyList<GraphItem>? _lastRegItems;

    private void Render(Job job)
    {
        switch (job.Outcome)
        {
            case OneVarOutcome o:
                SetError(StatTab.OneVar, o.Error);
                _oneTiles.SetTiles(o.Tiles);
                _oneChart.Scene = o.Scene;
                _oneNote.Text = o.Note ?? "";
                _oneNote.IsVisible = o.Note is not null;
                break;
            case TwoVarOutcome o:
                SetError(StatTab.TwoVar, o.Error);
                _twoTiles.SetTiles(o.Tiles);
                _twoVarHost.Show(o.Graph?.Viewport, o.Graph?.Annotations, job.Items, o.Scene);
                _twoNote.Text = o.Note ?? "";
                _twoNote.IsVisible = o.Note is not null;
                break;
            case RegressionOutcome o:
                _lastRegItems = job.Items;
                RenderRegression(o, job.Items);
                break;
            case TestOutcome o:
                RenderTest(o, job.Items);
                break;
            case DistributionOutcome o:
                SetError(StatTab.Distribution, o.Error);
                _distHeadline.Text = o.Error is null ? o.Headline : "";
                _distTiles.SetTiles(o.Tiles);
                ShowGraph(_distGraph, o.Graph, job.Items);
                _distChart.Scene = o.Scene ?? (o.Error is null ? null : ChartScene.Empty(o.Error));
                break;
            case PlotOutcome o:
                SetError(StatTab.Plots, o.Error);
                // Dot plots are as tall as their tallest stack needs; the other charts keep their size.
                if (_state.Plot.Kind == PlotKind.DotPlot && o.Scene is { Message: null } dots)
                    _plotChart.Height = Math.Clamp(dots.YMax * 20 + 70, 150, 300);
                else UpdatePlotOptions();
                _plotChart.Scene = o.Scene;
                ShowGraph(_plotGraph, o.Graph, job.Items);
                _plotGraphFrame.IsVisible = o.Graph is not null;
                _plotChartFrame.IsVisible = o.Graph is null;
                _plotSummary.Text = o.Summary;
                _plotTiles.SetTiles(o.Tiles);
                break;
        }
    }

    private void RenderRegression(RegressionOutcome o, IReadOnlyList<GraphItem>? items)
    {
        SetError(StatTab.Regression, o.Error);
        _regTiles.SetTiles(o.Tiles);
        _regEquation.Text = o.Error is null ? o.DisplayEquation : null;
        _regEquation.IsVisible = o.Error is null;
        _regNote.Text = o.Note ?? "";
        _regNote.IsVisible = o.Note is not null;
        var spec = o.Graph;
        if (spec is not null && _predicted is { } p && !_state.Regression.Residuals)
            spec = spec with { Annotations = [.. spec.Annotations, new GraphAnnotation(AnnotationKind.Point, [p], 1)] };
        _regHost.Show(spec?.Viewport, spec?.Annotations, spec is null ? null : items, o.Scene);
        UpdateRegressionOptions();
    }

    private void RenderTest(TestOutcome o, IReadOnlyList<GraphItem>? items)
    {
        SetError(StatTab.Tests, o.Error);
        _testTiles.SetTiles(o.Tiles);
        _testHypotheses.Text = o.Error is null ? (o.Interval is null ? o.Hypotheses : $"{o.Info.Name}: {o.Decision}") : "";
        _testHypotheses.IsVisible = o.Error is null;
        _testSentence.Text = o.Error is null ? o.Sentence : "";
        _testSentence.IsVisible = o.Error is null;
        var remark = o.Error is null ? Descriptives.Join(o.Note?.TrimEnd('.'), o.GraphNote) : null;
        _testGraphNote.Text = remark ?? "";
        _testGraphNote.IsVisible = remark is not null;
        _testGraphFrame.IsVisible = o.Graph is not null;
        ShowGraph(_testGraph, o.Graph, items);
    }
}
