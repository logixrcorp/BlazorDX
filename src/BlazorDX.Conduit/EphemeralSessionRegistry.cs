using System.Collections.Concurrent;

namespace BlazorDX.Conduit;

/// <summary>
/// Writes one SSE event frame to a single connected client. <see cref="EphemeralConnection"/>
/// holds one of these per active stream. Production code gets it from
/// <c>HttpResponseEventWriter</c> (a thin wrapper over the live <c>HttpResponse</c>, in
/// EphemeralEventsEndpoint.cs); tests substitute a Moq double so fan-out and ordering can be
/// asserted without a real socket.
/// </summary>
public interface IEphemeralEventWriter
{
    /// <summary>Writes one SSE frame (<paramref name="eventType"/>/<paramref name="data"/>) and flushes it. <paramref name="data"/> is opaque — never inspected, only forwarded.</summary>
    Task WriteEventAsync(string eventType, string data, CancellationToken cancellationToken);
}

/// <summary>One registered SSE stream: which session it belongs to, and how to write to it.</summary>
public sealed class EphemeralConnection
{
    internal EphemeralConnection(string sessionId, IEphemeralEventWriter writer)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        Writer = writer;
    }

    /// <summary>A unique id for this connection, distinct from other connections on the same session.</summary>
    public Guid Id { get; }

    /// <summary>The session this connection is subscribed to.</summary>
    public string SessionId { get; }

    /// <summary>How to write an event to this connection.</summary>
    internal IEphemeralEventWriter Writer { get; }
}

/// <summary>
/// Tracks every active ephemeral SSE connection, grouped by session id. A session can have more
/// than one live connection at once (e.g. the same family member with the app open on two
/// devices); a push fans out to all of them. The registry never inspects event payloads — it
/// only routes by session id, which is exactly the "blind router" contract the Conduit is built
/// around: it groups and delivers, it never reads.
///
/// Register this with DI as a Singleton: it must outlive any single request/connection scope,
/// since SSE connections are long-lived and pushes arrive from unrelated requests (the external
/// provider's webhook, or <see cref="McpBrokerClient"/>'s background listener).
/// </summary>
public sealed class EphemeralSessionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, EphemeralConnection>> sessions =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Adds a new connection to <paramref name="sessionId"/>'s group. Always succeeds — callers
    /// must have already authorized the connection (see <see cref="IEphemeralSessionAuthorizer"/>)
    /// before calling this; the registry itself performs no authorization.
    /// </summary>
    public EphemeralConnection Register(string sessionId, IEphemeralEventWriter writer)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(writer);

        var connection = new EphemeralConnection(sessionId, writer);
        ConcurrentDictionary<Guid, EphemeralConnection> group =
            sessions.GetOrAdd(sessionId, static _ => new ConcurrentDictionary<Guid, EphemeralConnection>());
        group[connection.Id] = connection;
        return connection;
    }

    /// <summary>
    /// Removes a connection, e.g. when the client disconnects. Pruning an emptied group keeps
    /// the registry from accumulating dictionary entries for sessions nobody is watching
    /// anymore — a long-running server would otherwise leak one entry per session id ever seen.
    /// </summary>
    public void Unregister(EphemeralConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!sessions.TryGetValue(connection.SessionId, out ConcurrentDictionary<Guid, EphemeralConnection>? group))
        {
            return;
        }

        group.TryRemove(connection.Id, out _);
        if (group.IsEmpty)
        {
            // Conditional remove: only prune the group if it is still the same (now-empty)
            // instance, so a concurrent Register() that just repopulated it is never lost.
            sessions.TryRemove(new KeyValuePair<string, ConcurrentDictionary<Guid, EphemeralConnection>>(connection.SessionId, group));
        }
    }

    /// <summary>The connections currently registered for <paramref name="sessionId"/>, or empty if none.</summary>
    public IReadOnlyCollection<EphemeralConnection> GetConnections(string sessionId) =>
        sessions.TryGetValue(sessionId, out ConcurrentDictionary<Guid, EphemeralConnection>? group)
            ? group.Values.ToArray()
            : Array.Empty<EphemeralConnection>();

    /// <summary>How many connections are currently registered for <paramref name="sessionId"/>.</summary>
    public int ConnectionCount(string sessionId) =>
        sessions.TryGetValue(sessionId, out ConcurrentDictionary<Guid, EphemeralConnection>? group) ? group.Count : 0;
}
