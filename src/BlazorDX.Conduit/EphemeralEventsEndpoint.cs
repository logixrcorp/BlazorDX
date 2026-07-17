using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Conduit;

/// <summary>
/// The Conduit Router's outbound half: a GET endpoint at <see cref="RoutePattern"/> that
/// upgrades to a Server-Sent Events stream, plus <see cref="PushEphemeralEventAsync"/> — the
/// internal push method other services (<see cref="McpBrokerClient"/>, via
/// <see cref="McpProxyEndpoint"/> too) call to fan a "WITHDRAW"/"REFRESH"/"PAYLOAD" event out to
/// every live connection on a session.
///
/// No SignalR: this is plain Server-Sent Events over a minimal-API endpoint, matching the style
/// of the existing <c>app.MapPost("/mcp", ...)</c> endpoint in samples/BlazorDX.Demo.
/// </summary>
public static class EphemeralEventsEndpoint
{
    /// <summary>
    /// The route contract a frontend's <c>EventSource</c> connects to:
    /// <c>GET /ephemeral-events/{sessionId}</c>. Keep this literal in sync with any consumer —
    /// it is a cross-team contract, not an implementation detail.
    /// </summary>
    public const string RoutePattern = "/ephemeral-events/{sessionId}";

    /// <summary>
    /// Maps the SSE endpoint. Buffering is disabled end-to-end: the response sets
    /// <c>Content-Type: text/event-stream</c>, <c>Cache-Control: no-cache</c>, and
    /// <c>X-Accel-Buffering: no</c>, and every push flushes immediately — any reverse proxy in
    /// front of this deployment must also be configured not to buffer that response.
    /// </summary>
    /// <remarks>
    /// Like ASP.NET Core's own <c>MapGet</c>, this performs reflection-based delegate parameter
    /// binding (<c>RequestDelegateFactory</c>) and so cannot be trimmed/AOT-compiled — the
    /// requirement is propagated honestly rather than suppressed. Every other member of this
    /// project (the registry, the connection/writer types, the broker client) has no such
    /// requirement and stays trim/AOT-clean.
    /// </remarks>
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet, which binds the handler delegate's parameters via reflection.")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet, which may generate code to bind the handler delegate's parameters.")]
    public static IEndpointRouteBuilder MapEphemeralEvents(this WebApplication app)
    {
        app.MapGet(RoutePattern, static (
            HttpContext context,
            string sessionId,
            EphemeralSessionRegistry registry,
            CancellationToken cancellationToken) =>
        {
            IEphemeralSessionAuthorizer? authorizer =
                context.RequestServices.GetService<IEphemeralSessionAuthorizer>();
            return HandleGetAsync(context, sessionId, registry, authorizer, cancellationToken);
        });

        return app;
    }

    /// <summary>
    /// The handler behind <see cref="MapEphemeralEvents"/>, exposed internally so tests can
    /// drive it directly — construct a <see cref="DefaultHttpContext"/>, assert headers and
    /// registration/unregistration — without standing up a real Kestrel listener.
    /// </summary>
    internal static async Task HandleGetAsync(
        HttpContext context,
        string sessionId,
        EphemeralSessionRegistry registry,
        IEphemeralSessionAuthorizer? authorizer,
        CancellationToken cancellationToken)
    {
        if (authorizer is not null &&
            !await authorizer.IsAllowedAsync(sessionId, context, cancellationToken).ConfigureAwait(false))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // No buffering anywhere in the chain: the origin app, and any reverse proxy sitting in
        // front of it (X-Accel-Buffering: no is the nginx convention; other proxies read it too).
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        var writer = new HttpResponseEventWriter(context.Response);
        EphemeralConnection connection = registry.Register(sessionId, writer);
        try
        {
            // Block until the client disconnects (or the host is shutting down) — the pushes
            // themselves happen out-of-band, driven by PushEphemeralEventAsync from an
            // unrelated request/background loop, not from anything this method does itself.
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(), disconnected);
            await disconnected.Task.ConfigureAwait(false);
        }
        finally
        {
            registry.Unregister(connection);
        }
    }

    /// <summary>
    /// The internal push method other services call to emit a "WITHDRAW"/"REFRESH"/"PAYLOAD"
    /// event to every live connection on <paramref name="sessionId"/>. Never inspects
    /// <paramref name="data"/> — it is forwarded verbatim, opaque to this router. A session
    /// nobody is currently watching is a silent no-op: the Conduit is an ephemeral relay, not a
    /// durable queue, so an event with no listener is simply not delivered to anyone.
    /// </summary>
    public static async Task PushEphemeralEventAsync(
        this EphemeralSessionRegistry registry,
        string sessionId,
        string eventType,
        string data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (EphemeralConnection connection in registry.GetConnections(sessionId))
        {
            await connection.Writer.WriteEventAsync(eventType, data, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Writes one SSE frame to the live <see cref="HttpResponse"/> and flushes immediately after —
/// buffering defeats the purpose of a push channel, so every write is followed by its own
/// <c>FlushAsync</c> rather than relying on the framework's end-of-request flush, which would
/// never come for a stream that stays open indefinitely.
/// </summary>
internal sealed class HttpResponseEventWriter : IEphemeralEventWriter
{
    private readonly HttpResponse response;

    public HttpResponseEventWriter(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        this.response = response;
    }

    public async Task WriteEventAsync(string eventType, string data, CancellationToken cancellationToken)
    {
        string frame = $"event: {eventType}\ndata: {data}\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(frame);
        await response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
