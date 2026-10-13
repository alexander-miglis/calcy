using System.Numerics;
using System.Text;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Polynomials;

/// <summary>An exponent vector (monomial) with a cached hash; compared lexicographically from variable 0.</summary>
internal readonly struct Mono : IEquatable<Mono>, IComparable<Mono>
{
    private readonly int[] _e;
    private readonly int _hash;

    public Mono(int[] exponents)
    {
        _e = exponents;
        var h = new HashCode();
        foreach (var x in exponents) h.Add(x);
        _hash = h.ToHashCode();
    }

    public static Mono One(int n) => new(new int[n]);

    public static Mono Var(int n, int v, int power = 1)
    {
        var e = new int[n];
        e[v] = power;
        return new Mono(e);
    }

    public int this[int i] => _e[i];
    public int Length => _e.Length;

    public int Total
    {
        get
        {
            int t = 0;
            foreach (var x in _e) t += x;
            return t;
        }
    }

    public bool IsOne => _e.All(x => x == 0);

    public int[] ToArray() => (int[])_e.Clone();

    public static Mono operator *(Mono a, Mono b)
    {
        var r = new int[a._e.Length];
        for (int i = 0; i < r.Length; i++) r[i] = a._e[i] + b._e[i];
        return new Mono(r);
    }

    public bool Divides(Mono other)
    {
        for (int i = 0; i < _e.Length; i++)
            if (_e[i] > other._e[i]) return false;
        return true;
    }

    /// <summary>other / this (requires Divides).</summary>
    public Mono DivideInto(Mono other)
    {
        var r = new int[_e.Length];
        for (int i = 0; i < r.Length; i++) r[i] = other._e[i] - _e[i];
        return new Mono(r);
    }

    public Mono With(int v, int power)
    {
        var r = (int[])_e.Clone();
        r[v] = power;
        return new Mono(r);
    }

    public bool Equals(Mono other)
    {
        if (_hash != other._hash || _e.Length != other._e.Length) return false;
        for (int i = 0; i < _e.Length; i++)
            if (_e[i] != other._e[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is Mono m && Equals(m);
    public override int GetHashCode() => _hash;

    public int CompareTo(Mono other)
    {
        for (int i = 0; i < _e.Length; i++)
        {
            int c = _e[i].CompareTo(other._e[i]);
            if (c != 0) return c;
        }
        return 0;
    }
}

/// <summary>Sparse multivariate polynomial over Q in a fixed number of variables (indices into a kernel map).</summary>
internal sealed class MPoly
{
    private readonly Dictionary<Mono, BigRational> _terms;
    private (Mono Mono, BigRational Coef)? _lead;

    public int Vars { get; }

    private MPoly(int vars, Dictionary<Mono, BigRational> terms)
    {
        Vars = vars;
        _terms = terms;
    }

    public static MPoly Zero(int n) => new(n, []);

    public static MPoly Constant(int n, BigRational c)
    {
        var d = new Dictionary<Mono, BigRational>();
        if (!c.IsZero) d[Mono.One(n)] = c;
        return new MPoly(n, d);
    }

    public static MPoly Var(int n, int v, int power = 1)
    {
        var d = new Dictionary<Mono, BigRational> { [Mono.Var(n, v, power)] = BigRational.One };
        return new MPoly(n, d);
    }

    public static MPoly FromTerms(int n, IEnumerable<(Mono, BigRational)> terms)
    {
        var d = new Dictionary<Mono, BigRational>();
        foreach (var (m, c) in terms)
        {
            if (c.IsZero) continue;
            if (d.TryGetValue(m, out var old))
            {
                var s = old + c;
                if (s.IsZero) d.Remove(m); else d[m] = s;
            }
            else d[m] = c;
        }
        return new MPoly(n, d);
    }

    public IEnumerable<(Mono Mono, BigRational Coef)> Terms => _terms.Select(kv => (kv.Key, kv.Value));
    public int TermCount => _terms.Count;
    public bool IsZero => _terms.Count == 0;
    public bool IsConstant => _terms.Count == 0 || (_terms.Count == 1 && _terms.Keys.First().IsOne);
    public bool IsOne => IsConstant && ConstantValue.IsOne;

    /// <summary>The value of a constant polynomial (zero for the zero polynomial).</summary>
    public BigRational ConstantValue => _terms.TryGetValue(Mono.One(Vars), out var c) ? c : BigRational.Zero;

    public BigRational Coefficient(Mono m) => _terms.TryGetValue(m, out var c) ? c : BigRational.Zero;

    /// <summary>Leading term in lexicographic order (variable 0 most significant).</summary>
    public (Mono Mono, BigRational Coef) Lead
    {
        get
        {
            if (_lead is { } l) return l;
            if (_terms.Count == 0) throw new InvalidOperationException("zero polynomial");
            Mono best = default;
            BigRational bc = BigRational.Zero;
            bool first = true;
            foreach (var kv in _terms)
            {
                if (first || kv.Key.CompareTo(best) > 0)
                {
                    best = kv.Key;
                    bc = kv.Value;
                    first = false;
                }
            }
            _lead = (best, bc);
            return _lead.Value;
        }
    }

    public int Degree(int v)
    {
        int d = _terms.Count == 0 ? -1 : 0;
        foreach (var m in _terms.Keys)
            if (m[v] > d) d = m[v];
        return d;
    }

    public int MinDegree(int v)
    {
        int d = int.MaxValue;
        foreach (var m in _terms.Keys)
            if (m[v] < d) d = m[v];
        return d == int.MaxValue ? 0 : d;
    }

    public int TotalDegree
    {
        get
        {
            int d = _terms.Count == 0 ? -1 : 0;
            foreach (var m in _terms.Keys) d = Math.Max(d, m.Total);
            return d;
        }
    }

    public bool Contains(int v)
    {
        foreach (var m in _terms.Keys)
            if (m[v] > 0) return true;
        return false;
    }

    /// <summary>Indices of the variables that occur.</summary>
    public List<int> UsedVars()
    {
        var used = new List<int>();
        for (int v = 0; v < Vars; v++)
            if (Contains(v)) used.Add(v);
        return used;
    }

    public bool IsHomogeneous
    {
        get
        {
            int t = -1;
            foreach (var m in _terms.Keys)
            {
                if (t < 0) t = m.Total;
                else if (m.Total != t) return false;
            }
            return true;
        }
    }

    // ------------------------------------------------------------------ arithmetic

    public static MPoly operator +(MPoly a, MPoly b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        var d = new Dictionary<Mono, BigRational>(a._terms);
        foreach (var kv in b._terms)
        {
            if (d.TryGetValue(kv.Key, out var old))
            {
                var s = old + kv.Value;
                if (s.IsZero) d.Remove(kv.Key); else d[kv.Key] = s;
            }
            else d[kv.Key] = kv.Value;
        }
        return new MPoly(a.Vars, d);
    }

    public static MPoly operator -(MPoly a) => new(a.Vars, a._terms.ToDictionary(kv => kv.Key, kv => -kv.Value));

    public static MPoly operator -(MPoly a, MPoly b) => a + -b;

    public static MPoly operator *(MPoly a, MPoly b)
    {
        if (a.IsZero || b.IsZero) return Zero(a.Vars);
        if ((long)a._terms.Count * b._terms.Count > 4_000_000) throw new CasLimitException("The polynomial is too large");
        var d = new Dictionary<Mono, BigRational>();
        foreach (var x in a._terms)
        {
            CasEnv.Check();
            foreach (var y in b._terms)
            {
                var m = x.Key * y.Key;
                var c = x.Value * y.Value;
                if (d.TryGetValue(m, out var old))
                {
                    var s = old + c;
                    if (s.IsZero) d.Remove(m); else d[m] = s;
                }
                else d[m] = c;
            }
        }
        if (d.Count > 200_000) throw new CasLimitException("The polynomial is too large");
        return new MPoly(a.Vars, d);
    }

    public MPoly Scale(BigRational s)
    {
        if (s.IsZero) return Zero(Vars);
        if (s.IsOne) return this;
        return new MPoly(Vars, _terms.ToDictionary(kv => kv.Key, kv => kv.Value * s));
    }

    public MPoly MulMono(Mono m, BigRational c)
    {
        if (c.IsZero) return Zero(Vars);
        return new MPoly(Vars, _terms.ToDictionary(kv => kv.Key * m, kv => kv.Value * c));
    }

    public MPoly Pow(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 0) return Constant(Vars, BigRational.One);
        if (_terms.Count == 1)
        {
            var (m, c) = Lead;
            var e = m.ToArray();
            for (int i = 0; i < e.Length; i++) e[i] *= n;
            return FromTerms(Vars, [(new Mono(e), c.Pow(n))]);
        }
        if ((long)TotalDegree * n > 4000) throw new CasLimitException("The power is too large to expand");
        MPoly result = Constant(Vars, BigRational.One), b = this;
        while (n > 0)
        {
            if ((n & 1) != 0) result *= b;
            n >>= 1;
            if (n > 0) b *= b;
        }
        return result;
    }

    public MPoly Diff(int v)
    {
        var terms = new List<(Mono, BigRational)>();
        foreach (var kv in _terms)
        {
            int e = kv.Key[v];
            if (e == 0) continue;
            terms.Add((kv.Key.With(v, e - 1), kv.Value * e));
        }
        return FromTerms(Vars, terms);
    }

    /// <summary>Substitutes a rational value for a variable.</summary>
    public MPoly Substitute(int v, BigRational value)
    {
        var terms = new List<(Mono, BigRational)>();
        foreach (var kv in _terms)
        {
            int e = kv.Key[v];
            terms.Add((kv.Key.With(v, 0), e == 0 ? kv.Value : kv.Value * value.Pow(e)));
        }
        return FromTerms(Vars, terms);
    }

    /// <summary>Substitutes a polynomial for a variable.</summary>
    public MPoly Substitute(int v, MPoly value)
    {
        var acc = Zero(Vars);
        foreach (var (k, c) in CoefficientsIn(v)) acc += c * value.Pow(k);
        return acc;
    }

    /// <summary>The coefficients of the powers of variable v (coefficient polynomials do not contain v).</summary>
    public SortedDictionary<int, MPoly> CoefficientsIn(int v)
    {
        var groups = new Dictionary<int, List<(Mono, BigRational)>>();
        foreach (var kv in _terms)
        {
            int e = kv.Key[v];
            if (!groups.TryGetValue(e, out var list)) groups[e] = list = [];
            list.Add((kv.Key.With(v, 0), kv.Value));
        }
        var result = new SortedDictionary<int, MPoly>();
        foreach (var (e, list) in groups) result[e] = FromTerms(Vars, list);
        return result;
    }

    public MPoly CoefficientOf(int v, int power)
    {
        var list = new List<(Mono, BigRational)>();
        foreach (var kv in _terms)
            if (kv.Key[v] == power) list.Add((kv.Key.With(v, 0), kv.Value));
        return FromTerms(Vars, list);
    }

    /// <summary>Leading coefficient with respect to variable v.</summary>
    public MPoly LeadingCoefficient(int v) => CoefficientOf(v, Degree(v));

    public static MPoly FromCoefficients(int n, int v, IEnumerable<KeyValuePair<int, MPoly>> coeffs)
    {
        var terms = new List<(Mono, BigRational)>();
        foreach (var (k, c) in coeffs)
            foreach (var (m, cc) in c.Terms) terms.Add((m.With(v, m[v] + k), cc));
        return FromTerms(n, terms);
    }

    // ------------------------------------------------------------------ division

    /// <summary>Exact quotient this / b, or null when b does not divide this.</summary>
    public MPoly? DivExact(MPoly b)
    {
        if (b.IsZero) throw new DivideByZeroException();
        if (IsZero) return this;
        if (b.IsConstant) return Scale(b.ConstantValue.Reciprocal());
        var (bm, bc) = b.Lead;
        var r = this;
        var q = new List<(Mono, BigRational)>();
        int guard = 0;
        while (!r.IsZero)
        {
            if (++guard > 100_000) throw new CasLimitException("The polynomial division is too large");
            CasEnv.Check();
            var (rm, rc) = r.Lead;
            if (!bm.Divides(rm)) return null;
            var m = bm.DivideInto(rm);
            var c = rc / bc;
            q.Add((m, c));
            r -= b.MulMono(m, c);
        }
        return FromTerms(Vars, q);
    }

    /// <summary>Pseudo-remainder of this by b with respect to variable v.</summary>
    public MPoly PseudoRemainder(MPoly b, int v)
    {
        int db = b.Degree(v);
        var lcb = b.LeadingCoefficient(v);
        var r = this;
        int guard = 0;
        while (!r.IsZero && r.Degree(v) >= db)
        {
            if (++guard > 10_000) throw new CasLimitException("The polynomial division is too large");
            CasEnv.Check();
            int dr = r.Degree(v);
            var lcr = r.LeadingCoefficient(v);
            r = lcb * r - (lcr * b).MulMono(Mono.Var(Vars, v, dr - db), BigRational.One);
        }
        return r;
    }

    // ------------------------------------------------------------------ content and gcd

    /// <summary>Positive rational c and primitive integer polynomial p with this = c·p and the leading coefficient of p positive.</summary>
    public (BigRational Content, MPoly Primitive) RationalContent()
    {
        if (IsZero) return (BigRational.Zero, this);
        BigInteger lcm = BigInteger.One, g = BigInteger.Zero;
        foreach (var c in _terms.Values) lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, c.Den) * c.Den;
        foreach (var c in _terms.Values) g = BigInteger.GreatestCommonDivisor(g, c.Num * (lcm / c.Den));
        var content = new BigRational(g, lcm);
        if (Lead.Coef.IsNegative) content = -content;
        return (content, Scale(content.Reciprocal()));
    }

    /// <summary>Primitive integer form with a positive leading coefficient (the canonical associate).</summary>
    public MPoly Normalized() => IsZero ? this : RationalContent().Primitive;

    /// <summary>Greatest common divisor over Q[vars], normalized (primitive, integer, positive leading coefficient).</summary>
    public static MPoly Gcd(MPoly a, MPoly b)
    {
        using var _ = CasEnv.Deeper(400);
        if (a.IsZero) return b.Normalized();
        if (b.IsZero) return a.Normalized();
        if (a.IsConstant || b.IsConstant) return Constant(a.Vars, BigRational.One);
        int v = -1;
        for (int i = 0; i < a.Vars; i++)
            if (a.Contains(i) || b.Contains(i)) { v = i; break; }
        bool inA = a.Contains(v), inB = b.Contains(v);
        if (!inA) return Gcd(a, b.ContentIn(v));
        if (!inB) return Gcd(a.ContentIn(v), b);
        if (a.UsedVars().Count == 1 && b.UsedVars().Count == 1 && a.UsedVars()[0] == b.UsedVars()[0])
            return FromQPoly(a.Vars, v, QPoly.Gcd(a.ToQPoly(v), b.ToQPoly(v))).Normalized();
        var ca = a.ContentIn(v);
        var cb = b.ContentIn(v);
        var pa = a.DivExact(ca)!;
        var pb = b.DivExact(cb)!;
        var c = Gcd(ca, cb);
        if (pa.Degree(v) < pb.Degree(v)) (pa, pb) = (pb, pa);
        while (true)
        {
            CasEnv.Check();
            var r = pa.PseudoRemainder(pb, v);
            if (r.IsZero) break;
            if (r.Degree(v) == 0)
            {
                pb = Constant(a.Vars, BigRational.One);
                break;
            }
            pa = pb;
            pb = r.PrimitiveIn(v);
        }
        return (c * pb.PrimitiveIn(v)).Normalized();
    }

    /// <summary>Gcd of the coefficients with respect to v (a polynomial without v).</summary>
    public MPoly ContentIn(int v)
    {
        MPoly? g = null;
        foreach (var c in CoefficientsIn(v).Values)
        {
            g = g is null ? c.Normalized() : Gcd(g, c);
            if (g.IsConstant) return Constant(Vars, BigRational.One);
        }
        return g ?? Zero(Vars);
    }

    public MPoly PrimitiveIn(int v)
    {
        if (IsZero) return this;
        var c = ContentIn(v);
        return (c.IsConstant ? this : DivExact(c)!).Normalized();
    }

    // ------------------------------------------------------------------ univariate views

    public QPoly ToQPoly(int v)
    {
        int d = Degree(v);
        var c = new BigRational[Math.Max(d + 1, 0)];
        for (int i = 0; i < c.Length; i++) c[i] = BigRational.Zero;
        foreach (var kv in _terms) c[kv.Key[v]] += kv.Value;
        return new QPoly(c);
    }

    public static MPoly FromQPoly(int n, int v, QPoly q)
    {
        var terms = new List<(Mono, BigRational)>();
        for (int i = 0; i <= q.Degree; i++)
            if (!q[i].IsZero) terms.Add((Mono.Var(n, v, i), q[i]));
        return FromTerms(n, terms);
    }

    public static MPoly FromZPoly(int n, int v, ZPoly z) => FromQPoly(n, v, z.ToQ());

    /// <summary>The same polynomial in more variables (new variables appended).</summary>
    public MPoly Extend(int n)
    {
        if (n == Vars) return this;
        var terms = new List<(Mono, BigRational)>();
        foreach (var kv in _terms)
        {
            var e = new int[n];
            for (int i = 0; i < Vars; i++) e[i] = kv.Key[i];
            terms.Add((new Mono(e), kv.Value));
        }
        return FromTerms(n, terms);
    }

    public bool StructurallyEquals(MPoly other)
    {
        if (other._terms.Count != _terms.Count) return false;
        foreach (var kv in _terms)
            if (!other._terms.TryGetValue(kv.Key, out var c) || c != kv.Value) return false;
        return true;
    }

    public override string ToString()
    {
        if (IsZero) return "0";
        var sb = new StringBuilder();
        foreach (var kv in _terms.OrderByDescending(t => t.Key))
        {
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append(kv.Value);
            for (int i = 0; i < Vars; i++)
                if (kv.Key[i] > 0) sb.Append("*v").Append(i).Append('^').Append(kv.Key[i]);
        }
        return sb.ToString();
    }
}
