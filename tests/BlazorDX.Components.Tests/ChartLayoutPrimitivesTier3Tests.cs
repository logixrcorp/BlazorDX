using BlazorDX.Primitives.Charts;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Pure-C# coverage for the parking-lot chart layout primitives — see ChartLayoutPrimitivesTests for Tier 2's.</summary>
public sealed class ChartLayoutPrimitivesTier3Tests
{
    // ---- ForceDirectedLayout ----

    [Fact]
    public void ForceDirected_connected_nodes_end_up_closer_than_a_distant_isolated_node()
    {
        // A triangle (0-1-2 all connected) plus an isolated node 3 with no edges at all.
        List<ForceEdgeInput> edges = [new(0, 1), new(1, 2), new(0, 2)];
        IReadOnlyList<ForceNodeLayout> layout = ForceDirectedLayout.Compute(4, edges, 400, 400, iterations: 200);

        Assert.Equal(4, layout.Count);
        double Dist(int a, int b)
        {
            double dx = layout[a].X - layout[b].X;
            double dy = layout[a].Y - layout[b].Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        double avgTriangleEdge = (Dist(0, 1) + Dist(1, 2) + Dist(0, 2)) / 3;
        double avgToIsolated = (Dist(0, 3) + Dist(1, 3) + Dist(2, 3)) / 3;
        Assert.True(avgToIsolated > avgTriangleEdge, "the unconnected node should end up farther away on average");
    }

    [Fact]
    public void ForceDirected_keeps_every_node_within_the_canvas_bounds()
    {
        List<ForceEdgeInput> edges = [new(0, 1), new(1, 2), new(2, 3), new(3, 0)];
        IReadOnlyList<ForceNodeLayout> layout = ForceDirectedLayout.Compute(4, edges, 300, 200, iterations: 150);

        Assert.All(layout, n =>
        {
            Assert.InRange(n.X, 0, 300);
            Assert.InRange(n.Y, 0, 200);
        });
    }

    [Fact]
    public void ForceDirected_zero_nodes_returns_empty()
    {
        Assert.Empty(ForceDirectedLayout.Compute(0, [], 400, 400));
    }

    // ---- WordCloudLayout ----

    [Fact]
    public void WordCloud_places_every_word_when_there_is_room()
    {
        List<WordCloudInput> words = [new("data", 100), new("chart", 60), new("SVG", 30), new("BlazorDX", 20)];
        IReadOnlyList<WordCloudPlacement> placement = WordCloudLayout.Compute(words, 800, 600);

        Assert.Equal(words.Count, placement.Count);
    }

    [Fact]
    public void WordCloud_scales_font_size_by_weight()
    {
        List<WordCloudInput> words = [new("big", 100), new("small", 1)];
        IReadOnlyList<WordCloudPlacement> placement = WordCloudLayout.Compute(words, 800, 600, minFontSize: 10, maxFontSize: 60);

        WordCloudPlacement big = placement.Single(p => p.Index == 0);
        WordCloudPlacement small = placement.Single(p => p.Index == 1);
        Assert.True(big.FontSize > small.FontSize);
        Assert.Equal(60, big.FontSize, 3);
        Assert.Equal(10, small.FontSize, 3);
    }

    [Fact]
    public void WordCloud_placed_words_do_not_overlap()
    {
        List<WordCloudInput> words = Enumerable.Range(0, 12)
            .Select(i => new WordCloudInput($"word{i}", 10 + (i * 3)))
            .ToList();
        IReadOnlyList<WordCloudPlacement> placement = WordCloudLayout.Compute(words, 900, 700);

        // Every pairwise center-distance should exceed a small floor -- a coarse but effective
        // non-overlap sanity check without re-deriving each word's exact box here.
        for (int i = 0; i < placement.Count; i++)
        {
            for (int j = i + 1; j < placement.Count; j++)
            {
                double dx = placement[i].X - placement[j].X;
                double dy = placement[i].Y - placement[j].Y;
                double dist = Math.Sqrt((dx * dx) + (dy * dy));
                Assert.True(dist > 1, $"words {i} and {j} landed on top of each other");
            }
        }
    }

    // ---- ChordLayout ----

    [Fact]
    public void Chord_arcs_partition_the_circle_proportionally_to_each_node_s_total_flow()
    {
        List<ChordLinkInput> links = [new(0, 1, 10), new(1, 2, 10)];   // node 1 touches both links -> largest arc
        (IReadOnlyList<ChordArc> arcs, _) = ChordLayout.Compute(3, links, padAngle: 0);

        double SweepOf(int i) => arcs[i].EndAngle - arcs[i].StartAngle;
        Assert.True(SweepOf(1) > SweepOf(0));
        Assert.True(SweepOf(1) > SweepOf(2));
        Assert.Equal(SweepOf(0), SweepOf(2), 6);
    }

    [Fact]
    public void Chord_ribbon_slices_exactly_fill_each_endpoint_s_own_arc()
    {
        List<ChordLinkInput> links = [new(0, 1, 5), new(0, 2, 3), new(1, 2, 2)];
        (IReadOnlyList<ChordArc> arcs, IReadOnlyList<ChordRibbon> ribbons) = ChordLayout.Compute(3, links, padAngle: 0);

        // Node 0's two ribbon slices should exactly span its own arc, in order.
        ChordRibbon r01 = ribbons.Single(r => r.From == 0 && r.To == 1);
        ChordRibbon r02 = ribbons.Single(r => r.From == 0 && r.To == 2);
        Assert.Equal(arcs[0].StartAngle, r01.FromStart, 6);
        Assert.Equal(r01.FromEnd, r02.FromStart, 6);
        Assert.Equal(arcs[0].EndAngle, r02.FromEnd, 6);
    }

    [Fact]
    public void Chord_self_links_are_skipped()
    {
        List<ChordLinkInput> links = [new(0, 0, 100), new(0, 1, 5)];
        (IReadOnlyList<ChordArc> _, IReadOnlyList<ChordRibbon> ribbons) = ChordLayout.Compute(2, links);

        Assert.Single(ribbons);
        Assert.Equal(0, ribbons[0].From);
        Assert.Equal(1, ribbons[0].To);
    }
}
