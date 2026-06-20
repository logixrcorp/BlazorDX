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
        IPage page = await fx.NewPageAsync();

        List<string> errors = [];
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                errors.Add(msg.Text);
            }
        };

        await page.GotoAsync($"{fx.BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForFunctionAsync("() => !!window.DotNet", null, new PageWaitForFunctionOptions { Timeout = 60_000 });

        Assert.True(errors.Count == 0, $"Console errors on {route}: {string.Join(" | ", errors)}");
    }
}
