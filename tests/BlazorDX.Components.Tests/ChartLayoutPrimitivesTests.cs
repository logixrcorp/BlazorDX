using BlazorDX.Primitives.Charts;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Pure-C# coverage for the Tier-2 chart layout primitives — no Blazor rendering involved, same
/// policy as <c>ChartSelectionPrimitiveTests</c>.
/// </summary>
public sealed class ChartLayoutPrimitivesTests
{
    // ---- TreemapLayout ----

    [Fact]
    public void Treemap_rects_fully_tile_the_given_area_with_no_overlap()
    {
        double[] values = [40, 30, 20, 10];
        IReadOnlyList<TreemapRect> rects = TreemapLayout.Compute(values, 0, 0, 100, 60);

        Assert.Equal(4, rects.Count);
        double totalArea = rects.Sum(r => r.Width * r.Height);
        Assert.Equal(100 * 60, totalArea, 3);

        // Larger values should get larger areas, in the same relative order as the input.
        Dictionary<int, double> areaByIndex = rects.ToDictionary(r => r.Index, r => r.Width * r.Height);
        Assert.True(areaByIndex[0] > areaByIndex[1]);
        Assert.True(areaByIndex[1] > areaByIndex[2]);
        Assert.True(areaByIndex[2] > areaByIndex[3]);
    }

    [Fact]
    public void Treemap_keeps_aspect_ratios_reasonable_not_thin_slivers()
    {
        // A classic squarify demo case: naive proportional slicing would produce a very thin
        // sliver for the smallest item; squarify should not.
        double[] values = [6, 6, 4, 3, 2, 2, 1];
        IReadOnlyList<TreemapRect> rects = TreemapLayout.Compute(values, 0, 0, 600, 400);

        foreach (TreemapRect r in rects)
        {
            double longSide = Math.Max(r.Width, r.Height);
            double shortSide = Math.Max(1e-9, Math.Min(r.Width, r.Height));
            Assert.True(longSide / shortSide < 4, $"index {r.Index} aspect ratio too extreme: {r.Width}x{r.Height}");
        }
    }

    [Fact]
    public void Treemap_ignores_non_positive_values_and_handles_empty_input()
    {
        Assert.Empty(TreemapLayout.Compute([], 0, 0, 100, 100));
        Assert.Empty(TreemapLayout.Compute([0, -5], 0, 0, 100, 100));

        IReadOnlyList<TreemapRect> rects = TreemapLayout.Compute([10, 0, 5], 0, 0, 100, 100);
        Assert.Equal(2, rects.Count);   // the zero-value item is skipped entirely
    }

    // ---- SunburstLayout ----

    [Fact]
    public void Sunburst_slices_partition_the_full_sweep_proportionally()
    {
        double[] values = [1, 1, 2];   // -> quarter, quarter, half
        IReadOnlyList<SunburstSlice> slices = SunburstLayout.Compute(values, 0, 2 * Math.PI);

        Assert.Equal(3, slices.Count);
        Assert.Equal(0, slices[0].StartAngle, 6);
        Assert.Equal(Math.PI / 2, slices[0].EndAngle, 6);
        Assert.Equal(Math.PI / 2, slices[1].StartAngle, 6);
        Assert.Equal(Math.PI, slices[1].EndAngle, 6);
        Assert.Equal(Math.PI, slices[2].StartAngle, 6);
        Assert.Equal(2 * Math.PI, slices[2].EndAngle, 6);
    }

    [Fact]
    public void Sunburst_skips_non_positive_values_and_reports_original_index()
    {
        double[] values = [5, 0, 5];
        IReadOnlyList<SunburstSlice> slices = SunburstLayout.Compute(values, 0, Math.PI);

        Assert.Equal(2, slices.Count);
        Assert.Equal(0, slices[0].Index);
        Assert.Equal(2, slices[1].Index);   // index 1 (the zero) is skipped, not renumbered
    }

    // ---- BoxPlotStatistics ----

    [Fact]
    public void BoxPlot_computes_the_standard_five_number_summary()
    {
        // 1..10 (already sorted): Q1=3.25, median=5.5, Q3=7.75 under linear-interpolation.
        double[] sorted = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();
        BoxPlotStats stats = BoxPlotStatistics.Compute(sorted);

        Assert.Equal(1, stats.Min);
        Assert.Equal(3.25, stats.Q1, 3);
        Assert.Equal(5.5, stats.Median, 3);
        Assert.Equal(7.75, stats.Q3, 3);
        Assert.Equal(10, stats.Max);
        Assert.Empty(stats.Outliers);
    }

    [Fact]
    public void BoxPlot_flags_values_beyond_1_5_iqr_as_outliers_and_excludes_them_from_whiskers()
    {
        double[] sorted = [1, 2, 3, 4, 5, 6, 7, 100];   // 100 is a far outlier
        BoxPlotStats stats = BoxPlotStatistics.Compute(sorted);

        Assert.Contains(100, stats.Outliers);
        Assert.True(stats.Max < 100);   // the whisker doesn't reach the outlier
    }

    [Fact]
    public void BoxPlot_empty_input_returns_zeroed_stats_not_a_throw()
    {
        BoxPlotStats stats = BoxPlotStatistics.Compute([]);
        Assert.Equal(new BoxPlotStats(0, 0, 0, 0, 0, []), stats);
    }

    // ---- SankeyLayout ----

    [Fact]
    public void Sankey_assigns_layers_by_longest_path_from_a_source()
    {
        // A -> B -> D, A -> C -> D : D's longest path is via either 2-hop route -> layer 2.
        List<SankeyLinkInput> links =
        [
            new("A", "B", 10),
            new("A", "C", 5),
            new("B", "D", 10),
            new("C", "D", 5),
        ];

        (IReadOnlyList<SankeyNodeLayout> nodes, IReadOnlyList<SankeyLinkLayout> _) =
            SankeyLayout.Compute(["A", "B", "C", "D"], links, 400, 200);

        Dictionary<string, int> layerById = nodes.ToDictionary(n => n.Id, n => n.Layer);
        Assert.Equal(0, layerById["A"]);
        Assert.Equal(1, layerById["B"]);
        Assert.Equal(1, layerById["C"]);
        Assert.Equal(2, layerById["D"]);
    }

    [Fact]
    public void Sankey_node_height_is_proportional_to_its_total_flow()
    {
        List<SankeyLinkInput> links = [new("A", "C", 10), new("B", "C", 30)];
        (IReadOnlyList<SankeyNodeLayout> nodes, IReadOnlyList<SankeyLinkLayout> _) =
            SankeyLayout.Compute(["A", "B", "C"], links, 400, 200);

        double heightA = nodes.Single(n => n.Id == "A").Height;
        double heightB = nodes.Single(n => n.Id == "B").Height;
        Assert.True(heightB > heightA * 2.5);   // B carries 3x A's flow
    }

    [Fact]
    public void Sankey_links_connect_the_correct_source_and_target_x_positions()
    {
        List<SankeyLinkInput> links = [new("A", "B", 10)];
        (IReadOnlyList<SankeyNodeLayout> nodes, IReadOnlyList<SankeyLinkLayout> linkLayouts) =
            SankeyLayout.Compute(["A", "B"], links, 400, 200, nodeWidth: 16);

        SankeyNodeLayout a = nodes.Single(n => n.Id == "A");
        SankeyNodeLayout b = nodes.Single(n => n.Id == "B");
        SankeyLinkLayout link = Assert.Single(linkLayouts);

        Assert.Equal(a.X + a.Width, link.SourceX, 3);
        Assert.Equal(b.X, link.TargetX, 3);
    }

    [Fact]
    public void Sankey_empty_input_returns_no_nodes_or_links()
    {
        (IReadOnlyList<SankeyNodeLayout> nodes, IReadOnlyList<SankeyLinkLayout> links) =
            SankeyLayout.Compute([], [], 400, 200);
        Assert.Empty(nodes);
        Assert.Empty(links);
    }
}
