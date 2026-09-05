using System.Text.Json;
using BlazorDX.Primitives.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The MCP HTTP transport: <c>POST</c> for request/response, <c>GET</c> for the server-to-client
/// SSE stream, <c>DELETE</c> to end a session.
/// </summary>
/// <remarks>
/// <see cref="McpHttpHost"/> speaks in strings and status codes rather than <c>HttpContext</c>,
/// because <c>BlazorDX.Primitives</c> is browser-WASM safe and cannot take an ASP.NET dependency.
/// The compensation is here: the transport rules are testable without a web server, so every
/// status code below is asserted rather than hoped for.
/// </remarks>
public sealed class McpHttpHostTests
{
    // A settable clock, so session expiry is a property of the test rather than of how long the
    // test happened to take. The suite already has two wall-clock flakes; this is the pattern
    // that avoids adding a third.
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static McpToolServer NewServer() => new() { ServerName = "BlazorDX Test" };

    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize"}""";
    private const string ToolsList = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";
    private const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    // ---- SSE framing -------------------------------------------------------------------------

    [Fact]
    public void A_single_line_message_is_one_data_frame()
    {
        Assert.Equal("data: {\"a\":1}\n\n", McpSse.Frame("""{"a":1}"""));
    }

    [Fact]
    public void Every_line_of_a_multi_line_message_gets_its_own_data_prefix()
    {
        // The failure this prevents: a bare newline inside the payload ends the frame early, so
        // the client sees a truncated event and then treats the remainder as garbage. Any tool
        // returning multi-line text produces exactly that payload.
        Assert.Equal("data: one\ndata: two\n\n", McpSse.Frame("one\ntwo"));
    }

    [Fact]
    public void A_frame_ends_with_the_blank_line_that_dispatches_it()
    {
        // Without the terminating blank line the client buffers the event forever and the stream
        // looks alive while delivering nothing.
        Assert.EndsWith("\n\n", McpSse.Frame("x"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","method":"x"}""")]
    [InlineData("one\ntwo")]
    [InlineData("trailing\n")]
    [InlineData("")]
    [InlineData("  leading spaces")]
    [InlineData("blank\n\nline")]
    public void A_framed_message_is_recovered_exactly_by_a_client(string payload)
    {
        // The property that matters, rather than the bytes: leading spaces survive (the "data: "
        // prefix strips exactly one space and Frame adds exactly one), as do empty lines and a
        // trailing newline. Byte-pinning the frame would pass while any of those quietly broke.
        Assert.Equal(payload, ParseSseData(McpSse.Frame(payload)));
    }

    // What a conforming client does: join the data lines with "\n", strip one trailing "\n".
    private static string ParseSseData(string frame)
    {
        System.Text.StringBuilder buffer = new();
        foreach (string line in frame.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                string value = line[5..];
                buffer.Append(value.StartsWith(' ') ? value[1..] : value).Append('\n');
            }
        }

        return buffer.Length > 0 ? buffer.ToString()[..^1] : string.Empty;
    }

    // ---- Sessions ----------------------------------------------------------------------------

    [Fact]
    public void Session_ids_are_unique_and_long_enough_to_be_unguessable()
    {
        McpSessionStore store = new();

        HashSet<string> ids = [.. Enumerable.Range(0, 50).Select(_ => store.Create().Id)];

        // The id is the only thing separating one caller's session from another's, so it is a
        // credential: 128 bits of RandomNumberGenerator, not a counter and not a Guid.
        Assert.Equal(50, ids.Count);
        Assert.All(ids, id => Assert.Equal(32, id.Length));
    }

    [Fact]
    public void A_terminated_session_is_no_longer_found()
    {
        McpSessionStore store = new();
        McpSession session = store.Create();

        Assert.True(store.Terminate(session.Id));
        Assert.False(store.TryGet(session.Id, out _));

        // A second DELETE is a client retrying, not an error.
        Assert.False(store.Terminate(session.Id));
    }

    [Fact]
    public async Task Terminating_a_session_ends_the_stream_holding_it_open()
    {
        McpSessionStore store = new();
        McpSession session = store.Create();

        store.Terminate(session.Id);

        // Without this the streaming response never completes and the request leaks a connection.
        IAsyncEnumerator<string> stream = session.ReadAllAsync().GetAsyncEnumerator();
        Assert.False(await stream.MoveNextAsync());
    }

    [Fact]
    public void An_idle_session_is_swept_and_an_active_one_is_not()
    {
        TestClock clock = new(DateTimeOffset.UnixEpoch);
        McpSessionStore store = new() { TimeProvider = clock };

        McpSession stale = store.Create();
        clock.Now = clock.Now.AddMinutes(10);
        McpSession fresh = store.Create();

        // A client can vanish without a DELETE — a closed tab, a crash, a partition — so without
        // a sweep the store only ever grows.
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal(1, store.Sweep(TimeSpan.FromMinutes(5)));

        Assert.False(store.TryGet(stale.Id, out _));
        Assert.True(store.TryGet(fresh.Id, out _));
    }

    [Fact]
    public void A_request_keeps_its_session_alive()
    {
        TestClock clock = new(DateTimeOffset.UnixEpoch);
        McpSessionStore store = new() { TimeProvider = clock };
        McpSession session = store.Create();

        clock.Now = clock.Now.AddMinutes(4);
        Assert.True(store.TryGet(session.Id, out _));   // touches LastSeen

        clock.Now = clock.Now.AddMinutes(4);
        Assert.Equal(0, store.Sweep(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void A_full_queue_drops_the_message_and_says_so()
    {
        McpSessionStore store = new() { QueueCapacity = 2 };
        McpSession session = store.Create();

        Assert.True(session.TryPost("one"));
        Assert.True(session.TryPost("two"));

        // A client that opened a session and never drained it would otherwise grow the server's
        // memory without limit, and a disconnected client is the case that actually happens.
        Assert.False(session.TryPost("three"));
    }

    [Fact]
    public void Broadcast_counts_what_was_delivered_not_what_was_attempted()
    {
        McpSessionStore store = new() { QueueCapacity = 1 };
        store.Create();
        McpSession full = store.Create();
        full.TryPost("already here");

        // Reporting 2 here would claim a delivery that did not happen.
        Assert.Equal(1, store.Broadcast(McpToolServer.ToolListChangedNotification));
    }

    // ---- Transport rules ---------------------------------------------------------------------

    [Fact]
    public async Task Initialize_mints_a_session_and_later_requests_must_carry_it()
    {
        McpSessionStore store = new();
        McpHttpHost host = new(NewServer(), store);

        McpHttpResponse init = await host.HandleAsync(new McpHttpRequest("POST", Initialize));

        Assert.Equal(200, init.StatusCode);
        Assert.NotNull(init.SessionId);

        McpHttpResponse listed = await host.HandleAsync(new McpHttpRequest("POST", ToolsList, init.SessionId));
        Assert.Equal(200, listed.StatusCode);
    }

    [Fact]
    public async Task An_expired_session_gets_404_so_the_client_initializes_again()
    {
        McpHttpHost host = new(NewServer(), new McpSessionStore());

        McpHttpResponse response = await host.HandleAsync(new McpHttpRequest("POST", ToolsList, "deadbeef"));

        // 404, not 401: the caller is not unauthorized, its session is gone. The protocol's
        // meaning of 404 here is "initialize again", which is what it should do — 401 would send
        // it to fetch credentials it already has.
        Assert.Equal(404, response.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(response.Body!);
        Assert.Equal(-32001, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Without_a_session_store_the_endpoint_is_plain_request_response()
    {
        McpHttpHost host = new(NewServer());

        McpHttpResponse init = await host.HandleAsync(new McpHttpRequest("POST", Initialize));
        Assert.Equal(200, init.StatusCode);
        Assert.Null(init.SessionId);

        // 405 is the protocol's way of saying "this server never initiates messages", and a
        // client that sees it simply does not open a listening stream.
        Assert.Equal(405, (await host.HandleAsync(new McpHttpRequest("GET"))).StatusCode);
    }

    [Fact]
    public async Task A_notification_is_accepted_with_no_body()
    {
        McpHttpHost host = new(NewServer());

        McpHttpResponse response = await host.HandleAsync(new McpHttpRequest("POST", Initialized));

        // JSON-RPC forbids answering a notification, so there is nothing to return but an
        // acknowledgement. Returning a response body here is a protocol violation the stdio host
        // already avoided; this is the same rule, shared rather than re-derived.
        Assert.Equal(202, response.StatusCode);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task An_empty_body_is_a_protocol_error_not_a_crash()
    {
        McpHttpHost host = new(NewServer());

        McpHttpResponse response = await host.HandleAsync(new McpHttpRequest("POST", "   "));

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("-32600", response.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unsupported_method_is_405()
    {
        McpHttpHost host = new(NewServer(), new McpSessionStore());

        Assert.Equal(405, (await host.HandleAsync(new McpHttpRequest("PUT", "{}"))).StatusCode);
    }

    [Fact]
    public async Task Delete_ends_the_session()
    {
        McpSessionStore store = new();
        McpHttpHost host = new(NewServer(), store);
        McpHttpResponse init = await host.HandleAsync(new McpHttpRequest("POST", Initialize));

        McpHttpResponse deleted = await host.HandleAsync(new McpHttpRequest("DELETE", SessionId: init.SessionId));

        Assert.Equal(204, deleted.StatusCode);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_get_opens_the_stream_that_carries_server_initiated_messages()
    {
        McpSessionStore store = new();
        McpHttpHost host = new(NewServer(), store);
        McpHttpResponse init = await host.HandleAsync(new McpHttpRequest("POST", Initialize));

        McpHttpResponse stream = await host.HandleAsync(new McpHttpRequest("GET", SessionId: init.SessionId));

        Assert.Equal(200, stream.StatusCode);
        Assert.Equal("text/event-stream", stream.ContentType);
        McpSession opened = Assert.IsType<McpSession>(stream.Stream);

        // The whole point of the session: a message with no request to ride back on.
        store.Broadcast(McpToolServer.ToolListChangedNotification);

        IAsyncEnumerator<string> messages = opened.ReadAllAsync().GetAsyncEnumerator();
        Assert.True(await messages.MoveNextAsync());
        Assert.Equal(McpToolServer.ToolListChangedNotification, messages.Current);
        Assert.Equal(
            "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n",
            McpSse.Frame(messages.Current));
    }
}
