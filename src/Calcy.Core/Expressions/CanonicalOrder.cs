using Calcy.Core.Numbers;

namespace Calcy.Core.Expressions;

/// <summary>A total, deterministic order on expressions used to keep sums and products canonical.</summary>
public static class CanonicalOrder
{
    public static int Compare(Expr? a, Expr? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        // Compare powers by their base first so x, x^2 and y sort as x, x^2, y.
        var (ab, ae) = Algebra.SplitPower(a);
        var (bb, be) = Algebra.SplitPower(b);
        if (!ReferenceEquals(ab, a) || !ReferenceEquals(bb, b))
        {
            int c = CompareCore(ab, bb);
            if (c != 0) return c;
            return CompareCore(ae, be);
        }
        return CompareCore(a, b);
    }

    private static int Rank(Expr e) => e.Kind switch
    {
        ExprKind.Number => 0,
        ExprKind.Constant => 1,
        ExprKind.Symbol => 2,
        ExprKind.Pow => 3,
        ExprKind.Mul => 4,
        ExprKind.Add => 5,
        ExprKind.Call => 6,
        _ => 7 + (int)e.Kind,
    };

    private static int CompareCore(Expr a, Expr b)
    {
        int ra = Rank(a), rb = Rank(b);
        if (ra != rb) return ra.CompareTo(rb);
        switch (a)
        {
            case NumberExpr na:
            {
                var nb = (NumberExpr)b;
                if (na.Value.IsReal && nb.Value.IsReal)
                {
                    int c = Number.CompareReal(na.Value, nb.Value);
                    if (c != 0) return c;
                    return na.Value.IsExact.CompareTo(nb.Value.IsExact);
                }
                return string.CompareOrdinal(na.Value.ToString(), nb.Value.ToString());
            }
            case ConstantExpr ca:
                return ca.Constant.CompareTo(((ConstantExpr)b).Constant);
            case SymbolExpr sa:
                return string.CompareOrdinal(sa.Name, ((SymbolExpr)b).Name);
            case CallExpr fa:
            {
                var fb = (CallExpr)b;
                int c = string.CompareOrdinal(fa.Name, fb.Name);
                if (c != 0) return c;
                return CompareLists(fa.Args, fb.Args);
            }
            case RelationExpr rel:
            {
                var rb2 = (RelationExpr)b;
                int c = rel.Op.CompareTo(rb2.Op);
                if (c != 0) return c;
                return CompareLists(a.Children, b.Children);
            }
            default:
                return CompareLists(a.Children, b.Children);
        }
    }

    private static int CompareLists(IReadOnlyList<Expr> x, IReadOnlyList<Expr> y)
    {
        // Compare from the most significant (last) element, like Mathematica's canonical order for products.
        int i = x.Count - 1, j = y.Count - 1;
        while (i >= 0 && j >= 0)
        {
            int c = Compare(x[i], y[j]);
            if (c != 0) return c;
            i--;
            j--;
        }
        return x.Count.CompareTo(y.Count);
    }
}
