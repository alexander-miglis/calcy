using System.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Cas.Symbolic;

/// <summary>
/// Simplification: rational-function normalization over kernels (cancelling common factors), identities that always
/// hold (sin² + cos² = 1, ln(e^x) = x, sin(asin x) = x, √(x²) = |x|, denesting √(a + b√c)), choosing the simplest
/// of several equivalent forms.
/// </summary>
internal static class Simplifier
{
    private const int MaxFactorTerms = 300;

    // ------------------------------------------------------------------ zero testing

    /// <summary>True when the expression is provably (or, for constants, numerically beyond doubt) zero.</summary>
    public static bool IsZero(Expr e)
    {
        switch (e)
        {
            case NumberExpr n: return n.Value.IsZero;
            case ConstantExpr or SymbolExpr: return false;
        }
        try
        {
            var rat = Rational(e);
            if (rat is not null && rat.Value.R.IsZero) return true;
        }
        catch (CasLimitException) { }
        if (e.IsConstantValue() && !e.ContainsKind(ExprKind.Symbol))
        {
            var v = CasEnv.Numeric(e);
            if (v is { } nv && nv.IsFinite)
            {
                double scale = 1;
                if (e is AddExpr a)
                    foreach (var t in a.Terms)
                    {
                        var tv = CasEnv.Numeric(t);
                        if (tv is { } tn && tn.IsFinite) scale = Math.Max(scale, tn.AbsDouble());
                    }
                return nv.AbsDouble() <= 1e-11 * scale;
            }
        }
        if (HasTrigPairs(e))
        {
            try
            {
                var s = Simplify(e);
                return s.IsZero;
            }
            catch (CasLimitException) { }
        }
        return false;
    }

    private static bool HasTrigPairs(Expr e)
    {
        bool found = false;
        void Visit(Expr x)
        {
            if (found) return;
            if (x is CallExpr { Name: "sin" or "cos" or "tan" or "sec" or "csc" or "cot" or "sinh" or "cosh" or "tanh" }) found = true;
            foreach (var c in x.Children) Visit(c);
        }
        Visit(e);
        return found;
    }

    /// <summary>The rational-function view of an expression over its kernels, or null.</summary>
    public static (KernelMap Km, RatFunc R)? Rational(Expr e, params string[] main)
    {
        var km = KernelMap.Build([e], main);
        if (km is null) return null;
        var r = km.ToRat(e);
        return r is null ? null : (km, r);
    }

    // ------------------------------------------------------------------ public operations

    /// <summary>Common denominator with the numerator expanded and the denominator factored.</summary>
    public static Expr Together(Expr e)
    {
        if (e is ListExpr or MatrixExpr or TupleExpr) return e.WithChildren([.. e.Children.Select(Together)]);
        if (e is RelationExpr rel) return new RelationExpr(rel.Op, Together(rel.Left), Together(rel.Right));
        var rat = Rational(e);
        if (rat is null) return e;
        var (km, r) = rat.Value;
        if (r.IsPolynomial) return km.ToExpr(r);
        var num = km.ToExpr(r.Num);
        var den = FactoredPolynomial(km, r.Den, canonical: true);
        return Algebra.Mul(num, Algebra.Pow(den, Expr.MinusOne));
    }

    /// <summary>Cancels common factors: expanded numerator over expanded denominator.</summary>
    public static Expr Cancel(Expr e)
    {
        if (e is ListExpr or MatrixExpr or TupleExpr) return e.WithChildren([.. e.Children.Select(Cancel)]);
        if (e is RelationExpr rel) return new RelationExpr(rel.Op, Cancel(rel.Left), Cancel(rel.Right));
        var rat = Rational(e);
        if (rat is null) return e;
        return rat.Value.Km.ToExpr(rat.Value.R);
    }

    /// <summary>Product of the irreducible factors (canonical Algebra form, or raw so it prints factored).</summary>
    public static Expr FactoredPolynomial(KernelMap km, MPoly p, bool canonical)
    {
        if (p.IsZero) return Expr.Zero;
        if (p.TermCount > MaxFactorTerms) return km.ToExpr(p);
        var (unit, factors) = PolyFactor.Factor(p);
        var parts = Factoring.OrderFactors(km, factors.Select(f => (km.ToExpr(f.Factor), f.Multiplicity)).ToList());
        var list = new List<Expr>();
        if (!unit.IsOne || parts.Count == 0) list.Add(Algebra.Q(unit));
        foreach (var (b, m) in parts) list.Add(m == 1 ? b : Algebra.Pow(b, Expr.Int(m)));
        if (canonical) return Algebra.Mul(list);
        return list.Count == 1 ? list[0] : new MulExpr([.. list]);
    }

    /// <summary>A measure of how complicated an expression looks (smaller is simpler).</summary>
    public static int Complexity(Expr e) => e.NodeCount() * 8 + ExprPrinter.ToText(e).Length;

    /// <summary>Light clean-up used for calculus results: rational normalization only, kept when simpler.</summary>
    public static Expr Tidy(Expr e)
    {
        if (e is ListExpr or MatrixExpr or TupleExpr) return e.WithChildren([.. e.Children.Select(Tidy)]);
        if (e is RelationExpr rel) return new RelationExpr(rel.Op, Tidy(rel.Left), Tidy(rel.Right));
        if (e.NodeCount() > 600) return e;
        try
        {
            var rat = Rational(e);
            if (rat is null) return e;
            var (km, r) = rat.Value;
            var cands = new List<Expr> { km.ToExpr(r) };
            if (!r.IsPolynomial && r.Den.TermCount <= MaxFactorTerms && r.Den.TotalDegree <= 40)
                cands.Add(Algebra.Mul(km.ToExpr(r.Num), Algebra.Pow(FactoredPolynomial(km, r.Den, canonical: true), Expr.MinusOne)));
            var best = e;
            int score = Complexity(e);
            foreach (var c in cands)
            {
                // Calculus results keep their shape unless the alternative is clearly simpler.
                int sc = Complexity(c);
                if (sc * 10 < score * 9)
                {
                    best = c;
                    score = sc;
                }
            }
            return best;
        }
        catch (CasLimitException) { return e; }
    }

    /// <summary>Full simplification.</summary>
    public static Expr Simplify(Expr e, bool trig = true)
    {
        using var _ = CasEnv.Deeper();
        switch (e)
        {
            case NumberExpr or ConstantExpr or SymbolExpr or StringExpr or BoolExpr or LambdaExpr:
                return e;
            case ListExpr or MatrixExpr or TupleExpr or LogicExpr or PiecewiseExpr:
                return e.WithChildren([.. e.Children.Select(c => Simplify(c, trig))]);
            case RelationExpr r:
                return new RelationExpr(r.Op, Simplify(r.Left, trig), Simplify(r.Right, trig));
        }
        Expr s;
        try { s = SimplifyNode(e, trig); }
        catch (CasLimitException) { s = e; }
        if (s.NodeCount() > 2000) return s;
        var cands = new List<Expr>();
        try
        {
            cands.AddRange(RationalCandidates(s, trig));
            var logs = ExpandIntegerLogs(s);
            if (!ReferenceEquals(logs, s)) cands.AddRange(RationalCandidates(CasEnv.Rebuild(logs), trig));
            if (trig && HasRewritableTrig(s))
            {
                var rw = CasEnv.Rebuild(RewriteSinCos(s));
                cands.AddRange(RationalCandidates(rw, trig));
            }
        }
        catch (CasLimitException) { }
        return Best(s, cands);
    }

    private static Expr Best(Expr original, IEnumerable<Expr> candidates)
    {
        var best = original;
        int score = Complexity(original);
        foreach (var c in candidates)
        {
            int sc = Complexity(c);
            if (sc < score)
            {
                best = c;
                score = sc;
            }
        }
        return best;
    }

    private static List<Expr> RationalCandidates(Expr s, bool trig)
    {
        var result = new List<Expr>();
        var exprs = new List<Expr> { s };
        if (trig) exprs.AddRange(MissingPartners(s));
        var km = KernelMap.Build(exprs);
        if (km is null) return result;
        var r = km.ToRat(s);
        if (r is null) return result;
        AddForms(km, r, result);
        if (trig)
        {
            foreach (var variant in PythagoreanVariants(km, r)) AddForms(km, variant, result);
        }
        return result;
    }

    private static void AddForms(KernelMap km, RatFunc r, List<Expr> into)
    {
        into.Add(km.ToExpr(r));
        bool small = r.Num.TermCount <= MaxFactorTerms && r.Den.TermCount <= MaxFactorTerms && r.Num.TotalDegree <= 40 && r.Den.TotalDegree <= 40;
        if (!small) return;
        var numF = FactoredPolynomial(km, r.Num, canonical: true);
        if (r.IsPolynomial)
        {
            into.Add(numF);
            return;
        }
        var denF = FactoredPolynomial(km, r.Den, canonical: true);
        into.Add(Algebra.Mul(numF, Algebra.Pow(denF, Expr.MinusOne)));
        into.Add(Algebra.Mul(km.ToExpr(r.Num), Algebra.Pow(denF, Expr.MinusOne)));
    }

    // ------------------------------------------------------------------ trigonometric identities

    private static IEnumerable<RatFunc> PythagoreanVariants(KernelMap km, RatFunc r)
    {
        var pairs = new List<(int A, int B, bool Hyperbolic)>();
        for (int i = 0; i < km.Count; i++)
        {
            if (km[i] is not CallExpr { Args.Length: 1 } ci) continue;
            string? partner = ci.Name switch { "sin" => "cos", "sinh" => "cosh", _ => null };
            if (partner is null) continue;
            int j = km.IndexOf(new CallExpr(partner, ci.Args));
            if (j >= 0) pairs.Add((i, j, ci.Name == "sinh"));
        }
        if (pairs.Count == 0) yield break;
        // Variant 1: eliminate squares of cos (cosh); variant 2: eliminate squares of sin (sinh).
        foreach (bool reduceSecond in new[] { true, false })
        {
            var num = r.Num;
            var den = r.Den;
            foreach (var (a, b, hyp) in pairs)
            {
                num = ReduceSquares(num, a, b, reduceSecond, hyp);
                den = ReduceSquares(den, a, b, reduceSecond, hyp);
            }
            if (den.IsZero) continue;
            yield return RatFunc.Create(num, den);
        }
    }

    /// <summary>
    /// Rewrites powers of one kernel of a pair using sin² + cos² = 1 (or cosh² - sinh² = 1) so its degree is at most 1.
    /// </summary>
    private static MPoly ReduceSquares(MPoly p, int s, int c, bool reduceC, bool hyperbolic)
    {
        int target = reduceC ? c : s, other = reduceC ? s : c;
        if (p.Degree(target) < 2) return p;
        int n = p.Vars;
        var one = MPoly.Constant(n, BigRational.One);
        var otherSq = MPoly.Var(n, other, 2);
        // Replacement for target²: circular 1 - other²; hyperbolic cosh² = 1 + sinh², sinh² = cosh² - 1.
        MPoly repl = !hyperbolic ? one - otherSq : reduceC ? one + otherSq : otherSq - one;
        var acc = MPoly.Zero(n);
        foreach (var (m, coef) in p.Terms)
        {
            int e = m[target];
            var mono = MPoly.FromTerms(n, [(m.With(target, e % 2), coef)]);
            acc += e >= 2 ? mono * repl.Pow(e / 2) : mono;
        }
        return acc;
    }

    /// <summary>ln(12) = 2ln(2) + ln(3) and ln(p/q) = ln(p) - ln(q), so logarithms of numbers can combine.</summary>
    private static Expr ExpandIntegerLogs(Expr e)
    {
        if (e is CallExpr { Name: "ln", Args.Length: 1 } c && c.Args[0] is NumberExpr { Value.IsExactRational: true } n && n.Value.IsPositiveReal)
        {
            var q = n.Value.ReQ;
            if (q.Num.GetBitLength() > 60 || q.Den.GetBitLength() > 60) return e;
            var terms = new List<Expr>();
            foreach (var (part, sign) in new[] { (q.Num, 1), (q.Den, -1) })
            {
                if (part.IsOne) continue;
                foreach (var (p, k) in IntegerMath.Factorize(part))
                    terms.Add(Algebra.Mul(Expr.Int(sign * k), new CallExpr("ln", [Algebra.Q(new BigRational(p))])));
            }
            return terms.Count <= 1 && terms.All(t => t is CallExpr) ? e : Algebra.Add(terms);
        }
        if (e.Children.Count == 0) return e;
        var children = e.Children.Select(ExpandIntegerLogs).ToList();
        for (int i = 0; i < children.Count; i++)
            if (!ReferenceEquals(children[i], e.Children[i])) return e.WithChildren(children);
        return e;
    }

    /// <summary>cos(u) for a lone sin(u) squared (and the other way round), so sin² + cos² = 1 can be applied.</summary>
    private static List<Expr> MissingPartners(Expr e)
    {
        var calls = new HashSet<Expr>();
        void Visit(Expr x)
        {
            if (x is PowExpr { Base: CallExpr { Name: "sin" or "cos" or "sinh" or "cosh", Args.Length: 1 } c, Exponent: NumberExpr { Value.IsExactInteger: true } k } && k.Value.Re >= 2)
                calls.Add(c);
            foreach (var ch in x.Children) Visit(ch);
        }
        Visit(e);
        var result = new List<Expr>();
        foreach (CallExpr c in calls)
        {
            var partner = c.Name switch { "sin" => "cos", "cos" => "sin", "sinh" => "cosh", _ => "sinh" };
            var p = new CallExpr(partner, c.Args);
            if (!calls.Contains(p)) result.Add(p);
        }
        return result;
    }

    private static bool HasRewritableTrig(Expr e)
    {
        bool found = false;
        void Visit(Expr x)
        {
            if (found) return;
            if (x is CallExpr { Name: "tan" or "cot" or "sec" or "csc" or "tanh" or "coth" or "sech" or "csch", Args.Length: 1 }) found = true;
            foreach (var c in x.Children) Visit(c);
        }
        Visit(e);
        return found;
    }

    /// <summary>tan = sin/cos, sec = 1/cos and so on (also for hyperbolic functions).</summary>
    public static Expr RewriteSinCos(Expr e)
    {
        if (e.Children.Count == 0) return e;
        var mapped = e.WithChildren([.. e.Children.Select(RewriteSinCos)]);
        if (mapped is not CallExpr { Args.Length: 1 } c) return mapped;
        var u = c.Args[0];
        Expr S(string n) => CallExpr.Of(n, u);
        Expr Inv(Expr x) => Algebra.Pow(x, Expr.MinusOne);
        return c.Name switch
        {
            "tan" => Algebra.Mul(S("sin"), Inv(S("cos"))),
            "cot" => Algebra.Mul(S("cos"), Inv(S("sin"))),
            "sec" => Inv(S("cos")),
            "csc" => Inv(S("sin")),
            "tanh" => Algebra.Mul(S("sinh"), Inv(S("cosh"))),
            "coth" => Algebra.Mul(S("cosh"), Inv(S("sinh"))),
            "sech" => Inv(S("cosh")),
            "csch" => Inv(S("sinh")),
            _ => mapped,
        };
    }

    // ------------------------------------------------------------------ structural identities

    private static Expr SimplifyNode(Expr e, bool trig)
    {
        using var _ = CasEnv.Deeper();
        switch (e)
        {
            case AddExpr a:
                return Algebra.Add(a.Terms.Select(t => SimplifyNode(t, trig)).ToArray());
            case MulExpr m:
                return Algebra.Mul(m.Factors.Select(f => SimplifyNode(f, trig)).ToArray());
            case PowExpr p:
                return SimplifyPow(Simplify(p.Base, trig), Simplify(p.Exponent, trig));
            case CallExpr c:
                if (CasModule.IsOwnFunction(c.Name)) return c;
                return SimplifyCall(c.Name, [.. c.Args.Select(x => Simplify(x, trig))]);
            default:
                return e;
        }
    }

    private static Expr SimplifyPow(Expr b, Expr ex)
    {
        // e^(c·ln u + rest) = u^c · e^rest
        if (b is ConstantExpr { Constant: ConstantKind.E })
        {
            var terms = ex is AddExpr sum ? sum.Terms.ToList() : [ex];
            var factors = new List<Expr>();
            var rest = new List<Expr>();
            foreach (var t in terms)
            {
                var (c, r) = Algebra.SplitCoefficient(t);
                if (r is CallExpr { Name: "ln", Args.Length: 1 } ln && c.IsExactRational) factors.Add(Algebra.Pow(ln.Args[0], new NumberExpr(c)));
                else rest.Add(t);
            }
            if (factors.Count > 0)
            {
                factors.Add(Algebra.Pow(b, Algebra.Add(rest)));
                return Algebra.Mul(factors);
            }
        }
        if (ex is NumberExpr { Value.IsExactRational: true } q && !q.Value.IsInteger)
        {
            if (q.Value.ReQ == BigRational.Half && Denest(b) is { } d) return d;
            if (b is MulExpr bm && bm.Factors.All(f => IsNonNegative(f)))
                return Algebra.Mul(bm.Factors.Select(f => Algebra.Pow(f, ex)));
        }
        return Algebra.Pow(b, ex);
    }

    /// <summary>√(a + b√c) = √p ± √q when a² - b²c is a perfect square.</summary>
    private static Expr? Denest(Expr b)
    {
        if (b is not AddExpr { Terms.Length: 2 } sum) return null;
        BigRational? a = null, coef = null, c = null;
        foreach (var t in sum.Terms)
        {
            if (t is NumberExpr { Value.IsExactRational: true } n) a = n.Value.ReQ;
            else
            {
                var (k, r) = Algebra.SplitCoefficient(t);
                if (!k.IsExactRational) return null;
                if (r is PowExpr { Base: NumberExpr { Value.IsExactRational: true } rb, Exponent: NumberExpr ee } && ee.Value.ReQ == BigRational.Half)
                {
                    coef = k.ReQ;
                    c = rb.Value.ReQ;
                }
                else return null;
            }
        }
        if (a is null || coef is null || c is null) return null;
        var disc = a.Value * a.Value - coef.Value * coef.Value * c.Value;
        if (disc.IsNegative) return null;
        var d = ExactSqrt(disc);
        if (d is null) return null;
        var p = (a.Value + d.Value) / 2;
        var qv = (a.Value - d.Value) / 2;
        if (p.IsNegative || qv.IsNegative) return null;
        var sp = Algebra.Sqrt(Algebra.Q(p));
        var sq = Algebra.Sqrt(Algebra.Q(qv));
        return coef.Value.IsNegative ? Algebra.Sub(sp, sq) : Algebra.Add(sp, sq);
    }

    private static BigRational? ExactSqrt(BigRational q)
    {
        if (q.IsNegative) return null;
        if (!IntegerMath.IsPerfectPower(q.Num, 2, out var rn) || !IntegerMath.IsPerfectPower(q.Den, 2, out var rd)) return null;
        return new BigRational(rn, rd);
    }

    private static Expr SimplifyCall(string name, Expr[] args)
    {
        if (args.Length == 1 && args[0] is CallExpr { Args.Length: 1 } inner)
        {
            var u = inner.Args[0];
            Expr OneMinusSq() => Algebra.Sqrt(Algebra.Sub(Expr.One, Algebra.Mul(u, u)));
            Expr OnePlusSq() => Algebra.Sqrt(Algebra.Add(Expr.One, Algebra.Mul(u, u)));
            switch (name, inner.Name)
            {
                case ("sin", "asin") or ("cos", "acos") or ("tan", "atan") or ("sinh", "asinh") or ("cosh", "acosh") or ("tanh", "atanh"):
                    return u;
                case ("sin", "acos") or ("cos", "asin"):
                    return OneMinusSq();
                case ("sin", "atan"):
                    return Algebra.Div(u, OnePlusSq());
                case ("cos", "atan"):
                    return Algebra.Pow(OnePlusSq(), Expr.MinusOne);
                case ("tan", "asin"):
                    return Algebra.Div(u, OneMinusSq());
                case ("tan", "acos"):
                    return Algebra.Div(OneMinusSq(), u);
                case ("ln", _) when inner.Name == "exp":
                    return u;
            }
        }
        if (args.Length == 1 && args[0] is NumberExpr { Value.IsNegativeReal: true } neg)
        {
            var pos = new NumberExpr(-neg.Value);
            if (name is "sin" or "tan" or "csc" or "cot" or "sinh" or "tanh" or "csch" or "coth" or "asin" or "atan" or "asinh" or "atanh")
                return Algebra.Neg(CasEnv.TryFn(name, pos) ?? new CallExpr(name, [pos]));
            if (name is "cos" or "sec" or "cosh" or "sech")
                return CasEnv.TryFn(name, pos) ?? new CallExpr(name, [pos]);
        }
        if (name == "ln" && args.Length == 1 && args[0] is PowExpr { Base: ConstantExpr { Constant: ConstantKind.E } } pe && !ContainsImaginary(pe.Exponent))
            return pe.Exponent;
        if (name == "abs" && args.Length == 1)
        {
            if (IsNonNegative(args[0])) return args[0];
            if (IsNonNegative(Algebra.Neg(args[0]))) return Algebra.Neg(args[0]);
        }
        return CasEnv.TryFn(name, args) ?? new CallExpr(name, [.. args]);
    }

    private static bool ContainsImaginary(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsReal,
        _ => e.Children.Any(ContainsImaginary),
    };

    /// <summary>True when the expression is known to be real and at least 0 for every real value of its symbols.</summary>
    public static bool IsNonNegative(Expr e)
    {
        switch (e)
        {
            case NumberExpr n: return n.Value.IsReal && !n.Value.IsNegativeReal;
            case ConstantExpr { Constant: ConstantKind.Pi or ConstantKind.E or ConstantKind.Infinity }: return true;
            case PowExpr p:
                if (p.Exponent is NumberExpr { Value.IsExactInteger: true } ie && ie.Value.ToBigInteger().IsEven && !ContainsImaginary(p.Base)) return true;
                if (p.Base is ConstantExpr { Constant: ConstantKind.E } && !ContainsImaginary(p.Exponent)) return true;
                if (p.Exponent is NumberExpr { Value.IsExactRational: true } re && re.Value.ReQ.Den.IsEven) return IsNonNegative(p.Base);
                if (IsPositive(p.Base) && !ContainsImaginary(p.Exponent)) return true;
                return IsNonNegative(p.Base) && p.Exponent is NumberExpr { Value.IsReal: true };
            case CallExpr { Name: "abs" or "cosh" }: return true;
            case CallExpr { Name: "sqrt" }: return true;
            case AddExpr a: return a.Terms.All(IsNonNegative);
            case MulExpr m: return m.Factors.All(IsNonNegative);
            default:
                if (e.IsConstantValue())
                {
                    var s = CasEnv.SignOf(e);
                    return s is >= 0;
                }
                return false;
        }
    }

    /// <summary>True when the expression is known to be real and strictly positive.</summary>
    public static bool IsPositive(Expr e)
    {
        switch (e)
        {
            case NumberExpr n: return n.Value.IsPositiveReal;
            case ConstantExpr { Constant: ConstantKind.Pi or ConstantKind.E or ConstantKind.Infinity }: return true;
            case PowExpr p when p.Base is ConstantExpr { Constant: ConstantKind.E } && !ContainsImaginary(p.Exponent): return true;
            case PowExpr p when IsPositive(p.Base) && !ContainsImaginary(p.Exponent): return true;
            case CallExpr { Name: "cosh" }: return true;
            case AddExpr a: return a.Terms.All(IsNonNegative) && a.Terms.Any(IsPositive);
            case MulExpr m: return m.Factors.All(IsPositive);
            default:
                if (e.IsConstantValue())
                {
                    var s = CasEnv.SignOf(e);
                    return s is > 0;
                }
                return false;
        }
    }
}
