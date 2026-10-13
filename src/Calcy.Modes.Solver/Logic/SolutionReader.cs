using Calcy.Core.Expressions;

namespace Calcy.Modes.Solver.Logic;

/// <summary>What a solve result says overall.</summary>
public enum SolutionKind
{
    /// <summary>Some values (or solution sets) solve it.</summary>
    Values,

    /// <summary>Nothing solves it (the engine answered false).</summary>
    None,

    /// <summary>Every value solves it (the engine answered true).</summary>
    All,

    /// <summary>The engine could not solve it (the call came back unevaluated, or in an unexpected shape).</summary>
    Unsolved,
}

/// <summary>The solutions of one equation in one unknown.</summary>
/// <param name="Kind">Values, none, all or unsolved.</param>
/// <param name="Values">The values of the unknown (empty unless <paramref name="Kind"/> is Values).</param>
public sealed record SingleSolutions(SolutionKind Kind, IReadOnlyList<Expr> Values);

/// <summary>The solution sets of a system.</summary>
/// <param name="Kind">Values, none, all or unsolved.</param>
/// <param name="Sets">Each set maps the unknowns to their values (empty unless <paramref name="Kind"/> is Values).</param>
public sealed record SystemSolutions(SolutionKind Kind, IReadOnlyList<IReadOnlyDictionary<string, Expr>> Sets);

/// <summary>
/// Reads the relation-shaped results of solve and nsolve: x = v, (x = a) or (x = b), (x = 1 and y = 2),
/// several such sets joined with or, true (every value) and false (no solution).
/// </summary>
public static class SolutionReader
{
    /// <summary>The values of <paramref name="x"/> in a one-unknown result.</summary>
    public static SingleSolutions ReadSingle(Expr result, string x)
    {
        switch (result)
        {
            case BoolExpr b:
                return new SingleSolutions(b.Value ? SolutionKind.All : SolutionKind.None, []);
            case RelationExpr rel:
                return ValueOf(rel, x) is { } v ? new SingleSolutions(SolutionKind.Values, [v]) : Unsolved();
            case LogicExpr { Op: LogicOp.Or } or:
                var values = new List<Expr>();
                foreach (var part in or.Operands)
                {
                    if (part is BoolExpr { Value: false }) continue;
                    if (part is BoolExpr { Value: true }) return new SingleSolutions(SolutionKind.All, []);
                    if (part is not RelationExpr r || ValueOf(r, x) is not { } value) return Unsolved();
                    if (!values.Contains(value)) values.Add(value);
                }
                return values.Count == 0 ? new SingleSolutions(SolutionKind.None, []) : new SingleSolutions(SolutionKind.Values, values);
            default:
                return Unsolved();
        }

        static SingleSolutions Unsolved() => new(SolutionKind.Unsolved, []);
    }

    /// <summary>The solution sets of a system in the given unknowns.</summary>
    public static SystemSolutions ReadSystem(Expr result, IReadOnlyList<string> unknowns)
    {
        switch (result)
        {
            case BoolExpr b:
                return new SystemSolutions(b.Value ? SolutionKind.All : SolutionKind.None, []);
            case LogicExpr { Op: LogicOp.Or } or:
                var sets = new List<IReadOnlyDictionary<string, Expr>>();
                foreach (var part in or.Operands)
                {
                    if (part is BoolExpr { Value: false }) continue;
                    if (Set(part, unknowns) is not { } set) return new SystemSolutions(SolutionKind.Unsolved, []);
                    sets.Add(set);
                }
                return sets.Count == 0 ? new SystemSolutions(SolutionKind.None, []) : new SystemSolutions(SolutionKind.Values, sets);
            default:
                return Set(result, unknowns) is { } one
                    ? new SystemSolutions(SolutionKind.Values, [one])
                    : new SystemSolutions(SolutionKind.Unsolved, []);
        }
    }

    /// <summary>One solution set: x = a and y = b (a single relation for one unknown).</summary>
    private static Dictionary<string, Expr>? Set(Expr e, IReadOnlyList<string> unknowns)
    {
        var parts = e switch
        {
            LogicExpr { Op: LogicOp.And } and => and.Operands.ToList(),
            RelationExpr => [e],
            _ => null,
        };
        if (parts is null) return null;
        var set = new Dictionary<string, Expr>();
        foreach (var p in parts)
        {
            if (p is BoolExpr { Value: true }) continue;
            if (p is not RelationExpr { Op: RelOp.Equal } r) return null;
            if (r.Left is SymbolExpr s && (unknowns.Count == 0 || unknowns.Contains(s.Name))) set[s.Name] = r.Right;
            else if (r.Right is SymbolExpr s2 && (unknowns.Count == 0 || unknowns.Contains(s2.Name))) set[s2.Name] = r.Left;
            else return null;
        }
        return set.Count == 0 ? null : set;
    }

    private static Expr? ValueOf(RelationExpr r, string x)
    {
        if (r.Op != RelOp.Equal) return null;
        if (r.Left is SymbolExpr l && l.Name == x) return r.Right;
        if (r.Right is SymbolExpr rr && rr.Name == x) return r.Left;
        return null;
    }
}
