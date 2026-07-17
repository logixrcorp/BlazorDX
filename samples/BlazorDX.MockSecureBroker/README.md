# BlazorDX Mock Secure Broker (Node/TypeScript sample)

A runnable **mock of the external MCP resource-provider server** on the other end of BlazorDX's
Zero-Trust Ephemeral Chat conduit. It plays the same role for that conduit that
`samples/BlazorDX.MockReportServer` plays for `DxReportViewer`: a faithful, deterministic
stand-in an E2E suite can point at instead of a real third-party service.

**Direction of the relationship matters:** BlazorDX is the *client* of this server, not the
reverse.

```
BlazorDX (client)                                   this sample (external broker, mocked)
──────────────────                                   ─────────────────────────────────────
McpBrokerClient  ──POST /mcp/tools/initialize_secure_session──►  SecureSessionManager
                 ◄──────── { sessionId, serverPublicKeyJwk } ────
McpBrokerClient  ──POST /mcp/tools/wrap_secure_payload────────►  SecurePayloadWrapper
                 ◄──────────────── { token } ─────────────────
McpProxyEndpoint ◄── (BlazorDX relays `token` onward at POST /mcp-proxy/deliver, its own
                      endpoint for the client Wasm core to fetch the encrypted payload)

BlazorDX Wasm core ──POST /api/telemetry/access──────────────►  TelemetryEndpoints
BlazorDX Wasm core ──POST /api/telemetry/destruction──────────►  TelemetryEndpoints
```

This package implements only the right-hand side. `McpBrokerClient` and `McpProxyEndpoint`
(the BlazorDX-side consumer, including the `/mcp-proxy/deliver` route this sample's README and
code point at) are being built separately in the main BlazorDX solution and are **not** part of
this sample. This is also unrelated to `samples/BlazorDX.McpServer`, which exposes *BlazorDX's
own* tools to an AI assistant over stdio -- the data flow here runs the opposite direction, over
HTTP, against a third party.

## Why Node/TypeScript, not C#

`BlazorDX.MockReportServer` mocks a same-ecosystem dependency (an SSRS-style report server) and
is naturally a .NET `Exe`. The secure broker being mocked here is explicitly an **external,
third-party MCP resource-provider** -- a separate vendor's service BlazorDX talks to over the
network. Modeling the mock in the vendor's own likely stack (Node/Express, native Web Crypto)
keeps the boundary honest: nothing in this package references BlazorDX's C# types, and nothing
in BlazorDX should ever import from here directly. E2E tests spawn or `fetch()` it purely as an
HTTP peer, the same way `BlazorDX.MockReportServer` is consumed as an HTTP peer via
`WebApplicationFactory<Program>` or a standalone process.

## Crypto: native Web Crypto API only

Every cryptographic operation uses Node's `node:crypto` `webcrypto` export -- the same
`SubtleCrypto` surface available in browsers and in a Blazor WebAssembly core. No third-party
crypto library is used anywhere in this package.

- **Key exchange:** ECDH over NIST P-256. Both sides generate an ephemeral keypair; the shared
  secret is derived via `SubtleCrypto.deriveBits` and never transmitted.
- **Payload encryption:** AES-256-GCM, with the AES key HKDF-derived (SHA-256) from the ECDH
  shared secret using a domain-separated `info` string. Each call generates a fresh random
  96-bit IV; GCM's 128-bit authentication tag travels inline in the ciphertext and is verified
  automatically on decrypt -- tampering fails closed (`TokenDecryptionError`).
- **Telemetry proof:** HMAC-SHA256, keyed by a *second*, independently HKDF-derived value from
  the same shared secret (different `info` string than the AES key, so compromising one key
  doesn't compromise the other). Only a party that completed the ECDH exchange can produce a
  valid proof.
- **Session storage:** the derived shared secret lives only in an in-memory, per-process
  `SessionCache` with a 5-minute TTL (configurable). Nothing is persisted to disk; an expired or
  deleted session cannot be used for further encryption, decryption, or telemetry verification.

## Layout

```
src/
  types.ts                  SessionRecord shape shared by the cache and both crypto modules.
  SessionCache.ts            The short-lived volatile cache, keyed by session id.
  SecureSessionManager.ts    initialize_secure_session: auth check, ECDH keypair + shared secret.
  SecurePayloadWrapper.ts     AES-256-GCM encrypt/decrypt of the delivered HTML/Markdown payload.
  TelemetryEndpoints.ts       Express routes: /api/telemetry/access, /api/telemetry/destruction.
  server.ts                  Express app wiring (createApp) -- mountable in-process by test hosts.
  index.ts                   Standalone entrypoint (`npm start`), mirroring MockReportServer's
                              split between endpoint-mapping extensions and its own Program.cs.
test/
  SessionCache.test.ts
  SecureSessionManager.test.ts
  SecurePayloadWrapper.test.ts
  TelemetryEndpoints.test.ts
```

## Run it

```bash
cd samples/BlazorDX.MockSecureBroker
npm install
npm run dev        # ts-executes src/index.ts directly (tsx), or:
npm run build && npm start   # compile to dist/ then run the compiled output
```

By default it listens on `http://localhost:4173` (override with `MOCK_SECURE_BROKER_PORT`).

### HTTP surface

| Method | Path                                     | Purpose |
| ------ | ----------------------------------------- | ------- |
| `POST` | `/mcp/tools/initialize_secure_session`    | `{ clientPublicKeyJwk, authToken }` → `{ sessionId, serverPublicKeyJwk, expiresAt }` |
| `POST` | `/mcp/tools/wrap_secure_payload`          | `{ sessionId, plaintext }` → `{ token }` (the opaque string relayed to `McpProxyEndpoint`) |
| `POST` | `/api/telemetry/access`                   | `{ sessionId, tokenId, timestamp, proof }` → `202` once the HMAC proof verifies |
| `POST` | `/api/telemetry/destruction`              | Same shape as `/access`, for the client's proof-of-zeroization |

## Test first (TDDD)

The Vitest suite in `test/` was written against this API surface before the implementation
files existed, per this repo's test-driven expectations. Some tests use real `webcrypto` calls
end-to-end (round-trip encrypt/decrypt, ECDH agreement across "two parties" within one test);
others `vi.spyOn` the real `webcrypto.subtle` to assert orchestration and failure-path behavior
(e.g. that an invalid auth token short-circuits *before* any key generation happens, or that an
unexpected crypto failure propagates instead of being swallowed).

```bash
npm test            # vitest run
npm run test:watch  # vitest, watch mode
npm run typecheck   # tsc --noEmit
```

## Integration point for BlazorDX

The parallel task building `McpBrokerClient` / `McpProxyEndpoint` on the BlazorDX side should
treat this sample as the contract to implement against:

1. `McpBrokerClient` calls `POST /mcp/tools/initialize_secure_session` with the Wasm core's
   ephemeral ECDH public key (JWK) and the current user's auth token, and stores the returned
   `sessionId` + `serverPublicKeyJwk` (deriving its own copy of the shared secret from the
   latter).
2. `McpBrokerClient` calls `POST /mcp/tools/wrap_secure_payload` to get an opaque `token` and
   hands it to `McpProxyEndpoint`, which is expected to expose it at **`POST /mcp-proxy/deliver`**
   under the Demo host for the client to retrieve.
3. After the client Wasm core decrypts and renders (and later zeroes) the payload, it reports
   both events to `/api/telemetry/access` and `/api/telemetry/destruction` with an HMAC proof
   computed the same way `computeTelemetryProof` does here.

This package deliberately does not implement `/mcp-proxy/deliver` itself -- that endpoint
belongs to BlazorDX, not to the external broker being mocked here.

## Disclaimers

This is a **reference/mock implementation** for local development and E2E testing, not a
production broker:

- `defaultAuthTokenVerifier` is a shape check (`Bearer <16+ chars>`), not real token validation.
  It is injectable (`SecureSessionManagerOptions.authTokenVerifier`) precisely so a real
  implementation can replace it with JWT/OIDC verification.
- `SessionCache` is an in-memory `Map`, not a distributed TTL store -- fine for one process, not
  for a horizontally-scaled broker.
- `AuditLog` is an in-memory array, not durable, tamper-evident storage.
