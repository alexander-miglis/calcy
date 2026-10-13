using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>How a variable's value was obtained.</summary>
public enum ValueSource
{
    /// <summary>Typed into its box.</summary>
    Typed,

    /// <summary>Left empty, so the usual value (for example g = 9.81) is used.</summary>
    Default,

    /// <summary>Left empty and there is no usual value.</summary>
    Missing,

    /// <summary>The box holds something that is not a usable number.</summary>
    Invalid,
}

/// <summary>The value read from one input box.</summary>
/// <param name="Variable">The variable.</param>
/// <param name="Text">The text as typed (trimmed).</param>
/// <param name="Source">Where the value comes from.</param>
/// <param name="Value">The evaluated value (exact when the input is exact), or null.</param>
/// <param name="Approx">The value as a double, or NaN.</param>
/// <param name="Error">Why the text cannot be used, or null.</param>
public sealed record VariableReading(FormulaVariable Variable, string Text, ValueSource Source, Expr? Value, double Approx, string? Error)
{
    /// <summary>True when a value is available (typed or the usual value).</summary>
    public bool HasValue => Source is ValueSource.Typed or ValueSource.Default;
}

/// <summary>
/// Reads the text of input boxes into values: any Calcy expression works (25, 2.5, 3e8, sqrt(2), 1/3, pi/4, 45°),
/// empty boxes fall back to the variable's usual value, and values outside a variable's limits (a negative time,
/// a fractional count) are reported in plain words.
/// </summary>
public static class InputReader
{
    /// <summary>Reads one box. Never throws for bad input; the reading carries the message instead.</summary>
    public static VariableReading Read(CalcSession session, FormulaVariable variable, string? text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(variable);
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return variable.DefaultValue is double d
                ? new VariableReading(variable, "", ValueSource.Default, new NumberExpr(Core.Numbers.Number.Approx(d)), d, null)
                : new VariableReading(variable, "", ValueSource.Missing, null, double.NaN, null);
        }

        Expr value;
        try
        {
            value = session.Evaluate(session.ParseExpression(trimmed), approximate: false, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CalcException ex)
        {
            return Invalid(variable, trimmed, ex.Message);
        }
        catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or ArgumentException or InsufficientExecutionStackException)
        {
            return Invalid(variable, trimmed, "This is not a number");
        }

        if (!value.IsConstantValue())
        {
            var free = value.FreeSymbols();
            return Invalid(variable, trimmed, free.Count > 0 ? $"{free[0]} has no value: type a number" : "This is not a number");
        }

        Core.Numbers.Number? n;
        try
        {
            n = Evaluator.Numeric(value, session.Context(true, ct));
        }
        catch (CalcException ex)
        {
            return Invalid(variable, trimmed, ex.Message);
        }
        if (n is not { } number) return Invalid(variable, trimmed, "This is not a number");
        if (!number.IsReal) return Invalid(variable, trimmed, "Use a real number here");
        double approx = number.Re;
        if (!double.IsFinite(approx)) return Invalid(variable, trimmed, "The number is too large");
        if (LimitProblem(variable, approx) is { } problem) return Invalid(variable, trimmed, problem);
        return new VariableReading(variable, trimmed, ValueSource.Typed, value, approx, null);
    }

    /// <summary>Why a value breaks a variable's limits ("t must be at least 0"), or null when it is fine.</summary>
    public static string? LimitProblem(FormulaVariable variable, double value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (variable.Accepts(value)) return null;
        var name = SymbolText.Plain(variable.Symbol);
        if (variable.Positive && value <= 0) return $"{name} must be more than 0";
        if (variable.Integer && Math.Abs(value - Math.Round(value)) > 1e-9) return $"{name} must be a whole number";
        if (variable.Min is double min && value < min) return $"{name} must be at least {Plain(min)}";
        if (variable.Max is double max && value > max) return $"{name} must be at most {Plain(max)}";
        return $"{name} cannot be {Plain(value)}";
    }

    /// <summary>The limits of a variable in words ("at least 0", "more than 0, whole number"), or an empty string.</summary>
    public static string LimitsText(FormulaVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var parts = new List<string>();
        if (variable.Positive) parts.Add("more than 0");
        if (variable.Min is double min && !(variable.Positive && min <= 0)) parts.Add("at least " + Plain(min));
        if (variable.Max is double max) parts.Add("at most " + Plain(max));
        if (variable.Integer) parts.Add("a whole number");
        return string.Join(", ", parts);
    }

    private static string Plain(double value) => SymbolText.Number(value, new Core.Printing.NumberFormatOptions { Digits = 10 });

    private static VariableReading Invalid(FormulaVariable variable, string text, string message) =>
        new(variable, text, ValueSource.Invalid, null, double.NaN, message);

    /// <summary>A worked-example value as box text that reads back exactly ("25", "5.5e14", "0.7853981633974483").</summary>
    public static string ExampleText(double value) => SymbolText.Literal(value);

    /// <summary>The usual value of a variable as placeholder text ("9.81"), or null.</summary>
    public static string? Placeholder(FormulaVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return variable.DefaultValue is double d ? SymbolText.Literal(d) : null;
    }
}
