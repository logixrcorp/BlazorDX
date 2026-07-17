using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The four Tier-2 chart types: Treemap, Sunburst, Box/Violin plot, Sankey. Each is exercised at
/// the component level (not just its layout primitive — see ChartLayoutPrimitivesTests) to prove
/// the flatten/render/selection wiring is correct.
/// </summary>
public sealed class DxTier2ChartsTests : TestContext
{
    public DxTier2ChartsTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    // ---- DxTreemap ----

    private static ChartTreeNode SampleTree() => new("All", Children:
    [
        new("Web", Children: [new("Search", Value: 40), new("Social", Value: 20)]),
        new("Mobile", Value: 30),
    ]);

    [Fact]
    public void Treemap_renders_one_cell_per_leaf()
    {
        IRenderedComponent<DxTreemap> chart = RenderComponent<DxTreemap>(p => p.Add(c => c.Root, SampleTree()));

        Assert.Equal(3, chart.FindAll("rect.dx-treemap-cell").Count);   // Search, Social, Mobile
    }

    [Fact]
    public void Treemap_click_reports_the_node_and_its_breadcrumb_path()
    {
        ChartTreeNodeEventArgs? selected = null;
        IRenderedComponent<DxTreemap> chart = RenderComponent<DxTreemap>(p => p
            .Add(c => c.Root, SampleTree())
            .Add(c => c.OnNodeSelected, e => selected = e));

        chart.Find("rect.dx-treemap-cell[tabindex]").Click();

        Assert.NotNull(selected);
        Assert.Equal(["Web", "Search"], selected!.Value.Path);
        Assert.Equal("Search", selected.Value.Node.Label);
    }

    [Fact]
    public void Treemap_with_no_children_renders_no_cells()
    {
        IRenderedComponent<DxTreemap> chart = RenderComponent<DxTreemap>(p => p.Add(c => c.Root, new ChartTreeNode("Empty")));
        Assert.Empty(chart.FindAll("rect.dx-treemap-cell"));
    }

    // ---- DxSunburst ----

    [Fact]
    public void Sunburst_renders_one_arc_per_node_including_branches()
    {
        IRenderedComponent<DxSunburst> chart = RenderComponent<DxSunburst>(p => p.Add(c => c.Root, SampleTree()));

        // Web, Mobile (depth 1) + Search, Social (depth 2) = 4 arcs; Mobile is also a leaf.
        Assert.Equal(4, chart.FindAll("path.dx-sunburst-arc").Count);
    }

    [Fact]
    public void Sunburst_click_reports_the_node()
    {
        ChartTreeNodeEventArgs? selected = null;
        IRenderedComponent<DxSunburst> chart = RenderComponent<DxSunburst>(p => p
            .Add(c => c.Root, SampleTree())
            .Add(c => c.OnNodeSelected, e => selected = e));

        chart.FindAll("path.dx-sunburst-arc[tabindex]")[0].Click();

        Assert.NotNull(selected);
        Assert.Equal("Web", selected!.Value.Node.Label);   // first top-level branch
    }

    // ---- DxBoxPlot ----

    private static List<BoxPlotGroup> SampleGroups() =>
    [
        new("A", Enumerable.Range(1, 20).Select(i => (double)i).ToList()),
        new("B", Enumerable.Range(10, 20).Select(i => (double)i * 2).ToList()),
    ];

    [Fact]
    public void BoxPlot_renders_one_box_per_group_with_whiskers_and_median()
    {
        IRenderedComponent<DxBoxPlot> chart = RenderComponent<DxBoxPlot>(p => p.Add(c => c.Groups, SampleGroups()));

        Assert.Equal(2, chart.FindAll("rect.dx-boxplot-box").Count);
        Assert.Equal(2, chart.FindAll("line.dx-boxplot-median").Count);
        Assert.Equal(4, chart.FindAll("line.dx-boxplot-whisker").Count);   // 2 per group (min-side, max-side)
    }

    [Fact]
    public void BoxPlot_flags_a_far_outlier_as_a_dot_not_inside_the_whisker()
    {
        List<BoxPlotGroup> groups = [new("Skewed", [1, 2, 3, 4, 5, 6, 7, 500])];
        IRenderedComponent<DxBoxPlot> chart = RenderComponent<DxBoxPlot>(p => p.Add(c => c.Groups, groups));

        Assert.Single(chart.FindAll("circle.dx-boxplot-outlier"));
    }

    [Fact]
    public void BoxPlot_Violin_draws_a_density_silhouette_per_group()
    {
        IRenderedComponent<DxBoxPlot> chart = RenderComponent<DxBoxPlot>(p => p
            .Add(c => c.Groups, SampleGroups())
            .Add(c => c.Violin, true));

        Assert.Equal(2, chart.FindAll("polygon.dx-boxplot-violin").Count);
    }

    [Fact]
    public void BoxPlot_empty_group_is_skipped_without_throwing()
    {
        List<BoxPlotGroup> groups = [new("Empty", [])];
        IRenderedComponent<DxBoxPlot> chart = RenderComponent<DxBoxPlot>(p => p.Add(c => c.Groups, groups));

        Assert.Empty(chart.FindAll("rect.dx-boxplot-box"));
        Assert.Single(chart.FindAll("text.dx-bar-label"));   // the group's label still renders
    }

    // ---- DxSankeyChart ----

    private static (List<SankeyNode>, List<SankeyLink>) SampleFlow() =>
    (
        [new("a", "A"), new("b", "B"), new("c", "C")],
        [new SankeyLink("a", "b", 10), new SankeyLink("b", "c", 10)]
    );

    [Fact]
    public void Sankey_renders_one_rect_per_node_and_one_path_per_link()
    {
        (List<SankeyNode> nodes, List<SankeyLink> links) = SampleFlow();
        IRenderedComponent<DxSankeyChart> chart = RenderComponent<DxSankeyChart>(p => p
            .Add(c => c.Nodes, nodes)
            .Add(c => c.Links, links));

        Assert.Equal(3, chart.FindAll("rect.dx-sankey-node").Count);
        Assert.Equal(2, chart.FindAll("path.dx-sankey-link").Count);
    }

    [Fact]
    public void Sankey_node_click_reports_the_node_when_wired()
    {
        (List<SankeyNode> nodes, List<SankeyLink> links) = SampleFlow();
        SankeyNode? selected = null;
        IRenderedComponent<DxSankeyChart> chart = RenderComponent<DxSankeyChart>(p => p
            .Add(c => c.Nodes, nodes)
            .Add(c => c.Links, links)
            .Add(c => c.OnNodeSelected, e => selected = e));

        chart.FindAll("rect.dx-sankey-node[tabindex]")[0].Click();

        Assert.NotNull(selected);
        Assert.Equal("A", selected!.Value.Label);
    }

    [Fact]
    public void Sankey_stays_non_interactive_with_no_tabindex_when_no_handler_wired()
    {
        (List<SankeyNode> nodes, List<SankeyLink> links) = SampleFlow();
        IRenderedComponent<DxSankeyChart> chart = RenderComponent<DxSankeyChart>(p => p
            .Add(c => c.Nodes, nodes)
            .Add(c => c.Links, links));

        Assert.Empty(chart.FindAll("rect.dx-sankey-node[tabindex]"));
    }
}
