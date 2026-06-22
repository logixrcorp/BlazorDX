# AI & MCP integration

BlazorDX treats AI as a first-class consumer, not an afterthought. The same annotated model a
person fills as a form is the tool an assistant calls — one source of truth, rendered as UI
**and** projected as a tool, with the same validation rules on both. This guide covers the
whole surface and, just as importantly, **how it is secured**.

> **Why this matters.** As assistants shift from "download an app" to "call a tool", a
> component library that can expose its forms and data as governed, validated tools — without
> reflection and without opening an injection hole — is a different kind of building block.

## The one-model story

```csharp
[DxFormModel(Name = "schedule_meeting", Description = "Schedule a meeting with a teammate.")]
public sealed class MeetingRequest
{
    [DxField("Title", Required = true, Description = "Meeting title.")]
    public string Title { get; set; } = "";

    [DxField("Attendees", Min = 1, Max = 50, Description = "Number of attendees.")]
    public int Attendees { get; set; } = 1;
}
```

At build time `BlazorDX.SourceGen` emits `MeetingRequestFormModel` — an `IFormModel<MeetingRequest>`
with field metadata, typed get/set, and validation, **all source-generated, zero reflection**.
That one descriptor:

- renders as a form via `DxForm` (see the Forms guide), and
- projects as an AI tool via `FormTool` / `FormAiTool<T>`.

DataAnnotations models work too: a model annotated only with `[Required]`, `[Range]`,
`[StringLength]`, `[EmailAddress]`, `[Display]`, and `IValidatableObject` becomes a tool by
adding the single class-level `[DxFormModel]`.

## Projecting a model to a tool

```csharp
IFormModel<MeetingRequest> model = new MeetingRequestFormModel();

string schema = FormTool.BuildInputSchema(model);      // JSON-Schema "object" of the arguments
string toolDef = FormTool.BuildToolDefinition(model);  // { name, description, input_schema }
```

The schema is the shape shared by the **Model Context Protocol** and Anthropic function-calling
(OpenAI nests the same schema under `parameters`). Applying a tool call's arguments runs the
**same validation the form uses**:

```csharp
var target = new MeetingRequest();
IReadOnlyList<FormValidationError> errors =
    FormTool.ApplyArguments(model, target, """{ "Title": "Q3 planning", "Attendees": 8 }""");
```

`ApplyArguments` is the trust boundary: arguments are applied through **generated typed setters**
(never reflection, never `eval`), and the **JSON-Schema is an allowlist** — an AI can only set
properties the model exposes, never an arbitrary field.

## Serving tools over MCP

`McpToolServer` is a transport-agnostic [Model Context Protocol](https://modelcontextprotocol.io)
server. Register `IAiTool`s and feed it JSON-RPC; it answers `initialize`, `tools/list`, and
`tools/call`.

```csharp
var server = new McpToolServer { ServerName = "My app" }
    .Add(new FormAiTool<MeetingRequest>(
        new MeetingRequestFormModel(),
        () => new MeetingRequest(),
        (meeting, ct) => Task.FromResult($"Scheduled \"{meeting.Title}\".")));

string response = await server.HandleAsync(jsonRpcRequest, cancellationToken);
```

### Transports

| Transport | How | Status |
|---|---|---|
| **stdio** | `McpStdioHost.RunAsync(server, Console.In, Console.Out, ct)` — newline-delimited JSON-RPC. How local assistants (e.g. Claude Desktop) connect. | ✅ Built — see [`samples/BlazorDX.McpServer`](../samples/BlazorDX.McpServer) |
| **HTTP** | A web agent POSTs a JSON-RPC message and gets the JSON-RPC response — the request/response subset of MCP Streamable HTTP. Enough for request/response tools. | ✅ Built — `/mcp` in the demo |
| **HTTP + SSE / sessions** | Server-initiated streaming (progress, sampling) over Server-Sent Events with session ids. | Planned |

A complete, runnable stdio server lives in [`samples/BlazorDX.McpServer`](../samples/BlazorDX.McpServer/README.md),
with the exact Claude Desktop config. The HTTP transport is ~10 lines of standard ASP.NET Core,
since the server is transport-agnostic — the host owns the transport:

```csharp
app.MapPost("/mcp", async (HttpContext http) =>
{
    using StreamReader reader = new(http.Request.Body);
    string body = await reader.ReadToEndAsync(http.RequestAborted);

    McpToolServer mcp = new McpToolServer
    {
        Authorizer = new MyAuthorizer(http.User),   // gate per caller (see Security below)
        Diagnostics = http.RequestServices.GetService<IDxDiagnostics>(),
    }.Add(/* your tools */);

    string response = await mcp.HandleAsync(body, http.RequestAborted);
    return Results.Content(response, "application/json");
}).RequireAuthorization();   // HTTPS + auth in production
```

## Security model

Opening a system to AI is the highest-risk surface, so it inherits BlazorDX's security
discipline rather than running wide open.

### Authorization — tools run as the *caller*, not ambient

Supply an `IAiToolAuthorizer` and `McpToolServer` consults it on **every** `tools/list` and
`tools/call`. A tool the caller may not use is **never advertised**, and an unauthorized call is
**indistinguishable from an unknown tool** — the server never reveals that a privileged tool
exists.

```csharp
var server = new McpToolServer
{
    Authorizer = new MyClaimsAuthorizer(currentUser),   // gate by your own policy
    Diagnostics = diagnostics,                            // audit sink (below)
};
```

When no authorizer is set, all tools are allowed (suitable for a trusted local stdio host).

### Audit — a trail of what the AI did

Set `Diagnostics` to any `IDxDiagnostics` sink (the same observability layer the rest of the
library uses). Every call — ok, error, denied, or cancelled — is reported, giving you an audit
log via your existing `ILogger` / OpenTelemetry wiring.

### Cancellation — agent loops are interruptible

`IAiTool.InvokeAsync` and the `FormAiTool` handler take a `CancellationToken`, threaded from
`HandleAsync`. A handler that throws is contained into an error result rather than crashing the
transport, so the AI sees a clean error it can react to.

### Sensitive-field redaction — what the AI must never see

Mark a field human-editable but invisible to AI with `[DxField(Sensitive = true)]` or the
standalone `[AiHidden]`:

```csharp
[DxField("Display name", Required = true)] public string Name { get; set; } = "";
[DxField("SSN", Sensitive = true)]         public string Ssn  { get; set; } = "";   // never to AI
[AiHidden] [DxField("API key")]            public string Key  { get; set; } = "";   // never to AI
```

A sensitive field is **excluded from the generated schema** (the AI is never told it exists)
**and refused by `ApplyArguments`** (the hard gate — AI arguments can never set it), yet a human
still sees and edits it in `DxForm`. Use it for PII and secrets.

### Input validation is the boundary

Because arguments flow through the source-generated `Validate` and typed setters, the model's own
rules (`Required`, `Min`/`Max`, `MaxLength`, `Pattern`, `IValidatableObject`) reject bad or
malicious tool calls — and the errors are returned to the AI so it can self-correct.

## What's next

The secured core and stdio transport are in place. Planned: the HTTP/SSE transport, exposing the
DataGrid as a read tool over `IGridDataSource`, and the broader MCP surface (resources, prompts).
See [ROADMAP.md](ROADMAP.md).
