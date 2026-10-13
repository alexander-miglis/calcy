using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Formulas;
using Calcy.Modes.Formulas.Logic;

namespace Calcy.Modes.Formulas.Tests;

/// <summary>Shared helpers for the logic tests: a real calculator session and evaluation as Advanced mode does it.</summary>
internal static class TestSupport
{
    public static FormulaLibrary Library => FormulaLibrary.Default;

    public static CalcSession Session() => Calcy.Engine.CalcyEngine.CreateSession(new CalcSettings());

    public static FormulaCalculator Calculator(CalcSession? session = null) => new(session ?? Session(), Library);

    public static Formula Formula(string id) => Library.Get(id) ?? throw new InvalidOperationException($"No formula {id}");

    public static Dictionary<string, string> Texts(params (string Symbol, string Text)[] values) =>
        values.ToDictionary(v => v.Symbol, v => v.Text, StringComparer.Ordinal);

    /// <summary>Evaluates a Calcy expression to a real number in a fresh session, as Advanced mode would.</summary>
    public static double Eval(string text)
    {
        var e = Session().EvaluateExpression(text, approximate: true);
        return e switch
        {
            NumberExpr n when n.Value.IsReal => n.Value.Re,
            _ => throw new InvalidOperationException($"'{text}' did not give a number: {e}"),
        };
    }

    public static void Near(double expected, double actual, double relative = 1e-9)
    {
        double tol = relative * Math.Max(Math.Abs(expected), double.Epsilon);
        Assert.True(Math.Abs(expected - actual) <= Math.Max(tol, 1e-300), $"expected {expected:R}, got {actual:R}");
    }

    /// <summary>Solves and asserts there is an answer.</summary>
    public static FormulaAnswer Answer(FormulaOutcome outcome)
    {
        Assert.True(outcome.Kind == OutcomeKind.Answered, $"{outcome.Kind}: {outcome.Message}");
        return outcome.Answer!;
    }
}
