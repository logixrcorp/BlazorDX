using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the ephemeral chat's TypeScript DOM helpers
/// (<c>ephemeral-chat.js</c>), which in turn drive the <c>dx_security</c>
/// Rust/wasm crypto core. Only functional under WebAssembly; on the server
/// there is no wasm runtime and no DOM to mount into, so
/// <see cref="NullEphemeralChatInterop"/> is used instead.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class EphemeralChatInterop : IEphemeralChatInterop
{
    private const string ModuleName = "dx/ephemeral-chat.js";
    // Relative to /_framework/; "../" reaches the app root's _content/ assets.
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/ephemeral-chat.js";

    private bool isLoaded;

    public async ValueTask EnsureLoadedAsync()
    {
        if (isLoaded)
        {
            return;
        }

        await JSHost.ImportAsync(ModuleName, ModulePath);
        isLoaded = true;
    }

    public async ValueTask<bool> DecryptAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper)
    {
        await EnsureLoadedAsync();
        return await DecryptAndMount(
            hostElementId,
            sessionId,
            serverPublicKeyBase64,
            nonceBase64,
            ciphertextBase64,
            eventsBaseUrl,
            telemetryBaseUrl,
            onWithdraw,
            onRefresh,
            onTamper);
    }

    public async ValueTask<string?> BeginHandshakeAsync(string sessionId)
    {
        await EnsureLoadedAsync();
        return await BeginHandshake(sessionId);
    }

    public async ValueTask<bool> CompleteAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper)
    {
        await EnsureLoadedAsync();
        return await CompleteAndMount(
            hostElementId,
            sessionId,
            serverPublicKeyBase64,
            nonceBase64,
            ciphertextBase64,
            eventsBaseUrl,
            telemetryBaseUrl,
            onWithdraw,
            onRefresh,
            onTamper);
    }

    public async ValueTask ScrubNodeAsync(string hostElementId)
    {
        await EnsureLoadedAsync();
        ScrubNode(hostElementId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("decryptAndMount", ModuleName)]
    private static partial Task<bool> DecryptAndMount(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        [JSMarshalAs<JSType.Function>] Action onWithdraw,
        [JSMarshalAs<JSType.Function>] Action onRefresh,
        [JSMarshalAs<JSType.Function>] Action onTamper);

    [JSImport("beginHandshake", ModuleName)]
    private static partial Task<string?> BeginHandshake(string sessionId);

    [JSImport("completeAndMount", ModuleName)]
    private static partial Task<bool> CompleteAndMount(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        string? telemetryBaseUrl,
        [JSMarshalAs<JSType.Function>] Action onWithdraw,
        [JSMarshalAs<JSType.Function>] Action onRefresh,
        [JSMarshalAs<JSType.Function>] Action onTamper);

    [JSImport("scrubNode", ModuleName)]
    private static partial void ScrubNode(string hostElementId);
}
