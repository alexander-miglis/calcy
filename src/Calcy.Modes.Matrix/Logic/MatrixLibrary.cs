using System.Globalization;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>The parsed value of one editor cell.</summary>
/// <param name="Text">What the user typed.</param>
/// <param name="Value">The exact value, or null when the text has an error.</param>
/// <param name="Error">Why the text could not be used.</param>
public sealed record CellValue(string Text, Expr? Value, string? Error)
{
    /// <summary>True when the cell has a usable value.</summary>
    public bool IsValid => Value is not null;
}

/// <summary>A named matrix (or column vector) in the library. Cells hold the text the user typed, row by row.</summary>
public sealed class MatrixEntry
{
    private string[] _cells;

    /// <summary>Creates an entry; missing cells are "0".</summary>
    public MatrixEntry(string name, int rows, int cols, bool isVector = false, IReadOnlyList<string>? cells = null)
    {
        Name = name;
        IsVector = isVector;
        Rows = Math.Clamp(rows, 1, MatrixLibrary.MaxSize);
        Cols = isVector ? 1 : Math.Clamp(cols, 1, MatrixLibrary.MaxSize);
        _cells = new string[Rows * Cols];
        for (int i = 0; i < _cells.Length; i++) _cells[i] = cells is not null && i < cells.Count ? cells[i] ?? "0" : "0";
    }

    /// <summary>The variable name (A, B, ..., u, v, ...).</summary>
    public string Name { get; internal set; }

    /// <summary>Vectors are single columns with lower-case names.</summary>
    public bool IsVector { get; }

    /// <summary>Number of rows.</summary>
    public int Rows { get; private set; }

    /// <summary>Number of columns.</summary>
    public int Cols { get; private set; }

    /// <summary>The cell texts in row-major order.</summary>
    public IReadOnlyList<string> Cells => _cells;

    /// <summary>Size text such as "3 × 3".</summary>
    public string Dimensions => $"{Rows} × {Cols}";

    /// <summary>The text of one cell.</summary>
    public string this[int r, int c]
    {
        get => _cells[r * Cols + c];
        set => _cells[r * Cols + c] = value ?? "";
    }

    /// <summary>Changes the size, keeping the cells that still fit and filling new ones with 0.</summary>
    public void Resize(int rows, int cols)
    {
        rows = Math.Clamp(rows, 1, MatrixLibrary.MaxSize);
        cols = IsVector ? 1 : Math.Clamp(cols, 1, MatrixLibrary.MaxSize);
        if (rows == Rows && cols == Cols) return;
        var next = new string[rows * cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                next[r * cols + c] = r < Rows && c < Cols ? _cells[r * Cols + c] : "0";
        _cells = next;
        Rows = rows;
        Cols = cols;
    }

    /// <summary>Replaces size and content in one go.</summary>
    public void SetAll(int rows, int cols, IReadOnlyList<string> cells)
    {
        Rows = Math.Clamp(rows, 1, MatrixLibrary.MaxSize);
        Cols = IsVector ? 1 : Math.Clamp(cols, 1, MatrixLibrary.MaxSize);
        _cells = new string[Rows * Cols];
        for (int i = 0; i < _cells.Length; i++) _cells[i] = i < cells.Count ? cells[i] : "0";
    }

    /// <summary>The identity pattern (1 on the diagonal, 0 elsewhere).</summary>
    public void FillIdentity()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++) this[r, c] = r == c ? "1" : "0";
    }

    /// <summary>All zeros.</summary>
    public void FillZero() => Array.Fill(_cells, "0");

    /// <summary>Random whole numbers from −9 to 9.</summary>
    public void FillRandom(Random random)
    {
        for (int i = 0; i < _cells.Length; i++) _cells[i] = random.Next(-9, 10).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The named matrices of Matrix mode. Each one is also a variable in the calculator session, so Advanced mode,
/// the CLI and MCP tools can use it (A⁻¹·B, det(A), ...).
/// </summary>
public sealed class MatrixLibrary
{
    /// <summary>The largest number of rows or columns.</summary>
    public const int MaxSize = 12;

    private static readonly string[] MatrixNames = [.. "ABCDFGHJKLMNOPQRSTUVWXYZE".Select(c => c.ToString())];
    private static readonly string[] VectorNames = ["u", "v", "w", "p", "q", "r", "s"];
    private static readonly HashSet<string> Reserved = ["pi", "π", "e", "i", "inf", "infinity", "true", "false", "ans", "undefined", "oo", "and", "or", "not", "xor", "mod", "I"];

    private readonly List<MatrixEntry> _entries = [];

    /// <summary>The entries in display order.</summary>
    public IReadOnlyList<MatrixEntry> Entries => _entries;

    /// <summary>Finds an entry by name.</summary>
    public MatrixEntry? Find(string name) => _entries.FirstOrDefault(e => e.Name == name);

    /// <summary>Adds an entry (replacing one with the same name).</summary>
    public MatrixEntry Add(MatrixEntry entry)
    {
        int i = _entries.FindIndex(e => e.Name == entry.Name);
        if (i >= 0) _entries[i] = entry;
        else _entries.Add(entry);
        return entry;
    }

    /// <summary>Removes an entry by name.</summary>
    public bool Remove(string name) => _entries.RemoveAll(e => e.Name == name) > 0;

    /// <summary>The next unused matrix name (A, B, C, ... skipping I, which means the identity).</summary>
    public string NextMatrixName(Func<string, bool>? taken = null) => NextName(MatrixNames, "M", taken);

    /// <summary>The next unused vector name (u, v, w, ...).</summary>
    public string NextVectorName(Func<string, bool>? taken = null) => NextName(VectorNames, "u", taken);

    private string NextName(string[] names, string prefix, Func<string, bool>? taken)
    {
        bool Used(string n) => Find(n) is not null || (taken?.Invoke(n) ?? false);
        foreach (var n in names)
            if (!Used(n)) return n;
        for (int k = 1; ; k++)
            if (!Used(prefix + k.ToString(CultureInfo.InvariantCulture))) return prefix + k.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The design's starting library: a 3×3 system matrix A, its right-hand side B and a 2×2 matrix C.</summary>
    public static MatrixLibrary CreateDefault()
    {
        var lib = new MatrixLibrary();
        lib.Add(new MatrixEntry("A", 3, 3, cells: ["2", "1", "-1", "-3", "-1", "2", "-2", "1", "2"]));
        lib.Add(new MatrixEntry("B", 3, 1, cells: ["8", "-11", "-3"]));
        lib.Add(new MatrixEntry("C", 2, 2, cells: ["1", "2", "3", "4"]));
        return lib;
    }

    /// <summary>
    /// True when a name cannot hold a matrix in this session: multi-letter built-in functions (det, inv) stay functions,
    /// while single letters such as C or P may be stored over (a stored value wins over the built-in alias).
    /// </summary>
    public static bool IsBuiltInName(string name, CalcSession session) => name.Length > 1 && session.Functions.Contains(name);

    /// <summary>
    /// Checks a name for STORE AS and new matrices: it must start with a letter, contain only letters, digits and _, and not be a
    /// constant or a multi-letter built-in function (single letters such as C are fine). I is kept for the identity matrix.
    /// </summary>
    public static bool IsValidName(string name, CalcSession session, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(name)) { error = "Type a name, for example D"; return false; }
        if (!char.IsLetter(name[0])) { error = "A name starts with a letter"; return false; }
        if (!name.All(c => char.IsLetterOrDigit(c) || c == '_')) { error = "Use only letters, digits and _ in a name"; return false; }
        if (name.Length > 16) { error = "Keep names to 16 characters"; return false; }
        if (Reserved.Contains(name)) { error = name == "I" ? "I means the identity matrix; pick another name" : $"{name} is a built-in name; pick another"; return false; }
        if (IsBuiltInName(name, session)) { error = $"{name} is a built-in function; pick another name"; return false; }
        return true;
    }

    // ------------------------------------------------------------------ evaluation

    /// <summary>Parses and evaluates one cell (empty means 0). Cells must be single numbers or expressions.</summary>
    public static CellValue EvaluateCell(string text, CalcSession session, CancellationToken ct = default)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return new CellValue(text ?? "", Expr.Zero, null);
        try
        {
            var value = session.Evaluate(session.ParseExpression(t), approximate: false, ct);
            string? problem = value switch
            {
                MatrixExpr => "A cell holds one value, not a matrix",
                ListExpr or TupleExpr => "A cell holds one value, not a list",
                StringExpr or BoolExpr or RelationExpr or LogicExpr or LambdaExpr => "A cell needs a number or an expression",
                _ => null,
            };
            return problem is null ? new CellValue(text!, value, null) : new CellValue(text!, null, problem);
        }
        catch (CalcException ex) { return new CellValue(text!, null, ex.Message); }
        catch (DivideByZeroException) { return new CellValue(text!, null, "Division by zero"); }
        catch (OverflowException) { return new CellValue(text!, null, "The number is too large"); }
        catch (InsufficientExecutionStackException) { return new CellValue(text!, null, "The expression is nested too deeply"); }
    }

    /// <summary>Evaluates every cell; the matrix is null when any cell has an error.</summary>
    public static (MatrixExpr? Matrix, CellValue[] Cells) Evaluate(MatrixEntry entry, CalcSession session, CancellationToken ct = default)
    {
        var cells = entry.Cells.Select(t => EvaluateCell(t, session, ct)).ToArray();
        if (cells.Any(c => !c.IsValid)) return (null, cells);
        return (new MatrixExpr(entry.Rows, entry.Cols, [.. cells.Select(c => c.Value!)]), cells);
    }

    /// <summary>Writes the entry into the session as a variable. Returns false (and writes nothing) when a cell has an error.</summary>
    public static bool PushToSession(MatrixEntry entry, CalcSession session)
    {
        var (m, _) = Evaluate(entry, session);
        if (m is null) return false;
        if (session.Env.TryGetVariable(entry.Name, out var existing) && existing.Equals(m)) return true;
        session.Env.SetVariable(entry.Name, m);
        return true;
    }

    /// <summary>Cell texts for a matrix value (Calcy input syntax, so they parse back to the same value).</summary>
    public static string[] CellTexts(MatrixExpr m) => [.. m.Items.Select(ExprPrinter.ToInput)];

    // ------------------------------------------------------------------ persistence

    /// <summary>A serialisable copy of the library.</summary>
    public List<MatrixEntryState> ToState() =>
        [.. _entries.Select(e => new MatrixEntryState { Name = e.Name, Rows = e.Rows, Cols = e.Cols, IsVector = e.IsVector, Cells = [.. e.Cells] })];

    /// <summary>Rebuilds a library from saved state (invalid entries are skipped).</summary>
    public static MatrixLibrary FromState(IEnumerable<MatrixEntryState>? state)
    {
        var lib = new MatrixLibrary();
        if (state is null) return lib;
        foreach (var s in state)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || lib.Find(s.Name) is not null) continue;
            lib.Add(new MatrixEntry(s.Name, s.Rows, s.Cols, s.IsVector, s.Cells ?? []));
        }
        return lib;
    }
}

/// <summary>Saved form of a <see cref="MatrixEntry"/>.</summary>
public sealed class MatrixEntryState
{
    /// <summary>Variable name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Rows.</summary>
    public int Rows { get; set; } = 1;

    /// <summary>Columns.</summary>
    public int Cols { get; set; } = 1;

    /// <summary>Single-column vector.</summary>
    public bool IsVector { get; set; }

    /// <summary>Cell texts, row by row.</summary>
    public List<string> Cells { get; set; } = [];
}
