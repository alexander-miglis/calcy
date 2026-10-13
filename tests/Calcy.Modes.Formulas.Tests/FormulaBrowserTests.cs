using Calcy.Formulas;
using Calcy.Modes.Formulas.Logic;
using static Calcy.Modes.Formulas.Tests.TestSupport;

namespace Calcy.Modes.Formulas.Tests;

/// <summary>The left column's model: categories, ranked search with typos and typed equations, constants.</summary>
public class FormulaBrowserTests
{
    private readonly FormulaBrowser _browser = new();

    [Fact]
    public void CategoriesAreTheFourteenInOrder()
    {
        Assert.Equal(
            ["algebra", "geometry", "trigonometry", "calculus", "vectors", "statistics", "mechanics", "waves", "electricity",
             "thermodynamics", "modern-physics", "chemistry", "finance", "everyday"],
            _browser.Categories.Select(c => c.Id));
        Assert.Equal(418, _browser.Categories.Sum(c => _browser.CountIn(c.Id)));
    }

    [Fact]
    public void CategoryListsItsFormulasWithTopicAndLevel()
    {
        var mechanics = _browser.InCategory("mechanics");
        Assert.Equal(Library.ByCategory("mechanics").Count, mechanics.Count);
        var first = mechanics[0];
        Assert.Equal("suvat-v-u-a-t", first.Formula.Id);
        Assert.Equal("Kinematics", first.Context);
        Assert.Equal("SCHOOL", first.LevelTag);
        Assert.Contains(_browser.Categories.SelectMany(c => _browser.InCategory(c.Id)), e => e.LevelTag == "UNI");
        Assert.Empty(_browser.InCategory("no-such-category"));
    }

    [Fact]
    public void FindsPythagorasByName()
    {
        var hits = _browser.Search("pythagoras");
        Assert.Equal("pythagoras-theorem", hits[0].Formula.Id);
        Assert.StartsWith(Library.GetCategory("trigonometry")!.Name + " · ", hits[0].Context);
        Assert.Equal("pythagoras-theorem", _browser.TopHit("Pythagoras")!.Id);
    }

    [Fact]
    public void FindsEverySuvatEquation()
    {
        var ids = _browser.Search("suvat").Select(e => e.Formula.Id).ToList();
        Assert.Equal(5, ids.Count(id => id.StartsWith("suvat-", StringComparison.Ordinal)));
        Assert.All(ids.Take(5), id => Assert.StartsWith("suvat-", id));
    }

    [Theory]
    [InlineData("snel")]
    [InlineData("snell")]
    [InlineData("snels law")]
    [InlineData("refraction snell")]
    public void ToleratesTyposInSnellsLaw(string query)
    {
        Assert.Equal("snells-law", _browser.Search(query)[0].Formula.Id);
    }

    [Theory]
    [InlineData("E=mc^2")]
    [InlineData("e = mc^2")]
    [InlineData("E = m*c^2")]
    public void FindsATypedEquation(string query)
    {
        Assert.Equal("mass-energy-equivalence", _browser.TopHit(query)!.Id);
    }

    [Fact]
    public void BlankSearchHasNoResults()
    {
        Assert.Empty(_browser.Search(""));
        Assert.Empty(_browser.Search("   "));
        Assert.Null(_browser.TopHit(null));
        Assert.Empty(_browser.Search("qqqzzzxxx"));
    }

    [Fact]
    public void SearchIsCapped()
    {
        Assert.True(_browser.Search("energy").Count <= FormulaBrowser.MaxResults);
        Assert.Equal(3, _browser.Search("energy", 3).Count);
    }

    [Fact]
    public void ConstantsListAndSearch()
    {
        Assert.Equal(56, _browser.FindConstants("").Count);
        Assert.Equal("h", _browser.FindConstants("planck")[0].Symbol);
        Assert.Contains(_browser.FindConstants("speed of light"), c => c.Symbol == "c");
        Assert.Equal("c", _browser.FindConstants("c")[0].Symbol);
        Assert.Empty(_browser.FindConstants("qqqzzzxxx"));
    }

    [Theory]
    [InlineData("mechanics", "MECHANICS")]
    [InlineData("trigonometry", "TRIG")]
    [InlineData("modern-physics", "MODERN")]
    [InlineData("electricity", "ELECTRIC")]
    [InlineData("something-new", "SOMETHING NEW")]
    public void ShortCategoryLabelsFitCardHeaders(string id, string expected)
    {
        Assert.Equal(expected, FormulaBrowser.ShortCategory(id));
    }

    [Fact]
    public void EveryShortLabelIsAtMostNineCharacters()
    {
        Assert.All(Library.Categories, c => Assert.True(FormulaBrowser.ShortCategory(c.Id).Length <= 9, c.Id));
    }

    [Fact]
    public void LevelTags()
    {
        Assert.Equal("SCHOOL", FormulaBrowser.LevelTag(FormulaLevel.School));
        Assert.Equal("UNI", FormulaBrowser.LevelTag(FormulaLevel.University));
        Assert.Equal("university level", FormulaBrowser.LevelName(FormulaLevel.University));
    }
}
