using System.Text;
using System.Text.Json;
using BlazorDX.Primitives.Diagnostics;

namespace BlazorDX.Primitives.Forms;

/// <summary>
/// A minimal, transport-agnostic Model Context Protocol tool server. Register
/// <see cref="IAiTool"/>s (e.g. <see cref="FormAiTool{TModel}"/> built from a
/// <c>[DxFormModel]</c>) and feed it JSON-RPC requests; it answers <c>initialize</c>,
/// <c>tools/list</c>, and <c>tools/call</c>. Wire <see cref="HandleAsync"/> to any
/// transport (stdio, a SSE/HTTP endpoint) to expose a BlazorDX form to an AI assistant.
/// Pure JSON (built by hand / parsed with <see cref="JsonDocument"/>) — no reflection.
/// </summary>
public sealed class McpToolServer
{
    private readonly Dictionary<string, IAiTool> tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAiResource> resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAiPrompt> prompts = new(StringComparer.Ordinal);

    /// <summary>Server name advertised during <c>initialize</c>.</summary>
    public string ServerName { get; init; } = "BlazorDX";

    /// <summary>
    /// Optional authorization gate. When set, a tool the caller may not use is neither listed
    /// nor callable (an unauthorized <c>tools/call</c> looks exactly like an unknown tool, so
    /// the server never reveals that a privileged tool exists). When null, all tools are allowed.
    /// </summary>
    public IAiToolAuthorizer? Authorizer { get; init; }

    /// <summary>
    /// Optional audit/observability sink. Every <c>tools/call</c> — allowed, denied, ok, or
    /// errored — is reported here, giving an audit trail of what the AI did. When null, no-op.
    /// </summary>
    public IDxDiagnostics? Diagnostics { get; init; }

    /// <summary>
    /// Whether <c>initialize</c> advertises <c>tools.listChanged</c> — a promise that the server
    /// sends <c>notifications/tools/list_changed</c> when its tool set changes.
    /// </summary>
    /// <remarks>
    /// Off by default, and it should stay off unless something actually broadcasts
    /// <see cref="ToolListChangedNotification"/> over a session store. A client that is told the
    /// list can change stops re-listing and waits for a notice; if none ever arrives it simply
    /// works from a stale tool list forever, with no error anywhere to explain why.
    /// </remarks>
    public bool NotifiesToolListChanged { get; init; }

    /// <summary>
    /// The JSON-RPC notification announcing that the tool list changed. Pass it to
    /// <see cref="McpSessionStore.Broadcast"/> after adding or removing tools.
    /// </summary>
    public static string ToolListChangedNotification =>
        "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}";

    /// <summary>
    /// Optional gate for resources and prompts. Separate from <see cref="Authorizer"/> because
    /// adding these to <see cref="IAiToolAuthorizer"/> would break every host implementing it, and
    /// "may you read this" is a different question from "may you run this". When null, all
    /// registered resources and prompts are allowed.
    /// </summary>
    public IAiContentAuthorizer? ContentAuthorizer { get; init; }

    /// <summary>Registers a tool (replacing any with the same name). Fluent.</summary>
    public McpToolServer Add(IAiTool tool)
    {
        tools[tool.Name] = tool;
        return this;
    }

    /// <summary>Registers a resource (replacing any with the same URI). Fluent.</summary>
    public McpToolServer Add(IAiResource resource)
    {
        resources[resource.Uri] = resource;
        return this;
    }

    /// <summary>Registers a prompt (replacing any with the same name). Fluent.</summary>
    public McpToolServer Add(IAiPrompt prompt)
    {
        prompts[prompt.Name] = prompt;
        return this;
    }

    /// <summary>The registered resources, by URI.</summary>
    public IReadOnlyDictionary<string, IAiResource> Resources => resources;

    /// <summary>The registered prompts, by name.</summary>
    public IReadOnlyDictionary<string, IAiPrompt> Prompts => prompts;

    /// <summary>The registered tools, by name.</summary>
    public IReadOnlyDictionary<string, IAiTool> Tools => tools;

    /// <summary>
    /// Handles one JSON-RPC 2.0 request and returns the JSON-RPC response string.
    /// </summary>
    public async Task<string> HandleAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        string id = "null";
        try
        {
            using JsonDocument document = JsonDocument.Parse(requestJson);
            JsonElement root = document.RootElement;

            // A JSON-RPC batch is a valid JSON array, and TryGetProperty on a non-object throws
            // InvalidOperationException — not JsonException — so without this a batched body
            // escaped the catch below and took the transport down. Batching was removed from the
            // protocol in 2025-06-18; refusing it as an Invalid Request is both correct and the
            // answer a client can act on.
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Error(id, -32600, "Invalid Request: expected a single JSON-RPC object.");
            }

            if (root.TryGetProperty("id", out JsonElement idElement))
            {
                id = idElement.GetRawText();
            }

            string method = root.TryGetProperty("method", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty;

            return method switch
            {
                "initialize" => Response(id, InitializeResult(root)),
                "tools/list" => Response(id, await ToolsListResult(cancellationToken)),
                "tools/call" => Response(id, await ToolsCallResult(root, cancellationToken)),
                "resources/list" => Response(id, await ResourcesListResult(cancellationToken)),
                "resources/read" => await ResourcesReadResponse(id, root, cancellationToken),
                "prompts/list" => Response(id, await PromptsListResult(cancellationToken)),
                "prompts/get" => await PromptsGetResponse(id, root, cancellationToken),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
        }
        catch (JsonException ex)
        {
            return Error(id, -32700, $"Parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether a message warrants a reply. JSON-RPC forbids answering a notification (a message
    /// carrying no <c>id</c>), and every transport has to honour that, so the rule lives here
    /// rather than being re-derived — and re-derived differently — in each host.
    /// </summary>
    /// <remarks>
    /// Malformed JSON counts as warranting a response: the caller gets the parse error rather
    /// than silence, which is the difference between a client that reports a bad request and one
    /// that hangs waiting.
    /// </remarks>
    public static bool ExpectsResponse(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.TryGetProperty("id", out _);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>
    /// Whether a message is an <c>initialize</c> request — the point at which a session-based
    /// transport mints a session, since it is the one request that arrives without one.
    /// </summary>
    public static bool IsInitialize(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("method", out JsonElement method)
                && method.ValueKind == JsonValueKind.String
                && method.ValueEquals("initialize");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a JSON-RPC error object, so a transport can answer with a protocol-shaped body for
    /// failures the tool server never sees — an expired session, an unreadable request.
    /// </summary>
    /// <param name="id">The request id as raw JSON, or <c>"null"</c> when it could not be read.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">Human-readable message, returned to the caller.</param>
    public static string ErrorJson(string id, int code, string message) => Error(id, code, message);

    // Whether the (optional) authorizer permits this tool for the current caller.
    private async ValueTask<bool> IsAllowedAsync(IAiTool tool, CancellationToken cancellationToken) =>
        Authorizer is null || await Authorizer.IsAllowedAsync(tool, cancellationToken);

    /// <summary>
    /// Protocol revisions this server speaks, newest last. <c>2025-03-26</c> is the revision that
    /// introduced the HTTP + Server-Sent Events transport (<see cref="McpHttpHost"/>).
    /// </summary>
    private static readonly string[] SupportedProtocolVersions = ["2024-11-05", "2025-03-26"];

    private string InitializeResult(JsonElement root)
    {
        // Echo the client's revision when it is one we speak, otherwise name our newest and let
        // the client decide whether it can proceed. Answering with a fixed version regardless of
        // what was asked is how a server ends up claiming a revision whose rules it does not
        // follow — which is what the previous hard-coded string did.
        string version = SupportedProtocolVersions[^1];
        if (root.TryGetProperty("params", out JsonElement p)
            && p.ValueKind == JsonValueKind.Object
            && p.TryGetProperty("protocolVersion", out JsonElement requested)
            && requested.ValueKind == JsonValueKind.String)
        {
            string? asked = requested.GetString();
            if (asked is not null && Array.IndexOf(SupportedProtocolVersions, asked) >= 0)
            {
                version = asked;
            }
        }

        StringBuilder sb = new();
        sb.Append("{\"protocolVersion\":");
        AppendString(sb, version);

        // listChanged is a promise to send notifications/tools/list_changed. It is opt-in because
        // only a host with a session store can deliver one, and advertising a capability the
        // server cannot honour makes a client wait for a message that never comes.
        sb.Append(",\"capabilities\":{\"tools\":{");
        if (NotifiesToolListChanged)
        {
            sb.Append("\"listChanged\":true");
        }

        sb.Append("}");

        // Only advertise what is actually registered. A client that is told this server has
        // resources will call resources/list; answering an empty list is a wasted round trip on
        // every single connection, and it makes a genuinely empty surface indistinguishable from
        // one whose registration was forgotten.
        if (resources.Count > 0)
        {
            sb.Append(",\"resources\":{}");
        }

        if (prompts.Count > 0)
        {
            sb.Append(",\"prompts\":{}");
        }

        sb.Append("},\"serverInfo\":{\"name\":");
        AppendString(sb, ServerName);
        sb.Append(",\"version\":\"1.0.0\"}}");
        return sb.ToString();
    }

    private async ValueTask<bool> IsAllowedAsync(IAiResource resource, CancellationToken cancellationToken) =>
        ContentAuthorizer is null || await ContentAuthorizer.IsAllowedAsync(resource, cancellationToken);

    private async ValueTask<bool> IsAllowedAsync(IAiPrompt prompt, CancellationToken cancellationToken) =>
        ContentAuthorizer is null || await ContentAuthorizer.IsAllowedAsync(prompt, cancellationToken);

    private async Task<string> ResourcesListResult(CancellationToken cancellationToken)
    {
        StringBuilder sb = new();
        sb.Append("{\"resources\":[");
        bool first = true;
        foreach (IAiResource resource in resources.Values)
        {
            if (!await IsAllowedAsync(resource, cancellationToken))
            {
                continue;   // never advertise a resource the caller may not read
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append("{\"uri\":");
            AppendString(sb, resource.Uri);
            sb.Append(",\"name\":");
            AppendString(sb, resource.Name);
            sb.Append(",\"description\":");
            AppendString(sb, resource.Description ?? string.Empty);
            if (resource.MimeType is not null)
            {
                sb.Append(",\"mimeType\":");
                AppendString(sb, resource.MimeType);
            }

            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private async Task<string> ResourcesReadResponse(string id, JsonElement root, CancellationToken cancellationToken)
    {
        string uri = string.Empty;
        if (root.TryGetProperty("params", out JsonElement p)
            && p.ValueKind == JsonValueKind.Object
            && p.TryGetProperty("uri", out JsonElement u)
            && u.ValueKind == JsonValueKind.String)
        {
            uri = u.GetString() ?? string.Empty;
        }

        // Unknown and unauthorized are answered identically, so the surface never reveals that a
        // privileged resource exists — the same rule tools already follow.
        if (!resources.TryGetValue(uri, out IAiResource? resource)
            || !await IsAllowedAsync(resource, cancellationToken))
        {
            Diagnostics.TryReportWarning("BlazorDX.Mcp", $"resources/read denied or unknown: '{uri}'");
            return Error(id, -32002, $"Resource not found: {uri}");
        }

        try
        {
            AiResourceContent content = await resource.ReadAsync(cancellationToken);
            Diagnostics.TryReportInfo("BlazorDX.Mcp", $"resources/read '{uri}' -> ok");

            StringBuilder sb = new();
            sb.Append("{\"contents\":[{\"uri\":");
            AppendString(sb, uri);
            sb.Append(",\"mimeType\":");
            AppendString(sb, content.MimeType);

            if (content.Bytes is { } bytes)
            {
                sb.Append(",\"blob\":");
                AppendString(sb, Convert.ToBase64String(bytes.Span));
            }
            else
            {
                sb.Append(",\"text\":");
                AppendString(sb, content.Text ?? string.Empty);
            }

            sb.Append("}]}");
            return Response(id, sb.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A reader threw: report it and answer a protocol error rather than crashing the
            // transport, matching how a failing tool is handled.
            Diagnostics.TryReportError("BlazorDX.Mcp", $"resources/read '{uri}' threw: {ex.Message}", ex);
            return Error(id, -32603, $"Resource '{uri}' could not be read.");
        }
    }

    private async Task<string> PromptsListResult(CancellationToken cancellationToken)
    {
        StringBuilder sb = new();
        sb.Append("{\"prompts\":[");
        bool first = true;
        foreach (IAiPrompt prompt in prompts.Values)
        {
            if (!await IsAllowedAsync(prompt, cancellationToken))
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append("{\"name\":");
            AppendString(sb, prompt.Name);
            sb.Append(",\"description\":");
            AppendString(sb, prompt.Description ?? string.Empty);
            sb.Append(",\"arguments\":[");

            for (int i = 0; i < prompt.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                AiPromptArgument argument = prompt.Arguments[i];
                sb.Append("{\"name\":");
                AppendString(sb, argument.Name);
                sb.Append(",\"description\":");
                AppendString(sb, argument.Description ?? string.Empty);
                sb.Append(",\"required\":").Append(argument.Required ? "true" : "false").Append('}');
            }

            sb.Append("]}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private async Task<string> PromptsGetResponse(string id, JsonElement root, CancellationToken cancellationToken)
    {
        string name = string.Empty;
        Dictionary<string, string> arguments = new(StringComparer.Ordinal);

        if (root.TryGetProperty("params", out JsonElement p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String)
            {
                name = n.GetString() ?? string.Empty;
            }

            if (p.TryGetProperty("arguments", out JsonElement a) && a.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty argument in a.EnumerateObject())
                {
                    // A client may send a number for a numeric-looking argument; take its raw
                    // text rather than dropping it, as the tool surface does.
                    arguments[argument.Name] = argument.Value.ValueKind switch
                    {
                        JsonValueKind.String => argument.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => argument.Value.GetRawText(),
                        _ => string.Empty,
                    };
                }
            }
        }

        if (!prompts.TryGetValue(name, out IAiPrompt? prompt)
            || !await IsAllowedAsync(prompt, cancellationToken))
        {
            Diagnostics.TryReportWarning("BlazorDX.Mcp", $"prompts/get denied or unknown: '{name}'");
            return Error(id, -32602, $"Prompt not found: {name}");
        }

        try
        {
            AiPromptResult result = await prompt.GetAsync(arguments, cancellationToken);
            Diagnostics.TryReportInfo("BlazorDX.Mcp", $"prompts/get '{name}' -> ok");

            StringBuilder sb = new();
            sb.Append("{\"description\":");
            AppendString(sb, result.Description ?? string.Empty);
            sb.Append(",\"messages\":[");

            for (int i = 0; i < result.Messages.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"role\":");
                AppendString(sb, result.Messages[i].Role);
                sb.Append(",\"content\":{\"type\":\"text\",\"text\":");
                AppendString(sb, result.Messages[i].Text);
                sb.Append("}}");
            }

            sb.Append("]}");
            return Response(id, sb.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Diagnostics.TryReportError("BlazorDX.Mcp", $"prompts/get '{name}' threw: {ex.Message}", ex);
            return Error(id, -32603, $"Prompt '{name}' could not be expanded.");
        }
    }

    private async Task<string> ToolsListResult(CancellationToken cancellationToken)
    {
        StringBuilder sb = new();
        sb.Append("{\"tools\":[");
        bool first = true;
        foreach (IAiTool tool in tools.Values)
        {
            if (!await IsAllowedAsync(tool, cancellationToken))
            {
                continue;   // never advertise a tool the caller may not use
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append("{\"name\":");
            AppendString(sb, tool.Name);
            sb.Append(",\"description\":");
            AppendString(sb, tool.Description ?? string.Empty);
            sb.Append(",\"inputSchema\":").Append(tool.InputSchemaJson);
            sb.Append(",\"annotations\":{\"readOnlyHint\":").Append(tool.IsReadOnly ? "true" : "false").Append("}}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private async Task<string> ToolsCallResult(JsonElement root, CancellationToken cancellationToken)
    {
        string name = string.Empty;
        string arguments = "{}";
        if (root.TryGetProperty("params", out JsonElement p))
        {
            if (p.TryGetProperty("name", out JsonElement n))
            {
                name = n.GetString() ?? string.Empty;
            }

            if (p.TryGetProperty("arguments", out JsonElement a) && a.ValueKind == JsonValueKind.Object)
            {
                arguments = a.GetRawText();
            }
        }

        // An unknown tool and an unauthorized tool look identical to the caller, so the server
        // never leaks that a privileged tool exists.
        if (!tools.TryGetValue(name, out IAiTool? tool) || !await IsAllowedAsync(tool, cancellationToken))
        {
            Diagnostics.TryReportWarning("BlazorDX.Mcp", $"tools/call denied or unknown: '{name}'");
            return ContentResult($"Unknown tool: {name}", isError: true);
        }

        try
        {
            AiToolResult result = await tool.InvokeAsync(arguments, cancellationToken);
            Diagnostics.TryReportInfo("BlazorDX.Mcp", $"tools/call '{name}' -> {(result.IsError ? "error" : "ok")}");
            return ContentResult(result.Text, result.IsError);
        }
        catch (OperationCanceledException)
        {
            Diagnostics.TryReportWarning("BlazorDX.Mcp", $"tools/call '{name}' cancelled");
            throw;
        }
        catch (Exception ex)
        {
            // A tool handler threw: report it and return an error result rather than crashing
            // the transport. The AI sees a clean error it can react to.
            Diagnostics.TryReportError("BlazorDX.Mcp", $"tools/call '{name}' threw: {ex.Message}", ex);
            return ContentResult($"Tool '{name}' failed.", isError: true);
        }
    }

    // MCP tool-call result: { content: [ { type: "text", text } ], isError }
    private static string ContentResult(string text, bool isError)
    {
        StringBuilder sb = new();
        sb.Append("{\"content\":[{\"type\":\"text\",\"text\":");
        AppendString(sb, text);
        sb.Append("}],\"isError\":").Append(isError ? "true" : "false").Append('}');
        return sb.ToString();
    }

    private static string Response(string id, string resultJson) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";

    private static string Error(string id, int code, string message)
    {
        StringBuilder sb = new();
        sb.Append("{\"jsonrpc\":\"2.0\",\"id\":").Append(id).Append(",\"error\":{\"code\":").Append(code)
          .Append(",\"message\":");
        AppendString(sb, message);
        sb.Append("}}");
        return sb.ToString();
    }

    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}
