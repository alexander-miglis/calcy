using System.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Calculus;

/// <summary>
/// Limits: direct substitution where the expression is continuous, exact analysis of rational functions
/// (cancellation, poles, degrees at infinity), asymptotic series expansion (standard limits, dominant terms,
/// exponential and logarithmic growth) and L'Hôpital's rule as a bounded fallback.
/// </summary>
internal static class Limits
{
    private static readonly HashSet<string> Discontinuous = ["floor", "ceil", "round", "trunc", "sign", "fpart", "mod", "rem", "int", "piecewise"];

    /// <summary>
    /// The limit of f as x tends to the point (direction +1 from the right, -1 from the left, 0 two-sided).
    /// Returns a value, ±∞, Undefined when the limit does not exist, or null when it cannot be determined.
    /// </summary>
    public static Expr? Limit(Expr f, string x, Expr point, int direction)
    {
        using var _ = CasEnv.Deeper(50);
        if (!f.ContainsSymbol(x)) return f;
        if (point is ConstantExpr { Constant: ConstantKind.Infinity }) return OneSided(f, x, point, +1);
        if (point is ConstantExpr { Constant: ConstantKind.NegativeInfinity }) return OneSided(f, x, point, -1);
        if (direction == 0 && Continuous(f))
        {
            var direct = CasEnv.TrySubst(f, x, point);
            if (direct is not null && IsFiniteValue(direct))
            {
                return direct;
            }
        }
        if (direction != 0) return OneSided(f, x, point, direction);
        var right = OneSided(f, x, point, +1);
        if (right is null) return null;
        var left = OneSided(f, x, point, -1);
        if (left is null) return null;
        return Combine(left, right);
    }

    private static Expr Combine(Expr left, Expr right)
    {
        if (left is ConstantExpr { Constant: ConstantKind.Undefined } || right is ConstantExpr { Constant: ConstantKind.Undefined }) return ConstantExpr.Undefined;
        if (left is ConstantExpr { IsInfinite: true } || right is ConstantExpr { IsInfinite: true })
            return left.Equals(right) ? left : ConstantExpr.Undefined;
        return Simplifier.IsZero(Algebra.Sub(left, right)) ? right : ConstantExpr.Undefined;
    }

    private static bool Continuous(Expr f)
    {
        if (f is PiecewiseExpr) return false;
        if (f is CallExpr c && Discontinuous.Contains(c.Name)) return false;
        return f.Children.All(Continuous);
    }

    private static bool IsFiniteValue(Expr v)
    {
        if (v.ContainsKind(ExprKind.Constant) && ContainsInfinite(v)) return false;
        if (!v.IsConstantValue() && v.FreeSymbols().Count == 0) return false;
        if (v.IsConstantValue())
        {
            var n = CasEnv.Numeric(v);
            if (n is null || !n.Value.IsFinite || n.Value.IsNaN) return false;
        }
        return true;
    }

    private static bool ContainsInfinite(Expr e) => e is ConstantExpr { IsInfinite: true } or ConstantExpr { Constant: ConstantKind.Undefined } || e.Children.Any(ContainsInfinite);

    private static Expr? OneSided(Expr f, string x, Expr point, int direction)
    {
        bool atInfinity = point is ConstantExpr { IsInfinite: true };
        if (!atInfinity)
        {
            var direct = Continuous(f) ? CasEnv.TrySubst(f, x, point) : null;
            if (direct is not null && IsFiniteValue(direct) && !HasBranchCut(f, x, point, direction)) return direct;
        }
        return RationalLimit(f, x, point, direction)
            ?? SeriesLimit(f, x, point, direction)
            ?? Hopital(f, x, point, direction, 0);
    }

    private static bool HasBranchCut(Expr f, string x, Expr point, int direction)
    {
        // √u and ln u approach from outside their real domain: let the series decide.
        bool found = false;
        void Visit(Expr e)
        {
            if (found) return;
            if (e is PowExpr { Exponent: NumberExpr { Value.IsExactRational: true } q } p && q.Value.ReQ.Den.IsEven && p.Base.ContainsSymbol(x))
            {
                var v = CasEnv.TrySubst(p.Base, x, point);
                if (v is not null && Simplifier.IsZero(v)) found = true;
            }
            if (e is CallExpr { Name: "ln" or "log" or "log2" } c && c.Args[0].ContainsSymbol(x))
            {
                var v = CasEnv.TrySubst(c.Args[0], x, point);
                if (v is not null && Simplifier.IsZero(v)) found = true;
            }
            foreach (var ch in e.Children) Visit(ch);
        }
        Visit(f);
        return found;
    }

    // ------------------------------------------------------------------ rational functions

    private static Expr? RationalLimit(Expr f, string x, Expr point, int direction)
    {
        var rat = Simplifier.Rational(f, x);
        if (rat is null) return null;
        var (km, r) = rat.Value;
        int v = km.IndexOf(x);
        for (int i = 0; i < km.Count; i++)
            if (i != v && km.KernelContains(i, x)) return null;
        var num = km.ToExpr(r.Num);
        var den = km.ToExpr(r.Den);
        if (point is ConstantExpr { IsInfinite: true } inf)
        {
            int dn = r.Num.Degree(v), dd = r.Den.Degree(v);
            if (r.Num.IsZero) return Expr.Zero;
            var ln = km.ToExpr(r.Num.LeadingCoefficient(v));
            var ld = km.ToExpr(r.Den.LeadingCoefficient(v));
            if (dn < dd) return Expr.Zero;
            if (dn == dd) return Simplifier.Simplify(Algebra.Div(ln, ld), trig: false);
            var s = CasEnv.SignOf(Algebra.Div(ln, ld));
            if (s is null or 0) return null;
            int sign = s.Value;
            if (inf.Constant == ConstantKind.NegativeInfinity && (dn - dd) % 2 == 1) sign = -sign;
            return sign > 0 ? ConstantExpr.Infinity : ConstantExpr.NegativeInfinity;
        }
        var dval = CasEnv.TrySubst(den, x, point);
        if (dval is null) return null;
        if (!Simplifier.IsZero(dval))
        {
            var nval = CasEnv.TrySubst(num, x, point);
            if (nval is null) return null;
            return Simplifier.Simplify(Algebra.Div(nval, dval), trig: false);
        }
        var nv = CasEnv.TrySubst(num, x, point);
        if (nv is null || Simplifier.IsZero(nv)) return null;
        // Pole: find the multiplicity k of the root and the sign of N/D^(k) at the point.
        Expr d = den;
        int k = 0;
        Expr dk = dval;
        while (Simplifier.IsZero(dk))
        {
            if (++k > 50) return null;
            d = Differentiator.D(d, x);
            dk = CasEnv.TrySubst(d, x, point) ?? Expr.Zero;
            if (dk.IsZero && d.IsZero) return null;
        }
        var sg = CasEnv.SignOf(Algebra.Div(nv, dk));
        if (sg is null or 0) return null;
        int signRight = sg.Value;
        int sideSign = direction > 0 ? signRight : (k % 2 == 0 ? signRight : -signRight);
        if (direction == 0)
        {
            if (k % 2 == 1) return ConstantExpr.Undefined;
            sideSign = signRight;
        }
        return sideSign > 0 ? ConstantExpr.Infinity : ConstantExpr.NegativeInfinity;
    }

    // ------------------------------------------------------------------ series

    private static Expr? SeriesLimit(Expr f, string x, Expr point, int direction)
    {
        var t = CasEnv.Temp();
        Expr sub = point switch
        {
            ConstantExpr { Constant: ConstantKind.Infinity } => Algebra.Pow(t, Expr.MinusOne),
            ConstantExpr { Constant: ConstantKind.NegativeInfinity } => Algebra.Neg(Algebra.Pow(t, Expr.MinusOne)),
            _ => direction > 0 ? Algebra.Add(point, t) : Algebra.Sub(point, t),
        };
        Expr g;
        try { g = CasEnv.Subst(f, x, sub); }
        catch (CalcException) { return null; }
        foreach (int terms in new[] { 4, 8, 14, 24 })
        {
            CasEnv.Check();
            try
            {
                var ex = new SeriesExpander(t.Name, terms, PS.Infinite);
                var a = ex.Expand(g);
                var value = Interpret(a);
                if (value is not null) return value;
            }
            catch (CasLimitException) { }
            catch (CalcException) { }
            catch (DivideByZeroException) { }
        }
        return null;
    }

    /// <summary>The limit as t → 0+ of an asymptotic value, or null when more terms are needed.</summary>
    public static Expr? Interpret(Asym a)
    {
        if (a.Bounded) return ConstantExpr.Undefined;
        if (a.G is not null && a.G.HasTerms)
        {
            var dir = SeriesExpander.CoefficientSign(a.G.LeadCoef);
            if (dir is null) return null;
            if (dir < 0) return Expr.Zero;
            if (!a.P.HasTerms) return null;
            var s = SeriesExpander.CoefficientSign(a.P.LeadCoef);
            if (s is null or 0) return null;
            return s > 0 ? ConstantExpr.Infinity : ConstantExpr.NegativeInfinity;
        }
        var p = a.P;
        if (!p.HasTerms) return p.Order > 0 ? Expr.Zero : null;
        var (e, c) = p.Terms[0];
        if (e > 0) return Expr.Zero;
        if (e < 0)
        {
            var s = SeriesExpander.CoefficientSign(c);
            if (s is null or 0) return null;
            return s > 0 ? ConstantExpr.Infinity : ConstantExpr.NegativeInfinity;
        }
        var lim = SeriesExpander.LimitInLog(c);
        if (lim is null) return null;
        if (lim.ContainsSymbol(SeriesExpander.LogT.Name)) return null;
        return Simplifier.Simplify(lim);
    }

    // ------------------------------------------------------------------ L'Hôpital

    private static Expr? Hopital(Expr f, string x, Expr point, int direction, int depth)
    {
        if (depth > 4) return null;
        var (num, den) = SplitFraction(f);
        if (den.IsOne) return null;
        var ln = OneSidedNoHopital(num, x, point, direction);
        var ld = OneSidedNoHopital(den, x, point, direction);
        if (ln is null || ld is null) return null;
        bool zeroOverZero = ln.IsZero && ld.IsZero;
        bool infOverInf = ln is ConstantExpr { IsInfinite: true } && ld is ConstantExpr { IsInfinite: true };
        if (!zeroOverZero && !infOverInf) return null;
        var dn = Simplifier.Tidy(Differentiator.D(num, x));
        var dd = Simplifier.Tidy(Differentiator.D(den, x));
        if (Simplifier.IsZero(dd)) return null;
        var q = Simplifier.Simplify(Algebra.Div(dn, dd), trig: false);
        var direct = OneSidedNoHopital(q, x, point, direction);
        return direct ?? Hopital(q, x, point, direction, depth + 1);
    }

    private static Expr? OneSidedNoHopital(Expr f, string x, Expr point, int direction)
    {
        if (!f.ContainsSymbol(x)) return f;
        bool atInfinity = point is ConstantExpr { IsInfinite: true };
        if (!atInfinity)
        {
            var direct = Continuous(f) ? CasEnv.TrySubst(f, x, point) : null;
            if (direct is not null && IsFiniteValue(direct) && !HasBranchCut(f, x, point, direction)) return direct;
        }
        return RationalLimit(f, x, point, direction == 0 ? 1 : direction) ?? SeriesLimit(f, x, point, direction == 0 ? 1 : direction);
    }

    /// <summary>Splits a product into numerator and denominator (factors with negative exponents).</summary>
    public static (Expr Num, Expr Den) SplitFraction(Expr f)
    {
        var factors = f is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [f];
        var num = new List<Expr>();
        var den = new List<Expr>();
        foreach (var factor in factors)
        {
            if (factor is PowExpr { Exponent: NumberExpr ne } p && ne.Value.IsNegativeReal)
                den.Add(Algebra.Pow(p.Base, new NumberExpr(-ne.Value)));
            else if (factor is NumberExpr { Value.IsExactRational: true } q && !q.Value.ReQ.Den.IsOne)
            {
                num.Add(Algebra.Q(q.Value.ReQ.Num));
                den.Add(Algebra.Q(q.Value.ReQ.Den));
            }
            else num.Add(factor);
        }
        return (Algebra.Mul(num), Algebra.Mul(den));
    }

    // ------------------------------------------------------------------ Taylor series

    /// <summary>Taylor (or Laurent/Puiseux) polynomial of f about a up to (x - a)^order.</summary>
    public static Expr? Taylor(Expr f, string x, Expr a, int order)
    {
        var right = SideSeries(f, x, a, order, +1);
        if (right is null) return null;
        // A two-sided expansion must agree with the expansion from the left (floor, sign, |x|, atan(1/x) do not).
        bool integral = right.Terms.All(t => t.E.IsInteger && !t.C.ContainsSymbol(SeriesExpander.LogT.Name));
        if (integral)
        {
            var left = SideSeries(f, x, a, order, -1);
            if (left is not null)
            {
                foreach (var (e, c) in right.Terms.Where(t => t.E <= order))
                {
                    var lc = left.Terms.FirstOrDefault(t => t.E == e).C ?? Expr.Zero;
                    var mirrored = e.Num.IsEven ? lc : Algebra.Neg(lc);
                    if (!Simplifier.IsZero(Algebra.Sub(c, mirrored))) return null;
                }
                foreach (var (e, c) in left.Terms.Where(t => t.E <= order))
                    if (!right.Terms.Any(t => t.E == e) && !Simplifier.IsZero(c)) return null;
            }
        }
        return BuildTaylor(right, x, a, order);
    }

    /// <summary>The series of f(a + side·t) in t up to t^order, or null.</summary>
    private static PS? SideSeries(Expr f, string x, Expr a, int order, int side)
    {
        var t = CasEnv.Temp();
        Expr g;
        try { g = CasEnv.Subst(f, x, side > 0 ? Algebra.Add(a, t) : Algebra.Sub(a, t)); }
        catch (CalcException) { return null; }
        foreach (int extra in new[] { 2, 6, 12, 24 })
        {
            CasEnv.Check();
            try
            {
                var ex = new SeriesExpander(t.Name, 100_000, new BigRational(order + 1 + extra));
                var s = ex.Expand(g);
                if (s.Bounded || s.G is not null) return null;
                if (s.P.Order <= order) continue;
                return s.P;
            }
            catch (CasLimitException) { }
            catch (CalcException) { }
            catch (DivideByZeroException) { }
        }
        return null;
    }

    private static Expr BuildTaylor(PS p, string x, Expr a, int order)
    {
        var xs = new SymbolExpr(x);
        var shifted = a.IsZero ? (Expr)xs : Algebra.Sub(xs, a);
        var logReplacement = CasEnv.Fn("ln", shifted);
        var terms = new List<Expr>();
        // Keep powers of (x - a) visible, except when the result is at most linear (then x - 1 + 1 would read badly).
        bool raw = !a.IsZero && p.Terms.Any(t => t.E > 1 && t.E <= order);
        foreach (var (e, c0) in p.Terms)
        {
            if (e > order) break;
            var c = Simplifier.Tidy(CasEnv.Subst(c0, SeriesExpander.LogT.Name, logReplacement));
            if (c.IsZero) continue;
            Expr power = e.IsZero ? Expr.One : e.IsOne ? shifted : Algebra.Pow(shifted, Algebra.Q(e));
            if (raw)
            {
                if (power.IsOne) terms.Add(c);
                else if (c.IsOne) terms.Add(power);
                else terms.Add(c is NumberExpr && power is AddExpr ? new MulExpr([c, power]) : Algebra.Mul(c, power));
            }
            else terms.Add(Algebra.Mul(c, power));
        }
        if (terms.Count == 0) return Expr.Zero;
        if (terms.Count == 1) return terms[0];
        return raw ? new AddExpr([.. terms]) : Algebra.Add(terms);
    }
}
