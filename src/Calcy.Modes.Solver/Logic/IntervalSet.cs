using Calcy.Core.Expressions;

namespace Calcy.Modes.Solver.Logic;

/// <summary>An end point of an interval: its exact value, its decimal position and whether it belongs to the interval.</summary>
/// <param name="Value">The exact end point (−2, √2, e ...).</param>
/// <param name="Approx">Its decimal value, for ordering and drawing.</param>
/// <param name="Closed">True when the end point is included (≤ or ≥), false when it is not (&lt; or &gt;).</param>
public sealed record Bound(Expr Value, double Approx, bool Closed);

/// <summary>An interval of the real line. A null lower end is −∞, a null upper end is +∞.</summary>
/// <param name="Lower">The lower end, or null for −∞.</param>
/// <param name="Upper">The upper end, or null for +∞.</param>
public sealed record SolutionInterval(Bound? Lower, Bound? Upper)
{
    /// <summary>The lower end as a number (−∞ when unbounded).</summary>
    public double Lo => Lower?.Approx ?? double.NegativeInfinity;

    /// <summary>The upper end as a number (+∞ when unbounded).</summary>
    public double Hi => Upper?.Approx ?? double.PositiveInfinity;

    /// <summary>A single point [a, a].</summary>
    public bool IsPoint => Lower is not null && Upper is not null && IntervalSet.Same(Lower.Approx, Upper.Approx);

    /// <summary>The whole real line.</summary>
    public bool IsAll => Lower is null && Upper is null;

    /// <summary>True when the number lies in the interval.</summary>
    public bool Contains(double v)
    {
        bool aboveLower = Lower is null || v > Lower.Approx || (Lower.Closed && IntervalSet.Same(v, Lower.Approx));
        bool belowUpper = Upper is null || v < Upper.Approx || (Upper.Closed && IntervalSet.Same(v, Upper.Approx));
        return aboveLower && belowUpper;
    }
}

/// <summary>
/// A union of disjoint intervals, kept sorted and merged: the solution set of an inequality. Gives the interval
/// notation, the inequality form and the end points for the number line.
/// </summary>
public sealed class IntervalSet
{
    private IntervalSet(IReadOnlyList<SolutionInterval> intervals) => Intervals = intervals;

    /// <summary>The disjoint intervals, left to right.</summary>
    public IReadOnlyList<SolutionInterval> Intervals { get; }

    /// <summary>No real number.</summary>
    public static IntervalSet Empty { get; } = new([]);

    /// <summary>Every real number.</summary>
    public static IntervalSet All { get; } = new([new SolutionInterval(null, null)]);

    /// <summary>True when nothing is in the set.</summary>
    public bool IsEmpty => Intervals.Count == 0;

    /// <summary>True when the set is the whole real line.</summary>
    public bool IsAll => Intervals.Count == 1 && Intervals[0].IsAll;

    /// <summary>The finite end points, left to right (for the number line).</summary>
    public IReadOnlyList<Bound> EndPoints
    {
        get
        {
            var list = new List<Bound>();
            foreach (var iv in Intervals)
            {
                if (iv.Lower is { } lo) list.Add(lo);
                if (iv.Upper is { } hi && !iv.IsPoint) list.Add(hi);
            }
            return list;
        }
    }

    /// <summary>The set of all numbers except some points, as the points (for x ≠ a); null when it is not of that shape.</summary>
    public IReadOnlyList<Bound>? ExcludedPoints
    {
        get
        {
            if (Intervals.Count < 2 || Intervals[0].Lower is not null || Intervals[^1].Upper is not null) return null;
            var points = new List<Bound>();
            for (int i = 0; i + 1 < Intervals.Count; i++)
            {
                var a = Intervals[i].Upper;
                var b = Intervals[i + 1].Lower;
                if (a is null || b is null || a.Closed || b.Closed || !Same(a.Approx, b.Approx)) return null;
                points.Add(a);
            }
            return points;
        }
    }

    /// <summary>True when the number is in the set.</summary>
    public bool Contains(double v) => Intervals.Any(i => i.Contains(v));

    /// <summary>The union of the intervals, sorted and merged where they overlap or touch.</summary>
    public static IntervalSet Union(IEnumerable<SolutionInterval> intervals)
    {
        var valid = intervals.Where(IsValid)
            .OrderBy(i => i.Lo)
            .ThenBy(i => i.Lower is { Closed: true } ? 0 : 1)
            .ToList();
        var merged = new List<SolutionInterval>();
        foreach (var iv in valid)
        {
            if (merged.Count == 0)
            {
                merged.Add(iv);
                continue;
            }
            var last = merged[^1];
            bool overlaps = iv.Lo < last.Hi && !Same(iv.Lo, last.Hi);
            bool touches = Same(iv.Lo, last.Hi) && ((last.Upper?.Closed ?? false) || (iv.Lower?.Closed ?? false));
            if (iv.Lower is null || last.Upper is null || overlaps || touches)
            {
                var lower = last.Lower is null || iv.Lower is null ? null
                    : Same(last.Lower.Approx, iv.Lower.Approx) ? last.Lower with { Closed = last.Lower.Closed || iv.Lower.Closed } : last.Lower;
                merged[^1] = new SolutionInterval(lower, MaxUpper(last.Upper, iv.Upper));
            }
            else merged.Add(iv);
        }
        return new IntervalSet(merged);
    }

    /// <summary>The union of two sets.</summary>
    public IntervalSet Or(IntervalSet other) => Union(Intervals.Concat(other.Intervals));

    /// <summary>The numbers in both sets.</summary>
    public IntervalSet And(IntervalSet other)
    {
        var parts = new List<SolutionInterval>();
        foreach (var a in Intervals)
            foreach (var b in other.Intervals)
                parts.Add(new SolutionInterval(MaxLower(a.Lower, b.Lower), MinUpper(a.Upper, b.Upper)));
        return Union(parts);
    }

    // ------------------------------------------------------------------ text

    /// <summary>The inequality form of each interval: −2 &lt; x &lt; 3, x ≥ 1, x = 0, or x ≠ 1 for the line without points.</summary>
    public IReadOnlyList<string> InequalityParts(string x, Func<Expr, string> show)
    {
        if (IsEmpty) return [];
        if (IsAll) return [$"{x} ∈ ℝ"];
        if (ExcludedPoints is { } excluded) return [.. excluded.Select(p => $"{x} ≠ {show(p.Value)}")];
        return [.. Intervals.Select(iv => Part(iv, x, show))];
    }

    private static string Part(SolutionInterval iv, string x, Func<Expr, string> show)
    {
        if (iv.IsPoint) return $"{x} = {show(iv.Lower!.Value)}";
        if (iv.Lower is null) return $"{x} {(iv.Upper!.Closed ? "≤" : "<")} {show(iv.Upper.Value)}";
        if (iv.Upper is null) return $"{x} {(iv.Lower.Closed ? "≥" : ">")} {show(iv.Lower.Value)}";
        return $"{show(iv.Lower.Value)} {(iv.Lower.Closed ? "≤" : "<")} {x} {(iv.Upper.Closed ? "≤" : "<")} {show(iv.Upper.Value)}";
    }

    /// <summary>The solution in words and symbols on one line: −2 &lt; x &lt; 3 or x ≥ 5.</summary>
    public string InequalityText(string x, Func<Expr, string> show)
    {
        if (IsEmpty) return "no real " + x;
        var parts = InequalityParts(x, show);
        return ExcludedPoints is not null ? string.Join(" and ", parts) : string.Join(" or ", parts);
    }

    /// <summary>Interval notation: (−∞, −2) ∪ [1, ∞), {3} for a point, ∅ when empty.</summary>
    public string IntervalNotation(Func<Expr, string> show)
    {
        if (IsEmpty) return "∅";
        return string.Join(" ∪ ", Intervals.Select(iv =>
        {
            if (iv.IsPoint) return "{" + show(iv.Lower!.Value) + "}";
            string lo = iv.Lower is null ? "(" + MathText.Minus + "∞" : (iv.Lower.Closed ? "[" : "(") + show(iv.Lower.Value);
            string hi = iv.Upper is null ? "∞)" : show(iv.Upper.Value) + (iv.Upper.Closed ? "]" : ")");
            return lo + ", " + hi;
        }));
    }

    /// <summary>Linear Calcy input for the set: x &lt; -2 or x &gt;= 1, -2 &lt; x and x &lt; 3.</summary>
    public string InputText(string x)
    {
        if (IsEmpty) return "false";
        if (IsAll) return "true";
        if (ExcludedPoints is { } excluded) return string.Join(" and ", excluded.Select(p => $"{x} != {MathText.Input(p.Value)}"));
        return string.Join(" or ", Intervals.Select(iv =>
        {
            if (iv.IsPoint) return $"{x} = {MathText.Input(iv.Lower!.Value)}";
            if (iv.Lower is null) return $"{x} {(iv.Upper!.Closed ? "<=" : "<")} {MathText.Input(iv.Upper.Value)}";
            if (iv.Upper is null) return $"{x} {(iv.Lower.Closed ? ">=" : ">")} {MathText.Input(iv.Lower.Value)}";
            return $"{MathText.Input(iv.Lower.Value)} {(iv.Lower.Closed ? "<=" : "<")} {x} and {x} {(iv.Upper.Closed ? "<=" : "<")} {MathText.Input(iv.Upper.Value)}";
        }));
    }

    /// <summary>LaTeX for the inequality form.</summary>
    public string Latex(string x)
    {
        if (IsEmpty) return @"\varnothing";
        if (IsAll) return x + @" \in \mathbb{R}";
        string L(Expr e) => MathText.Latex(e);
        if (ExcludedPoints is { } excluded) return MathText.LatexAnd(excluded.Select(p => $@"{x} \neq {L(p.Value)}"));
        return MathText.LatexOr(Intervals.Select(iv =>
        {
            if (iv.IsPoint) return $"{x} = {L(iv.Lower!.Value)}";
            if (iv.Lower is null) return $"{x} {(iv.Upper!.Closed ? @"\leq" : "<")} {L(iv.Upper.Value)}";
            if (iv.Upper is null) return $"{x} {(iv.Lower.Closed ? @"\geq" : ">")} {L(iv.Lower.Value)}";
            return $"{L(iv.Lower.Value)} {(iv.Lower.Closed ? @"\leq" : "<")} {x} {(iv.Upper.Closed ? @"\leq" : "<")} {L(iv.Upper.Value)}";
        }));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Two decimal positions that are the same point.</summary>
    public static bool Same(double a, double b) =>
        a == b || Math.Abs(a - b) <= 1e-12 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    private static bool IsValid(SolutionInterval iv)
    {
        if (iv.Lower is null || iv.Upper is null) return true;
        if (Same(iv.Lower.Approx, iv.Upper.Approx)) return iv.Lower.Closed && iv.Upper.Closed;
        return iv.Lower.Approx < iv.Upper.Approx;
    }

    private static Bound? MaxLower(Bound? a, Bound? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        if (Same(a.Approx, b.Approx)) return a with { Closed = a.Closed && b.Closed };
        return a.Approx > b.Approx ? a : b;
    }

    private static Bound? MinUpper(Bound? a, Bound? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        if (Same(a.Approx, b.Approx)) return a with { Closed = a.Closed && b.Closed };
        return a.Approx < b.Approx ? a : b;
    }

    private static Bound? MaxUpper(Bound? a, Bound? b)
    {
        if (a is null || b is null) return null;
        if (Same(a.Approx, b.Approx)) return a with { Closed = a.Closed || b.Closed };
        return a.Approx > b.Approx ? a : b;
    }
}

/// <summary>Turns the relation-shaped result of solving an inequality into an <see cref="IntervalSet"/>.</summary>
public static class IntervalReader
{
    /// <summary>
    /// Reads x &lt; a, a ≤ x, x = a, x ≠ a, true and false, joined with and/or. Returns null for any other shape.
    /// </summary>
    /// <param name="result">The solve result.</param>
    /// <param name="x">The unknown.</param>
    /// <param name="value">Gives the decimal value of an end point (NaN when it has none).</param>
    public static IntervalSet? Read(Expr result, string x, Func<Expr, double> value)
    {
        switch (result)
        {
            case BoolExpr b:
                return b.Value ? IntervalSet.All : IntervalSet.Empty;
            case LogicExpr { Op: LogicOp.Or or LogicOp.And } l:
                IntervalSet? acc = null;
                foreach (var part in l.Operands)
                {
                    var s = Read(part, x, value);
                    if (s is null) return null;
                    acc = acc is null ? s : l.Op == LogicOp.Or ? acc.Or(s) : acc.And(s);
                }
                return acc ?? (l.Op == LogicOp.Or ? IntervalSet.Empty : IntervalSet.All);
            case RelationExpr rel:
                return Relation(rel, x, value);
            default:
                return null;
        }
    }

    private static IntervalSet? Relation(RelationExpr rel, string x, Func<Expr, double> value)
    {
        RelOp op;
        Expr v;
        if (rel.Left is SymbolExpr l && l.Name == x && !rel.Right.ContainsSymbol(x))
        {
            op = rel.Op;
            v = rel.Right;
        }
        else if (rel.Right is SymbolExpr r && r.Name == x && !rel.Left.ContainsSymbol(x))
        {
            op = RelationExpr.Flip(rel.Op);
            v = rel.Left;
        }
        else return null;
        double d = value(v);
        if (!double.IsFinite(d)) return null;
        Bound B(bool closed) => new(v, d, closed);
        return op switch
        {
            RelOp.Less => IntervalSet.Union([new SolutionInterval(null, B(false))]),
            RelOp.LessEqual => IntervalSet.Union([new SolutionInterval(null, B(true))]),
            RelOp.Greater => IntervalSet.Union([new SolutionInterval(B(false), null)]),
            RelOp.GreaterEqual => IntervalSet.Union([new SolutionInterval(B(true), null)]),
            RelOp.Equal => IntervalSet.Union([new SolutionInterval(B(true), B(true))]),
            RelOp.NotEqual => IntervalSet.Union([new SolutionInterval(null, B(false)), new SolutionInterval(B(false), null)]),
            _ => null,
        };
    }
}
