using BlazorDX.Components;
using BlazorDX.Primitives.Files;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Folder-tree flattening, selection, breadcrumb, and open routing.</summary>
public sealed class DxFileManagerTests : TestContext
{
    private static readonly DateOnly Day = new(2026, 6, 1);

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
}
