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

    /// <summary>Registers a tool (replacing any with the same name). Fluent.</summary>
    public McpToolServer Add(IAiTool tool)
    {
        tools[tool.Name] = tool;
        return this;
    }

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

            if (root.TryGetProperty("id", out JsonElement idElement))
            {
                id = idElement.GetRawText();
            }

            string method = root.TryGetProperty("method", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty;

            return method switch
            {
                "initialize" => Response(id, InitializeResult()),
                "tools/list" => Response(id, await ToolsListResult(cancellationToken)),
                "tools/call" => Response(id, await ToolsCallResult(root, cancellationToken)),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
        }
        catch (JsonException ex)
        {
            return Error(id, -32700, $"Parse error: {ex.Message}");
        }
    }

    // Whether the (optional) authorizer permits this tool for the current caller.
    private async ValueTask<bool> IsAllowedAsync(IAiTool tool, CancellationToken cancellationToken) =>
        Authorizer is null || await Authorizer.IsAllowedAsync(tool, cancellationToken);

    private string InitializeResult()
    {
        StringBuilder sb = new();
        sb.Append("{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":");
        AppendString(sb, ServerName);
        sb.Append(",\"version\":\"1.0.0\"}}");
        return sb.ToString();
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
