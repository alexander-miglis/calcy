using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>The characteristic polynomial p(λ) = det(λI − A), computed exactly with the Faddeev-LeVerrier recurrence.</summary>
public static class CharacteristicPolynomial
{
    /// <summary>The variable used for characteristic polynomials.</summary>
    public const string Variable = "λ";

    /// <summary>
    /// Coefficients c₀ ... cₙ (ascending powers, cₙ = 1) of det(λI − A). Works for any entries (fractions, roots, symbols)
    /// because the recurrence only adds, multiplies and divides by whole numbers.
    /// </summary>
    /// <param name="a">A square matrix.</param>
    /// <param name="session">Optional session; when it has an expand() function, symbolic coefficients are expanded.</param>
    /// <param name="ct">Cancellation.</param>
    public static Expr[] Coefficients(MatrixExpr a, CalcSession? session = null, CancellationToken ct = default)
    {
        if (a.Rows != a.Cols) throw new CalcException($"The characteristic polynomial needs a square matrix, not {a.Rows}×{a.Cols}");
        int n = a.Rows;
        var c = new Expr[n + 1];
        c[n] = Expr.One;
        var identity = MatrixOps.Identity(n);
        MatrixExpr am = MatrixOps.Zeros(n, n);
        for (int k = 1; k <= n; k++)
        {
            ct.ThrowIfCancellationRequested();
            var m = Add(am, Scale(c[n - k + 1], identity));
            am = MatrixOps.Multiply(a, m);
            if (!NumericBridge.IsRational(am)) am = new MatrixExpr(n, n, [.. am.Items.Select(e => Tidy(e, session, ct))]);
            var trace = MatrixOps.Trace(am);
            c[n - k] = Tidy(Algebra.Neg(Algebra.Div(trace, Expr.Int(k))), session, ct);
        }
        return c;
    }

    /// <summary>The polynomial Σ cₖ λᵏ as an expression.</summary>
    public static Expr ToExpr(IReadOnlyList<Expr> ascending, string variable = Variable)
    {
        var x = new SymbolExpr(variable);
        var terms = new List<Expr>();
        for (int k = ascending.Count - 1; k >= 0; k--)
        {
            if (RowReduction.IsZero(ascending[k])) continue;
            terms.Add(k == 0 ? ascending[k] : Algebra.Mul(ascending[k], k == 1 ? x : Algebra.Pow(x, Expr.Int(k))));
        }
        return terms.Count == 0 ? Expr.Zero : Algebra.Add(terms);
    }

    /// <summary>Readable polynomial text, highest power first (for example λ³ - 6λ² + 11λ - 6).</summary>
    public static string Format(IReadOnlyList<Expr> ascending, PrintOptions options) => ExprPrinter.Print(ToExpr(ascending), options);

    private static MatrixExpr Add(MatrixExpr x, MatrixExpr y) => new(x.Rows, x.Cols, [.. x.Items.Zip(y.Items, (p, q) => Algebra.Add(p, q))]);

    private static MatrixExpr Scale(Expr k, MatrixExpr x) => new(x.Rows, x.Cols, [.. x.Items.Select(e => Algebra.Mul(k, e))]);

    /// <summary>Expands symbolic results when the engine offers expand(); exact numbers pass through unchanged.</summary>
    internal static Expr Tidy(Expr e, CalcSession? session, CancellationToken ct)
    {
        if (e is NumberExpr || session is null || !session.Functions.Contains("expand")) return e;
        try
        {
            return session.Evaluate(CallExpr.Of("expand", e), approximate: false, ct);
        }
        catch (CalcException)
        {
            return e;
        }
    }
}
