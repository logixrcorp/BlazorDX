using Microsoft.Playwright;

namespace BlazorDX.E2E.Tests;

internal static class E2EHelpers
{
    // The AOT-published WASM runtime (the `aot-publish` smoke job) boots noticeably slower
    // than the JIT build, and each test opens a fresh context that re-instantiates it, so a
    // 60s wait can lapse on a loaded CI runner. 120s gives the runtime room without masking
    // a genuine hang (a real failure still fails, just later).
    private const int InteractiveTimeoutMs = 120_000;

    /// <summary>
    /// Navigates to a demo route and waits for the WebAssembly runtime to boot and
    /// the page's first interactive element to appear, so handlers are wired before
    /// a test exercises them.
    /// </summary>
    public static async Task GotoInteractiveAsync(this IPage page, string url, string readySelector)
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = InteractiveTimeoutMs });
        await page.WaitForFunctionAsync("() => !!window.DotNet", null, new PageWaitForFunctionOptions { Timeout = InteractiveTimeoutMs });
        await page.WaitForSelectorAsync(readySelector, new PageWaitForSelectorOptions { Timeout = InteractiveTimeoutMs });
    }
}
