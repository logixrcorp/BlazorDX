using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// The acceptance gap bUnit cannot close for the file manager: native HTML5
/// drag-and-drop and its always-available, drag-free equivalents. bUnit has no
/// real DOM event loop, so a true <c>dragover</c>/<c>drop</c> (and the
/// browser's "is this drop accepted?" gate) can only be exercised in a real
/// browser. These tests drive the <c>/files</c> route and assert:
/// <list type="bullet">
///   <item>a native DnD move actually relocates an item in the DOM (and that the
///   drop is accepted because <c>dragover</c>'s default is prevented);</item>
///   <item>the keyboard / single-pointer "mark then place" move (WCAG 2.5.7)
///   relocates an item and announces it via <c>role=status</c> — no drag at all;</item>
///   <item>the standard <c>&lt;input type=file&gt;</c> upload control is present and
///   operable, confirming DnD is enhancement-only.</item>
/// </list>
/// </summary>
[Collection("e2e")]
public sealed class FileManagerE2ETests(PlaywrightFixture fx)
{
    // Native HTML5 move via DnD: dispatch a real dragover (asserting the drop
    // target prevents its default, so the browser accepts the drop) then a
    // dragstart on a file row and a drop onto a folder row in the same pane.
    // Assert the file left its origin folder and now lives under the target —
    // the DOM reflects the relocation. This is the move counterpart to the
    // reorder bug DragDropE2ETests guards: without preventDefault on dragover,
    // the browser denies the drop and nothing moves.
    [SkippableFact]
    public async Task FileManager_native_DnD_move_relocates_item_in_dom()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/files", ".dx-fm-content-row");

        // At the root the contents pane lists the two top-level folders (src, docs)
        // and README.md. Move README.md (a file row) onto the "docs" folder row,
        // then verify README.md is gone from the root listing and present once we
        // open docs. We address rows by their visible name to stay robust to order.
        JsonElement result = await page.EvaluateAsync<JsonElement>("""
            () => {
              const rows = () => [...document.querySelectorAll('.dx-fm-content-row')];
              const nameOf = (row) => row.querySelector('.dx-fm-name').textContent.trim();
              const find = (needle) => rows().find(r => nameOf(r).includes(needle));

              const file = find('README.md');
              const folder = find('docs');
              if (!file || !folder) {
                return { ok: false, reason: 'fixtures not found', names: rows().map(nameOf) };
              }

              const dt = new DataTransfer();
              const over = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt });
              folder.dispatchEvent(over);

              file.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt }));
              folder.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));

              return { ok: true, prevented: over.defaultPrevented, rootNames: rows().map(nameOf) };
            }
            """);

        Assert.True(result.GetProperty("ok").GetBoolean(),
            $"Expected fixture rows not present: {result}");
        Assert.True(result.GetProperty("prevented").GetBoolean(),
            "dragover default was not prevented on the folder drop target — the browser denies the drop (the move would silently fail).");

        // The drop handler moves the item and re-renders asynchronously; wait for
        // README.md to disappear from the root listing.
        await page.WaitForFunctionAsync("""
            () => {
              const names = [...document.querySelectorAll('.dx-fm-content-row .dx-fm-name')]
                .map(n => n.textContent.trim());
              return !names.some(n => n.includes('README.md'));
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        // Open the docs folder (double-click drills in) and confirm README.md landed there.
        await page.EvalOnSelectorAsync(".dx-fm-content-row .dx-fm-name",
            """
            (_) => {
              const rows = [...document.querySelectorAll('.dx-fm-content-row')];
              const docs = rows.find(r => r.querySelector('.dx-fm-name').textContent.trim().includes('docs'));
              docs.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
            }
            """);

        await page.WaitForFunctionAsync("""
            () => {
              const names = [...document.querySelectorAll('.dx-fm-content-row .dx-fm-name')]
                .map(n => n.textContent.trim());
              return names.some(n => n.includes('README.md'));
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        string[] docsNames = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.dx-fm-content-row .dx-fm-name')].map(n => n.textContent.trim())");
        Assert.Contains(docsNames, n => n.Contains("README.md"));
    }

    // Keyboard / single-pointer move alternative (WCAG 2.5.7): no dragging at all.
    // Click a file row's "Move" toggle (the ↔ button, .dx-fm-action), which arms
    // the move and announces it; then click a "Move here" target (.dx-fm-move-here)
    // on a destination folder. Assert the item relocated and that the role=status
    // region announced the completed move.
    [SkippableFact]
    public async Task FileManager_keyboard_move_relocates_item_and_announces()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/files", ".dx-fm-content-row");

        // Arm the move on README.md via its row's ↔ action button (a click, not a drag).
        ILocator readmeRow = page.Locator(".dx-fm-content-row")
            .Filter(new LocatorFilterOptions { HasText = "README.md" });
        await readmeRow.Locator(".dx-fm-action").ClickAsync();

        // Arming should announce readiness in the polite status region.
        ILocator status = page.Locator(".dx-fm-status[role=status]");
        await page.WaitForFunctionAsync("""
            () => {
              const s = document.querySelector('.dx-fm-status[role=status]');
              return s && /ready to move/i.test(s.textContent);
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        // "Move here" affordances appear only while a move is armed: one on the root
        // breadcrumb and one per eligible tree folder. Click the tree's "docs" target.
        ILocator docsMoveHere = page.Locator(".dx-fm-tree .dx-fm-node")
            .Filter(new LocatorFilterOptions { HasText = "docs" })
            .Locator(".dx-fm-move-here");
        await docsMoveHere.First.ClickAsync();

        // The completed move is announced ("Moved README.md to docs.").
        await page.WaitForFunctionAsync("""
            () => {
              const s = document.querySelector('.dx-fm-status[role=status]');
              return s && /Moved\s+README\.md/i.test(s.textContent);
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        // README.md is gone from the root listing — the move actually happened in the model.
        await page.WaitForFunctionAsync("""
            () => {
              const names = [...document.querySelectorAll('.dx-fm-content-row .dx-fm-name')]
                .map(n => n.textContent.trim());
              return !names.some(n => n.includes('README.md'));
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        string statusText = (await status.TextContentAsync()) ?? string.Empty;
        Assert.Contains("README.md", statusText);
        Assert.Contains("docs", statusText);
    }

    // Upload without drag: the always-available path is a standard InputFile, i.e.
    // an <input type=file>. Assert it is present and operable — driving it with
    // setInputFiles (no drag) selects files and fires the component's OnChange,
    // which announces the upload. This confirms DnD is enhancement-only: uploads
    // work entirely through the native control.
    [SkippableFact]
    public async Task FileManager_upload_input_is_present_and_operable_without_drag()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoInteractiveAsync($"{fx.BaseUrl}/files", ".dx-fm-content-row");

        // The standard upload control: InputFile renders a real <input type=file>.
        ILocator fileInput = page.Locator(".dx-fm-upload input[type=file]");
        Assert.Equal(1, await fileInput.CountAsync());

        // Operate it without any drag: hand it an in-memory file via setInputFiles.
        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = "notes.txt",
            MimeType = "text/plain",
            Buffer = System.Text.Encoding.UTF8.GetBytes("hello from e2e"),
        });

        // OnChange fires and the component announces the upload in the status region.
        await page.WaitForFunctionAsync("""
            () => {
              const s = document.querySelector('.dx-fm-status[role=status]');
              return s && /Uploaded\s+1\s+file/i.test(s.textContent);
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    // NOTE (intentionally not covered): a *true OS-file drop* carrying real bytes
    // cannot be synthesized — DataTransfer.files is read-only and the only way to
    // populate it is a genuine OS drag from outside the browser, which Playwright
    // cannot originate. The drag-free upload test above covers the same .NET code
    // path (RaiseDrop/AnnounceUpload) with real file bytes via setInputFiles. A
    // JS-disabled page is likewise out of scope here: /files is InteractiveWASM, so
    // the DnD/upload enhancements require JS by definition (the static-SSR no-JS
    // navigation fallback is a separate concern — see ADR 0013).
}
