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
    [InlineData("/dialog")]              // Overlays family (dx-overlay.css): Dialog, backed by DxDialog/DialogPrimitive
    [InlineData("/dialog?dir=rtl")]      // same route under dir="rtl" — ADR 0016's RTL pilot (logical-property CSS)
    // The RTL sweep, widened beyond the pilot. axe reports direction-independent failures
    // (contrast, names, roles), so these catch mirroring that breaks semantics — an overlapped or
    // clipped control loses its accessible name — not mirroring that merely looks wrong. The three
    // routes are the densest directional layouts in the showcase.
    [InlineData("/scheduler?dir=rtl")]   // grid of day columns — the most direction-sensitive layout
    [InlineData("/app/records?dir=rtl")] // DxDataGrid: sort/filter affordances, sticky header, toolbar
    [InlineData("/charts?dir=rtl")]      // chart geometry is C#-computed, so RTL is unreviewed here by construction
    // Added once every stylesheet was converted. These exercise the sheets whose bare
    // left/right positioning was flipped rather than merely mapped — the judgment calls, which
    // is where a conversion mistake would actually show.
    [InlineData("/controls?dir=rtl")]    // dx-input: DxPassword's affixed reveal button
    [InlineData("/files?dir=rtl")]       // dx-filemanager, plus dx-layout's toast host
    [InlineData("/excel?dir=rtl")]       // dx-spreadsheet: the sticky row-number gutter
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

    /// <summary>
    /// The one RTL assertion about <i>layout</i> rather than semantics: a control's directional
    /// order actually mirrors.
    /// </summary>
    /// <remarks>
    /// Every other RTL check in the suite is axe-based, and axe is direction-agnostic — a
    /// stylesheet could be entirely unconverted and pass all of them. <c>RtlLogicalPropertyTests</c>
    /// closes most of that gap statically, but it can only read CSS text; it cannot tell whether a
    /// browser actually flipped anything. This is the end-to-end half.
    /// <para>
    /// <c>DxSelect</c>'s trigger is the subject because it is the simplest control in the pilot
    /// stylesheet with an unambiguous leading/trailing pair: <c>.dx-select-value</c> then
    /// <c>.dx-select-caret</c>, laid out by <c>justify-content: space-between</c>. If anyone
    /// re-physicalizes that rule — a <c>float</c>, an absolute <c>left:</c>, a fixed
    /// <c>flex-direction: row</c> — the caret stops mirroring and this fails.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Select_trigger_mirrors_its_caret_under_rtl()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);

        (double value, double caret, string direction) ltr = await MeasureSelectTriggerAsync("/select");
        (double value, double caret, string direction) rtl = await MeasureSelectTriggerAsync("/select?dir=rtl");

        // Guard against the whole test passing vacuously by comparing two LTR pages: if ?dir=rtl
        // ever stops reaching App.razor's <html dir> the mirroring assertion below is meaningless.
        Assert.Equal("ltr", ltr.direction);
        Assert.Equal("rtl", rtl.direction);

        Assert.True(ltr.caret > ltr.value, $"LTR: caret ({ltr.caret}) should sit after the value ({ltr.value}).");
        Assert.True(rtl.caret < rtl.value, $"RTL: caret ({rtl.caret}) should sit before the value ({rtl.value}).");
    }

    private async Task<(double Value, double Caret, string Direction)> MeasureSelectTriggerAsync(string route)
    {
        IPage page = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

        // /select is @rendermode InteractiveWebAssembly: the server-prerendered markup is thrown
        // away and re-created when the WASM runtime finishes booting. Waiting for an element and
        // then measuring it in a second call straddles that swap — the node found by the wait can
        // be detached by the time it is measured, and BoundingBoxAsync returns null for a detached
        // node instead of re-resolving. That is what made this Firefox-only and intermittent:
        // Firefox is the slowest of the three to start WASM.
        await page.WaitForFunctionAsync("() => !!window.DotNet", null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });

        // So take one atomic in-page measurement, polled until both boxes are laid out. Returning
        // null keeps WaitForFunctionAsync polling, so there is no window between "found" and
        // "measured" for a re-render to slip into.
        //
        // Centres, not edges: the two boxes have different widths, so comparing left edges would
        // report an ordering difference that is really a width difference.
        IJSHandle measurement = await page.WaitForFunctionAsync(
            """
            () => {
                const value = document.querySelector('.dx-select-trigger .dx-select-value');
                const caret = document.querySelector('.dx-select-trigger .dx-select-caret');
                if (!value || !caret) return null;
                const v = value.getBoundingClientRect();
                const c = caret.getBoundingClientRect();
                if (v.width === 0 || c.width === 0) return null;
                return [v.x + (v.width / 2), c.x + (c.width / 2)];
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        double[] centres = await measurement.JsonValueAsync<double[]>();
        string direction = await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).direction");

        return (centres[0], centres[1], direction);
    }
}
