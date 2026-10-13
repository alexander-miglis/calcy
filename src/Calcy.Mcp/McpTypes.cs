using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Calcy.Mcp;

/// <summary>Hints that let MCP clients decide which tools are safe to approve automatically.</summary>
public sealed record ToolAnnotations
{
    /// <summary>The tool does not change Calcy's state (variables, settings).</summary>
    public bool ReadOnly { get; init; } = true;

    /// <summary>The tool may overwrite or remove something the user made.</summary>
    public bool Destructive { get; init; }

    /// <summary>Calling the tool twice with the same arguments has the same effect as calling it once.</summary>
    public bool Idempotent { get; init; } = true;

    /// <summary>The tool talks to something outside Calcy (never true for the built-in tools).</summary>
    public bool OpenWorld { get; init; }
}

/// <summary>A tool offered over MCP.</summary>
public sealed class McpTool
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required JsonObject InputSchema { get; init; }
    public JsonObject? OutputSchema { get; init; }
    public ToolAnnotations Annotations { get; init; } = new();

    /// <summary>Runs the tool. Runs on a worker thread; the server runs one tool at a time.</summary>
    public required Func<ToolCall, ToolResult> Handler { get; init; }

    /// <summary>Whether the tool can be offered right now (for example whether the functions it needs are in this build).</summary>
    public Func<bool>? IsAvailable { get; init; }

    /// <summary>True for tools that run user code; they are only offered when code execution is enabled.</summary>
    public bool RunsCode { get; init; }

    internal JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["name"] = Name,
            ["title"] = Title,
            ["description"] = Description,
            ["inputSchema"] = InputSchema.DeepClone(),
        };
        if (OutputSchema is not null) o["outputSchema"] = OutputSchema.DeepClone();
        o["annotations"] = new JsonObject
        {
            ["title"] = Title,
            ["readOnlyHint"] = Annotations.ReadOnly,
            ["destructiveHint"] = Annotations.Destructive,
            ["idempotentHint"] = Annotations.Idempotent,
            ["openWorldHint"] = Annotations.OpenWorld,
        };
        return o;
    }
}

/// <summary>Thrown by tool handlers for bad arguments; reported to the client as a tool error it can correct.</summary>
public sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>A protocol-level error with a JSON-RPC error code.</summary>
public sealed class McpProtocolException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;

    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ResourceNotFound = -32002;
}

/// <summary>The arguments of one tools/call request, with typed accessors that produce helpful errors.</summary>
public sealed class ToolCall(JsonObject arguments, CancellationToken token)
{
    public JsonObject Arguments { get; } = arguments;
    public CancellationToken Token { get; } = token;

    public bool Has(string name) => Arguments.TryGetPropertyValue(name, out var v) && v is not null;

    public JsonNode? Node(string name) => Arguments.TryGetPropertyValue(name, out var v) ? v : null;

    /// <summary>A required text argument. Numbers are accepted and keep their exact digits.</summary>
    public string String(string name)
    {
        var s = OptionalString(name);
        if (string.IsNullOrWhiteSpace(s)) throw new ToolArgumentException($"Missing argument '{name}'.");
        return s;
    }

    public string? OptionalString(string name)
    {
        var n = Node(name);
        if (n is null) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.GetValueKind() == JsonValueKind.Number) return v.ToJsonString();
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        throw new ToolArgumentException($"Argument '{name}' must be text.");
    }

    public bool Bool(string name, bool fallback)
    {
        var n = Node(name);
        if (n is null) return fallback;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out b)) return b;
        }
        throw new ToolArgumentException($"Argument '{name}' must be true or false.");
    }

    public int Int(string name, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        var n = Node(name);
        if (n is null) return fallback;
        int value;
        if (n is JsonValue v && v.TryGetValue<int>(out var i)) value = i;
        else if (n is JsonValue v2 && v2.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue) value = (int)d;
        else if (n is JsonValue v3 && v3.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) value = p;
        else throw new ToolArgumentException($"Argument '{name}' must be a whole number.");
        if (value < min || value > max) throw new ToolArgumentException($"Argument '{name}' must be between {min} and {max}.");
        return value;
    }

    /// <summary>A required argument that must be one of the given words (case-insensitive).</summary>
    public string Choice(string name, string fallback, params string[] options)
    {
        var s = OptionalString(name) ?? fallback;
        foreach (var o in options)
            if (string.Equals(o, s, StringComparison.OrdinalIgnoreCase)) return o;
        throw new ToolArgumentException($"Argument '{name}' must be one of: {string.Join(", ", options)}.");
    }
}

/// <summary>The result of a tool call: content blocks for the model plus optional structured data.</summary>
public sealed class ToolResult
{
    public List<JsonObject> Content { get; } = [];
    public JsonObject? Structured { get; init; }
    public bool IsError { get; init; }

    /// <summary>One line used in the audit log.</summary>
    public string Summary { get; init; } = "";

    public static ToolResult Text(string text) => new ToolResult { Summary = Shorten(text) }.AddText(text);

    public static ToolResult Error(string message) => new ToolResult { IsError = true, Summary = Shorten(message) }.AddText(message);

    /// <summary>Structured data, also serialized as a text block for clients that do not read structured content.</summary>
    public static ToolResult Data(JsonObject data, string? summary = null)
    {
        var r = new ToolResult { Structured = data, Summary = Shorten(summary ?? data.ToJsonString()) };
        r.AddText(data.ToJsonString(McpJson.Options));
        return r;
    }

    public ToolResult AddText(string text)
    {
        Content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        return this;
    }

    public ToolResult AddImage(byte[] data, string mimeType)
    {
        Content.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(data), ["mimeType"] = mimeType });
        return this;
    }

    /// <summary>An embedded text resource (for example an SVG drawing).</summary>
    public ToolResult AddResource(string uri, string mimeType, string text)
    {
        Content.Add(new JsonObject
        {
            ["type"] = "resource",
            ["resource"] = new JsonObject { ["uri"] = uri, ["mimeType"] = mimeType, ["text"] = text },
        });
        return this;
    }

    internal JsonObject ToJson()
    {
        var content = new JsonArray();
        foreach (var c in Content) content.Add(c.DeepClone());
        var o = new JsonObject { ["content"] = content };
        if (Structured is not null) o["structuredContent"] = Structured.DeepClone();
        if (IsError) o["isError"] = true;
        return o;
    }

    private static string Shorten(string s) => s.Length <= 200 ? s : s[..197] + "...";
}

/// <summary>A fixed resource (calcy://...).</summary>
public sealed class McpResource
{
    public required string Uri { get; init; }
    public required string Name { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string MimeType { get; init; } = "text/plain";
    public required Func<string> Read { get; init; }
}

/// <summary>A family of resources addressed by a URI template such as calcy://reference/functions/{name}.</summary>
public sealed class McpResourceTemplate
{
    public required string UriTemplate { get; init; }
    public required string Name { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string MimeType { get; init; } = "text/plain";

    /// <summary>Reads the resource for a concrete URI, or returns null when the URI does not belong to this template.</summary>
    public required Func<string, string?> TryRead { get; init; }
}

public sealed record PromptArgument(string Name, string Description, bool Required);

/// <summary>A prompt template offered to the client.</summary>
public sealed class McpPrompt
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<PromptArgument> Arguments { get; init; } = [];

    /// <summary>Produces the user message from the arguments.</summary>
    public required Func<IReadOnlyDictionary<string, string>, string> Render { get; init; }
}

/// <summary>One line of the audit log.</summary>
public sealed record AuditEntry(DateTimeOffset Time, string? Client, string Tool, string Arguments, bool IsError, string Summary, TimeSpan Duration);

internal static class McpJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}
