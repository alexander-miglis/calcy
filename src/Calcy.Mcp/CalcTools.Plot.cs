using System.Text.Json.Nodes;
using Calcy.Graphing;
using static Calcy.Mcp.Schema;

namespace Calcy.Mcp;

/// <summary>The graphing tool: plots return a PNG image, the SVG and the key points.</summary>
public sealed partial class CalcTools
{
    private int _plotCounter;

    private McpTool Plot2D() => new()
    {
        Name = "plot_2d",
        Title = "Plot a graph",
        Description =
            "Draws graphs and returns a PNG image, the SVG drawing and the points of interest (roots, extrema, intercepts, intersections). " +
            "Each entry may be a function (y = x^2 - 2 or just sin(x)), x = g(y), polar (r = 1 + cos(θ)), parametric ((cos(t), sin(t))), any relation " +
            "(x^2 + y^2 = 9), an inequality (y < x/2, x^2 + y^2 <= 4), points ((2, 3) or {(1, 2), (3, 4)}), a slope field (dy/dx = x - y), " +
            "a restriction (x^2 {0 < x < 3}) or a family (y = {1, 2, 3}·x). Free names such as a become sliders; give their values in parameters.",
        InputSchema = Object(
            Req("expressions", Array(String("One graph."), "The graphs to draw, in order (colours follow the order).")),
            Opt("x_min", Expression("Left edge (default -10).")),
            Opt("x_max", Expression("Right edge (default 10).")),
            Opt("y_min", Expression("Bottom edge (default: equal scale on both axes, centred on 0).")),
            Opt("y_max", Expression("Top edge.")),
            Opt("fit_y", Boolean("Choose the y range so the graphs fill the picture (circles may look stretched).", false)),
            Opt("width", Integer("Image width in pixels.", 200, 2000, 720)),
            Opt("height", Integer("Image height in pixels.", 150, 2000, 540)),
            Opt("parameters", new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Values for slider names, for example {\"a\": 2}. Unset names are 1.",
                ["additionalProperties"] = new JsonObject { ["type"] = "number" },
            }),
            Opt("points_of_interest", Boolean("Mark and list roots, extrema and intersections.", true)),
            Opt("pi_ticks", Boolean("Label the x axis in multiples of π.", false))),
        Handler = c => Guard(() =>
        {
            if (c.Node("expressions") is not JsonArray list || list.Count == 0) throw new ToolArgumentException("Give at least one expression to plot.");
            if (list.Count > 12) throw new ToolArgumentException("At most 12 graphs at once.");
            int width = c.Int("width", 720, 200, 2000), height = c.Int("height", 540, 150, 2000);
            var doc = new GraphDocument(Session);
            var rows = new JsonArray();
            for (int i = 0; i < list.Count; i++)
            {
                var item = doc.Add(ItemText(list[i], "expressions"));
                var row = new JsonObject { ["expression"] = item.Text, ["kind"] = item.KindLabel };
                if (item.Error is not null) row["error"] = item.Error;
                if (item.Parameters.Count > 0) row["sliders"] = new JsonArray([.. item.Parameters.Select(p => (JsonNode)p)]);
                rows.Add(row);
            }
            if (c.Node("parameters") is JsonObject ps)
            {
                foreach (var (name, node) in ps)
                {
                    var p = doc.Parameters[name] ?? throw new ToolArgumentException($"'{name}' is not a slider in these graphs.");
                    double v = node is JsonValue jv && jv.GetValueKind() == System.Text.Json.JsonValueKind.Number
                        && double.TryParse(jv.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
                        ? d
                        : throw new ToolArgumentException($"parameters.{name} must be a number.");
                    if (v < p.Min) p.Min = v;
                    if (v > p.Max) p.Max = v;
                    p.Value = v;
                }
            }
            var items = doc.Items.Where(i => i.IsPlottable).ToList();
            if (items.Count == 0)
                return ToolResult.Error("Nothing can be drawn: " + string.Join("; ", doc.Items.Where(i => i.Error is not null).Select(i => $"{i.Text}: {i.Error}")));

            double x0 = c.Has("x_min") ? NumberOf(Parse(c, "x_min"), c) : -10;
            double x1 = c.Has("x_max") ? NumberOf(Parse(c, "x_max"), c) : 10;
            if (!(x1 > x0)) throw new ToolArgumentException("x_max must be larger than x_min.");
            var viewport = new Viewport(x0, x1, -10, 10);
            if (c.Has("y_min") || c.Has("y_max"))
            {
                double y0 = c.Has("y_min") ? NumberOf(Parse(c, "y_min"), c) : -10;
                double y1 = c.Has("y_max") ? NumberOf(Parse(c, "y_max"), c) : 10;
                if (!(y1 > y0)) throw new ToolArgumentException("y_max must be larger than y_min.");
                viewport = new Viewport(x0, x1, y0, y1);
            }
            else if (c.Bool("fit_y", false)) viewport = GraphFit.FitY(items, doc.ParameterSource, viewport);
            else
            {
                // Equal scale on both axes, like graph paper: circles stay round.
                double half = (x1 - x0) / 2 * height / width;
                viewport = new Viewport(x0, x1, -half, half);
            }
            viewport = viewport.Normalized();

            var poi = c.Bool("points_of_interest", true)
                ? PointsOfInterestFinder.Find(items, doc.ParameterSource, viewport, Session)
                : [];
            var tickStyle = c.Bool("pi_ticks", false) ? TickStyle.Pi : TickStyle.Decimal;
            var scene = GraphScene.Build(items, doc.ParameterSource, viewport, width, height, tickStyle, points: poi, ct: c.Token);
            var palette = GraphPalette.Nes;
            var png = GraphRaster.RenderPng(scene, palette);
            var svg = SvgExporter.Export(scene, palette);

            var points = new JsonArray();
            foreach (var p in poi.Take(60))
            {
                var o = new JsonObject
                {
                    ["kind"] = p.Kind.ToString().ToLowerInvariant(),
                    ["graph"] = p.Item.Text,
                    ["x"] = Math.Round(p.X, 12),
                    ["y"] = Math.Round(p.Y, 12),
                    ["text"] = p.Text,
                };
                if (p.Other is not null) o["with"] = p.Other.Text;
                points.Add(o);
            }
            var data = new JsonObject
            {
                ["window"] = new JsonObject { ["x_min"] = viewport.XMin, ["x_max"] = viewport.XMax, ["y_min"] = viewport.YMin, ["y_max"] = viewport.YMax },
                ["graphs"] = rows,
                ["points_of_interest"] = points,
            };
            if (scene.Items.Any(i => i.MayBeIncomplete)) data["note"] = "Some graphs may be incomplete (very fine detail); zoom in for more.";
            if (doc.Parameters.All.Count > 0)
                data["sliders"] = new JsonObject([.. doc.Parameters.All.Select(p => new KeyValuePair<string, JsonNode?>(p.Name, p.Value))]);

            int n = Interlocked.Increment(ref _plotCounter);
            var result = new ToolResult { Structured = data, Summary = $"plot of {items.Count} graph{(items.Count == 1 ? "" : "s")}" };
            result.AddImage(png, "image/png");
            result.AddText(data.ToJsonString(McpJson.Options));
            result.AddResource($"calcy://plots/{n}.svg", "image/svg+xml", svg);
            return result;
        }),
    };
}
