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
server. Register `IAiTool`s, `IAiResource`s and `IAiPrompt`s and feed it JSON-RPC; it answers
`initialize`, `tools/list`, `tools/call`, `resources/list`, `resources/read`, `prompts/list` and
`prompts/get`.

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
| **HTTP + SSE / sessions** | `McpHttpHost` — `POST` for request/response, `GET` for a Server-Sent Events stream of server-initiated messages, `DELETE` to end a session. | ✅ Built — `/mcp` in the demo |

A complete, runnable stdio server lives in [`samples/BlazorDX.McpServer`](../samples/BlazorDX.McpServer/README.md),
with the exact Claude Desktop config.

### Why the HTTP transport is not an ASP.NET type

`BlazorDX.Primitives` is browser-WASM safe: it references the Blazor component *packages*, not the
ASP.NET Core framework. Binding the MCP transport to `HttpContext` would cost the whole library
that guarantee for one feature. So `McpHttpHost` speaks in strings and status codes, and the host
writes a short endpoint that moves bytes across.

That trade pays twice. The transport rules — which status code an expired session gets, whether a
notification may be answered, what `GET` returns when a server cannot stream — are **decided in a
plain class and unit-tested without a web server**, which matters in a repo where the browser
tests are the slowest thing in CI.

```csharp
McpSessionStore sessions = new();   // singleton; stdio needs none, its connection is the session

app.MapMethods("/mcp", ["POST", "GET", "DELETE"], async (HttpContext http) =>
{
    string? body = null;
    if (HttpMethods.IsPost(http.Request.Method))
    {
        using StreamReader reader = new(http.Request.Body);
        body = await reader.ReadToEndAsync(http.RequestAborted);
    }

    McpToolServer mcp = new McpToolServer
    {
        Authorizer = new MyAuthorizer(http.User),   // gate per caller (see Security below)
        Diagnostics = http.RequestServices.GetService<IDxDiagnostics>(),
    }.Add(/* your tools */);

    McpHttpResponse result = await new McpHttpHost(mcp, sessions).HandleAsync(
        new McpHttpRequest(http.Request.Method, body, http.Request.Headers["Mcp-Session-Id"]),
        http.RequestAborted);

    http.Response.StatusCode = result.StatusCode;
    if (result.SessionId is not null)
    {
        http.Response.Headers["Mcp-Session-Id"] = result.SessionId;
    }

    if (result.Stream is not null)
    {
        http.Response.ContentType = result.ContentType!;
        http.Response.Headers.CacheControl = "no-cache";
        await http.Response.Body.FlushAsync(http.RequestAborted);

        await foreach (string message in result.Stream.ReadAllAsync(http.RequestAborted))
        {
            await http.Response.WriteAsync(McpSse.Frame(message), http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
        }

        return Results.Empty;
    }

    return result.Body is null
        ? Results.Empty
        : Results.Content(result.Body, result.ContentType, null, result.StatusCode);
}).RequireAuthorization();   // HTTPS + auth in production
```

### Sessions, and what they're for

Sessions exist for the **server-to-client** direction. A tool call is a request the client already
has an open response for; a progress update or a "the tool list changed" notice has no such
response to ride on, so the client opens a long-lived `GET` that streams the session's queue.

- `initialize` mints the session id and returns it as `Mcp-Session-Id`; every later request must
  carry it.
- An unknown id gets **404, not 401** — the caller is not unauthorized, its session is gone (swept,
  deleted, or the server restarted). 404's meaning here is "initialize again", which is what the
  client should do; 401 would send it to fetch credentials it already has.
- The id comes from `RandomNumberGenerator`, not `Guid`. It is the only thing separating one
  caller's session from another's, so it is a credential and has to be unguessable, not merely
  unique.
- Each session's outbound queue is **bounded and lossy on purpose**. A client that opens a session
  and never drains it would otherwise grow the server's memory without limit, and a disconnected
  client is the case that actually happens. `TryPost` returns `false` rather than blocking or
  growing, and `Broadcast` returns how many sessions *accepted* the message rather than how many
  exist — a broadcast that quietly reached fewer clients than it claims is worse than one that
  says so.
- Call `Sweep(idleTimeout)` on a timer. A client can vanish without a `DELETE`, so without a sweep
  the store only grows.

Server-initiated notifications are opt-in:

```csharp
McpToolServer mcp = new() { NotifiesToolListChanged = true };   // a promise
sessions.Broadcast(McpToolServer.ToolListChangedNotification);  // that you then keep
```

`NotifiesToolListChanged` is off by default and should stay off unless something actually
broadcasts. Advertising the capability without delivering is worse than not advertising it: the
client stops re-listing, waits for a notice that never comes, and works from a stale tool list
forever with no error anywhere to explain why.

## Reading: the grid as a tool

A form is how an assistant *acts*. `GridAiTool<TRow>` is how it *looks something up* — it exposes
an `IGridDataSource<TRow>` (the same server-side source `DxDataGrid` binds to for paging, sorting
and filtering) as a read-only tool:

```csharp
server.Add(new GridAiTool<StockRow>(
    "query_stock",
    "Warehouse stock levels. Filter by SKU, product name or warehouse; sort by any column.",
    new StockRowGridAccessor(),   // source-generated
    new StockDataSource(),        // the same IGridDataSource the grid binds to
    maxRows: 25));
```

The pair is the point. A write-only tool surface makes an assistant guess at its arguments; a
read-only one leaves it narrating instead of acting. Together they close the loop, and neither is
a bespoke AI endpoint — both are declarations the UI already uses.

### The columns are the contract

Schema, filtering, sorting and output all come from the generated `IGridRowAccessor<TRow>`, so the
tool reaches exactly the properties marked `[GridColumn]` and nothing else. A property without the
attribute appears in neither the grid nor the tool, which means **narrowing what an assistant may
read is the same edit as narrowing what the grid shows** — not a second permission model kept in
sync by hand. It also inherits [ADR 0002](adr/0002-zero-reflection-identity.md): there is no
reflection, so no unlisted property can be reached at runtime.

Because the column set is known at compile time, the schema names the columns as a JSON-Schema
`enum` rather than asking for a free-text field name. A model cannot invent a column, and a wrong
guess is a schema violation the host rejects rather than a query that quietly matches nothing. A
guess that arrives anyway is answered with an error naming the real columns, so the model can
correct itself instead of retrying blind.

### Honest partial answers

`maxRows` caps what one call can return, and `take` is **clamped rather than rejected** — a model
asking for 1000 rows wants as many as it can have. What stops it reporting a page as if it were
the whole answer is that the reply always carries the unclamped `totalCount`:

```json
{"totalCount":4210,"skip":0,"take":25,"rows":[…]}
```

Numeric columns are emitted as JSON numbers rather than display text, so a model can total or
compare a column without re-parsing what it was just handed.

### Always read-only

`IGridDataSource` has no write path and this tool adds none, so `IsReadOnly` is hard-coded rather
than a constructor flag — it cannot be set wrongly, and it reaches the client as
`annotations.readOnlyHint` in `tools/list`. A host that wants a writable grid tool has to write
one deliberately.

The corollary belongs with the security model below: the data source is reached with the
*server's* permissions, so an `IAiToolAuthorizer` is what stops an assistant reading rows its user
could not have seen in the grid. A read tool is what makes that gate load-bearing rather than
merely advisable.

## Resources and prompts — the other two surfaces

A tool is chosen by the *model*. The rest of MCP is about the other two directions, and the
difference is who decides:

| | Chosen by | Runs anything? | BlazorDX type |
|---|---|---|---|
| **Tool** | the model, mid-task | yes | `FormAiTool<T>`, `GridAiTool<T>` |
| **Resource** | the user, or on request | no — it is fetched | `TextAiResource`, or your `IAiResource` |
| **Prompt** | the **user**, before the task | no — it expands into the chat | `FormAiPrompt<T>`, `TextAiPrompt` |

Both are registered the same fluent way, and both are advertised in `initialize` **only when
something is actually registered** — telling a client this server has resources buys a wasted
`resources/list` on every connection, and makes a genuinely empty surface look identical to one
whose registration was forgotten.

```csharp
server
    .Add(new TextAiResource(
        "stock://low", "Low stock report",
        "Every SKU at or below its reorder point.",
        StockDataSource.LowStockReportAsync, "text/markdown"))
    .Add(new FormAiPrompt<MeetingRequest>(new MeetingRequestFormModel()));
```

### Resources

`IAiResource` is a URI, a name, a description and a body. There is no argument schema, because
there is nothing to parameterise — only something to fetch. `AiResourceContent.FromText` and
`FromBytes` are the two shapes; binary is base64-encoded on the wire, and exactly one of `text` or
`blob` is ever sent.

The `Uri` is an **identifier, not a path**. Nothing in BlazorDX dereferences it, so a host that
maps URIs onto files owns the traversal check — which is why `TextAiResource` takes a delegate
rather than a directory: the safe shape is a fixed set of URIs you chose.

### Prompts, and why a form makes a good one

`FormAiPrompt<T>` turns the same `[DxFormModel]` descriptor into a user-invokable template — the
third surface on one declaration. In a client it appears as a slash-command; it states the task,
lists the fields **with the constraints read off the descriptor**, and names the tool that
submits. Without it, a user has to know the tool exists and describe its fields themselves.

Reading the constraints off the descriptor rather than restating them in prose is the point: a
rule changed on the model cannot drift out of sync with what the prompt claims.

Every argument is optional, deliberately. A prompt that demanded each required field up front
would just be the form again in a worse renderer; whatever the user does not supply, the assistant
asks for.

**Sensitive fields are omitted**, exactly as they are from the tool schema — see
[Sensitive-field redaction](#sensitive-field-redaction--what-the-ai-must-never-see) below. A
prompt that helpfully listed `SSN` among the fields to collect would reintroduce the very leak the
schema avoids, while looking like a convenience feature. There is a test asserting the negative.

### Authorization applies here too

`IAiContentAuthorizer` gates resources and prompts, and a disallowed item is answered **exactly**
as a non-existent one — same error code, same message — so the surface never reveals that a
privileged resource exists. It is separate from `IAiToolAuthorizer` because adding methods to that
interface would break every host already implementing it, and "may you read this" is a different
question from "may you run this".

This matters more for resources than for tools: a resource *is* raw data, with no validation layer
or handler between the caller and the bytes.

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

### Conditional fields — a field that only applies given another field's value

Gate a field on another field's current value with `DependsOn`:

```csharp
[DxField("Priority")]                                    public Priority Priority { get; set; }
[DxField("Escalation notes", Required = true,
    DependsOn = nameof(Priority), DependsOnValue = "High")]
public string? Notes { get; set; }   // only shown, required, and AI-settable when Priority == High
```

`DependsOn` must `nameof()` another *unconditional* field on the same model — chained
conditions and referencing a `Sensitive`/`[AiHidden]` field are both compile errors (an AI can
never legally satisfy a condition on a field it's never told exists). One condition governs
visibility and requiredness together: an inactive field is hidden in `DxForm` and its
constraints (`Required` included) don't apply.

**Two enforcement layers, only one of which is guaranteed.** The generated schema expresses a
conditionally-required field with JSON Schema's `allOf`/`if`/`then` (draft-07+), plus a
plain-English clause appended to the field's own `description` regardless of whether it's
required. The `allOf` clause is **advisory** — many function-calling hosts (OpenAI, Anthropic)
implement only a subset of JSON Schema and don't guarantee they evaluate it, which is exactly
why the `description` clause exists too, as a signal every host reads. The real enforcement
boundary is `ApplyArguments` itself: it applies unconditional fields first, then re-checks each
conditional field's activity against the *now-updated* target and silently skips a
conditionally-inactive one — an AI supplying `Notes` while `Priority` isn't `High` in the same
call simply has it ignored, the same posture as a `Sensitive` field.

### Array and nested-object fields — recursive schema, one model, still one call

A `[DxField]` property whose own type carries `[DxFormModel]` becomes a nested Object field; one
typed `List<T>` becomes an Array field, `T` either another `[DxFormModel]` type (array-of-nested)
or a scalar (array-of-scalar):

```csharp
[DxFormModel(Name = "office_location")] public sealed class Address { ... }
[DxFormModel(Name = "attendee_info")]   public sealed class Attendee { ... }

[DxFormModel(Name = "schedule_meeting_with_attendees")]
public sealed class MeetingWithAttendees
{
    [DxField("Location")]           public Address Location { get; set; } = new();
    [DxField("Attendees")]          public List<Attendee> Attendees { get; set; } = new();
    [DxField("Tags")]               public List<string> Tags { get; set; } = new();
}
```

The generated JSON Schema recurses the same way the type does — no flattening, no separate
schema-only representation:

```json
{
  "properties": {
    "Location":  { "type": "object", "properties": { "Street": {...}, "City": {...} }, "required": ["Street", "City"] },
    "Attendees": { "type": "array", "items": { "type": "object", "properties": { "Name": {...}, "Email": {...} } } },
    "Tags":      { "type": "array", "items": { "type": "string" } }
  }
}
```

**`ApplyArguments` replaces the whole collection on every call.** There's no per-element identity
in a JSON payload to merge against, so the simplest correct semantic is: whatever `Attendees`
array the AI supplies *is* the new `Attendees` list, in full — not a diff, not an append. A tool
call that wants to add one attendee to an existing list of three must supply all four. Nested
validation errors carry a dotted/indexed path back to the AI — `"Location.Street"`,
`"Attendees[1].Email"` — so it can tell exactly which element and field failed.

**`DependsOn` cannot cross a nested/array field boundary, in either direction** — a scalar field
can't gate on an Object/Array field's value, and an Object/Array field can't itself be
conditional. This is a compile error (`DX2007`), not a silent gap: the conditional-field
evaluator reads a flat scalar value with no dotted-path traversal, and this pass doesn't add one.
`Sensitive`/`[AiHidden]` compose normally at every level — a sensitive field nested inside
`Attendees[i]` is excluded from that element's own schema and refused by that element's own
`ApplyArguments` pass, exactly as it would be at the top level.

### Input validation is the boundary

Because arguments flow through the source-generated `Validate` and typed setters, the model's own
rules (`Required`, `Min`/`Max`, `MaxLength`, `Pattern`, `IValidatableObject`) reject bad or
malicious tool calls — and the errors are returned to the AI so it can self-correct.

## What's next

The MCP surface is complete for a server of this kind: the secured core, both transports (stdio
and HTTP+SSE with sessions), tools in both directions (writing through a form, reading through a
grid), and resources and prompts.

What is deliberately *not* here, because it belongs to the client rather than the server:
**sampling** (the server asking the client's model for a completion) and **roots** (the client
telling the server which directories it may work in). Neither has a BlazorDX-shaped answer — a
component library has no business initiating model calls on its host's account — so they are
declined rather than pending. See [ROADMAP.md](ROADMAP.md).
