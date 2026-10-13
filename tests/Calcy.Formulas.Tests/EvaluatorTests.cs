using Calcy.Formulas.Tests.Evaluation;

namespace Calcy.Formulas.Tests;

/// <summary>Self-tests of the test evaluator and solver, so that passing example tests mean something.</summary>
public class EvaluatorTests
{
    private static readonly Dictionary<string, double> Empty = new();

    [Theory]
    [InlineData("1 + 2*3", 7)]
    [InlineData("(1 + 2)*3", 9)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("2^-1", 0.5)]
    [InlineData("10/4/5", 0.5)]
    [InlineData("1.5e3 + 2E-1", 1500.2)]
    [InlineData("sqrt(16) + root(27, 3) + abs(-2)", 9)]
    [InlineData("root(-8, 3)", -2)]
    [InlineData("log(1000) + log(8, 2) + ln(e)", 7)]
    [InlineData("exp(0) + sin(pi/2) + cos(0) + tan(0)", 3)]
    [InlineData("asin(1) - pi/2 + acos(1) + atan(0)", 0)]
    [InlineData("cosh(0) + sinh(0) + tanh(0)", 1)]
    [InlineData("5!", 120)]
    [InlineData("3!^2", 36)]
    [InlineData("0!", 1)]
    public void Evaluates_numbers_operators_and_functions(string text, double expected)
    {
        Assert.Equal(expected, Expression.Parse(text).Evaluate(Empty), 9);
    }

    [Fact]
    public void Factorial_of_half_is_gamma_three_halves()
    {
        Assert.Equal(Math.Sqrt(Math.PI) / 2, Expression.Parse("0.5!").Evaluate(Empty), 10);
    }

    [Fact]
    public void Evaluates_unicode_and_subscripted_identifiers()
    {
        var values = new Dictionary<string, double> { ["v_0"] = 3, ["λ"] = 2, ["ΔT"] = 4, ["E_k"] = 5, ["Δν_Cs"] = 1 };
        var e = Expression.Parse("v_0*λ + ΔT/E_k - Δν_Cs");
        Assert.Equal(3 * 2 + 4.0 / 5 - 1, e.Evaluate(values), 12);
        Assert.Equal(new[] { "E_k", "v_0", "Δν_Cs", "ΔT", "λ" }.Order(StringComparer.Ordinal), e.Identifiers().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("2x")]
    [InlineData("a b")]
    [InlineData("sqrt 4")]
    [InlineData("f(x)")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("root(8)")]
    [InlineData("3 # 4")]
    public void Rejects_input_outside_the_restricted_syntax(string text)
    {
        Assert.Throws<FormatException>(() => Expression.Parse(text));
    }

    [Fact]
    public void Equation_needs_exactly_one_equals_sign()
    {
        Assert.Throws<FormatException>(() => Expression.ParseEquation("a + b"));
        Assert.Throws<FormatException>(() => Expression.ParseEquation("a = b = c"));
    }

    [Fact]
    public void Solver_finds_all_real_roots_of_a_quadratic()
    {
        var roots = Solver.FindRoots(x => x * x - 5 * x + 6);
        Assert.Equal(2, roots.Count);
        Assert.Equal(2, roots[0], 10);
        Assert.Equal(3, roots[1], 10);
    }

    [Fact]
    public void Solver_handles_tiny_and_huge_roots()
    {
        Assert.Contains(Solver.FindRoots(x => x / 6.62607015e-34 - 1), r => Math.Abs(r - 6.62607015e-34) < 1e-45);
        Assert.Contains(Solver.FindRoots(x => x - 6.02214076e23), r => Math.Abs(r / 6.02214076e23 - 1) < 1e-12);
    }

    [Fact]
    public void Solver_ignores_poles()
    {
        var roots = Solver.FindRoots(x => 1 / x - 0.2);
        Assert.Single(roots);
        Assert.Equal(5, roots[0], 10);
        Assert.Empty(Solver.FindRoots(x => 1 / (x - 1)));
    }

    [Fact]
    public void Solver_skips_regions_where_the_function_is_undefined()
    {
        var roots = Solver.FindRoots(x => Math.Sqrt(x) - 3);
        Assert.Single(roots);
        Assert.Equal(9, roots[0], 9);
    }
}
