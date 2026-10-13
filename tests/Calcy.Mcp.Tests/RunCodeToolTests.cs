using System.Text.Json.Nodes;
using Calcy.Engine;
using Calcy.Mcp;

namespace Calcy.Mcp.Tests;

public class RunCodeToolTests
{
    private static McpServer Server(bool allowCode, TimeSpan? timeout = null) => CalcyMcp.CreateServer(
        new McpServerOptions { AllowCode = allowCode, ToolTimeout = timeout ?? TimeSpan.FromSeconds(30) },
        CalcyEngine.CreateSession());

    private static async Task<JsonObject> Call(McpServer s, JsonObject args)
    {
        var r = (JsonObject)(await s.HandleAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = "run_code", ["arguments"] = args },
        }))!;
        Assert.True(r.ContainsKey("result"), r.ToJsonString());
        return (JsonObject)r["result"]!;
    }

    [Fact]
    public async Task HiddenUnlessCodeIsAllowed()
    {
        var r = await Server(false).HandleAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list" });
        Assert.DoesNotContain(r!["result"]!["tools"]!.AsArray(), t => (string)t!["name"]! == "run_code");
        var r2 = await Server(true).HandleAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list" });
        Assert.Contains(r2!["result"]!["tools"]!.AsArray(), t => (string)t!["name"]! == "run_code");
    }

    [Fact]
    public async Task RunsWithInputAndTheCalcModule()
    {
        var result = await Call(Server(true), new JsonObject
        {
            ["code"] = "import calc\nname = input('Name? ')\nprint('Hi', name)\nprint(calc.eval('1/3 + 1/6'))\nprint(sum(k * k for k in range(10)))",
            ["stdin"] = new JsonArray("Ada"),
        });
        Assert.False(result["isError"]?.GetValue<bool>() ?? false, result.ToJsonString());
        var output = (string)result["structuredContent"]!["output"]!;
        Assert.Contains("Hi Ada", output);
        Assert.Contains("1/2", output);
        Assert.Contains("285", output);
    }

    [Fact]
    public async Task ErrorsComeBackWithTheLine()
    {
        var result = await Call(Server(true), new JsonObject { ["code"] = "x = 1\ny = x / 0\n" });
        Assert.True(result["isError"]?.GetValue<bool>() ?? false);
        Assert.Equal("ZeroDivisionError", (string)result["structuredContent"]!["error"]!["type"]!);
        Assert.Equal(2, (int)result["structuredContent"]!["error"]!["line"]!);
    }

    [Fact]
    public async Task SandboxBlocksFiles()
    {
        var result = await Call(Server(true), new JsonObject { ["code"] = "open('secret.txt').read()" });
        Assert.True(result["isError"]?.GetValue<bool>() ?? false);
    }

    [Fact]
    public async Task EndlessLoopsAreStopped()
    {
        var result = await Call(Server(true), new JsonObject { ["code"] = "while True:\n    pass", ["timeout_seconds"] = 1 });
        Assert.True(result["isError"]?.GetValue<bool>() ?? false);
        Assert.Equal("timedout", (string)result["structuredContent"]!["status"]!);
    }

    [Fact]
    public async Task TurtleDrawingsComeBackAsAnImage()
    {
        var result = await Call(Server(true), new JsonObject
        {
            ["code"] = "import turtle\nfor i in range(5):\n    turtle.forward(80)\n    turtle.right(144)\n",
        });
        Assert.False(result["isError"]?.GetValue<bool>() ?? false, result.ToJsonString());
        var image = result["content"]!.AsArray().FirstOrDefault(x => (string)x!["type"]! == "image");
        Assert.NotNull(image);
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "run-code-turtle.png"), Convert.FromBase64String((string)image!["data"]!));
    }
}