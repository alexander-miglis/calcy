using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

public class DescriptiveTests
{
    private static readonly double[] D1 = [16, 4, 42, 8, 23, 15];
    private static readonly double[] D2 = [2, 4, 4, 4, 5, 5, 7, 9];
    private static readonly double[] D3 = [13, 1, 3, 11, 5, 7, 9];

    [Fact]
    public void OneVarMatchesTi84()
    {
        var s = Descriptive.OneVar(D1);
        Assert.Equal(6, s.N);
        Assert.Equal(18, s.Mean);
        Assert.Equal(108, s.Sum);
        Assert.Equal(2854, s.SumOfSquares);
        Tol.Rel(13.490737563232042, s.SampleStdDev, 1e-15);
        Tol.Rel(12.315302134607444, s.PopulationStdDev, 1e-15);
        Tol.Rel(13.490737563232042 * 13.490737563232042, s.SampleVariance, 1e-14);
        Assert.Equal(4, s.Min);
        Assert.Equal(8, s.Q1);
        Assert.Equal(15.5, s.Median);
        Assert.Equal(23, s.Q3);
        Assert.Equal(42, s.Max);
        Assert.Equal(38, s.Range);
        Assert.Equal(15, s.IQR);
        Tol.Rel(1.2426108050181426, s.Skewness, 1e-13);
        Tol.Rel(1.799746407438715, s.ExcessKurtosis, 1e-13);
        Tol.Rel(0.9074746241441206, s.PopulationSkewness, 1e-13);
        Tol.Rel(-0.24008694602101197, s.PopulationExcessKurtosis, 1e-13);
        Assert.Equal(6, s.Modes.Count);
    }

    [Fact]
    public void ClassicDataSetHasSigmaTwoAndModeFour()
    {
        var s = Descriptive.OneVar(D2);
        Assert.Equal(5, s.Mean);
        Assert.Equal(2, s.PopulationStdDev, 14);
        Tol.Rel(2.138089935299395, s.SampleStdDev, 1e-15);
        Assert.Equal(4, s.Q1);
        Assert.Equal(4.5, s.Median);
        Assert.Equal(6, s.Q3);
        Assert.Equal([4.0], s.Modes);
        Assert.Equal(3, s.ModeFrequency);
        Tol.Rel(0.8184875533567997, s.Skewness, 1e-13);
        Tol.Rel(0.940625, s.ExcessKurtosis, 1e-13);
    }

    [Theory]
    [InlineData(QuartileMethod.TI, 3, 7, 11)]
    [InlineData(QuartileMethod.TukeyHinges, 4, 7, 10)]
    [InlineData(QuartileMethod.Inclusive, 4, 7, 10)]
    [InlineData(QuartileMethod.Exclusive, 3, 7, 11)]
    [InlineData(QuartileMethod.CasioOnData, 3, 7, 11)]
    public void QuartileMethodsOddN(QuartileMethod method, double q1, double median, double q3)
    {
        var s = Descriptive.OneVar(D3, method: method);
        Assert.Equal(q1, s.Q1, 14);
        Assert.Equal(median, s.Median, 14);
        Assert.Equal(q3, s.Q3, 14);
        Assert.Equal(method, s.Method);
    }

    [Theory]
    [InlineData(QuartileMethod.TI, 8, 15.5, 23)]
    [InlineData(QuartileMethod.TukeyHinges, 8, 15.5, 23)]
    [InlineData(QuartileMethod.Inclusive, 9.75, 15.5, 21.25)]
    [InlineData(QuartileMethod.Exclusive, 7, 15.5, 27.75)]
    [InlineData(QuartileMethod.CasioOnData, 8, 15.5, 23)]
    public void QuartileMethodsEvenN(QuartileMethod method, double q1, double median, double q3)
    {
        var f = Descriptive.FiveNumberSummary(D1, method);
        Assert.Equal(q1, f.Q1, 14);
        Assert.Equal(median, f.Median, 14);
        Assert.Equal(q3, f.Q3, 14);
        Assert.Equal(4, f.Min);
        Assert.Equal(42, f.Max);
    }

    [Fact]
    public void CasioOnDataAveragesOnExactQuarter()
    {
        var s = Descriptive.OneVar([1, 2, 3, 4], method: QuartileMethod.CasioOnData);
        Assert.Equal(1.5, s.Q1);
        Assert.Equal(3.5, s.Q3);
    }

    [Fact]
    public void IntegerFrequenciesEqualExpandedData()
    {
        double[] values = [5, 1, 3, 2, 4];
        double[] freq = [1, 2, 5, 3, 3];
        double[] expanded = [1, 1, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 5];
        foreach (QuartileMethod m in Enum.GetValues<QuartileMethod>())
        {
            var w = Descriptive.OneVar(values, freq, m);
            var e = Descriptive.OneVar(expanded, null, m);
            Assert.Equal(e.N, w.N);
            Tol.Rel(e.Mean, w.Mean, 1e-15);
            Tol.Rel(e.SampleStdDev, w.SampleStdDev, 1e-14);
            Assert.Equal(e.Q1, w.Q1);
            Assert.Equal(e.Median, w.Median);
            Assert.Equal(e.Q3, w.Q3);
            Tol.Rel(e.Skewness, w.Skewness, 1e-12);
        }
        var s = Descriptive.OneVar(values, freq);
        Tol.Rel(2.857142857142857, s.Mean, 1e-15);
        Tol.Rel(1.167320591199077, s.SampleStdDev, 1e-14);
        Tol.Rel(1.124858267715973, s.PopulationStdDev, 1e-14);
        Assert.Equal(2, s.Q1);
        Assert.Equal(3, s.Median);
        Assert.Equal(4, s.Q3);
        Assert.Equal([3.0], s.Modes);
    }

    [Fact]
    public void RealValuedFrequencies()
    {
        var s = Descriptive.OneVar([1, 2, 3], [0.5, 1.5, 2]);
        Assert.Equal(4, s.N);
        Tol.Rel(2.375, s.Mean, 1e-15);
        Tol.Rel(Math.Sqrt(1.9375 / 3), s.SampleStdDev, 1e-14);
        Tol.Rel(Math.Sqrt(1.9375 / 4), s.PopulationStdDev, 1e-14);
        Assert.Equal(2.5, s.Median);
        Assert.Equal([3.0], s.Modes);
    }

    [Fact]
    public void SingleValue()
    {
        var s = Descriptive.OneVar([5]);
        Assert.Equal(1, s.N);
        Assert.Equal(5, s.Mean);
        Assert.True(double.IsNaN(s.SampleStdDev));
        Assert.Equal(0, s.PopulationStdDev);
        Assert.Equal(5, s.Q1);
        Assert.Equal(5, s.Median);
        Assert.Equal(5, s.Q3);
        Assert.True(double.IsNaN(s.Skewness));
    }

    [Fact]
    public void ZeroVariance()
    {
        var s = Descriptive.OneVar([3, 3, 3, 3]);
        Assert.Equal(0, s.SampleStdDev);
        Assert.Equal(0, s.IQR);
        Assert.True(double.IsNaN(s.Skewness));
        Assert.True(double.IsNaN(s.ExcessKurtosis));
        Assert.Equal([3.0], s.Modes);
    }

    [Fact]
    public void VarianceIsStableForLargeOffsets()
    {
        var s = Descriptive.OneVar([1e9 + 4, 1e9 + 7, 1e9 + 13, 1e9 + 16]);
        Assert.Equal(30, s.SampleVariance, 10);
        Assert.Equal(1e9 + 10, s.Mean);
    }

    [Theory]
    [InlineData(0.1, QuartileMethod.Inclusive, 6.0)]
    [InlineData(0.9, QuartileMethod.Exclusive, 42.0)]
    [InlineData(0.5, QuartileMethod.Inclusive, 15.5)]
    [InlineData(0.0, QuartileMethod.Exclusive, 4.0)]
    [InlineData(1.0, QuartileMethod.Inclusive, 42.0)]
    [InlineData(0.25, QuartileMethod.TI, 8.0)]
    [InlineData(0.75, QuartileMethod.TukeyHinges, 23.0)]
    public void Percentiles(double p, QuartileMethod method, double expected)
    {
        Tol.Rel(expected, Descriptive.Percentile(D1, p, method), 1e-14);
    }

    [Fact]
    public void PercentileOfClassicDataInclusive() => Tol.Rel(3.4, Descriptive.Percentile(D2, 0.1), 1e-14);

    [Fact]
    public void PercentileRejectsUnsupportedRequests()
    {
        Assert.Throws<ArgumentException>(() => Descriptive.Percentile(D1, 0.3, QuartileMethod.TI));
        Assert.Throws<ArgumentException>(() => Descriptive.Percentile(D1, 1.5));
        Assert.Throws<ArgumentException>(() => Descriptive.Percentile(D1, -0.1));
    }

    [Fact]
    public void TwoVarMatchesTi84()
    {
        double[] x = [1, 2, 3, 4, 5, 6, 7];
        double[] y = [2.3, 4.1, 6.2, 8.4, 9.9, 12.5, 13.9];
        var s = Descriptive.TwoVar(x, y);
        Assert.Equal(7, s.N);
        Assert.Equal(4, s.MeanX);
        Assert.Equal(28, s.SumX);
        Assert.Equal(140, s.SumX2);
        Tol.Rel(Math.Sqrt(28.0 / 6), s.Sx, 1e-15);
        Assert.Equal(2, s.SigmaX, 15);
        Tol.Rel(4.272559175103594, s.Sy, 1e-14);
        Tol.Rel(284.5, s.SumXY, 1e-15);
        Tol.Rel(9.216666666666667, s.CovarianceSample, 1e-14);
        Tol.Rel(7.9, s.CovariancePopulation, 1e-14);
        Tol.Rel(0.9985789433445107, s.R, 1e-14);
        Assert.Equal(1, s.MinX);
        Assert.Equal(13.9, s.MaxY);
    }

    [Fact]
    public void TwoVarWithConstantVariableHasUndefinedCorrelation()
    {
        var s = Descriptive.TwoVar([1, 2, 3], [4, 4, 4]);
        Assert.True(double.IsNaN(s.R));
        Assert.Equal(0, s.CovarianceSample);
    }

    [Fact]
    public void OutliersByIqrRule()
    {
        var o = Descriptive.Outliers([1, 2, 3, 4, 5, 6, 30]);
        Assert.Equal(2, o.Q1);
        Assert.Equal(6, o.Q3);
        Assert.Equal(-4, o.LowerFence);
        Assert.Equal(12, o.UpperFence);
        Assert.Equal([30.0], o.Outliers);
        Assert.Equal([6], o.Indices);
        Assert.Empty(Descriptive.Outliers(D3).Outliers);
    }

    [Fact]
    public void RanksAverageTies()
    {
        Assert.Equal([1, 2.5, 2.5, 4], Descriptive.Ranks([10, 20, 20, 30]));
        Assert.Equal([3, 1, 2], Descriptive.Ranks([9, 1, 5]));
    }

    [Fact]
    public void ConvenienceFunctions()
    {
        Assert.Equal(5, Descriptive.Mean(D2));
        Assert.Equal(4.5, Descriptive.Median(D2));
        Assert.Equal(4, Descriptive.Variance(D2, sample: false), 14);
        Tol.Rel(2.138089935299395, Descriptive.StdDev(D2), 1e-15);
    }

    [Fact]
    public void InvalidInputsThrow()
    {
        Assert.Throws<ArgumentException>(() => Descriptive.OneVar([]));
        Assert.Throws<ArgumentException>(() => Descriptive.OneVar([1, double.NaN]));
        Assert.Throws<ArgumentException>(() => Descriptive.OneVar([1, 2], [1]));
        Assert.Throws<ArgumentException>(() => Descriptive.OneVar([1, 2], [1, -1]));
        Assert.Throws<ArgumentException>(() => Descriptive.OneVar([1, 2], [0, 0]));
        Assert.Throws<ArgumentException>(() => Descriptive.TwoVar([1, 2], [1]));
    }
}
