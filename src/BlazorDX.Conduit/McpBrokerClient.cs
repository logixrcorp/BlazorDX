using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;

namespace BlazorDX.Conduit;

/// <summary>
/// What an update notification from the external MCP resource-provider is telling the client to
/// do with a resource it already delivered.
/// </summary>
public enum ConduitAction
{
    /// <summary>The resource is gone; the client should discard whatever it holds for it.</summary>
    Withdraw,

    /// <summary>A newer version exists; the client should re-fetch it.</summary>
    Refresh,
}

/// <summary>
/// One <c>notifications/resources/updated</c> event from the external MCP resource-provider,
/// already mapped to the BlazorDX session it concerns. <see cref="MessageId"/> is opaque to the
/// Conduit — it is never interpreted, only forwarded as the SSE event's <c>data</c>.
/// </summary>
public sealed record ConduitNotification(string MessageId, string SessionId, ConduitAction Action);

/// <summary>
/// The channel <see cref="McpBrokerClient"/> listens on for update notifications from the
/// external MCP resource-provider — a webhook relay or an Azure Service Bus subscription in
/// production. A host implements this over its own transport, mirroring how
/// <c>IAiToolAuthorizer</c> and <see cref="IEphemeralSessionAuthorizer"/> are host-supplied
/// extension points: BlazorDX defines the seam, the host wires the real connection. This
/// project ships no concrete implementation (no Azure SDK dependency, no webhook listener) —
/// that wiring belongs to the host, which knows the provider's real endpoint and credentials.
/// </summary>
public interface IConduitNotificationSource
{
    /// <summary>
    /// Yields notifications as they arrive. A source backing a queue/subscription that should
    /// run for the app's lifetime never completes on its own — it stops only when
    /// <paramref name="cancellationToken"/> is cancelled, at which point the enumeration ends
    /// (by returning or by throwing <see cref="OperationCanceledException"/>; both are handled).
    /// </summary>
    IAsyncEnumerable<ConduitNotification> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// BlazorDX acting as an MCP CLIENT of an external resource-provider server — the reverse role
/// of samples/BlazorDX.McpServer (which is BlazorDX serving ITS OWN tools to external
/// assistants; that project is untouched by this one and the two are never confused).
///
/// This background service drains <see cref="IConduitNotificationSource"/> and routes each
/// WITHDRAW/REFRESH notification to the right session's SSE stream via
/// <see cref="RouteToSessionAsync"/> — the same routing primitive <c>McpProxyEndpoint</c> uses
/// for the initial encrypted payload delivery, so there is exactly one place in the codebase
/// that knows how "a session id plus an event" becomes an SSE push.
/// </summary>
public sealed class McpBrokerClient : BackgroundService
{
    private readonly IConduitNotificationSource notificationSource;
    private readonly EphemeralSessionRegistry registry;

    public McpBrokerClient(IConduitNotificationSource notificationSource, EphemeralSessionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(notificationSource);
        ArgumentNullException.ThrowIfNull(registry);

        this.notificationSource = notificationSource;
        this.registry = registry;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    /// <summary>
    /// The drain loop, exposed internally so tests can await it directly instead of racing
    /// <see cref="BackgroundService"/>'s Start/Stop host lifecycle timing.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ConduitNotification notification in notificationSource
                .ReceiveAsync(cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                string eventType = notification.Action == ConduitAction.Withdraw ? "WITHDRAW" : "REFRESH";
                await RouteToSessionAsync(registry, notification.SessionId, eventType, notification.MessageId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown: the host asked us to stop draining.
        }
    }

    /// <summary>
    /// The Conduit's single routing primitive: hands an opaque event — a WITHDRAW/REFRESH
    /// invalidation from <see cref="IConduitNotificationSource"/>, or the initial encrypted
    /// response payload <c>McpProxyEndpoint</c> receives directly from the provider — to
    /// <see cref="EphemeralEventsEndpoint.PushEphemeralEventAsync"/> for SSE delivery.
    ///
    /// Never inspects <paramref name="data"/>: it is either an opaque message id or opaque
    /// ciphertext, forwarded verbatim to every connection on <paramref name="sessionId"/>. That
    /// is the Zero-Knowledge Routing contract this whole router is built around — this process
    /// never decrypts a payload and never holds a client's private key.
    /// </summary>
    internal static Task RouteToSessionAsync(
        EphemeralSessionRegistry registry,
        string sessionId,
        string eventType,
        string data,
        CancellationToken cancellationToken) =>
        registry.PushEphemeralEventAsync(sessionId, eventType, data, cancellationToken);
}
