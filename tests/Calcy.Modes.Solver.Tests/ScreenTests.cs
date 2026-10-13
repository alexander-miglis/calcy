using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Calcy.Modes.Solver.Logic;
using Calcy.Modes.Solver.Views;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Solver.Tests;

/// <summary>Headless tests of the Solver screen: every tool, cards, keyboard, navigation, persistence and screenshots.</summary>
public class ScreenTests
{
    // The app shows a mode below the top bar and above the status line; the design board is 1360 x 880.
    private const double Width = 1360;
    private const double Height = 790;

    private static (SolverView View, Window Window, AppServices Services) Open(string scheme = "nes", double width = Width, double height = Height, AppServices? services = null)
    {
        var s = services ?? Headless.CreateServices(scheme);
        var view = (SolverView)new SolverMode().CreateView(s);
        var w = Headless.Show(view, width, height);
        Idle(view);
        return (view, w, s);
    }

    /// <summary>Waits until no solve is running.</summary>
    private static void Idle(SolverView view)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        while (view.IsBusy && DateTime.UtcNow < deadline);
        Assert.False(view.IsBusy, "The solve did not finish");
        Headless.Pump();
    }

    private static void Wait(SolverView view, Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(task.IsCompleted, "The solve did not finish");
        task.GetAwaiter().GetResult();
        Idle(view);
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

    private static void Click(Control root, string automationName)
    {
        var button = root.GetLogicalDescendantsOfType<Button>().Concat(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Button>())
            .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == automationName);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Headless.Pump(1);
    }

    private static void SetPolynomial(SolverView view, params string[] highestFirst)
    {
        view.SetDegree(highestFirst.Length - 1);
        Idle(view);
        for (int i = 0; i < highestFirst.Length; i++) view.CoefficientBox(highestFirst.Length - 1 - i).Text = highestFirst[i];
        Headless.Pump(1);
    }

    private static void FillCards(SolverView view)
    {
        view.SelectTool(SolverTool.Inequality);
        Idle(view);
        Wait(view, view.SolveAsync());
        view.SelectTool(SolverTool.LinearSystem);
        Idle(view);
        Wait(view, view.SolveAsync());
        view.SelectTool(SolverTool.Polynomial);
        Idle(view);
        Wait(view, view.SolveAsync());
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
    public void Screenshot_polynomial_with_cards(string scheme)
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open(scheme);
            Assert.Equal(SolverTool.Polynomial, view.CurrentTool);
            FillCards(view);
            Assert.Equal(3, view.Cards.Count);
            Assert.All(view.Cards, c => Assert.False(c.IsError, string.Join(" ", c.Lines)));
            Assert.Equal("POLYNOMIAL", view.Cards[^1].Title);
            Assert.Equal("x = (5 ± √13)/2", view.Cards[^1].Lines[0]);
            var path = Headless.Save(w, $"solver-{scheme}.png");
            Assert.True(File.Exists(path));
        });
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("pico")]
    [InlineData("gameboy")]
    [InlineData("amber")]
    [InlineData("paper")]
    public void Screenshot_every_tool(string scheme)
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open(scheme);

            SetPolynomial(view, "1", "-6", "11", "-6");
            Wait(view, view.SolveAsync());
            Headless.Save(w, $"solver-polynomial-{scheme}.png");

            view.SelectTool(SolverTool.LinearSystem);
            Idle(view);
            Wait(view, view.SolveAsync());
            Headless.Save(w, $"solver-linear-{scheme}.png");

            view.SelectTool(SolverTool.NonlinearSystem);
            Idle(view);
            Wait(view, view.SolveAsync());
            Headless.Save(w, $"solver-nonlinear-{scheme}.png");

            view.SelectTool(SolverTool.Equation);
            Idle(view);
            Wait(view, view.SolveAsync());
            Headless.Save(w, $"solver-equation-{scheme}.png");

            view.SelectTool(SolverTool.Inequality);
            Idle(view);
            view.InequalityEditor.Text = "(x - 1)/(x + 2) >= 0";
            Wait(view, view.SolveAsync());
            Headless.Save(w, $"solver-inequality-{scheme}.png");
            Assert.Equal(5, view.Cards.Count);
            Assert.All(view.Cards, c => Assert.False(c.IsError, string.Join(" ", c.Lines)));
        });
    }

    [Fact]
    public void Screenshot_narrow_window()
    {
        Headless.Run(() =>
        {
            // The app's smallest window is 900 px wide.
            var (view, w, _) = Open("nes", 900, 700);
            Wait(view, view.SolveAsync());
            Headless.Save(w, "solver-narrow-polynomial-nes.png");
            view.SelectTool(SolverTool.Equation);
            Idle(view);
            Headless.Save(w, "solver-narrow-equation-nes.png");
            view.SelectTool(SolverTool.Inequality);
            Idle(view);
            Headless.Save(w, "solver-narrow-inequality-nes.png");
            Assert.True(view.EquationEditor.Bounds.Width >= 240, $"editor is {view.EquationEditor.Bounds.Width} wide");
        });
    }

    [Fact]
    public void Screenshot_special_cases()
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open("nes", Width, 1000);
            // A quartic with complex roots.
            SetPolynomial(view, "1", "0", "5", "0", "4");
            Wait(view, view.SolveAsync());
            Headless.Save(w, "solver-polynomial-complex-nes.png");

            // A quadratic with steps and no real solution, then its complex solutions.
            view.SelectTool(SolverTool.Equation);
            Idle(view);
            view.RangeBox.Text = "";
            view.EquationEditor.Text = "x^2 + x + 1 = 0";
            view.SetShowSteps(true);
            Idle(view);
            Wait(view, view.SolveAsync());
            Assert.Equal(EquationStatus.NoRealSolution, ((EquationOutcome)view.Outcomes[SolverTool.Equation]).Status);
            Headless.Save(w, "solver-equation-noreal-nes.png");
            Click(view, "Show complex solutions");
            Idle(view);
            Assert.Equal(EquationStatus.Solutions, ((EquationOutcome)view.Outcomes[SolverTool.Equation]).Status);
            Headless.Save(w, "solver-equation-complex-nes.png");

            // Steps of the quadratic formula.
            view.EquationEditor.Text = "x^2 - 5x + 3 = 0";
            Wait(view, view.SolveAsync());
            Assert.NotEmpty(((EquationOutcome)view.Outcomes[SolverTool.Equation]).Steps);
            Headless.Save(w, "solver-equation-steps-nes.png");

            // A degree-mode trig equation (general solution).
            view.Engine.Settings.Angle = Calcy.Core.AngleMode.Degree;
            view.EquationEditor.Text = "sin(x) = 1/2";
            view.SetComplex(false);
            Idle(view);
            Wait(view, view.SolveAsync());
            Headless.Save(w, "solver-equation-trig-nes.png");
            view.Engine.Settings.Angle = Calcy.Core.AngleMode.Radian;

            // Infinitely many solutions of a linear system, and an error card.
            view.SelectTool(SolverTool.LinearSystem);
            Idle(view);
            view.SetUnknownCount(2);
            view.SetEquationCount(2);
            view.GridBox(0, 0).Text = "1";
            view.GridBox(0, 1).Text = "1";
            view.GridBox(0, 6).Text = "1";
            view.GridBox(1, 0).Text = "2";
            view.GridBox(1, 1).Text = "2";
            view.GridBox(1, 6).Text = "2";
            Wait(view, view.SolveAsync());
            var lin = (LinearSystemOutcome)view.Outcomes[SolverTool.LinearSystem];
            Assert.True(lin.Kind == LinearKind.Infinite, string.Join(" | ", lin.EquationTexts) + " -> " + string.Join(", ", lin.Card.Lines));
            view.GridBox(1, 1).Text = "q";
            Wait(view, view.SolveAsync());
            Assert.True(view.Cards[^1].IsError);
            Headless.Save(w, "solver-linear-error-nes.png");
            view.SetLinearTyped(true);
            Headless.Save(w, "solver-linear-typed-nes.png");

            // The largest sizes: a sextic and a 6 × 6 system.
            view.SelectTool(SolverTool.Polynomial);
            Idle(view);
            SetPolynomial(view, "1", "0", "-14", "0", "49", "0", "-36");
            Wait(view, view.SolveAsync());
            Assert.Equal(6, ((PolynomialOutcome)view.Outcomes[SolverTool.Polynomial]).RealCount);
            Headless.Save(w, "solver-polynomial-sextic-nes.png");
            view.SelectTool(SolverTool.LinearSystem);
            Idle(view);
            view.SetLinearTyped(false);
            view.SetUnknownCount(6);
            view.SetEquationCount(6);
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 6; j++) view.GridBox(i, j).Text = i == j ? "4" : (i + j) % 2 == 0 ? "1" : "0";
                view.GridBox(i, 6).Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            Wait(view, view.SolveAsync());
            Assert.Equal(LinearKind.Unique, ((LinearSystemOutcome)view.Outcomes[SolverTool.LinearSystem]).Kind);
            Headless.Save(w, "solver-linear-6x6-nes.png");
        });
    }
}

/// <summary>Keyboard, background work, navigation and persistence of the Solver screen.</summary>
public class InteractionTests
{
    private static (SolverView View, Window Window, AppServices Services) Open(AppServices? services = null)
    {
        var s = services ?? Headless.CreateServices("nes");
        var view = (SolverView)new SolverMode().CreateView(s);
        var w = Headless.Show(view, 1360, 790);
        Idle(view);
        return (view, w, s);
    }

    private static void Idle(SolverView view)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        while (view.IsBusy && DateTime.UtcNow < deadline);
        Assert.False(view.IsBusy, "The solve did not finish");
        Headless.Pump();
    }

    private static void Wait(SolverView view, Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(task.IsCompleted, "The solve did not finish");
        Idle(view);
    }

    private static void Press(Window w, Key key)
    {
        w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Headless.Pump(1);
    }

    private static void Type(Window w, string text)
    {
        foreach (var c in text) w.KeyTextInput(c.ToString());
        Headless.Pump(1);
    }

    private static void Click(Control root, string automationName)
    {
        var button = root.GetLogicalDescendantsOfType<Button>().Concat(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Button>())
            .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == automationName);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Headless.Pump(1);
    }

    [Fact]
    public void Enter_in_a_coefficient_box_solves_and_makes_a_card()
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open();
            view.FocusFirst();
            Headless.Pump(1);
            Assert.True(view.CoefficientBox(2).IsFocused);
            view.CoefficientBox(2).SelectAll();
            Type(w, "2");
            Assert.Equal("2", view.CoefficientBox(2).Text);
            Press(w, Key.Enter);
            Idle(view);
            var card = Assert.Single(view.Cards);
            Assert.Equal("2x² − 5x + 3 = 0", card.Input);
            Assert.Equal(["x₁ = 1", "x₂ = 3/2"], card.Lines);
            Assert.Single(view.CardViews);
        });
    }

    [Fact]
    public void Typing_an_equation_and_pressing_enter()
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open();
            view.SelectTool(SolverTool.Equation);
            Idle(view);
            view.RangeBox.Text = "";
            view.EquationEditor.Text = "";
            view.FocusFirst();
            Headless.Pump(1);
            Type(w, "x^2");
            Press(w, Key.Right);
            Type(w, "=2");
            Assert.Equal("x^2=2", view.EquationEditor.Text.Replace(" ", ""));
            Press(w, Key.Enter);
            Idle(view);
            var card = Assert.Single(view.Cards);
            Assert.Equal(["x = −√2", "x = √2"], card.Lines);
            Assert.Equal("≈ −1.414213562,  1.414213562", card.Approx);
            var o = Assert.IsType<EquationOutcome>(view.Outcomes[SolverTool.Equation]);
            Assert.Equal("2 SOLUTIONS", o.Summary);
        });
    }

    [Fact]
    public void Working_card_appears_after_a_quarter_second_and_escape_stops_it()
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open();
            view.SolveHook = (_, _, ct) =>
            {
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("not cancelled");
            };
            view.FocusFirst();
            var task = view.SolveAsync();
            Assert.True(view.IsBusy);
            var until = DateTime.UtcNow.AddMilliseconds(400);
            while (DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Headless.Pump();
            Assert.Contains(view.GetLogicalDescendantsOfType<Card>(), c => c.Title == "WORKING");
            Headless.Save(w, "solver-working-nes.png");
            Press(w, Key.Escape);
            Wait(view, task);
            Assert.False(view.IsBusy);
            Assert.DoesNotContain(view.GetLogicalDescendantsOfType<Card>(), c => c.Title == "WORKING");
            var card = Assert.Single(view.Cards);
            Assert.Equal("STOPPED", card.Title);
            Assert.True(card.IsError);
            view.SolveHook = null;
        });
    }

    [Fact]
    public void A_solve_asked_for_during_a_background_refresh_still_runs()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            int calls = 0;
            view.SolveHook = (tool, request, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    // The refresh of the newly opened tab is slow.
                    ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                    ct.ThrowIfCancellationRequested();
                }
                return view.Engine.SolveEquation((EquationRequest)request, ct);
            };
            view.SelectTool(SolverTool.Equation);
            Assert.True(view.IsBusy);
            // Wait until the refresh is really running, so the solve below interrupts work in progress.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref calls) == 0 && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Assert.Equal(1, Volatile.Read(ref calls));
            var task = view.SolveAsync();
            Wait(view, task);
            var card = Assert.Single(view.Cards);
            Assert.Equal("EQUATION", card.Title);
            Assert.Equal(2, calls);
            view.SolveHook = null;
        });
    }

    [Fact]
    public void A_quick_solve_shows_no_working_card()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            var task = view.SolveAsync();
            Wait(view, task);
            Assert.DoesNotContain(view.GetLogicalDescendantsOfType<Card>(), c => c.Title == "WORKING");
            Assert.Single(view.Cards);
        });
    }

    [Fact]
    public void Use_and_plot_navigate_to_other_modes()
    {
        Headless.Run(() =>
        {
            var (view, _, s) = Open();
            var sent = new List<NavigationRequest>();
            s.NavigationRequested += (_, r) => sent.Add(r);
            Wait(view, view.SolveAsync());
            var cardView = view.CardViews[0];
            Click(cardView, "Use card 1 in Advanced");
            Click(cardView, "Plot card 1 in Graphing");
            Assert.Equal(2, sent.Count);
            Assert.Equal("advanced", sent[0].ModeId);
            var input = Assert.IsType<InputRequest>(sent[0].Payload);
            Assert.Equal("{(5 - sqrt(13))/2, (5 + sqrt(13))/2}", input.Text);
            Assert.False(input.Evaluate);
            Assert.Equal("graphing", sent[1].ModeId);
            Assert.Equal(["y = x^2 - 5*x + 3"], Assert.IsType<PlotRequest>(sent[1].Payload).Expressions);
        });
    }

    [Fact]
    public void Received_text_goes_to_the_right_tool()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            view.Receive(new InputRequest("solve(x^2 = 2, x)", Evaluate: true));
            Idle(view);
            Assert.Equal(SolverTool.Equation, view.CurrentTool);
            Assert.Equal("x^2 = 2", view.EquationEditor.Text);
            Assert.Single(view.Cards);
            Assert.True(view.EquationEditor.IsFocused);

            view.Receive(new InputRequest("x^2 < 4"));
            Idle(view);
            Assert.Equal(SolverTool.Inequality, view.CurrentTool);
            Assert.Equal("x^2 < 4", view.InequalityEditor.Text);
            Assert.Single(view.Cards);

            view.Receive("{x + y = 3, x - y = 1}");
            Idle(view);
            Assert.Equal(SolverTool.NonlinearSystem, view.CurrentTool);
            Assert.Equal("x + y = 3", view.NonlinearEditors[0].Text);
            Assert.NotNull(view.GuessBox("y"));
        });
    }

    [Fact]
    public void Inputs_and_cards_are_saved_and_restored()
    {
        Headless.Run(() =>
        {
            var (view, _, s) = Open();
            view.SetDegree(3);
            Idle(view);
            view.CoefficientBox(0).Text = "-6";
            Wait(view, view.SolveAsync());
            view.SelectTool(SolverTool.Inequality);
            Idle(view);
            view.InequalityEditor.Text = "x^2 >= 9";
            Wait(view, view.SolveAsync());
            var state = s.Prefs.GetModeState<SolverState>(SolverView.ModeId);
            Assert.NotNull(state);
            Assert.Equal(2, state!.Cards.Count);

            var fresh = Headless.CreateServices("nes");
            fresh.Prefs.ModeState[SolverView.ModeId] = s.Prefs.ModeState[SolverView.ModeId];
            var (restored, _, _) = Open(fresh);
            Assert.Equal(SolverTool.Inequality, restored.CurrentTool);
            Assert.Equal("x^2 >= 9", restored.InequalityEditor.Text);
            Assert.Equal("-6", restored.CoefficientBox(0).Text);
            Assert.Equal(2, restored.Cards.Count);
            Assert.Equal(2, restored.CardViews.Count);
            Assert.Equal("#2  INEQUALITY", restored.CardViews[0].Title);
            // The restored tab is solved again for its result area, without a new card.
            Assert.True(restored.Outcomes.ContainsKey(SolverTool.Inequality));
            Wait(restored, restored.SolveAsync());
            Assert.Equal(3, restored.Cards[^1].Number);
        });
    }

    [Fact]
    public void Edit_puts_a_card_back_into_its_tool()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            Wait(view, view.SolveAsync());
            view.CoefficientBox(1).Text = "7";
            view.SelectTool(SolverTool.Equation);
            Idle(view);
            Click(view.CardViews[0], "Edit card 1");
            Assert.Equal(SolverTool.Polynomial, view.CurrentTool);
            Assert.Equal("-5", view.CoefficientBox(1).Text);
        });
    }

    [Fact]
    public void Solve_for_chips_choose_the_unknown()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            view.SelectTool(SolverTool.Equation);
            Idle(view);
            view.RangeBox.Text = "";
            view.EquationEditor.Text = "a x + b = 0";
            Headless.Pump(1);
            var chips = view.GetLogicalDescendantsOfType<PixelToggle>().Where(t => (Avalonia.Automation.AutomationProperties.GetName(t) ?? "").StartsWith("Solve for ")).ToList();
            Assert.Contains(chips, c => c.IsChecked == true && Equals(c.Content, "x"));
            Assert.Contains(chips, c => Equals(c.Content, "b"));
            var b = chips.First(c => Equals(c.Content, "b"));
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Headless.Pump(1);
            Wait(view, view.SolveAsync());
            var o = Assert.IsType<EquationOutcome>(view.Outcomes[SolverTool.Equation]);
            Assert.Equal("b", o.Unknown);
            Assert.Equal("b = −ax", view.Cards[^1].Lines[0]);
        });
    }

    [Fact]
    public void Switching_grid_and_typed_keeps_the_system()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            view.SelectTool(SolverTool.LinearSystem);
            Idle(view);
            view.SetLinearTyped(true);
            Assert.Equal("2x + y - z = 8", view.LinearEditors[0].Text);
            view.LinearEditors[0].Text = "4x + y - z = 9";
            view.SetLinearTyped(false);
            Assert.Equal("4", view.GridBox(0, 0).Text);
            Assert.Equal("9", view.GridBox(0, 6).Text);
            Wait(view, view.SolveAsync());
            Assert.False(view.Cards[^1].IsError);
        });
    }

    [Fact]
    public void Tab_switch_refreshes_without_cards_and_errors_mark_the_box()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            foreach (var tool in SolverTools.All)
            {
                view.SelectTool(tool);
                Idle(view);
                Assert.True(view.Outcomes.ContainsKey(tool), tool.ToString());
            }
            Assert.Empty(view.Cards);
            view.SelectTool(SolverTool.Polynomial);
            view.CoefficientBox(0).Text = "1/";
            Wait(view, view.SolveAsync());
            Assert.Contains("error", view.CoefficientBox(0).Classes);
            Assert.True(view.Cards[^1].IsError);
            view.CoefficientBox(0).Text = "1";
            Assert.DoesNotContain("error", view.CoefficientBox(0).Classes);
        });
    }

    [Fact]
    public void Clear_removes_every_card()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            Wait(view, view.SolveAsync());
            Wait(view, view.SolveAsync());
            Assert.Equal(2, view.Cards.Count);
            Click(view, "Clear all cards");
            Assert.Empty(view.Cards);
            Assert.Empty(view.CardViews);
        });
    }
}

internal static class LogicalTreeHelpers
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root) where T : class =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
