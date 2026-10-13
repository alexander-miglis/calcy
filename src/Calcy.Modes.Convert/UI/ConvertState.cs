using Calcy.Units;

namespace Calcy.Modes.Convert;

/// <summary>A saved conversion card.</summary>
/// <param name="Category">Category name shown in the card header ("Temperature", "Dates").</param>
/// <param name="Text">The card body, for example "100 °C = 212 °F".</param>
/// <param name="Query">Text that reproduces the card when used again ("100 °C to °F", "2026-10-13 + 100 days").</param>
public sealed record ConvertCard(string Category, string Text, string? Query);

/// <summary>What Convert mode remembers between sessions (stored in the preferences file under the mode id).</summary>
public sealed class ConvertState
{
    /// <summary>Category id, or "dates".</summary>
    public string Category { get; set; } = "length";

    /// <summary>The From value as typed.</summary>
    public string Value { get; set; } = "5";

    /// <summary>Unit ids of the last conversion, per category id.</summary>
    public Dictionary<string, string[]> Units { get; set; } = [];

    /// <summary>Saved cards, newest first.</summary>
    public List<ConvertCard> Cards { get; set; } = [];
}
