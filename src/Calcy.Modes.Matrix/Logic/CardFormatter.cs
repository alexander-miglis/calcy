using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>Formats exact and decimal values for cards using the session's settings.</summary>
public sealed class CardFormatter(CalcSession session)
{
    /// <summary>Significant digits used for decimal matrix entries (keeps grids compact).</summary>
    public const int CellDigits = 6;

    private PrintOptions Scalar => new() { Unicode = true, Numbers = NumberFormatOptions.From(session.Settings) };

    private PrintOptions Cell => new()
    {
        Unicode = true,
        Numbers = NumberFormatOptions.From(session.Settings) with { Digits = Math.Min(session.Settings.Digits, CellDigits) },
    };

    /// <summary>Display text of a scalar result.</summary>
    public string Text(Expr e) => ExprPrinter.Print(e, Scalar);

    /// <summary>Display text of a matrix entry.</summary>
    public string CellText(Expr e) => ExprPrinter.Print(e, Cell);

    /// <summary>The decimal form of a scalar, or null when it would look the same as the exact form.</summary>
    public string? ApproxText(Expr e, CancellationToken ct = default)
    {
        var a = Approximate(e, ct);
        if (a is null) return null;
        var s = ExprPrinter.Print(a, Scalar);
        return s == Text(e) ? null : s;
    }

    /// <summary>Exact cells of a matrix.</summary>
    public string[][] Cells(MatrixExpr m) =>
        [.. Enumerable.Range(0, m.Rows).Select(r => Enumerable.Range(0, m.Cols).Select(c => CellText(m[r, c])).ToArray())];

    /// <summary>Decimal cells of a matrix (entries without a decimal form keep their exact text).</summary>
    public string[][] ApproxCells(MatrixExpr m, CancellationToken ct = default) =>
        [.. Enumerable.Range(0, m.Rows).Select(r => Enumerable.Range(0, m.Cols).Select(c =>
        {
            var a = Approximate(m[r, c], ct);
            return a is null ? CellText(m[r, c]) : ExprPrinter.Print(a, Cell);
        }).ToArray())];

    /// <summary>Cells of a column vector given as entries.</summary>
    public string[][] Column(IReadOnlyList<Expr> v) => [.. v.Select(x => new[] { CellText(x) })];

    /// <summary>Decimal cells of a column vector.</summary>
    public string[][] ApproxColumn(IReadOnlyList<Expr> v, CancellationToken ct = default) =>
        [.. v.Select(x => new[] { Approximate(x, ct) is { } a ? ExprPrinter.Print(a, Cell) : CellText(x) })];

    /// <summary>A matrix block with exact and decimal cells.</summary>
    public CardBlock Grid(string? label, MatrixExpr m, int augmentAt = -1, string? after = null, CancellationToken ct = default) =>
        CardBlock.Grid(label, Cells(m), ApproxCells(m, ct), augmentAt, after);

    /// <summary>A scalar line "prefix value" with its decimal alternative.</summary>
    public CardBlock Value(string prefix, Expr e, CancellationToken ct = default)
    {
        var approx = ApproxText(e, ct);
        return CardBlock.Line(prefix + Text(e), approx is null ? null : prefix.Replace("= ", "≈ ") + approx);
    }

    /// <summary>A number in the session's decimal format.</summary>
    public string Number(double x) => NumberFormatter.Format(x, Scalar.Numbers);

    /// <summary>A number in the compact cell format.</summary>
    public string CellNumber(double x) => NumberFormatter.Format(x, Cell.Numbers);

    private Expr? Approximate(Expr e, CancellationToken ct)
    {
        if (e is NumberExpr { Value.IsExact: false }) return null;
        try
        {
            return session.Approximate(e, ct);
        }
        catch (CalcException)
        {
            return null;
        }
    }
}
