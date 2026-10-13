using System.Globalization;
using System.Text.Json.Nodes;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.Statistics;
using static Calcy.Mcp.Schema;

namespace Calcy.Mcp;

/// <summary>Probability, statistics and finance tools.</summary>
public sealed partial class CalcTools
{
    private static readonly string[] Distributions = ["normal", "t", "chi2", "f", "binomial", "poisson", "geometric"];
    private static readonly string[] RegressionModels =
        ["linear", "quadratic", "cubic", "quartic", "exponential", "exponential_e", "power", "logarithmic", "logistic", "sinusoidal", "median_median", "polynomial"];
    private static readonly string[] TestKinds =
        ["t", "z", "two_sample_t", "paired_t", "one_proportion_z", "two_proportion_z", "chi2_goodness_of_fit", "chi2_independence", "anova", "regression_slope", "two_sample_f"];
    private static readonly string[] IntervalKinds = ["t", "z", "one_proportion", "two_proportion", "two_sample_t"];

    private McpTool Distribution() => new()
    {
        Name = "distribution",
        Title = "Probability distribution",
        IsAvailable = () => Has("normalcdf"),
        Description =
            "Densities, cumulative probabilities and quantiles of the normal, t, chi-squared, F, binomial, Poisson and geometric distributions, " +
            "with TI-84 conventions. pdf needs x; cdf needs lower and upper (use -inf or inf; for discrete distributions upper alone means P(X <= upper)); " +
            "inverse needs area (and tail left, right or center for continuous ones).",
        InputSchema = Object(
            Req("distribution", String("Which distribution.", Distributions)),
            Req("operation", String("What to compute.", "pdf", "cdf", "inverse")),
            Opt("x", Expression("The value for pdf.")),
            Opt("lower", Expression("Lower bound for cdf (default -inf, or 0 for counts).")),
            Opt("upper", Expression("Upper bound for cdf.")),
            Opt("area", Expression("Probability for inverse.")),
            Opt("tail", String("Which area inverse refers to.", "left", "right", "center")),
            Opt("mean", Expression("Normal mean μ (default 0), or the Poisson mean.")),
            Opt("sd", Expression("Normal standard deviation σ (default 1).")),
            Opt("df", Expression("Degrees of freedom (t, chi2) or numerator df (F).")),
            Opt("df2", Expression("Denominator degrees of freedom (F).")),
            Opt("n", Expression("Number of trials (binomial).")),
            Opt("p", Expression("Success probability (binomial, geometric)."))),
        OutputSchema = MathResult(),
        Handler = c => Guard(() =>
        {
            var dist = c.Choice("distribution", "", Distributions);
            var op = c.Choice("operation", "", "pdf", "cdf", "inverse");
            Expr A(string name) => Parse(c, name);
            Expr Or(string name, Expr fallback) => c.Has(name) ? Parse(c, name) : fallback;
            Expr inf = ConstantExpr.Infinity, ninf = ConstantExpr.NegativeInfinity;
            Expr Tail() => new StringExpr(c.Choice("tail", "left", "left", "right", "center"));
            Expr call = (dist, op) switch
            {
                ("normal", "pdf") => CallExpr.Of("normalpdf", A("x"), Or("mean", Expr.Int(0)), Or("sd", Expr.Int(1))),
                ("normal", "cdf") => CallExpr.Of("normalcdf", Or("lower", ninf), Or("upper", inf), Or("mean", Expr.Int(0)), Or("sd", Expr.Int(1))),
                ("normal", _) => CallExpr.Of("invnorm", A("area"), Or("mean", Expr.Int(0)), Or("sd", Expr.Int(1)), Tail()),
                ("t", "pdf") => CallExpr.Of("tpdf", A("x"), A("df")),
                ("t", "cdf") => CallExpr.Of("tcdf", Or("lower", ninf), Or("upper", inf), A("df")),
                ("t", _) => CallExpr.Of("invt", A("area"), A("df"), Tail()),
                ("chi2", "pdf") => CallExpr.Of("chi2pdf", A("x"), A("df")),
                ("chi2", "cdf") => CallExpr.Of("chi2cdf", Or("lower", Expr.Int(0)), Or("upper", inf), A("df")),
                ("chi2", _) => CallExpr.Of("invchi2", A("area"), A("df"), Tail()),
                ("f", "pdf") => CallExpr.Of("fpdf", A("x"), A("df"), A("df2")),
                ("f", "cdf") => CallExpr.Of("fcdf", Or("lower", Expr.Int(0)), Or("upper", inf), A("df"), A("df2")),
                ("f", _) => CallExpr.Of("invf", A("area"), A("df"), A("df2"), Tail()),
                ("binomial", "pdf") => CallExpr.Of("binompdf", A("n"), A("p"), A("x")),
                ("binomial", "cdf") => c.Has("lower") ? CallExpr.Of("binomcdf", A("n"), A("p"), A("lower"), A("upper")) : CallExpr.Of("binomcdf", A("n"), A("p"), A("upper")),
                ("binomial", _) => CallExpr.Of("invbinom", A("area"), A("n"), A("p")),
                ("poisson", "pdf") => CallExpr.Of("poissonpdf", A("mean"), A("x")),
                ("poisson", "cdf") => c.Has("lower") ? CallExpr.Of("poissoncdf", A("mean"), A("lower"), A("upper")) : CallExpr.Of("poissoncdf", A("mean"), A("upper")),
                ("poisson", _) => CallExpr.Of("invpoisson", A("area"), A("mean")),
                ("geometric", "pdf") => CallExpr.Of("geometpdf", A("p"), A("x")),
                ("geometric", "cdf") => c.Has("lower") ? CallExpr.Of("geometcdf", A("p"), A("lower"), A("upper")) : CallExpr.Of("geometcdf", A("p"), A("upper")),
                _ => throw new ToolArgumentException("The geometric distribution has no inverse here; use cdf with a few values."),
            };
            return Result(Evaluator.Eval(call, Session.Context(false, c.Token)), c, false);
        }),
    };

    private McpTool Regress() => new()
    {
        Name = "regress",
        Title = "Fit a regression model",
        IsAvailable = () => Has("linreg"),
        Description =
            "Least squares fit of paired data with TI-84 models: linear (y = a x + b), quadratic, cubic, quartic, exponential (a b^x), exponential_e (a e^(b x)), " +
            "power (a x^b), logarithmic (a + b ln x), logistic, sinusoidal, median_median or polynomial of any degree. Returns the equation, parameters, r and r².",
        InputSchema = Object(
            Req("x", Data("The x values.")),
            Req("y", Data("The y values, one per x.")),
            Opt("model", String("The model (default linear).", RegressionModels)),
            Opt("degree", Integer("Degree for the polynomial model.", 1, 10))),
        Handler = c => Guard(() =>
        {
            var model = c.Choice("model", "linear", RegressionModels);
            var x = DataArg(c, "x");
            var y = DataArg(c, "y");
            string fn = model switch
            {
                "linear" => "linreg",
                "quadratic" => "quadreg",
                "cubic" => "cubicreg",
                "quartic" => "quartreg",
                "exponential" => "expreg",
                "exponential_e" => "expereg",
                "power" => "pwrreg",
                "logarithmic" => "lnreg",
                "logistic" => "logisticreg",
                "sinusoidal" => "sinreg",
                "median_median" => "medmed",
                _ => "polyreg",
            };
            Expr call = fn == "polyreg"
                ? CallExpr.Of(fn, x, y, Expr.Int(c.Int("degree", 2, 1, 10)))
                : CallExpr.Of(fn, x, y);
            var result = Evaluator.Eval(call, Session.Context(false, c.Token));
            var o = NamedValues(result, c.Token);
            if (result is ListExpr { Items: [RelationExpr { Left: SymbolExpr { Name: "y" } } eq, ..] })
            {
                o["equation"] = "y = " + ExprPrinter.Print(eq.Right, Display);
                o["equation_input"] = "y = " + ExprPrinter.ToInput(eq.Right);
                o["equation_latex"] = "y = " + LatexPrinter.ToLatex(eq.Right);
            }
            o["model"] = model;
            return ToolResult.Data(o, (string?)o["equation"] ?? model);
        }),
    };

    private McpTool HypothesisTest() => new()
    {
        Name = "hypothesis_test",
        Title = "Hypothesis test",
        IsAvailable = () => Has("ttest"),
        Description =
            "Runs a significance test and says whether to reject the null hypothesis at alpha. Tests: t (mu0 and data), z (mu0, sigma, data), " +
            "two_sample_t (data, data2, pooled), paired_t (data, data2), one_proportion_z (p0, x, n), two_proportion_z (x, n, x2, n2), " +
            "chi2_goodness_of_fit (observed, expected), chi2_independence (table), anova (groups), regression_slope (data as x, data2 as y), two_sample_f (data, data2).",
        InputSchema = Object(
            Req("test", String("Which test.", TestKinds)),
            Opt("data", Data("The sample (or the first sample, or the x values).")),
            Opt("data2", Data("The second sample (or the y values).")),
            Opt("mu0", Expression("Hypothesised mean.")),
            Opt("sigma", Expression("Known population standard deviation (z test).")),
            Opt("p0", Expression("Hypothesised proportion.")),
            Opt("x", Expression("Number of successes.")),
            Opt("n", Expression("Sample size.")),
            Opt("x2", Expression("Successes in the second sample.")),
            Opt("n2", Expression("Size of the second sample.")),
            Opt("observed", Data("Observed counts.")),
            Opt("expected", Data("Expected counts.")),
            Opt("table", Matrix("Contingency table of counts.")),
            Opt("groups", Array(Data("One group."), "The groups for ANOVA.")),
            Opt("alternative", String("Alternative hypothesis.", "two_sided", "less", "greater")),
            Opt("pooled", Boolean("Pool the variances (two_sample_t).", false)),
            Opt("alpha", Expression("Significance level (default 0.05)."))),
        Handler = c => Guard(() =>
        {
            var test = c.Choice("test", "", TestKinds);
            var alt = new StringExpr(c.Choice("alternative", "two_sided", "two_sided", "less", "greater") switch { "less" => "<", "greater" => ">", _ => "!=" });
            Expr P(string n) => Parse(c, n);
            Expr call = test switch
            {
                "t" => CallExpr.Of("ttest", P("mu0"), DataArg(c, "data"), alt),
                "z" => CallExpr.Of("ztest", P("mu0"), P("sigma"), DataArg(c, "data"), alt),
                "two_sample_t" => CallExpr.Of("ttest2", DataArg(c, "data"), DataArg(c, "data2"), alt, c.Bool("pooled", false) ? BoolExpr.True : BoolExpr.False),
                "paired_t" => CallExpr.Of("pairedttest", DataArg(c, "data"), DataArg(c, "data2"), c.Has("mu0") ? P("mu0") : Expr.Int(0), alt),
                "one_proportion_z" => CallExpr.Of("propztest", P("p0"), P("x"), P("n"), alt),
                "two_proportion_z" => CallExpr.Of("propztest2", P("x"), P("n"), P("x2"), P("n2"), alt),
                "chi2_goodness_of_fit" => CallExpr.Of("chi2gof", DataArg(c, "observed"), DataArg(c, "expected")),
                "chi2_independence" => CallExpr.Of("chi2test", MatrixArg(c, "table")),
                "anova" => new CallExpr("anova", [.. Groups(c)]),
                "regression_slope" => CallExpr.Of("linregttest", DataArg(c, "data"), DataArg(c, "data2"), alt),
                _ => CallExpr.Of("ftest2", DataArg(c, "data"), DataArg(c, "data2"), alt),
            };
            var result = Evaluator.Eval(call, Session.Context(false, c.Token));
            var o = NamedValues(result, c.Token);
            double alpha = c.Has("alpha") ? NumberOf(Parse(c, "alpha"), c) : 0.05;
            if (o["p"] is JsonObject pObj && pObj["value"] is JsonValue pv && pv.TryGetValue<double>(out var p))
            {
                o["alpha"] = alpha;
                o["decision"] = p <= alpha ? "Reject the null hypothesis" : "Do not reject the null hypothesis";
            }
            o["test"] = test;
            return ToolResult.Data(o, (string?)o["decision"] ?? test);
        }),
    };

    private McpTool ConfidenceInterval() => new()
    {
        Name = "confidence_interval",
        Title = "Confidence interval",
        IsAvailable = () => Has("tinterval"),
        Description =
            "Confidence intervals: t (data), z (sigma, data), one_proportion (x, n), two_proportion (x, n, x2, n2), two_sample_t (data, data2). Level as 0.95 or 95.",
        InputSchema = Object(
            Req("kind", String("Which interval.", IntervalKinds)),
            Opt("data", Data("The sample.")),
            Opt("data2", Data("The second sample.")),
            Opt("sigma", Expression("Known population standard deviation.")),
            Opt("x", Expression("Successes.")),
            Opt("n", Expression("Sample size.")),
            Opt("x2", Expression("Successes in the second sample.")),
            Opt("n2", Expression("Size of the second sample.")),
            Opt("level", Expression("Confidence level (default 0.95)."))),
        Handler = c => Guard(() =>
        {
            var kind = c.Choice("kind", "", IntervalKinds);
            Expr level = c.Has("level") ? Parse(c, "level") : Expr.Rational(95, 100);
            Expr P(string n) => Parse(c, n);
            Expr call = kind switch
            {
                "t" => CallExpr.Of("tinterval", DataArg(c, "data"), level),
                "z" => CallExpr.Of("zinterval", P("sigma"), DataArg(c, "data"), level),
                "one_proportion" => CallExpr.Of("propzint", P("x"), P("n"), level),
                "two_proportion" => CallExpr.Of("propzint2", P("x"), P("n"), P("x2"), P("n2"), level),
                _ => CallExpr.Of("tinterval2", DataArg(c, "data"), DataArg(c, "data2"), level),
            };
            var result = Evaluator.Eval(call, Session.Context(false, c.Token));
            if (result is not ListExpr { Items: [ListExpr bounds, ..] } list) throw new CalcException("Unexpected interval result");
            var o = NamedValues(new ListExpr(list.Items.RemoveAt(0)), c.Token);
            o["lower"] = Brief(bounds.Items[0], c.Token);
            o["upper"] = Brief(bounds.Items[1], c.Token);
            o["kind"] = kind;
            return ToolResult.Data(o, $"({ExprPrinter.Print(bounds.Items[0], Display)}, {ExprPrinter.Print(bounds.Items[1], Display)})");
        }),
    };

    private McpTool FinanceTvm() => new()
    {
        Name = "finance_tvm",
        Title = "Loans and savings (time value of money)",
        Description =
            "Solves the TI-84 TVM equation for one of n (number of payments), rate (annual %), pv, pmt or fv. Sign convention: money you receive is positive, " +
            "money you pay is negative (a 200000 loan has pv = 200000 and a negative pmt). Defaults: 12 payments per year, compounding equal to payments, payments at the end.",
        InputSchema = Object(
            Req("solve_for", String("The unknown.", "n", "rate", "pv", "pmt", "fv")),
            Opt("n", Expression("Number of payments.")),
            Opt("rate", Expression("Annual interest rate in percent.")),
            Opt("pv", Expression("Present value.")),
            Opt("pmt", Expression("Payment per period.")),
            Opt("fv", Expression("Future value (default 0).")),
            Opt("payments_per_year", Expression("P/Y (default 12).")),
            Opt("compounding_per_year", Expression("C/Y (default P/Y).")),
            Opt("timing", String("When payments happen.", "end", "begin"))),
        Handler = c => Guard(() =>
        {
            var unknown = c.Choice("solve_for", "", "n", "rate", "pv", "pmt", "fv");
            double Get(string name, double? fallback = null)
            {
                if (c.Has(name)) return NumberOf(Parse(c, name), c);
                return fallback ?? (name == unknown ? 0 : throw new ToolArgumentException($"Give {name}."));
            }
            var values = new TvmValues(
                Get("n", unknown == "n" ? 0 : null),
                Get("rate", unknown == "rate" ? 0 : null),
                Get("pv", unknown == "pv" ? 0 : null),
                Get("pmt", unknown == "pmt" ? 0 : null),
                Get("fv", 0),
                Get("payments_per_year", 12),
                c.Has("compounding_per_year") ? Get("compounding_per_year") : null,
                c.Choice("timing", "end", "end", "begin") == "begin" ? PaymentTiming.Begin : PaymentTiming.End);
            var variable = unknown switch
            {
                "n" => TvmVariable.N,
                "rate" => TvmVariable.InterestRate,
                "pv" => TvmVariable.PresentValue,
                "pmt" => TvmVariable.Payment,
                _ => TvmVariable.FutureValue,
            };
            var solved = Tvm.SolveFor(variable, values);
            double answer = Tvm.Solve(variable, values);
            var o = new JsonObject
            {
                ["solve_for"] = unknown,
                ["value"] = answer,
                ["rounded"] = Math.Round(answer, unknown == "n" ? 4 : 2),
                ["n"] = solved.N,
                ["rate"] = solved.InterestRate,
                ["pv"] = solved.PresentValue,
                ["pmt"] = solved.Payment,
                ["fv"] = solved.FutureValue,
            };
            if (solved.N > 0 && solved.N == Math.Floor(solved.N) && solved.N <= 10_000)
            {
                double paid = solved.Payment * solved.N;
                o["total_payments"] = paid;
                o["total_interest"] = -(paid + solved.PresentValue + solved.FutureValue);
            }
            return ToolResult.Data(o, $"{unknown} = {answer.ToString("G10", CultureInfo.InvariantCulture)}");
        }),
    };

    private McpTool CashFlowTool() => new()
    {
        Name = "cash_flow",
        Title = "Cash flow analysis",
        Description = "Net present value, internal rate of return, MIRR and payback period of an investment: cf0 now (usually negative) and later cash flows per period.",
        InputSchema = Object(
            Req("cf0", Expression("Cash flow at time 0 (negative for an investment).")),
            Req("flows", Data("Cash flows for periods 1, 2, ...")),
            Opt("frequencies", Data("How many times each flow repeats (default 1 each).")),
            Opt("rate", Expression("Discount rate in percent for NPV and discounted payback.")),
            Opt("finance_rate", Expression("Finance rate in percent for MIRR.")),
            Opt("reinvest_rate", Expression("Reinvestment rate in percent for MIRR."))),
        Handler = c => Guard(() =>
        {
            double cf0 = NumberOf(Parse(c, "cf0"), c);
            var flows = Doubles(DataArg(c, "flows"), c);
            var freq = c.Has("frequencies") ? Doubles(DataArg(c, "frequencies"), c) : null;
            var o = new JsonObject { ["cf0"] = cf0 };
            var irrs = CashFlow.IrrAll(cf0, flows, freq);
            if (irrs.Count > 0) o["irr_percent"] = CashFlow.Irr(cf0, flows, freq);
            if (irrs.Count > 1) o["note"] = "The cash flows change sign more than once, so there are several IRRs: " + string.Join(", ", irrs.Select(v => v.ToString("G8", CultureInfo.InvariantCulture))) + ".";
            if (CashFlow.PaybackPeriod(cf0, flows, freq) is double pb) o["payback_periods"] = pb;
            if (c.Has("rate"))
            {
                double rate = NumberOf(Parse(c, "rate"), c);
                o["npv"] = CashFlow.Npv(rate, cf0, flows, freq);
                if (CashFlow.DiscountedPaybackPeriod(rate, cf0, flows, freq) is double dpb) o["discounted_payback_periods"] = dpb;
            }
            if (c.Has("finance_rate") && c.Has("reinvest_rate"))
                o["mirr_percent"] = CashFlow.Mirr(NumberOf(Parse(c, "finance_rate"), c), NumberOf(Parse(c, "reinvest_rate"), c), cf0, flows, freq);
            return ToolResult.Data(o, o.ContainsKey("npv") ? $"NPV {((double)o["npv"]!).ToString("G8", CultureInfo.InvariantCulture)}" : "cash flows");
        }),
    };

    // ---------------------------------------------------------------- helpers

    private IEnumerable<Expr> Groups(ToolCall c)
    {
        if (c.Node("groups") is not JsonArray groups || groups.Count < 2) throw new ToolArgumentException("Give at least two groups.");
        foreach (var g in groups)
        {
            if (g is JsonArray arr)
                yield return new ListExpr([.. arr.Select((v, i) => ParseText(ItemText(v, "groups"), $"groups item {i + 1}"))]);
            else yield return ParseText(ItemText(g, "groups"), "groups");
        }
    }

    /// <summary>{t = 2.1, p = 0.04, ...} as a JSON object of named values.</summary>
    private JsonObject NamedValues(Expr result, CancellationToken ct)
    {
        var o = new JsonObject();
        if (result is not ListExpr list) throw new CalcException("Unexpected result " + ExprPrinter.Print(result, Display));
        foreach (var item in list.Items)
        {
            if (item is RelationExpr { Op: RelOp.Equal, Left: SymbolExpr s } r)
            {
                if (s.Name == "y") continue;
                var b = Brief(r.Right, ct);
                if (Evaluator.Numeric(r.Right, Session.Context(false, ct)) is { IsReal: true } num) b["value"] = num.Re;
                o[s.Name] = b;
            }
        }
        return o;
    }

    private double NumberOf(Expr e, ToolCall c)
    {
        var v = Evaluator.Numeric(e, Session.Context(false, c.Token));
        if (v is { IsReal: true } n) return n.Re;
        throw new ToolArgumentException($"{ExprPrinter.Print(e, Display)} must be a real number.");
    }

    private double[] Doubles(Expr list, ToolCall c)
    {
        var value = Evaluator.Eval(list, Session.Context(false, c.Token));
        if (value is not ListExpr l) throw new ToolArgumentException("Expected a list of numbers.");
        return [.. l.Items.Select(i => NumberOf(i, c))];
    }
}
