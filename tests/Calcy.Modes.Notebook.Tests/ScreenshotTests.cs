using Avalonia.Controls;
using Avalonia.VisualTree;
using Calcy.Modes.Notebook.Views;
using Calcy.UI.TestKit;
using Xunit.Abstractions;

namespace Calcy.Modes.Notebook.Tests;

/// <summary>Screenshots of the Notebook screen in the colour schemes, at the default size and at the smallest mode area.</summary>
public class ScreenshotTests(ITestOutputHelper output)
{
    public static readonly TheoryData<string> Schemes = new() { "nes", "pico", "gameboy", "amber", "paper" };

    [Theory]
    [MemberData(nameof(Schemes))]
    public void PendulumLab(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create(scheme);
            TestSupport.Await(view.RunAllAsync());
            Assert.True(TestSupport.WaitIdle(view));
            Assert.False(view.IsNarrow);
            var top = Headless.Save(w, $"notebook-{scheme}.png");
            view.ScrollBy(10_000);
            TestSupport.WaitIdle(view);
            return top + "\n" + Headless.Save(w, $"notebook-{scheme}-end.png");
        });
        output.WriteLine(path);
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void SmallestModeArea(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create(scheme, 900, 510);
            TestSupport.Await(view.RunAllAsync());
            Assert.True(TestSupport.WaitIdle(view));
            Assert.False(view.IsNarrow);
            var doc = view.GetVisualDescendants().OfType<Border>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Notebook");
            Assert.True(doc.Bounds.Right <= 900 - 16 + 1, $"the document reaches {doc.Bounds.Right}");
            TestSupport.AssertInside(doc);
            var sidebar = view.GetVisualDescendants().OfType<Border>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Notebooks and outline");
            TestSupport.AssertInside(sidebar);
            return Headless.Save(w, $"notebook-{scheme}-small.png");
        });
        output.WriteLine(path);
    }

    [Fact]
    public void SelectedCodeCellWhileEditing()
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create("nes");
            TestSupport.Await(view.RunAllAsync());
            TestSupport.WaitIdle(view);
            var code = (CodeCellView)view.CellViews[4];
            view.ScrollTo(view.CellViews[3]);
            code.StartEditing();
            TestSupport.WaitIdle(view);
            return Headless.Save(w, "notebook-nes-editing.png");
        });
        output.WriteLine(path);
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("amber")]
    public void EveryKindOfOutput(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create(scheme);
            var doc = view.Library.Create("Features");
            TestSupport.WaitIdle(view);
            ((Model.TextCell)doc.Cells[0]).Text = """
                # Features
                - **Bold**, *italic* and `code`, a [link](https://example.org)
                - inline maths $x^2 + 1$ and a live value $=a*b$
                > A quote works as a callout.
                """;
            var maths = (Model.MathCell)doc.Cells[1];
            maths.Lines[0].Text = "a = 3";
            maths.AddLine("b = a/4");
            maths.AddLine("solve(x^2 = a, x)");
            maths.AddLine("2 +");
            var graph = doc.Add(new Model.GraphCell(["y = k*sin(x)", "y = b*x"]) { Caption = "Waves" });
            var code = doc.Add(new Model.CodeCell("""
                import calc, ti_draw
                ti_draw.set_color(255, 0, 0)
                ti_draw.fill_circle(60, 60, 40)
                calc.graph.add("y = x^3 - a*x")
                calc.show("sqrt(a)", "root")
                print(undefined_name)
                """));
            TestSupport.WaitIdle(view);
            TestSupport.Await(view.RunCodeAsync(code));
            TestSupport.WaitIdle(view);
            var graphView = view.CellViews.Single(v => v.Cell == graph);
            view.Select(graphView, focus: false);
            TestSupport.WaitIdle(view);
            var top = Headless.Save(w, $"notebook-{scheme}-features.png");
            view.ScrollTo(graphView);
            TestSupport.WaitIdle(view);
            top += "\n" + Headless.Save(w, $"notebook-{scheme}-features-mid.png");
            view.ScrollBy(10_000);
            TestSupport.WaitIdle(view);
            return top + "\n" + Headless.Save(w, $"notebook-{scheme}-features-end.png");
        });
        output.WriteLine(path);
    }

    [Fact]
    public void SelectedTextAndMathsCells()
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create("pico");
            view.Select(view.CellViews[1], focus: false);
            TestSupport.WaitIdle(view);
            var maths = Headless.Save(w, "notebook-pico-maths-selected.png");
            var text = (TextCellView)view.CellViews[0];
            view.Select(text, focus: false);
            text.StartEditing();
            TestSupport.WaitIdle(view);
            return maths + "\n" + Headless.Save(w, "notebook-pico-text-editing.png");
        });
        output.WriteLine(path);
    }

    [Fact]
    public void NarrowerThanTheModeAreaFoldsTheSidebar()
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create("paper", 700, 600);
            Assert.True(view.IsNarrow);
            var doc = view.GetVisualDescendants().OfType<Border>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Notebook");
            TestSupport.AssertInside(doc);
            return Headless.Save(w, "notebook-paper-narrow.png");
        });
        output.WriteLine(path);
    }
}
