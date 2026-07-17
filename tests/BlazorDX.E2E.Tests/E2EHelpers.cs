using Microsoft.Playwright;

namespace BlazorDX.E2E.Tests;

internal static class E2EHelpers
{
    /// <summary>
    /// Navigates to a demo route and waits for the WebAssembly runtime to boot and
    /// the page's first interactive element to appear, so handlers are wired before
    /// a test exercises them.
    /// </summary>
    /// <param name="waitUntil">
    /// Defaults to <see cref="WaitUntilState.NetworkIdle"/>, which most demo pages need:
    /// it's the only signal here that waits for a page's own async setup (fetches,
    /// lazy asset loads) to finish before the ready-selector check, and most pages have
    /// no long-lived connection to prevent it from ever firing.
    /// <para>
    /// Pass <see cref="WaitUntilState.Load"/> for any page that opens a persistent
    /// connection (EventSource/WebSocket) unconditionally as part of its <em>initial</em>
    /// render — e.g. <c>/ephemeral-chat-fixture</c>, which mounts a SecureEphemeralChat
    /// (and its EventSource) immediately on load. <c>networkidle</c> can never fire while
    /// that connection is open, so it hangs until this method's own 60s timeout on every
    /// run against such a page, deterministically, not just flakily. Pages where a
    /// long-lived connection only opens later, in response to a user action (e.g.
    /// <c>/ai-chat</c>, which has no SecureEphemeralChat mounted until a message is sent),
    /// are unaffected and should keep the default.
    /// </para>
    /// </param>
    public static async Task GotoInteractiveAsync(
        this IPage page, string url, string readySelector, WaitUntilState waitUntil = WaitUntilState.NetworkIdle)
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = waitUntil, Timeout = 60_000 });
        await page.WaitForFunctionAsync("() => !!window.DotNet", null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        await page.WaitForSelectorAsync(readySelector, new PageWaitForSelectorOptions { Timeout = 60_000 });
    }
}
