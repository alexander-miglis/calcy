using System.Text.Json;
using Calcy.Formulas.Tests.Evaluation;

namespace Calcy.Formulas.Tests;

/// <summary>Structural checks on every formula, category and constant in the embedded data.</summary>
public class DataIntegrityTests
{
    private static FormulaLibrary Lib => TestData.Library;

    [Fact]
    public void Library_loads_with_enough_content()
    {
        var lib = FormulaLibrary.Load();
        Assert.True(lib.All.Count >= 220, $"Only {lib.All.Count} formulas.");
        Assert.True(lib.Constants.Count >= 40, $"Only {lib.Constants.Count} constants.");
        Assert.True(lib.Categories.Count >= 10);
    }

    [Fact]
    public void Every_data_file_is_valid_json_with_the_expected_root()
    {
        var files = TestData.RawDataFiles();
        Assert.Contains(files, f => f.Name.EndsWith(".categories.json", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Name.EndsWith(".constants.json", StringComparison.Ordinal));
        foreach (var (name, json) in files)
        {
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            string expectedRoot = name.EndsWith(".categories.json", StringComparison.Ordinal) ? "categories"
                : name.EndsWith(".constants.json", StringComparison.Ordinal) ? "constants" : "formulas";
            Assert.True(doc.RootElement.TryGetProperty(expectedRoot, out var arr), $"{name} has no '{expectedRoot}' array.");
            Assert.Equal(JsonValueKind.Array, arr.ValueKind);
            Assert.True(arr.GetArrayLength() > 0, $"{name} is empty.");
        }
    }

    [Fact]
    public void Each_formula_file_declares_an_existing_category_and_matches_its_file_name()
    {
        foreach (var (name, json) in TestData.RawDataFiles())
        {
            if (name.EndsWith(".categories.json", StringComparison.Ordinal) || name.EndsWith(".constants.json", StringComparison.Ordinal)) continue;
            using var doc = JsonDocument.Parse(json);
            string category = doc.RootElement.GetProperty("category").GetString()!;
            Assert.NotNull(Lib.GetCategory(category));
            Assert.EndsWith($".{category}.json", name);
        }
    }

    [Fact]
    public void No_em_or_en_dashes_anywhere_in_the_data()
    {
        // Figure dash, en dash, em dash and horizontal bar (U+2012 to U+2015), built from code points
        // so that this source file itself stays free of them.
        char[] forbidden = [(char)0x2012, (char)0x2013, (char)0x2014, (char)0x2015];
        foreach (var (name, json) in TestData.RawDataFiles())
        {
            int i = json.IndexOfAny(forbidden);
            Assert.True(i < 0, $"{name} contains a dash at offset {i}: ...{(i < 0 ? "" : json.Substring(Math.Max(0, i - 40), Math.Min(80, json.Length - Math.Max(0, i - 40))))}...");
            Assert.DoesNotContain("\\u2013", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\\u2014", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Formula_ids_are_unique_and_kebab_case()
    {
        var duplicates = Lib.All.GroupBy(f => f.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
        var bad = Lib.All.Where(f => !TestData.KebabCase().IsMatch(f.Id)).Select(f => f.Id).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void Formula_names_are_unique()
    {
        var duplicates = Lib.All.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Categories_are_unique_ordered_and_all_used()
    {
        Assert.Equal(Lib.Categories.Count, Lib.Categories.Select(c => c.Id).Distinct().Count());
        Assert.Equal(Lib.Categories.Count, Lib.Categories.Select(c => c.Order).Distinct().Count());
        foreach (var c in Lib.Categories)
        {
            Assert.Matches(TestData.KebabCase(), c.Id);
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Description));
            Assert.Matches("^[a-z]+$", c.Icon);
            Assert.NotEmpty(Lib.ByCategory(c.Id));
        }
    }

    [Theory]
    [MemberData(nameof(TestData.FormulaIds), MemberType = typeof(TestData))]
    public void Formula_is_well_formed(string id)
    {
        var f = Lib.Get(id)!;
        Assert.NotNull(Lib.GetCategory(f.Category));
        Assert.False(string.IsNullOrWhiteSpace(f.Name));
        Assert.False(string.IsNullOrWhiteSpace(f.Subcategory));
        Assert.False(string.IsNullOrWhiteSpace(f.DisplayLatex));
        Assert.NotEmpty(f.Variables);
        Assert.NotEmpty(f.Tags);

        int sentences = TestData.SentenceCount(f.Explanation);
        Assert.True(sentences is >= 2 and <= 6, $"Explanation has {sentences} sentences.");
        int whenSentences = TestData.SentenceCount(f.WhenToUse);
        Assert.True(whenSentences is >= 1 and <= 3, $"WhenToUse has {whenSentences} sentences.");
        Assert.All(f.Assumptions, a => Assert.False(string.IsNullOrWhiteSpace(a)));
        Assert.All(f.Tags, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    [Theory]
    [MemberData(nameof(TestData.FormulaIds), MemberType = typeof(TestData))]
    public void Related_ids_exist(string id)
    {
        var f = Lib.Get(id)!;
        foreach (string r in f.Related)
        {
            Assert.True(Lib.Get(r) is not null, $"{id} refers to unknown related formula '{r}'.");
            Assert.NotEqual(id, r);
        }
        Assert.Equal(f.Related.Count, f.Related.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(TestData.FormulaIds), MemberType = typeof(TestData))]
    public void Equation_symbols_match_documented_variables_and_constants(string id)
    {
        var f = Lib.Get(id)!;
        var (left, right) = Expression.ParseEquation(f.Equation);
        var used = new HashSet<string>(left.Identifiers().Concat(right.Identifiers()), StringComparer.Ordinal);

        var constants = new List<FormulaConstant>();
        foreach (string cid in f.Constants)
        {
            var c = Lib.GetConstant(cid);
            Assert.True(c is not null && c.Id == cid, $"{id} lists unknown constant id '{cid}'.");
            constants.Add(c!);
        }

        var variableSymbols = f.Variables.Select(v => v.Symbol).ToList();
        Assert.Equal(variableSymbols.Count, variableSymbols.Distinct().Count());
        foreach (var v in f.Variables)
        {
            Assert.Matches(TestData.SymbolPattern(), v.Symbol);
            Assert.False(Expression.Functions.ContainsKey(v.Symbol) || Expression.BuiltInConstants.Contains(v.Symbol), $"'{v.Symbol}' is reserved.");
            Assert.DoesNotContain(constants, c => c.Symbol == v.Symbol);
            Assert.False(string.IsNullOrWhiteSpace(v.Name));
            Assert.False(string.IsNullOrWhiteSpace(v.Description), $"Variable {v.Symbol} has no description.");
            Assert.True(used.Contains(v.Symbol), $"Documented variable '{v.Symbol}' does not appear in '{f.Equation}'.");
            if (v.Min is { } min && v.Max is { } max) Assert.True(min < max);
        }

        foreach (var c in constants)
            Assert.True(used.Contains(c.Symbol), $"Listed constant '{c.Symbol}' does not appear in '{f.Equation}'.");

        var known = new HashSet<string>(variableSymbols.Concat(constants.Select(c => c.Symbol)), StringComparer.Ordinal);
        var undocumented = used.Where(s => !known.Contains(s)).ToList();
        Assert.True(undocumented.Count == 0, $"Undocumented symbols in '{f.Equation}': {string.Join(", ", undocumented)}");
    }

    [Theory]
    [MemberData(nameof(TestData.FormulaIds), MemberType = typeof(TestData))]
    public void Worked_example_is_complete_and_consistent(string id)
    {
        var f = Lib.Get(id)!;
        var ex = f.Example;
        Assert.NotNull(ex);
        Assert.False(string.IsNullOrWhiteSpace(ex.Story));
        Assert.True(ex.Steps.Count is >= 2 and <= 4, $"Example has {ex.Steps.Count} steps.");
        Assert.All(ex.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.True(ex.Tolerance > 0 && double.IsFinite(ex.Tolerance));
        Assert.True(double.IsFinite(ex.Result));
        Assert.True(ex.Tolerance <= Math.Max(Math.Abs(ex.Result) * 0.02, 1e-12), $"Tolerance {ex.Tolerance} is too loose for {ex.Result}.");

        var expectedGiven = f.Variables.Select(v => v.Symbol).Where(s => s != ex.SolveFor).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var actualGiven = ex.Given.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedGiven, actualGiven);

        if (f.IsIdentity)
        {
            Assert.Null(ex.SolveFor);
        }
        else
        {
            Assert.NotNull(ex.SolveFor);
            var unknown = f.GetVariable(ex.SolveFor!);
            Assert.NotNull(unknown);
            Assert.Equal(unknown!.Unit, ex.Unit);
            Assert.True(unknown.Accepts(ex.Result), $"Result {ex.Result} violates the constraints of {unknown.Symbol}.");
        }

        foreach (var (symbol, value) in ex.Given)
        {
            var v = f.GetVariable(symbol)!;
            Assert.True(v.Accepts(value), $"Given {symbol} = {value} violates its constraints.");
        }
    }

    [Fact]
    public void Constants_are_unique_and_documented()
    {
        Assert.Equal(Lib.Constants.Count, Lib.Constants.Select(c => c.Id).Distinct().Count());
        Assert.Equal(Lib.Constants.Count, Lib.Constants.Select(c => c.Symbol).Distinct().Count());
        foreach (var c in Lib.Constants)
        {
            Assert.Matches(TestData.KebabCase(), c.Id);
            Assert.True(TestData.SymbolPattern().IsMatch(c.Symbol) || Expression.BuiltInConstants.Contains(c.Symbol), $"Bad constant symbol '{c.Symbol}'.");
            Assert.False(Expression.Functions.ContainsKey(c.Symbol));
            Assert.True(double.IsFinite(c.Value) && c.Value > 0, $"{c.Id} has value {c.Value}.");
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.True(TestData.SentenceCount(c.Description) >= 1, $"{c.Id} needs a description.");
            Assert.False(string.IsNullOrWhiteSpace(c.Source));
            if (c.Exact) Assert.Null(c.Uncertainty);
            if (c.Uncertainty is { } u) Assert.True(u > 0 && u < c.Value * 1e-3, $"{c.Id} uncertainty looks wrong.");
        }
    }

    [Fact]
    public void Constants_used_by_formulas_are_not_also_their_variables()
    {
        foreach (var f in Lib.All)
        {
            var symbols = Lib.ConstantsFor(f).Select(c => c.Symbol).ToHashSet();
            Assert.DoesNotContain(f.Variables, v => symbols.Contains(v.Symbol));
        }
    }
}
