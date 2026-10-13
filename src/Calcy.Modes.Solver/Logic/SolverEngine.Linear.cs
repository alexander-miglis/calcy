using System.Collections.Immutable;
using Calcy.Core.Expressions;

namespace Calcy.Modes.Solver.Logic;

/// <summary>How a linear system came out.</summary>
public enum LinearKind
{
    /// <summary>Exactly one solution.</summary>
    Unique,

    /// <summary>No solution (the equations contradict each other).</summary>
    None,

    /// <summary>Infinitely many solutions, given with free parameters.</summary>
    Infinite,
}

/// <summary>The result of the LINEAR SYSTEM tool.</summary>
public sealed class LinearSystemOutcome : SolveOutcome
{
    /// <summary>The inputs that were solved.</summary>
    public required LinearSystemRequest Request { get; init; }

    /// <summary>The unknowns in order.</summary>
    public IReadOnlyList<string> Unknowns { get; set; } = [];

    /// <summary>The equations as on-screen text.</summary>
    public List<string> EquationTexts { get; } = [];

    /// <summary>Unique, none or infinitely many.</summary>
    public LinearKind Kind { get; set; }

    /// <summary>The value of each unknown (in terms of the parameters when there are infinitely many solutions).</summary>
    public List<(string Name, SolutionValue Value)> Values { get; } = [];

    /// <summary>The free parameters of an infinite solution set (t, t1, t2 ...).</summary>
    public IReadOnlyList<string> Parameters { get; set; } = [];

    /// <summary>The determinant of the coefficients (square systems with number coefficients).</summary>
    public Expr? Determinant { get; set; }

    /// <summary>The rank of the coefficient matrix, when it could be found.</summary>
    public int? Rank { get; set; }

    /// <summary>The rank of the augmented matrix [A | b], when it could be found.</summary>
    public int? AugmentedRank { get; set; }
}

public sealed partial class SolverEngine
{
    /// <summary>
    /// Solves simultaneous linear equations from the coefficient grid or typed equations with
    /// solve({equations}, {unknowns}): one solution, none, or infinitely many in parametric form, all exact.
    /// </summary>
    public LinearSystemOutcome SolveLinearSystem(LinearSystemRequest request, CancellationToken ct)
    {
        var r = request.Clone().Normalize();
        var snapshot = new SolverSnapshot { Linear = r.Clone() };
        var o = new LinearSystemOutcome { Request = r };
        try
        {
            var (eqs, unknowns, a, b) = r.Typed ? TypedLinear(r, o, ct) : GridLinear(r, o, ct);
            o.Unknowns = unknowns;
            var inputSymbols = new HashSet<string>(eqs.SelectMany(e => e.FreeSymbols()));
            var call = CallExpr.Of("solve", new ListExpr([.. eqs]), new ListExpr(Symbols(unknowns)));
            var read = SolutionReader.ReadSystem(Evaluate(call, ct), unknowns);
            Ranks(o, a, b, ct);
            switch (read.Kind)
            {
                case SolutionKind.None:
                    o.Kind = LinearKind.None;
                    break;
                case SolutionKind.All:
                    o.Kind = LinearKind.Infinite;
                    o.Parameters = unknowns;
                    break;
                case SolutionKind.Values when read.Sets.Count == 1:
                    var set = read.Sets[0];
                    foreach (var u in unknowns)
                    {
                        var value = set.TryGetValue(u, out var v) ? v : new SymbolExpr(u);
                        o.Values.Add((u, Describe(value, inputSymbols, ct)));
                    }
                    o.Parameters = [.. o.Values.SelectMany(v => v.Value.Parameters).Distinct()];
                    o.Kind = o.Parameters.Count > 0 ? LinearKind.Infinite : LinearKind.Unique;
                    break;
                case SolutionKind.Values:
                    throw new SolverException("This system has several separate solutions, so it is not linear: use the NONLINEAR SYSTEM tab");
                default:
                    throw new SolverException("This system could not be solved");
            }
            DescribeLinear(o);
            if (StoredNote(inputSymbols, unknowns) is { } stored) o.Notes.Add(stored);
            o.PlotTexts = SystemPlots(eqs, unknowns);
            o.Card = LinearCard(o, snapshot);
            return o;
        }
        catch (SolverException ex)
        {
            // The system as typed, since it could not be read.
            var lines = (r.Typed ? r.Lines : GridToLines(r)).Take(r.Equations).Where(l => !string.IsNullOrWhiteSpace(l));
            var input = MathText.Typographic(string.Join(Environment.NewLine, lines));
            return new LinearSystemOutcome { Request = r, Error = ex, Card = SolveOutcome.ErrorCard(SolverTool.LinearSystem, input, ex, snapshot) };
        }
    }

    private (List<Expr> Eqs, IReadOnlyList<string> Unknowns, Expr[,]? A, Expr[]? B) GridLinear(LinearSystemRequest r, LinearSystemOutcome o, CancellationToken ct)
    {
        var names = LinearSystemRequest.GridNames(r.Unknowns);
        int m = r.Equations, n = r.Unknowns;
        var a = new Expr[m, n];
        var b = new Expr[m];
        var eqs = new List<Expr>();
        for (int i = 0; i < m; i++)
        {
            var terms = new List<Expr>();
            for (int j = 0; j < n; j++)
            {
                a[i, j] = Coefficient(r.Cell(i, j), $"coefficient of {names[j]} in equation {i + 1}", ct);
                terms.Add(Algebra.Mul(a[i, j], new SymbolExpr(names[j])));
            }
            b[i] = Coefficient(r.Rhs(i), $"right side of equation {i + 1}", ct);
            var left = Evaluate(Algebra.Add(terms), ct, free: names);
            eqs.Add(new RelationExpr(RelOp.Equal, left, b[i]));
            o.EquationTexts.Add($"{Show(left)} = {Show(b[i])}");
        }
        return (eqs, names, a, b);
    }

    private (List<Expr> Eqs, IReadOnlyList<string> Unknowns, Expr[,]? A, Expr[]? B) TypedLinear(LinearSystemRequest r, LinearSystemOutcome o, CancellationToken ct)
    {
        var eqs = new List<Expr>();
        var labels = new List<int>();
        for (int i = 0; i < r.Equations; i++)
        {
            if (string.IsNullOrWhiteSpace(r.Lines[i])) continue;
            var e = Parse(r.Lines[i], $"equation {i + 1}");
            if (e is RelationExpr { Op: not RelOp.Equal } or LogicExpr or ListExpr or BoolExpr)
                throw new SolverException($"Equation {i + 1} must be one equation, like 2x + y = 5", $"equation {i + 1}");
            eqs.Add(e is RelationExpr ? e : new RelationExpr(RelOp.Equal, e, Expr.Zero));
            labels.Add(i + 1);
            o.EquationTexts.Add(Show(eqs[^1]));
        }
        if (eqs.Count == 0) throw new SolverException("Type at least one equation");
        var unknowns = UnknownPicker.Candidates([.. eqs.SelectMany(e => e.FreeSymbols())], IsStored);
        if (unknowns.Count == 0) throw new SolverException("The equations have no unknowns to solve for");

        // Coefficients by differentiation: linear exactly when no derivative still contains an unknown.
        var a = new Expr[eqs.Count, unknowns.Count];
        var b = new Expr[eqs.Count];
        bool numeric = true;
        for (int i = 0; i < eqs.Count; i++)
        {
            var f = Residual(eqs[i]);
            for (int j = 0; j < unknowns.Count; j++)
            {
                var d = Evaluate(CallExpr.Of("diff", f, new SymbolExpr(unknowns[j])), ct, free: unknowns);
                var bad = d.FreeSymbols().FirstOrDefault(unknowns.Contains);
                if (bad is not null)
                    throw new SolverException($"Equation {labels[i]} is not linear in {unknowns[j]} (it multiplies or powers the unknowns): use the NONLINEAR SYSTEM tab", $"equation {labels[i]}");
                a[i, j] = d;
                numeric &= d.FreeSymbols().Count == 0;
            }
            var atZero = f.Map(e => e is SymbolExpr s && unknowns.Contains(s.Name) ? Expr.Zero : e);
            b[i] = Evaluate(Algebra.Neg(atZero), ct);
            numeric &= b[i].FreeSymbols().Count == 0;
        }
        return (eqs, unknowns, numeric ? a : null, numeric ? b : null);
    }

    /// <summary>Ranks of A and [A | b] and the determinant of a square A, for the explanation.</summary>
    private void Ranks(LinearSystemOutcome o, Expr[,]? a, Expr[]? b, CancellationToken ct)
    {
        if (a is null || b is null) return;
        int m = a.GetLength(0), n = a.GetLength(1);
        try
        {
            var rows = new List<IReadOnlyList<Expr>>();
            var aug = new List<IReadOnlyList<Expr>>();
            for (int i = 0; i < m; i++)
            {
                var row = new Expr[n];
                for (int j = 0; j < n; j++) row[j] = a[i, j];
                rows.Add(row);
                aug.Add([.. row, b[i]]);
            }
            var am = MatrixExpr.FromRows(rows);
            if (Evaluate(CallExpr.Of("rank", am), ct) is NumberExpr { Value.IsExactInteger: true } ra) o.Rank = (int)ra.Value.Re;
            if (Evaluate(CallExpr.Of("rank", MatrixExpr.FromRows(aug)), ct) is NumberExpr { Value.IsExactInteger: true } rb) o.AugmentedRank = (int)rb.Value.Re;
            if (m == n) o.Determinant = Evaluate(CallExpr.Of("det", am), ct);
        }
        catch (SolverException)
        {
            // The explanation is optional.
        }
    }

    private void DescribeLinear(LinearSystemOutcome o)
    {
        int n = o.Unknowns.Count;
        switch (o.Kind)
        {
            case LinearKind.Unique:
                o.Summary = "UNIQUE SOLUTION";
                if (o.Determinant is { } det && !det.IsZero) o.Notes.Add($"det A = {Show(det)} ≠ 0, so there is exactly one solution.");
                else if (o.Rank is { } rk) o.Notes.Add($"rank A = {rk} = the number of unknowns, so there is exactly one solution.");
                break;
            case LinearKind.None:
                o.Summary = "NO SOLUTION";
                if (o.Rank is { } r1 && o.AugmentedRank is { } r2)
                    o.Notes.Add($"rank A = {r1} but rank [A | b] = {r2}: the equations contradict each other.");
                else o.Notes.Add("The equations contradict each other: no values satisfy all of them.");
                break;
            case LinearKind.Infinite:
                o.Summary = "INFINITELY MANY SOLUTIONS";
                int free = o.Parameters.Count;
                if (o.Rank is { } r3) o.Notes.Add($"rank A = {r3} < {n} unknowns: {free} free parameter{(free == 1 ? "" : "s")}.");
                if (o.Values.Count > 0)
                    o.Notes.Add($"{string.Join(", ", o.Parameters)} can be any real number{(free == 1 ? "" : "s")}: each choice gives a solution.");
                else o.Notes.Add("Every choice of the unknowns is a solution.");
                break;
        }
    }

    /// <summary>For systems in two unknowns: the equations as curves in the plane (unknowns renamed to x and y).</summary>
    internal List<string> SystemPlots(IReadOnlyList<Expr> eqs, IReadOnlyList<string> unknowns)
    {
        if (unknowns.Count != 2) return [];
        var all = eqs.SelectMany(e => e.FreeSymbols()).Distinct().ToList();
        if (all.Any(s => !unknowns.Contains(s) && !IsStored(s))) return [];
        var map = new Dictionary<string, string> { [unknowns[0]] = "x", [unknowns[1]] = "y" };
        if (all.Any(s => !unknowns.Contains(s) && s is "x" or "y")) return [];
        Expr Rename(Expr e) => e.Map(n => n is SymbolExpr s && map.TryGetValue(s.Name, out var to) ? new SymbolExpr(to) : n);
        return [.. eqs.Select(e => MathText.Input(Rename(e)))];
    }

    private SolverCard LinearCard(LinearSystemOutcome o, SolverSnapshot snapshot)
    {
        int m = o.EquationTexts.Count, n = o.Unknowns.Count;
        var card = new SolverCard
        {
            Tool = SolverTool.LinearSystem,
            Title = $"SYSTEM {m} × {n}",
            Input = string.Join(Environment.NewLine, o.EquationTexts),
            Request = snapshot,
            PlotTexts = o.PlotTexts,
        };
        switch (o.Kind)
        {
            case LinearKind.None:
                card.Lines = ["no solution"];
                card.CopyText = "no solution";
                card.LatexText = @"\text{no solution}";
                card.Note = o.Notes.FirstOrDefault();
                break;
            case LinearKind.Infinite when o.Values.Count == 0:
                card.Lines = ["every choice is a solution"];
                card.CopyText = card.Lines[0];
                card.LatexText = @"\text{every choice is a solution}";
                break;
            default:
                var line = string.Join(",  ", o.Values.Select(v => $"{v.Name} = {v.Value.Text}"));
                card.Lines = [line];
                var approx = o.Values.Where(v => v.Value.HasApprox).ToList();
                if (approx.Count > 0) card.Approx = "≈ " + string.Join(",  ", o.Values.Select(v => $"{v.Name} = {(v.Value.HasApprox ? v.Value.ApproxText : v.Value.Text)}"));
                card.CopyText = line;
                card.LatexText = MathText.LatexAnd(o.Values.Select(v => $"{v.Name} = {v.Value.Latex}"));
                card.UseText = "{" + string.Join(", ", o.Values.Select(v => v.Value.Input)) + "}";
                if (o.Kind == LinearKind.Infinite) card.Note = $"{string.Join(", ", o.Parameters)} can be any real number.";
                break;
        }
        return card;
    }

    /// <summary>
    /// Writes the grid as typed equations (2x + y − z = 8), so switching to typed mode keeps the system.
    /// </summary>
    public static List<string> GridToLines(LinearSystemRequest r)
    {
        var names = LinearSystemRequest.GridNames(r.Unknowns);
        var lines = new List<string>();
        for (int i = 0; i < LinearSystemRequest.Max; i++)
        {
            if (i >= r.Equations)
            {
                lines.Add("");
                continue;
            }
            var terms = new List<string>();
            for (int j = 0; j < names.Count; j++)
            {
                var c = MathText.Ascii(r.Cell(i, j).Trim());
                if (c.Length == 0 || c == "0") continue;
                string term = c switch
                {
                    "1" or "+1" => names[j],
                    "-1" => "-" + names[j],
                    _ when IsPlainNumber(c) => c + names[j],
                    _ => $"({c}){names[j]}",
                };
                terms.Add(term);
            }
            var left = terms.Count == 0 ? "0" : string.Join(" + ", terms).Replace("+ -", "- ", StringComparison.Ordinal);
            var right = MathText.Ascii(r.Rhs(i).Trim());
            lines.Add($"{left} = {(right.Length == 0 ? "0" : right)}");
        }
        return lines;
    }

    private static bool IsPlainNumber(string s) =>
        s.Length > 0 && s.TrimStart('-').All(ch => char.IsDigit(ch) || ch == '.') && s.TrimStart('-').Length > 0;

    /// <summary>
    /// Reads typed linear equations into a coefficient grid, so switching to grid mode keeps the system. Null when
    /// they are not linear with number coefficients, or have more than six unknowns or equations.
    /// </summary>
    public LinearSystemRequest? LinesToGrid(LinearSystemRequest r, CancellationToken ct = default)
    {
        try
        {
            var eqs = new List<Expr>();
            for (int i = 0; i < r.Equations; i++)
            {
                if (string.IsNullOrWhiteSpace(r.Lines[i])) continue;
                var e = Parse(r.Lines[i], $"equation {i + 1}");
                if (e is RelationExpr { Op: not RelOp.Equal } or LogicExpr or ListExpr or BoolExpr) return null;
                eqs.Add(e);
            }
            if (eqs.Count == 0) return null;
            var unknowns = UnknownPicker.Candidates([.. eqs.SelectMany(e => e.FreeSymbols())], IsStored);
            if (unknowns.Count > LinearSystemRequest.Max) return null;
            var grid = r.Clone();
            grid.Unknowns = Math.Max(2, unknowns.Count);
            grid.Equations = eqs.Count;
            grid.Typed = false;
            grid.Grid = [.. Enumerable.Range(0, LinearSystemRequest.Max).Select(_ => Enumerable.Repeat("", LinearSystemRequest.Max + 1).ToList())];
            for (int i = 0; i < eqs.Count; i++)
            {
                var f = Residual(eqs[i]);
                for (int j = 0; j < unknowns.Count; j++)
                {
                    var d = Evaluate(CallExpr.Of("diff", f, new SymbolExpr(unknowns[j])), ct, free: unknowns);
                    if (d.FreeSymbols().Count > 0) return null;
                    grid.Grid[i][j] = MathText.Input(d);
                }
                for (int j = unknowns.Count; j < grid.Unknowns; j++) grid.Grid[i][j] = "0";
                var atZero = f.Map(e => e is SymbolExpr s && unknowns.Contains(s.Name) ? Expr.Zero : e);
                var rhs = Evaluate(Algebra.Neg(atZero), ct);
                if (rhs.FreeSymbols().Count > 0) return null;
                grid.Grid[i][LinearSystemRequest.Max] = MathText.Input(rhs);
            }
            return grid;
        }
        catch (SolverException)
        {
            return null;
        }
    }

    /// <summary>Names as symbols, for the list of unknowns in solve and nsolve calls.</summary>
    internal static ImmutableArray<Expr> Symbols(IEnumerable<string> names) => [.. names.Select(n => (Expr)new SymbolExpr(n))];
}
