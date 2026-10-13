using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Calcy.Modes.Code.Programs;
using Calcy.UI.TestKit;
using Xunit.Abstractions;

namespace Calcy.Modes.Code.Tests;

/// <summary>Screenshots of the screen's other states (input prompt, REPL, graph, variables, find), in the default scheme.</summary>
public class FeatureScreenshotTests(ITestOutputHelper output)
{
    [Fact]
    public void InputPromptAndRepl()
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create("nes");
            view.OpenExample(Examples.Find("quadratic.py")!);
            var task = view.RunCurrentAsync();
            Assert.True(TestSupport.WaitUntil(() => view.Cards.First().PendingInput is not null));
            w.KeyTextInput("2");
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(TestSupport.WaitUntil(() => view.Cards.First().PendingInput is not null && view.Cards.First().ConsoleText.Contains("b [")));
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(TestSupport.WaitUntil(() => view.Cards.First().ConsoleText.Contains("c [")));
            Headless.Pump();
            Headless.Save(w, "code-input.png");
            w.KeyTextInput("-5");
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(TestSupport.WaitUntil(() => task.IsCompleted));
            var r = view.SubmitReplAsync("calc.eval('integrate(x^2, x, 0, 3) + 1/3')");
            Assert.True(TestSupport.WaitUntil(() => r.IsCompleted));
            var r2 = view.SubmitReplAsync("[n for n in range(10) if n % 3 == 0]");
            Assert.True(TestSupport.WaitUntil(() => r2.IsCompleted));
            return Headless.Save(w, "code-repl.png");
        });
        output.WriteLine(path);
    }

    [Fact]
    public void GraphAndVariables()
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create("nes");
            view.OpenExample(Examples.Find("wave.py")!);
            TestSupport.Run(view);
            TestSupport.WaitUntil(() => !view.GraphCanvas.IsBusy);
            Headless.Save(w, "code-graph.png");
            view.ShowSide(SidePanel.Variables);
            return Headless.Save(w, "code-vars.png");
        });
        output.WriteLine(path);
    }

    [Fact]
    public void FindBarAndCanvasExamples()
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create("pico");
            view.OpenExample(Examples.Find("house.py")!);
            TestSupport.Run(view);
            view.Editor.FocusEditor();
            view.ShowFind();
            Headless.Pump();
            w.KeyTextInput("fill");
            Headless.Pump();
            Headless.Save(w, "code-find.png");
            view.HideFind();
            view.OpenExample(Examples.Find("sierpinski.py")!);
            TestSupport.Run(view);
            Headless.Save(w, "code-casioplot.png");
            view.OpenExample(Examples.Find("scatter.py")!);
            TestSupport.Run(view);
            Headless.Save(w, "code-scatter.png");
            view.OpenExample(Examples.Find("projectile.py")!);
            TestSupport.Run(view);
            return Headless.Save(w, "code-projectile.png");
        });
        output.WriteLine(path);
    }

    [Fact]
    public void TinyWindow()
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create("synth", 560, 900);
            view.Open("star.py");
            TestSupport.Run(view);
            return Headless.Save(w, "code-synth-tiny.png");
        });
        output.WriteLine(path);
    }
}

public class FlyoutScreenshotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("nes")]
    [InlineData("amber")]
    public void FilesAndReferencePanels(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create(scheme);
            var files = view.BuildFilesPanel(new Button());
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,24,Auto"), Margin = new Avalonia.Thickness(16) };
            grid.Children.Add(files);
            var reference = CodeView.BuildReferencePanel();
            Grid.SetColumn(reference, 2);
            grid.Children.Add(reference);
            var w = Headless.Show(grid, 1100, 620);
            return Headless.Save(w, $"code-{scheme}-panels.png");
        });
        output.WriteLine(path);
    }
}
