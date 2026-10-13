using System.Globalization;
using System.Text;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Graphing.Symbolic;

namespace Calcy.Graphing;

/// <summary>One table cell: the value, its display text, and whether the text is exact.</summary>
public sealed record TableCell(double Value, string Text, bool IsExact);

/// <summary>One table row: the input value (x or t) and a cell per column.</summary>
public sealed record TableRow(double Key, string KeyText, IReadOnlyList<TableCell> Cells);

/// <summary>A table of values linked to graphs (TI TABLE, Casio TABLE).</summary>
public sealed class GraphTable
{
    /// <summary>Header of the input column ("x", "t" or "θ").</summary>
    public required string KeyHeader { get; init; }

    /// <summary>Column headers, for example "y1".</summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>The expression of each column, for a second header line.</summary>
    public IReadOnlyList<string> Formulas { get; init; } = [];

    /// <summary>Colour slot of each column.</summary>
    public IReadOnlyList<int> ColorIndexes { get; init; } = [];

    /// <summary>The rows.</summary>
    public required IReadOnlyList<TableRow> Rows { get; init; }

    /// <summary>Comma-separated values with a header line (decimals, invariant culture).</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.Append(Csv(KeyHeader));
        foreach (var h in Headers) sb.Append(',').Append(Csv(h));
        sb.Append('\n');
        foreach (var r in Rows)
        {
            sb.Append(r.Key.ToString("R", CultureInfo.InvariantCulture));
            foreach (var c in r.Cells) sb.Append(',').Append(double.IsFinite(c.Value) ? c.Value.ToString("R", CultureInfo.InvariantCulture) : "");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}

/// <summary>Builds value tables for function, parametric and polar items, with exact values when the session finds them.</summary>
public static class GraphTableBuilder
{
    /// <summary>
    /// A table of every visible function item (one column per family member) for x = start, start + step, ...
    /// With <paramref name="exact"/>, rational inputs give exact outputs (fractions, radicals) when they read well.
    /// </summary>
    public static GraphTable Functions(IEnumerable<GraphItem> items, ParameterSource parameters, double start, double step, int rows, bool exact, CalcSession? session)
    {
        rows = Math.Clamp(rows, 1, 10_000);
        var cols = new List<(GraphItem Item, GraphMember Member, double[] P)>();
        foreach (var item in items)
        {
            if (!item.IsVisible || !item.IsFunction) continue;
            var p = parameters(item);
            foreach (var m in item.Members) if (m.Main is not null) cols.Add((item, m, p));
        }
        var exactStart = ExactForms.ExactNumber(start);
        var exactStep = ExactForms.ExactNumber(step);
        var list = new List<TableRow>(rows);
        for (int i = 0; i < rows; i++)
        {
            double x = start + i * step;
            if (Math.Abs(x) < Math.Abs(step) * 1e-9) x = 0;
            Expr? ex = exact ? Algebra.Add(exactStart, Algebra.Mul(Expr.Int(i), exactStep)) : null;
            var cells = new List<TableCell>(cols.Count);
            foreach (var (item, m, p) in cols)
            {
                double y = m.Main!.Eval(x, p);
                cells.Add(Cell(y, exact && session is not null ? ExactAt(m.Main.Expression, item.Variable ?? "x", ex!, session, item, p) : null));
            }
            string keyText = ex is not null ? GraphFormat.Exact(Evaluate(ex, session) ?? ex) : GraphFormat.Number(x, 6, true);
            list.Add(new TableRow(x, keyText, cells));
        }
        return new GraphTable
        {
            KeyHeader = "x",
            Headers = [.. cols.Select(c => c.Item.Members.Count > 1 ? $"{c.Item.Name}[{c.Member.Index + 1}]" : c.Item.Name)],
            Formulas = [.. cols.Select(c => c.Item.Text.Trim())],
            ColorIndexes = [.. cols.Select(c => c.Item.ColorIndex)],
            Rows = list,
        };
    }

    /// <summary>A table of a parametric (x, y columns) or polar (r, x, y columns) item for t = start, start + step, ...</summary>
    public static GraphTable Parametric(GraphItem item, double[] p, double start, double step, int rows, int member = 0)
    {
        rows = Math.Clamp(rows, 1, 10_000);
        var m = item.Members[Math.Clamp(member, 0, item.Members.Count - 1)];
        var list = new List<TableRow>(rows);
        bool polar = item.Kind == GraphKind.Polar;
        for (int i = 0; i < rows; i++)
        {
            double t = start + i * step;
            if (Math.Abs(t) < Math.Abs(step) * 1e-9) t = 0;
            var cells = new List<TableCell>();
            if (polar)
            {
                double r = m.Main?.Eval(t, p) ?? double.NaN;
                double a = t * item.AngleToRadians;
                cells.Add(Cell(r, null));
                cells.Add(Cell(r * Math.Cos(a), null));
                cells.Add(Cell(r * Math.Sin(a), null));
            }
            else
            {
                cells.Add(Cell(m.X?.Eval(t, p) ?? double.NaN, null));
                cells.Add(Cell(m.Y?.Eval(t, p) ?? double.NaN, null));
            }
            list.Add(new TableRow(t, GraphFormat.Number(t, 6, true), cells));
        }
        return new GraphTable
        {
            KeyHeader = polar ? "θ" : "t",
            Headers = polar ? ["r", "x", "y"] : ["x", "y"],
            Formulas = [item.Text.Trim()],
            ColorIndexes = polar ? [item.ColorIndex, item.ColorIndex, item.ColorIndex] : [item.ColorIndex, item.ColorIndex],
            Rows = list,
        };
    }

    private static TableCell Cell(double v, Expr? exact)
    {
        if (exact is not null) return new TableCell(v, GraphFormat.Exact(exact), true);
        return new TableCell(v, double.IsFinite(v) ? GraphFormat.Number(v, 6, true) : "undefined", false);
    }

    private static Expr? Evaluate(Expr e, CalcSession? session)
    {
        if (session is null) return null;
        try
        {
            return Evaluator.Eval(e, session.Context());
        }
        catch (CalcException)
        {
            return null;
        }
    }

    private static Expr? ExactAt(Expr f, string variable, Expr x, CalcSession session, GraphItem item, double[] p)
    {
        var locals = new List<KeyValuePair<string, Expr>>();
        for (int i = 0; i < item.Parameters.Count && i < p.Length; i++) locals.Add(new(item.Parameters[i], ExactForms.ExactNumber(p[i])));
        return ExactForms.NiceValue(f, variable, x, session.Context(), locals);
    }
}
