namespace Calcy.Numerics;

/// <summary>Relation of a linear constraint.</summary>
public enum ConstraintRelation
{
    /// <summary>a . x &lt;= b.</summary>
    LessOrEqual,

    /// <summary>a . x &gt;= b.</summary>
    GreaterOrEqual,

    /// <summary>a . x = b.</summary>
    Equal,
}

/// <summary>A linear constraint a . x (relation) b.</summary>
/// <param name="Coefficients">Coefficient vector a.</param>
/// <param name="Relation">Relation.</param>
/// <param name="RightHandSide">Right-hand side b.</param>
public sealed record LinearConstraint(double[] Coefficients, ConstraintRelation Relation, double RightHandSide);

/// <summary>Outcome of a linear program.</summary>
public enum LpStatus
{
    /// <summary>An optimal solution was found.</summary>
    Optimal,

    /// <summary>No point satisfies the constraints.</summary>
    Infeasible,

    /// <summary>The objective is unbounded on the feasible set.</summary>
    Unbounded,

    /// <summary>The iteration limit was reached.</summary>
    IterationLimit,
}

/// <summary>Result of a linear program.</summary>
/// <param name="Status">Outcome.</param>
/// <param name="X">Optimal point (empty unless optimal).</param>
/// <param name="ObjectiveValue">Optimal objective value (NaN unless optimal).</param>
/// <param name="Iterations">Simplex pivots performed.</param>
public sealed record LpResult(LpStatus Status, double[] X, double ObjectiveValue, int Iterations);

/// <summary>
/// Linear programming over x &gt;= 0 with &lt;=, &gt;= and = constraints, by the two-phase
/// simplex method with Bland's anti-cycling rule.
/// </summary>
public static class LinearProgramming
{
    /// <summary>Minimizes c . x subject to the constraints and x &gt;= 0.</summary>
    public static LpResult Minimize(double[] objective, IReadOnlyList<LinearConstraint> constraints, int maxIterations = 50_000, CancellationToken cancellationToken = default)
        => Solve(objective, constraints, false, maxIterations, cancellationToken);

    /// <summary>Maximizes c . x subject to the constraints and x &gt;= 0.</summary>
    public static LpResult Maximize(double[] objective, IReadOnlyList<LinearConstraint> constraints, int maxIterations = 50_000, CancellationToken cancellationToken = default)
        => Solve(objective, constraints, true, maxIterations, cancellationToken);

    /// <summary>Solves the linear program (maximize when <paramref name="maximize"/> is true).</summary>
    public static LpResult Solve(double[] objective, IReadOnlyList<LinearConstraint> constraints, bool maximize, int maxIterations = 50_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(constraints);
        int n = objective.Length;
        int m = constraints.Count;
        foreach (var c in constraints)
        {
            if (c.Coefficients.Length != n) throw new ArgumentException("Every constraint must have one coefficient per variable.");
            if (!c.Coefficients.All(double.IsFinite) || !double.IsFinite(c.RightHandSide)) throw new ArgumentException("Constraint data must be finite.");
        }
        if (!objective.All(double.IsFinite)) throw new ArgumentException("Objective coefficients must be finite.");

        // Normalize to b >= 0.
        var rows = new double[m][];
        var rel = new ConstraintRelation[m];
        var rhs = new double[m];
        for (int i = 0; i < m; i++)
        {
            var c = constraints[i];
            rows[i] = (double[])c.Coefficients.Clone();
            rel[i] = c.Relation;
            rhs[i] = c.RightHandSide;
            if (rhs[i] < 0)
            {
                for (int j = 0; j < n; j++) rows[i][j] = -rows[i][j];
                rhs[i] = -rhs[i];
                rel[i] = rel[i] switch
                {
                    ConstraintRelation.LessOrEqual => ConstraintRelation.GreaterOrEqual,
                    ConstraintRelation.GreaterOrEqual => ConstraintRelation.LessOrEqual,
                    _ => ConstraintRelation.Equal,
                };
            }
        }

        int slackCount = rel.Count(r => r != ConstraintRelation.Equal);
        int artCount = rel.Count(r => r != ConstraintRelation.LessOrEqual);
        int cols = n + slackCount + artCount; // excluding the RHS column
        var T = new double[m, cols + 1];
        var basis = new int[m];
        var isArtificial = new bool[cols];
        int s = n, art = n + slackCount;
        double scale = 1;
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++) { T[i, j] = rows[i][j]; scale = Math.Max(scale, Math.Abs(rows[i][j])); }
            T[i, cols] = rhs[i];
            switch (rel[i])
            {
                case ConstraintRelation.LessOrEqual:
                    T[i, s] = 1; basis[i] = s; s++;
                    break;
                case ConstraintRelation.GreaterOrEqual:
                    T[i, s] = -1; s++;
                    T[i, art] = 1; basis[i] = art; isArtificial[art] = true; art++;
                    break;
                default:
                    T[i, art] = 1; basis[i] = art; isArtificial[art] = true; art++;
                    break;
            }
        }
        double tol = 1e-9 * scale;
        int iterations = 0;

        // Phase 1: minimize the sum of artificial variables.
        if (artCount > 0)
        {
            var cost1 = new double[cols];
            for (int j = 0; j < cols; j++) cost1[j] = isArtificial[j] ? 1 : 0;
            var status1 = Simplex(T, basis, cost1, m, cols, null, tol, maxIterations, ref iterations, cancellationToken);
            if (status1 == LpStatus.IterationLimit) return new LpResult(LpStatus.IterationLimit, [], double.NaN, iterations);
            double infeas = 0;
            for (int i = 0; i < m; i++) if (isArtificial[basis[i]]) infeas += T[i, cols];
            double rhsScale = Math.Max(1, rhs.Length > 0 ? rhs.Max() : 1);
            if (infeas > 1e-8 * rhsScale) return new LpResult(LpStatus.Infeasible, [], double.NaN, iterations);
            // Drive remaining (zero-valued) artificial variables out of the basis.
            for (int i = 0; i < m; i++)
            {
                if (!isArtificial[basis[i]]) continue;
                int enter = -1;
                for (int j = 0; j < cols; j++)
                    if (!isArtificial[j] && Math.Abs(T[i, j]) > tol) { enter = j; break; }
                if (enter >= 0) Pivot(T, basis, m, cols, i, enter);
                // Otherwise the row is redundant; its artificial stays basic at zero.
            }
        }

        // Phase 2: the real objective (as a minimization).
        var cost = new double[cols];
        for (int j = 0; j < n; j++) cost[j] = maximize ? -objective[j] : objective[j];
        var status = Simplex(T, basis, cost, m, cols, isArtificial, tol, maxIterations, ref iterations, cancellationToken);
        if (status != LpStatus.Optimal) return new LpResult(status, [], double.NaN, iterations);
        var x = new double[n];
        for (int i = 0; i < m; i++)
            if (basis[i] < n) x[basis[i]] = Math.Abs(T[i, cols]) <= tol * 1e-3 ? 0 : T[i, cols];
        double value = 0;
        for (int j = 0; j < n; j++) value += objective[j] * x[j];
        return new LpResult(LpStatus.Optimal, x, value, iterations);
    }

    private static LpStatus Simplex(double[,] T, int[] basis, double[] cost, int m, int cols, bool[]? excluded, double tol, int maxIterations, ref int iterations, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (iterations >= maxIterations) return LpStatus.IterationLimit;
            // Reduced costs r_j = c_j - c_B . column_j; Bland: first j with r_j < 0.
            int enter = -1;
            for (int j = 0; j < cols; j++)
            {
                if (excluded != null && excluded[j]) continue;
                double r = cost[j];
                for (int i = 0; i < m; i++) r -= cost[basis[i]] * T[i, j];
                if (r < -tol) { enter = j; break; }
            }
            if (enter < 0) return LpStatus.Optimal;
            // Ratio test; ties broken by the smallest basic variable index (Bland).
            int leave = -1;
            double bestRatio = double.PositiveInfinity;
            for (int i = 0; i < m; i++)
            {
                double a = T[i, enter];
                if (a <= tol) continue;
                double ratio = T[i, cols] / a;
                if (leave < 0 || ratio < bestRatio - 1e-12 * Math.Max(1, Math.Abs(bestRatio)) ||
                    (Math.Abs(ratio - bestRatio) <= 1e-12 * Math.Max(1, Math.Abs(bestRatio)) && basis[i] < basis[leave]))
                {
                    bestRatio = ratio;
                    leave = i;
                }
            }
            if (leave < 0) return LpStatus.Unbounded;
            Pivot(T, basis, m, cols, leave, enter);
            iterations++;
        }
    }

    private static void Pivot(double[,] T, int[] basis, int m, int cols, int row, int col)
    {
        double p = T[row, col];
        for (int j = 0; j <= cols; j++) T[row, j] /= p;
        T[row, col] = 1;
        for (int i = 0; i < m; i++)
        {
            if (i == row) continue;
            double f = T[i, col];
            if (f == 0) continue;
            for (int j = 0; j <= cols; j++) T[i, j] -= f * T[row, j];
            T[i, col] = 0;
        }
        basis[row] = col;
    }
}
