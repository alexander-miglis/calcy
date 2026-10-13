using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.Modes.Statistics.Logic;
using Calcy.Modes.Statistics.Views;
using Calcy.Statistics;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Statistics.Tests;

/// <summary>Headless tests of the Statistics screen: typing data, every tab, cards, persistence and screenshots.</summary>
public class ScreenTests
{
    // The app shows a mode below the 64 px top bar and above the status line; the design board is 1360 x 880.
    private const double Width = 1360;
    private const double Height = 790;

    private static (StatisticsView View, Window Window, AppServices Services) Open(string scheme = "nes", double width = Width, double height = Height, AppServices? services = null, bool empty = false)
    {
        var s = services ?? Headless.CreateServices(scheme);
        if (empty) s.Prefs.SetModeState("statistics", new StatsModeState { Columns = [new ColumnState { Name = "L1" }, new ColumnState { Name = "L2" }] });
        var view = (StatisticsView)new StatisticsMode().CreateView(s);
        Opened.Add(view);
        var w = Headless.Show(view, width, height);
        Idle(view);
        return (view, w, s);
    }

    private static readonly List<StatisticsView> Opened = [];

    /// <summary>
    /// Runs a screen test on the UI thread and lets every screen it opened settle (analyses, graph sampling, the save
    /// timer) before the next test resets the headless session, so no late work touches the next test's dispatcher.
    /// </summary>
    private static void RunScreen(Action body)
    {
        Headless.Run(() =>
        {
            try
            {
                body();
            }
            finally
            {
                foreach (var v in Opened) Settle(v);
                Opened.Clear();
            }
        });
    }

    private static void Settle(StatisticsView view)
    {
        Headless.WaitForIdle(view, TimeSpan.FromSeconds(10));
        var until = DateTime.UtcNow.AddSeconds(3);
        while (view.SavePending && DateTime.UtcNow < until)
        {
            Headless.Pump(1);
            Thread.Sleep(10);
        }
    }

    private static void Idle(StatisticsView view)
    {
        Assert.True(Headless.WaitForIdle(view, TimeSpan.FromSeconds(30)), "The screen did not settle");
        Headless.Pump();
    }

    private static void Type(Window w, string text)
    {
        foreach (var c in text) w.KeyTextInput(c.ToString());
        Headless.Pump(1);
    }

    private static void Press(Window w, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        w.KeyPress(key, modifiers, PhysicalKey.None, null);
        w.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Headless.Pump(1);
    }

    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(task.IsCompleted, "The operation did not finish");
        task.GetAwaiter().GetResult();
        Headless.Pump();
    }

    private static T Find<T>(Control root, string automationName) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().First(c => AutomationProperties.GetName(c) == automationName);

    private static void Click(Control root, string automationName)
    {
        var button = Find<Button>(root, automationName);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Headless.Pump(1);
    }

    /// <summary>Types the design's sample data into L1 and L2 with the keyboard, as a student would.</summary>
    private static void EnterDesignData(StatisticsView view, Window w)
    {
        view.ListEditor.Activate(0, 0);
        Headless.Pump(1);
        foreach (var v in TestData.Hours) { Type(w, v.ToString(System.Globalization.CultureInfo.InvariantCulture)); Press(w, Key.Enter); }
        view.ListEditor.Activate(1, 0);
        Headless.Pump(1);
        foreach (var v in TestData.Scores) { Type(w, v.ToString(System.Globalization.CultureInfo.InvariantCulture)); Press(w, Key.Enter); }
        Idle(view);
    }

    private static void ShowTab(StatisticsView view, StatTab tab)
    {
        view.ShowTab(tab);
        Idle(view);
    }

    [Fact]
    public void First_screen_shows_the_design_sample_with_a_regression()
    {
        RunScreen(() =>
        {
            var (view, w, s) = Open();
            Assert.Equal(StatTab.Regression, view.Tab);
            var o = Assert.IsType<RegressionOutcome>(view.Outcome(StatTab.Regression));
            Assert.Null(o.Error);
            Assert.Equal("y = 4.54262x + 47.9667", o.DisplayEquation);
            Assert.Equal("{1, 2, 2, 3, 4, 5, 5, 6, 7, 8}", ExprPrinter.ToInput(s.Session.Env.Variables["L1"]));
            Assert.Empty(view.Cards);
            Headless.Save(w, "statistics-start-nes.png");
        });
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("pico")]
    [InlineData("gameboy")]
    [InlineData("amber")]
    [InlineData("paper")]
    [InlineData("c64")]
    [InlineData("mono")]
    [InlineData("synth")]
    public void Screenshot_with_the_design_data_one_var_regression_and_t_test(string scheme)
    {
        RunScreen(() =>
        {
            var (view, w, s) = Open(scheme, empty: true);
            EnterDesignData(view, w);
            Assert.Equal(["1", "2", "2", "3", "4", "5", "5", "6", "7", "8"], view.Table.Find("L1")!.Cells);
            Assert.Equal("84", view.Table.Find("L2")![9]);
            view.RenameList("L1", "L1", "hours");
            view.RenameList("L2", "L2", "score");

            view.State.OneVar.List = "L2";
            ShowTab(view, StatTab.OneVar);
            var one = Assert.IsType<OneVarOutcome>(view.Outcome(StatTab.OneVar));
            Assert.Equal(67.5, one.Stats!.Mean);
            Wait(view.AddCardAsync());
            Headless.Save(w, $"statistics-{scheme}-onevar.png");

            ShowTab(view, StatTab.Regression);
            var reg = Assert.IsType<RegressionOutcome>(view.Outcome(StatTab.Regression));
            Assert.Equal(0.992066881882608, reg.R!.Value, 12);
            Wait(view.AddCardAsync());
            Headless.Save(w, $"statistics-{scheme}-regression.png");

            view.State.Test.Kind = TestKind.TTest;
            view.State.Test.Stats = false;
            view.State.Test.Fields["mu0"] = "65";
            view.State.Test.Fields["list"] = "L2";
            view.ShowRecipe(new StatRecipe { Tab = StatTab.Tests, Test = view.State.Test.Clone() });
            Idle(view);
            var test = Assert.IsType<TestOutcome>(view.Outcome(StatTab.Tests));
            Assert.Equal(0.746832677439348, test.Test!.Statistic, 12);
            Assert.Equal("Keep H₀", test.Decision);
            Assert.Equal(2, test.Graph!.Annotations.Where(a => a.Kind == Calcy.Graphing.AnnotationKind.Shade).DistinctBy(a => a.Points).Count());
            Wait(view.AddCardAsync());
            Idle(view);
            Assert.Equal(3, view.Cards.Count);
            Assert.Equal(["T-TEST L2", "LINREG AX+B L1,L2", "1-VAR L2"], view.Cards.Select(c => c.Title));
            Assert.Equal([3, 2, 1], view.Cards.Select(c => c.Number));
            var path = Headless.Save(w, $"statistics-{scheme}.png");
            Assert.True(File.Exists(path));
            _ = s;
        });
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("amber")]
    public void Distribution_explorer_shades_the_area(string scheme)
    {
        RunScreen(() =>
        {
            var (view, w, _) = Open(scheme);
            ShowTab(view, StatTab.Distribution);
            var o = Assert.IsType<DistributionOutcome>(view.Outcome(StatTab.Distribution));
            Assert.Equal(0.950004209703559, o.Value, 12);
            Wait(view.AddCardAsync());
            Headless.Save(w, $"statistics-distribution-{scheme}.png");

            view.ShowRecipe(new StatRecipe { Tab = StatTab.Distribution, Distribution = new DistributionSettings { Kind = DistributionKind.Binomial, Lower = "3", Upper = "6" } });
            Idle(view);
            var b = Assert.IsType<DistributionOutcome>(view.Outcome(StatTab.Distribution));
            Assert.Equal(new BinomialDistribution(10, 0.5).Probability(3, 6), b.Value, 12);
            Headless.Save(w, $"statistics-distribution-binomial-{scheme}.png");

            view.ShowRecipe(new StatRecipe { Tab = StatTab.Distribution, Distribution = new DistributionSettings { Kind = DistributionKind.ChiSquared, Mode = DistributionMode.Inverse, Area = "0.05", Tail = Tail.Right } });
            Idle(view);
            var chi = Assert.IsType<DistributionOutcome>(view.Outcome(StatTab.Distribution));
            Assert.Equal(7.814728, chi.Value, 5);
            Headless.Save(w, $"statistics-distribution-chi2-{scheme}.png");
        });
    }

    [Theory]
    [InlineData(PlotKind.Histogram)]
    [InlineData(PlotKind.BoxPlot)]
    [InlineData(PlotKind.Scatter)]
    [InlineData(PlotKind.NormalProbability)]
    [InlineData(PlotKind.DotPlot)]
    [InlineData(PlotKind.BarChart)]
    public void Each_plot_type(PlotKind kind)
    {
        RunScreen(() =>
        {
            var (view, w, _) = Open();
            if (kind == PlotKind.BarChart)
            {
                view.EditList("L3", ["red", "blue", "red", "green", "red", "blue", "red", "yellow", "blue", "red"], "colour");
            }
            if (kind == PlotKind.BoxPlot) view.EditCell("L2", 10, "20");
            var settings = new PlotSettings
            {
                Kind = kind,
                List = kind == PlotKind.BarChart ? "L3" : kind is PlotKind.Scatter or PlotKind.DotPlot ? "L1" : "L2",
                List2 = kind == PlotKind.Scatter ? "L2" : kind == PlotKind.BoxPlot ? "L1" : null,
            };
            view.ShowRecipe(new StatRecipe { Tab = StatTab.Plots, Plot = settings });
            Idle(view);
            var o = Assert.IsType<PlotOutcome>(view.Outcome(StatTab.Plots));
            Assert.True(o.Error is null, o.Error);
            if (kind == PlotKind.BoxPlot) Assert.Equal([20.0], o.Boxes[0].Outliers);
            Wait(view.AddCardAsync());
            Headless.Save(w, $"statistics-plot-{kind.ToString().ToLowerInvariant()}-nes.png");
        });
    }

    [Fact]
    public void Two_var_and_tests_tab_screenshots()
    {
        RunScreen(() =>
        {
            var (view, w, _) = Open();
            ShowTab(view, StatTab.TwoVar);
            var two = Assert.IsType<TwoVarOutcome>(view.Outcome(StatTab.TwoVar));
            Assert.Equal(0.992066881882608, two.Stats!.R, 12);
            Headless.Save(w, "statistics-twovar-nes.png");

            view.ShowRecipe(new StatRecipe
            {
                Tab = StatTab.Tests,
                Test = new TestSettings { Kind = TestKind.TTest, Alternative = Alternative.Greater, Fields = { ["mu0"] = "60", ["list"] = "L2" } },
            });
            Idle(view);
            var t = Assert.IsType<TestOutcome>(view.Outcome(StatTab.Tests));
            Assert.True(t.Rejects);
            Headless.Save(w, "statistics-test-reject-nes.png");

            view.ShowRecipe(new StatRecipe { Tab = StatTab.Tests, Test = new TestSettings { Kind = TestKind.TInterval, Fields = { ["list"] = "L2" } } });
            Idle(view);
            Assert.NotNull(Assert.IsType<TestOutcome>(view.Outcome(StatTab.Tests)).Interval);
            Headless.Save(w, "statistics-interval-nes.png");

            view.ShowRecipe(new StatRecipe { Tab = StatTab.Tests, Test = new TestSettings { Kind = TestKind.Anova, Fields = { ["lists"] = "L1,L2" } } });
            Idle(view);
            Assert.Null(Assert.IsType<TestOutcome>(view.Outcome(StatTab.Tests)).Error);
            Headless.Save(w, "statistics-anova-nes.png");
        });
    }

    [Fact]
    public void Narrow_window_and_a_long_list()
    {
        RunScreen(() =>
        {
            var (view, w, _) = Open("nes", 1120, 700);
            var rng = new Random(3);
            view.ListEditor.Activate(0, 0, focus: false);
            view.PasteText(string.Join("\n", Enumerable.Range(0, 2000).Select(i => $"{i}\t{(50 + 0.02 * i + rng.NextDouble() * 10).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}")));
            Idle(view);
            Assert.Equal(2000, view.Rows);
            var o = Assert.IsType<RegressionOutcome>(view.Outcome(StatTab.Regression));
            Assert.Null(o.Error);
            Assert.Equal(0.02, o.Parameters[0].Value, 2);
            view.ListEditor.Activate(1, 1500);
            Headless.Pump();
            Headless.Save(w, "statistics-narrow-nes.png");
        });
    }

    [Fact]
    public void Keyboard_editing_moves_between_cells_and_updates_the_session()
    {
        RunScreen(() =>
        {
            var (view, w, s) = Open(empty: true);
            view.FocusFirst();
            Headless.Pump(1);
            Assert.Equal((0, 0), view.ListEditor.Active);
            Type(w, "3.5");
            Press(w, Key.Tab);
            Assert.Equal((1, 0), view.ListEditor.Active);
            Type(w, "7");
            Press(w, Key.Enter);
            Assert.Equal((1, 1), view.ListEditor.Active);
            Press(w, Key.Left);
            Assert.Equal((0, 1), view.ListEditor.Active);
            Type(w, "1/4");
            Press(w, Key.Up);
            Assert.Equal((0, 0), view.ListEditor.Active);
            Assert.Equal("3.5", view.ListEditor.Editor.Text);
            Idle(view);
            Assert.Equal("{7/2, 1/4}", ExprPrinter.ToInput(s.Session.Env.Variables["L1"]));
            Assert.Equal("{7}", ExprPrinter.ToInput(s.Session.Env.Variables["L2"]));
            // Escape undoes the edit of the active cell.
            Type(w, "99");
            Press(w, Key.Escape);
            Assert.Equal("3.5", view.Table.Find("L1")![0]);
            Assert.Equal("L1 row 1", AutomationProperties.GetName(view.ListEditor.Editor));
        });
    }

    [Fact]
    public void Paste_formula_and_lists_from_other_modes()
    {
        RunScreen(() =>
        {
            var (view, _, s) = Open(empty: true);
            view.ListEditor.Activate(0, 0, focus: false);
            view.PasteText("x\ty\n1,5\t2\n2,5\t4\n3,5\t7\n");
            Assert.Equal(["1.5", "2.5", "3.5"], view.Table.Find("L1")!.Cells);
            Assert.Equal("x", view.Table.Find("L1")!.Caption);
            Assert.Equal("y", view.Table.Find("L2")!.Caption);

            view.Receive(new InputRequest("L3 = L2^2", Evaluate: true));
            Idle(view);
            Assert.Equal(["4", "16", "49"], view.Table.Find("L3")!.Cells);
            Assert.Equal("L2^2", view.Table.Find("L3")!.Formula);

            s.Session.Evaluate("L2 = {1, 2, 3}");
            Headless.Pump();
            Idle(view);
            Assert.Equal(["1", "2", "3"], view.Table.Find("L2")!.Cells);
            Assert.Equal(["1", "4", "9"], view.Table.Find("L3")!.Cells);

            view.Receive(new DataRequest("table", [[10, 20, 30], [1, 4, 9]], ["t", "v"]));
            Idle(view);
            Assert.Equal(["L4", "L5"], view.Table.Columns.Skip(3).Select(c => c.Name));
            Assert.Equal("t", view.Table.Find("L4")!.Caption);
            Assert.Equal(StatTab.Regression, view.Tab);
            Assert.Equal(("L4", "L5"), (view.State.Regression.X, view.State.Regression.Y));
            Assert.Equal("{10, 20, 30}", ExprPrinter.ToInput(s.Session.Env.Variables["L4"]));
        });
    }

    [Fact]
    public void Card_buttons_use_show_plot_and_regression_actions()
    {
        RunScreen(() =>
        {
            var (view, _, s) = Open();
            NavigationRequest? sent = null;
            s.NavigationRequested += (_, r) => sent = r;
            Wait(view.AddCardAsync());
            var card = view.CardViews.First();
            Click(card, "Send linreg(L1, L2) to Advanced");
            Assert.Equal("advanced", sent!.ModeId);
            Assert.Equal("linreg(L1, L2)", Assert.IsType<InputRequest>(sent.Payload).Text);
            Assert.False(((InputRequest)sent.Payload).Evaluate);

            Click(view, "Plot the points and the fitted curve in Graphing");
            Assert.Equal("graphing", sent.ModeId);
            var plot = Assert.IsType<PlotRequest>(sent.Payload);
            Assert.Equal("(L1, L2)", plot.Expressions[0]);
            Assert.StartsWith("y = 4.54261954", plot.Expressions[1]);

            Click(view, "Store the residuals y - f(x) as a new list");
            Idle(view);
            var resid = view.Table.Find("L3")!;
            Assert.Equal("resid", resid.Caption);
            Assert.Equal(52 - (4.54261954261954 + 47.966735966736), resid.Value(0, s.Session).Number, 9);

            Find<TextBox>(view, "Predict at x").Text = "4.5";
            Click(view, "Evaluate the fitted curve at x and keep it as a card");
            Assert.Equal("PREDICT LINREG AX+B", view.Cards[0].Title);
            Assert.Contains("68.4085", view.Cards[0].Headline);
            TestData.EvaluateUse(view.Cards[0].UseText!);

            Click(view, "Store the fitted equation as a function, for example f(x)");
            Assert.True(s.Session.Env.Functions.ContainsKey("f"));
            Assert.Equal(4.54261954261954 * 2 + 47.966735966736, ((NumberExpr)s.Session.EvaluateExpression("f(2)", approximate: true)).Value.Re, 9);

            ShowTab(view, StatTab.OneVar);
            Click(view.CardViews.Last(), "Open this analysis again with its plot");
            Idle(view);
            Assert.Equal(StatTab.Regression, view.Tab);
        });
    }

    [Fact]
    public void State_is_saved_and_restored()
    {
        RunScreen(() =>
        {
            var (view, _, s) = Open();
            view.Table.SetCell("L1", 0, "1.5");
            view.RenameList("L2", "score", "points");
            view.ShowTab(StatTab.OneVar);
            Idle(view);
            Wait(view.AddCardAsync());
            view.Save();
            var fresh = Headless.CreateServices("nes");
            fresh.Prefs.ModeState["statistics"] = s.Prefs.ModeState["statistics"];
            var (restored, _, _) = Open(services: fresh);
            Assert.Equal("1.5", restored.Table.Find("L1")![0]);
            Assert.Equal("points", restored.Table.Find("score")!.Caption);
            Assert.Equal(StatTab.OneVar, restored.Tab);
            Assert.Single(restored.Cards);
            Assert.Equal("1-VAR score", restored.Cards[0].Title);
            Assert.True(fresh.Session.Env.Variables.ContainsKey("score"));
        });
    }

    [Fact]
    public void Toolbar_formula_box_and_frequency_list()
    {
        RunScreen(() =>
        {
            var (view, _, s) = Open();
            Click(view, "Add a list");
            Assert.Equal(["L1", "L2", "L3"], view.Table.Columns.Select(c => c.Name));
            Assert.Equal((2, 0), view.ListEditor.Active);

            var formula = Find<TextBox>(view, "Column formula");
            formula.Text = "L3 = 2 + L1 * 0";
            view.ApplyFormula();
            Idle(view);
            Assert.Equal(Enumerable.Repeat("2", 10), view.Table.Find("L3")!.Cells);
            formula.Text = "L4 = L9 * 2";
            view.ApplyFormula();
            Assert.NotNull(view.Table.Find("L4")!.FormulaError);

            // L3 as frequencies: every value counts twice, so the mean stays and n doubles.
            view.State.OneVar.List = "L2";
            view.State.OneVar.Freq = "L3";
            ShowTab(view, StatTab.OneVar);
            var one = Assert.IsType<OneVarOutcome>(view.Outcome(StatTab.OneVar));
            Assert.Equal(20, one.Stats!.N);
            Assert.Equal(67.5, one.Stats.Mean, 12);
            Assert.Equal("L3", Find<ComboBox>(view, "Frequency list").SelectedItem);

            view.ListEditor.Activate(1, 0, focus: false);
            Click(view, "Sort the rows by the active list, largest first (pairs stay together)");
            Assert.Equal("84", view.Table.Find("L2")![0]);
            Assert.Equal("8", view.Table.Find("L1")![0]);
            Click(view, "Empty the active list");
            Assert.True(view.Table.Find("L2")!.IsEmpty);
            Assert.False(s.Session.Env.Variables.ContainsKey("L2"));
            Click(view, "Delete the active list");
            Assert.Null(view.Table.Find("L2"));
            Idle(view);
            // The 1-Var tab moves on to a list that still exists.
            Assert.Equal("L1", view.State.OneVar.List);
            Assert.Null(Assert.IsType<OneVarOutcome>(view.Outcome(StatTab.OneVar)).Error);
        });
    }

    [Fact]
    public void Test_fields_follow_typing_and_stats_input()
    {
        RunScreen(() =>
        {
            var (view, _, _) = Open();
            ShowTab(view, StatTab.Tests);
            Find<TextBox>(view, "The mean under H₀").Text = "58";
            Headless.Pump();
            Idle(view);
            var o = Assert.IsType<TestOutcome>(view.Outcome(StatTab.Tests));
            Assert.Equal("H₀: μ = 58   H₁: μ ≠ 58", o.Hypotheses);
            Assert.True(o.Rejects);

            var stats = Find<Avalonia.Controls.Primitives.ToggleButton>(view, "Type summary statistics (x̄, Sx, n)");
            stats.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Headless.Pump();
            Find<TextBox>(view, "The sample mean").Text = "61";
            Find<TextBox>(view, "The sample standard deviation").Text = "2";
            Find<TextBox>(view, "The sample size").Text = "16";
            Headless.Pump();
            Idle(view);
            o = Assert.IsType<TestOutcome>(view.Outcome(StatTab.Tests));
            Assert.Equal(6, o.Test!.Statistic, 12);
            Assert.Equal("ttest(58, 61, 2, 16)", o.UseText);
        });
    }

    [Fact]
    public void Mode_metadata()
    {
        var m = new StatisticsMode();
        Assert.Equal("statistics", m.Id);
        Assert.Equal("Statistics", m.Title);
        Assert.Equal("Data, regressions, tests, distributions", m.Description);
        Assert.Equal("Ctrl+5", m.Shortcut);
        Assert.Equal("Mem", m.AccentToken);
        Assert.Equal("stats", m.Icon);
    }
}
