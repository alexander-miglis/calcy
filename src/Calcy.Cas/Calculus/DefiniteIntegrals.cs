using Calcy.Cas.Numerics;
using Calcy.Cas.Polynomials;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Calculus;

/// <summary>
/// Definite integrals: F(b) - F(a) when the antiderivative is valid on the whole interval (checked for poles,
/// logarithm and root singularities and for discontinuities of F), improper integrals through one-sided limits,
/// and numeric integration (<see cref="CasModule.NumericIntegrator"/>) otherwise. An exact value is also checked
/// against the numeric value when that converges.
/// </summary>
internal static class DefiniteIntegrals
{
    /// <summary>The integral, an approximate number, ±∞, Undefined (divergent), or null when it cannot be computed.</summary>
    public static Expr? Integrate(Expr f, string x, Expr a, Expr b)
    {
        if (a.Equals(b)) return Expr.Zero;
        double av = CasEnv.Real(a), bv = CasEnv.Real(b);
        bool numericBounds = !double.IsNaN(av) && !double.IsNaN(bv);
        if (numericBounds && av > bv)
        {
            var r = Integrate(f, x, b, a);
            return r is null ? null : Negate(r);
        }
        var F = Integrator.Antiderivative(f, x);
        bool otherSymbols = f.FreeSymbols().Any(s => s != x);
        if (!numericBounds || otherSymbols)
        {
            if (F is null || !DangerFree(f, x) || !DangerFree(F, x)) return null;
            var upper = Value(F, x, b, -1) ?? (b is ConstantExpr ? null : CasEnv.TrySubst(F, x, b));
            var lower = Value(F, x, a, +1) ?? (a is ConstantExpr ? null : CasEnv.TrySubst(F, x, a));
            if (upper is null || lower is null) return null;
            if ((ContainsInfinity(upper) && upper.FreeSymbols().Count > 0) || (ContainsInfinity(lower) && lower.FreeSymbols().Count > 0)) return null;
            return Simplifier.Simplify(Algebra.Sub(upper, lower));
        }
        if (F is not null)
        {
            var exact = Exact(f, F, x, a, b, av, bv);
            if (exact is not null) return exact;
        }
        return Numeric(f, x, av, bv);
    }

    private static Expr Negate(Expr r) => r switch
    {
        ConstantExpr { Constant: ConstantKind.Infinity } => ConstantExpr.NegativeInfinity,
        ConstantExpr { Constant: ConstantKind.NegativeInfinity } => ConstantExpr.Infinity,
        ConstantExpr { Constant: ConstantKind.Undefined } => r,
        _ => Algebra.Neg(r),
    };

    private static Expr? Exact(Expr f, Expr F, string x, Expr a, Expr b, double av, double bv)
    {
        // Points in [a, b] where the integrand or the antiderivative may be singular or discontinuous.
        var points = new List<(double Value, Expr? Exact)>();
        try
        {
            CollectDanger(f, x, av, bv, points);
            CollectDanger(F, x, av, bv, points);
        }
        catch (CasLimitException) { return null; }
        if (!RealOnInterval(f, x, av, bv)) return null;
        var cuts = Interior(points, av, bv);
        if (cuts.Count > 20) return null;
        var bounds = new List<Expr> { a };
        foreach (var (c, exact) in cuts)
        {
            var exactPoint = exact ?? ExactPoint(c);
            if (exactPoint is null) return null;
            bounds.Add(exactPoint);
        }
        bounds.Add(b);
        var pieces = new List<Expr>();
        int posInf = 0, negInf = 0;
        for (int i = 0; i + 1 < bounds.Count; i++)
        {
            var hi = Value(F, x, bounds[i + 1], -1);
            var lo = Value(F, x, bounds[i], +1);
            if (hi is null || lo is null) return null;
            if (hi is ConstantExpr { Constant: ConstantKind.Undefined } || lo is ConstantExpr { Constant: ConstantKind.Undefined }) return null;
            var diff = Algebra.Sub(hi, lo);
            switch (diff)
            {
                case ConstantExpr { Constant: ConstantKind.Infinity }: posInf++; continue;
                case ConstantExpr { Constant: ConstantKind.NegativeInfinity }: negInf++; continue;
                case ConstantExpr { Constant: ConstantKind.Undefined }: return ConstantExpr.Undefined;
            }
            pieces.Add(diff);
        }
        if (posInf > 0 && negInf > 0) return ConstantExpr.Undefined;
        if (posInf > 0) return ConstantExpr.Infinity;
        if (negInf > 0) return ConstantExpr.NegativeInfinity;
        var value = Simplifier.Simplify(Algebra.Add(pieces));
        // Cross-check with numeric integration when it converges.
        var exactValue = CasEnv.Real(value);
        if (double.IsNaN(exactValue)) return null;
        var fn = NumericFunction.Compile(f, x);
        if (fn is not null)
        {
            double num = GaussKronrod.Integrate(fn, av, bv, 1e-10, 2000, out var err);
            if (double.IsFinite(num) && err < 1e-8 * Math.Max(1, Math.Abs(num)) && Math.Abs(num - exactValue) > 1e-6 * Math.Max(1, Math.Abs(num)))
                return new NumberExpr(Number.Approx(num));
        }
        return value;
    }

    /// <summary>Distinct points strictly inside (a, b), preferring exact forms.</summary>
    private static List<(double Value, Expr? Exact)> Interior(List<(double Value, Expr? Exact)> points, double a, double b)
    {
        var result = new List<(double Value, Expr? Exact)>();
        foreach (var p in points.OrderBy(p => p.Value))
        {
            if (!(p.Value > a + 1e-12 * Math.Max(1, Math.Abs(a)) && p.Value < b - 1e-12 * Math.Max(1, Math.Abs(b)))) continue;
            if (result.Count > 0 && Math.Abs(result[^1].Value - p.Value) <= 1e-10 * Math.Max(1, Math.Abs(p.Value)))
            {
                if (result[^1].Exact is null && p.Exact is not null) result[^1] = p;
                continue;
            }
            result.Add(p);
        }
        return result;
    }

    /// <summary>F at a bound: direct substitution, else the one-sided limit (side +1 from the right, -1 from the left).</summary>
    private static Expr? Value(Expr F, string x, Expr point, int side)
    {
        if (point is ConstantExpr { IsInfinite: true }) return Limits.Limit(F, x, point, 0);
        var direct = CasEnv.TrySubst(F, x, point);
        if (direct is not null && direct.IsConstantValue() && !ContainsInfinity(direct))
        {
            var n = CasEnv.Numeric(direct);
            if (n is { } nv && nv.IsFinite && nv.IsReal) return direct;
        }
        return Limits.Limit(F, x, point, side);
    }

    private static bool ContainsInfinity(Expr e) => e is ConstantExpr { IsInfinite: true } or ConstantExpr { Constant: ConstantKind.Undefined } || e.Children.Any(ContainsInfinity);

    /// <summary>An exact form of a numeric singular point (rational, or a simple multiple of π), or null.</summary>
    private static Expr? ExactPoint(double c)
    {
        var q = BigRational.Approximate(c, 10_000, 1e-12);
        if (Math.Abs(q.ToDouble() - c) < 1e-10 * Math.Max(1, Math.Abs(c))) return Algebra.Q(q);
        var k = BigRational.Approximate(c / Math.PI, 1000, 1e-12);
        if (Math.Abs(k.ToDouble() * Math.PI - c) < 1e-10 * Math.Max(1, Math.Abs(c))) return Algebra.Mul(Algebra.Q(k), ConstantExpr.Pi);
        return null;
    }

    private static bool RealOnInterval(Expr f, string x, double a, double b)
    {
        var fn = NumericFunction.Compile(f, x);
        if (fn is null) return true;
        double lo = double.IsInfinity(a) ? (double.IsInfinity(b) ? -50 : b - 50) : a;
        double hi = double.IsInfinity(b) ? lo + 100 : b;
        int bad = 0;
        for (int i = 1; i < 64; i++)
        {
            double t = lo + (hi - lo) * i / 64;
            if (double.IsNaN(fn(t))) bad++;
        }
        return bad == 0;
    }

    /// <summary>True when the expression has no part that can be singular or discontinuous for real x.</summary>
    public static bool DangerFree(Expr e, string x)
    {
        if (!e.ContainsSymbol(x)) return true;
        switch (e)
        {
            case PowExpr p when p.Base.ContainsSymbol(x):
                if (p.Exponent is NumberExpr ne && ne.Value.IsNegativeReal) return false;
                if (p.Exponent is NumberExpr { Value.IsExactRational: true } q && !q.Value.IsInteger) return false;
                if (p.Exponent.ContainsSymbol(x)) return false;
                break;
            case CallExpr c when c.Name is "ln" or "log" or "log2" or "tan" or "sec" or "csc" or "cot" or "coth" or "csch" or "abs" or "sign"
                                 or "floor" or "ceil" or "asin" or "acos" or "atanh" or "acosh":
                return false;
        }
        return e.Children.All(ch => DangerFree(ch, x));
    }

    private static void CollectDanger(Expr e, string x, double a, double b, List<(double Value, Expr? Exact)> points)
    {
        if (!e.ContainsSymbol(x)) return;
        switch (e)
        {
            case PowExpr p when p.Base.ContainsSymbol(x) && p.Exponent is NumberExpr ne && (ne.Value.IsNegativeReal || !ne.Value.IsInteger):
                Zeros(p.Base, x, a, b, points);
                break;
            case PowExpr p when p.Base.ContainsSymbol(x) && p.Exponent.ContainsSymbol(x):
                Zeros(p.Base, x, a, b, points);
                break;
            case CallExpr { Name: "ln" or "log" or "log2" or "abs" or "sign" } c:
                Zeros(c.Args[0], x, a, b, points);
                break;
            case CallExpr { Name: "tan" or "sec" } c:
                Zeros(CallExpr.Of("cos", c.Args[0]), x, a, b, points);
                break;
            case CallExpr { Name: "cot" or "csc" } c:
                Zeros(CallExpr.Of("sin", c.Args[0]), x, a, b, points);
                break;
            case CallExpr { Name: "coth" or "csch" } c:
                Zeros(c.Args[0], x, a, b, points);
                break;
            case CallExpr { Name: "floor" or "ceil" }:
                throw new CasLimitException("Step functions need numeric integration");
        }
        foreach (var ch in e.Children) CollectDanger(ch, x, a, b, points);
    }

    private static void Zeros(Expr g, string x, double a, double b, List<(double Value, Expr? Exact)> points)
    {
        double lo = double.IsNegativeInfinity(a) ? -1e6 : a;
        double hi = double.IsPositiveInfinity(b) ? 1e6 : b;
        // Exact real roots for polynomials with rational coefficients.
        var km = KernelMap.Build([g], x);
        if (km is not null && km.Count == 1)
        {
            var r = km.ToRat(g);
            if (r is not null && r.Den.IsConstant)
            {
                var poly = r.Num.ToQPoly(0);
                foreach (var (factor, _) in UniFactor.FactorQ(poly).Factors)
                {
                    if (factor.Degree <= 2)
                    {
                        foreach (var root in Solving.Solver.IrreducibleRoots(factor.ToQ()))
                        {
                            if (!Solving.Solver.IsRealValue(root)) continue;
                            double v = CasEnv.Real(root);
                            if (v >= lo - 1e-12 && v <= hi + 1e-12) points.Add((v, root));
                        }
                        continue;
                    }
                    foreach (var root in RootFinding.RealPolynomialRoots(factor.ToQ()))
                        if (root >= lo - 1e-12 && root <= hi + 1e-12) points.Add((root, null));
                }
                return;
            }
        }
        var fn = NumericFunction.Compile(g, x) ?? throw new CasLimitException("Cannot locate singularities");
        double sLo = Math.Max(lo, -1000), sHi = Math.Min(hi, 1000);
        if (sHi > sLo)
        {
            var found = RootFinding.ScanRoots(fn, sLo, sHi, 4000);
            if (found.Count > 60) throw new CasLimitException("Too many singular points");
            points.AddRange(found.Select(v => (v, (Expr?)null)));
        }
        if (Math.Abs(fn(lo)) < 1e-12) points.Add((lo, null));
        if (Math.Abs(fn(hi)) < 1e-12) points.Add((hi, null));
    }

    /// <summary>
    /// Sign (+1 or -1) of a non-integrable blow-up of f next to p on the given side (|f| growing like 1/|x - p| or
    /// faster), 0 when f stays integrable there, or null when it cannot be told numerically.
    /// </summary>
    private static int? BlowUp(Func<double, double> f, double p, int side, double delta)
    {
        double d1 = delta, d2 = delta * 1e-3;
        double f1 = f(p + side * d1), f2 = f(p + side * d2);
        if (double.IsInfinity(f2)) return Math.Sign(f2);
        if (!double.IsFinite(f1) || !double.IsFinite(f2)) return null;
        if (Math.Abs(f2) < 1e-300 || Math.Abs(f1) < 1e-300) return 0;
        double alpha = Math.Log(Math.Abs(f2) / Math.Abs(f1)) / Math.Log(d1 / d2);
        if (alpha > 0.9 && Math.Sign(f1) == Math.Sign(f2)) return Math.Sign(f2);
        return 0;
    }

    /// <summary>
    /// Numeric integration through the replaceable hook, split at the singular points of the integrand so that no
    /// piece crosses a pole; null when a piece fails (divergent or too hard) or the singularities cannot be located.
    /// </summary>
    public static Expr? Numeric(Expr f, string x, double a, double b)
    {
        var fn = NumericFunction.Compile(f, x);
        if (fn is null) return null;
        var points = new List<(double Value, Expr? Exact)>();
        try { CollectDanger(f, x, a, b, points); }
        catch (CasLimitException) { return null; }
        var cuts = Interior(points, a, b).Select(p => p.Value).ToList();
        if (cuts.Count > 50) return null;
        // A pole that is not integrable makes the integral diverge; the floating-point quadrature cannot see that.
        var singular = new List<double>(cuts);
        foreach (var p in points)
        {
            if (double.IsFinite(a) && Math.Abs(p.Value - a) <= 1e-12 * Math.Max(1, Math.Abs(a))) singular.Add(a);
            if (double.IsFinite(b) && Math.Abs(p.Value - b) <= 1e-12 * Math.Max(1, Math.Abs(b))) singular.Add(b);
        }
        int up = 0, down = 0;
        foreach (var p in singular.Distinct())
        {
            foreach (int side in new[] { -1, 1 })
            {
                double room = side < 0 ? p - a : b - p;
                if (!(room > 0)) continue;
                var blow = BlowUp(fn, p, side, Math.Min(room / 2, 1e-3 * Math.Max(1, Math.Abs(p))));
                if (blow is null) return null;
                if (blow > 0) up++;
                else if (blow < 0) down++;
            }
        }
        if (up > 0 && down > 0) return ConstantExpr.Undefined;
        if (up > 0) return ConstantExpr.Infinity;
        if (down > 0) return ConstantExpr.NegativeInfinity;
        var bounds = new List<double> { a };
        bounds.AddRange(cuts);
        bounds.Add(b);
        double total = 0;
        for (int i = 0; i + 1 < bounds.Count; i++)
        {
            double v;
            try { v = CasModule.NumericIntegrator(fn, bounds[i], bounds[i + 1]); }
            catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException) { return null; }
            if (!double.IsFinite(v)) return null;
            total += v;
        }
        return new NumberExpr(Number.Approx(total));
    }
}
