using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace BlazorDX.Primitives.Forms;

/// <summary>
/// One client's connection to an <see cref="McpToolServer"/> over a transport that has no
/// connection of its own — HTTP. It holds the queue of messages the <em>server</em> wants to send
/// the client, which is the thing a request/response endpoint cannot express.
/// </summary>
/// <remarks>
/// <para>
/// Sessions exist for the server-to-client direction. A tool call is a request the client already
/// has an open response for; a progress update, a "the tool list changed" notice, or anything the
/// app decides to volunteer has no such response to ride on. The client opens a long-lived
/// <c>GET</c> that streams this queue instead.
/// </para>
/// <para>
/// The queue is <b>bounded and lossy by design</b>. A client that opened a session and never
/// drained it would otherwise grow the server's memory without limit, and a disconnected client is
/// exactly the case that happens in production. <see cref="TryPost"/> returns
/// <see langword="false"/> rather than blocking or growing, so a caller learns its notification
/// was dropped instead of discovering the leak later.
/// </para>
/// </remarks>
public sealed class McpSession
{
    private readonly Channel<string> outbound;
    private long lastSeenTicks;

    internal McpSession(string id, int capacity, DateTimeOffset now)
    {
        Id = id;
        Created = now;
        lastSeenTicks = now.UtcTicks;

        // Wait, not one of the Drop modes, precisely because we only ever TryWrite: under Wait a
        // full queue makes TryWrite return false, whereas every Drop* mode returns true and
        // discards the item — which would make TryPost claim a delivery it did not make. Nothing
        // ever actually waits here, because TryWrite does not block.
        //
        // SingleReader stays false: the protocol says a client should not open two streams on one
        // session, but "should not" is not "cannot", and two readers on a single-reader channel is
        // undefined behaviour rather than a handled error.
        outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
        });
    }

    /// <summary>The session id, sent to the client as <c>Mcp-Session-Id</c>.</summary>
    public string Id { get; }

    /// <summary>When the session was created.</summary>
    public DateTimeOffset Created { get; }

    /// <summary>When the client was last seen, used by <see cref="McpSessionStore.Sweep"/>.</summary>
    public DateTimeOffset LastSeen => new(Interlocked.Read(ref lastSeenTicks), TimeSpan.Zero);

    internal void Touch(DateTimeOffset now) => Interlocked.Exchange(ref lastSeenTicks, now.UtcTicks);

    /// <summary>
    /// Queues a JSON-RPC message for delivery to the client. Returns <see langword="false"/> if
    /// the queue is full (a client that is not draining it) or the session has been terminated —
    /// in both cases the message is dropped, and the caller is told so rather than left to assume
    /// it arrived.
    /// </summary>
    public bool TryPost(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return outbound.Writer.TryWrite(message);
    }

    /// <summary>
    /// Streams queued messages until the session is terminated or the token is cancelled. This is
    /// what an SSE endpoint iterates.
    /// </summary>
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken = default) =>
        outbound.Reader.ReadAllAsync(cancellationToken);

    // Completing the writer ends every in-flight ReadAllAsync, so terminating a session also ends
    // the streaming response holding it open.
    internal void Close() => outbound.Writer.TryComplete();
}

/// <summary>
/// The live <see cref="McpSession"/>s, keyed by id. Attach one to <see cref="McpHttpHost"/> to
/// serve MCP over HTTP; stdio needs none, because its connection <em>is</em> the session.
/// </summary>
/// <remarks>
/// Session ids are generated with <see cref="RandomNumberGenerator"/>, not <see cref="Guid"/>:
/// the id is the only thing separating one caller's session from another's, so it is a credential
/// and has to be unguessable rather than merely unique.
/// </remarks>
public sealed class McpSessionStore
{
    private readonly Dictionary<string, McpSession> sessions = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>Clock source. Override in tests so expiry does not depend on wall time.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>How many server-to-client messages one session may buffer before dropping.</summary>
    public int QueueCapacity { get; init; } = 256;

    /// <summary>The number of live sessions.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return sessions.Count;
            }
        }
    }

    /// <summary>Mints a new session with a cryptographically random id.</summary>
    public McpSession Create()
    {
        // 32 hex characters = 128 bits. Visible ASCII only, as the transport puts it in a header.
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        McpSession session = new(id, QueueCapacity, TimeProvider.GetUtcNow());

        lock (gate)
        {
            sessions[id] = session;
        }

        return session;
    }

    /// <summary>Looks up a live session and marks it seen. Returns false for unknown or terminated ids.</summary>
    public bool TryGet(string? id, [NotNullWhen(true)] out McpSession? session)
    {
        session = null;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        lock (gate)
        {
            if (!sessions.TryGetValue(id, out session))
            {
                return false;
            }
        }

        session.Touch(TimeProvider.GetUtcNow());
        return true;
    }

    /// <summary>
    /// Ends a session and closes any stream holding it open. Returns whether it existed — a
    /// second <c>DELETE</c> is not an error, it is a client retrying.
    /// </summary>
    public bool Terminate(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        McpSession? session;
        lock (gate)
        {
            if (!sessions.Remove(id, out session))
            {
                return false;
            }
        }

        session.Close();
        return true;
    }

    /// <summary>
    /// Drops sessions unseen for longer than <paramref name="idleTimeout"/> and returns how many
    /// went. A client can vanish without a <c>DELETE</c> — a browser tab closing, a crash, a
    /// network partition — so without a sweep the store only ever grows.
    /// </summary>
    public int Sweep(TimeSpan idleTimeout)
    {
        DateTimeOffset cutoff = TimeProvider.GetUtcNow() - idleTimeout;
        List<McpSession> expired = [];

        lock (gate)
        {
            foreach (KeyValuePair<string, McpSession> entry in sessions)
            {
                if (entry.Value.LastSeen <= cutoff)
                {
                    expired.Add(entry.Value);
                }
            }

            foreach (McpSession session in expired)
            {
                sessions.Remove(session.Id);
            }
        }

        foreach (McpSession session in expired)
        {
            session.Close();
        }

        return expired.Count;
    }

    /// <summary>
    /// Queues a message to every live session, returning how many accepted it. The count is
    /// deliberately not the session count: a session whose queue is full drops the message, and a
    /// broadcast that quietly reached fewer clients than it claims is worse than one that says so.
    /// </summary>
    public int Broadcast(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        McpSession[] snapshot;
        lock (gate)
        {
            snapshot = [.. sessions.Values];
        }

        int delivered = 0;
        foreach (McpSession session in snapshot)
        {
            if (session.TryPost(message))
            {
                delivered++;
            }
        }

        return delivered;
    }
}
