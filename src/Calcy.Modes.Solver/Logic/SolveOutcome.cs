namespace Calcy.Modes.Solver.Logic;

/// <summary>What every tool's solve gives back: a headline, explanations, plot texts and the card, or an error.</summary>
public abstract class SolveOutcome
{
    /// <summary>The problem when the inputs could not be solved, or null.</summary>
    public SolverException? Error { get; init; }

    /// <summary>True when <see cref="Error"/> is set.</summary>
    public bool IsError => Error is not null;

    /// <summary>The headline in capitals: 2 SOLUTIONS, NO REAL SOLUTION, UNIQUE SOLUTION.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Explanations in words, shown under the result.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>What PLOT sends to Graphing (empty when there is nothing sensible to plot).</summary>
    public List<string> PlotTexts { get; set; } = [];

    /// <summary>The result card.</summary>
    public SolverCard Card { get; set; } = new();

    /// <summary>The card for an error: the message, EDIT, and FIX IT when a corrected input is known.</summary>
    internal static SolverCard ErrorCard(SolverTool tool, string input, SolverException ex, SolverSnapshot snapshot) => new()
    {
        Tool = tool,
        Title = "NEEDS A FIX",
        Input = input,
        Lines = [ex.Message],
        IsError = true,
        Hint = ex.Hint,
        Request = snapshot,
    };

    /// <summary>"1 SOLUTION", "3 SOLUTIONS".</summary>
    internal static string Count(int n, string word) => $"{n} {word}{(n == 1 ? "" : "S")}";
}
