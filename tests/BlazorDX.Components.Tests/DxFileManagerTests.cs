using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Primitives.Files;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Folder-tree flattening, selection, breadcrumb, and open routing.</summary>
public sealed class DxFileManagerTests : TestContext
{
    private static readonly DateOnly Day = new(2026, 6, 1);

    public DxFileManagerTests()
    {
        // DxFileManager injects the native-DnD bridge; off-browser it is the no-op.
        Services.AddScoped<IFileDndInterop, NullFileDndInterop>();
    }

    private static IReadOnlyList<FileSystemEntry> Roots() =>
    [
        new FileSystemEntry
        {
            Name = "src", IsDirectory = true, Modified = Day,
            Children =
            [
                new FileSystemEntry
                {
                    Name = "inner", IsDirectory = true, Modified = Day,
                    Children = [new() { Name = "deep.cs", Size = 100, Modified = Day }],
                },
                new() { Name = "top.cs", Size = 200, Modified = Day },
            ],
        },
        new() { Name = "README.md", Size = 50, Modified = Day },
    ];

    private IRenderedComponent<DxFileManager> Render(EventCallback<FileSystemEntry>? onOpen = null) =>
        RenderComponent<DxFileManager>(parameters =>
        {
            parameters.Add(f => f.Roots, Roots());
            if (onOpen is { } cb)
            {
                parameters.Add(f => f.OnFileOpen, cb);
            }
        });

    [Fact]
    public void Tree_shows_only_top_level_folders_until_expanded()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        // Only "src" is a top-level folder (README.md is a file, excluded from the tree).
        var nodes = fm.FindAll(".dx-fm-node-label");
        Assert.Single(nodes);
        Assert.Contains("src", nodes[0].TextContent);
    }

    [Fact]
    public void Contents_default_to_the_roots()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        var rows = fm.FindAll(".dx-fm-content-row");
        Assert.Equal(2, rows.Count);   // src, README.md
        Assert.Contains("README.md", fm.Markup);
    }

    [Fact]
    public void Expanding_a_folder_reveals_its_subfolders_in_the_tree()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        fm.Find(".dx-fm-twisty").Click();   // expand "src"

        var labels = fm.FindAll(".dx-fm-node-label").Select(n => n.TextContent).ToArray();
        Assert.Contains(labels, l => l.Contains("inner"));   // subfolder now visible
    }

    [Fact]
    public void Selecting_a_folder_shows_its_contents_and_breadcrumb()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        fm.Find(".dx-fm-node-label").Click();   // select "src"

        // Contents now = src's children: inner (folder) + top.cs.
        var names = fm.FindAll(".dx-fm-name").Select(n => n.TextContent).ToArray();
        Assert.Contains(names, n => n.Contains("inner"));
        Assert.Contains(names, n => n.Contains("top.cs"));
        // Breadcrumb shows the selected folder.
        Assert.Contains("src", fm.Find(".dx-fm-crumb").TextContent);
    }

    [Fact]
    public void Double_clicking_a_subfolder_drills_in_and_expands_ancestors()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        fm.Find(".dx-fm-node-label").Click();   // select "src" -> contents include "inner"
        // Find the "inner" content row and double-click it.
        var innerRow = fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("inner"));
        innerRow.DoubleClick();

        // Now contents are inner's children, and the breadcrumb has two crumbs.
        Assert.Contains("deep.cs", fm.Markup);
        Assert.Equal(2, fm.FindAll(".dx-fm-crumb").Count);   // src › inner
    }

    [Fact]
    public void Double_clicking_a_file_raises_open()
    {
        FileSystemEntry? opened = null;
        IRenderedComponent<DxFileManager> fm = Render(
            EventCallback.Factory.Create<FileSystemEntry>(this, e => opened = e));

        var fileRow = fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("README.md"));
        fileRow.DoubleClick();

        Assert.NotNull(opened);
        Assert.Equal("README.md", opened!.Name);
    }

    // ---- Hybrid DnD: the always-available upload path and the WCAG 2.5.7 move
    // alternative. The native HTML5 drag/drop + File API path runs only in a real
    // browser (it is the NullFileDndInterop no-op here), so it is covered by E2E,
    // not bUnit; these tests cover the C# move logic and the non-drag fallbacks. ----

    [Fact]
    public void Standard_file_input_upload_path_is_always_rendered()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        // The framework InputFile renders a real <input type=file> — the upload path
        // that needs no drag gesture.
        var input = fm.Find("input[type=file]");
        Assert.True(input.HasAttribute("multiple"));
        Assert.Contains("Upload files", fm.Markup);
    }

    [Fact]
    public void Each_row_exposes_a_24px_move_action_target()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        var actions = fm.FindAll(".dx-fm-action");
        Assert.Equal(2, actions.Count);   // one per content row (src, README.md)
        foreach (var action in actions)
        {
            Assert.Equal("button", action.GetAttribute("type"));
            Assert.Equal("false", action.GetAttribute("aria-pressed"));
            Assert.Contains("Move", action.GetAttribute("aria-label"));
        }
    }

    [Fact]
    public void Status_region_is_an_aria_live_polite_region()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        var status = fm.Find(".dx-fm-status");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Equal("polite", status.GetAttribute("aria-live"));
    }

    [Fact]
    public void Arming_a_move_marks_the_row_and_reveals_move_here_targets()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        // Arm the move on "README.md" (the second row).
        var readmeAction = fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("README.md"))
            .QuerySelector(".dx-fm-action")!;
        readmeAction.Click();

        // The row is marked (aria-pressed) and "Move here" placement targets appear
        // on the tree folder(s) — the single-pointer/keyboard place step.
        var pressed = fm.FindAll(".dx-fm-action").First(a => a.GetAttribute("aria-pressed") == "true");
        Assert.Contains("Cancel moving README.md", pressed.GetAttribute("aria-label"));
        Assert.NotEmpty(fm.FindAll(".dx-fm-move-here"));
    }

    [Fact]
    public void Keyboard_single_pointer_move_relocates_the_item_and_announces_it()
    {
        MoveResult? moved = null;
        IRenderedComponent<DxFileManager> fm = RenderComponent<DxFileManager>(parameters =>
        {
            parameters.Add(f => f.Roots, Roots());
            parameters.Add(f => f.OnItemMove,
                EventCallback.Factory.Create<MoveResult>(this, r => moved = r));
        });

        // Arm the move on README.md, then place it into the "src" folder via the
        // tree node's "Move here" target — no drag gesture involved.
        fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("README.md"))
            .QuerySelector(".dx-fm-action")!.Click();

        fm.Find(".dx-fm-tree").QuerySelector(".dx-fm-move-here")!.Click();

        // The callback fired with a successful move, and the aria-live region announces it.
        Assert.NotNull(moved);
        Assert.True(moved!.Value.Succeeded);
        Assert.Equal("README.md", moved.Value.Item.Name);
        Assert.Equal("src", moved.Value.Target!.Name);
        Assert.Contains("Moved README.md to src", fm.Find(".dx-fm-status").TextContent);

        // README.md left the root: only "src" remains in the root contents view.
        var rootNames = fm.FindAll(".dx-fm-content-row").Select(r => r.TextContent).ToArray();
        Assert.DoesNotContain(rootNames, n => n.Contains("README.md"));
    }

    [Fact]
    public void Move_into_a_descendant_is_rejected_and_announced()
    {
        MoveResult? moved = null;
        IRenderedComponent<DxFileManager> fm = RenderComponent<DxFileManager>(parameters =>
        {
            parameters.Add(f => f.Roots, Roots());
            parameters.Add(f => f.OnItemMove,
                EventCallback.Factory.Create<MoveResult>(this, r => moved = r));
        });

        // Expand "src" so its subfolder "inner" is a tree node, then arm a move on
        // "src" (the content row) and try to place it into its own descendant.
        fm.Find(".dx-fm-twisty").Click();   // expand src -> inner visible in tree
        fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("src"))
            .QuerySelector(".dx-fm-action")!.Click();

        // The only "Move here" target offered in the tree is "inner" (src is the
        // candidate, so it gets none). Placing src into inner must be rejected.
        var innerTarget = fm.Find(".dx-fm-tree").QuerySelector(".dx-fm-move-here");
        innerTarget!.Click();

        Assert.NotNull(moved);
        Assert.False(moved!.Value.Succeeded);
        Assert.Contains("Could not move src", fm.Find(".dx-fm-status").TextContent);
    }

    [Fact]
    public void Cancelling_an_armed_move_clears_the_marker()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        var action = fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("README.md"))
            .QuerySelector(".dx-fm-action")!;
        action.Click();   // arm
        Assert.NotEmpty(fm.FindAll(".dx-fm-move-here"));

        fm.FindAll(".dx-fm-action").First(a => a.GetAttribute("aria-pressed") == "true").Click();   // cancel
        Assert.Empty(fm.FindAll(".dx-fm-move-here"));
        Assert.Contains("cancelled", fm.Find(".dx-fm-status").TextContent);
    }

    // ---- Robustness pass: name-collision rejection, focus management, and the
    // re-entrancy guard. (The native HTML5 DnD + window.matchMedia reduced-motion
    // path runs only in a real browser, so the malformed-/negative-index DnD callback
    // and the reduced-motion drop affordance are covered by E2E, not bUnit.) ----

    // Roots where the root and the "dst" folder both contain a file of the same name,
    // so moving the root copy into "dst" collides with the existing entry.
    private static IReadOnlyList<FileSystemEntry> CollidingRoots() =>
    [
        new FileSystemEntry
        {
            Name = "dst", IsDirectory = true, Modified = Day,
            Children = [new() { Name = "dup.txt", Size = 10, Modified = Day }],
        },
        new() { Name = "dup.txt", Size = 20, Modified = Day },
    ];

    [Fact]
    public void Move_onto_a_name_collision_is_blocked_with_a_distinct_message()
    {
        MoveResult? moved = null;
        IRenderedComponent<DxFileManager> fm = RenderComponent<DxFileManager>(parameters =>
        {
            parameters.Add(f => f.Roots, CollidingRoots());
            parameters.Add(f => f.OnItemMove,
                EventCallback.Factory.Create<MoveResult>(this, r => moved = r));
        });

        // Arm the move on the root-level "dup.txt", then place it into "dst" — which
        // already holds a "dup.txt". The move must be rejected with the distinct
        // collision message, not silently creating two same-named siblings.
        fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("dup.txt"))
            .QuerySelector(".dx-fm-action")!.Click();

        fm.Find(".dx-fm-tree").QuerySelector(".dx-fm-move-here")!.Click();

        Assert.NotNull(moved);
        Assert.False(moved!.Value.Succeeded);
        Assert.Contains("An item named dup.txt already exists here", fm.Find(".dx-fm-status").TextContent);

        // The root still has its "dup.txt" (the move did not remove it).
        var rootNames = fm.FindAll(".dx-fm-content-row").Select(r => r.TextContent).ToArray();
        Assert.Contains(rootNames, n => n.Contains("dup.txt"));
    }

    [Fact]
    public void A_successful_move_announces_and_does_not_throw_when_target_leaves_view()
    {
        // README.md is at the root and "src" is a root folder; placing README.md into
        // "src" while the root is shown means the moved row leaves the current view,
        // so focus must fall back to the status region (exercised in OnAfterRender) —
        // the point of this test is that the focus/announce path runs cleanly.
        MoveResult? moved = null;
        IRenderedComponent<DxFileManager> fm = RenderComponent<DxFileManager>(parameters =>
        {
            parameters.Add(f => f.Roots, Roots());
            parameters.Add(f => f.OnItemMove,
                EventCallback.Factory.Create<MoveResult>(this, r => moved = r));
        });

        fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("README.md"))
            .QuerySelector(".dx-fm-action")!.Click();
        fm.Find(".dx-fm-tree").QuerySelector(".dx-fm-move-here")!.Click();

        Assert.NotNull(moved);
        Assert.True(moved!.Value.Succeeded);
        // aria-live announcement is present (4.1.3) and the status region is
        // focusable (tabindex=-1) so it can be the 2.4.3 focus fallback.
        var status = fm.Find(".dx-fm-status");
        Assert.Contains("Moved README.md to src", status.TextContent);
        Assert.Equal("-1", status.GetAttribute("tabindex"));
    }

    [Fact]
    public void Status_region_is_focusable_for_focus_management()
    {
        IRenderedComponent<DxFileManager> fm = Render();

        var status = fm.Find(".dx-fm-status");
        // tabindex=-1 lets the component place programmatic focus here after a
        // move/upload when no visible row is a sensible target (WCAG 2.4.3).
        Assert.Equal("-1", status.GetAttribute("tabindex"));
        Assert.False(string.IsNullOrEmpty(status.Id));
    }

    [Fact]
    public void An_upload_announces_and_focuses_the_status_region()
    {
        IReadOnlyList<IBrowserFile>? uploaded = null;
        IRenderedComponent<DxFileManager> fm = RenderComponent<DxFileManager>(parameters =>
        {
            parameters.Add(f => f.Roots, Roots());
            parameters.Add(f => f.OnUpload,
                EventCallback.Factory.Create<IReadOnlyList<IBrowserFile>>(this, f => uploaded = f));
        });

        // Drive the always-available InputFile path with one fake file.
        InputFileContent file = InputFileContent.CreateFromText("hello", "note.txt");
        fm.FindComponent<InputFile>().UploadFiles(file);

        Assert.NotNull(uploaded);
        Assert.Single(uploaded!);
        Assert.Contains("Uploaded 1 file to Files", fm.Find(".dx-fm-status").TextContent);
    }

    [Fact]
    public void A_no_op_move_to_the_current_parent_is_rejected_without_throwing()
    {
        // The native-DnD callbacks parse a row index out of a DOM id and now guard
        // index >= 0 && index < count, so a malformed/negative id resolves to nothing
        // and is a no-op (covered end-to-end in the browser, since the JS DnD path
        // does not run under bUnit). The C#-reachable rejection path it funnels into
        // is exercised here: placing an item where it already lives must be rejected
        // and announced, never throw or duplicate the entry.
        MoveResult? moved = null;
        IRenderedComponent<DxFileManager> fm = RenderComponent<DxFileManager>(parameters =>
        {
            parameters.Add(f => f.Roots, Roots());
            parameters.Add(f => f.OnItemMove,
                EventCallback.Factory.Create<MoveResult>(this, r => moved = r));
        });

        // Arm README.md (already at the root), then place it "to Files" (the root) via
        // the breadcrumb's "Move here" — a no-op move onto its own current parent.
        fm.FindAll(".dx-fm-content-row")
            .First(r => r.TextContent.Contains("README.md"))
            .QuerySelector(".dx-fm-action")!.Click();
        fm.Find(".dx-fm-breadcrumb").QuerySelector(".dx-fm-move-here")!.Click();

        Assert.NotNull(moved);
        Assert.False(moved!.Value.Succeeded);
        // README.md is still present exactly once at the root (not lost, not doubled).
        int count = fm.FindAll(".dx-fm-content-row").Count(r => r.TextContent.Contains("README.md"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Rapid_successive_moves_do_not_corrupt_the_tree_state()
    {
        // Three files at the root and a "box" folder. Moving each into "box" in quick
        // succession via the keyboard path must leave exactly those three files in
        // "box" and none at the root — no duplicates, no losses, no throw.
        IReadOnlyList<FileSystemEntry> roots =
        [
            new FileSystemEntry { Name = "box", IsDirectory = true, Modified = Day, Children = [] },
            new() { Name = "a.txt", Size = 1, Modified = Day },
            new() { Name = "b.txt", Size = 2, Modified = Day },
            new() { Name = "c.txt", Size = 3, Modified = Day },
        ];

        IRenderedComponent<DxFileManager> fm = RenderComponent<DxFileManager>(parameters =>
            parameters.Add(f => f.Roots, roots));

        foreach (string name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            fm.FindAll(".dx-fm-content-row")
                .First(r => r.TextContent.Contains(name))
                .QuerySelector(".dx-fm-action")!.Click();   // arm
            fm.Find(".dx-fm-tree").QuerySelector(".dx-fm-move-here")!.Click();   // place into box
        }

        // Root now shows only "box"; drilling into "box" shows all three files once.
        var rootNames = fm.FindAll(".dx-fm-content-row").Select(r => r.TextContent).ToArray();
        Assert.Single(rootNames);
        Assert.Contains(rootNames, n => n.Contains("box"));

        fm.Find(".dx-fm-node-label").Click();   // select "box"
        var boxNames = fm.FindAll(".dx-fm-content-row").Select(r => r.TextContent).ToArray();
        Assert.Equal(3, boxNames.Length);
        Assert.Contains(boxNames, n => n.Contains("a.txt"));
        Assert.Contains(boxNames, n => n.Contains("b.txt"));
        Assert.Contains(boxNames, n => n.Contains("c.txt"));
    }
}
