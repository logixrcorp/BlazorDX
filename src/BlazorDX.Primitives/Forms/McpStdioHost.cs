using System.Text.Json;

namespace BlazorDX.Primitives.Forms;

/// <summary>
/// Runs an <see cref="McpToolServer"/> over a <b>stdio</b> transport: newline-delimited
/// JSON-RPC read from <paramref name="input"/> and written to <paramref name="output"/>.
/// This is how local assistants (e.g. Claude Desktop) connect — they spawn a process and
/// speak MCP over its stdin/stdout. Per JSON-RPC, notifications (requests with no
/// <c>id</c>) get no reply. Reflection-free and dependency-free, so the same host runs on
/// any .NET target.
/// </summary>
public static class McpStdioHost
{
    /// <summary>
    /// Pumps requests from <paramref name="input"/> through <paramref name="server"/> until the
    /// input ends or <paramref name="cancellationToken"/> is cancelled. Each non-empty line is
    /// one JSON-RPC message; each request's response is written as one line.
    /// </summary>
    public static async Task RunAsync(
        McpToolServer server,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;   // input closed — the host exited
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string response = await server.HandleAsync(line, cancellationToken);

            // JSON-RPC forbids replying to a notification (a request carrying no "id").
            if (ExpectsResponse(line))
            {
                await output.WriteLineAsync(response.AsMemory(), cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
        }
    }

    // A message warrants a response unless it is a notification (a JSON object with no "id").
    // Malformed JSON still warrants the JSON-RPC parse-error response, so it counts as needing one.
    private static bool ExpectsResponse(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.TryGetProperty("id", out _);
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
