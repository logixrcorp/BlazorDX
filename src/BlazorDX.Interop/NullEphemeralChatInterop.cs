namespace BlazorDX.Interop;

/// <summary>
/// Server-side / non-browser implementation of <see cref="IEphemeralChatInterop"/>.
/// There is no wasm runtime and no DOM to decrypt into outside WebAssembly, so
/// every mount attempt fails safely (returns <see langword="false"/>) rather
/// than pretending to show a message it never decrypted.
/// </summary>
public sealed class NullEphemeralChatInterop : IEphemeralChatInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask<bool> DecryptAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        int? ttlSeconds,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper) =>
        ValueTask.FromResult(false);

    public ValueTask<string?> BeginHandshakeAsync(string sessionId) => ValueTask.FromResult<string?>(null);

    public ValueTask<bool> CompleteAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        int? ttlSeconds,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper) =>
        ValueTask.FromResult(false);

    public ValueTask ScrubNodeAsync(string hostElementId) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
