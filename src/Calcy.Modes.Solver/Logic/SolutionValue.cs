using Calcy.Core.Expressions;

namespace Calcy.Modes.Solver.Logic;

/// <summary>
/// One value from a solution, ready to show: the exact form (complex values as a + bi), the decimal form, and the
/// texts for the clipboard, LaTeX and the input of another mode.
/// </summary>
public sealed class SolutionValue
{
    /// <summary>The value as the engine gave it.</summary>
    public required Expr Exact { get; init; }

    /// <summary>The decimal form, or null when it would look the same as <see cref="Exact"/>.</summary>
    public Expr? Approx { get; init; }

    /// <summary>True for real values (and for values with a parameter, which are real for real parameters).</summary>
    public bool IsReal { get; init; } = true;

    /// <summary>True when the value is itself a decimal approximation (a numeric root).</summary>
    public bool IsNumeric { get; init; }

    /// <summary>The real part as a number (NaN when the value has a parameter).</summary>
    public double Re { get; init; } = double.NaN;

    /// <summary>The imaginary part as a number (NaN when the value has a parameter).</summary>
    public double Im { get; init; } = double.NaN;

    /// <summary>On-screen text, with a + bi for complex values: (5 − √13)/2, −1/2 − (√3/2)i.</summary>
    public required string Text { get; init; }

    /// <summary>The decimal form as text, or null.</summary>
    public string? ApproxText { get; init; }

    /// <summary>Linear Calcy input for the value (a + bi for exact complex values: -1/2 - (sqrt(3)/2)i).</summary>
    public required string Input { get; init; }

    /// <summary>LaTeX for the value.</summary>
    public required string Latex { get; init; }

    /// <summary>Parameters the value depends on (n in 2πn + π/6, t in a parametric solution).</summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];

    /// <summary>How many times the root repeats (polynomials).</summary>
    public int Multiplicity { get; set; } = 1;

    /// <summary>The check left − right at a numeric solution, as text (null for exact solutions).</summary>
    public string? Check { get; set; }

    /// <summary>True when the value has a decimal form worth showing next to the exact one.</summary>
    public bool HasApprox => ApproxText is not null && ApproxText != Text;
}
