using System.Text;
using System.Text.Json.Nodes;
using Calcy.Graphing;
using Calcy.Imaging;
using Calcy.Scripting;
using static Calcy.Mcp.Schema;

namespace Calcy.Mcp;

/// <summary>The sandboxed Python tool. Offered only when the server is started with code allowed.</summary>
public sealed partial class CalcTools
{
    private const int MaxReturnedOutput = 20_000;

    private McpTool RunCode() => new()
    {
        Name = "run_code",
        Title = "Run Python (sandboxed)",
        Description =
            "Runs a Python 3 program in Calcy's sandboxed interpreter: no files, network or operating system, with time and memory limits. " +
            "Modules: math, cmath, random, time, string, itertools, functools, collections, operator, fractions, and calc (the Calcy engine: " +
            "calc.eval(\"solve(x^2 = 2, x)\"), calc.solve, calc.diff, calc.integrate, calc.get/set for shared variables, calc.graph.add(\"y = sin(x)\")). " +
            "Drawing modules turtle, ti_plotlib, ti_draw, casioplot and calcydraw produce an image. input() reads from the stdin lines given.",
        InputSchema = Object(
            Req("code", String("The program.")),
            Opt("stdin", Array(String("One line."), "Lines that input() returns in order; input() raises EOFError after the last one.")),
            Opt("timeout_seconds", Integer("Time limit (the server's own limit still applies).", 1, 60, 10))),
        Annotations = new ToolAnnotations { ReadOnly = false, Destructive = false, Idempotent = false },
        RunsCode = true,
        Handler = c => Guard(() =>
        {
            var code = c.String("code");
            var lines = new Queue<string>();
            if (c.Node("stdin") is JsonArray stdin)
                foreach (var n in stdin) lines.Enqueue(ItemText(n, "stdin"));
            int timeout = c.Int("timeout_seconds", 10, 1, 60);

            using var host = new ScriptHost(Session, ScriptHostOptions.Default with { Timeout = TimeSpan.FromSeconds(timeout), MaxOutputChars = 200_000 });
            var output = new StringBuilder();
            host.Output += (_, e) =>
            {
                lock (output)
                {
                    if (e.Kind == ScriptOutputKind.Clear) output.Clear();
                    else output.Append(e.Text);
                }
            };
            host.InputProvider = (request, _) =>
            {
                string? line;
                lock (lines) line = lines.Count > 0 ? lines.Dequeue() : null;
                return Task.FromResult(line);
            };
            var result = host.Run(code, "main.py", c.Token);

            string text;
            lock (output) text = output.ToString();
            bool truncated = result.OutputTruncated || text.Length > MaxReturnedOutput;
            if (text.Length > MaxReturnedOutput) text = "...\n" + text[^MaxReturnedOutput..];

            var data = new JsonObject
            {
                ["status"] = result.Status.ToString().ToLowerInvariant(),
                ["success"] = result.Success,
                ["output"] = text,
                ["seconds"] = Math.Round(result.Elapsed.TotalSeconds, 3),
                ["steps"] = result.Steps,
            };
            if (truncated) data["output_truncated"] = true;
            if (result.Error is { } err)
            {
                data["error"] = new JsonObject
                {
                    ["type"] = err.TypeName,
                    ["message"] = err.Message,
                    ["line"] = result.ErrorLine,
                    ["traceback"] = err.Traceback,
                };
            }
            if (result.ExitCode is int exit) data["exit_code"] = exit;

            var images = new List<byte[]>();

            var frame = host.Canvas.Capture();
            if (frame.HasDrawing)
            {
                int scale = Math.Max(1, Math.Min(4, 640 / Math.Max(1, frame.Width)));
                var img = RasterImage.FromArgb(frame.Width, frame.Height, frame.ToArgb(), scale);
                images.Add(img.ToPng());
                data["drawing"] = new JsonObject { ["width"] = frame.Width, ["height"] = frame.Height };
            }
            if (host.Graphs.Items.Count > 0)
            {
                var doc = new GraphDocument(Session);
                foreach (var g in host.Graphs.Items.Take(12)) doc.Add(g.Text);
                var items = doc.Items.Where(i => i.IsPlottable).ToList();
                if (items.Count > 0)
                {
                    const int w = 720, h = 540;
                    var viewport = host.Graphs.Window ?? new Viewport(-10, 10, -10.0 * h / w, 10.0 * h / w);
                    var scene = GraphScene.Build(items, doc.ParameterSource, viewport.Normalized(), w, h, ct: c.Token);
                    images.Add(GraphRaster.RenderPng(scene, GraphPalette.Nes));
                    data["graphs"] = new JsonArray([.. host.Graphs.Items.Select(g => (JsonNode)g.Text)]);
                }
            }
            var toolResult = new ToolResult { Structured = data, IsError = !result.Success, Summary = result.Summary };
            toolResult.AddText(data.ToJsonString(McpJson.Options));
            foreach (var png in images) toolResult.AddImage(png, "image/png");
            return toolResult;
        }),
    };
}
