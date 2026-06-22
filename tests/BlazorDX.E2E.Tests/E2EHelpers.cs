using Microsoft.Playwright;

namespace BlazorDX.E2E.Tests;

internal static class E2EHelpers
{
    /// <summary>
    /// Navigates to a demo route and waits for the WebAssembly runtime to boot and
    /// the page's first interactive element to appear, so handlers are wired before
    /// a test exercises them.
    /// </summary>
    public static async Task GotoInteractiveAsync(this IPage page, string url, string readySelector)
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForFunctionAsync("() => !!window.DotNet", null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        await page.WaitForSelectorAsync(readySelector, new PageWaitForSelectorOptions { Timeout = 60_000 });
    }
}
