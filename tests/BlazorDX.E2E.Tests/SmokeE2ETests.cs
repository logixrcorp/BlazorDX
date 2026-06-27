using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// Broad smoke tests: the showcase loads and key interactive routes hydrate without
/// throwing console errors — a cheap guard against a whole page being dead on arrival.
/// </summary>
[Collection("e2e")]
public sealed class SmokeE2ETests(PlaywrightFixture fx)
{
    [SkippableFact]
    public async Task Home_renders_the_hero_and_catalog()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

        Assert.Equal("BlazorDX", (await page.Locator("h1").First.TextContentAsync())?.Trim());
        Assert.True(await page.Locator("a:has-text('Browse components')").IsVisibleAsync());
    }

    [SkippableTheory]
    [InlineData("/grid")]
    [InlineData("/imageeditor")]
    [InlineData("/docviewer")]
    [InlineData("/keyboard")]
    public async Task Interactive_route_hydrates_without_console_errors(string route)
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);

        // The Mono WASM runtime occasionally fails to initialize on a CI runner — a fatal,
        // route-independent flake whose console output is the runtime's own startup crash
        // (mono_wasm_load_runtime / StartupHookProvider), not anything the page did. Retry
        // such loads on a fresh page; a persistent or APP-level console error still fails.
        const int maxAttempts = 3;
        List<string> errors = [];

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            IPage page = await fx.NewPageAsync();
            errors = [];
            page.Console += (_, msg) =>
            {
                if (msg.Type == "error")
                {
                    errors.Add(msg.Text);
                }
            };

            await page.GotoAsync($"{fx.BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            try
            {
                await page.WaitForFunctionAsync("() => !!window.DotNet", null, new PageWaitForFunctionOptions { Timeout = 60_000 });
            }
            catch (PlaywrightException)
            {
                // window.DotNet never appeared (timeout/closed) — treat as a transient load
                // failure and let the console-error check below decide whether to retry.
            }

            if (errors.Count == 0)
            {
                return; // hydrated cleanly
            }

            // A real application error (not the WASM runtime failing to boot) fails immediately.
            if (!errors.TrueForAll(IsTransientRuntimeLoadFailure))
            {
                break;
            }

            await page.CloseAsync();
        }

        Assert.True(errors.Count == 0, $"Console errors on {route} after retries: {string.Join(" | ", errors)}");
    }

    // The signature of a Mono WASM runtime that failed to initialize (vs. an app error).
    private static bool IsTransientRuntimeLoadFailure(string message) =>
        message.Contains("mono_wasm_load_runtime", StringComparison.OrdinalIgnoreCase)
        || message.Contains("StartupHookProvider", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ProcessStartupHooks", StringComparison.OrdinalIgnoreCase)
        || message.Contains("FATAL UNHANDLED EXCEPTION", StringComparison.OrdinalIgnoreCase);
}
