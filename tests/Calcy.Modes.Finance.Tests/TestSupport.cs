using System.Globalization;
using Calcy.Core;
using Calcy.Modes.Finance.Logic;

namespace Calcy.Modes.Finance.Tests;

/// <summary>Shared helpers for the logic tests: a real calculator session and a fixed culture.</summary>
internal static class TestSupport
{
    public static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static CalcSession Session() => Calcy.Engine.CalcyEngine.CreateSession(new CalcSettings());

    public static ValueReader Reader(CalcSession? session = null) => new(session ?? Session(), Us);

    /// <summary>Evaluates a Calcy expression to a real number, as Advanced mode would.</summary>
    public static double Eval(CalcSession session, string text)
    {
        var e = session.EvaluateExpression(text, approximate: true);
        return e switch
        {
            Calcy.Core.Expressions.NumberExpr n => n.Value.Re,
            _ => throw new InvalidOperationException($"'{text}' did not give a number: {e}"),
        };
    }

    public static void Near(double expected, double actual, double relative = 1e-9)
    {
        double tol = relative * Math.Max(1, Math.Abs(expected));
        Assert.True(Math.Abs(expected - actual) <= tol, $"expected {expected:R}, got {actual:R}");
    }
}
