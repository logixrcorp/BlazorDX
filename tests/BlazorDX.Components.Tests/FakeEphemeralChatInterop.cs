using BlazorDX.Interop;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Off-DOM test double for <see cref="IEphemeralChatInterop"/>: lets a bUnit
/// test control whether "decryption" succeeds and fire the
/// onWithdraw/onRefresh/onTamper callbacks the real TS bridge would invoke
/// from a MutationObserver or an EventSource, without any JS or wasm runtime.
/// </summary>
internal sealed class FakeEphemeralChatInterop : IEphemeralChatInterop
{
    /// <summary>What <see cref="DecryptAndMountAsync"/> should return. Defaults to success.</summary>
    public bool MountSucceeds { get; set; } = true;

    /// <summary>Captures the arguments of the most recent <see cref="DecryptAndMountAsync"/> call.</summary>
    public (string HostElementId, string SessionId, string ServerPublicKeyBase64, string NonceBase64,
        string CiphertextBase64, string EventsBaseUrl)? LastMountArgs
    { get; private set; }

    /// <summary>Host element ids passed to <see cref="ScrubNodeAsync"/>, in call order.</summary>
    public List<string> ScrubbedHostElementIds { get; } = [];

    public int EnsureLoadedCallCount { get; private set; }

    private Action? withdraw;
    private Action? refresh;
    private Action? tamper;

    public ValueTask EnsureLoadedAsync()
    {
        EnsureLoadedCallCount++;
        return ValueTask.CompletedTask;
    }

    /// <summary>When set, <see cref="DecryptAndMountAsync"/> throws this instead of returning. Simulates e.g. the wasm module 404ing.</summary>
    public Exception? MountThrows { get; set; }

    public ValueTask<bool> DecryptAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper)
    {
        LastMountArgs = (hostElementId, sessionId, serverPublicKeyBase64, nonceBase64, ciphertextBase64, eventsBaseUrl);
        withdraw = onWithdraw;
        refresh = onRefresh;
        tamper = onTamper;
        return MountThrows is not null ? ValueTask.FromException<bool>(MountThrows) : ValueTask.FromResult(MountSucceeds);
    }

    /// <summary>What <see cref="BeginHandshakeAsync"/> should return. Defaults to a fixed fake key.</summary>
    public string? BeginHandshakeClientPublicKeyBase64 { get; set; } = "fake-client-public-key-b64";

    /// <summary>Session ids passed to <see cref="BeginHandshakeAsync"/>, in call order.</summary>
    public List<string> BeginHandshakeSessionIds { get; } = [];

    /// <summary>Captures the arguments of the most recent <see cref="CompleteAndMountAsync"/> call.</summary>
    public (string HostElementId, string SessionId, string ServerPublicKeyBase64, string NonceBase64,
        string CiphertextBase64, string EventsBaseUrl)? LastCompleteAndMountArgs
    { get; private set; }

    public ValueTask<string?> BeginHandshakeAsync(string sessionId)
    {
        BeginHandshakeSessionIds.Add(sessionId);
        return ValueTask.FromResult(BeginHandshakeClientPublicKeyBase64);
    }

    public ValueTask<bool> CompleteAndMountAsync(
        string hostElementId,
        string sessionId,
        string serverPublicKeyBase64,
        string nonceBase64,
        string ciphertextBase64,
        string eventsBaseUrl,
        Action onWithdraw,
        Action onRefresh,
        Action onTamper)
    {
        LastCompleteAndMountArgs = (hostElementId, sessionId, serverPublicKeyBase64, nonceBase64, ciphertextBase64, eventsBaseUrl);
        withdraw = onWithdraw;
        refresh = onRefresh;
        tamper = onTamper;
        return ValueTask.FromResult(MountSucceeds);
    }

    public ValueTask ScrubNodeAsync(string hostElementId)
    {
        ScrubbedHostElementIds.Add(hostElementId);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Simulates the TS bridge's EventSource delivering a server WITHDRAW event.</summary>
    public void RaiseWithdraw() => withdraw?.Invoke();

    /// <summary>Simulates the TS bridge's EventSource delivering a server REFRESH event.</summary>
    public void RaiseRefresh() => refresh?.Invoke();

    /// <summary>Simulates the TS bridge's MutationObserver detecting tampering.</summary>
    public void RaiseTamper() => tamper?.Invoke();
}
