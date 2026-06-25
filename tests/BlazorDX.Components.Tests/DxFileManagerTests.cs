using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Primitives.Files;
using Bunit;
using Microsoft.AspNetCore.Components;
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
}
