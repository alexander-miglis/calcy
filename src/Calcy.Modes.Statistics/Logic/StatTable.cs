using System.Globalization;
using System.Text.RegularExpressions;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>
/// The lists of the Statistics screen. Every list is also a session variable (L1 = {1, 2, 3}), so Advanced mode, code
/// and MCP clients see the same data; lists defined elsewhere (L4 = seq(k², k, 1, 10)) come back in with
/// <see cref="PullFromSession"/>. Column formulas (L3 = L2 / L1) stay live: they are recomputed on every change.
/// </summary>
public sealed partial class StatTable
{
    /// <summary>The most lists the editor holds.</summary>
    public const int MaxColumns = 26;

    /// <summary>The longest list accepted (rows).</summary>
    public const int MaxRows = 100_000;

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "x", "y", "t", "r", "θ", "e", "i", "pi", "π", "ans", "inf", "true", "false", "undefined", "and", "or", "not", "none",
    };

    private readonly List<StatColumn> _columns = [];
    private readonly Dictionary<string, Expr> _published = new(StringComparer.Ordinal);

    /// <summary>Creates an empty table. With a session, cells may hold expressions and lists are shared as variables.</summary>
    public StatTable(CalcSession? session = null) => Session = session;

    /// <summary>The calculator session the lists are shared with (null in pure logic use).</summary>
    public CalcSession? Session { get; }

    /// <summary>The lists, left to right.</summary>
    public IReadOnlyList<StatColumn> Columns => _columns;

    /// <summary>Rows in use: the length of the longest list.</summary>
    public int RowCount => _columns.Count == 0 ? 0 : _columns.Max(c => c.Length);

    /// <summary>The list with this name, or null.</summary>
    public StatColumn? Find(string? name) => name is null ? null : _columns.FirstOrDefault(c => c.Name == name);

    /// <summary>Index of a list, or -1.</summary>
    public int IndexOf(string? name) => _columns.FindIndex(c => c.Name == name);

    // ------------------------------------------------------------------ columns

    /// <summary>The first free name L1, L2, ... (not a list and not used by the session for something else).</summary>
    public string NextName()
    {
        for (int k = 1; ; k++)
        {
            var n = "L" + k.ToString(CultureInfo.InvariantCulture);
            if (Find(n) is null && !TakenInSession(n)) return n;
        }
    }

    /// <summary>Adds an empty list (named <paramref name="name"/> or the next free L name) at the end or at an index.</summary>
    public StatColumn AddColumn(string? name = null, int? index = null)
    {
        if (_columns.Count >= MaxColumns) throw new InvalidOperationException($"The editor holds at most {MaxColumns} lists");
        name ??= NextName();
        if (Find(name) is not null) throw new InvalidOperationException($"There is already a list called {name}");
        var c = new StatColumn(name);
        if (index is int i && i >= 0 && i <= _columns.Count) _columns.Insert(i, c);
        else _columns.Add(c);
        return c;
    }

    /// <summary>Removes a list (and its session variable). Returns false when there is no such list.</summary>
    public bool RemoveColumn(string name)
    {
        var c = Find(name);
        if (c is null) return false;
        _columns.Remove(c);
        Unpublish(name);
        Recompute();
        return true;
    }

    /// <summary>Checks a new list name. Returns an error message, or null when the name can be used.</summary>
    public string? ValidateName(string? name, string? currentName = null)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) return "Type a name, for example L1 or hours";
        if (name == currentName) return null;
        if (!Identifier().IsMatch(name)) return "A list name starts with a letter and has only letters, digits and _";
        if (name.Length > 24) return "Use a name of at most 24 characters";
        if (Reserved.Contains(name)) return $"{name} is reserved by the calculator; pick another name";
        if (Find(name) is not null) return $"There is already a list called {name}";
        if (Session is { } s)
        {
            if (s.Functions.Contains(name)) return $"{name} is a built-in function; pick another name";
            if (s.Env.TryGetFunction(name, out _)) return $"{name} is a function in this session; pick another name";
            if (s.Env.TryGetVariable(name, out var v) && v is not ListExpr && !_published.ContainsKey(name))
                return $"{name} is already a variable in this session; pick another name";
        }
        return null;
    }

    /// <summary>Renames a list (and its session variable) and sets its caption. Returns an error message or null.</summary>
    public string? Rename(string oldName, string newName, string? caption = null)
    {
        var c = Find(oldName);
        if (c is null) return $"There is no list called {oldName}";
        newName = newName.Trim();
        var error = ValidateName(newName, oldName);
        if (error is not null) return error;
        if (caption is not null) c.Caption = caption.Trim();
        if (newName == oldName) return null;
        Unpublish(oldName);
        c.Name = newName;
        // Formulas that read the old name follow the rename.
        foreach (var other in _columns)
            if (other.Formula is { } f) other.Formula = RenameSymbol(f, oldName, newName);
        Recompute();
        return null;
    }

    /// <summary>Moves a list to another position.</summary>
    public void Move(string name, int index)
    {
        var c = Find(name);
        if (c is null) return;
        _columns.Remove(c);
        _columns.Insert(Math.Clamp(index, 0, _columns.Count), c);
    }

    // ------------------------------------------------------------------ cells

    /// <summary>Sets one cell. Typing into a formula list turns it back into typed data. Returns true when something changed.</summary>
    public bool SetCell(string column, int row, string? text)
    {
        var c = Find(column) ?? throw new ArgumentException($"There is no list called {column}", nameof(column));
        if (row >= MaxRows) return false;
        if (c.Formula is not null)
        {
            if ((text ?? "") == c[row]) return false;
            c.Formula = null;
            c.FormulaError = null;
        }
        if (!c.Set(row, text)) return false;
        Recompute(c);
        return true;
    }

    /// <summary>Replaces all cells of a list (drops a formula).</summary>
    public void SetCells(string column, IEnumerable<string> cells)
    {
        var c = Find(column) ?? throw new ArgumentException($"There is no list called {column}", nameof(column));
        c.Formula = null;
        c.FormulaError = null;
        c.SetAll(cells.Take(MaxRows));
        Recompute(c);
    }

    /// <summary>Replaces all cells of a list with numbers.</summary>
    public void SetNumbers(string column, IEnumerable<double> values) => SetCells(column, values.Select(NumberText.Cell));

    /// <summary>Empties a list and removes its formula.</summary>
    public void Clear(string column) => SetCells(column, []);

    /// <summary>Inserts an empty row at <paramref name="row"/> in every typed list.</summary>
    public void InsertRow(int row)
    {
        foreach (var c in _columns.Where(c => c.Formula is null)) c.Insert(row);
        Recompute();
    }

    /// <summary>Deletes row <paramref name="row"/> from every typed list.</summary>
    public void DeleteRow(int row)
    {
        foreach (var c in _columns.Where(c => c.Formula is null)) c.RemoveAt(row);
        Recompute();
    }

    /// <summary>
    /// Sorts the rows by one list (numbers first in order, then text, empty cells last). Every typed list is reordered the
    /// same way, so pairs (x, y) stay together; formula lists are recomputed.
    /// </summary>
    public void SortRows(string key, bool descending = false)
    {
        var k = Find(key) ?? throw new ArgumentException($"There is no list called {key}", nameof(key));
        int rows = _columns.Where(c => c.Formula is null || c == k).Select(c => c.Length).DefaultIfEmpty(0).Max();
        var values = Enumerable.Range(0, rows).Select(r => k.Value(r, Session)).ToArray();
        var order = Enumerable.Range(0, rows).ToArray();
        int Rank(CellValue v) => v.Kind switch { CellKind.Number => 0, CellKind.Text => 1, _ => 2 };
        int Compare(int a, int b)
        {
            var va = values[a];
            var vb = values[b];
            int ra = Rank(va), rb = Rank(vb);
            if (ra != rb) return ra.CompareTo(rb);
            int c = ra switch
            {
                0 => va.Number.CompareTo(vb.Number),
                1 => string.CompareOrdinal(va.Text, vb.Text),
                _ => 0,
            };
            if (descending && ra != 2) c = -c;
            return c != 0 ? c : a.CompareTo(b);
        }
        Array.Sort(order, Compare);
        foreach (var c in _columns.Where(c => c.Formula is null))
        {
            var old = Enumerable.Range(0, rows).Select(r => c[r]).ToArray();
            c.SetAll(order.Select(i => old[i]));
        }
        Recompute();
    }

    /// <summary>
    /// Pastes a table with its top-left cell at (<paramref name="column"/>, <paramref name="row"/>), adding lists to the
    /// right when needed. A header row becomes the captions. Returns the names of the lists that were filled.
    /// </summary>
    public IReadOnlyList<string> Paste(PastedTable table, int column, int row)
    {
        var filled = new List<string>();
        column = Math.Max(0, column);
        row = Math.Max(0, row);
        for (int j = 0; j < table.ColumnCount; j++)
        {
            int index = column + j;
            if (index >= MaxColumns) break;
            while (_columns.Count <= index) AddColumn();
            var c = _columns[index];
            c.Formula = null;
            c.FormulaError = null;
            var cells = table.Column(j);
            if (row == 0 && c.Length <= cells.Count) c.SetAll(cells.Take(MaxRows));
            else
                for (int i = 0; i < cells.Count && row + i < MaxRows; i++) c.Set(row + i, cells[i]);
            if (table.Header is { } header && j < header.Count && header[j].Trim().Length > 0 && row == 0)
                c.Caption = header[j].Trim();
            filled.Add(c.Name);
        }
        Recompute();
        return filled;
    }

    // ------------------------------------------------------------------ formulas

    /// <summary>
    /// Applies a column formula such as "L3 = L2 / L1" or "L4 = seq(k^2, k, 1, 10)". The target list is created when it
    /// does not exist. Without "name =" the formula goes to <paramref name="defaultTarget"/>. Returns an error or null.
    /// </summary>
    public string? ApplyFormula(string text, string? defaultTarget = null)
    {
        if (!ColumnFormula.TryParse(text, out var target, out var rhs))
        {
            if (defaultTarget is null || string.IsNullOrWhiteSpace(text)) return "Type a formula such as L3 = L1^2";
            target = defaultTarget;
            rhs = text.Trim();
        }
        if (Session is null) return "Column formulas need the calculator session";
        var column = Find(target);
        if (column is null)
        {
            var error = ValidateName(target);
            if (error is not null) return error;
            if (_columns.Count >= MaxColumns) return $"The editor holds at most {MaxColumns} lists";
        }
        Expr parsed;
        try
        {
            parsed = Session.ParseExpression(rhs);
        }
        catch (CalcException ex)
        {
            return ex.Message;
        }
        if (parsed.ContainsSymbol(target)) return $"{target} cannot be computed from itself";
        column ??= AddColumn(target);
        column.Formula = rhs;
        Recompute();
        return column.FormulaError;
    }

    /// <summary>Turns a formula list into typed data (the values stay, the formula goes).</summary>
    public void Unlink(string column)
    {
        var c = Find(column);
        if (c is null) return;
        c.Formula = null;
        c.FormulaError = null;
    }

    private void Recompute(StatColumn? changed = null)
    {
        if (changed is not null) Publish(changed);
        else foreach (var c in _columns.Where(c => c.Formula is null)) Publish(c);
        var formulas = _columns.Where(c => c.Formula is not null).ToList();
        if (formulas.Count == 0) return;
        // Formulas may read other formula lists; a few passes settle any order, a cycle shows up as an error.
        for (int pass = 0; pass <= formulas.Count; pass++)
        {
            bool any = false;
            foreach (var f in formulas) any |= Evaluate(f);
            if (!any) return;
            if (pass == formulas.Count)
                foreach (var f in formulas) f.FormulaError ??= "These formulas depend on each other in a circle";
        }
    }

    /// <summary>Evaluates one formula list. Returns true when its values changed.</summary>
    private bool Evaluate(StatColumn c)
    {
        if (Session is null || c.Formula is null) return false;
        string? error = null;
        List<string> cells = [];
        Expr? list = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var value = Session.EvaluateExpression(c.Formula, approximate: false, ct: cts.Token);
            if (value is ListExpr l)
            {
                if (l.Count > MaxRows) error = $"The formula gives more than {MaxRows:N0} values";
                else
                {
                    list = l;
                    cells = [.. l.Items.Select(i => CellText(i, Session))];
                }
            }
            else if (value is NumberExpr)
                error = "The formula gives one number, not a list: use lists, for example L1 * 2 or seq(k, k, 1, 10)";
            else
            {
                var missing = value.FreeSymbols().FirstOrDefault();
                error = missing is not null ? $"{missing} is not a list or variable yet" : "The formula does not give a list";
            }
        }
        catch (CalcException ex)
        {
            error = ex.Message;
        }
        catch (OperationCanceledException)
        {
            error = "The formula took too long";
        }
        catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or InsufficientExecutionStackException)
        {
            error = ex.Message;
        }
        while (cells.Count > 0 && cells[^1].Trim().Length == 0) cells.RemoveAt(cells.Count - 1);
        bool changed = error != c.FormulaError || !cells.SequenceEqual(c.Cells);
        c.FormulaError = error;
        if (error is not null) cells = [];
        if (!cells.SequenceEqual(c.Cells)) c.SetAll(cells);
        if (list is not null && error is null) PublishValue(c.Name, list);
        else Publish(c);
        return changed;
    }

    /// <summary>A list item as cell text: exact decimals stay exact, other fractions keep their exact spelling.</summary>
    public static string CellText(Expr item, CalcSession? session = null)
    {
        switch (item)
        {
            case NumberExpr { Value: { IsReal: true } n }:
            {
                double d = n.Re;
                if (!n.IsExact) return NumberText.Input(d);
                var s = NumberText.Cell(d);
                try
                {
                    if (CellParser.TryPlainNumber(s, out var inv) && BigRational.ParseDecimal(inv) == n.ReQ) return s;
                }
                catch (FormatException)
                {
                    // Fall through to the exact spelling.
                }
                return ExprPrinter.ToInput(item);
            }
            case StringExpr str:
                return str.Value;
            case ConstantExpr { Constant: ConstantKind.Undefined }:
                return "";
            default:
                if (session is not null && Evaluator.Numeric(item, session.Context(true)) is { IsReal: true } v && double.IsFinite(v.Re))
                    return NumberText.Cell(v.Re);
                return ExprPrinter.ToInput(item);
        }
    }

    // ------------------------------------------------------------------ session

    /// <summary>The value a list has in the session: numbers exact where typed exactly, text as strings, gaps as undefined.</summary>
    public Expr ListValue(StatColumn c)
    {
        var items = new Expr[c.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var v = c.Value(i, Session);
            items[i] = v.Kind switch
            {
                CellKind.Number => v.Exact ?? new NumberExpr(Number.Approx(v.Number)),
                CellKind.Text => new StringExpr(v.Text),
                _ => ConstantExpr.Undefined,
            };
        }
        return new ListExpr([.. items]);
    }

    /// <summary>Writes a list into the session (removes the variable when the list is empty).</summary>
    private void Publish(StatColumn c)
    {
        if (c.IsEmpty)
        {
            Unpublish(c.Name);
            return;
        }
        PublishValue(c.Name, ListValue(c));
    }

    private void PublishValue(string name, Expr value)
    {
        if (Session is null) return;
        if (_published.TryGetValue(name, out var old) && old.Equals(value) &&
            Session.Env.TryGetVariable(name, out var current) && current.Equals(value)) return;
        _published[name] = value;
        Session.Env.SetVariable(name, value);
    }

    private void Unpublish(string name)
    {
        if (!_published.Remove(name) || Session is null) return;
        if (Session.Env.TryGetVariable(name, out var v) && v is ListExpr) Session.Env.Remove(name);
    }

    /// <summary>
    /// Replaces every list with saved ones without touching the session; follow with <see cref="PullFromSession"/>
    /// (lists already in the session are newer) and <see cref="PublishAll"/>.
    /// </summary>
    public void Restore(IEnumerable<ColumnState> columns)
    {
        _columns.Clear();
        foreach (var s in columns.Take(MaxColumns))
        {
            if (string.IsNullOrWhiteSpace(s.Name) || Find(s.Name) is not null) continue;
            var c = new StatColumn(s.Name, s.Cells.Take(MaxRows)) { Caption = s.Caption ?? "" };
            c.Formula = string.IsNullOrWhiteSpace(s.Formula) ? null : s.Formula;
            _columns.Add(c);
        }
    }

    /// <summary>The lists as saved state.</summary>
    public List<ColumnState> ToState() =>
        [.. _columns.Select(c => new ColumnState { Name = c.Name, Caption = c.Caption, Cells = c.Formula is null ? [.. c.Cells] : [], Formula = c.Formula })];

    /// <summary>Writes every list into the session (after a restore).</summary>
    public void PublishAll()
    {
        foreach (var c in _columns.Where(c => c.Formula is null)) Publish(c);
        Recompute();
    }

    /// <summary>
    /// Picks up lists changed elsewhere (Advanced, code, MCP): lists with the name of a column, and new lists named L1,
    /// L2, ... A list deleted elsewhere empties its column. Returns the names of the columns that changed.
    /// </summary>
    public IReadOnlyList<string> PullFromSession()
    {
        var changed = new List<string>();
        if (Session is null) return changed;
        var vars = Session.Env.Variables;
        foreach (var (name, value) in vars)
        {
            if (value is not ListExpr list) continue;
            if (_published.TryGetValue(name, out var known) && known.Equals(value)) continue;
            var column = Find(name);
            if (column is null)
            {
                if (!LName().IsMatch(name) || _columns.Count >= MaxColumns || list.Count == 0) continue;
                column = AddColumn(name, InsertionIndex(name));
            }
            column.Formula = null;
            column.FormulaError = null;
            column.SetAll(list.Items.Select(i => CellText(i, Session)).Take(MaxRows));
            _published[name] = value;
            changed.Add(name);
        }
        foreach (var name in _published.Keys.ToList())
        {
            if (vars.TryGetValue(name, out var v) && v is ListExpr) continue;
            _published.Remove(name);
            var column = Find(name);
            if (column is null || column.IsEmpty) continue;
            column.Formula = null;
            column.FormulaError = null;
            column.SetAll([]);
            changed.Add(name);
        }
        if (changed.Count > 0)
        {
            var formulas = _columns.Where(c => c.Formula is not null).ToList();
            foreach (var f in formulas)
                if (Evaluate(f)) changed.Add(f.Name);
        }
        return changed;
    }

    /// <summary>L4 goes after L3 and before L5 when those exist.</summary>
    private int InsertionIndex(string name)
    {
        int k = int.Parse(name[1..], CultureInfo.InvariantCulture);
        for (int i = 0; i < _columns.Count; i++)
        {
            var m = LName().Match(_columns[i].Name);
            if (m.Success && int.Parse(_columns[i].Name[1..], CultureInfo.InvariantCulture) > k) return i;
        }
        return _columns.Count;
    }

    private bool TakenInSession(string name)
    {
        if (Session is null) return false;
        if (Session.Functions.Contains(name) || Session.Env.TryGetFunction(name, out _)) return true;
        return Session.Env.TryGetVariable(name, out var v) && v is not ListExpr;
    }

    private static string RenameSymbol(string formula, string oldName, string newName) =>
        Regex.Replace(formula, $@"(?<![A-Za-z0-9_]){Regex.Escape(oldName)}(?![A-Za-z0-9_])", newName);

    [GeneratedRegex(@"^[A-Za-zα-ωΑ-Ω][A-Za-z0-9_α-ωΑ-Ω]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^L[1-9][0-9]?$")]
    private static partial Regex LName();
}
