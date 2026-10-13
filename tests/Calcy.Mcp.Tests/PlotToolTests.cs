using System.Text.Json.Nodes;
using Calcy.Engine;
using Calcy.Mcp;

namespace Calcy.Mcp.Tests;

public class PlotToolTests
{
    private readonly McpServer _server = CalcyMcp.CreateServer(session: CalcyEngine.CreateSession());

    private async Task<JsonObject> Call(JsonObject args)
    {
        var r = (JsonObject)(await _server.HandleAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = "plot_2d", ["arguments"] = args },
        }))!;
        return (JsonObject)r["result"]!;
    }

    [Fact]
    public async Task Plot_ReturnsPngSvgAndPoints()
    {
        var result = await Call(new JsonObject
        {
            ["expressions"] = new JsonArray("y = x^2 - 2", "y = a*sin(x)", "x^2 + y^2 = 9", "y < x/2", "(2, 2)"),
            ["parameters"] = new JsonObject { ["a"] = 2 },
        });
        Assert.False(result["isError"]?.GetValue<bool>() ?? false, result.ToJsonString());
        var content = result["content"]!.AsArray();
        var image = content.Single(c => (string)c!["type"]! == "image");
        Assert.Equal("image/png", (string)image!["mimeType"]!);
        var png = Convert.FromBase64String((string)image["data"]!);
        Assert.Equal(0x89, png[0]);
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "plot-tool.png"), png);
        var svg = content.Single(c => (string)c!["type"]! == "resource");
        Assert.StartsWith("<svg", (string)svg!["resource"]!["text"]!);
        var points = result["structuredContent"]!["points_of_interest"]!.AsArray();
        // x^2 - 2 has roots at ±√2.
        Assert.Contains(points, p => (string)p!["kind"]! == "root" && Math.Abs((double)p["x"]! - Math.Sqrt(2)) < 1e-9);
        Assert.Equal(2, (double)result["structuredContent"]!["sliders"]!["a"]!);
    }

    [Fact]
    public async Task Plot_ReportsBadExpressions()
    {
        var result = await Call(new JsonObject { ["expressions"] = new JsonArray("y = (x +") });
        Assert.True(result["isError"]?.GetValue<bool>() ?? false);
    }

    [Fact]
    public async Task Plot_UnknownSliderIsAnError()
    {
        var result = await Call(new JsonObject { ["expressions"] = new JsonArray("y = x"), ["parameters"] = new JsonObject { ["b"] = 1 } });
        Assert.True(result["isError"]?.GetValue<bool>() ?? false);
    }
}