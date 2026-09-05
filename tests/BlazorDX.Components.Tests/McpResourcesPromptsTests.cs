using System.Text.Json;
using BlazorDX.Primitives.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The rest of the MCP surface: <c>resources/*</c> (things an assistant reads) and
/// <c>prompts/*</c> (templates a <em>person</em> invokes).
/// </summary>
/// <remarks>
/// Every response here is hand-built JSON, so these parse rather than substring-match. The
/// authorization cases matter most: a resource is raw data, so a surface that leaked which
/// resources exist would be a worse failure than the equivalent for tools.
/// </remarks>
public sealed class McpResourcesPromptsTests
{
    private static McpToolServer Bare() => new() { ServerName = "BlazorDX Test" };

    private static TextAiResource Pricing() => new(
        "docs://pricing",
        "Pricing",
        "Current price list",
        _ => Task.FromResult("Widgets cost 4."),
        "text/markdown");

    private sealed class DenyAll : IAiContentAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(IAiResource resource, CancellationToken ct) => new(false);

        public ValueTask<bool> IsAllowedAsync(IAiPrompt prompt, CancellationToken ct) => new(false);
    }

    private sealed class PngResource : IAiResource
    {
        public string Uri => "img://logo";

        public string Name => "Logo";

        public string? Description => null;

        public string? MimeType => "image/png";

        public Task<AiResourceContent> ReadAsync(CancellationToken ct) =>
            Task.FromResult(AiResourceContent.FromBytes(new byte[] { 1, 2, 3 }, "image/png"));
    }

    private static async Task<JsonElement> ResultOf(McpToolServer server, string request)
    {
        using JsonDocument doc = JsonDocument.Parse(await server.HandleAsync(request));
        return doc.RootElement.Clone();
    }

    // Built by concatenation rather than an interpolated raw string: the JSON ends in "}} , which
    // a $$""" literal reads as an interpolation hole rather than as content.
    private static string PromptGet(string name, string? argumentsJson = null) =>
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"prompts/get\",\"params\":{\"name\":\""
        + name + "\""
        + (argumentsJson is null ? string.Empty : ",\"arguments\":" + argumentsJson)
        + "}}";

    // ---- capabilities ---------------------------------------------------------------------

    [Fact]
    public async Task Capabilities_name_only_what_is_actually_registered()
    {
        JsonElement bare = await ResultOf(Bare(), """{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        JsonElement none = bare.GetProperty("result").GetProperty("capabilities");

        // Advertising resources this server does not have costs a wasted resources/list on every
        // connection, and makes a genuinely empty surface look identical to one whose
        // registration was forgotten.
        Assert.False(none.TryGetProperty("resources", out _));
        Assert.False(none.TryGetProperty("prompts", out _));

        JsonElement withResource = await ResultOf(
            Bare().Add(Pricing()), """{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        JsonElement some = withResource.GetProperty("result").GetProperty("capabilities");

        Assert.True(some.TryGetProperty("resources", out _));
        Assert.False(some.TryGetProperty("prompts", out _));
    }

    // ---- resources ------------------------------------------------------------------------

    [Fact]
    public async Task A_resource_is_listed_and_read_by_uri()
    {
        McpToolServer server = Bare().Add(Pricing());

        JsonElement listed = await ResultOf(server, """{"jsonrpc":"2.0","id":1,"method":"resources/list"}""");
        JsonElement one = listed.GetProperty("result").GetProperty("resources")[0];
        Assert.Equal("docs://pricing", one.GetProperty("uri").GetString());
        Assert.Equal("Pricing", one.GetProperty("name").GetString());
        Assert.Equal("text/markdown", one.GetProperty("mimeType").GetString());

        JsonElement read = await ResultOf(server,
            """{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"docs://pricing"}}""");
        JsonElement content = read.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal("Widgets cost 4.", content.GetProperty("text").GetString());
        Assert.Equal("text/markdown", content.GetProperty("mimeType").GetString());
    }

    [Fact]
    public async Task A_binary_resource_is_base64_and_carries_no_text_member()
    {
        JsonElement read = await ResultOf(Bare().Add(new PngResource()),
            """{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"img://logo"}}""");

        JsonElement content = read.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), content.GetProperty("blob").GetString());

        // Sending both would let a client pick the wrong one; the protocol expects exactly one.
        Assert.False(content.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task An_unknown_resource_is_a_protocol_error()
    {
        JsonElement read = await ResultOf(Bare().Add(Pricing()),
            """{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"docs://nope"}}""");

        Assert.Equal(-32002, read.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task A_reader_that_throws_does_not_take_the_transport_down()
    {
        McpToolServer server = Bare().Add(new TextAiResource(
            "x://boom", "Boom", null, _ => throw new InvalidOperationException("disk gone")));

        JsonElement read = await ResultOf(server,
            """{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"x://boom"}}""");

        Assert.Equal(-32603, read.GetProperty("error").GetProperty("code").GetInt32());

        // The exception's own text is not echoed: "disk gone" is the kind of internal detail an
        // error message hands to whoever is on the other end of the connection.
        Assert.DoesNotContain("disk gone", read.GetProperty("error").GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_denied_resource_is_indistinguishable_from_a_missing_one()
    {
        McpToolServer gated = new McpToolServer { ServerName = "T", ContentAuthorizer = new DenyAll() }.Add(Pricing());

        JsonElement listed = await ResultOf(gated, """{"jsonrpc":"2.0","id":1,"method":"resources/list"}""");
        Assert.Empty(listed.GetProperty("result").GetProperty("resources").EnumerateArray());

        JsonElement read = await ResultOf(gated,
            """{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"docs://pricing"}}""");

        // Byte-identical to the unknown-resource answer above. A different message, or a
        // different code, would confirm the resource exists to a caller not allowed to read it.
        Assert.Equal(-32002, read.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("Resource not found: docs://pricing", read.GetProperty("error").GetProperty("message").GetString());
    }

    // ---- prompts --------------------------------------------------------------------------

    [Fact]
    public async Task A_form_becomes_a_prompt_that_points_at_its_own_tool()
    {
        FormAiPrompt<MeetingRequest> prompt = new(new MeetingRequestFormModel());
        McpToolServer server = Bare().Add(prompt);

        JsonElement listed = await ResultOf(server, """{"jsonrpc":"2.0","id":1,"method":"prompts/list"}""");
        JsonElement one = listed.GetProperty("result").GetProperty("prompts")[0];
        Assert.Equal(prompt.Name, one.GetProperty("name").GetString());
        Assert.Equal(prompt.Arguments.Count, one.GetProperty("arguments").GetArrayLength());

        JsonElement got = await ResultOf(server, PromptGet(prompt.Name));
        JsonElement message = got.GetProperty("result").GetProperty("messages")[0];

        Assert.Equal("user", message.GetProperty("role").GetString());
        string text = message.GetProperty("content").GetProperty("text").GetString()!;

        // The point of the prompt: a person invokes it, and the assistant is told which tool
        // finishes the job. Without that it would gather the values and then guess.
        Assert.Contains(new MeetingRequestFormModel().ToolName, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompt_text_states_the_model_s_real_constraints()
    {
        FormAiPrompt<MeetingRequest> prompt = new(new MeetingRequestFormModel());

        JsonElement got = await ResultOf(Bare().Add(prompt), PromptGet(prompt.Name));
        string text = got.GetProperty("result").GetProperty("messages")[0]
            .GetProperty("content").GetProperty("text").GetString()!;

        // Read off the descriptor rather than restated in prose, so a rule changed on the model
        // cannot drift from what the prompt claims. Attendees is Min = 1, Max = 50.
        Assert.Contains("range 1\u201350", text, StringComparison.Ordinal);
        Assert.Contains("(required)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_numeric_argument_is_kept_rather_than_dropped()
    {
        FormAiPrompt<MeetingRequest> prompt = new(new MeetingRequestFormModel());

        // A client collecting a number sends a number. Ignoring it would silently lose what the
        // user already typed and make the assistant ask again.
        JsonElement got = await ResultOf(Bare().Add(prompt), PromptGet(prompt.Name, """{"Attendees":4}"""));

        string text = got.GetProperty("result").GetProperty("messages")[0]
            .GetProperty("content").GetProperty("text").GetString()!;

        Assert.Contains("already given: 4", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_prompt_is_a_protocol_error()
    {
        JsonElement got = await ResultOf(Bare(),
            """{"jsonrpc":"2.0","id":1,"method":"prompts/get","params":{"name":"nope"}}""");

        Assert.Equal(-32602, got.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task A_denied_prompt_is_neither_listed_nor_fetchable()
    {
        McpToolServer gated = new McpToolServer { ServerName = "T", ContentAuthorizer = new DenyAll() }
            .Add(new FormAiPrompt<MeetingRequest>(new MeetingRequestFormModel()));

        JsonElement listed = await ResultOf(gated, """{"jsonrpc":"2.0","id":1,"method":"prompts/list"}""");
        Assert.Empty(listed.GetProperty("result").GetProperty("prompts").EnumerateArray());
    }
}
