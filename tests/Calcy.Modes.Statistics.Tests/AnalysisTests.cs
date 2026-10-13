using Calcy.Core.Expressions;
using Calcy.Graphing;
using Calcy.Modes.Statistics.Logic;
using Calcy.Statistics;
using static Calcy.Modes.Statistics.Tests.TestData;

namespace Calcy.Modes.Statistics.Tests;

/// <summary>Regression cards, tests with their decisions and pictures, and the distribution explorer.</summary>
public class AnalysisTests
{
    [Fact]
    public void Linear_regression_card_has_the_equation_r_and_the_calculator_expression()
    {
        var o = Regressions.Fit(DesignData(), new RegressionSettings { X = "L1", Y = "L2" });
        Assert.Null(o.Error);
        Assert.Equal(4.54261954261954, o.Parameters[0].Value, 10);
        Assert.Equal(47.966735966736, o.Parameters[1].Value, 9);
        Assert.Equal(0.992066881882608, o.R!.Value, 12);
        Assert.Equal(0.98419669812828, o.RSquared, 12);
        Assert.Equal("y = 4.54262x + 47.9667", o.DisplayEquation);
        Assert.StartsWith("y = 4.54261954261954*x + 47.966735966736", o.Equation);

        var card = o.Card;
        Assert.Equal("LINREG AX+B L1,L2", card.Title);
        Assert.Equal("y = 4.54262x + 47.9667", card.Headline);
        Assert.Equal("linreg(L1, L2)", card.UseText);
        Assert.Contains(card.Lines, l => l.Label == "r" && l.Value == "0.9920668819");
        Assert.Contains(card.Lines, l => l.Label == "r²" && l.Value == "0.9841966981");
        Assert.Contains("4.54262", card.Latex);
        Assert.StartsWith("y = ", card.Latex);
        Assert.Contains("y = 4.54261954261954*x", card.CopyText);
        Assert.Equal(StatTab.Regression, card.Recipe!.Tab);
        Assert.Equal(["a (slope)", "b (intercept)", "r", "r²"], o.Tiles.Select(t => t.Label));
        Assert.Equal("4.54262", o.Tiles[0].Value);
        var r = EvaluateUse(card.UseText!);
        Assert.Contains("4.54261954", Calcy.Core.Printing.ExprPrinter.ToInput(r.Exact!));

        // The plot: the points and the fitted line, both parse as graph items.
        var s = Session();
        Assert.Equal(2, o.Graph!.Lines.Count);
        Assert.All(o.Graph.Lines, l => Assert.True(GraphParser.Parse(l.Text, s, false).IsPlottable, l.Text));
        Assert.Equal(10, o.Residuals.Count);
        Assert.Equal(52 - (4.54261954261954 + 47.966735966736), o.Residuals[0], 9);
        Assert.Equal(4.54261954261954 * 4.5 + 47.966735966736, o.Predict!(4.5), 9);
    }

    [Theory]
    [InlineData(RegressionKind.LinearABx, "linregabx(L1, L2)")]
    [InlineData(RegressionKind.MedianMedian, "medmed(L1, L2)")]
    [InlineData(RegressionKind.Quadratic, "quadreg(L1, L2)")]
    [InlineData(RegressionKind.Cubic, "cubicreg(L1, L2)")]
    [InlineData(RegressionKind.Quartic, "quartreg(L1, L2)")]
    [InlineData(RegressionKind.Polynomial, "polyreg(L1, L2, 2)")]
    [InlineData(RegressionKind.Logarithmic, "lnreg(L1, L2)")]
    [InlineData(RegressionKind.Exponential, "expreg(L1, L2)")]
    [InlineData(RegressionKind.ExponentialE, "expereg(L1, L2)")]
    [InlineData(RegressionKind.Power, "pwrreg(L1, L2)")]
    [InlineData(RegressionKind.Logistic, "logisticreg(L1, L2)")]
    public void Every_model_fits_the_design_data(RegressionKind kind, string use)
    {
        var o = Regressions.Fit(DesignData(), new RegressionSettings { X = "L1", Y = "L2", Model = kind });
        Assert.True(o.Error is null, o.Error);
        Assert.Equal(use, o.Card.UseText);
        Assert.True(o.RSquared > 0.9, o.RSquared.ToString());
        EvaluateUse(use);
        var s = Session();
        var item = GraphParser.Parse(o.Graph!.Lines[1].Text, s, false);
        Assert.True(item.IsFunction, o.Graph.Lines[1].Text + " " + item.Error);
        Assert.Equal(o.Predict!(3.5), item.Members[0].Main!.Eval(3.5, []), 6);
    }

    [Fact]
    public void Sinusoidal_and_custom_models()
    {
        double[] x = [.. Enumerable.Range(0, 24).Select(i => i * 0.5)];
        double[] y = [.. x.Select(v => 3 * Math.Sin(0.8 * v + 0.5) + 2)];
        var data = DataSet.Of(("L1", x), ("L2", y));
        var sin = Regressions.Fit(data, new RegressionSettings { Model = RegressionKind.Sinusoidal });
        Assert.Null(sin.Error);
        Assert.True(sin.RSquared > 0.999);
        double[] gx = [0, 1, 2, 3, 4, 5];
        double[] gy = [.. gx.Select(v => 2 * Math.Exp(0.3 * v) + 1)];
        var custom = Regressions.Fit(DataSet.Of(("L1", gx), ("L2", gy)),
            new RegressionSettings { Model = RegressionKind.Custom, Custom = "a*e^(b*x) + c", CustomStart = "b = 0.1" }, Session());
        Assert.True(custom.Error is null, custom.Error);
        Assert.Equal(2, custom.Parameters.First(p => p.Name == "a").Value, 4);
        Assert.Equal(0.3, custom.Parameters.First(p => p.Name == "b").Value, 4);
        Assert.Equal(1, custom.Parameters.First(p => p.Name == "c").Value, 4);
        Assert.StartsWith("y = ", custom.Card.UseText);
        Assert.True(GraphParser.Parse(custom.Graph!.Lines[1].Text, Session(), false).IsFunction);
        var none = Regressions.Fit(DesignData(), new RegressionSettings { Model = RegressionKind.Custom, Custom = "3*x" }, Session());
        Assert.Contains("no parameters", none.Error);
    }

    [Fact]
    public void Regression_needs_two_lists_with_numbers()
    {
        var o = Regressions.Fit(DesignData(), new RegressionSettings { X = "L1", Y = "L9" });
        Assert.Contains("no list called L9", o.Error);
        Assert.True(o.Card.IsError);
        var residuals = Regressions.Fit(DesignData(), new RegressionSettings { Residuals = true });
        Assert.Single(residuals.Graph!.Lines);
        Assert.True(residuals.Graph.Viewport.YMin < 0 && residuals.Graph.Viewport.YMax > 0);
    }

    [Fact]
    public void T_test_on_the_scores_keeps_h0_and_says_so_in_plain_words()
    {
        var settings = new TestSettings { Kind = TestKind.TTest, Fields = { ["mu0"] = "65", ["list"] = "L2" } };
        var o = TestRunner.Run(settings, DesignData());
        Assert.Null(o.Error);
        Assert.Equal(0.746832677439348, o.Test!.Statistic, 12);
        Assert.Equal(0.47422199622133, o.Test.PValue, 12);
        Assert.False(o.Rejects);
        Assert.Equal("Keep H₀", o.Decision);
        Assert.Equal("H₀: μ = 65   H₁: μ ≠ 65", o.Hypotheses);
        Assert.Equal("p = 0.4742 is above α = 0.05, so do not reject H₀: the data do not give enough evidence that the mean of L2 differs from 65.", o.Sentence);
        Assert.Equal(["t", "p", "df", "at α = 0.05"], o.Tiles.Take(4).Select(t => t.Label));
        Assert.Equal("9", o.Tiles[2].Value);
        Assert.Equal("ttest(65, L2)", o.UseText);
        Assert.Equal("T-TEST L2", o.Card.Title);
        Assert.Equal(o.Sentence, o.Card.Note);
        EvaluateUse(o.UseText!);

        // The rejection region: both tails beyond ±t(0.025, 9) = ±2.262157, shaded in the second graph colour.
        var shades = o.Graph!.Annotations.Where(a => a.Kind == AnnotationKind.Shade).DistinctBy(a => a.Points).ToList();
        Assert.Equal(2, shades.Count);
        Assert.All(shades, a => Assert.Equal(1, a.ColorIndex));
        Assert.Equal(-2.262157, shades[0].Points[^1].X, 5);
        Assert.Equal(2.262157, shades[1].Points[0].X, 5);
        Assert.Contains(o.Graph.Annotations, a => a.Kind == AnnotationKind.Path && Math.Abs(a.Points[0].X - 0.746832677) < 1e-6);
        Assert.True(GraphParser.Parse(o.Graph.Lines[0].Text, Session(), false).IsFunction);
    }

    [Fact]
    public void One_sided_t_test_rejects_and_shades_one_tail()
    {
        var settings = new TestSettings { Kind = TestKind.TTest, Alternative = Alternative.Greater, Fields = { ["mu0"] = "55", ["list"] = "L2" } };
        var o = TestRunner.Run(settings, DesignData());
        Assert.True(o.Rejects);
        Assert.Equal("Reject H₀", o.Decision);
        Assert.Contains("so reject H₀: the data give evidence that the mean of L2 is greater than 55.", o.Sentence);
        Assert.Equal("ttest(55, L2, \">\")", o.UseText);
        var shade = Assert.Single(o.Graph!.Annotations.Where(a => a.Kind == AnnotationKind.Shade).DistinctBy(a => a.Points));
        Assert.Equal(1.833113, shade.Points[0].X, 5);
        Assert.True(o.Tiles[3].Highlight);
        EvaluateUse(o.UseText!);
    }

    [Fact]
    public void Summary_statistics_input_and_field_mapping()
    {
        var fields = TestCatalog.Fields(TestKind.TTest, stats: true).Select(f => f.Key);
        Assert.Equal(["mu0", "xbar", "sx", "n"], fields);
        Assert.Equal(["mu0", "list", "freq"], TestCatalog.Fields(TestKind.TTest, stats: false).Select(f => f.Key));
        Assert.Equal(["list1", "list2", "mu0"], TestCatalog.Fields(TestKind.PairedTTest, stats: true).Select(f => f.Key));
        var s = new TestSettings { Kind = TestKind.TwoSampleTTest };
        var list2 = TestCatalog.Fields(TestKind.TwoSampleTTest, false).First(f => f.Key == "list2");
        Assert.Equal("L2", TestCatalog.Value(s, list2, ["L1", "L2", "L3"]));
        s.Fields["list2"] = "L9";
        Assert.Equal("L2", TestCatalog.Value(s, list2, ["L1", "L2", "L3"]));

        var stats = new TestSettings { Kind = TestKind.TTest, Stats = true, Fields = { ["mu0"] = "65", ["xbar"] = "67.5", ["sx"] = "10.5856296721336", ["n"] = "10" } };
        var o = TestRunner.Run(stats, DesignData());
        Assert.Equal(0.746832677439348, o.Test!.Statistic, 9);
        Assert.Equal("ttest(65, 67.5, 10.5856296721336, 10)", o.UseText);
        Assert.Equal("H₀: μ = 65   H₁: μ ≠ 65", o.Hypotheses);
        var empty = TestRunner.Run(new TestSettings { Kind = TestKind.TTest, Stats = true, Fields = { ["xbar"] = "" } }, DesignData());
        Assert.Contains("Type a number for x̄", empty.Error);
    }

    [Theory]
    [InlineData(TestKind.ZTest)]
    [InlineData(TestKind.TwoSampleZTest)]
    [InlineData(TestKind.TwoSampleTTest)]
    [InlineData(TestKind.PairedTTest)]
    [InlineData(TestKind.OnePropZTest)]
    [InlineData(TestKind.TwoPropZTest)]
    [InlineData(TestKind.TwoSampleFTest)]
    [InlineData(TestKind.LinRegTTest)]
    [InlineData(TestKind.Anova)]
    [InlineData(TestKind.ChiSquareTwoWay)]
    [InlineData(TestKind.ZInterval)]
    [InlineData(TestKind.TInterval)]
    [InlineData(TestKind.TwoSampleZInterval)]
    [InlineData(TestKind.TwoSampleTInterval)]
    [InlineData(TestKind.OnePropZInterval)]
    [InlineData(TestKind.TwoPropZInterval)]
    [InlineData(TestKind.LinRegTInterval)]
    [InlineData(TestKind.SignTest)]
    [InlineData(TestKind.WilcoxonSignedRank)]
    [InlineData(TestKind.MannWhitney)]
    [InlineData(TestKind.KruskalWallis)]
    [InlineData(TestKind.Spearman)]
    [InlineData(TestKind.Kendall)]
    public void Every_test_runs_on_the_design_data_and_its_use_text_evaluates(TestKind kind)
    {
        var s = new TestSettings { Kind = kind, Fields = { ["sigma"] = "10", ["sigma1"] = "2", ["sigma2"] = "10", ["mu0"] = "4", ["m0"] = "4" } };
        var o = TestRunner.Run(s, DesignData());
        Assert.True(o.Error is null, $"{kind}: {o.Error}");
        Assert.NotEmpty(o.Sentence);
        Assert.NotEmpty(o.Tiles);
        Assert.False(o.Card.IsError);
        Assert.NotNull(o.UseText);
        EvaluateUse(o.UseText!);
        if (o.Graph is { } g) Assert.True(GraphParser.Parse(g.Lines[0].Text, Session(), false).IsFunction, g.Lines[0].Text);
    }

    [Fact]
    public void Chi_square_goodness_of_fit_and_intervals_word_their_result()
    {
        var data = DataSet.Of(("L1", [20, 30, 50]), ("L2", [25, 25, 50]));
        var gof = TestRunner.Run(new TestSettings { Kind = TestKind.ChiSquareGof }, data);
        Assert.Equal(2, gof.Test!.DegreesOfFreedom);
        Assert.Equal("H₀: the counts follow the expected counts", gof.Hypotheses);
        Assert.Contains("the counts do not follow the expected counts", gof.Sentence);
        Assert.Single(gof.Graph!.Annotations.Where(a => a.Kind == AnnotationKind.Shade).DistinctBy(a => a.Points));
        Assert.Equal("chi2gof(L1, L2)", gof.UseText);

        var ci = TestRunner.Run(new TestSettings { Kind = TestKind.TInterval, Fields = { ["list"] = "L2" } }, DesignData());
        Assert.Equal(67.5 - 7.57247, ci.Interval!.Lower, 4);
        Assert.StartsWith("With 95% confidence, the mean of L2 lies between 59.9275 and 75.0725", ci.Sentence);
        Assert.Equal("tinterval(L2, 0.95)", ci.UseText);
        var shade = Assert.Single(ci.Graph!.Annotations, a => a.Kind == AnnotationKind.Shade);
        Assert.Equal(ci.Interval.Lower, shade.Points[0].X, 9);
        Assert.Equal(ci.Interval.Upper, shade.Points[^1].X, 9);
    }

    [Fact]
    public void Standard_normal_area_is_shaded_between_the_bounds()
    {
        var o = Distributions.Compute(new DistributionSettings());
        Assert.Null(o.Error);
        Assert.Equal(0.950004209703559, o.Value, 12);
        Assert.Equal("P(-1.96 < X < 1.96) = 0.9500042", o.Headline);
        Assert.Equal("normalcdf(-1.96, 1.96, 0, 1)", o.Card.UseText);
        var shade = Assert.Single(o.Graph!.Annotations.DistinctBy(a => a.Points));
        Assert.Equal(AnnotationKind.Shade, shade.Kind);
        Assert.Equal(new Point2(-1.96, 0), shade.Points[0]);
        Assert.Equal(new Point2(1.96, 0), shade.Points[^1]);
        Assert.Equal(-1.96, shade.Points[1].X, 12);
        Assert.Equal(Math.Exp(-1.96 * 1.96 / 2) / Math.Sqrt(2 * Math.PI), shade.Points[1].Y, 12);
        Assert.All(shade.Points, p => Assert.InRange(p.X, -1.96, 1.96));
        Assert.True(o.Graph.Viewport.XMin < -3 && o.Graph.Viewport.XMax > 3);
        Assert.True(o.Graph.Viewport.YMin < 0);
        var curve = GraphParser.Parse(o.Graph.Lines[0].Text, Session(), false);
        Assert.Equal(0.398942280401433, curve.Members[0].Main!.Eval(0, []), 12);
        EvaluateUse(o.Card.UseText!);
    }

    [Fact]
    public void Open_bounds_density_and_inverse()
    {
        var left = Distributions.Compute(new DistributionSettings { Lower = "-inf", Upper = "1.645" });
        Assert.Equal(0.95, left.Value, 4);
        Assert.Equal("P(X < 1.645) = 0.9500151", left.Headline);
        EvaluateUse(left.Card.UseText!);
        var shade = Assert.Single(left.Graph!.Annotations.DistinctBy(a => a.Points));
        Assert.True(shade.Points[0].X < -3);

        var pdf = Distributions.Compute(new DistributionSettings { Mode = DistributionMode.Density, X = "0" });
        Assert.Equal(0.398942280401433, pdf.Value, 12);
        Assert.Equal("normalpdf(0, 0, 1)", pdf.Card.UseText);

        var inv = Distributions.Compute(new DistributionSettings { Mode = DistributionMode.Inverse, Area = "0.975" });
        Assert.Equal(1.959964, inv.Value, 5);
        Assert.Equal("invnorm(0.975, 0, 1)", inv.Card.UseText);
        var center = Distributions.Compute(new DistributionSettings { Mode = DistributionMode.Inverse, Area = "95", Tail = Tail.Center });
        Assert.Equal(-1.959964, center.Bounds.Lower, 5);
        Assert.Equal("invnorm(0.95, 0, 1, \"center\")", center.Card.UseText);
        EvaluateUse(center.Card.UseText!);
        var bad = Distributions.Compute(new DistributionSettings { Parameters = { ["sigma"] = "-1" } });
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public void Every_distribution_draws_and_computes()
    {
        foreach (var info in Distributions.All)
        {
            var s = new DistributionSettings { Kind = info.Kind, Lower = "1", Upper = "3" };
            if (info.Kind == DistributionKind.Beta) (s.Lower, s.Upper) = ("0.1", "0.4");
            var o = Distributions.Compute(s);
            Assert.True(o.Error is null, $"{info.Name}: {o.Error}");
            Assert.InRange(o.Value, 0, 1);
            if (info.IsDiscrete)
            {
                Assert.NotNull(o.Scene);
                Assert.Contains(o.Scene!.Rects, r => r.Fill == "Sel");
            }
            else
            {
                var curve = GraphParser.Parse(o.Graph!.Lines[0].Text, Session(), false);
                Assert.True(curve.IsFunction, $"{info.Name}: {o.Graph.Lines[0].Text} {curve.Error}");
                var d = Distributions.Create(info.Kind, info.Parameters.ToDictionary(p => p.Key, p => double.Parse(p.Default, System.Globalization.CultureInfo.InvariantCulture)));
                double x = info.Kind is DistributionKind.Beta or DistributionKind.Uniform ? 0.3 : 1.5;
                Assert.Equal(d.Density(x), curve.Members[0].Main!.Eval(x, []), 9);
            }
            if (o.Card.UseText is { } use) EvaluateUse(use);
        }
        var binom = Distributions.Compute(new DistributionSettings { Kind = DistributionKind.Binomial, Lower = "3", Upper = "5" });
        Assert.Equal(new BinomialDistribution(10, 0.5).Probability(3, 5), binom.Value, 12);
        Assert.Equal("binomcdf(10, 0.5, 3, 5)", binom.Card.UseText);
        Assert.Equal(3, binom.Scene!.Rects.Count(r => r.Fill == "Sel"));
        Assert.Equal("P(3 ≤ X ≤ 5) = 0.5683594", binom.Headline);
    }
}
