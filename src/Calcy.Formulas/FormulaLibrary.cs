using System.Text.Json;

namespace Calcy.Formulas;

/// <summary>
/// The formula collection: every formula, category and constant, loaded from the JSON files embedded
/// in this assembly (<c>Data/*.json</c>), with lookup and fuzzy search.
/// Instances are immutable and safe to share between threads.
/// </summary>
public sealed class FormulaLibrary
{
    private const string CategoriesFile = "categories.json";
    private const string ConstantsFile = "constants.json";

    private static readonly Lazy<FormulaLibrary> s_default = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, Formula> _byId;
    private readonly Dictionary<string, FormulaCategory> _categoriesById;
    private readonly Dictionary<string, FormulaConstant> _constantsById;
    private readonly Dictionary<string, FormulaConstant> _constantsBySymbol;
    private readonly Dictionary<string, IReadOnlyList<Formula>> _byCategory;
    private readonly SearchIndex<Formula> _formulaIndex;
    private readonly SearchIndex<FormulaConstant> _constantIndex;

    private FormulaLibrary(List<FormulaCategory> categories, List<FormulaConstant> constants, List<Formula> formulas)
    {
        Categories = categories.OrderBy(c => c.Order).ThenBy(c => c.Name, StringComparer.Ordinal).ToArray();
        _categoriesById = ToUniqueDictionary(Categories, c => c.Id, "category id");

        Constants = constants.ToArray();
        _constantsById = ToUniqueDictionary(Constants, c => c.Id, "constant id");
        _constantsBySymbol = ToUniqueDictionary(Constants, c => c.Symbol, "constant symbol");

        var categoryOrder = Categories.Select((c, i) => (c.Id, i)).ToDictionary(t => t.Id, t => t.i, StringComparer.Ordinal);
        All = formulas
            .Select((f, i) => (f, i))
            .OrderBy(t => categoryOrder.GetValueOrDefault(t.f.Category, int.MaxValue))
            .ThenBy(t => t.i)
            .Select(t => t.f)
            .ToArray();
        _byId = ToUniqueDictionary(All, f => f.Id, "formula id");

        _byCategory = All
            .GroupBy(f => f.Category, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Formula>)g.ToArray(), StringComparer.Ordinal);

        _formulaIndex = new SearchIndex<Formula>(All.Select(BuildDocument));
        _constantIndex = new SearchIndex<FormulaConstant>(Constants.Select(BuildDocument));
    }

    /// <summary>A shared instance loaded on first use.</summary>
    public static FormulaLibrary Default => s_default.Value;

    /// <summary>Every formula, ordered by category and then as listed in the data files.</summary>
    public IReadOnlyList<Formula> All { get; }

    /// <summary>All categories, ordered by <see cref="FormulaCategory.Order"/>.</summary>
    public IReadOnlyList<FormulaCategory> Categories { get; }

    /// <summary>All documented constants.</summary>
    public IReadOnlyList<FormulaConstant> Constants { get; }

    /// <summary>Loads the formula collection from the JSON files embedded in this assembly.</summary>
    /// <exception cref="InvalidDataException">A data file is missing, malformed, or contains duplicate ids.</exception>
    public static FormulaLibrary Load()
    {
        var assembly = typeof(FormulaLibrary).Assembly;
        var files = new List<(string Name, string Json)>();
        foreach (string resource in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            if (!resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException($"Embedded resource '{resource}' could not be opened.");
            using var reader = new StreamReader(stream);
            files.Add((resource, reader.ReadToEnd()));
        }
        return FromJsonFiles(files);
    }

    /// <summary>
    /// Builds a library from JSON text. Each entry is a file name and its content; the file named
    /// <c>categories.json</c> holds categories, <c>constants.json</c> holds constants, and every other file holds formulas.
    /// Resource style names such as <c>Calcy.Formulas.Data.constants.json</c> are accepted too.
    /// </summary>
    public static FormulaLibrary FromJsonFiles(IEnumerable<(string Name, string Json)> files)
    {
        var categories = new List<FormulaCategory>();
        var constants = new List<FormulaConstant>();
        var formulas = new List<Formula>();
        bool sawCategories = false, sawConstants = false;

        foreach (var (name, json) in files)
        {
            try
            {
                if (IsFile(name, CategoriesFile))
                {
                    var file = JsonSerializer.Deserialize(json, FormulaJsonContext.Default.CategoryFile)
                        ?? throw new InvalidDataException("The file is empty.");
                    categories.AddRange(file.Categories.Select(Normalize));
                    sawCategories = true;
                }
                else if (IsFile(name, ConstantsFile))
                {
                    var file = JsonSerializer.Deserialize(json, FormulaJsonContext.Default.ConstantFile)
                        ?? throw new InvalidDataException("The file is empty.");
                    constants.AddRange(file.Constants.Select(Normalize));
                    sawConstants = true;
                }
                else
                {
                    var file = JsonSerializer.Deserialize(json, FormulaJsonContext.Default.FormulaFile)
                        ?? throw new InvalidDataException("The file is empty.");
                    foreach (var f in file.Formulas)
                        formulas.Add(Normalize(f, file.Category ?? ""));
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Formula data file '{name}' is not valid: {ex.Message}", ex);
            }
        }

        if (!sawCategories) throw new InvalidDataException($"No {CategoriesFile} data file was found.");
        if (!sawConstants) throw new InvalidDataException($"No {ConstantsFile} data file was found.");
        return new FormulaLibrary(categories, constants, formulas);
    }

    /// <summary>Finds a formula by id, or returns null.</summary>
    public Formula? Get(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Finds a category by id, or returns null.</summary>
    public FormulaCategory? GetCategory(string id) => _categoriesById.GetValueOrDefault(id);

    /// <summary>Finds a constant by id (for example <c>speed-of-light</c>) or by symbol (for example <c>c</c>).</summary>
    public FormulaConstant? GetConstant(string idOrSymbol) =>
        _constantsById.GetValueOrDefault(idOrSymbol) ?? _constantsBySymbol.GetValueOrDefault(idOrSymbol);

    /// <summary>The formulas of one category, in data file order. Empty for an unknown id.</summary>
    public IReadOnlyList<Formula> ByCategory(string categoryId) =>
        _byCategory.GetValueOrDefault(categoryId) ?? [];

    /// <summary>The constants used by a formula, in the order listed.</summary>
    public IReadOnlyList<FormulaConstant> ConstantsFor(Formula formula) =>
        formula.Constants.Select(id => _constantsById.GetValueOrDefault(id)).OfType<FormulaConstant>().ToArray();

    /// <summary>The formulas listed in <see cref="Formula.Related"/> that exist in this library.</summary>
    public IReadOnlyList<Formula> RelatedTo(Formula formula) =>
        formula.Related.Select(id => _byId.GetValueOrDefault(id)).OfType<Formula>().ToArray();

    /// <summary>
    /// Ranked search over names, tags, categories, variable names and explanations. Case and accents are ignored,
    /// partial words match ("pyth" finds Pythagoras), and words of four letters or more tolerate one typo
    /// ("snel" finds Snell's law). Typing an equation such as <c>E=mc^2</c> also works.
    /// An empty query returns every formula.
    /// </summary>
    public IReadOnlyList<Formula> Search(string query) =>
        _formulaIndex.Search(query).Select(r => r.Item).ToArray();

    /// <summary>Like <see cref="Search(string)"/>, limited to <paramref name="maxResults"/> hits.</summary>
    public IReadOnlyList<Formula> Search(string query, int maxResults) =>
        _formulaIndex.Search(query).Take(Math.Max(0, maxResults)).Select(r => r.Item).ToArray();

    /// <summary>Like <see cref="Search(string)"/>, with the relevance score of each hit.</summary>
    public IReadOnlyList<FormulaSearchHit> SearchWithScores(string query) =>
        _formulaIndex.Search(query).Select(r => new FormulaSearchHit(r.Item, r.Score)).ToArray();

    /// <summary>Ranked search over constants by name, symbol, tags and description.</summary>
    public IReadOnlyList<FormulaConstant> SearchConstants(string query) =>
        _constantIndex.Search(query).Select(r => r.Item).ToArray();

    private SearchDocument<Formula> BuildDocument(Formula f)
    {
        var category = GetCategory(f.Category);
        var categoryWords = SearchText.Words($"{category?.Name} {f.Subcategory} {f.Category}");
        var variableWords = f.Variables.SelectMany(v => SearchText.Words(v.Name)).Distinct().ToArray();
        var tagWords = f.Tags.SelectMany(SearchText.Words).Distinct().ToArray();
        var textWords = SearchText.Words($"{f.Explanation} {f.WhenToUse}").Where(w => w.Length >= 3).Distinct().ToArray();
        return new SearchDocument<Formula>
        {
            Item = f,
            Name = SearchText.Normalize(f.Name),
            TagPhrases = f.Tags.Select(SearchText.Normalize).ToArray(),
            CompactEquation = SearchText.CompactEquation(f.Equation),
            Fields =
            [
                (SearchText.Words(f.Name), 10),
                (tagWords, 8),
                (categoryWords, 5),
                (variableWords, 4),
                (SearchText.Words(f.Id), 4),
                (textWords, 1.5),
            ],
        };
    }

    private static SearchDocument<FormulaConstant> BuildDocument(FormulaConstant c) => new()
    {
        Item = c,
        Name = SearchText.Normalize(c.Name),
        TagPhrases = c.Tags.Select(SearchText.Normalize).ToArray(),
        Symbol = c.Symbol,
        Fields =
        [
            (SearchText.Words(c.Name), 10),
            (c.Tags.SelectMany(SearchText.Words).Distinct().ToArray(), 8),
            (SearchText.Words(c.Id), 4),
            (SearchText.Words(c.Description).Where(w => w.Length >= 3).Distinct().ToArray(), 1.5),
        ],
    };

    // The source generated deserializer assigns every init-only property, so a property that is missing
    // from the JSON arrives as null instead of keeping its initializer. These restore the documented defaults.
    private static Formula Normalize(Formula f, string fileCategory) => f with
    {
        Category = string.IsNullOrEmpty(f.Category) ? fileCategory : f.Category,
        Subcategory = f.Subcategory ?? "",
        DisplayLatex = f.DisplayLatex ?? "",
        Variables = (f.Variables ?? []).Select(Normalize).ToArray(),
        Constants = f.Constants ?? [],
        Explanation = f.Explanation ?? "",
        WhenToUse = f.WhenToUse ?? "",
        Assumptions = f.Assumptions ?? [],
        Example = f.Example is null ? null : Normalize(f.Example),
        Related = f.Related ?? [],
        Tags = f.Tags ?? [],
    };

    private static FormulaVariable Normalize(FormulaVariable v) => v with
    {
        Unit = v.Unit ?? "",
        Description = v.Description ?? "",
    };

    private static WorkedExample Normalize(WorkedExample e) => e with
    {
        Given = e.Given ?? new Dictionary<string, double>(),
        Unit = e.Unit ?? "",
        Steps = e.Steps ?? [],
    };

    private static FormulaConstant Normalize(FormulaConstant c) => c with
    {
        Unit = c.Unit ?? "",
        Description = c.Description ?? "",
        Tags = c.Tags ?? [],
    };

    private static FormulaCategory Normalize(FormulaCategory c) => c with
    {
        Description = c.Description ?? "",
        Icon = c.Icon ?? "",
    };

    private static bool IsFile(string name, string fileName) =>
        name.Equals(fileName, StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("\\" + fileName, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, T> ToUniqueDictionary<T>(IEnumerable<T> items, Func<T, string> key, string what)
    {
        var dict = new Dictionary<string, T>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        foreach (var item in items)
            if (!dict.TryAdd(key(item), item)) duplicates.Add(key(item));
        if (duplicates.Count > 0)
            throw new InvalidDataException($"Duplicate {what}: {string.Join(", ", duplicates.Distinct())}");
        return dict;
    }
}
