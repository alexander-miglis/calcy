using Calcy.Modes.Formulas.Logic;
using Calcy.UI.Services;
using static Calcy.Modes.Formulas.Tests.TestSupport;

namespace Calcy.Modes.Formulas.Tests;

/// <summary>Plain-text symbols and numbers, constant texts, cards and the remembered state.</summary>
public class TextAndStateTests
{
    [Theory]
    [InlineData("v_0", "v₀")]
    [InlineData("x_12", "x₁₂")]
    [InlineData("E_k", "Eₖ")]
    [InlineData("k_B", "k_B")]
    [InlineData("h_bar", "ħ")]
    [InlineData("pi", "π")]
    [InlineData("Δx", "Δx")]
    [InlineData("v", "v")]
    [InlineData("a_", "a_")]
    public void SymbolsReadNaturally(string symbol, string expected)
    {
        Assert.Equal(expected, SymbolText.Plain(symbol));
    }

    [Theory]
    [InlineData(2.5, "2.5")]
    [InlineData(25, "25")]
    [InlineData(3.6443385825e-19, "3.6443385825e-19")]
    [InlineData(6.02214076e23, "6.02214076e23")]
    [InlineData(5.5e14, "5.5e14")]
    [InlineData(4.5e-7, "4.5e-7")]
    [InlineData(1e-12, "1e-12")]
    [InlineData(299792458, "299792458")]
    [InlineData(1000000, "1000000")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(-6, "-6")]
    [InlineData(0, "0")]
    public void LiteralsReadBackAsTheSameNumber(double value, string expected)
    {
        Assert.Equal(expected, SymbolText.Literal(value));
        Assert.Equal(value, Eval(SymbolText.Literal(value)));
    }

    [Fact]
    public void UnitsFollowNumbers()
    {
        Assert.Equal("2.5 m/s²", SymbolText.WithUnit("2.5", "m/s²"));
        Assert.Equal("30°", SymbolText.WithUnit("30", "°"));
        Assert.Equal("7", SymbolText.WithUnit("7", ""));
        Assert.Equal("v₀ = 3 m/s", SymbolText.Equation("v_0", "3", "m/s"));
    }

    [Fact]
    public void ConstantTexts()
    {
        var c = Library.GetConstant("c")!;
        Assert.Equal("299792458 m/s", ConstantText.Value(c));
        Assert.Equal("exact", ConstantText.Precision(c));
        Assert.Equal("EXACT", ConstantText.Tag(c));
        Assert.Equal("299792458", ConstantText.Use(c));

        var g = Library.GetConstant("G")!;
        Assert.Equal("6.6743×10⁻¹¹ m³/(kg·s²)", ConstantText.Value(g));
        Assert.Equal("± 1.5×10⁻¹⁵ m³/(kg·s²)", ConstantText.Precision(g));
        Assert.Null(ConstantText.Tag(g));
        Assert.Equal(6.6743e-11, Eval(ConstantText.Use(g)));

        Assert.Equal("TYPICAL", ConstantText.Tag(Library.GetConstant("v_sound")!));
        Assert.Equal("pi", ConstantText.Use(Library.GetConstant("pi")!));
        Assert.Equal("ħ = 1.054571817×10⁻³⁴ J·s (exact)", ConstantText.Describe(Library.GetConstant("h_bar")!));
        Assert.All(Library.Constants.Where(k => k.Symbol is not ("pi" or "e")), k => Assert.Equal(k.Value, Eval(ConstantText.Use(k))));
        Assert.Equal(Math.PI, Eval(ConstantText.Use(Library.GetConstant("pi")!)), 15);
    }

    [Fact]
    public void ExampleTexts()
    {
        var f = Formula("suvat-v-u-a-t");
        Assert.Equal(["v = 25 m/s", "u = 5 m/s", "t = 8 s"], FormulaTexts.ExampleGiven(f));
        Assert.Equal("a = 2.5 m/s²", FormulaTexts.ExampleAnswer(f));
        Assert.Equal(new Dictionary<string, string> { ["v"] = "25", ["u"] = "5", ["t"] = "8" }, FormulaTexts.ExampleInputs(f));
        Assert.Equal("both sides = 1", FormulaTexts.ExampleAnswer(Formula("pythagorean-identity")));
        Assert.Contains("Must be at least 0", FormulaTexts.VariableTip(f.GetVariable("t")!));
        Assert.Contains("usual value 9.81", FormulaTexts.VariableTip(Formula("projectile-range").GetVariable("g")!));
    }

    [Fact]
    public void EquationKeepsTheAuthorsOrder()
    {
        var session = Session();
        foreach (var f in Library.All) Assert.NotNull(FormulaTexts.Equation(session, f));
        // What the screen shows: display conventions (implicit products) in the author's term order.
        string Shown(string id) => Calcy.UI.Math.Model.MathSerializer.Serialize(
            Calcy.UI.Math.Model.MathDisplayBuilder.FromExpr(FormulaTexts.Equation(session, Formula(id))!, null, keepTermOrder: true));
        Assert.Equal("v=u+at", Shown("suvat-v-u-a-t"));
        Assert.Equal("s=ut+at^2/2", Shown("suvat-s-u-a-t"));
        Assert.Equal("E=mc^2", Shown("mass-energy-equivalence"));
        Assert.Equal("E=hc/λ", Shown("photon-energy-wavelength"));
    }

    [Fact]
    public void LimitsInWords()
    {
        var f = Formula("suvat-v-u-a-t");
        Assert.Equal("at least 0", InputReader.LimitsText(f.GetVariable("t")!));
        Assert.Equal("", InputReader.LimitsText(f.GetVariable("v")!));
        Assert.Equal("more than 0", InputReader.LimitsText(Formula("pythagoras-theorem").GetVariable("c")!));
    }

    [Fact]
    public void CardsKeepTheAnswer()
    {
        var calc = Calculator();
        var texts = Texts(("v", "25"), ("u", "5"), ("t", "8"), ("a", ""));
        var answer = Answer(calc.Solve(Formula("suvat-v-u-a-t"), texts, "a"));
        var state = new FormulasState();
        var card = state.AddCard(answer, texts, new DateTime(2026, 10, 13, 14, 2, 0));
        Assert.Equal(1, card.Number);
        Assert.Equal("#1  MECHANICS", card.Title);
        Assert.Equal(["a = 2.5 m/s²"], card.Results);
        Assert.Equal("v = 25 m/s, u = 5 m/s, t = 8 s", card.Given);
        Assert.Equal("a = 2.5 m/s²", card.CopyText);
        Assert.Equal("a", card.SolveFor);
        Assert.Equal(3, card.Values.Count);
        Assert.Equal("5/2", card.Uses.Single().Expression);
        Assert.Equal(2.5, Eval(card.Uses[0].Expression));
        var second = state.AddCard(answer, texts, DateTime.Now);
        Assert.Equal(2, second.Number);
        Assert.Same(second, state.Cards[0]);
    }

    [Fact]
    public void StateSurvivesThePreferencesFile()
    {
        var calc = Calculator();
        var texts = Texts(("f", "5.5e14"));
        var answer = Answer(calc.Solve(Formula("photon-energy-frequency"), texts, "E"));
        var state = new FormulasState { FormulaId = "photon-energy-frequency", Tab = FormulasTab.Constants };
        var saved = state.InputsFor("photon-energy-frequency");
        saved.SolveFor = "E";
        saved.Values["f"] = "5.5e14";
        state.AddCard(answer, texts, DateTime.Now);

        var prefs = new Preferences();
        prefs.SetModeState("formulas", state);
        var json = System.Text.Json.JsonSerializer.Serialize(prefs.ModeState);
        prefs.ModeState = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json)!;
        var back = prefs.GetModeState<FormulasState>("formulas")!.Normalize(Library);

        Assert.Equal("photon-energy-frequency", back.FormulaId);
        Assert.Equal(FormulasTab.Constants, back.Tab);
        Assert.Equal("5.5e14", back.InputsFor("photon-energy-frequency").Values["f"]);
        Assert.Equal("E", back.InputsFor("photon-energy-frequency").SolveFor);
        Assert.Equal("E = 3.644338583×10⁻¹⁹ J", back.Cards.Single().Results.Single());
        Assert.Equal(2, back.NextNumber);
    }

    [Fact]
    public void NormalizeRepairsOldState()
    {
        var state = new FormulasState
        {
            FormulaId = "gone",
            Inputs = [new SavedInputs { FormulaId = "gone" }, new SavedInputs { FormulaId = "suvat-v-u-a-t", SolveFor = "zz", Values = null! }],
            Cards = [new FormulaCard { Number = 7, Results = null!, Notes = null!, Uses = null!, Values = null! }],
            NextNumber = 3,
        }.Normalize(Library);
        Assert.Equal(FormulasState.DefaultFormulaId, state.FormulaId);
        Assert.Single(state.Inputs);
        Assert.Null(state.Inputs[0].SolveFor);
        Assert.NotNull(state.Inputs[0].Values);
        Assert.Equal(8, state.NextNumber);
        Assert.NotNull(state.Cards[0].Results);
    }

    [Fact]
    public void SavedInputsAreMostRecentFirstAndCapped()
    {
        var state = new FormulasState();
        foreach (var f in Library.All.Take(FormulasState.MaxSavedInputs + 20)) state.InputsFor(f.Id).Values["x"] = "1";
        Assert.Equal(FormulasState.MaxSavedInputs, state.Inputs.Count);
        var first = Library.All[FormulasState.MaxSavedInputs + 5].Id;
        state.InputsFor(first);
        Assert.Equal(first, state.Inputs[0].FormulaId);
    }
}
