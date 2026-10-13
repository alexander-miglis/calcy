using System.Numerics;
using Calcy.Core.Expressions;

namespace Calcy.Cas.Symbolic;

/// <summary>A polynomial in one variable whose coefficients are expressions free of that variable (a x² + b x + c).</summary>
internal sealed class ExprPoly
{
    private readonly Expr[] _c;

    public string Variable { get; }

    public ExprPoly(string variable, Expr[] coefficients)
    {
        Variable = variable;
        int n = coefficients.Length;
        while (n > 0 && Simplifier.IsZero(coefficients[n - 1])) n--;
        _c = coefficients[..n];
    }

    public int Degree => _c.Length - 1;
    public bool IsZero => _c.Length == 0;
    public Expr this[int i] => i >= 0 && i < _c.Length ? _c[i] : Expr.Zero;
    public Expr Lc => _c.Length == 0 ? Expr.Zero : _c[^1];
    public IReadOnlyList<Expr> Coefficients => _c;

    /// <summary>Reads e as a polynomial in x (after expansion), or null when x occurs other than in whole powers.</summary>
    public static ExprPoly? From(Expr e, string x, int maxDegree = 10_000)
    {
        if (!e.ContainsSymbol(x)) return new ExprPoly(x, [e]);
        Expr expanded;
        try { expanded = Expander.Expand(e); }
        catch (CasLimitException) { return null; }
        var terms = expanded is AddExpr a ? (IReadOnlyList<Expr>)a.Terms : [expanded];
        var acc = new Dictionary<int, List<Expr>>();
        int max = 0;
        foreach (var t in terms)
        {
            var factors = t is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [t];
            int k = 0;
            var rest = new List<Expr>();
            foreach (var f in factors)
            {
                if (f is SymbolExpr s && s.Name == x) k += 1;
                else if (f is PowExpr { Base: SymbolExpr bs, Exponent: NumberExpr { Value.IsExactInteger: true } ne } && bs.Name == x && ne.Value.IsPositiveReal)
                {
                    var p = ne.Value.ToBigInteger();
                    if (p > maxDegree) return null;
                    k += (int)p;
                }
                else if (f.ContainsSymbol(x)) return null;
                else rest.Add(f);
            }
            if (k > maxDegree) return null;
            if (!acc.TryGetValue(k, out var list)) acc[k] = list = [];
            list.Add(Algebra.Mul(rest));
            max = Math.Max(max, k);
        }
        var c = new Expr[max + 1];
        for (int i = 0; i <= max; i++) c[i] = acc.TryGetValue(i, out var l) ? Algebra.Add(l) : Expr.Zero;
        return new ExprPoly(x, c);
    }

    public Expr ToExpr()
    {
        var x = new SymbolExpr(Variable);
        var terms = new List<Expr>();
        for (int i = 0; i < _c.Length; i++) terms.Add(Algebra.Mul(_c[i], Algebra.Pow(x, Expr.Int(i))));
        return Algebra.Add(terms);
    }

    public ExprPoly Derivative()
    {
        if (_c.Length <= 1) return new ExprPoly(Variable, []);
        var r = new Expr[_c.Length - 1];
        for (int i = 1; i < _c.Length; i++) r[i - 1] = Algebra.Mul(Expr.Int(i), _c[i]);
        return new ExprPoly(Variable, r);
    }

    /// <summary>Division by another polynomial over the field of rational expressions in the parameters.</summary>
    public (ExprPoly Quotient, ExprPoly Remainder) DivRem(ExprPoly b)
    {
        if (b.IsZero) throw new DivideByZeroException("Polynomial division by zero");
        var r = _c.ToArray();
        int db = b.Degree;
        if (Degree < db) return (new ExprPoly(Variable, []), this);
        var q = new Expr[Degree - db + 1];
        for (int k = q.Length - 1; k >= 0; k--)
        {
            CasEnv.Check();
            var coef = Simplifier.Cancel(Algebra.Div(r[k + db], b.Lc));
            q[k] = coef;
            if (Simplifier.IsZero(coef)) continue;
            for (int j = 0; j <= db; j++) r[k + j] = Simplifier.Cancel(Algebra.Sub(r[k + j], Algebra.Mul(coef, b[j])));
        }
        return (new ExprPoly(Variable, q), new ExprPoly(Variable, r[..Math.Min(r.Length, db)]));
    }

    /// <summary>Value at a point.</summary>
    public Expr Eval(Expr value)
    {
        Expr acc = Expr.Zero;
        for (int i = _c.Length - 1; i >= 0; i--) acc = Algebra.Add(Algebra.Mul(acc, value), _c[i]);
        return acc;
    }

    public static BigInteger Binomial(int n, int k) => Core.Numbers.IntegerMath.Binomial(n, k);
}
