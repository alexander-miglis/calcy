using System.Globalization;
using System.Text;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;
using Calcy.Numerics;
using Complex = Calcy.Numerics.Complex;
using NumMatrix = Calcy.Numerics.Matrix;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>The operations offered by the Matrix screen.</summary>
public enum MatrixOperation
{
    /// <summary>det(X), exact.</summary>
    Determinant,

    /// <summary>X⁻¹, exact, with the Gauss-Jordan steps on [X | I].</summary>
    Inverse,

    /// <summary>Xᵀ.</summary>
    Transpose,

    /// <summary>Reduced row echelon form with steps.</summary>
    Rref,

    /// <summary>Row echelon form with steps.</summary>
    Ref,

    /// <summary>Number of pivots.</summary>
    Rank,

    /// <summary>Sum of the diagonal.</summary>
    Trace,

    /// <summary>Columns minus rank.</summary>
    Nullity,

    /// <summary>Basis of the null space.</summary>
    NullSpace,

    /// <summary>Basis of the column space.</summary>
    ColumnSpace,

    /// <summary>Eigenvalues and eigenvectors.</summary>
    Eigen,

    /// <summary>det(λI − X).</summary>
    CharacteristicPolynomial,

    /// <summary>2-norm condition number.</summary>
    Condition,

    /// <summary>1, ∞, Frobenius and 2-norms.</summary>
    Norms,

    /// <summary>Xⁿ.</summary>
    Power,

    /// <summary>LU decomposition with partial pivoting (numeric).</summary>
    Lu,

    /// <summary>QR decomposition (numeric).</summary>
    Qr,

    /// <summary>Cholesky decomposition (numeric).</summary>
    Cholesky,

    /// <summary>Singular value decomposition (numeric).</summary>
    Svd,

    /// <summary>k·X.</summary>
    Scale,

    /// <summary>X·Y.</summary>
    Multiply,

    /// <summary>Y·X.</summary>
    MultiplyReversed,

    /// <summary>X + Y.</summary>
    Add,

    /// <summary>X − Y.</summary>
    Subtract,

    /// <summary>Solve X·x = Y.</summary>
    Solve,
}

/// <summary>The inputs of an operation: the selected matrix X, the second operand Y, the scalar k and the power n.</summary>
public sealed record Operands
{
    /// <summary>Name of the selected matrix.</summary>
    public required string XName { get; init; }

    /// <summary>The selected matrix (null when a cell has an error).</summary>
    public MatrixExpr? X { get; init; }

    /// <summary>Why X has no value.</summary>
    public string? XError { get; init; }

    /// <summary>Name of the second matrix.</summary>
    public string? YName { get; init; }

    /// <summary>The second matrix.</summary>
    public MatrixExpr? Y { get; init; }

    /// <summary>Why Y has no value.</summary>
    public string? YError { get; init; }

    /// <summary>The scalar k for k·X.</summary>
    public string K { get; init; } = "2";

    /// <summary>The power n for Xⁿ.</summary>
    public string N { get; init; } = "2";
}

/// <summary>Runs matrix operations and turns their results into cards. Pure logic: no UI, safe on a background thread.</summary>
public sealed class MatrixCalculator(CalcSession session)
{
    private readonly CardFormatter _f = new(session);

    /// <summary>The formatter used for card text.</summary>
    public CardFormatter Formatter => _f;

    /// <summary>The header text for an operation.</summary>
    public static string TitleOf(MatrixOperation op) => op switch
    {
        MatrixOperation.Determinant => "DETERMINANT",
        MatrixOperation.Inverse => "INVERSE",
        MatrixOperation.Transpose => "TRANSPOSE",
        MatrixOperation.Rref => "RREF",
        MatrixOperation.Ref => "REF",
        MatrixOperation.Rank => "RANK",
        MatrixOperation.Trace => "TRACE",
        MatrixOperation.Nullity => "NULLITY",
        MatrixOperation.NullSpace => "NULL SPACE",
        MatrixOperation.ColumnSpace => "COLUMN SPACE",
        MatrixOperation.Eigen => "EIGEN",
        MatrixOperation.CharacteristicPolynomial => "CHAR POLY",
        MatrixOperation.Condition => "CONDITION",
        MatrixOperation.Norms => "NORMS",
        MatrixOperation.Power => "POWER",
        MatrixOperation.Lu => "LU",
        MatrixOperation.Qr => "QR",
        MatrixOperation.Cholesky => "CHOLESKY",
        MatrixOperation.Svd => "SVD",
        MatrixOperation.Scale => "SCALAR MULTIPLE",
        MatrixOperation.Multiply or MatrixOperation.MultiplyReversed => "PRODUCT",
        MatrixOperation.Add => "SUM",
        MatrixOperation.Subtract => "DIFFERENCE",
        MatrixOperation.Solve => "SOLVE",
        _ => "RESULT",
    };

    /// <summary>The maths notation shown on the card for an operation.</summary>
    public static string InputOf(MatrixOperation op, Operands o)
    {
        string x = o.XName, y = o.YName ?? "?";
        return op switch
        {
            MatrixOperation.Determinant => $"det({x})",
            MatrixOperation.Inverse => $"{x}⁻¹",
            MatrixOperation.Transpose => $"{x}ᵀ",
            MatrixOperation.Rref => $"rref({x})",
            MatrixOperation.Ref => $"ref({x})",
            MatrixOperation.Rank => $"rank({x})",
            MatrixOperation.Trace => $"tr({x})",
            MatrixOperation.Nullity => $"nullity({x})",
            MatrixOperation.NullSpace => $"null({x})",
            MatrixOperation.ColumnSpace => $"col({x})",
            MatrixOperation.Eigen => $"eigen({x})",
            MatrixOperation.CharacteristicPolynomial => $"det(λI − {x})",
            MatrixOperation.Condition => $"cond({x})",
            MatrixOperation.Norms => $"‖{x}‖",
            MatrixOperation.Power => $"{x}^{o.N.Trim()}",
            MatrixOperation.Lu => $"lu({x})",
            MatrixOperation.Qr => $"qr({x})",
            MatrixOperation.Cholesky => $"cholesky({x})",
            MatrixOperation.Svd => $"svd({x})",
            MatrixOperation.Scale => $"{o.K.Trim()}·{x}",
            MatrixOperation.Multiply => $"{x}·{y}",
            MatrixOperation.MultiplyReversed => $"{y}·{x}",
            MatrixOperation.Add => $"{x} + {y}",
            MatrixOperation.Subtract => $"{x} − {y}",
            MatrixOperation.Solve => $"{x}·x = {y}",
            _ => x,
        };
    }

    /// <summary>True when the operation uses the second matrix Y.</summary>
    public static bool UsesY(MatrixOperation op) =>
        op is MatrixOperation.Multiply or MatrixOperation.MultiplyReversed or MatrixOperation.Add or MatrixOperation.Subtract or MatrixOperation.Solve;

    /// <summary>Runs an operation. Problems become error cards; only cancellation throws.</summary>
    public MatrixCard Run(MatrixOperation op, Operands o, CancellationToken ct = default)
    {
        var card = new MatrixCard { Title = TitleOf(op), Input = InputOf(op, o), Time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) };
        try
        {
            if (o.X is null) throw new CalcException(o.XError ?? $"{o.XName} has a cell that is not a valid number");
            if (UsesY(op))
            {
                if (o.YName is null) throw new CalcException("Add a second matrix to the library first");
                if (o.Y is null) throw new CalcException(o.YError ?? $"{o.YName} has a cell that is not a valid number");
            }
            Fill(card, op, o, o.X, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (CalcException ex) { MakeError(card, ex.Message); }
        catch (DivideByZeroException) { MakeError(card, "Division by zero"); }
        catch (OverflowException) { MakeError(card, "The numbers are too large"); }
        catch (InsufficientExecutionStackException) { MakeError(card, "The entries are nested too deeply"); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            MakeError(card, ex.Message);
        }
        card.PreferDecimals = IsUnwieldy(card);
        return card;
    }

    /// <summary>Exact results with very long entries (big fractions) read better as decimals first, like Advanced mode does.</summary>
    public static bool IsUnwieldy(MatrixCard card)
    {
        const int LongCell = 14, LongText = 48;
        foreach (var b in card.Blocks)
        {
            if (b.ApproxCells is not null && b.Cells!.Any(r => r.Any(c => c.Length > LongCell))) return true;
            if (b.ApproxText is not null && b.Text!.Length > LongText) return true;
        }
        return false;
    }

    /// <summary>Evaluates an expression line and makes a card.</summary>
    public MatrixCard Expression(string text, int defaultIdentitySize, CancellationToken ct = default)
    {
        var outcome = ExpressionLine.Evaluate(text, session, defaultIdentitySize, ct);
        var card = new MatrixCard
        {
            Title = outcome.AssignedName is null ? "CALCULATION" : "STORED",
            Input = outcome.Input,
            Source = outcome.Input,
            Time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
        if (outcome.IsError)
        {
            MakeError(card, outcome.Error!);
            return card;
        }
        var value = outcome.Value!;
        string prefix = outcome.AssignedName is null ? "" : outcome.AssignedName + " = ";
        if (value is MatrixExpr m) card.Blocks.Add(_f.Grid(outcome.AssignedName is null ? null : outcome.AssignedName + " =", m, ct: ct));
        else card.Blocks.Add(_f.Value(prefix.Length > 0 ? prefix : "= ", value, ct));
        if (outcome.AssignedName is not null)
            card.Blocks.Add(CardBlock.NoteText(value is MatrixExpr ? $"{outcome.AssignedName} is now in the matrix list and in every mode." : $"{outcome.AssignedName} is now a variable in every mode."));
        SetReuse(card, value);
        card.PreferDecimals = IsUnwieldy(card);
        return card;
    }

    // ------------------------------------------------------------------ operations

    private void Fill(MatrixCard card, MatrixOperation op, Operands o, MatrixExpr x, CancellationToken ct)
    {
        string name = o.XName;
        switch (op)
        {
            case MatrixOperation.Determinant:
            {
                RequireSquare(x, "The determinant");
                var det = MatrixOps.Determinant(x);
                card.Blocks.Add(_f.Value("= ", det, ct));
                if (RowReduction.IsZero(det))
                    card.Blocks.Add(CardBlock.NoteText($"det = 0, so {name} is singular: it has no inverse and {name}·x = b has no unique solution."));
                else if (det.IsConstantValue())
                    card.Blocks.Add(CardBlock.NoteText($"det ≠ 0, so {name} is invertible."));
                SetReuse(card, det);
                break;
            }
            case MatrixOperation.Inverse:
            {
                RequireSquare(x, "Only square matrices have an inverse; this one");
                var aug = Augment(x, MatrixOps.Identity(x.Rows));
                var rref = RowReduction.Rref(aug, ct);
                bool invertible = rref.Rank == x.Rows && rref.PivotColumns.Take(x.Rows).Select((p, i) => p == i).All(b => b);
                AddSteps(card, rref, x.Cols, $"Reduce [{name} | I]. When the left half becomes I, the right half is {name}⁻¹.");
                if (!invertible) throw new CalcException($"{name} is singular (det = 0), so it has no inverse");
                var inv = MatrixOps.Inverse(x);
                card.Blocks.Add(_f.Grid(null, inv, ct: ct));
                SetReuse(card, inv);
                break;
            }
            case MatrixOperation.Transpose:
            {
                var t = MatrixOps.Transpose(x);
                card.Blocks.Add(_f.Grid(null, t, ct: ct));
                SetReuse(card, t);
                break;
            }
            case MatrixOperation.Rref:
            case MatrixOperation.Ref:
            {
                var result = op == MatrixOperation.Rref ? RowReduction.Rref(x, ct) : RowReduction.Ref(x, ct);
                card.Blocks.Add(_f.Grid(null, result.Result, ct: ct));
                card.Blocks.Add(CardBlock.NoteText(PivotNote(result, x.Cols) + (op == MatrixOperation.Ref ? " Row echelon form is not unique; rref is." : "")));
                AddSteps(card, result, -1, null);
                // The steps are the point of rref and ref for students, so they start open (unless there are very many).
                card.StepsOpen = result.Steps.Count is > 0 and <= 30;
                SetReuse(card, result.Result);
                break;
            }
            case MatrixOperation.Rank:
            {
                var result = RowReduction.Rref(x, ct);
                card.Blocks.Add(CardBlock.Line("= " + result.Rank.ToString(CultureInfo.InvariantCulture)));
                card.Blocks.Add(CardBlock.NoteText(PivotNote(result, x.Cols)));
                AddSteps(card, result, -1, null);
                SetReuse(card, Expr.Int(result.Rank));
                break;
            }
            case MatrixOperation.Trace:
            {
                RequireSquare(x, "The trace");
                var tr = MatrixOps.Trace(x);
                card.Blocks.Add(_f.Value("= ", tr, ct));
                var diag = string.Join(" + ", Enumerable.Range(0, x.Rows).Select(i => Paren(_f.CellText(x[i, i]))));
                card.Blocks.Add(CardBlock.NoteText($"The sum of the diagonal: {diag}."));
                SetReuse(card, tr);
                break;
            }
            case MatrixOperation.Nullity:
            {
                var result = RowReduction.Rref(x, ct);
                int nullity = x.Cols - result.Rank;
                card.Blocks.Add(CardBlock.Line("= " + nullity.ToString(CultureInfo.InvariantCulture)));
                card.Blocks.Add(CardBlock.NoteText($"columns − rank = {x.Cols} − {result.Rank} (rank-nullity theorem): the number of free variables in {name}·x = 0."));
                AddSteps(card, result, -1, null);
                SetReuse(card, Expr.Int(nullity));
                break;
            }
            case MatrixOperation.NullSpace:
            {
                var basis = Subspaces.NullSpace(x, out var rref, ct);
                if (basis.Count == 0)
                {
                    card.Blocks.Add(CardBlock.Line("= {0}"));
                    card.Blocks.Add(CardBlock.NoteText($"Only the zero vector: the columns of {name} are independent (rank {rref.Rank})."));
                    SetReuse(card, new MatrixExpr(x.Cols, 1, [.. Enumerable.Repeat<Expr>(Expr.Zero, x.Cols)]));
                }
                else
                {
                    AddBasis(card, basis, "v", ct);
                    card.Blocks.Add(CardBlock.NoteText($"One vector per free variable (nullity {basis.Count}). Every solution of {name}·x = 0 is a combination of them."));
                    SetReuse(card, Columns(basis));
                }
                AddSteps(card, rref, -1, null);
                break;
            }
            case MatrixOperation.ColumnSpace:
            {
                var basis = Subspaces.ColumnSpace(x, out var rref, ct);
                if (basis.Count == 0)
                {
                    card.Blocks.Add(CardBlock.Line("= {0}"));
                    card.Blocks.Add(CardBlock.NoteText($"{name} is the zero matrix."));
                    SetReuse(card, new MatrixExpr(x.Rows, 1, [.. Enumerable.Repeat<Expr>(Expr.Zero, x.Rows)]));
                }
                else
                {
                    AddBasis(card, basis, "c", ct);
                    var cols = string.Join(", ", rref.PivotColumns.Select(p => (p + 1).ToString(CultureInfo.InvariantCulture)));
                    card.Blocks.Add(CardBlock.NoteText($"Columns {cols} of {name} (the pivot columns) form a basis. Dimension {basis.Count} = rank."));
                    SetReuse(card, Columns(basis));
                }
                AddSteps(card, rref, -1, null);
                break;
            }
            case MatrixOperation.Eigen:
                FillEigen(card, x, name, ct);
                break;
            case MatrixOperation.CharacteristicPolynomial:
            {
                RequireSquare(x, "The characteristic polynomial");
                var coefficients = CharacteristicPolynomial.Coefficients(x, session, ct);
                var poly = CharacteristicPolynomial.ToExpr(coefficients);
                card.Blocks.Add(CardBlock.Line("p(λ) = " + _f.Text(poly)));
                if (NumericBridge.IsRational(x) && x.Rows <= MatrixLibrary.MaxSize)
                {
                    var eigen = Eigen.Analyse(x, session, ct);
                    if (eigen.Factored is { } factored && x.Rows > 1) card.Blocks.Add(CardBlock.FormulaText("= " + factored));
                }
                card.Blocks.Add(CardBlock.NoteText("Its roots are the eigenvalues. Built with the Faddeev-LeVerrier recurrence, exactly."));
                SetReuse(card, poly);
                break;
            }
            case MatrixOperation.Condition:
                FillCondition(card, x, name, ct);
                break;
            case MatrixOperation.Norms:
                FillNorms(card, x, name, ct);
                break;
            case MatrixOperation.Power:
            {
                RequireSquare(x, "Powers");
                var n = ParseInteger(o.N, "n");
                var p = Algebra.Pow(x, Expr.Int(n));
                card.Blocks.Add(_f.Grid(null, (MatrixExpr)p, ct: ct));
                SetReuse(card, p);
                break;
            }
            case MatrixOperation.Scale:
            {
                var k = ParseScalar(o.K, ct);
                var r = (MatrixExpr)Algebra.Mul(k, x);
                card.Input = $"{_f.Text(k)}·{name}";
                card.Blocks.Add(_f.Grid(null, r, ct: ct));
                SetReuse(card, r);
                break;
            }
            case MatrixOperation.Multiply:
            case MatrixOperation.MultiplyReversed:
            {
                var r = op == MatrixOperation.Multiply ? MatrixOps.Multiply(x, o.Y!) : MatrixOps.Multiply(o.Y!, x);
                card.Blocks.Add(_f.Grid(null, r, ct: ct));
                SetReuse(card, r);
                break;
            }
            case MatrixOperation.Add:
            case MatrixOperation.Subtract:
            {
                var y = o.Y!;
                if (x.Rows != y.Rows || x.Cols != y.Cols)
                    throw new CalcException($"Only matrices of the same size can be {(op == MatrixOperation.Add ? "added" : "subtracted")}: {name} is {x.Rows}×{x.Cols}, {o.YName} is {y.Rows}×{y.Cols}");
                var r = (MatrixExpr)(op == MatrixOperation.Add ? Algebra.Add(x, y) : Algebra.Sub(x, y));
                card.Blocks.Add(_f.Grid(null, r, ct: ct));
                SetReuse(card, r);
                break;
            }
            case MatrixOperation.Solve:
                FillSolve(card, x, name, o.Y!, o.YName!, ct);
                break;
            case MatrixOperation.Lu:
            case MatrixOperation.Qr:
            case MatrixOperation.Cholesky:
            case MatrixOperation.Svd:
                FillDecomposition(card, op, x, name, ct);
                break;
        }
    }

    // ------------------------------------------------------------------ solve

    private void FillSolve(MatrixCard card, MatrixExpr a, string name, MatrixExpr b, string bName, CancellationToken ct)
    {
        var s = LinearSystem.Solve(a, b, ct);
        AddSteps(card, s.Elimination, a.Cols, $"Reduce the augmented matrix [{name} | {bName}].");
        switch (s.Kind)
        {
            case SolutionKind.Unique:
            {
                var x = s.Particular!;
                if (x.Cols == 1)
                {
                    card.Blocks.Add(_f.Grid("x =", x, ct: ct));
                    var parts = s.Variables.Select((v, i) => $"{v} = {_f.Text(x[i, 0])}");
                    var decimals = s.Variables.Select((_, i) => _f.ApproxText(x[i, 0], ct)).ToList();
                    var approx = s.Variables.Select((v, i) => decimals[i] is { } d ? $"{v} ≈ {d}" : $"{v} = {_f.Text(x[i, 0])}");
                    card.Blocks.Add(CardBlock.Line(string.Join(",  ", parts), decimals.Any(d => d is not null) ? string.Join(",  ", approx) : null));
                    card.Blocks.Add(CardBlock.NoteText("Exactly one solution: every column has a pivot."));
                }
                else
                {
                    card.Blocks.Add(_f.Grid("X =", x, ct: ct));
                    card.Blocks.Add(CardBlock.NoteText($"One solution for each column of {bName}."));
                }
                SetReuse(card, x);
                break;
            }
            case SolutionKind.None:
            {
                var r = s.Elimination.Result;
                var rhs = _f.Text(r[s.ContradictionRow, a.Cols]);
                card.Blocks.Add(CardBlock.Line("No solution", emphasis: true));
                card.Blocks.Add(CardBlock.NoteText($"Row {s.ContradictionRow + 1} of the reduced matrix says 0 = {rhs}, which is impossible, so the equations contradict each other."));
                card.StoreParts.Clear();
                break;
            }
            default:
            {
                card.Blocks.Add(CardBlock.Line("Infinitely many solutions", emphasis: false));
                for (int i = 0; i < s.Variables.Count; i++)
                {
                    var comp = s.Components[i];
                    var decimals = _f.ApproxText(comp, ct);
                    card.Blocks.Add(CardBlock.Line($"{s.Variables[i]} = {_f.Text(comp)}", decimals is null ? null : $"{s.Variables[i]} ≈ {decimals}"));
                }
                var vector = new StringBuilder();
                vector.Append("x = ").Append(Tuple(s.Particular!.Items));
                for (int k = 0; k < s.Directions.Count; k++) vector.Append(" + ").Append(s.Parameters[k]).Append('·').Append(Tuple(s.Directions[k]));
                card.Blocks.Add(CardBlock.FormulaText(vector.ToString()));
                var free = string.Join(", ", s.FreeVariables.Select(f => s.Variables[f]));
                var parameters = string.Join(", ", s.Parameters);
                card.Blocks.Add(CardBlock.NoteText($"{free} {(s.FreeVariables.Count == 1 ? "is a free variable" : "are free variables")} with no pivot; {parameters} can be any number."));
                // x = particular + t·direction, as Calcy input.
                var copy = new StringBuilder(ExprPrinter.ToInput(s.Particular));
                for (int k = 0; k < s.Directions.Count; k++)
                    copy.Append(" + ").Append(ExprPrinter.ToInput(new SymbolExpr(s.Parameters[k]))).Append('*').Append(ExprPrinter.ToInput(new MatrixExpr(s.Directions[k].Length, 1, [.. s.Directions[k]])));
                card.CopyText = copy.ToString();
                card.UseText = card.CopyText;
                var latex = new StringBuilder(LatexPrinter.ToLatex(s.Particular));
                for (int k = 0; k < s.Directions.Count; k++)
                    latex.Append(" + ").Append(LatexPrinter.ToLatex(new SymbolExpr(s.Parameters[k]))).Append(' ').Append(LatexPrinter.ToLatex(new MatrixExpr(s.Directions[k].Length, 1, [.. s.Directions[k]])));
                card.Latex = "x = " + latex;
                card.StoreParts = [new StorePart { Label = "particular", Text = ExprPrinter.ToInput(s.Particular), IsMatrix = true }];
                for (int k = 0; k < s.Directions.Count; k++)
                    card.StoreParts.Add(new StorePart { Label = "direction " + s.Parameters[k], Text = ExprPrinter.ToInput(new MatrixExpr(s.Directions[k].Length, 1, [.. s.Directions[k]])), IsMatrix = true });
                break;
            }
        }
    }

    /// <summary>A point such as (3, 3, 0); no-break spaces keep it on one line.</summary>
    private string Tuple(IEnumerable<Expr> items) => "(" + string.Join(", ", items.Select(_f.CellText)) + ")";

    // ------------------------------------------------------------------ eigen

    private void FillEigen(MatrixCard card, MatrixExpr x, string name, CancellationToken ct)
    {
        var e = Eigen.Analyse(x, session, ct);
        var poly = CharacteristicPolynomial.ToExpr(e.CharacteristicPolynomial);
        int index = 0;
        var pColumns = new List<Expr[]>();
        var dValues = new List<Expr>();
        foreach (var v in e.Values)
        {
            string multiplicity = v.AlgebraicMultiplicity switch { 1 => "", 2 => "  (twice)", _ => $"  ({v.AlgebraicMultiplicity} times)" };
            index++;
            string lambda = e.Values.Count == 1 ? "λ" : "λ" + LinearSystem.Subscript(index);
            if (v.Exact is { } exact)
            {
                var approxValue = _f.ApproxText(exact, ct);
                card.Blocks.Add(CardBlock.Line(
                    $"{lambda} = {v.ExactText ?? _f.Text(exact)}{multiplicity}",
                    approxValue is null ? null : $"{lambda} ≈ {approxValue}{multiplicity}"));
                for (int k = 0; k < v.Vectors.Count; k++)
                {
                    string label = (v.Vectors.Count == 1 ? "v" + LinearSystem.Subscript(index) : "v" + LinearSystem.Subscript(index) + "," + LinearSystem.Subscript(k + 1)) + " =";
                    var col = new MatrixExpr(v.Vectors[k].Length, 1, [.. v.Vectors[k]]);
                    var cells = v.VectorTexts is { } texts ? texts[k].Select(t => new[] { t }).ToArray() : _f.Cells(col);
                    card.Blocks.Add(CardBlock.Grid(label, cells, _f.ApproxCells(col, ct)));
                    pColumns.Add(v.Vectors[k]);
                    dValues.Add(exact);
                }
            }
            else
            {
                card.Blocks.Add(CardBlock.Line($"{lambda} ≈ {ComplexText(v.Approx, cell: true)}{multiplicity}"));
                for (int k = 0; k < v.NumericVectors.Count; k++)
                {
                    string label = (v.NumericVectors.Count == 1 ? "v" + LinearSystem.Subscript(index) : "v" + LinearSystem.Subscript(index) + "," + LinearSystem.Subscript(k + 1)) + " ≈";
                    var cells = v.NumericVectors[k].Select(z => new[] { ComplexText(z, cell: true) }).ToArray();
                    card.Blocks.Add(CardBlock.Grid(label, cells));
                    pColumns.Add([.. v.NumericVectors[k].Select(ToExpr)]);
                    dValues.Add(ToExpr(v.Approx));
                }
            }
            if (v.GeometricMultiplicity < v.AlgebraicMultiplicity)
                card.Blocks.Add(CardBlock.NoteText($"{lambda} is repeated {v.AlgebraicMultiplicity} times but has only {v.GeometricMultiplicity} independent eigenvector{(v.GeometricMultiplicity == 1 ? "" : "s")}."));
        }
        var charText = "det(λI − " + name + ") = " + _f.Text(poly) + (e.Factored is { } f && x.Rows > 1 ? " = " + f : "");
        card.Blocks.Add(CardBlock.FormulaText(charText));
        if (e.Note is not null) card.Blocks.Add(CardBlock.NoteText(e.Note + "."));
        bool diagonalizable = e.IsDiagonalizable && pColumns.Count == x.Rows;
        card.Blocks.Add(CardBlock.NoteText(diagonalizable
            ? $"{name} = P·D·P⁻¹ with the eigenvectors as the columns of P and the eigenvalues on the diagonal of D."
            : $"{name} is not diagonalizable: there are fewer than {x.Rows} independent eigenvectors."));

        // COPY gives the eigenvalues; STORE AS can save P and D.
        var list = new ListExpr([.. e.Values.SelectMany(v => Enumerable.Repeat(v.Exact ?? ToExpr(v.Approx), v.AlgebraicMultiplicity))]);
        card.CopyText = ExprPrinter.ToInput(list);
        card.UseText = card.CopyText;
        card.Latex = string.Join(@",\; ", list.Items.Select(i => LatexPrinter.ToLatex(i)));
        if (diagonalizable)
        {
            var p = Columns(pColumns);
            int n = x.Rows;
            var d = new Expr[n * n];
            for (int i = 0; i < n * n; i++) d[i] = Expr.Zero;
            for (int i = 0; i < n; i++) d[i * n + i] = dValues[i];
            card.StoreParts =
            [
                new StorePart { Label = "P (eigenvectors)", Text = ExprPrinter.ToInput(p), IsMatrix = true },
                new StorePart { Label = "D (eigenvalues)", Text = ExprPrinter.ToInput(new MatrixExpr(n, n, [.. d])), IsMatrix = true },
            ];
        }
        else card.StoreParts = [new StorePart { Label = "eigenvalues", Text = card.CopyText, IsMatrix = false }];
    }

    private static Expr ToExpr(Complex z) => new NumberExpr(Number.Approx(z.Re, z.Im));

    private string ComplexText(Complex z, bool cell)
    {
        Func<double, string> fmt = cell ? new Func<double, string>(_f.CellNumber) : _f.Number;
        if (z.Im == 0) return fmt(z.Re);
        string im = fmt(Math.Abs(z.Im));
        if (z.Re == 0) return (z.Im < 0 ? "-" : "") + im + "i";
        return fmt(z.Re) + (z.Im < 0 ? " - " : " + ") + im + "i";
    }

    // ------------------------------------------------------------------ condition and norms

    private void FillCondition(MatrixCard card, MatrixExpr x, string name, CancellationToken ct)
    {
        var a = NumericBridge.ToNumeric(x, session, ct);
        var svd = new SvdDecomposition(a);
        double kappa = svd.ConditionNumber;
        if (double.IsInfinity(kappa) || double.IsNaN(kappa) || kappa > 1e16)
        {
            card.Blocks.Add(CardBlock.Line("κ₂ = ∞"));
            card.Blocks.Add(CardBlock.NoteText($"{name} is singular (its smallest singular value is 0), so A·x = b cannot be solved reliably."));
            card.CopyText = "inf";
            card.UseText = card.CopyText;
            card.Latex = @"\infty";
            card.StoreParts = [];
            return;
        }
        card.Blocks.Add(CardBlock.Line("κ₂ ≈ " + _f.Number(kappa)));
        card.Blocks.Add(CardBlock.NoteText($"σmax / σmin = {_f.CellNumber(svd.S[0])} / {_f.CellNumber(svd.S[^1])} (2-norm)."));
        double digits = Math.Log10(Math.Max(1, kappa));
        string advice = kappa < 100 ? "Well conditioned: solving A·x = b loses almost no accuracy."
            : kappa < 1e8 ? $"Solving A·x = b can lose about {Math.Round(digits):0} of the 16 significant digits."
            : $"Ill-conditioned: solving A·x = b can lose about {Math.Round(digits):0} of 16 digits, so decimal answers may be inaccurate. Exact answers are still exact.";
        card.Blocks.Add(CardBlock.NoteText(advice));
        SetReuse(card, NumericBridge.Approx(kappa));
    }

    private void FillNorms(MatrixCard card, MatrixExpr x, string name, CancellationToken ct)
    {
        var values = new List<Expr>();
        if (NumericBridge.IsRational(x))
        {
            var q = x.Items.Select(e => ((NumberExpr)e).Value.ReQ.Abs()).ToArray();
            BigRational one = BigRational.Zero, inf = BigRational.Zero, squares = BigRational.Zero;
            for (int c = 0; c < x.Cols; c++)
            {
                var s = BigRational.Zero;
                for (int r = 0; r < x.Rows; r++) s += q[r * x.Cols + c];
                if (s > one) one = s;
            }
            for (int r = 0; r < x.Rows; r++)
            {
                var s = BigRational.Zero;
                for (int c = 0; c < x.Cols; c++) s += q[r * x.Cols + c];
                if (s > inf) inf = s;
            }
            foreach (var v in q) squares += v * v;
            var fro = Algebra.Sqrt(Algebra.Q(squares));
            card.Blocks.Add(CardBlock.Line($"‖{name}‖₁ = {_f.Text(Algebra.Q(one))}", _f.ApproxText(Algebra.Q(one), ct) is { } a1 ? $"‖{name}‖₁ ≈ {a1}" : null));
            card.Blocks.Add(CardBlock.Line($"‖{name}‖∞ = {_f.Text(Algebra.Q(inf))}", _f.ApproxText(Algebra.Q(inf), ct) is { } ai ? $"‖{name}‖∞ ≈ {ai}" : null));
            card.Blocks.Add(CardBlock.Line($"‖{name}‖F = {_f.Text(fro)}", _f.ApproxText(fro, ct) is { } af ? $"‖{name}‖F ≈ {af}" : null));
            values.AddRange([Algebra.Q(one), Algebra.Q(inf), fro]);
            if (x.Cols == 1 || x.Rows == 1)
            {
                card.Blocks.Add(CardBlock.Line($"‖{name}‖₂ = {_f.Text(fro)}", _f.ApproxText(fro, ct) is { } a2 ? $"‖{name}‖₂ ≈ {a2}" : null));
                values.Add(fro);
            }
            else
            {
                double two = LinearAlgebra.Norm2(NumericBridge.ToNumeric(x, session, ct));
                card.Blocks.Add(CardBlock.Line($"‖{name}‖₂ ≈ {_f.Number(two)}"));
                values.Add(NumericBridge.Approx(two));
            }
        }
        else
        {
            var a = NumericBridge.ToNumeric(x, session, ct);
            double[] n = [LinearAlgebra.Norm1(a), LinearAlgebra.NormInf(a), LinearAlgebra.NormFrobenius(a), LinearAlgebra.Norm2(a)];
            string[] labels = ["₁", "∞", "F", "₂"];
            for (int i = 0; i < 4; i++) card.Blocks.Add(CardBlock.Line($"‖{name}‖{labels[i]} ≈ {_f.Number(n[i])}"));
            values.AddRange(n.Select(NumericBridge.Approx));
        }
        card.Blocks.Add(CardBlock.NoteText("1: largest column sum of |entries|. ∞: largest row sum. F: square root of the sum of squares. 2: largest singular value."));
        SetReuse(card, new ListExpr([.. values]));
        card.StoreParts = [new StorePart { Label = "2-norm", Text = ExprPrinter.ToInput(values[^1]), IsMatrix = false }];
    }

    // ------------------------------------------------------------------ decompositions

    private void FillDecomposition(MatrixCard card, MatrixOperation op, MatrixExpr x, string name, CancellationToken ct)
    {
        var a = NumericBridge.ToNumeric(x, session, ct);
        var parts = new List<(string Name, NumMatrix Value)>();
        var notes = new List<CardBlock>();
        CardBlock? lead = null;
        switch (op)
        {
            case MatrixOperation.Lu:
            {
                if (!a.IsSquare) throw new CalcException($"LU decomposition needs a square matrix, not {x.Rows}×{x.Cols}");
                var lu = new LuDecomposition(a);
                parts.Add(("P", lu.P));
                parts.Add(("L", lu.L));
                parts.Add(("U", lu.U));
                notes.Add(CardBlock.NoteText("Partial pivoting: P swaps rows so each pivot is the largest available. L has 1s on its diagonal." + (lu.IsSingular ? $" {name} is singular, so U has a zero on its diagonal." : "")));
                break;
            }
            case MatrixOperation.Qr:
            {
                var qr = new QrDecomposition(a);
                parts.Add(("Q", qr.Q));
                parts.Add(("R", qr.R));
                notes.Add(CardBlock.NoteText("Householder reflections: Q has orthonormal columns (QᵀQ = I) and R is upper triangular."));
                break;
            }
            case MatrixOperation.Cholesky:
            {
                if (!a.IsSquare) throw new CalcException($"Cholesky needs a square matrix, not {x.Rows}×{x.Cols}");
                if (!a.IsSymmetric(1e-12)) throw new CalcException($"Cholesky needs a symmetric matrix ({name} = {name}ᵀ), and {name} is not symmetric");
                CholeskyDecomposition ch;
                try
                {
                    ch = new CholeskyDecomposition(a);
                }
                catch (InvalidOperationException)
                {
                    throw new CalcException($"Cholesky needs a positive definite matrix (all eigenvalues > 0), and {name} is not");
                }
                parts.Add(("L", ch.L));
                notes.Add(CardBlock.NoteText($"{name} is symmetric positive definite, so {name} = L·Lᵀ with L lower triangular."));
                break;
            }
            default:
            {
                var svd = new SvdDecomposition(a);
                var sigma = new NumMatrix(svd.S.Length, svd.S.Length);
                for (int i = 0; i < svd.S.Length; i++) sigma[i, i] = svd.S[i];
                lead = CardBlock.Line("σ = " + string.Join(", ", svd.S.Select(_f.CellNumber)));
                parts.Add(("U", svd.U));
                parts.Add(("Σ", sigma));
                parts.Add(("V", svd.V));
                notes.Add(CardBlock.NoteText($"Singular values in decreasing order; rank {svd.Rank()} (the number of non-zero singular values)."));
                break;
            }
        }
        // The defining equation, with the matrix's own name unless it clashes with a factor's letter.
        string m = parts.Any(p => p.Name == name) ? "A" : name;
        string equation = op switch
        {
            MatrixOperation.Lu => $"P·{m} = L·U",
            MatrixOperation.Qr => $"{m} = Q·R",
            MatrixOperation.Cholesky => $"{m} = L·Lᵀ",
            _ => $"{m} = U·Σ·Vᵀ",
        };
        if (m != name) equation += $"  with A = {name}";
        card.Blocks.Add(CardBlock.FormulaText(equation));
        if (lead is not null) card.Blocks.Add(lead);
        foreach (var (pn, pm) in parts) card.Blocks.Add(CardBlock.Grid(pn + " =", _f.Cells(NumericBridge.FromNumeric(pm))));
        card.Blocks.AddRange(notes);
        card.Blocks.Add(CardBlock.NoteText("Computed numerically in double precision."));
        var inputs = parts.Select(p => (p.Name, Text: ExprPrinter.ToInput(NumericBridge.FromNumeric(p.Value)))).ToList();
        card.CopyText = string.Join("\n", inputs.Select(p => $"{AsciiName(p.Name)} = {p.Text}"));
        card.UseText = inputs[0].Text;
        card.Latex = string.Join(@",\quad ", parts.Select(p => (p.Name == "Σ" ? @"\Sigma" : p.Name) + " = " + LatexPrinter.ToLatex(NumericBridge.FromNumeric(p.Value))));
        card.StoreParts = [.. inputs.Select(p => new StorePart { Label = p.Name, Text = p.Text, IsMatrix = true })];
    }

    private static string AsciiName(string n) => n == "Σ" ? "S" : n;

    // ------------------------------------------------------------------ helpers

    private void AddSteps(MatrixCard card, EliminationResult result, int augmentAt, string? intro)
    {
        if (intro is not null) card.Steps.Add(CardBlock.NoteText(intro));
        if (result.Steps.Count == 0)
        {
            card.Steps.Add(CardBlock.NoteText("Nothing to do: the matrix is already reduced."));
            return;
        }
        EliminationPhase? phase = null;
        foreach (var s in result.Steps)
        {
            if (s.Phase != phase)
            {
                phase = s.Phase;
                card.Steps.Add(CardBlock.HeadingText(phase == EliminationPhase.Forward ? "Forward: a leading 1 in each pivot, zeros below it" : "Backward: zeros above each leading 1"));
            }
            card.Steps.Add(CardBlock.StepBlock(s.Operation.Describe(), _f.Cells(s.After), _f.ApproxCells(s.After), augmentAt));
        }
    }

    private void AddBasis(MatrixCard card, IReadOnlyList<Expr[]> basis, string letter, CancellationToken ct)
    {
        for (int k = 0; k < basis.Count; k++)
        {
            var col = new MatrixExpr(basis[k].Length, 1, [.. basis[k]]);
            card.Blocks.Add(_f.Grid(letter + LinearSystem.Subscript(k + 1) + " =", col, ct: ct));
        }
    }

    private static MatrixExpr Columns(IReadOnlyList<Expr[]> columns)
    {
        int rows = columns[0].Length, cols = columns.Count;
        var items = new Expr[rows * cols];
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++) items[r * cols + c] = columns[c][r];
        return new MatrixExpr(rows, cols, [.. items]);
    }

    private static MatrixExpr Augment(MatrixExpr a, MatrixExpr b)
    {
        var items = new List<Expr>(a.Rows * (a.Cols + b.Cols));
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Cols; c++) items.Add(a[r, c]);
            for (int c = 0; c < b.Cols; c++) items.Add(b[r, c]);
        }
        return new MatrixExpr(a.Rows, a.Cols + b.Cols, [.. items]);
    }

    private static string PivotNote(EliminationResult r, int cols)
    {
        if (r.Rank == 0) return "Rank 0: every entry is zero.";
        if (r.Rank == cols) return $"Rank {r.Rank}: a pivot in every column.";
        var p = string.Join(", ", r.PivotColumns.Select(c => (c + 1).ToString(CultureInfo.InvariantCulture)));
        return $"Rank {r.Rank}: pivots in column{(r.Rank == 1 ? "" : "s")} {p} of {cols}; the other columns are free.";
    }

    private static string Paren(string s) => s.StartsWith('-') ? "(" + s + ")" : s;

    private static void RequireSquare(MatrixExpr x, string what)
    {
        if (x.Rows != x.Cols) throw new CalcException($"{what} needs a square matrix, not {x.Rows}×{x.Cols}");
    }

    private int ParseInteger(string text, string what)
    {
        var e = ParseScalar(text, CancellationToken.None);
        if (e is NumberExpr { Value.IsExactInteger: true } n && n.Value.TryGetInt(out int v) && Math.Abs(v) <= 1000) return v;
        throw new CalcException($"{what} must be a whole number between -1000 and 1000");
    }

    private Expr ParseScalar(string text, CancellationToken ct)
    {
        var cell = MatrixLibrary.EvaluateCell(text, session, ct);
        if (!cell.IsValid) throw new CalcException(cell.Error ?? "Type a number");
        return cell.Value!;
    }

    private void SetReuse(MatrixCard card, Expr value)
    {
        card.CopyText = ExprPrinter.ToInput(value);
        card.UseText = card.CopyText;
        card.Latex = LatexPrinter.ToLatex(value);
        card.StoreParts = [new StorePart { Text = card.CopyText, IsMatrix = value is MatrixExpr }];
    }

    private static void MakeError(MatrixCard card, string message)
    {
        card.IsError = true;
        card.Blocks.Clear();
        card.StoreParts.Clear();
        card.CopyText = null;
        card.UseText = null;
        card.Latex = null;
        card.Blocks.Add(CardBlock.NoteText(message.EndsWith('.') ? message : message + "."));
    }
}
