using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// Real-browser coverage for the "Zero-Trust, Ephemeral AI Chat Conduit" that bUnit and
/// vitest cannot reach: that the plaintext genuinely never appears in the initial
/// server-rendered HTML, that the mount point really is a <em>closed</em> Shadow DOM node
/// (not a light-DOM child bUnit's rendered-markup string could be fooled by), and that a
/// WITHDRAW event genuinely tears the mounted content down in a live browser.
///
/// <para>
/// <b>Getting a real successful mount.</b> SecureEphemeralChat's decrypt handshake derives
/// its client keypair from <c>crypto.getRandomValues</c> inside the browser at mount time,
/// so no page load can hand it a pre-encrypted payload guaranteed to decrypt — a real
/// deployment establishes the session key through a round trip this demo does not
/// implement (see <c>docs/adr/0016-zero-trust-ephemeral-chat-conduit.md</c>). To still
/// exercise a genuine successful mount (real ECDH, real AES-256-GCM, a real Shadow DOM
/// attach) rather than only the failure path, this suite overrides
/// <c>window.crypto.getRandomValues</c> before navigating so the real, unmodified
/// client-side handshake lands on the fixed 32-byte seed
/// <c>Components.Pages.EphemeralChatFixture.ClientSeedForE2ETests</c> — the exact seed
/// <c>/ephemeral-chat-fixture</c> encrypted its payload for server-side. Only the source of
/// "randomness" is faked (and only for 32-byte reads — everything else, including .NET's own
/// GUID generation, still gets real entropy); every line of <c>ephemeral-chat.ts</c> and
/// <c>dx_security</c> that runs is the genuine, unmodified code.
/// </para>
///
/// <para>
/// <b>Simulating the WITHDRAW push.</b> This suite deliberately does not attempt a live
/// round trip through BlazorDX.Conduit's real <c>/ephemeral-events/{sessionId}</c> SSE
/// endpoint pushing a server-originated WITHDRAW: nothing in this demo wires an HTTP trigger
/// for that push (the only wired producer, <c>/mcp-proxy</c>, only ever emits
/// <c>PAYLOAD</c> — see <c>samples/BlazorDX.Demo/BlazorDX.Demo/Program.cs</c>), and standing
/// up a second server just to POST one event would duplicate a fair amount of session
/// plumbing for a signal Playwright can already deliver more directly. Instead, this suite
/// intercepts the page's own <c>EventSource</c> request and fulfills it with a hand-written
/// SSE <c>WITHDRAW</c> frame. The <c>EventSource</c>, its <c>"WITHDRAW"</c> listener, and
/// everything it calls (<c>destroySession</c>, the <c>onWithdraw</c> callback into Blazor,
/// the re-render) are the real, unmodified app code; only the network origin of the event
/// frame is simulated.
/// </para>
///
/// <para>
/// <b>What is structurally out of reach, even here.</b> The mount is attached with
/// <c>{ mode: "closed" }</c> specifically so nothing outside <c>ephemeral-chat.ts</c>'s own
/// closure — not this test, not any other script on the page — can read
/// <c>host.shadowRoot</c> (it is <see langword="null"/> from the outside) or enumerate its
/// content. That also means an outside actor cannot directly mutate the shadow tree to
/// simulate DOM <em>tampering</em> the way it can simulate a WITHDRAW push (there is no
/// reachable node to mutate) — which is exactly the isolation property being verified.
/// This suite proves the mount point exists and is genuinely closed, and that a withdraw
/// tears it down, without ever reading the plaintext or reaching into the shadow tree.
/// </para>
/// </summary>
[Collection("e2e")]
public sealed class EphemeralChatE2ETests(PlaywrightFixture fx)
{
    private const string FixtureRoute = "/ephemeral-chat-fixture";

    // Mirrors Components.Pages.EphemeralChatFixture.PlaintextMessage in the Demo host exactly.
    // Duplicated rather than shared across the assembly boundary: the fixture page is internal
    // to samples/BlazorDX.Demo, and this test project has no reference to it (nor should it —
    // E2E tests assert against the running app over HTTP/the DOM, not its internals).
    private const string PlaintextMessage = "the household link is verified end to end";
    private const string HostClass = "dx-ephemeral-chat";
    private const string StatusClass = "dx-ephemeral-chat-status";

    [SkippableFact]
    public async Task Plaintext_never_appears_in_the_initial_server_rendered_html()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);

        // A plain HTTP GET, deliberately not run through Playwright/a browser: these are
        // exactly the bytes the server sent before any JavaScript (WASM boot, decrypt, DOM
        // mount) has had a chance to run. SecureEphemeralChat is pinned to
        // @rendermode InteractiveWebAssembly precisely because there is no meaningful
        // server-rendered fallback for a component whose point is that the server-side
        // render never touches the plaintext either — this is the assertion that actually
        // proves that, rather than trusting the doc comment.
        using HttpClient http = new();
        string html = await http.GetStringAsync($"{fx.BaseUrl}{FixtureRoute}");

        Assert.DoesNotContain(PlaintextMessage, html);
        Assert.Contains(HostClass, html); // sanity: the host element itself did prerender
    }

    [SkippableFact]
    public async Task Successful_mount_uses_a_closed_shadow_root_with_no_light_dom_leak()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await OverrideRandomValuesWithFixedSeedAsync(page);

        // This fixture mounts a SecureEphemeralChat (and its EventSource) unconditionally on
        // load, so the default networkidle wait would hang forever -- see GotoInteractiveAsync's
        // waitUntil doc comment.
        await page.GotoInteractiveAsync($"{fx.BaseUrl}{FixtureRoute}", $".{HostClass}", WaitUntilState.Load);
        await WaitForMountedAsync(page);

        MountProbe probe = await ProbeMountAsync(page);

        Assert.True(probe.HadShadowAlready, "attachShadow on the host did not throw — no Shadow DOM was ever mounted.");
        Assert.True(probe.ShadowRootIsNull, "host.shadowRoot was not null — the root is not closed-mode (or not a shadow root at all).");
        Assert.Equal(0, probe.LightDomChildCount);
        Assert.Equal(string.Empty, probe.LightDomTextContent);
    }

    [SkippableFact]
    public async Task A_simulated_WITHDRAW_event_tears_down_the_mounted_content()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await OverrideRandomValuesWithFixedSeedAsync(page);

        // The client-side wasm module now verifies a WITHDRAW's signature before acting on it
        // (see docs/adr/0016 and dx_security::session::verify_and_end) -- an unsigned/wrong
        // frame is treated as tampering, not withdrawal. Because this suite's client and
        // server seeds are both fixed and known (see OverrideRandomValuesWithFixedSeedAsync /
        // EphemeralChatFixture.ServerSeed), the exact shared secret -- and so the exact signing
        // key -- is independently computable here, the same construction a real broker runs
        // server-side (see DemoAiChatBroker.DeriveSigningKey).
        string withdrawSignatureBase64 = ComputeControlSignatureBase64("WITHDRAW");

        // Fulfil the frontend's own EventSource request with a hand-written SSE stream: the
        // first response sets a short reconnect delay and ends immediately (matching the real
        // backend's actual "connected, nothing pushed yet" behavior); the second — after the
        // browser's automatic EventSource reconnect — delivers a genuinely-signed WITHDRAW.
        // See the type-level doc comment for why this substitutes for a live backend push.
        int requestCount = 0;
        await page.RouteAsync("**/ephemeral-events/**", async route =>
        {
            requestCount++;
            string body = requestCount == 1
                ? "retry: 50\n\n"
                : $"event: WITHDRAW\ndata: {{\"signature\":\"{withdrawSignatureBase64}\"}}\n\n";
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/event-stream",
                Body = body,
            });
        });

        // This fixture mounts a SecureEphemeralChat (and its EventSource) unconditionally on
        // load, so the default networkidle wait would hang forever -- see GotoInteractiveAsync's
        // waitUntil doc comment.
        await page.GotoInteractiveAsync($"{fx.BaseUrl}{FixtureRoute}", $".{HostClass}", WaitUntilState.Load);
        await WaitForMountedAsync(page);

        // Confirm the shadow root really was attached before withdrawing, so the
        // post-withdraw assertions below prove teardown rather than "it was never mounted."
        MountProbe beforeProbe = await ProbeMountAsync(page);
        Assert.True(beforeProbe.HadShadowAlready);

        // The withdrawn status paragraph is real Blazor light-DOM markup, rendered only after
        // the TS bridge's real "WITHDRAW" listener → destroySession (disconnects the
        // MutationObserver, scrubs the shadow root's children, closes the EventSource) → the
        // onWithdraw callback → HandleWithdrawnAsync → StateHasChanged path actually runs. It
        // is the correct externally-observable proxy for "content torn down": the shadow root
        // itself is closed (this suite cannot read its children even to prove emptiness, by
        // design — see the type-level doc comment), and per the DOM spec a shadow root, once
        // attached, cannot be detached from its host even after being scrubbed, so a repeat
        // attachShadow probe cannot distinguish "torn down" from "still mounted" the way it
        // could distinguish "mounted" from "never mounted" above.
        await page.WaitForSelectorAsync($".{StatusClass}", new PageWaitForSelectorOptions { Timeout = 10_000 });
        string statusText = (await page.Locator($".{StatusClass}").First.TextContentAsync())?.Trim() ?? "";
        Assert.Contains("withdrawn", statusText, StringComparison.OrdinalIgnoreCase);

        // Still nothing ever leaked into the light DOM, before or after teardown.
        string hostText = await page.EvaluateAsync<string>($$"""
            () => document.querySelector('.{{HostClass}}')?.textContent ?? ''
            """);
        Assert.Equal(string.Empty, hostText);
    }

    /// <summary>
    /// Forces the P-256 client seed <c>begin_session</c> derives to the fixed
    /// <c>ClientSeedForE2ETests</c> vector the fixture page encrypted its payload for, by
    /// overriding <c>crypto.getRandomValues</c> before any page script (including the WASM
    /// bundle) runs. Only 32-byte reads — the exact shape of the seed request in
    /// ephemeral-chat.ts's <c>decryptAndMount</c> — are redirected; everything else
    /// (including .NET's own GUID generation) still gets real entropy.
    /// </summary>
    private static Task OverrideRandomValuesWithFixedSeedAsync(IPage page)
    {
        // Mirrors Components.Pages.EphemeralChatFixture.ClientSeedForE2ETests: bytes 1..32.
        string seedLiteral = string.Join(",", Enumerable.Range(1, 32));
        return page.AddInitScriptAsync($$"""
            (() => {
              const fixedSeed = new Uint8Array([{{seedLiteral}}]);
              const real = crypto.getRandomValues.bind(crypto);
              crypto.getRandomValues = (array) => {
                if (array instanceof Uint8Array && array.length === fixedSeed.length) {
                  array.set(fixedSeed);
                  return array;
                }
                return real(array);
              };
            })();
            """);
    }

    // Mirrors Components.Pages.EphemeralChatFixture's own fixed vectors exactly: the client
    // seed OverrideRandomValuesWithFixedSeedAsync forces crypto.getRandomValues to return
    // (bytes 1..32), and the server seed the fixture encrypted its payload with (bytes 33..64).
    // Duplicated rather than shared across the assembly boundary, like PlaintextMessage above.
    private static readonly byte[] ClientSeed = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
    private static readonly byte[] ServerSeed = [.. Enumerable.Range(33, 32).Select(i => (byte)i)];
    private const string SessionId = "e2e-fixture-session";
    private const string HmacKeyDerivationLabel = "dx-security/hmac-key/v1";

    /// <summary>
    /// Independently computes the exact signature a real broker would send for a WITHDRAW/
    /// REFRESH targeting this fixture's fixed, known session -- same ECDH shared secret (both
    /// seeds are fixed and known here), same domain-separated HMAC-SHA256 signing-key
    /// derivation as <c>dx_security::session::derive_signing_key</c> and
    /// <c>DemoAiChatBroker.DeriveSigningKey</c>, so this is a genuine cross-implementation
    /// signature the real, unmodified client-side wasm verify path must accept.
    /// </summary>
    private static string ComputeControlSignatureBase64(string action)
    {
        using ECDiffieHellman server = ImportRawScalar(ServerSeed);
        using ECDiffieHellman client = ImportRawScalar(ClientSeed);
        byte[] sharedSecret = server.DeriveRawSecretAgreement(client.PublicKey);

        using HMACSHA256 keyDerivation = new(sharedSecret);
        byte[] hmacKey = keyDerivation.ComputeHash(Encoding.UTF8.GetBytes(HmacKeyDerivationLabel));

        using HMACSHA256 signer = new(hmacKey);
        byte[] signature = signer.ComputeHash(Encoding.UTF8.GetBytes($"{SessionId}|{action}"));
        return Convert.ToBase64String(signature);
    }

    // Imports a raw 32-byte P-256 scalar as a private key by wrapping it in the minimal SEC1
    // ECPrivateKey DER (RFC 5915) that names the curve but omits the public key -- .NET derives
    // the public point from the scalar on import. Test-fixture-only, mirrors
    // EphemeralChatFixture.razor's identical helper (duplicated, not shared, for the same
    // assembly-boundary reason as the constants above).
    private static ECDiffieHellman ImportRawScalar(byte[] scalar32)
    {
        byte[] curveOid = [0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07]; // 1.2.840.10045.3.1.7 (P-256)
        byte[] version = [0x02, 0x01, 0x01];
        byte[] privateKeyOctet = [0x04, (byte)scalar32.Length, .. scalar32];
        byte[] parametersField = [0xA0, (byte)curveOid.Length, .. curveOid];
        byte[] body = [.. version, .. privateKeyOctet, .. parametersField];
        byte[] der = [0x30, (byte)body.Length, .. body];

        ECDiffieHellman key = ECDiffieHellman.Create();
        key.ImportECPrivateKey(der, out _);
        return key;
    }

    /// <summary>
    /// Mounted is the only one of SecureEphemeralChat's four states that renders no status
    /// paragraph at all (Decrypting/Failed/Withdrawn each show one — see
    /// SecureEphemeralChat.razor), so its absence is the signal that the async mount finished
    /// and succeeded.
    /// </summary>
    private static Task WaitForMountedAsync(IPage page) =>
        page.WaitForFunctionAsync($$"""
            () => !document.querySelector('.{{StatusClass}}')
            """, null, new PageWaitForFunctionOptions { Timeout = 15_000 });

    // A plain settable-property class, not a positional record: Playwright's own JS-return
    // deserializer instantiates via a parameterless constructor and assigns properties, rather
    // than matching a record's primary-constructor parameters the way System.Text.Json does.
    private sealed class MountProbe
    {
        public bool HadShadowAlready { get; set; }
        public bool ShadowRootIsNull { get; set; }
        public int LightDomChildCount { get; set; }
        public string LightDomTextContent { get; set; } = string.Empty;
    }

    /// <summary>
    /// Probes the host element from plain page JS — exactly the vantage point an unrelated
    /// script elsewhere on the page would have. Deliberately never touches shadow content (it
    /// cannot: the root is closed). It proves a shadow root exists by observing that a second
    /// <c>attachShadow</c> call throws ("... cannot be created on a host which already hosts
    /// a shadow tree" per the DOM spec), proves it is closed rather than open by observing
    /// <c>host.shadowRoot === null</c> from the outside, and proves nothing leaked into the
    /// light DOM via <c>childElementCount</c>/<c>textContent</c>.
    /// </summary>
    private static async Task<MountProbe> ProbeMountAsync(IPage page)
    {
        return await page.EvaluateAsync<MountProbe>($$"""
            () => {
              const host = document.querySelector('.{{HostClass}}');
              let hadShadowAlready = false;
              try {
                host.attachShadow({ mode: 'open' });
              } catch (e) {
                hadShadowAlready = e instanceof DOMException;
              }
              return {
                HadShadowAlready: hadShadowAlready,
                ShadowRootIsNull: host.shadowRoot === null,
                LightDomChildCount: host.childElementCount,
                LightDomTextContent: host.textContent ?? '',
              };
            }
            """);
    }
}
