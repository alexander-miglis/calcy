using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Parsing;
using Calcy.Core.Printing;

namespace Calcy.Modes.Notebook.Model;

/// <summary>Shared rules for showing results (exact or decimal first) and for turning maths into LaTeX.</summary>
public static partial class ResultForms
{
    /// <summary>
    /// The form shown first, the other form (or null) and whether the first is the exact one. Follows the line's own
    /// choice when it has one, otherwise the settings (Casio style: exact first when it reads well).
    /// </summary>
    public static (Expr Primary, Expr? Secondary, bool PrimaryIsExact) Forms(CalcResult r, CalcSettings settings, bool? decimalFirst)
    {
        var exact = r.Exact ?? throw new ArgumentException("The result has no value.", nameof(r));
        if (r.Approx is not { } approx) return (exact, null, true);
        bool dec = decimalFirst ?? (settings.Results == ResultPreference.DecimalFirst || !r.ExactIsNice);
        return dec ? (approx, exact, false) : (exact, approx, true);
    }

    /// <summary>
    /// True when a line's result is worth showing next to it: not for function definitions, and not for a definition
    /// whose value is the number as typed (L = 0.8).
    /// </summary>
    public static bool ShowsResult(CalcResult r, string input)
    {
        if (r.IsError || r.Exact is null) return false;
        if (r.Kind == ResultKind.FunctionDefinition) return false;
        if (r.Kind == ResultKind.Assignment && r.Parsed is { } parsed && parsed.Equals(r.Exact))
            return r.Approx is not null && !new MathLine(input).CanSlide;
        return true;
    }

    /// <summary>
    /// The input of a line as LaTeX (definitions keep their left side); plain text when it does not parse. Decimal
    /// numbers stay as typed (0.8, not 4/5).
    /// </summary>
    public static string InputLatex(string input, CalcSession session)
    {
        var numbers = NumberFormatOptions.From(session.Settings, false);
        var literals = new List<string>();
        // Decimals parse to exact fractions; stand-in names keep them as written.
        var text = DecimalLiteral().Replace(input, m =>
        {
            literals.Add(m.Value);
            return $" {Placeholder}{literals.Count - 1}z ";
        });
        string tex;
        try
        {
            tex = session.Parse(text) switch
            {
                AssignStatement a => LatexPrinter.Name(a.Name) + " = " + LatexPrinter.ToLatex(a.Value, numbers),
                FunctionDefStatement f => LatexPrinter.Name(f.Name) + "(" + string.Join(", ", f.Parameters.Select(LatexPrinter.Name)) + ") = " + LatexPrinter.ToLatex(f.Body, numbers),
                ExprStatement e => LatexPrinter.ToLatex(e.Expression, numbers),
                _ => TextLatex(input),
            };
        }
        catch (CalcException)
        {
            return TextLatex(input);
        }
        for (int k = 0; k < literals.Count; k++) tex = tex.Replace(@"\mathrm{" + Placeholder + k + "z}", literals[k]);
        return tex.Contains(Placeholder, StringComparison.Ordinal) ? TextLatex(input) : tex;
    }

    private const string Placeholder = "zqdec";

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\p{L}\p{N}_.])(?:\d+\.\d+|\.\d+)(?:[eE][+-]?\d+)?")]
    private static partial System.Text.RegularExpressions.Regex DecimalLiteral();

    /// <summary>A value as LaTeX in the session's number format.</summary>
    public static string ValueLatex(Expr value, CalcSession session) => LatexPrinter.ToLatex(value, NumberFormatOptions.From(session.Settings, false));

    /// <summary>A whole maths line with its result as LaTeX: "T = 2\pi ... \approx 1.794".</summary>
    public static string LineLatex(string input, LineResult? result, bool? decimalFirst, CalcSession session)
    {
        var tex = InputLatex(input, session);
        if (result is null || !ShowsResult(result.Result, input)) return tex;
        var (primary, secondary, exactFirst) = Forms(result.Result, session.Settings, decimalFirst);
        tex += (exactFirst ? " = " : " \\approx ") + ValueLatex(primary, session);
        if (secondary is not null && exactFirst) tex += " \\approx " + ValueLatex(secondary, session);
        return tex;
    }

    /// <summary>A live value as plain display text: decimals for plain fractions (prose reads 0.4, not 2/5).</summary>
    public static Expr LiveDisplay(LiveValue v)
    {
        var value = v.Value ?? throw new ArgumentException("The value has no result.", nameof(v));
        if (v.Approx is { } a && value is NumberExpr { Value.IsExact: true }) return a;
        return value;
    }

    /// <summary>Plain text in LaTeX \text{...}.</summary>
    public static string TextLatex(string text) => "\\text{" + LatexEscape(text) + "}";

    /// <summary>Escapes LaTeX special characters in running text.</summary>
    public static string LatexEscape(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\\' => "\\textbackslash{}",
                '{' => "\\{",
                '}' => "\\}",
                '$' => "\\$",
                '&' => "\\&",
                '#' => "\\#",
                '^' => "\\^{}",
                '_' => "\\_",
                '%' => "\\%",
                '~' => "\\~{}",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }
}
