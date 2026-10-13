using Calcy.Core.Expressions;

namespace Calcy.Modes.Solver.Logic;

/// <summary>How an equation came out.</summary>
public enum EquationStatus
{
    /// <summary>Solutions were found.</summary>
    Solutions,

    /// <summary>No real solution; complex ones may exist (see <see cref="EquationOutcome.ComplexSolutions"/>).</summary>
    NoRealSolution,

    /// <summary>No solution at all (or none in the range).</summary>
    NoSolution,

    /// <summary>Every value is a solution (an identity such as x + 1 = 1 + x).</summary>
    AllValues,

    /// <summary>Neither the exact nor the numeric method found anything.</summary>
    NotFound,
}

/// <summary>The result of the ANY EQUATION tool.</summary>
public sealed class EquationOutcome : SolveOutcome
{
    /// <summary>The inputs that were solved.</summary>
    public required EquationRequest Request { get; init; }

    /// <summary>The parsed equation (null when it did not parse).</summary>
    public Expr? Equation { get; init; }

    /// <summary>The equation as on-screen text.</summary>
    public string EquationText { get; init; } = "";

    /// <summary>The unknown that was solved for.</summary>
    public string Unknown { get; init; } = "x";

    /// <summary>The other names the unknown could be (for the SOLVE FOR chips).</summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];

    /// <summary>How it came out.</summary>
    public EquationStatus Status { get; set; }

    /// <summary>The solutions shown (real ones, or all when complex results were asked for).</summary>
    public List<SolutionValue> Solutions { get; } = [];

    /// <summary>Complex solutions kept back because only real ones were asked for (shown on request).</summary>
    public List<SolutionValue> ComplexSolutions { get; } = [];

    /// <summary>True when the solutions were found numerically (a range, or no exact method).</summary>
    public bool Numeric { get; set; }

    /// <summary>The searched range as on-screen text, or null for an exact solve.</summary>
    public string? RangeText { get; set; }

    /// <summary>Integer parameters of general solutions (n in 2πn + π/6).</summary>
    public IReadOnlyList<string> Parameters { get; set; } = [];

    /// <summary>The step-by-step explanation (when asked for and available).</summary>
    public IReadOnlyList<string> Steps { get; set; } = [];

    /// <summary>The discriminant when the equation is a quadratic without real solutions.</summary>
    public Expr? Discriminant { get; set; }
}

public sealed partial class SolverEngine
{
    /// <summary>
    /// Solves one equation: exactly with solve(equation, x) (the engine falls back to numeric roots on its own), or
    /// numerically with nsolve(equation, x, a, b) when a range is given. Complex solutions are kept back unless asked for.
    /// </summary>
    public EquationOutcome SolveEquation(EquationRequest request, CancellationToken ct)
    {
        var r = request.Clone();
        var snapshot = new SolverSnapshot { Equation = r.Clone() };
        Expr eq;
        string x;
        IReadOnlyList<string> candidates;
        try
        {
            eq = Parse(r.Equation, "equation");
            if (eq is RelationExpr { Op: not RelOp.Equal })
                throw new SolverException("This is an inequality: solve it on the INEQUALITY tab", "equation");
            if (eq is LogicExpr or ListExpr or BoolExpr)
                throw new SolverException("Type one equation here; several equations go on the NONLINEAR SYSTEM tab", "equation");
            var free = eq.FreeSymbols();
            candidates = UnknownPicker.Candidates(free, IsStored);
            x = UnknownPicker.Pick(free, IsStored, r.Unknown);
        }
        catch (SolverException ex)
        {
            return new EquationOutcome { Request = r, Error = ex, Card = SolveOutcome.ErrorCard(SolverTool.Equation, MathText.Typographic(r.Equation), ex, snapshot) };
        }

        string eqText = Show(eq);
        try
        {
            var outcome = new EquationOutcome
            {
                Request = r,
                Equation = eq,
                EquationText = eqText,
                Unknown = x,
                Candidates = candidates,
            };
            var known = new HashSet<string>(eq.FreeSymbols()) { x };
            var range = ParseRange(r.Range, ct);
            if (range is { } rg) SolveInRange(outcome, eq, x, rg, known, ct);
            else SolveExactly(outcome, eq, x, r, known, ct);
            if (StoredNote(eq.FreeSymbols(), [x]) is { } stored) outcome.Notes.Add(stored);
            outcome.PlotTexts = EquationPlots(eq, x);
            outcome.Card = EquationCard(outcome, snapshot);
            return outcome;
        }
        catch (SolverException ex)
        {
            return new EquationOutcome
            {
                Request = r,
                Equation = eq,
                EquationText = eqText,
                Unknown = x,
                Candidates = candidates,
                Error = ex,
                Card = SolveOutcome.ErrorCard(SolverTool.Equation, eqText, ex, snapshot),
            };
        }
    }

    /// <summary>The range [a, b] typed by the user, null when empty. Bad ranges are errors.</summary>
    internal (Expr Low, Expr High, double A, double B)? ParseRange(string? text, CancellationToken ct, string field = "range")
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var split = RangeText.Split(text) ?? throw new SolverException($"Write the {field} as two numbers, like [-10, 10]", field);
        var low = Evaluate(Parse(split.Low, field), ct);
        var high = Evaluate(Parse(split.High, field), ct);
        double a = Real(low, ct), b = Real(high, ct);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new SolverException($"The ends of the {field} must be real numbers", field);
        if (a == b) throw new SolverException($"The {field} is a single point; give two different ends", field);
        if (a > b) (low, high, a, b) = (high, low, b, a);
        return (low, high, a, b);
    }

    private void SolveExactly(EquationOutcome o, Expr eq, string x, EquationRequest r, HashSet<string> known, CancellationToken ct)
    {
        var xs = new SymbolExpr(x);
        var result = Evaluate(CallExpr.Of("solve", eq, xs), ct, complex: r.Complex);
        var read = SolutionReader.ReadSingle(result, x);
        if (read.Kind == SolutionKind.Unsolved)
        {
            // No exact method applies: search numerically over a wide range.
            try
            {
                read = SolutionReader.ReadSingle(Evaluate(CallExpr.Of("nsolve", eq, xs), ct), x);
                o.Numeric = true;
            }
            catch (SolverException ex)
            {
                o.Status = EquationStatus.NotFound;
                o.Summary = "NO SOLUTION FOUND";
                o.Notes.Add(ex.Message.EndsWith('.') ? ex.Message : ex.Message + ".");
                o.Notes.Add("Give a range to search, for example [-10, 10].");
                return;
            }
        }

        switch (read.Kind)
        {
            case SolutionKind.All:
                o.Status = EquationStatus.AllValues;
                o.Summary = $"EVERY {x.ToUpperInvariant()} IS A SOLUTION";
                o.Notes.Add("Both sides are always equal: the equation is an identity.");
                break;
            case SolutionKind.None:
            case SolutionKind.Unsolved:
                o.Status = EquationStatus.NoSolution;
                if (!r.Complex)
                {
                    // Complex solutions on request: look for them now so the screen can offer them.
                    try
                    {
                        var complex = SolutionReader.ReadSingle(Evaluate(CallExpr.Of("solve", eq, xs), ct, complex: true), x);
                        if (complex.Kind == SolutionKind.Values)
                        {
                            o.ComplexSolutions.AddRange(complex.Values.Select(v => Describe(v, known, ct)).Where(v => !v.IsReal));
                            if (o.ComplexSolutions.Count > 0) o.Status = EquationStatus.NoRealSolution;
                        }
                    }
                    catch (SolverException)
                    {
                        // No complex solutions then.
                    }
                }
                break;
            default:
                var values = read.Values.Select(v => Describe(v, known, ct)).ToList();
                if (r.Complex) o.Solutions.AddRange(values);
                else
                {
                    o.Solutions.AddRange(values.Where(v => v.IsReal));
                    o.ComplexSolutions.AddRange(values.Where(v => !v.IsReal));
                }
                o.Status = o.Solutions.Count > 0 ? EquationStatus.Solutions : EquationStatus.NoRealSolution;
                break;
        }

        o.Numeric |= o.Solutions.Any(v => v.IsNumeric);
        o.Parameters = [.. o.Solutions.SelectMany(v => v.Parameters).Distinct()];
        if (o.Numeric) AddChecks(o.Solutions, eq, x, ct);
        if (r.ShowSteps) o.Steps = Steps(eq, x, ct, r.Complex);
        if (o.Status == EquationStatus.NoRealSolution) o.Discriminant = QuadraticDiscriminant(eq, x, ct);
        DescribeEquation(o, x);
    }

    private void SolveInRange(EquationOutcome o, Expr eq, string x, (Expr Low, Expr High, double A, double B) range, HashSet<string> known, CancellationToken ct)
    {
        o.Numeric = true;
        o.RangeText = $"[{Show(range.Low)}, {Show(range.High)}]";
        Expr result;
        try
        {
            result = Evaluate(CallExpr.Of("nsolve", eq, new SymbolExpr(x), range.Low, range.High), ct);
        }
        catch (SolverException ex) when (ex.Message.StartsWith("No ", StringComparison.Ordinal))
        {
            result = BoolExpr.False;
        }
        var read = SolutionReader.ReadSingle(result, x);
        if (read.Kind == SolutionKind.Unsolved) throw new SolverException("The numeric search could not read this equation");
        o.Solutions.AddRange(read.Values.Select(v => Describe(v, known, ct)));
        o.Status = read.Kind switch
        {
            SolutionKind.All => EquationStatus.AllValues,
            _ when o.Solutions.Count > 0 => EquationStatus.Solutions,
            _ => EquationStatus.NoSolution,
        };
        AddChecks(o.Solutions, eq, x, ct);
        DescribeEquation(o, x);
    }

    /// <summary>Fills in left − right at each numeric solution.</summary>
    private void AddChecks(List<SolutionValue> values, Expr eq, string x, CancellationToken ct)
    {
        var f = Residual(eq);
        foreach (var v in values)
        {
            if (!double.IsFinite(v.Re) || !v.IsReal) continue;
            v.Check = CheckText(ValueAt(f, x, v.Re, ct));
        }
    }

    /// <summary>The discriminant of a quadratic equation in x, or null when the equation is not quadratic.</summary>
    private Expr? QuadraticDiscriminant(Expr eq, string x, CancellationToken ct)
    {
        try
        {
            var f = Residual(eq);
            var xs = new SymbolExpr(x);
            if (Evaluate(CallExpr.Of("degree", f, xs), ct, free: [x]) is not NumberExpr { Value.IsExactInteger: true } d || d.Value.Re != 2) return null;
            var disc = Evaluate(CallExpr.Of("discriminant", f, xs), ct, free: [x]);
            return disc.FreeSymbols().Count == 0 ? disc : null;
        }
        catch (SolverException)
        {
            return null;
        }
    }

    private void DescribeEquation(EquationOutcome o, string x)
    {
        string inRange = o.RangeText is null ? "" : " IN " + o.RangeText;
        switch (o.Status)
        {
            case EquationStatus.Solutions:
                o.Summary = o.Parameters.Count > 0 ? "INFINITELY MANY SOLUTIONS" : SolveOutcome.Count(o.Solutions.Count, "SOLUTION") + inRange;
                break;
            case EquationStatus.NoRealSolution:
                o.Summary = "NO REAL SOLUTION";
                break;
            case EquationStatus.NoSolution:
                o.Summary = "NO SOLUTION" + inRange;
                break;
            case EquationStatus.AllValues:
                o.Summary = $"EVERY {x.ToUpperInvariant()} IS A SOLUTION";
                break;
        }

        if (o.Status == EquationStatus.NoRealSolution)
        {
            if (o.Discriminant is { } disc)
                o.Notes.Add($"The discriminant Δ = b² {MathText.Minus} 4ac = {Show(disc)} is negative, so the parabola never reaches 0: no real {x} solves it.");
            else o.Notes.Add($"No real number {x} makes both sides equal.");
            int n = o.ComplexSolutions.Count;
            if (n > 0) o.Notes.Add($"It has {n} complex solution{(n == 1 ? "" : "s")}: turn on COMPLEX to see {(n == 1 ? "it" : "them")}.");
        }
        else if (o.Status == EquationStatus.NoSolution && o.RangeText is null)
            o.Notes.Add($"No value of {x} makes both sides equal{(o.Request.Complex ? ", real or complex" : "")}.");
        else if (o.Status == EquationStatus.NoSolution)
            o.Notes.Add("Nothing in the range makes both sides equal. Widen the range, or clear it to solve exactly.");

        if (o.Status == EquationStatus.Solutions && o.Numeric)
            o.Notes.Add(o.RangeText is not null
                ? $"All solutions in the range are listed, found by scanning and refining. Each check shows left {MathText.Minus} right ≈ 0."
                : $"No exact method applies, so the solutions were found numerically. Each check shows left {MathText.Minus} right ≈ 0.");
        foreach (var p in o.Parameters) o.Notes.Add($"{p} is any integer: each value of {p} gives another solution.");
        if (o.Status == EquationStatus.Solutions && o.ComplexSolutions.Count > 0)
            o.Notes.Add($"There {(o.ComplexSolutions.Count == 1 ? "is" : "are")} also {o.ComplexSolutions.Count} complex solution{(o.ComplexSolutions.Count == 1 ? "" : "s")}: turn on COMPLEX to list {(o.ComplexSolutions.Count == 1 ? "it" : "them")}.");
        if (o.Request.Complex && o.RangeText is not null) o.Notes.Add("A range searches real numbers only.");
    }

    /// <summary>The sides of the equation as y = left and y = right, so the solutions show where the graphs meet.</summary>
    internal List<string> EquationPlots(Expr eq, string x)
    {
        var free = eq.FreeSymbols();
        if (free.Any(s => s != x && !IsStored(s))) return [];
        if (x != "x" && free.Contains("x")) return [];
        Expr Rename(Expr e) => x == "x" ? e : e.Map(n => n is SymbolExpr s && s.Name == x ? new SymbolExpr("x") : n);
        var (left, right) = Sides(eq);
        var list = new List<string>();
        if (!left.IsZero) list.Add("y = " + MathText.Input(Rename(left)));
        if (!right.IsZero) list.Add("y = " + MathText.Input(Rename(right)));
        return list;
    }

    private SolverCard EquationCard(EquationOutcome o, SolverSnapshot snapshot)
    {
        var x = o.Unknown;
        var card = new SolverCard
        {
            Tool = SolverTool.Equation,
            Title = "EQUATION",
            Input = o.EquationText + (o.RangeText is null ? "" : $",  {x} in {o.RangeText}"),
            Request = snapshot,
            PlotTexts = o.PlotTexts,
        };
        var shown = o.Solutions;
        switch (o.Status)
        {
            case EquationStatus.Solutions:
                card.Lines = ValueLines(x, shown, numbered: false);
                card.Approx = ApproxLine(shown);
                card.CopyText = string.Join(Environment.NewLine, card.Lines);
                card.LatexText = MathText.LatexOr(shown.Select(v => $"{x} = {v.Latex}"))
                    + string.Concat(o.Parameters.Select(p => $@",\quad {p} \in \mathbb{{Z}}"));
                card.UseText = shown.Count == 1 ? shown[0].Input : "{" + string.Join(", ", shown.Select(v => v.Input)) + "}";
                break;
            case EquationStatus.AllValues:
                card.Lines = [$"every {x} is a solution"];
                card.CopyText = card.Lines[0];
                card.LatexText = x + @" \in \mathbb{R}";
                break;
            default:
                card.Lines = [o.Summary.ToLowerInvariant()];
                if (o.ComplexSolutions.Count > 0) card.Lines.Add($"complex: {x} = " + Join([.. o.ComplexSolutions.Select(v => v.Text)]));
                card.CopyText = string.Join(Environment.NewLine, card.Lines);
                card.LatexText = @"\text{" + o.Summary.ToLowerInvariant() + "}";
                break;
        }
        card.Note = o.Parameters.Count > 0 ? string.Join(" ", o.Parameters.Select(p => $"{p} is any integer.")) : o.Numeric && o.Status == EquationStatus.Solutions ? "Found numerically." : null;
        return card;
    }

    /// <summary>
    /// Card lines for values of one unknown: x = v (x ≈ v for numeric roots), x₁ = ... when numbered, and a single
    /// x = (a ± b) line for a pair that differs only in one sign.
    /// </summary>
    internal static List<string> ValueLines(string x, IReadOnlyList<SolutionValue> values, bool numbered)
    {
        if (values.Count == 2 && !values[0].IsNumeric && MathText.PlusMinus(values[0].Text, values[1].Text) is { } pm)
            return [$"{x} = {pm}"];
        var lines = new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            var v = values[i];
            string name = numbered ? MathText.Subscript(x, i + 1) : x;
            string rel = v.IsNumeric ? "≈" : "=";
            string mult = v.Multiplicity switch { 1 => "", 2 => "  (double)", 3 => "  (triple)", var m => $"  (×{m})" };
            lines.Add($"{name} {rel} {v.Text}{mult}");
        }
        return lines;
    }

    /// <summary>The decimal forms of exact values as one line (≈ 0.697, 4.303), or null when none is needed.</summary>
    internal static string? ApproxLine(IReadOnlyList<SolutionValue> values)
    {
        var withApprox = values.Where(v => v.HasApprox && !v.IsNumeric).ToList();
        if (withApprox.Count == 0) return null;
        return "≈ " + Join(values.Select(v => v.HasApprox ? v.ApproxText! : v.Text).ToList());
    }

    /// <summary>Values joined with commas, or as a ± pair when they differ only in one sign.</summary>
    internal static string Join(IReadOnlyList<string> texts) =>
        texts.Count == 2 && MathText.PlusMinus(texts[0], texts[1]) is { } pm ? pm : string.Join(",  ", texts);
}
