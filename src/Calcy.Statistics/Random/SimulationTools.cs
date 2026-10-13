namespace Calcy.Statistics;

/// <summary>Frequency counts for consecutive integer outcomes MinValue, MinValue + 1, ...</summary>
public sealed record Tally(long MinValue, IReadOnlyList<long> Counts)
{
    /// <summary>The largest outcome covered.</summary>
    public long MaxValue => MinValue + Counts.Count - 1;

    /// <summary>Total number of observations.</summary>
    public long Total => Counts.Sum();

    /// <summary>Count of one outcome (0 outside the range).</summary>
    public long Count(long value) => value < MinValue || value > MaxValue ? 0 : Counts[(int)(value - MinValue)];

    /// <summary>Relative frequency of one outcome.</summary>
    public double RelativeFrequency(long value)
    {
        long total = Total;
        return total == 0 ? 0.0 : (double)Count(value) / total;
    }
}

/// <summary>Result of a coin simulation.</summary>
public sealed record CoinSimulationResult(long Trials, int CoinsPerTrial, IReadOnlyList<int>? HeadsPerTrial, Tally HeadsTally, long TotalHeads, long TotalTails);

/// <summary>Result of a dice simulation.</summary>
public sealed record DiceSimulationResult(long Trials, int DicePerTrial, int Sides, IReadOnlyList<int>? SumPerTrial, Tally SumTally, Tally FaceTally);

/// <summary>Result of a weighted outcome (spinner, urn) simulation; outcomes are indices into the weight list.</summary>
public sealed record WeightedSimulationResult(long Trials, IReadOnlyList<int>? Outcomes, Tally OutcomeTally);

/// <summary>Probability simulations (TI Probability Simulation, Casio Prob Sim): coins, dice and weighted outcomes.</summary>
public static class SimulationTools
{
    private const long MaxRecorded = 100_000_000;

    private static void CheckTrials(long trials, bool record)
    {
        if (trials < 1) throw new ArgumentException("the number of trials must be at least 1", nameof(trials));
        if (record && trials > MaxRecorded) throw new ArgumentException("too many trials to record every outcome; pass recordTrials: false to keep only the tallies", nameof(trials));
    }

    /// <summary>
    /// Tosses coinsPerTrial coins per trial. HeadsTally counts how many trials produced 0, 1, ..., coinsPerTrial heads.
    /// </summary>
    public static CoinSimulationResult TossCoins(CalcRandom rng, long trials, int coinsPerTrial = 1, double headsProbability = 0.5, bool recordTrials = true)
    {
        ArgumentNullException.ThrowIfNull(rng);
        CheckTrials(trials, recordTrials);
        if (coinsPerTrial < 1) throw new ArgumentException("at least one coin per trial is required", nameof(coinsPerTrial));
        if (!(headsProbability >= 0 && headsProbability <= 1)) throw new ArgumentException("the probability of heads must be between 0 and 1", nameof(headsProbability));
        var counts = new long[coinsPerTrial + 1];
        int[]? perTrial = recordTrials ? new int[trials] : null;
        long heads = 0;
        bool fair = headsProbability == 0.5;
        for (long t = 0; t < trials; t++)
        {
            int h = 0;
            if (fair)
            {
                int left = coinsPerTrial;
                while (left > 0)
                {
                    int take = Math.Min(64, left);
                    ulong bits = rng.NextULong();
                    if (take < 64) bits &= (1UL << take) - 1;
                    h += System.Numerics.BitOperations.PopCount(bits);
                    left -= take;
                }
            }
            else
            {
                for (int c = 0; c < coinsPerTrial; c++) if (rng.NextDouble() < headsProbability) h++;
            }
            counts[h]++;
            heads += h;
            if (perTrial is not null) perTrial[t] = h;
        }
        return new CoinSimulationResult(trials, coinsPerTrial, perTrial, new Tally(0, counts), heads, trials * coinsPerTrial - heads);
    }

    /// <summary>
    /// Rolls dicePerTrial fair dice with the given number of sides per trial. SumTally counts the trial sums and
    /// FaceTally counts every individual face.
    /// </summary>
    public static DiceSimulationResult RollDice(CalcRandom rng, long trials, int dicePerTrial = 1, int sides = 6, bool recordTrials = true)
    {
        ArgumentNullException.ThrowIfNull(rng);
        CheckTrials(trials, recordTrials);
        if (dicePerTrial < 1) throw new ArgumentException("at least one die per trial is required", nameof(dicePerTrial));
        if (sides < 2) throw new ArgumentException("a die needs at least 2 sides", nameof(sides));
        if ((long)dicePerTrial * sides > 100_000_000) throw new ArgumentException("too many dice or sides", nameof(dicePerTrial));
        var sumCounts = new long[dicePerTrial * (sides - 1) + 1];
        var faceCounts = new long[sides];
        int[]? perTrial = recordTrials ? new int[trials] : null;
        for (long t = 0; t < trials; t++)
        {
            int sum = 0;
            for (int d = 0; d < dicePerTrial; d++)
            {
                int face = (int)rng.NextInt(1, sides);
                faceCounts[face - 1]++;
                sum += face;
            }
            sumCounts[sum - dicePerTrial]++;
            if (perTrial is not null) perTrial[t] = sum;
        }
        return new DiceSimulationResult(trials, dicePerTrial, sides, perTrial, new Tally(dicePerTrial, sumCounts), new Tally(1, faceCounts));
    }

    /// <summary>
    /// Draws outcomes 0 .. k-1 with probabilities proportional to the weights (Walker's alias method, O(1) per draw).
    /// </summary>
    public static WeightedSimulationResult WeightedOutcomes(CalcRandom rng, IReadOnlyList<double> weights, long trials, bool recordTrials = true)
    {
        ArgumentNullException.ThrowIfNull(rng);
        CheckTrials(trials, recordTrials);
        var sampler = new AliasSampler(weights);
        var counts = new long[weights.Count];
        int[]? outcomes = recordTrials ? new int[trials] : null;
        for (long t = 0; t < trials; t++)
        {
            int k = sampler.Next(rng);
            counts[k]++;
            if (outcomes is not null) outcomes[t] = k;
        }
        return new WeightedSimulationResult(trials, outcomes, new Tally(0, counts));
    }

    /// <summary>One weighted draw (index into the weights).</summary>
    public static int WeightedChoice(CalcRandom rng, IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return new AliasSampler(weights).Next(rng);
    }

    private sealed class AliasSampler
    {
        private readonly double[] _prob;
        private readonly int[] _alias;

        public AliasSampler(IReadOnlyList<double> weights)
        {
            if (weights is null || weights.Count == 0) throw new ArgumentException("at least one weight is required", nameof(weights));
            int n = weights.Count;
            double sum = 0;
            foreach (var w in weights)
            {
                if (!(w >= 0) || !double.IsFinite(w)) throw new ArgumentException("weights must be non-negative finite numbers", nameof(weights));
                sum += w;
            }
            if (!(sum > 0)) throw new ArgumentException("the weights must not all be zero", nameof(weights));
            _prob = new double[n];
            _alias = new int[n];
            var scaled = new double[n];
            var small = new Stack<int>();
            var large = new Stack<int>();
            for (int i = 0; i < n; i++)
            {
                scaled[i] = weights[i] * n / sum;
                if (scaled[i] < 1) small.Push(i); else large.Push(i);
            }
            while (small.Count > 0 && large.Count > 0)
            {
                int s = small.Pop(), l = large.Pop();
                _prob[s] = scaled[s];
                _alias[s] = l;
                scaled[l] = scaled[l] + scaled[s] - 1;
                if (scaled[l] < 1) small.Push(l); else large.Push(l);
            }
            while (large.Count > 0) { int l = large.Pop(); _prob[l] = 1; _alias[l] = l; }
            while (small.Count > 0) { int s = small.Pop(); _prob[s] = 1; _alias[s] = s; }
        }

        public int Next(CalcRandom rng)
        {
            int i = (int)rng.NextInt(0, _prob.Length - 1);
            return rng.NextDouble() < _prob[i] ? i : _alias[i];
        }
    }
}
