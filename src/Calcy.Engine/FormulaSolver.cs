using Calcy.Core;
using Calcy.Core.Compilation;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Parsing;
using Calcy.Formulas;
using Calcy.Numerics;

namespace Calcy.Engine;

/// <summary>One solution of a formula for its unknown.</summary>
public sealed record FormulaSolution(Expr Value, double Approx, bool Exact);

/// <summary>The outcome of solving a formula: the solutions that respect the variable's limits, and notes.</summary>
public sealed record FormulaSolveResult(string Variable, string Unit, IReadOnlyList<FormulaSolution> Solutions, IReadOnlyList<string> Notes);

/// <summary>
/// Solves any formula of the collection for any of its variables. Known values and the formula's constants are
/// substituted, then the equation is solved exactly when the algebra module can, otherwise numerically over a wide
/// logarithmic grid (physics needs values from 1e-40 to 1e40), and solutions outside the variable's limits are dropped.
/// </summary>
public static class FormulaSolver
{
    /// <summary>Parses one side of a formula, treating every variable and constant symbol as a single name.</summary>
    public static Expr Parse(CalcSession session, Formula formula, string text, FormulaLibrary library)
    {
        var names = formula.Variables.Select(v => v.Symbol)
            .Concat(library.ConstantsFor(formula).Select(c => c.Symbol))
            .Distinct()
            .ToArray();
        return Parser.ParseExpression(text, session.ContextWith(names));
    }

    /// <summary>
    /// Solves the formula for <paramref name="unknown"/> given values for the other variables (expressions or numbers).
    /// Missing values fall back to the variable's default (for example g = 9.81).
    /// </summary>
    public static FormulaSolveResult Solve(CalcSession session, Formula formula, IReadOnlyDictionary<string, Expr> known, string unknown,
        FormulaLibrary? library = null, CancellationToken ct = default)
    {
        library ??= FormulaLibrary.Default;
        if (formula.IsIdentity) throw new CalcException($"'{formula.Name}' is an identity: it holds for every value, so there is nothing to solve");
        var target = formula.GetVariable(unknown) ?? throw new CalcException($"'{formula.Name}' has no variable called {unknown}");
        var notes = new List<string>();
        var locals = new Dictionary<string, Expr>(StringComparer.Ordinal);
        foreach (var c in library.ConstantsFor(formula)) locals[c.Symbol] = new NumberExpr(Number.Approx(c.Value));
        foreach (var v in formula.Variables)
        {
            if (v.Symbol == unknown) continue;
            if (known.TryGetValue(v.Symbol, out var value)) locals[v.Symbol] = value;
            else if (v.DefaultValue is double d)
            {
                locals[v.Symbol] = new NumberExpr(Number.Approx(d));
                notes.Add($"Used {v.Symbol} = {d.ToString(System.Globalization.CultureInfo.InvariantCulture)} {v.Unit}".TrimEnd() + " (the usual value).");
            }
            else throw new CalcException($"Give a value for {v.Symbol} ({v.Name})");
        }

        var left = Parse(session, formula, formula.LeftSide, library);
        var right = Parse(session, formula, formula.RightSide, library);
        var ctx = session.Context(false, ct).WithLocals(locals).WithFree(unknown);
        var residual = Evaluator.Eval(new AddExpr([left, new MulExpr([Expr.Int(-1), right])]), ctx);

        var solutions = new List<FormulaSolution>();
        // Values tried from a general solution (θ = 2πn + ...): dropping those for being out of range is expected, not news.
        var tried = new List<FormulaSolution>();
        // Exact first, when the algebra module is present and the inputs are exact.
        if (session.Functions.Contains("solve") && !ContainsDecimal(residual))
        {
            try
            {
                var exact = Evaluator.Eval(CallExpr.Of("solve", new RelationExpr(RelOp.Equal, residual, Expr.Int(0)), new SymbolExpr(unknown)), ctx);
                // The known values are already in the residual, so solutions are read without the formula's locals:
                // the algebra module's general-solution counter (θ = 2πn + ...) must not pick up a formula variable n.
                var plain = session.Context(false, ct);
                foreach (var value in Values(exact, unknown))
                {
                    var counters = value.FreeSymbols();
                    if (counters.Count == 0)
                    {
                        var approx = Evaluator.Numeric(value, plain.WithApproximate());
                        if (approx is { IsReal: true } a) AddSolution(solutions, value, a.Re);
                        continue;
                    }
                    // A general solution: take the principal value and its neighbours, the limits pick the valid ones.
                    foreach (int k in (int[])[0, 1, -1, 2, -2])
                    {
                        var at = Evaluator.Eval(value, plain.WithLocals(counters.Select(n => new KeyValuePair<string, Expr>(n, Expr.Int(k)))));
                        var approx = Evaluator.Numeric(at, plain.WithApproximate());
                        if (approx is { IsReal: true } a) AddSolution(tried, at, a.Re);
                    }
                }
            }
            catch (CalcException)
            {
                // Fall through to the numeric search.
            }
        }
        if (solutions.Count == 0 && tried.Count == 0)
        {
            var f = ExprCompiler.Compile1(residual, unknown, session.Context(false, ct));
            foreach (var root in NumericRoots(f, ct))
                solutions.Add(new FormulaSolution(new NumberExpr(Number.Approx(root)), root, false));
        }

        int before = solutions.Count;
        solutions = [.. solutions.Where(s => target.Accepts(s.Approx))];
        foreach (var t in tried.Where(s => target.Accepts(s.Approx))) AddSolution(solutions, t.Value, t.Approx);
        solutions = [.. solutions.OrderBy(s => Math.Abs(s.Approx))];
        int keptPlain = solutions.Count(s => !tried.Any(t => t.Approx == s.Approx));
        if (keptPlain < before)
            notes.Add($"Left out {before - keptPlain} solution{(before - keptPlain == 1 ? "" : "s")} outside the allowed range of {unknown}.");
        if (solutions.Count == 0) throw new CalcException($"No valid value of {unknown} fits these numbers");
        return new FormulaSolveResult(unknown, target.Unit, solutions, notes);
    }

    /// <summary>Reads solutions from a solve result: {x = 2, x = -2}, {2, -2}, x = 2 or a number.</summary>
    private static IEnumerable<Expr> Values(Expr e, string unknown) => e switch
    {
        ListExpr l => l.Items.SelectMany(i => Values(i, unknown)),
        LogicExpr { Op: LogicOp.Or } or => or.Operands.SelectMany(i => Values(i, unknown)),
        RelationExpr { Op: RelOp.Equal, Left: SymbolExpr s } r when s.Name == unknown => [r.Right],
        BoolExpr => [],
        _ when e.IsConstantValue() => [e],
        _ => [],
    };

    private static bool ContainsDecimal(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsExact,
        _ => e.Children.Any(ContainsDecimal),
    };

    /// <summary>Sign changes on a two-sided logarithmic grid from 1e-40 to 1e40, refined with Brent's method.</summary>
    public static IReadOnlyList<double> NumericRoots(Func<double, double> f, CancellationToken ct = default)
    {
        var grid = new List<double>();
        const int perDecade = 24;
        for (int k = 40 * perDecade; k >= -40 * perDecade; k--) grid.Add(-Math.Pow(10, k / (double)perDecade));
        grid.Add(0);
        for (int k = -40 * perDecade; k <= 40 * perDecade; k++) grid.Add(Math.Pow(10, k / (double)perDecade));

        var roots = new List<double>();
        double prevX = grid[0], prevY = f(prevX);
        for (int i = 1; i < grid.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            double x = grid[i], y = f(x);
            if (y == 0) AddRoot(roots, x);
            else if (double.IsFinite(prevY) && double.IsFinite(y) && prevY != 0 && Math.Sign(prevY) != Math.Sign(y))
            {
                // Relative tolerance: atomic physics lives around 1e-19, so an absolute one would stop at once.
                double tol = Math.Max(Math.Abs(prevX), Math.Abs(x)) * 1e-15;
                var r = RootFinding.Brent(f, prevX, x, tolerance: Math.Max(tol, double.Epsilon), cancellationToken: ct);
                if (r.Converged)
                {
                    // A pole also changes sign; a real root makes f small compared with its neighbours.
                    double scale = Math.Max(Math.Abs(prevY), Math.Abs(y));
                    if (r.FunctionValue == 0 || Math.Abs(r.FunctionValue) <= 1e-6 * scale) AddRoot(roots, r.Root);
                }
            }
            prevX = x;
            prevY = y;
        }
        return roots;
    }

    private static void AddSolution(List<FormulaSolution> solutions, Expr value, double approx)
    {
        if (!solutions.Any(s => Math.Abs(s.Approx - approx) <= 1e-12 * Math.Max(1, Math.Abs(approx))))
            solutions.Add(new FormulaSolution(value, approx, true));
    }

    private static void AddRoot(List<double> roots, double x)
    {
        if (!roots.Any(r => Math.Abs(r - x) <= 1e-12 * Math.Max(Math.Abs(r), Math.Abs(x)))) roots.Add(x);
    }
}
