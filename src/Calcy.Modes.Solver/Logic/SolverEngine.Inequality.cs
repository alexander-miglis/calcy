using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Solver.Logic;

/// <summary>The result of the INEQUALITY tool.</summary>
public sealed class InequalityOutcome : SolveOutcome
{
    /// <summary>The inputs that were solved.</summary>
    public required InequalityRequest Request { get; init; }

    /// <summary>The parsed inequality (null when it did not parse).</summary>
    public RelationExpr? Inequality { get; init; }

    /// <summary>The inequality as on-screen text.</summary>
    public string InequalityText { get; init; } = "";

    /// <summary>The unknown.</summary>
    public string Unknown { get; init; } = "x";

    /// <summary>The names the unknown could be (for the SOLVE FOR chips).</summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];

    /// <summary>The solution set.</summary>
    public IntervalSet Solution { get; set; } = IntervalSet.Empty;

    /// <summary>The solution in inequality form, one entry per interval: −2 &lt; x &lt; 3.</summary>
    public IReadOnlyList<string> Parts { get; set; } = [];

    /// <summary>The solution in interval notation: (−2, 3).</summary>
    public string IntervalNotation { get; set; } = "";

    /// <summary>True when the solution was found by a numeric sign chart over a range.</summary>
    public bool Numeric { get; set; }

    /// <summary>The searched range (numeric solutions only), for the number line.</summary>
    public (double Low, double High)? Range { get; set; }
}

public sealed partial class SolverEngine
{
    /// <summary>
    /// Solves one inequality with solve(inequality, x) (a sign chart over the zeros and the points where it is undefined)
    /// and reads the answer into intervals. When there is no exact answer, a numeric sign chart over the range is used.
    /// </summary>
    public InequalityOutcome SolveInequality(InequalityRequest request, CancellationToken ct)
    {
        var r = request.Clone();
        var snapshot = new SolverSnapshot { Inequality = r.Clone() };
        RelationExpr rel;
        string x;
        IReadOnlyList<string> candidates;
        try
        {
            var e = Parse(r.Inequality, "inequality");
            if (e is RelationExpr { Op: RelOp.Equal })
                throw new SolverException("This is an equation: solve it on the ANY EQUATION tab", "inequality");
            if (e is not RelationExpr re)
                throw new SolverException("Type one inequality using <, >, <=, >= or !=, like x^2 - x - 6 < 0", "inequality");
            rel = re;
            var free = rel.FreeSymbols();
            candidates = UnknownPicker.Candidates(free, IsStored);
            x = UnknownPicker.Pick(free, IsStored, r.Unknown);
        }
        catch (SolverException ex)
        {
            return new InequalityOutcome { Request = r, Error = ex, Card = SolveOutcome.ErrorCard(SolverTool.Inequality, MathText.Typographic(r.Inequality), ex, snapshot) };
        }

        string text = Show(rel);
        var o = new InequalityOutcome { Request = r, Inequality = rel, InequalityText = text, Unknown = x, Candidates = candidates };
        try
        {
            IntervalSet? set = null;
            var result = Evaluate(CallExpr.Of("solve", rel, new SymbolExpr(x)), ct);
            if (result is not CallExpr) set = IntervalReader.Read(result, x, v => Real(v, ct));
            if (set is null)
            {
                if (rel.FreeSymbols().Any(s => s != x && !IsStored(s)))
                    throw new SolverException($"This inequality has other unknowns besides {x}; give them values first", "inequality");
                var range = ParseRange(string.IsNullOrWhiteSpace(r.Range) ? "[-10, 10]" : r.Range, ct) ?? throw new SolverException("Give a range");
                set = NumericSignChart(rel, x, range, ct);
                o.Numeric = true;
                o.Range = (range.A, range.B);
                o.Notes.Add($"No exact method applies, so the sign of left {MathText.Minus} right was checked numerically on [{Show(range.Low)}, {Show(range.High)}]. The solution may continue outside that range.");
            }
            o.Solution = set;
            o.Parts = set.InequalityParts(x, Show);
            o.IntervalNotation = set.IntervalNotation(Show);
            DescribeInequality(o);
            if (StoredNote(rel.FreeSymbols(), [x]) is { } stored) o.Notes.Add(stored);
            o.PlotTexts = EquationPlots(rel, x);
            o.Card = InequalityCard(o, snapshot);
            return o;
        }
        catch (SolverException ex)
        {
            return new InequalityOutcome
            {
                Request = r,
                Inequality = rel,
                InequalityText = text,
                Unknown = x,
                Candidates = candidates,
                Error = ex,
                Card = SolveOutcome.ErrorCard(SolverTool.Inequality, text, ex, snapshot),
            };
        }
    }

    /// <summary>
    /// A numeric sign chart: the zeros of left − right in the range split it into pieces; each piece and each zero is
    /// tested, and the pieces where the inequality holds are joined.
    /// </summary>
    private IntervalSet NumericSignChart(RelationExpr rel, string x, (Expr Low, Expr High, double A, double B) range, CancellationToken ct)
    {
        var f = Algebra.Sub(rel.Left, rel.Right);
        var zeros = new List<double>();
        try
        {
            var read = SolutionReader.ReadSingle(
                Evaluate(CallExpr.Of("nsolve", new RelationExpr(RelOp.Equal, f, Expr.Zero), new SymbolExpr(x), range.Low, range.High), ct), x);
            foreach (var v in read.Values)
                if (Real(v, ct) is var d && double.IsFinite(d)) zeros.Add(d);
        }
        catch (SolverException)
        {
            // No zeros in the range: the sign never changes.
        }
        zeros.Sort();
        bool Holds(double value, bool atZero) => atZero
            ? rel.Op is RelOp.LessEqual or RelOp.GreaterEqual or RelOp.Equal
            : !double.IsNaN(value) && rel.Op switch
            {
                RelOp.Less => value < 0,
                RelOp.LessEqual => value <= 0,
                RelOp.Greater => value > 0,
                RelOp.GreaterEqual => value >= 0,
                RelOp.NotEqual => value != 0,
                _ => false,
            };
        Bound At(double v, Expr? exact, bool closed) => new(exact ?? new NumberExpr(Number.Approx(v)), v, closed);

        var points = new List<(double V, Expr? Exact, bool Holds)> { (range.A, range.Low, Holds(ValueAt(f, x, range.A, ct), false)) };
        foreach (var z in zeros)
            if (z > range.A && z < range.B) points.Add((z, null, Holds(0, true)));
        points.Add((range.B, range.High, Holds(ValueAt(f, x, range.B, ct), false)));
        var pieces = new List<SolutionInterval>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            var (lo, loExact, loHolds) = points[i];
            var (hi, hiExact, hiHolds) = points[i + 1];
            bool inside = Holds(ValueAt(f, x, 0.5 * (lo + hi), ct), false);
            if (inside) pieces.Add(new SolutionInterval(At(lo, loExact, loHolds), At(hi, hiExact, hiHolds)));
            else if (loHolds) pieces.Add(new SolutionInterval(At(lo, loExact, true), At(lo, loExact, true)));
        }
        if (points[^1].Holds) pieces.Add(new SolutionInterval(At(range.B, range.High, true), At(range.B, range.High, true)));
        return IntervalSet.Union(pieces);
    }

    private void DescribeInequality(InequalityOutcome o)
    {
        var set = o.Solution;
        var x = o.Unknown;
        if (set.IsEmpty)
        {
            o.Summary = "NO SOLUTION";
            o.Notes.Insert(0, $"No real {x} makes it true.");
            return;
        }
        if (set.IsAll)
        {
            o.Summary = $"EVERY {x.ToUpperInvariant()}";
            o.Notes.Insert(0, $"It is true for every real {x}.");
            return;
        }
        int pieces = set.Intervals.Count;
        o.Summary = set.ExcludedPoints is not null ? $"EVERY {x.ToUpperInvariant()} EXCEPT {SolveOutcome.Count(set.ExcludedPoints.Count, "POINT")}"
            : pieces == 1 ? (set.Intervals[0].IsPoint ? "ONE POINT" : "ONE INTERVAL")
            : set.Intervals.Any(i => i.IsPoint) ? $"{pieces} PIECES" : $"{pieces} INTERVALS";
        var ends = set.EndPoints;
        bool open = ends.Any(b => !b.Closed), closed = ends.Any(b => b.Closed);
        if (open && closed) o.Notes.Insert(0, "Filled squares: the end point is included. Open squares: it is not.");
        else if (open) o.Notes.Insert(0, "Open squares: the end points are not included.");
        else if (closed) o.Notes.Insert(0, "Filled squares: the end points are included.");
    }

    private SolverCard InequalityCard(InequalityOutcome o, SolverSnapshot snapshot)
    {
        var x = o.Unknown;
        var set = o.Solution;
        var line = set.InequalityText(x, Show);
        return new SolverCard
        {
            Tool = SolverTool.Inequality,
            Title = "INEQUALITY",
            Input = o.InequalityText,
            Lines = [line],
            Note = set.IsEmpty || set.IsAll ? null : $"{x} ∈ {o.IntervalNotation}" + (o.Numeric ? "  (numeric)" : ""),
            CopyText = line,
            LatexText = set.Latex(x),
            UseText = set.IsEmpty || set.IsAll ? null : set.InputText(x),
            PlotTexts = o.PlotTexts,
            Request = snapshot,
        };
    }
}
