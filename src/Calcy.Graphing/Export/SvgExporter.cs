using System.Globalization;
using System.Text;

namespace Calcy.Graphing;

/// <summary>Options for SVG export.</summary>
public sealed record SvgOptions
{
    /// <summary>Font family list for tick and point labels.</summary>
    public string LabelFont { get; init; } = "Silkscreen, 'JetBrains Mono', monospace";

    /// <summary>Line width of function graphs.</summary>
    public double CurveWidth { get; init; } = 4;

    /// <summary>Line width of implicit curves and boundaries.</summary>
    public double BoundaryWidth { get; init; } = 3;

    /// <summary>Opacity of hatched inequality shading.</summary>
    public double ShadeOpacity { get; init; } = 0.55;

    /// <summary>Draw the grid and tick labels.</summary>
    public bool Grid { get; init; } = true;

    /// <summary>The default options.</summary>
    public static readonly SvgOptions Default = new();
}

/// <summary>Writes a graph scene as a standalone SVG document (no dependencies; used by export and the MCP server).</summary>
public static class SvgExporter
{
    private static string F(double v) => double.IsFinite(v) ? Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture) : "0";

    /// <summary>Escapes text for XML content and attributes.</summary>
    public static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Samples the items and exports them in one step.</summary>
    public static string Export(IReadOnlyList<GraphItem> items, ParameterSource parameters, Viewport viewport, int width, int height, GraphPalette? palette = null,
        TickStyle tickStyle = TickStyle.Decimal, SvgOptions? options = null) =>
        Export(GraphScene.Build(items, parameters, viewport, width, height, tickStyle), palette ?? GraphPalette.Nes, options);

    /// <summary>Exports a sampled scene.</summary>
    public static string Export(GraphScene scene, GraphPalette palette, SvgOptions? options = null)
    {
        var o = options ?? SvgOptions.Default;
        var t = scene.Transform;
        int w = scene.Width, h = scene.Height;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\">\n");
        sb.Append("<defs>\n");
        var usedInks = scene.Items.Select(i => i.Item.ColorIndex).Concat(scene.Annotations.Select(a => a.ColorIndex)).Distinct().ToList();
        foreach (var k in usedInks)
        {
            string ink = Escape(palette.Ink(k));
            sb.Append(CultureInfo.InvariantCulture, $"<pattern id=\"hatch{k}\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\">");
            sb.Append($"<rect x=\"0\" y=\"6\" width=\"2\" height=\"2\" fill=\"{ink}\"/><rect x=\"2\" y=\"4\" width=\"2\" height=\"2\" fill=\"{ink}\"/>");
            sb.Append($"<rect x=\"4\" y=\"2\" width=\"2\" height=\"2\" fill=\"{ink}\"/><rect x=\"6\" y=\"0\" width=\"2\" height=\"2\" fill=\"{ink}\"/></pattern>\n");
        }
        sb.Append(CultureInfo.InvariantCulture, $"<clipPath id=\"plot\"><rect x=\"0\" y=\"0\" width=\"{w}\" height=\"{h}\"/></clipPath>\n</defs>\n");
        sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"0\" y=\"0\" width=\"{w}\" height=\"{h}\" fill=\"{Escape(palette.Background)}\"/>\n");

        double ax = t.ToPixelX(0), ay = t.ToPixelY(0);
        if (o.Grid)
        {
            sb.Append($"<g stroke=\"{Escape(palette.Grid)}\" stroke-width=\"1\" shape-rendering=\"crispEdges\">\n");
            foreach (var tick in scene.XTicks)
            {
                double x = Math.Round(t.ToPixelX(tick.Value)) + 0.5;
                sb.Append($"<line x1=\"{F(x)}\" y1=\"0\" x2=\"{F(x)}\" y2=\"{h}\"/>\n");
            }
            foreach (var tick in scene.YTicks)
            {
                double y = Math.Round(t.ToPixelY(tick.Value)) + 0.5;
                sb.Append($"<line x1=\"0\" y1=\"{F(y)}\" x2=\"{w}\" y2=\"{F(y)}\"/>\n");
            }
            sb.Append("</g>\n");
        }
        sb.Append($"<g stroke=\"{Escape(palette.Axis)}\" stroke-width=\"2\" shape-rendering=\"crispEdges\">\n");
        if (ay >= 0 && ay <= h) sb.Append($"<line x1=\"0\" y1=\"{F(Math.Round(ay))}\" x2=\"{w}\" y2=\"{F(Math.Round(ay))}\"/>\n");
        if (ax >= 0 && ax <= w) sb.Append($"<line x1=\"{F(Math.Round(ax))}\" y1=\"0\" x2=\"{F(Math.Round(ax))}\" y2=\"{h}\"/>\n");
        sb.Append("</g>\n");
        if (o.Grid)
        {
            string font = Escape(o.LabelFont);
            double labelY = Math.Clamp(ay + 17, 14, h - 4);
            foreach (var tick in scene.XTicks)
            {
                if (tick.Value == 0) continue;
                sb.Append($"<text x=\"{F(t.ToPixelX(tick.Value))}\" y=\"{F(labelY)}\" font-family=\"{font}\" font-size=\"12\" text-anchor=\"middle\" fill=\"{Escape(palette.Label)}\">{Escape(tick.Label)}</text>\n");
            }
            double labelX = Math.Clamp(ax - 8, 30, w - 4);
            foreach (var tick in scene.YTicks)
            {
                if (tick.Value == 0) continue;
                sb.Append($"<text x=\"{F(labelX)}\" y=\"{F(t.ToPixelY(tick.Value) + 4)}\" font-family=\"{font}\" font-size=\"12\" text-anchor=\"end\" fill=\"{Escape(palette.Label)}\">{Escape(tick.Label)}</text>\n");
            }
        }

        sb.Append("<g clip-path=\"url(#plot)\">\n");
        double m = 4 * Math.Max(w, h);
        foreach (var geo in scene.Items)
        {
            string ink = Escape(palette.Ink(geo.Item.ColorIndex));
            int k = geo.Item.ColorIndex;
            if (geo.FillCells.Count > 0)
            {
                var path = new StringBuilder();
                foreach (var c in geo.FillCells)
                {
                    double x0 = t.ToPixelX(c.X0), x1 = t.ToPixelX(c.X1), y0 = t.ToPixelY(c.Y1), y1 = t.ToPixelY(c.Y0);
                    if (x1 < 0 || x0 > w || y1 < 0 || y0 > h) continue;
                    path.Append($"M{F(x0)} {F(y0)}H{F(x1)}V{F(y1)}H{F(x0)}Z");
                }
                if (path.Length > 0)
                    sb.Append(CultureInfo.InvariantCulture, $"<path d=\"{path}\" fill=\"url(#hatch{k})\" fill-opacity=\"{F(o.ShadeOpacity)}\"/>\n");
            }
            foreach (var poly in geo.Fills)
            {
                var pts = string.Join(" ", poly.Points.Select(q => $"{F(Math.Clamp(t.ToPixelX(q.X), -m, m))},{F(Math.Clamp(t.ToPixelY(q.Y), -m, m))}"));
                sb.Append(CultureInfo.InvariantCulture, $"<polygon points=\"{pts}\" fill=\"url(#hatch{k})\" fill-opacity=\"{F(o.ShadeOpacity)}\"/>\n");
            }
            double width = geo.Item.Kind is GraphKind.Function or GraphKind.FunctionOfY or GraphKind.Polar or GraphKind.Parametric ? o.CurveWidth : o.BoundaryWidth;
            foreach (var line in geo.Curves)
            {
                var px = line.Points.Select(q => (t.ToPixelX(q.X), t.ToPixelY(q.Y))).ToList();
                foreach (var run in PolylineClipper.Clip(px, -20, -20, w + 20, h + 20, line.Closed))
                {
                    var pts = string.Join(" ", run.Select(q => $"{F(q.X)},{F(q.Y)}"));
                    string dash = line.Dashed ? " stroke-dasharray=\"10 6\"" : "";
                    sb.Append(CultureInfo.InvariantCulture, $"<polyline points=\"{pts}\" fill=\"none\" stroke=\"{ink}\" stroke-width=\"{F(width)}\" stroke-linejoin=\"round\" stroke-linecap=\"round\"{dash}/>\n");
                }
            }
            foreach (var s in geo.Segments)
            {
                sb.Append($"<line x1=\"{F(t.ToPixelX(s.A.X))}\" y1=\"{F(t.ToPixelY(s.A.Y))}\" x2=\"{F(t.ToPixelX(s.B.X))}\" y2=\"{F(t.ToPixelY(s.B.Y))}\" stroke=\"{ink}\" stroke-width=\"2\" stroke-linecap=\"square\"/>\n");
            }
            foreach (var pt in geo.Points)
            {
                double x = t.ToPixelX(pt.Position.X), y = t.ToPixelY(pt.Position.Y);
                if (x < -20 || x > w + 20 || y < -20 || y > h + 20) continue;
                sb.Append($"<rect x=\"{F(x - 6)}\" y=\"{F(y - 6)}\" width=\"12\" height=\"12\" fill=\"{ink}\" stroke=\"{Escape(palette.Line)}\" stroke-width=\"2\"/>\n");
                if (pt.Label is not null)
                    sb.Append($"<text x=\"{F(x + 12)}\" y=\"{F(y - 10)}\" font-family=\"{Escape(o.LabelFont)}\" font-size=\"13\" fill=\"{Escape(palette.Axis)}\">{Escape(pt.Label)}</text>\n");
            }
        }
        foreach (var a in scene.Annotations)
        {
            string ink = Escape(palette.Ink(a.ColorIndex));
            var px = a.Points.Select(q => (t.ToPixelX(q.X), t.ToPixelY(q.Y))).ToList();
            switch (a.Kind)
            {
                case AnnotationKind.Shade:
                    sb.Append($"<polygon points=\"{string.Join(" ", px.Select(q => $"{F(Math.Clamp(q.Item1, -m, m))},{F(Math.Clamp(q.Item2, -m, m))}"))}\" fill=\"{ink}\" fill-opacity=\"0.3\"/>\n");
                    break;
                case AnnotationKind.Line when px.Count >= 2:
                {
                    double dx = px[1].Item1 - px[0].Item1, dy = px[1].Item2 - px[0].Item2, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len == 0) break;
                    double s = 4 * (w + h) / len;
                    sb.Append($"<line x1=\"{F(px[0].Item1 - dx * s)}\" y1=\"{F(px[0].Item2 - dy * s)}\" x2=\"{F(px[0].Item1 + dx * s)}\" y2=\"{F(px[0].Item2 + dy * s)}\" stroke=\"{ink}\" stroke-width=\"2\" stroke-dasharray=\"8 5\"/>\n");
                    break;
                }
                case AnnotationKind.Path:
                    foreach (var run in PolylineClipper.Clip(px, -20, -20, w + 20, h + 20))
                        sb.Append($"<polyline points=\"{string.Join(" ", run.Select(q => $"{F(q.X)},{F(q.Y)}"))}\" fill=\"none\" stroke=\"{ink}\" stroke-width=\"3\" stroke-dasharray=\"6 4\"/>\n");
                    break;
                case AnnotationKind.Point:
                    foreach (var q in px)
                        sb.Append($"<rect x=\"{F(q.Item1 - 5)}\" y=\"{F(q.Item2 - 5)}\" width=\"10\" height=\"10\" fill=\"{Escape(palette.Selection)}\" stroke=\"{Escape(palette.Line)}\" stroke-width=\"2\"/>\n");
                    break;
            }
        }
        foreach (var poi in scene.PointsOfInterest)
        {
            double x = t.ToPixelX(poi.X), y = t.ToPixelY(poi.Y);
            if (x < 0 || x > w || y < 0 || y > h) continue;
            sb.Append($"<rect x=\"{F(x - 5)}\" y=\"{F(y - 5)}\" width=\"10\" height=\"10\" fill=\"{Escape(palette.Marker)}\" stroke=\"{Escape(palette.Line)}\" stroke-width=\"2\"><title>{Escape(poi.Title + ": " + poi.Text)}</title></rect>\n");
        }
        sb.Append("</g>\n</svg>\n");
        return sb.ToString();
    }
}
