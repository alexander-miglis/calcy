using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.Engine;
using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>Texts and expressions the formula page shows: the typeset equation and the worked example.</summary>
public static class FormulaTexts
{
    private static readonly NumberFormatOptions Reading = new() { Digits = 10 };

    /// <summary>
    /// The equation as the author wrote it (term order kept), parsed with every variable and constant symbol as one
    /// name, ready for a math view. Null when it does not parse.
    /// </summary>
    public static Expr? Equation(CalcSession session, Formula formula, FormulaLibrary? library = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(formula);
        library ??= FormulaLibrary.Default;
        try
        {
            var left = FormulaSolver.Parse(session, formula, formula.LeftSide, library);
            var right = FormulaSolver.Parse(session, formula, formula.RightSide, library);
            return new RelationExpr(RelOp.Equal, left, right);
        }
        catch (CalcException)
        {
            return null;
        }
    }

    /// <summary>The worked example's known values as "v = 25 m/s", in the formula's variable order.</summary>
    public static IReadOnlyList<string> ExampleGiven(Formula formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (formula.Example is not { } ex) return [];
        return [.. formula.Variables
            .Where(v => ex.Given.ContainsKey(v.Symbol))
            .Select(v => SymbolText.Equation(v.Symbol, SymbolText.Number(ex.Given[v.Symbol], Reading), v.Unit))];
    }

    /// <summary>The worked example's answer as "a = 2.5 m/s²" (or "both sides = 1" for an identity), or null.</summary>
    public static string? ExampleAnswer(Formula formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (formula.Example is not { } ex) return null;
        var number = SymbolText.Number(ex.Result, Reading);
        if (ex.SolveFor is null || formula.IsIdentity) return "both sides = " + SymbolText.WithUnit(number, ex.Unit);
        return SymbolText.Equation(ex.SolveFor, number, ex.Unit);
    }

    /// <summary>The box texts TRY EXAMPLE fills in, by symbol (values that read back exactly).</summary>
    public static IReadOnlyDictionary<string, string> ExampleInputs(Formula formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (formula.Example is not { } ex) return new Dictionary<string, string>();
        return formula.Variables
            .Where(v => ex.Given.ContainsKey(v.Symbol) && v.Symbol != ex.SolveFor)
            .ToDictionary(v => v.Symbol, v => InputReader.ExampleText(ex.Given[v.Symbol]), StringComparer.Ordinal);
    }

    /// <summary>"final velocity, in m/s. Velocity at time t. At least 0." for a variable's tooltip.</summary>
    public static string VariableTip(FormulaVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var parts = new List<string> { variable.Name + (variable.Unit.Length > 0 ? ", in " + variable.Unit : ", no unit") + "." };
        if (variable.Description.Length > 0) parts.Add(variable.Description);
        var limits = InputReader.LimitsText(variable);
        if (limits.Length > 0) parts.Add("Must be " + limits + ".");
        if (InputReader.Placeholder(variable) is { } usual) parts.Add($"Left empty, the usual value {usual} is used.");
        return string.Join(" ", parts);
    }
}
