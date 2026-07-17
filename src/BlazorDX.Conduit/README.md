# BlazorDX.Conduit

The server-side router for the "Zero-Trust, Ephemeral AI Chat Conduit": it makes BlazorDX an
**MCP client** of an external resource-provider server — the reverse role of
`samples/BlazorDX.McpServer` (BlazorDX **serving** its own tools). The two are independent,
non-conflicting integration points that share no code; see
[ADR 0016](../../docs/adr/0016-zero-trust-ephemeral-chat-conduit.md) for why that distinction
matters and for the rest of this feature's architecture (the `dx_security` crate boundary, SSE
over SignalR, the auth extension point).

This project is a **blind router**: every payload and event it forwards is opaque bytes it
never parses, inspects, or decrypts. Zero-Knowledge Routing is the whole design — the private
key that would make the ciphertext meaningful never exists in this process.

## Public surface

- **`GET /ephemeral-events/{sessionId}`** (`EphemeralEventsEndpoint.MapEphemeralEvents`) — the
  Server-Sent Events stream a frontend's `EventSource` connects to. Plain minimal-API SSE, not
  SignalR (see the ADR for why).
- **`POST /mcp-proxy/{sessionId}`** (`McpProxyEndpoint.MapMcpProxy`) — where the external MCP
  provider delivers the initial encrypted payload. Antiforgery is disabled: the caller is a
  server-to-server provider, not a browser form.
- **`EphemeralSessionRegistry`** — tracks every live SSE connection by session id and fans a
  push out to all of them. Register it as a **Singleton**: it must outlive any single
  request/connection.
- **`IEphemeralSessionAuthorizer`** — the host-supplied auth seam both endpoints consult before
  registering a connection or accepting a payload delivery. Mirrors `IAiToolAuthorizer`.
  BlazorDX implements no auth itself; when nothing is registered in DI, every session id is
  allowed (fine for a local demo, not for anything beyond one).
- **`McpBrokerClient`** / **`IConduitNotificationSource`** — a `BackgroundService` that drains
  WITHDRAW/REFRESH notifications from the external provider (a webhook relay or a message-bus
  subscription in production) and routes each one to its session's SSE stream. This project
  ships no concrete `IConduitNotificationSource` — that transport is host-specific.

## Wiring it into a host

```csharp
builder.Services.AddSingleton<EphemeralSessionRegistry>();
// Production: also register a real IEphemeralSessionAuthorizer and
// IConduitNotificationSource + McpBrokerClient. Anonymous/no-op by default.

app.MapEphemeralEvents();
app.MapMcpProxy();
```

See `samples/BlazorDX.Demo/BlazorDX.Demo/Program.cs` for the full wiring this demo runs with
(anonymous, single-process — every production caveat above is called out there too).

## Testing

- `tests/BlazorDX.Conduit.Tests` — xUnit + Moq coverage of the registry, both endpoint
  handlers, and the broker client's drain loop, driven directly (hand-built
  `DefaultHttpContext`, no real Kestrel listener).
- `tests/BlazorDX.E2E.Tests/EphemeralChatE2ETests.cs` — real-browser coverage of the frontend
  that actually connects to `GET /ephemeral-events/{sessionId}`.
