using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Calcy.Python.Runtime;

namespace Calcy.Python.Modules;

/// <summary>MT19937 exactly as in CPython's _randommodule.c.</summary>
internal sealed class MersenneTwister
{
    private const int N = 624;
    private const int M = 397;
    private const uint MatrixA = 0x9908b0dfU;
    private const uint UpperMask = 0x80000000U;
    private const uint LowerMask = 0x7fffffffU;

    private readonly uint[] _mt = new uint[N];
    private int _index = N + 1;

    private void InitGenrand(uint s)
    {
        _mt[0] = s;
        for (int i = 1; i < N; i++)
            _mt[i] = unchecked(1812433253U * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i);
        _index = N;
    }

    public void InitByArray(uint[] key)
    {
        InitGenrand(19650218U);
        int i = 1, j = 0;
        int keyLength = key.Length;
        int k = N > keyLength ? N : keyLength;
        unchecked
        {
            for (; k > 0; k--)
            {
                _mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525U)) + key[j] + (uint)j;
                i++;
                j++;
                if (i >= N)
                {
                    _mt[0] = _mt[N - 1];
                    i = 1;
                }
                if (j >= keyLength) j = 0;
            }
            for (k = N - 1; k > 0; k--)
            {
                _mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941U)) - (uint)i;
                i++;
                if (i >= N)
                {
                    _mt[0] = _mt[N - 1];
                    i = 1;
                }
            }
        }
        _mt[0] = 0x80000000U;
    }

    public uint NextUInt32()
    {
        uint y;
        if (_index >= N)
        {
            int kk;
            for (kk = 0; kk < N - M; kk++)
            {
                y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ ((y & 1U) != 0 ? MatrixA : 0U);
            }
            for (; kk < N - 1; kk++)
            {
                y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ ((y & 1U) != 0 ? MatrixA : 0U);
            }
            y = (_mt[N - 1] & UpperMask) | (_mt[0] & LowerMask);
            _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ ((y & 1U) != 0 ? MatrixA : 0U);
            _index = 0;
        }
        y = _mt[_index++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c5680U;
        y ^= (y << 15) & 0xefc60000U;
        y ^= y >> 18;
        return y;
    }

    public double NextDouble()
    {
        uint a = NextUInt32() >> 5, b = NextUInt32() >> 6;
        return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
    }

    public BigInteger GetRandBits(long k)
    {
        if (k < 0) throw PyErr.ValueError("number of bits must be non-negative");
        if (k == 0) return BigInteger.Zero;
        if (k <= 32) return NextUInt32() >> (int)(32 - k);
        Limits.CheckIntBits(k);
        long words = (k - 1) / 32 + 1;
        var bytes = new byte[words * 4 + 1];
        for (long i = 0; i < words; i++, k -= 32)
        {
            uint r = NextUInt32();
            if (k < 32) r >>= (int)(32 - k);
            bytes[i * 4] = (byte)r;
            bytes[i * 4 + 1] = (byte)(r >> 8);
            bytes[i * 4 + 2] = (byte)(r >> 16);
            bytes[i * 4 + 3] = (byte)(r >> 24);
        }
        return new BigInteger(bytes);
    }

    public void Seed(BigInteger n)
    {
        n = BigInteger.Abs(n);
        long bits = (long)n.GetBitLength();
        int keyUsed = bits == 0 ? 1 : (int)((bits - 1) / 32 + 1);
        var bytes = n.ToByteArray(isUnsigned: true, isBigEndian: false);
        var key = new uint[keyUsed];
        for (int i = 0; i < keyUsed; i++)
        {
            uint w = 0;
            for (int b = 0; b < 4; b++)
            {
                int idx = i * 4 + b;
                if (idx < bytes.Length) w |= (uint)bytes[idx] << (8 * b);
            }
            key[i] = w;
        }
        InitByArray(key);
    }

    public void SeedFromEntropy()
    {
        var key = new uint[N];
        var bytes = new byte[N * 4];
        RandomNumberGenerator.Fill(bytes);
        Buffer.BlockCopy(bytes, 0, key, 0, bytes.Length);
        InitByArray(key);
    }

    public PyTuple GetState()
    {
        var items = new PyObject[N + 1];
        for (int i = 0; i < N; i++) items[i] = PyInt.From(_mt[i]);
        items[N] = PyInt.From(_index);
        return new PyTuple(items);
    }

    public void SetState(PyTuple t)
    {
        if (t.Count != N + 1) throw PyErr.ValueError("state vector is the wrong size");
        for (int i = 0; i < N; i++) _mt[i] = (uint)Ops.Index(t.Items[i]).ToBig();
        long idx = Ops.IndexLong(t.Items[N]);
        if (idx < 0 || idx > N) throw PyErr.ValueError("invalid state");
        _index = (int)idx;
    }
}

/// <summary>An instance of random.Random.</summary>
internal sealed class PyRandom : PyObject
{
    public readonly MersenneTwister Mt = new();
    public PyObject GaussNext = Py.None;

    internal override PyType Type => RandomModule.RandomType;

    public void Seed(PyObject? a)
    {
        if (a == null || a == Py.None) Mt.SeedFromEntropy();
        else if (a is PyInt i) Mt.Seed(i.ToBig());
        else if (a is PyStr s)
        {
            var b = Encoding.UTF8.GetBytes(s.V);
            var digest = SHA512.HashData(b);
            var all = new byte[b.Length + digest.Length];
            Array.Copy(b, all, b.Length);
            Array.Copy(digest, 0, all, b.Length, digest.Length);
            Mt.Seed(new BigInteger(all, isUnsigned: true, isBigEndian: true));
        }
        else if (a is PyFloat)
        {
            ulong h = (ulong)Ops.Hash(a);
            Mt.Seed(new BigInteger(h));
        }
        else throw PyErr.TypeError("The only supported seed types are: None,\nint, float, str, bytes, and bytearray.");
        GaussNext = Py.None;
    }

    public double Random() => Mt.NextDouble();

    public BigInteger RandBelow(BigInteger n)
    {
        if (n.Sign <= 0) return BigInteger.Zero;
        long k = (long)n.GetBitLength();
        var r = Mt.GetRandBits(k);
        while (r >= n) r = Mt.GetRandBits(k);
        return r;
    }

    public long RandBelow(long n) => (long)RandBelow(new BigInteger(n));
}

internal static class RandomModule
{
    public static readonly PyType RandomType = CreateType();

    private static readonly double NvMagicConst = 4 * Math.Exp(-0.5) / Math.Sqrt(2.0);
    private static readonly double Log4 = Math.Log(4.0);
    private static readonly double SgMagicConst = 1.0 + Math.Log(4.5);
    private const double TwoPi = 2.0 * Math.PI;

    private static PyRandom R(PyObject o) => (PyRandom)o;

    private static PyType CreateType()
    {
        var t = new PyType("Random", PyTypes.Object, "random", final: true);
        t.Ctor = (type, a, kw) =>
        {
            var r = new PyRandom();
            r.Seed(a.Length > 0 ? a[0] : null);
            return r;
        };
        t.Method("seed", (self, a, kw) =>
        {
            var b = new Sig("seed", 0, 0, 2, "a", "version").Bind(a, kw);
            R(self).Seed(b[0]);
            return Py.None;
        });
        t.Method0("random", self => new PyFloat(R(self).Random()));
        t.Method1("getrandbits", (self, k) => PyInt.From(R(self).Mt.GetRandBits(Ops.IndexLong(k))));
        t.Method("randrange", (self, a, kw) =>
        {
            var b = new Sig("randrange", 1, 0, 3, "start", "stop", "step").Bind(a, kw);
            return RandRange(R(self), b[0]!, b[1], b[2]);
        });
        t.Method2("randint", (self, x, y) => RandRange(R(self), x, IntMath.Add(Ops.Index(y), PyInt.One), null));
        t.Method1("choice", (self, seq) =>
        {
            long n = Ops.Len(seq);
            if (n == 0) throw PyErr.IndexError("Cannot choose from an empty sequence");
            return Ops.GetItem(seq, PyInt.From(R(self).RandBelow(n)));
        });
        t.Method1("shuffle", (self, x) =>
        {
            var r = R(self);
            long n = Ops.Len(x);
            if (x is PyList l)
            {
                for (long i = n - 1; i >= 1; i--)
                {
                    long j = r.RandBelow(i + 1);
                    (l.Items[i], l.Items[j]) = (l.Items[j], l.Items[i]);
                }
                return Py.None;
            }
            for (long i = n - 1; i >= 1; i--)
            {
                long j = r.RandBelow(i + 1);
                var pi = PyInt.From(i);
                var pj = PyInt.From(j);
                var vi = Ops.GetItem(x, pi);
                var vj = Ops.GetItem(x, pj);
                Ops.SetItem(x, pi, vj);
                Ops.SetItem(x, pj, vi);
            }
            return Py.None;
        });
        t.Method("sample", (self, a, kw) =>
        {
            var b = new Sig("sample", 2, 0, 2, "population", "k", "counts").Bind(a, kw);
            return Sample(R(self), b[0]!, b[1]!, b[2]);
        });
        t.Method("choices", (self, a, kw) =>
        {
            var b = new Sig("choices", 1, 0, 2, "population", "weights", "cum_weights", "k").Bind(a, kw);
            return Choices(R(self), b[0]!, b[1], b[2], b[3]);
        });
        t.Method2("uniform", (self, x, y) =>
        {
            double lo = Ops.ToDouble(x), hi = Ops.ToDouble(y);
            return new PyFloat(lo + (hi - lo) * R(self).Random());
        });
        t.Method("triangular", (self, a, kw) =>
        {
            var b = new Sig("triangular", 0, 0, 3, "low", "high", "mode").Bind(a, kw);
            double low = b[0] == null ? 0.0 : Ops.ToDouble(b[0]!);
            double high = b[1] == null ? 1.0 : Ops.ToDouble(b[1]!);
            double u = R(self).Random();
            double c;
            if (b[2] == null || b[2] == Py.None) c = 0.5;
            else
            {
                if (high - low == 0) return new PyFloat(low);
                c = (Ops.ToDouble(b[2]!) - low) / (high - low);
            }
            if (u > c)
            {
                u = 1.0 - u;
                c = 1.0 - c;
                (low, high) = (high, low);
            }
            return new PyFloat(low + (high - low) * Math.Sqrt(u * c));
        });
        t.Method("gauss", (self, a, kw) =>
        {
            var b = new Sig("gauss", 0, 0, 2, "mu", "sigma").Bind(a, kw);
            double mu = b[0] == null ? 0.0 : Ops.ToDouble(b[0]!);
            double sigma = b[1] == null ? 1.0 : Ops.ToDouble(b[1]!);
            var r = R(self);
            double z;
            if (r.GaussNext is PyFloat gn)
            {
                z = gn.V;
                r.GaussNext = Py.None;
            }
            else
            {
                double x2pi = r.Random() * TwoPi;
                double g2rad = Math.Sqrt(-2.0 * Math.Log(1.0 - r.Random()));
                z = Math.Cos(x2pi) * g2rad;
                r.GaussNext = new PyFloat(Math.Sin(x2pi) * g2rad);
            }
            return new PyFloat(mu + z * sigma);
        });
        t.Method("normalvariate", (self, a, kw) =>
        {
            var b = new Sig("normalvariate", 0, 0, 2, "mu", "sigma").Bind(a, kw);
            double mu = b[0] == null ? 0.0 : Ops.ToDouble(b[0]!);
            double sigma = b[1] == null ? 1.0 : Ops.ToDouble(b[1]!);
            return new PyFloat(mu + NormalZ(R(self)) * sigma);
        });
        t.Method2("lognormvariate", (self, mu, sigma) =>
            new PyFloat(Math.Exp(Ops.ToDouble(mu) + NormalZ(R(self)) * Ops.ToDouble(sigma))));
        t.Method("expovariate", (self, a, kw) =>
        {
            var b = new Sig("expovariate", 0, 0, 1, "lambd").Bind(a, kw);
            double lambd = b[0] == null ? 1.0 : Ops.ToDouble(b[0]!);
            return new PyFloat(-Math.Log(1.0 - R(self).Random()) / lambd);
        });
        t.Method2("gammavariate", (self, alpha, beta) =>
            new PyFloat(Gamma(R(self), Ops.ToDouble(alpha), Ops.ToDouble(beta))));
        t.Method2("betavariate", (self, alpha, beta) =>
        {
            var r = R(self);
            double y = Gamma(r, Ops.ToDouble(alpha), 1.0);
            if (y != 0) return new PyFloat(y / (y + Gamma(r, Ops.ToDouble(beta), 1.0)));
            return PyFloat.Zero;
        });
        t.Method1("paretovariate", (self, alpha) =>
        {
            double u = 1.0 - R(self).Random();
            return new PyFloat(Math.Pow(u, -1.0 / Ops.ToDouble(alpha)));
        });
        t.Method2("weibullvariate", (self, alpha, beta) =>
        {
            double u = 1.0 - R(self).Random();
            return new PyFloat(Ops.ToDouble(alpha) * Math.Pow(-Math.Log(u), 1.0 / Ops.ToDouble(beta)));
        });
        t.Method0("getstate", self => new PyTuple(new PyObject[] { PyInt.From(3), R(self).Mt.GetState(), R(self).GaussNext }));
        t.Method1("setstate", (self, st) =>
        {
            if (st is not PyTuple tup || tup.Count != 3 || tup.Items[1] is not PyTuple inner)
                throw PyErr.ValueError("state with version other than 3 is not supported");
            R(self).Mt.SetState(inner);
            R(self).GaussNext = tup.Items[2];
            return Py.None;
        });
        return t;
    }

    private static double NormalZ(PyRandom r)
    {
        while (true)
        {
            double u1 = r.Random();
            double u2 = 1.0 - r.Random();
            double z = NvMagicConst * (u1 - 0.5) / u2;
            double zz = z * z / 4.0;
            if (zz <= -Math.Log(u2)) return z;
        }
    }

    private static double Gamma(PyRandom r, double alpha, double beta)
    {
        if (alpha <= 0.0 || beta <= 0.0) throw PyErr.ValueError("gammavariate: alpha and beta must be > 0.0");
        if (alpha > 1.0)
        {
            double ainv = Math.Sqrt(2.0 * alpha - 1.0);
            double bbb = alpha - Log4;
            double ccc = alpha + ainv;
            while (true)
            {
                Interp.CurrentOrNull?.Tick();
                double u1 = r.Random();
                if (!(1e-7 < u1 && u1 < 0.9999999)) continue;
                double u2 = 1.0 - r.Random();
                double v = Math.Log(u1 / (1.0 - u1)) / ainv;
                double x = alpha * Math.Exp(v);
                double z = u1 * u1 * u2;
                double rr = bbb + ccc * v - x;
                if (rr + SgMagicConst - 4.5 * z >= 0.0 || rr >= Math.Log(z)) return x * beta;
            }
        }
        if (alpha == 1.0) return -Math.Log(1.0 - r.Random()) * beta;
        double xx;
        while (true)
        {
            Interp.CurrentOrNull?.Tick();
            double u = r.Random();
            double bb = (Math.E + alpha) / Math.E;
            double p = bb * u;
            xx = p <= 1.0 ? Math.Pow(p, 1.0 / alpha) : -Math.Log((bb - p) / alpha);
            double u1 = r.Random();
            if (p > 1.0)
            {
                if (u1 <= Math.Pow(xx, alpha - 1.0)) break;
            }
            else if (u1 <= Math.Exp(-xx)) break;
        }
        return xx * beta;
    }

    private static PyObject RandRange(PyRandom r, PyObject start, PyObject? stop, PyObject? step)
    {
        var istart = Ops.Index(start).ToBig();
        if (stop == null || stop == Py.None)
        {
            if (step != null && step != Py.None && !(step is PyInt si && !si.IsBig && si.V == 1))
                throw PyErr.TypeError("Missing a non-None stop argument");
            if (istart > 0) return PyInt.From(r.RandBelow(istart));
            throw PyErr.ValueError("empty range for randrange()");
        }
        var istop = Ops.Index(stop).ToBig();
        var width = istop - istart;
        var istep = step == null || step == Py.None ? BigInteger.One : Ops.Index(step).ToBig();
        if (istep.IsOne)
        {
            if (width > 0) return PyInt.From(istart + r.RandBelow(width));
            throw PyErr.ValueError($"empty range in randrange({Ops.Repr(start)}, {Ops.Repr(stop)})");
        }
        BigInteger n;
        if (istep > 0) n = FloorDiv(width + istep - 1, istep);
        else if (istep < 0) n = FloorDiv(width + istep + 1, istep);
        else throw PyErr.ValueError("zero step for randrange()");
        if (n <= 0) throw PyErr.ValueError($"empty range in randrange({Ops.Repr(start)}, {Ops.Repr(stop)}, {Ops.Repr(step!)})");
        return PyInt.From(istart + istep * r.RandBelow(n));
    }

    private static BigInteger FloorDiv(BigInteger a, BigInteger b)
    {
        var q = BigInteger.DivRem(a, b, out var rem);
        if (!rem.IsZero && rem.Sign != b.Sign) q -= 1;
        return q;
    }

    private static PyObject Sample(PyRandom r, PyObject population, PyObject kObj, PyObject? counts)
    {
        if (population is PySetBase or PyDict)
            throw PyErr.TypeError("Population must be a sequence.  For dicts or sets, use sorted(d).");
        PyObject pop = population;
        if (counts != null && counts != Py.None)
        {
            var cl = Ops.ToArray(counts);
            var items = Ops.ToArray(population);
            if (cl.Length != items.Length) throw PyErr.ValueError("The number of counts does not match the population");
            var expanded = new List<PyObject>();
            for (int i = 0; i < items.Length; i++)
            {
                long c = Ops.IndexLong(cl[i]);
                if (c < 0) throw PyErr.ValueError("Counts must be non-negative");
                for (long j = 0; j < c; j++) expanded.Add(items[i]);
                Limits.CheckSize(expanded.Count);
            }
            pop = new PyList(expanded);
        }
        long n = Ops.Len(pop);
        long k = Ops.IndexLong(kObj);
        if (k < 0 || k > n) throw PyErr.ValueError("Sample larger than population or is negative");
        Limits.CheckSize(k);
        var result = new PyObject[k];
        long setsize = 21;
        if (k > 5) setsize += (long)Math.Pow(4, Math.Ceiling(Math.Log(k * 3) / Math.Log(4)));
        if (n <= setsize)
        {
            var pool = (PyObject[])Ops.ToArray(pop).Clone();
            for (long i = 0; i < k; i++)
            {
                long j = r.RandBelow(n - i);
                result[i] = pool[j];
                pool[j] = pool[n - i - 1];
            }
        }
        else
        {
            var selected = new HashSet<long>();
            for (long i = 0; i < k; i++)
            {
                long j = r.RandBelow(n);
                while (selected.Contains(j)) j = r.RandBelow(n);
                selected.Add(j);
                result[i] = Ops.GetItem(pop, PyInt.From(j));
            }
        }
        return PyList.Wrap(result);
    }

    private static PyObject Choices(PyRandom r, PyObject population, PyObject? weights, PyObject? cumWeights, PyObject? kObj)
    {
        long n = Ops.Len(population);
        long k = kObj == null ? 1 : Ops.IndexLong(kObj);
        Limits.CheckSize(k);
        var result = new PyObject[Math.Max(0, k)];
        if (cumWeights == null || cumWeights == Py.None)
        {
            if (weights == null || weights == Py.None)
            {
                double nf = n;
                for (long i = 0; i < k; i++) result[i] = Ops.GetItem(population, PyInt.From((long)Math.Floor(r.Random() * nf)));
                return PyList.Wrap(result);
            }
            var cum = new List<PyObject>();
            PyObject acc = PyInt.Zero;
            bool first = true;
            foreach (var w in Ops.Iterate(weights))
            {
                acc = first ? w : Arith.Add(acc, w);
                first = false;
                cum.Add(acc);
            }
            cumWeights = new PyList(cum);
        }
        else if (weights != null && weights != Py.None) throw PyErr.TypeError("Cannot specify both weights and cumulative weights");
        var cw = Ops.ToArray(cumWeights);
        if (cw.Length != n) throw PyErr.ValueError("The number of weights does not match the population");
        double total = Ops.ToDouble(cw[^1]) + 0.0;
        if (total <= 0.0) throw PyErr.ValueError("Total of weights must be greater than zero");
        if (!double.IsFinite(total)) throw PyErr.ValueError("Total of weights must be finite");
        long hi = n - 1;
        var cwd = cw.Select(Ops.ToDouble).ToArray();
        for (long i = 0; i < k; i++)
        {
            double x = r.Random() * total;
            long lo = 0, h = hi;
            while (lo < h)
            {
                long mid = (lo + h) / 2;
                if (x < cwd[mid]) h = mid;
                else lo = mid + 1;
            }
            result[i] = Ops.GetItem(population, PyInt.From(lo));
        }
        return PyList.Wrap(result);
    }

    public static PyModule Create()
    {
        var m = new PyModule("random", new Namespace(), true);
        m.Add("Random", RandomType);
        var inst = new PyRandom();
        inst.Seed(null);
        foreach (var name in new[]
                 {
                     "seed", "random", "getrandbits", "randrange", "randint", "choice", "shuffle", "sample", "choices", "uniform",
                     "triangular", "gauss", "normalvariate", "lognormvariate", "expovariate", "gammavariate", "betavariate",
                     "paretovariate", "weibullvariate", "getstate", "setstate",
                 })
        {
            var bm = (PyBuiltinMethod)RandomType.Dict[name];
            m.Add(name, new PyBuiltinFunction(name, (a, kw) => bm.Fn(inst, a, kw), "random"));
        }
        return m;
    }
}
