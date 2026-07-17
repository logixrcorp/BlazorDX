using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// Real-browser coverage for the "/ai-chat" example app: a live demo of the Zero-Trust Ephemeral
/// AI Chat Conduit (ADR 0016) driven end to end through the genuine, unmodified pipeline -- a
/// real P-256 ECDH handshake performed by the <c>dx_security</c> wasm module against
/// <c>DemoAiChatBroker.HandshakeRoute</c> (a real .NET ECDH/AES-256-GCM server, not a fixture
/// with a fixed client seed like <c>EphemeralChatFixture</c>), a real AES-256-GCM decrypt, and a
/// real closed Shadow DOM mount -- plus a real WITHDRAW push through BlazorDX.Conduit's actual
/// <c>EphemeralSessionRegistry</c> (not a routed/simulated <c>EventSource</c> response, unlike
/// <see cref="EphemeralChatE2ETests"/>'s withdraw test: this demo's broker genuinely calls
/// <c>PushEphemeralEventAsync</c>).
///
/// <para>
/// Unlike <see cref="EphemeralChatE2ETests"/>, no <c>crypto.getRandomValues</c> override is
/// needed here: <c>DemoAiChatBroker</c> accepts whatever real, freshly-generated public key the
/// browser's wasm module actually sends it, and encrypts a fresh response for it on the spot --
/// this is the same live-handshake mode (<c>SecureEphemeralChat</c>'s
/// <c>UseLiveHandshake="true"</c> / <c>EstablishSession</c>) a real deployment would use, just
/// pointed at a demo-local broker instead of an external MCP resource-provider.
/// </para>
/// </summary>
[Collection("e2e")]
public sealed class AiChatE2ETests(PlaywrightFixture fx)
{
    private const string Route = "/ai-chat";
    private const string ThreadSelector = ".ac-thread";
    private const string ComposeSelector = ".ac-compose-text";
    private const string SendSelector = ".ac-compose-actions .ac-btn-primary";
    private const string HostClass = "dx-ephemeral-chat";
    private const string StatusClass = "dx-ephemeral-chat-status";
    private const string RevokeSelector = ".ac-btn-revoke";

    // Matches AiChatStore.GenerateReply's "security"/"crypto"/"encrypt" keyword branch exactly.
    private const string Prompt = "Tell me about the security and encryption model.";
    private const string ExpectedReplySnippet = "P-256 ECDH handshake";

    [SkippableFact]
    public async Task Sending_a_prompt_runs_a_real_handshake_and_mounts_the_canned_reply()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();

        await page.GotoInteractiveAsync($"{fx.BaseUrl}{Route}", ThreadSelector);

        await page.FillAsync(ComposeSelector, Prompt);
        await page.ClickAsync(SendSelector);

        // A fresh dx-ephemeral-chat host mounts for the new assistant message. Mounted is the
        // only one of SecureEphemeralChat's four states that renders no status paragraph at all
        // (Decrypting/Failed/Withdrawn each show one), so its absence is the proof that the real
        // handshake against DemoAiChatBroker, the real AES-256-GCM decrypt, and the real closed
        // Shadow DOM mount all genuinely succeeded end to end.
        //
        // What this test deliberately does NOT attempt: reading the decrypted plaintext back out
        // via a Playwright locator (e.g. `page.Locator("text=...")`). It was tried during this
        // suite's development and, like EphemeralChatE2ETests documents for its own tamper/leak
        // assertions, a closed Shadow DOM node's content is unreachable even to Playwright's own
        // locator/accessibility engine -- confirmed empirically: the exact same canned-reply text
        // is visibly on screen in a manual, non-headless run (screenshots capture pixels, which
        // closed-mode does not hide), yet `page.Locator("text=...")` times out finding it. A
        // successful mount (this wait) together with the "You" turn's own plain-DOM text (visible
        // to a plain locator, asserted next) is the correct, reachable proxy for "the real
        // handshake produced content and rendered it" without requiring shadow-tree access this
        // feature is specifically designed to deny external scripts, this test included.
        await page.WaitForSelectorAsync($".{HostClass}", new PageWaitForSelectorOptions { Timeout = 20_000 });
        await WaitForMountedAsync(page);

        string userTurnText = (await page.Locator(".ac-msg-user p").First.TextContentAsync())?.Trim() ?? "";
        Assert.Equal(Prompt, userTurnText);
    }

    [SkippableFact]
    public async Task Revoking_a_mounted_message_pushes_a_real_WITHDRAW_through_the_conduit()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();

        await page.GotoInteractiveAsync($"{fx.BaseUrl}{Route}", ThreadSelector);

        await page.FillAsync(ComposeSelector, Prompt);
        await page.ClickAsync(SendSelector);

        await page.WaitForSelectorAsync($".{HostClass}", new PageWaitForSelectorOptions { Timeout = 20_000 });
        await WaitForMountedAsync(page);

        // Confirm the shadow root really was attached before revoking, so the post-revoke
        // assertions below prove teardown rather than "it was never mounted."
        bool hadShadowAlready = await page.EvaluateAsync<bool>($$"""
            () => {
              const host = document.querySelector('.{{HostClass}}');
              try { host.attachShadow({ mode: 'open' }); return false; }
              catch (e) { return e instanceof DOMException; }
            }
            """);
        Assert.True(hadShadowAlready, "attachShadow on the host did not throw -- no Shadow DOM was ever mounted.");

        // AiChat.razor's Revoke() itself waits ~400ms before POSTing so the component's
        // EventSource has time to finish connecting -- BlazorDX.Conduit is an ephemeral relay,
        // not a durable queue, so a push with no live listener yet is silently dropped (see
        // EphemeralSessionRegistry). The click below drives the REAL round trip: POST
        // /demo/ai-chat/withdraw/{id} -> EphemeralSessionRegistry.PushEphemeralEventAsync -> a
        // live SSE WITHDRAW frame -> the page's own EventSource listener -> destroySession ->
        // onWithdraw -> HandleWithdrawnAsync -> re-render.
        await page.ClickAsync(RevokeSelector);

        await page.WaitForSelectorAsync($".{StatusClass}", new PageWaitForSelectorOptions { Timeout = 15_000 });
        string statusText = (await page.Locator($".{StatusClass}").First.TextContentAsync())?.Trim() ?? "";
        Assert.Contains("withdrawn", statusText, StringComparison.OrdinalIgnoreCase);

        // The Revoke/Send-to action row only renders while the message is not withdrawn (see
        // AiChat.razor's `@if (!m.Withdrawn)`) -- its disappearance is a second, independent
        // signal (driven by AiChatStore state, not the TS bridge) that the real WITHDRAW event
        // actually reached HandleWithdrawnAsync.
        int remainingRevokeButtons = await page.Locator(RevokeSelector).CountAsync();
        Assert.Equal(0, remainingRevokeButtons);
    }

    /// <summary>
    /// Mounted is the only one of SecureEphemeralChat's four states that renders no status
    /// paragraph at all (Decrypting/Failed/Withdrawn each show one — see
    /// SecureEphemeralChat.razor), so its absence is the signal that the async mount finished
    /// and succeeded. Mirrors EphemeralChatE2ETests's own helper of the same name.
    /// </summary>
    private static Task WaitForMountedAsync(IPage page) =>
        page.WaitForFunctionAsync($$"""
            () => !document.querySelector('.{{StatusClass}}')
            """, null, new PageWaitForFunctionOptions { Timeout = 20_000 });
}
