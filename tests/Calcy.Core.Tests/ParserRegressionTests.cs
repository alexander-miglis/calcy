using Calcy.Core;

namespace Calcy.Core.Tests;

/// <summary>Parser behaviours fixed after the first release of the engine.</summary>
public class ParserRegressionTests
{
    private static string Eval(string input, CalcSession? s = null)
    {
        s ??= new CalcSession();
        var r = s.Evaluate(input);
        Assert.False(r.IsError, $"{input}: {r.ErrorMessage}");
        return s.Print(r.Exact!);
    }

    [Theory]
    [InlineData("πr^2", "πr²")]
    [InlineData("2πr", "2πr")]
    [InlineData("ab^2", "ab²")]
    [InlineData("(ab)^2", "a²b²")]
    [InlineData("xy^2 + x^2y", "xy² + x²y")]
    [InlineData("abc^3", "abc³")]
    public void SplitNamesTakeThePowerOnTheLastLetter(string input, string expected) => Assert.Equal(expected, Eval(input));

    [Fact]
    public void PiInASplitNameIsTheConstant()
    {
        var s = new CalcSession();
        Eval("r = 3", s);
        Assert.Equal("9π", Eval("πr^2", s));
    }

    [Fact]
    public void UnknownLongNameBeforeABracketIsAnError()
    {
        var s = new CalcSession();
        var r = s.Evaluate("sqrtt(4)");
        Assert.True(r.IsError);
        Assert.Contains("no function called 'sqrtt'", r.ErrorMessage);
        Assert.Equal("sqrt(4)", r.Hint);
        Assert.Equal("ab·(c + d)", Eval("ab(c + d)"));
    }

    [Fact]
    public void GreekNamesArePublic() => Assert.Equal("θ", Calcy.Core.Parsing.Parser.GreekNames["theta"]);

    [Fact]
    public void ProductBeforeAPowerOfANumberPrintsADot() => Assert.Equal("2·3^x", Eval("2*3^x"));
}