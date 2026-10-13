using Calcy.Formulas;
using Calcy.Modes.Formulas.Logic;
using static Calcy.Modes.Formulas.Tests.TestSupport;

namespace Calcy.Modes.Formulas.Tests;

/// <summary>Solving from box texts: every SUVAT variable, constants, usual values, identities, limits and the USE text.</summary>
public class FormulaCalculatorTests
{
    private readonly FormulaCalculator _calc = Calculator();

    // v = u + a t with v = 25, u = 5, a = 2.5, t = 8.
    [Theory]
    [InlineData("v", 25.0, "v = 25 m/s")]
    [InlineData("u", 5.0, "u = 5 m/s")]
    [InlineData("a", 2.5, "a = 2.5 m/s²")]
    [InlineData("t", 8.0, "t = 8 s")]
    public void SolvesSuvatForEachVariable(string unknown, double expected, string headline)
    {
        var all = Texts(("v", "25"), ("u", "5"), ("a", "2.5"), ("t", "8"));
        all.Remove(unknown);
        var answer = Answer(_calc.Solve(Formula("suvat-v-u-a-t"), all, unknown));
        Assert.Equal(AnswerKind.Solved, answer.Kind);
        Assert.Equal(unknown, answer.Symbol);
        Near(expected, answer.Values.Single().Approx);
        Assert.Equal(headline, answer.Headline);
        Assert.Equal(headline, answer.CopyText);
        Near(expected, Eval(answer.Uses.Single().Expression));
    }

    // s = u t + a t^2 / 2 with s = 25, u = 10, t = 5, a = -2 (t is a double root, found exactly).
    [Theory]
    [InlineData("s", 25.0)]
    [InlineData("u", 10.0)]
    [InlineData("t", 5.0)]
    [InlineData("a", -2.0)]
    public void SolvesSuvatDisplacementForEachVariable(string unknown, double expected)
    {
        var all = Texts(("s", "25"), ("u", "10"), ("t", "5"), ("a", "-2"));
        all.Remove(unknown);
        var answer = Answer(_calc.Solve(Formula("suvat-s-u-a-t"), all, unknown));
        Near(expected, answer.Values[0].Approx);
    }

    [Fact]
    public void ExactAnswersShowTheirExactForm()
    {
        var answer = Answer(_calc.Solve(Formula("suvat-v-u-a-t"), Texts(("v", "26"), ("u", "5"), ("t", "9")), "a"));
        Assert.Equal("a = 2.333333333 m/s²", answer.Headline);
        Assert.Equal("a = 7/3", answer.ExactLine);
        Assert.Equal("7/3", answer.Uses[0].Expression);
        Near(7.0 / 3, Eval(answer.Uses[0].Expression));
    }

    [Fact]
    public void ExpressionsWorkAsInputs()
    {
        // Pythagoras with legs 1 and 1: the hypotenuse is exactly sqrt(2).
        var p = Answer(_calc.Solve(Formula("pythagoras-theorem"), Texts(("a", "1"), ("b", "2/2")), "c"));
        Assert.Equal("c = 1.414213562 m", p.Headline);
        Assert.Equal("c = √2", p.ExactLine);
        Near(Math.Sqrt(2), Eval(p.Uses[0].Expression));

        // Degrees and scientific notation are fine.
        var range = Answer(_calc.Solve(Formula("projectile-range"), Texts(("v", "2e1"), ("θ", "45°"), ("g", "9.81")), "R"));
        Near(40.7747196738, range.Values[0].Approx, 1e-9);
    }

    [Fact]
    public void PhotonEnergyUsesPlanckConstant()
    {
        var f = Formula("photon-energy-frequency");
        Assert.Equal(["E", "f"], f.Variables.Select(v => v.Symbol));
        var answer = Answer(_calc.Solve(f, Texts(("f", "5.5e14")), "E"));
        Near(6.62607015e-34 * 5.5e14, answer.Values[0].Approx, 1e-12);
        Assert.Equal("E = 3.644338583×10⁻¹⁹ J", answer.Headline);
        Assert.Equal("f = 5.5e14 Hz", answer.GivenLine);
        Near(answer.Values[0].Approx, Eval(answer.Uses[0].Expression), 1e-15);

        // And back again: the frequency of a 3.6443385825e-19 J photon.
        var back = Answer(_calc.Solve(f, Texts(("E", answer.Uses[0].Expression)), "f"));
        Near(5.5e14, back.Values[0].Approx, 1e-12);
    }

    [Fact]
    public void MassEnergyUsesTheSpeedOfLight()
    {
        var answer = Answer(_calc.Solve(Formula("mass-energy-equivalence"), Texts(("m", "0.001")), "E"));
        Near(0.001 * 299792458.0 * 299792458.0, answer.Values[0].Approx, 1e-12);
    }

    [Fact]
    public void EmptyBoxesUseTheUsualValue()
    {
        var f = Formula("projectile-range");
        Assert.Equal("9.81", InputReader.Placeholder(f.GetVariable("g")!));
        Assert.Null(InputReader.Placeholder(f.GetVariable("v")!));
        var answer = Answer(_calc.Solve(f, Texts(("v", "20"), ("θ", "pi/4"), ("g", "  ")), "R"));
        Near(400 / 9.81, answer.Values[0].Approx, 1e-12);
        Assert.Contains(answer.Notes, n => n.Contains("g = 9.81", StringComparison.Ordinal) && n.Contains("usual value", StringComparison.Ordinal));
        Assert.Contains(answer.Given, g => g.Symbol == "g" && g.IsUsual && g.Text == "9.81");
        Assert.Equal("v = 20 m/s, θ = pi/4 rad, g = 9.81 m/s² (usual)", answer.GivenLine);
    }

    [Fact]
    public void MissingValuesAreNamed()
    {
        var f = Formula("suvat-v-u-a-t");
        var one = _calc.Solve(f, Texts(("v", "25"), ("t", "8")), "a");
        Assert.Equal(OutcomeKind.NeedsInput, one.Kind);
        Assert.Equal("Fill in u (initial velocity) to find a", one.Message);
        var two = _calc.Solve(f, Texts(), "a");
        Assert.Equal("Fill in v, u and t to find a", two.Message);
        Assert.Null(two.Answer);
    }

    [Fact]
    public void BadInputsAreReportedPerBox()
    {
        var f = Formula("suvat-v-u-a-t");
        var outcome = _calc.Solve(f, Texts(("v", "25"), ("u", "five"), ("t", "-3")), "a");
        Assert.Equal(OutcomeKind.Invalid, outcome.Kind);
        Assert.Contains("has no value", outcome.InputErrors["u"]);
        Assert.Equal("t must be at least 0", outcome.InputErrors["t"]);
        Assert.StartsWith("u: ", outcome.Message);

        var syntax = _calc.Solve(f, Texts(("v", "25"), ("u", "5*"), ("t", "8")), "a");
        Assert.Equal(OutcomeKind.Invalid, syntax.Kind);
        Assert.True(syntax.InputErrors.ContainsKey("u"));

        var positive = _calc.Solve(Formula("pythagoras-theorem"), Texts(("a", "0"), ("b", "1")), "c");
        Assert.Equal("a must be more than 0", positive.InputErrors["a"]);
    }

    [Fact]
    public void SolutionsOutsideTheLimitsAreDropped()
    {
        // s = u t + a t^2 / 2 with u = 0, a = 10, s = 5: t = 1 or t = -1, and a time cannot be negative.
        var answer = Answer(_calc.Solve(Formula("suvat-s-u-a-t"), Texts(("u", "0"), ("a", "10"), ("s", "5")), "t"));
        Assert.Equal("t = 1 s", answer.Headline);
        Assert.Contains(answer.Notes, n => n.StartsWith("Left out 1 solution", StringComparison.Ordinal));

        // A ball thrown up at 20 m/s passes 15 m twice.
        var twice = Answer(_calc.Solve(Formula("suvat-s-u-a-t"), Texts(("u", "20"), ("a", "-10"), ("s", "15")), "t"));
        Assert.Equal("t = 1 s or t = 3 s", twice.Headline);
        Assert.Equal(2, twice.Uses.Count);

        // v = u + a t with v below u and a positive needs a negative time: no valid answer.
        var none = _calc.Solve(Formula("suvat-v-u-a-t"), Texts(("v", "5"), ("u", "25"), ("a", "4")), "t");
        Assert.Equal(OutcomeKind.Failed, none.Kind);
        Assert.Contains("No valid value of t", none.Message);
    }

    [Fact]
    public void PeriodicEquationsFindThePrincipalAngle()
    {
        // Exact inputs make the algebra module return general solutions (θ = 2πn + ...); the principal one is kept.
        var slit = Answer(_calc.Solve(Formula("single-slit-minimum"), Texts(("a", "0.0001"), ("n", "1"), ("λ", "633e-9")), "θ"));
        Near(Math.Asin(6.33e-3), slit.Values.Single().Approx, 1e-9);
        var grating = Answer(_calc.Solve(Formula("diffraction-grating"), Texts(("d", "1/600000"), ("n", "1"), ("λ", "589e-9")), "θ"));
        Near(Math.Asin(589e-9 * 600000), grating.Values.Single().Approx, 1e-9);
    }

    [Fact]
    public void BothSignsComePositiveFirst()
    {
        var answer = Answer(_calc.Solve(Formula("suvat-v2-u2"), Texts(("u", "0"), ("a", "2"), ("s", "9")), "v"));
        Assert.Equal("v = 6 m/s or v = -6 m/s", answer.Headline);
        Assert.Equal(["6", "-6"], answer.Uses.Select(u => u.Expression));
    }

    [Fact]
    public void IdentitiesAreCheckedNotSolved()
    {
        var f = Formula("pythagorean-identity");
        Assert.True(f.IsIdentity);
        var outcome = _calc.Run(new FormulaJob(f, Texts(("θ", "0.7")), null));
        var answer = Answer(outcome);
        Assert.Equal(AnswerKind.Checked, answer.Kind);
        Assert.True(answer.Holds);
        Assert.Equal("both sides = 1", answer.Headline);
        Assert.Equal("Pythagorean identity: both sides = 1 for θ = 0.7 rad", answer.CopyText);
        Near(1, Eval(answer.Uses[0].Expression), 1e-12);

        var solve = _calc.Solve(f, Texts(("θ", "0.7")), "θ");
        Assert.Equal(OutcomeKind.Failed, solve.Kind);
        Assert.Contains("CHECK", solve.Message);

        var missing = _calc.Check(f, Texts());
        Assert.Equal(OutcomeKind.NeedsInput, missing.Kind);
        Assert.Equal("Fill in θ (angle) to check both sides", missing.Message);
    }

    [Fact]
    public void IdentityOutsideItsDomainSaysSo()
    {
        // ln(x) / ln(b) with b = 1 divides by zero.
        var outcome = _calc.Check(Formula("log-change-of-base"), Texts(("x", "8"), ("b", "1")));
        Assert.NotEqual(OutcomeKind.Answered, outcome.Kind);
        Assert.False(string.IsNullOrEmpty(outcome.Message));
    }

    [Fact]
    public void CancelledWorkStopsQuietly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var outcome = _calc.Solve(Formula("suvat-v-u-a-t"), Texts(("v", "25"), ("u", "5"), ("t", "8")), "a", cts.Token);
        Assert.Equal(OutcomeKind.Cancelled, outcome.Kind);
    }

    [Fact]
    public void DefaultUnknownIsTheExamplesUnknown()
    {
        Assert.Equal("a", FormulaCalculator.DefaultUnknown(Formula("suvat-v-u-a-t")));
        Assert.Equal("b", FormulaCalculator.DefaultUnknown(Formula("pythagoras-theorem")));
    }

    public static IEnumerable<object[]> Examples() =>
        Library.All.Where(f => f.Example is not null).Select(f => new object[] { f.Id });

    /// <summary>TRY EXAMPLE on every formula: the box texts it fills in give the worked example's answer.</summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryWorkedExampleWorksThroughTheBoxes(string id)
    {
        var f = Formula(id);
        var ex = f.Example!;
        var outcome = _calc.Run(new FormulaJob(f, FormulaTexts.ExampleInputs(f), f.IsIdentity ? null : ex.SolveFor));
        var answer = Answer(outcome);
        double tolerance = Math.Max(ex.Tolerance, 1e-9 * Math.Abs(ex.Result));
        if (f.IsIdentity)
        {
            Assert.True(answer.Holds, id);
            Assert.True(Math.Abs(answer.Left!.Approx - ex.Result) <= tolerance, $"{id}: {answer.Left.Approx} vs {ex.Result}");
            return;
        }
        Assert.Contains(answer.Values, v => Math.Abs(v.Approx - ex.Result) <= tolerance);
        // USE sends an expression that Advanced evaluates to the same number.
        foreach (var use in answer.Uses)
        {
            var value = answer.Values.First(v => v.Use == use.Expression).Approx;
            Near(value, Eval(use.Expression), 1e-15);
        }
    }
}
