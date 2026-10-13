using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>One row of the formula list: a formula with the context line and the level tag shown under its name.</summary>
/// <param name="Formula">The formula.</param>
/// <param name="Context">Where it belongs: the topic ("Kinematics"), or the category and topic for search results.</param>
/// <param name="LevelTag">SCHOOL or UNI.</param>
public sealed record FormulaListEntry(Formula Formula, string Context, string LevelTag)
{
    /// <summary>The formula name.</summary>
    public string Name => Formula.Name;
}

/// <summary>
/// The browsing and search model behind the left column: categories with their formulas, ranked formula search
/// (names, topics, typed equations such as E=mc^2, small typos) and the searchable list of constants.
/// </summary>
public sealed class FormulaBrowser
{
    /// <summary>The most search results shown at once.</summary>
    public const int MaxResults = 40;

    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.Ordinal)
    {
        ["algebra"] = "ALGEBRA",
        ["geometry"] = "GEOMETRY",
        ["trigonometry"] = "TRIG",
        ["calculus"] = "CALCULUS",
        ["vectors"] = "VECTORS",
        ["statistics"] = "STATS",
        ["mechanics"] = "MECHANICS",
        ["waves"] = "WAVES",
        ["electricity"] = "ELECTRIC",
        ["thermodynamics"] = "THERMO",
        ["modern-physics"] = "MODERN",
        ["chemistry"] = "CHEMISTRY",
        ["finance"] = "FINANCE",
        ["everyday"] = "EVERYDAY",
    };

    /// <summary>Creates the model over a library (the shared default library when null).</summary>
    public FormulaBrowser(FormulaLibrary? library = null)
    {
        Library = library ?? FormulaLibrary.Default;
    }

    /// <summary>The formula collection.</summary>
    public FormulaLibrary Library { get; }

    /// <summary>The categories in display order.</summary>
    public IReadOnlyList<FormulaCategory> Categories => Library.Categories;

    /// <summary>How many formulas a category holds.</summary>
    public int CountIn(string categoryId) => Library.ByCategory(categoryId).Count;

    /// <summary>The formulas of a category in collection order, each with its topic as context.</summary>
    public IReadOnlyList<FormulaListEntry> InCategory(string categoryId) =>
        [.. Library.ByCategory(categoryId).Select(f => new FormulaListEntry(f, f.Subcategory, LevelTag(f.Level)))];

    /// <summary>
    /// Ranked search results for a query (empty for a blank query), each with "Category · Topic" as context.
    /// </summary>
    public IReadOnlyList<FormulaListEntry> Search(string? query, int maxResults = MaxResults)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return [.. Library.Search(query.Trim(), maxResults).Select(f => new FormulaListEntry(f, ContextOf(f), LevelTag(f.Level)))];
    }

    /// <summary>The best match for a query, or null.</summary>
    public Formula? TopHit(string? query) => string.IsNullOrWhiteSpace(query) ? null : Library.Search(query.Trim(), 1).FirstOrDefault();

    /// <summary>The constants that match a query, ranked; every constant in collection order for a blank query.</summary>
    public IReadOnlyList<FormulaConstant> FindConstants(string? query) =>
        string.IsNullOrWhiteSpace(query) ? Library.Constants : Library.SearchConstants(query.Trim());

    /// <summary>"Mechanics · Kinematics".</summary>
    public string ContextOf(Formula formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        var category = Library.GetCategory(formula.Category)?.Name ?? formula.Category;
        return string.IsNullOrEmpty(formula.Subcategory) ? category : $"{category} · {formula.Subcategory}";
    }

    /// <summary>The tag shown next to a formula: SCHOOL or UNI.</summary>
    public static string LevelTag(FormulaLevel level) => level == FormulaLevel.University ? "UNI" : "SCHOOL";

    /// <summary>Spoken form of the level tag, for screen readers and tooltips.</summary>
    public static string LevelName(FormulaLevel level) => level == FormulaLevel.University ? "university level" : "school level";

    /// <summary>A short upper-case category label that fits a card header ("MECHANICS", "TRIG").</summary>
    public static string ShortCategory(string categoryId) =>
        ShortNames.TryGetValue(categoryId, out var name) ? name : categoryId.Replace('-', ' ').ToUpperInvariant();
}
