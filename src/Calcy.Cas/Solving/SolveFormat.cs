using Calcy.Core.Expressions;

namespace Calcy.Cas.Solving;

/// <summary>Result shapes of solve: x = v, (x = a) or (x = b), (x = 1 and y = 2), true, false.</summary>
internal static class SolveFormat
{
    public static Expr Single(RootSet roots, string x)
    {
        if (roots.All) return BoolExpr.True;
        var xs = new SymbolExpr(x);
        var parts = roots.Values.Select(v => (Expr)new RelationExpr(RelOp.Equal, xs, v)).ToList();
        return Or(parts);
    }

    public static Expr System(List<Dictionary<string, Expr>> solutions, List<string> unknowns)
    {
        var sets = new List<Expr>();
        foreach (var s in solutions)
        {
            var eqs = unknowns.Where(s.ContainsKey).Select(u => (Expr)new RelationExpr(RelOp.Equal, new SymbolExpr(u), s[u])).ToList();
            if (eqs.Count == 0) continue;
            sets.Add(eqs.Count == 1 ? eqs[0] : new LogicExpr(LogicOp.And, [.. eqs]));
        }
        return Or(sets);
    }

    private static Expr Or(List<Expr> parts) => parts.Count switch
    {
        0 => BoolExpr.False,
        1 => parts[0],
        _ => new LogicExpr(LogicOp.Or, [.. parts]),
    };
}
