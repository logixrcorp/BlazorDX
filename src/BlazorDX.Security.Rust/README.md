# dx_security

The crypto core of the "Zero-Trust, Ephemeral AI Chat Conduit," compiled to
`wasm32-unknown-unknown` and loaded as a sidecar module beside the .NET WASM runtime — the
same convention `dx_grid` (`BlazorDX.Compute.Rust`) established, but a separate crate: this one
carries key material and a different dependency/audit surface (`p256`, `aes-gcm`, `zeroize`),
not numeric kernels. See [ADR 0016](../../docs/adr/0016-zero-trust-ephemeral-chat-conduit.md)
for why it's a sibling crate rather than an addition to `dx_grid`, and for the rest of this
feature's architecture (SSE revocation, the MCP client role, the auth extension point).

No `wasm-bindgen`: the public surface is a small pointer/length C-ABI
(`#[no_mangle] extern "C"`) the TypeScript bridge (`BlazorDX.Interop.Ts/src/ephemeral-chat.ts`)
drives directly:

- `alloc` / `dealloc` — caller-managed buffers in wasm linear memory.
- `begin_session` — derives a client ephemeral P-256 keypair from 32 bytes of
  host-supplied entropy (`crypto.getRandomValues`, via the TS bridge — this crate has no OS
  RNG of its own and never generates randomness itself) and returns the public key.
- `complete_session` — runs ECDH against the server's public key and stores the raw shared
  secret as the session's AES-256-GCM key.
- `decrypt_payload` — decrypts and authenticates a payload for a session; returns a null
  pointer on any failure (unknown session, bad nonce length, or a forged/tampered tag —
  deliberately not distinguished in the return value).
- `clear_payload` — zeroizes a `decrypt_payload` buffer in place before freeing it. Mandatory,
  called in a `finally` on the TypeScript side so it always runs, mount success or failure.
- `end_session` — drops and zeroizes a session's key material (e.g. on a server `WITHDRAW`).

Session/crypto logic lives in `src/session.rs` as plain Rust (no FFI, no pointers), so it runs
under `cargo test` on the host target directly; `src/lib.rs` is the thin `unsafe extern "C"`
wrapper the wasm ABI needs. There is no managed C# fallback for this crate — unlike `dx_grid`,
which has one for static-SSR/Interactive Server, decryption is a wasm/JS-only capability by
design: `NullEphemeralChatInterop` is the off-browser stand-in, and it always reports "not
mounted" rather than ever handling key material outside the browser.

## Testing

- `cargo test` — the crypto/session logic and the FFI wrapper, with deterministic fixed-seed
  vectors (no RNG needed for reproducibility).
- `tests/BlazorDX.E2E.Tests/EphemeralChatE2ETests.cs` — real-browser coverage (Playwright) of
  what only a live wasm module in a real DOM can prove: a genuine closed-mode Shadow DOM mount,
  and that a WITHDRAW event actually tears the mounted content down.
