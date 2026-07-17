using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The four parking-lot chart types: Network graph, Parallel coordinates, Word cloud, Chord diagram.</summary>
public sealed class DxTier3ChartsTests : TestContext
{
    // ---- DxNetworkGraph ----

    [Fact]
    public void NetworkGraph_renders_one_node_and_one_edge_per_input()
    {
        List<GraphNode> nodes = [new("a", "A"), new("b", "B"), new("c", "C")];
        List<GraphEdge> edges = [new("a", "b"), new("b", "c")];
        IRenderedComponent<DxNetworkGraph> chart = RenderComponent<DxNetworkGraph>(p => p
            .Add(c => c.Nodes, nodes)
            .Add(c => c.Edges, edges)
            .Add(c => c.Iterations, 50));

        Assert.Equal(3, chart.FindAll("circle.dx-network-node").Count);
        Assert.Equal(2, chart.FindAll("line.dx-network-edge").Count);
    }

    [Fact]
    public void NetworkGraph_click_reports_the_node_when_wired()
    {
        List<GraphNode> nodes = [new("a", "A"), new("b", "B")];
        GraphNode? selected = null;
        IRenderedComponent<DxNetworkGraph> chart = RenderComponent<DxNetworkGraph>(p => p
            .Add(c => c.Nodes, nodes)
            .Add(c => c.Edges, (List<GraphEdge>)[])
            .Add(c => c.Iterations, 20)
            .Add(c => c.OnNodeSelected, e => selected = e));

        chart.FindAll("circle.dx-network-node[tabindex]")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal("B", selected!.Value.Label);
    }

    // ---- DxParallelCoordinates ----

    [Fact]
    public void ParallelCoordinates_renders_one_axis_and_one_polyline_per_row()
    {
        List<string> axes = ["Speed", "Power", "Range"];
        List<ParallelCoordinateRow> rows =
        [
            new("Model A", [80, 55, 70]),
            new("Model B", [60, 85, 50]),
        ];
        IRenderedComponent<DxParallelCoordinates> chart = RenderComponent<DxParallelCoordinates>(p => p
            .Add(c => c.Axes, axes)
            .Add(c => c.Rows, rows));

        Assert.Equal(3, chart.FindAll("line.dx-parallel-axis").Count);
        Assert.Equal(2, chart.FindAll("polyline.dx-parallel-line").Count);
    }

    [Fact]
    public void ParallelCoordinates_row_click_reports_it_when_wired()
    {
        List<string> axes = ["A", "B"];
        List<ParallelCoordinateRow> rows = [new("R1", [1, 2]), new("R2", [3, 4])];
        ParallelCoordinateRow? selected = null;
        IRenderedComponent<DxParallelCoordinates> chart = RenderComponent<DxParallelCoordinates>(p => p
            .Add(c => c.Axes, axes)
            .Add(c => c.Rows, rows)
            .Add(c => c.OnRowSelected, e => selected = e));

        chart.FindAll("polyline.dx-parallel-line[tabindex]")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal("R2", selected!.Value.Label);
    }

    // ---- DxWordCloud ----

    [Fact]
    public void WordCloud_renders_a_text_element_per_placed_word()
    {
        List<WordCloudEntry> words = [new("data", 100), new("chart", 60), new("svg", 30)];
        IRenderedComponent<DxWordCloud> chart = RenderComponent<DxWordCloud>(p => p.Add(c => c.Words, words));

        Assert.Equal(3, chart.FindAll("text.dx-wordcloud-word").Count);
    }

    [Fact]
    public void WordCloud_bigger_weight_gets_a_bigger_font_size()
    {
        List<WordCloudEntry> words = [new("big", 100), new("small", 1)];
        IRenderedComponent<DxWordCloud> chart = RenderComponent<DxWordCloud>(p => p
            .Add(c => c.Words, words)
            .Add(c => c.MinFontSize, 10)
            .Add(c => c.MaxFontSize, 60));

        var texts = chart.FindAll("text.dx-wordcloud-word");
        double FontSizeOf(string content) =>
            double.Parse(
                texts.Single(t => t.TextContent.StartsWith(content, StringComparison.Ordinal)).GetAttribute("font-size")!,
                System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(FontSizeOf("big") > FontSizeOf("small"));
    }

    // ---- DxChordDiagram ----

    [Fact]
    public void ChordDiagram_renders_one_arc_per_node_and_one_ribbon_per_link()
    {
        List<ChordNode> nodes = [new("A"), new("B"), new("C")];
        List<ChordLink> links = [new(0, 1, 10), new(1, 2, 5)];
        IRenderedComponent<DxChordDiagram> chart = RenderComponent<DxChordDiagram>(p => p
            .Add(c => c.Nodes, nodes)
            .Add(c => c.Links, links));

        Assert.Equal(3, chart.FindAll("path.dx-chord-arc").Count);
        Assert.Equal(2, chart.FindAll("path.dx-chord-ribbon").Count);
    }

    [Fact]
    public void ChordDiagram_node_click_reports_it_when_wired()
    {
        List<ChordNode> nodes = [new("A"), new("B")];
        List<ChordLink> links = [new(0, 1, 10)];
        ChordNode? selected = null;
        IRenderedComponent<DxChordDiagram> chart = RenderComponent<DxChordDiagram>(p => p
            .Add(c => c.Nodes, nodes)
            .Add(c => c.Links, links)
            .Add(c => c.OnNodeSelected, e => selected = e));

        chart.FindAll("path.dx-chord-arc[tabindex]")[0].Click();

        Assert.NotNull(selected);
        Assert.Equal("A", selected!.Value.Label);
    }
}
