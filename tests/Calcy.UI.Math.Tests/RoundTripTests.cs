using Calcy.Core.Expressions;
using Calcy.Core.Parsing;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Tests;

/// <summary>Text to model to text: the serialized text must parse back to the same expression tree.</summary>
public class RoundTripTests
{
    internal static readonly IParseContext Ctx = new SimpleParseContext(MathParsing.DefaultFunctionNames.Append("f").Append("g"));

    public static TheoryData<string> Inputs => new()
    {
        "1", "x", "2x", "-x", "-2x", "x + 1", "x - 1", "a - b - c", "a + -2x", "x^2 + 2x + 1", "3x^2 - 2x + 7",
        "a*b", "a*b*c", "2*a*b", "a*(b*c)", "a*2x", "-a*b", "-(a*b)", "-(a + b)", "(a + b)(c + d)", "2(x + 1)",
        "1/2", "a/b", "(a + b)/(c + d)", "1/(2x)", "2x/3", "-2x/3", "-x/2", "a/b/c", "a/b*c", "c*a/b", "x/2 + 1",
        "(1/2)x", "1/2*x", "(a/b)^2", "a/(b/c)", "a/b^2", "a^-1", "x^(-1)", "x^(1/2)", "x^(n + 1)", "x^y^z", "(x^y)^z",
        "2^x", "e^(-x^2)", "-x^2", "(-2)^2", "x!", "(x + 1)!", "(n!)!", "n!!", "50%", "30°", "sin(x)", "sin(x)^2", "sin x",
        "sin(2x)", "2sin(x)cos(x)", "sqrt(2)", "sqrt(x + 1)", "√x", "cbrt(8)", "root(x, 5)", "abs(x - 1)", "|x|", "log(x)",
        "log(x, 2)", "ln(x)", "exp(x)", "integrate(x^2, x, 0, 1)", "integrate(sin(x), x)", "diff(x^3, x)",
        "sum(k^2, k, 1, 10)", "product(k, k, 1, 5)", "prod(k, k, 1, 5)", "limit(sin(x)/x, x, 0)", "sum({1, 2, 3})",
        "{1, 2, 3}", "[1, 2, 3]", "{}", "[[1, 2], [3, 4]]", "[[a, b, c]]", "[[1], [2]]", "(1, 2)", "L[2]", "M[1, 2]",
        "{x < 0: -x, x}", "{x < 0: -x, x < 1: x^2, 1}", "x^2 {0 < x < 5}", "x > 0 and x < 1", "x < 0 or x > 1",
        "not x > 1", "a < b < c", "x <= 1", "x >= 1", "x != 1", "x == 1", "x^2 = 4", "f'(x)", "f''(2)", "v_0 + a*t",
        "v_0^2", "x_1*x_2", "pi", "2pi", "pi*r^2", "e", "inf", "-inf", "i", "3 + 4i", "theta", "2θ", "α + β", "0.5",
        "1.25x", ".5", "1e5", "\"hello\"", "true", "x => x^2", "(x, y) => x + y", "map(x => x^2, {1, 2})", "1..5",
        "7 mod 3", "mod(7, 3)", "nCr(5, 2)", "a = 5", "f(x) = x^2", "f(x, y) := x*y", "5 -> a", "ans + 1",
        "x(x + 1)", "2(x + 1)(x - 1)", "(x + 1)/2 + (x - 1)/3", "1/x + 1/y", "x^2/2", "(2x + 1)^3", "-(x^2)",
        "sqrt(x)^3", "abs(x)^2", "log(x, 2)^2", "1/sqrt(2)", "sqrt(2)/2", "(1 + sqrt(5))/2", "a/b + c/d",
        "integrate(x^2, x, 0, 1) + 1", "2integrate(x, x, 0, 1)", "diff(sin(x), x)^2", "sum(1/k^2, k, 1, inf)",
        "x*y/z", "x/y/z/w", "2/3/4", "-1/x", "(-1)/x", "x*-2", "x/-2", "x^2!", "-x!", "2^-1", "10^-3",
        "matrix", "det([[1, 2], [3, 4]])", "{1, 2}[1]", "[[1, 2], [3, 4]]^2", "if(x > 0, 1, 0)",
    };

    [Theory]
    [MemberData(nameof(Inputs))]
    public void TextToModelToTextParsesToTheSameTree(string input)
    {
        var expected = Parser.ParseStatement(input, Ctx);
        var state = new MathEditorState(Ctx);
        Assert.True(state.SetText(input), $"'{input}' should parse");
        string text = state.Text;
        var actual = Parser.ParseStatement(text, Ctx);
        AssertSameStatement(expected, actual, input, text);
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void SerializingTwiceIsStable(string input)
    {
        var state = new MathEditorState(Ctx);
        state.SetText(input);
        string once = state.Text;
        var again = new MathEditorState(Ctx);
        again.SetText(once);
        Assert.Equal(once, again.Text);
    }

    internal static void AssertSameStatement(Statement expected, Statement actual, string input, string text)
    {
        switch (expected)
        {
            case ExprStatement e:
                var a = Assert.IsType<ExprStatement>(actual);
                Assert.True(e.Expression.Equals(a.Expression), $"'{input}' serialized as '{text}': {Describe(e.Expression)} vs {Describe(a.Expression)}");
                break;
            case AssignStatement s:
                var b = Assert.IsType<AssignStatement>(actual);
                Assert.Equal(s.Name, b.Name);
                Assert.True(s.Value.Equals(b.Value), $"'{input}' serialized as '{text}'");
                break;
            case FunctionDefStatement f:
                var c = Assert.IsType<FunctionDefStatement>(actual);
                Assert.Equal(f.Name, c.Name);
                Assert.True(f.Parameters.SequenceEqual(c.Parameters));
                Assert.True(f.Body.Equals(c.Body), $"'{input}' serialized as '{text}'");
                break;
        }
    }

    internal static string Describe(Expr e) => e switch
    {
        NumberExpr n => n.Value.ToString(),
        SymbolExpr s => s.Name,
        ConstantExpr c => c.Constant.ToString(),
        CallExpr c => c.Name + "(" + string.Join(", ", c.Args.Select(Describe)) + ")",
        _ => e.Kind + "[" + string.Join(", ", e.Children.Select(Describe)) + "]",
    };
}
