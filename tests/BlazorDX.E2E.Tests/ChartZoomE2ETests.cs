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

        ILocator chart = page.Locator(".dx-chart-zoomable").First;
        string? fullViewBox = await chart.GetAttributeAsync("viewBox");

        double scrollBefore = await page.EvaluateAsync<double>("() => window.scrollY");

        var box = await chart.BoundingBoxAsync();
        Assert.NotNull(box);
        await page.Mouse.MoveAsync(box!.X + (box.Width / 2), box.Y + (box.Height / 2));
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

        ILocator chart = page.Locator(".dx-chart-zoomable").First;
        var box = await chart.BoundingBoxAsync();
        Assert.NotNull(box);
        float centerY = box!.Y + (box.Height / 2);

        // Zoom in first — panning at full zoom-out has nowhere to go (both edges already touch).
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), centerY);
        await page.Mouse.WheelAsync(0, -600);
        await page.WaitForTimeoutAsync(200); // let the render settle

        string? beforePan = await chart.GetAttributeAsync("viewBox");

        // A real drag: down inside the chart, move across (still within the page — the pan
        // overlay is a full-viewport element so this is a realistic gesture), up.
        await page.Mouse.MoveAsync(box.X + (box.Width * 0.3f), centerY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + (box.Width * 0.7f), centerY, new MouseMoveOptions { Steps = 5 });
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
}
