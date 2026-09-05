using System.Text.Json;
using System.Threading.Tasks;
using BlazorDX.Primitives.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The MCP tool-server: a [DxFormModel] becomes a callable assistant tool. Exercises
/// tools/list (schema), tools/call (validate → run handler), and JSON-RPC framing.
/// </summary>
public sealed class McpToolServerTests
{
    private string? scheduledTitle;

    private McpToolServer NewServer() => new McpToolServer { ServerName = "BlazorDX Test" }
        .Add(new FormAiTool<MeetingRequest>(
            new MeetingRequestFormModel(),
            () => new MeetingRequest(),
            (m, ct) =>
            {
                scheduledTitle = m.Title;
                return Task.FromResult($"Scheduled '{m.Title}' with {m.Attendees} attendee(s).");
            }));

    [Fact]
    public async Task Initialize_advertises_the_server()
    {
        string res = await NewServer().HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        using JsonDocument doc = JsonDocument.Parse(res);

        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("BlazorDX Test", doc.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Initialize_echoes_a_protocol_revision_it_actually_speaks()
    {
        string res = await NewServer().HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""");

        using JsonDocument doc = JsonDocument.Parse(res);
        Assert.Equal("2024-11-05", doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task An_unknown_client_revision_is_answered_with_our_newest()
    {
        // Not an error: naming what we speak lets the client decide whether it can proceed.
        string res = await NewServer().HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");

        using JsonDocument doc = JsonDocument.Parse(res);
        Assert.Equal("2025-03-26", doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task The_tool_list_changed_capability_is_off_unless_something_can_send_it()
    {
        string res = await NewServer().HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        using JsonDocument doc = JsonDocument.Parse(res);
        JsonElement tools = doc.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("tools");

        // Advertising it without delivering is worse than not advertising: the client stops
        // re-listing, waits for a notice that never comes, and works from a stale tool list with
        // no error anywhere to explain why.
        Assert.False(tools.TryGetProperty("listChanged", out _));

        McpToolServer promising = new() { ServerName = "x", NotifiesToolListChanged = true };
        using JsonDocument opted = JsonDocument.Parse(
            await promising.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize"}"""));
        Assert.True(opted.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean());
    }

    [Fact]
    public async Task A_batched_request_is_refused_rather_than_taking_the_transport_down()
    {
        // TryGetProperty on a JSON array throws InvalidOperationException, not JsonException, so
        // a batch body used to escape the parse-error catch entirely. Batching left the protocol
        // in 2025-06-18; refusing it as an Invalid Request is both correct and actionable.
        string res = await NewServer().HandleAsync(
            """[{"jsonrpc":"2.0","id":1,"method":"initialize"}]""");

        using JsonDocument doc = JsonDocument.Parse(res);
        Assert.Equal(-32600, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Notifications_are_told_apart_from_requests()
    {
        // Both hosts route on this, so it is worth asserting directly rather than only through
        // the transports that consume it.
        Assert.True(McpToolServer.ExpectsResponse("""{"jsonrpc":"2.0","id":1,"method":"initialize"}"""));
        Assert.False(McpToolServer.ExpectsResponse("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));

        // Malformed input still gets the parse error, which is the difference between a client
        // that reports a bad request and one that hangs waiting.
        Assert.True(McpToolServer.ExpectsResponse("{not json"));

        Assert.True(McpToolServer.IsInitialize("""{"jsonrpc":"2.0","id":1,"method":"initialize"}"""));
        Assert.False(McpToolServer.IsInitialize("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""));
        Assert.False(McpToolServer.IsInitialize("{not json"));
    }

    [Fact]
    public async Task Tools_list_returns_the_generated_schema()
    {
        string res = await NewServer().HandleAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        using JsonDocument doc = JsonDocument.Parse(res);

        Assert.Equal(2, doc.RootElement.GetProperty("id").GetInt32());
        JsonElement tool = doc.RootElement.GetProperty("result").GetProperty("tools")[0];
        Assert.Equal("schedule_meeting", tool.GetProperty("name").GetString());

        JsonElement schema = tool.GetProperty("inputSchema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("integer", schema.GetProperty("properties").GetProperty("Attendees").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Tools_call_validates_runs_the_handler_and_returns_text()
    {
        const string request = """
            {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{
              "name":"schedule_meeting",
              "arguments":{"Title":"Sprint sync","Attendees":4,"Email":"lead@team.io"}}}
            """;

        string res = await NewServer().HandleAsync(request);
        using JsonDocument doc = JsonDocument.Parse(res);
        JsonElement result = doc.RootElement.GetProperty("result");

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Sprint sync", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("Sprint sync", scheduledTitle);   // the handler actually ran
    }

    [Fact]
    public async Task Tools_call_with_invalid_arguments_returns_an_error_result()
    {
        const string request = """
            {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{
              "name":"schedule_meeting","arguments":{"Attendees":0}}}
            """;

        string res = await NewServer().HandleAsync(request);
        using JsonDocument doc = JsonDocument.Parse(res);
        JsonElement result = doc.RootElement.GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Validation failed", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Null(scheduledTitle);   // handler did not run
    }

    [Fact]
    public async Task Unknown_method_yields_a_json_rpc_error()
    {
        string res = await NewServer().HandleAsync("""{"jsonrpc":"2.0","id":5,"method":"frobnicate"}""");
        using JsonDocument doc = JsonDocument.Parse(res);

        Assert.Equal(-32601, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Stdio_host_answers_requests_and_stays_silent_on_notifications()
    {
        // What a local assistant (e.g. Claude Desktop) sends over stdin: an initialize
        // request, the standard initialized *notification* (no id), then a tools/list request.
        string input = string.Join("\n",
            """{"jsonrpc":"2.0","id":1,"method":"initialize"}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        System.IO.StringWriter output = new();
        await McpStdioHost.RunAsync(NewServer(), new System.IO.StringReader(input), output);

        string[] lines = output.ToString().Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);                 // a reply per request, none for the notification
        Assert.Contains("\"id\":1", lines[0]);          // initialize
        Assert.Contains("\"id\":2", lines[1]);          // tools/list
        Assert.Contains("schedule_meeting", lines[1]);  // the tool is advertised
    }
}
