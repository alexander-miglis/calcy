using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Calcy.Core.Expressions;
using Calcy.Modes.Matrix.Logic;
using Calcy.Modes.Matrix.Views;
using Calcy.UI.Services;
using Calcy.UI.TestKit;
using static Calcy.Modes.Matrix.Tests.TestUtil;

namespace Calcy.Modes.Matrix.Tests;

/// <summary>Headless tests of the Matrix screen: typing, keyboard navigation, operations, cards, persistence and screenshots.</summary>
public class ScreenTests
{
    // The app shows a mode below the 64 px top bar and above the status line; the design board is 1360 x 880.
    private const double Width = 1360;
    private const double Height = 790;

    private static (MatrixModeView View, Window Window, AppServices Services) Open(string scheme = "nes", double width = Width, double height = Height, AppServices? services = null)
    {
        var s = services ?? Headless.CreateServices(scheme);
        var view = (MatrixModeView)new MatrixMode().CreateView(s);
        var w = Headless.Show(view, width, height);
        return (view, w, s);
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

    /// <summary>Types a matrix into the grid with the keyboard: Tab between cells, like a student would.</summary>
    private static void EnterMatrix(MatrixModeView view, Window w, string[][] rows)
    {
        var e = view.Selected!;
        while (e.Rows < rows.Length) Click(view, "More rows");
        while (e.Rows > rows.Length) Click(view, "Fewer rows");
        while (e.Cols < rows[0].Length) Click(view, "More columns");
        while (e.Cols > rows[0].Length) Click(view, "Fewer columns");
        view.Editor.FocusCell(0, 0);
        Headless.Pump(1);
        foreach (var row in rows)
        {
            foreach (var cell in row)
            {
                Type(w, cell);
                Press(w, Key.Tab);
            }
        }
    }

    private static void Click(Control root, string automationName)
    {
        var button = root.GetLogicalDescendantsOfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == automationName);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Headless.Pump(1);
    }

    private static readonly string[][] Tridiagonal = [["2", "-1", "0"], ["-1", "2", "-1"], ["0", "-1", "2"]];

    [Theory]
    [InlineData("nes")]
    [InlineData("pico")]
    [InlineData("gameboy")]
    [InlineData("amber")]
    [InlineData("paper")]
    [InlineData("c64")]
    [InlineData("mono")]
    [InlineData("synth")]
    public void Screenshot_with_a_three_by_three_matrix_and_cards(string scheme)
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open(scheme, Width, 880);
            EnterMatrix(view, w, Tridiagonal);
            Assert.Equal(["2", "-1", "0", "-1", "2", "-1", "0", "-1", "2"], view.Selected!.Cells);
            Wait(view.RunOperationAsync(MatrixOperation.Determinant));
            Wait(view.RunOperationAsync(MatrixOperation.Inverse));
            Wait(view.RunOperationAsync(MatrixOperation.Eigen));
            Wait(view.RunOperationAsync(MatrixOperation.Rref));
            Assert.Equal(4, view.Cards.Count);
            Assert.All(view.Cards, c => Assert.False(c.IsError, string.Join(" ", c.Blocks.Select(b => b.Text))));
            Assert.True(view.CardViews.First().StepsVisible);
            view.Editor.FocusCell(1, 1);
            Headless.Pump();
            var path = Headless.Save(w, $"matrix-{scheme}.png");
            Assert.True(File.Exists(path));

            // A tall frame that shows every card in full, for review.
            var (tallView, tall, _) = Open(scheme, Width, 2000);
            EnterMatrix(tallView, tall, Tridiagonal);
            Wait(tallView.RunOperationAsync(MatrixOperation.Rref));
            Wait(tallView.RunOperationAsync(MatrixOperation.Inverse));
            Wait(tallView.RunOperationAsync(MatrixOperation.Eigen));
            Wait(tallView.RunOperationAsync(MatrixOperation.Determinant));
            Headless.Save(tall, $"matrix-{scheme}-cards.png");
        });
    }

    [Fact]
    public void Solve_cards_screenshot()
    {
        Headless.Run(() =>
        {
            var (view, w, s) = Open("nes", Width, 1400);
            Wait(view.RunOperationAsync(MatrixOperation.Solve));
            s.Session.Env.SetVariable("P", M("[[1, 1, 1], [0, 1, 2]]"));
            s.Session.Env.SetVariable("q", M("[[6], [3]]"));
            Headless.Pump();
            view.Select("P");
            var combo = view.GetLogicalDescendantsOfType<ComboBox>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Second matrix");
            combo.SelectedItem = "q";
            Headless.Pump();
            Wait(view.RunOperationAsync(MatrixOperation.Solve));
            Wait(view.RunOperationAsync(MatrixOperation.NullSpace));
            Wait(view.RunOperationAsync(MatrixOperation.Lu));
            Assert.StartsWith("Infinitely", view.Cards[2].Blocks[0].Text);
            Headless.Save(w, "matrix-solve-nes.png");
        });
    }

    [Fact]
    public void Twelve_by_twelve_matrix_scrolls_and_solves()
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open("nes");
            var rng = new Random(7);
            var text = string.Join("\n", Enumerable.Range(0, 12).Select(_ => string.Join("\t", Enumerable.Range(0, 12).Select(_ => rng.Next(-9, 10)))));
            view.PasteText(text);
            Assert.Equal(12, view.Selected!.Rows);
            Assert.Equal(12, view.Editor.Columns);
            var started = DateTime.UtcNow;
            Wait(view.RunOperationAsync(MatrixOperation.Determinant));
            Wait(view.RunOperationAsync(MatrixOperation.Eigen));
            Wait(view.RunOperationAsync(MatrixOperation.Inverse));
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20));
            Assert.All(view.Cards, c => Assert.False(c.IsError, string.Join(" ", c.Blocks.Select(b => b.Text))));
            Headless.Save(w, "matrix-12x12-nes.png");
        });
    }

    [Fact]
    public void Store_as_row_for_a_decomposition()
    {
        Headless.Run(() =>
        {
            var (view, w, s) = Open("nes");
            Wait(view.RunOperationAsync(MatrixOperation.Lu));
            var card = view.CardViews.First();
            Click(card, "Save the result under a name");
            Headless.Save(w, "matrix-store-nes.png");
            Assert.Null(card.StoreAs("L", 1));
            var l = Assert.IsType<MatrixExpr>(s.Session.Env.Variables["L"]);
            Assert.Equal(3, l.Rows);
            Assert.NotNull(view.Library.Find("L"));
        });
    }

    [Fact]
    public void Decimals_first_setting_shows_decimals()
    {
        Headless.Run(() =>
        {
            var (view, _, s) = Open("nes");
            s.Settings.Results = Calcy.Core.ResultPreference.DecimalFirst;
            view.Select("C");
            Wait(view.RunOperationAsync(MatrixOperation.Inverse));
            Assert.True(view.CardViews.First().ShowsDecimals);
        });
    }

    [Fact]
    public void Keyboard_moves_between_cells_and_shows_parsed_values()
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open();
            view.FocusFirst();
            Headless.Pump(1);
            Assert.Equal((0, 0), view.Editor.FocusedCell);
            Type(w, "sqrt(8)");
            Press(w, Key.Tab);
            Assert.Equal((0, 1), view.Editor.FocusedCell);
            // The cell we left shows the exact value, the library keeps what was typed.
            Assert.Equal("2√2", view.Editor.Cell(0, 0).Text);
            Assert.Equal("sqrt(8)", view.Selected![0, 0]);
            Press(w, Key.Down);
            Assert.Equal((1, 1), view.Editor.FocusedCell);
            Press(w, Key.Enter);
            Assert.Equal((1, 2), view.Editor.FocusedCell);
            Press(w, Key.Tab, RawInputModifiers.Shift);
            Assert.Equal((1, 1), view.Editor.FocusedCell);
            Press(w, Key.Up);
            Press(w, Key.Left);
            Assert.Equal((0, 0), view.Editor.FocusedCell);
            Assert.Equal("sqrt(8)", view.Editor.Cell(0, 0).Text);
            Type(w, "1/");
            Assert.Contains("err", view.Editor.Cell(0, 0).Classes);
            Type(w, "2");
            Assert.DoesNotContain("err", view.Editor.Cell(0, 0).Classes);
            Press(w, Key.Tab);
            Assert.Equal("1/2", view.Editor.Cell(0, 0).Text);
        });
    }

    [Fact]
    public void Cells_update_the_session_variable()
    {
        Headless.Run(() =>
        {
            var (view, w, s) = Open();
            view.FocusFirst();
            Type(w, "a");
            Headless.Pump();
            var a = Assert.IsType<MatrixExpr>(s.Session.Env.Variables["A"]);
            Assert.Equal("a", In(a[0, 0]));
        });
    }

    [Fact]
    public void Paste_fills_and_resizes_the_grid()
    {
        Headless.Run(() =>
        {
            var (view, _, s) = Open();
            view.PasteText("1\t2\t3\t4\n5\t6\t7\t8\n");
            Assert.Equal(2, view.Selected!.Rows);
            Assert.Equal(4, view.Selected!.Cols);
            Assert.Equal(4, view.Editor.Columns);
            Assert.Equal("[[1, 2, 3, 4], [5, 6, 7, 8]]", In(s.Session.Env.Variables["A"]));
            view.PasteText("[[1, 1/2], [sqrt(2), 0]]");
            Assert.Equal("√2", view.Editor.Cell(1, 0).Text);
        });
    }

    [Fact]
    public void Library_follows_the_session()
    {
        Headless.Run(() =>
        {
            var (view, _, s) = Open();
            s.Session.Env.SetVariable("M", M("[[1, 2], [3, 4]]"));
            Headless.Pump();
            Assert.NotNull(view.Library.Find("M"));
            s.Session.Env.SetVariable("A", M("[[9]]"));
            Headless.Pump();
            Assert.Equal(["9"], view.Library.Find("A")!.Cells);
            Assert.Equal("9", view.Editor.Cell(0, 0).Text);
            s.Session.Env.Remove("M");
            Headless.Pump();
            Assert.Null(view.Library.Find("M"));
            Click(view, "Add a new matrix");
            Assert.Equal("D", view.Selected!.Name);
            Assert.True(s.Session.Env.Variables.ContainsKey("D"));
            Click(view, "Add a new column vector");
            Assert.Equal("u", view.Selected!.Name);
            Assert.Equal(1, view.Selected!.Cols);
            Click(view, "Delete the selected matrix");
            Assert.Null(view.Library.Find("u"));
            Assert.False(s.Session.Env.Variables.ContainsKey("u"));
        });
    }

    [Fact]
    public void Expression_line_store_as_and_use()
    {
        Headless.Run(() =>
        {
            var (view, _, s) = Open();
            NavigationRequest? sent = null;
            s.NavigationRequested += (_, r) => sent = r;
            view.Receive(new InputRequest("A⁻¹·B"));
            Assert.Equal("A⁻¹·B", view.ExpressionBox.Text);
            Wait(view.EvaluateExpressionAsync());
            var card = view.Cards[0];
            Assert.Equal("[[2], [3], [-1]]", card.CopyText);
            var cardView = view.CardViews.First();
            Assert.Null(cardView.StoreAs("X"));
            Assert.NotNull(view.Library.Find("X"));
            Assert.Equal("[[2], [3], [-1]]", In(s.Session.Env.Variables["X"]));
            Assert.NotNull(cardView.StoreAs("det"));
            Click(cardView, "Use the result in Advanced mode");
            Assert.NotNull(sent);
            Assert.Equal("advanced", sent!.ModeId);
            Assert.Equal("[[2], [3], [-1]]", Assert.IsType<InputRequest>(sent.Payload).Text);
            view.Receive(new InputRequest("D = 2A", Evaluate: true));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((view.IsBusy || view.Cards.Count < 2) && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Headless.Pump();
            Assert.Equal("STORED", view.Cards[0].Title);
            Assert.NotNull(view.Library.Find("D"));
        });
    }

    [Fact]
    public void Decimal_toggle_and_steps_toggle()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            view.Select("C");
            Wait(view.RunOperationAsync(MatrixOperation.Inverse));
            var cv = view.CardViews.First();
            Assert.False(cv.StepsVisible);
            Click(cv, "Show or hide the elimination steps");
            Assert.True(cv.StepsVisible);
            Click(cv, "Switch between exact values and decimals");
            Assert.True(cv.ShowsDecimals);
            var texts = cv.GetLogicalDescendantsOfType<MatrixView>().First().Cells!.SelectMany(r => r).ToList();
            Assert.Contains("1.5", texts);
        });
    }

    [Fact]
    public void State_is_saved_and_restored()
    {
        Headless.Run(() =>
        {
            var (view, w, s) = Open();
            view.FocusFirst();
            Type(w, "7");
            Wait(view.RunOperationAsync(MatrixOperation.Determinant));
            view.Save();
            var state = s.Prefs.GetModeState<MatrixModeState>("matrix");
            Assert.NotNull(state);
            Assert.Single(state!.Cards);

            var fresh = Headless.CreateServices("nes");
            fresh.Prefs.ModeState["matrix"] = s.Prefs.ModeState["matrix"];
            var (restored, _, _) = Open(services: fresh);
            Assert.Equal("7", restored.Library.Find("A")![0, 0]);
            Assert.Single(restored.Cards);
            Assert.Equal("DETERMINANT", restored.Cards[0].Title);
            Assert.True(fresh.Session.Env.Variables.ContainsKey("A"));
        });
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("amber")]
    public void First_run_screen(string scheme)
    {
        Headless.Run(() =>
        {
            var (view, w, _) = Open(scheme);
            view.FocusFirst();
            Headless.Pump();
            // The default library from the design: A (3×3), B (3×1) and a 2×2 matrix; no cards yet, so a welcome card.
            Assert.Equal(3, view.Library.Entries.Count);
            Assert.Empty(view.Cards);
            var scroll = view.Editor.Parent as ScrollViewer;
            Assert.NotNull(scroll);
            // At the app's mode height a 3×3 grid fits without scrolling.
            Assert.True(scroll!.Extent.Height <= scroll.Viewport.Height + 0.5, $"extent {scroll.Extent.Height} viewport {scroll.Viewport.Height}");
            Headless.Save(w, $"matrix-start-{scheme}.png");
        });
    }

    [Fact]
    public void Mode_metadata()
    {
        var m = new MatrixMode();
        Assert.Equal("matrix", m.Id);
        Assert.Equal("Matrix", m.Title);
        Assert.Equal("Edit matrices, det, inverse, eigen", m.Description);
        Assert.Equal("Ctrl+6", m.Shortcut);
        Assert.Equal("Head", m.AccentToken);
        Assert.Equal("matrix", m.Icon);
    }
}

internal static class LogicalTreeHelpers
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root) where T : class =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
