namespace Calcy.Core;

/// <summary>A user-facing calculation error with a plain-language message and an optional source position.</summary>
public class CalcException : Exception
{
    /// <summary>Character offset in the input where the problem is, or -1 when unknown.</summary>
    public int Position { get; }

    /// <summary>Length of the offending input span (0 when unknown).</summary>
    public int Length { get; }

    /// <summary>Optional suggestion shown to the user, for example a corrected input.</summary>
    public string? Hint { get; init; }

    public CalcException(string message, int position = -1, int length = 0) : base(message)
    {
        Position = position;
        Length = length;
    }
}

/// <summary>Raised when the input cannot be parsed.</summary>
public sealed class ParseException(string message, int position, int length = 1) : CalcException(message, position, length);
