using Calcy.Core.Printing;
using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>Display and input texts for the physical constants.</summary>
public static class ConstantText
{
    private static readonly NumberFormatOptions Full = new() { Digits = 15 };

    /// <summary>The value at full precision with its unit: "6.62607015×10⁻³⁴ J·s".</summary>
    public static string Value(FormulaConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return SymbolText.WithUnit(SymbolText.Number(constant.Value, Full), constant.Unit);
    }

    /// <summary>"exact", "± 1.5×10⁻¹⁵ m³/(kg·s²)" or "typical value", describing how well the value is known.</summary>
    public static string Precision(FormulaConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        if (constant.Exact) return "exact";
        if (constant.Uncertainty is double u) return "± " + SymbolText.WithUnit(SymbolText.Number(u, Full), constant.Unit);
        return "typical value";
    }

    /// <summary>The tag shown next to the value: EXACT for defined values, TYPICAL for conventional ones, null when measured.</summary>
    public static string? Tag(FormulaConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        if (constant.Exact) return "EXACT";
        return constant.Uncertainty is null ? "TYPICAL" : null;
    }

    /// <summary>
    /// The text USE inserts or sends: a Calcy expression with the value ("6.62607015e-34"), or the built-in name for
    /// π and e so the answer stays exact.
    /// </summary>
    public static string Use(FormulaConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return constant.Symbol is "pi" or "e" ? constant.Symbol : SymbolText.Literal(constant.Value);
    }

    /// <summary>"c = 299792458 m/s (exact)" for COPY, tooltips and screen readers.</summary>
    public static string Describe(FormulaConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return $"{SymbolText.Plain(constant.Symbol)} = {Value(constant)} ({Precision(constant)})";
    }
}
