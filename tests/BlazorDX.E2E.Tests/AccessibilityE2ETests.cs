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
    [InlineData("/faq")]
    [InlineData("/app")]
    [InlineData("/app/records")]
    [InlineData("/app/changes")]
    [InlineData("/app/cmdb")]
    [InlineData("/app/kb")]
    [InlineData("/app/new")]
    [InlineData("/app/record/1")]
    [InlineData("/files")]       // hybrid drag-and-drop file manager
    [InlineData("/calendar")]    // inline month calendar (single + range)
    [InlineData("/scheduler")]   // scheduler month/day/week views
    [InlineData("/docviewer")]   // PDF / document viewer
    [InlineData("/excel")]       // read-only virtualized spreadsheet viewer
    [InlineData("/excel-edit")]  // editable spreadsheet: cell edit + formula recalc
    [InlineData("/word")]        // read-only semantic Word document viewer
    [InlineData("/word-edit")]   // round-trip .docx editor over the rich-text surface
    [InlineData("/htmx/doc")]            // static-SSR + HTMX read-only doc viewer (Excel default)
    [InlineData("/htmx/doc?kind=word")]  // ... Word semantic HTML
    [InlineData("/htmx/doc?kind=pdf")]   // ... PDF embed shell + download fallback
    [InlineData("/reports")]             // static-SSR + HTMX SSRS report viewer (embed + parameter form)
    [InlineData("/powerbi")]             // interactive Power BI embed (wrapper container, loading/error)
    [InlineData("/charts")]              // all 25 chart types, incl. interactive selection (Bar/Treemap/Network graph)
    [InlineData("/insights/articles/zero-trust-ephemeral-chat-conduit")]  // flagship editorial piece: hero, scrollytelling, sidebars, pull quotes, TOC
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
