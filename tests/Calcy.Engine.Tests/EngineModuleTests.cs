using System.Globalization;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Engine;

namespace Calcy.Engine.Tests;

public class EngineModuleTests
{
    private static readonly CalcSession Session = CalcyEngine.CreateSession();

    private static string Eval(string input)
    {
        var r = Session.Evaluate(input);
        Assert.False(r.IsError, $"{input}: {r.ErrorMessage}");
        return Session.Print(r.Exact!);
    }

    private static double Num(string input)
    {
        var r = Session.Evaluate(input);
        Assert.False(r.IsError, $"{input}: {r.ErrorMessage}");
        var e = r.Approx ?? r.Exact!;
        Assert.True(e is NumberExpr, $"{input} gave {Session.Print(e)}");
        return ((NumberExpr)e).Value.Re;
    }

    private static void Close(double expected, string input, double tol = 1e-9) =>
        Assert.True(Math.Abs(Num(input) - expected) <= tol * Math.Max(1, Math.Abs(expected)), $"{input} = {Num(input).ToString("R", CultureInfo.InvariantCulture)}, expected {expected}");

    private static string Error(string input)
    {
        var r = Session.Evaluate(input);
        Assert.True(r.IsError, $"{input} should fail");
        return r.ErrorMessage!;
    }

    // ---------------------------------------------------------------- numerics

    [Fact] public void NSolve_Guess() => Assert.Equal("x = 0.7390851332", Eval("nsolve(cos(x) = x, x, 1)"));
    [Fact] public void NSolve_NoGuess() => Assert.Equal("x = 0.7390851332", Eval("nsolve(cos(x) = x, x)"));
    [Fact] public void NSolve_AllRootsInInterval() => Assert.Equal("x = -1.414213562 or x = 1.414213562", Eval("nsolve(x^2 = 2, x, -5, 5)"));
    [Fact] public void NSolve_System() => Assert.Equal("x = 2 and y = 1", Eval("nsolve({x + y = 3, x - y = 1}, {x, y}, {0, 0})"));
    [Fact] public void NSolve_NoSolution() => Assert.Contains("No", Error("nsolve(x^2 + 1 = 0, x, -5, 5)"));
    [Fact] public void NInt_Gaussian() => Close(Math.Sqrt(Math.PI), "nint(e^(-x^2), x, -inf, inf)");
    [Fact] public void NInt_Singular() => Close(2, "nint(1/sqrt(x), x, 0, 1)", 1e-8);
    [Fact] public void NDeriv() => Close(4 * (Math.Log(2) + 1), "nderiv(x^x, x, 2)", 1e-7);
    [Fact] public void FMin() => Close(2, "fmin(x^2 - 4x, x, -10, 10)", 1e-7);
    [Fact] public void FMax() => Close(Math.PI / 2, "fmax(sin(x), x, 0, 3)", 1e-7);
    [Fact] public void NSum_Infinite() => Close(Math.PI * Math.PI / 6, "nsum(1/k^2, k, 1, inf)", 1e-8);
    [Fact] public void OdeSolve_Exponential() => Close(Math.E, "odesolve(y, t, y, 0, 1, 1)", 1e-8);
    [Fact] public void OdeSolve_System() => Assert.Equal("{0, -1}", Eval("round(odesolve({y2, -y1}, t, {y1, y2}, 0, {0, 1}, pi), 6)"));
    [Fact] public void PolyRoots() => Assert.Equal("{-1.414213562, 1.414213562}", Eval("polyroots({1, 0, -2})"));
    [Fact] public void PolyRoots_Complex() => Assert.Equal("{-i, i}", Eval("polyroots({1, 0, 1})"));
    [Fact] public void Eigenvalues() => Assert.Equal("{1, 3}", Eval("eigenvals([[2, 1], [1, 2]])"));
    [Fact] public void Eigenvalues_Rotation() => Assert.Equal("{-i, i}", Eval("eigenvals([[0, -1], [1, 0]])"));
    [Fact] public void Svd() => Assert.Equal("{6.708203932, 2.236067977}", Eval("svd([[3, 0], [4, 5]])"));
    [Fact] public void Cond() => Close(14.933034373659268, "cond([[1, 2], [3, 4]])");
    [Fact] public void Fft() => Assert.Equal("{1, 1, 1, 1}", Eval("fft({1, 0, 0, 0})"));

    // ---------------------------------------------------------------- probability

    [Fact] public void NormalCdf() => Close(0.9500042097035593, "normalcdf(-1.96, 1.96)");
    [Fact] public void NormalPdf_SymbolicStaysUnevaluated()
    {
        Assert.Equal("normalpdf(x)", Eval("normalpdf(x)"));
        Close(0.3989422804014327, "subst(normalpdf(x), x, 0)");
    }

    [Fact] public void NormalCdf_OpenEnded() => Close(0.5, "normalcdf(0, inf)");
    [Fact] public void InvNorm() => Close(1.959963984540054, "invnorm(0.975)");
    [Fact] public void InvNorm_Center() => Close(1.6448536269514722, "invnorm(0.9, 0, 1, \"center\")");
    [Fact] public void TCdf() => Close(0.9266119652, "tcdf(-2, 2, 10)", 1e-9);
    [Fact] public void BinomPdf() => Close(0.1171875, "binompdf(10, 0.5, 3)");
    [Fact] public void BinomPdf_List() => Assert.Equal("{0.0625, 0.25, 0.375, 0.25, 0.0625}", Eval("binompdf(4, 0.5)"));
    [Fact] public void BinomCdf_MapsOverX() => Assert.Equal("{0.0546875, 0.171875}", Eval("binomcdf(10, 0.5, {2, 3})"));
    [Fact] public void PoissonPdf() => Close(0.22404180765538775, "poissonpdf(3, 2)");
    [Fact] public void Chi2Cdf() => Close(0.95, "chi2cdf(0, invchi2(0.95, 3), 3)");

    // ---------------------------------------------------------------- statistics

    [Fact]
    public void OneVar()
    {
        var s = Eval("onevar({2, 4, 4, 4, 5, 5, 7, 9})");
        Assert.Contains("n = 8", s);
        Assert.Contains("xbar = 5", s);
        Assert.Contains("sigmax = 2", s);
        Assert.Contains("mode = 4", s);
    }

    [Fact] public void Quartiles() => Assert.Equal("{min = 1, Q1 = 3, median = 8, Q3 = 12, max = 15}", Eval("quartiles({1, 3, 4, 8, 9, 12, 15})"));
    [Fact] public void Corr() => Close(1, "corr({1, 2, 3}, {2, 4, 6})");

    [Fact]
    public void LinReg_GivesEquationAndStatistics()
    {
        var s = Eval("linreg({1, 2, 3, 4}, {2.1, 3.9, 6.2, 7.8})");
        Assert.StartsWith("{y = 1.94x + 0.15, a = 1.94, b = 0.15", s);
        Assert.Contains("r2 = 0.99566", s);
    }

    [Fact] public void ExpReg_PrintsTheProductClearly() => Assert.StartsWith("{y = 2·3^x, a = 2, b = 3", Eval("expreg({0, 1, 2, 3}, {2, 6, 18, 54})"));
    [Fact] public void PolyReg_CleansRoundingNoise() => Assert.StartsWith("{y = x² + 1, a = 1, b = 0, c = 1", Eval("polyreg({0, 1, 2, 3, 4}, {1, 2, 5, 10, 17}, 2)"));

    [Fact]
    public void TTest()
    {
        var s = Eval("ttest(5, {5.1, 4.9, 5.6, 5.8, 6.0, 5.4})");
        Assert.StartsWith("{t = 2.735126328, p = 0.04102919855, df = 5", s);
    }

    [Fact] public void TInterval() => Assert.StartsWith("{{5.028074546, 5.905258787}", Eval("tinterval({5.1, 4.9, 5.6, 5.8, 6.0, 5.4})"));
    [Fact] public void Chi2Test() => Assert.StartsWith("{chi2 = 0.7936507937, p = 0.3729984836, df = 1", Eval("chi2test([[10, 20], [30, 40]])"));

    // ---------------------------------------------------------------- finance and special functions

    [Fact] public void TvmPmt() => Close(-1199.101050304, "tvm_pmt(360, 6, 200000, 0)", 1e-9);
    [Fact] public void TvmPmt_Begin() => Close(-1193.135373, "tvm_pmt(360, 6, 200000, 0, 12, 12, \"begin\")", 1e-8);
    [Fact] public void Bal_WithCompounding() => Close(Num("bal(12, 360, 6, 200000, -1199.10105, 0, 12, 12)"), "bal(12, 360, 6, 200000, -1199.10105)", 1e-12);
    [Fact] public void Irr() => Close(8.896339469, "irr(-1000, {300, 400, 500})", 1e-8);
    [Fact] public void Npv() => Close(-21.036814425244188, "npv(10, -1000, {300, 400, 500})", 1e-9);
    [Fact] public void Eff() => Close(6.167781186449761, "eff(6, 12)", 1e-12);
    [Fact] public void GammaIsAFunctionWithBrackets() => Close(Math.PI, "gamma(0.5)^2");
    [Fact] public void GammaAloneIsTheLetter() => Assert.Equal("γ", Eval("gamma"));
    [Fact] public void Beta() => Close(1.0 / 12, "beta(2, 3)");
    [Fact] public void Zeta() => Close(Math.PI * Math.PI / 6, "zeta(2)");
    [Fact] public void Erf() => Close(0.8427007929497149, "erf(1)");
    [Fact] public void LambertW() => Close(0.5671432904097838, "lambertw(1)");

    [Fact]
    public void RandomFunctions()
    {
        var r = Session.Evaluate("randintnorep(1, 10)");
        var list = Assert.IsType<ListExpr>(r.Exact);
        Assert.Equal(10, list.Count);
        Assert.Equal("{1, 2, 3, 4, 5, 6, 7, 8, 9, 10}", Eval("sort(randintnorep(1, 10))"));
        Assert.IsType<ListExpr>(Session.Evaluate("randnorm(0, 1, 5)").Exact);
    }

    [Fact]
    public void EveryFunctionHasHelp()
    {
        foreach (var f in Session.Functions.All.Where(f => !f.Hidden))
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Description), $"{f.Name} has no description");
            Assert.False(string.IsNullOrWhiteSpace(f.Category), $"{f.Name} has no category");
        }
    }

    [Fact]
    public void EveryExampleRuns()
    {
        var failures = new List<string>();
        foreach (var f in Session.Functions.All.Where(f => !f.Hidden && f.Example.Length > 0))
        {
            var s = CalcyEngine.CreateSession();
            var r = s.Evaluate(f.Example);
            if (r.IsError) failures.Add($"{f.Name}: {f.Example} -> {r.ErrorMessage}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
