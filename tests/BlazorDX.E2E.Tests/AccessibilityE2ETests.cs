using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// Runs the axe-core accessibility engine against the showcase and the TicketDesk demo app in a
/// real browser, and fails on any serious/critical WAI-ARIA / WCAG violation. This is the
/// automated half of the accessibility story (the screen-reader audit is the manual half) — and
/// the backing for the "axe checks pass" claim, enforced in CI rather than asserted in a doc.
/// </summary>
[Collection("e2e")]
public sealed class AccessibilityE2ETests(PlaywrightFixture fx)
{
    [SkippableTheory]
    [InlineData("/")]
    [InlineData("/app")]
    [InlineData("/app/tickets")]
    [InlineData("/app/board")]
    [InlineData("/app/new")]
    [InlineData("/app/ticket/1")]
    public async Task Page_has_no_serious_axe_violations(string route)
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

        // Let interactive components hydrate so axe inspects the live DOM, not just the prerender.
        try
        {
            await page.WaitForFunctionAsync("() => !!window.DotNet", null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            // Static-only routes never define window.DotNet — the prerendered DOM is what we audit.
        }

        await page.WaitForTimeoutAsync(400);

        AxeResult result = await page.RunAxe();

        AxeResultItem[] serious = result.Violations
            .Where(v => v.Impact is "serious" or "critical")
            .ToArray();

        string report = string.Join("\n", serious.Select(v =>
        {
            string targets = string.Join(", ", v.Nodes.Take(2).Select(n => n.Target?.ToString()));
            return $"  [{v.Impact}] {v.Id} — {v.Help} ({v.Nodes.Length} node(s)): {targets}";
        }));

        Assert.True(serious.Length == 0, $"axe-core found {serious.Length} serious/critical violation(s) on {route}:\n{report}");
    }
}
