namespace Calcy.Modes.Solver.Logic;

/// <summary>A problem with what the user typed (or with solving it), explained in plain words for an error card.</summary>
public sealed class SolverException : Exception
{
    /// <summary>Creates the error.</summary>
    /// <param name="message">What went wrong, in plain words.</param>
    /// <param name="field">Which input it is about ("equation 2", "coefficient of x²"), or null.</param>
    /// <param name="position">Where in that input (character index), or -1.</param>
    /// <param name="hint">A corrected input to offer with FIX IT, or null.</param>
    public SolverException(string message, string? field = null, int position = -1, string? hint = null) : base(message)
    {
        Field = field;
        Position = position;
        Hint = hint;
    }

    /// <summary>Which input the problem is in, or null.</summary>
    public string? Field { get; }

    /// <summary>The character position in that input, or -1.</summary>
    public int Position { get; }

    /// <summary>A corrected input for FIX IT, or null.</summary>
    public string? Hint { get; }
}
