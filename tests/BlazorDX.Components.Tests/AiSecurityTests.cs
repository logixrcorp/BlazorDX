using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlazorDX.Primitives.Diagnostics;
using BlazorDX.Primitives.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>A model with fields a human may edit but the AI must never see or set.</summary>
[DxFormModel(Name = "update_profile", Description = "Update a user profile.")]
public sealed class ProfileUpdate
{
    [DxField("Display name", Required = true)]
    public string Name { get; set; } = string.Empty;

    [DxField("SSN", Sensitive = true, Description = "Social security number")]
    public string Ssn { get; set; } = string.Empty;

    [DxField("API key")]
    [AiHidden]
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Security of the AI surface: sensitive-field redaction (schema + ApplyArguments),
/// authorization gating (hide + deny without leaking existence), audit via IDxDiagnostics,
/// and cancellation reaching the tool handler.
/// </summary>
public sealed class AiSecurityTests
{
    private static readonly ProfileUpdateFormModel Profile = new();

    private static FormFieldInfo Field(string name) => Profile.Fields.First(f => f.Name == name);

    private sealed class PredicateAuthorizer(System.Func<IAiTool, bool> allow) : IAiToolAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(IAiTool tool, CancellationToken ct) => new(allow(tool));
    }

    private static FormAiTool<MeetingRequest> MeetingTool() =>
        new(new MeetingRequestFormModel(), () => new MeetingRequest(), (m, ct) => Task.FromResult("done"));

    // ---- Sensitive-field redaction ----

    [Fact]
    public void Sensitive_fields_are_human_editable_but_hidden_from_the_schema()
    {
        // All three are real fields (a human sees/edits them in DxForm)...
        Assert.Equal(3, Profile.Fields.Count);
        Assert.True(Field("Ssn").Sensitive);          // [DxField(Sensitive = true)]
        Assert.True(Field("ApiKey").Sensitive);       // [AiHidden]
        Assert.False(Field("Name").Sensitive);

        // ...but the AI tool schema only exposes the non-sensitive ones.
        using JsonDocument schema = JsonDocument.Parse(FormTool.BuildInputSchema(Profile));
        JsonElement props = schema.RootElement.GetProperty("properties");
        Assert.True(props.TryGetProperty("Name", out _));
        Assert.False(props.TryGetProperty("Ssn", out _));
        Assert.False(props.TryGetProperty("ApiKey", out _));
    }

    [Fact]
    public void ApplyArguments_refuses_to_set_a_sensitive_field_even_when_supplied()
    {
        ProfileUpdate target = new();
        // A malicious / confused AI sends values for the hidden fields anyway.
        var errors = FormTool.ApplyArguments(Profile, target,
            """{ "Name": "Alice", "Ssn": "123-45-6789", "ApiKey": "leaked-secret" }""");

        Assert.Empty(errors);
        Assert.Equal("Alice", target.Name);       // the allowed field is set
        Assert.Equal(string.Empty, target.Ssn);    // the sensitive fields are NOT
        Assert.Equal(string.Empty, target.ApiKey);
    }

    // ---- Authorization ----

    [Fact]
    public async Task Unauthorized_tools_are_hidden_from_tools_list()
    {
        McpToolServer server = new McpToolServer { Authorizer = new PredicateAuthorizer(_ => false) }
            .Add(MeetingTool());

        string res = await server.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        using JsonDocument doc = JsonDocument.Parse(res);
        Assert.Empty(doc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray());
    }

    [Fact]
    public async Task Unauthorized_tools_call_is_indistinguishable_from_an_unknown_tool()
    {
        McpToolServer server = new McpToolServer { Authorizer = new PredicateAuthorizer(_ => false) }
            .Add(MeetingTool());

        string res = await server.HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"schedule_meeting","arguments":{}}}""");
        using JsonDocument doc = JsonDocument.Parse(res);
        JsonElement result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Unknown tool", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Authorized_tools_still_list_and_run()
    {
        McpToolServer server = new McpToolServer { Authorizer = new PredicateAuthorizer(_ => true) }
            .Add(MeetingTool());

        string list = await server.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        Assert.Contains("schedule_meeting", list);
    }

    // ---- Audit ----

    [Fact]
    public async Task Tool_calls_are_audited_to_diagnostics()
    {
        List<DiagnosticEvent> events = new();
        McpToolServer server = new McpToolServer { Diagnostics = new DelegateDxDiagnostics(events.Add) }
            .Add(MeetingTool());

        await server.HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"schedule_meeting","arguments":{"Title":"Sync","Email":"a@b.co","Attendees":2}}}""");

        Assert.Contains(events, e => e.Source == "BlazorDX.Mcp" && e.Message.Contains("schedule_meeting"));
    }

    // ---- Cancellation ----

    [Fact]
    public async Task Cancellation_reaches_the_tool_handler()
    {
        bool handlerObservedToken = false;
        McpToolServer server = new McpToolServer().Add(new FormAiTool<MeetingRequest>(
            new MeetingRequestFormModel(),
            () => new MeetingRequest(),
            (m, ct) =>
            {
                handlerObservedToken = ct.IsCancellationRequested;
                ct.ThrowIfCancellationRequested();
                return Task.FromResult("ran");
            }));

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() => server.HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"schedule_meeting","arguments":{"Title":"X","Email":"a@b.co","Attendees":1}}}""",
            cts.Token));
        Assert.True(handlerObservedToken);
    }
}
