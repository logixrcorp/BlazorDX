using System.Text;

namespace BlazorDX.Primitives.Forms;

/// <summary>One inbound HTTP request, reduced to the parts the MCP transport actually reads.</summary>
/// <param name="Method">The HTTP method, e.g. <c>POST</c>. Case-insensitive.</param>
/// <param name="Body">The request body, for <c>POST</c>.</param>
/// <param name="SessionId">The <c>Mcp-Session-Id</c> request header, if the client sent one.</param>
public sealed record McpHttpRequest(string Method, string? Body = null, string? SessionId = null);

/// <summary>
/// What the endpoint should send back. Either <see cref="Body"/> (a complete response) or
/// <see cref="Stream"/> (an open server-to-client stream) is set, never both.
/// </summary>
/// <param name="StatusCode">The HTTP status to return.</param>
/// <param name="ContentType">The <c>Content-Type</c>, when there is a body or a stream.</param>
/// <param name="Body">The complete response body, if any.</param>
/// <param name="SessionId">Set the <c>Mcp-Session-Id</c> response header to this, if non-null.</param>
/// <param name="Stream">When set, write <see cref="McpSse.Frame"/> of each message from
/// <see cref="McpSession.ReadAllAsync"/> until it ends.</param>
public sealed record McpHttpResponse(
    int StatusCode,
    string? ContentType = null,
    string? Body = null,
    string? SessionId = null,
    McpSession? Stream = null);

/// <summary>
/// Server-Sent Events framing. Kept separate from the host because the framing is the only part
/// an endpoint has to get byte-exact, and it is worth being able to test on its own.
/// </summary>
public static class McpSse
{
    /// <summary>
    /// Frames one message as an SSE <c>data:</c> event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every line needs its own <c>data:</c> prefix. A JSON-RPC message containing a newline —
    /// which any tool returning multi-line text produces — would otherwise be cut in half by the
    /// first bare newline, and the client would see a truncated event followed by garbage.
    /// </para>
    /// <para>
    /// The framing round-trips: a client joins the <c>data:</c> lines with <c>\n</c> and strips
    /// one trailing <c>\n</c>, recovering the message exactly — including leading spaces, empty
    /// lines, and a trailing newline. The one exception is <c>\r\n</c> inside the payload, which
    /// normalises to <c>\n</c>. That is harmless for JSON-RPC, where a real carriage return inside
    /// a string is escaped as <c>\\r</c> and a literal CRLF can only be insignificant whitespace.
    /// </para>
    /// </remarks>
    public static string Frame(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        StringBuilder sb = new();
        foreach (ReadOnlySpan<char> line in message.AsSpan().EnumerateLines())
        {
            sb.Append("data: ").Append(line).Append('\n');
        }

        // The blank line is what dispatches the event; without it the client buffers forever.
        sb.Append('\n');
        return sb.ToString();
    }
}

/// <summary>
/// Serves an <see cref="McpToolServer"/> over MCP's HTTP transport: <c>POST</c> for
/// request/response, <c>GET</c> for a Server-Sent Events stream carrying server-initiated
/// messages, and <c>DELETE</c> to end a session.
/// </summary>
/// <remarks>
/// <para>
/// This type speaks in strings and status codes rather than <c>HttpContext</c>, deliberately.
/// <c>BlazorDX.Primitives</c> is browser-WASM safe and references the Blazor component packages
/// rather than the ASP.NET Core framework, so binding the transport to a web framework would cost
/// the whole library that guarantee for one feature. The trade is that a host writes a short
/// endpoint to move headers and the body across — see <c>samples/BlazorDX.McpServer</c> — and gets
/// a transport that is unit-testable without a web server in exchange.
/// </para>
/// <para>
/// <b>Sessions are optional.</b> Without an <see cref="McpSessionStore"/> this is a plain
/// request/response endpoint and <c>GET</c> answers <c>405</c>, which is what the protocol
/// prescribes for a server that cannot stream. With a store, <c>initialize</c> mints a session id,
/// every later request must carry it, and an unknown id answers <c>404</c> so the client knows to
/// initialize again rather than retrying forever.
/// </para>
/// </remarks>
/// <param name="server">The tool server handling JSON-RPC.</param>
/// <param name="sessions">Session store; omit for a request/response-only endpoint.</param>
public sealed class McpHttpHost(McpToolServer server, McpSessionStore? sessions = null)
{
    private const string Json = "application/json";
    private const string EventStream = "text/event-stream";

    /// <summary>The session store, or null when this host is request/response only.</summary>
    public McpSessionStore? Sessions => sessions;

    /// <summary>Applies the transport rules to one request.</summary>
    public async Task<McpHttpResponse> HandleAsync(
        McpHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Method.ToUpperInvariant() switch
        {
            "POST" => await PostAsync(request, cancellationToken),
            "GET" => Get(request),
            "DELETE" => Delete(request),
            _ => new McpHttpResponse(405),
        };
    }

    private async Task<McpHttpResponse> PostAsync(McpHttpRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return new McpHttpResponse(400, Json, McpToolServer.ErrorJson("null", -32600, "Empty request body."));
        }

        bool initializing = McpToolServer.IsInitialize(request.Body);
        McpSession? session = null;

        if (sessions is not null)
        {
            if (initializing)
            {
                session = sessions.Create();
            }
            else if (!sessions.TryGet(request.SessionId, out session))
            {
                // 404, not 401: the client is not unauthorized, its session is gone (swept,
                // deleted, or the server restarted). The protocol's meaning of 404 here is
                // "initialize again", which is exactly what it should do.
                return new McpHttpResponse(404, Json, McpToolServer.ErrorJson("null", -32001, "Unknown or expired session."));
            }
        }

        // A notification carries no id and JSON-RPC forbids answering it, so there is nothing to
        // return but an acknowledgement that it was accepted.
        if (!McpToolServer.ExpectsResponse(request.Body))
        {
            await server.HandleAsync(request.Body, cancellationToken);
            return new McpHttpResponse(202, SessionId: session?.Id);
        }

        string body = await server.HandleAsync(request.Body, cancellationToken);
        return new McpHttpResponse(200, Json, body, session?.Id);
    }

    private McpHttpResponse Get(McpHttpRequest request)
    {
        if (sessions is null)
        {
            // The protocol's way of saying "this server never initiates messages". A client that
            // gets this simply does not open a listening stream.
            return new McpHttpResponse(405);
        }

        if (!sessions.TryGet(request.SessionId, out McpSession? session))
        {
            return new McpHttpResponse(404, Json, McpToolServer.ErrorJson("null", -32001, "Unknown or expired session."));
        }

        return new McpHttpResponse(200, EventStream, Stream: session);
    }

    private McpHttpResponse Delete(McpHttpRequest request)
    {
        if (sessions is null)
        {
            return new McpHttpResponse(405);
        }

        // Terminating an already-terminated session is a client retrying, not an error.
        sessions.Terminate(request.SessionId);
        return new McpHttpResponse(204);
    }
}
