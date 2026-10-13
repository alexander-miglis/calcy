namespace Calcy.Modes.Matrix.Logic;

/// <summary>Everything Matrix mode remembers between sessions (stored with Preferences.SetModeState("matrix", ...)).</summary>
public sealed class MatrixModeState
{
    /// <summary>Format version, for future changes.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The matrix library.</summary>
    public List<MatrixEntryState> Matrices { get; set; } = [];

    /// <summary>The selected matrix.</summary>
    public string? Selected { get; set; }

    /// <summary>The second operand (Y) of binary operations.</summary>
    public string? Operand { get; set; }

    /// <summary>The scalar k.</summary>
    public string K { get; set; } = "2";

    /// <summary>The power n.</summary>
    public string N { get; set; } = "2";

    /// <summary>The text in the expression line.</summary>
    public string? Expression { get; set; }

    /// <summary>Result cards, newest first.</summary>
    public List<MatrixCard> Cards { get; set; } = [];

    /// <summary>The number the next card gets.</summary>
    public int NextCardNumber { get; set; } = 1;

    /// <summary>The most cards kept.</summary>
    public const int MaxCards = 100;
}
