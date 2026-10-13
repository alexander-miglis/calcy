namespace Calcy.Formulas.Tests;

/// <summary>Search must find the obvious formula for common queries, typos, partial words and accents.</summary>
public class SearchTests
{
    private static FormulaLibrary Lib => TestData.Library;

    private static IReadOnlyList<string> TopIds(string query, int count) =>
        Lib.Search(query).Take(count).Select(f => f.Id).ToList();

    [Theory]
    [InlineData("pythagoras", "pythagoras-theorem")]
    [InlineData("Pythagoras' theorem", "pythagoras-theorem")]
    [InlineData("PYTHAGORAS", "pythagoras-theorem")]
    [InlineData("pyth", "pythagoras-theorem")]
    [InlineData("ohms", "ohms-law")]
    [InlineData("ohm's law", "ohms-law")]
    [InlineData("snell", "snells-law")]
    [InlineData("ideal gas", "ideal-gas-law")]
    [InlineData("compound interest", "compound-interest")]
    [InlineData("bayes", "bayes-theorem")]
    [InlineData("sine rule", "sine-rule")]
    [InlineData("cosine rule", "cosine-rule")]
    [InlineData("law of cosines", "cosine-rule")]
    [InlineData("kinetic energy", "kinetic-energy")]
    [InlineData("heron", "herons-formula")]
    [InlineData("de broglie", "de-broglie-wavelength")]
    [InlineData("henderson hasselbalch", "henderson-hasselbalch")]
    [InlineData("beer lambert", "beer-lambert-law")]
    [InlineData("rule of 72", "rule-of-72")]
    [InlineData("bmi", "body-mass-index")]
    [InlineData("newton raphson", "newton-method-step")]
    [InlineData("escape velocity", "escape-velocity")]
    [InlineData("mortgage", "loan-payment")]
    public void Finds_the_expected_formula_first(string query, string expectedId)
    {
        var top = TopIds(query, 1);
        Assert.True(top.Count == 1 && top[0] == expectedId,
            $"'{query}' ranked first: {string.Join(", ", TopIds(query, 5))}");
    }

    [Theory]
    [InlineData("snel", "snells-law")]
    [InlineData("pythagorus", "pythagoras-theorem")]
    [InlineData("pytagoras", "pythagoras-theorem")]
    [InlineData("kinetc energy", "kinetic-energy")]
    [InlineData("quadratc formula", "quadratic-formula-plus")]
    [InlineData("dopler", "doppler-effect-sound")]
    [InlineData("hookes law", "hookes-law")]
    [InlineData("hooks law", "hookes-law")]
    public void Tolerates_one_typo(string query, string expectedId)
    {
        var top = TopIds(query, 3);
        Assert.True(top.Contains(expectedId), $"'{query}' gave: {string.Join(", ", top)}");
    }

    [Fact]
    public void Suvat_finds_all_five_equations_of_motion_first()
    {
        string[] suvat = ["suvat-v-u-a-t", "suvat-s-u-a-t", "suvat-v2-u2", "suvat-s-average", "suvat-s-v-a-t"];
        var top = TopIds("suvat", 5);
        Assert.Equal(suvat.Order(), top.Order());
    }

    [Fact]
    public void Half_life_finds_the_half_life_formulas()
    {
        var top = TopIds("half life", 4);
        Assert.Contains("half-life-decay-constant", top);
        Assert.Contains("half-life-remaining", top);
        Assert.Contains("first-order-half-life", top);
    }

    [Fact]
    public void Accents_are_ignored()
    {
        Assert.Equal(TopIds("snell", 3), TopIds("Snéll", 3));
        Assert.Equal("apery-constant", Lib.SearchConstants("Apéry").First().Id);
        Assert.Equal("apery-constant", Lib.SearchConstants("apery").First().Id);
    }

    [Fact]
    public void Typed_equations_are_recognised()
    {
        Assert.Equal("mass-energy-equivalence", TopIds("E=mc^2", 1).Single());
        Assert.Equal("ohms-law", TopIds("V = I*R", 1).Single());
        Assert.Equal("newtons-second-law", TopIds("F=ma", 1).Single());
    }

    [Fact]
    public void Category_names_and_variable_names_are_searchable()
    {
        Assert.All(Lib.Search("thermodynamics").Take(5), f => Assert.Equal("thermodynamics", f.Category));
        Assert.Contains("specific-heat-capacity", TopIds("specific heat capacity", 3));
        Assert.Contains("rlc-resonance", TopIds("resonant frequency", 3));
    }

    [Fact]
    public void Quadratic_finds_both_roots_and_the_discriminant()
    {
        var top = TopIds("quadratic", 4);
        Assert.Contains("quadratic-formula-plus", top);
        Assert.Contains("quadratic-formula-minus", top);
        Assert.Contains("quadratic-discriminant", top);
    }

    [Fact]
    public void Empty_query_returns_everything_and_nonsense_returns_nothing()
    {
        Assert.Equal(Lib.All.Count, Lib.Search("").Count);
        Assert.Equal(Lib.All.Count, Lib.Search("   ").Count);
        Assert.Empty(Lib.Search("xyzzyplugh"));
        Assert.Empty(Lib.Search("qwertyuiop asdf"));
    }

    [Fact]
    public void Filler_words_do_not_block_results()
    {
        Assert.Equal("pythagoras-theorem", TopIds("the formula for pythagoras", 1).Single());
        Assert.Equal("ideal-gas-law", TopIds("equation of the ideal gas law", 1).Single());
    }

    [Fact]
    public void Every_word_must_match()
    {
        // "ohm" matches Ohm's law, but nothing about Ohm's law mentions bananas.
        Assert.Empty(Lib.Search("ohm banana"));
    }

    [Fact]
    public void Max_results_and_scores_are_consistent()
    {
        Assert.Equal(3, Lib.Search("energy", 3).Count);
        var hits = Lib.SearchWithScores("energy");
        Assert.True(hits.Count > 5);
        for (int i = 1; i < hits.Count; i++) Assert.True(hits[i - 1].Score >= hits[i].Score);
        Assert.Equal(hits.Select(h => h.Formula.Id), Lib.Search("energy").Select(f => f.Id));
    }

    [Theory]
    [InlineData("speed of light", "speed-of-light")]
    [InlineData("k_B", "boltzmann-constant")]
    [InlineData("avogadro", "avogadro-constant")]
    [InlineData("planck", "planck-constant")]
    [InlineData("gas constant", "molar-gas-constant")]
    [InlineData("golden ratio", "golden-ratio")]
    [InlineData("G", "gravitational-constant")]
    [InlineData("g_n", "standard-gravity")]
    [InlineData("elementary charge", "elementary-charge")]
    public void Constant_search_finds_by_name_and_symbol(string query, string expectedId)
    {
        Assert.Equal(expectedId, Lib.SearchConstants(query).First().Id);
    }

    [Theory]
    [InlineData("Ohm's Law", "ohms law")]
    [InlineData("Schrödinger-équation", "schrodinger equation")]
    [InlineData("  E = m*c^2 ", "e m c 2")]
    [InlineData("ΔT, λ!", "δt λ")]
    public void Normalize_handles_case_accents_apostrophes_and_punctuation(string input, string expected)
    {
        Assert.Equal(expected, SearchText.Normalize(input));
    }

    [Theory]
    [InlineData("snel", "snell", true)]
    [InlineData("pythagorus", "pythagoras", true)]
    [InlineData("hte", "the", true)]
    [InlineData("abcd", "abcd", true)]
    [InlineData("abcd", "abdc", true)]
    [InlineData("abcd", "badc", false)]
    [InlineData("abcd", "ab", false)]
    [InlineData("kinetc", "kinetic", true)]
    [InlineData("kinetics", "kinetic", true)]
    [InlineData("kintec", "kinetic", false)]
    public void Within_one_edit(string a, string b, bool expected)
    {
        Assert.Equal(expected, SearchText.WithinOneEdit(a, b));
        Assert.Equal(expected, SearchText.WithinOneEdit(b, a));
    }
}
