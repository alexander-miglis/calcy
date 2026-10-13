using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

public class InferenceTests
{
    private static readonly double[] Sample = [299.4, 297.7, 301, 298.9, 300.2, 297];
    private static readonly double[] A = [12.1, 14.3, 13.8, 15.2, 12.9, 14.7, 13.3];
    private static readonly double[] B = [11.2, 12.8, 12.1, 13.5, 11.9, 12.4];

    [Fact]
    public void ZTestFromData()
    {
        var r = Inference.ZTest(300, 2, Sample);
        Assert.Equal("z", r.StatisticName);
        Tol.Rel(-1.1839200423452167, r.Statistic, 1e-13);
        Tol.Rel(0.23644471557229832, r.PValue, 1e-13);
        Tol.Rel(299.0333333333333, r.Value("xbar"), 1e-15);
        Tol.Rel(1.5028861123407407, r.Value("Sx"), 1e-13);
        Assert.Equal(6, r.Value("n"));
        Assert.Null(r.DegreesOfFreedom);
        Tol.Rel(0.11822235778614916, Inference.ZTest(300, 2, Sample, Alternative.Less).PValue, 1e-13);
        Tol.Rel(1 - 0.11822235778614916, Inference.ZTest(300, 2, Sample, Alternative.Greater).PValue, 1e-13);
    }

    [Fact]
    public void ZTestFromStats()
    {
        var r = Inference.ZTest(300, 2, 299.0333333333333, 6);
        Tol.Rel(-1.1839200423452167, r.Statistic, 1e-13);
        Assert.False(r.RejectsNull(0.05));
        Assert.Equal("Fail to reject H0", r.Decision());
    }

    [Fact]
    public void TTestFromData()
    {
        var r = Inference.TTest(300, Sample);
        Tol.Rel(-1.575528621395356, r.Statistic, 1e-13);
        Tol.Rel(0.17595432932963312, r.PValue, 1e-12);
        Assert.Equal(5, r.DegreesOfFreedom);
        Tol.Rel(0.08797716466481656, Inference.TTest(300, Sample, Alternative.Less).PValue, 1e-12);
        Assert.Equal(Alternative.NotEqual, r.Alternative);
    }

    [Fact]
    public void TwoSampleTTestWelchAndPooled()
    {
        var w = Inference.TwoSampleTTest(A, B);
        Tol.Rel(2.7760180449457406, w.Statistic, 1e-13);
        Tol.Rel(10.788353726289039, w.DegreesOfFreedom!.Value, 1e-13);
        Tol.Rel(0.01833279075828731, w.PValue, 1e-11);
        Tol.Rel(0.009166395379143655, Inference.TwoSampleTTest(A, B, Alternative.Greater).PValue, 1e-11);
        var p = Inference.TwoSampleTTest(A, B, pooled: true);
        Tol.Rel(2.706688220779279, p.Statistic, 1e-13);
        Assert.Equal(11, p.DegreesOfFreedom);
        Tol.Rel(0.020411332458239603, p.PValue, 1e-11);
        Tol.Rel(0.9565789512859302, p.Value("Sxp"), 1e-13);
        Assert.True(w.RejectsNull(0.05));
        Assert.Equal("Reject H0", w.Decision(0.05));
        Assert.False(w.RejectsNull(0.01));
    }

    [Fact]
    public void TwoSampleZTest()
    {
        var r = Inference.TwoSampleZTest(1.2, 0.9, A, B);
        Tol.Rel(2.4678054134426843, r.Statistic, 1e-13);
        Tol.Rel(0.01359441964431957, r.PValue, 1e-12);
    }

    [Fact]
    public void TwoSampleFTest()
    {
        var r = Inference.TwoSampleFTest(A, B);
        Tol.Rel(1.8651857525852145, r.Statistic, 1e-13);
        Tol.Rel(0.5106674900622387, r.PValue, 1e-12);
        Assert.Equal(6, r.DegreesOfFreedom);
        Assert.Equal(5, r.DenominatorDegreesOfFreedom);
        Tol.Rel(0.25533374503111933, Inference.TwoSampleFTest(A, B, Alternative.Greater).PValue, 1e-12);
    }

    [Fact]
    public void PairedTTest()
    {
        double[] before = [200, 190, 210, 180, 205, 195, 220, 185];
        double[] after = [195, 186, 200, 181, 198, 190, 210, 183];
        var r = Inference.PairedTTest(before, after);
        Tol.Rel(3.9385990928336883, r.Statistic, 1e-13);
        Tol.Rel(0.005613416719266003, r.PValue, 1e-11);
        Tol.Rel(0.0028067083596330015, Inference.PairedTTest(before, after, alternative: Alternative.Greater).PValue, 1e-11);
        Assert.Equal(7, r.DegreesOfFreedom);
    }

    [Fact]
    public void ProportionTests()
    {
        var one = Inference.OnePropZTest(0.5, 57, 100);
        Tol.Rel(1.4, one.Statistic, 1e-14);
        Tol.Rel(0.1615133184675421, one.PValue, 1e-13);
        Tol.Rel(0.57, one.Value("phat"), 1e-15);
        Tol.Rel(0.08075665923377105, Inference.OnePropZTest(0.5, 57, 100, Alternative.Greater).PValue, 1e-13);
        var two = Inference.TwoPropZTest(45, 100, 30, 90);
        Tol.Rel(1.6427266128719291, two.Statistic, 1e-13);
        Tol.Rel(0.1004395099796355, two.PValue, 1e-12);
        Tol.Rel(75.0 / 190, two.Value("phat"), 1e-15);
    }

    [Fact]
    public void ChiSquareGoodnessOfFit()
    {
        var r = Inference.ChiSquareGoodnessOfFit([22, 17, 21, 25, 15, 20], [20, 20, 20, 20, 20, 20]);
        Tol.Rel(3.2, r.Statistic, 1e-14);
        Tol.Rel(0.6691829020332433, r.PValue, 1e-13);
        Assert.Equal(5, r.DegreesOfFreedom);
        Assert.Equal(6, r.Contributions.Count);
        Tol.Rel(0.2, r.Contributions[0], 1e-14);
        var custom = Inference.ChiSquareGoodnessOfFit([22, 17, 21, 25, 15, 20], [20, 20, 20, 20, 20, 20], 3);
        Assert.Equal(3, custom.DegreesOfFreedom);
    }

    [Fact]
    public void ChiSquareTwoWay()
    {
        var r = Inference.ChiSquareTwoWay(new double[,] { { 20, 30, 25 }, { 30, 25, 20 } });
        Tol.Rel(3.01010101010101, r.Statistic, 1e-13);
        Tol.Rel(0.2220060811204631, r.PValue, 1e-13);
        Assert.Equal(2, r.DegreesOfFreedom);
        Assert.Equal(25, r.Expected[0, 0], 13);
        Assert.Equal(2, r.Expected.GetLength(0));
        Assert.Equal(3, r.Contributions.GetLength(1));
    }

    [Fact]
    public void OneWayAnova()
    {
        var r = Inference.OneWayAnova([6, 8, 4, 5, 3, 4], [8, 12, 9, 11, 6, 8], [13, 9, 11, 8, 7, 12]);
        Tol.Rel(9.264705882352942, r.Statistic, 1e-13);
        Tol.Rel(0.0023987773293929083, r.PValue, 1e-11);
        Assert.Equal(2, r.Factor.DegreesOfFreedom);
        Assert.Equal(84, r.Factor.SumOfSquares, 12);
        Assert.Equal(42, r.Factor.MeanSquare, 12);
        Assert.Equal(15, r.Error.DegreesOfFreedom);
        Assert.Equal(68, r.Error.SumOfSquares, 12);
        Tol.Rel(2.1291625896895083, r.PooledStdDev, 1e-14);
        Assert.Equal(17, r.Total.DegreesOfFreedom);
    }

    [Fact]
    public void TwoWayAnovaWithReplication()
    {
        double[][][] data =
        [
            [[14, 16, 15], [18, 20, 19], [12, 13, 11]],
            [[17, 19, 18], [24, 21, 23], [15, 14, 13]],
        ];
        var r = Inference.TwoWayAnova(data);
        Tol.Rel(30.727272727272727, r.FactorA.F!.Value, 1e-13);
        Tol.Rel(0.00012714421588680237, r.FactorA.PValue!.Value, 1e-10);
        Tol.Rel(37.55555555555556, r.FactorA.SumOfSquares, 1e-13);
        Tol.Rel(75.5909090909091, r.FactorB.F!.Value, 1e-13);
        Tol.Rel(1.5814551132051287e-07, r.FactorB.PValue!.Value, 1e-9);
        Tol.Rel(184.77777777777777, r.FactorB.SumOfSquares, 1e-13);
        Tol.Rel(0.8636363636363636, r.Interaction.F!.Value, 1e-12);
        Tol.Rel(0.4462537755216681, r.Interaction.PValue!.Value, 1e-11);
        Tol.Rel(14.666666666666666, r.Error.SumOfSquares, 1e-13);
        Assert.Equal(12, r.Error.DegreesOfFreedom);
        Assert.Equal(17, r.Total.DegreesOfFreedom);
        Assert.True(r.FactorBTest.RejectsNull(0.001));
        Assert.False(r.InteractionTest.RejectsNull(0.05));
    }

    [Fact]
    public void LinRegTTestAndIntervals()
    {
        double[] x = [1, 2, 3, 4, 5, 6, 7];
        double[] y = [2.3, 4.1, 6.2, 8.4, 9.9, 12.5, 13.9];
        var t = Inference.LinRegTTest(x, y);
        Tol.Rel(41.89874898460951, t.Statistic, 1e-12);
        Tol.Rel(1.461004731608116e-07, t.PValue, 1e-9);
        Assert.Equal(5, t.DegreesOfFreedom);
        Tol.Rel(1.975, t.B, 1e-14);
        Tol.Rel(0.2857142857142855, t.A, 1e-12);
        Tol.Rel(0.24942791687035695, t.S, 1e-13);
        Tol.Rel(0.9971599060910395, t.RSquared, 1e-14);
        Tol.Rel(t.R, t.Value("r"), 1e-15);
        Tol.Rel(7.30502365804058e-08, Inference.LinRegTTest(x, y, Alternative.Greater).PValue, 1e-9);
        var ci = Inference.LinRegTInterval(x, y);
        Tol.Rel(1.853829338621862, ci.Lower, 1e-13);
        Tol.Rel(2.096170661378138, ci.Upper, 1e-13);
        Assert.Equal(5, ci.DegreesOfFreedom);
        var mr = Inference.MeanResponseInterval(x, y, 4.5);
        Tol.Rel(8.923414567920279, mr.Lower, 1e-13);
        Tol.Rel(9.423014003508293, mr.Upper, 1e-13);
        var pi = Inference.PredictionInterval(x, y, 4.5);
        Tol.Rel(8.48509720554784, pi.Lower, 1e-13);
        Tol.Rel(9.861331365880734, pi.Upper, 1e-13);
    }

    [Fact]
    public void MeanIntervals()
    {
        var z = Inference.ZInterval(2, Sample);
        Tol.Rel(297.4330294412149, z.Lower, 1e-14);
        Tol.Rel(300.63363722545176, z.Upper, 1e-14);
        Tol.Rel(299.0333333333333, z.PointEstimate, 1e-15);
        Tol.Rel((z.Upper - z.Lower) / 2, z.MarginOfError, 1e-12);
        Assert.Equal(0.95, z.ConfidenceLevel);
        var t = Inference.TInterval(Sample);
        Tol.Rel(297.4561510843382, t.Lower, 1e-14);
        Tol.Rel(300.6105155823284, t.Upper, 1e-14);
        Assert.Equal(5, t.DegreesOfFreedom);
        var t99 = Inference.TInterval(Sample, 99);
        Tol.Rel(296.55940923921895, t99.Lower, 1e-14);
        Assert.Equal(0.99, t99.ConfidenceLevel, 15);
    }

    [Fact]
    public void TwoSampleIntervals()
    {
        var w = Inference.TwoSampleTInterval(A, B);
        Tol.Rel(0.2956450736418839, w.Lower, 1e-12);
        Tol.Rel(2.5853073073104973, w.Upper, 1e-13);
        Tol.Rel(10.788353726289039, w.DegreesOfFreedom!.Value, 1e-13);
        var p = Inference.TwoSampleTInterval(A, B, pooled: true);
        Tol.Rel(0.26913081927173443, p.Lower, 1e-12);
        var z = Inference.TwoSampleZInterval(1.2, 0.9, A, B);
        Tol.Rel(0.29643078132774203, z.Lower, 1e-12);
    }

    [Fact]
    public void ProportionIntervals()
    {
        var one = Inference.OnePropZInterval(57, 100);
        Tol.Rel(0.4729669356893162, one.Lower, 1e-14);
        Tol.Rel(0.6670330643106838, one.Upper, 1e-14);
        var two = Inference.TwoPropZInterval(45, 100, 30, 90);
        Tol.Rel(-0.02114731698196271, two.Lower, 1e-12);
        Tol.Rel(0.25448065031529604, two.Upper, 1e-13);
    }

    [Fact]
    public void SignTest()
    {
        double[] data = [5.1, 4.8, 6.2, 5.9, 7.1, 4.5, 6.6, 5.0, 6.8, 7.4, 5.5, 6.1];
        var r = Inference.SignTest(data, 5.0);
        Assert.Equal(9, r.Statistic);
        Assert.Equal(11, r.Value("n"));
        Assert.Equal(1, r.Value("ties"));
        Tol.Rel(0.0654296875, r.PValue, 1e-14);
        Tol.Rel(0.03271484375, Inference.SignTest(data, 5.0, Alternative.Greater).PValue, 1e-14);
        Tol.Rel(1.0, Inference.SignTest([1, -1, 2, -2], 0).PValue, 1e-15);
    }

    [Fact]
    public void WilcoxonSignedRankMatchesRReference()
    {
        // Hollander and Wolfe depression scale data (R wilcox.test example): V = 40, exact one-sided p = 0.01953.
        double[] x = [1.83, 0.50, 1.62, 2.48, 1.68, 1.88, 1.55, 3.06, 1.30];
        double[] y = [0.878, 0.647, 0.598, 2.05, 1.06, 1.29, 1.06, 3.14, 1.29];
        var exact = Inference.WilcoxonSignedRank(x, y, Alternative.Greater, exact: true);
        Assert.Equal(40, exact.Statistic);
        Tol.Rel(0.01953125, exact.PValue, 1e-13);
        Tol.Rel(0.0390625, Inference.WilcoxonSignedRank(x, y, exact: true).PValue, 1e-13);
        var approx = Inference.WilcoxonSignedRank(x, y, Alternative.Greater);
        Tol.Rel(0.022005492006475703, approx.PValue, 1e-12);
        Tol.Rel(0.04401098401295141, Inference.WilcoxonSignedRank(x, y).PValue, 1e-12);
    }

    [Fact]
    public void MannWhitneyMatchesRReference()
    {
        // R wilcox.test example (permeability constants): W = 35, exact p = 0.1272 for "greater".
        double[] x = [0.80, 0.83, 1.89, 1.04, 1.45, 1.38, 1.91, 1.64, 0.73, 1.46];
        double[] y = [1.15, 0.88, 0.90, 0.74, 1.21];
        var exact = Inference.MannWhitneyU(x, y, Alternative.Greater, exact: true);
        Assert.Equal(35, exact.Statistic);
        Tol.Rel(0.1272061272061272, exact.PValue, 1e-13);
        Tol.Rel(0.2544122544122544, Inference.MannWhitneyU(x, y, exact: true).PValue, 1e-13);
        Tol.Rel(0.24462360512698333, Inference.MannWhitneyU(x, y).PValue, 1e-12);
        Assert.Equal(15, exact.Value("U2"));
    }

    [Fact]
    public void MannWhitneyWithTies()
    {
        var r = Inference.MannWhitneyU([1, 2, 2, 3, 4, 5, 5], [2, 3, 3, 4, 6, 6]);
        Assert.Equal(14.5, r.Statistic);
        Tol.Rel(0.38408440088274914, r.PValue, 1e-12);
        Assert.Throws<ArgumentException>(() => Inference.MannWhitneyU([1, 2, 2], [2, 3], exact: true));
    }

    [Fact]
    public void KruskalWallis()
    {
        var r = Inference.KruskalWallis([2.9, 3.0, 2.5, 2.6, 3.2], [3.8, 2.7, 4.0, 2.4], [2.8, 3.4, 3.7, 2.2, 2.0], [2.9, 3.0, 3.3]);
        Tol.Rel(1.161015561015561, r.Statistic, 1e-13);
        Tol.Rel(0.7623687571660432, r.PValue, 1e-12);
        Assert.Equal(3, r.DegreesOfFreedom);
    }

    [Fact]
    public void RankCorrelations()
    {
        double[] iq = [86, 97, 99, 100, 101, 103, 106, 110, 112, 113];
        double[] tv = [2, 20, 28, 27, 50, 29, 7, 17, 6, 12];
        var s = Inference.SpearmanCorrelation(iq, tv);
        Tol.Rel(-29.0 / 165, s.Statistic, 1e-14);
        Tol.Rel(0.6271883447764844, s.PValue, 1e-12);
        var k = Inference.KendallTau(iq, tv);
        Tol.Rel(-1.0 / 9, k.Statistic, 1e-14);
        Assert.Equal(-5, k.Value("S"));
        Tol.Rel(0.654720846018577, k.PValue, 1e-12);
        Tol.Rel(0.7274895282186948, Inference.KendallTau(iq, tv, exact: true).PValue, 1e-12);
        var perfect = Inference.KendallTau([1, 2, 3, 4, 5], [2, 4, 6, 8, 10], Alternative.Greater, exact: true);
        Assert.Equal(1, perfect.Statistic);
        Tol.Rel(1.0 / 120, perfect.PValue, 1e-14);
    }

    [Fact]
    public void ConfidenceLevelAcceptsPercent()
    {
        var a = Inference.ZInterval(2, 10, 25, 0.9);
        var b = Inference.ZInterval(2, 10, 25, 90);
        Assert.Equal(a.Lower, b.Lower);
        Assert.Throws<ArgumentException>(() => Inference.ZInterval(2, 10, 25, 0));
        Assert.Throws<ArgumentException>(() => Inference.ZInterval(2, 10, 25, 1));
        Assert.Throws<ArgumentException>(() => Inference.ZInterval(2, 10, 25, 100));
    }

    [Fact]
    public void InvalidInferenceInputsThrow()
    {
        Assert.Throws<ArgumentException>(() => Inference.ZTest(0, -1, 1, 10));
        Assert.Throws<ArgumentException>(() => Inference.TTest(0, 1, 0, 10));
        Assert.Throws<ArgumentException>(() => Inference.TTest(0, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => Inference.OnePropZTest(0.5, 11, 10));
        Assert.Throws<ArgumentException>(() => Inference.OnePropZTest(1.5, 5, 10));
        Assert.Throws<ArgumentException>(() => Inference.OnePropZTest(0.5, 2.5, 10));
        Assert.Throws<ArgumentException>(() => Inference.OneWayAnova([1, 2, 3]));
        Assert.Throws<ArgumentException>(() => Inference.ChiSquareTwoWay(new double[,] { { 1, 2 } }));
        Assert.Throws<ArgumentException>(() => Inference.ChiSquareGoodnessOfFit([1, 2], [1, 0]));
        Assert.Throws<ArgumentException>(() => Inference.PairedTTest([1, 2, 3], [1, 2]));
        Assert.Throws<ArgumentException>(() => Inference.TwoWayAnova([[[1, 2], [3, 4]], [[5, 6], [7]]]));
        Assert.Throws<ArgumentException>(() => Inference.SignTest([3, 3, 3], 3));
        Assert.Throws<ArgumentException>(() => Inference.ZTest(0, 1, 0, 10).RejectsNull(1.5));
        Assert.Throws<ArgumentException>(() => Inference.ZTest(0, 1, 0, 10).Value("nope"));
    }
}
