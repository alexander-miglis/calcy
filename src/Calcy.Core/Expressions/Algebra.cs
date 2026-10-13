using System.Collections.Immutable;
using System.Numerics;
using Calcy.Core.Numbers;

namespace Calcy.Core.Expressions;

/// <summary>
/// Simplifying constructors. Every function assumes its inputs are already simplified and returns a canonical form:
/// sums and products are flattened, numbers are folded, like terms and equal bases are combined, radicals are reduced
/// (√8 = 2√2, 1/√2 = √2/2) and lists and matrices are handled elementwise.
/// </summary>
public static class Algebra
{
    [ThreadStatic] private static int _depth;

    /// <summary>When true (the default), odd roots of negative numbers are real: (-8)^(1/3) = -2.</summary>
    [ThreadStatic] private static bool _principalRoots;

    /// <summary>Runs <paramref name="body"/> with principal (complex) roots instead of real odd roots.</summary>
    public static T WithPrincipalRoots<T>(Func<T> body)
    {
        bool old = _principalRoots;
        _principalRoots = true;
        try { return body(); }
        finally { _principalRoots = old; }
    }

    public static bool RealOddRoots => !_principalRoots;

    public static NumberExpr Num(Number n) => new(n);
    public static NumberExpr Int(long v) => new(Number.FromInt(v));
    public static NumberExpr Q(BigRational r) => new(Number.Exact(r));

    public static Expr Neg(Expr a) => Mul(Expr.MinusOne, a);
    public static Expr Sub(Expr a, Expr b) => Add(a, Neg(b));
    public static Expr Div(Expr a, Expr b) => Mul(a, Pow(b, Expr.MinusOne));
    public static Expr Sqrt(Expr a) => Pow(a, Expr.HalfExpr);
    public static Expr Add(params Expr[] terms) => Add((IEnumerable<Expr>)terms);
    public static Expr Mul(params Expr[] factors) => Mul((IEnumerable<Expr>)factors);

    // ------------------------------------------------------------------ Add

    public static Expr Add(IEnumerable<Expr> terms)
    {
        var flat = new List<Expr>();
        foreach (var t in terms) Flatten(t, ExprKind.Add, flat);
        if (flat.Count == 0) return Expr.Zero;
        if (flat.Count == 1) return flat[0];

        if (flat.Any(t => t is ListExpr)) return Broadcast(flat, a => Add(a));
        if (flat.Any(t => t is MatrixExpr)) return MatrixAdd(flat);
        if (flat.Any(t => t is TupleExpr)) return TupleAdd(flat);
        if (flat.Any(t => t is StringExpr or BoolExpr or RelationExpr or LogicExpr))
            return RelationArithmetic(flat, a => Add(a)) ?? throw new CalcException("These values cannot be added");

        Number sum = Number.Zero;
        bool sawNumber = false;
        int posInf = 0, negInf = 0;
        var order = new List<Expr>();
        var coeffs = new Dictionary<Expr, Number>();
        foreach (var t in flat)
        {
            switch (t)
            {
                case NumberExpr n:
                    sum += n.Value;
                    sawNumber = true;
                    break;
                case ConstantExpr { Constant: ConstantKind.Infinity }:
                    posInf++;
                    break;
                case ConstantExpr { Constant: ConstantKind.NegativeInfinity }:
                    negInf++;
                    break;
                case ConstantExpr { Constant: ConstantKind.Undefined }:
                    return ConstantExpr.Undefined;
                default:
                    var (c, rest) = SplitCoefficient(t);
                    if (rest is ConstantExpr { IsInfinite: true } inf)
                    {
                        if (c.IsReal && !c.IsZero)
                        {
                            bool positive = (inf.Constant == ConstantKind.Infinity) == c.IsPositiveReal;
                            if (positive) posInf++; else negInf++;
                            break;
                        }
                    }
                    if (coeffs.TryGetValue(rest, out var existing)) coeffs[rest] = existing + c;
                    else
                    {
                        coeffs[rest] = c;
                        order.Add(rest);
                    }
                    break;
            }
        }
        if (posInf > 0 && negInf > 0) return ConstantExpr.Undefined;
        if (posInf > 0) return ConstantExpr.Infinity;
        if (negInf > 0) return ConstantExpr.NegativeInfinity;

        var result = new List<Expr>(order.Count + 1);
        foreach (var rest in order)
        {
            var c = coeffs[rest];
            if (c.IsZero) continue;
            result.Add(c.IsOne ? rest : Mul(new NumberExpr(c), rest));
        }
        if (!sum.IsZero || (sawNumber && result.Count == 0) || (!sum.IsExact && result.Count == 0)) result.Add(new NumberExpr(sum));
        // Terms produced by Mul above can themselves be sums (distribution), so flatten again if needed.
        if (result.Any(r => r is AddExpr))
        {
            if (++_depth > 50) { _depth--; return new AddExpr([.. result]); }
            try { return Add(result); }
            finally { _depth--; }
        }
        if (result.Count == 0) return sum.IsExact ? Expr.Zero : new NumberExpr(Number.Approx(0));
        if (result.Count == 1) return result[0];
        result.Sort(CanonicalOrder.Compare);
        return new AddExpr([.. result]);
    }

    /// <summary>Splits a term into its numeric coefficient and the remaining factors.</summary>
    public static (Number Coefficient, Expr Remainder) SplitCoefficient(Expr term)
    {
        if (term is NumberExpr n) return (n.Value, Expr.One);
        if (term is MulExpr m && m.Factors[0] is NumberExpr c)
        {
            if (m.Factors.Length == 2) return (c.Value, m.Factors[1]);
            return (c.Value, new MulExpr(m.Factors.RemoveAt(0)));
        }
        return (Number.One, term);
    }

    // ------------------------------------------------------------------ Mul

    public static Expr Mul(IEnumerable<Expr> factors)
    {
        var flat = new List<Expr>();
        foreach (var f in factors) Flatten(f, ExprKind.Mul, flat);
        if (flat.Count == 0) return Expr.One;
        if (flat.Count == 1) return flat[0];

        if (flat.Any(t => t is ListExpr)) return Broadcast(flat, a => Mul(a));
        if (flat.Any(t => t is MatrixExpr)) return MatrixMul(flat);
        if (flat.Any(t => t is TupleExpr)) return TupleScale(flat);
        if (flat.Any(t => t is StringExpr or BoolExpr or RelationExpr or LogicExpr))
            return RelationArithmetic(flat, a => Mul(a)) ?? throw new CalcException("These values cannot be multiplied");

        Number coeff = Number.One;
        int infinities = 0;
        var bases = new List<Expr>();
        var exponents = new Dictionary<Expr, List<Expr>>();
        foreach (var f in flat)
        {
            switch (f)
            {
                case NumberExpr n:
                    coeff *= n.Value;
                    break;
                case ConstantExpr { Constant: ConstantKind.Infinity }:
                    infinities++;
                    break;
                case ConstantExpr { Constant: ConstantKind.NegativeInfinity }:
                    infinities++;
                    coeff = -coeff;
                    break;
                case ConstantExpr { Constant: ConstantKind.Undefined }:
                    return ConstantExpr.Undefined;
                default:
                    var (b, e) = SplitPower(f);
                    if (!exponents.TryGetValue(b, out var list))
                    {
                        list = [];
                        exponents[b] = list;
                        bases.Add(b);
                    }
                    list.Add(e);
                    break;
            }
        }

        if (infinities > 0)
        {
            if (coeff.IsZero) return ConstantExpr.Undefined;
            if (bases.Count == 0 && coeff.IsReal)
                return coeff.IsPositiveReal ? ConstantExpr.Infinity : ConstantExpr.NegativeInfinity;
        }
        if (coeff.IsZero && infinities == 0)
            return coeff.IsExact ? Expr.Zero : new NumberExpr(Number.Approx(0));

        // Combine exponents per base, then fold numeric results back into the coefficient.
        var others = new List<Expr>();
        var radicals = new Dictionary<Number, BigRational>(); // exponent -> product of positive rational bases
        var radicalOrder = new List<Number>();
        foreach (var b in bases)
        {
            var exps = exponents[b];
            var e = exps.Count == 1 ? exps[0] : Add(exps);
            if (b is NumberExpr { Value.IsExactRational: true } nb && nb.Value.IsPositiveReal &&
                e is NumberExpr { Value.IsExactRational: true } ne && !ne.Value.IsInteger)
            {
                if (radicals.TryGetValue(ne.Value, out var prod)) radicals[ne.Value] = prod * nb.Value.ReQ;
                else
                {
                    radicals[ne.Value] = nb.Value.ReQ;
                    radicalOrder.Add(ne.Value);
                }
                continue;
            }
            var p = Pow(b, e);
            AddFactor(p, ref coeff, others);
        }
        foreach (var e in radicalOrder)
        {
            var p = Pow(Q(radicals[e]), new NumberExpr(e));
            AddFactor(p, ref coeff, others);
        }
        if (infinities > 0) others.Add(ConstantExpr.Infinity);

        if (coeff.IsZero) return coeff.IsExact ? Expr.Zero : new NumberExpr(Number.Approx(0));

        // Re-merge if folding produced factors that share a base (for example √8 = 2√2 next to √2).
        if (HasDuplicateBases(others))
        {
            if (++_depth > 50) { _depth--; }
            else
            {
                try
                {
                    var again = new List<Expr>(others.Count + 1) { new NumberExpr(coeff) };
                    again.AddRange(others);
                    return Mul(again);
                }
                finally { _depth--; }
            }
        }

        if (others.Count == 0) return new NumberExpr(coeff);
        // Distribute a plain number over a single sum: 2(x + 1) = 2x + 2.
        if (others.Count == 1 && others[0] is AddExpr sumExpr && !coeff.IsOne)
            return Add(sumExpr.Terms.Select(t => Mul(new NumberExpr(coeff), t)));
        if (others.Count == 1 && coeff.IsOne) return others[0];
        others.Sort(CanonicalOrder.Compare);
        if (!coeff.IsOne) others.Insert(0, new NumberExpr(coeff));
        return others.Count == 1 ? others[0] : new MulExpr([.. others]);
    }

    private static void AddFactor(Expr p, ref Number coeff, List<Expr> others)
    {
        switch (p)
        {
            case NumberExpr pn:
                coeff *= pn.Value;
                break;
            case MulExpr pm:
                foreach (var f in pm.Factors)
                {
                    if (f is NumberExpr fn) coeff *= fn.Value;
                    else others.Add(f);
                }
                break;
            default:
                if (!p.IsOne) others.Add(p);
                break;
        }
    }

    private static bool HasDuplicateBases(List<Expr> factors)
    {
        if (factors.Count < 2) return false;
        var seen = new HashSet<Expr>();
        foreach (var f in factors)
        {
            var (b, _) = SplitPower(f);
            if (!seen.Add(b)) return true;
        }
        return false;
    }

    /// <summary>Splits a factor into base and exponent (x is x^1).</summary>
    public static (Expr Base, Expr Exponent) SplitPower(Expr factor) =>
        factor is PowExpr p ? (p.Base, p.Exponent) : (factor, Expr.One);

    // ------------------------------------------------------------------ Pow

    public static Expr Pow(Expr b, Expr e)
    {
        if (e is NumberExpr ne)
        {
            if (ne.Value.IsZero)
            {
                if (b is MatrixExpr mz) return MatrixOps.Identity(RequireSquare(mz));
                return ne.Value.IsExact ? Expr.One : new NumberExpr(Number.Approx(1));
            }
            if (ne.Value.IsOne) return b;
        }
        if (b is ListExpr || e is ListExpr) return Broadcast([b, e], items => Pow(items[0], items[1]));
        if (b is MatrixExpr m) return MatrixPower(m, e);
        if (b is NumberExpr nb)
        {
            if (nb.Value.IsOne && nb.Value.IsExact) return Expr.One;
            if (nb.Value.IsZero)
            {
                if (e is NumberExpr ez && ez.Value.IsReal)
                {
                    if (ez.Value.IsPositiveReal) return b;
                    throw new CalcException("Division by zero");
                }
                return new PowExpr(b, e);
            }
            if (e is NumberExpr en) return NumberPow(nb.Value, en.Value);
        }
        if (b is ConstantExpr cb)
        {
            if (cb.Constant == ConstantKind.E && e is CallExpr { Name: "ln", Args.Length: 1 } lnCall) return lnCall.Args[0];
            if (cb.IsInfinite && e is NumberExpr ie && ie.Value.IsReal)
            {
                if (ie.Value.IsNegativeReal) return Expr.Zero;
                if (cb.Constant == ConstantKind.Infinity) return ConstantExpr.Infinity;
                if (ie.Value.IsExactInteger) return ie.Value.ToBigInteger().IsEven ? ConstantExpr.Infinity : ConstantExpr.NegativeInfinity;
            }
            if (cb.Constant == ConstantKind.Undefined) return cb;
        }
        if (b is PowExpr pb)
        {
            if (e is NumberExpr { Value.IsExactInteger: true }) return Pow(pb.Base, Mul(pb.Exponent, e));
            if (pb.Exponent is NumberExpr { Value.IsExactInteger: true } be && e is NumberExpr { Value.IsExactRational: true } re)
            {
                var beI = be.Value.ToBigInteger();
                var product = be.Value * re.Value;
                if (beI.IsEven && product.IsExactInteger)
                    return Pow(CallExpr.Of("abs", pb.Base), new NumberExpr(product));
            }
            if (pb.Base is NumberExpr { Value.IsPositiveReal: true } || pb.Base is ConstantExpr { Constant: ConstantKind.E or ConstantKind.Pi })
                return Pow(pb.Base, Mul(pb.Exponent, e));
        }
        if (b is MulExpr mb)
        {
            if (e is NumberExpr { Value.IsExactInteger: true }) return Mul(mb.Factors.Select(f => Pow(f, e)));
            if (e is NumberExpr && mb.Factors[0] is NumberExpr { Value.IsPositiveReal: true } c)
            {
                var rest = mb.Factors.Length == 2 ? mb.Factors[1] : new MulExpr(mb.Factors.RemoveAt(0));
                return Mul(Pow(c, e), Pow(rest, e));
            }
        }
        if (b is TupleExpr) throw new CalcException("Cannot raise a point to a power");
        return new PowExpr(b, e);
    }

    private static Expr NumberPow(Number b, Number e)
    {
        if (e.IsExactInteger)
        {
            var r = b.PowInteger(e.ToBigInteger());
            if (r is null) return new PowExpr(new NumberExpr(b), new NumberExpr(e));
            return new NumberExpr(r.Value);
        }
        if (!b.IsExact || !e.IsExact)
        {
            if (e.IsExactRational && b.IsNegativeReal && RealOddRoots)
            {
                var q = e.ReQ;
                if (!q.Den.IsEven)
                {
                    double mag = Math.Pow(-b.Re, q.ToDouble());
                    return new NumberExpr(Number.Approx(q.Num.IsEven ? mag : -mag));
                }
            }
            if (!e.IsExact && b.IsReal && e.IsReal && b.Re > 0) return new NumberExpr(Number.Approx(Math.Pow(b.Re, e.Re)));
            if (b.IsReal && e.IsReal && b.Re >= 0) return new NumberExpr(Number.Approx(Math.Pow(b.Re, e.Re)));
            return new NumberExpr(ComplexMath.Pow(b.ToApprox(), e.ToApprox()));
        }
        // Both exact, exponent not an integer.
        if (!e.IsReal) return new PowExpr(new NumberExpr(b), new NumberExpr(e));
        if (!b.IsReal) return new PowExpr(new NumberExpr(b), new NumberExpr(e));
        var exp = e.ReQ;
        var bq = b.ReQ;
        if (bq.IsNegative)
        {
            var p = exp.Num;
            var q = exp.Den;
            if (!q.IsEven && RealOddRoots)
            {
                var mag = RationalRoot(-bq, exp);
                return p.IsEven ? mag : Neg(mag);
            }
            if (q == 2)
            {
                // (-b)^(p/2) = i^p * b^(p/2)
                var ip = Number.I.PowInteger(p)!.Value;
                return Mul(new NumberExpr(ip), RationalRoot(-bq, exp));
            }
            return new PowExpr(new NumberExpr(b), new NumberExpr(e));
        }
        return RationalRoot(bq, exp);
    }

    /// <summary>Exact b^(p/q) for b &gt; 0 as coefficient times a reduced radical with a rational (rationalized) coefficient.</summary>
    private static Expr RationalRoot(BigRational b, BigRational exponent)
    {
        var p = exponent.Num;
        var q = exponent.Den;
        Expr Raw() => new PowExpr(Q(b), Q(exponent));
        if (q > 64) return Raw();
        int qi = (int)q;
        var k = BigInteger.Divide(p, q);
        var r = p - k * q;
        if (r.Sign < 0) { r += q; k -= 1; }
        if (BigInteger.Abs(k) > 10_000 || b.BitSize * BigInteger.Abs(k) > 2_000_000) return Raw();
        var intPart = b.Pow((int)k);
        if (r.IsZero) return Q(intPart);
        var c = b.Pow((int)r);
        if (c.BitSize > 4000) return Raw();
        var n = c.Num;
        var d = c.Den;
        var N = n * BigInteger.Pow(d, qi - 1);
        if (N.GetBitLength() > 160)
        {
            // Too large to factor reliably; keep the reduced form without extracting powers.
            return Mul(Q(intPart), new PowExpr(Q(c), Q(new BigRational(1, q))));
        }
        var (outside, inside) = IntegerMath.ExtractPower(N, qi);
        var coefficient = intPart * new BigRational(outside, d);
        if (inside.IsOne) return Q(coefficient);
        int root = qi;
        for (int g = qi; g >= 2; g--)
        {
            if (root % g != 0) continue;
            if (IntegerMath.IsPerfectPower(inside, g, out var w))
            {
                inside = w;
                root /= g;
                break;
            }
        }
        if (root == 1) return Q(coefficient * new BigRational(inside));
        var radical = new PowExpr(Q(new BigRational(inside)), Q(new BigRational(1, root)));
        if (coefficient.IsOne) return radical;
        return new MulExpr([Q(coefficient), radical]);
    }

    // ------------------------------------------------------------------ helpers

    private static void Flatten(Expr e, ExprKind kind, List<Expr> into)
    {
        if (e.Kind == kind)
        {
            foreach (var c in e.Children) Flatten(c, kind, into);
        }
        else into.Add(e);
    }

    private static Expr Broadcast(List<Expr> items, Func<IReadOnlyList<Expr>, Expr> op)
    {
        int length = -1;
        foreach (var it in items)
        {
            if (it is ListExpr l)
            {
                if (length >= 0 && l.Count != length) throw new CalcException($"Lists have different lengths ({length} and {l.Count})");
                length = l.Count;
            }
        }
        var result = ImmutableArray.CreateBuilder<Expr>(length);
        for (int i = 0; i < length; i++)
        {
            var args = items.Select(it => it is ListExpr l ? l.Items[i] : it).ToList();
            result.Add(op(args));
        }
        return new ListExpr(result.MoveToImmutable());
    }


    private static Expr MatrixAdd(List<Expr> items)
    {
        MatrixExpr? acc = null;
        foreach (var it in items)
        {
            if (it is not MatrixExpr m) throw new CalcException("Cannot add a number to a matrix (use a matrix of the same size)");
            if (acc is null) { acc = m; continue; }
            if (acc.Rows != m.Rows || acc.Cols != m.Cols)
                throw new CalcException($"Matrix sizes differ ({acc.Rows}×{acc.Cols} and {m.Rows}×{m.Cols})");
            acc = new MatrixExpr(acc.Rows, acc.Cols, [.. acc.Items.Zip(m.Items, (a, b) => Add(a, b))]);
        }
        return acc!;
    }

    private static Expr MatrixMul(List<Expr> items)
    {
        var scalars = new List<Expr>();
        MatrixExpr? acc = null;
        foreach (var it in items)
        {
            if (it is MatrixExpr m) acc = acc is null ? m : MatrixOps.Multiply(acc, m);
            else scalars.Add(it);
        }
        if (scalars.Count == 0) return acc!;
        var s = Mul(scalars);
        return new MatrixExpr(acc!.Rows, acc.Cols, [.. acc.Items.Select(x => Mul(s, x))]);
    }

    private static Expr MatrixPower(MatrixExpr m, Expr e)
    {
        int n = RequireSquare(m);
        if (e is not NumberExpr { Value.IsExactInteger: true } ne) throw new CalcException("Matrix powers need an integer exponent");
        var k = ne.Value.ToBigInteger();
        if (k.Sign < 0)
        {
            m = MatrixOps.Inverse(m);
            k = -k;
        }
        if (k > 10_000) throw new CalcException("Matrix exponent is too large");
        var result = MatrixOps.Identity(n);
        var b = m;
        while (k > 0)
        {
            if (!k.IsEven) result = MatrixOps.Multiply(result, b);
            k >>= 1;
            if (k > 0) b = MatrixOps.Multiply(b, b);
        }
        return result;
    }

    private static int RequireSquare(MatrixExpr m)
    {
        if (m.Rows != m.Cols) throw new CalcException($"This needs a square matrix, not {m.Rows}×{m.Cols}");
        return m.Rows;
    }

    private static Expr TupleAdd(List<Expr> items)
    {
        TupleExpr? acc = null;
        foreach (var it in items)
        {
            if (it is not TupleExpr t) throw new CalcException("Cannot add a number to a point");
            if (acc is null) { acc = t; continue; }
            if (acc.Items.Length != t.Items.Length) throw new CalcException("Points have different dimensions");
            acc = new TupleExpr([.. acc.Items.Zip(t.Items, (a, b) => Add(a, b))]);
        }
        return acc!;
    }

    private static Expr TupleScale(List<Expr> items)
    {
        var tuples = items.OfType<TupleExpr>().ToList();
        if (tuples.Count > 1) throw new CalcException("Cannot multiply two points");
        var s = Mul(items.Where(i => i is not TupleExpr));
        return new TupleExpr([.. tuples[0].Items.Select(x => Mul(s, x))]);
    }

    /// <summary>Applies arithmetic to both sides of an equation: (x = 2) * 3 gives 3x = 6.</summary>
    private static Expr? RelationArithmetic(List<Expr> items, Func<IEnumerable<Expr>, Expr> op)
    {
        var rels = items.OfType<RelationExpr>().ToList();
        if (rels.Count == 0 || items.Any(i => i is StringExpr or BoolExpr or LogicExpr)) return null;
        if (rels.Count == 1 && rels[0].Op == RelOp.Equal)
        {
            var left = items.Select(i => i is RelationExpr r ? r.Left : i);
            var right = items.Select(i => i is RelationExpr r ? r.Right : i);
            return new RelationExpr(RelOp.Equal, op(left), op(right));
        }
        if (rels.Count == items.Count && rels.All(r => r.Op == RelOp.Equal))
            return new RelationExpr(RelOp.Equal, op(rels.Select(r => r.Left)), op(rels.Select(r => r.Right)));
        return null;
    }

    /// <summary>True when the expression is the exact number zero.</summary>
    public static bool IsExactZero(Expr e) => e is NumberExpr { Value.IsExact: true, Value.IsZero: true };
}
