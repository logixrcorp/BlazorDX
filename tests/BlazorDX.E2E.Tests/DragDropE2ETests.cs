using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// The regression suite for the drag-and-drop bug bUnit could not catch: in a real
/// browser, an HTML5 drop is DENIED unless the drop target prevents the dragover
/// default. These tests assert that prevention actually happens and that a drop
/// reorders — exactly the behavior that was silently broken.
/// </summary>
[Collection("e2e")]
public sealed class DragDropE2ETests(PlaywrightFixture fx)
{
    [SkippableFact]
    public async Task Sortable_prevents_dragover_default_and_reorders_on_drop()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/sortable", ".dx-sortable-item");

        // Dispatch a real dragover + a drag from item 0 onto item 2, capturing whether
        // the browser accepted the drop (dragover.defaultPrevented).
        JsonElement result = await page.EvaluateAsync<JsonElement>("""
            () => {
              const items = () => [...document.querySelectorAll('.dx-sortable-item')].map(i => i.textContent.trim());
              const nodes = () => [...document.querySelectorAll('.dx-sortable-item')];
              const before = items();
              const ov = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() });
              nodes()[2].dispatchEvent(ov);
              nodes()[0].dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() }));
              nodes()[2].dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() }));
              return { prevented: ov.defaultPrevented, before };
            }
            """);

        bool prevented = result.GetProperty("prevented").GetBoolean();
        string[] before = result.GetProperty("before").EnumerateArray().Select(e => e.GetString()!).ToArray();

        // The exact failure mode of the bug: without preventDefault the cursor shows
        // "denied" and no drop fires.
        Assert.True(prevented, "dragover default was not prevented — HTML5 drop is denied (the original bug).");

        // The drop handler reorders asynchronously; wait for the list to actually change.
        await page.WaitForFunctionAsync("""
            (first) => {
              const items = [...document.querySelectorAll('.dx-sortable-item')].map(i => i.textContent.trim());
              return items[0] !== first;
            }
            """, before[0], new PageWaitForFunctionOptions { Timeout = 5_000 });

        string[] after = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.dx-sortable-item')].map(i => i.textContent.trim())");

        Assert.NotEqual(before, after);
        Assert.NotEqual(before[0], after[0]);   // the grabbed item moved
    }

    [SkippableFact]
    public async Task Kanban_columns_accept_card_drops()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/kanban", ".dx-kanban-card");

        bool prevented = await page.EvaluateAsync<bool>("""
            () => {
              const card = document.querySelector('.dx-kanban-card');
              const ov = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() });
              card.dispatchEvent(ov);
              return ov.defaultPrevented;
            }
            """);

        Assert.True(prevented, "Kanban dragover default was not prevented — cards cannot be dropped.");
    }
}
