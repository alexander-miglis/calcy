using System.Numerics;

namespace Calcy.Core.Numbers;

/// <summary>Exact integer algorithms: primality, factorization, integer roots, combinatorics.</summary>
public static class IntegerMath
{
    private static readonly int[] SmallPrimes = BuildSmallPrimes(1000);

    private static int[] BuildSmallPrimes(int limit)
    {
        var sieve = new bool[limit + 1];
        var list = new List<int>();
        for (int i = 2; i <= limit; i++)
        {
            if (sieve[i]) continue;
            list.Add(i);
            for (int j = i * i; j <= limit; j += i) sieve[j] = true;
        }
        return list.ToArray();
    }

    /// <summary>Floor of the k-th root of a non-negative integer.</summary>
    public static BigInteger IntegerRoot(BigInteger n, int k)
    {
        if (n.Sign < 0) throw new ArgumentException("negative radicand");
        if (n.IsZero || n.IsOne || k == 1) return n;
        // Initial guess from the bit length, then Newton iteration from above.
        long bits = (long)n.GetBitLength();
        var x = BigInteger.One << (int)((bits + k - 1) / k);
        while (true)
        {
            var xk1 = BigInteger.Pow(x, k - 1);
            var y = ((k - 1) * x + n / xk1) / k;
            if (y >= x) break;
            x = y;
        }
        while (BigInteger.Pow(x, k) > n) x--;
        while (BigInteger.Pow(x + 1, k) <= n) x++;
        return x;
    }

    public static bool IsPerfectPower(BigInteger n, int k, out BigInteger root)
    {
        if (n.Sign < 0)
        {
            if (k % 2 == 0) { root = 0; return false; }
            bool ok = IsPerfectPower(-n, k, out var r);
            root = -r;
            return ok;
        }
        root = IntegerRoot(n, k);
        return BigInteger.Pow(root, k) == n;
    }

    /// <summary>
    /// Writes n = outside^k * inside where inside has no k-th power factor found by trial division
    /// (complete for |n| up to about 10^12, best effort beyond). n must be positive.
    /// </summary>
    public static (BigInteger Outside, BigInteger Inside) ExtractPower(BigInteger n, int k)
    {
        if (n.Sign <= 0) throw new ArgumentException("ExtractPower requires a positive integer");
        if (IsPerfectPower(n, k, out var whole)) return (whole, BigInteger.One);
        BigInteger outside = BigInteger.One, inside = BigInteger.One, rest = n;
        foreach (var (p, e) in Factorize(n, trialOnlyLimitBits: 128))
        {
            var q = e / k;
            var r = e % k;
            if (q > 0) outside *= BigInteger.Pow(p, q);
            if (r > 0) inside *= BigInteger.Pow(p, r);
        }
        return (outside, inside);
    }

    public static bool IsProbablePrime(BigInteger n)
    {
        if (n < 2) return false;
        foreach (var p in SmallPrimes)
        {
            if (n == p) return true;
            if ((n % p).IsZero) return false;
        }
        if (n < 1_000_000) return true;
        // Deterministic Miller-Rabin bases for n < 3.3 * 10^24, probabilistic (but very reliable) beyond.
        int[] bases = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41];
        var d = n - 1;
        int s = 0;
        while (d.IsEven) { d >>= 1; s++; }
        foreach (var a in bases)
        {
            if (a >= n) continue;
            var x = BigInteger.ModPow(a, d, n);
            if (x.IsOne || x == n - 1) continue;
            bool composite = true;
            for (int r = 1; r < s; r++)
            {
                x = BigInteger.ModPow(x, 2, n);
                if (x == n - 1) { composite = false; break; }
            }
            if (composite) return false;
        }
        return true;
    }

    public static BigInteger NextPrime(BigInteger n)
    {
        if (n < 2) return 2;
        var c = n + 1;
        if (c.IsEven && c != 2) c++;
        while (!IsProbablePrime(c)) c += 2;
        return c;
    }

    public static BigInteger PreviousPrime(BigInteger n)
    {
        if (n <= 2) throw new ArgumentException("There is no prime below 2");
        var c = n - 1;
        if (c == 2) return 2;
        if (c.IsEven) c--;
        while (c > 2 && !IsProbablePrime(c)) c -= 2;
        return c;
    }

    /// <summary>Prime factorization as (prime, exponent) pairs in increasing prime order. n must be positive.</summary>
    public static List<(BigInteger Prime, int Exponent)> Factorize(BigInteger n, int trialOnlyLimitBits = int.MaxValue, CancellationToken ct = default)
    {
        if (n.Sign <= 0) throw new ArgumentException("Factorization requires a positive integer");
        var factors = new SortedDictionary<BigInteger, int>();
        var rest = n;
        foreach (var p in SmallPrimes)
        {
            if (rest.IsOne) break;
            while ((rest % p).IsZero)
            {
                rest /= p;
                factors[p] = factors.GetValueOrDefault(p) + 1;
            }
        }
        // Wheel trial division up to a modest bound.
        BigInteger limit = 100_000;
        for (BigInteger p = 1009; p <= limit && p * p <= rest; p += 2)
        {
            while ((rest % p).IsZero)
            {
                rest /= p;
                factors[p] = factors.GetValueOrDefault(p) + 1;
            }
        }
        if (!rest.IsOne)
        {
            if ((long)rest.GetBitLength() > trialOnlyLimitBits)
            {
                factors[rest] = factors.GetValueOrDefault(rest) + 1;
            }
            else
            {
                var stack = new Stack<BigInteger>();
                stack.Push(rest);
                while (stack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var m = stack.Pop();
                    if (m.IsOne) continue;
                    if (IsProbablePrime(m))
                    {
                        factors[m] = factors.GetValueOrDefault(m) + 1;
                        continue;
                    }
                    if (IsPerfectPower(m, 2, out var sq))
                    {
                        stack.Push(sq); stack.Push(sq);
                        continue;
                    }
                    var d = PollardRhoBrent(m, ct);
                    stack.Push(d);
                    stack.Push(m / d);
                }
            }
        }
        return factors.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static BigInteger PollardRhoBrent(BigInteger n, CancellationToken ct)
    {
        if (n.IsEven) return 2;
        var rng = new Random(12345);
        for (int attempt = 0; attempt < 200; attempt++)
        {
            BigInteger y = rng.NextInt64(1, long.MaxValue) % n;
            BigInteger c = rng.NextInt64(1, long.MaxValue) % n;
            BigInteger m = 128, g = 1, r = 1, q = 1, x = 0, ys = 0;
            while (g.IsOne)
            {
                ct.ThrowIfCancellationRequested();
                x = y;
                for (BigInteger i = 0; i < r; i++) y = (y * y + c) % n;
                BigInteger k = 0;
                while (k < r && g.IsOne)
                {
                    ys = y;
                    var lim = BigInteger.Min(m, r - k);
                    for (BigInteger i = 0; i < lim; i++)
                    {
                        y = (y * y + c) % n;
                        q = q * BigInteger.Abs(x - y) % n;
                    }
                    g = BigInteger.GreatestCommonDivisor(q, n);
                    k += m;
                }
                r *= 2;
                if (r > (BigInteger.One << 26)) break;
            }
            if (g == n)
            {
                do
                {
                    ys = (ys * ys + c) % n;
                    g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - ys), n);
                } while (g.IsOne);
            }
            if (!g.IsOne && g != n) return g;
        }
        throw new InvalidOperationException("Factorization failed (number too hard to factor)");
    }

    public static BigInteger Factorial(BigInteger n)
    {
        if (n.Sign < 0) throw new ArgumentException("Factorial is undefined for negative integers");
        if (n > 100_000) throw new OverflowException("Factorial argument too large for exact computation");
        int m = (int)n;
        return ProductRange(1, m);
    }

    private static BigInteger ProductRange(long lo, long hi)
    {
        if (lo > hi) return BigInteger.One;
        if (hi - lo < 16)
        {
            BigInteger r = BigInteger.One;
            for (long i = lo; i <= hi; i++) r *= i;
            return r;
        }
        long mid = (lo + hi) / 2;
        return ProductRange(lo, mid) * ProductRange(mid + 1, hi);
    }

    public static BigInteger DoubleFactorial(BigInteger n)
    {
        if (n.Sign < 0) throw new ArgumentException("Double factorial needs a non-negative integer");
        if (n > 200_000) throw new OverflowException("Argument too large");
        BigInteger r = 1;
        for (var i = n; i > 1; i -= 2) r *= i;
        return r;
    }

    public static BigInteger Binomial(BigInteger n, BigInteger k)
    {
        if (k.Sign < 0) return 0;
        if (n.Sign >= 0 && k > n) return 0;
        if (n.Sign < 0)
        {
            // Generalized: C(n,k) = (-1)^k C(k-n-1, k)
            var v = Binomial(k - n - 1, k);
            return k.IsEven ? v : -v;
        }
        if (k > n - k) k = n - k;
        if (k > 1_000_000) throw new OverflowException("Arguments too large for exact computation");
        BigInteger r = 1;
        for (BigInteger i = 1; i <= k; i++) r = r * (n - k + i) / i;
        return r;
    }

    public static BigInteger Permutations(BigInteger n, BigInteger k)
    {
        if (k.Sign < 0 || n.Sign < 0) throw new ArgumentException("nPr needs non-negative integers");
        if (k > n) return 0;
        if (k > 1_000_000) throw new OverflowException("Arguments too large for exact computation");
        BigInteger r = 1;
        for (var i = n - k + 1; i <= n; i++) r *= i;
        return r;
    }

    public static BigInteger Mod(BigInteger a, BigInteger m)
    {
        if (m.IsZero) throw new DivideByZeroException("Modulo by zero");
        var r = BigInteger.Remainder(a, m);
        if (!r.IsZero && (r.Sign < 0) != (m.Sign < 0)) r += m;
        return r;
    }

    /// <summary>Extended Euclid: returns g = gcd(a, b) and x, y with a x + b y = g.</summary>
    public static (BigInteger G, BigInteger X, BigInteger Y) ExtendedGcd(BigInteger a, BigInteger b)
    {
        BigInteger oldR = a, r = b, oldS = 1, s = 0, oldT = 0, t = 1;
        while (!r.IsZero)
        {
            var q = BigInteger.Divide(oldR, r);
            (oldR, r) = (r, oldR - q * r);
            (oldS, s) = (s, oldS - q * s);
            (oldT, t) = (t, oldT - q * t);
        }
        if (oldR.Sign < 0) { oldR = -oldR; oldS = -oldS; oldT = -oldT; }
        return (oldR, oldS, oldT);
    }

    public static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        var (g, x, _) = ExtendedGcd(Mod(a, m), m);
        if (!g.IsOne) throw new ArgumentException($"{a} has no inverse modulo {m}");
        return Mod(x, m);
    }

    public static BigInteger Totient(BigInteger n)
    {
        if (n.Sign <= 0) throw new ArgumentException("Totient needs a positive integer");
        BigInteger result = n;
        foreach (var (p, _) in Factorize(n)) result = result / p * (p - 1);
        return result;
    }

    public static List<BigInteger> Divisors(BigInteger n)
    {
        if (n.Sign == 0) throw new ArgumentException("Divisors of zero are undefined");
        n = BigInteger.Abs(n);
        var divs = new List<BigInteger> { 1 };
        foreach (var (p, e) in Factorize(n))
        {
            int count = divs.Count;
            BigInteger pk = 1;
            for (int k = 1; k <= e; k++)
            {
                pk *= p;
                for (int i = 0; i < count; i++) divs.Add(divs[i] * pk);
            }
            if (divs.Count > 1_000_000) throw new OverflowException("Too many divisors");
        }
        divs.Sort();
        return divs;
    }

    /// <summary>Chinese remainder theorem for pairwise coprime or compatible moduli. Returns (x, M) or null if inconsistent.</summary>
    public static (BigInteger X, BigInteger M)? ChineseRemainder(IReadOnlyList<BigInteger> residues, IReadOnlyList<BigInteger> moduli)
    {
        BigInteger x = 0, m = 1;
        for (int i = 0; i < residues.Count; i++)
        {
            var a = Mod(residues[i], moduli[i]);
            var n = moduli[i];
            var (g, p, _) = ExtendedGcd(m, n);
            if (!Mod(a - x, g).IsZero) return null;
            var lcm = m / g * n;
            x = Mod(x + (a - x) / g * p % (n / g) * m, lcm);
            m = lcm;
        }
        return (x, m);
    }
}
