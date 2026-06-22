using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// Verifies the image editor in a real browser: that the canvas filter actually
/// rewrites pixels (not just applies a CSS class) — something bUnit, which has no
/// canvas, cannot check.
/// </summary>
[Collection("e2e")]
public sealed class ImageEditorE2ETests(PlaywrightFixture fx)
{
    [SkippableFact]
    public async Task Grayscale_rewrites_canvas_pixels()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        // The editor desaturates via the Canvas2D `ctx.filter = "grayscale(100%)"` API.
        // Playwright's WebKit build does not reliably apply canvas context filters, so the
        // pixels stay colored there. The behavior is verified on Chromium and Firefox.
        Skip.If(fx.BrowserName == "webkit",
            "Canvas2D ctx.filter (grayscale) is not reliably applied in Playwright's WebKit.");
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/imageeditor", "canvas.dx-imgedit-canvas");

        // Wait until the sample image has actually painted (non-zero canvas).
        await page.WaitForFunctionAsync(
            "() => { const c = document.querySelector('canvas.dx-imgedit-canvas'); return c && c.width > 0; }",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });

        bool isGray = await page.EvaluateAsync<bool>("""
            async () => {
              const btns = [...document.querySelectorAll('.dx-imgedit-btn')];
              btns.find(b => b.textContent.trim() === 'Grayscale').click();
              await new Promise(r => setTimeout(r, 400));
              const c = document.querySelector('canvas.dx-imgedit-canvas');
              const p = c.getContext('2d').getImageData(Math.floor(c.width / 2), Math.floor(c.height / 2), 1, 1).data;
              return Math.abs(p[0] - p[1]) < 4 && Math.abs(p[1] - p[2]) < 4;   // R≈G≈B → grayscale
            }
            """);

        Assert.True(isGray, "Grayscale did not desaturate the canvas pixels.");
    }
}
