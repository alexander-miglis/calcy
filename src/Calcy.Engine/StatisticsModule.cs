using System.Globalization;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Parsing;
using Calcy.Statistics;

namespace Calcy.Engine;

/// <summary>
/// Statistics, probability and finance from Calcy.Statistics as calculator functions, with TI-84 names and argument
/// orders (normalcdf, invNorm, binompdf, LinReg, 1-sample t test, tvm_Pmt, npv, irr, ...). Results that have several
/// parts (tests, regressions, one-variable statistics) come back as lists of "name = value" equations.
/// </summary>
public static class StatisticsModule
{
    private const string Dist = "Probability";
    private const string Desc = "Statistics";
    private const string Reg = "Regression";
    private const string Tests = "Hypothesis tests";
    private const string Fin = "Finance";
    private const string Special = "Special functions";
    private const string Rand = "Random";

    private static readonly CalcRandom Random = new();
    private static readonly object RandomGate = new();

    public static void Register(FunctionRegistry r)
    {
        void Add(FunctionDefinition f)
        {
            if (r.Contains(f.Name)) return;
            r.Add(new FunctionDefinition
            {
                Name = f.Name,
                Aliases = [.. f.Aliases.Where(a => !r.Contains(a))],
                MinArgs = f.MinArgs,
                MaxArgs = f.MaxArgs,
                Listable = f.Listable,
                HoldArgs = f.HoldArgs,
                Evaluate = f.Evaluate,
                Category = f.Category,
                Signature = f.Signature,
                Description = f.Description,
                Example = f.Example,
            });
        }

        // ---------------------------------------------------------------- distributions
        Add(Def("normalpdf", c => N(CalcDistributions.NormalPdf(R(c, 0), Opt(c, 1, 0), Opt(c, 2, 1))), Dist,
            "normalpdf(x, μ, σ)", "Normal probability density (μ = 0 and σ = 1 when left out).", "normalpdf(0)", 1, 3, aliases: ["normalPdf", "normpdf"]));
        Add(Def("normalcdf", c => N(CalcDistributions.NormalCdf(R(c, 0), R(c, 1), Opt(c, 2, 0), Opt(c, 3, 1))), Dist,
            "normalcdf(lower, upper, μ, σ)", "Normal probability between two bounds (use -inf or inf for open ends).",
            "normalcdf(-1.96, 1.96)", 2, 4, aliases: ["normalCdf", "normcdf"]));
        Add(Def("invnorm", c => N(CalcDistributions.InvNorm(R(c, 0), Opt(c, 1, 0), Opt(c, 2, 1), TailArg(c, 3))), Dist,
            "invnorm(area, μ, σ, tail)", "The x with the given area to its left (tail \"right\" or \"center\" for the other areas).",
            "invnorm(0.975)", 1, 4, aliases: ["invNorm"]));
        Add(Def("tpdf", c => N(CalcDistributions.TPdf(R(c, 0), R(c, 1))), Dist, "tpdf(x, df)", "Student t probability density.", "tpdf(0, 10)", 2, 2, aliases: ["tPdf"]));
        Add(Def("tcdf", c => N(CalcDistributions.TCdf(R(c, 0), R(c, 1), R(c, 2))), Dist, "tcdf(lower, upper, df)", "Student t probability between two bounds.",
            "tcdf(-2, 2, 10)", 3, 3, aliases: ["tCdf"]));
        Add(Def("invt", c => N(CalcDistributions.InvT(R(c, 0), R(c, 1), TailArg(c, 2))), Dist, "invt(area, df, tail)", "Student t quantile.",
            "invt(0.975, 10)", 2, 3, aliases: ["invT"]));
        Add(Def("chi2pdf", c => N(CalcDistributions.Chi2Pdf(R(c, 0), R(c, 1))), Dist, "chi2pdf(x, df)", "Chi-squared probability density.",
            "chi2pdf(2, 3)", 2, 2, aliases: ["χ²pdf", "chisqpdf"]));
        Add(Def("chi2cdf", c => N(CalcDistributions.Chi2Cdf(R(c, 0), R(c, 1), R(c, 2))), Dist, "chi2cdf(lower, upper, df)", "Chi-squared probability between two bounds.",
            "chi2cdf(0, 7.81, 3)", 3, 3, aliases: ["χ²cdf", "chisqcdf"]));
        Add(Def("invchi2", c => N(CalcDistributions.InvChi2(R(c, 0), R(c, 1), TailArg(c, 2))), Dist, "invchi2(area, df, tail)", "Chi-squared quantile.",
            "invchi2(0.95, 3)", 2, 3, aliases: ["invχ²", "invChi2"]));
        Add(Def("fpdf", c => N(CalcDistributions.FPdf(R(c, 0), R(c, 1), R(c, 2))), Dist, "fpdf(x, df1, df2)", "F probability density.",
            "fpdf(1, 5, 10)", 3, 3, aliases: ["Fpdf"]));
        Add(Def("fcdf", c => N(CalcDistributions.FCdf(R(c, 0), R(c, 1), R(c, 2), R(c, 3))), Dist, "fcdf(lower, upper, df1, df2)", "F probability between two bounds.",
            "fcdf(0, 3.33, 5, 10)", 4, 4, aliases: ["Fcdf"]));
        Add(Def("invf", c => N(CalcDistributions.InvF(R(c, 0), R(c, 1), R(c, 2), TailArg(c, 3))), Dist, "invf(area, df1, df2, tail)", "F quantile.",
            "invf(0.95, 5, 10)", 3, 4, aliases: ["invF"]));
        Add(Def("binompdf", c => c.Count == 2 ? Nums(CalcDistributions.BinomPdf(R(c, 0), R(c, 1))) : N(CalcDistributions.BinomPdf(R(c, 0), R(c, 1), R(c, 2))), Dist,
            "binompdf(n, p, x)", "Binomial probability of exactly x successes (all of them as a list when x is left out).",
            "binompdf(10, 0.5, 3)", 2, 3, aliases: ["binomPdf"]));
        Add(Def("binomcdf", c => c.Count switch
            {
                2 => Nums(CalcDistributions.BinomCdf(R(c, 0), R(c, 1))),
                3 => N(CalcDistributions.BinomCdf(R(c, 0), R(c, 1), R(c, 2))),
                _ => N(CalcDistributions.BinomCdf(R(c, 0), R(c, 1), R(c, 2), R(c, 3))),
            }, Dist, "binomcdf(n, p, x) or binomcdf(n, p, lower, upper)", "Binomial probability of at most x successes, or between two counts.",
            "binomcdf(10, 0.5, 3)", 2, 4, aliases: ["binomCdf"]));
        Add(Def("invbinom", c => N(CalcDistributions.InvBinom(R(c, 0), R(c, 1), R(c, 2))), Dist, "invbinom(area, n, p)",
            "Smallest count whose cumulative binomial probability reaches the area.", "invbinom(0.5, 10, 0.3)", 3, 3, aliases: ["invBinom"]));
        Add(Def("poissonpdf", c => N(CalcDistributions.PoissonPdf(R(c, 0), R(c, 1))), Dist, "poissonpdf(μ, x)", "Poisson probability of exactly x events.",
            "poissonpdf(3, 2)", 2, 2, aliases: ["poissonPdf"]));
        Add(Def("poissoncdf", c => c.Count == 2 ? N(CalcDistributions.PoissonCdf(R(c, 0), R(c, 1))) : N(CalcDistributions.PoissonCdf(R(c, 0), R(c, 1), R(c, 2))), Dist,
            "poissoncdf(μ, x) or poissoncdf(μ, lower, upper)", "Poisson probability of at most x events, or between two counts.",
            "poissoncdf(3, 2)", 2, 3, aliases: ["poissonCdf"]));
        Add(Def("invpoisson", c => N(CalcDistributions.InvPoisson(R(c, 0), R(c, 1))), Dist, "invpoisson(area, μ)", "Poisson quantile.",
            "invpoisson(0.5, 3)", 2, 2, aliases: ["invPoisson"]));
        Add(Def("geometpdf", c => N(CalcDistributions.GeometPdf(R(c, 0), R(c, 1))), Dist, "geometpdf(p, x)", "Probability that the first success is on trial x.",
            "geometpdf(0.2, 3)", 2, 2, aliases: ["geometPdf"]));
        Add(Def("geometcdf", c => c.Count == 2 ? N(CalcDistributions.GeometCdf(R(c, 0), R(c, 1))) : N(CalcDistributions.GeometCdf(R(c, 0), R(c, 1), R(c, 2))), Dist,
            "geometcdf(p, x) or geometcdf(p, lower, upper)", "Probability that the first success is on trial x or earlier.",
            "geometcdf(0.2, 3)", 2, 3, aliases: ["geometCdf"]));

        // ---------------------------------------------------------------- descriptive
        Add(Def("onevar", OneVar, Desc, "onevar(list) or onevar(list, frequencies)",
            "One-variable statistics: n, mean, sum, Sx, σx, quartiles, range, mode, skewness.", "onevar({2, 4, 4, 4, 5, 5, 7, 9})", 1, 2,
            listable: false, aliases: ["oneVar", "1-Var Stats", "stats"]));
        Add(Def("twovar", TwoVar, Desc, "twovar(xlist, ylist)", "Two-variable statistics: means, sums, standard deviations, covariance and r.",
            "twovar({1, 2, 3}, {2, 4, 7})", 2, 3, listable: false, aliases: ["twoVar", "2-Var Stats"]));
        Add(Def("q1", c => N(Descriptive.OneVar(Data(c, 0)).Q1), Desc, "q1(list)", "First quartile (TI method: median of the lower half).", "q1({1, 2, 3, 4, 5, 6})", listable: false, aliases: ["Q1"]));
        Add(Def("q3", c => N(Descriptive.OneVar(Data(c, 0)).Q3), Desc, "q3(list)", "Third quartile (TI method).", "q3({1, 2, 3, 4, 5, 6})", listable: false, aliases: ["Q3"]));
        Add(Def("iqr", c => N(Descriptive.OneVar(Data(c, 0)).IQR), Desc, "iqr(list)", "Interquartile range Q3 - Q1.", "iqr({1, 2, 3, 4, 5, 6})", listable: false, aliases: ["IQR"]));
        Add(Def("quartiles", c =>
            {
                var f = Descriptive.FiveNumberSummary(Data(c, 0));
                return Named(("min", f.Min), ("Q1", f.Q1), ("median", f.Median), ("Q3", f.Q3), ("max", f.Max));
            }, Desc, "quartiles(list)", "Five-number summary: min, Q1, median, Q3, max.", "quartiles({1, 3, 4, 8, 9, 12, 15})",
            listable: false, aliases: ["fivenum", "summary"]));
        Add(Def("mode", c => Nums([.. Descriptive.OneVar(Data(c, 0)).Modes]), Desc, "mode(list)", "The most frequent values.", "mode({1, 2, 2, 3})", listable: false));
        Add(Def("percentile", c => N(Descriptive.Percentile(Data(c, 0), R(c, 1) > 1 ? R(c, 1) / 100 : R(c, 1))), Desc, "percentile(list, p)",
            "The p-th percentile (p as 0.9 or 90), interpolated like Excel PERCENTILE.INC.", "percentile({1, 2, 3, 4, 5}, 40)", 2, 2, listable: false));
        Add(Def("skewness", c => N(Descriptive.OneVar(Data(c, 0)).Skewness), Desc, "skewness(list)", "Sample skewness (Excel SKEW).", "skewness({1, 2, 2, 3, 9})", listable: false, aliases: ["skew"]));
        Add(Def("kurtosis", c => N(Descriptive.OneVar(Data(c, 0)).ExcessKurtosis), Desc, "kurtosis(list)", "Sample excess kurtosis (Excel KURT).", "kurtosis({1, 2, 2, 3, 9})", listable: false, aliases: ["kurt"]));
        Add(Def("corr", c => N(Descriptive.TwoVar(Data(c, 0), Data(c, 1)).R), Desc, "corr(xlist, ylist)", "Correlation coefficient r.", "corr({1, 2, 3}, {2, 4, 7})",
            2, 2, listable: false, aliases: ["correlation", "pearson"]));
        Add(Def("cov", c => N(Descriptive.TwoVar(Data(c, 0), Data(c, 1)).CovarianceSample), Desc, "cov(xlist, ylist)", "Sample covariance.", "cov({1, 2, 3}, {2, 4, 7})",
            2, 2, listable: false, aliases: ["covariance"]));

        // ---------------------------------------------------------------- regression
        void Regression(string name, RegressionModel model, string description, string[]? aliases = null) =>
            Add(Def(name, c => Fit(c, model), Reg, $"{name}(xlist, ylist)", description, $"{name}({{1, 2, 3, 4, 5, 6}}, {{2.1, 3.9, 6.2, 7.8, 10.1, 11.8}})", 2, 3,
                listable: false, aliases: aliases));
        Regression("linreg", RegressionModel.LinearAxB, "Least squares line y = a x + b, with r and r².", ["LinReg", "linregaxb"]);
        Regression("linregabx", RegressionModel.LinearABx, "Least squares line y = a + b x.", ["LinRegABx"]);
        Regression("medmed", RegressionModel.MedianMedian, "Median-median line y = a x + b.", ["MedMed"]);
        Regression("quadreg", RegressionModel.Quadratic, "Quadratic fit y = a x² + b x + c.", ["QuadReg"]);
        Regression("cubicreg", RegressionModel.Cubic, "Cubic fit y = a x³ + b x² + c x + d.", ["CubicReg"]);
        Regression("quartreg", RegressionModel.Quartic, "Quartic fit.", ["QuartReg"]);
        Regression("lnreg", RegressionModel.Logarithmic, "Logarithmic fit y = a + b ln x.", ["LnReg", "logreg"]);
        Regression("expreg", RegressionModel.Exponential, "Exponential fit y = a·b^x.", ["ExpReg"]);
        Regression("expereg", RegressionModel.ExponentialE, "Exponential fit y = a·e^(b x).", ["ExpEReg"]);
        Regression("pwrreg", RegressionModel.Power, "Power fit y = a·x^b.", ["PwrReg", "powerreg"]);
        Regression("logisticreg", RegressionModel.Logistic, "Logistic fit y = c / (1 + a·e^(-b x)).", ["Logistic"]);
        Regression("sinreg", RegressionModel.Sinusoidal, "Sinusoidal fit y = a·sin(b x + c) + d (radians).", ["SinReg"]);
        Add(Def("polyreg", c => FitResult(c, Statistics.Regression.FitPolynomial((int)R(c, 2), Data(c, 0), Data(c, 1))), Reg, "polyreg(xlist, ylist, degree)",
            "Polynomial least squares fit of any degree.", "polyreg({0, 1, 2, 3, 4}, {1, 2, 5, 10, 17}, 2)", 3, 3, listable: false, aliases: ["PolyReg", "polyfit"]));

        // ---------------------------------------------------------------- tests and intervals
        Add(Def("ttest", c => c.Count >= 4 && c[1] is not ListExpr
                ? TestList(Inference.TTest(R(c, 0), R(c, 1), R(c, 2), R(c, 3), AltArg(c, 4)))
                : TestList(Inference.TTest(R(c, 0), Data(c, 1), AltArg(c, 2))), Tests,
            "ttest(μ0, list, alternative) or ttest(μ0, x̄, Sx, n, alternative)", "One-sample t test. alternative: \"≠\" (default), \"<\" or \">\".",
            "ttest(5, {5.1, 4.9, 5.6, 5.8, 6.0, 5.4})", 2, 5, listable: false, aliases: ["T-Test", "tTest"]));
        Add(Def("ztest", c => c.Count >= 4 && c[2] is not ListExpr
                ? TestList(Inference.ZTest(R(c, 0), R(c, 1), R(c, 2), R(c, 3), AltArg(c, 4)))
                : TestList(Inference.ZTest(R(c, 0), R(c, 1), Data(c, 2), AltArg(c, 3))), Tests,
            "ztest(μ0, σ, list, alternative) or ztest(μ0, σ, x̄, n, alternative)", "One-sample z test.",
            "ztest(100, 15, 104, 36)", 3, 5, listable: false, aliases: ["Z-Test", "zTest"]));
        Add(Def("ttest2", c => TestList(Inference.TwoSampleTTest(Data(c, 0), Data(c, 1), AltArg(c, 2), c.Count >= 4 && Truthy(c, 3))), Tests,
            "ttest2(list1, list2, alternative, pooled)", "Two-sample t test (Welch unless pooled is true).",
            "ttest2({5, 6, 7, 8}, {6, 8, 9, 10})", 2, 4, listable: false, aliases: ["2-SampTTest", "twosampttest"]));
        Add(Def("pairedttest", c => TestList(Inference.PairedTTest(Data(c, 0), Data(c, 1), c.Count >= 3 ? R(c, 2) : 0, AltArg(c, 3))), Tests,
            "pairedttest(before, after, μ0, alternative)", "Paired t test on the differences.", "pairedttest({10, 12, 9}, {11, 14, 10})", 2, 4, listable: false));
        Add(Def("propztest", c => TestList(Inference.OnePropZTest(R(c, 0), R(c, 1), R(c, 2), AltArg(c, 3))), Tests,
            "propztest(p0, x, n, alternative)", "One-proportion z test.", "propztest(0.5, 60, 100)", 3, 4, listable: false, aliases: ["1-PropZTest", "onepropztest"]));
        Add(Def("propztest2", c => TestList(Inference.TwoPropZTest(R(c, 0), R(c, 1), R(c, 2), R(c, 3), AltArg(c, 4))), Tests,
            "propztest2(x1, n1, x2, n2, alternative)", "Two-proportion z test.", "propztest2(45, 100, 30, 100)", 4, 5, listable: false, aliases: ["2-PropZTest", "twopropztest"]));
        Add(Def("chi2gof", c => TestList(Inference.ChiSquareGoodnessOfFit(Data(c, 0), Data(c, 1), c.Count == 3 ? R(c, 2) : null)), Tests,
            "chi2gof(observed, expected, df)", "Chi-squared goodness of fit test.", "chi2gof({20, 30, 50}, {25, 25, 50})", 2, 3, listable: false, aliases: ["χ²GOF-Test"]));
        Add(Def("chi2test", c => TestList(Inference.ChiSquareTwoWay(Table(c, 0))), Tests, "chi2test(matrix)", "Chi-squared test of independence on a table of counts.",
            "chi2test([[10, 20], [30, 40]])", listable: false, aliases: ["χ²-Test"]));
        Add(Def("ftest2", c => TestList(Inference.TwoSampleFTest(Data(c, 0), Data(c, 1), AltArg(c, 2))), Tests, "ftest2(list1, list2, alternative)",
            "Two-sample F test for equal variances.", "ftest2({1, 3, 5, 7}, {2, 3, 4, 5})", 2, 3, listable: false, aliases: ["2-SampFTest"]));
        Add(Def("linregttest", c => TestList(Inference.LinRegTTest(Data(c, 0), Data(c, 1), AltArg(c, 2))), Tests, "linregttest(xlist, ylist, alternative)",
            "t test for the slope of the regression line.", "linregttest({1, 2, 3, 4, 5}, {2, 4, 5, 4, 5})", 2, 3, listable: false, aliases: ["LinRegTTest"]));
        Add(Def("anova", c => TestList(Inference.OneWayAnova([.. Enumerable.Range(0, c.Count).Select(i => (IReadOnlyList<double>)Data(c, i))])), Tests,
            "anova(list1, list2, ...)", "One-way analysis of variance.", "anova({1, 2, 3}, {2, 3, 4}, {5, 6, 7})", 2, 20, listable: false, aliases: ["ANOVA"]));
        Add(Def("tinterval", c => c.Count >= 3 && c[0] is not ListExpr
                ? Interval(Inference.TInterval(R(c, 0), R(c, 1), R(c, 2), Level(c, 3)))
                : Interval(Inference.TInterval(Data(c, 0), Level(c, 1))), Tests,
            "tinterval(list, level) or tinterval(x̄, Sx, n, level)", "t confidence interval for a mean (level 0.95 or 95, default 95%).",
            "tinterval({5.1, 4.9, 5.6, 5.8, 6.0, 5.4})", 1, 4, listable: false, aliases: ["TInterval"]));
        Add(Def("zinterval", c => c.Count >= 3 && c[1] is not ListExpr
                ? Interval(Inference.ZInterval(R(c, 0), R(c, 1), R(c, 2), Level(c, 3)))
                : Interval(Inference.ZInterval(R(c, 0), Data(c, 1), Level(c, 2))), Tests,
            "zinterval(σ, list, level) or zinterval(σ, x̄, n, level)", "z confidence interval for a mean.", "zinterval(15, 104, 36)", 2, 4,
            listable: false, aliases: ["ZInterval"]));
        Add(Def("propzint", c => Interval(Inference.OnePropZInterval(R(c, 0), R(c, 1), Level(c, 2))), Tests, "propzint(x, n, level)",
            "Confidence interval for a proportion.", "propzint(60, 100)", 2, 3, listable: false, aliases: ["1-PropZInt"]));
        Add(Def("propzint2", c => Interval(Inference.TwoPropZInterval(R(c, 0), R(c, 1), R(c, 2), R(c, 3), Level(c, 4))), Tests, "propzint2(x1, n1, x2, n2, level)",
            "Confidence interval for a difference of proportions.", "propzint2(45, 100, 30, 100)", 4, 5, listable: false, aliases: ["2-PropZInt"]));
        Add(Def("tinterval2", c => Interval(Inference.TwoSampleTInterval(Data(c, 0), Data(c, 1), Level(c, 2), c.Count >= 4 && Truthy(c, 3))), Tests,
            "tinterval2(list1, list2, level, pooled)", "Confidence interval for a difference of means.", "tinterval2({5, 6, 7, 8}, {6, 8, 9, 10})", 2, 4,
            listable: false, aliases: ["2-SampTInt"]));

        // ---------------------------------------------------------------- finance (TI-84 TVM argument order)
        Add(Def("tvm_pmt", c => N(Tvm.SolvePayment(R(c, 0), R(c, 1), R(c, 2), Opt(c, 3, 0), Opt(c, 4, 12), OptNull(c, 5), Timing(c, 6))), Fin,
            "tvm_pmt(N, I%, PV, FV, P/Y, C/Y, timing)", "Payment per period (money paid out is negative). timing is \"end\" (default) or \"begin\".", "tvm_pmt(360, 6, 200000, 0)", 3, 7, aliases: ["tvm_Pmt", "pmt"]));
        Add(Def("tvm_n", c => N(Tvm.SolveN(R(c, 0), R(c, 1), R(c, 2), Opt(c, 3, 0), Opt(c, 4, 12), OptNull(c, 5), Timing(c, 6))), Fin,
            "tvm_n(I%, PV, PMT, FV, P/Y, C/Y, timing)", "Number of payments.", "tvm_n(6, 200000, -1199.10, 0)", 3, 7, aliases: ["tvm_N", "nper"]));
        Add(Def("tvm_i", c => N(Tvm.SolveInterestRate(R(c, 0), R(c, 1), R(c, 2), Opt(c, 3, 0), Opt(c, 4, 12), OptNull(c, 5), Timing(c, 6))), Fin,
            "tvm_i(N, PV, PMT, FV, P/Y, C/Y, timing)", "Annual interest rate in percent.", "tvm_i(48, 20000, -500, 0)", 3, 7, aliases: ["tvm_I%", "tvm_I", "rate"]));
        Add(Def("tvm_pv", c => N(Tvm.SolvePresentValue(R(c, 0), R(c, 1), R(c, 2), Opt(c, 3, 0), Opt(c, 4, 12), OptNull(c, 5), Timing(c, 6))), Fin,
            "tvm_pv(N, I%, PMT, FV, P/Y, C/Y, timing)", "Present value.", "tvm_pv(360, 6, -1199.10, 0)", 3, 7, aliases: ["tvm_PV", "pv"]));
        Add(Def("tvm_fv", c => N(Tvm.SolveFutureValue(R(c, 0), R(c, 1), R(c, 2), Opt(c, 3, 0), Opt(c, 4, 12), OptNull(c, 5), Timing(c, 6))), Fin,
            "tvm_fv(N, I%, PV, PMT, P/Y, C/Y, timing)", "Future value.", "tvm_fv(120, 5, -1000, -100)", 3, 7, aliases: ["tvm_FV", "fv"]));
        Add(Def("npv", c => N(CashFlow.Npv(R(c, 0), R(c, 1), Data(c, 2), c.Count == 4 ? Data(c, 3) : null)), Fin, "npv(rate%, CF0, {CF1, CF2, ...}, {frequencies})",
            "Net present value of cash flows.", "npv(10, -1000, {300, 400, 500})", 3, 4, listable: false));
        Add(Def("irr", c => N(CashFlow.Irr(R(c, 0), Data(c, 1), c.Count == 3 ? Data(c, 2) : null)), Fin, "irr(CF0, {CF1, CF2, ...}, {frequencies})",
            "Internal rate of return in percent.", "irr(-1000, {300, 400, 500})", 2, 3, listable: false));
        Add(Def("mirr", c => N(CashFlow.Mirr(R(c, 0), R(c, 1), R(c, 2), Data(c, 3), c.Count == 5 ? Data(c, 4) : null)), Fin, "mirr(finance%, reinvest%, CF0, {CF1, ...}, {frequencies})",
            "Modified internal rate of return in percent.", "mirr(10, 12, -1000, {300, 400, 500})", 4, 5, listable: false));
        Add(Def("eff", c => N(Statistics.Finance.EffectiveRate(R(c, 0), R(c, 1))), Fin, "eff(nominal%, compoundings per year)", "Effective annual rate.",
            "eff(6, 12)", 2, 2, aliases: ["Eff"]));
        Add(Def("nom", c => N(Statistics.Finance.NominalRate(R(c, 0), R(c, 1))), Fin, "nom(effective%, compoundings per year)", "Nominal annual rate.",
            "nom(6.17, 12)", 2, 2, aliases: ["Nom"]));
        Add(Def("dbd", c => N(Statistics.Finance.DaysBetweenDates(R(c, 0), R(c, 1))), Fin, "dbd(date1, date2)",
            "Days between two dates written MM.DDYY or DDMM.YY, like the TI.", "dbd(1.0126, 3.0126)", 2, 2));
        Add(Def("bal", c => N(Amortization.Balance(TvmArgs(c, 1), (int)R(c, 0))), Fin, "bal(k, N, I%, PV, PMT, FV, P/Y, C/Y, timing)",
            "Loan balance after payment k.", "bal(12, 360, 6, 200000, -1199.10)", 5, 9, listable: false));
        Add(Def("sumprn", c => N(Amortization.SumPrincipal(TvmArgs(c, 2), (int)R(c, 0), (int)R(c, 1))), Fin, "sumprn(from, to, N, I%, PV, PMT, FV, P/Y, C/Y, timing)",
            "Principal paid between two payments.", "sumprn(1, 12, 360, 6, 200000, -1199.10)", 6, 10, listable: false, aliases: ["ΣPrn"]));
        Add(Def("sumint", c => N(Amortization.SumInterest(TvmArgs(c, 2), (int)R(c, 0), (int)R(c, 1))), Fin, "sumint(from, to, N, I%, PV, PMT, FV, P/Y, C/Y, timing)",
            "Interest paid between two payments.", "sumint(1, 12, 360, 6, 200000, -1199.10)", 6, 10, listable: false, aliases: ["ΣInt"]));

        // ---------------------------------------------------------------- special functions
        Add(Def("gamma", c => N(SpecialFunctions.Gamma(R(c, 0))), Special, "gamma(x)", "Gamma function: gamma(n) = (n - 1)!.", "gamma(0.5)", aliases: ["Γ"]));
        Add(Def("lngamma", c => N(SpecialFunctions.LogGamma(R(c, 0))), Special, "lngamma(x)", "Natural log of |gamma(x)|.", "lngamma(100)", aliases: ["lgamma"]));
        Add(Def("beta", c => N(SpecialFunctions.Beta(R(c, 0), R(c, 1))), Special, "beta(a, b)", "Beta function.", "beta(2, 3)", 2, 2, aliases: ["Beta"]));
        Add(Def("digamma", c => N(SpecialFunctions.Digamma(R(c, 0))), Special, "digamma(x)", "Digamma function ψ(x).", "digamma(1)", aliases: ["psi"]));
        Add(Def("erf", c => N(SpecialFunctions.Erf(R(c, 0))), Special, "erf(x)", "Error function.", "erf(1)"));
        Add(Def("erfc", c => N(SpecialFunctions.Erfc(R(c, 0))), Special, "erfc(x)", "Complementary error function 1 - erf(x).", "erfc(1)"));
        Add(Def("erfinv", c => N(SpecialFunctions.ErfInv(R(c, 0))), Special, "erfinv(y)", "Inverse error function.", "erfinv(0.5)"));
        Add(Def("lambertw", c => N(SpecialFunctions.LambertW(R(c, 0), c.Count == 2 ? (int)R(c, 1) : 0)), Special, "lambertw(x, branch)",
            "Lambert W: the w with w·e^w = x (branch 0 or -1).", "lambertw(1)", 1, 2, aliases: ["W", "productlog"]));
        Add(Def("zeta", c => N(SpecialFunctions.RiemannZeta(R(c, 0))), Special, "zeta(s)", "Riemann zeta function.", "zeta(3)", aliases: ["ζ"]));
        Add(Def("gammainc", c => N(SpecialFunctions.RegularizedGammaP(R(c, 0), R(c, 1))), Special, "gammainc(a, x)", "Regularised lower incomplete gamma P(a, x).",
            "gammainc(2, 1)", 2, 2));
        Add(Def("betainc", c => N(SpecialFunctions.RegularizedIncompleteBeta(R(c, 0), R(c, 1), R(c, 2))), Special, "betainc(a, b, x)",
            "Regularised incomplete beta I_x(a, b).", "betainc(2, 3, 0.4)", 3, 3));

        // ---------------------------------------------------------------- random
        Add(Def("randnorm", c => RandomMany(c, 2, () => Random.NextNormal(R(c, 0), R(c, 1)), "randnorm"), Rand, "randnorm(μ, σ, n)",
            "Normally distributed random number (a list of n when n is given).", "randnorm(0, 1, 5)", 2, 3, listable: false, aliases: ["randNorm"]));
        Add(Def("randbin", c => RandomMany(c, 2, () => Random.NextBinomial((int)R(c, 0), R(c, 1)), "randbin"), Rand, "randbin(n, p, count)",
            "Binomial random count (a list when count is given).", "randbin(10, 0.5, 5)", 2, 3, listable: false, aliases: ["randBin"]));
        Add(Def("randintnorep", c =>
            {
                lock (RandomGate)
                {
                    long lo = (long)R(c, 0), hi = (long)R(c, 1);
                    int count = c.Count == 3 ? (int)R(c, 2) : (int)(hi - lo + 1);
                    return new ListExpr([.. Random.RandIntNoRep(lo, hi, count).Select(v => (Expr)Expr.Int(v))]);
                }
            }, Rand, "randintnorep(a, b, n)", "n different whole numbers from a to b in random order (all of them when n is left out).",
            "randintnorep(1, 10, 3)", 2, 3, listable: false, aliases: ["randIntNoRep"]));
    }

    // ------------------------------------------------------------------ composite results

    private static Expr OneVar(CallInfo c)
    {
        var s = Descriptive.OneVar(Data(c, 0), c.Count == 2 ? Data(c, 1) : null);
        var items = new List<(string, double)>
        {
            ("n", s.N), ("xbar", s.Mean), ("sumx", s.Sum), ("sumx2", s.SumOfSquares), ("Sx", s.SampleStdDev), ("sigmax", s.PopulationStdDev),
            ("minX", s.Min), ("Q1", s.Q1), ("median", s.Median), ("Q3", s.Q3), ("maxX", s.Max), ("range", s.Range), ("IQR", s.IQR),
        };
        if (!double.IsNaN(s.Skewness)) items.Add(("skewness", s.Skewness));
        var list = Named([.. items]);
        if (s.Modes.Count > 0 && s.Modes.Count < s.N)
            list = new ListExpr([.. list.Items, Eq("mode", s.Modes.Count == 1 ? N(s.Modes[0]) : Nums([.. s.Modes]))]);
        return list;
    }

    private static Expr TwoVar(CallInfo c)
    {
        var s = Descriptive.TwoVar(Data(c, 0), Data(c, 1), c.Count == 3 ? Data(c, 2) : null);
        return Named(("n", s.N), ("xbar", s.MeanX), ("sumx", s.SumX), ("sumx2", s.SumX2), ("Sx", s.Sx), ("sigmax", s.SigmaX),
            ("ybar", s.MeanY), ("sumy", s.SumY), ("sumy2", s.SumY2), ("Sy", s.Sy), ("sigmay", s.SigmaY), ("sumxy", s.SumXY),
            ("cov", s.CovarianceSample), ("r", s.R), ("minX", s.MinX), ("maxX", s.MaxX), ("minY", s.MinY), ("maxY", s.MaxY));
    }

    private static Expr Fit(CallInfo c, RegressionModel model)
    {
        var x = Data(c, 0);
        var y = Data(c, 1);
        var w = c.Count == 3 ? Data(c, 2) : null;
        var result = model switch
        {
            RegressionModel.Logistic => Statistics.Regression.FitLogistic(x, y, w),
            RegressionModel.Sinusoidal => Statistics.Regression.FitSinusoidal(x, y, null, w),
            _ => Statistics.Regression.Fit(model, x, y, w),
        };
        return FitResult(c, result);
    }

    /// <summary>{y = 2.1x + 0.4, a = 2.1, b = 0.4, r = 0.99, r2 = 0.98}: the equation first, then the numbers.</summary>
    private static Expr FitResult(CallInfo c, RegressionResult r)
    {
        var items = new List<Expr>();
        var values = CleanParameters(r);
        var equation = Equation(c, r, values);
        if (equation is not null) items.Add(new RelationExpr(RelOp.Equal, new SymbolExpr("y"), equation));
        foreach (var p in r.Parameters) items.Add(Eq(p.Name, N(values[p.Name])));
        if (r.R is double rr) items.Add(Eq("r", N(rr)));
        items.Add(Eq("r2", N(r.RSquared)));
        return new ListExpr([.. items]);
    }

    /// <summary>The fitted function of x, built by reading the template ("y = a*x + b") and filling in the parameters.</summary>
    private static Expr? Equation(CallInfo c, RegressionResult r, IReadOnlyDictionary<string, double> cleaned)
    {
        var template = r.EquationTemplate;
        int eq = template.IndexOf('=');
        if (eq < 0) return null;
        try
        {
            var names = r.Parameters.Select(p => p.Name).Append("x").ToArray();
            var ctx = new FixedNames(names);
            var rhs = Parser.ParseExpression(template[(eq + 1)..].Trim(), ctx);
            var values = r.Parameters.Select(p => new KeyValuePair<string, Expr>(p.Name, N(cleaned[p.Name])));
            return Evaluator.Eval(rhs, c.Context.WithApproximate(false).WithFree("x").WithLocals(values));
        }
        catch (CalcException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rounding noise from the least squares solve becomes exact: 4e-16 next to coefficients of size 1 is 0, and
    /// 0.9999999999999998 is 1. Real fits are never that close to a round number by accident.
    /// </summary>
    private static Dictionary<string, double> CleanParameters(RegressionResult r)
    {
        double scale = r.Parameters.Select(p => Math.Abs(p.Value)).Where(double.IsFinite).DefaultIfEmpty(0).Max();
        var cleaned = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in r.Parameters)
        {
            double v = p.Value;
            if (double.IsFinite(v))
            {
                if (Math.Abs(v) <= 1e-12 * scale) v = 0;
                else
                {
                    double round = Math.Round(v);
                    if (Math.Abs(v - round) <= 1e-12 * Math.Max(1, Math.Abs(v))) v = round;
                }
            }
            cleaned[p.Name] = v;
        }
        return cleaned;
    }

    private sealed class FixedNames(string[] names) : IParseContext
    {
        public bool IsFunction(string name) => name is "sin" or "cos" or "ln" or "exp" or "log" && !names.Contains(name);
        public bool IsKnownName(string name) => names.Contains(name);
        public ImplicitMultiplication Implicit => ImplicitMultiplication.BindsTighter;
        public bool SplitUnknownNames => false;
    }

    private static Expr TestList(TestResult t)
    {
        var items = new List<Expr> { Eq(Sanitize(t.StatisticName), N(t.Statistic)), Eq("p", N(t.PValue)) };
        if (t.DegreesOfFreedom is double df) items.Add(Eq("df", N(df)));
        if (t.DenominatorDegreesOfFreedom is double df2) items.Add(Eq("df2", N(df2)));
        foreach (var (k, v) in t.Values)
        {
            var name = Sanitize(k);
            if (name is "p" or "df") continue;
            items.Add(Eq(name, N(v)));
        }
        return new ListExpr([.. items]);
    }

    private static Expr Interval(ConfidenceInterval ci) => new ListExpr(
    [
        new ListExpr([N(ci.Lower), N(ci.Upper)]),
        Eq("estimate", N(ci.PointEstimate)),
        Eq("margin", N(ci.MarginOfError)),
        .. ci.Values.Select(kv => Eq(Sanitize(kv.Key), N(kv.Value))),
    ]);

    /// <summary>Names like "chi2" or "x̄" become plain identifiers that the parser reads back.</summary>
    private static string Sanitize(string name)
    {
        var s = name.Replace("χ²", "chi2").Replace("x̄", "xbar").Replace("p̂", "phat").Replace("²", "2").Replace("-", "_").Replace(" ", "_");
        s = new string([.. s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_')]);
        if (s.Length == 0 || char.IsDigit(s[0])) s = "v" + s;
        return s;
    }

    private static Expr RandomMany(CallInfo c, int countIndex, Func<double> next, string name)
    {
        lock (RandomGate)
        {
            if (c.Count <= countIndex) return N(next());
            int n = (int)R(c, countIndex);
            if (n is < 1 or > 100_000) throw c.Error("the count must be from 1 to 100000");
            return new ListExpr([.. Enumerable.Range(0, n).Select(_ => (Expr)N(next()))]);
        }
    }

    // ------------------------------------------------------------------ argument helpers

    private static FunctionDefinition Def(string name, Func<CallInfo, Expr?> eval, string category, string signature, string description,
        string example = "", int min = 1, int max = 1, bool listable = true, string[]? aliases = null) => new()
    {
        Name = name,
        Evaluate = c => Guard(c, eval),
        Category = category,
        Signature = signature,
        Description = description,
        Example = example,
        MinArgs = min,
        MaxArgs = max,
        Listable = listable,
        Aliases = aliases ?? [],
    };

    private static Expr? Guard(CallInfo c, Func<CallInfo, Expr?> eval)
    {
        try
        {
            var result = eval(c);
            if (result is NumberExpr { Value.IsNaN: true }) throw new CalcException($"{c.Function.Name}: the result is undefined for these values");
            return result;
        }
        catch (SymbolicArgument)
        {
            // normalpdf(x) with a symbolic x stays as it is, so graphs and later substitution can evaluate it.
            return null;
        }
        catch (ArgumentException ex)
        {
            throw new CalcException($"{c.Function.Name}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            throw new CalcException($"{c.Function.Name}: {ex.Message}");
        }
    }

    /// <summary>An argument still contains unknowns: the call is left unevaluated.</summary>
    private sealed class SymbolicArgument : Exception;

    private static NumberExpr N(double x) => new(Number.Approx(x));

    private static ListExpr Nums(double[] values) => new([.. values.Select(v => (Expr)N(v))]);

    private static RelationExpr Eq(string name, Expr value) => new(RelOp.Equal, new SymbolExpr(name), value);

    private static ListExpr Named(params (string Name, double Value)[] items) => new([.. items.Select(i => (Expr)Eq(i.Name, N(i.Value)))]);

    /// <summary>A real argument; inf and -inf become infinities (for open-ended probabilities).</summary>
    private static double R(CallInfo c, int i)
    {
        var e = c[i];
        switch (e)
        {
            case NumberExpr n when n.Value.IsReal:
                return n.Value.Re;
            case ConstantExpr { Constant: ConstantKind.Infinity }:
                return double.PositiveInfinity;
            case ConstantExpr { Constant: ConstantKind.NegativeInfinity }:
                return double.NegativeInfinity;
        }
        var v = Evaluator.Numeric(e, c.Context);
        if (v is { IsReal: true } num) return num.Re;
        if (!e.IsConstantValue()) throw new SymbolicArgument();
        throw c.Error($"argument {i + 1} must be a real number, not {e}");
    }

    private static double Opt(CallInfo c, int i, double fallback) => c.Count > i ? R(c, i) : fallback;

    private static double? OptNull(CallInfo c, int i) => c.Count > i ? R(c, i) : null;

    private static double[] Data(CallInfo c, int i)
    {
        if (c[i] is not ListExpr l) throw c.Error($"argument {i + 1} must be a list like {{1, 2, 3}}");
        var result = new double[l.Items.Length];
        for (int k = 0; k < result.Length; k++)
        {
            var v = Evaluator.Numeric(l.Items[k], c.Context);
            if (v is not { IsReal: true } num) throw c.Error($"item {k + 1} of argument {i + 1} must be a real number");
            result[k] = num.Re;
        }
        return result;
    }

    private static double[,] Table(CallInfo c, int i)
    {
        if (c[i] is not MatrixExpr m) throw c.Error($"argument {i + 1} must be a matrix of counts like [[10, 20], [30, 40]]");
        var t = new double[m.Rows, m.Cols];
        for (int r = 0; r < m.Rows; r++)
            for (int k = 0; k < m.Cols; k++)
                t[r, k] = Evaluator.Numeric(m[r, k], c.Context) is { IsReal: true } v ? v.Re : throw c.Error("every count must be a real number");
        return t;
    }

    private static TvmValues TvmArgs(CallInfo c, int start) =>
        new(R(c, start), R(c, start + 1), R(c, start + 2), R(c, start + 3), Opt(c, start + 4, 0), Opt(c, start + 5, 12), OptNull(c, start + 6), Timing(c, start + 7));

    private static Tail TailArg(CallInfo c, int i)
    {
        if (c.Count <= i) return Tail.Left;
        var s = (c[i] as StringExpr)?.Value ?? (c[i] as SymbolExpr)?.Name ?? throw c.Error("the tail must be \"left\", \"right\" or \"center\"");
        return s.ToLowerInvariant() switch
        {
            "left" or "l" => Tail.Left,
            "right" or "r" => Tail.Right,
            "center" or "centre" or "c" or "middle" => Tail.Center,
            _ => throw c.Error("the tail must be \"left\", \"right\" or \"center\""),
        };
    }

    private static Alternative AltArg(CallInfo c, int i)
    {
        if (c.Count <= i) return Alternative.NotEqual;
        string s = c[i] switch
        {
            StringExpr str => str.Value,
            NumberExpr n => n.Value.Re switch { < 0 => "<", > 0 => ">", _ => "!=" },
            _ => throw c.Error("the alternative must be \"≠\", \"<\" or \">\""),
        };
        return s.Trim().ToLowerInvariant() switch
        {
            "<" or "less" or "left" => Alternative.Less,
            ">" or "greater" or "right" => Alternative.Greater,
            "≠" or "!=" or "<>" or "two" or "two-sided" or "both" => Alternative.NotEqual,
            _ => throw c.Error("the alternative must be \"≠\", \"<\" or \">\""),
        };
    }

    /// <summary>Payment timing: "end" (default, ordinary annuity) or "begin" (annuity due); 0 and 1 work too.</summary>
    private static PaymentTiming Timing(CallInfo c, int i)
    {
        if (c.Count <= i) return PaymentTiming.End;
        return c[i] switch
        {
            StringExpr { Value: var s } when s.Equals("begin", StringComparison.OrdinalIgnoreCase) || s.Equals("bgn", StringComparison.OrdinalIgnoreCase) => PaymentTiming.Begin,
            StringExpr { Value: var s } when s.Equals("end", StringComparison.OrdinalIgnoreCase) => PaymentTiming.End,
            NumberExpr n when n.Value.IsOne => PaymentTiming.Begin,
            NumberExpr n when n.Value.IsZero => PaymentTiming.End,
            _ => throw c.Error("timing must be \"end\" or \"begin\""),
        };
    }

    private static double Level(CallInfo c, int i) => c.Count > i ? R(c, i) : 0.95;

    private static bool Truthy(CallInfo c, int i) => c[i] switch
    {
        BoolExpr b => b.Value,
        NumberExpr n => !n.Value.IsZero,
        _ => throw c.Error($"argument {i + 1} must be true or false"),
    };

    internal static string Invariant(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
