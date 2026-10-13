using System.Text.Json.Nodes;
using Calcy.Engine;
using Calcy.Mcp;

namespace Calcy.Mcp.Tests;

/// <summary>The tools that use the full engine: probability, regression, tests, finance and formulas.</summary>
public class StatisticsAndFormulaToolTests
{
    private int _id;
    private readonly McpServer _server = CalcyMcp.CreateServer(session: CalcyEngine.CreateSession());

    private async Task<JsonObject> Call(string tool, JsonObject args)
    {
        var r = (JsonObject)(await _server.HandleAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ++_id,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = args },
        }))!;
        Assert.True(r.ContainsKey("result"), r.ToJsonString());
        var result = (JsonObject)r["result"]!;
        Assert.False(result["isError"]?.GetValue<bool>() ?? false, (string?)result["content"]![0]!["text"]);
        return (JsonObject)result["structuredContent"]!;
    }

    private async Task<string> Error(string tool, JsonObject args)
    {
        var r = (JsonObject)(await _server.HandleAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ++_id,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = args },
        }))!;
        var result = (JsonObject)r["result"]!;
        Assert.True(result["isError"]?.GetValue<bool>() ?? false);
        return (string)result["content"]![0]!["text"]!;
    }

    private static double D(JsonNode? n) => double.Parse((string)n!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task AllToolsAreListedWithTheFullEngine()
    {
        var r = await _server.HandleAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list" });
        var names = r!["result"]!["tools"]!.AsArray().Select(t => (string)t!["name"]!).ToHashSet();
        foreach (var n in new[] { "distribution", "regress", "hypothesis_test", "confidence_interval", "finance_tvm", "cash_flow", "search_formulas", "get_formula", "solve_formula" })
            Assert.Contains(n, names);
    }

    [Fact]
    public async Task Distribution_NormalCdf()
    {
        var o = await Call("distribution", new JsonObject { ["distribution"] = "normal", ["operation"] = "cdf", ["lower"] = -1.96, ["upper"] = 1.96 });
        Assert.StartsWith("0.95000420970", (string)o["result"]!);
    }

    [Fact]
    public async Task Distribution_InverseAndBinomial()
    {
        var inv = await Call("distribution", new JsonObject { ["distribution"] = "normal", ["operation"] = "inverse", ["area"] = 0.975 });
        Assert.StartsWith("1.95996398454", (string)inv["result"]!);
        var b = await Call("distribution", new JsonObject { ["distribution"] = "binomial", ["operation"] = "pdf", ["n"] = 10, ["p"] = 0.5, ["x"] = 3 });
        Assert.Equal("0.1171875", (string)b["result"]!);
    }

    [Fact]
    public async Task Regress_Linear()
    {
        var o = await Call("regress", new JsonObject { ["x"] = new JsonArray(1, 2, 3, 4), ["y"] = new JsonArray(2.1, 3.9, 6.2, 7.8) });
        Assert.Equal("y = 1.94x + 0.15", (string)o["equation"]!);
        Assert.Equal("y = 1.94*x + 0.15", (string)o["equation_input"]!);
        Assert.Equal("1.94", (string)o["a"]!["result"]!);
    }

    [Fact]
    public async Task HypothesisTest_TTestDecides()
    {
        var o = await Call("hypothesis_test", new JsonObject { ["test"] = "t", ["mu0"] = 5, ["data"] = new JsonArray(5.1, 4.9, 5.6, 5.8, 6.0, 5.4) });
        Assert.Equal("Reject the null hypothesis", (string)o["decision"]!);
        Assert.StartsWith("0.0410291985", (string)o["p"]!["result"]!);
        var strict = await Call("hypothesis_test", new JsonObject { ["test"] = "t", ["mu0"] = 5, ["data"] = new JsonArray(5.1, 4.9, 5.6, 5.8, 6.0, 5.4), ["alpha"] = 0.01 });
        Assert.Equal("Do not reject the null hypothesis", (string)strict["decision"]!);
    }

    [Fact]
    public async Task HypothesisTest_ChiSquareTable()
    {
        var o = await Call("hypothesis_test", new JsonObject
        {
            ["test"] = "chi2_independence",
            ["table"] = new JsonArray(new JsonArray(10, 20), new JsonArray(30, 40)),
        });
        Assert.StartsWith("0.372998", (string)o["p"]!["result"]!);
    }

    [Fact]
    public async Task ConfidenceInterval_T()
    {
        var o = await Call("confidence_interval", new JsonObject { ["kind"] = "t", ["data"] = new JsonArray(5.1, 4.9, 5.6, 5.8, 6.0, 5.4) });
        Assert.StartsWith("5.02807454", (string)o["lower"]!["result"]!);
        Assert.StartsWith("5.90525878", (string)o["upper"]!["result"]!);
    }

    [Fact]
    public async Task FinanceTvm_MortgagePayment()
    {
        var o = await Call("finance_tvm", new JsonObject { ["solve_for"] = "pmt", ["n"] = 360, ["rate"] = 6, ["pv"] = 200000 });
        Assert.Equal(-1199.1, (double)o["rounded"]!, 2);
        Assert.True((double)o["total_interest"]! > 231_000);
    }

    [Fact]
    public async Task CashFlow_NpvAndIrr()
    {
        var o = await Call("cash_flow", new JsonObject { ["cf0"] = -1000, ["flows"] = new JsonArray(300, 400, 500), ["rate"] = 10 });
        Assert.Equal(-21.0368144252, (double)o["npv"]!, 8);
        Assert.Equal(8.896339469, (double)o["irr_percent"]!, 7);
    }

    [Fact]
    public async Task Formulas_SearchExplainSolve()
    {
        var s = await Call("search_formulas", new JsonObject { ["query"] = "suvat" });
        Assert.Contains(s["formulas"]!.AsArray(), f => (string)f!["id"]! == "suvat-v-u-a-t");
        var g = await Call("get_formula", new JsonObject { ["id"] = "suvat-v-u-a-t" });
        Assert.Equal("v = u + a*t", (string)g["equation"]!);
        Assert.NotEmpty((string)g["explanation"]!);
        var solved = await Call("solve_formula", new JsonObject
        {
            ["id"] = "suvat-v-u-a-t",
            ["solve_for"] = "a",
            ["values"] = new JsonObject { ["v"] = 25, ["u"] = 5, ["t"] = 8 },
        });
        Assert.Contains((string)solved["solutions"]![0]!["result"]!, new[] { "5/2", "2.5" });
        Assert.Equal("m/s²", (string)solved["unit"]!);
    }

    [Fact]
    public async Task Formulas_UnknownIdSuggests()
    {
        var msg = await Error("get_formula", new JsonObject { ["id"] = "pythagoras" });
        Assert.Contains("Did you mean", msg);
    }

    [Fact]
    public async Task Formulas_AreResources()
    {
        var r = await _server.HandleAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 99,
            ["method"] = "resources/read",
            ["params"] = new JsonObject { ["uri"] = "calcy://formulas/suvat-v-u-a-t" },
        });
        Assert.Contains("v = u + a*t", (string)r!["result"]!["contents"]![0]!["text"]!);
    }

    [Fact]
    public async Task UnitConvert_QueryAndValues()
    {
        var q = await Call("unit_convert", new JsonObject { ["query"] = "170 cm to ft in" });
        Assert.StartsWith("5 ft 6.9", (string)q["result"]!);
        var v = await Call("unit_convert", new JsonObject { ["value"] = 1, ["from"] = "lb", ["to"] = "kg" });
        Assert.True((bool)v["exact"]!);
        Assert.Equal("45359237/100000000", (string)v["value"]!["result"]!);
        var t = await Call("unit_convert", new JsonObject { ["value"] = 212, ["from"] = "F", ["to"] = "C" });
        Assert.Equal("100", (string)t["value"]!["result"]!);
    }

    [Fact]
    public async Task UnitConvert_IncompatibleUnitsExplain()
    {
        var msg = await Error("unit_convert", new JsonObject { ["value"] = 1, ["from"] = "km", ["to"] = "kg" });
        Assert.Contains("cannot be converted", msg);
    }

    [Fact]
    public async Task DateCalc_BetweenAndShift()
    {
        var b = await Call("date_calc", new JsonObject { ["from"] = "2026-01-01", ["to"] = "2026-12-25" });
        Assert.Equal(358, (int)b["days"]!);
        var s = await Call("date_calc", new JsonObject { ["request"] = "2026-01-31 + 1 month" });
        Assert.Equal("2026-02-28", (string)s["date"]!);
    }
}