using Calcy.Cas.Polynomials;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Cas.Symbolic;

/// <summary>Factorization of expressions (polynomials and rational functions over Q in their kernels).</summary>
internal static class Factoring
{
    /// <summary>
    /// Factors an expression. With a variable, irreducible quadratics in that variable whose roots are real are split
    /// into linear factors with radicals (TI-Nspire behaviour: factor(x^2 - 3, x) = (x - √3)(x + √3)).
    /// The result is built without simplification so it prints in factored form.
    /// </summary>
    public static Expr Factor(Expr e, string? variable = null)
    {
        using var _ = CasEnv.Deeper();
        switch (e)
        {
            case ListExpr or MatrixExpr or TupleExpr:
                return e.WithChildren([.. e.Children.Select(c => Factor(c, variable))]);
            case RelationExpr r:
                return new RelationExpr(r.Op, Factor(r.Left, variable), Factor(r.Right, variable));
            case NumberExpr { Value.IsExactRational: true }:
                return CasEnv.Fn("ifactor", e);
            case NumberExpr or ConstantExpr or SymbolExpr:
                return e;
        }
        var km = KernelMap.Build([e], variable is null ? [] : [variable]);
        if (km is null) return e;
        var r0 = km.ToRat(e);
        if (r0 is null) return e;
        var (unitN, numFactors) = PolyFactor.Factor(r0.Num);
        var (unitD, denFactors) = r0.Den.IsConstant ? (r0.Den.ConstantValue, new List<(MPoly, int)>()) : PolyFactor.Factor(r0.Den);
        var unit = unitN / unitD;
        var num = numFactors.Select(f => (km.ToExpr(f.Factor), f.Multiplicity)).ToList();
        var den = denFactors.Select(f => (km.ToExpr(f.Item1), f.Item2)).ToList();
        if (variable is not null)
        {
            num = SplitQuadratics(km, numFactors, variable, ref unit);
            if (denFactors.Count > 0)
            {
                var inv = BigRational.One;
                den = SplitQuadratics(km, denFactors, variable, ref inv);
                unit /= inv;
            }
        }
        // Nothing to factor (a single factor to the first power): keep the expression as it was.
        bool trivial = num.Count + den.Count <= 1 && num.All(t => t.Item2 == 1) && den.All(t => t.Item2 == 1) && (unit.IsOne || unit == BigRational.MinusOne && num.Count + den.Count == 0);
        if (trivial && num.Count == 1 && unit.IsOne && den.Count == 0) return km.ToExpr(r0.Num.Scale(BigRational.One / r0.Den.ConstantValue));
        if (trivial && den.Count == 1 && num.Count == 0 && unit.IsOne) return e;
        return Build(unit, OrderFactors(km, num), OrderFactors(km, den));
    }

    private static List<(Expr, int)> SplitQuadratics(KernelMap km, List<(MPoly Factor, int Multiplicity)> factors, string variable, ref BigRational unit)
    {
        var result = new List<(Expr, int)>();
        int v = km.IndexOf(variable);
        foreach (var (f, m) in factors)
        {
            if (v >= 0 && f.UsedVars() is [var only] && only == v && f.Degree(v) == 2)
            {
                var q = f.ToQPoly(v);
                var disc = q[1] * q[1] - 4 * q[2] * q[0];
                if (disc.IsPositive)
                {
                    var x = new SymbolExpr(variable);
                    var sq = Algebra.Sqrt(Algebra.Q(disc));
                    var twoA = Algebra.Q(2 * q[2]);
                    var r1 = Algebra.Div(Algebra.Sub(Algebra.Q(-q[1]), sq), twoA);
                    var r2 = Algebra.Div(Algebra.Add(Algebra.Q(-q[1]), sq), twoA);
                    for (int k = 0; k < m; k++) unit *= q[2];
                    result.Add((Algebra.Sub(x, r1), m));
                    result.Add((Algebra.Sub(x, r2), m));
                    continue;
                }
            }
            result.Add((km.ToExpr(f), m));
        }
        return result;
    }

    /// <summary>Orders factors for display: single kernels first, then by degree, then canonically.</summary>
    public static List<(Expr Base, int Multiplicity)> OrderFactors(KernelMap km, List<(Expr Base, int Multiplicity)> factors)
    {
        int Degree(Expr b) => b switch
        {
            AddExpr a => a.Terms.Max(TermDegree),
            _ => TermDegree(b),
        };
        int TermDegree(Expr t) => t switch
        {
            NumberExpr => 0,
            PowExpr { Exponent: NumberExpr { Value.IsExactInteger: true } n } p => TermDegree(p.Base) * (int)n.Value.Re,
            MulExpr m => m.Factors.Sum(TermDegree),
            _ => 1,
        };
        // Within a degree: by the value with the first kernel at 0 and the others at 1 (x - 3 before x - 2, x - y before x + y).
        double Key(Expr b)
        {
            if (km.Count == 0) return 0;
            var mapped = b;
            // Replace larger kernels first so sin(1/x) is replaced before x.
            foreach (var i in Enumerable.Range(0, km.Count).OrderByDescending(i => km[i].NodeCount()))
                mapped = CasEnv.ReplaceExpr(mapped, km[i], i == 0 ? Expr.Zero : Expr.One);
            try
            {
                double v = CasEnv.Real(CasEnv.Rebuild(mapped));
                return double.IsNaN(v) ? 0 : v;
            }
            catch (Core.CalcException) { return 0; }
            catch (ArithmeticException) { return 0; }
        }
        return [.. factors
            .OrderBy(f => f.Base is AddExpr ? 1 : 0)
            .ThenBy(f => Degree(f.Base))
            .ThenBy(f => Key(f.Base))
            .ThenBy(f => f.Base, Comparer<Expr>.Create(CanonicalOrder.Compare))];
    }

    private static Expr Build(BigRational unit, List<(Expr Base, int M)> num, List<(Expr Base, int M)> den)
    {
        var list = new List<Expr>();
        bool unitShown = !unit.IsOne;
        if (unitShown) list.Add(Algebra.Q(unit));
        foreach (var (b, m) in num) list.Add(m == 1 ? b : new PowExpr(b, Expr.Int(m)));
        foreach (var (b, m) in den) list.Add(new PowExpr(b, Expr.Int(-m)));
        if (list.Count == 0) return Expr.One;
        if (list.Count == 1) return list[0];
        // A plain monomial such as 6x² reads better in canonical form.
        if (num.All(f => f.Base is not AddExpr) && den.Count == 0) return Algebra.Mul(list);
        return new MulExpr([.. list]);
    }
}
