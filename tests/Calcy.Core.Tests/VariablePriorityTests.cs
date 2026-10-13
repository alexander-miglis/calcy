using Calcy.Core;

namespace Calcy.Core.Tests;

public class VariablePriorityTests
{
    [Fact]
    public void StoredVariableBeatsBuiltinFunctionName()
    {
        var s = new CalcSession();
        var a = s.Evaluate("C = [[1, 2], [3, 4]]");
        Assert.False(a.IsError, a.ErrorMessage);
        Assert.True(s.Env.Variables.ContainsKey("C"), string.Join(",", s.Env.Variables.Keys));
        Assert.False(s.IsFunction("C"));
        var r = s.Evaluate("C^-1");
        Assert.False(r.IsError, r.ErrorMessage);
        Assert.Equal("[[-2, 1], [3/2, -1/2]]", ExprPrinterInput(r));
        Assert.Equal("10", s.Print(s.Evaluate("nCr(5, 2)").Exact!));
    }

    [Fact]
    public void TransposeSuperscript()
    {
        var s = new CalcSession();
        s.Evaluate("A = [[1, 2], [3, 4]]");
        var r = s.Evaluate("Aᵀ");
        Assert.False(r.IsError, r.ErrorMessage);
        Assert.Equal("[[1, 3], [2, 4]]", ExprPrinterInput(r));
    }

    private static string ExprPrinterInput(CalcResult r) => Calcy.Core.Printing.ExprPrinter.ToInput(r.Exact!);

    [Fact]
    public void PostfixOnASplitNameBelongsToTheLastLetter()
    {
        var s = new CalcSession();
        s.Evaluate("A = [[1, 2], [3, 4]]");
        s.Evaluate("B = [[0, 1], [1, 0]]");
        var r = s.Evaluate("ABᵀ");
        Assert.False(r.IsError, r.ErrorMessage);
        // A·Bᵀ = [[2, 1], [4, 3]]; (A·B)ᵀ would be [[2, 4], [1, 3]].
        Assert.Equal("[[2, 1], [4, 3]]", ExprPrinterInput(r));
        Assert.Equal("2·3!", s.Print(s.Parse("2·3!") is Calcy.Core.Parsing.ExprStatement e ? e.Expression : throw new InvalidOperationException()));
    }
}