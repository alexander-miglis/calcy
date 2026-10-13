using Avalonia.Controls;
using Avalonia.LogicalTree;
using Calcy.Graphing;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.TestKit;
using Xunit.Abstractions;

namespace Calcy.Modes.Graphing.Tests;

public class GraphingScreenTests(ITestOutputHelper output)
{
    private static readonly string[] DesignExpressions = ["y = x² − 2", "y = a·sin(x)", "y < x/2", "x² + y² = 9", "A = (2, 2)"];

    /// <summary>Builds the screen as on the design board: five expressions, a = 1.5, y1 selected, its minimum pinned, two analysis cards.</summary>
    private static (Window Window, GraphingView View) Design(string scheme, AppServices? services = null)
    {
        var s = services ?? Headless.CreateServices(scheme);
        var view = (GraphingView)new GraphingMode().CreateView(s);
        var w = Headless.Show(view);
        foreach (var e in DesignExpressions) view.AddExpression(e);
        view.Document.Parameters["a"]!.Value = 1.5;
        Headless.Pump();
        var size = view.Canvas.Bounds.Size;
        view.Canvas.Viewport = new Viewport(-6, 6, -4.5, 4.5).Square(size.Width, size.Height);
        view.Select(1);
        view.AddAnalysis(AnalysisTool.Slope, "0");
        view.Select(0);
        view.AddAnalysis(AnalysisTool.Integral, "-1", "2");
        TestSupport.WaitWhile(() => view.IsBusy);
        view.Canvas.SelectedPoint = view.Canvas.PointsOfInterest.First(p => p.Kind == PoiKind.Minimum && p.Item == view.Document.Items[0]);
        TestSupport.WaitWhile(() => view.IsBusy);
        return (w, view);
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("pico")]
    [InlineData("gameboy")]
    [InlineData("amber")]
    [InlineData("paper")]
    public void DesignScreenshot(string scheme)
    {
        TestSupport.EnsureSkia();
        var path = Headless.Run(() =>
        {
            var (w, view) = Design(scheme);
            Assert.Equal(5, view.Document.Items.Count);
            Assert.Equal([GraphKind.Function, GraphKind.Function, GraphKind.InequalityY, GraphKind.Implicit, GraphKind.Point], view.Document.Items.Select(i => i.Kind));
            return Headless.Save(w, $"graphing-{scheme}.png");
        });
        output.WriteLine(path);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(GraphViewMode.Table, "graphing-table.png")]
    [InlineData(GraphViewMode.Split, "graphing-split.png")]
    public void TableAndSplitViews(GraphViewMode mode, string file)
    {
        TestSupport.EnsureSkia();
        var path = Headless.Run(() =>
        {
            var (w, view) = Design("nes");
            view.SetView(mode);
            TestSupport.WaitWhile(() => view.IsBusy);
            Assert.Equal(mode, view.ViewMode);
            return Headless.Save(w, file);
        });
        output.WriteLine(path);
    }

    [Fact]
    public void ModeMetadata()
    {
        var m = new GraphingMode();
        Assert.Equal("graphing", m.Id);
        Assert.Equal("Graphing", m.Title);
        Assert.Equal("Plot, trace, roots, areas, sliders", m.Description);
        Assert.Equal("Ctrl+3", m.Shortcut);
        Assert.Equal("G2", m.AccentToken);
        Assert.Equal("graph", m.Icon);
    }

    [Fact]
    public void AnalysisCardsAndPointsOfInterest()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (_, view) = Design("nes");
            var pois = view.Canvas.PointsOfInterest.Where(p => p.Item == view.Document.Items[0] || p.Other == view.Document.Items[0]).ToList();
            Assert.Equal(2, pois.Count(p => p.Kind == PoiKind.Root));
            Assert.Single(pois, p => p.Kind == PoiKind.Minimum);
            Assert.Equal(2, pois.Count(p => p.Kind == PoiKind.Intersection && p.Other?.Kind == GraphKind.Function));
            Assert.Contains(view.Results, r => r.Text.StartsWith("= −3"));
            Assert.Contains(view.Results, r => r.Text.StartsWith("= a = 3/2"));
            var cards = view.GetLogicalDescendants().OfType<Card>().Select(c => c.Title).ToList();
            Assert.Contains("Minimum of y1", cards);
            Assert.Contains("Root of y1", cards);
        });
    }

    [Fact]
    public void PlotRequestAddsExpressions()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var s = Headless.CreateServices("nes");
            var view = new GraphingMode().CreateView(s);
            Headless.Show(view);
            Assert.IsAssignableFrom<IReceivesPayload>(view);
            ((IReceivesPayload)view).Receive(new PlotRequest(["y = x^3", "y = 2x"]));
            ((IReceivesPayload)view).Receive(new PlotRequest(["y = x^3"]));
            var g = (GraphingView)view;
            Assert.Equal(["y = x^3", "y = 2x"], g.Document.Items.Select(i => i.Text));
            Assert.Equal(GraphKind.Function, g.Document.Items[0].Kind);
        });
    }

    [Fact]
    public void ErrorsShowInline()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var s = Headless.CreateServices("nes");
            var view = (GraphingView)new GraphingMode().CreateView(s);
            var w = Headless.Show(view);
            view.AddExpression("y = (x + ");
            view.AddExpression("y = sqrt(x)");
            TestSupport.WaitWhile(() => view.IsBusy);
            Assert.Equal(GraphKind.Error, view.Document.Items[0].Kind);
            List<string?> VisibleTexts() => [.. view.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text)];
            static bool ShowsMissing(List<string?> texts) => texts.Any(t => t is not null && t.Contains("missing", StringComparison.OrdinalIgnoreCase));
            // A row's error can wait for its typing pause (700 ms) before it shows; busy machines get there later.
            TestSupport.WaitWhile(() => !ShowsMissing(VisibleTexts()), 5000);
            var texts = VisibleTexts();
            Assert.True(ShowsMissing(texts), "Visible text: " + string.Join(" | ", texts));
            Headless.Save(w, "graphing-error.png");
        });
    }

    [Fact]
    public void StateIsSavedAndRestored()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var s = Headless.CreateServices("nes");
            var (_, view) = Design("nes", s);
            view.Document.Items[3].IsVisible = false;
            view.SetView(GraphViewMode.Split);
            TestSupport.WaitWhile(() => view.IsBusy);
            Assert.True(s.Prefs.ModeState.ContainsKey("graphing"));
            var again = (GraphingView)new GraphingMode().CreateView(s);
            Assert.Equal(view.Document.Items.Select(i => i.Text), again.Document.Items.Select(i => i.Text));
            Assert.False(again.Document.Items[3].IsVisible);
            Assert.Equal(1.5, again.Document.Parameters["a"]!.Value);
            Assert.Equal(GraphViewMode.Split, again.ViewMode);
            Assert.Equal(view.Canvas.Viewport.XMin, again.Canvas.Viewport.XMin, 9);
        });
    }

    [Fact]
    public void SliderAnimationAdvancesTheParameter()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var s = Headless.CreateServices("nes");
            var view = (GraphingView)new GraphingMode().CreateView(s);
            Headless.Show(view);
            view.AddExpression("y = k*x");
            var k = view.Document.Parameters["k"]!;
            double before = k.Value;
            k.IsPlaying = true;
            view.AnimationStep();
            Assert.Equal(before + k.Step, k.Value, 9);
            var slider = view.GetLogicalDescendants().OfType<Controls.PixelSlider>().Single();
            Assert.Equal(k.Value, slider.Value, 9);
            slider.Value = 2;
            Assert.Equal(2, k.Value, 9);
        });
    }

    [Fact]
    public void DefinitionRowsDefineFunctions()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var s = Headless.CreateServices("nes");
            var view = (GraphingView)new GraphingMode().CreateView(s);
            Headless.Show(view);
            view.AddExpression("f(x) = x^2 - 1");
            Assert.True(s.Session.Env.TryGetFunction("f", out _));
            view.AddExpression("y = f(x) + 3");
            Assert.Equal(GraphKind.Function, view.Document.Items[1].Kind);
            Assert.Empty(view.Document.Items[1].Parameters);
        });
    }
}
