using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>TreeView (expand/collapse/select) and Splitter (resizable panes).</summary>
public sealed class TreeSplitterTests : TestContext
{
    private static List<TreeNode> SampleTree() =>
    [
        new("src", new List<TreeNode> { new("App.cs"), new("Program.cs") }),
        new("docs", new List<TreeNode> { new("README.md") }),
    ];

    [Fact]
    public void TreeView_collapsed_shows_only_roots_then_expands_on_twisty_click()
    {
        IRenderedComponent<DxTreeView> tree = RenderComponent<DxTreeView>(p => p
            .Add(t => t.Items, SampleTree()));

        Assert.Equal(2, tree.FindAll("[role=treeitem]").Count);   // src, docs
        Assert.Equal("tree", tree.Find("[role=tree]").GetAttribute("role"));

        tree.FindAll(".dx-tree-twisty")[0].Click();   // expand "src"
        Assert.Equal(4, tree.FindAll("[role=treeitem]").Count);   // src, App.cs, Program.cs, docs
    }

    [Fact]
    public void TreeView_initially_expanded_shows_every_node()
    {
        IRenderedComponent<DxTreeView> tree = RenderComponent<DxTreeView>(p => p
            .Add(t => t.Items, SampleTree())
            .Add(t => t.InitiallyExpanded, true));

        Assert.Equal(5, tree.FindAll("[role=treeitem]").Count);
    }

    [Fact]
    public void TreeView_clicking_a_row_selects_it()
    {
        TreeNode? selected = null;
        IRenderedComponent<DxTreeView> tree = RenderComponent<DxTreeView>(p => p
            .Add(t => t.Items, SampleTree())
            .Add(t => t.SelectedChanged, EventCallback.Factory.Create<TreeNode>(this, n => selected = n)));

        tree.FindAll(".dx-tree-row")[0].Click();   // "src"

        Assert.Equal("src", selected?.Text);

        // Selected is a controlled parameter (consumers use @bind-Selected); push the
        // bound value back, as the framework would, and the row shows as selected.
        tree.SetParametersAndRender(p => p.Add(t => t.Selected, selected));
        Assert.Single(tree.FindAll(".dx-tree-selected"));
    }

    [Fact]
    public void Splitter_sizes_the_first_pane_and_exposes_a_separator()
    {
        IRenderedComponent<DxSplitter> splitter = RenderComponent<DxSplitter>(p => p
            .Add(s => s.InitialSize, 200.0)
            .Add(s => s.First, "left")
            .Add(s => s.Second, "right"));

        Assert.Contains("200px", splitter.Find(".dx-splitter-pane").GetAttribute("style")!);
        var divider = splitter.Find(".dx-splitter-divider");
        Assert.Equal("separator", divider.GetAttribute("role"));
        Assert.Equal("vertical", divider.GetAttribute("aria-orientation"));   // horizontal layout → vertical divider
    }

    [Fact]
    public void Splitter_arrow_keys_resize_and_drag_shows_the_overlay()
    {
        IRenderedComponent<DxSplitter> splitter = RenderComponent<DxSplitter>(p => p
            .Add(s => s.InitialSize, 200.0)
            .Add(s => s.Step, 16.0)
            .Add(s => s.First, "left")
            .Add(s => s.Second, "right"));

        splitter.Find(".dx-splitter-divider").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Contains("216px", splitter.Find(".dx-splitter-pane").GetAttribute("style")!);

        Assert.Empty(splitter.FindAll(".dx-splitter-resize-overlay"));
        splitter.Find(".dx-splitter-divider").PointerDown(new PointerEventArgs());
        Assert.Single(splitter.FindAll(".dx-splitter-resize-overlay"));
    }
}
