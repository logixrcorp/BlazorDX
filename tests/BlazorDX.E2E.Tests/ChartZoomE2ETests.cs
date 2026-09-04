using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// A real wheel-zoom and a real drag-pan gesture against the actual browser, on the demo's
/// zoomable line chart (<c>/charts</c>). bUnit already covers the full gesture matrix (wheel,
/// drag, keyboard, reset) against synthetic event args — this exists to prove the real browser
/// wiring works too: actual event names, <c>preventDefault</c> genuinely stopping page scroll
/// under the wheel, and pointer capture surviving across the pan overlay. Narrow and deliberate,
/// the same role <see cref="AccessibilityE2ETests"/>'s axe-core sweep plays for a11y.
/// </summary>
[Collection("e2e")]
public sealed class ChartZoomE2ETests(PlaywrightFixture fx)
{
    [SkippableFact]
    public async Task Wheel_over_the_zoomable_line_chart_narrows_the_viewBox_and_does_not_scroll_the_page()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/charts", ".dx-chart-zoomable");

        // Measure first: settling the box is also what proves hydration finished (see ChartBoxAsync).
        (double x, double y, double width, double height) = await ChartBoxAsync(page);

        ILocator chart = page.Locator(".dx-chart-zoomable").First;
        string? fullViewBox = await chart.GetAttributeAsync("viewBox");

        double scrollBefore = await page.EvaluateAsync<double>("() => window.scrollY");

        await page.Mouse.MoveAsync((float)(x + (width / 2)), (float)(y + (height / 2)));
        await page.Mouse.WheelAsync(0, -300); // zoom in

        // The zoom is applied synchronously in the wheel handler, but Blazor's own render still
        // needs a tick to update the DOM attribute.
        await page.WaitForFunctionAsync(
            "([sel, old]) => document.querySelector(sel).getAttribute('viewBox') !== old",
            new object?[] { ".dx-chart-zoomable", fullViewBox });

        string? zoomedViewBox = await chart.GetAttributeAsync("viewBox");
        Assert.NotEqual(fullViewBox, zoomedViewBox);

        double scrollAfter = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.Equal(scrollBefore, scrollAfter); // preventDefault actually stopped the page scroll
    }

    [SkippableFact]
    public async Task Dragging_the_zoomed_in_chart_pans_it()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/charts", ".dx-chart-zoomable");

        (double x, double y, double width, double height) = await ChartBoxAsync(page);
        ILocator chart = page.Locator(".dx-chart-zoomable").First;
        float centerY = (float)(y + (height / 2));

        // Zoom in first — panning at full zoom-out has nowhere to go (both edges already touch).
        await page.Mouse.MoveAsync((float)(x + (width / 2)), centerY);
        await page.Mouse.WheelAsync(0, -600);
        await page.WaitForTimeoutAsync(200); // let the render settle

        string? beforePan = await chart.GetAttributeAsync("viewBox");

        // A real drag: down inside the chart, move across (still within the page — the pan
        // overlay is a full-viewport element so this is a realistic gesture), up.
        await page.Mouse.MoveAsync((float)(x + (width * 0.3)), centerY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(x + (width * 0.7)), centerY, new MouseMoveOptions { Steps = 5 });
        await page.Mouse.UpAsync();

        await page.WaitForFunctionAsync(
            "([sel, old]) => document.querySelector(sel).getAttribute('viewBox') !== old",
            new object?[] { ".dx-chart-zoomable", beforePan });

        string? afterPan = await chart.GetAttributeAsync("viewBox");
        Assert.NotEqual(beforePan, afterPan);

        // The pan overlay must not linger after pointerup, or it would block clicks on the rest
        // of the page.
        Assert.Equal(0, await page.Locator(".dx-chart-pan-overlay").CountAsync());
    }

    /// <summary>
    /// Scrolls the zoomable chart into view and returns its box, in one in-page call.
    /// </summary>
    /// <remarks>
    /// <c>/charts</c> is <c>@rendermode InteractiveWebAssembly</c>, so the server-prerendered DOM
    /// is discarded and rebuilt when the WASM runtime finishes its first render.
    /// <c>GotoInteractiveAsync</c> waits for <c>window.DotNet</c> and the ready selector, but
    /// <c>window.DotNet</c> exists <i>before</i> that first render completes — so the element the
    /// ready selector matched can be the prerendered one, and a locator captured against it is
    /// detached moments later. Playwright then throws "Element is not attached to the DOM" from
    /// <c>ScrollIntoViewIfNeededAsync</c> rather than re-resolving, which is what made these two
    /// tests fail intermittently on WebKit (the slowest of the three to boot WASM).
    /// <para>
    /// Doing the scroll and the measurement inside a single <c>WaitForFunctionAsync</c> removes
    /// the window entirely: returning <c>null</c> keeps it polling until the element both exists
    /// and has a non-zero box, and there is no "found" state for a re-render to invalidate.
    /// </para>
    /// </remarks>
    private static async Task<(double X, double Y, double Width, double Height)> ChartBoxAsync(IPage page)
    {
        IJSHandle handle = await page.WaitForFunctionAsync(
            """
            () => {
                const el = document.querySelector('.dx-chart-zoomable');
                if (!el) return null;
                el.scrollIntoView({ block: 'center' });
                const r = el.getBoundingClientRect();
                return r.width > 0 && r.height > 0 ? [r.x, r.y, r.width, r.height] : null;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        double[] box = await handle.JsonValueAsync<double[]>();
        return (box[0], box[1], box[2], box[3]);
    }
}
