namespace BlazorDX.Components;

/// <summary>
/// A broker's response to an ephemeral chat session handshake: its ephemeral
/// P-256 public key and an AES-GCM payload encrypted for the shared secret
/// that key agrees to with the client's public key from
/// <see cref="BlazorDX.Interop.IEphemeralChatInterop.BeginHandshakeAsync"/>.
/// Never carries key material -- only what <see cref="SecureEphemeralChat"/>
/// needs to hand to <c>CompleteAndMountAsync</c>.
/// </summary>
/// <param name="ServerPublicKeyBase64">The broker's ephemeral P-256 public key (uncompressed SEC1, base64).</param>
/// <param name="NonceBase64">The AES-GCM nonce (base64) for <paramref name="CiphertextBase64"/>.</param>
/// <param name="CiphertextBase64">The AES-GCM ciphertext, including its trailing authentication tag (base64).</param>
/// <param name="TtlSeconds">
/// Optional cryptographic time-to-live: when set, the mounted session self-destructs (a
/// Destruction Receipt with trigger <c>TTL_EXPIRY</c>) this many seconds after mounting, even
/// with no WITHDRAW, tamper, or unmount. <see langword="null"/> means the session lives until
/// one of those other triggers fires.
/// </param>
public sealed record EphemeralHandshakeResult(
    string ServerPublicKeyBase64,
    string NonceBase64,
    string CiphertextBase64,
    int? TtlSeconds = null);
