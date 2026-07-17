# ADR 0016 — Zero-trust ephemeral chat conduit: crate boundary, SSE revocation, MCP client role, and auth seam

**Status:** Accepted

## Context

The "Zero-Trust, Ephemeral AI Chat Conduit" lets BlazorDX mount a message from an external,
already-encrypted source (an MCP resource-provider) such that the plaintext exists only inside
an isolated Shadow DOM node in the browser, is derivable only through a per-session ECDH
handshake, and disappears the instant the provider revokes it or a MutationObserver sees an
externally-originated mutation. Landing it touched four points that are each a real
architectural fork, not just an implementation detail, and are recorded together here because
they were decided together and a reviewer evaluating one needs the others for context.

## Decision

1. **`BlazorDX.Security.Rust` is a new sibling crate, not an addition to `BlazorDX.Compute.Rust`.**
   `dx_grid` (in `BlazorDX.Compute.Rust`) is a pure numeric-kernel module — sort/filter/aggregate
   over `f64` slices — with no notion of a session, a key, or a secret. `dx_security` holds
   ECDH key agreement, AES-256-GCM state, and zeroizing memory: a different threat model, a
   different audit boundary, and different dependencies (`p256`, `aes-gcm`, `zeroize`, none of
   which `dx_grid` needs). Folding it into `BlazorDX.Compute.Rust` would mean every future
   compute kernel added to that crate expands the code that ships next to key material, and
   every crypto change forces a rebuild/re-audit of unrelated numeric kernels sharing the same
   `Cargo.toml`. A second small crate keeps the two `cargo audit`/review surfaces independent at
   the cost of a second wasm module the browser downloads and a second entry in
   `build/Rust.targets`. `dx_security` follows the same no-`wasm-bindgen`, raw-pointer/length
   `#[no_mangle] extern "C"` ABI convention `dx_grid` established (see
   [ADR 0003](0003-rust-wasm-heavy-compute.md)) — one FFI calling convention for the whole
   codebase, so `BlazorDX.Interop.Ts` has exactly one pattern to load and drive a Rust/wasm
   sidecar, regardless of which crate it comes from.

2. **Real-time revocation is Server-Sent Events over a minimal-API endpoint, not SignalR.**
   A WITHDRAW/REFRESH push is one-directional (server → client), has no need for the client to
   call back over the same channel, and must degrade to "the message just doesn't update"
   rather than blocking on a circuit — none of which needs a stateful, bidirectional SignalR
   hub. This codebase already leans away from SignalR circuits elsewhere ([ADR 0004](0004-htmx-static-ssr-tier.md),
   [ADR 0013](0013-render-mode-selection.md) both choose static-SSR/HTMX or WASM specifically to
   avoid a circuit's server-held state and reconnect complexity), so introducing one just for
   this feature's revocation channel would be the odd one out. `EphemeralEventsEndpoint` is a
   plain `MapGet` returning `text/event-stream`, matching the existing style of
   `app.MapPost("/mcp", …)` in `samples/BlazorDX.Demo/Program.cs`. The browser's native
   `EventSource` (not the .NET SignalR client) drives it from `ephemeral-chat.ts`, with its own
   built-in reconnect/backoff — one fewer client dependency, and no server-held per-connection
   circuit state to leak across users (the mistake [ADR 0007](0007-security-baseline.md) already
   guards against for Singleton UI state).

3. **`BlazorDX.Conduit` makes BlazorDX an MCP *client*; this is a distinct, non-conflicting role
   from `samples/BlazorDX.McpServer`.** `samples/BlazorDX.McpServer` is BlazorDX **serving** its
   own `[DxFormModel]`-derived tools to an external AI assistant over stdio — BlazorDX as MCP
   server. `BlazorDX.Conduit` is BlazorDX **consuming** resources — an already-encrypted chat
   payload and its WITHDRAW/REFRESH lifecycle notifications — from an external MCP
   resource-provider server: BlazorDX as MCP client. These are opposite ends of an MCP
   connection, they share no code (`McpProxyEndpoint`/`McpBrokerClient` never touch
   `McpToolServer`/`FormAiTool`), and neither one's auth, session, or transport model applies to
   the other. Naming the distinction here explicitly is the point of this entry: without it,
   "BlazorDX and MCP" reads as one relationship, and a future change to one role's routing or
   auth is an easy, silent way to accidentally couple it to the other's.

4. **`IEphemeralSessionAuthorizer` is a host-supplied extension point, mirroring
   `IAiToolAuthorizer`; BlazorDX implements no auth of its own.** `IAiToolAuthorizer.IsAllowedAsync(IAiTool,
   CancellationToken)` gates a tool call per caller; `IEphemeralSessionAuthorizer.IsAllowedAsync(sessionId,
   HttpContext, CancellationToken)` gates SSE registration (`EphemeralEventsEndpoint`) and payload
   delivery (`McpProxyEndpoint`) per session id, the same shape applied to this feature's two
   entry points. Both interfaces exist so BlazorDX can define *where* an authorization check
   must run without deciding *how* — a host wires a real `ClaimsPrincipal`/session-ownership
   check or a shared-secret check for the external provider. When no authorizer is registered in
   DI, every call is allowed (matching `samples/BlazorDX.Demo`'s existing anonymous `/mcp`
   posture) — a local demo works zero-config, and a production deployment is one DI registration
   away from being gated, never a code change to `BlazorDX.Conduit` itself.

## Consequences

- Two Rust/wasm modules ship instead of one, and `BlazorDX.Interop.Ts` now loads both — a small,
  bounded cost for keeping the crypto crate's audit surface and dependency set independent of
  the numeric-kernel crate's.
- Revocation latency and delivery are whatever a plain SSE connection provides (no guaranteed
  delivery to a disconnected client, no replay) — acceptable because the Conduit is documented
  as "an ephemeral relay, not a durable queue" (`EphemeralEventsEndpoint.PushEphemeralEventAsync`):
  a session nobody is currently watching silently drops the event, by design.
  Anything needing guaranteed delivery is a different, not-yet-built feature.
- `BlazorDX.Conduit` and `samples/BlazorDX.McpServer` can evolve independently (different auth
  models, different transports, different lifecycles) with no risk of one's change silently
  altering the other's behavior.
- Every deployment of this feature ships with **no** authorization by default and must
  deliberately register an `IEphemeralSessionAuthorizer` before it is safe to expose beyond a
  local demo — the same trade-off already accepted for `IAiToolAuthorizer`, and one this ADR
  makes explicit rather than leaving as a silent gap discovered in production.
