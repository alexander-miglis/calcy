using System.Collections.Immutable;
using System.Globalization;
using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>How many solutions a linear system has.</summary>
public enum SolutionKind
{
    /// <summary>Exactly one solution.</summary>
    Unique,

    /// <summary>No solution (the equations contradict each other).</summary>
    None,

    /// <summary>Infinitely many solutions, described with free parameters.</summary>
    Infinite,
}

/// <summary>
/// The solution of A·x = b. For <see cref="SolutionKind.Infinite"/> every solution is
/// <see cref="Particular"/> + Σ parameterₖ · <see cref="Directions"/>[k].
/// </summary>
public sealed record LinearSolution
{
    /// <summary>The kind of solution set.</summary>
    public required SolutionKind Kind { get; init; }

    /// <summary>Gauss-Jordan elimination of the augmented matrix [A | b], with its steps.</summary>
    public required EliminationResult Elimination { get; init; }

    /// <summary>The unique solution (n × k), or one particular solution (n × 1) when there are infinitely many.</summary>
    public MatrixExpr? Particular { get; init; }

    /// <summary>One direction per free variable (each has n entries).</summary>
    public IReadOnlyList<Expr[]> Directions { get; init; } = [];

    /// <summary>Zero-based indices of the free variables, in the same order as <see cref="Directions"/>.</summary>
    public IReadOnlyList<int> FreeVariables { get; init; } = [];

    /// <summary>The parameter names used for the free variables (t, or t₁, t₂, ...).</summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];

    /// <summary>The variable names (x, y, z for up to three unknowns, otherwise x₁ ... xₙ).</summary>
    public required IReadOnlyList<string> Variables { get; init; }

    /// <summary>For <see cref="SolutionKind.None"/>: the zero-based row of the reduced matrix that reads 0 = c.</summary>
    public int ContradictionRow { get; init; } = -1;

    /// <summary>For each unknown, its value written with the parameters (for example 2 - t).</summary>
    public IReadOnlyList<Expr> Components { get; init; } = [];
}

/// <summary>Solves A·x = b exactly by Gauss-Jordan elimination and classifies the solution set.</summary>
public static class LinearSystem
{
    /// <summary>Solves A·x = b. b needs as many rows as A; several columns are allowed when the solution is unique.</summary>
    /// <exception cref="CalcException">The sizes do not match, or b has several columns and the system is not uniquely solvable.</exception>
    public static LinearSolution Solve(MatrixExpr a, MatrixExpr b, CancellationToken ct = default)
    {
        if (a.Rows != b.Rows)
            throw new CalcException($"A·x = b needs b to have {a.Rows} rows (one per equation), but it has {b.Rows}");
        int m = a.Rows, n = a.Cols, k = b.Cols;
        var items = ImmutableArray.CreateBuilder<Expr>(m * (n + k));
        for (int row = 0; row < m; row++)
        {
            for (int c = 0; c < n; c++) items.Add(a[row, c]);
            for (int c = 0; c < k; c++) items.Add(b[row, c]);
        }
        var aug = new MatrixExpr(m, n + k, items.MoveToImmutable());
        var rref = RowReduction.Rref(aug, ct);
        var variables = VariableNames(n);

        int badPivot = rref.PivotColumns.ToList().FindIndex(p => p >= n);
        if (badPivot >= 0)
            return new LinearSolution { Kind = SolutionKind.None, Elimination = rref, Variables = variables, ContradictionRow = badPivot };

        var pivots = rref.PivotColumns.ToList();
        var r = rref.Result;
        if (pivots.Count == n)
        {
            var x = ImmutableArray.CreateBuilder<Expr>(n * k);
            for (int i = 0; i < n; i++)
                for (int c = 0; c < k; c++) x.Add(r[i, n + c]);
            var unique = new MatrixExpr(n, k, x.MoveToImmutable());
            return new LinearSolution
            {
                Kind = SolutionKind.Unique,
                Elimination = rref,
                Particular = unique,
                Variables = variables,
                Components = k == 1 ? [.. unique.Items] : [],
            };
        }
        if (k != 1)
            throw new CalcException("This system has infinitely many solutions; use a single column for b to see them in parametric form");

        var free = Enumerable.Range(0, n).Where(c => !pivots.Contains(c)).ToList();
        var particular = new Expr[n];
        for (int i = 0; i < n; i++) particular[i] = Expr.Zero;
        for (int i = 0; i < pivots.Count; i++) particular[pivots[i]] = r[i, n];
        var directions = new List<Expr[]>();
        foreach (int f in free)
        {
            var v = new Expr[n];
            for (int i = 0; i < n; i++) v[i] = Expr.Zero;
            v[f] = Expr.One;
            for (int i = 0; i < pivots.Count; i++) v[pivots[i]] = Algebra.Neg(r[i, f]);
            directions.Add(v);
        }
        var parameters = ParameterNames(free.Count);
        var components = new Expr[n];
        for (int i = 0; i < n; i++)
        {
            var terms = new List<Expr> { particular[i] };
            for (int j = 0; j < directions.Count; j++) terms.Add(Algebra.Mul(directions[j][i], new SymbolExpr(parameters[j])));
            components[i] = Algebra.Add(terms);
        }
        return new LinearSolution
        {
            Kind = SolutionKind.Infinite,
            Elimination = rref,
            Particular = new MatrixExpr(n, 1, [.. particular]),
            Directions = directions,
            FreeVariables = free,
            Parameters = parameters,
            Variables = variables,
            Components = components,
        };
    }

    /// <summary>x, y, z for up to three unknowns, otherwise x₁ ... xₙ.</summary>
    public static IReadOnlyList<string> VariableNames(int n)
    {
        if (n <= 3) return new[] { "x", "y", "z" }[..n];
        return [.. Enumerable.Range(1, n).Select(i => "x" + Subscript(i))];
    }

    /// <summary>t for one free variable, otherwise t₁, t₂, ...</summary>
    public static IReadOnlyList<string> ParameterNames(int count)
    {
        if (count == 1) return ["t"];
        return [.. Enumerable.Range(1, count).Select(i => "t" + Subscript(i))];
    }

    /// <summary>A number written with subscript digits.</summary>
    public static string Subscript(int i) =>
        new([.. i.ToString(CultureInfo.InvariantCulture).Select(c => (char)('₀' + (c - '0')))]);
}
