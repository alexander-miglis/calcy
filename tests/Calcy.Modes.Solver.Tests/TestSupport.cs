using Calcy.Core;
using Calcy.Modes.Solver.Logic;

namespace Calcy.Modes.Solver.Tests;

/// <summary>Engines on fresh sessions with every module, like the app.</summary>
internal static class TestSupport
{
    public static SolverEngine E(CalcSettings? settings = null) => new(Calcy.Engine.CalcyEngine.CreateSession(settings ?? new CalcSettings()));

    public static SolverEngine Degrees() => E(new CalcSettings { Angle = AngleMode.Degree });

    public static void Close(double expected, double actual, double tol = 1e-9) =>
        Assert.True(Math.Abs(expected - actual) <= tol * Math.Max(1, Math.Abs(expected)), $"expected {expected}, got {actual}");

    /// <summary>Text with the typographic minus turned back into ASCII, for readable expectations.</summary>
    public static string A(string text) => MathText.Ascii(text);
}
