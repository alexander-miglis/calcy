namespace Calcy.Formulas.Tests;

/// <summary>Behaviour of the public FormulaLibrary API.</summary>
public class LibraryApiTests
{
    private static FormulaLibrary Lib => TestData.Library;

    private const string MinimalCategories = """{ "categories": [ { "id": "demo", "name": "Demo", "description": "Demo.", "icon": "star", "order": 1 } ] }""";
    private const string MinimalConstants = """{ "constants": [ { "id": "two", "symbol": "k_2", "name": "Two", "value": 2, "description": "The number two.", "source": "test" } ] }""";
    private const string MinimalFormulas = """
        {
          "category": "demo",
          "formulas": [
            {
              "id": "double-it", "name": "Double it", "subcategory": "Demo", "level": "university",
              "equation": "y = k_2*x", "displayLatex": "y = 2x",
              "variables": [ { "symbol": "y", "name": "result" }, { "symbol": "x", "name": "input", "positive": true } ],
              "constants": [ "two" ],
              "example": { "story": "Double 3.", "given": { "x": 3 }, "solveFor": "y", "result": 6, "tolerance": 1e-9, "steps": [ "2 × 3", "6" ] }
            }
          ]
        }
        """;

    [Fact]
    public void Default_is_shared_and_load_creates_an_equivalent_library()
    {
        Assert.Same(FormulaLibrary.Default, FormulaLibrary.Default);
        var fresh = FormulaLibrary.Load();
        Assert.NotSame(FormulaLibrary.Default, fresh);
        Assert.Equal(FormulaLibrary.Default.All.Select(f => f.Id), fresh.All.Select(f => f.Id));
    }

    [Fact]
    public void Get_finds_formulas_by_id()
    {
        var f = Lib.Get("ohms-law");
        Assert.NotNull(f);
        Assert.Equal("Ohm's law", f.Name);
        Assert.Equal("electricity", f.Category);
        Assert.Equal(FormulaLevel.School, f.Level);
        Assert.Null(Lib.Get("no-such-formula"));
    }

    [Fact]
    public void Categories_are_sorted_and_by_category_returns_their_formulas()
    {
        var orders = Lib.Categories.Select(c => c.Order).ToList();
        Assert.Equal(orders.Order(), orders);
        Assert.Equal("algebra", Lib.Categories[0].Id);
        foreach (var c in Lib.Categories)
            Assert.All(Lib.ByCategory(c.Id), f => Assert.Equal(c.Id, f.Category));
        Assert.Equal(Lib.All.Count, Lib.Categories.Sum(c => Lib.ByCategory(c.Id).Count));
        Assert.Empty(Lib.ByCategory("no-such-category"));
        Assert.Null(Lib.GetCategory("no-such-category"));
    }

    [Fact]
    public void All_is_grouped_by_category_order()
    {
        var categoryIndex = Lib.Categories.Select((c, i) => (c.Id, i)).ToDictionary(t => t.Id, t => t.i);
        var sequence = Lib.All.Select(f => categoryIndex[f.Category]).ToList();
        Assert.Equal(sequence.Order(), sequence);
    }

    [Fact]
    public void Constants_and_related_formulas_resolve()
    {
        var f = Lib.Get("newtons-law-of-gravitation")!;
        var constants = Lib.ConstantsFor(f);
        Assert.Single(constants);
        Assert.Equal("G", constants[0].Symbol);
        Assert.Equal(6.6743e-11, constants[0].Value);
        Assert.Equal(f.Related.Count, Lib.RelatedTo(f).Count);
    }

    [Fact]
    public void Formula_helpers_split_the_equation_and_find_variables()
    {
        var f = Lib.Get("suvat-v-u-a-t")!;
        Assert.Equal("v", f.LeftSide);
        Assert.Equal("u + a*t", f.RightSide);
        Assert.Equal("m/s²", f.GetVariable("a")!.Unit);
        Assert.Null(f.GetVariable("zzz"));
    }

    [Fact]
    public void Variable_accepts_respects_constraints()
    {
        var positive = new FormulaVariable { Symbol = "x", Name = "x", Positive = true };
        Assert.True(positive.Accepts(0.1));
        Assert.False(positive.Accepts(0));
        Assert.False(positive.Accepts(double.NaN));

        var count = new FormulaVariable { Symbol = "n", Name = "n", Integer = true, Min = 1, Max = 10 };
        Assert.True(count.Accepts(3));
        Assert.False(count.Accepts(3.5));
        Assert.False(count.Accepts(0));
        Assert.False(count.Accepts(11));
        Assert.True(new FormulaVariable { Symbol = "z", Name = "z" }.IsDimensionless);
    }

    [Fact]
    public void Missing_optional_fields_get_sensible_defaults()
    {
        var lib = FormulaLibrary.FromJsonFiles([("categories.json", MinimalCategories), ("constants.json", MinimalConstants), ("demo.json", MinimalFormulas)]);
        var f = Assert.Single(lib.All);
        Assert.Equal("demo", f.Category);
        Assert.Equal(FormulaLevel.University, f.Level);
        Assert.Equal("", f.Explanation);
        Assert.Empty(f.Tags);
        Assert.Empty(f.Related);
        Assert.Equal("", f.GetVariable("y")!.Unit);
        Assert.Equal("", f.Example!.Unit);
        Assert.Equal(3, f.Example.Given["x"]);
        Assert.Equal(2, lib.GetConstant("k_2")!.Value);
        Assert.Empty(lib.GetConstant("two")!.Tags);
    }

    [Fact]
    public void Unknown_json_properties_are_rejected()
    {
        string typo = MinimalFormulas.Replace("\"subcategory\"", "\"subcatgory\"", StringComparison.Ordinal);
        var ex = Assert.Throws<InvalidDataException>(() =>
            FormulaLibrary.FromJsonFiles([("categories.json", MinimalCategories), ("constants.json", MinimalConstants), ("demo.json", typo)]));
        Assert.Contains("demo.json", ex.Message);
    }

    [Fact]
    public void Duplicate_ids_and_missing_files_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            FormulaLibrary.FromJsonFiles([("categories.json", MinimalCategories), ("constants.json", MinimalConstants), ("a.json", MinimalFormulas), ("b.json", MinimalFormulas)]));
        Assert.Throws<InvalidDataException>(() =>
            FormulaLibrary.FromJsonFiles([("constants.json", MinimalConstants), ("a.json", MinimalFormulas)]));
        Assert.Throws<InvalidDataException>(() =>
            FormulaLibrary.FromJsonFiles([("categories.json", MinimalCategories), ("constants.json", "{ not json")]));
    }

    [Fact]
    public void Content_is_spread_over_all_categories_and_levels()
    {
        Assert.All(Lib.Categories, c => Assert.True(Lib.ByCategory(c.Id).Count >= 10, $"{c.Id} has only {Lib.ByCategory(c.Id).Count} formulas."));
        Assert.Contains(Lib.All, f => f.Level == FormulaLevel.University);
        Assert.True(Lib.All.Count(f => f.Level == FormulaLevel.School) > Lib.All.Count / 2);
        Assert.Contains(Lib.All, f => f.IsIdentity);
        Assert.Contains(Lib.All, f => f.Source is not null);
    }
}
