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
}
