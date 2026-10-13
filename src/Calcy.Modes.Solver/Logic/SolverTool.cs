namespace Calcy.Modes.Solver.Logic;

/// <summary>The tools of the Solver screen, in tab order (the design's order: polynomial first).</summary>
public enum SolverTool
{
    /// <summary>A polynomial of degree 2 to 6 from its coefficients (TI PlySmlt2, Casio EQUA polynomial).</summary>
    Polynomial,

    /// <summary>Simultaneous linear equations as a coefficient grid or typed equations.</summary>
    LinearSystem,

    /// <summary>Typed nonlinear equations in several unknowns, with optional starting guesses.</summary>
    NonlinearSystem,

    /// <summary>One equation in one unknown, solved exactly or numerically in a range.</summary>
    Equation,

    /// <summary>One inequality in one unknown, shown as intervals and on a number line.</summary>
    Inequality,
}

/// <summary>Labels and card titles of the tools.</summary>
public static class SolverTools
{
    /// <summary>Every tool in tab order.</summary>
    public static IReadOnlyList<SolverTool> All { get; } = Enum.GetValues<SolverTool>();

    /// <summary>The tab label (upper case, as the pixel font draws it).</summary>
    public static string Label(SolverTool tool) => tool switch
    {
        SolverTool.Polynomial => "POLYNOMIAL",
        SolverTool.LinearSystem => "LINEAR SYSTEM",
        SolverTool.NonlinearSystem => "NONLINEAR SYSTEM",
        SolverTool.Equation => "ANY EQUATION",
        SolverTool.Inequality => "INEQUALITY",
        _ => tool.ToString().ToUpperInvariant(),
    };

    /// <summary>A short lower-case name for status messages and screen readers.</summary>
    public static string Name(SolverTool tool) => tool switch
    {
        SolverTool.Polynomial => "polynomial",
        SolverTool.LinearSystem => "linear system",
        SolverTool.NonlinearSystem => "nonlinear system",
        SolverTool.Equation => "equation",
        SolverTool.Inequality => "inequality",
        _ => tool.ToString().ToLowerInvariant(),
    };
}
