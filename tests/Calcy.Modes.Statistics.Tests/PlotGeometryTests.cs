using Calcy.Modes.Statistics.Logic;
using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Tests;

/// <summary>Histogram binning, box plot whiskers and outliers, dot plots, normal probability plots and bar charts.</summary>
public class PlotGeometryTests
{
    [Fact]
    public void Values_on_a_bin_edge_go_to_the_bin_on_the_right()
    {
        var h = Histogram.Bin([0, 0.1, 0.2, 0.3, 0.35], null, 0.1, 0);
        Assert.Equal(4, h.Bins.Count);
        Assert.Equal([1.0, 1, 1, 2], h.Bins.Select(b => b.Count));
        Assert.Equal(0.3, h.Bins[3].Lower);
        Assert.Equal(0.4, h.Bins[3].Upper);
    }

    [Fact]
    public void The_maximum_on_an_edge_gets_its_own_bin_like_the_ti84()
    {
        var h = Histogram.Bin([0, 2, 5, 9, 10], null, 5);
        Assert.Equal(0, h.Start);
        Assert.Equal([2.0, 2, 1], h.Bins.Select(b => b.Count));
        Assert.Equal(15, h.Bins[^1].Upper);
        Assert.Equal(5, h.Total);
    }

    [Fact]
    public void Frequencies_start_and_values_below_the_start()
    {
        var h = Histogram.Bin([1, 2, 3, 4], [2, 1, 0, 5], 2, 2);
        Assert.Equal(2, h.Start);
        Assert.Equal([1.0, 5], h.Bins.Select(b => b.Count));
        Assert.Equal(2, h.LeftOut);
        Assert.Equal(-2, Histogram.AutoStart(-1.5, 2));
        Assert.Throws<StatDataException>(() => Histogram.Bin([1, 2], null, 0));
        Assert.Throws<StatDataException>(() => Histogram.Bin([0, 1e6], null, 1));
    }

    [Fact]
    public void Automatic_bin_width_is_a_round_number()
    {
        // Sturges: 5 bins for 10 values; 7 / 5 = 1.4 is closest to 1 and 32 / 5 = 6.4 closest to 5.
        Assert.Equal(1, Histogram.AutoWidth([1, 2, 2, 3, 4, 5, 5, 6, 7, 8], 10));
        Assert.Equal(1, Histogram.AutoWidth([1, 2, 3], 3));
        Assert.Equal(5, Histogram.AutoWidth([52, 55, 60, 61, 66, 70, 72, 75, 80, 84], 10));
        Assert.Equal(0.5, Histogram.AutoWidth([0.1, 0.4, 0.9, 1.3, 1.8], 5));
        Assert.Equal(2.5, Histogram.Nice(2.2));
    }

    [Fact]
    public void Modified_box_plot_uses_ti_quartiles_and_marks_outliers()
    {
        double[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 30];
        var b = BoxPlot.Compute("L1", data);
        Assert.Equal(3, b.Q1);
        Assert.Equal(5.5, b.Median);
        Assert.Equal(8, b.Q3);
        Assert.Equal(-4.5, b.LowerFence);
        Assert.Equal(15.5, b.UpperFence);
        Assert.Equal(1, b.LowerWhisker);
        Assert.Equal(9, b.UpperWhisker);
        Assert.Equal([30.0], b.Outliers);

        var plain = BoxPlot.Compute("L1", data, modified: false);
        Assert.Equal(30, plain.UpperWhisker);
        Assert.Empty(plain.Outliers);

        // Odd n: the TI method leaves the median out of both halves.
        var odd = BoxPlot.Compute("L1", [1, 2, 3, 4, 5, 6, 7]);
        Assert.Equal((2.0, 4.0, 6.0), (odd.Q1, odd.Median, odd.Q3));
        var tukey = BoxPlot.Compute("L1", [1, 2, 3, 4, 5, 6, 7], method: QuartileMethod.TukeyHinges);
        Assert.Equal((2.5, 5.5), (tukey.Q1, tukey.Q3));
    }

    [Fact]
    public void Box_scene_has_whiskers_box_median_and_outlier_markers()
    {
        var b = BoxPlot.Compute("L1", [1, 2, 3, 4, 5, 6, 7, 8, 9, 30]);
        var scene = ChartBuilder.BoxScene([b], "L1");
        var box = Assert.Single(scene.Rects);
        Assert.Equal((3.0, 8.0), (box.X0, box.X1));
        Assert.Contains(scene.Segments, s => s.X0 == 5.5 && s.X1 == 5.5);
        Assert.Contains(scene.Segments, s => s.X0 == 8 && s.X1 == 9);
        Assert.Equal(30, Assert.Single(scene.Markers).X);
        Assert.True(scene.XMin < 1 && scene.XMax > 30);
        Assert.False(scene.ShowYAxis);
    }

    [Fact]
    public void Histogram_scene_ticks_follow_the_bin_edges()
    {
        var h = Histogram.Bin([52, 55, 60, 61, 66, 70, 72, 75, 80, 84], null, 5);
        var scene = ChartBuilder.HistogramScene(h, "L2 score");
        Assert.Equal(h.Bins.Count(b => b.Count > 0), scene.Rects.Count);
        Assert.Equal(50, scene.XTicks![0].Value);
        Assert.Equal("50", scene.XTicks[0].Label);
        Assert.True(scene.IntegerY);
        Assert.True(scene.YMax > h.Bins.Max(b => b.Count));
    }

    [Fact]
    public void Dot_plot_stacks_one_dot_per_value()
    {
        var (res, stacks) = DotPlot.Stack([1, 2, 2, 3, 4, 5, 5, 6, 7, 8]);
        Assert.Equal(1, res);
        Assert.Equal(2, stacks.First(s => s.X == 2).Count);
        Assert.Equal(10, stacks.Sum(s => s.Count));
        var scene = ChartBuilder.DotScene(res, stacks, "L1");
        Assert.Equal(10, scene.Markers.Count);
        Assert.Contains(scene.Markers, m => m.X == 5 && m.Y == 1.5);
        var (r2, s2) = DotPlot.Stack([0.12, 0.14, 0.5, 0.9]);
        Assert.True(r2 < 1);
        Assert.All(s2, s => Assert.Equal(0, Math.Round(s.X / r2) * r2 - s.X, 9));
    }

    [Fact]
    public void Normal_probability_scores_are_symmetric()
    {
        var (x, z, mean, sd) = NormalProbability.Compute([5, 1, 3, 2, 4]);
        Assert.Equal([1.0, 2, 3, 4, 5], x);
        Assert.Equal(0, z[2], 12);
        Assert.Equal(-z[0], z[4], 12);
        Assert.Equal(-1.17976, z[0], 4);
        Assert.Equal(3, mean);
        Assert.Equal(Math.Sqrt(2.5), sd, 12);
    }

    [Fact]
    public void Bar_chart_counts_categories()
    {
        var t = new StatTable(TestData.Session());
        t.AddColumn("L1");
        t.SetCells("L1", ["red", "blue", "red", "green", "red", "", "blue"]);
        var cats = DataSet.From(t).Categories("L1");
        Assert.Equal(["red", "blue", "green"], cats.Select(c => c.Label));
        Assert.Equal([3.0, 2, 1], cats.Select(c => c.Count));
        var scene = ChartBuilder.BarScene(cats, "L1");
        Assert.Equal(3, scene.Rects.Count);
        Assert.Equal("red", scene.XTicks![0].Label);
        var numbers = DataSet.Of(("L1", [3, 1, 3, 2])).Categories("L1");
        Assert.Equal(["1", "2", "3"], numbers.Select(c => c.Label));
    }

    [Fact]
    public void Plots_tab_builds_every_kind_with_a_card()
    {
        var data = TestData.DesignData();
        foreach (var (kind, _, _) in Plots.Kinds)
        {
            var o = Plots.Build(data, new PlotSettings { Kind = kind, List = "L1", List2 = "L2" });
            Assert.True(o.Error is null, $"{kind}: {o.Error}");
            Assert.True(o.Scene is not null || o.Graph is not null, kind.ToString());
            Assert.False(o.Card.IsError);
            Assert.Equal(StatTab.Plots, o.Card.Recipe!.Tab);
            Assert.NotEmpty(o.Summary);
            if (o.Card.UseText is { } use) TestData.EvaluateUse(use);
        }
        var hist = Plots.Build(data, new PlotSettings { Kind = PlotKind.Histogram, List = "L2", BinWidth = "10" });
        Assert.Equal(10, hist.Histogram!.Width);
        Assert.Equal(50, hist.Histogram.Start);
        var bad = Plots.Build(data, new PlotSettings { Kind = PlotKind.Histogram, List = "L2", BinWidth = "-1" });
        Assert.NotNull(bad.Error);
    }
}
