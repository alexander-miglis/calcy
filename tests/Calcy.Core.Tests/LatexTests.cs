using Calcy.Core;
using Calcy.Core.Printing;

namespace Calcy.Core.Tests;

public class LatexTests
{
    private static string Latex(string input)
    {
        var s = new CalcSession();
        return LatexPrinter.ToLatex(s.EvaluateExpression(input));
    }

    [Theory]
    [InlineData("1/3 + 1/6", @"\frac{1}{2}")]
    [InlineData("-3/4", @"-\frac{3}{4}")]
    [InlineData("sqrt(72)", @"6 \sqrt{2}")]
    [InlineData("cbrt(2)", @"\sqrt[3]{2}")]
    [InlineData("pi/2", @"\frac{\pi}{2}")]
    [InlineData("x^2 - 5x + 6", @"x^{2} - 5 x + 6")]
    [InlineData("1/(2x)", @"\frac{1}{2 x}")]
    [InlineData("x^-2", @"\frac{1}{x^{2}}")]
    [InlineData("sin(x)^2", @"\sin^{2}\left(x\right)")]
    [InlineData("abs(x)", @"\left|x\right|")]
    [InlineData("{1, 2, 3}", @"\left\{1, 2, 3\right\}")]
    [InlineData("[[1, 2], [3, 4]]", @"\begin{bmatrix}1 & 2 \\ 3 & 4\end{bmatrix}")]
    [InlineData("x <= 3", @"x \leq 3")]
    [InlineData("theta + alpha", @"\alpha + \theta")]
    [InlineData("x_1 + x_2", @"x_{1} + x_{2}")]
    [InlineData("3 + 4i", "3 + 4i")]
    [InlineData("nCr(n, k)", @"\binom{n}{k}")]
    [InlineData("(x + 1)^3", @"\left(x + 1\right)^{3}")]
    [InlineData("e^x", "e^{x}")]
    public void Prints(string input, string expected) => Assert.Equal(expected, Latex(input));

    [Fact]
    public void Decimals_UseTimesTen()
    {
        var s = new CalcSession();
        Assert.Equal(@"6.02 \times 10^{23}", LatexPrinter.ToLatex(s.EvaluateExpression("6.02e23", approximate: true)));
    }

    [Fact]
    public void Piecewise_UsesCases()
    {
        var s = new CalcSession();
        var e = s.ParseExpression("{x < 0: -x, x}");
        Assert.Equal(@"\begin{cases}-x & \text{if } x < 0 \\ x & \text{otherwise}\end{cases}", LatexPrinter.ToLatex(e));
    }

    [Fact]
    public void LongNames_AreUpright()
    {
        Assert.Equal(@"\mathrm{speed}", LatexPrinter.Name("speed"));
        Assert.Equal(@"\theta", LatexPrinter.Name("θ"));
    }
}
