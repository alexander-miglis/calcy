using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Calcy.Mcp;

/// <summary>Who may do what over MCP, and how long a tool may run.</summary>
public sealed class McpServerOptions
{
    public string Name { get; init; } = "calcy";
    public string Title { get; init; } = "Calcy";
    public string Version { get; init; } =
        (typeof(McpServer).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>Shown to the client after initialize; tells a model how to use the tools well.</summary>
    public string? Instructions { get; init; }

    /// <summary>Refuse every tool that changes state (variables, settings).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Offer tools that run code (sandboxed). Off unless the user turns it on.</summary>
    public bool AllowCode { get; init; }

    /// <summary>Tools that are never offered.</summary>
    public IReadOnlySet<string> DisabledTools { get; init; } = new HashSet<string>();

    /// <summary>Maximum run time of one tool call.</summary>
    public TimeSpan ToolTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Receives one entry per tool call.</summary>
    public Action<AuditEntry>? Audit { get; init; }

    /// <summary>Diagnostics (never stdout in stdio mode).</summary>
    public TextWriter? Log { get; init; }
}

/// <summary>
/// A Model Context Protocol server (JSON-RPC 2.0). Transport-independent: <see cref="HandleAsync"/> takes one message,
/// <see cref="RunAsync"/> runs the newline-delimited stdio transport. Tool calls run one at a time on a worker thread
/// with a large stack, a timeout and cancellation (notifications/cancelled).
/// </summary>
public sealed class McpServer
{
    /// <summary>Protocol revisions this server speaks, newest first.</summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    private readonly McpServerOptions _options;
    private readonly List<McpTool> _tools;
    private readonly List<McpResource> _resources;
    private readonly List<McpResourceTemplate> _templates;
    private readonly List<McpPrompt> _prompts;
    private readonly SemaphoreSlim _toolGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new();

    public McpServer(McpServerOptions options, IEnumerable<McpTool> tools, IEnumerable<McpResource>? resources = null,
        IEnumerable<McpResourceTemplate>? templates = null, IEnumerable<McpPrompt>? prompts = null)
    {
        _options = options;
        _tools = [.. tools];
        _resources = [.. resources ?? []];
        _templates = [.. templates ?? []];
        _prompts = [.. prompts ?? []];
        var dup = _tools.GroupBy(t => t.Name).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new ArgumentException($"Two tools are named '{dup.Key}'.");
    }

    /// <summary>The client's name from initialize, for the audit log.</summary>
    public string? ClientName { get; private set; }

    /// <summary>The negotiated protocol revision, once initialized.</summary>
    public string? ProtocolVersion { get; private set; }

    /// <summary>The tools a client can see right now.</summary>
    public IEnumerable<McpTool> VisibleTools => _tools.Where(IsOffered);

    private bool IsOffered(McpTool t) =>
        !_options.DisabledTools.Contains(t.Name) &&
        (!t.RunsCode || _options.AllowCode) &&
        (t.IsAvailable?.Invoke() ?? true);

    /// <summary>
    /// Runs the stdio transport: one JSON-RPC message per line in, one per line out, until the input ends.
    /// Responses to concurrent requests may be written in any order.
    /// </summary>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        var writeLock = new SemaphoreSlim(1, 1);
        var pending = new ConcurrentDictionary<Task, byte>();
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await input.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var task = Task.Run(async () =>
            {
                string? response = await HandleLineAsync(line, ct).ConfigureAwait(false);
                if (response is null) return;
                await writeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await output.WriteLineAsync(response).ConfigureAwait(false);
                    await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    writeLock.Release();
                }
            }, CancellationToken.None);
            pending[task] = 0;
            _ = task.ContinueWith(t => pending.TryRemove(t, out _), TaskScheduler.Default);
        }
        await Task.WhenAll(pending.Keys).ConfigureAwait(false);
    }

    /// <summary>Handles one line of JSON text and returns the response line, or null when there is nothing to send.</summary>
    public async Task<string?> HandleLineAsync(string line, CancellationToken ct = default)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException ex)
        {
            return ErrorResponse(null, McpProtocolException.ParseError, "Parse error: " + ex.Message).ToJsonString(McpJson.Options);
        }
        var response = await HandleAsync(message, ct).ConfigureAwait(false);
        return response?.ToJsonString(McpJson.Options);
    }

    /// <summary>Handles one JSON-RPC message (or a batch) and returns the response, or null for notifications.</summary>
    public async Task<JsonNode?> HandleAsync(JsonNode? message, CancellationToken ct = default)
    {
        if (message is JsonArray batch)
        {
            if (batch.Count == 0) return ErrorResponse(null, McpProtocolException.InvalidRequest, "Empty batch");
            var results = new JsonArray();
            foreach (var item in batch.ToList())
            {
                var r = await HandleSingleAsync(item, ct).ConfigureAwait(false);
                if (r is not null) results.Add(r);
            }
            return results.Count > 0 ? results : null;
        }
        return await HandleSingleAsync(message, ct).ConfigureAwait(false);
    }

    private async Task<JsonNode?> HandleSingleAsync(JsonNode? node, CancellationToken ct)
    {
        if (node is not JsonObject msg)
            return ErrorResponse(null, McpProtocolException.InvalidRequest, "A message must be a JSON object");
        bool hasId = msg.TryGetPropertyValue("id", out var idNode) && idNode is not null;
        var id = idNode?.DeepClone();
        string? method = msg["method"] is JsonValue mv && mv.TryGetValue<string>(out var m) ? m : null;
        if (method is null)
        {
            // A response to a request we never send, or garbage. Nothing to answer unless it has an id.
            if (msg.ContainsKey("result") || msg.ContainsKey("error")) return null;
            return ErrorResponse(id, McpProtocolException.InvalidRequest, "Missing method");
        }
        var p = msg["params"];
        if (p is not null and not JsonObject)
            return hasId ? ErrorResponse(id, McpProtocolException.InvalidParams, "params must be an object") : null;
        var parameters = p as JsonObject;

        if (!hasId)
        {
            HandleNotification(method, parameters);
            return null;
        }
        try
        {
            JsonNode? result = method switch
            {
                "initialize" => Initialize(parameters),
                "ping" => new JsonObject(),
                "tools/list" => ListTools(),
                "tools/call" => await CallToolAsync(parameters, id!, ct).ConfigureAwait(false),
                "resources/list" => ListResources(),
                "resources/templates/list" => ListTemplates(),
                "resources/read" => ReadResource(parameters),
                "prompts/list" => ListPrompts(),
                "prompts/get" => GetPrompt(parameters),
                "logging/setLevel" => new JsonObject(),
                _ => throw new McpProtocolException(McpProtocolException.MethodNotFound, $"Method not found: {method}"),
            };
            // A cancelled request gets no response.
            if (result is null) return null;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }
        catch (McpProtocolException ex)
        {
            return ErrorResponse(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _options.Log?.WriteLine($"calcy mcp: internal error in {method}: {ex}");
            return ErrorResponse(id, McpProtocolException.InternalError, "Internal error: " + ex.Message);
        }
    }

    private void HandleNotification(string method, JsonObject? p)
    {
        switch (method)
        {
            case "notifications/cancelled":
                var rid = p?["requestId"];
                if (rid is not null && _inflight.TryGetValue(Key(rid), out var cts))
                {
                    _cancelledByClient[Key(rid)] = 0;
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                break;
            case "notifications/initialized":
                break;
        }
    }

    private static JsonObject ErrorResponse(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static string Key(JsonNode id) => id.ToJsonString();

    private JsonObject Initialize(JsonObject? p)
    {
        string? requested = p?["protocolVersion"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        ProtocolVersion = requested is not null && SupportedProtocolVersions.Contains(requested) ? requested : SupportedProtocolVersions[0];
        if (p?["clientInfo"] is JsonObject ci && ci["name"] is JsonValue n && n.TryGetValue<string>(out var name))
            ClientName = name;
        var result = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
                ["resources"] = new JsonObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = _options.Name,
                ["title"] = _options.Title,
                ["version"] = _options.Version,
            },
        };
        if (!string.IsNullOrEmpty(_options.Instructions)) result["instructions"] = _options.Instructions;
        return result;
    }

    private JsonObject ListTools()
    {
        var arr = new JsonArray();
        foreach (var t in VisibleTools) arr.Add(t.ToJson());
        return new JsonObject { ["tools"] = arr };
    }

    private async Task<JsonNode?> CallToolAsync(JsonObject? p, JsonNode id, CancellationToken ct)
    {
        string name = p?["name"] is JsonValue nv && nv.TryGetValue<string>(out var n) ? n
            : throw new McpProtocolException(McpProtocolException.InvalidParams, "tools/call needs a tool name");
        var tool = _tools.FirstOrDefault(t => t.Name == name && IsOffered(t))
                   ?? throw new McpProtocolException(McpProtocolException.InvalidParams, $"Unknown tool: {name}");
        var argsNode = p!["arguments"];
        if (argsNode is not null and not JsonObject)
            throw new McpProtocolException(McpProtocolException.InvalidParams, "arguments must be an object");
        var args = (JsonObject?)argsNode?.DeepClone() ?? new JsonObject();
        var started = Stopwatch.StartNew();

        if (_options.ReadOnly && !tool.Annotations.ReadOnly)
        {
            var refused = ToolResult.Error($"Calcy is running in read-only mode, so '{name}' is not allowed. Ask the user to restart the server without --read-only.");
            AuditCall(name, args, refused, started.Elapsed);
            return refused.ToJson();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var key = Key(id);
        _inflight[key] = cts;
        bool gateTaken = false;
        Task<ToolResult>? work = null;
        try
        {
            try
            {
                gateTaken = await _toolGate.WaitAsync(_options.ToolTimeout, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            ToolResult result;
            if (!gateTaken)
            {
                result = ToolResult.Error("Calcy is still busy with an earlier calculation that did not stop. Try again in a moment.");
                AuditCall(name, args, result, started.Elapsed);
                return result.ToJson();
            }
            cts.CancelAfter(_options.ToolTimeout);
            work = RunOnWorker(tool, new ToolCall(args, cts.Token));
            try
            {
                result = await work.WaitAsync(_options.ToolTimeout + TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The tool ignored its cancellation token.
                result = TimedOut();
            }
            catch (OperationCanceledException)
            {
                // The client cancelled (no response is sent), or our own timeout fired inside the tool.
                if (ct.IsCancellationRequested || _cancelledByClient.ContainsKey(key)) return null;
                result = TimedOut();
            }
            AuditCall(name, args, result, started.Elapsed);
            return result.ToJson();
        }
        finally
        {
            _inflight.TryRemove(key, out _);
            _cancelledByClient.TryRemove(key, out _);
            if (gateTaken)
            {
                // Keep the gate until the worker really stops, so a runaway tool never runs next to another one.
                if (work is null || work.IsCompleted) _toolGate.Release();
                else _ = work.ContinueWith(_ => _toolGate.Release(), TaskScheduler.Default);
            }
        }
    }

    private readonly ConcurrentDictionary<string, byte> _cancelledByClient = new();

    private ToolResult TimedOut() =>
        ToolResult.Error($"The calculation took longer than {_options.ToolTimeout.TotalSeconds:0} seconds and was stopped. Try a simpler input, a numeric method, or a smaller range.");

    /// <summary>Runs a handler on a dedicated thread with a 64 MB stack (deep symbolic recursion is fine there).</summary>
    private static Task<ToolResult> RunOnWorker(McpTool tool, ToolCall call)
    {
        var tcs = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(tool.Handler(call));
            }
            catch (ToolArgumentException ex)
            {
                tcs.TrySetResult(ToolResult.Error(ex.Message));
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetException(ex);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(ToolResult.Error($"Calcy could not finish '{tool.Name}': {ex.Message}"));
            }
        }, 64 * 1024 * 1024)
        {
            IsBackground = true,
            Name = "calcy-mcp-tool",
        };
        thread.Start();
        return tcs.Task;
    }

    private void AuditCall(string tool, JsonObject args, ToolResult result, TimeSpan duration)
    {
        if (_options.Audit is null) return;
        try
        {
            _options.Audit(new AuditEntry(DateTimeOffset.Now, ClientName, tool, args.ToJsonString(McpJson.Options), result.IsError, result.Summary, duration));
        }
        catch (Exception ex)
        {
            _options.Log?.WriteLine("calcy mcp: audit log failed: " + ex.Message);
        }
    }

    private JsonObject ListResources()
    {
        var arr = new JsonArray();
        foreach (var r in _resources)
        {
            var o = new JsonObject { ["uri"] = r.Uri, ["name"] = r.Name, ["mimeType"] = r.MimeType };
            if (r.Title is not null) o["title"] = r.Title;
            if (r.Description is not null) o["description"] = r.Description;
            arr.Add(o);
        }
        return new JsonObject { ["resources"] = arr };
    }

    private JsonObject ListTemplates()
    {
        var arr = new JsonArray();
        foreach (var t in _templates)
        {
            var o = new JsonObject { ["uriTemplate"] = t.UriTemplate, ["name"] = t.Name, ["mimeType"] = t.MimeType };
            if (t.Title is not null) o["title"] = t.Title;
            if (t.Description is not null) o["description"] = t.Description;
            arr.Add(o);
        }
        return new JsonObject { ["resourceTemplates"] = arr };
    }

    private JsonObject ReadResource(JsonObject? p)
    {
        string uri = p?["uri"] is JsonValue v && v.TryGetValue<string>(out var s) ? s
            : throw new McpProtocolException(McpProtocolException.InvalidParams, "resources/read needs a uri");
        string? text = null, mime = null;
        var fixedResource = _resources.FirstOrDefault(r => r.Uri == uri);
        if (fixedResource is not null)
        {
            text = fixedResource.Read();
            mime = fixedResource.MimeType;
        }
        else
        {
            foreach (var t in _templates)
            {
                text = t.TryRead(uri);
                if (text is not null)
                {
                    mime = t.MimeType;
                    break;
                }
            }
        }
        if (text is null) throw new McpProtocolException(McpProtocolException.ResourceNotFound, $"Resource not found: {uri}");
        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = mime, ["text"] = text }),
        };
    }

    private JsonObject ListPrompts()
    {
        var arr = new JsonArray();
        foreach (var pr in _prompts)
        {
            var args = new JsonArray();
            foreach (var a in pr.Arguments)
                args.Add(new JsonObject { ["name"] = a.Name, ["description"] = a.Description, ["required"] = a.Required });
            arr.Add(new JsonObject { ["name"] = pr.Name, ["title"] = pr.Title, ["description"] = pr.Description, ["arguments"] = args });
        }
        return new JsonObject { ["prompts"] = arr };
    }

    private JsonObject GetPrompt(JsonObject? p)
    {
        string name = p?["name"] is JsonValue v && v.TryGetValue<string>(out var s) ? s
            : throw new McpProtocolException(McpProtocolException.InvalidParams, "prompts/get needs a name");
        var prompt = _prompts.FirstOrDefault(x => x.Name == name)
                     ?? throw new McpProtocolException(McpProtocolException.InvalidParams, $"Unknown prompt: {name}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (p!["arguments"] is JsonObject args)
        {
            foreach (var (k, val) in args)
                if (val is JsonValue jv && jv.TryGetValue<string>(out var str)) values[k] = str;
        }
        foreach (var a in prompt.Arguments.Where(a => a.Required))
            if (!values.ContainsKey(a.Name))
                throw new McpProtocolException(McpProtocolException.InvalidParams, $"Prompt '{name}' needs the argument '{a.Name}'");
        return new JsonObject
        {
            ["description"] = prompt.Description,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = prompt.Render(values) },
            }),
        };
    }
}
