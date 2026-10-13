namespace Calcy.Modes.Solver.Logic;

/// <summary>
/// Chooses the unknown of an equation when the user has not picked one: x first, then the other usual letters, then
/// the first name in reading order. Names that hold a stored value (a = 5 in Advanced) are known numbers, not unknowns.
/// </summary>
public static class UnknownPicker
{
    /// <summary>The letters people solve for, most likely first.</summary>
    private static readonly string[] Preferred = ["x", "y", "z", "t", "θ", "w", "u", "v"];

    /// <summary>The names that can be solved for, most likely first (stored variables only when nothing else is left).</summary>
    public static IReadOnlyList<string> Candidates(IReadOnlyList<string> freeSymbols, Func<string, bool>? isStored = null)
    {
        var free = freeSymbols.Distinct().ToList();
        var open = isStored is null ? free : free.Where(n => !isStored(n)).ToList();
        if (open.Count == 0) open = free;
        return Order(open);
    }

    /// <summary>The unknown to solve for: the preferred one when it occurs, otherwise the most likely candidate, x when there is none.</summary>
    public static string Pick(IReadOnlyList<string> freeSymbols, Func<string, bool>? isStored = null, string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && freeSymbols.Contains(preferred)) return preferred;
        var candidates = Candidates(freeSymbols, isStored);
        return candidates.Count > 0 ? candidates[0] : "x";
    }

    /// <summary>Sorts names: x, y, z, t, θ, w, u, v first (in that order), then the rest in their original order.</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> names)
    {
        var list = names.Distinct().ToList();
        return [.. list.OrderBy(n => Array.IndexOf(Preferred, n) is var i && i >= 0 ? i : Preferred.Length + list.IndexOf(n))];
    }
}
