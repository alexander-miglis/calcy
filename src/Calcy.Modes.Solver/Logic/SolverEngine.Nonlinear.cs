using Calcy.Core.Expressions;

namespace Calcy.Modes.Solver.Logic;

/// <summary>One solution of a system: a value for each unknown.</summary>
public sealed class SolutionSet
{
    /// <summary>The value of each unknown, in the order of the unknowns.</summary>
    public List<(string Name, SolutionValue Value)> Values { get; } = [];

    /// <summary>The largest |left − right| over the equations at this solution, as text (numeric solutions only).</summary>
    public string? Check { get; set; }

    /// <summary>True when the solution was found from the user's starting guesses.</summary>
    public bool FromGuess { get; set; }

    /// <summary>True when the values are decimal approximations.</summary>
    public bool IsNumeric => Values.Any(v => v.Value.IsNumeric);

    /// <summary>The solution as one line: x = 3, y = 4 (≈ for decimals).</summary>
    public string Line => string.Join(",  ", Values.Select(v => $"{v.Name} {(v.Value.IsNumeric ? "≈" : "=")} {v.Value.Text}"));
}

/// <summary>The result of the NONLINEAR SYSTEM tool.</summary>
public sealed class NonlinearSystemOutcome : SolveOutcome
{
    /// <summary>The inputs that were solved.</summary>
    public required NonlinearSystemRequest Request { get; init; }

    /// <summary>The unknowns in order.</summary>
    public IReadOnlyList<string> Unknowns { get; set; } = [];

    /// <summary>The equations as on-screen text.</summary>
    public List<string> EquationTexts { get; } = [];

    /// <summary>Every solution found.</summary>
    public List<SolutionSet> Sets { get; } = [];

    /// <summary>True when every point solves the system.</summary>
    public bool AllValues { get; set; }

    /// <summary>True when the solutions were found numerically.</summary>
    public bool Numeric { get; set; }

    /// <summary>Free parameters in the solutions (when the system has fewer equations than unknowns).</summary>
    public IReadOnlyList<string> Parameters { get; set; } = [];
}

public sealed partial class SolverEngine
{
    /// <summary>The unknowns of typed equations, most likely first (for the guess boxes).</summary>
    public IReadOnlyList<string> UnknownsOf(IEnumerable<string> lines)
    {
        var names = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                names.AddRange(Session.ParseExpression(MathText.Ascii(line)).FreeSymbols());
            }
            catch (Calcy.Core.CalcException)
            {
                // Half-typed lines add nothing.
            }
        }
        return UnknownPicker.Candidates(names, IsStored);
    }

    /// <summary>
    /// Solves typed equations in several unknowns: every solution solve({equations}, {unknowns}) finds (exactly by
    /// substitution and elimination, or numerically from many starting points), plus the solution nearest the
    /// starting guesses when they are given.
    /// </summary>
    public NonlinearSystemOutcome SolveNonlinearSystem(NonlinearSystemRequest request, CancellationToken ct)
    {
        var r = request.Clone().Normalize();
        var snapshot = new SolverSnapshot { Nonlinear = r.Clone() };
        var o = new NonlinearSystemOutcome { Request = r };
        try
        {
            var eqs = new List<Expr>();
            for (int i = 0; i < r.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(r.Lines[i])) continue;
                var e = Parse(r.Lines[i], $"equation {i + 1}");
                if (e is RelationExpr { Op: not RelOp.Equal } or LogicExpr or ListExpr or BoolExpr)
                    throw new SolverException($"Equation {i + 1} must be one equation, like x^2 + y^2 = 25", $"equation {i + 1}");
                eqs.Add(e is RelationExpr ? e : new RelationExpr(RelOp.Equal, e, Expr.Zero));
                o.EquationTexts.Add(Show(eqs[^1]));
            }
            if (eqs.Count == 0) throw new SolverException("Type at least one equation");
            var unknowns = UnknownPicker.Candidates([.. eqs.SelectMany(e => e.FreeSymbols())], IsStored);
            if (unknowns.Count == 0) throw new SolverException("The equations have no unknowns to solve for");
            o.Unknowns = unknowns;
            var inputSymbols = new HashSet<string>(eqs.SelectMany(e => e.FreeSymbols()));
            var eqList = new ListExpr([.. eqs]);
            var vars = new ListExpr(Symbols(unknowns));

            var guesses = Guesses(r, unknowns, ct);
            SystemSolutions read;
            try
            {
                read = SolutionReader.ReadSystem(Evaluate(CallExpr.Of("solve", eqList, vars), ct), unknowns);
            }
            catch (SolverException)
            {
                read = new SystemSolutions(SolutionKind.Unsolved, []);
            }
            if (read.Kind == SolutionKind.Unsolved && eqs.Count == unknowns.Count)
            {
                try
                {
                    read = SolutionReader.ReadSystem(Evaluate(CallExpr.Of("nsolve", eqList, vars), ct), unknowns);
                    o.Numeric = true;
                }
                catch (SolverException) when (guesses is not null)
                {
                    read = new SystemSolutions(SolutionKind.None, []);
                }
                catch (SolverException ex) when (ex.Message.StartsWith("No ", StringComparison.Ordinal))
                {
                    read = new SystemSolutions(SolutionKind.None, []);
                }
            }
            if (read.Kind == SolutionKind.Unsolved && guesses is null)
                throw new SolverException(eqs.Count == unknowns.Count
                    ? "This system could not be solved; give starting guesses"
                    : $"This system could not be solved: there are {eqs.Count} equations for {unknowns.Count} unknowns ({string.Join(", ", unknowns)})");

            o.AllValues = read.Kind == SolutionKind.All;
            foreach (var set in read.Kind == SolutionKind.Values ? read.Sets : [])
                o.Sets.Add(MakeSet(set, unknowns, inputSymbols, ct));

            if (guesses is not null)
            {
                bool found = false;
                try
                {
                    var near = SolutionReader.ReadSystem(Evaluate(CallExpr.Of("nsolve", eqList, vars, guesses), ct), unknowns);
                    foreach (var set in near.Kind == SolutionKind.Values ? near.Sets : [])
                    {
                        found = true;
                        var s = MakeSet(set, unknowns, inputSymbols, ct);
                        if (o.Sets.Any(existing => SameSet(existing, s))) continue;
                        s.FromGuess = true;
                        o.Sets.Add(s);
                    }
                }
                catch (SolverException)
                {
                    found = false;
                }
                o.Notes.Add(found ? "The solution nearest your starting guesses was refined with Newton's method." : "No solution was found near your starting guesses.");
            }

            o.Numeric |= o.Sets.Any(s => s.IsNumeric);
            foreach (var s in o.Sets.Where(s => s.IsNumeric)) s.Check = SystemCheck(eqs, s, ct);
            o.Parameters = [.. o.Sets.SelectMany(s => s.Values.SelectMany(v => v.Value.Parameters)).Distinct()];
            DescribeNonlinear(o);
            if (StoredNote(inputSymbols, unknowns) is { } stored) o.Notes.Add(stored);
            o.PlotTexts = SystemPlots(eqs, unknowns);
            o.Card = NonlinearCard(o, snapshot);
            return o;
        }
        catch (SolverException ex)
        {
            var input = o.EquationTexts.Count > 0 ? string.Join(Environment.NewLine, o.EquationTexts) : "system";
            return new NonlinearSystemOutcome { Request = r, Error = ex, Card = SolveOutcome.ErrorCard(SolverTool.NonlinearSystem, input, ex, snapshot) };
        }
    }

    /// <summary>The starting guesses as a list for nsolve, null when none are given. Some but not all is an error.</summary>
    private ListExpr? Guesses(NonlinearSystemRequest r, IReadOnlyList<string> unknowns, CancellationToken ct)
    {
        var texts = unknowns.Select(u => r.Guesses.TryGetValue(u, out var g) ? g : "").ToList();
        if (texts.All(string.IsNullOrWhiteSpace)) return null;
        var missing = unknowns.Where((u, i) => string.IsNullOrWhiteSpace(texts[i])).ToList();
        if (missing.Count > 0)
            throw new SolverException($"Give a starting guess for every unknown (missing: {string.Join(", ", missing)}), or none", "starting guesses");
        var values = new List<Expr>();
        for (int i = 0; i < unknowns.Count; i++)
        {
            var field = $"starting guess for {unknowns[i]}";
            var v = Evaluate(Parse(texts[i], field), ct);
            if (!double.IsFinite(Real(v, ct))) throw new SolverException($"The {field} must be a real number", field);
            values.Add(v);
        }
        return new ListExpr([.. values]);
    }

    private SolutionSet MakeSet(IReadOnlyDictionary<string, Expr> set, IReadOnlyList<string> unknowns, IReadOnlyCollection<string> known, CancellationToken ct)
    {
        var s = new SolutionSet();
        foreach (var u in unknowns)
            s.Values.Add((u, Describe(set.TryGetValue(u, out var v) ? v : new SymbolExpr(u), known, ct)));
        return s;
    }

    private static bool SameSet(SolutionSet a, SolutionSet b)
    {
        for (int i = 0; i < a.Values.Count && i < b.Values.Count; i++)
        {
            double x = a.Values[i].Value.Re, y = b.Values[i].Value.Re;
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x - y) > 1e-6 * Math.Max(1, Math.Abs(x))) return false;
        }
        return true;
    }

    private string SystemCheck(IReadOnlyList<Expr> eqs, SolutionSet s, CancellationToken ct)
    {
        var at = s.Values.Where(v => double.IsFinite(v.Value.Re)).ToDictionary(v => v.Name, v => v.Value.Re);
        double worst = 0;
        foreach (var e in eqs)
        {
            double v = ValueAt(Residual(e), at, ct);
            if (double.IsNaN(v)) return "?";
            worst = Math.Max(worst, Math.Abs(v));
        }
        return CheckText(worst);
    }

    private static void DescribeNonlinear(NonlinearSystemOutcome o)
    {
        if (o.AllValues)
        {
            o.Summary = "EVERY POINT IS A SOLUTION";
            o.Notes.Insert(0, "The equations hold for every value of the unknowns.");
            return;
        }
        if (o.Sets.Count == 0)
        {
            o.Summary = "NO REAL SOLUTION";
            o.Notes.Insert(0, o.Numeric
                ? "The numeric search between −10 and 10 found nothing. Try starting guesses near where you expect a solution."
                : "No real values satisfy all the equations at once.");
            return;
        }
        o.Summary = o.Parameters.Count > 0 ? "INFINITELY MANY SOLUTIONS" : SolveOutcome.Count(o.Sets.Count, "SOLUTION");
        o.Notes.Insert(0, o.Numeric
            ? $"Found numerically from many starting points. Each check shows the largest left {MathText.Minus} right ≈ 0."
            : "Solved exactly by substitution and elimination.");
        if (o.Parameters.Count > 0) o.Notes.Add($"{string.Join(", ", o.Parameters)} can be any real number.");
        o.Notes.Add("Only real solutions are listed.");
    }

    private SolverCard NonlinearCard(NonlinearSystemOutcome o, SolverSnapshot snapshot)
    {
        var card = new SolverCard
        {
            Tool = SolverTool.NonlinearSystem,
            Title = "NONLINEAR SYSTEM",
            Input = string.Join(Environment.NewLine, o.EquationTexts),
            Request = snapshot,
            PlotTexts = o.PlotTexts,
        };
        if (o.Sets.Count == 0)
        {
            card.Lines = [o.AllValues ? "every point is a solution" : "no real solution"];
            card.CopyText = card.Lines[0];
            card.LatexText = @"\text{" + card.Lines[0] + "}";
            card.Note = o.AllValues ? null : o.Notes.FirstOrDefault();
            return card;
        }
        card.Lines = [.. o.Sets.Select(s => s.Line)];
        card.CopyText = string.Join(Environment.NewLine, card.Lines);
        card.LatexText = MathText.LatexOr(o.Sets.Select(s => MathText.LatexAnd(s.Values.Select(v => $"{v.Name} = {v.Value.Latex}"))));
        string One(SolutionSet s) => "{" + string.Join(", ", s.Values.Select(v => v.Value.Input)) + "}";
        card.UseText = o.Sets.Count == 1 ? One(o.Sets[0]) : "{" + string.Join(", ", o.Sets.Select(One)) + "}";
        card.Note = o.Numeric ? "Found numerically." : null;
        return card;
    }
}
