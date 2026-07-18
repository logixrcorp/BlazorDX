namespace BlazorDX.Interop;

/// <summary>
/// Bridge to the zero-trust ephemeral chat DOM module (<c>ephemeral-chat.js</c>).
/// Decryption happens entirely in the <c>dx_security</c> Rust/wasm module, and
/// the plaintext is injected directly into an isolated Shadow DOM node in
/// TypeScript -- it never crosses into C# memory or the Blazor virtual DOM.
/// Only functional under WebAssembly.
/// </summary>
public interface IEphemeralChatInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// Runs the P-256 ECDH handshake, decrypts the AES-GCM payload for
    /// <paramref name="sessionId"/>, and mounts the plaintext into an isolated
    /// Shadow DOM node under the element identified by
    /// <paramref name="hostElementId"/>. Starts a MutationObserver that
    /// self-destructs the node (invoking <paramref name="onTamper"/>) on any
    /// externally-originated mutation, and opens an <c>EventSource</c> against
    /// <c>{eventsBaseUrl}/{sessionId}</c> that invokes
    /// <paramref name="onWithdraw"/> on a server <c>WITHDRAW</c> event and
    /// <paramref name="onRefresh"/> on a <c>REFRESH</c> event.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if key agreement, decryption, or mounting
    /// failed for any reason (e.g. a tampered or malformed payload). Callers
    /// must never fall back to rendering the raw ciphertext -- "nothing to
    /// show" is the only safe fallback.
    /// </returns>
    /// <param name="telemetryBaseUrl">
    /// Optional (pass <see langword="null"/> to disable): when set, signs and posts an Access
    /// Receipt to <c>{telemetryBaseUrl}/access</c> right after a successful mount, and a
    /// signed Destruction Receipt to <c>{telemetryBaseUrl}/destruction</c> on every
    /// termination path (WITHDRAW, tamper, or unmount) -- the Proof-of-Destruction protocol.
    /// </param>
    ValueTask<bool> DecryptAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper);

    /// <summary>
    /// Starts a session's client-side ECDH handshake: generates a fresh P-256
    /// keypair from browser CSPRNG entropy (which never leaves the wasm
    /// module) and returns its public key (uncompressed SEC1, base64) for the
    /// caller to forward to a broker. The derived private key is held by the
    /// wasm module under <paramref name="sessionId"/> until a matching
    /// <see cref="CompleteAndMountAsync"/> call consumes it. Returns
    /// <see langword="null"/> on failure.
    /// </summary>
    ValueTask<string?> BeginHandshakeAsync(string sessionId);

    /// <summary>
    /// The second half of the handshake started by
    /// <see cref="BeginHandshakeAsync"/>: completes ECDH against the
    /// broker's server public key, decrypts, and mounts exactly as
    /// <see cref="DecryptAndMountAsync"/> does, but without generating a new
    /// client keypair. Calling this for a <paramref name="sessionId"/> with
    /// no preceding, successful <see cref="BeginHandshakeAsync"/> call always
    /// fails.
    /// </summary>
    /// <param name="telemetryBaseUrl">Optional (pass <see langword="null"/> to disable) -- see <see cref="DecryptAndMountAsync"/>.</param>
    ValueTask<bool> CompleteAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper);

    /// <summary>
    /// Scrubs the mounted node's memory, closes its <c>EventSource</c>, and
    /// stops observing it. Safe to call for a host element id that was never
    /// mounted (or already scrubbed) -- a no-op in that case.
    /// </summary>
    ValueTask ScrubNodeAsync(string hostElementId);
}
