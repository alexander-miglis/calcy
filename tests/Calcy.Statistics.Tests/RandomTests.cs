using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

public class RandomTests
{
    [Fact]
    public void Xoshiro256StarStarReferenceVector()
    {
        var r = CalcRandom.FromState(1, 2, 3, 4);
        Assert.Equal(11520UL, r.NextULong());
        Assert.Equal(0UL, r.NextULong());
        Assert.Equal(1509978240UL, r.NextULong());
        Assert.Equal(1215971899390074240UL, r.NextULong());
    }

    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = new CalcRandom(12345);
        var b = new CalcRandom(12345);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
        var c = a.Clone();
        Assert.Equal(a.NextDouble(), c.NextDouble());
        var (s0, s1, s2, s3) = a.State;
        var d = CalcRandom.FromState(s0, s1, s2, s3);
        Assert.Equal(a.NextInt(1, 1000), d.NextInt(1, 1000));
        Assert.NotEqual(new CalcRandom(1).NextULong(), new CalcRandom(2).NextULong());
        Assert.Equal(12345UL, new CalcRandom(12345).Seed);
        Assert.Throws<ArgumentException>(() => CalcRandom.FromState(0, 0, 0, 0));
    }

    [Fact]
    public void UniformDoublesStayInRange()
    {
        var r = new CalcRandom(7);
        double min = 1, max = 0, sum = 0;
        for (int i = 0; i < 100000; i++)
        {
            double u = r.NextDouble();
            Assert.InRange(u, 0.0, 0.9999999999999999);
            double o = r.NextOpenDouble();
            Assert.True(o > 0 && o < 1);
            min = Math.Min(min, u); max = Math.Max(max, u); sum += u;
        }
        Assert.True(min < 0.001 && max > 0.999);
        Assert.InRange(sum / 100000, 0.495, 0.505);
        double v = r.NextUniform(-3, 5);
        Assert.InRange(v, -3, 5);
    }

    [Fact]
    public void NextIntIsInclusiveAndUniform()
    {
        var r = new CalcRandom(2024);
        var counts = new long[6];
        for (int i = 0; i < 60000; i++)
        {
            long k = r.NextInt(1, 6);
            Assert.InRange(k, 1, 6);
            counts[k - 1]++;
        }
        double chi2 = counts.Sum(c => (c - 10000.0) * (c - 10000.0) / 10000.0);
        Assert.True(chi2 < new ChiSquaredDistribution(5).InverseSurvival(0.001), $"chi2 = {chi2}");
        Assert.Equal(5, r.NextInt(5, 5));
        long big = r.NextInt(long.MinValue, long.MaxValue);
        Assert.InRange(big, long.MinValue, long.MaxValue);
        Assert.InRange(r.NextInt(-10, -7), -10, -7);
        Assert.Throws<ArgumentException>(() => r.NextInt(3, 2));
    }

    [Fact]
    public void NormalVariatesHaveRightMoments()
    {
        var r = new CalcRandom(99);
        int n = 200000;
        double s = 0, s2 = 0;
        int beyond2 = 0;
        for (int i = 0; i < n; i++)
        {
            double z = r.NextNormal();
            s += z; s2 += z * z;
            if (Math.Abs(z) > 1.959963984540054) beyond2++;
        }
        double mean = s / n;
        Assert.InRange(mean, -0.01, 0.01);
        Assert.InRange(Math.Sqrt(s2 / n - mean * mean), 0.99, 1.01);
        Assert.InRange(beyond2 / (double)n, 0.047, 0.053);
        double x = r.NextNormal(100, 15);
        Assert.InRange(x, 0, 200);
    }

    private static void AssertGoodnessOfFit(Func<long> draw, DiscreteDistribution d, int n)
    {
        var counts = new Dictionary<long, long>();
        for (int i = 0; i < n; i++)
        {
            long k = draw();
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        // Bins with expected count >= 5, tails lumped.
        double lo = d.InverseCdf(1e-4), hi = d.InverseCdf(1 - 1e-4);
        double chi2 = 0;
        int bins = 0;
        double observedLow = counts.Where(kv => kv.Key < lo).Sum(kv => kv.Value);
        double expectedLow = n * d.Cdf(lo - 1);
        double observedHigh = counts.Where(kv => kv.Key > hi).Sum(kv => kv.Value);
        double expectedHigh = n * d.Survival(hi);
        foreach (var (o, e) in new[] { (observedLow, expectedLow), (observedHigh, expectedHigh) })
        {
            if (e >= 5) { chi2 += (o - e) * (o - e) / e; bins++; }
        }
        for (double k = lo; k <= hi; k++)
        {
            double e = n * d.Pmf(k);
            double o = counts.GetValueOrDefault((long)k);
            if (e < 5) continue;
            chi2 += (o - e) * (o - e) / e;
            bins++;
        }
        double critical = new ChiSquaredDistribution(bins - 1).InverseSurvival(0.0005);
        Assert.True(chi2 < critical, $"chi2 = {chi2:F2} with {bins - 1} df exceeds {critical:F2}");
    }

    [Fact]
    public void BinomialVariatesFollowTheBinomialDistribution()
    {
        var r = new CalcRandom(31337);
        AssertGoodnessOfFit(() => r.NextBinomial(20, 0.1), new BinomialDistribution(20, 0.1), 50000);
        AssertGoodnessOfFit(() => r.NextBinomial(1000, 0.3), new BinomialDistribution(1000, 0.3), 50000);
        AssertGoodnessOfFit(() => r.NextBinomial(60, 0.85), new BinomialDistribution(60, 0.85), 50000);
        Assert.Equal(0, r.NextBinomial(10, 0));
        Assert.Equal(10, r.NextBinomial(10, 1));
        Assert.Equal(0, r.NextBinomial(0, 0.5));
    }

    [Fact]
    public void PoissonVariatesFollowThePoissonDistribution()
    {
        var r = new CalcRandom(4242);
        AssertGoodnessOfFit(() => r.NextPoisson(3.5), new PoissonDistribution(3.5), 50000);
        AssertGoodnessOfFit(() => r.NextPoisson(50), new PoissonDistribution(50), 50000);
        Assert.Throws<ArgumentException>(() => r.NextPoisson(0));
    }

    [Fact]
    public void GeometricVariatesUseTrialCounts()
    {
        var r = new CalcRandom(5);
        AssertGoodnessOfFit(() => r.NextGeometric(0.3), new GeometricDistribution(0.3), 50000);
        Assert.Equal(1, r.NextGeometric(1));
        double mean = Enumerable.Range(0, 20000).Average(_ => r.NextExponential(4));
        Assert.InRange(mean, 0.24, 0.26);
    }

    [Fact]
    public void SamplingWithoutReplacementAndShuffling()
    {
        var r = new CalcRandom(11);
        int[] population = Enumerable.Range(1, 50).ToArray();
        var sample = r.Sample(population, 20);
        Assert.Equal(20, sample.Length);
        Assert.Equal(20, sample.Distinct().Count());
        Assert.All(sample, v => Assert.InRange(v, 1, 50));
        var shuffled = r.Shuffled(population);
        Assert.Equal(population, shuffled.OrderBy(v => v));
        Assert.NotEqual(population, shuffled);
        var list = new List<string> { "a", "b", "c", "d" };
        r.Shuffle(list);
        Assert.Equal(["a", "b", "c", "d"], list.OrderBy(s => s));
        var noRep = r.RandIntNoRep(1, 10);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), noRep.OrderBy(v => v));
        var sparse = r.RandIntNoRep(1, 1_000_000_000_000, 5);
        Assert.Equal(5, sparse.Distinct().Count());
        Assert.Throws<ArgumentException>(() => r.Sample(population, 51));
        Assert.Throws<ArgumentException>(() => r.RandIntNoRep(1, 5, 6));
    }

    [Fact]
    public void ShuffleIsUnbiasedForThreeItems()
    {
        var r = new CalcRandom(1);
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 60000; i++)
        {
            var a = r.Shuffled(new[] { 'x', 'y', 'z' });
            string key = new(a);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        Assert.Equal(6, counts.Count);
        double chi2 = counts.Values.Sum(c => (c - 10000.0) * (c - 10000.0) / 10000.0);
        Assert.True(chi2 < new ChiSquaredDistribution(5).InverseSurvival(0.0001), $"chi2 = {chi2}");
    }

    [Fact]
    public void CoinSimulation()
    {
        var r = new CalcRandom(1);
        var res = SimulationTools.TossCoins(r, 20000, coinsPerTrial: 3);
        Assert.Equal(20000, res.HeadsTally.Total);
        Assert.Equal(60000, res.TotalHeads + res.TotalTails);
        Assert.Equal(20000, res.HeadsPerTrial!.Count);
        Assert.Equal(res.TotalHeads, res.HeadsPerTrial.Sum(h => (long)h));
        Assert.Equal(res.HeadsPerTrial.Count(h => h == 2), res.HeadsTally.Count(2));
        Assert.InRange(res.HeadsTally.RelativeFrequency(0), 0.11, 0.14);
        Assert.InRange(res.HeadsTally.RelativeFrequency(3), 0.11, 0.14);
        Assert.Equal(0, res.HeadsTally.Count(4));
        var biased = SimulationTools.TossCoins(r, 20000, 1, headsProbability: 0.7, recordTrials: false);
        Assert.Null(biased.HeadsPerTrial);
        Assert.InRange(biased.TotalHeads / 20000.0, 0.68, 0.72);
        var many = SimulationTools.TossCoins(r, 100, coinsPerTrial: 150);
        Assert.Equal(15000, many.TotalHeads + many.TotalTails);
        Assert.Equal(150, many.HeadsTally.MaxValue);
    }

    [Fact]
    public void DiceSimulation()
    {
        var r = new CalcRandom(2);
        var res = SimulationTools.RollDice(r, 36000, dicePerTrial: 2, sides: 6);
        Assert.Equal(2, res.SumTally.MinValue);
        Assert.Equal(12, res.SumTally.MaxValue);
        Assert.Equal(36000, res.SumTally.Total);
        Assert.Equal(72000, res.FaceTally.Total);
        Assert.InRange(res.SumTally.RelativeFrequency(7), 6.0 / 36 - 0.01, 6.0 / 36 + 0.01);
        Assert.InRange(res.SumTally.RelativeFrequency(2), 1.0 / 36 - 0.005, 1.0 / 36 + 0.005);
        for (int face = 1; face <= 6; face++) Assert.InRange(res.FaceTally.RelativeFrequency(face), 0.155, 0.178);
        var d20 = SimulationTools.RollDice(r, 1000, 1, 20, recordTrials: false);
        Assert.Equal(20, d20.FaceTally.MaxValue);
        Assert.Null(d20.SumPerTrial);
        Assert.Throws<ArgumentException>(() => SimulationTools.RollDice(r, 10, 1, 1));
        Assert.Throws<ArgumentException>(() => SimulationTools.RollDice(r, 0));
    }

    [Fact]
    public void WeightedOutcomes()
    {
        var r = new CalcRandom(3);
        var res = SimulationTools.WeightedOutcomes(r, [1, 2, 0, 7], 50000);
        Assert.Equal(0, res.OutcomeTally.Count(2));
        Assert.InRange(res.OutcomeTally.RelativeFrequency(0), 0.09, 0.11);
        Assert.InRange(res.OutcomeTally.RelativeFrequency(1), 0.19, 0.21);
        Assert.InRange(res.OutcomeTally.RelativeFrequency(3), 0.69, 0.71);
        Assert.Equal(50000, res.Outcomes!.Count);
        Assert.InRange(SimulationTools.WeightedChoice(r, [0, 0, 5]), 2, 2);
        Assert.Throws<ArgumentException>(() => SimulationTools.WeightedOutcomes(r, [0, 0], 10));
        Assert.Throws<ArgumentException>(() => SimulationTools.WeightedOutcomes(r, [1, -1], 10));
    }

    [Fact]
    public void InvalidGeneratorArgumentsThrow()
    {
        var r = new CalcRandom(4);
        Assert.Throws<ArgumentException>(() => r.NextNormal(0, 0));
        Assert.Throws<ArgumentException>(() => r.NextBinomial(-1, 0.5));
        Assert.Throws<ArgumentException>(() => r.NextBinomial(10, 1.2));
        Assert.Throws<ArgumentException>(() => r.NextGeometric(0));
        Assert.Throws<ArgumentException>(() => r.NextUniform(2, 1));
    }
}
