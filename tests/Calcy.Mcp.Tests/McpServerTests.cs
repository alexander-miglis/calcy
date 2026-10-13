using System.Text.Json.Nodes;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Mcp;

namespace Calcy.Mcp.Tests;

public class McpServerTests
{
    private int _id;

    private static McpServer Server(McpServerOptions? options = null, CalcSession? session = null, IEnumerable<McpTool>? extra = null) =>
        CalcyMcp.CreateServer(options, session ?? new CalcSession(new CalcSettings(), FunctionRegistry.CreateDefault()), extra);

    private JsonObject Request(string method, JsonObject? p = null)
    {
        var o = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = method };
        if (p is not null) o["params"] = p;
        return o;
    }

    private async Task<JsonObject> Send(McpServer s, string method, JsonObject? p = null) =>
        (JsonObject)(await s.HandleAsync(Request(method, p)))!;

    private async Task<JsonObject> Call(McpServer s, string tool, JsonObject args)
    {
        var r = await Send(s, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args });
        Assert.True(r.ContainsKey("result"), r.ToJsonString());
        return (JsonObject)r["result"]!;
    }

    private static string Text(JsonObject result) => (string)result["content"]![0]!["text"]!;
    private static bool IsError(JsonObject result) => result["isError"]?.GetValue<bool>() ?? false;
    private static string Field(JsonObject result, string name) => (string)result["structuredContent"]![name]!;

    [Fact]
    public async Task Initialize_NegotiatesVersionAndDescribesServer()
    {
        var s = Server();
        var r = await Send(s, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "test-client", ["version"] = "1" },
        });
        var result = r["result"]!;
        Assert.Equal("2025-06-18", (string)result["protocolVersion"]!);
        Assert.Equal("calcy", (string)result["serverInfo"]!["name"]!);
        Assert.NotNull(result["capabilities"]!["tools"]);
        Assert.Contains("exact", (string)result["instructions"]!);
        Assert.Equal("test-client", s.ClientName);
    }

    [Fact]
    public async Task Initialize_UnknownVersion_AnswersWithNewest()
    {
        var r = await Send(Server(), "initialize", new JsonObject { ["protocolVersion"] = "1999-01-01" });
        Assert.Equal(McpServer.SupportedProtocolVersions[0], (string)r["result"]!["protocolVersion"]!);
    }

    [Fact]
    public async Task ToolsList_HasSchemasAndAnnotations()
    {
        var r = await Send(Server(), "tools/list");
        var tools = r["result"]!["tools"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => (string)t["name"]!);
        Assert.Contains("evaluate", tools.Keys);
        Assert.Contains("define", tools.Keys);
        Assert.Contains("matrix", tools.Keys);
        Assert.Equal("object", (string)tools["evaluate"]["inputSchema"]!["type"]!);
        Assert.True((bool)tools["evaluate"]["annotations"]!["readOnlyHint"]!);
        Assert.False((bool)tools["define"]["annotations"]!["readOnlyHint"]!);
        Assert.True((bool)tools["clear_variables"]["annotations"]!["destructiveHint"]!);
        Assert.False((bool)tools["evaluate"]["annotations"]!["openWorldHint"]!);
        // The core-only registry has no solver, so the solve tool is not offered.
        Assert.DoesNotContain("solve", tools.Keys);
        Assert.DoesNotContain("differentiate", tools.Keys);
    }

    [Fact]
    public async Task ToolsList_OffersToolsWhenTheirFunctionsExist()
    {
        var registry = FunctionRegistry.CreateDefault();
        registry.Add(new FunctionDefinition { Name = "solve", MinArgs = 2, MaxArgs = 2, Listable = false, HoldArgs = true, Evaluate = _ => Expr.Int(2) });
        var r = await Send(Server(session: new CalcSession(new CalcSettings(), registry)), "tools/list");
        Assert.Contains(r["result"]!["tools"]!.AsArray(), t => (string)t!["name"]! == "solve");
    }

    [Fact]
    public async Task Evaluate_GivesExactDecimalAndLatex()
    {
        var res = await Call(Server(), "evaluate", new JsonObject { ["expression"] = "1/3 + 1/6" });
        Assert.False(IsError(res));
        Assert.Equal("1/2", Field(res, "result"));
        Assert.Equal("0.5", Field(res, "decimal"));
        Assert.Equal(@"\frac{1}{2}", Field(res, "latex"));
        Assert.Equal("number", Field(res, "kind"));
        Assert.Contains("1/2", Text(res));
    }

    [Fact]
    public async Task Evaluate_Radicals()
    {
        var res = await Call(Server(), "evaluate", new JsonObject { ["expression"] = "sqrt(72)" });
        Assert.Equal("6√2", Field(res, "result"));
        Assert.Equal("6*sqrt(2)", Field(res, "input"));
        Assert.StartsWith("8.48528137423857", Field(res, "decimal"));
    }

    [Fact]
    public async Task Evaluate_AcceptsJsonNumbersExactly()
    {
        var res = await Call(Server(), "evaluate", new JsonObject { ["expression"] = 0.1 });
        Assert.Equal("1/10", Field(res, "result"));
    }

    [Fact]
    public async Task Evaluate_WithVariablesForOneCall()
    {
        var s = Server();
        var res = await Call(s, "evaluate", new JsonObject { ["expression"] = "x^2 + 1", ["variables"] = new JsonObject { ["x"] = 3 } });
        Assert.Equal("10", Field(res, "result"));
        var again = await Call(s, "evaluate", new JsonObject { ["expression"] = "x^2 + 1" });
        Assert.Equal("x² + 1", Field(again, "result"));
    }

    [Fact]
    public async Task Evaluate_Approximate()
    {
        var res = await Call(Server(), "evaluate", new JsonObject { ["expression"] = "pi", ["approximate"] = true });
        Assert.StartsWith("3.14159265358979", Field(res, "result"));
        Assert.False((bool)res["structuredContent"]!["exact"]!);
    }

    [Fact]
    public async Task Evaluate_ParseErrorIsAToolError()
    {
        var res = await Call(Server(), "evaluate", new JsonObject { ["expression"] = "1 + (2" });
        Assert.True(IsError(res));
        Assert.Contains("bracket", Text(res));
        Assert.Contains("1 + (2)", Text(res));
    }

    [Fact]
    public async Task Evaluate_MissingArgumentIsAToolError()
    {
        var res = await Call(Server(), "evaluate", new JsonObject());
        Assert.True(IsError(res));
        Assert.Contains("expression", Text(res));
    }

    [Fact]
    public async Task Evaluate_RejectsDefinitions()
    {
        var res = await Call(Server(), "evaluate", new JsonObject { ["expression"] = "a = 5" });
        Assert.True(IsError(res));
        Assert.Contains("define", Text(res));
    }

    [Fact]
    public async Task Define_ThenUseInLaterCalls()
    {
        var s = Server();
        var d = await Call(s, "define", new JsonObject { ["definition"] = "f(x) = x^2 - 5x + 6" });
        Assert.False(IsError(d));
        Assert.Equal("function", Field(d, "kind"));
        await Call(s, "define", new JsonObject { ["definition"] = "a = 5" });
        var v = await Call(s, "evaluate", new JsonObject { ["expression"] = "f(a)" });
        Assert.Equal("6", Field(v, "result"));
        var vars = await Call(s, "get_variables", new JsonObject());
        var names = vars["structuredContent"]!["variables"]!.AsArray().Select(x => (string)x!["name"]!).ToList();
        Assert.Contains("a", names);
        Assert.Contains(vars["structuredContent"]!["functions"]!.AsArray(), f => (string)f!["name"]! == "f");
        var cleared = await Call(s, "clear_variables", new JsonObject { ["names"] = new JsonArray("a") });
        Assert.False(IsError(cleared));
        var after = await Call(s, "evaluate", new JsonObject { ["expression"] = "f(a)" });
        Assert.Equal("a² - 5a + 6", Field(after, "result"));
    }

    [Fact]
    public async Task Define_NothingDefinedIsAnError()
    {
        var res = await Call(Server(), "define", new JsonObject { ["definition"] = "2 + 2" });
        Assert.True(IsError(res));
    }

    [Fact]
    public async Task ReadOnlyMode_RefusesStateChanges()
    {
        var s = Server(new McpServerOptions { ReadOnly = true });
        var res = await Call(s, "define", new JsonObject { ["definition"] = "a = 5" });
        Assert.True(IsError(res));
        Assert.Contains("read-only", Text(res));
        var ok = await Call(s, "evaluate", new JsonObject { ["expression"] = "2 + 2" });
        Assert.Equal("4", Field(ok, "result"));
    }

    [Fact]
    public async Task DisabledTool_IsHiddenAndUnknown()
    {
        var s = Server(new McpServerOptions { DisabledTools = new HashSet<string> { "define" } });
        var list = await Send(s, "tools/list");
        Assert.DoesNotContain(list["result"]!["tools"]!.AsArray(), t => (string)t!["name"]! == "define");
        var r = await Send(s, "tools/call", new JsonObject { ["name"] = "define", ["arguments"] = new JsonObject { ["definition"] = "a = 1" } });
        Assert.Equal(McpProtocolException.InvalidParams, (int)r["error"]!["code"]!);
    }

    [Fact]
    public async Task Settings_DegreesChangeTrig()
    {
        var s = Server();
        var set = await Call(s, "settings", new JsonObject { ["angle"] = "degrees" });
        Assert.Equal("degrees", Field(set, "angle"));
        var v = await Call(s, "evaluate", new JsonObject { ["expression"] = "sin(30)" });
        Assert.Equal("1/2", Field(v, "result"));
    }

    [Fact]
    public async Task Matrix_DeterminantAndInverse()
    {
        var s = Server();
        var m = new JsonArray(new JsonArray(1, 2), new JsonArray(3, 4));
        var det = await Call(s, "matrix", new JsonObject { ["operation"] = "determinant", ["matrix"] = m.DeepClone() });
        Assert.Equal("-2", Field(det, "result"));
        var inv = await Call(s, "matrix", new JsonObject { ["operation"] = "inverse", ["matrix"] = "[[1, 2], [3, 4]]" });
        Assert.Equal("matrix", Field(inv, "kind"));
        Assert.Equal("[[-2, 1], [3/2, -1/2]]", Field(inv, "input"));
        var prod = await Call(s, "matrix", new JsonObject { ["operation"] = "multiply", ["matrix"] = m.DeepClone(), ["other"] = "[[1, 0], [0, 1]]" });
        Assert.Equal("[[1, 2], [3, 4]]", Field(prod, "input"));
    }

    [Fact]
    public async Task Matrix_RaggedRowsAreReported()
    {
        var res = await Call(Server(), "matrix", new JsonObject
        {
            ["operation"] = "determinant",
            ["matrix"] = new JsonArray(new JsonArray(1, 2), new JsonArray(3)),
        });
        Assert.True(IsError(res));
        Assert.Contains("row 2", Text(res));
    }

    [Fact]
    public async Task LinearSolve_Exact()
    {
        var res = await Call(Server(), "linear_solve", new JsonObject
        {
            ["matrix"] = new JsonArray(new JsonArray(2, 1), new JsonArray(1, 3)),
            ["vector"] = new JsonArray(3, 5),
        });
        Assert.False(IsError(res), Text(res));
        Assert.Contains("4/5", Field(res, "result"));
        Assert.Contains("7/5", Field(res, "result"));
    }

    [Fact]
    public async Task DescribeData_Statistics()
    {
        var res = await Call(Server(), "describe_data", new JsonObject { ["data"] = new JsonArray(2, 4, 4, 4, 5, 5, 7, 9) });
        var sc = res["structuredContent"]!;
        Assert.Equal(8, (int)sc["count"]!);
        Assert.Equal("5", (string)sc["mean"]!["result"]!);
        Assert.Equal("2", (string)sc["population_stdev"]!["result"]!);
        Assert.Equal("40", (string)sc["sum"]!["result"]!);
    }

    [Fact]
    public async Task FunctionHelp_AndSuggestions()
    {
        var s = Server();
        var ok = await Call(s, "function_help", new JsonObject { ["name"] = "nCr" });
        Assert.Equal("nCr", Field(ok, "name"));
        var bad = await Call(s, "function_help", new JsonObject { ["name"] = "fact" });
        Assert.True(IsError(bad));
        Assert.Contains("factorial", Text(bad));
    }

    [Fact]
    public async Task ListFunctions_Search()
    {
        var res = await Call(Server(), "list_functions", new JsonObject { ["search"] = "prime" });
        var names = res["structuredContent"]!["functions"]!.AsArray().Select(f => (string)f!["name"]!).ToList();
        Assert.Contains("isprime", names);
    }

    [Fact]
    public async Task UnknownMethod_ParseError_AndNotifications()
    {
        var s = Server();
        var r = await Send(s, "does/not/exist");
        Assert.Equal(McpProtocolException.MethodNotFound, (int)r["error"]!["code"]!);
        var parse = await s.HandleLineAsync("{not json");
        var parsed = JsonNode.Parse(parse!)!;
        Assert.Equal(McpProtocolException.ParseError, (int)parsed["error"]!["code"]!);
        Assert.Null(parsed["id"]);
        var note = await s.HandleAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        Assert.Null(note);
        var ping = await Send(s, "ping");
        Assert.Empty(ping["result"]!.AsObject());
    }

    [Fact]
    public async Task UnknownTool_IsInvalidParams()
    {
        var r = await Send(Server(), "tools/call", new JsonObject { ["name"] = "nope", ["arguments"] = new JsonObject() });
        Assert.Equal(McpProtocolException.InvalidParams, (int)r["error"]!["code"]!);
    }

    [Fact]
    public async Task Resources_ListAndRead()
    {
        var s = Server();
        var list = await Send(s, "resources/list");
        Assert.Contains(list["result"]!["resources"]!.AsArray(), r => (string)r!["uri"]! == "calcy://guide/syntax");
        var templates = await Send(s, "resources/templates/list");
        Assert.Equal(2, templates["result"]!["resourceTemplates"]!.AsArray().Count);
        var read = await Send(s, "resources/read", new JsonObject { ["uri"] = "calcy://reference/functions/nCr" });
        Assert.Contains("nCr", (string)read["result"]!["contents"]![0]!["text"]!);
        var syntax = await Send(s, "resources/read", new JsonObject { ["uri"] = "calcy://guide/syntax" });
        Assert.Contains("1/2x", (string)syntax["result"]!["contents"]![0]!["text"]!);
        var missing = await Send(s, "resources/read", new JsonObject { ["uri"] = "calcy://nothing" });
        Assert.Equal(McpProtocolException.ResourceNotFound, (int)missing["error"]!["code"]!);
    }

    [Fact]
    public async Task Prompts_ListAndGet()
    {
        var s = Server();
        var list = await Send(s, "prompts/list");
        Assert.Contains(list["result"]!["prompts"]!.AsArray(), p => (string)p!["name"]! == "check_solution");
        var get = await Send(s, "prompts/get", new JsonObject
        {
            ["name"] = "check_solution",
            ["arguments"] = new JsonObject { ["problem"] = "Solve 2x = 6", ["solution"] = "x = 3" },
        });
        var text = (string)get["result"]!["messages"]![0]!["content"]!["text"]!;
        Assert.Contains("Solve 2x = 6", text);
        var missing = await Send(s, "prompts/get", new JsonObject { ["name"] = "check_solution", ["arguments"] = new JsonObject() });
        Assert.Equal(McpProtocolException.InvalidParams, (int)missing["error"]!["code"]!);
    }

    private static McpTool SlowTool() => new()
    {
        Name = "slow",
        Title = "Slow",
        Description = "Waits.",
        InputSchema = Schema.Object(),
        Handler = c =>
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < until)
            {
                c.Token.ThrowIfCancellationRequested();
                Thread.Sleep(5);
            }
            return ToolResult.Text("done");
        },
    };

    [Fact]
    public async Task Timeout_StopsLongCalls()
    {
        var s = Server(new McpServerOptions { ToolTimeout = TimeSpan.FromMilliseconds(200) }, extra: [SlowTool()]);
        var res = await Call(s, "slow", new JsonObject());
        Assert.True(IsError(res));
        Assert.Contains("longer than", Text(res));
    }

    [Fact]
    public async Task Cancellation_GetsNoResponse()
    {
        var s = Server(extra: [SlowTool()]);
        var req = Request("tools/call", new JsonObject { ["name"] = "slow", ["arguments"] = new JsonObject() });
        var pending = s.HandleAsync(req);
        await Task.Delay(100);
        await s.HandleAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/cancelled",
            ["params"] = new JsonObject { ["requestId"] = req["id"]!.DeepClone(), ["reason"] = "user" },
        });
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(result);
    }

    [Fact]
    public async Task Stdio_RoundTrip()
    {
        var s = Server();
        var lines = string.Join("\n",
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"evaluate","arguments":{"expression":"2^10"}}}""",
            "");
        var output = new StringWriter();
        await s.RunAsync(new StringReader(lines), output);
        var responses = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.Equal(2, responses.Count);
        var call = responses.Single(r => (int)r["id"]! == 2);
        Assert.Equal("1024", (string)call["result"]!["structuredContent"]!["result"]!);
    }

    [Fact]
    public async Task Batch_IsAnsweredAsArray()
    {
        var s = Server();
        var batch = new JsonArray(Request("ping"), Request("tools/list"));
        var r = await s.HandleAsync(batch);
        Assert.Equal(2, r!.AsArray().Count);
    }

    [Fact]
    public async Task Audit_RecordsCalls()
    {
        var entries = new List<AuditEntry>();
        var s = Server(new McpServerOptions { Audit = e => { lock (entries) entries.Add(e); } });
        await Send(s, "initialize", new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "auditor" } });
        await Call(s, "evaluate", new JsonObject { ["expression"] = "1 + 1" });
        var e = Assert.Single(entries);
        Assert.Equal("evaluate", e.Tool);
        Assert.Equal("auditor", e.Client);
        Assert.False(e.IsError);
        Assert.Equal("2", e.Summary);
    }
}
