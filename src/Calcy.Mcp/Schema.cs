using System.Text.Json.Nodes;

namespace Calcy.Mcp;

/// <summary>Small builders for the JSON Schemas that describe tool inputs and outputs.</summary>
public static class Schema
{
    public sealed record Property(string Name, JsonObject Schema, bool Required = false);

    public static Property Req(string name, JsonObject schema) => new(name, schema, true);
    public static Property Opt(string name, JsonObject schema) => new(name, schema);

    public static JsonObject Object(params Property[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var p in properties)
        {
            props[p.Name] = p.Schema;
            if (p.Required) required.Add(p.Name);
        }
        var o = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Count > 0) o["required"] = required;
        return o;
    }

    public static JsonObject String(string description, params string[] options)
    {
        var o = new JsonObject { ["type"] = "string", ["description"] = description };
        if (options.Length > 0)
        {
            var arr = new JsonArray();
            foreach (var x in options) arr.Add(x);
            o["enum"] = arr;
        }
        return o;
    }

    /// <summary>A maths expression: text, or a plain JSON number.</summary>
    public static JsonObject Expression(string description) => new()
    {
        ["type"] = new JsonArray("string", "number"),
        ["description"] = description,
    };

    public static JsonObject Integer(string description, int? min = null, int? max = null, int? fallback = null)
    {
        var o = new JsonObject { ["type"] = "integer", ["description"] = description };
        if (min is not null) o["minimum"] = min;
        if (max is not null) o["maximum"] = max;
        if (fallback is not null) o["default"] = fallback;
        return o;
    }

    public static JsonObject Boolean(string description, bool? fallback = null)
    {
        var o = new JsonObject { ["type"] = "boolean", ["description"] = description };
        if (fallback is not null) o["default"] = fallback;
        return o;
    }

    public static JsonObject Array(JsonObject items, string description) => new()
    {
        ["type"] = "array",
        ["items"] = items,
        ["description"] = description,
    };

    /// <summary>A list of numbers, or a Calcy list expression such as {1, 2, 3} or seq(k^2, k, 1, 10).</summary>
    public static JsonObject Data(string description) => new()
    {
        ["description"] = description,
        ["anyOf"] = new JsonArray(
            new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = new JsonArray("number", "string") } },
            new JsonObject { ["type"] = "string" }),
    };

    /// <summary>A matrix: rows of numbers or expressions, or Calcy text such as [[1, 2], [3, 4]].</summary>
    public static JsonObject Matrix(string description) => new()
    {
        ["description"] = description,
        ["anyOf"] = new JsonArray(
            new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = new JsonArray("number", "string") } },
            },
            new JsonObject { ["type"] = "string" }),
    };

    /// <summary>The output shape shared by the maths tools.</summary>
    public static JsonObject MathResult() => Object(
        Req("result", String("The answer as Calcy shows it (exact when possible, Unicode).")),
        Req("input", String("The answer as plain text that can be typed back into Calcy.")),
        Req("latex", String("The answer in LaTeX.")),
        Opt("decimal", String("Decimal value (15 significant digits) when it differs from the exact answer.")),
        Req("kind", String("What the answer is.", "number", "expression", "equation", "list", "matrix", "boolean", "text", "function", "set")),
        Opt("exact", Boolean("True when the answer is exact (no rounding happened).")),
        Opt("notes", Array(String("A remark."), "Assumptions and warnings, for example 'treated n as an integer'.")));
}
