using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// WCAG 2.5.8 Target Size (Minimum): interactive reorder controls must present at
/// least a 24x24 CSS-px hit area. axe-core cannot detect this — it needs real layout,
/// not just the DOM — so it is verified here against the live browser box model. This
/// is the automated guard for the target-size gap the accessibility audit flagged.
/// </summary>
[Collection("e2e")]
public sealed class TargetSizeE2ETests(PlaywrightFixture fx)
{
    private const double Min = 24.0;
    private const double Epsilon = 0.5;   // tolerate sub-pixel layout rounding

    [SkippableTheory]
    [InlineData("/sortable", ".dx-sortable-item", ".dx-sortable-move")]
    [InlineData("/tiles", ".dx-tile", ".dx-tile-move")]
    public async Task Reorder_controls_meet_target_size_minimum(string route, string ready, string target)
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}{route}", ready);

        await AssertAllAtLeastMinAsync(page, target);
    }

    [SkippableFact]
    public async Task Grid_column_chooser_move_buttons_meet_target_size_minimum()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/grid", ".dx-grid");

        // The no-drag reorder buttons live inside the column chooser; open it first.
        await page.ClickAsync(".dx-grid-chooser-toggle");
        await page.WaitForSelectorAsync(".dx-grid-chooser-move", new PageWaitForSelectorOptions { Timeout = 10_000 });

        await AssertAllAtLeastMinAsync(page, ".dx-grid-chooser-move");
    }

    private static async Task AssertAllAtLeastMinAsync(IPage page, string selector)
    {
        IReadOnlyList<IElementHandle> elements = await page.QuerySelectorAllAsync(selector);
        Assert.True(elements.Count > 0, $"No elements matched '{selector}'; the size check would pass vacuously.");

        foreach (IElementHandle element in elements)
        {
            var box = await element.BoundingBoxAsync();
            if (box is null)
            {
                continue;   // not laid out (e.g. off-screen) — nothing to measure
            }

            Assert.True(
                box.Width >= Min - Epsilon && box.Height >= Min - Epsilon,
                $"'{selector}' has a {box.Width}x{box.Height} CSS-px target; WCAG 2.5.8 requires at least 24x24.");
        }
    }
}
