using System.Diagnostics;
using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>
/// Reproducible pseudo random generator: xoshiro256** (Blackman and Vigna) seeded through SplitMix64. The same seed
/// gives the same sequence on every platform. Instances are not thread safe.
/// </summary>
public sealed class CalcRandom
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Creates a generator from a 64-bit seed.</summary>
    public CalcRandom(ulong seed)
    {
        Seed = seed;
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>Creates a generator seeded from the clock.</summary>
    public CalcRandom() : this(unchecked((ulong)Stopwatch.GetTimestamp() * 0x9E3779B97F4A7C15UL ^ (ulong)Environment.TickCount64 ^ (ulong)DateTime.UtcNow.Ticks))
    {
    }

    private CalcRandom(ulong s0, ulong s1, ulong s2, ulong s3, ulong seed)
    {
        _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
        Seed = seed;
    }

    /// <summary>Creates a generator from an explicit xoshiro256 state (not all zero).</summary>
    public static CalcRandom FromState(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0) throw new ArgumentException("the xoshiro256 state must not be all zero");
        return new CalcRandom(s0, s1, s2, s3, 0);
    }

    /// <summary>The seed this generator was created with (0 when created from an explicit state).</summary>
    public ulong Seed { get; }

    /// <summary>The current internal state, which can be passed to <see cref="FromState"/> to resume the sequence.</summary>
    public (ulong S0, ulong S1, ulong S2, ulong S3) State => (_s0, _s1, _s2, _s3);

    /// <summary>An independent copy that continues the same sequence.</summary>
    public CalcRandom Clone() => new(_s0, _s1, _s2, _s3, Seed);

    private static ulong SplitMix64(ref ulong x)
    {
        unchecked
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>Next 64 random bits.</summary>
    public ulong NextULong()
    {
        unchecked
        {
            ulong result = RotL(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotL(_s3, 45);
            return result;
        }
    }

    /// <summary>Uniform double in [0, 1) with 53 random bits.</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform double in (0, 1), never exactly 0 or 1 (TI rand semantics).</summary>
    public double NextOpenDouble()
    {
        while (true)
        {
            double u = NextDouble();
            if (u > 0) return u;
        }
    }

    /// <summary>Uniform double in [lower, upper).</summary>
    public double NextUniform(double lower, double upper)
    {
        Guard.Finite(lower, "the lower bound", nameof(lower));
        Guard.Finite(upper, "the upper bound", nameof(upper));
        if (!(upper > lower)) throw new ArgumentException("the upper bound must be greater than the lower bound", nameof(upper));
        return lower + (upper - lower) * NextDouble();
    }

    /// <summary>Uniform integer in [lower, upper], both inclusive, without modulo bias (TI randInt).</summary>
    public long NextInt(long lower, long upper)
    {
        if (lower > upper) throw new ArgumentException("the lower bound must not exceed the upper bound", nameof(lower));
        unchecked
        {
            ulong range = (ulong)upper - (ulong)lower + 1UL;
            if (range == 0) return (long)NextULong();
            ulong x = NextULong();
            ulong high = Math.BigMul(x, range, out ulong low);
            if (low < range)
            {
                ulong threshold = (0UL - range) % range;
                while (low < threshold)
                {
                    x = NextULong();
                    high = Math.BigMul(x, range, out low);
                }
            }
            return lower + (long)high;
        }
    }

    /// <summary>Normal variate (TI randNorm) by inversion of a uniform with the AS 241 quantile.</summary>
    public double NextNormal(double mean = 0, double standardDeviation = 1)
    {
        Guard.Finite(mean, "the mean", nameof(mean));
        Guard.PositiveFinite(standardDeviation, "the standard deviation", nameof(standardDeviation));
        double u = NextOpenDouble();
        return mean + standardDeviation * StdNormal.As241(u - 0.5, Math.Min(u, 1 - u));
    }

    /// <summary>Exponential variate with the given rate.</summary>
    public double NextExponential(double rate = 1)
    {
        Guard.PositiveFinite(rate, "the rate", nameof(rate));
        return -Math.Log(NextOpenDouble()) / rate;
    }

    /// <summary>Binomial variate (TI randBin): inversion for small n p, Hormann's BTRS rejection otherwise.</summary>
    public long NextBinomial(long trials, double probability)
    {
        if (trials < 0) throw new ArgumentException("the number of trials must be non-negative", nameof(trials));
        Guard.Probability(probability, "the success probability", nameof(probability));
        if (trials == 0 || probability == 0) return 0;
        if (probability == 1) return trials;
        if (probability > 0.5) return trials - NextBinomial(trials, 1 - probability);
        double n = trials, p = probability;
        if (n * p < 10) return BinomialInversion(n, p);
        return Btrs(n, p);
    }

    private long BinomialInversion(double n, double p)
    {
        double q = 1 - p;
        double qn = Math.Exp(n * Num.Log1p(-p));
        double np = n * p;
        double bound = Math.Min(n, np + 10 * Math.Sqrt(np * q + 1));
        long x = 0;
        double px = qn;
        double u = NextDouble();
        while (u > px)
        {
            x++;
            if (x > bound)
            {
                x = 0;
                px = qn;
                u = NextDouble();
            }
            else
            {
                u -= px;
                px = (n - x + 1) * p * px / (x * q);
            }
        }
        return x;
    }

    private long Btrs(double n, double p)
    {
        double q = 1 - p;
        double spq = Math.Sqrt(n * p * q);
        double b = 1.15 + 2.53 * spq;
        double a = -0.0873 + 0.0248 * b + 0.01 * p;
        double c = n * p + 0.5;
        double vr = 0.92 - 4.2 / b;
        double alpha = (2.83 + 5.1 / b) * spq;
        double lpq = Math.Log(p / q);
        double m = Math.Floor((n + 1) * p);
        double h = SpecialFunctions.LogGamma(m + 1) + SpecialFunctions.LogGamma(n - m + 1);
        while (true)
        {
            double u = NextDouble() - 0.5;
            double v = NextDouble();
            double us = 0.5 - Math.Abs(u);
            if (us <= 0) continue;
            double k = Math.Floor((2 * a / us + b) * u + c);
            if (k < 0 || k > n) continue;
            if (us >= 0.07 && v <= vr) return (long)k;
            double lv = Math.Log(v * alpha / (a / (us * us) + b));
            if (lv <= h - SpecialFunctions.LogGamma(k + 1) - SpecialFunctions.LogGamma(n - k + 1) + (k - m) * lpq) return (long)k;
        }
    }

    /// <summary>Poisson variate: multiplication method for small means, Hormann's PTRS rejection otherwise.</summary>
    public long NextPoisson(double lambda)
    {
        Guard.PositiveFinite(lambda, "the mean", nameof(lambda));
        if (lambda < 10)
        {
            double limit = Math.Exp(-lambda);
            long k = 0;
            double prod = NextDouble();
            while (prod > limit)
            {
                k++;
                prod *= NextDouble();
            }
            return k;
        }
        double slam = Math.Sqrt(lambda), loglam = Math.Log(lambda);
        double bb = 0.931 + 2.53 * slam;
        double aa = -0.059 + 0.02483 * bb;
        double invalpha = 1.1239 + 1.1328 / (bb - 3.4);
        double vr = 0.9277 - 3.6224 / (bb - 2);
        while (true)
        {
            double u = NextDouble() - 0.5;
            double v = NextDouble();
            double us = 0.5 - Math.Abs(u);
            if (us <= 0) continue;
            double k = Math.Floor((2 * aa / us + bb) * u + lambda + 0.43);
            if (us >= 0.07 && v <= vr) return (long)k;
            if (k < 0 || (us < 0.013 && v > us)) continue;
            if (Math.Log(v) + Math.Log(invalpha) - Math.Log(aa / (us * us) + bb) <= -lambda + k * loglam - SpecialFunctions.LogGamma(k + 1))
                return (long)k;
        }
    }

    /// <summary>Geometric variate: the trial number of the first success (1, 2, ...).</summary>
    public long NextGeometric(double probability)
    {
        if (!(probability > 0 && probability <= 1)) throw new ArgumentException("the success probability must be greater than 0 and at most 1", nameof(probability));
        if (probability == 1) return 1;
        double k = Math.Ceiling(Math.Log(NextOpenDouble()) / Num.Log1p(-probability));
        return (long)Math.Max(1, k);
    }

    /// <summary>Random sample of count distinct elements (without replacement), in random order.</summary>
    public T[] Sample<T>(IReadOnlyList<T> population, int count)
    {
        Guard.NotNull(population, nameof(population));
        if (count < 0 || count > population.Count) throw new ArgumentException("the sample size must be between 0 and the population size", nameof(count));
        var a = population.ToArray();
        for (int i = 0; i < count; i++)
        {
            int j = (int)NextInt(i, a.Length - 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
        Array.Resize(ref a, count);
        return a;
    }

    /// <summary>Shuffles the list in place (Fisher-Yates).</summary>
    public void Shuffle<T>(IList<T> items)
    {
        Guard.NotNull(items, nameof(items));
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = (int)NextInt(0, i);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Returns a shuffled copy.</summary>
    public T[] Shuffled<T>(IReadOnlyList<T> items)
    {
        Guard.NotNull(items, nameof(items));
        var a = items.ToArray();
        Shuffle(a);
        return a;
    }

    /// <summary>TI randIntNoRep(lower, upper[, count]): distinct random integers from [lower, upper].</summary>
    public long[] RandIntNoRep(long lower, long upper, int? count = null)
    {
        if (lower > upper) throw new ArgumentException("the lower bound must not exceed the upper bound", nameof(lower));
        double range = (double)upper - lower + 1;
        int k = count ?? (range <= 10_000_000 ? (int)range : throw new ArgumentException("the range is too large to list every integer; pass a count", nameof(count)));
        if (k < 0 || k > range) throw new ArgumentException("the count must be between 0 and the number of integers in the range", nameof(count));
        if (range <= 10_000_000 && k > range / 16)
        {
            var all = new long[(int)range];
            for (int i = 0; i < all.Length; i++) all[i] = lower + i;
            return Sample(all, k);
        }
        var seen = new HashSet<long>();
        var result = new long[k];
        for (int i = 0; i < k;)
        {
            long v = NextInt(lower, upper);
            if (seen.Add(v)) result[i++] = v;
        }
        return result;
    }
}
