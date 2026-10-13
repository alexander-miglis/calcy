using System.Globalization;
using System.Text.RegularExpressions;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>What a list cell holds.</summary>
public enum CellKind
{
    /// <summary>Nothing typed.</summary>
    Empty,

    /// <summary>A real number (typed, or an expression such as 1/3 or sqrt(2)).</summary>
    Number,

    /// <summary>Text that is not a number: a category such as "red", or a typo.</summary>
    Text,
}

/// <summary>The parsed value of one list cell.</summary>
/// <param name="Kind">Empty, number or text.</param>
/// <param name="Number">The value of a number cell (NaN otherwise).</param>
/// <param name="Exact">The exact value for the session list (a fraction for 0.1, √2 for sqrt(2)); null unless a number.</param>
/// <param name="Text">The trimmed text as typed.</param>
public readonly record struct CellValue(CellKind Kind, double Number, Expr? Exact, string Text)
{
    /// <summary>An empty cell.</summary>
    public static readonly CellValue Empty = new(CellKind.Empty, double.NaN, null, "");

    /// <summary>True for number cells.</summary>
    public bool IsNumber => Kind == CellKind.Number;
}

/// <summary>
/// Reads list cells: plain numbers (1.5, -2, 3e-4, also the decimal comma 1,5 and 1.234,5 that spreadsheets in many
/// countries copy), fractions and calculator expressions (1/3, sqrt(2), pi/4, mean(L1)) evaluated with the session, and
/// anything else as text (a category).
/// </summary>
public static partial class CellParser
{
    private static readonly TimeSpan ExpressionBudget = TimeSpan.FromMilliseconds(250);

    /// <summary>Parses one cell. Expressions need a session; without one they count as text.</summary>
    public static CellValue Parse(string? text, CalcSession? session = null)
    {
        var t = Normalize(text);
        if (t.Length == 0) return CellValue.Empty;
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') return new CellValue(CellKind.Text, double.NaN, null, t[1..^1]);
        if (TryPlainNumber(t, out var decimalText))
        {
            var q = BigRational.ParseDecimal(decimalText);
            double d = double.Parse(decimalText, NumberStyles.Float, CultureInfo.InvariantCulture);
            return new CellValue(CellKind.Number, d, new NumberExpr(Calcy.Core.Numbers.Number.Exact(q)), t);
        }
        if (session is not null && LooksLikeExpression(t) && TryExpression(t, session, out var value, out var exact))
            return new CellValue(CellKind.Number, value, exact, t);
        return new CellValue(CellKind.Text, double.NaN, null, t);
    }

    /// <summary>
    /// True when <paramref name="text"/> is a plain decimal number (with an optional exponent, decimal comma or
    /// thousands dots), giving its invariant spelling such as "-1234.5".
    /// </summary>
    public static bool TryPlainNumber(string text, out string invariant)
    {
        var t = Normalize(text);
        invariant = "";
        if (PlainNumber().IsMatch(t))
        {
            invariant = t.StartsWith('+') ? t[1..] : t;
            if (invariant.StartsWith('.')) invariant = "0" + invariant;
            if (invariant.StartsWith("-.", StringComparison.Ordinal)) invariant = "-0" + invariant[1..];
            return true;
        }
        if (DecimalComma().IsMatch(t))
        {
            invariant = t.TrimStart('+').Replace(',', '.');
            return true;
        }
        if (GroupedDecimalComma().IsMatch(t))
        {
            invariant = t.TrimStart('+').Replace(".", "").Replace(',', '.');
            return true;
        }
        return false;
    }

    /// <summary>A plain number parsed to a double, or null.</summary>
    public static double? PlainNumberValue(string? text) =>
        TryPlainNumber(text ?? "", out var inv) ? double.Parse(inv, NumberStyles.Float, CultureInfo.InvariantCulture) : null;

    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Trim();
        // The typographic minus and non-breaking spaces come with copies from documents and some spreadsheets.
        if (t.Contains('−')) t = t.Replace('−', '-');
        if (t.Contains('\u00A0') || t.Contains('\u202F')) t = t.Replace("\u00A0", "").Replace("\u202F", "");
        return t;
    }

    /// <summary>Words like "red" are categories, not products of variables; only maths-looking text is evaluated.</summary>
    private static bool LooksLikeExpression(string t)
    {
        foreach (var ch in t)
            if (char.IsDigit(ch) || ch is '(' or '/' or '*' or '^' or '+' or '-' or '√' or 'π') return true;
        return t is "pi" or "e" or "π";
    }

    private static bool TryExpression(string t, CalcSession session, out double value, out Expr? exact)
    {
        value = double.NaN;
        exact = null;
        try
        {
            using var cts = new CancellationTokenSource(ExpressionBudget);
            var e = session.EvaluateExpression(t, approximate: false, ct: cts.Token);
            var n = Evaluator.Numeric(e, session.Context(true, cts.Token));
            if (n is not { IsReal: true } num || !double.IsFinite(num.Re)) return false;
            value = num.Re;
            exact = e is NumberExpr || e.IsConstantValue() ? e : new NumberExpr(Calcy.Core.Numbers.Number.Approx(value));
            return true;
        }
        catch (Exception ex) when (ex is CalcException or OperationCanceledException or ArithmeticException or InvalidOperationException or InsufficientExecutionStackException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$")]
    private static partial Regex PlainNumber();

    [GeneratedRegex(@"^[+-]?\d+,\d+$")]
    private static partial Regex DecimalComma();

    [GeneratedRegex(@"^[+-]?\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex GroupedDecimalComma();
}
