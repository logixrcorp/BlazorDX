using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The Blazor container for the zero-trust ephemeral chat conduit. All the
/// actual decryption/mounting happens in TypeScript/Rust; these tests only
/// verify the C# side's contract with <see cref="IEphemeralChatInterop"/>:
/// what it passes in, how it reacts to the three callbacks, and that it never
/// renders the raw ciphertext/keys anywhere in the (server-rendered) markup.
/// </summary>
public sealed class SecureEphemeralChatTests : TestContext
{
    private const string SessionId = "session-abc-123";
    private const string ServerPublicKeyBase64 = "server-public-key-b64";
    private const string NonceBase64 = "nonce-b64";
    private const string CiphertextBase64 = "super-secret-ciphertext-b64";

    private readonly FakeEphemeralChatInterop fake = new();

    public SecureEphemeralChatTests()
    {
        Services.AddScoped<IEphemeralChatInterop>(_ => fake);
    }

    private IRenderedComponent<SecureEphemeralChat> RenderChat(bool mountSucceeds = true)
    {
        fake.MountSucceeds = mountSucceeds;
        return RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.ServerPublicKeyBase64, ServerPublicKeyBase64)
            .Add(c => c.NonceBase64, NonceBase64)
            .Add(c => c.CiphertextBase64, CiphertextBase64));
    }

    [Fact]
    public void Renders_a_host_element_with_a_stable_unique_id_and_aria_live()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderChat();

        var host = chat.Find(".dx-ephemeral-chat");
        Assert.StartsWith("dx-ephemeral-chat-", host.Id);
        Assert.Equal("polite", host.GetAttribute("aria-live"));
        Assert.False(string.IsNullOrWhiteSpace(host.GetAttribute("aria-label")));
    }

    [Fact]
    public void Passes_all_payload_coordinates_and_the_default_events_base_url_to_the_bridge()
    {
        RenderChat();

        Assert.NotNull(fake.LastMountArgs);
        var args = fake.LastMountArgs!.Value;
        Assert.Equal(SessionId, args.SessionId);
        Assert.Equal(ServerPublicKeyBase64, args.ServerPublicKeyBase64);
        Assert.Equal(NonceBase64, args.NonceBase64);
        Assert.Equal(CiphertextBase64, args.CiphertextBase64);
        Assert.Equal("/ephemeral-events", args.EventsBaseUrl);
        Assert.StartsWith("dx-ephemeral-chat-", args.HostElementId);
        Assert.Null(args.TelemetryBaseUrl); // opt-in only -- unset by default
    }

    [Fact]
    public void A_TelemetryBaseUrl_parameter_is_forwarded_to_the_bridge()
    {
        fake.MountSucceeds = true;
        RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.ServerPublicKeyBase64, ServerPublicKeyBase64)
            .Add(c => c.NonceBase64, NonceBase64)
            .Add(c => c.CiphertextBase64, CiphertextBase64)
            .Add(c => c.TelemetryBaseUrl, "/demo/ai-chat/telemetry"));

        Assert.Equal("/demo/ai-chat/telemetry", fake.LastMountArgs!.Value.TelemetryBaseUrl);
    }

    [Fact]
    public void A_custom_events_base_url_parameter_is_forwarded()
    {
        fake.MountSucceeds = true;
        IRenderedComponent<SecureEphemeralChat> chat = RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.ServerPublicKeyBase64, ServerPublicKeyBase64)
            .Add(c => c.NonceBase64, NonceBase64)
            .Add(c => c.CiphertextBase64, CiphertextBase64)
            .Add(c => c.EventsBaseUrl, "/custom-events"));

        Assert.Equal("/custom-events", fake.LastMountArgs!.Value.EventsBaseUrl);
    }

    [Fact]
    public void Successful_mount_shows_no_status_message()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderChat(mountSucceeds: true);

        Assert.Empty(chat.FindAll(".dx-ephemeral-chat-status"));
    }

    [Fact]
    public void Failed_mount_shows_a_generic_error_and_never_the_ciphertext_or_keys()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderChat(mountSucceeds: false);

        var error = chat.Find(".dx-ephemeral-chat-error");
        Assert.Equal("alert", error.GetAttribute("role"));

        string markup = chat.Markup;
        Assert.DoesNotContain(CiphertextBase64, markup);
        Assert.DoesNotContain(ServerPublicKeyBase64, markup);
        Assert.DoesNotContain(NonceBase64, markup);
    }

    [Fact]
    public void Withdraw_event_shows_withdrawn_status_and_raises_OnWithdrawn()
    {
        bool raised = false;
        IRenderedComponent<SecureEphemeralChat> chat = RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.ServerPublicKeyBase64, ServerPublicKeyBase64)
            .Add(c => c.NonceBase64, NonceBase64)
            .Add(c => c.CiphertextBase64, CiphertextBase64)
            .Add(c => c.OnWithdrawn, EventCallback.Factory.Create(this, () => raised = true)));

        chat.InvokeAsync(fake.RaiseWithdraw);

        Assert.True(raised);
        Assert.Contains("withdrawn", chat.Find(".dx-ephemeral-chat-status").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tamper_event_shows_the_generic_error_and_raises_OnTamperDetected()
    {
        bool raised = false;
        IRenderedComponent<SecureEphemeralChat> chat = RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.ServerPublicKeyBase64, ServerPublicKeyBase64)
            .Add(c => c.NonceBase64, NonceBase64)
            .Add(c => c.CiphertextBase64, CiphertextBase64)
            .Add(c => c.OnTamperDetected, EventCallback.Factory.Create(this, () => raised = true)));

        chat.InvokeAsync(fake.RaiseTamper);

        Assert.True(raised);
        Assert.NotEmpty(chat.FindAll(".dx-ephemeral-chat-error"));
    }

    [Fact]
    public void Refresh_event_raises_OnRefreshed_without_touching_the_mounted_state()
    {
        bool raised = false;
        IRenderedComponent<SecureEphemeralChat> chat = RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.ServerPublicKeyBase64, ServerPublicKeyBase64)
            .Add(c => c.NonceBase64, NonceBase64)
            .Add(c => c.CiphertextBase64, CiphertextBase64)
            .Add(c => c.OnRefreshed, EventCallback.Factory.Create(this, () => raised = true)));

        chat.InvokeAsync(fake.RaiseRefresh);

        Assert.True(raised);
        Assert.Empty(chat.FindAll(".dx-ephemeral-chat-status")); // still "mounted" -- no status text
    }

    [Fact]
    public void Disposing_a_mounted_component_scrubs_the_node_exactly_once()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderChat();

        DisposeComponents();

        Assert.Single(fake.ScrubbedHostElementIds);
        Assert.Equal(fake.LastMountArgs!.Value.HostElementId, fake.ScrubbedHostElementIds[0]);
    }

    [Fact]
    public void Disposing_after_a_withdraw_does_not_scrub_again()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderChat();
        chat.InvokeAsync(fake.RaiseWithdraw);

        DisposeComponents();

        // The TS bridge already scrubbed the node before raising WITHDRAW;
        // the component must not ask it to scrub a second time.
        Assert.Empty(fake.ScrubbedHostElementIds);
    }

    [Fact]
    public void Disposing_after_tamper_does_not_scrub_again()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderChat();
        chat.InvokeAsync(fake.RaiseTamper);

        DisposeComponents();

        Assert.Empty(fake.ScrubbedHostElementIds);
    }

    [Fact]
    public void An_exception_from_the_interop_layer_shows_the_generic_error_instead_of_hanging_on_Decrypting()
    {
        // Regression test: a 404 loading the wasm module (e.g. a stale deploy) used to throw an
        // unhandled exception out of OnAfterRenderAsync, leaving the component stuck showing
        // "Decrypting..." forever instead of resolving to the normal failed-mount state.
        fake.MountThrows = new InvalidOperationException("simulated wasm 404");

        IRenderedComponent<SecureEphemeralChat> chat = RenderChat();

        Assert.DoesNotContain("Decrypting", chat.Markup); // not stuck on "Decrypting..."
        var error = chat.Find(".dx-ephemeral-chat-error");
        Assert.Equal("alert", error.GetAttribute("role"));
    }

    [Fact]
    public void Live_handshake_forwards_the_wasm_generated_client_public_key_to_EstablishSession_and_mounts_its_response()
    {
        fake.BeginHandshakeClientPublicKeyBase64 = "client-pub-from-wasm";
        string? capturedClientPublicKey = null;
        var response = new EphemeralHandshakeResult("broker-server-pub-b64", "broker-nonce-b64", "broker-ciphertext-b64");

        RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.UseLiveHandshake, true)
            .Add(c => c.EstablishSession, (Func<string, Task<EphemeralHandshakeResult>>)(clientPublicKey =>
            {
                capturedClientPublicKey = clientPublicKey;
                return Task.FromResult(response);
            })));

        Assert.Equal(SessionId, Assert.Single(fake.BeginHandshakeSessionIds));
        Assert.Equal("client-pub-from-wasm", capturedClientPublicKey);
        Assert.NotNull(fake.LastCompleteAndMountArgs);
        var args = fake.LastCompleteAndMountArgs!.Value;
        Assert.Equal(SessionId, args.SessionId);
        Assert.Equal(response.ServerPublicKeyBase64, args.ServerPublicKeyBase64);
        Assert.Equal(response.NonceBase64, args.NonceBase64);
        Assert.Equal(response.CiphertextBase64, args.CiphertextBase64);
        // The pre-supplied-ciphertext path must never fire in live mode.
        Assert.Null(fake.LastMountArgs);
    }

    [Fact]
    public void Live_handshake_fails_safely_when_no_EstablishSession_delegate_is_supplied()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.UseLiveHandshake, true));

        Assert.NotEmpty(chat.FindAll(".dx-ephemeral-chat-error"));
        Assert.Empty(fake.BeginHandshakeSessionIds);
    }

    [Fact]
    public void Live_handshake_fails_safely_when_BeginHandshakeAsync_returns_null()
    {
        fake.BeginHandshakeClientPublicKeyBase64 = null;

        IRenderedComponent<SecureEphemeralChat> chat = RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.UseLiveHandshake, true)
            .Add(c => c.EstablishSession, (Func<string, Task<EphemeralHandshakeResult>>)(_ =>
                throw new InvalidOperationException("must not be called without a client public key"))));

        Assert.NotEmpty(chat.FindAll(".dx-ephemeral-chat-error"));
    }

    [Fact]
    public void Live_handshake_fails_safely_when_EstablishSession_throws()
    {
        IRenderedComponent<SecureEphemeralChat> chat = RenderComponent<SecureEphemeralChat>(parameters => parameters
            .Add(c => c.SessionId, SessionId)
            .Add(c => c.UseLiveHandshake, true)
            .Add(c => c.EstablishSession, (Func<string, Task<EphemeralHandshakeResult>>)(_ =>
                throw new InvalidOperationException("broker unreachable"))));

        Assert.NotEmpty(chat.FindAll(".dx-ephemeral-chat-error"));
        Assert.Null(fake.LastCompleteAndMountArgs);
    }
}
